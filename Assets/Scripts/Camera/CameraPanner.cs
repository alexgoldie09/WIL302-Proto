using UnityEngine;

public class CameraPanner : MonoBehaviour
{
    public static CameraPanner Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private Camera targetCamera;

    [Header("Settings")]
    [SerializeField] private float panSpeed = 1f;

    private bool _panEnabled = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        // Subscribe — InputManager owns all input; we just react to drags
        if (InputManager.Instance != null)
            InputManager.Instance.OnScreenDrag += OnScreenDrag;
    }

    private void OnDestroy()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnScreenDrag -= OnScreenDrag;
    }

    public void SetPanEnabled(bool enabled) => _panEnabled = enabled;

    private void OnScreenDrag(Vector2 screenDelta)
    {
        if (!_panEnabled || targetCamera == null) return;
        
        float worldPerPixelY = (2f * targetCamera.orthographicSize) / Screen.height;
        float worldPerPixelX = worldPerPixelY * targetCamera.aspect;

        Vector3 move = new Vector3(
            -screenDelta.x * worldPerPixelX,
            -screenDelta.y * worldPerPixelY,
            0f
        ) * panSpeed;

        targetCamera.transform.position += move;
        ClampToBounds();
    }
    
    private void ClampToBounds()
    {
        if (BiomeManager.Instance == null) return;

        Bounds bounds = BiomeManager.Instance.ActiveBiomeBounds;
        
        float halfH = targetCamera.orthographicSize;
        float halfW = halfH * targetCamera.aspect;

        Vector3 pos = targetCamera.transform.position;
        pos.x = Mathf.Clamp(pos.x, bounds.min.x + halfW, bounds.max.x - halfW);
        pos.y = Mathf.Clamp(pos.y, bounds.min.y + halfH, bounds.max.y - halfH);
        targetCamera.transform.position = pos;
    }
}