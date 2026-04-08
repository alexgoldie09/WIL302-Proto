using System.Collections;
using UnityEngine;

/// <summary>
/// Growth stages for all fauna.
/// </summary>
public enum FaunaGrowthStage
{
    Baby,
    Middle,
    Adult
}

/// <summary>
/// Serialisable data block for fauna save state.
/// </summary>
[System.Serializable]
public class FaunaData
{
    public float hunger;
    public float happiness;
    public FaunaGrowthStage stage;
    public float gracePeriodTimer;
    public bool inGracePeriod;
}

/// <summary>
/// Base class for all fauna (ducks, clams, etc).
/// Handles hunger/happiness decay, growth stage, grace period, and sprite swapping.
/// Subclasses implement Feed, Grow, OnTapped, and ProduceOutput.
/// </summary>
public abstract class FaunaBase : SaveableBehaviour<FaunaData>, IHandler
{
    [Header("Stats")]
    [SerializeField, Range(0f, 1f)] protected float hunger = 1f;
    [SerializeField, Range(0f, 1f)] protected float happiness = 1f;

    [Header("Decay Rates")]
    [SerializeField, Tooltip("Base hunger decay per tick.")]
    protected float hungerDecayRate = 0.05f;
    [SerializeField, Tooltip("Happiness decay per second.")]
    protected float happinessDecayRate = 0.01f;
    [SerializeField, Tooltip("How much low happiness accelerates hunger decay. At 0 happiness, hunger decays at hungerDecayRate * this multiplier.")]
    protected float lowHappinessHungerMultiplier = 2f;

    [Header("Hunger Tick")]
    [SerializeField, Tooltip("Base interval in seconds between hunger ticks.")]
    protected float hungerTickInterval = 10f;
    [SerializeField, Tooltip("Random variation added to each tick interval (±seconds).")]
    protected float hungerTickVariation = 2f;

    [Header("Grace Period")]
    [SerializeField, Tooltip("Seconds hunger can sit at 0 before the animal is lost.")]
    protected float loseGracePeriod = 30f;

    [Header("Growth")]
    [SerializeField] protected FaunaGrowthStage stage = FaunaGrowthStage.Baby;

