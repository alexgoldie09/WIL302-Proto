using System.Collections.Generic;
using UnityEngine;

public class WaterSprinklerStructure : StructureBase
{
    [Header("Sprinkler Settings")]
    [Tooltip("Time interval (in seconds) between watering cycles.")]
    [SerializeField] private float waterInterval = 3f;
    [Tooltip("How long (in seconds) each watering cycle runs before stopping.")]
    [SerializeField] private float wateringDuration = 1f;
    [Tooltip("Amount of water applied per second to each plant while watering.")]
    [SerializeField] private float waterAmountPerSecond = 0.2f;
    [Tooltip("Amount to increase waterAmountPerSecond by when this structure is upgraded. " +
             "Applied additively and clamped to a max of 1.0f.")]
    [SerializeField] private float waterUpgradeAmount = 0.2f;
    [Tooltip("Layer to specify which contains plants that can be watered.")]
    [SerializeField] private LayerMask plantLayer;

    [Header("Range")]
    [Tooltip("Trigger collider used as the sprinkler range.")]
    [SerializeField] private CircleCollider2D rangeCollider;

    private float _nextWaterTime;
    private float _wateringEndTime;
    private bool _isWatering;

    private Collider2D[] _overlapResults = new Collider2D[16];

    private readonly HashSet<FloraBase> _floraWithActiveFx = new();
    private readonly List<FloraBase> _floraToDisableBuffer = new();

    private ContactFilter2D _plantFilter;

    #region Unity Lifecycle
    private void Awake()
    {
        _plantFilter.SetLayerMask(plantLayer);
        _plantFilter.useLayerMask = true;
    }
    
    protected override void OnDisable()
    {
        base.OnDisable();
        StopWateringCycle();
    }

    /// <summary>
    /// Manages the watering cycle:
    /// - If not built, ensures watering is stopped.
    /// - If currently watering, checks if the cycle should end or continues watering.
    /// - If not watering, checks if it's time to start a new cycle and starts it if so.
    /// </summary>
    private void Update()
    {
        if (!IsBuilt)
        {
            if (_isWatering)
                StopWateringCycle();
            return;
        }

        if (_isWatering)
        {
            if (Time.time >= _wateringEndTime)
            {
                StopWateringCycle();
                _nextWaterTime = Time.time + Mathf.Max(0.01f, waterInterval);
                return;
            }

            WaterPlants();
            return;
        }

        if (Time.time < _nextWaterTime) return;

        if (wateringDuration <= 0f)
        {
            _nextWaterTime = Time.time + Mathf.Max(0.01f, waterInterval);
            return;
        }

        _isWatering = true;
        _wateringEndTime = Time.time + wateringDuration;
        WaterPlants();
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        waterAmountPerSecond = Mathf.Clamp(waterAmountPerSecond + waterUpgradeAmount, 0f, 1f);
    }

    #endregion

    #region Watering Methods
    /// <summary>
    /// Finds all plants within the sprinkler's range and applies water to them.
    /// - Uses a ContactFilter2D to efficiently find relevant plants on the specified layer.
    /// - Maintains a set of plants that currently have watering FX active to avoid redundant calls.
    /// - Uses a buffer list to track which plants need their FX disabled after processing the current cycle.
    /// </summary>
    private void WaterPlants()
    {
        int hitCount = rangeCollider.Overlap(_plantFilter, _overlapResults);

        // Resize buffer if needed
        while (hitCount == _overlapResults.Length)
        {
            _overlapResults = new Collider2D[_overlapResults.Length * 2];
            hitCount = rangeCollider.Overlap(_plantFilter, _overlapResults);
        }

        // Assume all current FX may need disabling
        _floraToDisableBuffer.Clear();
        foreach (var flora in _floraWithActiveFx)
        {
            _floraToDisableBuffer.Add(flora);
        }

        // Process current plants
        for (int i = 0; i < hitCount; i++)
        {
            var plant = _overlapResults[i];
            var flora = plant.GetComponent<FloraBase>();
            if (flora == null) continue;

            flora.Water(waterAmountPerSecond * Time.deltaTime);

            // Enable FX only if newly added
            if (_floraWithActiveFx.Add(flora))
            {
                flora.SetWateringFx(true);
            }

            // Still valid this frame so don't disable
            _floraToDisableBuffer.Remove(flora);
        }

        // Disable anything no longer in range
        foreach (var flora in _floraToDisableBuffer)
        {
            flora?.SetWateringFx(false);
            _floraWithActiveFx.Remove(flora);
        }
    }

    /// <summary>
    /// Stops the watering cycle immediately, disables FX on all plants that had it active,
    /// and clears the tracking collections.
    /// </summary>
    private void StopWateringCycle()
    {
        _isWatering = false;

        foreach (FloraBase flora in _floraWithActiveFx)
            flora?.SetWateringFx(false);

        _floraWithActiveFx.Clear();
        _floraToDisableBuffer.Clear();
    }
    #endregion
}