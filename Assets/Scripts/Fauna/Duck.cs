using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Serialisable save data for Duck.
/// </summary>
[System.Serializable]
public class DuckData : FaunaData
{
    public float growthTimer;
}

/// <summary>
/// Duck fauna. Grows from Baby to Adult over time gated by happiness.
/// Only produces eggs at Adult stage on an interval timer with slight variation.
/// Wanders randomly across the Pond tilemap, flipping sprite on direction change.
/// </summary>
public class Duck : FaunaBase
{
    [Header("Duck - Growth")]
    [SerializeField, Tooltip("Time in seconds to grow from Baby to Middle.")]
    private float babyToMiddleDuration = 120f;
    [SerializeField, Tooltip("Time in seconds to grow from Middle to Adult.")]
    private float middleToAdultDuration = 180f;
    [SerializeField, Tooltip("Minimum happiness required to progress growth.")]
    private float happinessGrowthThreshold = 0.4f;

    [Header("Duck - Feeding")]
    [SerializeField, Tooltip("Base happiness restored by any feedable item.")]
    private float baseHappinessRestore = 0.1f;
    [SerializeField, Tooltip("Additional happiness restore amount added by each upgrade, applied additively and capped at 1.0.")]
    private float happinessUpgradeAmountIncrease = 0.2f;
    [SerializeField, Tooltip("Happiness multiplier applied when fed bread.")]
    private float breadHappinessMultiplier = 2.5f;
    [SerializeField, Tooltip("The bread ItemDefinition.")]
    private ItemDefinition breadItem;

    [Header("Duck - Egg Production")]
    [SerializeField, Tooltip("The egg ItemDefinition added to PlayerInventory on production.")]
    private ItemDefinition eggItem;
    [SerializeField, Tooltip("Prefab spawned in the world when an egg is produced.")]
    private GameObject eggPrefab;
    [SerializeField, Tooltip("Base interval in seconds between egg production.")]
    private float eggProductionInterval = 60f;
    [SerializeField, Tooltip("Random variation added to each egg interval (±seconds).")]
    private float eggProductionVariation = 10f;
    [SerializeField, Tooltip("Radius in tiles around the duck the egg can spawn.")]
    private int eggSpawnRadius = 2;

    [Header("Duck - Wandering")]
    [SerializeField, Tooltip("The Pond tilemap used to validate wander positions.")]
    private Tilemap pondTilemap;
    [SerializeField, Tooltip("Invisible collision tilemap — tiles painted here block animal movement.")]
    private Tilemap collisionTilemap;
    [SerializeField, Tooltip("Time in seconds the duck waits before picking a new tile.")]
    private float wanderInterval = 3f;
    [SerializeField, Tooltip("Random variation on wander interval (±seconds).")]
    private float wanderVariation = 1f;
    [SerializeField, Tooltip("Speed the duck glides between tiles in world units per second.")]
    private float wanderSpeed = 2f;

    [Header("Duck - Alerts")]
    [SerializeField, Tooltip("Hunger threshold below which the low hunger alert fires.")]
    private float lowHungerThreshold = 0.3f;
    [SerializeField, Tooltip("Happiness threshold below which the unhappy alert fires.")]
    private float lowHappinessThreshold = 0.3f;
    [SerializeField, Tooltip("World-space offset for alerts above the duck's head.")]
    private Vector2 alertOffset = new Vector2(5f, 14f);

    [Header("Duck - References")]
    [SerializeField] private SpriteRenderer duckSpriteRenderer;

    private float _growthTimer;
    private Coroutine _eggCoroutine;
    private Coroutine _wanderCoroutine;

    private bool _lowHungerAlertActive;
    private bool _graceAlertActive;
    private bool _unhappyAlertActive;

    #region SaveableBehaviour

    public override string RecordType => "Duck";
    public override int LoadPriority => 10;
    
    protected override FaunaData DeserializeData(string json) => JsonUtility.FromJson<DuckData>(json);

