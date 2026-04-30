using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the three upgrade types available in the system.
///
/// BiomeSlotUnlock
/// - Activates Tier 2 slots in the target biome by raising biome tier.
/// - Slots with requiredTier = 2 self-activate via OnBiomeTierChanged.
///
/// BiomeFaunaCapIncrease
/// - Increases maxMobileFauna on the target biome's BiomeData.
/// - Controls how many free-roaming animals (Duck, Sheep) can exist.
///
/// ObjectTierUpgrade
/// - Calls ApplyUpgrade on all registered IUpgradeable occupants
///   whose UpgradeTypeId matches this definition's upgradeTypeId.
/// - Used for structures, flora, and fauna with Tier 2 behaviour.
/// </summary>
public enum UpgradeType
{
    BiomeSlotUnlock,
    BiomeFaunaCapIncrease,
    ObjectTierUpgrade
}

/// <summary>
/// ScriptableObject defining a single purchasable upgrade.
///
/// Upgrade types:
///   BiomeSlotUnlock — raises biome tier, activating Tier 2 slots in that biome.
///   BiomeFaunaCapIncrease — raises the mobile fauna cap on a biome.
///   ObjectTierUpgrade — upgrades all registered instances of a specific type globally.
///
/// Prerequisites:
///   requiredBiomeTier — biome must be at or above this tier before purchase is allowed.
///   This allows sequencing e.g. unlock Tier 2 biome before upgrading its structures.
/// </summary>
[CreateAssetMenu(menuName = "SuperFarm/Upgrade Definition", fileName = "Upgrade_")]
public class UpgradeDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField, Tooltip("Display name shown in the upgrades tab e.g. 'Irrigation Upgrade'.")]
    private string upgradeName = "New Upgrade";
    [SerializeField, Tooltip("Description shown in the upgrades tab.")]
    private string description = string.Empty;
    [SerializeField, Tooltip("Icon shown in the upgrades tab.")]
    private Sprite icon;

    [Header("Upgrade Type")]
    [SerializeField, Tooltip("What kind of upgrade this is. Determines how UpgradeManager applies it.")]
    private UpgradeType upgradeType;
    [SerializeField, Tooltip("Target biome for BiomeSlotUnlock and BiomeFaunaCapIncrease upgrades. " +
                             "For ObjectTierUpgrade this filters which biome's occupants are upgraded.")]
    private BiomeManager.BiomeType targetBiome;
    [SerializeField, Tooltip("For ObjectTierUpgrade only — must match IUpgradeable.UpgradeTypeId " +
                             "on the target class e.g. 'WaterSprinkler', 'EggCoop', 'Crop'.")]
    private string upgradeTypeId = string.Empty;
    [SerializeField, Tooltip("For BiomeFaunaCapIncrease — how much to increase maxMobileFauna by.")]
    private int faunaCapIncrease = 2;
    
    [Header("Cost")]
    [SerializeField, Tooltip("Coin cost to purchase this upgrade.")]
    private float coinCost = 30f;
    [SerializeField, Tooltip("Optional material ingredients required alongside the coin cost. " +
                             "Leave empty for coin-only upgrades.")]
    private List<RecipeIngredient> materialCost = new();
    
    [Header("Prerequisites")]
    [SerializeField, Tooltip("The biome must be at or above this tier before this upgrade can be purchased. " +
                             "Set to 1 to allow purchase from the start.")]
    private int requiredBiomeTier = 1;

    #region Accessors
    public string UpgradeName => upgradeName;
    public string Description => description;
    public Sprite Icon => icon;
    public UpgradeType UpgradeType => upgradeType;
    public BiomeManager.BiomeType TargetBiome => targetBiome;
    public string UpgradeTypeId => upgradeTypeId;
    public int FaunaCapIncrease => faunaCapIncrease;
    public float CoinCost => coinCost;
    public List<RecipeIngredient> MaterialCost => materialCost;
    public int RequiredBiomeTier => requiredBiomeTier;
    #endregion
}