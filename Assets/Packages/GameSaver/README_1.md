# ThanhDV.GameSaver

A professional save game library for Unity. Async/sync APIs, multi-profile slots, authenticated encryption, atomic writes with self-healing metadata cache, and rename-safe type identity.

---

## Features

- **Async + sync save APIs** with awaitable handles (progress + completion events)
- **Multi-profile** support with metadata for save-slot UI
- **AES-256-CBC + HMAC-SHA256** (Encrypt-then-MAC) — tamper detection built in
- **Atomic file writes** with automatic `.bak` backup and crash-safe `.meta` sidecar
- **Auto-save** with periodic interval and Application-quit / mobile-focus-loss flush
- **Direct-access API** (`SetSimple` / `GetSimple`) for primitive key/value storage
- **Rename-safe type identity** via `[SaveDataAlias]` attribute
- **RCE-resistant deserialization** via whitelist `SafeTypeBinder`
- **Thread-safe registry** and per-profile coalesced save pipeline
- **Path-traversal defense** in the default file-system provider
- **Clock-skew detection** logs a warning when system clock moves backward
- **Editor-only verbose logging** — log noise is stripped from built games

---

## Requirements

- Unity **2022.3** or later
- **.NET Standard 2.1**
- Dependency: [`thanhdv.newtonsoft.json`](https://github.com/ThanhDang143/ThanhDV.Newtonsoft.Json)

---

## Platform Support

See [README.md](README.md) for the full platform matrix. Quick summary:

| Platform | `LocalStorageProvider` |
|---|---|
| Windows, macOS, Linux, iOS, Android | ✅ Supported |
| WebGL, PlayStation, Xbox, Switch | ❌ Provide a custom `IStorageProvider` |

The rest of the library (serializer, encryption, registry, async pipeline) works on all platforms — only file-system I/O needs platform-specific replacement.

---

## Installation

UPM via Git URL or Scoped Registry. See [README.md](README.md) for details.

---

## Quick Start

```csharp
using ThanhDV.GameSaver.Core;
using ThanhDV.GameSaver.Infrastructure;

// 1. Bootstrap (do this at a loading/splash screen — see "Initialization timing" below)
SaveSettings settings   = Resources.Load<SaveSettings>("SaveSettings");
SaveRegistry registry   = new SaveRegistry();
IStorageProvider store  = new LocalStorageProvider(Application.persistentDataPath);
ISerializer ser         = new JsonSerializer();
IEncryptionProvider enc = new AESProvider("your-master-key-here");
GameSaver saver         = new GameSaver(registry, store, ser, enc, settings);

// 2. Implement ISavable on a service
public class PlayerStats : MonoBehaviour, ISavable
{
    public string SaveKey => "PlayerStats";   // unique, immutable

    public ISaveData CaptureData() => new PlayerStatsData { Hp = _hp, Gold = _gold };

    public void RestoreData(ISaveData data)
    {
        var d = (PlayerStatsData)data;
        _hp = d.Hp;
        _gold = d.Gold;
    }
}

[SaveDataAlias("PlayerStats")]                 // stable alias for $type
public class PlayerStatsData : ISaveData
{
    public int Hp;
    public int Gold;
}

// 3. Register, save, load
registry.Register(playerStats);
await saver.SaveAsync(profileId: "Slot1", metadata: new MySaveMeta { /* ... */ });
await saver.LoadAsync(profileId: "Slot1");
```

---

## How to Use

### ISavable — for service-level state

`ISavable` represents a long-lived object whose state persists across game sessions: player stats, inventory, quest progress, settings, world state.

- Each `ISavable` declares a unique, immutable `SaveKey`.
- On save, the library calls `CaptureData()` on every registered savable and stores results keyed by `SaveKey`.
- On load, the library calls `RestoreData(data)` only for keys present in the loaded file. Missing entries are skipped — your code does not need to null-check.

### SimpleData — for primitives and one-off values

For values that do not warrant a dedicated `ISaveData` class:

```csharp
saver.SetSimple("difficulty", "Hard");
saver.SetSimple("tutorial_completed", true);
saver.SetSimple("last_played_map", 7);

string diff   = saver.GetSimple("difficulty",      defaultValue: "Normal");
bool   tutDone = saver.GetSimple("tutorial_completed", defaultValue: false);
int    map    = saver.GetSimple("last_played_map", defaultValue: 1);
```

`SetSimple` / `DeleteSimple` write to memory only — call `SaveAsync` or `SaveImmediate` to persist to disk.

### Profiles

A profile is a save slot. Each profile gets its own folder under `Application.persistentDataPath`.

```csharp
await saver.SaveAsync("Slot1", metadata);     // explicit profile
await saver.SaveAsync();                       // uses current profile (set by last save/load)

await saver.LoadAsync("Slot1");
await saver.LoadMostRecentAsync();             // resolves the most recently modified profile

saver.GetAllProfiles();                        // list of profileIds
saver.DeleteProfile("Slot1");
```

### Save metadata (for save-slot UI)

```csharp
public class MySaveMeta : ISaveMeta
{
    public string ProfileID   { get; set; }
    public DateTime LastTimeSaved { get; set; }
    public string LevelName;
    public int PlayTimeMinutes;
}

await saver.SaveAsync("Slot1", new MySaveMeta {
    ProfileID       = "Slot1",
    LastTimeSaved   = DateTime.UtcNow,
    LevelName       = "Forest",
    PlayTimeMinutes = 42
});

List<MySaveMeta> slots = await saver.GetAllMetadataAsync<MySaveMeta>();
// Sorted by LastTimeSaved descending.
```

If you pass `null` as metadata, the library uses `DefaultSaveMeta` (only `ProfileID` + `LastTimeSaved`). Those slots will not appear in `GetAllMetadataAsync<MyCustomMeta>()` because the type does not match — use `GetAllMetadataAsync<DefaultSaveMeta>()` or `GetAllMetadataAsync<ISaveMeta>()` to see them.

### Auto-save

Configured via `SaveSettings`:

- `EnableAutoSave` — periodic save every `AutoSaveTime` seconds while a profile is active.
- `AutoSaveOnQuit` — flush pending saves on `Application.quitting` (desktop) and focus loss (mobile only).
- `AutoSaveOnQuitTimeout` — milliseconds to wait for in-flight async saves before forcing a synchronous `SaveImmediate`.

Subscribe to `OnSaveCompleted` for UI feedback:

```csharp
saver.OnSaveCompleted += profileId => ShowSavedIcon(profileId);
// Fires on the SynchronizationContext where GameSaver was constructed (typically Unity main thread).
```

### Async handles

All async APIs return `GameSaverOperationHandle` (or `GameSaverOperationHandle<T>`):

```csharp
var handle = saver.SaveAsync("Slot1", metadata);

// Option A: await
await handle;

// Option B: callback
handle.Completed += h => Debug.Log($"Save: {h.Status}");

// Option C: poll
while (!handle.IsDone) yield return null;
```

Handles expose `Status`, `Error`, `PercentComplete`, and `Result` (for generic handles).

---

## Best Practices & Contracts

### SaveKey must be unique project-wide

Registering two `ISavable` instances with the same `SaveKey` throws `DuplicateSaveKeyException`. `SaveKey` is also **immutable after Register** — do not return a different string later, or `Unregister` will leak entries.

If a `MonoBehaviour`-based savable is destroyed without calling `Unregister`, and a new instance later registers the same key, the registry auto-replaces the dead reference and logs a warning. Still call `Unregister` in `OnDestroy` as the recommended flow.

### Use SaveKey for services, NOT for runtime-spawned instances

`SaveKey` identifies a long-lived role in your game — not individual game objects. Per-instance keys cause file bloat, slow registry scans, and ambiguous lifecycle.

✅ **DO** — one service holds collection state:

```csharp
public class EnemyManager : MonoBehaviour, ISavable
{
    public string SaveKey => "EnemyManager";

    public ISaveData CaptureData() => new EnemyManagerData {
        Enemies = _activeEnemies.Select(e => e.Snapshot()).ToList()
    };
}

[SaveDataAlias("EnemyManager")]
public class EnemyManagerData : ISaveData
{
    public List<EnemySnapshot> Enemies;
}
```

❌ **DON'T** — per-spawned-instance keys:

```csharp
public class Enemy : MonoBehaviour, ISavable
{
    public string SaveKey => $"Enemy_{_uniqueId}";   // unbounded growth
}
```

**Why?** SaveKey scales with services (10–50 typical), not with game objects (thousands).

### `[SaveDataAlias]` for rename-safe type identity

Annotate every concrete `ISaveData` / `ISaveMeta` with a stable alias:

```csharp
[SaveDataAlias("Inventory")]
public class InventoryData : ISaveData { /* ... */ }
```

- The alias is written to the save file's `$type` field, **not** the C# class name.
- The class can be renamed or moved between namespaces freely as long as the alias stays.
- **Alias is immutable after shipping** — changing it makes existing player saves unreadable.
- Aliases must be **unique across the project**. Duplicate aliases throw at startup.
- Inheritance does NOT propagate the attribute. Each concrete class needs its own `[SaveDataAlias]`. Best practice: base class has none, concrete classes have one each.
- The library skips interfaces, abstract classes, and generic type definitions during scan.

Without the attribute, the library falls back to `Type.FullName` — renaming the class then breaks saves.

### Assembly scan timing — initialize at a loading screen

`SafeTypeBinder` scans loaded assemblies once when the serializer is constructed. Typical cost: 30–100 ms; can reach several hundred milliseconds on large projects.

Construct `GameSaver` (and therefore `JsonSerializer`) at a splash / bootstrap / loading screen, not during gameplay. Subsequent saves and loads do not re-scan.

For runtime-loaded assemblies (mod support, hot-loaded `.dll`), register manually after the load:

```csharp
serializer.Binder.Register(typeof(MyModSaveData), "MyMod.Inventory");
```

Most games do not need this.

### Encryption — master key immutable after shipping

Changing the master key passed to `AESProvider` after release means previous saves cannot be decrypted — players lose progress. Choose carefully at launch.

The library does not support key rotation by design: any attacker who can decompile the binary already has the key, so rotation is security theater for client-side saves. The library's HMAC authentication detects **tampered** save files regardless.

A weak key (under 16 characters) logs a warning at startup. Use a GUID or a 32+ character random string.

### Metadata contract — caller is responsible for field correctness

The library does **not** mutate caller-supplied `ISaveMeta`. If you pass `metadata` to `SaveAsync`:

- `metadata.ProfileID` is written to disk **as-is**. Set it to match `profileId`.
- `metadata.LastTimeSaved` is written to disk **as-is**. Set it to `DateTime.UtcNow` (or your server time).
- Other fields (level name, playtime, etc.) are written as-is.

If you pass `null`, the library creates a `DefaultSaveMeta(profileId, DateTime.UtcNow)`. The default contains only the two required `ISaveMeta` fields — no custom info.

**Implication:** if you mix `null` and custom metadata across saves on the same profile, custom info is lost when `null` is passed.

### Unsaved-changes safeguard

`SetSimple` and `DeleteSimple` mark the registry as dirty. The dirty flag is cleared on every successful save or load.

`LoadAsync` / `LoadBackupAsync` / `LoadMostRecentAsync` **throw** (the handle completes failed) when dirty data exists, to prevent silent loss when switching profiles. Override explicitly:

```csharp
await saver.LoadAsync("Slot2", discardUnsavedChanges: true);
```

Inspect via `saver.IsSimpleDataDirty` to decide whether to prompt the user first.

### Save file layout

Per-profile folder under `Application.persistentDataPath`:

```
profile/
├── save.sav          source of truth — encrypted, contains Meta + ObjectData + SimpleData
├── save.sav.bak      backup created by atomic safe-replace
├── save.meta         fast-list cache — plain JSON, just the Meta header
└── save.meta.bak     backup of the sidecar
```

- `.sav` is the **source of truth**. `LoadAsync` always reads it.
- `.meta` is a **cache** for fast slot-listing UI. If it is missing or stale (mtime older than `.sav`), `GetAllMetadataAsync` rebuilds it from `.sav` automatically (self-heal).
- File names and extensions are configurable via `SaveSettings.FileName` / `SaveExtension` / `MetaExtension`.

### Logging behavior

The library uses a single static `DebugLog` class. Behavior across build modes:

| Category | Editor | Built game |
|---|---|---|
| `Log` / `Success` | Visible | Stripped (`[Conditional("UNITY_EDITOR")]`) |
| `Warning` / `Error` | Visible | Visible |

Sensitive data (file paths, exception stack traces) is sanitized in built games via `DebugLog.SanitizePath` / `SanitizeException` to avoid leaking PII to crash reporters (Bugsnag, Sentry, Unity Cloud Diagnostics).

`SaveKey` and `ProfileID` are **not** sanitized — if you use real player names as `ProfileID`, consider hashing them yourself before passing to the library.

### Clock skew detection

If the system clock moves backward by more than 1 second between observations, the library logs a warning. This commonly happens with timezone travel on mobile, manual time changes, or NTP corrections.

The library does **not** correct timestamps automatically. Slot ordering by `LastTimeSaved` may be wrong after a clock jump; the warning lets you debug the symptom.

---

## Platform Limitations

`LocalStorageProvider` throws `PlatformNotSupportedException` on its constructor for:

- **WebGL** — browser sandbox has no real filesystem and no thread pool for async I/O. Use a custom provider backed by `PlayerPrefs` or `IndexedDB` (via a `.jslib` bridge).
- **PlayStation 4 / 5, Xbox One / Series, Nintendo Switch** — console certification requires the platform's official Save Data API. Wrap that SDK in a custom `IStorageProvider`.

The rest of the library is platform-agnostic. See [README.md](README.md) for an `IStorageProvider` implementation skeleton.

---

## Architecture

The system design diagrams live in [`SystemDesign~/`](SystemDesign~/):

- `SystemDesign.mmd` — index pointing to the 4 sub-diagrams
- `01-Architecture-Overview.mmd` — contracts, core, services
- `02-Save-Load-Pipeline.mmd` — runtime flow
- `03-Operation-Handles.mmd` — async handle pattern
- `04-Serialization-Storage.mmd` — infrastructure layer
- `Flow_SaveGame.mmd` — save sequence diagram
- `Flow_LoadGame.mmd` — load sequence diagram

Render with [Mermaid Live Editor](https://mermaid.live), the VS Code Mermaid extension, or any tool supporting `.mmd`.

---

## API Reference (selected)

| Type | Purpose |
|---|---|
| `GameSaver` | Main facade. Save/Load/Delete + SimpleData. |
| `SaveRegistry` | Register / Unregister `ISavable` instances. |
| `SaveSettings` | ScriptableObject configuration. |
| `ISavable` | Implement on your service classes. |
| `ISaveData` | Implement on data containers per savable. |
| `ISaveMeta` | Implement on metadata for save-slot UI. |
| `DefaultSaveMeta` | Library-provided fallback meta. |
| `SaveDataAliasAttribute` | `[SaveDataAlias("alias")]` for rename-safe identity. |
| `DuplicateSaveKeyException` | Thrown when two savables share a `SaveKey`. |
| `GameSaverOperationHandle` | Awaitable handle returned by async APIs. |
| `IStorageProvider` / `LocalStorageProvider` | File-system I/O abstraction + default. |
| `ISerializer` / `JsonSerializer` | Serialization abstraction + Newtonsoft impl. |
| `IEncryptionProvider` / `AESProvider` | Encryption abstraction + AES + HMAC impl. |

---

## License

MIT

## Author

ThanhDV — vanthanh1998@gmail.com
