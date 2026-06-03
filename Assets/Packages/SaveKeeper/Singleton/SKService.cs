using System;
using System.Reflection;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using ThanhDV.SaveKeeper.Infrastructure;
using UnityEngine;
using CoreKeeper = ThanhDV.SaveKeeper.Core.SaveKeeper;

namespace ThanhDV.SaveKeeper.Singleton
{
    /// <summary>
    /// Optional singleton access point for one <see cref="Keeper"/> instance.
    /// Call <c>Initialize(...)</c> before using <see cref="Instance"/> or <see cref="Registry"/>.
    /// </summary>
    /// <remarks>
    /// This facade keeps global state separate from the core SaveKeeper classes.
    /// Projects using dependency injection can ignore this assembly.
    /// </remarks>
    public static class SKService
    {
        // Salt used with Application.identifier to derive the default encryption key.
        // Do not change after release: existing saves will no longer decrypt.
        private const string DEFAULT_KEY_SALT = "ThanhDV.SaveKeeper.Json.";

        // Resolve the generated factory by reflection to avoid a compile-time assembly reference.
        // Must match SaveKeeperSettingsGenerator's generated type and assembly names.
        private const string GENERATED_FACTORY_AQN = "ThanhDV.SaveKeeper.Core.SaveKeeperSettings, SaveKeeper.Generated";

        private static volatile ISaveKeeper _instance;
        private static volatile SaveRegistry _registry;

        /// <summary>
        /// True once an <c>Initialize</c> overload has run.
        /// </summary>
        public static bool Exists => _instance != null;

        /// <summary>The shared SaveKeeper instance.</summary>
        /// <exception cref="InvalidOperationException">Thrown if accessed before Initialize.</exception>
        public static ISaveKeeper Instance => _instance ?? throw NotInitialized();

        // <summary>
        /// The SaveRegistry backing the shared instance. ISavable objects register/unregister here.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if accessed before Initialize.</exception>
        public static SaveRegistry Registry => _registry ?? throw NotInitialized();

        /// <summary>
        /// Sets the shared SaveKeeper instance and registry.
        /// Disposes the previous instance if one already exists.
        /// </summary>
        /// <param name="keeper">The SaveKeeper instance to use.</param>
        /// <param name="registry">The registry used by the keeper.</param>
        public static void Initialize(CoreKeeper keeper, SaveRegistry registry)
        {
            if (keeper == null) throw new ArgumentNullException(nameof(keeper));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            _instance?.Dispose();

            _instance = keeper;
            _registry = registry;
        }

        /// <summary>
        /// Initializes the service with local storage, JSON serialization, and AES encryption.
        /// </summary>
        /// <param name="encryptionKey">
        /// Master key to use. If null or empty, a per-project key is derived automatically.
        /// </param>
        /// <param name="settings">
        /// Settings to use. If null, library defaults are used.
        /// </param>
        /// <remarks>
        /// Unsupported platforms should call <see cref="Initialize(SaveKeeper, SaveRegistry)"/> with a
        /// custom <see cref="IStorageProvider"/>.
        /// </remarks>
        public static void Initialize(string encryptionKey = null, SaveSettings settings = null)
        {
            SaveRegistry registry = new();

            string key = encryptionKey;
            if (string.IsNullOrEmpty(key))
            {
                key = DEFAULT_KEY_SALT + Application.identifier;
                DebugLog.Warning("SKService is using an auto-derived default encryption key. For production, pass your own key: SKService.Initialize(\"your-master-key\").");
            }

            CoreKeeper keeper = new(
                registry,
                new LocalStorageProvider(Application.persistentDataPath),
                new JsonSerializer(),
                new AESProvider(key),
                settings ?? GetSettings()
            );

            Initialize(keeper, registry);
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
            _instance?.Dispose();   // unsubscribe registry events, Application events, AutoSaveTicker
            _instance = null;
            _registry = null;
        }

        private static SaveSettings GetSettings()
        {
            Type factory = Type.GetType(GENERATED_FACTORY_AQN);
            if (factory == null)
            {
                DebugLog.Warning("Generated settings not found; using defaults. Open Settings and click Apply.");
                return new SaveSettings();
            }

            MethodInfo create = factory.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            if (create == null || create.Invoke(null, null) is not SaveSettings settings)
            {
                DebugLog.Error("Generated factory contract mismatch. Method 'Create()' is missing. " +
                               "\nUpdate the AQN or method name if the generator output changed.");
                return new SaveSettings();
            }

            return settings;
        }

        private static InvalidOperationException NotInitialized() => new("SKService is not initialized. Call SKService.Initialize() (or an Initialize overload) in your startup code before accessing Instance/Registry.");

        #endregion
    }
}
