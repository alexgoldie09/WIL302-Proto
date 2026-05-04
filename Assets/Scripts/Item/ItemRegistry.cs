using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject registry for all ItemDefinitions. Used by save/load code
/// to resolve item names back to their runtime ScriptableObject references.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Item Registry", fileName = "ItemRegistry")]
public class ItemRegistry : ScriptableObject
{
    [SerializeField] private List<ItemDefinition> items = new();

    /// <summary>Returns the ItemDefinition whose ItemName matches, or null if not found.</summary>
    public ItemDefinition Find(string itemName)
    {
        foreach (var item in items)
            if (item != null && item.ItemName == itemName)
                return item;
        return null;
    }
}