using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Concrete flora class representing a harvestable tree.
/// Grows through standard FloraBase stages. At Harvestable the player must use
/// the axe tool to chop it down — each Chop() call reduces health until it hits
/// zero at which point Harvest() is called automatically.
/// Water and growth stat bars are replaced by a chop health bar at Harvestable stage.
/// </summary>
public class WoodTree : FloraBase
{
    [Header("Tree Output")]
    [SerializeField, Tooltip("The item this tree produces when chopped down. " +
                             "Determines what tree this is (e.g. Apple, Wood Log).")]
    private ItemDefinition outputItem;

    [Header("Chop")]
    [SerializeField, Tooltip("Total chop health. Axe reduces this on each Chop() call.")]
    private float maxChopHealth = 5f;
    [SerializeField, Tooltip("World-space Image fill representing remaining chop health. " +
                             "Should be a child of statBarsRoot alongside the other fills.")]
    private Image chopFillImage;
    
    private float _chopHealth;
    
    public override string RecordType => "Tree";
    protected override ItemDefinition GetOutputItem() => outputItem;

    #region  Unity Lifecycle Overrides
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
            Harvest();
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
        var data = base.BuildData();
        // TODO: Persist chopHealth and outputItem via ItemRegistry when save
        // system is fully implemented.
        return data;
    }

    protected override void ApplyData(FloraData data, SaveContext context)
    {
        base.ApplyData(data, context);
        ResetChopHealth();
        // TODO: Restore outputItem reference from ItemRegistry using saved name.
    }
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