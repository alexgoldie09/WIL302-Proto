using System;
using System.Collections;
using UnityEngine;

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
public abstract class StructureBase : MonoBehaviour, IHandler, IBiomeOccupant
{
    public enum StructureStage { UnderConstruction, Built }
    
    [Header("Structure Identity")]
    [SerializeField, Tooltip("The biome this structure belongs to. " +
                             "Must match the Slot it is placed in.")]
    private BiomeManager.BiomeType homeBiome;

    [Header("Construction")]
    [SerializeField, Tooltip("Time in seconds to complete construction.")]
    private float constructionDuration = 30f;

    [Header("Sprites")]
    [SerializeField, Tooltip("Sprite shown while the structure is under construction.")]
    private Sprite constructionSprite;
    [SerializeField, Tooltip("Sprite shown once the structure is fully built.")]
    private Sprite builtSprite;

    [Header("References")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] protected LayerMask interactableLayer;
    
    private StructureStage _stage = StructureStage.UnderConstruction;
    private float _constructionTimer;
    private Slot _parentSlot;
    
    /// <summary>Fired when construction completes and the structure becomes active.</summary>
    public event Action OnConstructionComplete;
    
    public StructureStage Stage       => _stage;
    public bool IsBuilt               => _stage == StructureStage.Built;

    /// <summary>0-1 progress through the construction timer. Always 1 when built.</summary>
    public float ConstructionProgress => _stage == StructureStage.Built
        ? 1f
        : Mathf.Clamp01(_constructionTimer / constructionDuration);

    /// <summary>The biome this structure belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => homeBiome;

    #region Unity Lifecycle
    protected virtual void Start()
    {
        UpdateSprite();
        StartCoroutine(ConstructionRoutine());
    }

    protected virtual void OnEnable()
    {
        BiomeManager.Instance?.RegisterOccupant(this);

        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    protected virtual void OnDisable()
    {
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
    public virtual void OnTapped() { }
    #endregion

    #region Construction Methods
    /// <summary>
    /// Ticks the construction timer and transitions to Built on completion.
    /// </summary>
    private IEnumerator ConstructionRoutine()
    {
        _constructionTimer = 0f;
        _stage = StructureStage.UnderConstruction;
        UpdateSprite();

        Debug.Log($"[{gameObject.name}] Construction started. Duration: {constructionDuration}s");

        while (_constructionTimer < constructionDuration)
        {
            _constructionTimer += Time.deltaTime;
            yield return null;
        }

        _constructionTimer = constructionDuration;
        _stage = StructureStage.Built;
        UpdateSprite();

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

    #region Abstract methods
    /// <summary>
    /// Called once when construction completes.
    /// Override in functional subclasses to activate structure behaviour
    /// e.g. start applying decay reduction, activate trigger collider, etc.
    /// Leave unimplemented for aesthetic structures.
    /// </summary>
    protected virtual void OnBuilt() { }
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