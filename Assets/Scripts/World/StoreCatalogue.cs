using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that defines a store's catalogue of items available for purchase.
/// Each entry defines the item, its starting stock, and the maximum it can hold.
/// Assign to StoreFrontManager to define what the store stocks.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Store Catalogue", fileName = "StoreCatalogue_")]
public class StoreCatalogue : ScriptableObject
{
    [Serializable]
    public class CatalogueEntry
    {
        [Tooltip("The item available for purchase.")]
        public ItemDefinition item;
        [Tooltip("How many the store starts with.")]
        public int startingStock = 10;
        [Tooltip("The maximum stock this store can hold (used for restocking later).")]
        public int maxStock = 10;
    }

    [SerializeField, Tooltip("Display name for this catalogue (e.g. 'General Store', 'Spring Catalogue').")]
    private string catalogueName = "General Store";

    [SerializeField, Tooltip("Items available for the player to buy from this store.")]
    private List<CatalogueEntry> entries = new();

    #region Accessors
    public string CatalogueName => catalogueName;
    /// <summary>Returns a copy of the catalogue entries to prevent external mutation.</summary>
    public List<CatalogueEntry> GetEntries() => new(entries);
    #endregion
}