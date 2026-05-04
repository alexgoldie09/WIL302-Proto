using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// UI for the crafting panel.
/// Displays unlocked recipe cards and a bottom bar with one timer row per
/// available crafting slot. Slot count is driven by CraftingManager.MaxConcurrentCrafts
/// so the bar expands automatically if slots are upgraded without UI changes.
/// </summary>
public class CraftingUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_FontAsset defaultFont;
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform gridContainer;
    [SerializeField] private GameObject biomePanel;
    [SerializeField] private GameObject feedPanel;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Collider2D shopFrontCollider;

    [Header("Slide Animation")]
    [SerializeField] private float slideDuration = 0.3f;
    
    private Vector2 _hiddenPos;
    private Vector2 _shownPos;
    private bool _isOpen = false;
    private Coroutine _anim;

    private RecipeDefinition _selectedRecipe;
    private Image _selectedCardImage;
    private Coroutine _feedbackCoroutine;

    // Bottom bar
    private GameObject _bottomBar;
    private Button _craftButton;
    private Image _craftButtonImage;
    private TextMeshProUGUI _craftButtonText;

    /// <summary>One timer label per crafting slot, built dynamically on open.</summary>
    private readonly List<TextMeshProUGUI> _slotTimerTexts = new();

    // Colors
    private readonly Color _normalCardColor  = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    private readonly Color _selectedCardColor = new Color(0.4f, 0.2f, 0.8f, 0.7f);
    private readonly Color _craftActiveColor  = new Color(0.4f, 0.2f, 0.8f, 1f);
    private readonly Color _craftInactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    private readonly Color _craftingColor     = new Color(0.2f, 0.2f, 0.2f, 1f);
    private readonly Color _craftedColor      = new Color(0.1f, 0.7f, 0.2f, 1f);

    public GameObject ToggleButton => toggleButton.gameObject;
    #region Unity Lifecycle
    private void Awake()
    {
        _shownPos = panel.anchoredPosition;
        _hiddenPos = _shownPos - new Vector2(0, panel.rect.height + 150f);
        panel.anchoredPosition = _hiddenPos;

        BuildBottomBar();
        gridContainer.gameObject.SetActive(false);
        toggleButton.onClick.AddListener(Toggle);
    }

    private void OnEnable()
    {
        StartCoroutine(WaitForInstances());
    }

    private IEnumerator WaitForInstances()
    {
        while (CraftingManager.Instance == null || PlayerInventory.Instance == null)
            yield return null;

        CraftingManager.Instance.OnCraftingStarted   += OnCraftingStarted;
        CraftingManager.Instance.OnCraftingCompleted += OnCraftingCompleted;
        CraftingManager.Instance.OnCraftingTick      += OnCraftingTick;
        PlayerInventory.Instance.OnInventoryChanged  += OnInventoryChanged;
    }

    private void OnDisable()
    {
        if (CraftingManager.Instance != null)
        {
            CraftingManager.Instance.OnCraftingStarted   -= OnCraftingStarted;
            CraftingManager.Instance.OnCraftingCompleted -= OnCraftingCompleted;
            CraftingManager.Instance.OnCraftingTick      -= OnCraftingTick;
        }
        if (PlayerInventory.Instance != null)
            PlayerInventory.Instance.OnInventoryChanged -= OnInventoryChanged;
    }

    private void Update()
    {
        if (!_isOpen) return;
        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            SetOpen(false);
    }
    #endregion

    #region Bottom Bar UI
    /// <summary>
    /// Builds the bottom bar shell. Slot timer rows are added in RebuildSlotTimers
    /// once CraftingManager is available so the count matches MaxConcurrentCrafts.
    /// </summary>
    private void BuildBottomBar()
    {
        _bottomBar = new GameObject("BottomBar", typeof(RectTransform), typeof(Image));
        _bottomBar.transform.SetParent(panel, false);

        var barRect = _bottomBar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0, 0);
        barRect.anchorMax = new Vector2(1, 0);
        barRect.pivot     = new Vector2(0.5f, 0);
        barRect.anchoredPosition = new Vector2(0, 34f);
        // Height set in RebuildSlotTimers once we know slot count.
        barRect.sizeDelta = new Vector2(0, 100f);

        _bottomBar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.98f);

        var barLayout = _bottomBar.AddComponent<VerticalLayoutGroup>();
        barLayout.padding            = new RectOffset(10, 10, 8, 8);
        barLayout.spacing            = 4;
        barLayout.childControlWidth  = true;
        barLayout.childControlHeight = true;
        barLayout.childForceExpandWidth  = true;
        barLayout.childForceExpandHeight = false;

        _bottomBar.AddComponent<ContentSizeFitter>().verticalFit =
            ContentSizeFitter.FitMode.PreferredSize;

        // Craft button — always present, sits below the slot rows.
        var btnGO = new GameObject("CraftButton",
            typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_bottomBar.transform, false);

        var btnLayout = btnGO.AddComponent<LayoutElement>();
        btnLayout.preferredHeight = 44;
        btnLayout.flexibleWidth   = 1;

        _craftButtonImage       = btnGO.GetComponent<Image>();
        _craftButtonImage.color = _craftInactiveColor;

        var txtGO  = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(btnGO.transform, false);
        var txtRect = txtGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        _craftButtonText = txtGO.GetComponent<TextMeshProUGUI>();
        _craftButtonText.text = "Select a recipe";
        _craftButtonText.font = defaultFont;
        _craftButtonText.fontSize  = 16;
        _craftButtonText.alignment = TextAlignmentOptions.Center;
        _craftButtonText.fontStyle = FontStyles.Bold;

        _craftButton = btnGO.GetComponent<Button>();
        _craftButton.interactable = false;
        _craftButton.onClick.AddListener(OnCraftTapped);
    }

    /// <summary>
    /// Builds or rebuilds the slot timer rows in the bottom bar to match
    /// CraftingManager.MaxConcurrentCrafts. Called on panel open so it always
    /// reflects the current slot count including any upgrades purchased mid-session.
    /// </summary>
    private void RebuildSlotTimers()
    {
        if (_bottomBar == null || _craftButton == null)
            BuildBottomBar();

        foreach (var t in _slotTimerTexts)
            if (t != null) DestroyImmediate(t.gameObject);
        _slotTimerTexts.Clear();

        if (CraftingManager.Instance == null) return;

        int slotCount = CraftingManager.Instance.MaxConcurrentCrafts;

        for (int i = 0; i < slotCount; i++)
        {
            var rowGO = new GameObject($"SlotRow_{i + 1}",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            rowGO.transform.SetParent(_bottomBar.transform, false);
            // No sibling index — just append, button gets moved to last below

            var layout = rowGO.AddComponent<LayoutElement>();
            layout.preferredHeight = 20;
            layout.flexibleWidth   = 1;

            var txt = rowGO.GetComponent<TextMeshProUGUI>();
            txt.fontSize  = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.gray;
            txt.text = $"Slot {i + 1}: Empty";
            txt.font = defaultFont;

            _slotTimerTexts.Add(txt);
        }

        // Always ensure the craft button sits below all slot rows.
        _craftButton.transform.SetAsLastSibling();
    }
    #endregion

    #region Recipe Card UI
    private void BuildRecipeCard(RecipeDefinition recipe)
    {
        var card = new GameObject("RecipeCard",
            typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(gridContainer, false);

        var bgImage = card.GetComponent<Image>();
        bgImage.color = _normalCardColor;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding            = new RectOffset(20, 20, 15, 15);
        layout.spacing            = 6;
        layout.childControlHeight = true;
        layout.childControlWidth  = true;
        layout.childForceExpandHeight = false;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(card.transform, false);
        var hLayout = header.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 10;
        hLayout.childForceExpandHeight = false;

        if (recipe.Icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(header.transform, false);
            var iconEl = iconGO.AddComponent<LayoutElement>();
            iconEl.preferredWidth  = 64;
            iconEl.preferredHeight = 64;
            var iconImg = iconGO.GetComponent<Image>();
            iconImg.sprite         = recipe.Icon;
            iconImg.preserveAspect = true;
        }

        var nameTxt = CreateLabel(header, "Name", recipe.RecipeName, 18, Color.white);
        nameTxt.fontStyle = FontStyles.Bold;

        CreateLabel(card, "Output",
            $"Makes: {recipe.OutputQuantity}x {recipe.OutputItem?.ItemName}",
            13, new Color(0.7f, 0.7f, 0.7f));

        var line = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(card.transform, false);
        line.GetComponent<Image>().color = new Color(1, 1, 1, 0.1f);
        line.AddComponent<LayoutElement>().preferredHeight = 1;

        foreach (var ing in recipe.Ingredients)
        {
            int  count    = PlayerInventory.Instance.GetCount(ing.item);
            bool hasEnough = count >= ing.quantity;
            string hex    = hasEnough ? "#50FF50" : "#FF5050";
            CreateLabel(card, "Ingredient",
                $"{ing.item?.ItemName}: <color={hex}>{count}/{ing.quantity}</color>",
                14, Color.white);
        }

        card.GetComponent<Button>().onClick.AddListener(
            () => OnRecipeSelected(recipe, bgImage));
    }

    private TextMeshProUGUI CreateLabel(GameObject parent, string n,
        string content, int size, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent.transform, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.font = defaultFont;
        t.color = c;
        t.richText = true;
        return t;
    }
    #endregion

    #region Selection & Crafting Methods
    private void RefreshGrid()
    {
        foreach (Transform child in gridContainer) Destroy(child.gameObject);
        _selectedRecipe   = null;
        _selectedCardImage = null;

        if (CraftingManager.Instance == null) return;

        var recipes = CraftingManager.Instance.GetUnlockedRecipes();
        if (recipes == null || recipes.Count == 0)
        {
            CreateLabel(gridContainer.gameObject, "Empty",
                "No recipes unlocked!", 16, Color.white)
                .alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var r in recipes) BuildRecipeCard(r);
    }

    private void OnRecipeSelected(RecipeDefinition recipe, Image cardImage)
    {
        if (_feedbackCoroutine != null) return;

        // Deselect previous.
        if (_selectedCardImage != null) _selectedCardImage.color = _normalCardColor;

        _selectedRecipe    = recipe;
        _selectedCardImage = cardImage;
        cardImage.color    = _selectedCardColor;
        AudioManager.Instance?.PlaySFX("menu_click", 0.4f);

        UpdateCraftButton();
    }

    private void UpdateCraftButton()
    {
        if (_feedbackCoroutine != null) return;
        if (CraftingManager.Instance == null) return;

        bool allSlotsFull = CraftingManager.Instance.IsCrafting;
        bool hasSelection = _selectedRecipe != null;
        bool canCraft     = hasSelection &&
                            CraftingManager.Instance.CanStartCrafting(_selectedRecipe);

        _craftButton.interactable  = canCraft;
        _craftButtonImage.color    = allSlotsFull ? _craftingColor
                                    : canCraft    ? _craftActiveColor
                                    : _craftInactiveColor;

        _craftButtonText.text = !hasSelection        ? "Select a recipe"
                              : allSlotsFull         ? "All slots busy"
                              : canCraft             ? $"Craft {_selectedRecipe.RecipeName}"
                              : /* missing items */    "Need Ingredients";
        _craftButtonText.font = defaultFont;
    }

    private void OnCraftTapped()
    {
        if (_selectedRecipe == null ||
            CraftingManager.Instance == null ||
            _feedbackCoroutine != null) return;

        var recipe = _selectedRecipe; // cache before RefreshGrid clears it

        if (CraftingManager.Instance.StartCrafting(recipe))
        {
            AudioManager.Instance?.PlaySFX("menu_click", 0.4f);
            UpdateCraftButton();
            RefreshGrid(); // refresh ingredient counts
        }
    }
    #endregion

    #region Slot Timer Updates
    /// <summary>
    /// Refreshes all slot timer rows to match the current active crafts.
    /// Called on craft start, completion, and each tick.
    /// </summary>
    private void RefreshSlotTimers()
    {
        if (CraftingManager.Instance == null) return;

        var active = CraftingManager.Instance.ActiveCrafts;

        for (int i = 0; i < _slotTimerTexts.Count; i++)
        {
            if (_slotTimerTexts[i] == null) continue;

            if (i < active.Count)
                _slotTimerTexts[i].text =
                    $"Slot {i + 1}: {active[i].recipe.RecipeName} — " +
                    $"{active[i].remainingTime:F1}s";
            else
                _slotTimerTexts[i].text = $"Slot {i + 1}: Empty";
            
            _slotTimerTexts[i].font = defaultFont;
        }
    }
    #endregion

    #region Event Handlers
    private void OnCraftingStarted(RecipeDefinition recipe, float duration)
    {
        RefreshSlotTimers();
        UpdateCraftButton();
    }

    private void OnCraftingCompleted(RecipeDefinition recipe)
    {
        RefreshSlotTimers();
        UpdateCraftButton();

        if (_isOpen) RefreshGrid();

        if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(CraftedFeedbackRoutine(recipe));
    }

    private void OnCraftingTick(RecipeDefinition recipe, float remaining)
    {
        // Only refresh timers — avoid full grid rebuild every frame.
        RefreshSlotTimers();
    }

    private void OnInventoryChanged(ItemDefinition item, int count)
    {
        if (_isOpen) RefreshGrid();
    }

    private IEnumerator CraftedFeedbackRoutine(RecipeDefinition recipe)
    {
        _craftButtonImage.color = _craftedColor;
        _craftButtonText.text = $"{recipe.RecipeName} done!";
        _craftButtonText.font = defaultFont;

        yield return new WaitForSeconds(2f);

        _feedbackCoroutine = null;
        UpdateCraftButton();
    }
    #endregion

    #region Animation & Toggle
    public void Toggle() => SetOpen(!_isOpen);

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;
        
        AudioManager.Instance?.PlaySFX("menu_click", 0.4f);
        
        toggleButton.gameObject.SetActive(!_isOpen);
        
        if (_isOpen)
        {
            if (inventoryPanel) inventoryPanel.GetComponent<InventoryUI>().SetOpen(false);
            if (shopFrontCollider) shopFrontCollider.enabled = false;
            if (biomePanel) biomePanel.SetActive(false);
            if (feedPanel) feedPanel.SetActive(false);
            gridContainer.gameObject.SetActive(true);
            RebuildSlotTimers(); // rebuild to reflect any slot upgrades
            RefreshGrid();
            RefreshSlotTimers();
            UpdateCraftButton();
        }
        else
        {
            if (biomePanel) biomePanel.SetActive(true);
            if (shopFrontCollider) shopFrontCollider.enabled = true;
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }

    private IEnumerator SlidePanel(Vector2 target)
    {
        Vector2 start   = panel.anchoredPosition;
        float   elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            panel.anchoredPosition = Vector2.Lerp(start, target, elapsed / slideDuration);
            yield return null;
        }

        panel.anchoredPosition = target;
        if (!_isOpen) gridContainer.gameObject.SetActive(false);
    }
    #endregion
}