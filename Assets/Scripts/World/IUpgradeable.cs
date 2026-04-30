/// <summary>
/// Implemented by any MonoBehaviour that supports object-level tier upgrades —
/// functional structures, flora, and fauna that have improved Tier 2 behaviour.
///
/// Classes that do NOT need this interface:
/// - Slot — tier activation handled automatically via OnBiomeTierChanged
/// - StoreFrontManager — catalogue merge handled automatically via OnBiomeTierChanged
/// - BiomeData mobile fauna cap — set directly by UpgradeManager via SetBiomeTier
///
/// Implementation pattern:
///   1. Add Tier 2 serialized fields in the Inspector (e.g. tier2Radius, tier2Capacity).
///   2. Implement UpgradeTypeId returning a unique string matching the UpgradeDefinition asset.
///   3. Implement ApplyUpgrade to swap active fields to their Tier 2 values.
///
/// UpgradeManager applies upgrades globally when the biome tier changes, so these methods will be called on all registered instances of IUpgradeable.
/// </summary>
public interface IUpgradeable
{
    /// <summary>
    /// Unique identifier matching the UpgradeDefinition.upgradeTypeId field.
    /// Used by UpgradeManager to find all objects affected by a given upgrade.
    /// Should be a stable string e.g. "WaterSprinkler", "EggCoop", "Crop".
    /// </summary>
    public string UpgradeTypeId { get; }

    /// <summary>
    /// Called by UpgradeManager on all registered instances when an upgrade is applied.
    /// Swap active fields to their Tier 2 values here.
    /// The UpgradeDefinition is passed in case the object needs to read upgrade metadata
    /// (e.g. which biome the upgrade belongs to) but most implementations will ignore it
    /// and just apply their own pre-serialized Tier 2 values.
    /// </summary>
    public void ApplyUpgrade(UpgradeDefinition upgrade);
}