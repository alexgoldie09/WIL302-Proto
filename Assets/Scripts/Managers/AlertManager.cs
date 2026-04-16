using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// All available alert types. Add new entries here when a new alert is needed.
/// Appears as a dropdown in the Inspector on AlertEntry.
/// </summary>
public enum AlertType
{
    Hungry,
    Happy,
    Unhappy,
    Love,
    Angry,
    NeedsAttention,
    NeedsWater,
    ReadyToHarvest,
    ReadyToCollect,
}

/// <summary>
/// Manages all in-world alert emojis for animals, plants, and any future alertable objects.
/// 
/// Active biome behaviour:
///   Emojis are only shown when the owner's biome is currently active.
///   Alerts fired on inactive biomes are queued as pending entries.
///   When a biome becomes active, its pending queue is flushed and all queued
///   emojis are shown immediately.
/// 
/// Cross-biome notifications:
///   When an alert is queued for an inactive biome, OnBiomeNotification fires.
///   BiomeNotificationUI listens to this and shows a banner so the player knows
///   something needs attention in another biome.
/// </summary>
public class AlertManager : MonoBehaviour
{
    public static AlertManager Instance { get; private set; }
    
    [Serializable]
    public class AlertEntry
    {
        [Tooltip("The alert type this entry handles.")]
        public AlertType alertType;
        [Tooltip("Emoji prefab to spawn for this alert.")]
        public GameObject prefab;
        [Tooltip("How many instances to pre-pool.")]
        public int poolSize = 8;
    }

    /// <summary>
    /// A queued alert waiting to be shown when its biome becomes active.
    /// Keyed by (owner, alertType) so duplicates naturally overwrite.
    /// </summary>
    private struct PendingAlert
    {
        public GameObject owner;
        public AlertType  alertType;
        public Vector2    offset;
        public bool       persistent;
    }

    [Header("Alert Settings")]
    [SerializeField, Tooltip("How long in seconds each non-persistent alert stays visible.")]
    private float alertDuration = 3f;

    [Header("Alert Catalogue")]
    [SerializeField, Tooltip("All available alert types. Each AlertType should appear at most once.")]
    private List<AlertEntry> alertEntries = new();
    
    /// <summary>
    /// Fired when an alert is queued for an inactive biome.
    /// BiomeNotificationUI subscribes to show a cross-biome banner.
    /// Passes the biome type and a human-readable message.
    /// </summary>
    public event Action<BiomeManager.BiomeType, string> OnBiomeNotification;
    
    /// <summary>Available (inactive) instances per alert type.</summary>
    private readonly Dictionary<AlertType, Queue<GameObject>> _pool = new();

    /// <summary>Currently showing alerts keyed by (owner, alertType).</summary>
    private readonly Dictionary<(GameObject owner, AlertType alertType), GameObject> _active = new();

    /// <summary>Running auto-dismiss coroutines keyed by (owner, alertType).</summary>
    private readonly Dictionary<(GameObject owner, AlertType alertType), Coroutine> _timers = new();

    /// <summary>
    /// Pending alerts per biome — alerts queued while that biome was inactive.
    /// Flushed when the biome becomes active.
    /// Keyed by (owner, alertType) so duplicate alerts overwrite rather than stack.
    /// </summary>
    private readonly Dictionary<BiomeManager.BiomeType,
        Dictionary<(GameObject owner, AlertType alertType), PendingAlert>> _pending = new();

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    private void Start()
    {
        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeActivated += HandleBiomeActivated;
        else
            Debug.LogWarning("[AlertManager] BiomeManager not found on Start.");
    }

    private void OnDestroy()
    {
        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeActivated -= HandleBiomeActivated;
    }
    #endregion

    #region Public API
    /// <summary>
    /// Shows an alert emoji on the given owner GameObject.
    /// If the owner's biome is inactive the alert is queued and a cross-biome
    /// notification is fired instead. If the biome is active the emoji shows immediately.
    /// If persistent is true the alert stays until ClearAlert is called explicitly.
    /// </summary>
    public void ShowAlert(GameObject owner, AlertType alertType, Vector2 offset,
                          bool persistent = false)
    {
        if (owner == null)
        {
            Debug.LogWarning("[AlertManager] ShowAlert called with null owner.");
            return;
        }

        if (!_pool.ContainsKey(alertType))
        {
            Debug.LogWarning($"[AlertManager] No pool found for AlertType '{alertType}'.");
            return;
        }

        // Check if the owner's biome is currently active.
        var occupant = owner.GetComponent<IBiomeOccupant>();
        if (occupant != null && BiomeManager.Instance != null &&
            occupant.HomeBiome != BiomeManager.Instance.ActiveBiomeData.biomeType)
        {
            QueueAlert(occupant.HomeBiome, owner, alertType, offset, persistent);
            return;
        }

        ShowAlertImmediate(owner, alertType, offset, persistent);
    }

