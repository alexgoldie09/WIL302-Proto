using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Slide-up banner UI for quest pickup and completion notifications.
/// Queues notifications so they never overlap.
/// Tap the banner to dismiss early and show the next in queue.
/// Follows the same slide pattern as BiomeNotificationUI.
/// </summary>
public class QuestNotificationUI : MonoBehaviour
{
    public static QuestNotificationUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private RectTransform bannerRect;
    [SerializeField] private Image         bannerBackground;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;

    [Header("Colours")]
    [SerializeField] private Color questPickupColour  = new Color(0.30f, 0.65f, 1.00f);
    [SerializeField] private Color questCompleteColour = new Color(0.25f, 0.80f, 0.40f);

    [Header("Timing")]
    [SerializeField] private float slideDuration   = 0.3f;
    [SerializeField] private float displayDuration = 6f;

    // ── Private state ─────────────────────────────────────────────────────────
    private readonly Queue<(string title, string message, Color colour)> _queue
        = new Queue<(string, string, Color)>();

    private bool    _isShowing;
    private Vector2 _shownPos;
    private Vector2 _hiddenPos;
    private Coroutine _displayCoroutine;

    // ─────────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Capture shown position from current rect position, then compute hidden
        // position as directly below the screen.
        _shownPos  = bannerRect.anchoredPosition;
        _hiddenPos = new Vector2(_shownPos.x, _shownPos.y - bannerRect.rect.height - 20f);

        bannerRect.anchoredPosition = _hiddenPos;
        bannerRect.gameObject.SetActive(false);

        // Wire tap-to-dismiss — add Button if not already present.
        var btn = bannerRect.GetComponent<Button>();
        if (btn == null) btn = bannerRect.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(OnBannerTapped);
    }

    private void Start()
    {
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.OnQuestPickedUp  += HandleQuestPickedUp;
            QuestManager.Instance.OnQuestCompleted += HandleQuestCompleted;
        }
    }

    private void OnDestroy()
    {
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.OnQuestPickedUp  -= HandleQuestPickedUp;
            QuestManager.Instance.OnQuestCompleted -= HandleQuestCompleted;
        }
    }

    // ── Event Handlers ────────────────────────────────────────────────────────
    private void HandleQuestPickedUp(Quest quest)
    {
        Enqueue(
            quest.Definition.questTitle,
            quest.Definition.questDescription,
            questPickupColour
        );
    }

    private void HandleQuestCompleted(Quest quest)
    {
        Enqueue(
            quest.Definition.questTitle,
            quest.Definition.rewardDialogue,
            questCompleteColour
        );
    }

    // ── Queue ─────────────────────────────────────────────────────────────────
    private void Enqueue(string title, string message, Color colour)
    {
        _queue.Enqueue((title, message, colour));
        if (!_isShowing)
            StartNextNotification();
    }

    private void StartNextNotification()
    {
        if (_queue.Count == 0) { _isShowing = false; return; }

        var (title, message, colour) = _queue.Dequeue();
        _isShowing = true;

        titleText.text   = title;
        messageText.text = message;

        if (bannerBackground != null)
            bannerBackground.color = colour;

        if (_displayCoroutine != null)
            StopCoroutine(_displayCoroutine);

        _displayCoroutine = StartCoroutine(DisplayRoutine());
    }

    // ── Display Routine ───────────────────────────────────────────────────────
    private IEnumerator DisplayRoutine()
    {
        bannerRect.gameObject.SetActive(true);

        // Slide in.
        yield return StartCoroutine(SlideRoutine(_hiddenPos, _shownPos));

        // Hold.
        float elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Slide out.
        yield return StartCoroutine(SlideRoutine(_shownPos, _hiddenPos));

        bannerRect.gameObject.SetActive(false);
        _displayCoroutine = null;

        StartNextNotification();
    }

    private IEnumerator SlideRoutine(Vector2 from, Vector2 to)
    {
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / slideDuration);
            bannerRect.anchoredPosition = Vector2.Lerp(from, to, t);
            yield return null;
        }
        bannerRect.anchoredPosition = to;
    }

    // ── Tap to Dismiss ────────────────────────────────────────────────────────
    public void OnBannerTapped()
    {
        if (!_isShowing) return;

        if (_displayCoroutine != null)
        {
            StopCoroutine(_displayCoroutine);
            _displayCoroutine = null;
        }

        // Snap to hidden immediately then show next.
        bannerRect.anchoredPosition = _hiddenPos;
        bannerRect.gameObject.SetActive(false);

        StartNextNotification();
    }
}