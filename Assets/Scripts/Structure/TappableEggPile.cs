using TMPro;
using UnityEngine;

/// <summary>
/// Child component of EggCollectorStructure that shows a prefab egg sprite
/// and a count text when eggs are stored. Tapping calls CollectAll on the coop.
/// The structure's main collider is disabled once built so this collider
/// takes over tap detection cleanly.
///
/// Hierarchy:
///   EggCollectorStructure (root)
///   └── EggPile (this component + Collider2D for tap detection)
///       ├── EggSprite (assigned via eggVisualRoot — shown/hidden based on count)
///       └── CountText (TextMeshPro showing e.g. "3 / 10")
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class TappableEggPile : MonoBehaviour, IHandler
{
    [Header("Visuals")]
    [SerializeField, Tooltip("The egg sprite GameObject shown when eggs are stored. " +
                             "Hidden when count is 0.")]
    private GameObject eggVisualRoot;
    [SerializeField, Tooltip("Text displaying the current stored count e.g. '3 / 10'.")]
    private TextMeshProUGUI countText;

    [Header("Tap Detection")]
    [SerializeField, Tooltip("Layer mask for this pile's collider — used for tap raycast.")]
    private LayerMask interactableLayer;
    
    private EggCollectorStructure _coop;
    private Collider2D _collider;

    #region Unity Lifecycle
    private void Awake()
    {
        _coop     = GetComponentInParent<EggCollectorStructure>();
        _collider = GetComponent<Collider2D>();

        // Start hidden and non-interactable until eggs are stored.
        SetVisible(false);
    }

    private void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }
    #endregion

    #region IHandler
    private void HandleWorldTap(Vector2 worldPos)
    {
        if (!_collider.enabled) return;

        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        _coop?.CollectAll();
    }
    #endregion

    #region Public API
    /// <summary>
    /// Called by EggCollectorStructure.Start() and on capacity upgrade.
    /// Resets visual state cleanly.
    /// </summary>
    public void Initialise(int capacity)
    {
        UpdateVisuals(0, capacity);
    }

    /// <summary>
    /// Updates the count text and shows or hides the visual based on stored count.
    /// Called by EggCollectorStructure whenever _storedCount changes.
    /// </summary>
    public void UpdateVisuals(int storedCount, int capacity)
    {
        bool hasSome = storedCount > 0;
        SetVisible(hasSome);

        if (countText != null)
            countText.text = hasSome ? $"{storedCount} / {capacity}" : string.Empty;
    }
    #endregion

    #region Helper Methods
    private void SetVisible(bool visible)
    {
        _collider.enabled = visible;

        if (eggVisualRoot != null)
            eggVisualRoot.SetActive(visible);

        if (countText != null)
            countText.gameObject.SetActive(visible);
    }
    #endregion
}