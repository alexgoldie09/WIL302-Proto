using System.Collections;
using UnityEngine;

/// <summary>
/// Draggable tool that chops any Harvestable WoodTree it overlaps while dragged.
/// Each time the blade enters a tree a swing coroutine plays — arcing from
/// swingStartAngle to swingEndAngle with an ease-in — and calls WoodTree.Chop()
/// at the impact point. The swing loops while the blade stays over the tree
/// and cancels immediately on exit or drag end.
/// </summary>
public class ChoppingAxe : ToolBase
{
    [Header("Chop")]
    [SerializeField, Tooltip("Damage dealt to the tree per swing impact.")]
    private float chopDamage = 1f;
    [SerializeField, Tooltip("How much the chop damage increases with each upgrade. " +
                             "Upgrades are applied additively and capped at 10 damage.")]
    private float chopDamageUpgrade = 2f;

    [Header("Swing Animation")]
    [SerializeField, Tooltip("Starting Z rotation of the swing arc (degrees). " +
                             "The axe snaps here at the start of each swing.")]
    private float swingStartAngle = -45f;
    [SerializeField, Tooltip("Ending Z rotation at the impact point (degrees). " +
                             "Chop() is called when this angle is reached.")]
    private float swingEndAngle = 60f;
    [SerializeField, Tooltip("Time in seconds to complete one full swing arc.")]
    private float swingDuration = 0.2f;
    
    private Coroutine _swingCoroutine;
    
    

    #region ToolBase Overrides
    /// <summary>
    /// Only accepts WoodTree objects at Harvestable stage.
    /// Non-harvestable trees and all other objects are ignored.
    /// </summary>
    protected override bool CanInteractWith(GameObject obj)
    {
        var tree = obj.GetComponent<WoodTree>();
        return tree != null && tree.Stage == FloraGrowthStage.Harvestable;
    }

    /// <summary>
    /// Shows the tree's chop health stat bar in persistent mode and starts the swing loop.
    /// </summary>
    protected override void OnObjectTouched(GameObject obj)
    {
        var tree = obj.GetComponent<WoodTree>();
        if (tree == null) return;

        tree.ShowStats(false);
        StartSwing(tree);
    }

    /// <summary>
    /// Hides the tree's stat bar and cancels the swing coroutine.
    /// Called on trigger exit or drag end.
    /// </summary>
    protected override void OnObjectLeft(GameObject obj)
    {
        obj.GetComponent<WoodTree>()?.HideStats();
        CancelSwing();
    }

    /// <summary>
    /// Cancels any in-progress swing and resets rotation as a safety net
    /// when the drag ends — covers the case where drag ends while over a tree.
    /// </summary>
    protected override void OnDragEnded()
    {
        CancelSwing();
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        chopDamage = Mathf.Clamp(chopDamage + chopDamageUpgrade, 0f, 10f);
    }

    #endregion
    
    #region Swing
    /// <summary>Starts a fresh swing coroutine targeting the given tree.</summary>
    private void StartSwing(WoodTree tree)
    {
        if (_swingCoroutine != null)
            StopCoroutine(_swingCoroutine);

        _swingCoroutine = StartCoroutine(SwingCoroutine(tree));
    }

    /// <summary>
    /// Stops the swing coroutine and resets the axe to its neutral rotation.
    /// </summary>
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
    /// Swing loop — runs while the blade overlaps the tree.
    /// Each iteration snaps to swingStartAngle, eases in to swingEndAngle,
    /// calls Chop() at impact, then pauses briefly before the next swing.
    /// </summary>
    private IEnumerator SwingCoroutine(WoodTree tree)
    {
        while (true)
        {
            // Snap to wind-up angle at the start of each swing.
            transform.rotation = Quaternion.Euler(0f, 0f, swingStartAngle);

            // Arc toward the impact angle with an ease-in so the swing
            // accelerates into the hit rather than moving at constant speed.
            float elapsed = 0f;
            while (elapsed < swingDuration)
            {
                elapsed += Time.deltaTime;
                float t     = Mathf.Clamp01(elapsed / swingDuration);
                float eased = t * t;
                float angle = Mathf.LerpUnclamped(swingStartAngle, swingEndAngle, eased);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }

            // Hold at impact angle and deal damage.
            transform.rotation = Quaternion.Euler(0f, 0f, swingEndAngle);
            tree?.Chop(chopDamage);

            // Short pause at impact before winding up for the next swing.
            yield return new WaitForSeconds(swingDuration * 0.3f);
        }
    }
    #endregion
}