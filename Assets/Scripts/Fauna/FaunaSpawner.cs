using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages fauna population for a single biome.
/// Supports multiple fauna types (e.g. Sheep + Cow on Farm), the cap is divided
/// evenly across all prefab types. Remainder slots go to earlier types in the list.
/// Example: cap=6, 2 types → 3 each. cap=5, 2 types → 3 of type 0, 2 of type 1.
///
/// Spawns fauna at randomised positions from a fixed list of spawn transforms.
/// Each transform holds at most one fauna at a time. Active count never exceeds
/// the lower of maxMobileFauna and spawnPoints.Count.
///
/// </summary>
public class FaunaSpawner : MonoBehaviour, IBiomeOccupant
{
    [Header("Identity")]
    [SerializeField, Tooltip("The biome this spawner manages.")]
    private BiomeManager.BiomeType homeBiome;

    [Header("Spawn Settings")]
    [SerializeField, Tooltip("Fauna prefabs to spawn. Cap is divided evenly across all types. " +
                             "Add multiple entries for mixed ecosystems e.g. Sheep + Cow.")]
    private List<GameObject> faunaPrefabs = new();

    [SerializeField, Tooltip("Fixed world positions where fauna can be spawned. " +
                             "Each position holds at most one fauna at a time. " +
                             "Active count never exceeds this list's length.")]
    private List<Transform> spawnPoints = new();

    [Header("Respawn")]
    [SerializeField, Tooltip("Seconds to wait before respawning after a fauna dies.")]
    private float respawnDelay = 5f;
    
    public BiomeManager.BiomeType HomeBiome => homeBiome;
    
    /// <summary>All currently live fauna keyed by prefab index.</summary>
    private readonly Dictionary<int, List<FaunaBase>> _activeByType = new();

    /// <summary>Spawn points currently occupied by a live fauna.</summary>
    private readonly HashSet<Transform> _occupiedPoints = new();

    #region Unity Lifecycle
    private void OnEnable()
    {
        BiomeManager.Instance?.RegisterOccupant(this);

        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeTierChanged += HandleBiomeTierChanged;
        
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied += HandleUpgradeApplied;
    }

    private void OnDisable()
    {
        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeTierChanged -= HandleBiomeTierChanged;
        
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied -= HandleUpgradeApplied;
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }

    private void Start()
    {
        // Initialise per-type tracking lists.
        for (int i = 0; i < faunaPrefabs.Count; i++)
            _activeByType[i] = new List<FaunaBase>();

        // Register fauna already in the scene (restored by SaveManager) so
        // SpawnToCapacity does not spawn duplicates on top of them.
        RegisterRestoredFauna();
        SpawnToCapacity();

        // Fauna that died offline have Destroy() called during Load() (Awake-phase),
        // but Unity defers the destruction to end-of-frame. SpawnToCapacity above
        // therefore counts them as still alive. Re-check one frame later once pending
        // destructions have resolved so we respawn up to quota if any were lost.
        StartCoroutine(LateCapacityCheck());
    }

    private IEnumerator LateCapacityCheck()
    {
        yield return null;
        SpawnToCapacity();
    }
    #endregion

    #region Spawning Methods
    /// <summary>
    /// Spawns fauna of each type until each reaches its quota.
    /// Called on Start, after a death respawn delay, and on tier upgrade.
    /// </summary>
    private void SpawnToCapacity()
    {
        if (faunaPrefabs.Count == 0) return;

        int cap = GetEffectiveCap();

        for (int typeIndex = 0; typeIndex < faunaPrefabs.Count; typeIndex++)
        {
            int quota   = GetQuotaForType(typeIndex, cap);
            int current = GetActiveCountForType(typeIndex);
            int needed  = quota - current;

            for (int i = 0; i < needed; i++)
            {
                Transform point = GetFreeSpawnPoint();
                if (point == null) return; // no free points left across all types

                SpawnAt(typeIndex, point);
            }
        }
    }

