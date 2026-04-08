using System.Collections;
using UnityEngine;

/// <summary>
/// Draggable tool that chops any Harvestable Tree it overlaps while dragged.
/// Each time the blade enters a tree a swing coroutine plays — arcing from
/// startAngle to endAngle — and calls Tree.Chop() at the impact point.
/// Resets rotation on drag end or flora exit.
/// </summary>
public class ChoppingAxe : ToolBase
{
    [Header("Chop")]
    [SerializeField, Tooltip("Damage dealt to the tree per swing impact.")]
    private float chopDamage = 1f;

    [Header("Swing Animation")]
    [SerializeField, Tooltip("Starting Z rotation of the swing arc (degrees).")]
    private float swingStartAngle = -45f;
    [SerializeField, Tooltip("Ending Z rotation at impact point (degrees).")]
    private float swingEndAngle = 60f;
    [SerializeField, Tooltip("Time in seconds to complete one full swing.")]
    private float swingDuration = 0.2f;
    
    private Coroutine _swingCoroutine;

    #region ToolBase Overrides
    protected override bool CanInteractWith(FloraBase flora)
    {
        // Axe only interacts with Trees at Harvestable stage.
        return flora is WoodTree && flora.Stage == FloraGrowthStage.Harvestable;
    }

    protected override void OnFloraTouched(FloraBase flora)
    {
        flora.ShowStats(false);
        StartSwing(flora as WoodTree);
    }

    protected override void OnFloraLeft(FloraBase flora)
    {
        flora.HideStats();
        CancelSwing();
    }

    protected override void OnDragEnded()
    {
        CancelSwing();
    }
    #endregion

    #region Animation
    private void StartSwing(WoodTree tree)
    {
        if (_swingCoroutine != null)
            StopCoroutine(_swingCoroutine);

        _swingCoroutine = StartCoroutine(SwingCoroutine(tree));
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
    /// Arcs from swingStartAngle to swingEndAngle over swingDuration.
    /// Calls Chop() at the impact point (swingEndAngle), then loops back
    /// to swingStartAngle to repeat while the blade remains over the tree.
    /// </summary>
    private IEnumerator SwingCoroutine(WoodTree tree)
    {
        while (true)
        {
            // Wind up to start angle instantly.
            transform.rotation = Quaternion.Euler(0f, 0f, swingStartAngle);

            // Swing through to end angle.
            float elapsed = 0f;
            while (elapsed < swingDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / swingDuration);
                // Ease in so the swing accelerates into the impact.
                float eased = t * t;
                float angle = Mathf.LerpUnclamped(swingStartAngle, swingEndAngle, eased);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }

            // Impact — call Chop at the end of the arc.
            transform.rotation = Quaternion.Euler(0f, 0f, swingEndAngle);
            tree?.Chop(chopDamage);

            // Brief pause at impact before next swing.
            yield return new WaitForSeconds(swingDuration * 0.3f);
        }
    }
    #endregion
}