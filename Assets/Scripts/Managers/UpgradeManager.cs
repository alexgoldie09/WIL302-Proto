using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages upgrade application and tracks which upgrades have been applied.
///
/// Responsibilities:
///   - Validates biome tier prerequisites before allowing application.
///   - Applies the upgrade effect based on UpgradeType.
///   - Tracks which upgrades have been permanently applied.
///   - Fires OnUpgradeApplied for UI to react.
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public static UpgradeManager Instance { get; private set; }

    [Header("Available Upgrades")]
    [SerializeField, Tooltip("All upgrade definitions available in this session. " +
                             "Order determines display order in the upgrades tab.")]
    private List<UpgradeDefinition> allUpgrades = new();

    /// <summary>
    /// Fired when an upgrade is successfully applied.
    /// StoreFrontUI subscribes to refresh the upgrades tab.
    /// </summary>
    public event Action<UpgradeDefinition> OnUpgradeApplied;

    /// <summary>Upgrades that have been permanently applied this session.</summary>
    private readonly HashSet<string> _appliedUpgrades = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    #region Public API

    /// <summary>
    /// Returns all upgrade definitions registered with this manager.
    /// Used by StoreFrontUI to build the upgrades tab.
    /// </summary>
    public List<UpgradeDefinition> GetAllUpgrades() => new(allUpgrades);

    /// <summary>
    /// Returns true if the given upgrade has already been purchased and applied.
    /// Applied upgrades cannot be purchased again.
    /// </summary>
    public bool IsUpgradeApplied(UpgradeDefinition upgrade)
    {
        if (upgrade == null) return false;
        return _appliedUpgrades.Contains(upgrade.UpgradeName);
    }

    /// <summary>
    /// Returns true if the biome tier prerequisite is met and the upgrade
    /// has not already been applied.
    /// Does NOT check coin or material cost — that is StoreFrontManager's responsibility.
    /// </summary>
    public bool MeetsPrerequisites(UpgradeDefinition upgrade)
    {
        if (upgrade == null) return false;
        if (IsUpgradeApplied(upgrade)) return false;

        int currentTier = BiomeManager.Instance?.GetBiomeTier(upgrade.TargetBiome) ?? 1;
        return currentTier >= upgrade.RequiredBiomeTier;
    }

    /// <summary>
    /// Applies the upgrade effect and marks it as applied.
    /// Called by StoreFrontManager after successfully deducting coin and material cost.
    /// Returns false if prerequisites are not met or upgrade is already applied.
    /// </summary>
    public bool ApplyUpgrade(UpgradeDefinition upgrade)
    {
        if (!MeetsPrerequisites(upgrade))
        {
            Debug.LogWarning($"[UpgradeManager] Cannot apply '{upgrade?.UpgradeName}' " +
                             $"— prerequisites not met or already applied.");
            return false;
        }

        switch (upgrade.UpgradeType)
        {
            case UpgradeType.BiomeSlotUnlock:
                ApplyBiomeSlotUnlock(upgrade);
                break;

            case UpgradeType.BiomeFaunaCapIncrease:
                ApplyFaunaCapIncrease(upgrade);
                break;

            case UpgradeType.ObjectTierUpgrade:
                ApplyObjectTierUpgrade(upgrade);
                break;
        }

        _appliedUpgrades.Add(upgrade.UpgradeName);
        QuestManager.Instance?.RecordUpgradeProgress();
        OnUpgradeApplied?.Invoke(upgrade);

        Debug.Log($"[UpgradeManager] Applied upgrade: {upgrade.UpgradeName}.");
        return true;
    }

    #endregion

    #region Private Effects

    /// <summary>
    /// Raises the biome tier via BiomeManager.
    /// Fires OnBiomeTierChanged which Slot and StoreFrontManager subscribe to —
    /// Tier 2 slots self-activate and the Tier 2 catalogue merges automatically.
    /// </summary>
    private void ApplyBiomeSlotUnlock(UpgradeDefinition upgrade)
    {
        if (BiomeManager.Instance == null) return;

        int currentTier = BiomeManager.Instance.GetBiomeTier(upgrade.TargetBiome);
        BiomeManager.Instance.SetBiomeTier(upgrade.TargetBiome, currentTier + 1);

        Debug.Log($"[UpgradeManager] {upgrade.TargetBiome} raised to Tier {currentTier + 1}.");
    }

    /// <summary>
    /// Increases the fauna cap on the target biome's BiomeData.
    /// Checked at spawn time when placing or hatching fauna.
    /// </summary>
    private void ApplyFaunaCapIncrease(UpgradeDefinition upgrade)
    {
        if (BiomeManager.Instance == null) return;

        var biomeData = BiomeManager.Instance.GetBiomeByType(upgrade.TargetBiome);
        if (biomeData == null) return;

        biomeData.maxFauna += upgrade.FaunaCapIncrease;

        Debug.Log($"[UpgradeManager] {upgrade.TargetBiome} maxFauna increased to " +
                  $"{biomeData.maxFauna}.");
    }

    /// <summary>
    /// Finds all IUpgradeable occupants on the target biome whose UpgradeTypeId
    /// matches this upgrade and calls ApplyUpgrade on each.
    /// Global — every registered instance of the type is upgraded simultaneously.
    /// </summary>
    private void ApplyObjectTierUpgrade(UpgradeDefinition upgrade)
    {
        if (BiomeManager.Instance == null) return;
        if (string.IsNullOrEmpty(upgrade.UpgradeTypeId)) return;

        var upgradeables = BiomeManager.Instance
            .GetOccupantsOfType<IUpgradeable>(upgrade.TargetBiome);

        int count = 0;
        foreach (var upgradeable in upgradeables)
        {
            if (upgradeable.UpgradeTypeId != upgrade.UpgradeTypeId) continue;
            upgradeable.ApplyUpgrade(upgrade);
            count++;
        }

        Debug.Log($"[UpgradeManager] Applied '{upgrade.UpgradeName}' to " +
                  $"{count} instance(s) of type '{upgrade.UpgradeTypeId}'.");
    }

    #endregion

    #region Debug Methods