    [Header("Stage Sprites")]
    [SerializeField] private Sprite babySprite;
    [SerializeField] private Sprite middleSprite;
    [SerializeField] private Sprite adultSprite;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] protected LayerMask interactableLayer;

    private float _gracePeriodTimer;
    private bool _inGracePeriod;
    private bool _isLost;

    // Events
    public event System.Action<float> OnHungerChanged;
    public event System.Action<float> OnHappinessChanged;
    public event System.Action<FaunaGrowthStage> OnStageChanged;
    public event System.Action OnLost;

    #region Accessors

    public float Hunger => hunger;
    public float Happiness => happiness;
    public FaunaGrowthStage Stage => stage;
    public bool IsInGracePeriod => _inGracePeriod;
    public bool IsLost => _isLost;
    protected float GracePeriodTimer => _gracePeriodTimer;
    protected bool InGracePeriod => _inGracePeriod;

    #endregion

    #region Unity Lifecycle

    protected virtual void Start()
    {
        Debug.Log($"[FaunaBase] Start called on {gameObject.name}");
        StartCoroutine(HungerTickLoop());
        UpdateSprite();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    protected virtual void Update()
    {
        if (_isLost) return;

        TickHappiness();
        TickGracePeriod();
        Grow();
    }

    #endregion

    #region Hunger Tick

    private IEnumerator HungerTickLoop()
    {
        while (!_isLost)
        {
            float interval = hungerTickInterval + Random.Range(-hungerTickVariation, hungerTickVariation);
            interval = Mathf.Max(1f, interval);
            yield return new WaitForSeconds(interval);

            ApplyHungerDecay();
        }
    }

    private void ApplyHungerDecay()
    {
        float happinessMultiplier = Mathf.Lerp(lowHappinessHungerMultiplier, 1f, happiness);
        float decay = hungerDecayRate * happinessMultiplier;
        SetHunger(hunger - decay);
    }

    #endregion

    #region Happiness Tick

    private void TickHappiness()
    {
        if (happiness <= 0f) return;
        SetHappiness(happiness - happinessDecayRate * Time.deltaTime);
    }

    #endregion

    #region Grace Period

    private void TickGracePeriod()
    {
        if (!_inGracePeriod) return;

        _gracePeriodTimer -= Time.deltaTime;
        Debug.Log($"[{gameObject.name}] Grace period: {_gracePeriodTimer:F1}s remaining.");

        if (_gracePeriodTimer <= 0f)
            LoseFauna();
    }

    private void EnterGracePeriod()
    {
        if (_inGracePeriod) return;
        _inGracePeriod = true;
        _gracePeriodTimer = loseGracePeriod;
        Debug.Log($"[{gameObject.name}] Entered grace period. {loseGracePeriod}s until lost.");
    }

    private void ExitGracePeriod()
    {
        if (!_inGracePeriod) return;
        _inGracePeriod = false;
        _gracePeriodTimer = 0f;
        Debug.Log($"[{gameObject.name}] Exited grace period.");
    }

    private void LoseFauna()
    {
        if (_isLost) return;
        _isLost = true;
        Debug.Log($"[{gameObject.name}] Lost due to starvation.");
        OnLost?.Invoke();
        Destroy(gameObject);
    }

    #endregion

    #region Setters

    protected void SetHunger(float value)
    {
        hunger = Mathf.Clamp01(value);
        OnHungerChanged?.Invoke(hunger);

        if (hunger <= 0f)
            EnterGracePeriod();
        else
            ExitGracePeriod();
    }

    protected void SetHappiness(float value)
    {
        happiness = Mathf.Clamp01(value);
        OnHappinessChanged?.Invoke(happiness);
    }

    protected void SetStage(FaunaGrowthStage newStage)
    {
        if (stage == newStage) return;
        stage = newStage;
        OnStageChanged?.Invoke(stage);
        UpdateSprite();
        Debug.Log($"[{gameObject.name}] Growth stage changed to {stage}.");
    }

    #endregion

    #region Sprite

    private void UpdateSprite()
    {
        if (spriteRenderer == null) return;

        spriteRenderer.sprite = stage switch
        {
            FaunaGrowthStage.Baby => babySprite,
            FaunaGrowthStage.Middle => middleSprite,
            FaunaGrowthStage.Adult => adultSprite,
            _ => spriteRenderer.sprite
        };
    }

    #endregion

    #region IHandler

    private void HandleWorldTap(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    #endregion

    #region SaveableBehaviour

    public override string RecordType => "Fauna";
    public override int LoadPriority => 10;

    protected override FaunaData BuildData() => new FaunaData
    {
        hunger = hunger,
        happiness = happiness,
        stage = stage,
        gracePeriodTimer = _gracePeriodTimer,
        inGracePeriod = _inGracePeriod
    };

    protected override void ApplyData(FaunaData data, SaveContext context)
    {
        SetHunger(data.hunger);
        SetHappiness(data.happiness);
        SetStage(data.stage);
        _gracePeriodTimer = data.gracePeriodTimer;
        _inGracePeriod = data.inGracePeriod;
    }

    #endregion

    #region Abstract Methods

    public abstract void OnTapped();
    public abstract void Feed(ItemDefinition item);
    protected abstract void Grow();
    protected abstract void ProduceOutput(ItemDefinition item);

    #endregion

    #region Debug

#if UNITY_EDITOR
    [ContextMenu("Debug/Fill Hunger")]
    private void DebugFillHunger() => SetHunger(1f);

    [ContextMenu("Debug/Drain Hunger")]
    private void DebugDrainHunger() => SetHunger(0f);

    [ContextMenu("Debug/Fill Happiness")]
    private void DebugFillHappiness() => SetHappiness(1f);

    [ContextMenu("Debug/Drain Happiness")]
    private void DebugDrainHappiness() => SetHappiness(0f);

    [ContextMenu("Debug/Advance Stage")]
    private void DebugAdvanceStage()
    {
        if (stage == FaunaGrowthStage.Adult) return;
        SetStage(stage + 1);
    }
#endif

    #endregion
}