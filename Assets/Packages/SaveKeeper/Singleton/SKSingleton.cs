using System;
using ThanhDV.SaveKeeper.AutoSave;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using ThanhDV.SaveKeeper.Infrastructure;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Singleton
{
    /// <summary>
    /// Optional singleton access point for one SaveKeeper instance.
    /// Call <see cref="InitializeBasic(string, SaveSettings)"/> for SaveKeeper alone, or <see cref="InitializeFull(string, SaveSettings, AutoSaveSettings)"/> to also bundle the auto-save service. Manual (DI) overloads accept pre-built instances.
    /// </summary>
    /// <remarks>
    /// This facade keeps global state separate from the core SaveKeeper classes.
    /// Projects using dependency injection can ignore this assembly.
    /// </remarks>
    public static class SKSingleton
    {
        // Salt used with Application.identifier to derive the default encryption key.
        // Do not change after release: existing saves will no longer decrypt.
        private const string DEFAULT_KEY_SALT = "ThanhDV.SaveKeeper.Json.";

        private static volatile ISaveKeeper _instance;
        private static volatile ISaveRegistry _registry;
        private static volatile ISKAutoSave _autoSave;

        /// <summary>
        /// True once an <c>Initialize</c> overload has run.
        /// </summary>
        public static bool Exists => _instance != null;

        /// <summary>The shared SaveKeeper instance.</summary>
        /// <exception cref="InvalidOperationException">Thrown if accessed before Initialize.</exception>
        public static ISaveKeeper Instance => _instance ?? throw NotInitialized();

        /// <summary>
        /// The SaveRegistry backing the shared instance. ISavable objects register/unregister here.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if accessed before Initialize.</exception>
        public static ISaveRegistry Registry => _registry ?? throw NotInitialized();

        /// <summary>
        /// The bundled auto-save service. Available only when initialized via <see cref="InitializeFull(string, SaveSettings, AutoSaveSettings)"/> or <see cref="InitializeFull(ISaveKeeper, ISaveRegistry, ISKAutoSave)"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if SKSingleton was not initialized, or was initialized via <c>InitializeBasic</c> (which does not bundle auto-save).
        /// </exception>
        public static ISKAutoSave AutoSave => _autoSave ?? throw NotAutoSaveAvailable();

        /// <summary>
        /// Manual basic init: sets a pre-built keeper and registry (no auto-save).
        /// Disposes the previous keeper and auto-save (if any) before swapping.
        /// </summary>
        /// <param name="keeper">The SaveKeeper instance to use.</param>
        /// <param name="registry">The registry used by the keeper.</param>
        public static void InitializeBasic(ISaveKeeper keeper, ISaveRegistry registry)
        {
            if (keeper == null) throw new ArgumentNullException(nameof(keeper));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _autoSave?.Dispose(); _autoSave = null;
            _instance?.Dispose();

            _instance = keeper;
            _registry = registry;
        }

        /// <summary>
        /// Manual full init: sets a pre-built keeper, registry, and auto-save service. SKSingleton owns lifecycle
        /// of all three: <see cref="Shutdown"/> (or a subsequent Initialize) disposes them in order auto-save → keeper.
        /// </summary>
        /// <remarks>
        /// Caller is responsible for calling <c>autoSave.Start()</c> if desired. SKSingleton does NOT start it
        /// automatically — manual callers may need to defer Start until a profile is loaded or a scene is ready.
        /// </remarks>
        public static void InitializeFull(ISaveKeeper keeper, ISaveRegistry registry, ISKAutoSave autoSave)
        {
            if (autoSave == null) throw new ArgumentNullException(nameof(autoSave));

            InitializeBasic(keeper, registry);
            _autoSave = autoSave;
        }

        /// <summary>
        /// Auto-config basic init: builds SaveKeeper with local storage, JSON serialization, and AES encryption.
        /// Does NOT bundle auto-save — use <see cref="InitializeFull(string, SaveSettings, AutoSaveSettings)"/> for that.
        /// </summary>
        /// <param name="encryptionKey">
        /// Master key to use. If null or empty, a per-project key is derived automatically.
        /// </param>
        /// <param name="settings">
        /// Settings to use. If null, library defaults are used (<c>new SaveSettings()</c>).
        /// </param>
        /// <remarks>
        /// Unsupported platforms should call <see cref="InitializeBasic(ISaveKeeper, ISaveRegistry)"/> with a custom <see cref="IStorageProvider"/>.
        /// </remarks>
        public static void InitializeBasic(string encryptionKey = null, SaveSettings settings = null)
        {
            SaveRegistry registry = new();

            string key = encryptionKey;
            if (string.IsNullOrEmpty(key))
            {
                key = DEFAULT_KEY_SALT + Application.identifier;
                SKLogger.Warning("SKSingleton is using an auto-derived default encryption key. For production, pass your own key: SKSingleton.InitializeBasic(\"your-master-key\").");
            }

            ISaveKeeper keeper = new Core.SaveKeeper(
                registry,
                new LocalStorageProvider(Application.persistentDataPath),
                new JsonSerializer(),
                new AESProvider(key),
                settings ?? new SaveSettings()
            );

            InitializeBasic(keeper, registry);
        }

        /// <summary>
        /// Auto-config full init: same as <see cref="InitializeBasic(string, SaveSettings)"/> plus a bundled auto-save service.
        /// SKSingleton constructs, starts, and owns the lifecycle of the auto-save.
        /// </summary>
        /// <param name="encryptionKey">Master key. If null or empty, derived automatically.</param>
        /// <param name="settings">SaveKeeper settings. Pass null for library defaults.</param>
        /// <param name="autoSaveSettings">Auto-save settings. Pass <c>null</c> for built-in defaults (300s interval, quit-flush=on).</param>
        public static void InitializeFull(string encryptionKey = null, SaveSettings settings = null, AutoSaveSettings autoSaveSettings = null)
        {
            InitializeBasic(encryptionKey, settings);

            _autoSave = new SKAutoSave(_instance, autoSaveSettings ?? new AutoSaveSettings());
            _autoSave.Start();
        }

        /// <summary>
        /// Clears static state before runtime starts.
        /// Prevents stale instances when Unity enters Play Mode without domain reload.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shutdown();

        #region Helper

        /// <summary>
        /// Disposes the current instance and clears the service.
        /// Accessing it again requires a new initialization.
        /// </summary>
        public static void Shutdown()
        {
            _autoSave?.Dispose(); _autoSave = null;

            _instance?.Dispose(); _instance = null;

            _registry = null;
        }

        private static InvalidOperationException NotInitialized() => new("SKSingleton is not initialized. Call SKSingleton.InitializeBasic() or SKSingleton.InitializeFull() in your startup code before accessing Instance/Registry.");

        private static InvalidOperationException NotAutoSaveAvailable() => new("SKSingleton.AutoSave is unavailable. Use SKSingleton.InitializeFull(...) to bundle auto-save, or construct SKAutoSave manually.");

        #endregion
    }
}
