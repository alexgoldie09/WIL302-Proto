using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Serialisable save data for Cow.
/// </summary>
[System.Serializable]
public class CowData : FaunaData
{
    public float growthTimer;
    public bool hasMilkReady;
}

/// <summary>
/// Cow fauna. Grows Baby -> Middle -> Adult.
/// Wanders randomly across the Farm tilemap, flipping sprite on direction change.
/// At Adult, produces milk on a timer. Shakes gently ONLY when milk is ready to collect.
/// No special sprite for the ready state — uses the standard adult sprite throughout.
/// </summary>
public class Cow : FaunaBase
{
    [Header("Cow - Growth")]
    [SerializeField] private float babyToMiddleDuration = 150f;
    [SerializeField] private float middleToAdultDuration = 200f;
    [SerializeField] private float happinessGrowthThreshold = 0.4f;

    [Header("Cow - Feeding")]
    [SerializeField] private float baseHappinessRestore = 0.1f;
    [SerializeField] private float happinessUpgradeAmountIncrease = 0.2f;
    [SerializeField, Tooltip("Happiness multiplier applied when fed the favourite item.")]
    private float favouriteFoodHappinessMultiplier = 2.5f;
    [SerializeField, Tooltip("The favourite ItemDefinition (e.g. wheat).")]
    private ItemDefinition favouriteFoodItem;

    [Header("Cow - Milk Production")]
    [SerializeField] private ItemDefinition milkItem;
    [SerializeField] private float milkProductionInterval = 90f;
    [SerializeField] private float milkProductionVariation = 15f;

    [Header("Cow - Shake (Milk Ready Only)")]
    [SerializeField] private float shakeAngle = 6f;
    [SerializeField] private float shakeDuration = 0.5f;

    [Header("Cow - Wandering")]
    [SerializeField, Tooltip("The Farm tilemap used to validate wander positions.")]
    private Tilemap farmTilemap;
    [SerializeField, Tooltip("Invisible collision tilemap — tiles painted here block animal movement.")]
    private Tilemap collisionTilemap;
    [SerializeField] private float wanderInterval = 3f;
    [SerializeField] private float wanderVariation = 1f;
    [SerializeField] private float wanderSpeed = 2f;

    [Header("Cow - References")]
    [SerializeField] private SpriteRenderer cowSpriteRenderer;

    [Header("Cow - Alerts")]
    [SerializeField] private float lowHungerThreshold = 0.3f;
    [SerializeField] private float lowHappinessThreshold = 0.3f;
    [SerializeField] private Vector2 alertOffset = new Vector2(5f, 14f);

    private float _growthTimer;
    private bool _hasMilkReady;

    private Coroutine _milkCoroutine;
    private Coroutine _shakeCoroutine;
    private Coroutine _wanderCoroutine;

    private bool _lowHungerAlertActive;
    private bool _graceAlertActive;
    private bool _unhappyAlertActive;

    #region SaveableBehaviour

    public override string RecordType => "Cow";
    public override int LoadPriority => 10;

    protected override FaunaData BuildData() => new CowData
    {
        hunger           = hunger,
        happiness        = happiness,
        stage            = stage,
        gracePeriodTimer = GracePeriodTimer,
        inGracePeriod    = InGracePeriod,
        growthTimer      = _growthTimer,
        hasMilkReady     = _hasMilkReady
    };

