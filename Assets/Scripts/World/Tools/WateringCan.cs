using UnityEngine;

/// <summary>
/// Draggable tool that waters any non-Harvestable FloraBase it overlaps while dragged.
/// Tilts to tiltAngle while actively watering and resets upright on release.
/// </summary>
public class WateringCan : ToolBase
{
    [Header("Watering")]
    [SerializeField, Tooltip("Water added to a flora per second while overlapping.")]
    private float waterPerSecond = 0.3f;

    [Header("Tilt")]
    [SerializeField, Tooltip("Z rotation angle in degrees when actively watering.")]
    private float tiltAngle = 35f;

    #region ToolBase Overrides
    protected override bool CanInteractWith(FloraBase flora)
    {
        // Watering can ignores Harvestable flora — they don't need water.
        return flora.Stage != FloraGrowthStage.Harvestable;
    }

    protected override void OnFloraTouched(FloraBase flora)
    {
        flora.ShowStats(false);
        SetTilt(true);
    }

    protected override void OnFloraLeft(FloraBase flora)
    {
        flora.HideStats();
        SetTilt(false);
    }

    protected override void OnDragEnded()
    {
        SetTilt(false);
    }
    #endregion

    #region Unity Lifecycle
    private void Update()
    {
        if (!_isDragging || _currentFlora == null) 
            return;
        
        _currentFlora.Water(waterPerSecond * Time.deltaTime);
    }
    #endregion

    #region Animation
    private void SetTilt(bool watering)
    {
        float angle = watering ? tiltAngle : 0f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
    #endregion
}