using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// Growth stages for all flora.
/// </summary>
public enum FloraGrowthStage
{
    Seed,
    Sprout,
    Mature,
    Harvestable
}

/// <summary>
/// Serialisable data block for flora save state.
/// </summary>
[Serializable]
public class FloraData
{
    public float waterLevel;
    public float stageTimer;
    public FloraGrowthStage stage;
    public float gracePeriodTimer;
    public bool inGracePeriod;
}

/// <summary>
/// Base class for all flora (crops, plants, etc).
/// Handles water level decay, time-based growth stages, grace period, sprite swapping,
/// harvesting, alert integration, and world-space stat bar display on tap.
/// Subclasses implement GetOutputItem() and optionally override OnTapped().
/// </summary>
public abstract class FloraBase : SaveableBehaviour<FloraData>, IHandler, IBiomeOccupant, IUpgradeable
{
    [Header("Flora Identity")]
    [SerializeField, Tooltip("The biome this flora belongs to. ")]
    private BiomeManager.BiomeType homeBiome;
    [SerializeField, Tooltip("The upgrade type ID for this flora. Matches UpgradeDefinition.UpgradeTypeId.")]
    private string plantId;
    
    [Header("Stats")]
    [SerializeField, Range(0f, 1f)] protected float waterLevel = 1f;

    [Header("Rates")]
    [SerializeField, Tooltip("Flat water decay applied per tick.")]
    protected float waterDecayRate = 0.1f;

    [Header("Water Tick")]
    [SerializeField, Tooltip("Base interval in seconds between water decay ticks.")]
    protected float waterTickInterval = 10f;
    [SerializeField, Tooltip("Random variation added to each tick interval (±seconds).")]
    protected float waterTickVariation = 2f;
    [SerializeField, Tooltip("Minimum water level required for growth to advance.")]
    protected float waterThreshold = 0.4f;

    [Header("Grace Period")]
    [SerializeField, Tooltip("Seconds water can stay at 0 before the flora is lost.")]
    protected float loseGracePeriod = 30f;

    [Header("Growth Durations")]
    [SerializeField, Tooltip("Seconds spent in Seed stage before becoming a Sprout.")]
    protected float seedDuration = 30f;
    [SerializeField, Tooltip("Seconds spent in Sprout stage before becoming Mature.")]
    protected float sproutDuration = 60f;
    [SerializeField, Tooltip("Seconds spent in Mature stage before becoming Harvestable.")]
    protected float matureDuration = 60f;

    [Header("Growth")]
    [SerializeField] protected FloraGrowthStage stage = FloraGrowthStage.Seed;

    [Header("Harvest")]
    [SerializeField, Tooltip("If true the crop resets to Seed after harvest. " +
                             "If false the slot is cleared and the crop is destroyed.")]
    protected bool resetOnHarvest = true;
    [SerializeField, Tooltip("The amount of the output item given to the player on harvest.")] 
    protected int harvestAmount = 1;

    [Header("Stage Sprites")]
    [SerializeField] private Sprite seedSprite;
    [SerializeField] private Sprite sproutSprite;
    [SerializeField] private Sprite matureSprite;
    [SerializeField] private Sprite harvestableSprite;

    [Header("Stat Bars")]
    [SerializeField, Tooltip("World-space Image fill representing current water level.")]
    private Image waterFillImage;
    [SerializeField, Tooltip("World-space Image fill representing growth progress in current stage.")]
    private Image growthFillImage;
    [SerializeField, Tooltip("World-space Image fill representing removal progress when holding to remove.")]
    private Image removeFillImage;
    [SerializeField, Tooltip("Root GameObject containing both stat bars. Shown on tap, hidden after delay.")]
    private GameObject statBarsRoot;
    [SerializeField, Tooltip("Seconds the stat bars stay visible after a tap.")]
    private float showStatsDuration = 3f;

    [Header("Alerts")] [SerializeField, Tooltip("Local offset for alert emoji position relative to this flora.")]
    private Vector2[] alertOffsets = new[]
    {
        new Vector2(0f, 1f), // Default offset
        new Vector2(-0.5f, 1f), // For left-leaning sprites
        new Vector2(0.5f, 1f), // For right-leaning sprites
        new Vector2(0.25f, 0.25f)
    };
    
