using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Central coordinator for saving, loading, spawning missing saveables, and handling save file lifecycle events.
/// Maintains a static registry of all active ISaveable objects, serializes their state to JSON on save,
/// and restores them in load-priority order on load. Also handles spawning prefabs for any records
/// that are missing from the scene.
/// </summary>
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }
    private static readonly List<ISaveable> registeredSaveables = new();

    [Header("Save Settings")]
    [SerializeField, Tooltip("File name stored in Application.persistentDataPath.")]
    private string saveFileName = "save.json";
    [SerializeField, Tooltip("Clamp catch-up time to prevent extreme clock changes from exploding simulation.")]
    private float maxCatchupHours = 168f; // 7 days

    [Header("Auto Save/Load")]
    [SerializeField, Tooltip("If true, automatically loads save data on game start.")]
    private bool autoLoadOnStart = true;
    [SerializeField, Tooltip("If true, automatically saves when the application is quitting.")]
    private bool autoSaveOnQuit = true;
    [SerializeField, Tooltip("If true, automatically saves when the application loses focus (mobile/home button).")]
    private bool autoSaveOnFocusLost = true;
    [SerializeField, Tooltip("How often in seconds the game auto-saves while running. Set to 0 to disable.")] 
    private float autoSaveInterval = 600f;

    [Header("Spawn Registry")]
    [SerializeField, Tooltip("Spawnable prefabs for records that are missing in-scene (prefabKey -> prefab).")]
    private List<SpawnEntry> spawnRegistry = new();
    
    [Header("Debug")]
    [SerializeField] private bool verboseLogging = true;
    [SerializeField] private TextMeshProUGUI debugText;
    
    /// <summary>
    /// Maps a prefab key stored in save data to the prefab used to recreate missing objects.
    /// </summary>
    [Serializable]
    private class SpawnEntry
    {
        public string prefabKey;
        public GameObject prefab;
    }

    private bool hasQuit = false;
    private Dictionary<string, GameObject> spawnLookup;
    private float _lastSaveTime = -1f;
    private const float SaveCooldown = 2f;
    private string SavePath => Path.Combine(Application.persistentDataPath, saveFileName);

    #region Unity Lifecycle
    /// <summary>
    /// Enforces singleton behavior, prepares lookup caches, and optionally loads persisted data.
    /// </summary>
    private void Awake()
    {
        // Enforce singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Optional: keep across scenes
        // DontDestroyOnLoad(gameObject);

        BuildSpawnLookup();

        if (debugText != null)
            debugText.gameObject.SetActive(false);

        if (autoLoadOnStart)
            Load();
    }
    
    private void Start()
    {
        if (autoSaveInterval > 0f)
            StartCoroutine(AutoSaveLoop());
    }

    /// <summary>
    /// Saves data on application quit when the auto-save option is enabled.
    /// </summary>
    private void OnApplicationQuit()
    {
        if (autoSaveOnQuit && CanSave())
            Save();
    }
    
    /// <summary>
    /// Saves data when the application is paused (mobile/home button), supporting mobile workflows.
    /// </summary>
    /// <param name="paused"></param>
    private void OnApplicationPause(bool paused)
    {
        if (paused && CanSave())
        {
            Save();
            Debug.Log("[SaveManager] Save triggered by app pause.");
        }
    }

    /// <summary>
    /// Saves data when focus is lost, supporting mobile/background workflows.
    /// </summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!autoSaveOnFocusLost)
            return;

        if (!hasFocus && CanSave())
        {
            Save();
            Debug.Log("[SaveManager] Save triggered by focus loss.");
        }
    }
    
    private IEnumerator AutoSaveLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoSaveInterval);
            if (CanSave())
            {
                Save();
                Debug.Log("[SaveManager] Auto-save triggered.");
            }
        }
    }

    private bool CanSave()
    {
        if (Time.unscaledTime - _lastSaveTime < SaveCooldown) return false;
        _lastSaveTime = Time.unscaledTime;
        return true;
    }
    #endregion

    #region Saveable Registration
    /// <summary>
    /// Adds a saveable to the active registry so it is included in future save and load operations.
    /// Silently ignores null entries and duplicates.
    /// </summary>
    public static void RegisterSaveable(ISaveable saveable)
    {
        if (saveable == null || registeredSaveables.Contains(saveable))
            return;

        registeredSaveables.Add(saveable);
    }

    /// <summary>
    /// Removes a saveable from the active registry so it is excluded from future save and load operations.
    /// </summary>
    public static void UnregisterSaveable(ISaveable saveable)
    {
        if (saveable == null)
            return;

        registeredSaveables.Remove(saveable);
    }
    #endregion

    #region Save/Load/Delete
    /// <summary>
    /// Captures all registered saveables and writes their state to a JSON save file in persistent storage.
    /// Records with missing GUIDs or type keys are skipped with a warning.
    /// </summary>
    public void Save()
    {
        var utcNow = DateTime.UtcNow;

        // 1) Build the save file shell with the current timestamp.
        var saveFile = new SaveFile
        {
            version = 1,
            lastQuitUtcTicks = utcNow.Ticks,
            records = new List<SaveRecord>()
        };
        
        // 2) Capture state from every registered saveable, skipping invalid records.
        int captured = 0;

        foreach (var saveable in registeredSaveables)
        {
            if (saveable == null)
                continue;

            var record = saveable.CaptureState();

            if (record == null)
            {
                LogWarn($"CaptureState returned null on {GetSaveableName(saveable)}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.id))
            {
                LogWarn($"Skipping record with missing id on {GetSaveableName(saveable)}. Did you assign a persistent GUID?");
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.type))
            {
                LogWarn($"Skipping record with missing type on {GetSaveableName(saveable)}.");
                continue;
            }

            saveFile.records.Add(record);
            captured++;
        }

        // 3) Serialize and write to disk.
        var json = JsonUtility.ToJson(saveFile, prettyPrint: true);

        try
        {
            File.WriteAllText(SavePath, json);
            Log($"Saved {captured} record(s) to: {SavePath}");

            StartCoroutine(ShowDebugText("Save Complete!", 2f));

            if (verboseLogging)
                Log($"JSON:\n{json}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Failed to write save file at {SavePath}\n{ex}");
        }
        
        // FlushToDisk();
    }

    /// <summary>
    /// Reads the save file from disk, spawns any missing scene objects, then restores all
    /// registered saveables in load-priority order using a shared SaveContext.
    /// </summary>
    private void Load()
    {
        if (!File.Exists(SavePath))
        {
            LogWarn($"No save file found at: {SavePath}");
            StartCoroutine(ShowDebugText("No Save File Found.", 2f));
            return;
        }

        SaveFile saveFile;

        // 1) Read and deserialize the save file from disk.
        try
        {
            var json = File.ReadAllText(SavePath);
            saveFile = JsonUtility.FromJson<SaveFile>(json);

            if (saveFile == null)
            {
                Debug.LogError("[SaveManager] FromJson returned null. Save file may be invalid.");
                return;
            }

            if (verboseLogging)
                Log($"Loaded JSON:\n{json}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Failed to read/parse save file at {SavePath}\n{ex}");
            return;
        }

        // 2) Calculate elapsed real time since the last save, clamped to maxCatchupHours.
        var utcNow = DateTime.UtcNow;
        var savedUtc = new DateTime(saveFile.lastQuitUtcTicks, DateTimeKind.Utc);

        var elapsed = utcNow - savedUtc;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        var maxCatchup = TimeSpan.FromHours(maxCatchupHours);
        if (elapsed > maxCatchup)
            elapsed = maxCatchup;

        // 3) Index scene saveables by GUID for fast lookup.
        var saveableLookup = BuildSaveableLookup();

        // 4) Spawn missing saveables using prefabKey for any record not already in the scene.
        int spawnedCount = 0;

        foreach (var record in saveFile.records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.id))
                continue;

            if (saveableLookup.ContainsKey(record.id))
                continue;

            var spawned = SpawnFromRecord(record, saveableLookup);
            if (spawned != null && !string.IsNullOrWhiteSpace(spawned.PersistentGuid))
            {
                saveableLookup[spawned.PersistentGuid] = spawned;
                spawnedCount++;
            }
            else
            {
                // Not necessarily an error (scene-authored object could be missing), but good to know.
                LogWarn($"Missing saveable id={record.id}, type={record.type}, prefabKey={record.prefabKey} (not spawnable or prefab not registered).");
            }
        }

        // 5) Build context with elapsed time and a resolver so saveables can link to each other by GUID.
        var context = new SaveContext
        {
            UtcNow = utcNow,
            Elapsed = elapsed,
            ResolveById = id => (id != null && saveableLookup.TryGetValue(id, out var s)) ? s : null
        };

        // 6) Sort records by load priority, then restore each saveable in order.
        saveFile.records.Sort((a, b) => GetRecordPriority(a, saveableLookup).CompareTo(GetRecordPriority(b, saveableLookup)));

        int restored = 0;

        foreach (var record in saveFile.records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.id))
                continue;

            if (saveableLookup.TryGetValue(record.id, out var saveable))
            {
                saveable.RestoreState(record, context);
                restored++;
            }
            else
            {
                LogWarn($"Still missing after spawn: id={record.id}, type={record.type}");
            }
        }

        StartCoroutine(ShowDebugText("Load Complete!", 0.4f));
        Log($"Load complete. Spawned={spawnedCount}, Restored={restored}, elapsed={elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// Deletes the save file from persistent storage. Exposed as a context menu item for editor use.
    /// </summary>
    [ContextMenu("DELETE SAVE FILE")]
    public void DeleteSave()
    {
        if (!File.Exists(SavePath))
        {
            LogWarn($"No save file to delete at: {SavePath}");
            return;
        }

        File.Delete(SavePath);
        StartCoroutine(ShowDebugText("Deleted Old Save!", 2f));
        Log($"Deleted save file: {SavePath}");
    }
    #endregion

    #region Internal Helpers
    /// <summary>
    /// Builds a GUID-to-ISaveable dictionary from all currently registered saveables,
    /// used during load to match save records to in-scene objects.
    /// </summary>
    private Dictionary<string, ISaveable> BuildSaveableLookup()
    {
        var lookup = new Dictionary<string, ISaveable>();

        foreach (var saveable in registeredSaveables)
        {
            if (saveable == null)
                continue;

            var guid = saveable.PersistentGuid;
            if (string.IsNullOrWhiteSpace(guid))
                continue;

            lookup[guid] = saveable;
        }

        return lookup;
    }

    /// <summary>
    /// Returns a human-readable name for a saveable, using the GameObject name if available,
    /// or the type name as a fallback for non-Component saveables.
    /// </summary>
    private static string GetSaveableName(ISaveable saveable)
    {
        if (saveable is Component component)
            return component.name;

        return saveable.GetType().Name;
    }
    
    /// <summary>
    /// Looks up the load priority of the saveable matching the given record.
    /// Returns a default priority of 100 if the record is invalid or unmapped.
    /// </summary>
    private int GetRecordPriority(SaveRecord record, Dictionary<string, ISaveable> lookup)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.id))
            return 100;

        if (lookup.TryGetValue(record.id, out var saveable) && saveable != null)
            return saveable.LoadPriority;

        return 100;
    }

    /// <summary>
    /// Converts the inspector spawn registry list into a dictionary for fast prefabKey lookups at load time.
    /// </summary>
    private void BuildSpawnLookup()
    {
        spawnLookup = new Dictionary<string, GameObject>();

        foreach (var e in spawnRegistry)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.prefabKey) || e.prefab == null)
                continue;

            spawnLookup[e.prefabKey] = e.prefab;
        }
    }

    /// <summary>
    /// Instantiates the prefab registered under the record's prefabKey, applies any saved transform,
    /// stamps the persistent GUID onto the spawned ISaveable, and returns it.
    /// Returns null if the record has no prefabKey or the prefab is not in the spawn registry.
    /// </summary>
    private ISaveable SpawnFromRecord(SaveRecord record, Dictionary<string, ISaveable> saveableLookup)
    {
        if (record == null)
            return null;

        if (string.IsNullOrWhiteSpace(record.prefabKey))
            return null;

        if (spawnLookup == null)
            BuildSpawnLookup();

        // Abort if no matching prefab is registered for this key.
        if (!spawnLookup.TryGetValue(record.prefabKey, out var prefab) || prefab == null)
            return null;

        // Instantiate without a parent first, then apply correct hierarchy below.
        var go = Instantiate(prefab);

        if (record.transform != null)
            record.transform.ApplyTo(go.transform);

        // Slot-bound objects (structures, flora) — parent to the slot's transform and centre on it.
        if (!string.IsNullOrWhiteSpace(record.parentGuid) &&
            saveableLookup != null &&
            saveableLookup.TryGetValue(record.parentGuid, out var parentSaveable) &&
            parentSaveable is Slot slot)
        {
            go.transform.SetParent(slot.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
        }
        // Free-standing dynamic objects (fauna) — parent to the biome root so renderers
        // are toggled correctly when the player switches biomes.
        else if (BiomeManager.Instance != null)
        {
            var occupant = go.GetComponent<IBiomeOccupant>();
            if (occupant != null)
            {
                var biomeData = BiomeManager.Instance.GetBiomeByType(occupant.HomeBiome);
                if (biomeData?.rootObject != null)
                    go.transform.SetParent(biomeData.rootObject.transform, worldPositionStays: true);
            }
        }

        // Retrieve the ISaveable component and stamp the GUID so it can be matched to its record.
        var saveable = go.GetComponent<ISaveable>();
        if (saveable == null)
        {
            LogWarn($"Spawned prefabKey={record.prefabKey} but it has no ISaveable component.");
            return null;
        }
        
        if (!string.IsNullOrWhiteSpace(record.id))
            saveable.SetPersistentGuid(record.id);

        return saveable;
    }
    
    private void FlushToDisk()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SyncFilesystem();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void SyncFilesystem();
#endif
    #endregion

    #region Debug Functions
    /// <summary>
    /// Deletes the save file and reloads the active scene. Guards against being called more than once
    /// during the same session using the hasQuit flag.
    /// </summary>
    public void DeleteAndRestartScene()
    {
        if(hasQuit)
            return;
        
        hasQuit = true;
        StartCoroutine(DeleteAndRestartRoutine());
    }

    /// <summary>
    /// Coroutine that deletes the save file, waits briefly, then reloads the active scene.
    /// </summary>
    private IEnumerator DeleteAndRestartRoutine()
    {
        DeleteSave();
        
        yield return new WaitForSeconds(3f);
        
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// Writes a prefixed info message to the Unity console.
    /// </summary>
    private void Log(string msg)
    {
        Debug.Log($"[SaveManager] {msg}");
    }

    /// <summary>
    /// Writes a prefixed warning message to the Unity console, with an optional context object for scene highlighting.
    /// </summary>
    private void LogWarn(string msg, UnityEngine.Object context = null)
    {
        Debug.LogWarning($"[SaveManager] {msg}", context);
    }

    /// <summary>
    /// Briefly displays a message on the debug UI overlay, then hides it after the given duration.
    /// </summary>
    private IEnumerator ShowDebugText(string msg, float duration)
    {
        if (debugText == null)
            yield break;

        debugText.gameObject.SetActive(true);
        debugText.text = msg;
        yield return new WaitForSeconds(duration);
        debugText.gameObject.SetActive(false);
    }
    #endregion
}