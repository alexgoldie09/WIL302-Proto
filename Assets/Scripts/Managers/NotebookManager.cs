using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A single notebook entry created when a quest is picked up or completed.
/// </summary>
[Serializable]
public class NotebookEntry
{
    public string title;
    public string body;
    public bool   isReward;

    public NotebookEntry(string title, string body, bool isReward)
    {
        this.title    = title;
        this.body     = body;
        this.isReward = isReward;
    }
}

/// <summary>
/// Save data for NotebookManager.
/// </summary>
[Serializable]
public class NotebookManagerData
{
    public List<NotebookEntry> entries = new List<NotebookEntry>();
}

/// <summary>
/// Stores all notebook entries and persists them across sessions.
/// Loads at priority 2 so NotebookUI can populate on Start before quests fire.
/// </summary>
public class NotebookManager : SaveableBehaviour<NotebookManagerData>
{
    public static NotebookManager Instance { get; private set; }

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired whenever a new entry is added. NotebookUI subscribes to refresh.</summary>
    public event Action<NotebookEntry> OnEntryAdded;

    // ── Private state ─────────────────────────────────────────────────────────
    private List<NotebookEntry> _entries = new List<NotebookEntry>();

    // ── SaveableBehaviour ─────────────────────────────────────────────────────
    public override string RecordType   => "NotebookManager";
    public override int    LoadPriority => 2;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>Creates a new entry, stores it, and fires OnEntryAdded.</summary>
    public void AddEntry(string title, string body, bool isReward)
    {
        var entry = new NotebookEntry(title, body, isReward);
        _entries.Add(entry);
        OnEntryAdded?.Invoke(entry);
        Debug.Log($"[NotebookManager] Entry added: '{title}' (reward: {isReward})");
    }

    /// <summary>Returns all entries as a read-only list.</summary>
    public IReadOnlyList<NotebookEntry> GetEntries() => _entries.AsReadOnly();

    // ── SaveableBehaviour ─────────────────────────────────────────────────────
    protected override NotebookManagerData BuildData() => new NotebookManagerData
    {
        entries = new List<NotebookEntry>(_entries)
    };

    protected override void ApplyData(NotebookManagerData data, SaveContext context)
    {
        _entries = data.entries ?? new List<NotebookEntry>();
        Debug.Log($"[NotebookManager] Restored {_entries.Count} entries.");
    }
}