    [Header("FX")]
    [SerializeField] private ParticleSystem wateringFxPrefab;
    [SerializeField] private ParticleSystem removeFxPrefab;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] protected LayerMask interactableLayer;

    private float _stageTimer;
    private float _gracePeriodTimer;
    private bool _inGracePeriod;
    private bool _isLost;
    private Slot _parentSlot;
    private Coroutine _hideStatsCoroutine;

    // Events
    public event Action<float> OnWaterChanged;
    public event Action<FloraGrowthStage> OnStageChanged;
    public event Action OnLost;

    #region Mutators and Accessors

    public float WaterLevel => waterLevel;
    public FloraGrowthStage Stage => stage;
    public bool IsInGracePeriod => _inGracePeriod;
    public bool IsLost => _isLost;
    protected float GracePeriodTimer => _gracePeriodTimer;
    protected bool InGracePeriod  => _inGracePeriod;
    protected bool IsBeingRemoved { get; private set; }

    /// <summary>Protected accessor so subclasses can read the water fill image for stat bar overrides.</summary>
    protected Image WaterFillImage => waterFillImage;
    /// <summary>Protected accessor so subclasses can read the growth fill image for stat bar overrides.</summary>
    protected Image GrowthFillImage => growthFillImage;
    /// <summary>Returns true if the stat bars root is currently active.</summary>
    protected bool StatBarsActive => statBarsRoot != null && statBarsRoot.activeSelf;
    /// <summary>The biome this flora belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => homeBiome;
    /// <summary>The Slot this flora occupies. Set by Slot.Place() immediately after instantiation.</summary>
    public Slot ParentSlot => _parentSlot;
    /// <summary> The unique id for this plant. </summary>
    public string UpgradeTypeId => plantId;
    /// <summary>
    /// Current stage progress as a 0-1 value derived from the stage timer.
    /// Use this for UI progress bars.
    /// </summary>
    public float GrowthProgress
    {
        get
        {
            float duration = CurrentStageDuration;
            return duration > 0f ? Mathf.Clamp01(_stageTimer / duration) : 1f;
        }
    }

    private float CurrentStageDuration => stage switch
    {
        FloraGrowthStage.Seed    => seedDuration,
        FloraGrowthStage.Sprout  => sproutDuration,
        FloraGrowthStage.Mature  => matureDuration,
        _                        => float.MaxValue
    };
    
    /// <summary>
    /// Activates or deactivates the watering particle effect.
    /// Called by WateringCan when starting or stopping watering.
    /// Subclasses can also call this in OnWatered() for additional feedback.
    /// </summary>
    /// <param name="active"></param>
    public void SetWateringFx(bool active)
    {
        if (wateringFxPrefab != null)
            wateringFxPrefab.gameObject.SetActive(active);
    }
    
    // Progress fill for the removal hold — 0-1 drives a fill image in statBarsRoot
    public void SetRemoveProgress(float progress)
    {
        if (removeFillImage == null) return;

        IsBeingRemoved = progress > 0f;

        if (waterFillImage != null)
            waterFillImage.gameObject.SetActive(!IsBeingRemoved);
        if (growthFillImage != null)
            growthFillImage.gameObject.SetActive(!IsBeingRemoved);

        removeFillImage.gameObject.SetActive(IsBeingRemoved);
        removeFillImage.fillAmount = progress;
        
        if (removeFxPrefab != null)
            removeFxPrefab.gameObject.SetActive(IsBeingRemoved);
    }
    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitialiseStatBars();
    }

    protected virtual void Start()
    {
        if (wateringFxPrefab == null)
            wateringFxPrefab = GetComponentInChildren<ParticleSystem>(includeInactive: true);
        StartCoroutine(WaterTickLoop());
        UpdateSprite();
        CheckAndApplySelfUpgrade(plantId);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        BiomeManager.Instance?.RegisterOccupant(this);
        
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
        // Clear the parent slot so it becomes available for new placement.
        _parentSlot?.Clear();
    }
    
    protected virtual void Update()
    {
        if (_isLost) return;

        TickGracePeriod();
        Grow();
    }

    #endregion

    #region Stat Bar Initialisation

    /// <summary>
    /// Hides stat bars on Awake so prefab state doesn't matter.
    /// Override in subclasses to also hide additional fills (e.g. chop bar on Tree).
    /// </summary>
    protected virtual void InitialiseStatBars()
    {
        if (statBarsRoot != null)
            statBarsRoot.SetActive(false);
    }

    #endregion

    #region Slot

    /// <summary>
    /// Assigns the slot this flora occupies.
    /// Called by Slot.Place() immediately after instantiation.
    /// </summary>
    public void SetSlot(Slot slot)
    {
        _parentSlot = slot;
        homeBiome = slot.HomeBiome;
    }

    #endregion

    #region IHandler

    private void HandleWorldTap(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    /// <summary>
    /// If Harvestable, calls Harvest(). Otherwise shows the stat bars.
    /// Override in subclasses to add extra behaviour, calling base.OnTapped() to retain this logic.
    /// </summary>
    public virtual void OnTapped()
    {
        if (stage == FloraGrowthStage.Harvestable)
            Harvest();
        else
            ShowStats();
    }

    #endregion

    #region Stat Bars

    /// <summary>
    /// Shows the world-space stat bars.
    /// If autoHide is true the bars hide after showStatsDuration seconds.
    /// If false they stay visible until HideStats() is called explicitly.
    /// </summary>
    public void ShowStats(bool autoHide = true)
    {
        if (statBarsRoot == null) return;

        UpdateStatFills();
        statBarsRoot.SetActive(true);

        if (_hideStatsCoroutine != null)
            StopCoroutine(_hideStatsCoroutine);

        if (autoHide)
            _hideStatsCoroutine = StartCoroutine(HideStatsAfterDelay());
    }

    /// <summary>
    /// Immediately hides the stat bars and cancels any pending hide timer.
    /// </summary>
    public void HideStats()
    {
        if (_hideStatsCoroutine != null)
        {
            StopCoroutine(_hideStatsCoroutine);
            _hideStatsCoroutine = null;
        }

        if (statBarsRoot != null)
            statBarsRoot.SetActive(false);
    }

    /// <summary>
    /// Updates fill amounts on the water and growth images.
    /// Override in subclasses to swap which fills are shown (e.g. Tree shows chop bar at Harvestable).
    /// </summary>
    protected virtual void UpdateStatFills()
    {
        if (waterFillImage != null)
            waterFillImage.fillAmount = waterLevel;

        if (growthFillImage != null)
            growthFillImage.fillAmount = GrowthProgress;
    }

    private IEnumerator HideStatsAfterDelay()
    {
        yield return new WaitForSeconds(showStatsDuration);
        if (statBarsRoot != null)
            statBarsRoot.SetActive(false);
        _hideStatsCoroutine = null;
    }

    #endregion

    #region Water

    /// <summary>
    /// Adds waterAmount to the current water level, clamped to 1.
    /// No-op if already full. Calls OnWatered() for subclass feedback.
    /// </summary>
    public void Water(float waterAmount)
    {
        if (waterLevel >= 1f) return;
        SetWaterLevel(Mathf.Clamp01(waterLevel + waterAmount));
        OnWatered();
    }

    /// <summary>
    /// Called after a successful Water() call.
    /// Override in subclasses to add watering feedback without reimplementing Water logic.
    /// </summary>
    protected virtual void OnWatered()
    {
        // Debug.Log($"[{gameObject.name}] Watered. Water level: {waterLevel:F2}");
    }

    private IEnumerator WaterTickLoop()
    {
        while (!_isLost)
        {
            float interval = waterTickInterval + Random.Range(-waterTickVariation, waterTickVariation);
            interval = Mathf.Max(1f, interval);
            yield return new WaitForSeconds(interval);
            ApplyWaterLevelDecay();
        }
    }

    private void ApplyWaterLevelDecay()
    {
        if (waterLevel <= 0f) return;
        if (stage == FloraGrowthStage.Harvestable) return;

        // waterDecayRate is a flat amount per tick — no Time.deltaTime since the
        // tick interval is already the time gate.
        SetWaterLevel(waterLevel - waterDecayRate);
    }

    #endregion

    #region Growth

    /// <summary>
    /// Ticks the stage timer while watered and above threshold.
    /// Advances to the next stage when the current stage duration is met.
    /// Halts at Harvestable — waiting for the player to tap and harvest.
    /// </summary>
    protected virtual void Grow()
    {
        if (stage == FloraGrowthStage.Harvestable) return;
        if (waterLevel < waterThreshold) return;

        _stageTimer += Time.deltaTime;

        if (StatBarsActive)
            UpdateStatFills();

        if (_stageTimer >= CurrentStageDuration)
            SetStage(stage + 1);
    }

    #endregion

    #region Harvest

    /// <summary>
    /// Adds the output item to PlayerInventory and clears all alerts.
    /// If resetOnHarvest is true, resets to Seed stage.
    /// If false, clears the slot and destroys this GameObject.
    /// </summary>
    protected void Harvest()
    {
        ItemDefinition output = GetOutputItem();

        if (output == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Harvest called but GetOutputItem() returned null.");
            return;
        }

        PlayerInventory.Instance.Add(output, harvestAmount);
        QuestManager.Instance?.RecordProgress(
            QuestObjectiveType.HarvestItem,
            GetOutputItem()?.ItemName,
            harvestAmount
        );
        AlertManager.Instance?.ClearAllAlerts(gameObject);
        HideStats();
        Debug.Log($"[{gameObject.name}] Harvested {output.ItemName}.");

        if (resetOnHarvest)
        {
            SetStage(FloraGrowthStage.Seed);
            Debug.Log($"[{gameObject.name}] Reset to Seed stage.");
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Grace Period

    private void TickGracePeriod()
    {
        if (!_inGracePeriod) return;

        _gracePeriodTimer -= Time.deltaTime;
        // Debug.Log($"[{gameObject.name}] Grace period: {_gracePeriodTimer:F1}s remaining.");

        if (_gracePeriodTimer <= 0f)
            LoseFlora();
    }

    private void EnterGracePeriod()
    {
        if (_inGracePeriod) return;
        _inGracePeriod = true;
        _gracePeriodTimer = loseGracePeriod;
        var alertOffset = OffsetPerStage();
        AlertManager.Instance?.ShowAlert(gameObject, AlertType.NeedsAttention, alertOffset);
        Debug.Log($"[{gameObject.name}] Entered grace period. {loseGracePeriod}s until lost.");
    }

    private void ExitGracePeriod()
    {
        if (!_inGracePeriod) return;
        _inGracePeriod = false;
        _gracePeriodTimer = 0f;
        AlertManager.Instance?.ClearAlert(gameObject, AlertType.NeedsAttention);
        Debug.Log($"[{gameObject.name}] Exited grace period.");
    }

    private void LoseFlora()
    {
        if (_isLost) return;
        _isLost = true;

        AlertManager.Instance?.ClearAllAlerts(gameObject);

        Debug.Log($"[{gameObject.name}] Lost due to dehydration.");
        OnLost?.Invoke();

        Destroy(gameObject);
    }

    #endregion

    #region Setters

    protected void SetWaterLevel(float value)
    {
        waterLevel = Mathf.Clamp01(value);
        OnWaterChanged?.Invoke(waterLevel);

        if (StatBarsActive)
            UpdateStatFills();

        if (waterLevel <= 0f)
            EnterGracePeriod();
        else
            ExitGracePeriod();
        
        var alertOffset = OffsetPerStage();

        if (waterLevel < waterThreshold)
            AlertManager.Instance?.ShowAlert(gameObject, AlertType.NeedsWater, alertOffset);
        else
            AlertManager.Instance?.ClearAlert(gameObject, AlertType.NeedsWater);
    }

    protected void SetStage(FloraGrowthStage newStage)
    {
        if (stage == newStage) return;
        stage = newStage;
        _stageTimer = 0f;
        OnStageChanged?.Invoke(stage);
        UpdateSprite();

        if (StatBarsActive)
            UpdateStatFills();
        
        var alertOffset = OffsetPerStage();

        if (stage == FloraGrowthStage.Harvestable)
            AlertManager.Instance?.ShowAlert(gameObject, AlertType.ReadyToHarvest, alertOffset, persistent: true);
        else
            AlertManager.Instance?.ClearAlert(gameObject, AlertType.ReadyToHarvest);

        Debug.Log($"[{gameObject.name}] Growth stage changed to {stage}.");
    }

    #endregion

    #region Sprite

    private void UpdateSprite()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.sprite = stage switch
        {
            FloraGrowthStage.Seed => seedSprite,
            FloraGrowthStage.Sprout => sproutSprite,
            FloraGrowthStage.Mature => matureSprite,
            FloraGrowthStage.Harvestable => harvestableSprite,
            _ => spriteRenderer.sprite
        };
    }

    #endregion

    #region SaveableBehaviour

    public override abstract string RecordType { get; }
    public override int LoadPriority => 10;

    protected override FloraData BuildData() => new FloraData
    {
        waterLevel = waterLevel,
        stageTimer = _stageTimer,
        stage = stage,
        gracePeriodTimer = _gracePeriodTimer,
        inGracePeriod = _inGracePeriod
    };

    protected override void ApplyData(FloraData data, SaveContext context)
    {
        SetWaterLevel(data.waterLevel);
        _stageTimer = data.stageTimer;
        SetStage(data.stage);
        _gracePeriodTimer = data.gracePeriodTimer;
        _inGracePeriod = data.inGracePeriod;
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Returns the ItemDefinition this flora produces on harvest.
    /// Implement in subclass by returning the serialized output item field.
    /// </summary>
    protected abstract ItemDefinition GetOutputItem();
    public ItemDefinition GetOutputItemPublic() => GetOutputItem();

    public virtual void ApplyUpgrade(UpgradeDefinition upgrade) { }
    #endregion
    
    #region Upgrade Methods
    /// <summary>
    /// Checks if this object's upgrade has already been purchased and applies it
    /// if so. Call from Start on any subclass that implements IUpgradeable.
    /// Handles the case where an object is placed after its upgrade was purchased.
    /// </summary>
    protected void CheckAndApplySelfUpgrade(string upgradeTypeId)
    {
        if (UpgradeManager.Instance == null) return;

        foreach (var upgrade in UpgradeManager.Instance.GetAllUpgrades())
        {
            if (upgrade.UpgradeTypeId == upgradeTypeId &&
                UpgradeManager.Instance.IsUpgradeApplied(upgrade))
            {
                (this as IUpgradeable)?.ApplyUpgrade(upgrade);
                return;
            }
        }
    }
    #endregion

    #region Helper Method
    private Vector2 OffsetPerStage() => stage switch
    {
        FloraGrowthStage.Seed  => alertOffsets[0],
        FloraGrowthStage.Sprout => alertOffsets[1],
        FloraGrowthStage.Mature => alertOffsets[2],
        FloraGrowthStage.Harvestable => alertOffsets[3],
        _  => new Vector2(0f, 0f)
    };
    #endregion
    
    #region Debug
#if UNITY_EDITOR
    [ContextMenu("Debug/Fill WaterLevel")]
    private void DebugFillWaterLevel() => SetWaterLevel(1f);

    [ContextMenu("Debug/Drain WaterLevel")]
    private void DebugDrainWaterLevel() => SetWaterLevel(0f);

    [ContextMenu("Debug/Advance Stage")]
    private void DebugAdvanceStage()
    {
        if (stage == FloraGrowthStage.Harvestable) return;
        SetStage(stage + 1);
    }

    [ContextMenu("Debug/Force Harvest")]
    private void DebugForceHarvest()
    {
        SetStage(FloraGrowthStage.Harvestable);
        Harvest();
    }
#endif
    #endregion
}