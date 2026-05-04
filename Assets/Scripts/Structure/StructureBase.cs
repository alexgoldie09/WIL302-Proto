using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;


/// <summary>
/// Serialisable save data for StructureBase.
/// </summary>
[Serializable]
public class StructureData
{
    public int stage;             // 0 = UnderConstruction, 1 = Built
    public float constructionTimer;
    public string parentSlotGuid;
}

/// <summary>
/// Base class for all placeable structures (functional and aesthetic).
/// 
/// Lifecycle:
///   1. Placed into a Slot via SlotPlacementUI — Slot calls SetSlot() after instantiation.
///   2. Starts in the UnderConstruction stage and begins the construction timer.
///   3. On construction complete, transitions to Built stage and calls OnBuilt().
///   4. Subclasses override OnBuilt() to activate their functionality.
///   5. On destruction, clears the parent slot and deregisters from BiomeManager.
/// 
/// Aesthetic structures need no subclass — they just sit at Built stage doing nothing.
/// Functional structures override OnBuilt() and OnTapped() to implement behaviour.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class StructureBase : SaveableBehaviour<StructureData>, IHandler, IBiomeOccupant, IUpgradeable
{
    public enum StructureStage { UnderConstruction, Built }
    
    [Header("Structure Identity")]
    [SerializeField, Tooltip("The biome this structure belongs to. " +
                             "Must match the Slot it is placed in.")]
    private BiomeManager.BiomeType homeBiome;
    [SerializeField, Tooltip("The upgrade type ID for this structure. Matches UpgradeDefinition.UpgradeTypeId.")]
    private string structureId;

    [Header("Construction")]
    [SerializeField, Tooltip("Time in seconds to complete construction.")]
    private float constructionDuration = 30f;

    [Header("Sprites")]
    [SerializeField, Tooltip("Sprite shown while the structure is under construction.")]
    private Sprite constructionSprite;
    [SerializeField, Tooltip("Sprite shown once the structure is fully built.")]
    private Sprite builtSprite;
    
    [Header("Construction UI")]
    [SerializeField] private Image constructionFillImage;
    [SerializeField] private GameObject statBarsRoot;
    [SerializeField] private float showStatsDuration = 3f;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] protected LayerMask interactableLayer;
    
    protected Collider2D _collider2D;
    private StructureStage _stage = StructureStage.UnderConstruction;
    private float _constructionTimer;
    private Coroutine _hideStatsCoroutine;
    private Slot _parentSlot;
    private bool _wasRestored;
    
    #region Accessors
    /// <summary>Fired when construction completes and the structure becomes active.</summary>
    public event Action OnConstructionComplete;
    
    public StructureStage Stage => _stage;
    public bool IsBuilt  => _stage == StructureStage.Built;

    /// <summary>0-1 progress through the construction timer. Always 1 when built.</summary>
    public float ConstructionProgress => _stage == StructureStage.Built
        ? 1f
        : Mathf.Clamp01(_constructionTimer / constructionDuration);
    
    /// <summary>Exposes the raw construction timer to subclasses for save data building.</summary>
    protected float ConstructionTimer => _constructionTimer;
    
    /// <summary>Exposes the parent slot GUID to subclasses for save data building.</summary>
    protected string ParentSlotGuid => _parentSlot?.PersistentGuid ?? string.Empty;

    /// <summary>The biome this structure belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => homeBiome;
    /// <summary> The unique id for this structure. </summary>
    public string UpgradeTypeId => structureId;
    #endregion

    #region Unity Lifecycle
    protected virtual void Start()
    {
        EnsurePersistentGuid();
        
        UpdateSprite();
        CheckAndApplySelfUpgrade(structureId);
        if (statBarsRoot != null)
            statBarsRoot.SetActive(false);
        if (_collider2D == null)
            _collider2D = GetComponent<Collider2D>();
        
        if (_wasRestored && _stage == StructureStage.Built)
            CompleteConstruction();
        else
        {
            if (!_wasRestored)
                _constructionTimer = 0f;
            StartCoroutine(ConstructionRoutine());
        }
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
    #endregion

    #region Slot
    /// <summary>
    /// Assigns the slot this structure occupies.
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
    /// Called when the structure is tapped.
    /// Override in functional subclasses to open UI or trigger behaviour.
    /// Aesthetic structures leave this empty.
    /// </summary>
    public virtual void OnTapped()
    {
        if (_stage == StructureStage.Built)
            return;

        ShowStats();
    }
    #endregion

    #region Construction Methods
    /// <summary>
    /// Ticks the construction timer and transitions to Built on completion.
    /// </summary>
    private IEnumerator ConstructionRoutine()
    {
        _stage = StructureStage.UnderConstruction;
        UpdateSprite();

        Debug.Log($"[{gameObject.name}] Construction started. Duration: {constructionDuration}s");

        while (_constructionTimer < constructionDuration)
        {
            _constructionTimer += Time.deltaTime;
            if (statBarsRoot != null && statBarsRoot.activeSelf)
            {
                UpdateStatFills();
            }
            yield return null;
        }

        _constructionTimer = constructionDuration;
        CompleteConstruction();
    }
    
    /// <summary>
    /// Transitions the structure to the Built stage, updates the sprite,
    /// hides construction UI, and fires the OnConstructionComplete event.
    /// </summary>
    private void CompleteConstruction()
    {
        _stage = StructureStage.Built;
        UpdateSprite();
        
        HideStats();

        Debug.Log($"[{gameObject.name}] Construction complete.");

        OnConstructionComplete?.Invoke();
        OnBuilt();
    }
    #endregion

    #region Sprite Methods
    private void UpdateSprite()
    {
        if (spriteRenderer == null) return;
        spriteRenderer.sprite = _stage == StructureStage.Built ? builtSprite : constructionSprite;
    }
    #endregion
    
    #region UI Methods
    /// <summary>
    /// Shows the construction progress UI.
    /// If autoHide is true, will automatically hide after a delay.
    /// </summary>
    /// <param name="autoHide"></param>
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
    /// Immediately hides the construction progress UI and cancels any pending auto-hide.
    /// Can be called by subclasses when opening a more detailed UI to prevent overlap.
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
    /// Updates the fill amounts of the construction progress bars based on the current construction progress.
    /// </summary>
    private void UpdateStatFills()
    {
        if (constructionFillImage != null && !IsBuilt)
            constructionFillImage.fillAmount = ConstructionProgress;
        else
            if (constructionFillImage != null)
                constructionFillImage.gameObject.SetActive(false);
    }
    
    /// <summary>
    /// Coroutine to hide the construction progress UI after a delay.
    /// </summary>
    /// <returns></returns>
    private IEnumerator HideStatsAfterDelay()
    {
        yield return new WaitForSeconds(showStatsDuration);

        if (statBarsRoot != null)
            statBarsRoot.SetActive(false);

        _hideStatsCoroutine = null;
    }
    #endregion

    #region Abstract methods
    /// <summary>
    /// Called once when construction completes.
    /// Override in functional subclasses to activate structure behaviour
    /// e.g. start applying decay reduction, activate trigger collider, etc.
    /// Leave unimplemented for aesthetic structures.
    /// </summary>
    protected virtual void OnBuilt() { }
    
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
    
    #region SaveableBehaviour

    public override string RecordType  => "Structure";
    public override int LoadPriority => 8;

    protected override string GetParentGuid() => ParentSlotGuid;

    protected override StructureData BuildData() => new StructureData
    {
        stage             = (int)_stage,
        constructionTimer = _constructionTimer,
        parentSlotGuid    = ParentSlotGuid
    };

    protected override void ApplyData(StructureData data, SaveContext context)
    {
        _wasRestored       = true;
        _stage             = (StructureStage)data.stage;
        _constructionTimer = data.constructionTimer;

        if (!string.IsNullOrEmpty(data.parentSlotGuid))
        {
            var saveable = context.ResolveById(data.parentSlotGuid);
            if (saveable is Slot slot)
                SetSlot(slot);
        }
    }

    #endregion

    #region Debug Method
#if UNITY_EDITOR
    [ContextMenu("Debug/Force Complete Construction")]
    private void DebugForceComplete()
    {
        StopAllCoroutines();
        _constructionTimer = constructionDuration;
        _stage = StructureStage.Built;
        UpdateSprite();
        OnConstructionComplete?.Invoke();
        OnBuilt();
        Debug.Log($"[{gameObject.name}] Debug: Construction forced complete.");
    }
#endif
    #endregion
}