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
    public List<string> activeCraftRecipeNames = new();
    public List<float>  activeCraftRemainingTimes = new();
}

/// <summary>
/// Tracks a single active crafting operation.
/// </summary>
[Serializable]
public class ActiveCraft
{
    public RecipeDefinition recipe;
    public float remainingTime;

    public ActiveCraft(RecipeDefinition recipe)
    {
        this.recipe = recipe;
        this.remainingTime = recipe.CraftingDuration;
    }
}

/// <summary>
/// Manages which recipes the player has unlocked and handles crafting logic.
/// Supports multiple concurrent crafting slots controlled by maxConcurrentCrafts.
/// Recipes are unlocked permanently via UnlockRecipe.
/// Crafting deducts ingredients immediately and adds output after a timer.
/// </summary>
public class CraftingManager : SaveableBehaviour<CraftingManagerData>
{
    public static CraftingManager Instance { get; private set; }
    
    [Header("References")]
    [SerializeField, Tooltip("Tier 1 recipe database.")]
    private RecipeBook tier1RecipeBook;
    [SerializeField, Tooltip("Tier 2 recipe database. Recipes become craftable once unlocked via storefront.")]
    private RecipeBook tier2RecipeBook;

    [Header("Crafting")]
    [SerializeField, Tooltip("Maximum number of recipes that can be crafted simultaneously. " +
                             "Increase via upgrade to unlock additional slots.")]
    private int maxConcurrentCrafts = 1;
    
    /// <summary>Fired when a new recipe is unlocked.</summary>
    public event Action<RecipeDefinition> OnRecipeUnlocked;

    /// <summary>Fired when crafting starts. Passes recipe and duration.</summary>
    public event Action<RecipeDefinition, float> OnCraftingStarted;

    /// <summary>Fired when crafting completes.</summary>
    public event Action<RecipeDefinition> OnCraftingCompleted;

    /// <summary>
    /// Fired every frame for each active craft so UI can update timers.
    /// Passes the recipe and remaining time.
    /// </summary>
    public event Action<RecipeDefinition, float> OnCraftingTick;
    
    private readonly HashSet<string>  _unlockedRecipes = new();
    private readonly List<ActiveCraft> _activeCrafts   = new();
    
    /// <summary>True if all crafting slots are occupied.</summary>
    public bool IsCrafting => _activeCrafts.Count >= maxConcurrentCrafts;

    /// <summary>Number of crafting slots currently in use.</summary>
    public int ActiveCraftCount => _activeCrafts.Count;

    /// <summary>Maximum number of simultaneous crafts.</summary>
    public int MaxConcurrentCrafts => maxConcurrentCrafts;

    /// <summary>Read-only view of currently active crafts for UI display.</summary>
    public IReadOnlyList<ActiveCraft> ActiveCrafts => _activeCrafts;
    
    #region ISaveable Methods
    public override string RecordType  => "CraftingManager";
    public override int LoadPriority => 5;

    protected override CraftingManagerData BuildData()
    {
        var data = new CraftingManagerData();
        foreach (var name in _unlockedRecipes)
            data.unlockedRecipeNames.Add(name);
        foreach (var craft in _activeCrafts)
        {
            data.activeCraftRecipeNames.Add(craft.recipe.RecipeName);
            data.activeCraftRemainingTimes.Add(craft.remainingTime);
        }
        return data;
    }

    protected override void ApplyData(CraftingManagerData data, SaveContext context)
    {
        _unlockedRecipes.Clear();
        foreach (var name in data.unlockedRecipeNames)
            _unlockedRecipes.Add(name);
        
        _activeCrafts.Clear();
        float elapsed = (float)context.Elapsed.TotalSeconds;

        for (int i = 0; i < data.activeCraftRecipeNames.Count; i++)
        {
            var recipe = FindRecipeByName(data.activeCraftRecipeNames[i]);
            if (recipe == null)
            {
                Debug.LogWarning($"[CraftingManager] Recipe '{data.activeCraftRecipeNames[i]}' not found — skipping active craft.");
                continue;
            }

            float remaining = data.activeCraftRemainingTimes[i] - elapsed;

            if (remaining <= 0f)
            {
                // Completed while offline — grant output immediately without a coroutine.
                PlayerInventory.Instance?.Add(recipe.OutputItem, recipe.OutputQuantity);
                QuestManager.Instance?.RecordProgress(
                    QuestObjectiveType.CraftItem,
                    recipe.OutputItem.ItemName,
                    recipe.OutputQuantity
                );
                OnCraftingCompleted?.Invoke(recipe);
                Debug.Log($"[CraftingManager] '{recipe.RecipeName}' finished offline.");
                continue;
            }

            var craft = new ActiveCraft(recipe);
            craft.remainingTime = remaining;
            _activeCrafts.Add(craft);
            StartCoroutine(CraftingRoutine(craft));
            OnCraftingStarted?.Invoke(recipe, craft.remainingTime);
        }
    }

