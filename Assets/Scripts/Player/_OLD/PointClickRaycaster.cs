// using UnityEngine;
// using UnityEngine.InputSystem;
//
// /// <summary>
// /// Listens for pointer press input and casts a 2D ray into the scene to determine what was clicked.
// /// Prioritises AnimalSpawnPoints over Plot sockets, delegating to the appropriate handler for each.
// /// A configurable cooldown prevents accidental double-triggers.
// /// </summary>
// public class PointerClickRaycaster : MonoBehaviour
// {
//     [Header("References")]
//     [SerializeField] private Camera targetCamera;
//
//     [Header("Input Actions")]
//     [SerializeField] private InputActionReference pointAction; // <Pointer>/position
//     [SerializeField] private InputActionReference pressAction; // <Pointer>/press
//
//     [Header("Raycast")]
//     [SerializeField, Tooltip("Layers that include BOTH plot sockets and animal spawn points.")]
//     private LayerMask interactionLayerMask;
//     [SerializeField, Tooltip("Optional cooldown to prevent accidental double triggers (seconds).")]
//     private float clickCooldown = 0.10f;
//
//     [Header("Animal Spawning")]
//     [SerializeField, Tooltip("Animal prefab to spawn when clicking an AnimalSpawnPoint.")]
//     private Animal animalPrefab;
//
//     private float lastClickTime = -999f;
//
//     #region Unity Lifecycle
//     /// <summary>
//     /// Falls back to Camera.main if no target camera is assigned in the inspector.
//     /// </summary>
//     private void Awake()
//     {
//         if (targetCamera == null)
//             targetCamera = Camera.main;
//     }
//
//     /// <summary>
//     /// Enables input actions and subscribes to the press event.
//     /// </summary>
//     private void OnEnable()
//     {
//         if (pointAction != null) pointAction.action.Enable();
//         if (pressAction != null) pressAction.action.Enable();
//
//         if (pressAction != null)
//             pressAction.action.started += OnPressStarted;
//     }
//
//     /// <summary>
//     /// Unsubscribes from the press event and disables input actions.
//     /// </summary>
//     private void OnDisable()
//     {
//         if (pressAction != null)
//             pressAction.action.started -= OnPressStarted;
//
//         if (pointAction != null) pointAction.action.Disable();
//         if (pressAction != null) pressAction.action.Disable();
//     }
//     #endregion
//
//     #region Input Handling
//     /// <summary>
//     /// Callback fired by the press input action. Triggers a raycast on the first frame of each press.
//     /// </summary>
//     private void OnPressStarted(InputAction.CallbackContext ctx)
//     {
//         RaycastAndInteract();
//     }
//
//     /// <summary>
//     /// Reads the current pointer position, casts a 2D ray at that point, and dispatches
//     /// to the appropriate interaction handler based on what was hit.
//     /// AnimalSpawnPoints take priority over Plot sockets.
//     /// </summary>
//     private void RaycastAndInteract()
//     {
//         // Enforce cooldown to prevent accidental double-triggers.
//         if (Time.unscaledTime - lastClickTime < clickCooldown)
//             return;
//
//         lastClickTime = Time.unscaledTime;
//
//         if (targetCamera == null || pointAction == null)
//             return;
//
//         // Convert screen-space pointer position to a world-space ray.
//         Vector2 screenPos = pointAction.action.ReadValue<Vector2>();
//         Vector3 worldPos = targetCamera.ScreenToWorldPoint(screenPos);
//
//         var hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactionLayerMask);
//         if (hit.collider == null)
//             return;
//
//         // 1) AnimalSpawnPoint has priority — check the hit collider and its parents.
//         var spawnPoint = hit.collider.CompareTag("SpawnPoint") ? hit.collider.gameObject.transform : null;
//
//         if (spawnPoint != null)
//         {
//             TrySpawnAnimal(spawnPoint.position);
//             return;
//         }
//
//         // 2) Otherwise treat the hit as a plot socket click.
//         var plot = hit.collider.GetComponentInParent<Plot>();
//         if (plot == null)
//             return;
//
//         int socketIndex = plot.GetSocketIndexFromCollider(hit.collider);
//         if (socketIndex < 0)
//             return;
//
//         plot.TryPlantDefaultAtSocket(socketIndex);
//     }
//     #endregion
//
//     #region Spawning
//     /// <summary>
//     /// Instantiates the assigned animal prefab at the given world position and calls InitializeNew
//     /// to set it up as a freshly spawned animal.
//     /// </summary>
//     private void TrySpawnAnimal(Vector3 position)
//     {
//         if (animalPrefab == null)
//         {
//             Debug.LogWarning("[PointerClickRaycaster] Animal prefab not assigned.");
//             return;
//         }
//
//         var animal = Instantiate(animalPrefab, position, Quaternion.identity);
//         animal.InitializeNew();
//     }
//     #endregion
// }