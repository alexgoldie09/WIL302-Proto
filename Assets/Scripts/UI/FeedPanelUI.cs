using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class FeedPanelUI : MonoBehaviour
{
    public static FeedPanelUI Instance { get; private set; }

    [Header("References")]
    [SerializeField] private TMP_FontAsset defaultFont;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Canvas rootCanvas;

    [Header("Layout")]
    [SerializeField] private float panelWidth = 260f;
    [SerializeField] private float panelHeight = 180f;
    [SerializeField] private Vector2 cellSize = new Vector2(80, 100);
    [SerializeField] private Vector2 cellSpacing = new Vector2(8, 8);
    [SerializeField] private int columnCount = 3;

    private FaunaBase _currentFauna;
    private ItemDefinition _selectedItem;
    private Image _selectedCellHighlight;
    private bool _isOpen;

    private GameObject _gridContainer;
    private Button _confirmButton;
    private Image _confirmButtonImage;
    private TextMeshProUGUI _confirmButtonText;

    private readonly Color _normalCellColor = new Color(0f, 0f, 0f, 0.4f);
    private readonly Color _selectedCellColor = new Color(0.2f, 0.6f, 0.2f, 0.6f);
    private readonly Color _confirmActiveColor = new Color(0.2f, 0.6f, 0.2f, 1f);
    private readonly Color _confirmInactiveColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BuildPanel();
        panel.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_isOpen) return;

        if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
            Close();
    }

    #endregion

    #region Panel Construction

    private void BuildPanel()
    {
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

        panel.sizeDelta = new Vector2(panelWidth, panelHeight);

        // Grid container
        _gridContainer = new GameObject("GridContainer", typeof(RectTransform));
        _gridContainer.transform.SetParent(panel, false);
        var gridRect = _gridContainer.GetComponent<RectTransform>();
        gridRect.anchorMin = new Vector2(0, 0.25f);
        gridRect.anchorMax = Vector2.one;
        gridRect.offsetMin = new Vector2(8, 0);
        gridRect.offsetMax = new Vector2(-8, -8);

        var grid = _gridContainer.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = cellSpacing;
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columnCount;

        var fitter = _gridContainer.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Confirm button
        var btnGO = new GameObject("ConfirmButton", typeof(Image), typeof(Button));
        btnGO.transform.SetParent(panel, false);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0, 0);
        btnRect.anchorMax = new Vector2(1, 0.22f);
        btnRect.offsetMin = new Vector2(8, 8);
        btnRect.offsetMax = new Vector2(-8, -4);

        _confirmButtonImage = btnGO.GetComponent<Image>();
        _confirmButtonImage.color = _confirmInactiveColor;

        var btnTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTextRect = btnTextGO.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = (Vector2.zero);
        btnTextRect.offsetMax = Vector2.zero;
        _confirmButtonText = btnTextGO.GetComponent<TextMeshProUGUI>();
        _confirmButtonText.text = "Select a food";
        _confirmButtonText.font = defaultFont;
        _confirmButtonText.fontSize = 14;
        _confirmButtonText.fontStyle = FontStyles.Bold;
        _confirmButtonText.color = Color.white;
        _confirmButtonText.alignment = TextAlignmentOptions.Center;

        _confirmButton = btnGO.GetComponent<Button>();
        _confirmButton.interactable = false;
        _confirmButton.onClick.AddListener(OnConfirmFeed);
    }

    #endregion

    #region Public API

    public void Open(FaunaBase fauna, Vector3 worldPosition)
    {
        _currentFauna = fauna;
        _selectedItem = null;
        _selectedCellHighlight = null;

        RefreshGrid();
        PositionPanel();
        UpdateConfirmButton();

        panel.gameObject.SetActive(true);
        _isOpen = true;
    }

    public void Close()
    {
        _isOpen = false;
        _currentFauna = null;
        _selectedItem = null;
        panel.gameObject.SetActive(false);
    }

    #endregion

    #region Grid

    private void RefreshGrid()
    {
        foreach (Transform child in _gridContainer.transform)
            Destroy(child.gameObject);

        var feedableItems = PlayerInventory.Instance.GetFeedableItems();

        if (feedableItems.Count == 0)
        {
            var emptyGO = new GameObject("EmptyText", typeof(TextMeshProUGUI));
            emptyGO.transform.SetParent(_gridContainer.transform, false);
            var emptyText = emptyGO.GetComponent<TextMeshProUGUI>();
            emptyText.text = "No food in inventory!";
            emptyText.font = defaultFont;
            emptyText.fontSize = 13;
            emptyText.color = Color.white;
            emptyText.alignment = TextAlignmentOptions.Center;
            return;
        }

        foreach (var item in feedableItems)
        {
            int count = PlayerInventory.Instance.GetCount(item);

            var cell = new GameObject("FeedCell", typeof(RectTransform));
            cell.transform.SetParent(_gridContainer.transform, false);

            // Card background
            var cardGO = new GameObject("CardBackground", typeof(Image));
            cardGO.transform.SetParent(cell.transform, false);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            var cardImage = cardGO.GetComponent<Image>();
            cardImage.color = _normalCellColor;

            // Icon
            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(cell.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.35f);
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(8, 0);
            iconRect.offsetMax = new Vector2(-8, -8);
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = item.Icon;
            iconImage.enabled = item.Icon != null;

            // Gradient overlay
            var gradientGO = new GameObject("Gradient", typeof(Image));
            gradientGO.transform.SetParent(cell.transform, false);
            var gradientRect = gradientGO.GetComponent<RectTransform>();
            gradientRect.anchorMin = new Vector2(0, 0.35f);
            gradientRect.anchorMax = new Vector2(1, 0.55f);
            gradientRect.offsetMin = new Vector2(0, -19.5f);
            gradientRect.offsetMax = new Vector2(0, -19.5f);
            gradientGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            // Item name
            var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
            nameGO.transform.SetParent(cell.transform, false);
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0.18f);
            nameRect.anchorMax = new Vector2(1, 0.35f);
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

            // Count badge
            var countGO = new GameObject("Count", typeof(TextMeshProUGUI));
            countGO.transform.SetParent(cell.transform, false);
            var countRect = countGO.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(1, 1);
            countRect.anchorMax = new Vector2(1, 1);
            countRect.pivot = new Vector2(1, 1);
            countRect.anchoredPosition = new Vector2(-4, -4);
            countRect.sizeDelta = new Vector2(40, 20);
            var countText = countGO.GetComponent<TextMeshProUGUI>();
            countText.text = count > 1 ? $"x{count}" : string.Empty;
            countText.font = defaultFont;
            countText.fontSize = 12;
            countText.fontStyle = FontStyles.Bold;
            countText.color = Color.white;
            countText.alignment = TextAlignmentOptions.TopRight;

            // Selection button
            var btn = cell.AddComponent<Button>();
            var capturedItem = item;
            var capturedCardImage = cardImage;
            btn.onClick.AddListener(() => OnItemSelected(capturedItem, capturedCardImage));
        }
    }

    #endregion

    #region Selection

    private void OnItemSelected(ItemDefinition item, Image cardImage)
    {
        if (_selectedCellHighlight != null)
            _selectedCellHighlight.color = _normalCellColor;

        _selectedItem = item;
        _selectedCellHighlight = cardImage;
        cardImage.color = _selectedCellColor;

        UpdateConfirmButton();
    }

    private void UpdateConfirmButton()
    {
        bool hasSelection = _selectedItem != null;
        _confirmButton.interactable = hasSelection;
        _confirmButtonImage.color = hasSelection ? _confirmActiveColor : _confirmInactiveColor;
        _confirmButtonText.text = hasSelection ? $"Feed {_selectedItem.ItemName}" : "Select a food";
        _confirmButtonText.font = defaultFont;
    }

    private void OnConfirmFeed()
    {
        if (_currentFauna == null || _selectedItem == null) return;

        _currentFauna.Feed(_selectedItem);
        Close();
    }

    #endregion

    #region Positioning

    private void PositionPanel()
    {
        panel.anchorMin = new Vector2(0.5f, 1f);
        panel.anchorMax = new Vector2(0.5f, 1f);
        panel.pivot = new Vector2(0.5f, 1f);
        panel.anchoredPosition = new Vector2(0, -47f);
    }

    #endregion
}