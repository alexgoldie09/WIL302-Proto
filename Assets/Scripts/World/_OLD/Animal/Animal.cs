using System;
using UnityEngine;

/// <summary>
/// A saveable animal that wanders the screen by periodically picking a new random direction.
/// Clamps its position to the orthographic camera bounds with configurable padding.
/// Flips its sprite to face the direction of travel.
/// </summary>
[DisallowMultipleComponent]
public class Animal : SaveableBehaviour<Animal.Data>
{
    /// <summary>
    /// Serializable save payload for an Animal, storing the UTC spawn time for debugging and reference.
    /// </summary>
    [Serializable]
    public class Data
    {
        public long spawnedUtcTicks;
    }

    [Header("Movement")]
    [SerializeField, Tooltip("Units per second.")]
    private float moveSpeed = 1.5f;
    [SerializeField, Tooltip("Seconds between changing wander direction.")]
    private float changeDirectionInterval = 1.5f;
    [SerializeField, Tooltip("Extra padding so the animal stays fully on-screen.")]
    private float cameraPadding = 0.25f;

    // UTC ticks when this animal was first spawned.
    private long spawnedUtcTicks;
    // Current normalised wander direction.
    private Vector2 direction;
    // Timestamp at which the next direction change is due.
    private float nextDirChangeTime;

    private Camera cam;
    private SpriteRenderer spriteRenderer;

    public override string RecordType => "Animal";
    public override int LoadPriority => 10;

    #region Unity Lifecycle
    /// <summary>
    /// Ensures a persistent GUID is set, caches component references, and picks an initial wander direction.
    /// </summary>
    private void Awake()
    {
        EnsurePersistentGuid();

        cam = Camera.main;
        spriteRenderer = GetComponent<SpriteRenderer>();

        PickNewDirection();
        nextDirChangeTime = Time.time + changeDirectionInterval;
    }

    /// <summary>
    /// Each frame, changes wander direction on a timer and moves the animal within camera bounds.
    /// </summary>
    private void Update()
    {
        if (cam == null)
            cam = Camera.main;

        // Change wander direction once the interval has elapsed.
        if (Time.time >= nextDirChangeTime)
        {
            PickNewDirection();
            nextDirChangeTime = Time.time + changeDirectionInterval;
        }

        MoveAndClamp();
    }
    #endregion

    #region Initialisation
    /// <summary>
    /// Records the spawn timestamp for a freshly created animal and ensures it has a persistent GUID.
    /// Should be called immediately after Instantiate for new (non-loaded) animals.
    /// </summary>
    public void InitializeNew()
    {
        spawnedUtcTicks = DateTime.UtcNow.Ticks;
        EnsurePersistentGuid();
    }
    #endregion

    #region Movement
    /// <summary>
    /// Picks a new random normalised wander direction, falling back to Vector2.right
    /// if the random result is near-zero.
    /// </summary>
    private void PickNewDirection()
    {
        direction = UnityEngine.Random.insideUnitCircle;
        if (direction.sqrMagnitude < 0.05f)
            direction = Vector2.right;

        direction.Normalize();
    }

    /// <summary>
    /// Moves the animal along the current direction, flips the sprite to face travel direction,
    /// and clamps the position inside the orthographic camera bounds with padding applied.
    /// </summary>
    private void MoveAndClamp()
    {
        // Flip sprite to face the horizontal travel direction.
        if (spriteRenderer != null)
        {
            if (direction.x > 0.01f)
                spriteRenderer.flipX = true;
            else if (direction.x < -0.01f)
                spriteRenderer.flipX = false;
        }

        var pos = (Vector2)transform.position;
        pos += direction * (moveSpeed * Time.deltaTime);

        // Clamp to camera bounds and reflect direction on each axis when a boundary is hit.
        if (cam != null && cam.orthographic)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;

            float minX = cam.transform.position.x - halfW + cameraPadding;
            float maxX = cam.transform.position.x + halfW - cameraPadding;
            float minY = cam.transform.position.y - halfH + cameraPadding;
            float maxY = cam.transform.position.y + halfH - cameraPadding;

            if (pos.x < minX) { pos.x = minX; direction.x =  Mathf.Abs(direction.x); }
            if (pos.x > maxX) { pos.x = maxX; direction.x = -Mathf.Abs(direction.x); }
            if (pos.y < minY) { pos.y = minY; direction.y =  Mathf.Abs(direction.y); }
            if (pos.y > maxY) { pos.y = maxY; direction.y = -Mathf.Abs(direction.y); }
        }

        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
    }
    #endregion

    #region Save and Load
    /// <summary>
    /// Builds a save payload containing the UTC ticks at which this animal was spawned.
    /// </summary>
    protected override Data BuildData()
    {
        return new Data
        {
            spawnedUtcTicks = spawnedUtcTicks
        };
    }

    /// <summary>
    /// Restores the spawn timestamp from a saved payload and resumes wandering from a fresh direction.
    /// </summary>
    protected override void ApplyData(Data data, SaveContext context)
    {
        if (data == null)
            return;

        // Restore spawn timestamp.
        spawnedUtcTicks = data.spawnedUtcTicks;

        // Resume wandering with a fresh direction and reset the direction change timer.
        PickNewDirection();
        nextDirChangeTime = Time.time + changeDirectionInterval;
    }
    #endregion
}