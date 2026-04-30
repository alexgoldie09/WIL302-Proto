using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Slide-up notebook panel. Displays all NotebookEntry records as cards.
/// Follows the same toggle/slide/outside-tap-close pattern as CraftingUI.
/// Cards are rebuilt from scratch each time the panel opens or a new entry arrives.
/// </summary>
public class NotebookUI : MonoBehaviour
{
    public static NotebookUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Button        toggleButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform     contentContainer;
    [SerializeField] private GameObject    biomePanel;

    [Header("Slide")]
    [SerializeField] private float slideDuration = 0.3f;

    [Header("Card Colours")]
    [SerializeField] private Color questEntryColour  = new Color(0.30f, 0.65f, 1.00f);
    [SerializeField] private Color rewardEntryColour = new Color(0.25f, 0.80f, 0.40f);

    [Header("Card Prefab Sizes")]
    [SerializeField] private float borderWidth    = 6f;
    [SerializeField] private float cardPadding    = 12f;
    [SerializeField] private float cardSpacing    = 8f;
    [SerializeField] private int   titleFontSize  = 18;
    [SerializeField] private int   tagFontSize    = 13;
    [SerializeField] private int   bodyFontSize   = 15;

    // ── Private state ─────────────────────────────────────────────────────────
    private bool      _isOpen;
    private Vector2   _shownPos;
    private Vector2   _hiddenPos;
    private Coroutine _anim;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Capture shown position, compute hidden position below screen.
        _shownPos  = panel.anchoredPosition;
        _hiddenPos = new Vector2(_shownPos.x, _shownPos.y - panel.rect.height - 40f);

        panel.anchoredPosition = _hiddenPos;

