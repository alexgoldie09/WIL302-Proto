# Claude Code Prompt — Save System Integration

## Context

This is a Unity 2D mobile farm game built in C#. The project already has a save system scaffold in place with the following files already written and working:

- `SaveManager.cs` — registers all `ISaveable` objects, calls `CaptureState()` on save, `RestoreState()` on load, and stores `lastQuitUtcTicks` in the `SaveFile` for offline time calculation
- `SaveableBehaviour<TData>` — abstract generic base class for all saveable MonoBehaviours. Subclasses implement `BuildData()` and `ApplyData(TData data, SaveContext context)`
- `ISaveable.cs` — interface defining `PersistentGuid`, `RecordType`, `LoadPriority`, `CaptureState()`, and `RestoreState()`
- `SaveModels.cs` — defines `SaveFile`, `SaveRecord`, `TransformData`, and `SaveContext`. `SaveContext` includes `DateTime UtcNow`, `TimeSpan Elapsed`, and `Func<string, ISaveable> ResolveById`

The following systems **already correctly implement** `SaveableBehaviour` and do not need to be touched:

- `FaunaBase` — saves hunger, happiness, stage, gracePeriodTimer, inGracePeriod
- `CraftingManager` — saves unlocked recipe names
- `QuestManager` — saves activeQuestIndex, currentProgress, completedQuestNumbers
- `NotebookManager` — saves all notebook entries

---

## What Needs to Be Implemented

Implement `SaveableBehaviour` on each system below in the exact order listed. After implementing each one, stop and wait so the changes can be tested before proceeding to the next.

---

### 1. `PlayerInventory` — Load Priority 0

**Why first:** Every other system that loads after it (StoreFrontManager, QuestManager, FaunaBase feeding checks) depends on inventory being fully restored before it runs.

**Data to save:**

```csharp
[Serializable]
public class PlayerInventoryData
{
    public List<string> itemNames = new();
    public List<int> itemCounts = new();
}
```

Serialise `_inventory` as parallel lists of item names and counts. On restore, look up each `ItemDefinition` by name using an `ItemRegistry` (see note below). Restore all counts via the existing `Add()` method so `OnInventoryChanged` fires correctly for any subscribers that are already listening.

**ItemRegistry note:** There is no `ItemRegistry` yet. Create a simple `ScriptableObject` called `ItemRegistry` with a `List<ItemDefinition>` that can be searched by `ItemName`. `PlayerInventory` should hold a serialised reference to it. This same registry will be reused by `StoreFrontManager`.

---

### 2. `BiomeManager` — Load Priority 1

**Why second:** Biome tiers control which slots are visible, which store items are available, and which upgrades can be purchased. Everything tier-dependent must restore after this.

**Data to save:**

```csharp
[Serializable]
public class BiomeManagerData
{
    public List<int> biomeTiers = new(); // one int per biome, index matches BiomeType enum
}
```

The `upgradeTier` field on each `BiomeData` is currently `[NonSerialized]` and resets to 1 every session. On restore, call the existing `SetBiomeTier()` for each biome so `OnBiomeTierChanged` fires and all subscribers (slots, storefront) self-update correctly. Do not fire tier events if the restored tier equals 1 — that is the default and would cause unnecessary work on fresh saves.

---

### 3. `UpgradeManager` — Load Priority 4

**Why here:** Must load after BiomeManager (1) so tier prerequisite checks pass, but before world objects (10) that call `CheckAndApplySelfUpgrade()` in their `Start()`.

**Data to save:**

```csharp
[Serializable]
public class UpgradeManagerData
{
    public List<string> appliedUpgradeNames = new();
}
```

Serialise `_appliedUpgrades` as a list of upgrade names. On restore, repopulate `_appliedUpgrades` directly — **do not call `ApplyUpgrade()` again** during restore. The effects (biome tier changes, fauna cap increases, object upgrades) are restored by their respective systems. `UpgradeManager` only needs to know which upgrades are flagged as already done so `MeetsPrerequisites()` returns correctly and the store shows them as Owned.

---

### 4. `StoreFrontManager` — Load Priority 5

**Why here:** Depends on PlayerInventory (0) and BiomeManager (1) being restored. Must load before world objects.