    private RecipeDefinition FindRecipeByName(string recipeName)
    {
        return tier1RecipeBook?.GetRecipeByName(recipeName)
               ?? tier2RecipeBook?.GetRecipeByName(recipeName);
    }
    #endregion
    
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    
    #region Crafting Methods
    /// <summary>
    /// Unlocks a recipe permanently. Safe to call multiple times.
    /// </summary>
    public void UnlockRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return;
        if (_unlockedRecipes.Contains(recipe.RecipeName)) return;

        _unlockedRecipes.Add(recipe.RecipeName);
        OnRecipeUnlocked?.Invoke(recipe);
        Debug.Log($"[CraftingManager] Unlocked recipe: {recipe.RecipeName}");
    }

    /// <summary>Returns true if the player has unlocked the given recipe.</summary>
    public bool HasRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return false;
        return _unlockedRecipes.Contains(recipe.RecipeName);
    }

    /// <summary>
    /// Returns all unlocked recipes across both recipe books.
    /// </summary>
    public List<RecipeDefinition> GetUnlockedRecipes()
    {
        var result = new List<RecipeDefinition>();
        SearchBook(tier1RecipeBook, result);
        SearchBook(tier2RecipeBook, result);
        return result;
    }

    private void SearchBook(RecipeBook book, List<RecipeDefinition> result)
    {
        if (book == null) return;
        foreach (var recipe in book.GetAllRecipes())
            if (recipe != null && _unlockedRecipes.Contains(recipe.RecipeName))
                result.Add(recipe);
    }

    /// <summary>Returns true if the player has all required ingredients.</summary>
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
    /// Returns true if a slot is available and the recipe is not already being crafted.
    /// </summary>
    public bool CanStartCrafting(RecipeDefinition recipe)
    {
        if (recipe == null) return false;
        if (_activeCrafts.Count >= maxConcurrentCrafts) return false;

        // Prevent the same recipe running in two slots simultaneously.
        foreach (var active in _activeCrafts)
            if (active.recipe == recipe) return false;

        return HasRecipe(recipe) && CanCraft(recipe);
    }

    /// <summary>
    /// Starts crafting a recipe in the next available slot.
    /// Deducts ingredients immediately and adds output after the timer.
    /// Returns false if no slots available, already crafting this recipe,
    /// recipe not unlocked, or missing ingredients.
    /// </summary>
    public bool StartCrafting(RecipeDefinition recipe)
    {
        if (!CanStartCrafting(recipe))
        {
            Debug.LogWarning($"[CraftingManager] Cannot start crafting '{recipe?.RecipeName}'.");
            return false;
        }

        // Deduct ingredients immediately.
        foreach (var ingredient in recipe.Ingredients)
            PlayerInventory.Instance.Remove(ingredient.item, ingredient.quantity);

        var craft = new ActiveCraft(recipe);
        _activeCrafts.Add(craft);

        OnCraftingStarted?.Invoke(recipe, recipe.CraftingDuration);
        StartCoroutine(CraftingRoutine(craft));

        Debug.Log($"[CraftingManager] Started crafting: {recipe.RecipeName} " +
                  $"(slot {_activeCrafts.Count}/{maxConcurrentCrafts})");
        return true;
    }

    /// <summary>
    /// Increases the maximum concurrent crafting slots.
    /// Called by the upgrade system when a crafting slot upgrade is purchased.
    /// </summary>
    public void UpgradeCraftingSlots(int newMax)
    {
        maxConcurrentCrafts = Mathf.Max(maxConcurrentCrafts, newMax);
        Debug.Log($"[CraftingManager] Max concurrent crafts upgraded to {maxConcurrentCrafts}.");
    }
    
    private IEnumerator CraftingRoutine(ActiveCraft craft)
    {
        while (craft.remainingTime > 0f)
        {
            craft.remainingTime -= Time.deltaTime;
            OnCraftingTick?.Invoke(craft.recipe, Mathf.Max(0f, craft.remainingTime));
            yield return null;
        }

        craft.remainingTime = 0f;
        PlayerInventory.Instance.Add(craft.recipe.OutputItem, craft.recipe.OutputQuantity);
        QuestManager.Instance?.RecordProgress(
            QuestObjectiveType.CraftItem,
            craft.recipe.OutputItem.ItemName,
            craft.recipe.OutputQuantity
        );
        _activeCrafts.Remove(craft);
        OnCraftingCompleted?.Invoke(craft.recipe);

        Debug.Log($"[CraftingManager] Crafting complete: {craft.recipe.RecipeName}. " +
                  $"Added {craft.recipe.OutputQuantity}x {craft.recipe.OutputItem.ItemName}.");
    }
    #endregion

    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Log Unlocked Recipes")]
    private void DebugLogUnlocked()
    {
        if (_unlockedRecipes.Count == 0) { Debug.Log("[CraftingManager] No recipes unlocked."); return; }
        foreach (var name in _unlockedRecipes)
            Debug.Log($"[CraftingManager] Unlocked: {name}");
    }

    [ContextMenu("Debug/Log Active Crafts")]
    private void DebugLogActive()
    {
        if (_activeCrafts.Count == 0) { Debug.Log("[CraftingManager] No active crafts."); return; }
        foreach (var craft in _activeCrafts)
            Debug.Log($"[CraftingManager] Crafting: {craft.recipe.RecipeName} — {craft.remainingTime:F1}s left");
    }
#endif
    #endregion
}