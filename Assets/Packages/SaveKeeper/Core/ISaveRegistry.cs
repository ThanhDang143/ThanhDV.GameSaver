using System;
using System.Collections.Generic;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Tracks <see cref="ISavable"/> instances. The default <see cref="SaveRegistry"/> covers most cases;
    /// DI users may bind a custom implementation.
    /// </summary>
    public interface ISaveRegistry
    {
        event Action<ISavable> OnSavableRegistered;
        event Action<ISavable> OnSavableUnregistered;

        IReadOnlyList<ISavable> Savables { get; }

        void Register(ISavable savable);
        void Unregister(ISavable savable);
    }
}
