using System;
using System.Collections.Generic;

namespace ThanhDV.GameSaver.Core
{
    public static class SaveRegistry
    {
        private static HashSet<ISavable> savablesPending = new();
        private static Action<ISavable> register;
        private static Action<ISavable> unregister;

        public static void Bind(Action<ISavable> _register, Action<ISavable> _unregister)
        {
            register = _register;
            unregister = _unregister;

            FlushPending();
        }

        private static void FlushPending()
        {
            if (savablesPending.Count == 0) return;

            List<ISavable> pending = new(savablesPending);
            savablesPending.Clear();

            foreach (ISavable savable in pending)
            {
                register?.Invoke(savable);
            }
        }

        public static void Register(ISavable savable)
        {
            if (register != null)
            {
                register(savable);
            }
            else
            {
                savablesPending.Add(savable);
            }
        }

        public static void Unregister(ISavable savable)
        {
            if (unregister != null)
            {
                unregister(savable);
            }
            else
            {
                savablesPending.Remove(savable);
            }
        }
    }
}
