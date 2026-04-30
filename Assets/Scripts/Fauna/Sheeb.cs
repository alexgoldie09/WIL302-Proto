using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Serialisable save data for Sheep.
/// </summary>
[System.Serializable]
public class SheebData : FaunaData
{
    public float growthTimer;
    public bool hasWoolReady;
    public bool isShaven;
}

/// <summary>
/// Sheep fauna. Grows Baby -> Middle -> Adult.
/// Wanders randomly across the Farm tilemap, flipping sprite on direction change.
/// At Adult, produces wool on a timer. Shakes gently ONLY when wool is ready to harvest.
/// On harvest enters a Shaven state (4th sprite), then cycles Adult (shaven) -> Adult (ready).
/// </summary>
public class Sheeb : FaunaBase
{
    [Header("Sheep - Growth")]
    [SerializeField] private float babyToMiddleDuration = 120f;
    [SerializeField] private float middleToAdultDuration = 180f;
    [SerializeField] private float happinessGrowthThreshold = 0.4f;

    [Header("Sheep - Feeding")]
    [SerializeField] private float baseHappinessRestore = 0.1f;
    [SerializeField] private float happinessUpgradeAmountIncrease = 0.2f;
    [SerializeField, Tooltip("Happiness multiplier applied when fed the favourite item.")]
    private float favouriteFoodHappinessMultiplier = 2.5f;
    [SerializeField, Tooltip("The favourite ItemDefinition (e.g. hay).")]
    private ItemDefinition favouriteFoodItem;

    [Header("Sheep - Wool Production")]
    [SerializeField] private ItemDefinition woolItem;
    [SerializeField] private float woolProductionInterval = 90f;
    [SerializeField] private float woolProductionVariation = 15f;

    [Header("Sheep - Sprites")]
    [SerializeField, Tooltip("Sprite shown when wool is ready to harvest.")]
    private Sprite woolReadySprite;
    [SerializeField, Tooltip("Sprite shown immediately after shearing.")]
    private Sprite shavenSprite;
    [SerializeField] private SpriteRenderer sheepSpriteRenderer;

    [Header("Sheep - Shake (Wool Ready Only)")]
    [SerializeField] private float shakeAngle = 6f;
    [SerializeField] private float shakeDuration = 0.5f;

    [Header("Sheep - Wandering")]
    [SerializeField, Tooltip("The Farm tilemap used to validate wander positions.")]
    private Tilemap farmTilemap;
    [SerializeField, Tooltip("Invisible collision tilemap — tiles painted here block animal movement.")]
    private Tilemap collisionTilemap;
    [SerializeField] private float wanderInterval = 3f;
    [SerializeField] private float wanderVariation = 1f;
    [SerializeField] private float wanderSpeed = 2f;

    [Header("Sheep - Alerts")]
    [SerializeField] private float lowHungerThreshold = 0.3f;
    [SerializeField] private float lowHappinessThreshold = 0.3f;
    [SerializeField] private Vector2 alertOffset = new Vector2(5f, 14f);

    private float _growthTimer;
    private bool _hasWoolReady;
    private bool _isShaven;

    private Coroutine _woolCoroutine;
    private Coroutine _shakeCoroutine;
    private Coroutine _wanderCoroutine;

    private bool _lowHungerAlertActive;
    private bool _graceAlertActive;
    private bool _unhappyAlertActive;

    #region SaveableBehaviour

    public override string RecordType => "Sheep";
    public override int LoadPriority => 10;

    protected override FaunaData BuildData() => new SheebData
    {
        hunger           = hunger,
        happiness        = happiness,
        stage            = stage,
        gracePeriodTimer = GracePeriodTimer,
        inGracePeriod    = InGracePeriod,
        growthTimer      = _growthTimer,
        hasWoolReady     = _hasWoolReady,
        isShaven         = _isShaven
    };