    protected override FaunaData BuildData() => new DuckData
    {
        hunger = hunger,
        happiness = happiness,
        stage = stage,
        gracePeriodTimer = GracePeriodTimer,
        inGracePeriod = InGracePeriod,
        growthTimer = _growthTimer
    };

    protected override void ApplyData(FaunaData data, SaveContext context)
    {
        base.ApplyData(data, context);
        if (data is DuckData duckData)
            _growthTimer = duckData.growthTimer;
    }

    #endregion

    #region Unity Lifecycle

    protected override void Start()
    {
        base.Start();

        // Subscribe to stat change events
        OnHungerChanged += HandleHungerChanged;
        OnHappinessChanged += HandleHappinessChanged;
        OnLost += HandleLost;
        
        if (stage == FaunaGrowthStage.Baby)
            AudioManager.Instance?.PlaySFX("duck_quack", 0.4f);

        if (stage == FaunaGrowthStage.Adult)
            StartEggProduction();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        if (pondTilemap == null)
            pondTilemap = BiomeManager.Instance?.GetBiomeByType(HomeBiome)?.groundTilemap;
        
        if (collisionTilemap == null)
            collisionTilemap = BiomeManager.Instance?.GetBiomeByType(HomeBiome)?.collisionTilemap;

        if (_wanderCoroutine != null)
            StopCoroutine(_wanderCoroutine);
        _wanderCoroutine = StartCoroutine(WanderLoop());

        if (stage == FaunaGrowthStage.Adult && _eggCoroutine == null)
            StartEggProduction();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (_wanderCoroutine != null)
        {
            StopCoroutine(_wanderCoroutine);
            _wanderCoroutine = null;
        }

        if (_eggCoroutine != null)
        {
            StopCoroutine(_eggCoroutine);
            _eggCoroutine = null;
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
        AudioManager.Instance?.PlaySFX("menu_click", 0.4f);
        
        if (FeedPanelUI.Instance != null)
            FeedPanelUI.Instance.Open(this, transform.position);
    }

    public override void Feed(ItemDefinition item)
    {
        if (item == null) return;
        if (!item.IsAvailableForFeeding)
        {
            Debug.LogWarning($"[Duck] {item.ItemName} is not feedable.");
            return;
        }

        if (!PlayerInventory.Instance.Remove(item, 1))
        {
            Debug.LogWarning($"[Duck] Could not remove {item.ItemName} from inventory.");
            return;
        }

        SetHunger(hunger + item.BaseFeedValue);

        float happinessRestore = baseHappinessRestore;
        if (breadItem != null && item == breadItem)
            happinessRestore *= breadHappinessMultiplier;

        SetHappiness(happiness + happinessRestore);
        
        QuestManager.Instance?.RecordProgress(
            QuestObjectiveType.FeedAnimal, item.ItemName, 1, RecordType);

        Debug.Log($"[Duck] Fed {item.ItemName}. Hunger: {hunger:F2} Happiness: {happiness:F2}");
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
                StartEggProduction();
        }
    }

    protected override void ProduceOutput(ItemDefinition item)
    {
        if (item == null)
        {
            Debug.LogWarning("[Duck] ProduceOutput called but item is null — is eggItem assigned?");
            return;
        }

        if (eggPrefab != null)
            SpawnEggInWorld();
        
        AudioManager.Instance?.PlaySFX("duck_quack", 0.4f);

        // Show ready to collect alert briefly
        if (AlertManager.Instance != null)
            AlertManager.Instance.ShowAlert(gameObject, AlertType.ReadyToCollect, alertOffset);

        Debug.Log($"[Duck] Produced 1x {item.ItemName}.");
    }

    public override void ApplyUpgrade(UpgradeDefinition upgrade)
    {
        baseHappinessRestore = Mathf.Clamp(baseHappinessRestore + happinessUpgradeAmountIncrease, 0f, 1f);
    }

    #endregion

    #region Alert Handling

