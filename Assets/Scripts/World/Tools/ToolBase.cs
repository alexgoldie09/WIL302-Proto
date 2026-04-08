using UnityEngine;

/// <summary>
/// Abstract base class for all draggable tools (WateringCan, ChoppingAxe, etc.).
/// Handles drag input via InputManager, origin snap on release, camera panner
/// disable/enable, and flora trigger detection via a ToolTrigger child component.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class ToolBase : MonoBehaviour
{
    [Header("Drag Detection")]
    [SerializeField, Tooltip("Layer mask for the handle collider used to detect drag pickup.")]
    private LayerMask interactableLayer;
    
    protected FloraBase _currentFlora;
    protected bool _isDragging;

    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private ToolTrigger _toolTrigger;

    #region Unity Lifecycle
    protected virtual void Awake()
    {
        _originPosition = transform.position;
        _originRotation = transform.rotation;

        _toolTrigger = GetComponentInChildren<ToolTrigger>();
        if (_toolTrigger == null)
            Debug.LogWarning($"[{GetType().Name}] No ToolTrigger found in children. " +
                             $"Add a child GameObject with a trigger Collider2D and ToolTrigger component.");
    }

    protected virtual void OnEnable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnWorldDragStart += HandleDragStart;
            InputManager.Instance.OnWorldDrag += HandleDrag;
            InputManager.Instance.OnWorldDragEnd += HandleDragEnd;
        }

        if (_toolTrigger != null)
        {
            _toolTrigger.OnTriggerEntered += HandleTriggerEnter;
            _toolTrigger.OnTriggerExited  += HandleTriggerExit;
        }
    }

    protected virtual void OnDisable()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnWorldDragStart -= HandleDragStart;
            InputManager.Instance.OnWorldDrag -= HandleDrag;
            InputManager.Instance.OnWorldDragEnd -= HandleDragEnd;
        }

        if (_toolTrigger != null)
        {
            _toolTrigger.OnTriggerEntered -= HandleTriggerEnter;
            _toolTrigger.OnTriggerExited -= HandleTriggerExit;
        }

        CameraPanner.Instance?.SetPanEnabled(true);
    }
    #endregion

    #region Input Handling
    private void HandleDragStart(Vector2 worldPos)
    {
        // Only own the drag if it started on the handle collider.
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider == null || hit.collider.gameObject != gameObject) return;

        _isDragging = true;
        CameraPanner.Instance?.SetPanEnabled(false);
        OnDragStarted();
    }

    private void HandleDrag(Vector2 worldPos)
    {
        if (!_isDragging) return;
        transform.position = new Vector3(worldPos.x, worldPos.y, transform.position.z);
    }

    private void HandleDragEnd(Vector2 worldPos)
    {
        if (!_isDragging) return;

        _isDragging = false;

        if (_currentFlora != null)
        {
            OnFloraLeft(_currentFlora);
            _currentFlora = null;
        }

        transform.position = _originPosition;
        transform.rotation = _originRotation;

        CameraPanner.Instance?.SetPanEnabled(true);
        OnDragEnded();
    }
    #endregion

    #region Collision Handling
    private void HandleTriggerEnter(Collider2D other)
    {
        if (!_isDragging) return;

        var flora = other.GetComponent<FloraBase>();
        if (flora != null && !flora.IsLost && CanInteractWith(flora))
        {
            _currentFlora = flora;
            OnFloraTouched(flora);
        }
    }

    private void HandleTriggerExit(Collider2D other)
    {
        if (_currentFlora != null && other.gameObject == _currentFlora.gameObject)
        {
            OnFloraLeft(_currentFlora);
            _currentFlora = null;
        }
    }
    #endregion
    
    #region Abstract Methods
    /// <summary>
    /// Filter which flora this tool can interact with.
    /// Override to restrict interaction e.g. only Harvestable stage for axe.
    /// Default accepts any non-lost flora.
    /// </summary>
    protected virtual bool CanInteractWith(FloraBase flora) => true;

    /// <summary>Called when the drag is first confirmed on this tool's handle.</summary>
    protected virtual void OnDragStarted() { }

    /// <summary>Called when the drag ends and the tool snaps back to origin.</summary>
    protected virtual void OnDragEnded() { }

    /// <summary>Called when the tool's active area enters a valid flora collider.</summary>
    protected abstract void OnFloraTouched(FloraBase flora);

    /// <summary>Called when the tool's active area exits a flora collider, or drag ends while overlapping.</summary>
    protected abstract void OnFloraLeft(FloraBase flora);
    #endregion
}