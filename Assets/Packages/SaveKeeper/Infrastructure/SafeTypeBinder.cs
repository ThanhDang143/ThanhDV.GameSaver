using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;

namespace ThanhDV.SaveKeeper.Infrastructure
{
    /// <summary>
    /// Whitelist <see cref="ISerializationBinder"/> for SaveKeeper's JSON. Replaces Newtonsoft's default
    /// assembly-qualified names (rename-brittle + RCE-vulnerable) with short, explicitly registered aliases.
    /// </summary>
    /// <remarks>
    /// Serialize: writes the registered alias into <c>$type</c>. Deserialize: rejects any <c>$type</c> not
    /// in the whitelist — blocks RCE via gadget chains.
    /// Auto-discovers types in the constructor; use <see cref="Register{T}"/> for assemblies loaded later.
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
        /// Auto-discovers every type in currently-loaded assemblies that implements <see cref="ISaveData"/>
        /// or <see cref="ISaveMeta"/>, or carries <see cref="SaveDataAliasAttribute"/>.
        /// Alias = attribute value when present, otherwise <c>Type.FullName</c>.
        /// </summary>
        /// <remarks>
        /// Types loaded later (AssetBundles, DLC) are NOT auto-discovered — call <see cref="Register{T}"/>
        /// for them before serialization.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Two distinct types claim the same alias — rename one, change a <see cref="SaveDataAliasAttribute"/>
        /// value, or move types so their FullNames differ.
        /// </exception>
        public SafeTypeBinder()
        {
            ScanAssemblies();
        }

        /// <summary>Registers <typeparamref name="T"/> with the given alias (written to the <c>$type</c> field).</summary>
        /// <param name="alias">Stable, unique alias for the type.</param>
        /// <exception cref="ArgumentException">
        /// Alias is null/whitespace, already bound to a different type, or this type already has a different alias.
        /// </exception>
        public void Register<T>(string alias)
        {
            Register(typeof(T), alias);
        }

        /// <summary>Non-generic <see cref="Register{T}"/>. Idempotent for identical (type, alias) pairs.</summary>
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

        /// <summary>True if the given alias has been registered.</summary>
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
        /// Newtonsoft hook: converts a runtime <see cref="Type"/> to its registered alias for the <c>$type</c> field.
        /// </summary>
        /// <param name="assemblyName">Always null — assemblies are omitted for portability.</param>
        /// <param name="typeName">Output: the registered alias.</param>
        /// <exception cref="ArgumentNullException">serializedType is null.</exception>
        /// <exception cref="JsonSerializationException">The type is not registered.</exception>
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
                throw new JsonSerializationException($"Type '{serializedType.FullName}' is not registered. Implement ISaveData/ISaveMeta, add [SaveDataAlias], or call Serializer.Binder.Register<T>().");
            }

            assemblyName = null;
            typeName = alias;
        }

        /// <summary>
        /// Newtonsoft hook: resolves a <c>$type</c> string back to a registered <see cref="Type"/>.
        /// Tries the direct alias first, falls back to <c>Type.FullName</c> match for legacy saves.
        /// </summary>
        /// <param name="assemblyName">Ignored (legacy compatibility only).</param>
        /// <exception cref="ArgumentException">typeName is null or empty.</exception>
        /// <exception cref="JsonSerializationException">The alias is not in the whitelist (security boundary).</exception>
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
                // Strategy 2: FullName fallback for legacy saves where [SaveDataAlias] was added later.
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

            throw new JsonSerializationException($"SafeTypeBinder cannot deserialize type '{typeName}'. Check: type deleted, renamed without [SaveDataAlias] update, save from newer build, assembly loaded after construction, or save file was tampered.");
        }

        #endregion

        #region Helper

        /// <summary>Walks every loaded non-system assembly and registers each candidate type.</summary>
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
                    SKLogger.Warning($"SafeTypeBinder partial type load in assembly '{assembly.GetName().Name}'. {types.Length} types loaded, {ex.LoaderExceptions?.Length ?? 0} failures skipped.");
                }
                catch (Exception ex)
                {
                    SKLogger.Warning($"SafeTypeBinder failed to enumerate types in assembly '{assembly.GetName().Name}': {ex.Message}. Skipped.");
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
                SKLogger.Warning($"SafeTypeBinder scan took {elapsedMs}ms ({registeredCount} types, {assembliesScanned} assemblies) — exceeds {SLOW_SCAN_THRESHOLD_MS}ms budget.");
            }
            else
            {
                SKLogger.Success($"SafeTypeBinder scan complete in {elapsedMs}ms ({registeredCount} types, {assembliesScanned} assemblies).");
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

        /// <summary>Concrete + implements <see cref="ISaveData"/>/<see cref="ISaveMeta"/> or carries <see cref="SaveDataAliasAttribute"/>.</summary>
        private static bool IsCandidate(Type type)
        {
            if (type.IsAbstract) return false;
            if (type.IsInterface) return false;
            if (type.IsGenericTypeDefinition) return false;
            if (string.IsNullOrEmpty(type.FullName)) return false;

            bool implementsSavable = typeof(ISaveData).IsAssignableFrom(type) || typeof(ISaveMeta).IsAssignableFrom(type);
            bool hasAttribute = type.GetCustomAttribute<SaveDataAliasAttribute>() != null;

            return implementsSavable || hasAttribute;
        }

        /// <summary>Returns the alias from <see cref="SaveDataAliasAttribute"/> if present, else <c>Type.FullName</c>.</summary>
        private static string ResolveAlias(Type type)
        {
            SaveDataAliasAttribute attr = type.GetCustomAttribute<SaveDataAliasAttribute>();
            return attr != null ? attr.Alias : type.FullName;
        }

        #endregion
    }
}