#if UNITY_EDITOR
    [ContextMenu("Debug/Log Applied Upgrades")]
    private void DebugLogApplied()
    {
        if (_appliedUpgrades.Count == 0)
        {
            Debug.Log("[UpgradeManager] No upgrades applied.");
            return;
        }

        foreach (var name in _appliedUpgrades)
            Debug.Log($"[UpgradeManager] Applied: {name}");
    }

    [ContextMenu("Debug/Log All Upgrade States")]
    private void DebugLogAll()
    {
        foreach (var upgrade in allUpgrades)
        {
            bool applied = IsUpgradeApplied(upgrade);
            bool prereqs = MeetsPrerequisites(upgrade);
            Debug.Log($"[UpgradeManager] {upgrade.UpgradeName} — " +
                      $"Applied: {applied}, Prerequisites Met: {prereqs}");
        }
    }

    /// <summary>
    /// Force-applies all ObjectTierUpgrade definitions regardless of cost or prerequisites.
    /// Useful for testing IUpgradeable.ApplyUpgrade on registered occupants.
    /// </summary>
    [ContextMenu("Debug/Force Apply All Object Upgrades")]
    private void DebugForceApplyAllObjectUpgrades()
    {
        int count = 0;
        foreach (var upgrade in allUpgrades)
        {
            if (upgrade.UpgradeType != UpgradeType.ObjectTierUpgrade) continue;
            if (IsUpgradeApplied(upgrade))
            {
                Debug.Log($"[UpgradeManager] Skipping '{upgrade.UpgradeName}' — already applied.");
                continue;
            }

            ApplyObjectTierUpgrade(upgrade);
            _appliedUpgrades.Add(upgrade.UpgradeName);
            OnUpgradeApplied?.Invoke(upgrade);
            count++;
        }

        Debug.Log($"[UpgradeManager] Debug: Force-applied {count} object upgrade(s).");
    }
    
    /// <summary>
    /// Force-applies all BiomeSlotUnlock upgrades regardless of cost or prerequisites.
    /// Raises biome tiers and activates Tier 2 slots immediately.
    /// </summary>
    [ContextMenu("Debug/Force Apply All Biome Slot Unlocks")]
    private void DebugForceApplyAllSlotUnlocks()
    {
        int count = 0;
        foreach (var upgrade in allUpgrades)
        {
            if (upgrade.UpgradeType != UpgradeType.BiomeSlotUnlock) continue;
            if (IsUpgradeApplied(upgrade)) continue;

            ApplyBiomeSlotUnlock(upgrade);
            _appliedUpgrades.Add(upgrade.UpgradeName);
            OnUpgradeApplied?.Invoke(upgrade);
            count++;
        }

        Debug.Log($"[UpgradeManager] Debug: Force-applied {count} biome slot unlock(s).");
    }

    /// <summary>
    /// Force-applies all BiomeFaunaCapIncrease upgrades regardless of cost or prerequisites.
    /// </summary>
    [ContextMenu("Debug/Force Apply All Fauna Cap Increases")]
    private void DebugForceApplyAllFaunaCapIncreases()
    {
        int count = 0;
        foreach (var upgrade in allUpgrades)
        {
            if (upgrade.UpgradeType != UpgradeType.BiomeFaunaCapIncrease) continue;
            if (IsUpgradeApplied(upgrade)) continue;

            ApplyFaunaCapIncrease(upgrade);
            _appliedUpgrades.Add(upgrade.UpgradeName);
            OnUpgradeApplied?.Invoke(upgrade);
            count++;
        }

        Debug.Log($"[UpgradeManager] Debug: Force-applied {count} fauna cap increase(s).");
    }

    /// <summary>
    /// Resets all applied upgrades so they can be purchased and tested again.
    /// Does not reverse any effects already applied to objects — use for fresh testing only.
    /// </summary>
    [ContextMenu("Debug/Reset All Applied Upgrades")]
    private void DebugResetAllUpgrades()
    {
        int count = _appliedUpgrades.Count;
        _appliedUpgrades.Clear();
        Debug.Log($"[UpgradeManager] Debug: Reset {count} applied upgrade(s). " +
                  $"Note: effects already applied to objects are NOT reversed.");
    }
#endif

    #endregion
}