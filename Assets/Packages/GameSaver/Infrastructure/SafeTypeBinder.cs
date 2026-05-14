using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ThanhDV.GameSaver.Common;
using ThanhDV.GameSaver.Core;

namespace ThanhDV.GameSaver.Infrastructure
{
    /// <summary>
    /// A safe <see cref="ISerializationBinder"/> for GameSaver's JSON serialization.
    /// Replaces Newtonsoft's default behavior of writing assembly-qualified names — which is
    /// vulnerable to type-injection attacks and brittle when classes are renamed — with a
    /// whitelist of explicitly-registered short aliases.
    /// </summary>
    /// <remarks>
    /// Behavior overview:
    /// <list type="bullet">
    /// <item>On serialize: writes a short stable alias into the $type field.</item>
    /// <item>On deserialize: rejects any $type value that is not in the registered whitelist,
    /// blocking remote-code-execution via gadget chains.</item>
    /// </list>
    /// Auto-discovery of ISaveData / ISaveMeta types is performed in the constructor (Step 3).
    /// Manual <see cref="Register{T}"/> calls may be used to override or add entries before
    /// any serialization happens.
    /// </remarks>
    public sealed class SafeTypeBinder : ISerializationBinder
    {
        private readonly Dictionary<string, Type> _aliasToType = new();
        private readonly Dictionary<Type, string> _typeToAlias = new();

        // Guards both dictionaries. Reads happen on every BindToName / BindToType call —
        // these may run on threadpool threads when save pipelines for different profiles
        // serialize in parallel. Writes happen only at init or via Register.
        private readonly object _lock = new();

        /// <summary>
        /// Creates a SafeTypeBinder and auto-discovers all types in currently-loaded assemblies that
        /// implement <see cref="ISaveData"/>, <see cref="ISaveMeta"/>, or carry a <see cref="SaveDataTypeAttribute"/>.
        /// Each candidate is registered with its <see cref="SaveDataTypeAttribute.Alias"/> when present,
        /// otherwise with its <c>Type.FullName</c>.
        /// </summary>
        /// <remarks>
        /// Types loaded after construction (e.g., from AssetBundles) are NOT auto-discovered. Use
        /// <see cref="Register{T}"/> to add them manually before any serialization occurs.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Thrown when two distinct types claim the same alias. Fix the conflict — either rename one
        /// class, change one of the <see cref="SaveDataTypeAttribute"/> values, or move types to different
        /// namespaces so their <c>FullName</c> differs.
        /// </exception>
        public SafeTypeBinder()
        {
            ScanAssemblies();
        }

        /// <summary>
        /// Registers a type with the given alias. The alias is what appears in the save file's $type field at runtime.
        /// </summary>
        /// <typeparam name="T">The type to register.</typeparam>
        /// <param name="alias">A stable alias for T. Must be unique across all registered types.</param>
        /// <exception cref="ArgumentException">
        /// Thrown when alias is null or whitespace, when the alias is already bound to a different type, or when the type is already bound to a different alias.
        /// </exception>
        public void Register<T>(string alias)
        {
            Register(typeof(T), alias);
        }

        /// <summary>
        /// Used by the auto-scan path.
        /// </summary>
        public void Register(Type type, string alias)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentException("Alias cannot be null or whitespace.", nameof(alias));

            lock (_lock)
            {
                // Idempotent: registering the exact same mapping twice is a no-op.
                // This keeps auto-scan + user manual registration friendly.
                if (_aliasToType.TryGetValue(alias, out Type existingType))
                {
                    if (existingType == type) return;
                    throw new ArgumentException($"Alias '{alias}' is already bound to type '{existingType.FullName}'. Cannot rebind to '{type.FullName}'. Aliases must be unique.");
                }

                if (_typeToAlias.TryGetValue(type, out string existingAlias))
                {
                    throw new ArgumentException($"Type '{type.FullName}' is already registered with alias '{existingAlias}'. Cannot rebind to '{alias}'. A type may only have one alias.");
                }

                _aliasToType[alias] = type;
                _typeToAlias[type] = alias;
            }
        }

        /// <summary>
        /// Returns true if the given alias has been registered.
        /// </summary>
        public bool IsRegistered(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return false;
            lock (_lock)
            {
                return _aliasToType.ContainsKey(alias);
            }
        }

        public bool IsRegistered(Type type)
        {
            if (type == null) return false;
            lock (_lock)
            {
                return _typeToAlias.ContainsKey(type);
            }
        }

        #region Implementation

        /// <summary>
        /// Converts a runtime type to its registered alias for JSON serialization.
        /// </summary>
        /// <param name="serializedType">The type to serialize.</param>
        /// <param name="assemblyName">Always null (assemblies omitted for portability).</param>
        /// <param name="typeName">Returns the registered alias.</param>
        /// <exception cref="ArgumentNullException">When type is null.</exception>
        /// <exception cref="JsonSerializationException">When type is not registered.</exception>
        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            if (serializedType == null) throw new ArgumentNullException(nameof(serializedType));

            bool found;
            string alias;
            lock (_lock)
            {
                found = _typeToAlias.TryGetValue(serializedType, out alias);
            }

