using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the player's coin balance and all storefront trading.
/// Exists as a world object that opens/closes the StoreFrontUI when tapped.
/// The store maintains finite stock per catalogue entry — buying depletes supply.
/// Selling is always open; the player can always offload their goods which will apply in the buyback.
/// Buyback allows the player to buy back the items they sold at the same price they sold it for.
/// Note: Will extend SaveableBehaviour<CurrencyData> when save system is integrated.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class StoreFrontManager : MonoBehaviour, IHandler
{
    [Header("Storefront Settings")]
    [SerializeField, Tooltip("Layer mask for tap detection.")]
    private LayerMask interactableLayer;
    [SerializeField, Tooltip("Starting coin balance for the player.")]
    private float startingCoinBalance = 0f;
    [SerializeField, Tooltip("The catalogue of items this store stocks for the player to buy.")]
    private StoreCatalogue catalogue;
    
    /// <summary>
    /// Fired whenever coinBalance changes.
    /// Passes the new balance so the HUD coin display can update directly.
    /// </summary>
    public event Action<float> OnBalanceChanged;

    /// <summary>
    /// Fired when the storefront is opened or closed.
    /// Passes true when opening, false when closing.
    /// </summary>
    public event Action<bool> OnStorefrontToggled;

    /// <summary>
    /// Fired whenever a stock count changes.
    /// Passes the item and its new stock level so the UI can update per-item.
    /// </summary>
    public event Action<ItemDefinition, int> OnStockChanged;
    
    /// <summary>
    /// Fired whenever the buyback changes.
    /// Passes the item and its new buyback count.
    /// </summary>
    public event Action<ItemDefinition, int> OnBuybackChanged;
    
    private float _coinBalance;
    private bool _isOpen;

    /// <summary>Runtime stock levels, initialised from catalogue on Start.</summary>
    private readonly Dictionary<ItemDefinition, int> _stock = new();

    /// <summary>Max stock per item, kept for the UI and future restock logic.</summary>
    private readonly Dictionary<ItemDefinition, int> _maxStock = new();
    
    /// <summary>
    /// Tracks items the player has sold with the price they sold at.
    /// </summary>
    private struct BuybackEntry
    {
        public int count;
        public float pricePerUnit; // This will be snapshotted at sell time.
    }
    
    /// <summary>Runtime buyback data for items sold by the player. </summary>
    private readonly Dictionary<ItemDefinition, BuybackEntry> _buyback = new();
    
    #region Accessors
    /// <summary>The player's current coin balance.</summary>
    public float CoinBalance => _coinBalance;

    /// <summary>Whether the storefront UI is currently open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    /// Returns the current stock count for an item, or 0 if not in the catalogue.
    /// </summary>
    public int GetStock(ItemDefinition item)
    {
        if (item == null) return 0;
        return _stock.GetValueOrDefault(item, 0);
    }

    /// <summary>
    /// Returns the max stock for an item, or 0 if not in the catalogue.
    /// </summary>
    public int GetMaxStock(ItemDefinition item)
    {
        if (item == null) return 0;
        return _maxStock.GetValueOrDefault(item, 0);
    }

    /// <summary>
    /// Returns how many of an item are available, or 0 if not in it the buyback pool.
    /// </summary>
    public int GetBuybackCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return _buyback.TryGetValue(item, out var entry) ? entry.count : 0;
    }

    /// <summary>
    /// Returns the snapshotted buyback price for an item, or 0 if not in the buyback pool.
    /// This is the price the player originally sold it for, not the current BaseSellValue (or BaseBuyValue).
    /// </summary>
    public float GetBuybackPrice(ItemDefinition item)
    {
        if (item == null) return 0f;
        return _buyback.TryGetValue(item, out var entry) ? entry.pricePerUnit : 0f;
    }
    #endregion
    
    #region Unity Lifecycle
    private void Start()
    {
        SetBalance(startingCoinBalance);
        InitialiseStock();
    }

    private void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }
    #endregion
    
    #region IHandler
    private void HandleWorldTap(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        _isOpen = !_isOpen;
        Debug.Log($"[StoreFrontManager] Storefront {(_isOpen ? "opened" : "closed")}.");
        OnStorefrontToggled?.Invoke(_isOpen);
    }
    #endregion
    
    #region Public API - UI
    /// <summary>
    /// Returns all items in the player's inventory flagged for storefront.
    /// For Lucille to populate the sell panel.
    /// </summary>
    public List<ItemDefinition> GetSellableItems()
    {
        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[StoreFrontManager] No PlayerInventory instance found.");
            return new List<ItemDefinition>();
        }

        return PlayerInventory.Instance.GetSellableItems();
    }

    /// <summary>
    /// Returns all items in the catalogue regardless of current stock.
    /// For Lucille to populate the buy panel.
    /// Stock per item should be checked via GetStock().
    /// </summary>
    public List<ItemDefinition> GetBuyableItems()
    {
        if (catalogue == null)
        {
            Debug.LogWarning("[StoreFrontManager] No StoreCatalogue assigned.");
            return new List<ItemDefinition>();
        }

        var result = new List<ItemDefinition>();
        foreach (var entry in catalogue.GetEntries())
        {
            if (entry.item != null)
                result.Add(entry.item);
        }
        return result;
    }

    /// <summary>
    /// Returns all items currently in the buyback pool.
    /// For Lucille to populate the buyback panel.
    /// Count and price per item should be checked via GetBuybackCount() and GetBuybackPrice().
    /// </summary>
    public List<ItemDefinition> GetBuybackItems()
    {
        var result = new List<ItemDefinition>();
        foreach (var kvp in _buyback)
        {
            if(kvp.Value.count > 0)
                result.Add(kvp.Key);
        }
        return result;
    }
    #endregion
    
    #region Public API - Trading
    /// <summary>
    /// Sells an amount of an item from the player's inventory.
    /// Deducts from inventory and credits coinBalance by BaseSellValue * amount.
    /// Returns false if the inventory remove fails.
    /// </summary>
    public bool Sell(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;

        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[StoreFrontManager] No PlayerInventory instance found.");
            return false;
        }

        if (!PlayerInventory.Instance.Remove(item, amount))
        {
            Debug.LogWarning($"[StoreFrontManager] Sell failed - not enough {item.ItemName} in inventory.");
            return false;
        }

        float pricePerUnit = item.BaseSellValue;
        float earned = pricePerUnit * amount;
        SetBalance(_coinBalance + earned);
        AddToBuyback(item, amount, pricePerUnit);
 
        Debug.Log($"[StoreFrontManager] Sold {amount}x {item.ItemName} for {earned} coins. " +
                  $"Balance: {_coinBalance}");
        return true;
    }

    /// <summary>
    /// Buys an amount of an item from the catalogue and adds it to the player's inventory.
    /// Validates catalogue membership, stock availability, and coin balance.
    /// Returns false if any check fails meaning no partial state changes are made.
    /// </summary>
    public bool Buy(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;

        if (!_stock.TryGetValue(item, out var currentStock))
        {
            Debug.LogWarning($"[StoreFrontManager] Buy failed - {item.ItemName} is not in the catalogue.");
            return false;
        }

        if (currentStock < amount)
        {
            Debug.LogWarning($"[StoreFrontManager] Buy failed - not enough stock for {item.ItemName}. " +
                             $"Requested: {amount}, Available: {currentStock}");
            return false;
        }

        float cost = item.BaseBuyValue * amount;
        if (_coinBalance < cost)
        {
            Debug.LogWarning($"[StoreFrontManager] Buy failed - insufficient coins for " +
                             $"{amount}x {item.ItemName} (costs {cost}, have {_coinBalance}).");
            return false;
        }

        // All checks passed so we can commit changes.
        SetStock(item, currentStock - amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);

        Debug.Log($"[StoreFrontManager] Bought {amount}x {item.ItemName} for {cost} coins. " +
                  $"Stock remaining: {_stock[item]}. Balance: {_coinBalance}");
        return true;
    }
    
    /// <summary>
    /// Buys back an amount of an item the player previously sold, at the original sell price.
    /// Deducts from the buyback pool and the player's coin balance.
    /// Returns false if there is insufficient buyback stock or coins.
    /// </summary>
    public bool Buyback(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
 
        if (!_buyback.TryGetValue(item, out var entry) || entry.count <= 0)
        {
            Debug.LogWarning($"[StoreFrontManager] Buyback failed - {item.ItemName} is not in the buyback pool.");
            return false;
        }
 
        if (entry.count < amount)
        {
            Debug.LogWarning($"[StoreFrontManager] Buyback failed - not enough in buyback pool for " +
                             $"{item.ItemName}. Requested: {amount}, Available: {entry.count}");
            return false;
        }
 
        float cost = entry.pricePerUnit * amount;
        if (_coinBalance < cost)
        {
            Debug.LogWarning($"[StoreFrontManager] Buyback failed - insufficient coins for " +
                             $"{amount}x {item.ItemName} (costs {cost}, have {_coinBalance}).");
            return false;
        }
 
        // All checks passed — commit changes.
        RemoveFromBuyback(item, amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);
 
        Debug.Log($"[StoreFrontManager] Bought back {amount}x {item.ItemName} for {cost} coins. " +
                  $"Balance: {_coinBalance}");
        return true;
    }

    /// <summary>
    /// Deducts a raw coin cost from the balance without involving an item.
    /// Used for biome expansion or any other non-item spend.
    /// Returns false if the player cannot afford it.
    /// </summary>
    public bool SpendCoins(float cost)
    {
        if (cost <= 0) return false;

        if (_coinBalance < cost)
        {
            Debug.LogWarning($"[StoreFrontManager] SpendCoins failed - " +
                             $"need {cost}, have {_coinBalance}.");
            return false;
        }

        SetBalance(_coinBalance - cost);
        Debug.Log($"[StoreFrontManager] Spent {cost} coins. Balance: {_coinBalance}");
        return true;
    }
    #endregion
    
    #region Private Helpers
    /// <summary>
    /// Reads the catalogue and populates the runtime stock dictionaries.
    /// Clamps startingStock to maxStock and warns if a designer has set it over cap.
    /// </summary>
    private void InitialiseStock()
    {
        _stock.Clear();
        _maxStock.Clear();

        if (catalogue == null)
        {
            Debug.LogWarning("[StoreFrontManager] No StoreCatalogue assigned — stock not initialised.");
            return;
        }

        foreach (var entry in catalogue.GetEntries())
        {
            if (entry.item == null) continue;

            int max = Mathf.Max(0, entry.maxStock);
            int starting = Mathf.Clamp(entry.startingStock, 0, max);

            if (entry.startingStock > max)
                Debug.LogWarning($"[StoreFrontManager] {entry.item.ItemName} startingStock " +
                                 $"({entry.startingStock}) exceeds maxStock ({max}). Clamped to {starting}.");

            _stock[entry.item] = starting;
            _maxStock[entry.item] = max;
        }

        Debug.Log($"[StoreFrontManager] Stock initialised from '{catalogue.CatalogueName}' " +
                  $"({_stock.Count} items).");
    }
    
    /// <summary>
    /// Adds sold items to the buyback pool at the given price per unit.
    /// If the item is already in the pool the count is incremented;
    /// the price is kept as-is since BaseSellValue is fixed on the asset.
    /// </summary>
    private void AddToBuyback(ItemDefinition item, int amount, float pricePerUnit)
    {
        if (_buyback.TryGetValue(item, out var existing))
        {
            _buyback[item] = new BuybackEntry
            {
                count = existing.count + amount,
                pricePerUnit = pricePerUnit
            };
        }
        else
        {
            _buyback[item] = new BuybackEntry { count = amount, pricePerUnit = pricePerUnit };
        }
 
        OnBuybackChanged?.Invoke(item, _buyback[item].count);
    }
 
    /// <summary>
    /// Removes items from the buyback pool, clearing the entry entirely when count hits zero.
    /// </summary>
    private void RemoveFromBuyback(ItemDefinition item, int amount)
    {
        if (!_buyback.TryGetValue(item, out var entry)) return;
 
        int newCount = entry.count - amount;
        if (newCount <= 0)
            _buyback.Remove(item);
        else
            _buyback[item] = new BuybackEntry { count = newCount, pricePerUnit = entry.pricePerUnit };
 
        OnBuybackChanged?.Invoke(item, Mathf.Max(0, newCount));
    }

    /// <summary>
    /// Sets stock for an item and fires OnStockChanged.
    /// All stock changes go through here.
    /// </summary>
    private void SetStock(ItemDefinition item, int newStock)
    {
        _stock[item] = Mathf.Max(0, newStock);
        OnStockChanged?.Invoke(item, _stock[item]);
    }

    /// <summary>
    /// Sets the coin balance and fires OnBalanceChanged.
    /// All balance changes go through here.
    /// </summary>
    private void SetBalance(float newBalance)
    {
        _coinBalance = Mathf.Max(0f, newBalance);
        OnBalanceChanged?.Invoke(_coinBalance);
    }
    #endregion
    
    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Add 100 Coins")]
    private void DebugAdd100Coins()
    {
        SetBalance(_coinBalance + 100f);
        Debug.Log($"[StoreFrontManager] Debug: Added 100 coins. Balance: {_coinBalance}");
    }

    [ContextMenu("Debug/Reset Balance")]
    private void DebugResetBalance()
    {
        SetBalance(0f);
        Debug.Log("[StoreFrontManager] Debug: Balance reset to 0.");
    }

    [ContextMenu("Debug/Sell First Available Item")]
    private void DebugSellFirstItem()
    {
        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[StoreFrontManager] No PlayerInventory instance found.");
            return;
        }

        var sellable = PlayerInventory.Instance.GetSellableItems();
        if (sellable == null || sellable.Count == 0)
        {
            Debug.LogWarning("[StoreFrontManager] No sellable items in inventory.");
            return;
        }

        Sell(sellable[0], 1);
    }

    [ContextMenu("Debug/Buy First Available Item")]
    private void DebugBuyFirstItem()
    {
        var buyable = GetBuyableItems();
        if (buyable == null || buyable.Count == 0)
        {
            Debug.LogWarning("[StoreFrontManager] No items in catalogue to buy.");
            return;
        }

        Buy(buyable[0], 1);
    }
    
    [ContextMenu("Debug/Buyback First Available Item")]
    private void DebugBuybackFirstItem()
    {
        var buyback = GetBuybackItems();
        if (buyback == null || buyback.Count == 0)
        {
            Debug.LogWarning("[StoreFrontManager] No items in buyback pool.");
            return;
        }
 
        Buyback(buyback[0], 1);
    }
#endif
    #endregion
}