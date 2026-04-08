using System;
using System.Collections;
using UnityEngine;

public class CameraSwapper : MonoBehaviour
{
    public static CameraSwapper Instance { get; private set; }
    
    public event Action<int> OnBiomeSwitchComplete;
    
    [Header("Biome Camera Anchors")]
    [Tooltip("World-space positions the camera will move to (one per biome).")]
    [SerializeField] private Transform[] biomeAnchors = new Transform[3];

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.5f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private Camera   _cam;
    private int      _currentBiome = 0;
    private Coroutine _activeTransition;
    
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _cam = Camera.main;

        if (biomeAnchors.Length < 3)
            Debug.LogWarning("[CameraSwapper] Expected 3 biome anchors. Check the Inspector.");

        SnapToBiome(_currentBiome);
    }

    public void SwitchToBiome(int index)
    {
        if (index < 0 || index >= biomeAnchors.Length)
        {
            Debug.LogError($"[CameraSwapper] Biome index {index} is out of range.");
            return;
        }

        if (index == _currentBiome) return;

        _currentBiome = index;

        if (_activeTransition != null)
            StopCoroutine(_activeTransition);

        _activeTransition = StartCoroutine(TransitionRoutine(index));
    }
    
    public bool IsTransitioning => _activeTransition != null;

    public void StopTransition()
    {
        if (_activeTransition != null)
        {
            StopCoroutine(_activeTransition);
            _activeTransition = null;
        }
    }

    public void SnapToBiome(int index)
    {
        if (index < 0 || index >= biomeAnchors.Length) return;
        if (_cam == null) _cam = Camera.main;

        Transform anchor = biomeAnchors[index];
        if (anchor == null) return;

        Vector3 target = anchor.position;
        target.z = _cam.transform.position.z;
        _cam.transform.position = target;

        _currentBiome = index;
        BiomeManager.Instance?.SetActiveBiome(index);
        OnBiomeSwitchComplete?.Invoke(index);
    }

    public int CurrentBiome => _currentBiome;

    private IEnumerator TransitionRoutine(int targetIndex)
    {
        InputManager.Instance?.SetInputEnabled(false);
        BiomeManager.Instance?.SetActiveBiome(targetIndex);

        Transform anchor   = biomeAnchors[targetIndex];
        Vector3   startPos = _cam.transform.position;
        Vector3   endPos   = anchor.position;
        endPos.z = startPos.z;

        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = transitionCurve.Evaluate(elapsed / transitionDuration);
            _cam.transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        _cam.transform.position = endPos;
        _activeTransition = null;

        InputManager.Instance?.SetInputEnabled(true);
        OnBiomeSwitchComplete?.Invoke(targetIndex);
    }
}