            if (!found)
            {
                throw new JsonSerializationException($"Type '{serializedType.FullName}' is not registered. Implement ISaveData/ISaveMeta, add [SaveDataType], or call Serializer.Binder.Register<T>().");
            }

            assemblyName = null;
            typeName = alias;
        }

        /// <summary>
        /// Converts a $type field back to a Type via whitelisted lookup.
        /// Tries direct alias match first, then FullName for backward compatibility with old saves.
        /// </summary>
        /// <param name="assemblyName">Ignored (legacy compatibility only).</param>
        /// <param name="typeName">The type alias or FullName from $type.</param>
        /// <returns>The registered Type.</returns>
        /// <exception cref="ArgumentException">When typeName is null or empty.</exception>
        /// <exception cref="JsonSerializationException">When type is not registered (security boundary).</exception>
        public Type BindToType(string assemblyName, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                throw new ArgumentException("typeName cannot be null or empty.", nameof(typeName));
            }

            Type result = null;
            lock (_lock)
            {
                // Strategy 1: direct alias lookup (covers both new aliases and legacy FullName).
                if (_aliasToType.TryGetValue(typeName, out Type direct))
                {
                    result = direct;
                }
                // Strategy 2: FullName fallback for legacy saves where [SaveDataType] was added later.
                else
                {
                    foreach (KeyValuePair<Type, string> tta in _typeToAlias)
                    {
                        if (tta.Key.FullName == typeName)
                        {
                            result = tta.Key;
                            break;
                        }
                    }
                }
            }

            if (result != null) return result;

            throw new JsonSerializationException($"SafeTypeBinder cannot deserialize type '{typeName}'. Check: type deleted, renamed without [SaveDataType] update, save from newer build, assembly loaded after construction, or save file was tampered.");
        }

        #endregion

        #region Helper

        /// <summary>
        /// Walks every currently-loaded non-system assembly and registers every candidate type.
        /// </summary>
        private void ScanAssemblies()
        {
            const long SLOW_SCAN_THRESHOLD_MS = 100;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int assembliesScanned = 0;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly assembly in assemblies)
            {
                if (IsSystemAssembly(assembly)) continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Some types in this assembly failed to load (missing dependency, etc.).
                    // Use only the types that did load — partial scan is better than aborting.
                    types = ex.Types.Where(t => t != null).ToArray();
                    DebugLog.Warning($"SafeTypeBinder partial type load in assembly '{assembly.GetName().Name}'. {types.Length} types loaded, {ex.LoaderExceptions?.Length ?? 0} failures skipped.");
                }
                catch (Exception ex)
                {
                    DebugLog.Warning($"SafeTypeBinder failed to enumerate types in assembly '{assembly.GetName().Name}': {ex.Message}. Skipped.");
                    continue;
                }

                assembliesScanned++;

                foreach (Type type in types)
                {
                    if (!IsCandidate(type)) continue;
                    string alias = ResolveAlias(type);
                    Register(type, alias);
                }
            }

            stopwatch.Stop();
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            int registeredCount = _aliasToType.Count;

            if (elapsedMs > SLOW_SCAN_THRESHOLD_MS)
            {
                DebugLog.Warning($"SafeTypeBinder scan took {elapsedMs}ms ({registeredCount} types, {assembliesScanned} assemblies) — exceeds {SLOW_SCAN_THRESHOLD_MS}ms budget.");
            }
            else
            {
                DebugLog.Success($"SafeTypeBinder scan complete in {elapsedMs}ms ({registeredCount} types, {assembliesScanned} assemblies).");
            }
        }

        private static bool IsSystemAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;

            return name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("mscorlib", StringComparison.Ordinal)
                || name.StartsWith("Microsoft", StringComparison.Ordinal)
                || name.StartsWith("Unity", StringComparison.Ordinal)
                || name.StartsWith("Newtonsoft", StringComparison.Ordinal)
                || name.StartsWith("netstandard", StringComparison.Ordinal)
                || name.StartsWith("Mono.", StringComparison.Ordinal)
                || name.StartsWith("nunit", StringComparison.Ordinal);
        }

        /// <summary>
        /// A type is a candidate iff it is concrete and either implements ISaveData / ISaveMeta or carries [SaveDataType].
        /// </summary>
        private static bool IsCandidate(Type type)
        {
            if (type.IsAbstract) return false;
            if (type.IsInterface) return false;
            if (type.IsGenericTypeDefinition) return false;
            if (string.IsNullOrEmpty(type.FullName)) return false;

            bool implementsSavable = typeof(ISaveData).IsAssignableFrom(type) || typeof(ISaveMeta).IsAssignableFrom(type);
            bool hasAttribute = type.GetCustomAttribute<SaveDataTypeAttribute>() != null;

            return implementsSavable || hasAttribute;
        }

        /// <summary>
        /// Returns the alias declared by [SaveDataType] if present, otherwise <c>Type.FullName</c>.
        /// </summary>
        private static string ResolveAlias(Type type)
        {
            SaveDataTypeAttribute attr = type.GetCustomAttribute<SaveDataTypeAttribute>();
            return attr != null ? attr.Alias : type.FullName;
        }

        #endregion
    }
}