    /// <summary>
    /// Manually clears an active alert before the timer expires.
    /// Also removes any matching pending alert for this owner.
    /// Safe to call even if the alert is not currently showing.
    /// </summary>
    public void ClearAlert(GameObject owner, AlertType alertType)
    {
        if (owner == null) return;

        var key = (owner, alertType);

        // Cancel timer if running.
        if (_timers.TryGetValue(key, out Coroutine timer))
        {
            if (timer != null) StopCoroutine(timer);
            _timers.Remove(key);
        }

        // Return instance to pool.
        if (_active.TryGetValue(key, out GameObject instance))
        {
            ReturnToPool(alertType, instance);
            _active.Remove(key);
        }

        // Remove from pending queue if present.
        var occupant = owner.GetComponent<IBiomeOccupant>();
        if (occupant != null && _pending.TryGetValue(occupant.HomeBiome, out var queue))
            queue.Remove(key);
    }

    /// <summary>
    /// Clears all active and pending alerts on the given owner.
    /// Call from OnDestroy on any fauna or flora that uses alerts.
    /// </summary>
    public void ClearAllAlerts(GameObject owner)
    {
        if (owner == null) return;

        // Clear active alerts.
        var activeKeys = _active.Keys.Where(k => k.owner == owner).ToList();
        foreach (var key in activeKeys)
            ClearAlert(key.owner, key.alertType);

        // Clear pending alerts.
        var occupant = owner.GetComponent<IBiomeOccupant>();
        if (occupant != null && _pending.TryGetValue(occupant.HomeBiome, out var queue))
        {
            var pendingKeys = queue.Keys.Where(k => k.owner == owner).ToList();
            foreach (var key in pendingKeys)
                queue.Remove(key);
        }
    }

    /// <summary>Returns true if the given alert type is currently showing on the owner.</summary>
    public bool IsAlertActive(GameObject owner, AlertType alertType)
        => _active.ContainsKey((owner, alertType));
    #endregion

    #region Immediate Display Method
    /// <summary>
    /// Shows the alert immediately without any biome check.
    /// Used both for active-biome alerts and when flushing the pending queue.
    /// </summary>
    private void ShowAlertImmediate(GameObject owner, AlertType alertType,
                                    Vector2 offset, bool persistent)
    {
        var key = (owner, alertType);

        // Replace existing alert so timer resets cleanly.
        if (_active.ContainsKey(key))
            ClearAlert(owner, alertType);

        GameObject instance = RentFromPool(alertType);
        if (instance == null)
        {
            Debug.LogWarning($"[AlertManager] Pool exhausted for '{alertType}'. " +
                             $"Consider increasing poolSize.");
            return;
        }

        instance.transform.SetParent(owner.transform);
        instance.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
        instance.SetActive(true);

        _active[key] = instance;

        if (!persistent)
        {
            Coroutine timer = StartCoroutine(AutoDismiss(owner, alertType, alertDuration));
            _timers[key] = timer;
        }
    }
    #endregion

    #region Pending Queues
    /// <summary>
    /// Adds an alert to the pending queue for the given biome.
    /// Duplicate (owner, alertType) entries overwrite so counts stay flat.
    /// Fires OnBiomeNotification so the HUD banner knows to show.
    /// </summary>
    private void QueueAlert(BiomeManager.BiomeType biome, GameObject owner,
                            AlertType alertType, Vector2 offset, bool persistent)
    {
        if (!_pending.ContainsKey(biome))
            _pending[biome] = new Dictionary<(GameObject, AlertType), PendingAlert>();

        var key = (owner, alertType);
        _pending[biome][key] = new PendingAlert
        {
            owner      = owner,
            alertType  = alertType,
            offset     = offset,
            persistent = persistent
        };

        string message = BuildNotificationMessage(biome, alertType);
        OnBiomeNotification?.Invoke(biome, message);
    }

