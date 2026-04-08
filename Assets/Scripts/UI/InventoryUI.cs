using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventoryUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform gridContainer;
    [SerializeField] private GameObject biomePanel;

    [Header("Slide Animation")]
    [SerializeField] private float slideDuration = 0.3f;

    public GameObject ToggleButton => toggleButton.gameObject;
    
    private Vector2 _hiddenPos;
    private Vector2 _shownPos;
    private bool _isOpen = false;
    private Coroutine _anim;

    private void Awake()
    {
        _shownPos = panel.anchoredPosition;
        _hiddenPos = _shownPos - new Vector2(0, panel.rect.height + 50f);
        panel.anchoredPosition = _hiddenPos;
        gridContainer.gameObject.SetActive(false);
        toggleButton.onClick.AddListener(Toggle);
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

    public void Toggle() => SetOpen(!_isOpen);

    public void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        biomePanel.SetActive(!_isOpen);
        ToggleButton.SetActive(!_isOpen);
        
        if (_isOpen)
        {
            gridContainer.gameObject.SetActive(true);
            RefreshGrid();
        }

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(SlidePanel(_isOpen ? _shownPos : _hiddenPos));
    }

    private void RefreshGrid()
    {
        foreach (Transform child in gridContainer)
            Destroy(child.gameObject);

        foreach (var kvp in PlayerInventory.Instance.GetAllItems())
        {
            //Cell root
            var cell = new GameObject("Cell", typeof(RectTransform));
            cell.transform.SetParent(gridContainer, false);

            //Card background
            var cardGO = new GameObject("CardBackground", typeof(Image));
            cardGO.transform.SetParent(cell.transform, false);
            var cardRect = cardGO.GetComponent<RectTransform>();
            cardRect.anchorMin = Vector2.zero;
            cardRect.anchorMax = Vector2.one;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;
            var cardImage = cardGO.GetComponent<Image>();
            cardImage.color = new Color(0f, 0f, 0f, 0.4f);

            //Icon
            var iconGO = new GameObject("Icon", typeof(Image));
            iconGO.transform.SetParent(cell.transform, false);
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImage = iconGO.GetComponent<Image>();
            iconImage.sprite = kvp.Key.Icon;
            iconImage.enabled = kvp.Key.Icon != null;

            //Gradient overlay (faked with semi-transparent solid at bottom)
            var gradientGO = new GameObject("GradientOverlay", typeof(Image));
            gradientGO.transform.SetParent(cell.transform, false);
            var gradientRect = gradientGO.GetComponent<RectTransform>();
            gradientRect.anchorMin = new Vector2(0, 0);
            gradientRect.anchorMax = new Vector2(1, 0.45f);
            gradientRect.offsetMin = Vector2.zero;
            gradientRect.offsetMax = Vector2.zero;
            var gradientImage = gradientGO.GetComponent<Image>();
            gradientImage.color = new Color(0f, 0f, 0f, 0.55f);

            //Item name
            var nameGO = new GameObject("ItemName", typeof(TextMeshProUGUI));
            nameGO.transform.SetParent(cell.transform, false);
            var nameRect = nameGO.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0, 0);
            nameRect.anchorMax = new Vector2(1, 0.4f);
            nameRect.offsetMin = new Vector2(4, 2);
            nameRect.offsetMax = new Vector2(-4, -2);
            var nameText = nameGO.GetComponent<TextMeshProUGUI>();
            nameText.text = kvp.Key.ItemName;
            nameText.fontSize = 14;
            nameText.fontStyle = FontStyles.Bold;
            nameText.color = Color.white;
            nameText.alignment = TextAlignmentOptions.Bottom;
            nameText.overflowMode = TextOverflowModes.Truncate;

            //Count badge
            var countGO = new GameObject("Count", typeof(TextMeshProUGUI));
            countGO.transform.SetParent(cell.transform, false);
            var countRect = countGO.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(1, 1);
            countRect.anchorMax = new Vector2(1, 1);
            countRect.pivot = new Vector2(1, 1);
            countRect.anchoredPosition = new Vector2(-6, -6);
            countRect.sizeDelta = new Vector2(60, 30);
            var countText = countGO.GetComponent<TextMeshProUGUI>();
            countText.fontSize = 22;
            countText.fontStyle = FontStyles.Bold;
            countText.color = Color.white;
            countText.alignment = TextAlignmentOptions.TopRight;
            bool hideCount = kvp.Value <= 1 && kvp.Key.ItemType == ItemType.Collectible;
            countText.text = hideCount ? string.Empty : kvp.Value.ToString();
        }
    }

    private void OnInventoryChanged(ItemDefinition item, int newCount)
    {
        if (_isOpen) RefreshGrid();
    }

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
        if (!_isOpen) gridContainer.gameObject.SetActive(false);
        _anim = null;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
}