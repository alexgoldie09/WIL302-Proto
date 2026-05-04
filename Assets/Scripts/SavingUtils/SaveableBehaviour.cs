using UnityEngine;

/// <summary>
/// Base class for saveable MonoBehaviours.
/// Enforces a strongly-typed payload via BuildData / ApplyData.
/// </summary>
[DisallowMultipleComponent]
public abstract class SaveableBehaviour<TData> : MonoBehaviour, ISaveable
{
    [Header("Persistent ID Settings")]
    [SerializeField, Tooltip("Stable unique identifier used for save/load mapping.")]
    private string persistentGuid;
    [SerializeField, Tooltip("If yes, the object remains in the scene at all times and is given a default a GUID.")]
    private bool isStatic = false;
    
    [Header("Save Record Settings")]
    [SerializeField, Tooltip("If set, SaveManager can spawn this object when missing.")]
    protected string prefabKey = string.Empty;
    [SerializeField, Tooltip("If true, save/restore this object's transform.")]
    protected bool saveTransform = false;

    #region ISaveable Properties
    public string PersistentGuid => persistentGuid;

    /// <summary>Type key used in the save file, e.g., "Crop", "Animal".</summary>
    public abstract string RecordType { get; }

    /// <summary>Lower loads first (anchors/containers before children).</summary>
    public abstract int LoadPriority { get; }
    #endregion
    
    #region Unity Lifecycle
    /// <summary>
    /// Registers this GameObject to the SaveManager when activated / instantiated.
    /// </summary>
    protected virtual void OnEnable()
    {
        SaveManager.RegisterSaveable(this);
    }

    /// <summary>
    /// Deregisters this GameObject from the SaveManager when deactivated / destroyed.
    /// </summary>
    protected virtual void OnDisable()
    {
        SaveManager.UnregisterSaveable(this);
    }
    #endregion
    
    #region GUID Functions
    /// <summary>
    /// Assigns the persistent GUID used to map this object to a save record.
    /// </summary>
    public void SetPersistentGuid(string guid)
    {
        persistentGuid = guid;
    }

    /// <summary>
    /// Creates a GUID if one is missing so this object can be tracked across sessions.
    /// </summary>
    protected void EnsurePersistentGuid()
    {
        if (!string.IsNullOrWhiteSpace(persistentGuid))
            return;

        persistentGuid = System.Guid.NewGuid().ToString();
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Keeps static objects pre-seeded with a GUID while editing.
    /// </summary>
    private void OnValidate()
    {
        if (!isStatic)
            return;

        EnsurePersistentGuid();
    }
#endif
    #endregion

    #region Save and Load Functions
    /// <summary>Build a serializable payload for this saveable.</summary>
    protected abstract TData BuildData();

    /// <summary>Apply a previously built payload back onto this saveable.</summary>
    protected abstract void ApplyData(TData data, SaveContext context);
    
    /// <summary>
    /// Override to return the persistent GUID of this object's parent saveable
    /// (e.g. the Slot that contains this flora or structure). Used by SaveManager
    /// to re-parent spawned objects into the correct hierarchy on load.
    /// </summary>
    protected virtual string GetParentGuid() => string.Empty;

    /// <summary>
    /// Captures common save fields and serializes the derived payload into JSON.
    /// </summary>
    public virtual SaveRecord CaptureState()
    {
        var data = BuildData();

        return new SaveRecord
        {
            id         = PersistentGuid,
            type       = RecordType,
            prefabKey  = prefabKey,
            parentGuid = GetParentGuid(),
            transform  = saveTransform ? TransformData.FromTransform(transform) : null,
            jsonData   = JsonUtility.ToJson(data)
        };
    }

    /// <summary>
    /// Restores common save fields and deserializes the derived payload from JSON.
    /// </summary>
    public virtual void RestoreState(SaveRecord record, SaveContext context)
    {
        if (record == null)
            return;

        // Apply ID
        if (!string.IsNullOrWhiteSpace(record.id))
            SetPersistentGuid(record.id);

        // Apply transform if present
        if (record.transform != null)
            record.transform.ApplyTo(transform);

        // Apply payload
        if (!string.IsNullOrWhiteSpace(record.jsonData))
        {
            var data = DeserializeData(record.jsonData);
            ApplyData(data, context);
        }
    }
    
    /// <summary>
    /// Override in concrete subclasses whose data model extends TData (e.g. DuckData : FaunaData)
    /// so the deserialized object has the correct runtime type and "data is SubclassData"
    /// checks in ApplyData succeed.
    /// </summary>
    protected virtual TData DeserializeData(string json) => JsonUtility.FromJson<TData>(json);
    #endregion
}