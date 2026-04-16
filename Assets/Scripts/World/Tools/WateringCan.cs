using UnityEngine;

/// <summary>
/// Draggable tool that waters any non-Harvestable FloraBase it overlaps while dragged.
/// Tilts to tiltAngle while actively watering and resets upright on release or exit.
/// </summary>
public class WateringCan : ToolBase
{
    [Header("Watering")]
    [SerializeField, Tooltip("Water added to a flora per second while overlapping.")]
    private float waterPerSecond = 0.3f;

    [Header("Tilt")]
    [SerializeField, Tooltip("Z rotation angle in degrees when actively watering.")]
    private float tiltAngle = 35f;
    
    /// <summary>
    /// Waters the current flora every frame while dragging and overlapping.
    /// Continuous call — amount is scaled by Time.deltaTime so rate is per-second.
    /// </summary>
    private void Update()
    {
        if (!_isDragging || _currentObject == null) return;

        var flora = _currentObject.GetComponent<FloraBase>();
        flora?.Water(waterPerSecond * Time.deltaTime);
    }

    #region ToolBase Overrides
    /// <summary>
    /// Only accepts FloraBase objects that are not yet at Harvestable stage.
    /// Harvestable flora no longer needs water so the can ignores them entirely.
    /// </summary>
    protected override bool CanInteractWith(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        return flora != null && !flora.IsLost && flora.Stage != FloraGrowthStage.Harvestable;
    }

    /// <summary>
    /// Shows the flora's stat bars in persistent mode and tilts the can.
    /// Stat bars stay visible until the can leaves — no auto-hide timer.
    /// </summary>
    protected override void OnObjectTouched(GameObject obj)
    {
        obj.GetComponent<FloraBase>()?.ShowStats(false);
        SetTilt(true);
    }

    /// <summary>
    /// Hides the flora's stat bars and resets the can upright.
    /// Called on trigger exit or drag end.
    /// </summary>
    protected override void OnObjectLeft(GameObject obj)
    {
        obj.GetComponent<FloraBase>()?.HideStats();
        SetTilt(false);
    }

    /// <summary>
    /// Resets tilt as a safety net when the drag ends —
    /// covers the case where drag ends while not over any flora.
    /// </summary>
    protected override void OnDragEnded()
    {
        SetTilt(false);
    }
    #endregion

    #region Tilt
    /// <summary>Applies or removes the watering tilt on the Z axis.</summary>
    private void SetTilt(bool watering)
    {
        float angle = watering ? tiltAngle : 0f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
    #endregion
}