using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }
    public event Action<Vector2> OnWorldTap;
    public event Action<Vector2> OnScreenTap;
    public event Action<Vector2> OnScreenDrag;
    public event Action<Vector2> OnWorldDragStart;
    public event Action<Vector2> OnWorldDrag;
    public event Action<Vector2> OnWorldDragEnd;

    [Header("Settings")]
    [Tooltip("Maximum pixel distance to still count as a tap (not a drag).")]
    [SerializeField] private float tapThresholdPixels = 20f;

    private Camera  _mainCamera;
    private Vector2 _startPos;
    private Vector2 _lastDragPos;
    private bool    _inputEnabled = true;
    private bool    _inputBegan;
    private bool    _isDragging;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnhancedTouchSupport.Enable();
    }

    private void Start() => _mainCamera = Camera.main;

    private void OnDestroy() => EnhancedTouchSupport.Disable();

    private void Update()
    {
        HandleMouseInput();
        HandleTouchInput();
    }
    
    public void SetInputEnabled(bool isEnabled) => _inputEnabled = isEnabled;

    // Mouse (PC / Editor)
    private void HandleMouseInput()
    {
        // In the Device Simulator, mouse clicks are converted to Touch events.
        // If active touches exist this frame, let HandleTouchInput own the gesture.
        if (Touch.activeTouches.Count > 0) return;
        
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasPressedThisFrame)
            BeginInput(mouse.position.ReadValue());

        if (mouse.leftButton.isPressed && _inputBegan)
            UpdateDrag(mouse.position.ReadValue());

        if (mouse.leftButton.wasReleasedThisFrame && _inputBegan)
            EndInput(mouse.position.ReadValue());
    }

    // Touch (Mobile)
    private void HandleTouchInput()
    {
        if (Touch.activeTouches.Count == 0) return;
        Touch touch = Touch.activeTouches[0];

        switch (touch.phase)
        {
            case TouchPhase.Began:
                BeginInput(touch.screenPosition);
                break;
            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (_inputBegan) UpdateDrag(touch.screenPosition);
                break;
            case TouchPhase.Ended:
                if (_inputBegan) EndInput(touch.screenPosition);
                break;
            case TouchPhase.Canceled:
                _inputBegan = false;
                _isDragging = false;
                break;
        }
    }

    #region Shared Gesture Inputs
    private void BeginInput(Vector2 screenPos)
    {
        if (!_inputEnabled) return;
        
        _startPos     = screenPos;
        _lastDragPos  = screenPos;
        _inputBegan   = true;
        _isDragging   = false;
    }

    private void UpdateDrag(Vector2 currentPos)
    {
        // Promote to drag once the finger/cursor moves past the tap threshold
        if (!_isDragging && Vector2.Distance(_startPos, currentPos) > tapThresholdPixels)
        {
            _isDragging = true;
            if (_mainCamera == null) _mainCamera = Camera.main;
            Vector2 worldStart = _mainCamera.ScreenToWorldPoint(_startPos);
            OnWorldDragStart?.Invoke(worldStart);
        }

        if (!_isDragging) return;

        Vector2 delta = currentPos - _lastDragPos;
        if (delta.sqrMagnitude > 0.001f)
        {
            OnScreenDrag?.Invoke(delta);
            if (_mainCamera == null) _mainCamera = Camera.main;
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(currentPos);
            OnWorldDrag?.Invoke(worldPos);
        }

        _lastDragPos = currentPos;
    }

    private void EndInput(Vector2 screenPos)
    {
        _inputBegan = false;

        if (_isDragging)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            Vector2 worldPos = _mainCamera.ScreenToWorldPoint(screenPos);
            OnWorldDragEnd?.Invoke(worldPos);
        }
        else
        {
            // Gesture never left the tap radius — it's a tap.
            RegisterTap(screenPos);
        }

        _isDragging = false;
    }
    #endregion
    
    #region Tap Handling
    private void RegisterTap(Vector2 screenPos)
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        OnScreenTap?.Invoke(screenPos);

        if (_mainCamera == null) _mainCamera = Camera.main;
        Vector2 worldPos = _mainCamera.ScreenToWorldPoint(screenPos);
        OnWorldTap?.Invoke(worldPos);
    }

    public void RequestBiomeSwitch(int biomeIndex) =>
        CameraSwapper.Instance?.SwitchToBiome(biomeIndex);
    #endregion
}