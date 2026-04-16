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
public class StoreFrontManager : MonoBehaviour, IHandler, IBiomeOccupant
{
    [Header("StoreFront Identity")]
    [SerializeField, Tooltip("The biome this object belongs to. Used to validate placement.")]
    private BiomeManager.BiomeType parentBiome;
    
    [Header("Storefront Settings")]
    [SerializeField, Tooltip("Layer mask for tap detection.")]
    private LayerMask interactableLayer;
    [SerializeField, Tooltip("Starting coin balance for the player.")]
    private float startingCoinBalance = 0f;
    [SerializeField, Tooltip("The catalogue of items this store stocks for the player to buy.")]
    private StoreCatalogue catalogue;
    [SerializeField, Tooltip("The recipe book this store stocks for the player to buy.")]
    private RecipeBook recipeBook;

    /// <summary>Fired whenever coinBalance changes.</summary>
    public event Action<float> OnBalanceChanged;

    /// <summary>Fired when the storefront is opened or closed.</summary>
    public event Action<bool> OnStorefrontToggled;

    /// <summary>Fired whenever a stock count changes.</summary>
    public event Action<ItemDefinition, int> OnStockChanged;

    /// <summary>Fired whenever the buyback changes.</summary>
    public event Action<ItemDefinition, int> OnBuybackChanged;

    /// <summary>Fired whenever recipe stock changes.</summary>
    public event Action<RecipeDefinition, int> OnRecipeStockChanged;

    private float _coinBalance;
    private bool _isOpen;

    private readonly Dictionary<ItemDefinition, int> _stock = new();
    private readonly Dictionary<ItemDefinition, int> _maxStock = new();

    private readonly Dictionary<ItemDefinition, BuybackEntry> _buyback = new();

    /// <summary>Runtime stock levels for recipes.</summary>
    private readonly Dictionary<RecipeDefinition, int> _recipeStock = new();

    private struct BuybackEntry
    {
        public int count;
        public float pricePerUnit;
    }

    #region Accessors

    public float CoinBalance => _coinBalance;
    public bool IsOpen => _isOpen;

    public int GetStock(ItemDefinition item)
    {
        if (item == null) return 0;
        return _stock.GetValueOrDefault(item, 0);
    }

    public int GetMaxStock(ItemDefinition item)
    {
        if (item == null) return 0;
        return _maxStock.GetValueOrDefault(item, 0);
    }

    public int GetBuybackCount(ItemDefinition item)
    {
        if (item == null) return 0;
        return _buyback.TryGetValue(item, out var entry) ? entry.count : 0;
    }

    public float GetBuybackPrice(ItemDefinition item)
    {
        if (item == null) return 0f;
        return _buyback.TryGetValue(item, out var entry) ? entry.pricePerUnit : 0f;
    }

    public int GetRecipeStock(RecipeDefinition recipe)
    {
        if (recipe == null) return 0;
        return _recipeStock.GetValueOrDefault(recipe, 0);
    }
    
