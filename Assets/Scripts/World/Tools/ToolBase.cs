using System;
using UnityEngine;

/// <summary>
/// Abstract base class for all draggable tools (WateringCan, ChoppingAxe, ShearingTool, etc.).
/// 
/// Responsibilities:
///   - Subscribes to InputManager world drag events to move the tool through the scene.
///   - Raycasts on drag start to confirm the drag began on this tool's handle collider.
///   - Snaps position and rotation back to origin when drag ends.
///   - Disables CameraPanner while dragging so the camera does not pan simultaneously.
///   - Listens to ItemTrigger events from the blade/spout child to detect world object overlap.
///   - Exposes abstract callbacks so subclasses respond to contact without reimplementing input logic.
/// 
/// Expected GameObject hierarchy:
///   ToolGameObject
///   ├── [non-trigger Collider2D]  — handle/body, used for drag pickup raycast
///   ├── ToolBase subclass
///   └── BladeOrSpout (child GameObject)
///       ├── [trigger Collider2D]  — active area, fires overlap events
///       └── ItemTrigger
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class ToolBase : MonoBehaviour, IBiomeOccupant, IUpgradeable
{
    
    [Header("Tool Identity")]
    [SerializeField, Tooltip("The biome this tool belongs to. Used to validate upgrades.")]
    private BiomeManager.BiomeType parentBiome;
    [SerializeField, Tooltip("The upgrade type ID for this tool. Matches UpgradeDefinition.UpgradeTypeId." +
                             "Used to apply upgrades purchased before this tool was placed.")]
    private string toolId;
    
    [Header("Drag Detection")]
    [SerializeField, Tooltip("Layer mask matching the handle collider layer. " +
                             "Only drags that start on this layer and this GameObject are owned.")]
    private LayerMask interactableLayer;
    
    /// <summary>
    /// The GameObject currently overlapping the tool's active area.
    /// Null when nothing is in contact. Subclasses use this to call
    /// type-specific methods e.g. flora.Water(), sheep.Shear().
    /// </summary>
    protected GameObject _currentObject;

    /// <summary>True while the player is actively dragging this tool.</summary>
    protected bool _isDragging;

    /// <summary>World position at scene start — tool snaps back here on drag end.</summary>
    private Vector3 _originPosition;

    /// <summary>World rotation at scene start — tool snaps back here on drag end.</summary>
    private Quaternion _originRotation;

    /// <summary>The ItemTrigger found on the child blade/spout GameObject.</summary>
    private ItemTrigger _itemTrigger;
    
    #region Accessors
    /// <summary>The biome this tool belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => parentBiome;
    /// <summary> The unique id for this tool. </summary>
    public string UpgradeTypeId => toolId;
    #endregion

    #region  Unity Lifecycle
    protected virtual void Awake()
    {
        _originPosition = transform.position;
        _originRotation = transform.rotation;

        _itemTrigger = GetComponentInChildren<ItemTrigger>();

        if (_itemTrigger == null)
            Debug.LogWarning($"[{GetType().Name}] No ItemTrigger found in children. " +
                             $"Add a child GameObject with a trigger Collider2D and ItemTrigger component.");
    }

    private void Start()
    {
        CheckAndApplySelfUpgrade(toolId);
    }

    protected virtual void OnEnable()
    {
        BiomeManager.Instance?.RegisterOccupant(this);
        
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnWorldDragStart += HandleDragStart;
            InputManager.Instance.OnWorldDrag      += HandleDrag;
            InputManager.Instance.OnWorldDragEnd   += HandleDragEnd;
        }

        if (_itemTrigger != null)
        {
            _itemTrigger.OnTriggerEntered += HandleTriggerEnter;
            _itemTrigger.OnTriggerExited  += HandleTriggerExit;
        }
    }

    protected virtual void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnWorldDragStart -= HandleDragStart;
            InputManager.Instance.OnWorldDrag      -= HandleDrag;
            InputManager.Instance.OnWorldDragEnd   -= HandleDragEnd;
        }

        if (_itemTrigger != null)
        {
            _itemTrigger.OnTriggerEntered -= HandleTriggerEnter;
            _itemTrigger.OnTriggerExited  -= HandleTriggerExit;
        }

        // Always restore camera panning — guards against tool being disabled mid-drag.
        CameraPanner.Instance?.SetPanEnabled(true);
    }
    
    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }
    #endregion

    #region Input Handlers
    /// <summary>
    /// Fires when any drag begins in the world.
    /// Raycasts at the start position — only takes ownership if the hit is
    /// this tool's own GameObject on the interactableLayer.
    /// </summary>
    private void HandleDragStart(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider == null || hit.collider.gameObject != gameObject) return;

        _isDragging = true;
        CameraPanner.Instance?.SetPanEnabled(false);
        OnDragStarted();
    }

    /// <summary>
    /// Fires every frame the drag moves.
    /// Moves the tool to the current world position while this tool owns the drag.
    /// </summary>
    private void HandleDrag(Vector2 worldPos)
    {
        if (!_isDragging) return;
        transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
    }

    /// <summary>
    /// Fires when the drag is released.
    /// Clears the current contact, snaps back to origin, re-enables the camera panner.
    /// </summary>
    private void HandleDragEnd(Vector2 worldPos)
    {
        if (!_isDragging) return;

        _isDragging = false;

        // Notify subclass that contact is ending before clearing the reference.
        if (_currentObject != null)
        {
            OnObjectLeft(_currentObject);
            _currentObject = null;
        }

        transform.position = _originPosition;
        transform.rotation = _originRotation;

        CameraPanner.Instance?.SetPanEnabled(true);
        OnDragEnded();
    }
    #endregion
    
    #region Collision Detection
    /// <summary>
    /// Fires when the tool's active area enters a collider.
    /// Passes the hit GameObject to CanInteractWith — if valid, stores the reference
    /// and notifies the subclass via OnObjectTouched.
    /// </summary>
    private void HandleTriggerEnter(Collider2D other)
    {
        if (!_isDragging) return;

        GameObject hit = other.gameObject;
        if (CanInteractWith(hit))
        {
            _currentObject = hit;
            OnObjectTouched(_currentObject);
        }
    }

    /// <summary>
    /// Fires when the tool's active area exits a collider.
    /// Clears the current contact and notifies the subclass via OnObjectLeft.
    /// </summary>
    private void HandleTriggerExit(Collider2D other)
    {
        if (_currentObject != null && other.gameObject == _currentObject)
        {
            OnObjectLeft(_currentObject);
            _currentObject = null;
        }
    }
    #endregion
    
    #region Abstract Methods
    /// <summary>
    /// Filter which GameObjects this tool can interact with.
    /// Called on every trigger enter — return false to ignore the contact entirely.
    /// Override to restrict by component type, state, or any other condition.
    /// Default accepts any GameObject.
    /// </summary>
    protected virtual bool CanInteractWith(GameObject obj) => true;

    /// <summary>
    /// Called once when the drag is first confirmed on this tool's handle collider.
    /// Override for any setup that should happen at drag start.
    /// </summary>
    protected virtual void OnDragStarted() { }

    /// <summary>
    /// Called once when the drag ends and the tool snaps back to origin.
    /// Override to clean up animations, stop coroutines, or reset rotation.
    /// </summary>
    protected virtual void OnDragEnded() { }

    /// <summary>
    /// Called when the tool's active area enters a valid object's collider.
    /// The subclass receives the raw GameObject — cast to the expected type
    /// (FloraBase, FaunaBase, Sheep, etc.) to call type-specific methods.
    /// </summary>
    protected abstract void OnObjectTouched(GameObject obj);

    /// <summary>
    /// Called when the tool's active area exits a valid object's collider,
    /// or when the drag ends while still overlapping an object.
    /// Use this to stop coroutines, hide stat bars, reset visuals etc.
    /// </summary>
    protected abstract void OnObjectLeft(GameObject obj);
    
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
}