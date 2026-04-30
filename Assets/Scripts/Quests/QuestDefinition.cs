using UnityEngine;

/// <summary>
/// Objective types supported by the quest system.
/// </summary>
public enum QuestObjectiveType
{
    BuyItem,
    PlaceItem,
    WaterCrop,
    HarvestItem,
    CraftItem,
    ApplyUpgrade,
    ReopenApp
}

/// <summary>
/// Data asset driving a single quest. Create via Assets > Create > SuperFarm > Quest Definition.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Quest Definition", fileName = "QuestDefinition")]
public class QuestDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Determines activation order. Must be unique and sequential.")]
    public int questNumber;

    [Tooltip("Shown in popup header and notebook entry header.")]
    public string questTitle;

    [Tooltip("What the player must do. Shown on pickup popup and stored in notebook.")]
    [TextArea(2, 4)]
    public string questDescription;

    [Tooltip("Shown on the world Quest GameObject sprite renderer and in popups.")]
    public Sprite questIcon;

    [Header("Objective")]
    public QuestObjectiveType objectiveType;

    [Tooltip("Item name to match against for BuyItem, PlaceItem, WaterCrop, HarvestItem, CraftItem. " +
             "Leave empty to match any item of that objective type.")]
    public string targetItemName;

    [Tooltip("How many times the action must occur before the quest completes.")]
    public int requiredAmount = 1;

    [Header("Reward")]
    [Tooltip("Whether this quest gives an item on completion.")]
    public bool hasItemReward;

    [Tooltip("The item to give on completion. Only used if hasItemReward is true.")]
    public ItemDefinition rewardItem;

    [Tooltip("Quantity of reward item added to PlayerInventory on completion.")]
    public int rewardItemAmount = 1;

    [Tooltip("Superannuation explanation text shown in the completion popup and stored in notebook.")]
    [TextArea(2, 5)]
    public string rewardDialogue;
}