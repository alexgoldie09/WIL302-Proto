using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class StoreFrontUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StoreFrontManager storeFrontManager;
    [SerializeField] private RectTransform panel;
    [SerializeField] private GameObject biomePanel;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI coinDisplay;
    [SerializeField] private Button buyTab;
    [SerializeField] private Button sellTab;

    [Header("Pages")]
    [SerializeField] private GameObject buyPage;
    [SerializeField] private GameObject sellPage;

    [Header("Buy Page")]
    [SerializeField] private Transform buyGridContainer;

    [Header("Sell Page")]
    [SerializeField] private Transform sellInventoryGrid;
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

    private void Awake()
    {
        _shownPos = panel.anchoredPosition;
        _hiddenPos = _shownPos - new Vector2(0, panel.rect.height + 50f);
        panel.anchoredPosition = _hiddenPos;

        buyTab.onClick.AddListener(() => SetPage(true));
        sellTab.onClick.AddListener(() => SetPage(false));
        dropZone.OnItemDropped += OnItemDroppedInZone;
        confirmSellButton.onClick.AddListener(OnConfirmSell);

        SetPage(true);
    }

    private void OnEnable()
    {
        storeFrontManager.OnStorefrontToggled += SetOpen;
        storeFrontManager.OnBalanceChanged += UpdateCoinDisplay;
        storeFrontManager.OnStockChanged += OnStockChanged;
        storeFrontManager.OnRecipeStockChanged += OnRecipeStockChanged;
    }

    private void OnDisable()
    {
        storeFrontManager.OnStorefrontToggled -= SetOpen;
        storeFrontManager.OnBalanceChanged -= UpdateCoinDisplay;
        storeFrontManager.OnStockChanged -= OnStockChanged;
        storeFrontManager.OnRecipeStockChanged -= OnRecipeStockChanged;
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

        if (_isOpen)
        {
            panel.gameObject.SetActive(true);
            UpdateCoinDisplay(storeFrontManager.CoinBalance);
            RefreshCurrentPage();
        }
        else
        {
            _sellBasket.Clear();
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }

    private void SetPage(bool isBuyPage)
    {
        buyPage.SetActive(isBuyPage);
        sellPage.SetActive(!isBuyPage);

        buyTab.image.color = isBuyPage ? Color.white : new Color(0.6f, 0.6f, 0.6f);
        sellTab.image.color = isBuyPage ? new Color(0.6f, 0.6f, 0.6f) : Color.white;

        RefreshCurrentPage();
    }

    private void RefreshCurrentPage()
    {
        if (!_isOpen) return;

        if (buyPage.activeSelf)
            RefreshBuyPage();
        else
            RefreshSellPage();
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
        iconRect.anchorMin = new Vector2(0, 0.35f);
        iconRect.anchorMax = new Vector2(1, 1f);
        iconRect.offsetMin = new Vector2(8, 0);
        iconRect.offsetMax = new Vector2(-8, -8);
        var iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = item.Icon;
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
        priceText.fontSize = 11;
        priceText.color = Color.yellow;
        priceText.alignment = TextAlignmentOptions.Center;

        // Item name
        var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(cell.transform, false);
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.6f);
        nameRect.anchorMax = new Vector2(1, 0.8f);
        nameRect.offsetMin = new Vector2(4, 0);
        nameRect.offsetMax = new Vector2(-4, 0);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = item.ItemName;
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
        btnText.fontSize = 12;
        btnText.fontStyle = FontStyles.Bold;
        btnText.color = Color.white;
        btnText.alignment = TextAlignmentOptions.Center;

        var capturedItem = item;
        var btn = btnGO.GetComponent<Button>();
        btn.interactable = stock > 0;
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
        bool canBuy = !alreadyOwned && stock > 0;

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
        iconRect.anchorMin = new Vector2(0, 0.35f);
        iconRect.anchorMax = new Vector2(1, 1f);
        iconRect.offsetMin = new Vector2(8, 0);
        iconRect.offsetMax = new Vector2(-8, -8);
        var iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = recipe.Icon;
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
        priceText.fontSize = 11;
        priceText.color = alreadyOwned ? Color.green : Color.yellow;
        priceText.alignment = TextAlignmentOptions.Center;

        // Recipe name
        var nameGO = new GameObject("RecipeName", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(cell.transform, false);
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.6f);
        nameRect.anchorMax = new Vector2(1, 0.8f);
        nameRect.offsetMin = new Vector2(4, 0);
        nameRect.offsetMax = new Vector2(-4, 0);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = recipe.RecipeName;
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
        ingRect.offsetMin = new Vector2(4, 0);
        ingRect.offsetMax = new Vector2(-4, 0);
        var ingText = ingGO.GetComponent<TextMeshProUGUI>();
        var ingLines = new System.Text.StringBuilder();
        foreach (var ing in recipe.Ingredients)
            ingLines.AppendLine($"{ing.item?.ItemName} x{ing.quantity}");
        ingText.text = ingLines.ToString().TrimEnd();
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
        foreach (Transform child in sellInventoryGrid)
            Destroy(child.gameObject);

        foreach (var kvp in PlayerInventory.Instance.GetAllItems())
        {
            if (!kvp.Key.IsAvailableForStorefront) continue;

            var cell = new GameObject("SellCell", typeof(RectTransform));
            cell.transform.SetParent(sellInventoryGrid, false);

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
            iconImage.enabled = kvp.Key.Icon != null;

            // Gradient overlay
            var gradientGO = new GameObject("GradientOverlay", typeof(Image));
            gradientGO.transform.SetParent(cell.transform, false);
            var gradientRect = gradientGO.GetComponent<RectTransform>();
            gradientRect.anchorMin = new Vector2(0, 0.3f);
            gradientRect.anchorMax = new Vector2(1, 0.55f);
            gradientRect.offsetMin = Vector2.zero;
            gradientRect.offsetMax = Vector2.zero;
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

    #region Events

    private void UpdateCoinDisplay(float balance)
    {
        coinDisplay.text = $"{balance:F0} coins";
    }

    private void OnStockChanged(ItemDefinition item, int newStock)
    {
        if (_isOpen && buyPage.activeSelf)
            RefreshBuyPage();
    }

    private void OnRecipeStockChanged(RecipeDefinition recipe, int newStock)
    {
        if (_isOpen && buyPage.activeSelf)
            RefreshBuyPage();
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