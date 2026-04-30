using UnityEngine;

/// <summary>
/// Draggable tool that waters any non-Harvestable FloraBase it overlaps while dragged.
/// Tilts to tiltAngle while actively watering and resets upright on release or exit.
/// Reports watering progress to QuestManager once per drag session.
/// </summary>
public class WateringCan : ToolBase
{
    [Header("Watering")]
    [SerializeField, Tooltip("Water added to a flora per second while overlapping.")]
    private float waterPerSecond = 0.3f;
    [SerializeField, Tooltip("Additional water per second added by each watering upgrade. " +
                             "Upgrades stack additively (e.g. 2 upgrades = +0.4f water/s).")]
    private float waterUpgrade = 0.2f;

    [Header("Tilt")]
    [SerializeField, Tooltip("Z rotation angle in degrees when actively watering.")]
    private float tiltAngle = 35f;

    private bool _hasReportedWateringThisSession;

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
    /// Reports watering progress to QuestManager once per drag session.
    /// </summary>
    protected override void OnObjectTouched(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        flora?.ShowStats(false);
        SetTilt(true);
        flora?.SetWateringFx(true);

        if (!_hasReportedWateringThisSession)
        {
            string floraItemName = flora?.GetOutputItemPublic()?.ItemName;
            QuestManager.Instance?.RecordProgress(QuestObjectiveType.WaterCrop, floraItemName, 1);
            _hasReportedWateringThisSession = true;
        }
    }

    /// <summary>
    /// Hides the flora's stat bars and resets the can upright.
    /// Called on trigger exit or drag end.
    /// </summary>
    protected override void OnObjectLeft(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        flora?.HideStats();
        flora?.SetWateringFx(false);
        SetTilt(false);
    }

    /// <summary>
    /// Resets tilt as a safety net when the drag ends —
    /// covers the case where drag ends while not over any flora.
    /// Also resets the watering session flag so the next drag can report again.
    /// </summary>
    protected override void OnDragEnded()
    {
        SetTilt(false);
        _hasReportedWateringThisSession = false;
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        waterPerSecond = Mathf.Clamp(waterPerSecond + waterUpgrade, 0f, 1f);
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