# GameSaver

A lightweight save system for Unity featuring:
- Multiple profiles (separate folders).
- Auto-selection of the most recently modified profile.
- Periodic auto-saving.
- AES encryption (GCM with a fallback to CBC + HMAC).
- Safe backup and recovery mechanisms.
- In-editor data viewing and editing.
- Prevents consecutive rapid save calls from overlapping/overwriting each other
- Dependency Injection friendly.
---

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
- ``Name`` ThanhDV
- ``URL`` https://upm.thanhdv.com
- ``Scope(s)`` thanhdv

2. In Unity, open **Window** → **Package Manager**.
- Press the **+** button, choose "**Add package by name...**" → ``thanhdv.gamesaver``
- or
- Press the **Packages** button, choose "**My Registries**"

---

## How to Use

### 1. Configure SaveSettings
In Unity, navigate to **Tools** → **GameSaver** → **SaveSettings** to create and modify the settings asset.

**Options:**
- `Create Profile If Null`: If no save data is found, automatically create a new profile with default settings.
- `Use Encryption`: Toggles data encryption on or off.
- `Save As Separate Files`: Determines whether to save all data into a single file or separate files for each module.
- `Enable Auto Save` & `Auto Save Time`: Configures the automatic saving interval.

### 2. Create Data Container
Create a class to hold save data by implementing the `ISaveData` interface. This class will contain the actual data you want to persist.

```csharp
public class PlayerData : ISaveData
{
    public int Level { get; set; }
    public float Health { get; set; }
    public Vector3 LastPosition { get; set; }
    public Dictionary<string, int> Inventory { get; set; }

    public PlayerData()
    {
        Level = 1;
        Health = 100f;
        LastPosition = Vector3.zero;
        Inventory = new Dictionary<string, int>();
    }
}
```

### 3. Implement the ISavable Interface
- Any object that needs to be saved, whether it's a `MonoBehaviour` or a plain C# class, must implement the `ISavable` interface.
- You can also implement `ISavableLoad` if you need to be notified when the data has been successfully loaded.
```csharp
public class Player : ISavable, ISavableLoad
{
    // --- Player's runtime data ---
    private int level;
    private float health;
    private Vector3 position;
    private Dictionary<string, int> inventory;

    // 1. Specify the data container type created in the previous step.
    public Type SaveType => typeof(PlayerData);

    // 2. Implement the logic to load data from the container.
    public void LoadData(SaveData data)
    {
        // Retrieve the specific data container from the global save data.
        if (data.TryGetData(out PlayerData playerData))
        {
            // Apply the loaded data to this object's state.
            this.level = playerData.Level;
            this.health = playerData.Health;
            this.transform.position = playerData.LastPosition;
            this.inventory = playerData.Inventory;
        }
    }

    // 3. Implement the logic to save data into the container.
    public void SaveData(SaveData data)
    {
        // Retrieve the specific data container.
        if (data.TryGetData(out PlayerData playerData))
        {
            // Update the container with this object's current state.
            playerData.Level = this.level;
            playerData.Health = this.health;
            playerData.LastPosition = this.transform.position;
            playerData.Inventory = this.inventory;
        }
    }

    // (Optional) 4. Implement the logic after load done.
    public void OnLoadCompleted(SaveData data)
    {
        if (data.TryGetData(out PlayerData playerData))
        {
            // Do somethings
        }
    }
}
```

### 4. Register Objects with GameSaver
To be included in the save/load process, each `ISavable` object must be registered.

**For `MonoBehaviour` components:**
Use `Awake` and `OnDestroy` for registration. This is the standard and safest approach.
```csharp
public class Player : MonoBehaviour, ISavable
{
    private void Awake()
    {
        SaveRegistry.Register(this);
    }

    private void OnDestroy()
    {
        SaveRegistry.Unregister(this);
    }
    // ... ISavable implementation ...
}
```