`StoreFrontManager` currently extends `MonoBehaviour`. Change it to extend `SaveableBehaviour<StoreFrontManagerData>`.

**Data to save:**

```csharp
[Serializable]
public class StoreFrontManagerData
{
    public float coinBalance;
    public List<string> stockItemNames = new();
    public List<int> stockItemCounts = new();
    public List<string> buybackItemNames = new();
    public List<int> buybackItemCounts = new();
    public List<float> buybackItemPrices = new();
    public List<string> soldOutRecipeNames = new(); // recipes with stock == 0
}
```

**Coin balance:** Restore via the existing `SetBalance()` method.

**Stock:** `_stock` is seeded from the catalogue in `Start()` via `InitialiseStock()`. On restore, `InitialiseStock()` still runs first to populate the dictionary with catalogue defaults, then override each entry with the saved count using item name lookup via `ItemRegistry`. Only items that exist in the catalogue need restoring — ignore any saved names that no longer match.

**Buyback:** The `_buyback` dictionary is not seeded from any data source — it is built up entirely during play. Restore it fully from the saved parallel lists.

**Recipe stock:** Recipes start at stock 1 and drop to 0 when purchased. Only sold-out recipes (stock == 0) need saving. On restore, run `InitialiseRecipeStock()` first to seed defaults, then set stock to 0 for any recipe names in `soldOutRecipeNames`. Look up `RecipeDefinition` by name from the `RecipeBook` references already held by `StoreFrontManager`.

**Important:** `StoreFrontManager.Start()` currently calls `SetBalance(startingCoinBalance)` and then `InitialiseStock()`. After adding save/load, these calls should only run on a **fresh save** (no existing save record). On restore, skip `SetBalance(startingCoinBalance)` and let `ApplyData` drive the balance instead.

---

### 5. `StructureBase` — Load Priority 8

**Why here:** Structures live in slots. Their state must be restored before fauna and flora (10) so the slot system is in the correct state when world objects initialise.

`StructureBase` is already abstract and is the base for `EggCollectorStructure` and `WaterSprinklerStructure`. Add `SaveableBehaviour` here at the base level.

**Data to save:**

```csharp
[Serializable]
public class StructureData
{
    public int stage; // 0 = UnderConstruction, 1 = Built
    public float constructionTimer;
}
```

On restore: if `stage == 1` (Built), skip the construction coroutine entirely and call `CompleteConstruction()` directly. If `stage == 0`, restore `_constructionTimer` and let the coroutine resume from where it left off — do not restart from zero.

**prefabKey:** Each structure subclass must set `prefabKey` to the name of its prefab so `SaveManager` can respawn structures that were placed by the player and are not static scene objects.

---

### 6. `EggCollectorStructure` — Load Priority 11

Extends `StructureBase`. Override `BuildData()` and `ApplyData()` to add `_storedCount` on top of the base structure data.

```csharp
[Serializable]
public class EggCollectorData : StructureData
{
    public int storedCount;
}
```

On restore, call `UpdateStorageVisuals()` after setting `_storedCount` so the egg pile UI reflects the correct count immediately.

---

### 7. `FloraBase` (Crop and WoodTree) — Load Priority 10

`FloraBase` is abstract. Add `SaveableBehaviour` here. `Crop` and `WoodTree` override `RecordType` (already done — `"Crop"` and `"Tree"` respectively).

**Data to save:**

```csharp
[Serializable]
public class FloraData
{
    public float waterLevel;
    public float growthProgress;
    public int stage; // FloraGrowthStage enum as int
    public bool isLost;
    public float gracePeriodTimer;
    public bool inGracePeriod;
}
```

**Offline time compensation in `ApplyData`:** After restoring water level and growth progress, apply the time elapsed since the last save:

- Reduce water level by `(waterDecayRate * context.Elapsed.TotalSeconds)`, clamped to 0
- If water hits 0 during this calculation, enter the grace period and reduce the grace timer accordingly
- If the grace timer expires during offline time, mark the flora as lost
- Do **not** advance growth stage during offline time — growth only happens while the player is active

**prefabKey:** Each crop and tree type must set `prefabKey` to its prefab name.

---

