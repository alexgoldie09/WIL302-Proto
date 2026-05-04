using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.Serialization;

public class StoreFrontUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StoreFrontManager storeFrontManager;
    [SerializeField] private RectTransform panel;
    [SerializeField] private GameObject biomePanel;
    [SerializeField] private GameObject craftingPanel;
    [SerializeField] private GameObject inventoryPanel;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI coinDisplay;
    [SerializeField] private TMP_FontAsset defaultFont;
    [SerializeField] private Button buyTab;
    [SerializeField] private Button sellTab;
    [SerializeField] private Button upgradeTab;

    [Header("Pages")]
    [SerializeField] private GameObject buyPage;
    [SerializeField] private GameObject sellPage;
    [SerializeField] private GameObject upgradePage;

    [Header("Upgrade Page")]
    [SerializeField] private Transform upgradeGridContainer;

    [Header("Buy Page")]
    [SerializeField] private Transform buyGridContainer;
    
    [Header("Sell Page")]
    [SerializeField] private Transform sellGridContainer;
    [SerializeField] private DropZoneHandler dropZone;
    [SerializeField] private TextMeshProUGUI totalSummaryText;
    [SerializeField] private Button confirmSellButton;
    [SerializeField] private Transform buybackGridContainer;
    [SerializeField] private Canvas rootCanvas;

    [Header("Slide Animation")]
    [SerializeField] private float slideDuration = 0.3f;

    private Vector2 _hiddenPos;
    private Vector2 _shownPos;
    private bool _isOpen = false;
    private Coroutine _anim;
    private readonly Dictionary<ItemDefinition, int> _sellBasket = new();

    private enum StorePage { Buy, Sell, Upgrades }
    private StorePage _currentPage = StorePage.Buy;

    private void Awake()
    {
        _shownPos = panel.anchoredPosition;
        _hiddenPos = _shownPos - new Vector2(0, panel.rect.height + 50f);
        panel.anchoredPosition = _hiddenPos;

        buyTab.onClick.AddListener(() => SetPage(StorePage.Buy));
        sellTab.onClick.AddListener(() => SetPage(StorePage.Sell));
        upgradeTab.onClick.AddListener(() => SetPage(StorePage.Upgrades));
        dropZone.OnItemDropped += OnItemDroppedInZone;
        confirmSellButton.onClick.AddListener(OnConfirmSell);

        SetPage(StorePage.Buy);
    }

    private void OnEnable()
    {
        storeFrontManager.OnStorefrontToggled += SetOpen;
        storeFrontManager.OnBalanceChanged += UpdateCoinDisplay;
        storeFrontManager.OnStockChanged += OnStockChanged;
        storeFrontManager.OnRecipeStockChanged += OnRecipeStockChanged;

        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied += OnUpgradeApplied;
    }

    private void OnDisable()
    {
        storeFrontManager.OnStorefrontToggled -= SetOpen;
        storeFrontManager.OnBalanceChanged -= UpdateCoinDisplay;
        storeFrontManager.OnStockChanged -= OnStockChanged;
        storeFrontManager.OnRecipeStockChanged -= OnRecipeStockChanged;

        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied -= OnUpgradeApplied;
    }

    private void Update()
    {
        if (!_isOpen) return;

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            SetOpen(false);
    }

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;
        
        biomePanel.SetActive(!_isOpen);
        AudioManager.Instance?.PlaySFX("menu_click", 0.4f);
        
        if (_isOpen)
        {
            panel.gameObject.SetActive(true);
            UpdateCoinDisplay(storeFrontManager.CoinBalance);
            RefreshCurrentPage();
            if (craftingPanel) craftingPanel.GetComponent<CraftingUI>().ToggleButton.SetActive(false);
            if (inventoryPanel) inventoryPanel.GetComponent<InventoryUI>().ToggleButton.SetActive(false);
        }
        else
        {
            _sellBasket.Clear();
            if (craftingPanel) craftingPanel.GetComponent<CraftingUI>().ToggleButton.SetActive(true);
            if (inventoryPanel) inventoryPanel.GetComponent<InventoryUI>().ToggleButton.SetActive(true);
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }

    private void SetPage(StorePage page)
    {
        _currentPage = page;
        
        AudioManager.Instance?.PlaySFX("menu_click", 0.4f);

        buyPage.SetActive(page == StorePage.Buy);
        sellPage.SetActive(page == StorePage.Sell);
        upgradePage.SetActive(page == StorePage.Upgrades);

        var activeColor   = Color.white;
        var inactiveColor = new Color(0.6f, 0.6f, 0.6f);

        buyTab.image.color     = page == StorePage.Buy      ? activeColor : inactiveColor;
        sellTab.image.color    = page == StorePage.Sell     ? activeColor : inactiveColor;
        upgradeTab.image.color = page == StorePage.Upgrades ? activeColor : inactiveColor;

        RefreshCurrentPage();
    }

    private void RefreshCurrentPage()
    {
        if (!_isOpen) return;

        switch (_currentPage)
        {
            case StorePage.Buy:      RefreshBuyPage();      break;
            case StorePage.Sell:     RefreshSellPage();     break;
            case StorePage.Upgrades: RefreshUpgradesPage(); break;
        }
    }

    #region Buy Page

    private void RefreshBuyPage()
    {
        foreach (Transform child in buyGridContainer)
            Destroy(child.gameObject);
        
        BuildItemsSection();
        BuildRecipesSection();
    }

    private void BuildItemsSection()
    {
        var items = storeFrontManager.GetBuyableItems();
        if (items.Count == 0) return;

        // Section label
        BuildSectionLabel("Items", buyGridContainer);

        foreach (var item in items)
            BuildItemCell(item);
    }

    private void BuildRecipesSection()
    {
        var recipes = storeFrontManager.GetBuyableRecipes();
        if (recipes.Count == 0) return;

        // Section label
        BuildSectionLabel("Recipes", buyGridContainer);

        foreach (var recipe in recipes)
            BuildRecipeCell(recipe);
    }

    private void BuildSectionLabel(string text, Transform parent)
    {
        var labelGO = new GameObject($"Label_{text}", typeof(TextMeshProUGUI));
        labelGO.transform.SetParent(parent, false);

        // Force full width by breaking out of grid
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(0, 30);

        var label = labelGO.GetComponent<TextMeshProUGUI>();
        label.text = text.ToUpper();
        label.fontSize = 13;
        label.font = defaultFont;
        label.fontStyle = FontStyles.Bold;
        label.color = new Color(0.7f, 0.7f, 0.7f);
        label.alignment = TextAlignmentOptions.MidlineLeft;

        // Add layout element to make it span full width
        var layout = labelGO.AddComponent<LayoutElement>();
        layout.preferredWidth = 9999;
        layout.preferredHeight = 30;
        layout.flexibleWidth = 1;
    }

    private void BuildItemCell(ItemDefinition item)
    {
        int stock = storeFrontManager.GetStock(item);
        int maxStock = storeFrontManager.GetMaxStock(item);

        var cell = new GameObject("BuyCell", typeof(RectTransform));
        cell.transform.SetParent(buyGridContainer, false);

        // Card background
        var cardGO = new GameObject("CardBackground", typeof(Image));
        cardGO.transform.SetParent(cell.transform, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.one;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        cardGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

        // Icon
        var iconGO = new GameObject("Icon", typeof(Image));
        iconGO.transform.SetParent(cell.transform, false);
        var iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0, 30f);
        iconRect.sizeDelta = new Vector2(64, 64);  
        var iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = item.Icon;
        iconImage.preserveAspect = true;
        iconImage.enabled = item.Icon != null;

        // Price
        var priceGO = new GameObject("Price", typeof(TextMeshProUGUI));
        priceGO.transform.SetParent(cell.transform, false);
        var priceRect = priceGO.GetComponent<RectTransform>();
        priceRect.anchorMin = new Vector2(0, 0.8f);
        priceRect.anchorMax = new Vector2(1, 1f);
        priceRect.offsetMin = new Vector2(4, 0);
        priceRect.offsetMax = new Vector2(-4, 0);
        var priceText = priceGO.GetComponent<TextMeshProUGUI>();
        priceText.text = $"{item.BaseBuyValue:F0} coins";
        priceText.font = defaultFont;
        priceText.fontSize = 11;
        priceText.color = Color.yellow;
        priceText.alignment = TextAlignmentOptions.Center;

        // Item name gradient — sits behind the name text to improve legibility
        // over the icon. Same offset pattern as sell cell gradient.
        var buyGradientGO = new GameObject("NameGradient", typeof(Image));
        buyGradientGO.transform.SetParent(cell.transform, false);
        var buyGradientRect = buyGradientGO.GetComponent<RectTransform>();
        buyGradientRect.anchorMin = new Vector2(0, 0.55f);
        buyGradientRect.anchorMax = new Vector2(1, 0.8f);
        buyGradientRect.offsetMin = new Vector2(0, -70f);
        buyGradientRect.offsetMax = new Vector2(0, -70f);
        buyGradientGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // Item name
        var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(cell.transform, false);
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.6f);
        nameRect.anchorMax = new Vector2(1, 0.8f);
        nameRect.offsetMin = new Vector2(4, -65f);
        nameRect.offsetMax = new Vector2(-4, -65f);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = item.ItemName;
        nameText.font = defaultFont;
        nameText.fontSize = 12;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.overflowMode = TextOverflowModes.Ellipsis;

        // Stock
        var stockGO = new GameObject("Stock", typeof(TextMeshProUGUI));
        stockGO.transform.SetParent(cell.transform, false);
        var stockRect = stockGO.GetComponent<RectTransform>();
        stockRect.anchorMin = new Vector2(0, 0.18f);
        stockRect.anchorMax = new Vector2(1, 0.35f);
        stockRect.offsetMin = new Vector2(4, 0);
        stockRect.offsetMax = new Vector2(-4, 0);
        var stockText = stockGO.GetComponent<TextMeshProUGUI>();
        stockText.text = $"{stock}/{maxStock}";
        stockText.font = defaultFont;
        stockText.fontSize = 11;
        stockText.color = stock > 0 ? Color.white : Color.red;
        stockText.alignment = TextAlignmentOptions.Center;

        // Buy button
        var btnGO = new GameObject("BuyButton", typeof(Image), typeof(Button));
        btnGO.transform.SetParent(cell.transform, false);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.1f, 0f);
        btnRect.anchorMax = new Vector2(0.9f, 0.18f);
        btnRect.offsetMin = new Vector2(0, 4);
        btnRect.offsetMax = new Vector2(0, -4);
        btnGO.GetComponent<Image>().color = stock > 0
            ? new Color(0.2f, 0.6f, 0.2f)
            : new Color(0.4f, 0.4f, 0.4f);
        var btnTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTextRect = btnTextGO.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = stock > 0 ? "Buy" : "Sold Out";
        btnText.font = defaultFont;
        btnText.fontSize = 12;
        btnText.fontStyle = FontStyles.Bold;
        btnText.color = Color.white;
        btnText.alignment = TextAlignmentOptions.Center;

        var capturedItem = item;
        var btn = btnGO.GetComponent<Button>();
        bool canAfford = storeFrontManager.CoinBalance >= item.BaseBuyValue;
        btn.interactable = stock > 0 && canAfford;
        btn.onClick.AddListener(() =>
        {
            if (storeFrontManager.Buy(capturedItem, 1))
                RefreshBuyPage();
        });
    }

    private void BuildRecipeCell(RecipeDefinition recipe)
    {
        bool alreadyOwned = CraftingManager.Instance != null &&
                            CraftingManager.Instance.HasRecipe(recipe);
        int stock = storeFrontManager.GetRecipeStock(recipe);
        bool canAfford = storeFrontManager.CoinBalance >= recipe.BuyPrice;
        bool canBuy = !alreadyOwned && stock > 0 && canAfford;

        var cell = new GameObject("RecipeCell", typeof(RectTransform));
        cell.transform.SetParent(buyGridContainer, false);

        // Card background
        var cardGO = new GameObject("CardBackground", typeof(Image));
        cardGO.transform.SetParent(cell.transform, false);
        var cardRect = cardGO.GetComponent<RectTransform>();
        cardRect.anchorMin = Vector2.zero;
        cardRect.anchorMax = Vector2.one;
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        cardGO.GetComponent<Image>().color = new Color(0.1f, 0.05f, 0.2f, 0.6f);

        // Icon
        var iconGO = new GameObject("Icon", typeof(Image));
        iconGO.transform.SetParent(cell.transform, false);
        var iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0, 30f);
        iconRect.sizeDelta = new Vector2(64, 64);         
        var iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = recipe.Icon;
        iconImage.preserveAspect = true;
        iconImage.enabled = recipe.Icon != null;

        // Price
        var priceGO = new GameObject("Price", typeof(TextMeshProUGUI));
        priceGO.transform.SetParent(cell.transform, false);
        var priceRect = priceGO.GetComponent<RectTransform>();
        priceRect.anchorMin = new Vector2(0, 0.8f);
        priceRect.anchorMax = new Vector2(1, 1f);
        priceRect.offsetMin = new Vector2(4, 0);
        priceRect.offsetMax = new Vector2(-4, 0);
        var priceText = priceGO.GetComponent<TextMeshProUGUI>();
        priceText.text = alreadyOwned ? "Owned" : $"{recipe.BuyPrice:F0} coins";
        priceText.font = defaultFont;
        priceText.fontSize = 11;
        priceText.color = alreadyOwned ? Color.green : Color.yellow;
        priceText.alignment = TextAlignmentOptions.Center;
        
        // Recipe name gradient
        var recipeGradientGO = new GameObject("NameGradient", typeof(Image));
        recipeGradientGO.transform.SetParent(cell.transform, false);
        var recipeGradientRect = recipeGradientGO.GetComponent<RectTransform>();
        recipeGradientRect.anchorMin = new Vector2(0, 0.55f);
        recipeGradientRect.anchorMax = new Vector2(1, 0.8f);
        recipeGradientRect.offsetMin = new Vector2(0, -70f);
        recipeGradientRect.offsetMax = new Vector2(0, -70f);
        recipeGradientGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

        // Recipe name
        var nameGO = new GameObject("RecipeName", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(cell.transform, false);
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.6f);
        nameRect.anchorMax = new Vector2(1, 0.8f);
        nameRect.offsetMin = new Vector2(4, -65f);
        nameRect.offsetMax = new Vector2(-4, -65f);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = recipe.RecipeName;
        nameText.font = defaultFont;
        nameText.fontSize = 12;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.overflowMode = TextOverflowModes.Ellipsis;

        // Ingredients preview
        var ingGO = new GameObject("Ingredients", typeof(TextMeshProUGUI));
        ingGO.transform.SetParent(cell.transform, false);
        var ingRect = ingGO.GetComponent<RectTransform>();
        ingRect.anchorMin = new Vector2(0, 0.18f);
        ingRect.anchorMax = new Vector2(1, 0.6f);
        ingRect.offsetMin = new Vector2(4, -22f);
        ingRect.offsetMax = new Vector2(-4, -22f);
        var ingText = ingGO.GetComponent<TextMeshProUGUI>();
        var ingLines = new System.Text.StringBuilder();
        foreach (var ing in recipe.Ingredients)
            ingLines.AppendLine($"{ing.item?.ItemName} x{ing.quantity}");
        ingText.text = ingLines.ToString().TrimEnd();
        ingText.font = defaultFont;
        ingText.fontSize = 10;
        ingText.color = new Color(0.8f, 0.8f, 0.8f);
        ingText.alignment = TextAlignmentOptions.Center;
        ingText.overflowMode = TextOverflowModes.Ellipsis;

        // Buy button
        var btnGO = new GameObject("BuyButton", typeof(Image), typeof(Button));
        btnGO.transform.SetParent(cell.transform, false);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.1f, 0f);
        btnRect.anchorMax = new Vector2(0.9f, 0.18f);
        btnRect.offsetMin = new Vector2(0, 4);
        btnRect.offsetMax = new Vector2(0, -4);
        btnGO.GetComponent<Image>().color = canBuy
            ? new Color(0.4f, 0.2f, 0.8f)
            : new Color(0.4f, 0.4f, 0.4f);
        var btnTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTextRect = btnTextGO.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = alreadyOwned ? "Owned" : stock <= 0 ? "Out of Stock" : "Buy Recipe";
        btnText.font = defaultFont;
        btnText.fontSize = 12;
        btnText.fontStyle = FontStyles.Bold;
        btnText.color = Color.white;
        btnText.alignment = TextAlignmentOptions.Center;

        var capturedRecipe = recipe;
        var btn = btnGO.GetComponent<Button>();
        btn.interactable = canBuy;
        btn.onClick.AddListener(() =>
        {
            if (storeFrontManager.BuyRecipe(capturedRecipe))
                RefreshBuyPage();
        });
    }

    #endregion

    #region Sell Page

    private void RefreshSellPage()
    {
        RefreshSellInventoryGrid();
        RefreshBuybackGrid();
        UpdateSellSummary();
        
    }

    private void RefreshSellInventoryGrid()
    {
        foreach (Transform child in sellGridContainer)
            Destroy(child.gameObject);

        foreach (var kvp in PlayerInventory.Instance.GetAllItems())
        {
            if (!kvp.Key.IsAvailableForStorefront) continue;

            var cell = new GameObject("SellCell", typeof(RectTransform));
            cell.transform.SetParent(sellGridContainer, false);

            // Card background
            var cardGO = new GameObject("CardBackground", typeof(Image));
            cardGO.transform.SetParent(cell.transform, false);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            cardGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            // Icon
            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(cell.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.3f);
            iconRect.anchorMax = new Vector2(1, 1f);
            iconRect.offsetMin = new Vector2(8, 0);
            iconRect.offsetMax = new Vector2(-8, -8);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = kvp.Key.Icon;
            iconImage.preserveAspect = true;
            iconImage.enabled = kvp.Key.Icon != null;

            // Gradient overlay — sits between icon bottom and text top.
            // offsetMin.y = 22 and offsetMax.y = -22 give the text breathing room
            // so the overlay does not bleed over the name label below it.
            var gradientGO = new GameObject("GradientOverlay", typeof(Image));
            gradientGO.transform.SetParent(cell.transform, false);
            var gradientRect = gradientGO.GetComponent<RectTransform>();
            gradientRect.anchorMin = new Vector2(0, 0.3f);
            gradientRect.anchorMax = new Vector2(1, 0.55f);
            gradientRect.offsetMin = new Vector2(0, -22f);
            gradientRect.offsetMax = new Vector2(0, -22f);
            gradientGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            // Item name
            var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
            nameGO.transform.SetParent(cell.transform, false);
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0.15f);
            nameRect.anchorMax = new Vector2(1, 0.3f);
            nameRect.offsetMin = new Vector2(4, 0);
            nameRect.offsetMax = new Vector2(-4, 0);
            var nameText = nameGO.GetComponent<TextMeshProUGUI>();
            nameText.text = kvp.Key.ItemName;
            nameText.font = defaultFont;
            nameText.fontSize = 11;
            nameText.fontStyle = FontStyles.Bold;
            nameText.color = Color.white;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            // Count
            var countGO = new GameObject("Count", typeof(TextMeshProUGUI));
            countGO.transform.SetParent(cell.transform, false);
            var countRect = countGO.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(0, 0f);
            countRect.anchorMax = new Vector2(1, 0.15f);
            countRect.offsetMin = new Vector2(4, 2);
            countRect.offsetMax = new Vector2(-4, -2);
            var countText = countGO.GetComponent<TextMeshProUGUI>();
            countText.text = $"x{kvp.Value}";
            countText.font = defaultFont;
            countText.fontSize = 11;
            countText.color = Color.white;
            countText.alignment = TextAlignmentOptions.Center;

            var draggable = cell.AddComponent<DraggableItem>();
            draggable.Initialise(kvp.Key, kvp.Value, rootCanvas, countText);
        }
    }

    private void RefreshBuybackGrid()
    {
        foreach (Transform child in buybackGridContainer)
            Destroy(child.gameObject);

        var buybackItems = storeFrontManager.GetBuybackItems();
        if (buybackItems.Count == 0) return;

        foreach (var item in buybackItems)
        {
            int count = storeFrontManager.GetBuybackCount(item);
            float price = storeFrontManager.GetBuybackPrice(item);

            var cell = new GameObject("BuybackCell", typeof(RectTransform));
            cell.transform.SetParent(buybackGridContainer, false);

            // Card background
            var cardGO = new GameObject("CardBackground", typeof(Image));
            cardGO.transform.SetParent(cell.transform, false);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            cardGO.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.3f, 0.6f);

            // Icon
            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(cell.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.35f);
            iconRect.anchorMax = new Vector2(1, 1f);
            iconRect.offsetMin = new Vector2(8, 0);
            iconRect.offsetMax = new Vector2(-8, -8);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = item.Icon;
            iconImage.preserveAspect = true;
            iconImage.enabled = item.Icon != null;

            // Item name
            var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
            nameGO.transform.SetParent(cell.transform, false);
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0.55f);
            nameRect.anchorMax = new Vector2(1, 0.72f);
            nameRect.offsetMin = new Vector2(4, 0);
            nameRect.offsetMax = new Vector2(-4, 0);
            var nameText = nameGO.GetComponent<TextMeshProUGUI>();
            nameText.text = item.ItemName;
            nameText.font = defaultFont;
            nameText.fontSize = 11;
            nameText.fontStyle = FontStyles.Bold;
            nameText.color = Color.white;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.overflowMode = TextOverflowModes.Ellipsis;

            // Price + count
            var priceGO = new GameObject("Price", typeof(TextMeshProUGUI));
            priceGO.transform.SetParent(cell.transform, false);
            var priceRect = priceGO.GetComponent<RectTransform>();
            priceRect.anchorMin = new Vector2(0, 0.35f);
            priceRect.anchorMax = new Vector2(1, 0.55f);
            priceRect.offsetMin = new Vector2(4, 0);
            priceRect.offsetMax = new Vector2(-4, 0);
            var priceText = priceGO.GetComponent<TextMeshProUGUI>();
            priceText.text = $"{price:F0} coins  x{count}";
            priceText.font = defaultFont;
            priceText.fontSize = 11;
            priceText.color = Color.yellow;
            priceText.alignment = TextAlignmentOptions.Center;

            // Buyback button
            var btnGO = new GameObject("BuybackButton", typeof(Image), typeof(Button));
            btnGO.transform.SetParent(cell.transform, false);
            var btnRect = btnGO.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.1f, 0f);
            btnRect.anchorMax = new Vector2(0.9f, 0.35f);
            btnRect.offsetMin = new Vector2(0, 4);
            btnRect.offsetMax = new Vector2(0, -4);
            btnGO.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.8f);
            var btnTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
            btnTextGO.transform.SetParent(btnGO.transform, false);
            var btnTextRect = btnTextGO.GetComponent<RectTransform>();
            btnTextRect.anchorMin = Vector2.zero;
            btnTextRect.anchorMax = Vector2.one;
            btnTextRect.offsetMin = Vector2.zero;
            btnTextRect.offsetMax = Vector2.zero;
            var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
            btnText.text = "Buyback";
            btnText.font = defaultFont;
            btnText.fontSize = 11;
            btnText.fontStyle = FontStyles.Bold;
            btnText.color = Color.white;
            btnText.alignment = TextAlignmentOptions.Center;

            var capturedItem = item;
            btnGO.GetComponent<Button>().onClick.AddListener(() =>
            {
                if (storeFrontManager.Buyback(capturedItem, 1))
                    RefreshSellPage();
            });
        }
    }

    private void OnItemDroppedInZone(ItemDefinition item)
    {
        if (item == null) return;

        if (_sellBasket.ContainsKey(item))
            _sellBasket[item]++;
        else
            _sellBasket[item] = 1;

        UpdateSellSummary();
    }

    private void UpdateSellSummary()
    {
        if (_sellBasket.Count == 0)
        {
            totalSummaryText.text = "Drag items into the sell zone";
            confirmSellButton.interactable = false;
            return;
        }

        float total = 0f;
        var lines = new System.Text.StringBuilder();

        foreach (var kvp in _sellBasket)
        {
            float lineTotal = kvp.Key.BaseSellValue * kvp.Value;
            total += lineTotal;
            lines.AppendLine($"{kvp.Key.ItemName} x{kvp.Value}  =  {lineTotal:F0} coins");
        }

        lines.AppendLine($"\nTotal: {total:F0} coins");
        totalSummaryText.text = lines.ToString();
        confirmSellButton.interactable = true;
    }

    private void OnConfirmSell()
    {
        foreach (var kvp in _sellBasket)
            storeFrontManager.Sell(kvp.Key, kvp.Value);

        _sellBasket.Clear();
        RefreshSellPage();
    }

    #endregion

    #region Upgrades Page

    private void RefreshUpgradesPage()
    {
        foreach (Transform child in upgradeGridContainer)
            Destroy(child.gameObject);
        

        if (UpgradeManager.Instance == null) return;

        var upgrades = UpgradeManager.Instance.GetAllUpgrades();
        if (upgrades.Count == 0)
        {
            BuildSectionLabel("No upgrades available", upgradeGridContainer);
            return;
        }

        int currentBiomeTier = BiomeManager.Instance != null
            ? BiomeManager.Instance.GetBiomeTier(BiomeManager.Instance.ActiveBiomeData.biomeType)
            : 1;

        bool anyVisible = false;
        foreach (var upgrade in upgrades)
        {
            // Hide upgrades whose biome tier prerequisite hasn't been met yet.
            if (upgrade.RequiredBiomeTier > currentBiomeTier) continue;

            BuildUpgradeCard(upgrade);
            anyVisible = true;
        }

        if (!anyVisible)
            BuildSectionLabel("Upgrade your biome to unlock more", upgradeGridContainer);
    }

    private void BuildUpgradeCard(UpgradeDefinition upgrade)
    {
        bool isApplied  = UpgradeManager.Instance.IsUpgradeApplied(upgrade);
        bool canBuy     = !isApplied && storeFrontManager.CanBuyUpgrade(upgrade);

        // Full-width card — upgrade cards are rows not grid cells since they have
        // more info (name, description, cost breakdown) than item cells.
        var card = new GameObject("UpgradeCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(upgradeGridContainer, false);

        var cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(12, 12, 10, 10);
        cardLayout.spacing = 4;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        var cardLayoutEl = card.AddComponent<LayoutElement>();
        cardLayoutEl.preferredWidth = 9999;
        cardLayoutEl.flexibleWidth = 1;

        card.GetComponent<Image>().color = isApplied
            ? new Color(0.05f, 0.2f, 0.05f, 0.6f)   // green tint when owned
            : new Color(0.05f, 0.05f, 0.15f, 0.6f);  // dark blue when available

        // Header row — icon + name
        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(card.transform, false);
        var headerLayout = headerGO.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandHeight = false;

        if (upgrade.Icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(headerGO.transform, false);
            var iconLayout = iconGO.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = 28;
            iconLayout.preferredHeight = 28;
            iconLayout.minWidth = 28;
            iconLayout.minHeight = 28;
            iconGO.GetComponent<Image>().sprite = upgrade.Icon;
            iconGO.GetComponent<Image>().preserveAspect = true;
        }

        var nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(headerGO.transform, false);
        nameGO.GetComponent<RectTransform>().sizeDelta = new Vector2(150, 100);
        var nameTxt = nameGO.GetComponent<TextMeshProUGUI>();
        nameTxt.text = upgrade.UpgradeName;
        nameTxt.font = defaultFont;
        nameTxt.fontSize = 15;
        nameTxt.fontStyle = FontStyles.Bold;
        nameTxt.color = Color.white;
        nameGO.AddComponent<LayoutElement>().flexibleWidth = 1;

        // Description
        if (!string.IsNullOrEmpty(upgrade.Description))
        {
            var descGO = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI));
            descGO.transform.SetParent(card.transform, false);
            var descTxt = descGO.GetComponent<TextMeshProUGUI>();
            descTxt.text = upgrade.Description;
            descTxt.font = defaultFont;
            descTxt.fontSize = 12;
            descTxt.color = new Color(0.75f, 0.75f, 0.75f);
        }

        // Cost line — coins + materials if any
        if (!isApplied)
        {
            var costGO = new GameObject("Cost", typeof(RectTransform), typeof(TextMeshProUGUI));
            costGO.transform.SetParent(card.transform, false);
            var costTxt = costGO.GetComponent<TextMeshProUGUI>();

            var costStr = new System.Text.StringBuilder();
            costStr.Append($"{upgrade.CoinCost:F0} coins");

            foreach (var ing in upgrade.MaterialCost)
            {
                if (ing.item == null) continue;
                int have = PlayerInventory.Instance?.GetCount(ing.item) ?? 0;
                string hex = have >= ing.quantity ? "#50FF50" : "#FF5050";
                costStr.Append($"  +  <color={hex}>{ing.item.ItemName} x{ing.quantity}</color>");
            }

            costTxt.text = costStr.ToString();
            costTxt.font = defaultFont;
            costTxt.fontSize = 12;
            costTxt.color = Color.yellow;
            costTxt.richText = true;
        }

        // Purchase button
        var btnGO = new GameObject("PurchaseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(card.transform, false);
        var btnLayout = btnGO.AddComponent<LayoutElement>();
        btnLayout.preferredHeight = 32;
        btnLayout.flexibleWidth = 1;
        btnGO.GetComponent<Image>().color = isApplied
            ? new Color(0.1f, 0.5f, 0.1f)
            : canBuy
                ? new Color(0.2f, 0.5f, 0.85f)
                : new Color(0.3f, 0.3f, 0.3f);

        var btnTextGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTextLayout = btnTextGO.AddComponent<LayoutElement>();
        btnTextLayout.flexibleWidth = 1;
        btnTextLayout.flexibleHeight = 1;
        var btnTxt = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnTxt.text = isApplied
            ? "Owned"
            : canBuy ? "Purchase" : "Can't Afford";
        btnTxt.font = defaultFont;
        btnTxt.fontSize = 13;
        btnTxt.fontStyle = FontStyles.Bold;
        btnTxt.color = Color.white;
        btnTxt.alignment = TextAlignmentOptions.Center;

        var btn = btnGO.GetComponent<Button>();
        btn.interactable = canBuy;

        var capturedUpgrade = upgrade;
        btn.onClick.AddListener(() =>
        {
            if (storeFrontManager.BuyUpgrade(capturedUpgrade))
                RefreshUpgradesPage();
        });
    }

    #endregion

    #region Events

    private void UpdateCoinDisplay(float balance)
    {
        coinDisplay.text = $"{balance:F0} coins";
    }

    private void OnStockChanged(ItemDefinition item, int newStock)
    {
        if (_isOpen && _currentPage == StorePage.Buy)
            RefreshBuyPage();
    }

    private void OnRecipeStockChanged(RecipeDefinition recipe, int newStock)
    {
        if (_isOpen && _currentPage == StorePage.Buy)
            RefreshBuyPage();
    }

    private void OnUpgradeApplied(UpgradeDefinition upgrade)
    {
        if (_isOpen && _currentPage == StorePage.Upgrades)
            RefreshUpgradesPage();
    }

    #endregion

    #region Animation

    private IEnumerator SlidePanel(Vector2 target)
    {
        panel.gameObject.SetActive(true);
        Vector2 start = panel.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Ease(elapsed / slideDuration);
            panel.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }

        panel.anchoredPosition = target;
        if (!_isOpen) panel.gameObject.SetActive(false);
        _anim = null;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }

    #endregion

    [ContextMenu("Debug/Toggle Storefront")]
    private void DebugToggle()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[StoreFrontUI] Enter play mode first.");
            return;
        }
        SetOpen(!_isOpen);
    }
}