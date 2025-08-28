# GameSaver

Lightweight save system for Unity featuring:
- Multiple profiles (separate folders)
- Auto-select most recently modified profile
- Periodic Auto Save
- AES encryption (GCM or fallback CBC + HMAC)
- Safe backup / recovery
- View and edit data in editor.

---

## Installation
### Unity Package Manager
```
https://github.com/ThanhDang143/ThanhDV.GameSaver.git?path=/Assets/Packages/GameSaver
```

1. In Unity, open **Window** → **Package Manager**.
2. Press the **+** button, choose "**Add package from git URL...**"
3. Enter url above and press **Add**.

### Scoped Registry

1. In Unity, open **Project Settings** → **Package Manager** → **Add New Scoped Registry**
- ``Name`` ThanhDVs
- ``URL`` https://upm.thanhdv.icu
- ``Scope(s)`` thanhdv

2. In Unity, open **Window** → **Package Manager**.
- Press the **+** button, choose "**Add package by name...**" → ``thanhdv.gamesaver``
- or
- Press the **Packages** button, choose "**My Registries**"

---

## How to use

### 1. Configure SaveSettings
In Unity, open **Tools** → **GameSaver** → **SaveSettings**
Key options:
- Use Encryption: Turn encryption on/off.
- File Name: Save file name inside each profile folder.
- Enable Auto Save + Auto Save Time: Auto save interval.

### 2. Switch / select profile
```csharp
GameSaver.Instance.SetProfileID("Player01"); // creates if not existing
await GameSaver.Instance.LoadGame();         // loads that profile
```

### 3. Create fresh data
```csharp
GameSaver.Instance.NewGame(); // Clears in‑memory data (not yet written)
GameSaver.Instance.SaveGame();
```

### 4. Create a savable component
Implement `ISavable`:

```csharp
using UnityEngine;
using ThanhDV.GameSaver.Core;

public class PlayerStats : MonoBehaviour, ISavable
{
    [SerializeField] int level;
    [SerializeField] float hp;

    // Data block
    private class PlayerStatsData : ISaveData
    {
        public int Level;
        public float Hp;
    }

    // Write to SaveData
    public void SaveData(SaveData data)
    {
        var block = data.TryGetData<PlayerStatsData>();
        block.Level = level;
        block.Hp = hp;
    }

    // Read from SaveData
    public void LoadData(SaveData data)
    {
        var block = data.TryGetData<PlayerStatsData>();
        level = block.Level;
        hp = block.Hp;
    }
}
```

Registration is handled automatically by `SaveRegistry` (just have the component alive when saving/loading).

### 5. Save & load
```csharp
GameSaver.Instance.SaveGame();        // Save (internally may perform async I/O)
await GameSaver.Instance.LoadGame();  // Reload
```

### 6. Delete a profile
```csharp
GameSaver.Instance.DeleteData("Player01");
```

### 7. List / load all profiles
```csharp
Dictionary<string, SaveData> all = await GameSaver.Instance.LoadAll();
```

---

## Internal flow

1. `GameSaver` initializes -> reads `SaveSettings`
2. Resolves current profile (most recent or default)
3. Loads game -> invokes `LoadData` on every `ISavable`
4. Starts Auto Save loop (if enabled)
5. On save: aggregates data -> writes temp file, backup, replace

---

## Encryption

- AES-GCM if available, fallback AES-CBC + HMAC-SHA256
- Stable passphrase (optionally device-bound)
- Can be disabled for debugging

---

## Quick API

```csharp
GameSaver.Instance.SaveGame();
await GameSaver.Instance.LoadGame();
GameSaver.Instance.NewGame();
GameSaver.Instance.SetProfileID("ProfileX");
GameSaver.Instance.DeleteData("ProfileX");
var all = await GameSaver.Instance.LoadAll();
```