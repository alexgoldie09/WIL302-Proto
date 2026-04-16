using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays a slide-in banner when AlertManager fires OnBiomeNotification.
/// Multiple notifications queue and display one at a time.
/// Tapping the banner switches directly to the relevant biome.
/// </summary>
public class BiomeNotificationUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Tooltip("The banner RectTransform that slides in from the top.")]
    private RectTransform bannerRect;
    [SerializeField, Tooltip("Background image of the banner — tinted per biome.")]
    private Image bannerBackground;
    [SerializeField, Tooltip("Main message text.")]
    private TextMeshProUGUI messageText;

    [Header("Biome Tint Colours")]
    [SerializeField] private Color farmColour    = new Color(0.4f, 0.75f, 0.3f);
    [SerializeField] private Color pondColour    = new Color(0.2f, 0.55f, 0.9f);
    [SerializeField] private Color coastColour   = new Color(0.9f, 0.75f, 0.2f);
    [SerializeField] private Color nurseryColour = new Color(0.7f, 0.35f, 0.9f);

    [Header("Animation")]
    [SerializeField, Tooltip("How long the banner takes to slide in or out.")]
    private float slideDuration = 0.3f;
    [SerializeField, Tooltip("How long the banner stays visible before sliding out.")]
    private float displayDuration = 3f;
    
    private Vector2 _shownPos;
    private Vector2 _hiddenPos;
    private bool _isShowing;
    private Coroutine _displayCoroutine;

    private readonly Queue<(BiomeManager.BiomeType biome, string message)> _queue = new();

    #region Unity Lifecycle
    private void Awake()
    {
        _shownPos  = bannerRect.anchoredPosition;
        _hiddenPos = _shownPos + new Vector2(0, bannerRect.rect.height + 20f);
        bannerRect.anchoredPosition = _hiddenPos;
        
        bannerRect.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (AlertManager.Instance != null)
            AlertManager.Instance.OnBiomeNotification += HandleNotification;
        else
            Debug.LogWarning("[BiomeNotificationUI] AlertManager not found on Start.");
    }

    private void OnDestroy()
    {
        if (AlertManager.Instance != null)
            AlertManager.Instance.OnBiomeNotification -= HandleNotification;
    }
    #endregion

    #region Notifications
    /// <summary>
    /// Receives a notification from AlertManager.
    /// Deduplicates by biome — if this biome is already in the queue or showing,
    /// the message is updated but no duplicate entry is added.
    /// </summary>
    private void HandleNotification(BiomeManager.BiomeType biome, string message)
    {
        // Deduplicate — don't queue the same biome twice.
        foreach (var entry in _queue)
            if (entry.biome == biome) return;

        _queue.Enqueue((biome, message));

        if (!_isShowing)
            StartNextNotification();
    }

    private void StartNextNotification()
    {
        if (_queue.Count == 0) return;

        var (biome, message) = _queue.Dequeue();

        // Configure banner content.
        messageText.text = message;
        bannerBackground.color = GetBiomeColour(biome);

        // Store biome on button for tap-to-switch.
        _currentBiome = biome;

        if (_displayCoroutine != null)
            StopCoroutine(_displayCoroutine);

        _displayCoroutine = StartCoroutine(DisplayRoutine());
    }

    private BiomeManager.BiomeType _currentBiome;

    private void OnBannerTapped()
    {
        // Switch to the biome immediately and dismiss the banner.
        BiomeManager.Instance?.SetActiveBiome((int)_currentBiome);

        if (_displayCoroutine != null)
        {
            StopCoroutine(_displayCoroutine);
            _displayCoroutine = null;
        }

        StartCoroutine(SlideOut());
    }
    #endregion

    #region Animations
    private IEnumerator DisplayRoutine()
    {
        _isShowing = true;
        bannerRect.gameObject.SetActive(true);

        yield return StartCoroutine(Slide(_shownPos));
        yield return new WaitForSeconds(displayDuration);
        yield return StartCoroutine(SlideOut());
    }

    private IEnumerator SlideOut()
    {
        yield return StartCoroutine(Slide(_hiddenPos));
        bannerRect.gameObject.SetActive(false);
        _isShowing = false;

        if (_queue.Count > 0)
            StartNextNotification();
    }

    private IEnumerator Slide(Vector2 target)
    {
        Vector2 start   = bannerRect.anchoredPosition;
        float   elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Ease(elapsed / slideDuration);
            bannerRect.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }

        bannerRect.anchoredPosition = target;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
    #endregion

    #region Helpers
    private Color GetBiomeColour(BiomeManager.BiomeType biome) => biome switch
    {
        BiomeManager.BiomeType.Farm    => farmColour,
        BiomeManager.BiomeType.Pond    => pondColour,
        BiomeManager.BiomeType.Coast   => coastColour,
        BiomeManager.BiomeType.Nursery => nurseryColour,
        _                              => Color.white
    };
    #endregion
}