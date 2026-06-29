using System;
using System.Collections.Generic;
using System.Linq;
using ThanhDV.SaveKeeper.Common;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>Default <see cref="ISaveRegistry"/>: tracks live <see cref="ISavable"/> instances by SaveKey.</summary>
    public class SaveRegistry : ISaveRegistry
    {
        /// <summary>Primary storage keyed by <see cref="ISavable.SaveKey"/>.</summary>
        private readonly Dictionary<string, ISavable> _savables = new();

        /// <summary>Protects all access to <see cref="_savables"/>.</summary>
        private readonly object _lock = new();

        /// <summary>
        /// Returns a fresh snapshot of registered savables, pruning dead references first.
        /// Safe to iterate independently of later registry changes.
        /// </summary>
        public IReadOnlyList<ISavable> Savables
        {
            get
            {
                int prunedCount;
                List<ISavable> snapshot;

                lock (_lock)
                {
                    List<string> deadKeys = null;
                    foreach (KeyValuePair<string, ISavable> kvp in _savables)
                    {
                        if (IsDeadReference(kvp.Value))
                        {
                            deadKeys ??= new();
                            deadKeys.Add(kvp.Key);
                        }
                    }

                    if (deadKeys != null)
                    {
                        for (int i = 0; i < deadKeys.Count; i++) _savables.Remove(deadKeys[i]);
                        prunedCount = deadKeys.Count;
                    }
                    else
                    {
                        prunedCount = 0;
                    }

                    snapshot = _savables.Values.ToList();
                }

                if (prunedCount > 0)
                {
                    SKLogger.Warning($"SaveRegistry pruned {prunedCount} dead reference(s). Make sure to call Unregister() for any ISavable instances.");
                }

                return snapshot;
            }
        }

        /// <summary>Raised after a savable is successfully registered.</summary>
        public event Action<ISavable> OnSavableRegistered;

        /// <summary>Raised after a savable is successfully unregistered.</summary>
        public event Action<ISavable> OnSavableUnregistered;

        /// <summary>
        /// Registers a savable by its <see cref="ISavable.SaveKey"/>.
        /// Null/destroyed/empty-key savables and re-registration of the same instance are silently ignored;
        /// dead entries sharing the key are auto-replaced with a warning.
        /// </summary>
        /// <param name="savable">The savable instance to register.</param>
        /// <exception cref="DuplicateSaveKeyException">A different live savable already uses the same key.</exception>
        public void Register(ISavable savable)
        {
            if (IsDeadReference(savable)) return;

            string key = savable.SaveKey;
            if (string.IsNullOrEmpty(key)) return;

            bool autoReplaced = false;
            bool shouldFireEvent = false;

            lock (_lock)
            {
                if (_savables.TryGetValue(key, out ISavable existing))
                {
                    if (ReferenceEquals(existing, savable)) return;

                    if (IsDeadReference(existing))
                    {
                        _savables[key] = savable;
                        autoReplaced = true;
                        shouldFireEvent = true;
                    }
                    else
                    {
                        throw new DuplicateSaveKeyException(key, existing.GetType(), savable.GetType());
                    }
                }
                else
                {
                    _savables[key] = savable;
                    shouldFireEvent = true;
                }
            }

            if (autoReplaced)
            {
                SKLogger.Warning($"SaveRegistry auto-replaced dead reference for SaveKey '{key}'. Make sure to call Unregister() for any ISavable instances.");
            }

            if (shouldFireEvent) OnSavableRegistered?.Invoke(savable);
        }

        /// <summary>
        /// Removes a registered savable. Looks up by current <see cref="ISavable.SaveKey"/>;
        /// falls back to reference lookup if the key changed (with a warning — SaveKey must be immutable).
        /// </summary>
        public void Unregister(ISavable savable)
        {
            if (savable is null) return;

            bool removed = false;
            bool keyChanged = false;

            lock (_lock)
            {
                string curKey = savable.SaveKey;

                // SaveKey unchanged since Register.
                if (!string.IsNullOrEmpty(curKey) && _savables.TryGetValue(curKey, out ISavable existing) && ReferenceEquals(existing, savable))
                {
                    _savables.Remove(curKey);
                    removed = true;
                }
                // SaveKey was changed since Register, or savable was never registered.
                else
                {
                    string foundKey = null;
                    foreach (KeyValuePair<string, ISavable> kvp in _savables)
                    {
                        if (ReferenceEquals(kvp.Value, savable))
                        {
                            foundKey = kvp.Key;
                            break;
                        }
                    }

                    if (foundKey != null)
                    {
                        _savables.Remove(foundKey);
                        removed = true;
                        keyChanged = !string.IsNullOrEmpty(curKey) && curKey != foundKey;
                    }
                }
            }

            if (keyChanged)
            {
                SKLogger.Warning($"SaveKey was changed since Register for '{savable.GetType().FullName}'. SaveKey must be immutable after registration.");
            }

            if (removed) OnSavableUnregistered?.Invoke(savable);
        }

        /// <summary>
        /// True if the reference is null or a destroyed Unity object — catches dangling entries when
        /// the user forgets to Unregister in OnDestroy.
        /// </summary>
        private static bool IsDeadReference(ISavable savable)
        {
            if (savable is null) return true;
            if (savable is UnityEngine.Object unityObj) return unityObj == null;

            return false;
        }
    }
}
