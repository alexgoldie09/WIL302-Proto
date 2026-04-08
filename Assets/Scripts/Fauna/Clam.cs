using System.Collections;
using UnityEngine;

/// <summary>
/// Serialisable save data for Clam.
/// </summary>
[System.Serializable]
public class ClamData : FaunaData
{
    public float growthTimer;
    public bool hasPearlReady;
}

/// <summary>
/// Clam fauna. Grows from Baby to Adult over time gated by happiness.
/// Stays in place but wiggles on a random timer.
/// At Adult stage produces pearls on a timer — swaps to pearl sprite when ready.
/// Player taps the clam to collect the pearl and restore the adult sprite.
/// Plankton restores more happiness than other feedable items.
/// </summary>
public class Clam : FaunaBase
{
    [Header("Clam - Growth")]
    [SerializeField, Tooltip("Time in seconds to grow from Baby to Middle.")]
    private float babyToMiddleDuration = 150f;
    [SerializeField, Tooltip("Time in seconds to grow from Middle to Adult.")]
    private float middleToAdultDuration = 200f;
    [SerializeField, Tooltip("Minimum happiness required to progress growth.")]
    private float happinessGrowthThreshold = 0.4f;

    [Header("Clam - Feeding")]
    [SerializeField, Tooltip("Base happiness restored by any feedable item.")]
    private float baseHappinessRestore = 0.1f;
    [SerializeField, Tooltip("Happiness multiplier applied when fed plankton.")]
    private float planktonHappinessMultiplier = 2.5f;
    [SerializeField, Tooltip("The plankton ItemDefinition.")]
    private ItemDefinition planktonItem;

    [Header("Clam - Pearl Production")]
    [SerializeField, Tooltip("The pearl ItemDefinition added to PlayerInventory on collection.")]
    private ItemDefinition pearlItem;
    [SerializeField, Tooltip("Base interval in seconds between pearl production.")]
    private float pearlProductionInterval = 90f;
    [SerializeField, Tooltip("Random variation added to each pearl interval (±seconds).")]
    private float pearlProductionVariation = 15f;

    [Header("Clam - Sprites")]
    [SerializeField, Tooltip("Regular adult sprite to restore after pearl is collected.")]
    private Sprite clamAdultSprite;
    [SerializeField, Tooltip("Sprite shown when a pearl is ready to collect.")]
    private Sprite pearlReadySprite;
    [SerializeField] private SpriteRenderer clamSpriteRenderer;

    [Header("Clam - Wiggle")]
    [SerializeField, Tooltip("Base interval in seconds between wiggles.")]
    private float wiggleInterval = 5f;
    [SerializeField, Tooltip("Random variation on wiggle interval (±seconds).")]
    private float wiggleVariation = 2f;
    [SerializeField, Tooltip("Max rotation angle in degrees for the wobble.")]
    private float wiggleAngle = 8f;
    [SerializeField, Tooltip("Duration of one full wiggle cycle in seconds.")]
    private float wiggleDuration = 0.4f;

    [Header("Clam - Pulse")]
    [SerializeField, Tooltip("Scale multiplier at peak of pulse.")]
    private float pulseScale = 1.15f;
    [SerializeField, Tooltip("Duration of one full pulse cycle in seconds.")]
    private float pulseDuration = 0.6f;

    [Header("Clam - Alerts")]
    [SerializeField, Tooltip("Hunger threshold below which the low hunger alert fires.")]
    private float lowHungerThreshold = 0.3f;
    [SerializeField, Tooltip("Happiness threshold below which the unhappy alert fires.")]
    private float lowHappinessThreshold = 0.3f;
    [SerializeField, Tooltip("World-space offset for alerts above the clam.")]
    private Vector2 alertOffset = new Vector2(5f, 14f);

    private float _growthTimer;
    private bool _hasPearlReady;
    private Coroutine _pearlCoroutine;
    private Coroutine _wiggleCoroutine;
    private Coroutine _pulseCoroutine;

    private bool _lowHungerAlertActive;
    private bool _graceAlertActive;
    private bool _unhappyAlertActive;

    #region SaveableBehaviour

    public override string RecordType => "Clam";
    public override int LoadPriority => 10;

    protected override FaunaData BuildData() => new ClamData
    {
        hunger = hunger,
        happiness = happiness,
        stage = stage,
        gracePeriodTimer = GracePeriodTimer,
        inGracePeriod = InGracePeriod,
        growthTimer = _growthTimer,
        hasPearlReady = _hasPearlReady
    };

