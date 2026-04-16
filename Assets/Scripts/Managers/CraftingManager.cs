using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serialisable save data for CraftingManager.
/// </summary>
[Serializable]
public class CraftingManagerData
{
    public List<string> unlockedRecipeNames = new();
}

/// <summary>
/// Manages which recipes the player has unlocked and handles crafting logic.
/// Recipes are unlocked permanently via UnlockRecipe.
/// Crafting deducts ingredients from PlayerInventory and adds output after a timer.
/// </summary>
public class CraftingManager : SaveableBehaviour<CraftingManagerData>
{
    public static CraftingManager Instance { get; private set; }

    [Header("References")]
    [SerializeField, Tooltip("The master recipe database.")]
    private RecipeBook recipeBook;

    /// <summary>Fired when a new recipe is unlocked.</summary>
    public event Action<RecipeDefinition> OnRecipeUnlocked;

    /// <summary>Fired when crafting starts. Passes recipe and duration.</summary>
    public event Action<RecipeDefinition, float> OnCraftingStarted;

    /// <summary>Fired when crafting completes.</summary>
    public event Action<RecipeDefinition> OnCraftingCompleted;
    
    public float RemainingCraftTime { get; private set; }

    private readonly HashSet<string> _unlockedRecipes = new();
    private bool _isCrafting;

    #region SaveableBehaviour

    public override string RecordType => "CraftingManager";
    public override int LoadPriority => 5;

    protected override CraftingManagerData BuildData()
    {
        var data = new CraftingManagerData();
        foreach (var name in _unlockedRecipes)
            data.unlockedRecipeNames.Add(name);
        return data;
    }

    protected override void ApplyData(CraftingManagerData data, SaveContext context)
    {
        _unlockedRecipes.Clear();
        foreach (var name in data.unlockedRecipeNames)
            _unlockedRecipes.Add(name);
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Unlocks a recipe permanently for the player.
    /// Safe to call multiple times — ignores if already unlocked.
    /// </summary>
    public void UnlockRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return;
        if (_unlockedRecipes.Contains(recipe.RecipeName)) return;

        _unlockedRecipes.Add(recipe.RecipeName);
        OnRecipeUnlocked?.Invoke(recipe);
        Debug.Log($"[CraftingManager] Unlocked recipe: {recipe.RecipeName}");
    }

    /// <summary>
    /// Returns true if the player has unlocked the given recipe.
    /// </summary>
    public bool HasRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return false;
        return _unlockedRecipes.Contains(recipe.RecipeName);
    }

    /// <summary>
    /// Returns all unlocked recipes from the recipe book.
    /// </summary>
    public List<RecipeDefinition> GetUnlockedRecipes()
    {
        var result = new List<RecipeDefinition>();
        if (recipeBook == null) return result;

        foreach (var recipe in recipeBook.GetAllRecipes())
        {
            if (recipe != null && _unlockedRecipes.Contains(recipe.RecipeName))
                result.Add(recipe);
        }
        return result;
    }

    /// <summary>
    /// Returns true if the player has all required ingredients for the recipe.
    /// </summary>
    public bool CanCraft(RecipeDefinition recipe)
    {
        if (recipe == null) return false;

        foreach (var ingredient in recipe.Ingredients)
        {
            if (ingredient.item == null) continue;
            if (PlayerInventory.Instance.GetCount(ingredient.item) < ingredient.quantity)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Starts crafting a recipe. Deducts ingredients immediately and adds
    /// output to inventory after craftingDuration seconds.
    /// Returns false if already crafting, recipe not unlocked, or missing ingredients.
    /// </summary>
    public bool StartCrafting(RecipeDefinition recipe)
    {
        if (recipe == null) return false;

        if (_isCrafting)
        {
            Debug.LogWarning("[CraftingManager] Already crafting.");
            return false;
        }

        if (!HasRecipe(recipe))
        {
            Debug.LogWarning($"[CraftingManager] Recipe not unlocked: {recipe.RecipeName}");
            return false;
        }

        if (!CanCraft(recipe))
        {
            Debug.LogWarning($"[CraftingManager] Missing ingredients for: {recipe.RecipeName}");
            return false;
        }

        // Deduct ingredients
        foreach (var ingredient in recipe.Ingredients)
            PlayerInventory.Instance.Remove(ingredient.item, ingredient.quantity);

        _isCrafting = true;
        OnCraftingStarted?.Invoke(recipe, recipe.CraftingDuration);
        StartCoroutine(CraftingRoutine(recipe));

        Debug.Log($"[CraftingManager] Started crafting: {recipe.RecipeName} ({recipe.CraftingDuration}s)");
        return true;
    }

    public bool IsCrafting => _isCrafting;

    #endregion

    #region Crafting Routine

    private IEnumerator CraftingRoutine(RecipeDefinition recipe)
    {
        RemainingCraftTime = recipe.CraftingDuration;
    
        while (RemainingCraftTime > 0f)
        {
            RemainingCraftTime -= Time.deltaTime;
            yield return null;
        }
    
        RemainingCraftTime = 0f;
        PlayerInventory.Instance.Add(recipe.OutputItem, recipe.OutputQuantity);
        _isCrafting = false;
        OnCraftingCompleted?.Invoke(recipe);
    
        Debug.Log($"[CraftingManager] Crafting complete: {recipe.RecipeName}. " +
                  $"Added {recipe.OutputQuantity}x {recipe.OutputItem.ItemName}.");
    }


    #endregion

    #region Debug

#if UNITY_EDITOR
    [ContextMenu("Debug/Log Unlocked Recipes")]
    private void DebugLogUnlocked()
    {
        if (_unlockedRecipes.Count == 0)
        {
            Debug.Log("[CraftingManager] No recipes unlocked.");
            return;
        }
        foreach (var name in _unlockedRecipes)
            Debug.Log($"[CraftingManager] Unlocked: {name}");
    }
#endif

    #endregion
}