    protected override void ApplyData(FaunaData data, SaveContext context)
    {
        base.ApplyData(data, context);
        if (data is SheebData d)
        {
            _growthTimer = d.growthTimer;
            _isShaven    = d.isShaven;
            if (d.hasWoolReady)
                SetWoolReady(true);
            else if (_isShaven)
                ApplyShavenSprite();
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

        if (stage == FaunaGrowthStage.Adult && !_hasWoolReady)
            StartWoolProduction();
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

        if (stage == FaunaGrowthStage.Adult && _woolCoroutine == null && !_hasWoolReady)
            StartWoolProduction();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        StopAndClear(ref _wanderCoroutine);
        StopAndClear(ref _woolCoroutine);
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
        if (_hasWoolReady)
            CollectWool();
        else if (FeedPanelUI.Instance != null)
            FeedPanelUI.Instance.Open(this, transform.position);
    }

    public override void Feed(ItemDefinition item)
    {
        if (item == null) return;
        if (!item.IsAvailableForFeeding)
        {
            Debug.LogWarning($"[Sheep] {item.ItemName} is not feedable.");
            return;
        }

        if (!PlayerInventory.Instance.Remove(item, 1))
        {
            Debug.LogWarning($"[Sheep] Could not remove {item.ItemName} from inventory.");
            return;
        }

        SetHunger(hunger + item.BaseFeedValue);

        float happinessRestore = baseHappinessRestore;
        if (favouriteFoodItem != null && item == favouriteFoodItem)
            happinessRestore *= favouriteFoodHappinessMultiplier;

        SetHappiness(happiness + happinessRestore);
        Debug.Log($"[Sheep] Fed {item.ItemName}. Hunger: {hunger:F2} Happiness: {happiness:F2}");
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
                StartWoolProduction();
        }
    }

    protected override void ProduceOutput(ItemDefinition item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Sheep] ProduceOutput called but woolItem is null.");
            return;
        }

        SetWoolReady(true);
        Debug.Log("[Sheep] Wool is ready to harvest.");
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        baseHappinessRestore = Mathf.Clamp(
            baseHappinessRestore + happinessUpgradeAmountIncrease, 0f, 1f);
    }

    #endregion

    #region Wool Collection

    private void SetWoolReady(bool ready)
    {
        _hasWoolReady = ready;

        if (ready)
        {
            ApplyWoolReadySprite();
            StartReadyShakeLoop();
            AlertManager.Instance?.ShowAlert(gameObject, AlertType.ReadyToCollect, alertOffset, persistent: true);
        }
        else
        {
            // Stop shaking and snap rotation back — wandering resumes naturally.
            StopAndClear(ref _shakeCoroutine);
            transform.rotation = Quaternion.identity;
            AlertManager.Instance?.ClearAlert(gameObject, AlertType.ReadyToCollect);
        }
    }

    private void CollectWool()
    {
        if (woolItem == null)
        {
            Debug.LogWarning("[Sheep] Cannot collect wool — woolItem is not assigned.");
            return;
        }

        PlayerInventory.Instance.Add(woolItem, 1);
        SetWoolReady(false);

        _isShaven = true;
        ApplyShavenSprite();

        StartWoolProduction();
        Debug.Log("[Sheep] Wool collected. Sheep is now shaven.");
    }

    #endregion

    #region Sprites

    private void ApplyWoolReadySprite()
    {
        if (sheepSpriteRenderer != null && woolReadySprite != null)
            sheepSpriteRenderer.sprite = woolReadySprite;
    }

    private void ApplyShavenSprite()
    {
        if (sheepSpriteRenderer != null && shavenSprite != null)
            sheepSpriteRenderer.sprite = shavenSprite;
    }

    #endregion

    #region Wool Production

    private void StartWoolProduction()
    {
        if (_woolCoroutine != null) return;
        _woolCoroutine = StartCoroutine(WoolProductionLoop());
        Debug.Log("[Sheep] Wool production started.");
    }

    private IEnumerator WoolProductionLoop()
    {
        float interval = woolProductionInterval
            + Random.Range(-woolProductionVariation, woolProductionVariation);
        interval = Mathf.Max(5f, interval);
        yield return new WaitForSeconds(interval);

        if (!IsLost)
            ProduceOutput(woolItem);

        _woolCoroutine = null;
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
        while (_hasWoolReady && !IsLost)
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
            Debug.LogWarning("[Sheep] farmTilemap is not assigned!");
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

        if (sheepSpriteRenderer != null)
            sheepSpriteRenderer.flipX = target.x > start.x;

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
        StartWoolProduction();
    }

    [ContextMenu("Debug/Force Wool Ready")]
    private void DebugForceWoolReady() => ProduceOutput(woolItem);

    [ContextMenu("Debug/Trigger Low Hunger")]
    private void DebugTriggerLowHunger() => SetHunger(0.2f);

    [ContextMenu("Debug/Trigger Grace Period")]
    private void DebugTriggerGracePeriod() => SetHunger(0f);

    [ContextMenu("Debug/Trigger Low Happiness")]
    private void DebugTriggerLowHappiness() => SetHappiness(0.2f);
#endif

    #endregion
}