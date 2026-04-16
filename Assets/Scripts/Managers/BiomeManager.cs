using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Manages all biomes — camera bounds, button wiring, upgrade tiers,
/// and the persistent registry of active occupants (flora, fauna, structures, slots).
/// 
/// Biome switching hides/shows renderers rather than deactivating GameObjects so
/// all MonoBehaviours keep ticking mid-session regardless of which biome is visible.
/// This removes the need for offline time compensation during biome switches and
/// keeps the occupant registry accurate at all times.
/// 
/// </summary>
public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }
    
    public enum BiomeType { Farm = 0, Pond = 1, Coast = 2, Nursery = 3 }
    
    [Serializable]
    public class BiomeData
    {
        [Tooltip("Friendly name shown in debug/UI.")]
        public string label;

        [Tooltip("Type of this biome (used for events and occupant registration).")]
        public BiomeType biomeType;

        [Tooltip("Root GameObject for this biome. Should contain Grid + Tilemap children.")]
        public GameObject rootObject;

        [Tooltip("Ground Tilemap inside this biome.")]
        public Tilemap groundTilemap;

        [Tooltip("Decoration / foreground Tilemap (optional).")]
        public Tilemap decorTilemap;

        [Tooltip("UI Button that switches to this biome.")]
        public Button switchButton;
        
        [Tooltip("Colour for the UI button. Match the biome notification UI.")]
        public Color activeColor;

        [Tooltip("Extra padding added to the ground tilemap bounds.")]
        public Vector2 boundsPadding = Vector2.zero;

        /// <summary>
        /// Current upgrade tier for this biome. Starts at 1.
        /// Higher tiers unlock capacity increases, new catalogue items,
        /// and improved structure behaviour.
        /// </summary>
        [NonSerialized] public int upgradeTier = 1;

        /// <summary>
        /// All IBiomeOccupants currently registered to this biome.
        /// Populated on first OnEnable and only cleared on OnDestroy —
        /// persists across biome visibility changes.
        /// </summary>
        [NonSerialized] public readonly List<IBiomeOccupant> activeOccupants = new();
    }
    
    [Header("Biomes")]
    [SerializeField] private BiomeData[] biomes = new BiomeData[4];

    [Header("Button Visual Feedback")]
    [SerializeField] private Color inactiveColor = new Color(0.8f,  0.8f,  0.8f);

    /// <summary>Fired when the active biome changes.</summary>
    public event Action<BiomeType> OnBiomeActivated;

    /// <summary>
    /// Fired when an occupant registers to a biome for the first time.
    /// Structures like IrrigationSystem subscribe to apply effects to
    /// newly placed flora without rescanning the whole biome list.
    /// </summary>
    public event Action<BiomeType, IBiomeOccupant> OnOccupantRegistered;

    /// <summary>Fired when an occupant is permanently removed from a biome.</summary>
    public event Action<BiomeType, IBiomeOccupant> OnOccupantRemoved;

    /// <summary>Fired when a biome's upgrade tier changes.</summary>
    public event Action<BiomeType, int> OnBiomeTierChanged;
    
    private int _activeBiome = 0;
    
    /// <summary>Camera bounds of the currently active biome.</summary>
    public Bounds ActiveBiomeBounds { get; private set; }

    public BiomeData ActiveBiomeData  => biomes[_activeBiome];
    public int       ActiveBiomeIndex => _activeBiome;

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        WireButtons();
    }

    private void Start()
    {
        for (int i = 0; i < biomes.Length; i++)
            SetBiomeVisible(i, i == _activeBiome);

        UpdateButtonVisuals(_activeBiome);
        RecalculateBounds(_activeBiome);
    }
    #endregion

    #region Biome Switching
    public void SetActiveBiome(int index)
    {
        if (index < 0 || index >= biomes.Length)
        {
            Debug.LogError($"[BiomeManager] Biome index {index} out of range.");
            return;
        }

        if (index == _activeBiome) return;

        SetBiomeVisible(_activeBiome, false);
        SetBiomeVisible(index, true);
        _activeBiome = index;

        UpdateButtonVisuals(index);
        RecalculateBounds(index);
        OnBiomeActivated?.Invoke((BiomeType)index);

        Debug.Log($"[BiomeManager] Activated biome: {biomes[index].label}");
    }

    /// <summary>
    /// Shows or hides a biome by toggling its renderer components only.
    /// GameObjects remain active so all MonoBehaviours keep ticking —
    /// crops grow, animals decay hunger, and the occupant registry stays accurate
    /// regardless of which biome the player is currently viewing.
    /// </summary>
    private void SetBiomeVisible(int index, bool visible)
    {
        BiomeData biome = biomes[index];
        if (biome?.rootObject == null) return;

        // Tilemap renderers — ground and decor
        if (biome.groundTilemap != null)
            biome.groundTilemap.GetComponent<TilemapRenderer>().enabled = visible;

        if (biome.decorTilemap != null)
            biome.decorTilemap.GetComponent<TilemapRenderer>().enabled = visible;

        // All SpriteRenderers under the biome root — flora, fauna, structures, slots
        foreach (var sr in biome.rootObject.GetComponentsInChildren<SpriteRenderer>(true))
            sr.enabled = visible;

        // All Canvas components under the biome root — world space UI on flora/fauna
        foreach (var canvas in biome.rootObject.GetComponentsInChildren<Canvas>(true))
            canvas.enabled = visible;
    }
    #endregion

    #region BiomeData Accessors
    /// <summary>Returns BiomeData by array index, or null if out of range.</summary>
    public BiomeData GetBiome(int index)
    {
        if (index < 0 || index >= biomes.Length) return null;
        return biomes[index];
    }

    /// <summary>Returns BiomeData by type, or null if not found.</summary>
    public BiomeData GetBiomeByType(BiomeType type)
    {
        foreach (var biome in biomes)
            if (biome.biomeType == type) return biome;
        return null;
    }
    #endregion

    #region Occupant Methods
    /// <summary>
    /// Registers an occupant to its home biome's persistent list.
    /// Safe to call from OnEnable — Contains-guarded so duplicates are ignored.
    /// Fires OnOccupantRegistered only on first registration.
    /// </summary>
    public void RegisterOccupant(IBiomeOccupant occupant)
    {
        if (occupant == null) return;

        var biome = GetBiomeByType(occupant.HomeBiome);
        if (biome == null)
        {
            Debug.LogWarning($"[BiomeManager] RegisterOccupant failed — " +
                             $"no biome found for type {occupant.HomeBiome}.");
            return;
        }

        if (!biome.activeOccupants.Contains(occupant))
        {
            biome.activeOccupants.Add(occupant);
            OnOccupantRegistered?.Invoke(occupant.HomeBiome, occupant);
        }
    }

    /// <summary>
    /// Permanently removes an occupant from its home biome's list.
    /// Call this from OnDestroy only — not from OnDisable.
    /// Fires OnOccupantRemoved so structures can clean up any references.
    /// </summary>
    public void RemoveOccupant(IBiomeOccupant occupant)
    {
        if (occupant == null) return;

        var biome = GetBiomeByType(occupant.HomeBiome);
        if (biome == null) return;

        if (biome.activeOccupants.Remove(occupant))
            OnOccupantRemoved?.Invoke(occupant.HomeBiome, occupant);
    }
    
    /// <summary>
    /// Returns all active occupants on the given biome.
    /// Returns an empty list if the biome is not found.
    /// </summary>
    public List<IBiomeOccupant> GetOccupants(BiomeType type)
    {
        var biome = GetBiomeByType(type);
        return biome?.activeOccupants ?? new List<IBiomeOccupant>();
    }

    /// <summary>
    /// Returns all active occupants of a specific type on the given biome.
    /// Works for both class types and interfaces.
    /// Example: GetOccupantsOfType&lt;FloraBase&gt;(BiomeType.Farm)
    /// </summary>
    public List<T> GetOccupantsOfType<T>(BiomeType type) where T : class
    {
        var result = new List<T>();
        var biome  = GetBiomeByType(type);
        if (biome == null) return result;

        foreach (var occupant in biome.activeOccupants)
            if (occupant is T typed)
                result.Add(typed);

        return result;
    }
    #endregion

    #region Biome Tier
    /// <summary>Returns the current upgrade tier for the given biome (minimum 1).</summary>
    public int GetBiomeTier(BiomeType type)
    {
        var biome = GetBiomeByType(type);
        return biome?.upgradeTier ?? 1;
    }

    /// <summary>
    /// Sets the upgrade tier for the given biome and fires OnBiomeTierChanged.
    /// Clamps to a minimum of 1. Registered occupants that scale with tier
    /// should subscribe to OnBiomeTierChanged to react.
    /// </summary>
    public void SetBiomeTier(BiomeType type, int tier)
    {
        var biome = GetBiomeByType(type);
        if (biome == null)
        {
            Debug.LogWarning($"[BiomeManager] SetBiomeTier failed — " +
                             $"no biome found for type {type}.");
            return;
        }

        biome.upgradeTier = Mathf.Max(1, tier);
        OnBiomeTierChanged?.Invoke(type, biome.upgradeTier);
        Debug.Log($"[BiomeManager] {biome.label} upgraded to Tier {biome.upgradeTier}.");
    }
    #endregion
    
    #region Helper Methods
    private void WireButtons()
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            Button btn = biomes[i].switchButton;
            if (btn == null)
            {
                Debug.LogWarning($"[BiomeManager] No button assigned for biome {i} " +
                                 $"({biomes[i].label}).");
                continue;
            }

            int captured = i;
            btn.onClick.AddListener(() => InputManager.Instance?.RequestBiomeSwitch(captured));
        }
    }

    private void UpdateButtonVisuals(int activeIndex)
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            if (biomes[i].switchButton == null) continue;
            var img = biomes[i].switchButton.GetComponent<Image>();
            if (img != null)
                img.color = (i == activeIndex) ? biomes[i].activeColor : inactiveColor;
        }
    }

    private void RecalculateBounds(int index)
    {
        BiomeData biome   = biomes[index];
        Tilemap   tilemap = biome.groundTilemap;
        if (tilemap == null)
        {
            Debug.LogWarning($"[BiomeManager] No groundTilemap on biome {index} — " +
                             $"bounds not updated.");
            return;
        }

        BoundsInt cellBounds = tilemap.cellBounds;
        Vector3 localMin = new Vector3(cellBounds.min.x, cellBounds.min.y, 0f) * tilemap.cellSize.x;
        Vector3 localMax = new Vector3(cellBounds.max.x, cellBounds.max.y, 0f) * tilemap.cellSize.y;

        Vector3 worldMin = tilemap.transform.TransformPoint(localMin);
        Vector3 worldMax = tilemap.transform.TransformPoint(localMax);

        Vector3 center = (worldMin + worldMax) / 2f;
        Vector3 size   = (worldMax - worldMin) +
                         new Vector3(biome.boundsPadding.x, biome.boundsPadding.y, 0f);

        ActiveBiomeBounds = new Bounds(center, size);
    }
    #endregion

    #region Debug Methods
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
        Gizmos.DrawCube(ActiveBiomeBounds.center, ActiveBiomeBounds.size);

        Gizmos.color = new Color(0f, 1f, 0.4f, 1f);
        Gizmos.DrawWireCube(ActiveBiomeBounds.center, ActiveBiomeBounds.size);
    }
    
#if UNITY_EDITOR
    [ContextMenu("Debug/Log All Occupants")]
    private void DebugLogOccupants()
    {
        foreach (var biome in biomes)
        {
            Debug.Log($"[BiomeManager] {biome.label} — " +
                      $"{biome.activeOccupants.Count} occupant(s), Tier {biome.upgradeTier}:");
            foreach (var o in biome.activeOccupants)
                Debug.Log($"  - {(o as UnityEngine.Object)?.name ?? o.ToString()} " +
                          $"({o.GetType().Name})");
        }
    }

    [ContextMenu("Debug/Upgrade Farm to Tier 2")]
    private void DebugUpgradeFarm() => SetBiomeTier(BiomeType.Farm, 2);

    [ContextMenu("Debug/Upgrade Pond to Tier 2")]
    private void DebugUpgradePond() => SetBiomeTier(BiomeType.Pond, 2);
#endif
    #endregion
}