    /// <summary>
    /// Instantiates one fauna of the given type at the given point.
    /// Subscribes to OnLost, tracks in per-type list, marks point occupied.
    /// </summary>
    private void SpawnAt(int typeIndex, Transform point)
    {
        var prefab = faunaPrefabs[typeIndex];
        if (prefab == null)
        {
            Debug.LogWarning($"[FaunaSpawner] Prefab at index {typeIndex} is null.");
            return;
        }

        var biomeRoot = BiomeManager.Instance?.GetBiomeByType(homeBiome)?.rootObject?.transform;
        var instance  = Instantiate(prefab, point.position, Quaternion.identity, biomeRoot);

        // If this biome is not currently active hide the renderer immediately
        // since BiomeManager's SetBiomeVisible has already run for this frame.
        if (BiomeManager.Instance != null &&
            BiomeManager.Instance.ActiveBiomeData.biomeType != homeBiome)
        {
            var sr = instance.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }
        
        var fauna = instance.GetComponent<FaunaBase>();
        if (fauna == null)
        {
            Debug.LogWarning("[FaunaSpawner] Spawned prefab has no FaunaBase component.");
            Destroy(instance);
            return;
        }

        _activeByType[typeIndex].Add(fauna);
        _occupiedPoints.Add(point);

        // Capture for closure — typeIndex and point are specific to this spawn.
        fauna.OnLost += () => HandleFaunaLost(fauna, typeIndex, point);

        Debug.Log($"[FaunaSpawner] Spawned {prefab.name} (type {typeIndex}) at {point.name}. " +
                  $"Active: {GetTotalActive()}/{GetEffectiveCap()}.");
    }

    /// <summary>
    /// Removes the dead fauna from tracking, frees its spawn point, and
    /// schedules a respawn for that specific type so quotas are maintained.
    /// </summary>
    private void HandleFaunaLost(FaunaBase fauna, int typeIndex, Transform point)
    {
        if (_activeByType.ContainsKey(typeIndex))
        {
            _activeByType[typeIndex].Remove(fauna);
            _activeByType[typeIndex].RemoveAll(f => f == null);
        }

        _occupiedPoints.Remove(point);

        int remaining = GetActiveCountForType(typeIndex);

        Debug.Log($"[FaunaSpawner] Fauna (type {typeIndex}) lost. " +
                  $"Remaining of this type: {remaining}.");

        // Only respawn when the entire type is wiped out not on individual deaths.
        // This teaches the player to maintain their animals while guaranteeing
        // a full reset if they completely neglect a type.
        if (remaining == 0)
        {
            Debug.Log($"[FaunaSpawner] All type {typeIndex} fauna lost — " +
                      $"respawning to quota after {respawnDelay}s.");
            StartCoroutine(RespawnTypeAfterDelay(typeIndex));
        }
    }

    /// <summary>
    /// Waits respawnDelay then spawns this specific type back up to its quota.
    /// Only spawns the one that died — not a full capacity reset.
    /// </summary>
    private IEnumerator RespawnTypeAfterDelay(int typeIndex)
    {
        yield return new WaitForSeconds(respawnDelay);

        int cap     = GetEffectiveCap();
        int quota   = GetQuotaForType(typeIndex, cap);
        int current = GetActiveCountForType(typeIndex);
        int needed  = quota - current;

        for (int i = 0; i < needed; i++)
        {
            Transform point = GetFreeSpawnPoint();
            if (point == null) break;
            SpawnAt(typeIndex, point);
        }
    }
    #endregion

    #region Upgrade
    private void HandleBiomeTierChanged(BiomeManager.BiomeType biome, int tier)
    {
        if (biome != homeBiome) return;
        SpawnToCapacity();

        Debug.Log($"[FaunaSpawner] Tier changed to {tier} — " +
                  $"new cap {GetEffectiveCap()} across {faunaPrefabs.Count} type(s).");
    }
    
