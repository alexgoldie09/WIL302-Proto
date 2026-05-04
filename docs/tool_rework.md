# Claude Code Prompt — Tool Equip System Rework & Bag UI Overhaul

## Overview

The drag-to-use tool mechanic is being replaced with a tap-to-equip system. Tools are tapped in the world to equip them into `PlayerInventory`. Once equipped, tapping a `FloraBase` triggers the tool automatically on a timer — no dragging or holding required. The Bag UI is overhauled to match a new visual design with a dedicated tool slot row. All upgrades and tool effects remain functionally the same.

---

## Part 1 — ToolBase Rework

### 1A. Remove all drag input logic

Remove the following entirely from `ToolBase`:

- `OnWorldDragStart` subscription and `HandleDragStart`
- `OnWorldDrag` subscription and `HandleDrag`
- `OnWorldDragEnd` subscription and `HandleDragEnd`
- `_originPosition`, `_originRotation`, `_isDragging` fields
- `CameraPanner.SetPanEnabled` calls
- `ItemTrigger` subscription, `HandleTriggerEnter`, `HandleTriggerExit`
- `OnDragStarted()`, `OnDragEnded()` abstract/virtual methods
- `_currentObject` reference — now managed per-use inside `UseRoutine`

**Keep:**
- `IBiomeOccupant`, `IUpgradeable` interfaces
- `HomeBiome`, `UpgradeTypeId` accessors
- `CheckAndApplySelfUpgrade()` in `Start()`
- `ApplyUpgrade()` virtual method
- `interactableLayer` field — repurposed for tap detection

### 1B. Add tap-to-equip

In `OnEnable`, subscribe to `InputManager.Instance.OnWorldTap += HandleWorldTap`. In `OnDisable`, unsubscribe.

```csharp
private void HandleWorldTap(Vector2 worldPos)
{
    RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
    if (hit.collider == null || hit.collider.gameObject != gameObject) return;
    PlayerInventory.Instance?.EquipTool(this);
}
```

### 1C. Add tool usage API

Add the following to `ToolBase`:

```csharp
public bool _isInUse { get; private set; }

/// <summary>
/// Called by FloraBase when the player taps a plant while this tool is equipped.
/// Returns false if already in use.
/// </summary>
public bool TryUse(FloraBase target)
{
    if (_isInUse) return false;
    _isInUse = true;
    StartCoroutine(UseRoutine(target));
    return true;
}

protected abstract IEnumerator UseRoutine(FloraBase target);

protected void FinishUse()
{
    _isInUse = false;
}
```

Add these serialised fields to `ToolBase`:

```csharp
[Header("Use Settings")]
[SerializeField] protected float useDuration = 2f;
[SerializeField] protected GameObject toolSpritePrefab;
[SerializeField] protected float toolSpriteAngle = -45f;
[SerializeField] protected Vector2 toolSpriteOffset = new Vector2(0.5f, 0.5f);

[Header("UI")]
[SerializeField] private Sprite toolIcon;
public Sprite ToolIcon => toolIcon;
```

---

## Part 2 — Subclass Rework

### 2A. WateringCan

**Remove:** `Update()` watering loop, `OnObjectTouched`, `OnObjectLeft`, `OnDragEnded`, `SetTilt`, `_hasReportedWateringThisSession`, `tiltAngle`.

**Keep:** `waterPerSecond`, `waterUpgrade`, `ApplyUpgrade()`.

**Implement `UseRoutine`:**

```csharp
protected override IEnumerator UseRoutine(FloraBase target)
{
    GameObject spriteInstance = null;
    if (toolSpritePrefab != null)
    {
        Vector3 spawnPos = (Vector2)target.transform.position + toolSpriteOffset;
        spriteInstance = Instantiate(toolSpritePrefab, spawnPos,
            Quaternion.Euler(0f, 0f, toolSpriteAngle));
    }

    target.SetWateringFx(true);
    target.ShowStats(false);

    float elapsed = 0f;
    while (elapsed < useDuration && target != null && !target.IsLost)
    {
        target.Water(waterPerSecond * Time.deltaTime);
        elapsed += Time.deltaTime;
        yield return null;
    }

    target?.SetWateringFx(false);
    target?.HideStats();
    if (spriteInstance != null) Destroy(spriteInstance);

    string itemName = target?.GetOutputItemPublic()?.ItemName;
    QuestManager.Instance?.RecordProgress(QuestObjectiveType.WaterCrop, itemName, 1);

    FinishUse();
}
```

### 2B. ChoppingAxe

**Remove:** `OnObjectTouched`, `OnObjectLeft`, `OnDragEnded`, swing coroutine management from drag handlers.

