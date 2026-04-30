using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class StoreFrontManager : MonoBehaviour, IHandler, IBiomeOccupant
{
    [Header("StoreFront Identity")]
    [Tooltip("The biome this storefront belongs to. Determines when tier 2 items/recipes are added to the store.")]
    [SerializeField] private BiomeManager.BiomeType parentBiome;

    [Header("Storefront Settings")]
    [Tooltip("LayerMask used to detect taps on the storefront. Should be set to a layer that the storefront's collider is on.")]
    [SerializeField] private LayerMask interactableLayer;
    [Tooltip("The amount of coins the player starts with when the game begins. Can be set to 0 for a 'broke' start, or higher for testing purposes.")]
    [SerializeField] private float startingCoinBalance = 0f;

    [Header("Tiered Data (Data Sources Only)")]
    [Tooltip("Catalogue of items available for purchase in this store at tier 1. Items in this catalogue are available for purchase as soon as the game starts.")]
    [SerializeField] private StoreCatalogue tier1Catalogue;
    [Tooltip("Catalogue of items available for purchase in this store at tier 2. " +
             "Items in this catalogue are added to the store when the biome reaches tier 2.")]
    [SerializeField] private StoreCatalogue tier2Catalogue;
    [Tooltip("The recipe book defining recipes available for purchase in this store at tier 1. Recipes in this book are available for purchase as soon as the game starts.")]
    [SerializeField] private RecipeBook tier1RecipeBook;
    [Tooltip("The recipe book defining recipes available for purchase in this store at tier 2. " +
             "Recipes in this book are added to the store when the biome reaches tier 2.")]
    [SerializeField] private RecipeBook tier2RecipeBook;

    // Actions
    public event Action<float> OnBalanceChanged;
    public event Action<bool> OnStorefrontToggled;
    public event Action<ItemDefinition, int> OnStockChanged;
    public event Action<ItemDefinition, int> OnBuybackChanged;
    public event Action<RecipeDefinition, int> OnRecipeStockChanged;

    private float _coinBalance;
    private bool _isOpen;

    // Runtime
    private readonly Dictionary<ItemDefinition, int> _stock = new();
    private readonly Dictionary<ItemDefinition, int> _maxStock = new();
    private readonly Dictionary<ItemDefinition, BuybackEntry> _buyback = new();
    private readonly Dictionary<RecipeDefinition, int> _recipeStock = new();

    /// <summary>
    /// Struct to track buyback items. Stores the count of the item in the buyback pool, and the price per unit (based on the price it was sold to the store for).
    /// This allows the player to buy back items they sold, at the same price they sold them for, until that buyback stock runs out.
    /// </summary>
    private struct BuybackEntry
    {
        public int count;
        public float pricePerUnit;
    }

    #region Accessors

    public float CoinBalance => _coinBalance;
    public bool IsOpen => _isOpen;
    public BiomeManager.BiomeType HomeBiome => parentBiome;

    public int GetStock(ItemDefinition item) => item == null ? 0 : _stock.GetValueOrDefault(item, 0);
    public int GetMaxStock(ItemDefinition item) => item == null ? 0 : _maxStock.GetValueOrDefault(item, 0);

    public int GetBuybackCount(ItemDefinition item)
        => item == null ? 0 : (_buyback.TryGetValue(item, out var e) ? e.count : 0);

    public float GetBuybackPrice(ItemDefinition item)
        => item == null ? 0f : (_buyback.TryGetValue(item, out var e) ? e.pricePerUnit : 0f);

    public int GetRecipeStock(RecipeDefinition recipe)
        => recipe == null ? 0 : _recipeStock.GetValueOrDefault(recipe, 0);

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

        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeTierChanged += HandleBiomeTierChanged;

        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    private void OnDisable()
    {
        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeTierChanged -= HandleBiomeTierChanged;

        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }

    #endregion

    #region Input Handling

    private void HandleWorldTap(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        _isOpen = !_isOpen;
        OnStorefrontToggled?.Invoke(_isOpen);
    }

    #endregion

    #region UI API (Runtime Driven)
    /// <summary>
    /// Returns a list of items that are currently available for purchase in the store (i.e. have stock > 0).
    /// This is used by the UI to display the store's inventory.
    /// </summary>
    public List<ItemDefinition> GetBuyableItems()
    {
        return new List<ItemDefinition>(_stock.Keys);
    }

    /// <summary>
    /// Returns a list of items that the player currently has in their inventory that are sellable to the store
    /// (i.e. have count > 0 and ItemDefinition.IsAvailableForStorefront is true).
    /// </summary>
    /// <returns></returns>
    public List<ItemDefinition> GetSellableItems()
    {
        return PlayerInventory.Instance?.GetSellableItems() ?? new List<ItemDefinition>();
    }

    /// <summary>
    /// Returns a list of items that are currently in the buyback pool (i.e. have count > 0).
    /// </summary>
    /// <returns></returns>
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
    /// Returns a list of recipes that are currently available for purchase in the store (i.e. have stock > 0).
    /// Used by the UI to display the store's recipe inventory.
    /// </summary>
    /// <returns></returns>
    public List<RecipeDefinition> GetBuyableRecipes()
    {
        return new List<RecipeDefinition>(_recipeStock.Keys);
    }

    #endregion

    #region Trading
    /// <summary>
    /// Attempts to sell the given item from the player's inventory to the store.
    /// Validates that the item is sellable, that the player has enough of it, and that the store can accept it (e.g. not exceeding max stock).
    /// If successful, removes the item from the player's inventory, adds coins to the player's balance based on the item's BaseSellValue,
    /// and adds the item to the buyback pool at that price.
    /// </summary>
    /// <param name="item"></param>
    /// <param name="amount"></param>
    /// <returns></returns>
    public bool Sell(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (PlayerInventory.Instance == null) return false;

        if (!PlayerInventory.Instance.Remove(item, amount)) return false;

        float price = item.BaseSellValue;
        float earned = price * amount;

        SetBalance(_coinBalance + earned);
        AddToBuyback(item, amount, price);

        return true;
    }

    /// <summary>
    /// Attempts to buy the given item from the store and add it to the player's inventory.
    /// Validates that the item is in stock, that the player has enough coins, and that the store has enough stock to fulfill the purchase.
    /// If successful, deducts coins from the player's balance based on the item's BaseBuyValue, reduces the store's stock of that item,
    /// and adds the item to the player's inventory.
    /// </summary>
    /// <param name="item"></param>
    /// <param name="amount"></param>
    /// <returns></returns>
    public bool Buy(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (!_stock.TryGetValue(item, out var currentStock)) return false;
        if (currentStock < amount) return false;

        float cost = item.BaseBuyValue * amount;
        if (_coinBalance < cost) return false;

        SetStock(item, currentStock - amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);
        QuestManager.Instance?.RecordProgress(
            QuestObjectiveType.BuyItem,
            item.ItemName,
            amount
        );

        return true;
    }

    /// <summary>
    /// Attempts to buy the given recipe from the store and add it to the player's known recipes in the CraftingManager.
    /// Validates that the recipe is in stock, that the player has enough coins, and that the player doesn't already know the recipe.
    /// If successful, deducts coins from the player's balance based on the recipe's BuyPrice, reduces the store's stock of that recipe,
    /// and unlocks the recipe in the CraftingManager so the player can craft it.
    /// </summary>
    /// <param name="recipe"></param>
    /// <returns></returns>
    public bool BuyRecipe(RecipeDefinition recipe)
    {
        if (recipe == null) return false;
        if (CraftingManager.Instance == null) return false;
        if (CraftingManager.Instance.HasRecipe(recipe)) return false;

        if (!_recipeStock.TryGetValue(recipe, out int stock) || stock <= 0)
            return false;

        if (_coinBalance < recipe.BuyPrice) return false;

        SetRecipeStock(recipe, stock - 1);
        SpendCoins(recipe.BuyPrice);
        CraftingManager.Instance.UnlockRecipe(recipe);

        return true;
    }

    /// <summary>
    /// Attempts to buy back the given item from the buyback pool and add it to the player's inventory.
    /// Validates that the item is in the buyback pool, that the player has enough coins, and that the buyback pool has enough of that item to fulfill the purchase.
    /// </summary>
    /// <param name="item"></param>
    /// <param name="amount"></param>
    /// <returns></returns>
    public bool Buyback(ItemDefinition item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (!_buyback.TryGetValue(item, out var entry) || entry.count < amount) return false;

        float cost = entry.pricePerUnit * amount;
        if (_coinBalance < cost) return false;

        RemoveFromBuyback(item, amount);
        SpendCoins(cost);
        PlayerInventory.Instance.Add(item, amount);

        return true;
    }
    
    /// <summary>
    /// Returns true if the player can afford the given upgrade — checks coin balance,
    /// material ingredients in inventory, and UpgradeManager prerequisites.
    /// Called by StoreFrontUI to drive the purchase button state on each upgrade card.
    /// </summary>
    public bool CanBuyUpgrade(UpgradeDefinition upgrade)
    {
        if (upgrade == null) return false;
        if (UpgradeManager.Instance == null) return false;

        // Prerequisite check — biome tier and already-applied guard.
        if (!UpgradeManager.Instance.MeetsPrerequisites(upgrade)) return false;

        // Coin check.
        if (_coinBalance < upgrade.CoinCost) return false;

        // Material ingredient check.
        if (PlayerInventory.Instance != null)
        {
            foreach (var ingredient in upgrade.MaterialCost)
            {
                if (ingredient.item == null) continue;
                if (PlayerInventory.Instance.GetCount(ingredient.item) < ingredient.quantity)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to purchase an upgrade. Validates affordability, deducts coin and
    /// material cost, then delegates effect application to UpgradeManager.
    /// Returns false if any check fails — no partial deductions occur.
    /// </summary>
    public bool BuyUpgrade(UpgradeDefinition upgrade)
    {
        if (!CanBuyUpgrade(upgrade)) return false;

        // Deduct coin cost.
        if (upgrade.CoinCost > 0)
            SpendCoins(upgrade.CoinCost);

        // Deduct material cost.
        if (PlayerInventory.Instance != null)
        {
            foreach (var ingredient in upgrade.MaterialCost)
            {
                if (ingredient.item == null) continue;
                PlayerInventory.Instance.Remove(ingredient.item, ingredient.quantity);
            }
        }

        // Apply the upgrade effect via UpgradeManager.
        return UpgradeManager.Instance.ApplyUpgrade(upgrade);
    }

    /// <summary>
    /// Attempts to spend the given amount of coins from the player's balance.
    /// Validates that the amount is positive and that the player has enough coins.
    /// </summary>
    /// <param name="cost"></param>
    /// <returns></returns>
    public bool SpendCoins(float cost)
    {
        if (cost <= 0 || _coinBalance < cost) return false;
        SetBalance(_coinBalance - cost);
        return true;
    }

    #endregion

    #region Initialisation (Data → Runtime)

    private void InitialiseStock()
    {
        _stock.Clear();
        _maxStock.Clear();

        LoadCatalogueIntoStock(tier1Catalogue);
    }

    private void InitialiseRecipeStock()
    {
        _recipeStock.Clear();
        LoadRecipeBookIntoStock(tier1RecipeBook);
    }

    /// <summary>
    /// Loads the items from the given catalogue into the store's runtime stock dictionaries, using the starting stock and max stock defined in each entry.
    /// </summary>
    /// <param name="catalogue"></param>
    private void LoadCatalogueIntoStock(StoreCatalogue catalogue)
    {
        if (catalogue == null) return;

        foreach (var entry in catalogue.GetEntries())
        {
            if (entry.item == null) continue;

            int max = Mathf.Max(0, entry.maxStock);
            int starting = Mathf.Clamp(entry.startingStock, 0, max);

            _stock[entry.item] = starting;
            _maxStock[entry.item] = max;
        }
    }

    /// <summary>
    /// Loads the recipes from the given recipe book into the store's runtime recipe stock dictionary, using the starting stock defined in each recipe (or 1 if not defined).
    /// </summary>
    /// <param name="book"></param>
    private void LoadRecipeBookIntoStock(RecipeBook book)
    {
        if (book == null) return;

        foreach (var recipe in book.GetStoreRecipes())
        {
            if (recipe == null) continue;
            _recipeStock[recipe] = 1;
        }
    }

    #endregion

    #region Tier Handling
    /// <summary>
    /// Called when biome tier changes.
    /// If the change is for this storefront's parent biome, and the new tier is 2 or higher, merges the tier 2 catalogue
    /// and recipe book into the store's runtime stock.
    /// </summary>
    /// <param name="biome"></param>
    /// <param name="tier"></param>
    private void HandleBiomeTierChanged(BiomeManager.BiomeType biome, int tier)
    {
        if (biome != parentBiome) return;
        if (tier < 2) return;

        MergeTier2Catalogue();
        MergeTier2RecipeBook();
    }

    private void MergeTier2Catalogue()
    {
        if (tier2Catalogue == null) return;

        foreach (var entry in tier2Catalogue.GetEntries())
        {
            if (entry.item == null) continue;
            if (_stock.ContainsKey(entry.item)) continue;

            int max = Mathf.Max(0, entry.maxStock);
            int starting = Mathf.Clamp(entry.startingStock, 0, max);

            _stock[entry.item] = starting;
            _maxStock[entry.item] = max;

            OnStockChanged?.Invoke(entry.item, starting);
        }
    }

    private void MergeTier2RecipeBook()
    {
        if (tier2RecipeBook == null) return;

        foreach (var recipe in tier2RecipeBook.GetStoreRecipes())
        {
            if (recipe == null) continue;
            if (_recipeStock.ContainsKey(recipe)) continue;

            _recipeStock[recipe] = 1;
            OnRecipeStockChanged?.Invoke(recipe, 1);
        }
    }

    #endregion

    #region Helpers

    private void AddToBuyback(ItemDefinition item, int amount, float price)
    {
        if (_buyback.TryGetValue(item, out var existing))
        {
            _buyback[item] = new BuybackEntry
            {
                count = existing.count + amount,
                pricePerUnit = price
            };
        }
        else
        {
            _buyback[item] = new BuybackEntry
            {
                count = amount,
                pricePerUnit = price
            };
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

    private void SetStock(ItemDefinition item, int value)
    {
        _stock[item] = Mathf.Max(0, value);
        OnStockChanged?.Invoke(item, _stock[item]);
    }

    private void SetRecipeStock(RecipeDefinition recipe, int value)
    {
        _recipeStock[recipe] = Mathf.Max(0, value);
        OnRecipeStockChanged?.Invoke(recipe, _recipeStock[recipe]);
    }

    private void SetBalance(float value)
    {
        _coinBalance = Mathf.Max(0f, value);
        OnBalanceChanged?.Invoke(_coinBalance);
    }

    #endregion
}