        toggleButton?.onClick.AddListener(Toggle);
    }

    private void Start()
    {
        if (NotebookManager.Instance != null)
            NotebookManager.Instance.OnEntryAdded += HandleEntryAdded;
    }

    private void OnDestroy()
    {
        if (NotebookManager.Instance != null)
            NotebookManager.Instance.OnEntryAdded -= HandleEntryAdded;
    }

    private void Update()
    {
        if (!_isOpen) return;

        // Close on tap outside — matches CraftingUI pattern.
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(panel, mouse.position.ReadValue()))
                SetOpen(false);
        }

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 touchPos = Touchscreen.current.primaryTouch.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(panel, touchPos))
                SetOpen(false);
        }
    }

    // ── Toggle / Open ─────────────────────────────────────────────────────────
    public void Toggle() => SetOpen(!_isOpen);

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        if (open)
        {
            RefreshEntries();
            if (biomePanel != null) biomePanel.SetActive(false);
        }
        else
        {
            if (biomePanel != null) biomePanel.SetActive(true);
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlideRoutine(open ? _shownPos : _hiddenPos));
    }

    // ── Entry Handling ────────────────────────────────────────────────────────
    private void HandleEntryAdded(NotebookEntry entry)
    {
        if (_isOpen) RefreshEntries();
    }

    // ── Card Building ─────────────────────────────────────────────────────────
    /// <summary>Destroys all cards and rebuilds from the full entry list.</summary>
    private void RefreshEntries()
    {
        foreach (Transform child in contentContainer)
            Destroy(child.gameObject);

        if (NotebookManager.Instance == null) return;

        var entries = NotebookManager.Instance.GetEntries();

        // Show newest first.
        for (int i = entries.Count - 1; i >= 0; i--)
            BuildEntryCard(entries[i]);
    }

    /// <summary>
    /// Builds a full-width card for a single NotebookEntry.
    ///
    /// Layout (all created in code — no prefab required):
    ///
    ///  ┌──────────────────────────────────────┐
    ///  │▌ Title (bold)              [Quest] │
    ///  │  Body text with word wrap           │
    ///  └──────────────────────────────────────┘
    ///
    /// The left coloured border is a child Image sized to (borderWidth, 100%).
    /// </summary>
    private void BuildEntryCard(NotebookEntry entry)
    {
        Color accentColour = entry.isReward ? rewardEntryColour : questEntryColour;
        string tagLabel    = entry.isReward ? "Reward" : "Quest";

        // ── Card root ──────────────────────────────────────────────────────────
        var card = new GameObject("NotebookCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(contentContainer, false);

        var cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.12f, 0.12f, 0.14f, 1f);

        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0f, 1f);
        cardRect.anchorMax = new Vector2(1f, 1f);
        cardRect.pivot     = new Vector2(0.5f, 1f);

        // ContentSizeFitter so card height grows with body text.
        var csf = card.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var cardLayout = card.AddComponent<HorizontalLayoutGroup>();
        cardLayout.childForceExpandHeight = false;
        cardLayout.childForceExpandWidth  = false;
        cardLayout.childControlHeight     = true;
        cardLayout.childControlWidth      = false;
        cardLayout.spacing                = 0f;
        cardLayout.padding                = new RectOffset(0, 0, 0, 0);

        // ── Left border ────────────────────────────────────────────────────────
        var border     = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(card.transform, false);
        border.GetComponent<Image>().color = accentColour;

        var borderLayout = border.AddComponent<LayoutElement>();
        borderLayout.minWidth      = borderWidth;
        borderLayout.preferredWidth = borderWidth;
        borderLayout.flexibleHeight = 1f;

        // ── Content column ─────────────────────────────────────────────────────
        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(card.transform, false);

        var contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.childForceExpandWidth  = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childControlWidth      = true;
        contentLayout.childControlHeight     = true;
        contentLayout.spacing                = 4f;
        contentLayout.padding                = new RectOffset(
            (int)cardPadding, (int)cardPadding,
            (int)cardPadding, (int)cardPadding
        );

        var contentElement = content.AddComponent<LayoutElement>();
        contentElement.flexibleWidth = 1f;

        var contentCsf = content.AddComponent<ContentSizeFitter>();
        contentCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Header row (title + tag) ───────────────────────────────────────────
        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(content.transform, false);

        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.childForceExpandWidth  = false;
        headerLayout.childForceExpandHeight = false;
        headerLayout.childControlWidth      = true;
        headerLayout.childControlHeight     = true;
        headerLayout.spacing                = 6f;

        var headerCsf = header.AddComponent<ContentSizeFitter>();
        headerCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title text.
        var titleGO   = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleGO.transform.SetParent(header.transform, false);
        var titleTMP  = titleGO.GetComponent<TextMeshProUGUI>();
        titleTMP.text      = entry.title;
        titleTMP.fontSize  = titleFontSize;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color     = Color.white;
        titleTMP.overflowMode = TextOverflowModes.Ellipsis;

        var titleElement = titleGO.AddComponent<LayoutElement>();
        titleElement.flexibleWidth = 1f;

        // Tag label.
        var tagGO  = new GameObject("Tag", typeof(RectTransform), typeof(TextMeshProUGUI));
        tagGO.transform.SetParent(header.transform, false);
        var tagTMP = tagGO.GetComponent<TextMeshProUGUI>();
        tagTMP.text      = tagLabel;
        tagTMP.fontSize  = tagFontSize;
        tagTMP.color     = accentColour;
        tagTMP.fontStyle = FontStyles.Bold;
        tagTMP.alignment = TextAlignmentOptions.Right;

        var tagElement = tagGO.AddComponent<LayoutElement>();
        tagElement.flexibleWidth = 0f;

        // ── Body text ──────────────────────────────────────────────────────────
        var bodyGO  = new GameObject("Body", typeof(RectTransform), typeof(TextMeshProUGUI));
        bodyGO.transform.SetParent(content.transform, false);
        var bodyTMP = bodyGO.GetComponent<TextMeshProUGUI>();
        bodyTMP.text         = entry.body;
        bodyTMP.fontSize     = bodyFontSize;
        bodyTMP.color        = new Color(0.85f, 0.85f, 0.85f);
        bodyTMP.textWrappingMode = TextWrappingModes.Normal;
        bodyTMP.overflowMode = TextOverflowModes.Overflow;

        // ── Spacing between cards ──────────────────────────────────────────────
        var spacing = card.AddComponent<LayoutElement>();
        spacing.minHeight = cardSpacing;
    }

    // ── Slide ─────────────────────────────────────────────────────────────────
    private IEnumerator SlideRoutine(Vector2 target)
    {
        Vector2 start   = panel.anchoredPosition;
        float   elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / slideDuration);
            panel.anchoredPosition = Vector2.Lerp(start, target, t);
            yield return null;
        }

        panel.anchoredPosition = target;
        _anim = null;
    }
    
#if UNITY_EDITOR
    [ContextMenu("Debug/Toggle")]
    private void DebugToggle()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[StoreFrontUI] Enter play mode first.");
            return;
        }
        SetOpen(!_isOpen);
    }

#endif
}