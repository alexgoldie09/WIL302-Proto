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
/// Pre-pools emoji prefabs on Awake and rents/returns instances on ShowAlert/ClearAlert.
/// Each alert auto-dismisses after alertDuration seconds.
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
        [Tooltip("How many instances to pre-pool. Set to max expected simultaneous alerts of this type.")]
        public int poolSize = 8;
    }

    [Header("Alert Settings")]
    [SerializeField, Tooltip("How long in seconds each alert stays visible before auto-dismissing.")]
    private float alertDuration = 3f;

    [Header("Alert Catalogue")]
    [SerializeField, Tooltip("All available alert types. Each AlertType should appear at most once.")]
    private List<AlertEntry> alertEntries = new();

    /// <summary>Available (inactive) instances per alert type.</summary>
    private readonly Dictionary<AlertType, Queue<GameObject>> _pool = new();
    /// <summary>Currently showing alerts keyed by (owner, alertType).</summary>
    private readonly Dictionary<(GameObject owner, AlertType alertType), GameObject> _active = new();
    /// <summary>Running coroutines keyed by (owner, alertType) so we can cancel on manual clear.</summary>
    private readonly Dictionary<(GameObject owner, AlertType alertType), Coroutine> _timers = new();

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        BuildPool();
    }
    #endregion

    #region Public API
    /// <summary>
    /// Shows an alert emoji on the given owner GameObject.
    /// If that alert type is already showing on the owner it is replaced (timer resets).
    /// The emoji is parented to the owner at the given local offset.
    /// Auto-dismisses after alertDuration seconds.
    /// </summary>
    /// <summary>
    /// Shows an alert emoji on the given owner GameObject.
    /// If that alert type is already showing on the owner it is replaced (timer resets).
    /// The emoji is parented to the owner at the given local offset.
    /// If persistent is true the alert stays until manually cleared via ClearAlert.
    /// Otherwise auto-dismisses after alertDuration seconds.
    /// </summary>
    public void ShowAlert(GameObject owner, AlertType alertType, Vector2 offset, bool persistent = false)
    {
        if (owner == null)
        {
            Debug.LogWarning("[AlertManager] ShowAlert called with null owner.");
            return;
        }

        if (!_pool.ContainsKey(alertType))
        {
            Debug.LogWarning($"[AlertManager] No pool found for AlertType '{alertType}'. " +
                             $"Check that an AlertEntry exists for this type.");
            return;
        }

        var key = (owner, alertType);

        // If this alert is already active on this owner, clear it first so the
        // timer resets cleanly rather than stacking.
        if (_active.ContainsKey(key))
            ClearAlert(owner, alertType);

        GameObject instance = RentFromPool(alertType);
        if (instance == null)
        {
            Debug.LogWarning($"[AlertManager] Pool exhausted for '{alertType}'. " +
                             $"Consider increasing poolSize for this entry.");
            return;
        }

        // Parent, position, and activate.
        instance.transform.SetParent(owner.transform);
        instance.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
        instance.SetActive(true);

        _active[key] = instance;

        // Persistent alerts stay until manually cleared — no timer started.
        if (!persistent)
        {
            Coroutine timer = StartCoroutine(AutoDismiss(owner, alertType, alertDuration));
            _timers[key] = timer;
        }
    }

    /// <summary>
    /// Manually clears an active alert before the timer expires.
    /// Safe to call even if the alert is not currently showing.
    /// </summary>
    public void ClearAlert(GameObject owner, AlertType alertType)
    {
        if (owner == null) return;

        var key = (owner, alertType);

        // Cancel the running timer if there is one.
        if (_timers.TryGetValue(key, out Coroutine timer))
        {
            if (timer != null) StopCoroutine(timer);
            _timers.Remove(key);
        }

        // Return the instance to the pool.
        if (_active.TryGetValue(key, out GameObject instance))
        {
            ReturnToPool(alertType, instance);
            _active.Remove(key);
        }
    }

    /// <summary>
    /// Clears all active alerts on the given owner.
    /// Call this from OnDestroy on any fauna or flora that uses alerts.
    /// </summary>
    public void ClearAllAlerts(GameObject owner)
    {
        if (owner == null) return;

        var keysToRemove = _active.Keys.Where(key => key.owner == owner).ToList();

        foreach (var key in keysToRemove)
            ClearAlert(key.owner, key.alertType);
    }

    /// <summary>
    /// Returns true if the given alert type is currently showing on the owner.
    /// </summary>
    public bool IsAlertActive(GameObject owner, AlertType alertType)
    {
        return _active.ContainsKey((owner, alertType));
    }
    #endregion
    
    #region Pool Management
    private void BuildPool()
    {
        foreach (var entry in alertEntries)
        {
            if (entry.prefab == null)
            {
                Debug.LogWarning($"[AlertManager] AlertEntry '{entry.alertType}' has no prefab assigned — skipping.");
                continue;
            }

            if (_pool.ContainsKey(entry.alertType))
            {
                Debug.LogWarning($"[AlertManager] Duplicate AlertEntry for '{entry.alertType}' — skipping.");
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

    /// <summary>Returns an inactive instance from the pool, or null if exhausted.</summary>
    private GameObject RentFromPool(AlertType alertType)
    {
        if (_pool.TryGetValue(alertType, out Queue<GameObject> queue) && queue.Count > 0)
            return queue.Dequeue();

        return null;
    }

    /// <summary>
    /// Deactivates an instance, resets its local transform, re-parents it
    /// to AlertManager, and returns it to the pool queue.
    /// </summary>
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