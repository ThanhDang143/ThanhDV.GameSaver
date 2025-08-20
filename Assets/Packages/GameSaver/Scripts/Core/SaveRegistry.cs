using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            foreach (ISavable savable in savablesPending)
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