    protected override void ApplyData(FaunaData data, SaveContext context)
    {
        base.ApplyData(data, context);
        if (data is ClamData clamData)
        {
            _growthTimer = clamData.growthTimer;
            if (clamData.hasPearlReady)
                SetPearlReady(true);
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Start()
    {
        base.Start();

        OnHungerChanged += HandleHungerChanged;
        OnHappinessChanged += HandleHappinessChanged;
        OnLost += HandleLost;

        if (stage == FaunaGrowthStage.Adult)
            StartPearlProduction();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        if (_wiggleCoroutine != null)
            StopCoroutine(_wiggleCoroutine);
        _wiggleCoroutine = StartCoroutine(WiggleLoop());

        if (stage == FaunaGrowthStage.Adult && _pearlCoroutine == null && !_hasPearlReady)
            StartPearlProduction();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (_wiggleCoroutine != null)
        {
            StopCoroutine(_wiggleCoroutine);
            _wiggleCoroutine = null;
        }

        if (_pearlCoroutine != null)
        {
            StopCoroutine(_pearlCoroutine);
            _pearlCoroutine = null;
        }

        if (_pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
        }
    }

    private void OnDestroy()
    {
        OnHungerChanged -= HandleHungerChanged;
        OnHappinessChanged -= HandleHappinessChanged;
        OnLost -= HandleLost;

        if (AlertManager.Instance != null)
            AlertManager.Instance.ClearAllAlerts(gameObject);
    }

    #endregion

    #region FaunaBase - Abstract Implementations

    public override void OnTapped()
    {
        if (_hasPearlReady)
            CollectPearl();
        else if (FeedPanelUI.Instance != null)
            FeedPanelUI.Instance.Open(this, transform.position);
    }

    public override void Feed(ItemDefinition item)
    {
        if (item == null) return;
        if (!item.IsAvailableForFeeding)
        {
            Debug.LogWarning($"[Clam] {item.ItemName} is not feedable.");
            return;
        }

        if (!PlayerInventory.Instance.Remove(item, 1))
        {
            Debug.LogWarning($"[Clam] Could not remove {item.ItemName} from inventory.");
            return;
        }

        SetHunger(hunger + item.BaseFeedValue);

        float happinessRestore = baseHappinessRestore;
        if (planktonItem != null && item == planktonItem)
            happinessRestore *= planktonHappinessMultiplier;

        SetHappiness(happiness + happinessRestore);

        Debug.Log($"[Clam] Fed {item.ItemName}. Hunger: {hunger:F2} Happiness: {happiness:F2}");
    }

    protected override void Grow()
    {
        if (stage == FaunaGrowthStage.Adult) return;
        if (happiness < happinessGrowthThreshold) return;

        _growthTimer += Time.deltaTime;

        float requiredTime = stage == FaunaGrowthStage.Baby
            ? babyToMiddleDuration
            : middleToAdultDuration;

        if (_growthTimer >= requiredTime)
        {
            _growthTimer = 0f;
            var nextStage = stage == FaunaGrowthStage.Baby
                ? FaunaGrowthStage.Middle
                : FaunaGrowthStage.Adult;

            SetStage(nextStage);

            if (nextStage == FaunaGrowthStage.Adult)
                StartPearlProduction();
        }
    }

    protected override void ProduceOutput(ItemDefinition item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Clam] ProduceOutput called but item is null — is pearlItem assigned?");
            return;
        }

        SetPearlReady(true);
        Debug.Log($"[Clam] Pearl is ready to collect.");
    }

    #endregion

    #region Pearl Collection

    private void SetPearlReady(bool ready)
    {
        _hasPearlReady = ready;

        if (clamSpriteRenderer != null)
            clamSpriteRenderer.sprite = ready ? pearlReadySprite : clamAdultSprite;

        if (ready)
        {
            if (_pulseCoroutine != null) StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = StartCoroutine(PulseLoop());

            if (AlertManager.Instance != null)
                AlertManager.Instance.ShowAlert(gameObject, AlertType.ReadyToCollect, alertOffset, persistent: true);
        }
        else
        {
            if (_pulseCoroutine != null)
            {
                StopCoroutine(_pulseCoroutine);
                _pulseCoroutine = null;
            }

            transform.localScale = new Vector3(0.04f, 0.04f, 0.04f);

            if (AlertManager.Instance != null)
                AlertManager.Instance.ClearAlert(gameObject, AlertType.ReadyToCollect);
        }
    }

    private void CollectPearl()
    {
        if (pearlItem == null)
        {
            Debug.LogWarning("[Clam] Cannot collect pearl — pearlItem is not assigned.");
            return;
        }

        PlayerInventory.Instance.Add(pearlItem, 1);
        SetPearlReady(false);
        StartPearlProduction();

        Debug.Log($"[Clam] Pearl collected and added to inventory.");
    }

    #endregion

    #region Pearl Production

    private void StartPearlProduction()
    {
        if (_pearlCoroutine != null) return;
        _pearlCoroutine = StartCoroutine(PearlProductionLoop());
        Debug.Log($"[Clam] Pearl production started.");
    }

