using System;
using System.Collections.Generic;
using System.Linq;
using ThanhDV.GameSaver.Common;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Manages the registration and tracking of all <see cref="ISavable"/> objects in the game.
    /// </summary>
    public class SaveRegistry
    {
        private readonly HashSet<ISavable> _savables = new();

        /// <summary>
        /// Gets an immediate snapshot of the currently registered <see cref="ISavable"/> instances.
        /// </summary>
        /// <remarks>
        /// Side effect: the getter removes dead references (null or destroyed Unity objects) from the registry
        /// before creating the snapshot. A warning is logged when this happens, which usually means user code
        /// forgot to call <see cref="Unregister"/> in OnDestroy.
        /// Each access creates a new List; the result is not cached. Callers can iterate safely even if
        /// Register or Unregister is called afterward.
        /// </remarks>
        public IReadOnlyList<ISavable> Savables
        {
            get
            {
                int pruned;
                List<ISavable> snapshot;

                lock (_lock)
                {
                    pruned = _savables.RemoveWhere(IsDeadReference);
                    snapshot = _savables.ToList();
                }

                if (pruned > 0)
                {
                    DebugLog.Warning($"SaveRegistry pruned {pruned} dead reference(s). Make sure to call Unregister() in OnDestroy for MonoBehaviour ISavable instances.");
                }

                return snapshot;
            }
        }

        /// <summary>
        /// Protects all access to _savables. Events are fired outside the lock to avoid deadlocks with subscribers.
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// Event triggered when a new <see cref="ISavable"/> object is successfully registered.
        /// </summary>
        public event Action<ISavable> OnSavableRegistered;

        /// <summary>
        /// Event triggered when an <see cref="ISavable"/> object is successfully unregistered.
        /// </summary>
        public event Action<ISavable> OnSavableUnregistered;

        /// <summary>
        /// Registers an <see cref="ISavable"/> object to the registry.
        /// </summary>
        /// <param name="savable">The savable object to register.</param>
        public void Register(ISavable savable)
        {
            if (IsDeadReference(savable)) return;
            if (string.IsNullOrEmpty(savable.SaveKey)) return;

            bool added;
            lock (_lock) added = _savables.Add(savable);

            if (!added) return;

            OnSavableRegistered?.Invoke(savable);
        }

        /// <summary>
        /// Unregisters an <see cref="ISavable"/> object from the registry.
        /// </summary>
        /// <param name="savable">The savable object to unregister.</param>
        public void Unregister(ISavable savable)
        {
            if (savable is null) return;

            bool removed;
            lock (_lock) removed = _savables.Remove(savable);

            if (!removed) return;

            OnSavableUnregistered?.Invoke(savable);
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
