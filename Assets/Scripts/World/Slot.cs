using System;
using UnityEngine;

/// <summary>
/// Serialisable save data for Slot.
/// </summary>
[Serializable]
public class SlotData
{
    public bool   isOccupied;
    public string occupantGuid;
}

/// <summary>
/// A fixed scene container that holds a single occupant — a crop, stationary animal,
/// plant, or building. When empty the slot's collider handles tap input to open the
/// placement UI. When occupied the collider is deactivated and the occupant's own
/// collider takes over for interaction.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Slot : SaveableBehaviour<SlotData>, IHandler, IBiomeOccupant
{
    public enum SlotState { Empty, Occupied }

    [Header("Slot Identity")]
    [SerializeField, Tooltip("The biome this slot belongs to. Used to validate placement.")]
    private BiomeManager.BiomeType parentBiome;
    
    [Header("Tier")]
    [SerializeField, Tooltip("Minimum biome tier required to activate this slot. " +
                             "Tier 1 slots are always active. Tier 2 slots start hidden.")]
    private int requiredTier = 1;

    [Header("Placement Validation")]
    [SerializeField, Tooltip("LayerMask for raycast checks when this slot is tapped." +
                             "Should be set to the layer(s) that the player can interact with for placement.")]
    private LayerMask interactableLayer;
    [SerializeField, Tooltip("Item type accepted by this slot. Only items of this type can be placed here.")]
    private ItemType validItemType = ItemType.Placeable;
    [SerializeField, Tooltip("Specific item names accepted by this slot. Leave empty to accept any name. " +
                             "If both lists are set, an item must pass both checks.")]
    private string[] validItemNames;

    [Header("References")] 
    [SerializeField, Tooltip("Collider for this plot. Disabled when occupied so the occupant can handle input.")]
    private Collider2D col;
    [SerializeField, Tooltip("SpriteRenderer for this plot. Disabled for higher tier slots until they are activated.")]
    private SpriteRenderer spriteRenderer;
    
    private SlotState _state = SlotState.Empty;
    private GameObject _currentOccupant;
    private SlotPlacementUI _placementUI;
    
    #region Accessors
    public SlotState State => _state;
    public bool IsOccupied => _state == SlotState.Occupied;
    public GameObject CurrentOccupant => _currentOccupant;
    /// <summary>The biome this slot belongs to. Used by BiomeManager to track occupancy and apply biome effects.</summary>
    public BiomeManager.BiomeType HomeBiome => parentBiome;
    /// <summary>
    /// Returns false if this slot's required tier has not been met yet.
    /// Used by BiomeManager.SetBiomeVisible to avoid re-enabling tier-locked renderers.
    /// </summary>
    public bool IsVisuallyActive => BiomeManager.Instance == null ||
                                    BiomeManager.Instance.GetBiomeTier(parentBiome) >= requiredTier;
    #endregion

    #region  Unity Lifecycle
    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();
        
        if (col == null)
            col = GetComponent<Collider2D>();
        
        _placementUI = FindFirstObjectByType<SlotPlacementUI>();
    }

    private void Start()
    {
        // Higher tier slots start hidden, but skip if the biome tier was already
        // restored by the save system before Start() ran.
        if (requiredTier > 1 && !IsVisuallyActive)
        {
            col.enabled = false;
            spriteRenderer.enabled = false;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        
        if (BiomeManager.Instance != null)
        {
            BiomeManager.Instance.RegisterOccupant(this);
            BiomeManager.Instance.OnBiomeTierChanged += HandleBiomeTierChanged;
        }

        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        
        if (BiomeManager.Instance != null)
            BiomeManager.Instance.OnBiomeTierChanged -= HandleBiomeTierChanged;
        
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void HandleBiomeTierChanged(BiomeManager.BiomeType biome, int tier)
    {
        if (biome != parentBiome) return;
        if (tier < requiredTier) return;

        if (!IsOccupied)
        {
            col.enabled = true;
            spriteRenderer.enabled = true;
        }

        Debug.Log($"[Slot] {name} activated by biome tier {tier}.");
    }

    private void OnDestroy()
    {
        BiomeManager.Instance?.RemoveOccupant(this);
    }
    #endregion

    #region IHandler
    private void HandleWorldTap(Vector2 worldPos)
    {
        if (!col.enabled) return;

        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
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
            
            AudioManager.Instance?.PlaySFX("menu_click", 0.4f);

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

        GameObject occupant = Instantiate(item.PlaceablePrefab, transform, false);
        QuestManager.Instance?.RecordProgress(
            QuestObjectiveType.PlaceItem,
            item.ItemName,
            1
        );
        occupant.transform.localPosition = Vector3.zero;

        _currentOccupant = occupant;
        _state = SlotState.Occupied;
        col.enabled = false;
        spriteRenderer.enabled = false;

        // Notify FloraBase or StructureBase or FaunaBase on the occupant which slot it belongs
        // to so they can call slot.Clear() on their own destruction.
        var flora = occupant.GetComponent<FloraBase>();
        if (flora != null)
        {
            flora.SetSlot(this);
            AudioManager.Instance?.PlaySFX("plant", 0.4f);
        }

        var structure = occupant.GetComponent<StructureBase>();
        if (structure != null)
        {
            structure.SetSlot(this);
            AudioManager.Instance?.PlaySFX("build", 0.4f);
        }

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
        col.enabled = true;
        spriteRenderer.enabled = true;

        Debug.Log($"[Slot] {name} cleared and ready for placement.");
    }
    #endregion
    
    #region SaveableBehaviour

    public override string RecordType  => "Slot";
    public override int LoadPriority => 6;

    protected override SlotData BuildData()
    {
        string occupantGuid = string.Empty;
        if (_currentOccupant != null)
        {
            var saveable = _currentOccupant.GetComponent<ISaveable>();
            if (saveable != null)
                occupantGuid = saveable.PersistentGuid ?? string.Empty;
        }
        return new SlotData
        {
            isOccupied   = _state == SlotState.Occupied,
            occupantGuid = occupantGuid
        };
    }

    protected override void ApplyData(SlotData data, SaveContext context)
    {
        if (!data.isOccupied) return;

        _state = SlotState.Occupied;
        if (col != null)           col.enabled = false;
        if (spriteRenderer != null) spriteRenderer.enabled = false;

        if (!string.IsNullOrEmpty(data.occupantGuid))
        {
            var saveable = context.ResolveById(data.occupantGuid);
            if (saveable is Component component)
                _currentOccupant = component.gameObject;
        }
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