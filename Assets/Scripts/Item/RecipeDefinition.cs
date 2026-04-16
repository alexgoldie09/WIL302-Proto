using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a single crafting ingredient requirement.
/// </summary>
[Serializable]
public class RecipeIngredient
{
    [Tooltip("The item required for this recipe.")]
    public ItemDefinition item;
    [Tooltip("How many of this item are required.")]
    public int quantity = 1;
}

/// <summary>
/// ScriptableObject that defines a craftable recipe, including
/// its ingredients, output, and crafting time.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Recipe Definition", fileName = "Recipe_")]
public class RecipeDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField, Tooltip("Display name for this recipe (e.g. 'Mayonnaise').")]
    private string recipeName = "New Recipe";
    [SerializeField, Tooltip("Icon for this recipe.")]
    private Sprite icon;
    [SerializeField, Tooltip("Description of the recipe.")]
    private string description = string.Empty;

    [Header("Ingredients")]
    [SerializeField, Tooltip("List of items and quantities required to craft this recipe.")]
    private List<RecipeIngredient> ingredients = new();

    [Header("Output")]
    [SerializeField, Tooltip("The item produced by crafting this recipe.")]
    private ItemDefinition outputItem;
    [SerializeField, Tooltip("How many of the output item are produced.")]
    private int outputQuantity = 1;

    [Header("Crafting")]
    [SerializeField, Tooltip("Time in seconds to craft this recipe.")]
    private float craftingDuration = 3f;

    [Header("Store")]
    [SerializeField, Tooltip("If true this recipe can be purchased from the storefront.")]
    private bool isAvailableInStore = true;
    [SerializeField, Tooltip("Buy price of this recipe in the storefront.")]
    private float buyPrice = 10f;

    #region Accessors
    public string RecipeName => recipeName;
    public Sprite Icon => icon;
    public string Description => description;
    public List<RecipeIngredient> Ingredients => ingredients;
    public ItemDefinition OutputItem => outputItem;
    public int OutputQuantity => outputQuantity;
    public float CraftingDuration => craftingDuration;
    public bool IsAvailableInStore => isAvailableInStore;
    public float BuyPrice => buyPrice;
    #endregion
}