// using System;
// using System.Collections.Generic;
// using UnityEngine;
//
// /// <summary>
// /// Represents a farmable plot containing one or more sockets, each of which can hold a single Crop.
// /// Handles planting, harvesting, and save/load of per-socket occupancy state.
// /// Loads before crops (LoadPriority 0) so that crops can attach themselves to their parent socket on restore.
// /// </summary>
// [DisallowMultipleComponent]
// public class Plot : SaveableBehaviour<Plot.Data>
// {
//     /// <summary>
//     /// Serializable save payload for a Plot, storing the occupancy state of every socket.
//     /// </summary>
//     [Serializable]
//     public class Data
//     {
//         public List<SocketData> sockets = new();
//     }
//
//     /// <summary>
//     /// Serializable save payload for a single socket, recording which crop GUID occupies it (if any).
//     /// </summary>
//     [Serializable]
//     public class SocketData
//     {
//         public int socketIndex;
//         public string occupiedCropId;
//     }
//
//     [Header("References")]
//     [SerializeField, Tooltip("Multiple socket transforms. Each should have a Collider2D for clicking.")]
//     private List<Transform> sockets = new();
//     [SerializeField, Tooltip("Crop prefab to spawn when planting.")]
//     private GameObject cropPrefab;
//     [SerializeField, Tooltip("Database for crop configs.")]
//     private CropDatabase cropDatabase;
//
//     [Header("Prototype Settings")]
//     [SerializeField, Tooltip("Default crop to plant when clicking a free socket.")]
//     private string defaultCropTypeId = "Carrot";
//
//     // Per-socket crop occupancy (index -> cropId).
//     private List<string> occupiedCropIds = new();
//     // Per-socket runtime crop references (index -> Crop component).
//     private List<Crop> currentCrops = new();
//
//     public override string RecordType => "Plot";
//     public override int LoadPriority => 0;
//
//     /// <summary>Read-only view of the socket transforms, used externally to query socket positions.</summary>
//     public IReadOnlyList<Transform> Sockets => sockets;
//
//     #region Unity Lifecycle
//     /// <summary>
//     /// Ensures a persistent GUID is set and that runtime lists match the number of sockets.
//     /// </summary>
//     private void Awake()
//     {
//         EnsurePersistentGuid();
//         EnsureCapacity();
//     }
//
//     /// <summary>
//     /// Editor-only reset that auto-populates the socket list from children named "Socket_*"
//     /// and ensures a persistent GUID is assigned.
//     /// </summary>
//     private void Reset()
//     {
//         EnsurePersistentGuid();
//
//         // Convenience: auto-pick children named "Socket_*" to pre-populate the socket list.
//         sockets.Clear();
//         for (int i = 0; i < transform.childCount; i++)
//         {
//             var child = transform.GetChild(i);
//             if (child.name.StartsWith("Socket", StringComparison.OrdinalIgnoreCase))
//                 sockets.Add(child);
//         }
//
//         EnsureCapacity();
//     }
//     #endregion
//
//     #region Socket Helpers
//     /// <summary>
//     /// Grows or shrinks the occupancy and crop runtime lists to match the current socket count,
//     /// ensuring the lists are always in sync with the inspector-assigned sockets.
//     /// </summary>
//     private void EnsureCapacity()
//     {
//         int n = sockets != null ? sockets.Count : 0;
//         if (n <= 0)
//             return;
//
//         while (occupiedCropIds.Count < n) occupiedCropIds.Add(string.Empty);
//         while (occupiedCropIds.Count > n) occupiedCropIds.RemoveAt(occupiedCropIds.Count - 1);
//
//         while (currentCrops.Count < n) currentCrops.Add(null);
//         while (currentCrops.Count > n) currentCrops.RemoveAt(currentCrops.Count - 1);
//     }
//
//     /// <summary>
//     /// Returns the socket index whose transform matches the given collider's transform,
//     /// or -1 if the collider does not belong to any socket on this plot.
//     /// </summary>
//     public int GetSocketIndexFromCollider(Collider2D col)
//     {
//         if (col == null || sockets == null)
//             return -1;
//
//         for (int i = 0; i < sockets.Count; i++)
//         {
//             if (sockets[i] != null && col.transform == sockets[i])
//                 return i;
//         }
//
//         return -1;
//     }
//
//     /// <summary>
//     /// Returns true if the given socket index holds an assigned crop GUID.
//     /// </summary>
//     public bool IsSocketOccupied(int socketIndex)
//     {
//         if (socketIndex < 0 || socketIndex >= occupiedCropIds.Count)
//             return false;
//
//         return !string.IsNullOrWhiteSpace(occupiedCropIds[socketIndex]);
//     }
//     #endregion
//
//     #region Planting and Harvesting
//     /// <summary>
//     /// Plants the default crop at the given socket if it is empty. If the socket is already
//     /// occupied, attempts to harvest a mature crop or logs the current growth stage.
//     /// </summary>
//     public void TryPlantDefaultAtSocket(int socketIndex)
//     {
//         EnsureCapacity();
//
//         if (socketIndex < 0 || socketIndex >= sockets.Count)
//             return;
//
//         // If occupied, try to harvest if mature, otherwise report growth stage.
//         if (IsSocketOccupied(socketIndex))
//         {
//             var currentCrop = currentCrops[socketIndex];
//
//             if (currentCrop == null)
//             {
//                 Debug.Log($"[Plot] {name} socket {socketIndex} occupied (missing runtime ref).", this);
//                 return;
//             }
//
//             if (currentCrop.IsMature)
//             {
//                 HarvestAtSocket(socketIndex);
//             }
//             else
//             {
//                 Debug.Log($"[Plot] {name} socket {socketIndex} crop not ready. stage={currentCrop.CurrentStageIndex}", this);
//             }
//
//             return;
//         }
//
//         if (sockets[socketIndex] == null || cropPrefab == null || cropDatabase == null)
//         {
//             Debug.LogWarning($"[Plot] Missing references on {name}. sockets/cropPrefab/cropDatabase required.", this);
//             return;
//         }
//
//         // Instantiate the crop prefab as a child of the socket transform.
//         var socket = sockets[socketIndex];
//         var cropGO = Instantiate(cropPrefab, socket.position, Quaternion.identity, socket);
//         var crop = cropGO.GetComponent<Crop>();
//
//         // Ensure the new crop has a GUID before registering it.
//         if (crop != null && string.IsNullOrWhiteSpace(crop.PersistentGuid))
//             crop.SetPersistentGuid(System.Guid.NewGuid().ToString());
//
//         crop.Initialize(defaultCropTypeId, DateTime.UtcNow, PersistentGuid, cropDatabase);
//
//         // Record occupancy and bind the socket index on the crop.
//         occupiedCropIds[socketIndex] = crop.PersistentGuid;
//         currentCrops[socketIndex] = crop;
//
//         crop.SetParentSocketIndex(socketIndex);
//
//         Debug.Log($"[Plot] Planted {defaultCropTypeId} on {name} socket {socketIndex}. cropId={crop.PersistentGuid}", this);
//     }
//
//     /// <summary>
//     /// Reattaches a crop that was restored by the SaveManager to the correct socket on this plot.
//     /// Re-parents the crop transform, rebinds the crop database, and updates occupancy tracking.
//     /// </summary>
//     public void AttachCropFromLoad(Crop crop, int socketIndex)
//     {
//         if (crop == null)
//             return;
//
//         EnsureCapacity();
//
//         if (socketIndex < 0 || socketIndex >= sockets.Count)
//         {
//             Debug.LogWarning($"[Plot] Invalid socketIndex={socketIndex} for plot {name}.", this);
//             return;
//         }
//
//         var socket = sockets[socketIndex];
//
//         // Update runtime occupancy tracking.
//         currentCrops[socketIndex] = crop;
//         occupiedCropIds[socketIndex] = crop.PersistentGuid;
//
//         // Re-parent and reset local transform so the crop sits exactly on the socket.
//         if (socket != null)
//         {
//             crop.transform.SetParent(socket, worldPositionStays: false);
//             crop.transform.localPosition = Vector3.zero;
//             crop.transform.localRotation = Quaternion.identity;
//         }
//
//         crop.BindDatabase(cropDatabase);
//         crop.SetParentSocketIndex(socketIndex);
//     }
//
//     /// <summary>
//     /// Destroys the crop at the given socket and clears its occupancy entry,
//     /// making the socket available for planting again.
//     /// </summary>
//     private void HarvestAtSocket(int socketIndex)
//     {
//         if (socketIndex < 0 || socketIndex >= currentCrops.Count)
//             return;
//
//         var crop = currentCrops[socketIndex];
//         if (crop == null)
//             return;
//
//         Debug.Log($"[Plot] Harvested cropType={crop.CropTypeId} at {name} socket {socketIndex}. cropId={crop.PersistentGuid}", this);
//
//         Destroy(crop.gameObject);
//
//         currentCrops[socketIndex] = null;
//         occupiedCropIds[socketIndex] = string.Empty;
//     }
//     #endregion
//
//     #region Save and Load
//     /// <summary>
//     /// Builds a save payload containing the current occupancy state of every socket.
//     /// </summary>
//     protected override Data BuildData()
//     {
//         EnsureCapacity();
//
//         var data = new Data();
//         for (int i = 0; i < occupiedCropIds.Count; i++)
//         {
//             data.sockets.Add(new SocketData
//             {
//                 socketIndex = i,
//                 occupiedCropId = occupiedCropIds[i]
//             });
//         }
//
//         return data;
//     }
//
//     /// <summary>
//     /// Restores socket occupancy from a saved payload. Clears all runtime references first,
//     /// then repopulates occupiedCropIds so that crops can attach themselves via AttachCropFromLoad.
//     /// Note: this does not restore Crop references directly — that is handled by each Crop's own RestoreState.
//     /// </summary>
//     protected override void ApplyData(Data data, SaveContext context)
//     {
//         EnsureCapacity();
//
//         // Clear all runtime references before applying saved state.
//         for (int i = 0; i < currentCrops.Count; i++)
//             currentCrops[i] = null;
//
//         for (int i = 0; i < occupiedCropIds.Count; i++)
//             occupiedCropIds[i] = string.Empty;
//
//         if (data == null || data.sockets == null)
//             return;
//
//         // Restore each socket's occupying crop GUID from the saved payload.
//         foreach (var s in data.sockets)
//         {
//             if (s == null)
//                 continue;
//
//             if (s.socketIndex < 0 || s.socketIndex >= occupiedCropIds.Count)
//                 continue;
//
//             occupiedCropIds[s.socketIndex] = s.occupiedCropId;
//         }
//     }
//     #endregion
// }