    /// <summary>
    /// Flushes all pending alerts for the given biome, showing each immediately.
    /// Called when the player switches to that biome.
    /// </summary>
    private void FlushPendingAlerts(BiomeManager.BiomeType biome)
    {
        if (!_pending.TryGetValue(biome, out var queue) || queue.Count == 0) return;

        // Copy to avoid modifying during iteration.
        var pending = queue.Values.ToList();
        queue.Clear();

        foreach (var alert in pending)
        {
            if (alert.owner == null) continue;
            ShowAlertImmediate(alert.owner, alert.alertType, alert.offset, alert.persistent);
        }

        Debug.Log($"[AlertManager] Flushed {pending.Count} pending alert(s) for {biome}.");
    }

    /// <summary>
    /// Builds a human-readable notification message for the banner.
    /// </summary>
    private static string BuildNotificationMessage(BiomeManager.BiomeType biome,
                                                   AlertType alertType)
    {
        string biomeName = biome.ToString();
        return alertType switch
        {
            AlertType.Hungry        => $"Animals are hungry in {biomeName}!",
            AlertType.NeedsAttention => $"Something is dying in {biomeName}!",
            AlertType.NeedsWater    => $"Crops need water in {biomeName}!",
            AlertType.ReadyToHarvest => $"Crops ready to harvest in {biomeName}!",
            AlertType.ReadyToCollect => $"Items ready to collect in {biomeName}!",
            AlertType.Unhappy       => $"Animals are unhappy in {biomeName}!",
            _                       => $"Check on {biomeName}!"
        };
    }
    #endregion

    #region Biome Switching
    /// <summary>
    /// Fires when the active biome changes.
    /// Flushes any pending alerts for the newly active biome.
    /// </summary>
    private void HandleBiomeActivated(BiomeManager.BiomeType biome)
    {
        FlushPendingAlerts(biome);
    }
    #endregion

    #region Pooling Methods
    private void BuildPool()
    {
        foreach (var entry in alertEntries)
        {
            if (entry.prefab == null)
            {
                Debug.LogWarning($"[AlertManager] AlertEntry '{entry.alertType}' " +
                                 $"has no prefab — skipping.");
                continue;
            }

            if (_pool.ContainsKey(entry.alertType))
            {
                Debug.LogWarning($"[AlertManager] Duplicate AlertEntry for " +
                                 $"'{entry.alertType}' — skipping.");
                continue;
            }

            var queue = new Queue<GameObject>();
            for (int i = 0; i < entry.poolSize; i++)
            {
                GameObject instance = Instantiate(entry.prefab, transform);
                instance.name = $"{entry.alertType}_Pooled_{i}";
                instance.SetActive(false);
                queue.Enqueue(instance);
            }

            _pool[entry.alertType] = queue;
        }

        Debug.Log($"[AlertManager] Pool built with {_pool.Count} alert type(s).");
    }

    private GameObject RentFromPool(AlertType alertType)
    {
        if (_pool.TryGetValue(alertType, out Queue<GameObject> queue) && queue.Count > 0)
            return queue.Dequeue();
        return null;
    }

    private void ReturnToPool(AlertType alertType, GameObject instance)
    {
        instance.SetActive(false);
        instance.transform.SetParent(transform);
        instance.transform.localPosition = Vector3.zero;

        if (_pool.TryGetValue(alertType, out Queue<GameObject> queue))
            queue.Enqueue(instance);
    }
    #endregion

    #region Coroutines
    private IEnumerator AutoDismiss(GameObject owner, AlertType alertType, float duration)
    {
        yield return new WaitForSeconds(duration);
        ClearAlert(owner, alertType);
    }
    #endregion
    
    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Log Pool Status")]
    private void DebugLogPoolStatus()
    {
        foreach (var kvp in _pool)
            Debug.Log($"[AlertManager] Pool '{kvp.Key}': {kvp.Value.Count} available, " +
                      $"{CountActive(kvp.Key)} active.");
    }

    [ContextMenu("Debug/Log Pending Alerts")]
    private void DebugLogPending()
    {
        if (_pending.Count == 0) { Debug.Log("[AlertManager] No pending alerts."); return; }
        foreach (var kvp in _pending)
            Debug.Log($"[AlertManager] {kvp.Key}: {kvp.Value.Count} pending alert(s).");
    }

    private int CountActive(AlertType alertType)
    {
        int count = 0;
        foreach (var key in _active.Keys)
            if (key.alertType == alertType) count++;
        return count;
    }
#endif
    #endregion
}