using System;
using System.Collections;
using UnityEngine;


/// <summary>
/// Serialisable save data for EggCollectorStructure.
/// </summary>
[Serializable]
public class EggCollectorData : StructureData
{
    public int storedCount;
}

/// <summary>
/// Structure that automatically collects duck egg pickups within its radius.
/// Scans periodically for eggs — handles eggs spawned directly inside the radius.
/// Stores eggs up to capacity. Once full new eggs are left in the world.
/// Player drags the DraggableEggPile child past the collect threshold to add eggs to inventory.
///
/// capacity is the single source of truth shared with DraggableEggPile.
/// On upgrade, call Initialise(capacity) on the pile to rebuild at the new size.
/// </summary>
public class EggCollectorStructure : StructureBase
{
    [Header("Egg Collector Settings")]
    [SerializeField, Tooltip("The egg ItemDefinition added to PlayerInventory on collect.")]
    private ItemDefinition eggItem;
    [SerializeField, Tooltip("Maximum number of eggs this structure can hold. " +
                             "Also controls the number of egg sprites on DraggableEggPile.")]
    private int capacity = 10;
    [SerializeField, Tooltip("Amount to increase capacity by when this structure is upgraded. " +
                             "Applied additively on top of the current capacity.")]
    private int capacityUpgradeIncrease = 5;
    [SerializeField, Tooltip("Layer mask for egg pickup GameObjects.")]
    private LayerMask eggLayer;
    [SerializeField, Tooltip("Collection radius in world units.")]
    private float collectionRadius = 2f;
    [SerializeField, Tooltip("How often in seconds the structure scans for nearby eggs.")]
    private float scanInterval = 0.5f;

    [Header("References")]
    [SerializeField, Tooltip("The tappable egg pile child component.")]
    private TappableEggPile eggPile;
    
    private int _storedCount;
    private Coroutine _scanCoroutine;

    #region Unity Lifecycle
    protected override void Start()
    {
        base.Start();

        // Initialise pile with our capacity — pile never owns this value itself.
        eggPile?.Initialise(capacity);
        UpdateStorageVisuals();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        StopScan();
    }
    #endregion

    #region Upgrade Methods
    /// <summary>
    /// Call this when the upgrade system increases capacity.
    /// Rebuilds the egg pile sprites at the new size and clamps stored count.
    /// </summary>
    public void UpgradeCapacity(int newCapacity)
    {
        capacity = newCapacity;
        _storedCount = Mathf.Min(_storedCount, capacity);
        eggPile?.Initialise(capacity);
        UpdateStorageVisuals();
        Debug.Log($"[EggCollectorStructure] Capacity upgraded to {capacity}.");
    }
    #endregion

    #region StructureBase Overrides
    protected override void OnBuilt()
    {
        StartScan();
        _collider2D.enabled = false;
        HideStats();
    }

    public override void OnTapped()
    {
        if (IsBuilt)
            ShowStats(true);
        else
            base.OnTapped();
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        capacity = Mathf.Clamp(capacity + capacityUpgradeIncrease, 1, 100);
    }

    #endregion

    #region Scan Methods
    private void StartScan()
    {
        if (_scanCoroutine != null) return;
        _scanCoroutine = StartCoroutine(CollectionScanLoop());
    }

    private void StopScan()
    {
        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
        }
    }

    private IEnumerator CollectionScanLoop()
    {
        while (true)
        {
            ScanAndCollect();
            yield return new WaitForSeconds(scanInterval);
        }
    }

    private void ScanAndCollect()
    {
        if (_storedCount >= capacity) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(
            transform.position, collectionRadius, eggLayer);

        foreach (var hit in hits)
        {
            if (hit == null) continue;
            if (_storedCount >= capacity) break;
            TryCollectEgg(hit.gameObject);
        }
    }
    #endregion

    #region Egg Methods
    private void TryCollectEgg(GameObject eggPickup)
    {
        if (eggPickup == null) return;

        if (_storedCount >= capacity)
        {
            Debug.Log($"[EggCollectorStructure] At capacity ({capacity}) — egg left in world.");
            return;
        }

        _storedCount++;
        Destroy(eggPickup);
        UpdateStorageVisuals();

        Debug.Log($"[EggCollectorStructure] Collected egg. Stored: {_storedCount}/{capacity}.");
    }

    /// <summary>
    /// Adds all stored eggs to PlayerInventory and resets the stored count.
    /// Called by DraggableEggPile when the player drags past the collect threshold.
    /// </summary>
    public void CollectAll()
    {
        if (_storedCount <= 0)
        {
            Debug.Log("[EggCollectorStructure] Nothing to collect.");
            return;
        }

        if (eggItem == null)
        {
            Debug.LogWarning("[EggCollectorStructure] eggItem is not assigned.");
            return;
        }

        PlayerInventory.Instance.Add(eggItem, _storedCount);
        Debug.Log($"[EggCollectorStructure] Collected {_storedCount}x {eggItem.ItemName}.");

        _storedCount = 0;
        UpdateStorageVisuals();
    }
    #endregion

    #region Visuals
    /// <summary>
    /// Passes both storedCount and capacity to the pile so it never needs
    /// to own or cache capacity itself.
    /// </summary>
    private void UpdateStorageVisuals()
    {
        eggPile?.UpdateVisuals(_storedCount, capacity);
    }
    #endregion
    
    #region SaveableBehaviour

    public override string RecordType  => "Egg Coop";
    public override int    LoadPriority => 11;
    
    protected override StructureData DeserializeData(string json) => JsonUtility.FromJson<EggCollectorData>(json);

    protected override StructureData BuildData() => new EggCollectorData
    {
        stage             = (int)Stage,
        constructionTimer = ConstructionTimer,
        parentSlotGuid    = ParentSlotGuid,
        storedCount       = _storedCount
    };

    protected override void ApplyData(StructureData data, SaveContext context)
    {
        base.ApplyData(data, context);
        if (data is EggCollectorData eggData)
        {
            _storedCount = eggData.storedCount;
            UpdateStorageVisuals();
        }
    }

    #endregion

    #region Debug Methods
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.3f);
        Gizmos.DrawSphere(transform.position, collectionRadius);
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, collectionRadius);
    }

    [ContextMenu("Debug/Add Test Egg")]
    private void DebugAddTestEgg()
    {
        if (!IsBuilt) { Debug.LogWarning("[EggCollectorStructure] Not built yet."); return; }
        if (_storedCount >= capacity) { Debug.Log("[EggCollectorStructure] At capacity."); return; }
        _storedCount++;
        UpdateStorageVisuals();
        Debug.Log($"[EggCollectorStructure] Debug egg added. Stored: {_storedCount}/{capacity}.");
    }

    [ContextMenu("Debug/Force Collect All")]
    private void DebugForceCollect() => CollectAll();

    [ContextMenu("Debug/Test Upgrade Capacity to 20")]
    private void DebugUpgradeCapacity() => UpgradeCapacity(20);
#endif
    #endregion
}