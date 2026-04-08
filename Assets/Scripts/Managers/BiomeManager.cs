using System;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Manages the three biomes (Farm, Pond, Tree Nursery) and owns the
/// bottom-bar button wiring. Camera bounds are now handled automatically
/// by CameraPanner reading from each biome's Ground Tilemap.
/// </summary>
public class BiomeManager : MonoBehaviour
{
    public static BiomeManager Instance { get; private set; }

    public event Action<BiomeType> OnBiomeActivated;

    public enum BiomeType { Farm = 0, Pond = 1, Coast = 2, Nursery = 3}

    [Serializable]
    public class BiomeData
    {
        [Tooltip("Friendly name shown in debug/UI.")]
        public string label;
        
        [Tooltip("Type of this biome (used for events).")]
        public BiomeType biomeType;

        [Tooltip("Root GameObject for this biome. Should contain Grid + Tilemap children.")]
        public GameObject rootObject;

        [Tooltip("Ground Tilemap inside this biome.")]
        public Tilemap groundTilemap;

        [Tooltip("Decoration / foreground Tilemap (optional).")]
        public Tilemap decorTilemap;

        [Tooltip("UI Button that switches to this biome.")]
        public Button switchButton;
        
        [Tooltip("Extra padding added to the ground tilemap bounds.")]
        public Vector2 boundsPadding = Vector2.zero;
    }

    [Header("Biomes")]
    [SerializeField] private BiomeData[] biomes = new BiomeData[3];

    [Header("Button Visual Feedback")]
    [SerializeField] private Color activeColor   = new Color(0.25f, 0.75f, 0.4f);
    [SerializeField] private Color inactiveColor = new Color(0.8f,  0.8f,  0.8f);

    // Currently active biome bounds for camera panner
    public Bounds ActiveBiomeBounds { get; private set; }
    
    private int _activeBiome = 0;
    
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
            SetBiomeActive(i, i == _activeBiome);

        UpdateButtonVisuals(_activeBiome);
        RecalculateBounds(_activeBiome);
    }

    public void SetActiveBiome(int index)
    {
        if (index < 0 || index >= biomes.Length)
        {
            Debug.LogError($"[BiomeManager] Biome index {index} out of range.");
            return;
        }

        if (index == _activeBiome) return;

        SetBiomeActive(_activeBiome, false);
        SetBiomeActive(index, true);
        _activeBiome = index;

        UpdateButtonVisuals(index);
        RecalculateBounds(index);
        OnBiomeActivated?.Invoke((BiomeType)index);

        Debug.Log($"[BiomeManager] Activated biome: {biomes[index].label}");
    }

    public BiomeData GetBiome(int index)
    {
        if (index < 0 || index >= biomes.Length) return null;
        return biomes[index];
    }

    public BiomeData ActiveBiomeData  => biomes[_activeBiome];
    public int       ActiveBiomeIndex => _activeBiome;

    private void WireButtons()
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            Button btn = biomes[i].switchButton;
            if (btn == null)
            {
                Debug.LogWarning($"[BiomeManager] No button assigned for biome {i} ({biomes[i].label}).");
                continue;
            }

            int captured = i;
            btn.onClick.AddListener(() => InputManager.Instance?.RequestBiomeSwitch(captured));
        }
    }

    private void SetBiomeActive(int index, bool active)
    {
        if (biomes[index]?.rootObject != null)
            biomes[index].rootObject.SetActive(active);
    }

    private void UpdateButtonVisuals(int activeIndex)
    {
        for (int i = 0; i < biomes.Length; i++)
        {
            if (biomes[i].switchButton == null) continue;
            var img = biomes[i].switchButton.GetComponent<Image>();
            if (img != null)
                img.color = (i == activeIndex) ? activeColor : inactiveColor;
        }
    }
    
    private void RecalculateBounds(int index)
    {
        BiomeData biome = biomes[index];
        Tilemap tilemap = biome.groundTilemap;
        if (tilemap == null)
        {
            Debug.LogWarning($"[BiomeManager] No groundTilemap on biome {index} — bounds not updated.");
            return;
        }

        BoundsInt cellBounds = tilemap.cellBounds;
        Vector3 localMin = new Vector3(cellBounds.min.x, cellBounds.min.y, 0f) * tilemap.cellSize.x;
        Vector3 localMax = new Vector3(cellBounds.max.x, cellBounds.max.y, 0f) * tilemap.cellSize.y;

        Vector3 worldMin = tilemap.transform.TransformPoint(localMin);
        Vector3 worldMax = tilemap.transform.TransformPoint(localMax);

        Vector3 center = (worldMin + worldMax) / 2f;
        Vector3 size   = (worldMax - worldMin) + new Vector3(biome.boundsPadding.x, biome.boundsPadding.y, 0f);

        ActiveBiomeBounds = new Bounds(center, size);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
        Gizmos.DrawCube(ActiveBiomeBounds.center, ActiveBiomeBounds.size);

        Gizmos.color = new Color(0f, 1f, 0.4f, 1f);
        Gizmos.DrawWireCube(ActiveBiomeBounds.center, ActiveBiomeBounds.size);
    }
}