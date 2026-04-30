using System.Collections;
using UnityEngine;

/// <summary>
/// Draggable tool that removes any planted FloraBase from its slot.
/// The player must hold the tool over the flora for holdDuration seconds
/// before the slot is cleared — giving them a chance to pull back if they
/// drag over the wrong crop.
///
/// Swing animation mirrors ChoppingAxe — arcs from swingStartAngle to
/// swingEndAngle but in the upward direction, looping while the blade
/// stays over the flora. The slot is cleared and the flora destroyed
/// when the hold timer completes regardless of swing position.
///
/// Implements IUpgradeable — Tier 2 reduces holdDuration.
/// Implements IBiomeOccupant — registered with BiomeManager on enable.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Hoe : ToolBase
{
    [Header("Hold Settings")]
    [SerializeField, Tooltip("Time in seconds the tool must be held over flora before removal triggers.")]
    private float holdDuration = 2f;
    [SerializeField, Tooltip("Hold duration applied after Tier 2 upgrade is purchased.")]
    private float tier2HoldDuration = 1f;

    [Header("Swing Animation")]
    [SerializeField, Tooltip("Starting Z rotation of the upward swing arc (degrees). " +
                             "Should be positive — axe starts low, hoe starts high.")]
    private float swingStartAngle = 60f;
    [SerializeField, Tooltip("Ending Z rotation at the top of the arc (degrees). " +
                             "Should be negative — hoe swings upward opposite to axe.")]
    private float swingEndAngle = -45f;
    [SerializeField, Tooltip("Time in seconds to complete one full swing arc.")]
    private float swingDuration = 0.2f;
    
    private float _holdTimer;
    private Coroutine _swingCoroutine;

    #region Unity Lifecycle

    protected override void OnEnable()
    {
        base.OnEnable();
        BiomeManager.Instance?.RegisterOccupant(this);
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }

    private void Update()
    {
        if (!_isDragging || _currentObject == null) return;

        _holdTimer += Time.deltaTime;

        // Update progress fill on the flora's stat bars if visible.
        var flora = _currentObject.GetComponent<FloraBase>();
        if (flora != null)
            flora.SetRemoveProgress(Mathf.Clamp01(_holdTimer / holdDuration));

        if (_holdTimer >= holdDuration)
            RemoveFlora();
    }
    #endregion

    #region Tool Interaction Overrides
    /// <summary>
    /// Accepts any FloraBase that is not lost, regardless of growth stage.
    /// The hoe can remove crops at any stage including seeds and harvestable.
    /// </summary>
    protected override bool CanInteractWith(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        return flora != null && !flora.IsLost;
    }

    /// <summary>
    /// Shows flora stat bars in persistent mode, resets hold timer, and starts swing.
    /// </summary>
    protected override void OnObjectTouched(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        flora?.ShowStats(false);
        flora?.SetRemoveProgress(0f);

        _holdTimer = 0f;
        StartSwing(obj.GetComponent<FloraBase>());
    }

    /// <summary>
    /// Cancels swing and resets all state.
    /// Flora is NOT removed — player must hold for full duration.
    /// </summary>
    protected override void OnObjectLeft(GameObject obj)
    {
        var flora = obj.GetComponent<FloraBase>();
        flora?.HideStats();
        flora?.SetRemoveProgress(0f);

        _holdTimer = 0f;
        CancelSwing();
    }

    /// <summary>
    /// Safety net — cancels swing and resets hold timer if drag ends mid-hold.
    /// </summary>
    protected override void OnDragEnded()
    {
        _holdTimer = 0f;
        CancelSwing();
    }
    
    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        holdDuration = tier2HoldDuration;
        Debug.Log($"[Hoe] Upgraded — holdDuration reduced to {holdDuration}s.");
    }
    #endregion

    #region Flora Removal
    /// <summary>
    /// Clears the flora's parent slot, destroying the occupant and freeing the slot.
    /// Called automatically when _holdTimer reaches holdDuration.
    /// </summary>
    private void RemoveFlora()
    {
        if (_currentObject == null) return;

        var flora = _currentObject.GetComponent<FloraBase>();
        if (flora == null) return;

        // Clear all active alerts on the flora before destroying.
        AlertManager.Instance?.ClearAllAlerts(_currentObject);

        // Clear the parent slot — this destroys the occupant and resets the slot.
        var slot = flora.ParentSlot;
        if (slot != null)
            slot.Clear();
        else
            Destroy(_currentObject); // fallback if somehow not in a slot

        // Reset hoe state — _currentObject is gone so clear the reference.
        _currentObject = null;
        _holdTimer     = 0f;
        CancelSwing();

        Debug.Log("[Hoe] Flora removed and slot cleared.");
    }
    #endregion

    #region Swing Animations
    private void StartSwing(FloraBase flora)
    {
        if (_swingCoroutine != null)
            StopCoroutine(_swingCoroutine);

        _swingCoroutine = StartCoroutine(SwingCoroutine());
    }

    private void CancelSwing()
    {
        if (_swingCoroutine != null)
        {
            StopCoroutine(_swingCoroutine);
            _swingCoroutine = null;
        }

        transform.rotation = Quaternion.Euler(0f, 0f, 0f);
    }

    /// <summary>
    /// Arcs upward from swingStartAngle to swingEndAngle over swingDuration.
    /// Mirrors ChoppingAxe SwingCoroutine but in the reverse direction.
    /// Loops while the blade remains over the flora.
    /// </summary>
    private IEnumerator SwingCoroutine()
    {
        while (true)
        {
            // Snap to wind-up angle at the start of each swing.
            transform.rotation = Quaternion.Euler(0f, 0f, swingStartAngle);

            // Arc upward with ease-in so the swing accelerates into the motion.
            float elapsed = 0f;
            while (elapsed < swingDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / swingDuration);
                float eased = t * t;
                float angle = Mathf.LerpUnclamped(swingStartAngle, swingEndAngle, eased);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }

            // Hold at end angle briefly before next swing.
            transform.rotation = Quaternion.Euler(0f, 0f, swingEndAngle);
            yield return new WaitForSeconds(swingDuration * 0.3f);
        }
    }
    #endregion

    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Force Remove Current Flora")]
    private void DebugForceRemove()
    {
        if (_currentObject == null)
        {
            Debug.LogWarning("[Hoe] No flora currently in contact — drag the hoe over a crop first.");
            return;
        }

        RemoveFlora();
    }
#endif
    #endregion
}