    protected override void ApplyData(FaunaData data, SaveContext context)
    {
        base.ApplyData(data, context);
        if (data is CowData d)
        {
            _growthTimer = d.growthTimer;
            if (d.hasMilkReady)
                SetMilkReady(true);
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Start()
    {
        base.Start();

        OnHungerChanged    += HandleHungerChanged;
        OnHappinessChanged += HandleHappinessChanged;
        OnLost             += HandleLost;

        if (stage == FaunaGrowthStage.Adult && !_hasMilkReady)
            StartMilkProduction();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        if (farmTilemap == null)
            farmTilemap = BiomeManager.Instance?.GetBiomeByType(HomeBiome)?.groundTilemap;
        
        if (collisionTilemap == null)
            collisionTilemap = BiomeManager.Instance?.GetBiomeByType(HomeBiome)?.collisionTilemap;

        if (_wanderCoroutine != null)
            StopCoroutine(_wanderCoroutine);
        _wanderCoroutine = StartCoroutine(WanderLoop());

        if (stage == FaunaGrowthStage.Adult && _milkCoroutine == null && !_hasMilkReady)
            StartMilkProduction();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        StopAndClear(ref _wanderCoroutine);
        StopAndClear(ref _milkCoroutine);
        StopAndClear(ref _shakeCoroutine);
    }

    private void OnDestroy()
    {
        OnHungerChanged    -= HandleHungerChanged;
        OnHappinessChanged -= HandleHappinessChanged;
        OnLost             -= HandleLost;

        if (AlertManager.Instance != null)
            AlertManager.Instance.ClearAllAlerts(gameObject);
    }

    #endregion

    #region FaunaBase - Abstract Implementations

    public override void OnTapped()
    {
        if (_hasMilkReady)
            CollectMilk();
        else if (FeedPanelUI.Instance != null)
            FeedPanelUI.Instance.Open(this, transform.position);
    }

    public override void Feed(ItemDefinition item)
    {
        if (item == null) return;
        if (!item.IsAvailableForFeeding)
        {
            Debug.LogWarning($"[Cow] {item.ItemName} is not feedable.");
            return;
        }

        if (!PlayerInventory.Instance.Remove(item, 1))
        {
            Debug.LogWarning($"[Cow] Could not remove {item.ItemName} from inventory.");
            return;
        }

        SetHunger(hunger + item.BaseFeedValue);

        float happinessRestore = baseHappinessRestore;
        if (favouriteFoodItem != null && item == favouriteFoodItem)
            happinessRestore *= favouriteFoodHappinessMultiplier;

        SetHappiness(happiness + happinessRestore);
        Debug.Log($"[Cow] Fed {item.ItemName}. Hunger: {hunger:F2} Happiness: {happiness:F2}");
    }

    protected override void Grow()
    {
        if (stage == FaunaGrowthStage.Adult) return;
        if (happiness < happinessGrowthThreshold) return;

        _growthTimer += Time.deltaTime;

        float required = stage == FaunaGrowthStage.Baby
            ? babyToMiddleDuration
            : middleToAdultDuration;

        if (_growthTimer >= required)
        {
            _growthTimer = 0f;
            var next = stage == FaunaGrowthStage.Baby
                ? FaunaGrowthStage.Middle
                : FaunaGrowthStage.Adult;

            SetStage(next);

            if (next == FaunaGrowthStage.Adult)
                StartMilkProduction();
        }
    }

    protected override void ProduceOutput(ItemDefinition item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Cow] ProduceOutput called but milkItem is null.");
            return;
        }

        SetMilkReady(true);
        Debug.Log("[Cow] Milk is ready to collect.");
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        baseHappinessRestore = Mathf.Clamp(
            baseHappinessRestore + happinessUpgradeAmountIncrease, 0f, 1f);
    }

    #endregion

    #region Milk Collection

    private void SetMilkReady(bool ready)
    {
        _hasMilkReady = ready;

        if (ready)
        {
            StartReadyShakeLoop();
            AlertManager.Instance?.ShowAlert(gameObject, AlertType.ReadyToCollect, alertOffset, persistent: true);
        }
        else
        {
            StopAndClear(ref _shakeCoroutine);
            transform.rotation = Quaternion.identity;
            AlertManager.Instance?.ClearAlert(gameObject, AlertType.ReadyToCollect);
        }
    }

    private void CollectMilk()
    {
        if (milkItem == null)
        {
            Debug.LogWarning("[Cow] Cannot collect milk — milkItem is not assigned.");
            return;
        }

        PlayerInventory.Instance.Add(milkItem, 1);
        SetMilkReady(false);
        StartMilkProduction();
        Debug.Log("[Cow] Milk collected.");
    }

