using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.AutoSave;
using ThanhDV.SaveKeeper.Core;
using ThanhDV.SaveKeeper.Singleton;
using CoreKeeper = ThanhDV.SaveKeeper.Core.SaveKeeper;

namespace ThanhDV.SaveKeeper.Tests.Editor
{
    /// <summary>
    /// Tests for the optional <see cref="SKSingleton"/> singleton facade. SKSingleton holds global static
    /// state, so every test brackets with <see cref="SKSingleton.Shutdown"/> in SetUp/TearDown to stay
    /// isolated. SaveKeeper instances are built with no-op infrastructure doubles — these tests cover
    /// the facade lifecycle (init / access / re-init / shutdown), not save/load behavior.
    /// </summary>
    public class SKSingletonTests
    {
        [SetUp]
        public void SetUp() => SKSingleton.Shutdown();

        [TearDown]
        public void TearDown() => SKSingleton.Shutdown();

        #region Before initialization

        [Test]
        public void Exists_BeforeInitialize_IsFalse()
        {
            Assert.IsFalse(SKSingleton.Exists);
        }

        [Test]
        public void Instance_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.Instance);
        }

        [Test]
        public void Registry_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.Registry);
        }

        #endregion

        #region InitializeBasic / InitializeFull (manual)

        [Test]
        public void InitializeBasic_WithKeeperAndRegistry_SetsInstanceAndRegistry()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);

            SKSingleton.InitializeBasic(keeper, registry);

            Assert.IsTrue(SKSingleton.Exists);
            Assert.AreSame(keeper, SKSingleton.Instance);
            Assert.AreSame(registry, SKSingleton.Registry);
        }

        [Test]
        public void InitializeBasic_NullKeeper_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SKSingleton.InitializeBasic(null, new SaveRegistry()));
        }

        [Test]
        public void InitializeBasic_NullRegistry_ThrowsArgumentNullException()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);

            Assert.Throws<ArgumentNullException>(() => SKSingleton.InitializeBasic(keeper, null));
        }

        [Test]
        public void InitializeBasic_CalledTwice_SwapsToNewInstance()
        {
            SaveRegistry firstRegistry = new();
            SKSingleton.InitializeBasic(NewKeeper(firstRegistry), firstRegistry);

            SaveRegistry secondRegistry = new();
            CoreKeeper secondKeeper = NewKeeper(secondRegistry);
            SKSingleton.InitializeBasic(secondKeeper, secondRegistry);

            Assert.AreSame(secondKeeper, SKSingleton.Instance);
            Assert.AreSame(secondRegistry, SKSingleton.Registry);
        }

        [Test]
        public void InitializeBasic_FailedReinit_PreservesPreviousInstance()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);
            SKSingleton.InitializeBasic(keeper, registry);

            // A re-init with a null argument must validate BEFORE disposing/swapping, so the
            // working instance survives. The previous bug disposed + reassigned _instance before
            // throwing on the null registry, leaving the service corrupted.
            CoreKeeper throwaway = NewKeeper(new SaveRegistry());
            try
            {
                Assert.Throws<ArgumentNullException>(() => SKSingleton.InitializeBasic(throwaway, null));

                Assert.IsTrue(SKSingleton.Exists);
                Assert.AreSame(keeper, SKSingleton.Instance);     // original keeper not replaced
                Assert.AreSame(registry, SKSingleton.Registry);   // original registry not replaced
            }
            finally
            {
                // throwaway was never adopted by SKSingleton — dispose it so its event/ticker
                // subscriptions don't leak past this test.
                throwaway.Dispose();
            }
        }

        #endregion

        #region Convenience InitializeBasic

        [Test]
        public void InitializeBasic_Convenience_BuildsWorkingInstance()
        {
            // Explicit key (>=16 chars, no weak-key warning) and explicit settings keep this test free of log noise.
            SKSingleton.InitializeBasic("test-master-key-0123456789", new SaveSettings());

            Assert.IsTrue(SKSingleton.Exists);
            Assert.IsNotNull(SKSingleton.Instance);
            Assert.IsNotNull(SKSingleton.Registry);
        }

        #endregion

        #region Shutdown

        [Test]
        public void Shutdown_AfterInitialize_ResetsState()
        {
            SaveRegistry registry = new();
            SKSingleton.InitializeBasic(NewKeeper(registry), registry);
            Assert.IsTrue(SKSingleton.Exists);

            SKSingleton.Shutdown();

            Assert.IsFalse(SKSingleton.Exists);
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.Instance);
        }

        [Test]
        public void Shutdown_WhenNotInitialized_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => SKSingleton.Shutdown());
        }

        #endregion

        #region AutoSave bundling

        [Test]
        public void AutoSave_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.AutoSave);
        }

        [Test]
        public void AutoSave_AfterInitializeBasic_Throws()
        {
            // InitializeBasic (manual) does NOT bundle auto-save — accessing it must throw with a
            // dedicated message, distinct from the "not initialized" exception.
            SaveRegistry registry = new();
            SKSingleton.InitializeBasic(NewKeeper(registry), registry);

            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.AutoSave);
        }

        [Test]
        public void InitializeFull_WithAutoSaveSettings_BundlesAndStartsAutoSave()
        {
            // InitializeFull (auto-config) bundles + starts the auto-save service.
            SKSingleton.InitializeFull("test-master-key-0123456789", new SaveSettings(), new AutoSaveSettings { AutoSaveTime = 60 });

            Assert.IsNotNull(SKSingleton.AutoSave);
            Assert.IsTrue(SKSingleton.AutoSave.IsRunning, "Auto-config overload with AutoSaveSettings must start the service.");
        }

        [Test]
        public void InitializeFull_WithNullAutoSaveSettings_FallsBackToDefaults_AndBundles()
        {
            // Passing null AutoSaveSettings means "I want auto-save with default settings" — must still bundle and start.
            SKSingleton.InitializeFull("test-master-key-0123456789", new SaveSettings(), null);

            Assert.IsNotNull(SKSingleton.AutoSave);
            Assert.IsTrue(SKSingleton.AutoSave.IsRunning, "Null AutoSaveSettings must fall back to defaults, not skip bundling.");
        }

        [Test]
        public void InitializeFull_ManualWithAutoSave_AssignsButDoesNotStart()
        {
            // InitializeFull (manual) trusts caller's wiring: assigns the supplied auto-save but does NOT call Start().
            SaveRegistry registry = new();
            StubAutoSave stub = new();

            SKSingleton.InitializeFull(NewKeeper(registry), registry, stub);

            Assert.AreSame(stub, SKSingleton.AutoSave);
            Assert.IsFalse(stub.IsRunning, "InitializeFull (manual) must NOT auto-start — caller controls lifecycle.");
            Assert.AreEqual(0, stub.StartCount);
        }

        [Test]
        public void InitializeFull_ManualWithAutoSave_NullAutoSave_ThrowsArgumentNullException()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);

            Assert.Throws<ArgumentNullException>(() => SKSingleton.InitializeFull(keeper, registry, null));
        }

        [Test]
        public void Shutdown_DisposesBundledAutoSave_BeforeKeeper()
        {
            SaveRegistry registry = new();
            StubAutoSave stub = new();
            SKSingleton.InitializeFull(NewKeeper(registry), registry, stub);

            SKSingleton.Shutdown();

            Assert.AreEqual(1, stub.DisposeCount, "Shutdown must dispose the bundled auto-save.");
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.AutoSave, "After Shutdown, AutoSave getter must throw again.");
        }

        [Test]
        public void ReInitialize_DisposesPreviousBundledAutoSave()
        {
            // Re-initializing (here: switching from manual-with-autosave to manual-without-autosave) must
            // dispose the previous auto-save so it stops ticking and unhooks Application events.
            SaveRegistry registry1 = new();
            StubAutoSave stub1 = new();
            SKSingleton.InitializeFull(NewKeeper(registry1), registry1, stub1);

            SaveRegistry registry2 = new();
            SKSingleton.InitializeBasic(NewKeeper(registry2), registry2);

            Assert.AreEqual(1, stub1.DisposeCount, "Re-Initialize must dispose previously bundled auto-save.");
            Assert.Throws<InvalidOperationException>(() => _ = SKSingleton.AutoSave, "After re-init without auto-save, AutoSave getter must throw.");
        }

        #endregion

        #region Helpers & no-op doubles

        private static CoreKeeper NewKeeper(SaveRegistry registry)
            => new(registry, new NoopStorage(), new NoopSerializer(), new NoopEncryption(), new SaveSettings());

        private sealed class NoopStorage : IStorageProvider
        {
            public Task WriteAsync(string profileId, string fileName, string data) => Task.CompletedTask;
            public void WriteImmediate(string profileId, string fileName, string data) { }
            public void RestoreBackup(string profileId, string fileName) { }
            public Task<string> ReadAsync(string profileId, string fileName) => Task.FromResult<string>(null);
            public Task<string> ReadBackupAsync(string profileId, string fileName) => Task.FromResult<string>(null);
            public void DeleteProfile(string profileId) { }
            public bool Exists(string profileId, string fileName) => false;
            public DateTime? GetLastWriteTimeUtc(string profileId, string fileName) => null;
            public IEnumerable<string> GetAllProfileIds() => Array.Empty<string>();
            public string GetMostRecentProfileId(string fileName) => null;
        }

        private sealed class NoopSerializer : ISerializer
        {
            public string Serialize<T>(T obj) => string.Empty;
            public T Deserialize<T>(string data) => default;
        }

        private sealed class NoopEncryption : IEncryptionProvider
        {
            public string Encrypt(string plainText) => plainText;
            public string Decrypt(string cipherText) => cipherText;
        }

        /// <summary>
        /// Minimal <see cref="ISKAutoSave"/> stub: records call counts and tracks running state.
        /// Used by AutoSave-bundling tests so we don't pull in the real PlayerLoop ticker.
        /// </summary>
        private sealed class StubAutoSave : ISKAutoSave
        {
            public bool IsRunning { get; private set; }
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }
            public int DisposeCount { get; private set; }

            public void Start() { IsRunning = true; StartCount++; }
            public void Stop() { IsRunning = false; StopCount++; }
            public void Dispose() { Stop(); DisposeCount++; }
        }

        #endregion
    }
}