**Keep:** `chopDamage`, `chopDamageUpgrade`, `swingStartAngle`, `swingEndAngle`, `swingDuration`, `ApplyUpgrade()`.

**Note:** `chopDamage` is now applied as `chopDamage * Time.deltaTime` per frame. Adjust the default value so the total damage over `useDuration` matches the intended number of hits (e.g. `chopDamage = 5f` over `useDuration = 2f` = 10 damage/sec, so 20 total).

**Implement `UseRoutine`:**

```csharp
protected override IEnumerator UseRoutine(FloraBase target)
{
    var tree = target as WoodTree;
    if (tree == null || tree.Stage != FloraGrowthStage.Harvestable)
    {
        FinishUse();
        yield break;
    }

    GameObject spriteInstance = null;
    if (toolSpritePrefab != null)
    {
        Vector3 spawnPos = (Vector2)target.transform.position + toolSpriteOffset;
        spriteInstance = Instantiate(toolSpritePrefab, spawnPos,
            Quaternion.Euler(0f, 0f, toolSpriteAngle));
    }

    tree.ShowStats(false);

    float elapsed = 0f;
    while (elapsed < useDuration && tree != null && !tree.IsLost
           && tree.Stage == FloraGrowthStage.Harvestable)
    {
        tree.Chop(chopDamage * Time.deltaTime);
        elapsed += Time.deltaTime;

        if (spriteInstance != null)
        {
            float t = Mathf.PingPong(elapsed / swingDuration, 1f);
            float angle = Mathf.Lerp(swingStartAngle, swingEndAngle, t * t);
            spriteInstance.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        yield return null;
    }

    tree?.HideStats();
    if (spriteInstance != null) Destroy(spriteInstance);
    FinishUse();
}
```

### 2C. Hoe

**Remove:** `Update()` hold timer loop, `OnObjectTouched`, `OnObjectLeft`, `OnDragEnded`, `_holdTimer`, swing coroutine management from drag handlers, `IBiomeOccupant` registration (tool upgrades now go through `PlayerInventory`).

**Keep:** `tier2HoldDuration`, `swingStartAngle`, `swingEndAngle`, `swingDuration`, `ApplyUpgrade()`. `ApplyUpgrade()` sets `useDuration = tier2HoldDuration`.

**Implement `UseRoutine`:**

```csharp
protected override IEnumerator UseRoutine(FloraBase target)
{
    if (target == null || target.IsLost)
    {
        FinishUse();
        yield break;
    }

    GameObject spriteInstance = null;
    if (toolSpritePrefab != null)
    {
        Vector3 spawnPos = (Vector2)target.transform.position + toolSpriteOffset;
        spriteInstance = Instantiate(toolSpritePrefab, spawnPos,
            Quaternion.Euler(0f, 0f, toolSpriteAngle));
    }

    target.ShowStats(false);

    float elapsed = 0f;
    while (elapsed < useDuration)
    {
        target?.SetRemoveProgress(Mathf.Clamp01(elapsed / useDuration));

        if (spriteInstance != null)
        {
            float t = Mathf.PingPong(elapsed / swingDuration, 1f);
            float angle = Mathf.Lerp(swingStartAngle, swingEndAngle, t * t);
            spriteInstance.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        elapsed += Time.deltaTime;
        yield return null;
    }

    AlertManager.Instance?.ClearAllAlerts(target.gameObject);
    var slot = target.ParentSlot;
    if (slot != null) slot.Clear();
    else if (target != null) Destroy(target.gameObject);

    if (spriteInstance != null) Destroy(spriteInstance);
    FinishUse();
}
```

---

## Part 3 — FloraBase Tap Rework

Update `OnTapped()` in `FloraBase` (and each subclass that overrides it) to check for an equipped tool first:

```csharp
public override void OnTapped()
{
    var tool = PlayerInventory.Instance?.EquippedTool;
    if (tool != null)
    {
        if (tool._isInUse)
        {
            // Tool busy — still show stats so the player has feedback
            ShowStats();
            return;
        }

        bool used = tool.TryUse(this);
        if (used)
        {
            ShowStats(false); // Keep stats visible during use
            return;
        }
    }

    // No tool equipped or tool rejected this flora (e.g. wrong type/stage)
    ShowStats();
}
```

**Note:** `WoodTree` overrides `OnTapped()` to call `ShowStats()` only. The axe still works via `TryUse()` — it validates the tree type and stage at the start of `UseRoutine`. No changes needed to `WoodTree.OnTapped()`.

---

## Part 4 — PlayerInventory Tool Slot

Add the following to `PlayerInventory`:

