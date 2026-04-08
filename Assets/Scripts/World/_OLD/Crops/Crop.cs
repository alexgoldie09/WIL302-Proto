// using System;
// using UnityEngine;
//
// /// <summary>
// /// Represents a single crop planted in a Plot socket. Tracks its type, planting time, and parent socket,
// /// and derives its current growth stage each frame from elapsed real-world time.
// /// On load, self-attaches to its parent Plot via the SaveContext resolver.
// /// Loads after plots (LoadPriority 10) to ensure the parent Plot is ready to receive attachment calls.
// /// </summary>
// [DisallowMultipleComponent]
// public class Crop : SaveableBehaviour<Crop.Data>
// {
//     /// <summary>
//     /// Serializable save payload for a Crop, capturing everything needed to restore its
//     /// type, age, and position within its parent Plot.
//     /// </summary>
//     [Serializable]
//     public class Data
//     {
//         public string cropTypeId;
//         public long plantedUtcTicks;
//         public string parentPlotId;
//         public int parentSocketIndex;
//     }
//
//     [Header("References")]
//     [SerializeField] private Animator animator;
//     [SerializeField] private SpriteRenderer spriteRenderer;
//     [SerializeField, Tooltip("Database for crop configs (assign on prefab).")]
//     private CropDatabase cropDatabase;
//
//     [Header("Runtime State")]
//     // Stored in save data (e.g. 'Carrot').
//     private string cropTypeId;
//     // UTC ticks when planted.
//     private long plantedUtcTicks;
//     // Plot GUID this crop belongs to.
//     private string parentPlotId;
//     // Socket index within the parent plot this crop occupies.
//     private int parentSocketIndex = -1;
//     // Current growth stage index derived from elapsed time since planting.
//     private int currentStageIndex = 0;
//
//     // Resolved CropConfig for this crop's type, used for stage calculations and visuals.
//     private CropConfig config;
//
//     public override string RecordType => "Crop";
//     public override int LoadPriority => 10;
//
//     /// <summary>Unique string identifier for this crop's type (e.g. "Carrot").</summary>
//     public string CropTypeId => cropTypeId;
//
//     /// <summary>The current growth stage index, derived each frame from elapsed time since planting.</summary>
//     public int CurrentStageIndex => currentStageIndex;
//
//     /// <summary>
//     /// Returns true when the crop has reached or passed the final growth stage and is ready to harvest.
//     /// </summary>
//     public bool IsMature =>
//         config != null &&
//         config.Stages != null &&
//         config.Stages.Count > 0 &&
//         currentStageIndex >= config.Stages.Count - 1;
//
//     #region Unity Lifecycle
//     /// <summary>
//     /// Ensures a persistent GUID is assigned and binds the database if a type ID is already set,
//     /// supporting cases where the crop is pre-configured on a prefab.
//     /// </summary>
//     private void Awake()
//     {
//         EnsurePersistentGuid();
//
//         if (cropDatabase != null && !string.IsNullOrWhiteSpace(cropTypeId))
//             BindDatabase(cropDatabase);
//     }
//
//     /// <summary>
//     /// Editor-only reset that assigns a GUID and auto-populates animator and sprite renderer references
//     /// from child components.
//     /// </summary>
//     private void Reset()
//     {
//         EnsurePersistentGuid();
//
//         animator = GetComponentInChildren<Animator>();
//         spriteRenderer = GetComponentInChildren<SpriteRenderer>();
//     }
//
//     /// <summary>
//     /// Recalculates and applies the correct growth stage visuals each frame based on current UTC time.
//     /// Skipped if the config or planted time are not yet set.
//     /// </summary>
//     private void Update()
//     {
//         if (config != null && plantedUtcTicks > 0)
//             ApplyStageVisuals(DateTime.UtcNow);
//     }
//     #endregion
//
//     #region Initialisation
//     /// <summary>
//     /// Records the socket index within the parent plot that this crop occupies.
//     /// </summary>
//     public void SetParentSocketIndex(int socketIndex) => parentSocketIndex = socketIndex;
//
//     /// <summary>
//     /// Sets up all runtime state for a freshly planted crop, including its type, planting timestamp,
//     /// parent plot reference, and database binding. Immediately applies the initial stage visuals.
//     /// </summary>
//     public void Initialize(string cropType, DateTime utcPlantedAt, string plotId, CropDatabase database)
//     {
//         cropTypeId = cropType;
//         plantedUtcTicks = utcPlantedAt.Ticks;
//         parentPlotId = plotId;
//
//         cropDatabase = database != null ? database : cropDatabase;
//         config = cropDatabase != null ? cropDatabase.Get(cropTypeId) : null;
//
//         ApplyStageVisuals(utcPlantedAt);
//     }
//
//     /// <summary>
//     /// Binds (or rebinds) the crop database and resolves the CropConfig for this crop's type.
//     /// Used after load to reconnect the config reference without changing any other state.
//     /// </summary>
//     public void BindDatabase(CropDatabase database)
//     {
//         cropDatabase = database != null ? database : cropDatabase;
//         config = cropDatabase != null ? cropDatabase.Get(cropTypeId) : null;
//     }
//     #endregion
//
//     #region Save and Load
//     /// <summary>
//     /// Builds a save payload capturing the crop's type, planting time, and parent socket location.
//     /// </summary>
//     protected override Data BuildData()
//     {
//         return new Data
//         {
//             cropTypeId = cropTypeId,
//             plantedUtcTicks = plantedUtcTicks,
//             parentPlotId = parentPlotId,
//             parentSocketIndex = parentSocketIndex
//         };
//     }
//
//     /// <summary>
//     /// Restores all runtime state from a saved payload, rebinds the database, then self-attaches
//     /// to the parent Plot using the SaveContext resolver so the plot's socket occupancy is updated.
//     /// Finally, applies the correct stage visuals for the elapsed time at load.
//     /// </summary>
//     protected override void ApplyData(Data data, SaveContext context)
//     {
//         if (data == null)
//             return;
//
//         // Restore identity and parent references from the saved payload.
//         cropTypeId = data.cropTypeId;
//         plantedUtcTicks = data.plantedUtcTicks;
//         parentPlotId = data.parentPlotId;
//         parentSocketIndex = data.parentSocketIndex;
//
//         // Rebind the database to restore the config reference.
//         BindDatabase(cropDatabase);
//
//         // Resolve the parent Plot by GUID and attach to the correct socket.
//         if (context != null && context.ResolveById != null && !string.IsNullOrWhiteSpace(parentPlotId))
//         {
//             var parent = context.ResolveById(parentPlotId);
//             if (parent is Plot plot)
//                 plot.AttachCropFromLoad(this, parentSocketIndex);
//         }
//
//         // Apply stage visuals using the load-time UTC timestamp so offline growth is reflected.
//         if (context != null && config != null)
//             ApplyStageVisuals(context.UtcNow);
//     }
//     #endregion
//
//     #region Growth and Visuals
//     /// <summary>
//     /// Calculates the current growth stage from elapsed time since planting and updates
//     /// the animator or sprite renderer to display the correct stage visuals.
//     /// </summary>
//     private void ApplyStageVisuals(DateTime utcNow)
//     {
//         if (config == null || config.Stages == null || config.Stages.Count == 0)
//             return;
//
//         // Calculate elapsed time since planting, clamped to zero for clock corrections.
//         var plantedUtc = new DateTime(plantedUtcTicks, DateTimeKind.Utc);
//         var elapsed = utcNow - plantedUtc;
//
//         if (elapsed < TimeSpan.Zero)
//             elapsed = TimeSpan.Zero;
//
//         // Derive the stage index from elapsed seconds and apply the matching visual.
//         int stageIndex = GetStageIndex((float)elapsed.TotalSeconds, config);
//         currentStageIndex = stageIndex;
//
//         var stage = config.Stages[Mathf.Clamp(stageIndex, 0, config.Stages.Count - 1)];
//
//         if (animator != null && !string.IsNullOrWhiteSpace(stage.animatorStateName))
//         {
//             animator.Play(stage.animatorStateName);
//         }
//         else if (spriteRenderer != null && stage.sprite != null)
//         {
//             spriteRenderer.sprite = stage.sprite;
//         }
//     }
//
//     /// <summary>
//     /// Walks the crop's growth stages in order, subtracting each stage's duration from the
//     /// elapsed seconds until the remaining time falls within a stage. Always returns the last
//     /// stage index once all durations are exhausted.
//     /// </summary>
//     private int GetStageIndex(float elapsedSeconds, CropConfig cfg)
//     {
//         float remaining = Mathf.Max(0f, elapsedSeconds);
//
//         for (int i = 0; i < cfg.Stages.Count; i++)
//         {
//             float dt = Mathf.Max(0f, cfg.Stages[i].secondsToNextStage);
//
//             // Always return the last stage once we reach it.
//             if (i == cfg.Stages.Count - 1)
//                 return i;
//
//             if (remaining < dt)
//                 return i;
//
//             remaining -= dt;
//         }
//
//         return cfg.Stages.Count - 1;
//     }
//     #endregion
// }