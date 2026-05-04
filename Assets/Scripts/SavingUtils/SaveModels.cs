using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Root JSON model that stores metadata and all save records for a single save slot/file.
/// </summary>
[Serializable]
public class SaveFile
{
    public int version = 1;
    public long lastQuitUtcTicks;
    public List<SaveRecord> records = new();
}

/// <summary>
/// Generic serialized object record containing identity, type, optional transform, and payload JSON.
/// </summary>
[Serializable]
public class SaveRecord
{
    public string id;
    public string type;       // "Plot", "Crop", etc.
    public string prefabKey;  // used for spawning missing objects later
    public TransformData transform; // transform information
    public string parentGuid; // GUID of parent saveable (e.g. Slot for flora/structures, empty for fauna)
    public string jsonData;   // type-specific payload serialized as JSON
}

/// <summary>
/// Serializable transform snapshot used to save and restore a world position/rotation.
/// </summary>
[Serializable]
public class TransformData
{
    public float posX, posY;
    public float rotZ;

    /// <summary>
    /// Creates a serializable transform snapshot from a runtime transform.
    /// </summary>
    public static TransformData FromTransform(Transform t)
    {
        var p = t.position;
        return new TransformData
        {
            posX = p.x,
            posY = p.y,
            rotZ = t.eulerAngles.z
        };
    }

    /// <summary>
    /// Creates a serializable transform snapshot from a runtime transform.
    /// </summary>
    public void ApplyTo(Transform t)
    {
        t.position = new Vector3(posX, posY, t.position.z);
        t.rotation = Quaternion.Euler(0f, 0f, rotZ);
    }
}

/// <summary>
/// Shared context passed into restore calls, including time info and ID-based object resolution.
/// </summary>
public class SaveContext
{
    public DateTime UtcNow;
    public TimeSpan Elapsed;

    /// <summary>Lookup other saveables by persistent GUID.</summary>
    public Func<string, ISaveable> ResolveById;
}