// using System;
// using System.Collections.Generic;
// using UnityEngine;
//
// /// <summary>
// /// ScriptableObject that defines a single crop type, including its unique ID and the sequence
// /// of growth stages it passes through from seed to maturity. Referenced by CropDatabase.
// /// </summary>
// [CreateAssetMenu(menuName = "IdleFarm/Crop Config", fileName = "CropConfig_")]
// public class CropConfig : ScriptableObject
// {
//     [Header("Crop Identity")]
//     [SerializeField, Tooltip("Unique key stored in save data (e.g. 'Carrot').")]
//     private string cropTypeId = "Carrot";
//
//     [Header("Growth Stages")]
//     [SerializeField, Tooltip("Stage 0..N visuals and duration to reach the NEXT stage.")]
//     private List<GrowthStage> stages = new();
//
//     /// <summary>Unique string identifier for this crop type, used as a save key and database lookup.</summary>
//     public string CropTypeId => cropTypeId;
//
//     /// <summary>Read-only ordered list of growth stages from seedling to maturity.</summary>
//     public IReadOnlyList<GrowthStage> Stages => stages;
//
//     /// <summary>
//     /// Defines the visual and timing properties for a single growth stage.
//     /// A crop advances to the next stage after secondsToNextStage real-time seconds have elapsed.
//     /// </summary>
//     [Serializable]
//     public class GrowthStage
//     {
//         [Tooltip("Seconds needed in this stage before progressing to the next stage.")]
//         public float secondsToNextStage = 10f;
//         [Tooltip("Animator state name for this stage (optional if you use sprites).")]
//         public string animatorStateName = "Seedling";
//         [Tooltip("Sprite for this stage (optional if you use animator).")]
//         public Sprite sprite;
//     }
// }