using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.AutoSave;
using ThanhDV.SaveKeeper.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanhDV.SaveKeeper.Tests.Editor
{
    /// <summary>
    /// Tests for the optional <see cref="SKAutoSave"/> service. It only consumes the public
    /// <see cref="ISaveKeeper"/> API, so a lightweight stub keeper records calls. The internal
    /// <c>Tick</c> and private quit/save-completed handlers are exercised via reflection (consistent
    /// with the rest of the suite) to keep the periodic logic deterministic without the real PlayerLoop.
    /// </summary>
    public class SKAutoSaveTests
    {
        private SKAutoSave _service;

        [TearDown]
        public void TearDown()
        {
            // Unsubscribe from the static ticker / Application events for any service that called Start().
            _service?.Dispose();
            _service = null;
        }

        #region Construction

        [Test]
        public void Ctor_NullSaveKeeper_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SKAutoSave(null, new AutoSaveSettings()));
        }

        [Test]
        public void Ctor_NullSettings_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new SKAutoSave(new StubSaveKeeper(), null));
        }

        #endregion

        #region Periodic tick

        [Test]
        public void Tick_CountdownElapsed_TriggersSaveAsync()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 10f });
            SetCountdown(_service, 0.1f);

            InvokeTick(_service, 1f);

            Assert.AreEqual(1, keeper.SaveAsyncCount);
        }

        [Test]
        public void Tick_BeforeCountdownElapsed_DoesNotSave()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 10f });
            SetCountdown(_service, 10f);

            InvokeTick(_service, 1f);

            Assert.AreEqual(0, keeper.SaveAsyncCount);
            Assert.That(GetCountdown(_service), Is.EqualTo(9f).Within(0.001f));
        }

        [Test]
        public void Tick_Triggered_ResetsCountdownToAutoSaveTime()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 42f });
            SetCountdown(_service, 0.1f);

            InvokeTick(_service, 1f);

            Assert.That(GetCountdown(_service), Is.EqualTo(42f).Within(0.001f));
        }

        [Test]
        public void Tick_AutoSaveTimeZero_Disabled_DoesNotSave()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 0f });

            InvokeTick(_service, 100f);

            Assert.AreEqual(0, keeper.SaveAsyncCount);
        }

        [Test]
        public void Tick_NoCurrentProfile_DoesNotSave()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = null };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 10f });
            SetCountdown(_service, 0.1f);

            InvokeTick(_service, 100f);

            Assert.AreEqual(0, keeper.SaveAsyncCount);
        }

        #endregion

        #region Countdown reset on save completed

        [Test]
        public void OnSaveCompleted_SameProfile_ResetsCountdown()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 30f });
            _service.Start(); // subscribes to keeper.OnSaveCompleted
            SetCountdown(_service, 0.1f);

            keeper.RaiseSaveCompleted("p");

            Assert.That(GetCountdown(_service), Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void OnSaveCompleted_DifferentProfile_DoesNotResetCountdown()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveTime = 30f });
            _service.Start();
            SetCountdown(_service, 0.1f);

            keeper.RaiseSaveCompleted("other");

            Assert.That(GetCountdown(_service), Is.EqualTo(0.1f).Within(0.001f),
                "Countdown must not reset when a different profile was saved.");
        }

        #endregion

        #region Quit / focus-loss flush

        [Test]
        public void OnApplicationQuitting_FlushEnabled_DrainsAndSavesImmediate()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveOnQuit = true });

            InvokeQuit(_service);

            Assert.AreEqual(1, keeper.WaitForPendingCount, "Should drain pending async saves first.");
            Assert.AreEqual(1, keeper.SaveImmediateCount, "Should force a final synchronous save.");
        }

        [Test]
        public void OnApplicationQuitting_FlushDisabled_DoesNothing()
        {
            StubSaveKeeper keeper = new() { CurrentProfileId = "p" };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveOnQuit = false });

            InvokeQuit(_service);

            Assert.AreEqual(0, keeper.WaitForPendingCount);
            Assert.AreEqual(0, keeper.SaveImmediateCount);
        }

        [Test]
        public void OnApplicationQuitting_SaveImmediateThrows_IsSwallowed()
        {
            StubSaveKeeper keeper = new()
            {
                CurrentProfileId = "p",
                SaveImmediateException = new InvalidOperationException("no profile"),
            };
            _service = new SKAutoSave(keeper, new AutoSaveSettings { AutoSaveOnQuit = true });

            // FlushOnExit catches save errors internally — the quit handler must not propagate them.
            // SaveImmediate throws InvalidOperationException, which FlushOnExit swallows and reports as a skip.
            LogAssert.Expect(LogType.Warning, new Regex(".*has been skipped.*"));
            Assert.DoesNotThrow(() => InvokeQuit(_service));
            Assert.AreEqual(1, keeper.SaveImmediateCount);
        }

        #endregion

        #region Lifecycle

        [Test]
        public void Start_SetsIsRunning_Stop_Clears()
        {
            _service = new SKAutoSave(new StubSaveKeeper(), new AutoSaveSettings());

            _service.Start();
            Assert.IsTrue(_service.IsRunning);

            _service.Stop();
            Assert.IsFalse(_service.IsRunning);
        }

        [Test]
        public void Start_CalledTwice_IsIdempotent()
        {
            _service = new SKAutoSave(new StubSaveKeeper(), new AutoSaveSettings());

            _service.Start();
            Assert.DoesNotThrow(() => _service.Start());
            Assert.IsTrue(_service.IsRunning);
        }

        [Test]
        public void Stop_WhenNotRunning_DoesNotThrow()
        {
            _service = new SKAutoSave(new StubSaveKeeper(), new AutoSaveSettings());

            Assert.DoesNotThrow(() => _service.Stop());
            Assert.IsFalse(_service.IsRunning);
        }

        [Test]
        public void Dispose_StopsRunningService()
        {
            _service = new SKAutoSave(new StubSaveKeeper(), new AutoSaveSettings());
            _service.Start();

            _service.Dispose();

            Assert.IsFalse(_service.IsRunning);
        }

        #endregion

        #region Reflection helpers

        private static void InvokeTick(SKAutoSave svc, float deltaTime) =>
            typeof(SKAutoSave).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(svc, new object[] { deltaTime });

        private static void InvokeQuit(SKAutoSave svc) =>
            typeof(SKAutoSave).GetMethod("OnApplicationQuitting", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(svc, null);

        private static void SetCountdown(SKAutoSave svc, float value) =>
            typeof(SKAutoSave).GetField("_countdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(svc, value);

        private static float GetCountdown(SKAutoSave svc) =>
            (float)typeof(SKAutoSave).GetField("_countdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(svc);

        #endregion

        #region Stub keeper

        /// <summary>Minimal <see cref="ISaveKeeper"/> stub: records the calls SKAutoSave makes; everything else is inert.</summary>
        private sealed class StubSaveKeeper : ISaveKeeper
        {
            public string CurrentProfileId { get; set; }
            public int SaveAsyncCount { get; private set; }
            public int SaveImmediateCount { get; private set; }
            public int WaitForPendingCount { get; private set; }
            public Exception SaveImmediateException { get; set; }

            public event Action<string> OnSaveCompleted;
            public void RaiseSaveCompleted(string profileId) => OnSaveCompleted?.Invoke(profileId);

            public bool IsSimpleDataDirty => false;

            public SaveKeeperOperationHandle SaveAsync(string profileId = null, ISaveMeta metadata = null)
            {
                SaveAsyncCount++;
                return default;
            }

            public void SaveImmediate(string profileId = null, ISaveMeta metadata = null)
            {
                SaveImmediateCount++;
                if (SaveImmediateException != null) throw SaveImmediateException;
            }

            public Task WaitForPendingOperationsAsync()
            {
                WaitForPendingCount++;
                return Task.CompletedTask;
            }

            // ----- Unused by SKAutoSave — inert implementations. -----
            public SaveKeeperOperationHandle<List<T>> GetAllMetadataAsync<T>() where T : class, ISaveMeta => default;
            public string GetMostRecentProfileId() => null;
            public IEnumerable<string> GetAllProfiles() => Array.Empty<string>();
            public bool ProfileExists(string profileId) => false;
            public void CreateProfile(string profileId, ISaveMeta metadata = null, bool overwrite = false) { }
            public void DeleteProfile(string profileId) { }
            public SaveKeeperOperationHandle LoadAsync(string profileId, bool discardUnsavedChanges = false) => default;
            public SaveKeeperOperationHandle LoadBackupAsync(string profileId, bool discardUnsavedChanges = false) => default;
            public SaveKeeperOperationHandle LoadMostRecentAsync(bool discardUnsavedChanges = false) => default;
            public void RestoreBackup(string profileId) { }
            public void SetSimple<T>(string key, T value) { }
            public T GetSimple<T>(string key, T defaultValue = default) => defaultValue;
            public bool HasSimpleKey(string key) => false;
            public void DeleteSimple(string key) { }
            public void Dispose() { }
        }

        #endregion
    }
}
