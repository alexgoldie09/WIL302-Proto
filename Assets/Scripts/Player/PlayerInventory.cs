using System;
using System.Collections.Generic;
using UnityEngine;


/// <summary>
/// Serialisable save data for PlayerInventory.
/// </summary>
[Serializable]
public class PlayerInventoryData
{
    public List<string> itemNames  = new();
    public List<int>    itemCounts = new();
}

/// <summary>
/// Singleton that manages the player's item counts and notifies listeners on any change.
/// </summary>
public class PlayerInventory : SaveableBehaviour<PlayerInventoryData>
{
    public static PlayerInventory Instance { get; private set; }
    
    [Serializable]
    private class StartingItem
    {
        public ItemDefinition item;
        public int amount;
    }

    /// <summary>
    /// Fired whenever an Add or successful Remove occurs.
    /// Passes the item that changed and its new count.
    /// </summary>
    public event Action<ItemDefinition, int> OnInventoryChanged;
    
    [Header("Starting Items")]
    [SerializeField, Tooltip("The list of starting items in the inventory for the player.")] 
    private List<StartingItem> startingItems = new();
    
    [Header("Save")]
    [SerializeField, Tooltip("Registry used to resolve saved item names back to ItemDefinition assets.")]
    private ItemRegistry itemRegistry;

    private readonly Dictionary<ItemDefinition, int> _inventory = new();
    private bool _wasRestored;

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    private void Start()
    {
        if (_wasRestored) return;
        
        foreach (var entry in startingItems)
            Add(entry.item, entry.amount);
    }
    #endregion
    
    #region Public API
    /// <summary>
    /// Adds an amount of the item to the inventory.
    /// For Lucille to plug in new items from the world, and for the storefront to add items bought by the player.
    /// </summary>
    public void Add(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return;

        if (_inventory.TryGetValue(item, out int current))
            _inventory[item] = current + amount;
        else
            _inventory[item] = amount;

        int newCount = _inventory[item];
        Debug.Log($"[PlayerInventory] Added {amount}x {item.ItemName}. New count: {newCount}");
        OnInventoryChanged?.Invoke(item, newCount);
    }

    /// <summary>
    /// Removes an amount of the item from the inventory.
    /// Returns false and makes no change if there are not enough.
    /// For Lucille to plug in selling items to the storefront, and for the feed panel to remove items fed to the animals.
    /// </summary>
    public bool Remove(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;

        _inventory.TryGetValue(item, out int current);

        if (current < amount)
        {
            Debug.LogWarning($"[PlayerInventory] Cannot remove {amount}x {item.ItemName}. " +
                             $"Current count: {current}");
            return false;
        }

        int newCount = current - amount;

        if (newCount == 0)
            _inventory.Remove(item);
        else
            _inventory[item] = newCount;

        Debug.Log($"[PlayerInventory] Removed {amount}x {item.ItemName}. New count: {newCount}");
        OnInventoryChanged?.Invoke(item, newCount);
        return true;
    }

    /// <summary>
    /// Returns the current count of item, or 0 if not present.
    /// </summary>
    public int GetCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return _inventory.GetValueOrDefault(item, 0);
    }
    
    /// <summary>
    /// Returns a snapshot of the full inventory as a read-only dictionary (only class mutates).
    /// Use for displaying the complete inventory UI without filtering.
    /// </summary>
    public IReadOnlyDictionary<ItemDefinition, int> GetAllItems() => _inventory;

    /// <summary>
    /// Returns all held items where ItemDefinition.IsAvailableForFeeding is true.
    /// For Lucille to be used to populate the feed panel (or filter).
    /// </summary>
    public List<ItemDefinition> GetFeedableItems()
    {
        var result = new List<ItemDefinition>();
        foreach (var kvp in _inventory)
        {
            if (kvp.Value > 0 && kvp.Key.IsAvailableForFeeding)
                result.Add(kvp.Key);
        }
        return result;
    }

    /// <summary>
    /// Returns all held items where ItemDefinition.IsAvailableForStorefront is true.
    /// For Lucille to be used to populate the storefront panel (or filter).
    /// </summary>
    public List<ItemDefinition> GetSellableItems()
    {
        var result = new List<ItemDefinition>();
        foreach (var kvp in _inventory)
        {
            if (kvp.Value > 0 && kvp.Key.IsAvailableForStorefront)
                result.Add(kvp.Key);
        }
        return result;
    }
    #endregion
    
    #region SaveableBehaviour

    public override string RecordType  => "PlayerInventory";
    public override int LoadPriority => 0;

    protected override PlayerInventoryData BuildData()
    {
        var data = new PlayerInventoryData();
        foreach (var kvp in _inventory)
        {
            data.itemNames.Add(kvp.Key.ItemName);
            data.itemCounts.Add(kvp.Value);
        }
        return data;
    }

    protected override void ApplyData(PlayerInventoryData data, SaveContext context)
    {
        _wasRestored = true;
        _inventory.Clear();

        for (int i = 0; i < data.itemNames.Count; i++)
        {
            var item = itemRegistry != null ? itemRegistry.Find(data.itemNames[i]) : null;
            if (item == null)
            {
                Debug.LogWarning($"[PlayerInventory] Item '{data.itemNames[i]}' not found in ItemRegistry - skipping.");
                continue;
            }
            Add(item, data.itemCounts[i]);
        }
    }

    #endregion
    
    #region Debug Methods
    /// <summary>
    /// Removes the first item from the inventory.
    /// </summary>
    [ContextMenu("Debug/Remove First Item")]
    private void DebugRemoveFirstItem()
    {
        foreach (var kvp in _inventory)
        {
            Remove(kvp.Key, 1);
            return;
        }
        Debug.LogWarning("[PlayerInventory] Nothing in inventory to remove.");
    }
    #endregion
}