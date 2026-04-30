using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Save data for QuestManager.
/// </summary>
[Serializable]
public class QuestManagerData
{
    public int activeQuestIndex;
    public int currentProgress;
    public List<int> completedQuestNumbers = new List<int>();
}

/// <summary>
/// Manages quest activation, progress tracking, completion, and save/load.
/// Loads at priority 3 so it restores before other systems subscribe to its events.
/// </summary>
public class QuestManager : SaveableBehaviour<QuestManagerData>
{
    public static QuestManager Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Quests")]
    [Tooltip("All Quest scene objects in sequential order matching questNumber.")]
    [SerializeField] private List<Quest> allQuests = new List<Quest>();

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<Quest>      OnQuestPickedUp;
    public event Action<Quest>      OnQuestCompleted;
    public event Action<Quest, int> OnQuestProgressUpdated;

    // ── Private state ─────────────────────────────────────────────────────────
    private int  _activeQuestIndex  = 0;
    private int  _currentProgress   = 0;
    private bool _restoringFromSave = false;

    private Quest ActiveQuest => (_activeQuestIndex >= 0 && _activeQuestIndex < allQuests.Count)
        ? allQuests[_activeQuestIndex] : null;

    // ── SaveableBehaviour ─────────────────────────────────────────────────────
    public override string RecordType   => "QuestManager";
    public override int    LoadPriority => 3;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Deactivate all quest world objects — ActivateCurrentQuest enables the right one.
        foreach (var q in allQuests)
            if (q != null) q.gameObject.SetActive(false);
    }

    // No base.Start() — SaveableBehaviour has no Start.
    private void Start()
    {
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied += HandleUpgradeApplied;

        ActivateCurrentQuest();
    }

    private void OnDestroy()
    {
        if (UpgradeManager.Instance != null)
            UpgradeManager.Instance.OnUpgradeApplied -= HandleUpgradeApplied;
    }

    // ── Activation ────────────────────────────────────────────────────────────
    public void ActivateCurrentQuest()
    {
        if (_activeQuestIndex >= allQuests.Count)
        {
            Debug.Log("[QuestManager] All quests completed.");
            return;
        }

        Quest quest = ActiveQuest;
        if (quest == null) return;

        quest.gameObject.SetActive(true);
        _currentProgress = 0;

        Debug.Log($"[QuestManager] Activated quest {_activeQuestIndex}: {quest.Definition?.questTitle}");

        // ReopenApp — auto-complete immediately if restoring from save with this quest active.
        if (_restoringFromSave && quest.Definition?.objectiveType == QuestObjectiveType.ReopenApp)
        {
            quest.MarkPickedUp();
            CompleteQuest();
        }
    }

    // ── Pick Up ───────────────────────────────────────────────────────────────
    public void PickUpQuest(Quest quest)
    {
        if (quest != ActiveQuest)
        {
            Debug.LogWarning("[QuestManager] PickUpQuest called on a quest that isn't active.");
            return;
        }

        if (quest.IsPickedUp) return;

        quest.MarkPickedUp();
        OnQuestPickedUp?.Invoke(quest);

        NotebookManager.Instance?.AddEntry(
            quest.Definition.questTitle,
            quest.Definition.questDescription,
            isReward: false
        );

        Debug.Log($"[QuestManager] Quest picked up: {quest.Definition.questTitle}");
    }

    // ── Progress ──────────────────────────────────────────────────────────────
    public void RecordProgress(QuestObjectiveType type, string itemName, int amount)
    {
        Quest quest = ActiveQuest;
        if (quest == null)                          return;
        if (!quest.IsPickedUp)                      return;
        if (quest.IsCompleted)                      return;
        if (quest.Definition.objectiveType != type) return;

        // Empty targetItemName matches any; otherwise must match exactly (case-insensitive).
        string target = quest.Definition.targetItemName;
        if (!string.IsNullOrEmpty(target) &&
            !string.Equals(target, itemName, StringComparison.OrdinalIgnoreCase))
            return;

        _currentProgress += amount;
        OnQuestProgressUpdated?.Invoke(quest, _currentProgress);

        Debug.Log($"[QuestManager] Progress {_currentProgress}/{quest.Definition.requiredAmount} ({type}: {itemName})");

        if (_currentProgress >= quest.Definition.requiredAmount)
            CompleteQuest();
    }

    public void RecordUpgradeProgress()
    {
        Quest quest = ActiveQuest;
        if (quest == null)                                                     return;
        if (!quest.IsPickedUp)                                                 return;
        if (quest.IsCompleted)                                                 return;
        if (quest.Definition.objectiveType != QuestObjectiveType.ApplyUpgrade) return;

        CompleteQuest();
    }

    // ── Completion ────────────────────────────────────────────────────────────
    public void CompleteQuest()
    {
        Quest quest = ActiveQuest;
        if (quest == null || quest.IsCompleted) return;

        quest.MarkCompleted();

        // Give reward item directly from the QuestDefinition reference — no registry needed.
        if (quest.Definition.hasItemReward && quest.Definition.rewardItem != null)
        {
            PlayerInventory.Instance?.Add(quest.Definition.rewardItem, quest.Definition.rewardItemAmount);
        }
        else if (quest.Definition.hasItemReward && quest.Definition.rewardItem == null)
        {
            Debug.LogWarning($"[QuestManager] Quest '{quest.Definition.questTitle}' hasItemReward is true but rewardItem is not assigned.");
        }

        NotebookManager.Instance?.AddEntry(
            quest.Definition.questTitle,
            quest.Definition.rewardDialogue,
            isReward: true
        );

        OnQuestCompleted?.Invoke(quest);
        Debug.Log($"[QuestManager] Quest completed: {quest.Definition.questTitle}");

        StartCoroutine(AdvanceQuestCoroutine());
    }

    private IEnumerator AdvanceQuestCoroutine()
    {
        yield return new WaitForSeconds(1.5f);

        _activeQuestIndex++;

        if (_activeQuestIndex >= allQuests.Count)
        {
            Debug.Log("[QuestManager] All quests completed — no further activation.");
            yield break;
        }

        ActivateCurrentQuest();
    }

    // ── Event Handlers ────────────────────────────────────────────────────────
    private void HandleUpgradeApplied(UpgradeDefinition upgrade)
    {
        RecordUpgradeProgress();
    }

    // ── SaveableBehaviour ─────────────────────────────────────────────────────
    protected override QuestManagerData BuildData()
    {
        var completed = new List<int>();
        foreach (var q in allQuests)
            if (q != null && q.IsCompleted)
                completed.Add(q.Definition.questNumber);

        return new QuestManagerData
        {
            activeQuestIndex      = _activeQuestIndex,
            currentProgress       = _currentProgress,
            completedQuestNumbers = completed
        };
    }

    protected override void ApplyData(QuestManagerData data, SaveContext context)
    {
        _activeQuestIndex = data.activeQuestIndex;
        _currentProgress  = data.currentProgress;

        var completedSet = new HashSet<int>(data.completedQuestNumbers);
        foreach (var q in allQuests)
        {
            if (q != null && completedSet.Contains(q.Definition.questNumber))
                q.MarkCompletedSilent();
        }

        _restoringFromSave = true;
        ActivateCurrentQuest();
        _restoringFromSave = false;
    }

    // ── Debug ─────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Debug/Complete Active Quest")]
    private void DebugCompleteActiveQuest() => CompleteQuest();

    [ContextMenu("Debug/Log Quest State")]
    private void DebugLogQuestState()
    {
        Quest q = ActiveQuest;
        Debug.Log($"[QuestManager] Index: {_activeQuestIndex} | Progress: {_currentProgress} | " +
                  $"Quest: {q?.Definition?.questTitle ?? "none"} | " +
                  $"PickedUp: {q?.IsPickedUp} | Completed: {q?.IsCompleted}");
    }

    [ContextMenu("Debug/Skip To Quest 0")]
    private void DebugSkipToQuest0() => DebugSkipToQuest(0);

    private void DebugSkipToQuest(int index)
    {
        _activeQuestIndex = Mathf.Clamp(index, 0, allQuests.Count - 1);
        _currentProgress  = 0;
        ActivateCurrentQuest();
        Debug.Log($"[QuestManager] Skipped to quest index {_activeQuestIndex}.");
    }

    [ContextMenu("Debug/Reset All Quests")]
    private void DebugResetAllQuests()
    {
        foreach (var q in allQuests)
            if (q != null) q.gameObject.SetActive(false);

        _activeQuestIndex = 0;
        _currentProgress  = 0;
        ActivateCurrentQuest();
        Debug.Log("[QuestManager] All quests reset.");
    }
#endif
}