    private void HandleHungerChanged(float newHunger)
    {
        if (AlertManager.Instance == null) return;

        if (newHunger <= 0f)
        {
            // Grace period — urgent alert, clear low hunger warning
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
            // Low hunger warning
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
            // Hunger restored — clear both hunger alerts
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

    #region Egg Spawning

    private void SpawnEggInWorld()
    {
        Vector3Int duckCell = pondTilemap.WorldToCell(transform.position);

        var candidates = new List<Vector3Int>();
        for (int x = -eggSpawnRadius; x <= eggSpawnRadius; x++)
        {
            for (int y = -eggSpawnRadius; y <= eggSpawnRadius; y++)
            {
                if (x == 0 && y == 0) continue;
                Vector3Int candidate = duckCell + new Vector3Int(x, y, 0);
                if (pondTilemap.HasTile(candidate))
                    candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[Duck] No valid tiles to spawn egg near {gameObject.name}.");
            return;
        }

        Vector3Int chosenCell = candidates[Random.Range(0, candidates.Count)];
        Vector3 spawnPos = pondTilemap.GetCellCenterWorld(chosenCell);
        spawnPos.z = transform.position.z;
        var biomeRoot = BiomeManager.Instance?.GetBiomeByType(HomeBiome)?.rootObject?.transform;
        Instantiate(eggPrefab, spawnPos, Quaternion.identity, biomeRoot);

        Debug.Log($"[Duck] Egg spawned at tile {chosenCell}.");
    }

    #endregion

    #region Egg Production

    private void StartEggProduction()
    {
        if (_eggCoroutine != null) return;
        _eggCoroutine = StartCoroutine(EggProductionLoop());
        Debug.Log($"[Duck] Egg production started.");
    }

    private IEnumerator EggProductionLoop()
    {
        while (stage == FaunaGrowthStage.Adult && !IsLost)
        {
            float interval = eggProductionInterval + Random.Range(-eggProductionVariation, eggProductionVariation);
            interval = Mathf.Max(5f, interval);
            yield return new WaitForSeconds(interval);

            if (!IsLost)
                ProduceOutput(eggItem);
        }

        _eggCoroutine = null;
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
        if (pondTilemap == null)
        {
            Debug.LogWarning("[Duck] pondTilemap is not assigned!");
            return Vector3Int.zero;
        }

        Vector3Int currentCell = pondTilemap.WorldToCell(transform.position);

        var directions = new List<Vector3Int>
        {
            currentCell + Vector3Int.up,
            currentCell + Vector3Int.down,
            currentCell + Vector3Int.left,
            currentCell + Vector3Int.right
        };

        for (int i = directions.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (directions[i], directions[j]) = (directions[j], directions[i]);
        }

        foreach (var dir in directions)
        {
            if (pondTilemap.HasTile(dir) && 
                (collisionTilemap == null || !collisionTilemap.HasTile(dir)))
                return dir;
        }

        return Vector3Int.zero;
    }

    private IEnumerator GlideToTile(Vector3Int targetCell)
    {
        Vector3 start = transform.position;
        Vector3 target = pondTilemap.GetCellCenterWorld(targetCell);
        target.z = start.z;

        if (duckSpriteRenderer != null)
            duckSpriteRenderer.flipX = target.x > start.x;

        float distance = Vector3.Distance(start, target);
        float duration = distance / wanderSpeed;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, target, elapsed / duration);
            yield return null;
        }

        transform.position = target;
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
        StartEggProduction();
    }

    [ContextMenu("Debug/Force Produce Egg")]
    private void DebugProduceEgg() => ProduceOutput(eggItem);

    [ContextMenu("Debug/Trigger Low Hunger")]
    private void DebugTriggerLowHunger() => SetHunger(0.2f);

    [ContextMenu("Debug/Trigger Grace Period")]
    private void DebugTriggerGracePeriod() => SetHunger(0f);

    [ContextMenu("Debug/Trigger Low Happiness")]
    private void DebugTriggerLowHappiness() => SetHappiness(0.2f);
#endif

    #endregion
}