### 8. `WoodTree` — Add `_chopHealth` to `BuildData`

`WoodTree` already overrides `BuildData()` and `ApplyData()` but has a TODO for `_chopHealth`. Fill it in:

```csharp
[Serializable]
public class WoodTreeData : FloraData
{
    public float chopHealth;
}
```

In `BuildData()`, call `base.BuildData()` and add `chopHealth = _chopHealth`. In `ApplyData()`, call `base.ApplyData()` then set `_chopHealth = data.chopHealth` instead of calling `ResetChopHealth()`. This preserves partial chop progress across sessions.

**Note:** Both `Crop` and `WoodTree` have TODOs for restoring `outputItem` via `ItemRegistry`. These TODOs should be resolved as part of this step — look up the `ItemDefinition` by the saved item name using the `ItemRegistry` created in step 1.

---

### 9. Offline Time Compensation — `FaunaBase.ApplyData`

`FaunaBase` already saves and restores correctly, and all four animal subclasses (`Duck`, `Cow`, `Sheeb`, `Clam`) already correctly extend `FaunaData` with their own fields — **do not touch those subclasses**. Only add offline time compensation to the existing `FaunaBase.ApplyData` method:

- Apply hunger decay for `context.Elapsed` using the existing `hungerDecayRate` and `lowHappinessHungerMultiplier`. Use the restored happiness value to calculate the correct multiplier
- Apply happiness decay for `context.Elapsed` using `happinessDecayRate`
- Clamp both to 0–1 after applying
- If hunger hits 0, enter the grace period and subtract elapsed time from the grace timer
- If the grace timer expires, call `LoseFauna()`
- If hunger remains above 0, exit the grace period

Do not call the hunger tick coroutine during `ApplyData` — the coroutine starts fresh from `Start()` after restoration.

---

### 9. `FaunaSpawner` — Coordination with Save System

`FaunaSpawner` itself does not need to implement `SaveableBehaviour`. Mobile fauna (Duck, Sheep) are saved and respawned by `SaveManager` using their `prefabKey`. However, `FaunaSpawner.SpawnToCapacity()` currently spawns a full fresh population on `Start()` with no awareness of fauna already restored by `SaveManager`.

**Fix required:** Before spawning, `SpawnToCapacity()` must count how many fauna of each type have already been restored into the scene by `SaveManager`. Only spawn the difference between the quota and the already-live count. The simplest approach is to scan the biome's active occupants via `BiomeManager.Instance.GetOccupantsOfType<FaunaBase>()` and count by prefab type before spawning.

**Timing note:** `SaveManager` restores objects before `Start()` runs on `FaunaSpawner`, so the restored fauna will already be registered as biome occupants by the time `SpawnToCapacity()` is called.

---

## Load Priority Summary

| Priority | System |
|---|---|
| 0 | PlayerInventory |
| 1 | BiomeManager |
| 2 | NotebookManager ✅ |
| 3 | QuestManager ✅ |
| 4 | UpgradeManager |
| 5 | StoreFrontManager |
| 8 | StructureBase |
| 10 | FaunaBase ✅ (add offline compensation only) |
| 10 | Duck, Cow, Sheeb, Clam ✅ (subclass data already implemented) |
| 10 | FloraBase |
| 10 | WoodTree (add _chopHealth + resolve outputItem TODO) |
| 10 | Crop (resolve outputItem TODO only) |
| 11 | EggCollectorStructure |

---

## General Rules for All Implementations

- All serialised data classes must be decorated with `[Serializable]` and use only `JsonUtility`-compatible types (no Dictionaries — use parallel lists)
- Item and recipe lookups must go through name-based matching, not direct object references, since `ScriptableObject` references cannot be serialised to JSON
- Never call game logic methods (e.g. `Add()`, `SetBiomeTier()`) during `BuildData()` — only read state
- Always call the appropriate setter methods during `ApplyData()` rather than setting fields directly, so events fire and subscribers update correctly
- Static scene objects (structures already in the scene) must have `isStatic = true` and a pre-assigned GUID in the Inspector
- Dynamically placed objects (crops, animals placed by the player) must have `prefabKey` set to their prefab name so `SaveManager` can respawn them on load