```csharp
[Header("Equipped Tool")]
private ToolBase _equippedTool;
public ToolBase EquippedTool => _equippedTool;

public event Action<ToolBase> OnToolChanged;

public void EquipTool(ToolBase tool)
{
    if (_equippedTool == tool) return;
    if (_equippedTool != null) UnequipTool();

    _equippedTool = tool;
    tool.gameObject.SetActive(false);

    ApplyToolUpgradeIfNeeded(tool);
    OnToolChanged?.Invoke(_equippedTool);
    Debug.Log($"[PlayerInventory] Equipped: {tool.name}");
}

public void UnequipTool()
{
    if (_equippedTool == null) return;

    _equippedTool.gameObject.SetActive(true);
    var prev = _equippedTool;
    _equippedTool = null;

    OnToolChanged?.Invoke(null);
    Debug.Log($"[PlayerInventory] Unequipped: {prev.name}");
}

private void ApplyToolUpgradeIfNeeded(ToolBase tool)
{
    if (UpgradeManager.Instance == null) return;
    foreach (var upgrade in UpgradeManager.Instance.GetAllUpgrades())
    {
        if (upgrade.UpgradeTypeId == tool.UpgradeTypeId &&
            UpgradeManager.Instance.IsUpgradeApplied(upgrade))
        {
            tool.ApplyUpgrade(upgrade);
            return;
        }
    }
}
```

**Save note:** Do NOT add `_equippedTool` to `PlayerInventoryData`. Tool world GameObjects re-enable at default on load (static scene objects). The player re-equips each session.

---

## Part 5 — Upgrade System for Tools

Keep `UpgradeManager` as-is. `EquipTool()` calls `ApplyToolUpgradeIfNeeded()` on equip, which guarantees upgrades apply regardless of whether the tool was active when the upgrade was purchased.

Tool upgrades are now biome-agnostic. `UpgradeDefinition.TargetBiome` is ignored for tool upgrades — `UpgradeManager` still handles purchase validation and owned state correctly via its existing logic.

---

## Part 6 — Bag UI Overhaul (InventoryUI)

### 6A. New visual layout

The Bag panel structure:
- Black semi-transparent panel background
- **Header row:** "Bag" label left-aligned, X close button right-aligned
- **Scrollable item grid:** 3 columns, existing items
- **Tool slot row:** centred single slot, always visible below the item grid

The panel slides up from the bottom as before.

### 6B. Tool slot implementation

Subscribe/unsubscribe in `OnEnable`/`OnDisable`:

```csharp
PlayerInventory.Instance.OnToolChanged += RefreshToolSlot;
```

Build the tool slot in code as a centred row below the item grid:

- Single slot cell matching item grid cell size
- Shows equipped tool icon (`PlayerInventory.Instance.EquippedTool?.ToolIcon`) when equipped
- Shows a greyed placeholder when empty
- Unequip button that calls `PlayerInventory.Instance.UnequipTool()`
- Button only interactable when a tool is equipped

```csharp
private void RefreshToolSlot()
{
    // Rebuild tool slot visuals based on PlayerInventory.Instance.EquippedTool
    // If equipped: show icon, enable unequip button
    // If null: show placeholder, disable unequip button
}
```

### 6C. X close button

Add an X button to the panel header that calls `SetOpen(false)`. The existing toggle button outside the panel hides when the inventory is open, as before.

---

## Inspector Setup After Implementation

### Tools (WateringCan, ChoppingAxe, Hoe)

| Field | Notes |
|---|---|
| `toolSpritePrefab` | Prefab with SpriteRenderer using the tool's existing sprite |
| `toolSpriteAngle` | Match old `swingStartAngle` for that tool |
| `toolSpriteOffset` | Tune per tool so sprite appears beside the flora naturally |
| `toolIcon` | UI icon sprite shown in the bag tool slot |
| `useDuration` | Suggested: WateringCan 2f, Hoe 2f, ChoppingAxe 3f |
| `interactableLayer` | Unchanged — same layer as before for tap detection |

### PlayerInventory
No new Inspector fields needed.

### InventoryUI
Wire `PlayerInventory.OnToolChanged` subscriptions in `OnEnable`/`OnDisable`.

---

## Systems Not Affected

- `AlertManager` — unchanged
- `BiomeManager` — unchanged
- `QuestManager` — `WaterCrop` and `HarvestItem` quest progress fires from `UseRoutine`, same as before
- `FaunaBase` / `FeedPanelUI` — unchanged, tapping animals still opens the feed panel
- `Slot` / `SlotPlacementUI` — unchanged
- `SaveManager` — tool equip state is intentionally not saved
