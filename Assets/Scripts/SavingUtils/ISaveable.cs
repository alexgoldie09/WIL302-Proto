/// <summary>
/// Defines which objects can serialize and restore their state through the save system.
/// </summary>
public interface ISaveable
{
    /// <summary>Stable ID for save/load mapping.</summary>
    public string PersistentGuid { get; }
    
    /// <summary>Assign the stable ID used for save/load mapping.</summary>
    public void SetPersistentGuid(string guid);

    /// <summary>Type key used in the save file, e.g., "Plot" or "Crop".</summary>
    public string RecordType { get; }

    /// <summary>
    /// Load order priority. Lower loads first (anchors/containers before children).
    /// Example: Plot = 0, Crop = 10, Player = 20.
    /// </summary>
    public int LoadPriority { get; }

    /// <summary>Return a serializable record representing this object's state.</summary>
    public SaveRecord CaptureState();

    /// <summary>Apply a previously captured state record.</summary>
    public void RestoreState(SaveRecord record, SaveContext context);
}