**For `non-MonoBehaviour` (plain C#) classes:**
Implement `IDisposable` and register in the constructor.
```csharp
public class Player : ISavable, IDisposable
{
    public PlayerStats()
    {
        SaveRegistry.Register(this);
    }

    public void Dispose()
    {
        SaveRegistry.Unregister(this);
    }
    // ... ISavable implementation ...
}
```
⚠️ **Important:** For `non-MonoBehaviour` classes, you are responsible for calling `Dispose()` manually when the object is no longer needed. The C# garbage collector does not call `Dispose()` automatically.

### 5. Interacting with GameSaver
Use the `GameSaver.Instance` singleton to manage save/load operations.

```csharp
// Initialize GameSaver. This happens automatically if you have prefab GameSaver in scene.
// IMPORTANT: Wait for GameSaver to finish initializing before calling any Save/Load/NewGame APIs.
await GameSaver.Instance.WaitForInitializeDone();

// Create a new game.
GameSaver.Instance.NewGame(); // Uses the current or default profile ID.
GameSaver.Instance.NewGame("Profile_Slot_1"); // Specifies a custom profile ID.

// Save the current game state.
GameSaver.Instance.SaveGameAsync();
GameSaver.Instance.SaveGameImmediate();

// Load the game state.
await GameSaver.Instance.LoadGame(); // Loads the current profile.

// Load all existing profiles.
Dictionary<string, SaveData> allProfiles = await GameSaver.Instance.LoadAllProfile();

// Switch to a different profile.
GameSaver.Instance.SetProfileID("Profile_Slot_2");
await GameSaver.Instance.LoadGame(); // Load data from the new profile's data.
// GameSaver.Instance.SaveGame(); // Save current data to the new profile.

// Delete a profile.
GameSaver.Instance.DeleteData("Profile_Slot_1");
```
⚠️ **Key Points:**
- Always ensure `GameSaver` is initialized before calling methods like `NewGame`, `SaveGame`, or `LoadGame`...
- If `Create Profile If Null` is disabled, you must call `NewGame()` at least once before `SaveGame()` or `LoadGame()` can succeed.
- After calling `SetProfileID()`, you typically want to call `LoadGame()` to load the new profile's data. Or call `SaveGame()` to save current data to the new profile.
- After `DeleteData()`, the system automatically switches to the most recently used profile or the default profile.
- By default, data is loaded when `GameSaver` initializes and saved when the application quits (or is paused on Android/iOS).

### 7. Dependency Injection (DI) Integration
For projects using a Dependency Injection framework (like Reflex, VContainer, Zenject, etc.), you can make `GameSaver` DI-friendly by defining the `INJECTION_ENABLED` scripting symbol in your project settings.

**How it works:**
- When `INJECTION_ENABLED` is defined, the standard singleton (`GameSaver.Instance`) is disabled.
- This allows you to register `GameSaver` with your DI container and inject it into other classes instead of accessing it globally.

**Example (using a generic DI container):**
```csharp
// In your Reflex installer or composition root
var gameSaverObject = new GameObject("GameSaver");
var gameSaverInstance = gameSaverObject.AddComponent<GameSaver>();

builder.AddSingleton(gameSaverInstance);

// In your consumer class
public class MyGameManager
{
    [Inject] private readonly GameSaver _gameSaver;

    public MyGameManager(GameSaver gameSaver)
    {
        _gameSaver = gameSaver;
    }

    public void Save()
    {
        _gameSaver.SaveGameAsync();
    }
}
```

By defining `INJECTION_ENABLED`, you take responsibility for creating and managing the `GameSaver` instance's lifecycle through your DI container.

### 6. Others
#### Wait until data is loaded
```csharp
// Use when running the game for the first time. Data is automatically loaded by the system
// and this completes after data is applied to all ISavable and OnLoadCompleted is called.
await GameSaver.Instance.WhenInitialized;
DoSomething();

// Use when you manually trigger a game load
await GameSaver.Instance.LoadGame();
DoSomething();

// For other systems that need to observe this event
GameSaver.Instance.OnLoadDataCompleted += DoSomething; // Subscribe
GameSaver.Instance.OnLoadDataCompleted -= DoSomething; // Unsubscribe
```
#### Save/Load Events
```csharp
// Called when data loading starts
GameSaver.Instance.OnLoadDataStarted += DoSomething; // Subscribe
GameSaver.Instance.OnLoadDataStarted -= DoSomething; // Unsubscribe

// Called when data loading is complete
GameSaver.Instance.OnLoadDataCompleted += DoSomething; // Subscribe
GameSaver.Instance.OnLoadDataCompleted -= DoSomething; // Unsubscribe

// Called when data saving starts
GameSaver.Instance.OnSaveDataStarted += DoSomething; // Subscribe
GameSaver.Instance.OnSaveDataStarted -= DoSomething; // Unsubscribe

// Called when data saving is complete
GameSaver.Instance.OnSaveDataCompleted += DoSomething; // Subscribe
GameSaver.Instance.OnSaveDataCompleted -= DoSomething; // Unsubscribe
```

## Other Information
### Encryption
- Uses AES-GCM if available, with a fallback to AES-CBC + HMAC-SHA256 for broader compatibility.
- Employs a stable passphrase that can be optionally bound to the device for added security.
- Encryption can be disabled in the `SaveSettings` for easier debugging.