    /// <summary>The biome this slot belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => parentBiome;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        SetBalance(startingCoinBalance);
        InitialiseStock();
        InitialiseRecipeStock();
    }

    private void OnEnable()
    {
        BiomeManager.Instance?.RegisterOccupant(this);
        
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
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

    public List<ItemDefinition> GetSellableItems()
    {
        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[StoreFrontManager] No PlayerInventory instance found.");
            return new List<ItemDefinition>();
        }
        return PlayerInventory.Instance.GetSellableItems();
    }

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

    public List<ItemDefinition> GetBuybackItems()
    {
        var result = new List<ItemDefinition>();
        foreach (var kvp in _buyback)
        {
            if (kvp.Value.count > 0)
                result.Add(kvp.Key);
        }
        return result;
    }

    /// <summary>
    /// Returns all recipes available in the store regardless of stock.
    /// Stock per recipe should be checked via GetRecipeStock().
    /// </summary>
    public List<RecipeDefinition> GetBuyableRecipes()
    {
        if (recipeBook == null)
        {
            Debug.LogWarning("[StoreFrontManager] No RecipeBook assigned.");
            return new List<RecipeDefinition>();
        }
        return recipeBook.GetStoreRecipes();
    }

    #endregion

    #region Public API - Trading

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

        SetStock(item, currentStock - amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);

        Debug.Log($"[StoreFrontManager] Bought {amount}x {item.ItemName} for {cost} coins. " +
                  $"Stock remaining: {_stock[item]}. Balance: {_coinBalance}");
        return true;
    }

    /// <summary>
    /// Buys a recipe from the store, unlocks it in CraftingManager, and deducts coins.
    /// Returns false if not in stock, already owned, or insufficient coins.
    /// </summary>
    public bool BuyRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return false;

        if (CraftingManager.Instance == null)
        {
            Debug.LogWarning("[StoreFrontManager] No CraftingManager instance found.");
            return false;
        }

        if (CraftingManager.Instance.HasRecipe(recipe))
        {
            Debug.LogWarning($"[StoreFrontManager] BuyRecipe failed - already owns: {recipe.RecipeName}");
            return false;
        }

        if (!_recipeStock.TryGetValue(recipe, out int stock) || stock <= 0)
        {
            Debug.LogWarning($"[StoreFrontManager] BuyRecipe failed - out of stock: {recipe.RecipeName}");
            return false;
        }

        if (_coinBalance < recipe.BuyPrice)
        {
            Debug.LogWarning($"[StoreFrontManager] BuyRecipe failed - insufficient coins for " +
                             $"{recipe.RecipeName} (costs {recipe.BuyPrice}, have {_coinBalance}).");
            return false;
        }

        SetRecipeStock(recipe, stock - 1);
        SpendCoins(recipe.BuyPrice);
        CraftingManager.Instance.UnlockRecipe(recipe);

        Debug.Log($"[StoreFrontManager] Bought recipe: {recipe.RecipeName} for {recipe.BuyPrice} coins. " +
                  $"Balance: {_coinBalance}");
        return true;
    }

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

        RemoveFromBuyback(item, amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);

        Debug.Log($"[StoreFrontManager] Bought back {amount}x {item.ItemName} for {cost} coins. " +
                  $"Balance: {_coinBalance}");
        return true;
    }

    public bool SpendCoins(float cost)
    {
        if (cost <= 0) return false;

        if (_coinBalance < cost)
        {
            Debug.LogWarning($"[StoreFrontManager] SpendCoins failed - need {cost}, have {_coinBalance}.");
            return false;
        }

        SetBalance(_coinBalance - cost);
        Debug.Log($"[StoreFrontManager] Spent {cost} coins. Balance: {_coinBalance}");
        return true;
    }

    #endregion

    #region Private Helpers

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

    private void InitialiseRecipeStock()
    {
        _recipeStock.Clear();

        if (recipeBook == null)
        {
            Debug.LogWarning("[StoreFrontManager] No RecipeBook assigned — recipe stock not initialised.");
            return;
        }

        foreach (var recipe in recipeBook.GetStoreRecipes())
        {
            if (recipe == null) continue;
            _recipeStock[recipe] = 1; // one copy available per recipe by default
        }

        Debug.Log($"[StoreFrontManager] Recipe stock initialised ({_recipeStock.Count} recipes).");
    }

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

    private void SetStock(ItemDefinition item, int newStock)
    {
        _stock[item] = Mathf.Max(0, newStock);
        OnStockChanged?.Invoke(item, _stock[item]);
    }

    private void SetRecipeStock(RecipeDefinition recipe, int newStock)
    {
        _recipeStock[recipe] = Mathf.Max(0, newStock);
        OnRecipeStockChanged?.Invoke(recipe, _recipeStock[recipe]);
    }

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

    [ContextMenu("Debug/Buy First Available Recipe")]
    private void DebugBuyFirstRecipe()
    {
        var recipes = GetBuyableRecipes();
        if (recipes == null || recipes.Count == 0)
        {
            Debug.LogWarning("[StoreFrontManager] No recipes in store.");
            return;
        }

        BuyRecipe(recipes[0]);
    }
#endif
    #endregion
}