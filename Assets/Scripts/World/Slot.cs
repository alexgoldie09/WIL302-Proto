using UnityEngine;

/// <summary>
/// A fixed scene container that holds a single occupant — a crop, stationary animal,
/// plant, or building. When empty the slot's collider handles tap input to open the
/// placement UI. When occupied the collider is deactivated and the occupant's own
/// collider takes over for interaction.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Slot : MonoBehaviour, IHandler
{
    public enum SlotState { Empty, Occupied }

    [Header("Slot Identity")]
    [SerializeField, Tooltip("The biome this slot belongs to. Used to validate placement.")]
    private BiomeManager.BiomeType parentBiome;

    [Header("Placement Validation")]
    [SerializeField, Tooltip("Item type accepted by this slot. Only items of this type can be placed here.")]
    private ItemType validItemType = ItemType.Placeable;
    [SerializeField, Tooltip("Specific item names accepted by this slot. Leave empty to accept any name. " +
                             "If both lists are set, an item must pass both checks.")]
    private string[] validItemNames;
    
    private SlotState _state = SlotState.Empty;
    private GameObject _currentOccupant;
    private Collider2D _collider;
    private SlotPlacementUI _placementUI;
    
    public SlotState State => _state;
    public bool IsOccupied => _state == SlotState.Occupied;
    public GameObject CurrentOccupant => _currentOccupant;
    public BiomeManager.BiomeType ParentBiome => parentBiome;

    #region  Unity Lifecycle
    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        _placementUI = FindFirstObjectByType<SlotPlacementUI>();
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

        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        if (_state == SlotState.Empty)
        {
            if (_placementUI == null)
            {
                Debug.LogWarning("[Slot] No SlotPlacementUI found in scene.");
                return;
            }

            _placementUI.Open(this);
        }
        else
        {
            if (_currentOccupant == null) return;

            var handler = _currentOccupant.GetComponent<IHandler>();
            if (handler != null)
                handler.OnTapped();
            else
                Debug.LogWarning($"[Slot] Occupant {_currentOccupant.name} does not implement IHandler.");
        }
    }
    #endregion
    
    #region Public API
    /// <summary>
    /// Validates whether the given ItemDefinition is a legal occupant for this slot.
    /// Checks biome, item type, and item name against the configured filter lists.
    /// </summary>
    public bool CanPlace(ItemDefinition item)
    {
        if (item == null) return false;

        if (IsOccupied)
        {
            // Debug.LogWarning($"[Slot] {name} is already occupied.");
            return false;
        }

        if (item.PlaceablePrefab == null)
        {
            // Debug.LogWarning($"[Slot] {item.ItemName} has no PlaceablePrefab assigned.");
            return false;
        }

        // Biome check.
        if (BiomeManager.Instance != null &&
            BiomeManager.Instance.ActiveBiomeData.biomeType != parentBiome)
        {
            // Debug.LogWarning($"[Slot] {name} belongs to {parentBiome} but active biome is " +
            //                 $"{BiomeManager.Instance.ActiveBiomeData.biomeType}.");
            return false;
        }

        // Item type check
        if (item.ItemType != validItemType)
        {
            // Debug.LogWarning($"[Slot] {name} does not accept item type '{item.ItemType}'.");
            return false;
        }

        // Item name check — skip if list is empty (accept all).
        if (validItemNames != null && validItemNames.Length > 0)
        {
            bool nameMatch = false;
            foreach (var n in validItemNames)
                if (item.ItemName == n) { nameMatch = true; break; }

            if (!nameMatch)
            {
                // Debug.LogWarning($"[Slot] {name} does not accept item named '{item.ItemName}'.");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Instantiates the item's PlaceablePrefab, parents it to this slot, centres it,
    /// and deactivates the slot collider so the occupant handles input.
    /// Removes one of the item from PlayerInventory.
    /// </summary>
    public void Place(ItemDefinition item)
    {
        if (!CanPlace(item)) return;

        if (!PlayerInventory.Instance.Remove(item, 1))
        {
            Debug.LogWarning($"[Slot] Could not remove {item.ItemName} from inventory.");
            return;
        }

        GameObject occupant = Instantiate(item.PlaceablePrefab, transform, true);
        occupant.transform.localPosition = Vector3.zero;

        _currentOccupant = occupant;
        _state = SlotState.Occupied;
        _collider.enabled = false;

        // Notify any FloraBase on the occupant which slot it belongs to so it
        // can call slot.Clear() on its own destruction.
        var flora = occupant.GetComponent<FloraBase>();
        if (flora != null) flora.SetSlot(this);

        Debug.Log($"[Slot] {name} occupied by {item.ItemName}.");
    }

    /// <summary>
    /// Destroys the current occupant, reactivates the slot collider,
    /// and resets state to Empty.
    /// </summary>
    public void Clear()
    {
        if (_currentOccupant != null)
        {
            Destroy(_currentOccupant);
            _currentOccupant = null;
        }

        _state = SlotState.Empty;
        _collider.enabled = true;

        Debug.Log($"[Slot] {name} cleared and ready for placement.");
    }
    #endregion
    
    #region Debug Methods
#if UNITY_EDITOR
    [ContextMenu("Debug/Clear Slot")]
    private void DebugClearSlot()
    {
        if (!IsOccupied) { Debug.LogWarning($"[Slot] {name} is already empty."); return; }
        Clear();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = IsOccupied
            ? new Color(0.2f, 1f, 0.4f, 0.6f)
            : new Color(1f, 0.4f, 0.8f, 0.6f);
        Gizmos.DrawWireCube(transform.position, new Vector3(0.32f, 0.32f, 0f));
    }
#endif
    #endregion
}