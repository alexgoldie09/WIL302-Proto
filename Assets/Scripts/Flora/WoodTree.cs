using System;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;


/// <summary>
/// Serialisable save data for WoodTree — extends FloraData with chop health.
/// </summary>
[Serializable]
public class WoodTreeData : FloraData
{
    public float chopHealth;
}

/// <summary>
/// Concrete flora class representing a harvestable tree.
/// Grows through standard FloraBase stages. At Harvestable the player must use
/// the axe tool to chop it down — each Chop() call reduces health until it hits
/// zero at which point Harvest() is called automatically.
/// Water and growth stat bars are replaced by a chop health bar at Harvestable stage.
/// </summary>
public class WoodTree : FloraBase
{
    enum TreeType
    {
        Wood,
        Apple,
        Orange
    }
    
    [Header("Tree Output")]
    [SerializeField, Tooltip("The type of tree this is. Used for saving and upgrade application.")]
    private TreeType treeType;
    [SerializeField, Tooltip("The item this tree produces when chopped down. " +
                             "Determines what tree this is (e.g. Apple, Wood Log).")]
    private ItemDefinition outputItem;
    [SerializeField, Tooltip("Maximum amount of the output item produced when harvested.")]
    private int maxHarvestAmount = 4;
    [SerializeField, Tooltip("How many of the output item are produced when harvested. " +
                             "Upgrades are applied additively and capped at 10.")]
    private int harvestAmountIncrease = 1;

    [Header("Chop")]
    [SerializeField, Tooltip("Total chop health. Axe reduces this on each Chop() call.")]
    private float maxChopHealth = 5f;
    [SerializeField, Tooltip("World-space Image fill representing remaining chop health. " +
                             "Should be a child of statBarsRoot alongside the other fills.")]
    private Image chopFillImage;
    
    private float _chopHealth;
    
    protected override ItemDefinition GetOutputItem() => outputItem;

    #region  FloraBase Overrides
    protected override void Start()
    {
        base.Start();
        ResetChopHealth();
        OnStageChanged += HandleStageChanged;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        OnStageChanged -= HandleStageChanged;
    }

    private void HandleStageChanged(FloraGrowthStage newStage)
    {
        if (newStage == FloraGrowthStage.Harvestable)
            ResetChopHealth();
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        harvestAmount = Mathf.Clamp(harvestAmount + harvestAmountIncrease, 1, 10);
    }
    #endregion

    #region IHandler Overrides
    public override void OnTapped()
    {
        ShowStats();
    }
    #endregion

    #region Stats Overrides
    protected override void UpdateStatFills()
    {
        // Don't touch water or growth fills while the hoe is active.
        if (IsBeingRemoved) 
            return;
        
        if (Stage == FloraGrowthStage.Harvestable)
        {
            if (WaterFillImage != null)
                WaterFillImage.gameObject.SetActive(false);
            if (GrowthFillImage != null)
                GrowthFillImage.gameObject.SetActive(false);
            if (chopFillImage != null)
            {
                chopFillImage.gameObject.SetActive(true);
                chopFillImage.fillAmount = maxChopHealth > 0f
                    ? Mathf.Clamp01(_chopHealth / maxChopHealth)
                    : 0f;
            }
        }
        else
        {
            if (WaterFillImage != null)
                WaterFillImage.gameObject.SetActive(true);
            if (GrowthFillImage != null)
                GrowthFillImage.gameObject.SetActive(true);
            if (chopFillImage != null)
                chopFillImage.gameObject.SetActive(false);

            base.UpdateStatFills();
        }
    }
    
    protected override void InitialiseStatBars()
    {
        base.InitialiseStatBars();
        if (chopFillImage != null)
            chopFillImage.gameObject.SetActive(false);
    }
    #endregion

    #region Chop Method
    /// <summary>
    /// Called by the axe tool each time it hits the tree.
    /// Reduces chop health by chopAmount. When health reaches zero Harvest()
    /// is called automatically — no tap required.
    /// No-op if the tree is not at Harvestable stage.
    /// </summary>
    public void Chop(float chopAmount)
    {
        if (Stage != FloraGrowthStage.Harvestable)
        {
            // Debug.LogWarning($"[Tree] Chop called on {gameObject.name} but stage is {Stage} — must be Harvestable.");
            return;
        }

        _chopHealth = Mathf.Max(0f, _chopHealth - chopAmount);
        // Debug.Log($"[Tree] {gameObject.name} chopped. Health remaining: {_chopHealth:F1}/{maxChopHealth}");

        // Keep chop fill current if stat bars are visible.
        if (StatBarsActive)
            UpdateStatFills();

        if (_chopHealth <= 0f)
        {
            harvestAmount = Random.Range(harvestAmount, maxHarvestAmount + 1);
            Harvest();
        }
    }
    #endregion

    #region Helpers
    private void ResetChopHealth()
    {
        _chopHealth = maxChopHealth;
    }
    #endregion

    #region SaveableBehaviour
    protected override FloraData BuildData()
    {
        var b = base.BuildData();
        return new WoodTreeData
        {
            waterLevel       = b.waterLevel,
            stageTimer       = b.stageTimer,
            stage            = b.stage,
            gracePeriodTimer = b.gracePeriodTimer,
            inGracePeriod    = b.inGracePeriod,
            parentSlotGuid   = b.parentSlotGuid,
            chopHealth       = _chopHealth
        };
    }

    protected override void ApplyData(FloraData data, SaveContext context)
    {
        base.ApplyData(data, context);
        _chopHealth = data is WoodTreeData treeData ? treeData.chopHealth : maxChopHealth;
    }
    
    public override string RecordType => treeType switch
    {
        TreeType.Wood   => "Wood Tree",
        TreeType.Apple  => "Apple Tree",
        TreeType.Orange => "Orange Tree",
        _               => "Unknown"
    };

    protected override FloraData DeserializeData(string json) => JsonUtility.FromJson<WoodTreeData>(json);
    #endregion
    
    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Chop Once")]
    private void DebugChopOnce() => Chop(1f);

    [ContextMenu("Debug/Full Chop")]
    private void DebugFullChop() => Chop(maxChopHealth);
#endif
    #endregion
}