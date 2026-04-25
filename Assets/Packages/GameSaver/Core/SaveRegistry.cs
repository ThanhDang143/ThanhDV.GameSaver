using System;
using System.Collections.Generic;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Manages the registration and tracking of all <see cref="ISavable"/> objects in the game.
    /// </summary>
    public class SaveRegistry
    {
        private readonly HashSet<ISavable> _savables = new();
        public IEnumerable<ISavable> Savables => _savables;

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
            if (savable == null || string.IsNullOrEmpty(savable.SaveKey)) return;
            if (!_savables.Add(savable)) return;

            OnSavableRegistered?.Invoke(savable);
        }

        /// <summary>
        /// Unregisters an <see cref="ISavable"/> object from the registry.
        /// </summary>
        /// <param name="savable">The savable object to unregister.</param>
        public void Unregister(ISavable savable)
        {
            if (savable == null || !_savables.Contains(savable)) return;

            _savables.Remove(savable);
            OnSavableUnregistered?.Invoke(savable);
        }
    }
}
