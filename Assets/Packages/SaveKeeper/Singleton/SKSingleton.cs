using System;
using ThanhDV.SaveKeeper.AutoSave;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using ThanhDV.SaveKeeper.Infrastructure;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Singleton
{
    /// <summary>
    /// Optional singleton facade around one SaveKeeper. Use <see cref="InitializeBasic(string, SaveSettings)"/>
    /// for SaveKeeper alone, or <see cref="InitializeFull(string, SaveSettings, AutoSaveSettings)"/> to bundle
    /// auto-save. Manual overloads accept pre-built instances for DI scenarios.
    /// </summary>
    /// <remarks>Projects using a DI container can ignore this assembly entirely.</remarks>
    public static class SKSingleton
    {
        // Salt used with Application.identifier to derive the default encryption key.
        // Do not change after release: existing saves will no longer decrypt.
        private const string DEFAULT_KEY_SALT = "ThanhDV.SaveKeeper.Json.";

        private static volatile ISaveKeeper _instance;
        private static volatile ISaveRegistry _registry;
        private static volatile ISKAutoSave _autoSave;

        /// <summary>True once any Initialize overload has run.</summary>
        public static bool Exists => _instance != null;

        /// <summary>The shared SaveKeeper instance.</summary>
        /// <exception cref="InvalidOperationException">Accessed before Initialize.</exception>
        public static ISaveKeeper Instance => _instance ?? throw NotInitialized();

        /// <summary>The registry backing <see cref="Instance"/>; ISavables register/unregister here.</summary>
        /// <exception cref="InvalidOperationException">Accessed before Initialize.</exception>
        public static ISaveRegistry Registry => _registry ?? throw NotInitialized();

        /// <summary>
        /// The bundled auto-save service — available only after an <c>InitializeFull</c> overload.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not initialized, or initialized via <c>InitializeBasic</c>.</exception>
        public static ISKAutoSave AutoSave => _autoSave ?? throw NotAutoSaveAvailable();

        /// <summary>
        /// Manual basic init: stores a pre-built keeper + registry (no auto-save). Disposes any previously
        /// stored keeper/auto-save before swapping.
        /// </summary>
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
        /// Manual full init: stores a pre-built keeper, registry, and auto-save service. SKSingleton owns
        /// lifecycle for all three; <see cref="Shutdown"/> (or a subsequent Initialize) disposes auto-save → keeper.
        /// </summary>
        /// <remarks>
        /// Caller controls <c>autoSave.Start()</c> — SKSingleton does NOT auto-start the supplied service.
        /// </remarks>
        public static void InitializeFull(ISaveKeeper keeper, ISaveRegistry registry, ISKAutoSave autoSave)
        {
            if (autoSave == null) throw new ArgumentNullException(nameof(autoSave));

            InitializeBasic(keeper, registry);
            _autoSave = autoSave;
        }

        /// <summary>
        /// Auto-config basic init: builds SaveKeeper with local storage + JSON + AES. No auto-save —
        /// use <see cref="InitializeFull(string, SaveSettings, AutoSaveSettings)"/> for that.
        /// </summary>
        /// <param name="encryptionKey">Master key; null/empty derives one from <c>Application.identifier</c>.</param>
        /// <param name="settings">SaveKeeper settings; null uses <c>new SaveSettings()</c>.</param>
        /// <remarks>
        /// Unsupported platforms (WebGL, consoles) should use <see cref="InitializeBasic(ISaveKeeper, ISaveRegistry)"/>
        /// with a custom <see cref="IStorageProvider"/>.
        /// </remarks>
        public static void InitializeBasic(string encryptionKey = null, SaveSettings settings = null)
        {
            SaveRegistry registry = new();

            string key = encryptionKey;
            if (string.IsNullOrEmpty(key))
            {
                key = DEFAULT_KEY_SALT + Application.identifier;
                SKLogger.Warning("SKSingleton is using an auto-derived default encryption key. For production, pass your own key: SKSingleton.InitializeBasic(\"your-master-key\") or SKSingleton.InitializeFull(\"your-master-key\").");
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
        /// Auto-config full init: <see cref="InitializeBasic(string, SaveSettings)"/> plus a bundled, auto-started auto-save service.
        /// </summary>
        /// <param name="encryptionKey">Master key; null/empty derives one from <c>Application.identifier</c>.</param>
        /// <param name="settings">SaveKeeper settings; null uses library defaults.</param>
        /// <param name="autoSaveSettings">Auto-save settings; null uses defaults (300s interval, quit-flush on).</param>
        public static void InitializeFull(string encryptionKey = null, SaveSettings settings = null, AutoSaveSettings autoSaveSettings = null)
        {
            InitializeBasic(encryptionKey, settings);

            _autoSave = new SKAutoSave(_instance, autoSaveSettings ?? new AutoSaveSettings());
            _autoSave.Start();
        }

        /// <summary>
        /// Clears static state before runtime starts — prevents stale instances when Unity enters Play Mode
        /// with domain reload disabled.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Shutdown();

        #region Helper

        /// <summary>Disposes and clears the keeper, registry, and bundled auto-save. Requires re-init to use again.</summary>
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
