// using System.Collections.Generic;
// using UnityEngine;
//
// /// <summary>
// /// ScriptableObject that stores all CropConfig definitions and provides fast lookup by crop type ID.
// /// The lookup dictionary is built lazily on first access and does not need to be manually refreshed.
// /// </summary>
// [CreateAssetMenu(menuName = "IdleFarm/Crop Database", fileName = "CropDatabase")]
// public class CropDatabase : ScriptableObject
// {
//     [SerializeField] private List<CropConfig> crops = new();
//
//     // Lazy-initialised lookup built from the crops list on first Get() call.
//     private Dictionary<string, CropConfig> lookup;
//
//     #region Public API
//     /// <summary>
//     /// Returns the CropConfig registered under the given crop type ID, or null if not found.
//     /// Initialises the internal lookup on first call.
//     /// </summary>
//     public CropConfig Get(string cropTypeId)
//     {
//         if (lookup == null)
//             BuildLookup();
//
//         if (string.IsNullOrWhiteSpace(cropTypeId))
//             return null;
//
//         lookup.TryGetValue(cropTypeId, out var config);
//         return config;
//     }
//     #endregion
//
//     #region Internal Helpers
//     /// <summary>
//     /// Builds the cropTypeId-to-CropConfig dictionary from the serialised crops list.
//     /// Entries with null configs or missing IDs are silently skipped.
//     /// </summary>
//     private void BuildLookup()
//     {
//         lookup = new Dictionary<string, CropConfig>();
//
//         foreach (var c in crops)
//         {
//             if (c == null)
//                 continue;
//
//             if (string.IsNullOrWhiteSpace(c.CropTypeId))
//                 continue;
//
//             lookup[c.CropTypeId] = c;
//         }
//     }
//     #endregion
// }