    private void HandleUpgradeApplied(UpgradeDefinition upgrade)
    {
        if (upgrade.UpgradeType != UpgradeType.BiomeFaunaCapIncrease) return;
        if (upgrade.TargetBiome != homeBiome) return;

        SpawnToCapacity();
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Scans the biome's registered occupants for FaunaBase instances that were
    /// spawned by SaveManager during load. Adds them to _activeByType so the
    /// existing population is counted before fresh spawning begins.
    /// </summary>
    private void RegisterRestoredFauna()
    {
        if (BiomeManager.Instance == null) return;

        // Map each prefab's RecordType → its type index in our list.
        var typeMap = new Dictionary<string, int>();
        for (int i = 0; i < faunaPrefabs.Count; i++)
        {
            if (faunaPrefabs[i] == null) continue;
            var proto = faunaPrefabs[i].GetComponent<FaunaBase>();
            if (proto != null && !typeMap.ContainsKey(proto.RecordType))
                typeMap[proto.RecordType] = i;
        }

        var occupants = BiomeManager.Instance.GetOccupantsOfType<FaunaBase>(homeBiome);
        foreach (var fauna in occupants)
        {
            if (!typeMap.TryGetValue(fauna.RecordType, out int typeIndex)) continue;
            if (_activeByType[typeIndex].Contains(fauna)) continue;

            _activeByType[typeIndex].Add(fauna);
            // Restored fauna are not at a fixed spawn point; pass null so the
            // occupied-points set is not affected.
            fauna.OnLost += () => HandleFaunaLost(fauna, typeIndex, null);
        }
    }
    
    /// <summary>
    /// Effective cap = min(BiomeData.maxFauna, spawnPoints.Count).
    /// Spawn points are the hard ceiling — each holds at most one fauna.
    /// </summary>
    private int GetEffectiveCap()
    {
        int biomeCap = BiomeManager.Instance?.GetBiomeByType(homeBiome)?.maxFauna
                       ?? spawnPoints.Count;
        return Mathf.Min(biomeCap, spawnPoints.Count);
    }

    /// <summary>
    /// Returns the quota for a given prefab type within the total cap.
    /// Divides cap evenly — remainder slots distributed to earlier types.
    /// Example: cap=5, 2 types → type 0 gets 3, type 1 gets 2.
    /// </summary>
    private int GetQuotaForType(int typeIndex, int cap)
    {
        if (faunaPrefabs.Count == 0) return 0;

        int baseQuota  = cap / faunaPrefabs.Count;
        int remainder  = cap % faunaPrefabs.Count;

        // Earlier types absorb the remainder slots.
        return baseQuota + (typeIndex < remainder ? 1 : 0);
    }

    private int GetActiveCountForType(int typeIndex)
    {
        if (!_activeByType.ContainsKey(typeIndex)) return 0;
        _activeByType[typeIndex].RemoveAll(f => f == null);
        return _activeByType[typeIndex].Count;
    }

    private int GetTotalActive()
    {
        int total = 0;
        foreach (var list in _activeByType.Values) total += list.Count;
        return total;
    }

    /// <summary>Returns a random unoccupied spawn point, or null if all are taken.</summary>
    private Transform GetFreeSpawnPoint()
    {
        var free = new List<Transform>();
        foreach (var point in spawnPoints)
            if (point != null && !_occupiedPoints.Contains(point))
                free.Add(point);

        return free.Count == 0 ? null : free[Random.Range(0, free.Count)];
    }
    #endregion

    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Log Spawner State")]
    private void DebugLogState()
    {
        int cap = GetEffectiveCap();
        Debug.Log($"[FaunaSpawner] Biome: {homeBiome} | Total: {GetTotalActive()}/{cap} | " +
                  $"Free points: {spawnPoints.Count - _occupiedPoints.Count}");

        for (int i = 0; i < faunaPrefabs.Count; i++)
            Debug.Log($"  Type {i} ({faunaPrefabs[i]?.name}): " +
                      $"{GetActiveCountForType(i)}/{GetQuotaForType(i, cap)}");
    }

    [ContextMenu("Debug/Force Spawn To Capacity")]
    private void DebugSpawnToCapacity() => SpawnToCapacity();

    [ContextMenu("Debug/Kill All Fauna")]
    private void DebugKillAll()
    {
        foreach (var list in _activeByType.Values)
        {
            foreach (var f in list)
                if (f != null) Destroy(f.gameObject);
        }
    }
#endif
    #endregion
}