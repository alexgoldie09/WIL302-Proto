using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ItemDefinition Item { get; private set; }
    public int AvailableCount { get; private set; }

    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Transform _originalParent;
    private Vector2 _originalPosition;
    private bool _dropped;
    private TextMeshProUGUI _countText;

    public void Initialise(ItemDefinition item, int count, Canvas canvas, TextMeshProUGUI countText)
    {
        Item = item;
        AvailableCount = count;
        _canvas = canvas;
        _rectTransform = GetComponent<RectTransform>();
        _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _countText = countText;
    }

    public void DecrementCount()
    {
        AvailableCount--;
        if (_countText != null)
            _countText.text = $"x{AvailableCount}";
        if (AvailableCount <= 0)
            gameObject.SetActive(false);
    }

    public void IncrementCount()
    {
        AvailableCount++;
        gameObject.SetActive(true);
        if (_countText != null)
            _countText.text = $"x{AvailableCount}";
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (AvailableCount <= 0) return;

        _originalParent = transform.parent;
        _originalPosition = _rectTransform.anchoredPosition;
        _dropped = false;

        transform.SetParent(_canvas.transform, true);
        transform.SetAsLastSibling();
        _canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (AvailableCount <= 0) return;
        _rectTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = true;

        if (!_dropped)
        {
            transform.SetParent(_originalParent, true);
            _rectTransform.anchoredPosition = _originalPosition;
        }
        else
        {
            // Return visual to grid but keep count decremented
            transform.SetParent(_originalParent, true);
            _rectTransform.anchoredPosition = _originalPosition;
        }

        _dropped = false;
    }

    public void NotifyDropped()
    {
        _dropped = true;
        DecrementCount();
    }
}