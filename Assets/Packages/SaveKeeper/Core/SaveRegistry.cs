using System;
using System.Collections.Generic;
using System.Linq;
using ThanhDV.SaveKeeper.Common;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Manages the registration and tracking of all <see cref="ISavable"/> objects in the game.
    /// </summary>
    public class SaveRegistry
    {
        /// <summary>
        /// Primary storage keyed by <see cref="ISavable.SaveKey"/>.
        /// </summary>
        private readonly Dictionary<string, ISavable> _savables = new();

        /// <summary>
        /// Protects all access to _savables.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// Gets a fresh snapshot of registered <see cref="ISavable"/> instances.
        /// </summary>
        /// <remarks>
        /// Dead references are pruned before the snapshot is created. The returned list is independent
        /// of later registry changes, so callers can iterate it safely.
        /// </remarks>
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
                    DebugLog.Warning($"SaveRegistry pruned {prunedCount} dead reference(s). Make sure to call Unregister() for any ISavable instances.");
                }

                return snapshot;
            }
        }

        /// <summary>
        /// Event triggered when a new <see cref="ISavable"/> object is successfully registered.
        /// </summary>
        public event Action<ISavable> OnSavableRegistered;

        /// <summary>
        /// Event triggered when an <see cref="ISavable"/> object is successfully unregistered.
        /// </summary>
        public event Action<ISavable> OnSavableUnregistered;

        /// <summary>
        /// Registers a valid <see cref="ISavable"/> by its <see cref="ISavable.SaveKey"/>.
        /// </summary>
        /// <param name="savable">The savable instance to register.</param>
        /// <exception cref="DuplicateSaveKeyException">
        /// Thrown when a different live savable already uses the same key.
        /// </exception>
        /// <remarks>
        /// Re-registering the same instance is ignored. Null, destroyed, or empty-key savables are ignored.
        /// Destroyed entries with the same key are replaced and logged.
        /// </remarks>
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
                DebugLog.Warning($"SaveRegistry auto-replaced dead reference for SaveKey '{key}'. Make sure to call Unregister() for any ISavable instances.");
            }

            if (shouldFireEvent) OnSavableRegistered?.Invoke(savable);
        }

        /// <summary>
        /// Removes a registered <see cref="ISavable"/>.
        /// </summary>
        /// <param name="savable">The savable instance to remove.</param>
        /// <remarks>
        /// Uses the current <see cref="ISavable.SaveKey"/> first. If the key changed after registration,
        /// falls back to reference lookup, removes the entry, and logs a warning.
        /// </remarks>
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
                DebugLog.Warning($"SaveKey was changed since Register for '{savable.GetType().FullName}'. SaveKey must be immutable after registration.");
            }

            if (removed) OnSavableUnregistered?.Invoke(savable);
        }

        /// <summary>
        /// Returns true if the reference is null or a destroyed Unity object.
        /// Used to detect dangling registry references when Unregister is not called in OnDestroy.
        /// </summary>
        private static bool IsDeadReference(ISavable savable)
        {
            if (savable is null) return true;
            if (savable is UnityEngine.Object unityObj) return unityObj == null;

            return false;
        }
    }
}