    #endregion

    #region Milk Production

    private void StartMilkProduction()
    {
        if (_milkCoroutine != null) return;
        _milkCoroutine = StartCoroutine(MilkProductionLoop());
        Debug.Log("[Cow] Milk production started.");
    }

    private IEnumerator MilkProductionLoop()
    {
        float interval = milkProductionInterval
            + Random.Range(-milkProductionVariation, milkProductionVariation);
        interval = Mathf.Max(5f, interval);
        yield return new WaitForSeconds(interval);

        if (!IsLost)
            ProduceOutput(milkItem);

        _milkCoroutine = null;
    }

    #endregion

    #region Shake (Harvest Ready Only)

    private void StartReadyShakeLoop()
    {
        StopAndClear(ref _shakeCoroutine);
        _shakeCoroutine = StartCoroutine(ReadyShakeLoop());
    }

    private IEnumerator ReadyShakeLoop()
    {
        while (_hasMilkReady && !IsLost)
        {
            yield return StartCoroutine(ShakeOnce());
            yield return new WaitForSeconds(shakeDuration * 0.5f);
        }
    }

    private IEnumerator ShakeOnce()
    {
        float elapsed = 0f;
        Quaternion original = transform.rotation;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float angle = Mathf.Sin((elapsed / shakeDuration) * Mathf.PI * 2f) * shakeAngle;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }

        transform.rotation = original;
    }

    #endregion

    #region Wandering

    private IEnumerator WanderLoop()
    {
        while (!IsLost)
        {
            float wait = wanderInterval + Random.Range(-wanderVariation, wanderVariation);
            wait = Mathf.Max(1f, wait);
            yield return new WaitForSeconds(wait);

            Vector3Int targetCell = GetRandomAdjacentTile();
            if (targetCell != Vector3Int.zero)
                yield return StartCoroutine(GlideToTile(targetCell));
        }
    }

    private Vector3Int GetRandomAdjacentTile()
    {
        if (farmTilemap == null)
        {
            Debug.LogWarning("[Cow] farmTilemap is not assigned!");
            return Vector3Int.zero;
        }

        Vector3Int currentCell = farmTilemap.WorldToCell(transform.position);

        var directions = new List<Vector3Int>
        {
            currentCell + Vector3Int.up,
            currentCell + Vector3Int.down,
            currentCell + Vector3Int.left,
            currentCell + Vector3Int.right
        };

        // Shuffle.
        for (int i = directions.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }

        foreach (var dir in directions)
        {
            if (farmTilemap.HasTile(dir) && 
                (collisionTilemap == null || !collisionTilemap.HasTile(dir)))
                return dir;
        }

        return Vector3Int.zero;
    }

    private IEnumerator GlideToTile(Vector3Int targetCell)
    {
        Vector3 start  = transform.position;
        Vector3 target = farmTilemap.GetCellCenterWorld(targetCell);
        target.z = start.z;

        if (cowSpriteRenderer != null)
            cowSpriteRenderer.flipX = target.x > start.x;

        float distance = Vector3.Distance(start, target);
        float duration = distance / wanderSpeed;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        transform.position = target;
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
        AlertManager.Instance?.ClearAllAlerts(gameObject);
    }

    #endregion

    #region Helpers

    private void StopAndClear(ref Coroutine coroutine)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
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
        StartMilkProduction();
    }

    [ContextMenu("Debug/Force Milk Ready")]
    private void DebugForceMilkReady() => ProduceOutput(milkItem);

    [ContextMenu("Debug/Trigger Low Hunger")]
    private void DebugTriggerLowHunger() => SetHunger(0.2f);

    [ContextMenu("Debug/Trigger Grace Period")]
    private void DebugTriggerGracePeriod() => SetHunger(0f);

    [ContextMenu("Debug/Trigger Low Happiness")]
    private void DebugTriggerLowHappiness() => SetHappiness(0.2f);
#endif

    #endregion
}