    private IEnumerator PearlProductionLoop()
    {
        float interval = pearlProductionInterval + Random.Range(-pearlProductionVariation, pearlProductionVariation);
        interval = Mathf.Max(5f, interval);
        yield return new WaitForSeconds(interval);

        if (!IsLost)
            ProduceOutput(pearlItem);

        _pearlCoroutine = null;
    }

    #endregion

    #region Wiggle

    private IEnumerator WiggleLoop()
    {
        while (!IsLost)
        {
            float wait = wiggleInterval + Random.Range(-wiggleVariation, wiggleVariation);
            wait = Mathf.Max(1f, wait);
            yield return new WaitForSeconds(wait);

            yield return StartCoroutine(WiggleOnce());
        }
    }

    private IEnumerator WiggleOnce()
    {
        float elapsed = 0f;
        Quaternion original = transform.rotation;

        while (elapsed < wiggleDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / wiggleDuration;
            float angle = Mathf.Sin(t * Mathf.PI * 2f) * wiggleAngle;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        transform.rotation = original;
    }

    #endregion

    #region Pulse

    private IEnumerator PulseLoop()
    {
        while (_hasPearlReady)
        {
            yield return StartCoroutine(PulseOnce());
            yield return new WaitForSeconds(pulseDuration * 0.5f);
        }
    }

    private IEnumerator PulseOnce()
    {
        float elapsed = 0f;
        Vector3 originalScale = transform.localScale;

        while (elapsed < pulseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin((elapsed / pulseDuration) * Mathf.PI);
            transform.localScale = Vector3.Lerp(originalScale, originalScale * pulseScale, t);
            yield return null;
        }

        transform.localScale = originalScale;
    }

    #endregion

    #region Alert Handling

    private void HandleHungerChanged(float newHunger)
    {
        if (AlertManager.Instance == null) return;

        if (newHunger <= 0f)
        {
            if (_lowHungerAlertActive)
            {
                AlertManager.Instance.ClearAlert(gameObject, AlertType.Hungry);
                _lowHungerAlertActive = false;
            }

            if (!_graceAlertActive)
            {
                AlertManager.Instance.ShowAlert(gameObject, AlertType.NeedsAttention, alertOffset, persistent: true);
                _graceAlertActive = true;
            }
        }
        else if (newHunger <= lowHungerThreshold)
        {
            if (_graceAlertActive)
            {
                AlertManager.Instance.ClearAlert(gameObject, AlertType.NeedsAttention);
                _graceAlertActive = false;
            }

            if (!_lowHungerAlertActive)
            {
                AlertManager.Instance.ShowAlert(gameObject, AlertType.Hungry, alertOffset, persistent: true);
                _lowHungerAlertActive = true;
            }
        }
        else
        {
            if (_lowHungerAlertActive)
            {
                AlertManager.Instance.ClearAlert(gameObject, AlertType.Hungry);
                _lowHungerAlertActive = false;
            }

            if (_graceAlertActive)
            {
                AlertManager.Instance.ClearAlert(gameObject, AlertType.NeedsAttention);
                _graceAlertActive = false;
            }
        }
    }

    private void HandleHappinessChanged(float newHappiness)
    {
        if (AlertManager.Instance == null) return;

        if (newHappiness <= lowHappinessThreshold)
        {
            if (!_unhappyAlertActive)
            {
                AlertManager.Instance.ShowAlert(gameObject, AlertType.Unhappy, alertOffset, persistent: true);
                _unhappyAlertActive = true;
            }
        }
        else
        {
            if (_unhappyAlertActive)
            {
                AlertManager.Instance.ClearAlert(gameObject, AlertType.Unhappy);
                _unhappyAlertActive = false;
            }
        }
    }

    private void HandleLost()
    {
        if (AlertManager.Instance != null)
            AlertManager.Instance.ClearAllAlerts(gameObject);
    }

    #endregion

    #region Debug

#if UNITY_EDITOR
    [ContextMenu("Debug/Force Grow to Middle")]
    private void DebugGrowToMiddle() => SetStage(FaunaGrowthStage.Middle);

    [ContextMenu("Debug/Force Grow to Adult")]
    private void DebugGrowToAdult()
    {
        SetStage(FaunaGrowthStage.Adult);
        StartPearlProduction();
    }

    [ContextMenu("Debug/Force Pearl Ready")]
    private void DebugForcePearlReady() => ProduceOutput(pearlItem);

    [ContextMenu("Debug/Trigger Low Hunger")]
    private void DebugTriggerLowHunger() => SetHunger(0.2f);

    [ContextMenu("Debug/Trigger Grace Period")]
    private void DebugTriggerGracePeriod() => SetHunger(0f);

    [ContextMenu("Debug/Trigger Low Happiness")]
    private void DebugTriggerLowHappiness() => SetHappiness(0.2f);
#endif

    #endregion
}