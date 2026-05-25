using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Static PlayerLoop-driven dispatcher that ticks every active SaveKeeper instance each Update frame.
    /// Uses no MonoBehaviour or GameObject — keeps the library out of the scene tree.
    /// </summary>
    internal static class AutoSaveTicker
    {
        /// <summary>
        /// Marker type used to identify our subsystem inside PlayerLoop's tree.
        /// </summary>
        private struct AutoSaveTickerMarker { }

        private static readonly List<SaveKeeper> _subscribers = new();
        private static readonly object _lock = new();
        private static bool _installed;

        /// <summary>
        /// Registers a SaveKeeper for per-frame ticks. Installs the PlayerLoop hook on first subscriber.
        /// </summary>
        public static void Subscribe(SaveKeeper SaveKeeper)
        {
            lock (_lock)
            {
                if (_subscribers.Contains(SaveKeeper)) return;
                _subscribers.Add(SaveKeeper);

                if (!_installed)
                {
                    InstallPlayerLoop();
                    _installed = true;
                }
            }
        }

        /// <summary>
        /// Unregisters a SaveKeeper. Removes the PlayerLoop hook when the last subscriber leaves.
        /// </summary>
        public static void Unsubscribe(SaveKeeper SaveKeeper)
        {
            lock (_lock)
            {
                _subscribers.Remove(SaveKeeper);

                if (_subscribers.Count <= 0 && _installed)
                {
                    UninstallPlayerLoop();
                    _installed = false;
                }
            }
        }

        /// <summary>
        /// Invoked by Unity's PlayerLoop every Update frame. Snapshots subscribers outside the lock so
        /// AutoSaveTick callbacks may freely subscribe / unsubscribe without breaking the iteration.
        /// </summary>
        private static void OnTick()
        {
            SaveKeeper[] snapshot;
            lock (_lock) snapshot = _subscribers.ToArray();

            float deltaTime = Time.unscaledDeltaTime;
            foreach (SaveKeeper saver in snapshot)
            {
                saver.AutoSaveTick(deltaTime);
            }
        }

        /// <summary>
        /// Injects our tick callback as a subsystem of Unity's Update phase. Idempotent.
        /// </summary>
        private static void InstallPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            PlayerLoopSystem tickSystem = new()
            {
                type = typeof(AutoSaveTickerMarker),
                updateDelegate = OnTick
            };

            for (int i = 0; i < rootLoop.subSystemList.Length; i++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[i];
                if (subSystem.type != typeof(Update)) continue;


                List<PlayerLoopSystem> subs = subSystem.subSystemList?.ToList() ?? new();
                if (subs.Any(s => s.type == typeof(AutoSaveTickerMarker))) return;

                subs.Add(tickSystem);
                subSystem.subSystemList = subs.ToArray();
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }

        /// <summary>
        /// Removes our tick callback from Unity's Update phase.
        /// </summary>
        private static void UninstallPlayerLoop()
        {
            PlayerLoopSystem rootLoop = PlayerLoop.GetCurrentPlayerLoop();

            for (int i = 0; i < rootLoop.subSystemList.Length; i++)
            {
                PlayerLoopSystem subSystem = rootLoop.subSystemList[i];
                if (subSystem.type != typeof(Update)) continue;

                PlayerLoopSystem[] subs = subSystem.subSystemList?.Where(s => s.type != typeof(AutoSaveTickerMarker)).ToArray();
                subSystem.subSystemList = subs;
                break;
            }

            PlayerLoop.SetPlayerLoop(rootLoop);
        }
    }
}
