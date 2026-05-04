using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Slides up when a Slot is tapped while empty.
/// Displays the player's inventory filtered to items valid for the tapped slot.
/// On selection, calls Slot.Place() which handles the inventory deduction and spawn.
/// </summary>
public class SlotPlacementUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TMP_FontAsset defaultFont;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform gridContainer;
    [SerializeField] private GameObject biomePanel;
    [SerializeField] private GameObject feedPanel;
    [SerializeField] private GameObject craftingPanel;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private Collider2D shopFrontCollider;
    
    [Header("Slide Animation")]
    [SerializeField] private float slideDuration = 0.3f;
    
    private Vector2 _hiddenPos;
    private Vector2 _shownPos;
    private bool _isOpen = false;
    private Coroutine _anim;
    private Slot _targetSlot;

    #region Unity Lifecycle
    private void Awake()
    {
        _shownPos = panel.anchoredPosition;
        _hiddenPos = _shownPos - new Vector2(0, panel.rect.height + 50f);
        panel.anchoredPosition = _hiddenPos;
        gridContainer.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        PlayerInventory.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDisable()
    {
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

    #region Public API
    /// <summary>
    /// Opens the placement UI filtered to items valid for the given slot.
    /// Called by Slot.OnTapped() when the slot is empty.
    /// </summary>
    public void Open(Slot slot)
    {
        _targetSlot = slot;
        SetOpen(true);
    }

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        if (_isOpen)
        {
            panel.gameObject.SetActive(true);
            gridContainer.gameObject.SetActive(true);
            RefreshGrid();
            if (biomePanel != null) biomePanel.SetActive(false);
            if (shopFrontCollider) shopFrontCollider.enabled = false;
            if (inventoryPanel) inventoryPanel.GetComponent<InventoryUI>().ToggleButton.SetActive(false);
            if (craftingPanel) craftingPanel.GetComponent<CraftingUI>().ToggleButton.SetActive(false);
            if (feedPanel) feedPanel.SetActive(false);
        }
        else
        {
            if (biomePanel != null) biomePanel.SetActive(true);
            if (shopFrontCollider) shopFrontCollider.enabled = true;
            if (inventoryPanel) inventoryPanel.GetComponent<InventoryUI>().ToggleButton.SetActive(true);
            if (craftingPanel) craftingPanel.GetComponent<CraftingUI>().ToggleButton.SetActive(true);
            _targetSlot = null;
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }
    #endregion

    #region Grid
    private void RefreshGrid()
    {
        foreach (Transform child in gridContainer)
            Destroy(child.gameObject);

        if (_targetSlot == null) return;

        // Filter the full inventory to items the slot will accept.
        var validItems = new List<KeyValuePair<ItemDefinition, int>>();
        foreach (var kvp in PlayerInventory.Instance.GetAllItems())
        {
            if (kvp.Value > 0 && _targetSlot.CanPlace(kvp.Key))
                validItems.Add(kvp);
        }

        if (validItems.Count == 0)
        {
            // Empty state — single message card.
            var emptyGO = new GameObject("EmptyMessage", typeof(RectTransform));
            emptyGO.transform.SetParent(gridContainer, false);
            var emptyRect = emptyGO.GetComponent<RectTransform>();
            emptyRect.anchorMin = Vector2.zero;
            emptyRect.anchorMax = Vector2.one;
            emptyRect.offsetMin = Vector2.zero;
            emptyRect.offsetMax = Vector2.zero;

            var msgGO = new GameObject("Message", typeof(TextMeshProUGUI));
            msgGO.transform.SetParent(emptyGO.transform, false);
            var msgRect = msgGO.GetComponent<RectTransform>();
            msgRect.anchorMin = Vector2.zero;
            msgRect.anchorMax = Vector2.one;
            msgRect.offsetMin = Vector2.zero;
            msgRect.offsetMax = Vector2.zero;
            var msgText = msgGO.GetComponent<TextMeshProUGUI>();
            msgText.text = "No valid items in inventory.";
            msgText.font = defaultFont;
            msgText.fontSize = 24;
            msgText.color = new Color(1f, 0f, 0f);
            msgText.alignment = TextAlignmentOptions.Center;

            Debug.Log($"[SlotPlacementUI] No valid items for slot '{_targetSlot.name}'.");
            return;
        }

        foreach (var kvp in validItems)
            BuildItemCell(kvp.Key, kvp.Value);
    }

    private void BuildItemCell(ItemDefinition item, int count)
    {
        // Cell root
        var cell = new GameObject("Cell", typeof(RectTransform));
        cell.transform.SetParent(gridContainer, false);

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
        iconRect.anchorMax = new Vector2(1, 0.85f);
        iconRect.offsetMin = new Vector2(8, 0);
        iconRect.offsetMax = new Vector2(-8, 0);
        var iconImage = iconGO.GetComponent<Image>();
        iconImage.sprite = item.Icon;
        iconImage.enabled = item.Icon != null;

        // Item name
        var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(cell.transform, false);
        var nameRect = nameGO.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0, 0.55f);
        nameRect.anchorMax = new Vector2(1, 0.75f);
        nameRect.offsetMin = new Vector2(4, 0);
        nameRect.offsetMax = new Vector2(-4, 0);
        var nameText = nameGO.GetComponent<TextMeshProUGUI>();
        nameText.text = item.ItemName;
        nameText.font = defaultFont;
        nameText.fontSize = 12;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.white;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.overflowMode = TextOverflowModes.Ellipsis;

        // Count badge (top-right)
        var countGO = new GameObject("Count", typeof(TextMeshProUGUI));
        countGO.transform.SetParent(cell.transform, false);
        var countRect = countGO.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(1, 1);
        countRect.anchorMax = new Vector2(1, 1);
        countRect.pivot = new Vector2(1, 1);
        countRect.anchoredPosition = new Vector2(-6, -6);
        countRect.sizeDelta = new Vector2(50, 26);
        var countText = countGO.GetComponent<TextMeshProUGUI>();
        countText.text = $"x{count}";
        countText.font = defaultFont;
        countText.fontSize = 16;
        countText.fontStyle = FontStyles.Bold;
        countText.color = Color.white;
        countText.alignment = TextAlignmentOptions.TopRight;

        // Place button
        var btnGO = new GameObject("PlaceButton", typeof(Image), typeof(Button));
        btnGO.transform.SetParent(cell.transform, false);
        var btnRect = btnGO.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.1f, 0f);
        btnRect.anchorMax = new Vector2(0.9f, 0.3f);
        btnRect.offsetMin = new Vector2(0, 4);
        btnRect.offsetMax = new Vector2(0, -4);
        btnGO.GetComponent<Image>().color = new Color(0.2f, 0.65f, 0.3f);

        var btnTextGO = new GameObject("Text", typeof(TextMeshProUGUI));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnTextRect = btnTextGO.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
        var btnText = btnTextGO.GetComponent<TextMeshProUGUI>();
        btnText.text = "Place";
        btnText.font = defaultFont;
        btnText.fontSize = 12;
        btnText.fontStyle = FontStyles.Bold;
        btnText.color = Color.white;
        btnText.alignment = TextAlignmentOptions.Center;

        var capturedItem = item;
        btnGO.GetComponent<Button>().onClick.AddListener(() => OnPlacePressed(capturedItem));
    }
    #endregion
    
    #region Interaction Methods
    private void OnPlacePressed(ItemDefinition item)
    {
        if (_targetSlot == null) return;

        _targetSlot.Place(item);
        SetOpen(false);
    }

    private void OnInventoryChanged(ItemDefinition item, int newCount)
    {
        // Refresh so count badges stay current and cells hide if stock hits 0.
        if (_isOpen) RefreshGrid();
    }
    #endregion
    
    #region Animation Methods
    private IEnumerator SlidePanel(Vector2 target)
    {
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
        if (!_isOpen)
        {
            gridContainer.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
        }
        _anim = null;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
    #endregion
    
    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Toggle Panel")]
    private void DebugToggle() => SetOpen(!_isOpen);
#endif
    #endregion
}