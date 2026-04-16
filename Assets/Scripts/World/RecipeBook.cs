using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that holds all recipes available in the game.
/// Acts as the master database — CraftingManager checks this for known recipes
/// and StoreCatalogue pulls from this for purchasable recipes.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Recipe Book", fileName = "RecipeBook")]
public class RecipeBook : ScriptableObject
{
    [SerializeField, Tooltip("All recipes in the game.")]
    private List<RecipeDefinition> recipes = new();

    /// <summary>Returns a copy of all recipes.</summary>
    public List<RecipeDefinition> GetAllRecipes() => new(recipes);

    /// <summary>Returns all recipes flagged as available in the store.</summary>
    public List<RecipeDefinition> GetStoreRecipes()
    {
        var result = new List<RecipeDefinition>();
        foreach (var recipe in recipes)
        {
            if (recipe != null && recipe.IsAvailableInStore)
                result.Add(recipe);
        }
        return result;
    }

    /// <summary>Returns a recipe by name, or null if not found.</summary>
    public RecipeDefinition GetRecipeByName(string name)
    {
        foreach (var recipe in recipes)
        {
            if (recipe != null && recipe.RecipeName == name)
                return recipe;
        }
        return null;
    }
}