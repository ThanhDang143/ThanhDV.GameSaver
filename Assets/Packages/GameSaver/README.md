# GameSaver

A lightweight save system for Unity featuring:

- Feature 1
- Feature 2
- ...

---

## Platform Support

The default `LocalStorageProvider` uses `System.IO.File` APIs and is supported on the following platforms:

| Platform | Supported | Notes |
|---|:---:|---|
| Windows (Standalone) | ✅ | Full support |
| macOS (Intel + Apple Silicon) | ✅ | Full support |
| Linux (Standalone) | ✅ | Full support |
| iOS | ✅ | Save data stored under `Application.persistentDataPath`. iCloud backup is enabled by default — opt out per Apple guidelines if your save is regeneratable cache. |
| Android | ✅ | Stored in app internal storage (scoped storage compliant) |
| WebGL | ❌ | Browser sandbox has no real filesystem and no thread pool for async I/O. Provide a custom `IStorageProvider` backed by `PlayerPrefs` or `IndexedDB` (via a `.jslib` bridge). |
| PlayStation 4 / 5 | ❌ | Console certification requires using Sony's Save Data API. Provide a custom `IStorageProvider`. |
| Xbox One / Series X\|S (GDK) | ❌ | Console certification requires using Xbox Connected Storage / Title Storage. Provide a custom `IStorageProvider`. |
| Nintendo Switch | ❌ | Console certification requires using `nn::fs` SDK. Provide a custom `IStorageProvider`. |

On unsupported platforms, `LocalStorageProvider`'s constructor throws `PlatformNotSupportedException` at startup. Inject your own `IStorageProvider` implementation to enable save/load on those platforms — the rest of the library (serializer, encryption, registry, async pipeline) remains fully functional.

### Implementing a custom `IStorageProvider`

```csharp
public class MyPlatformStorageProvider : IStorageProvider
{
    public Task WriteAsync(string profileId, string fileName, string data) { /* ... */ }
    public void WriteImmediate(string profileId, string fileName, string data) { /* ... */ }
    public Task<string> ReadAsync(string profileId, string fileName) { /* ... */ }
    public Task<string> ReadBackupAsync(string profileId, string fileName) { /* ... */ }
    public void RestoreBackup(string profileId, string fileName) { /* ... */ }
    public void DeleteProfile(string profileId) { /* ... */ }
    public IEnumerable<string> GetAllProfileIds() { /* ... */ }
    public string GetMostRecentProfileId() { /* ... */ }
    public bool Exists(string profileId, string fileName) { /* ... */ }
}

// Then inject it when constructing GameSaver:
var gameSaver = new GameSaver(registry, new MyPlatformStorageProvider(), serializer, encryption, settings);
```

## Installation

### Unity Package Manager (via Git URL)

```
https://github.com/ThanhDang143/ThanhDV.GameSaver.git?path=/Assets/Packages/GameSaver
```

1. In Unity, open **Window** → **Package Manager**.
2. Click the **+** button and select "**Add package from git URL...**"
3. Paste the URL above and click **Add**.

### Scoped Registry

1. In Unity, open **Project Settings** → **Package Manager** → **Add New Scoped Registry**

- `Name` ThanhDV
- `URL` https://upm.thanhdv.com
- `Scope(s)` thanhdv

2. In Unity, open **Window** → **Package Manager**.

- Press the **+** button, choose "**Add package by name...**" → `thanhdv.gamesaver`
- or
- Press the **Packages** button, choose "**My Registries**"

## How to Use
