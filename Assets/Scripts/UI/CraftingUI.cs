using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class CraftingUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform gridContainer;
    [SerializeField] private GameObject biomePanel;

    [Header("Slide Animation")]
    [SerializeField] private float slideDuration = 0.3f;

    private Vector2 _hiddenPos;
    private Vector2 _shownPos;
    private bool _isOpen = false;
    private Coroutine _anim;

    private RecipeDefinition _selectedRecipe;
    private Image _selectedCardImage;

    private GameObject _bottomBar;
    private Button _craftButton;
    private Image _craftButtonImage;
    private TextMeshProUGUI _craftButtonText;
    private TextMeshProUGUI _craftTimerText;

    private Coroutine _timerDisplay;
    private Coroutine _feedbackCoroutine;

    private readonly Color _normalCardColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
    private readonly Color _selectedCardColor = new Color(0.4f, 0.2f, 0.8f, 0.7f);
    private readonly Color _craftActiveColor = new Color(0.4f, 0.2f, 0.8f, 1f);
    private readonly Color _craftInactiveColor = new Color(0.3f, 0.3f, 0.3f, 1f);
    private readonly Color _craftingColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    private readonly Color _craftedColor = new Color(0.1f, 0.7f, 0.2f, 1f);

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
        // Added safety check for Singleton instances
        StartCoroutine(WaitForInstances());
    }

    private IEnumerator WaitForInstances()
    {
        while (CraftingManager.Instance == null || PlayerInventory.Instance == null)
            yield return null;

        CraftingManager.Instance.OnCraftingStarted += OnCraftingStarted;
        CraftingManager.Instance.OnCraftingCompleted += OnCraftingCompleted;
        PlayerInventory.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDisable()
    {
        if (CraftingManager.Instance != null)
        {
            CraftingManager.Instance.OnCraftingStarted -= OnCraftingStarted;
            CraftingManager.Instance.OnCraftingCompleted -= OnCraftingCompleted;
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

    #region UI Construction

    private void BuildBottomBar()
    {
        _bottomBar = new GameObject("BottomBar", typeof(RectTransform), typeof(Image));
        _bottomBar.transform.SetParent(panel, false);
        var barRect = _bottomBar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0, 0);
        barRect.anchorMax = new Vector2(1, 0);
        barRect.pivot = new Vector2(0.5f, 0);
        barRect.anchoredPosition = new Vector2(0, 34f);
        barRect.sizeDelta = new Vector2(0, 100f);
        _bottomBar.GetComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.98f);

        var timerGO = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
        timerGO.transform.SetParent(_bottomBar.transform, false);
        _craftTimerText = timerGO.GetComponent<TextMeshProUGUI>();
        var timerRect = timerGO.GetComponent<RectTransform>();
        timerRect.anchorMin = new Vector2(0, 0.75f);
        timerRect.anchorMax = new Vector2(1, 1);
        timerRect.sizeDelta = Vector2.zero;
        _craftTimerText.fontSize = 13;
        _craftTimerText.alignment = TextAlignmentOptions.Center;
        _craftTimerText.color = Color.gray;

        var btnGO = new GameObject("CraftButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnGO.transform.SetParent(_bottomBar.transform, false);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.05f, 0.1f);
        btnRect.anchorMax = new Vector2(0.95f, 0.7f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        
        _craftButtonImage = btnGO.GetComponent<Image>();
        _craftButtonImage.color = _craftInactiveColor;

        var txtGO = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
        txtGO.transform.SetParent(btnGO.transform, false);
        _craftButtonText = txtGO.GetComponent<TextMeshProUGUI>();
        var txtRect = txtGO.GetComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.sizeDelta = Vector2.zero;
        _craftButtonText.text = "Select a recipe";
        _craftButtonText.fontSize = 16;
        _craftButtonText.alignment = TextAlignmentOptions.Center;
        _craftButtonText.fontStyle = FontStyles.Bold;

        _craftButton = btnGO.GetComponent<Button>();
        _craftButton.interactable = false;
        _craftButton.onClick.AddListener(OnCraftTapped);
    }

    private void BuildRecipeCard(RecipeDefinition recipe)
    {
        var card = new GameObject("RecipeCard", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(gridContainer, false);
        
        var bgImage = card.GetComponent<Image>();
        bgImage.color = _normalCardColor;

        var layout = card.AddComponent<VerticalLayoutGroup>();
        // UPDATED: 160 Padding Left
        layout.padding = new RectOffset(160, 20, 15, 15);
        layout.spacing = 6;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(card.transform, false);
        var hLayout = header.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 10;
        hLayout.childControlWidth = false;

        if (recipe.Icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGO.transform.SetParent(header.transform, false);
            iconGO.GetComponent<Image>().sprite = recipe.Icon;
            iconGO.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
        }

        var nameTxt = CreateLabel(header, "Name", recipe.RecipeName, 18, Color.white);
        nameTxt.fontStyle = FontStyles.Bold;

        CreateLabel(card, "Output", $"Makes: {recipe.OutputQuantity}x {recipe.OutputItem?.ItemName}", 13, new Color(0.7f, 0.7f, 0.7f));

        var line = new GameObject("Divider", typeof(RectTransform), typeof(Image));
        line.transform.SetParent(card.transform, false);
        line.GetComponent<Image>().color = new Color(1, 1, 1, 0.1f);
        line.AddComponent<LayoutElement>().preferredHeight = 1;

        foreach (var ing in recipe.Ingredients)
        {
            int count = PlayerInventory.Instance.GetCount(ing.item);
            bool hasEnough = count >= ing.quantity;
            string hex = hasEnough ? "#50FF50" : "#FF5050";
            CreateLabel(card, "Ingredient", $"{ing.item?.ItemName}: <color={hex}>{count}/{ing.quantity}</color>", 14, Color.white);
        }

        card.GetComponent<Button>().onClick.AddListener(() => OnRecipeSelected(recipe, bgImage));
    }

    private TextMeshProUGUI CreateLabel(GameObject parent, string n, string content, int size, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent.transform, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = size;
        t.color = c;
        t.richText = true;
        return t;
    }

    #endregion

    #region Selection & Logic

    private void RefreshGrid()
    {
        foreach (Transform child in gridContainer) Destroy(child.gameObject);
        _selectedRecipe = null;
        _selectedCardImage = null;

        if (CraftingManager.Instance == null) return;

        var recipes = CraftingManager.Instance.GetUnlockedRecipes();
        if (recipes == null || recipes.Count == 0)
        {
            CreateLabel(gridContainer.gameObject, "Empty", "No recipes unlocked!", 16, Color.white).alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var r in recipes) BuildRecipeCard(r);
    }

    private void OnRecipeSelected(RecipeDefinition recipe, Image cardImage)
    {
        if (CraftingManager.Instance.IsCrafting || _feedbackCoroutine != null) return;

        if (_selectedCardImage != null) _selectedCardImage.color = _normalCardColor;
        _selectedRecipe = recipe;
        _selectedCardImage = cardImage;
        cardImage.color = _selectedCardColor;
        UpdateCraftButton();
    }

    private void UpdateCraftButton()
    {
        if (_feedbackCoroutine != null) return;

        bool isCrafting = CraftingManager.Instance != null && CraftingManager.Instance.IsCrafting;
        if (isCrafting) { SetCraftingUIState(); return; }

        bool hasSelection = _selectedRecipe != null;
        bool canCraft = hasSelection && CraftingManager.Instance != null && CraftingManager.Instance.CanCraft(_selectedRecipe);

        _craftButton.interactable = canCraft;
        _craftButtonImage.color = canCraft ? _craftActiveColor : _craftInactiveColor;
        _craftButtonText.text = hasSelection 
            ? (canCraft ? $"Craft {_selectedRecipe.RecipeName}" : "Need Ingredients") 
            : "Select a recipe";
        _craftTimerText.text = "";
    }

    private void OnCraftTapped()
    {
        if (_selectedRecipe == null || CraftingManager.Instance == null || _feedbackCoroutine != null) return;

        var recipe = _selectedRecipe; // cache before StartCrafting clears it via RefreshGrid

        if (CraftingManager.Instance.StartCrafting(recipe))
        {
            SetCraftingUIState();
            if (_timerDisplay != null) StopCoroutine(_timerDisplay);
            _timerDisplay = StartCoroutine(TimerDisplay(recipe.CraftingDuration));
        }
    }

    private void SetCraftingUIState()
    {
        _craftButton.interactable = false;
        _craftButtonImage.color = _craftingColor;
        _craftButtonText.text = "Crafting...";
    }

    private IEnumerator TimerDisplay(float duration)
    {
        float elapsed = 0;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _craftTimerText.text = $"{Mathf.Max(0, duration - elapsed):F1}s remaining";
            yield return null;
        }
    }

    private void OnCraftingCompleted(RecipeDefinition recipe)
    {
        if (_timerDisplay != null) StopCoroutine(_timerDisplay);
        if (_isOpen) RefreshGrid();
        if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(CraftedFeedbackRoutine());
    }

    private IEnumerator CraftedFeedbackRoutine()
    {
        _craftButtonImage.color = _craftedColor;
        _craftButtonText.text = "Crafted!";
        _craftTimerText.text = "";

        yield return new WaitForSeconds(2f);

        _feedbackCoroutine = null;
        _selectedRecipe = null;
        _selectedCardImage = null;
        UpdateCraftButton();
    }

    #endregion

    #region Animation & Toggle

    public void Toggle() => SetOpen(!_isOpen);

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;
        if (biomePanel) biomePanel.SetActive(!_isOpen);
        if (_isOpen) { gridContainer.gameObject.SetActive(true); RefreshGrid(); UpdateCraftButton(); }
        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }

    private void OnCraftingStarted(RecipeDefinition r, float d) => SetCraftingUIState();
    private void OnInventoryChanged(ItemDefinition item, int count) { if (_isOpen) RefreshGrid(); }

    private IEnumerator SlidePanel(Vector2 target)
    {
        Vector2 start = panel.anchoredPosition;
        float elapsed = 0;
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