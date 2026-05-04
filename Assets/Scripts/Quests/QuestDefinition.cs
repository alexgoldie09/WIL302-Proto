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
    ReopenApp,
    UnlockBiome,
    FeedAnimal,
    CollectAnimalItem
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
    
    [Header("Notification")]
    [Tooltip("Whether to show a notification when this quest is picked up or completed. If false, the quest will still be added to the notebook but no popup will appear.")]
    public bool sendsQuestNotification = true;
    [Tooltip("Whether to add an entry to the notebook when this quest is picked up or completed. If false, popups will still appear but the quest won't be recorded in the notebook.")]
    public bool sendsNotebookNotification = true;

    [Header("Objective")]
    public QuestObjectiveType objectiveType;

    [Header("Biome Unlock")]
    [Tooltip("The biome to unlock when this quest completes. Only used when objectiveType is UnlockBiome.")]
    public BiomeManager.BiomeType targetBiome;
    
    [Header("Animal Objective")]
    [Tooltip("The type of animal that must be fed. Only used when objectiveType is FeedAnimal or CollectAnimalItem. " +
             "Matches the animal's RecordType (e.g. \"Duck\", \"Cow\", \"Sheep\", \"Clam\"). Leave empty to match any animal.")]
    public string targetAnimalType;
    
    [Header("Item Objective")]
    [Tooltip("Item name to match against for BuyItem, PlaceItem, WaterCrop, HarvestItem, CraftItem, FeedAnimal, CollectAnimalItem. " +
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