using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.Core;
using ThanhDV.SaveKeeper.Singleton;
using CoreKeeper = ThanhDV.SaveKeeper.Core.SaveKeeper;

namespace ThanhDV.SaveKeeper.Tests.Editor
{
    /// <summary>
    /// Tests for the optional <see cref="SKService"/> singleton facade. SKService holds global static
    /// state, so every test brackets with <see cref="SKService.Shutdown"/> in SetUp/TearDown to stay
    /// isolated. SaveKeeper instances are built with no-op infrastructure doubles — these tests cover
    /// the facade lifecycle (init / access / re-init / shutdown), not save/load behavior.
    /// </summary>
    public class SKServiceTests
    {
        [SetUp]
        public void SetUp() => SKService.Shutdown();

        [TearDown]
        public void TearDown() => SKService.Shutdown();

        #region Before initialization

        [Test]
        public void Exists_BeforeInitialize_IsFalse()
        {
            Assert.IsFalse(SKService.Exists);
        }

        [Test]
        public void Instance_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = SKService.Instance);
        }

        [Test]
        public void Registry_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = SKService.Registry);
        }

        #endregion

        #region Initialize(keeper, registry)

        [Test]
        public void Initialize_WithKeeperAndRegistry_SetsInstanceAndRegistry()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);

            SKService.Initialize(keeper, registry);

            Assert.IsTrue(SKService.Exists);
            Assert.AreSame(keeper, SKService.Instance);
            Assert.AreSame(registry, SKService.Registry);
        }

        [Test]
        public void Initialize_NullKeeper_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => SKService.Initialize(null, new SaveRegistry()));
        }

        [Test]
        public void Initialize_NullRegistry_ThrowsArgumentNullException()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);

            Assert.Throws<ArgumentNullException>(() => SKService.Initialize(keeper, null));
        }

        [Test]
        public void Initialize_CalledTwice_SwapsToNewInstance()
        {
            SaveRegistry firstRegistry = new();
            SKService.Initialize(NewKeeper(firstRegistry), firstRegistry);

            SaveRegistry secondRegistry = new();
            CoreKeeper secondKeeper = NewKeeper(secondRegistry);
            SKService.Initialize(secondKeeper, secondRegistry);

            Assert.AreSame(secondKeeper, SKService.Instance);
            Assert.AreSame(secondRegistry, SKService.Registry);
        }

        [Test]
        public void Initialize_FailedReinit_PreservesPreviousInstance()
        {
            SaveRegistry registry = new();
            CoreKeeper keeper = NewKeeper(registry);
            SKService.Initialize(keeper, registry);

            // A re-init with a null argument must validate BEFORE disposing/swapping, so the
            // working instance survives. The previous bug disposed + reassigned _instance before
            // throwing on the null registry, leaving the service corrupted.
            CoreKeeper throwaway = NewKeeper(new SaveRegistry());
            try
            {
                Assert.Throws<ArgumentNullException>(() => SKService.Initialize(throwaway, null));

                Assert.IsTrue(SKService.Exists);
                Assert.AreSame(keeper, SKService.Instance);     // original keeper not replaced
                Assert.AreSame(registry, SKService.Registry);   // original registry not replaced
            }
            finally
            {
                // throwaway was never adopted by SKService — dispose it so its event/ticker
                // subscriptions don't leak past this test.
                throwaway.Dispose();
            }
        }

        #endregion

        #region Convenience Initialize

        [Test]
        public void Initialize_Convenience_BuildsWorkingInstance()
        {
            // Explicit key (>=16 chars, no weak-key warning) and explicit settings (skips the
            // reflection-based generated-settings lookup) keep this test free of log noise.
            SKService.Initialize("test-master-key-0123456789", new SaveSettings());

            Assert.IsTrue(SKService.Exists);
            Assert.IsNotNull(SKService.Instance);
            Assert.IsNotNull(SKService.Registry);
        }

        #endregion

        #region Shutdown

        [Test]
        public void Shutdown_AfterInitialize_ResetsState()
        {
            SaveRegistry registry = new();
            SKService.Initialize(NewKeeper(registry), registry);
            Assert.IsTrue(SKService.Exists);

            SKService.Shutdown();

            Assert.IsFalse(SKService.Exists);
            Assert.Throws<InvalidOperationException>(() => _ = SKService.Instance);
        }

        [Test]
        public void Shutdown_WhenNotInitialized_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => SKService.Shutdown());
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

        #endregion
    }
}
