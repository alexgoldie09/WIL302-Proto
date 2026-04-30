using UnityEngine;

/// <summary>
/// Concrete flora class representing a harvestable crop (e.g. Carrot, Eggplant).
/// All growth, watering, and harvest logic lives in FloraBase.
/// Crop identifies itself by the ItemDefinition asset assigned in the Inspector.
/// </summary>
public class Crop : FloraBase
{
    enum CropType
    {
        Carrot,
        Pineapple,
        Eggplant
    }
    
    [Header("Crop Output")]
    [SerializeField, Tooltip("The type of crop this is. Used for saving and upgrade application.")]
    private CropType cropType;
    [SerializeField, Tooltip("The item this crop produces on harvest. " +
                             "Determines what crop this is (e.g. Carrot, Eggplant).")]
    private ItemDefinition outputItem;
    [SerializeField, Tooltip("How many of the output item are produced when harvested. " +
                             "Upgrades are applied additively and capped at 10.")]
    private int harvestAmountIncrease = 1;

    #region FloraBase Implementation
    protected override ItemDefinition GetOutputItem() => outputItem;
    
    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        harvestAmount = Mathf.Clamp(harvestAmount + harvestAmountIncrease, 1, 10);
    }
    #endregion
    
    #region SaveableBehaviour
    protected override FloraData BuildData()
    {
        var data = base.BuildData();
        // TODO: Persist outputItem identity via ItemRegistry when save system
        // is fully implemented. For now the outputItem reference is set in the
        // prefab and survives session restarts as long as the prefab is correct.
        return data;
    }

    protected override void ApplyData(FloraData data, SaveContext context)
    {
        base.ApplyData(data, context);
        // TODO: Restore outputItem reference from ItemRegistry using saved name.
    }
    
    public override string RecordType => cropType switch
    {
        CropType.Carrot => "Carrot",
        CropType.Pineapple => "Pineapple",
        CropType.Eggplant => "Eggplant",
        _ => "Unknown"
    };
    
    #endregion
}