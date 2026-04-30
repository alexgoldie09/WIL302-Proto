using UnityEngine;

/// <summary>
/// World-space quest object. Sits in a biome until the player taps it to pick up the quest.
/// Activated and deactivated by QuestManager as quests progress.
/// </summary>
public class Quest : MonoBehaviour, IHandler, IBiomeOccupant
{
    [Header("Quest Data")]
    [SerializeField] private QuestDefinition questDefinition;

    [Header("Biome")]
    [SerializeField] private BiomeManager.BiomeType homeBiome;

    [Header("References")]
    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField] private LayerMask interactableLayer;
    
    [Header("Bobbing")]
    [Tooltip("If enabled, the pickup will bob up and down.")]
    [SerializeField] private bool enableBobbing = true;
    [Tooltip("How high the bob moves (world units).")]
    [SerializeField] private float bobAmplitude = 0.1f;
    [Tooltip("How fast the bob cycles (cycles per second).")]
    [SerializeField] private float bobFrequency = 0.4f;
    [Tooltip("Randomizes the bob start so multiple pickups don't move in sync.")]
    [SerializeField] private bool randomizePhase = true;
    
    private Vector3 _baseWorldPos;
    private float _phaseOffset;

    // ── State ─────────────────────────────────────────────────────────────────
    public bool IsPickedUp  { get; private set; }
    public bool IsCompleted { get; private set; }

    // ── IBiomeOccupant ────────────────────────────────────────────────────────
    public BiomeManager.BiomeType HomeBiome => homeBiome;

    // ── Public accessors ──────────────────────────────────────────────────────
    public QuestDefinition Definition => questDefinition;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (questDefinition != null && iconRenderer != null)
            iconRenderer.sprite = questDefinition.questIcon;
    }

    private void OnEnable()
    {
        BiomeManager.Instance?.RegisterOccupant(this);

        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
        
        // Cache snapped base position for bobbing.
        _baseWorldPos = transform.position;

        if (randomizePhase)
            _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }

    private void Update()
    {
        if (!enableBobbing)
            return;

        // Sin wave in world-space (keeps it simple).
        float t = Time.time * bobFrequency * Mathf.PI * 2f + _phaseOffset;
        float yOffset = Mathf.Sin(t) * bobAmplitude;

        transform.position = _baseWorldPos + (Vector3.up * yOffset);
    }

    // ── IHandler ──────────────────────────────────────────────────────────────
    private void HandleWorldTap(Vector2 worldPos)
    {
        if (IsPickedUp) return;

        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        if (IsPickedUp) return;
        QuestManager.Instance?.PickUpQuest(this);
    }

    // ── Called by QuestManager ────────────────────────────────────────────────
    /// <summary>Marks this quest as picked up and hides the world icon.</summary>
    public void MarkPickedUp()
    {
        IsPickedUp = true;

        if (iconRenderer != null)
            iconRenderer.enabled = false;

        // Disable collider so further taps pass through.
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
    }

    /// <summary>Marks this quest as completed.</summary>
    public void MarkCompleted()
    {
        IsCompleted = true;
    }

    /// <summary>Resets quest state — used when restoring save data.</summary>
    public void MarkCompletedSilent()
    {
        IsPickedUp  = true;
        IsCompleted = true;

        if (iconRenderer != null) iconRenderer.enabled = false;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        gameObject.SetActive(false);
    }

    // ── Debug ─────────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Debug/Force Pick Up")]
    private void DebugForcePickUp() => QuestManager.Instance?.PickUpQuest(this);

    [ContextMenu("Debug/Force Complete")]
    private void DebugForceComplete() => QuestManager.Instance?.CompleteQuest();
#endif
}