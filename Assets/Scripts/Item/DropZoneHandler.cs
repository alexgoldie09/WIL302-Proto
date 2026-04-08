using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class DropZoneHandler : MonoBehaviour, IDropHandler
{
    public event Action<ItemDefinition> OnItemDropped;

    [SerializeField] private Image zoneImage;

    private Color _normalColor = new Color(1f, 1f, 1f, 0.1f);
    private Color _highlightColor = new Color(0.2f, 0.8f, 0.2f, 0.3f);

    private void Awake()
    {
        if (zoneImage == null) zoneImage = GetComponent<Image>();
        zoneImage.color = _normalColor;
    }

    public void OnDrop(PointerEventData eventData)
    {
        var draggable = eventData.pointerDrag?.GetComponent<DraggableItem>();
        if (draggable == null) return;

        draggable.NotifyDropped();
        OnItemDropped?.Invoke(draggable.Item);
        zoneImage.color = _normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData) => 
        zoneImage.color = _highlightColor;

    public void OnPointerExit(PointerEventData eventData) => 
        zoneImage.color = _normalColor;
}