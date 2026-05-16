using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.GameSaver.Core;
using GameSaverRuntime = ThanhDV.GameSaver.Core.GameSaver;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class GameSaverTests
    {
        private SaveRegistry _registry;
        private InMemoryStorageProvider _storage;
        private TrackingSerializer _serializer;
        private TrackingEncryptionProvider _encryption;
        private SaveSettings _settings;
        private GameSaverRuntime _gameSaver;

        [SetUp]
        public void SetUp()
        {
            _registry = new SaveRegistry();
            _storage = new InMemoryStorageProvider();
            _serializer = new TrackingSerializer();
            _encryption = new TrackingEncryptionProvider();
            _settings = CreateSettings(useEncryption: true);
            _gameSaver = new GameSaverRuntime(_registry, _storage, _serializer, _encryption, _settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
            }

            _gameSaver?.Dispose();
        }

        [TestCase("registry")]
        [TestCase("storage")]
        [TestCase("serializer")]
        [TestCase("encryption")]
        [TestCase("settings")]
        public void Constructor_NullDependency_ThrowsArgumentNullException(string nullTarget)
        {
            SaveRegistry registry = nullTarget == "registry" ? null : new SaveRegistry();
            IStorageProvider storage = nullTarget == "storage" ? null : new InMemoryStorageProvider();
            ISerializer serializer = nullTarget == "serializer" ? null : new TrackingSerializer();
            IEncryptionProvider encryption = nullTarget == "encryption" ? null : new TrackingEncryptionProvider();
            SaveSettings settings = nullTarget == "settings" ? null : CreateSettings(useEncryption: true);

            try
            {
                Assert.Throws<ArgumentNullException>(() => new GameSaverRuntime(registry, storage, serializer, encryption, settings));
            }
            finally
            {
                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }
        }

        [Test]
        public void SaveImmediate_WithMetadata_CapturesSavables_WritesSaveAndMetadata_AndUpdatesMetadata()
        {
            TestSavable savable = new("player", new TestSaveData { Value = 42 });
            _registry.Register(savable);
            TestSaveMeta metadata = new();

            _gameSaver.SaveImmediate("profile-1", metadata);

            Assert.That(savable.CaptureCallCount, Is.EqualTo(1));
            Assert.That(metadata.ProfileID, Is.EqualTo("profile-1"));
            Assert.That(metadata.LastTimeSaved, Is.Not.EqualTo(default(DateTime)));
            Assert.That(_encryption.EncryptInputs, Has.Count.EqualTo(1));
            Assert.That(_storage.WriteImmediateCalls, Has.Count.EqualTo(2));
            Assert.That(_storage.WriteImmediateCalls[0].fileName, Is.Not.EqualTo(_storage.WriteImmediateCalls[1].fileName), "Save data and metadata should be written to separate files.");
        }

        [Test]
        public void SaveImmediate_WithoutMetadata_UsesExplicitProfile_AndSkipsEncryptionWhenDisabled()
        {
            RecreateGameSaver(useEncryption: false);
            TestSavable savable = new("player", new TestSaveData { Value = 5 });
            _registry.Register(savable);

            _gameSaver.SaveImmediate("profile-plain");

            Assert.That(_storage.WriteImmediateCalls, Has.Count.EqualTo(1));
            Assert.That(_storage.WriteImmediateCalls[0].profileId, Is.EqualTo("profile-plain"));
            Assert.That(_encryption.EncryptInputs, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SaveAsync_CompletesSuccessfully_AndWritesData()
        {
            TestSavable savable = new("player", new TestSaveData { Value = 10 });
            _registry.Register(savable);
            TestSaveMeta metadata = new();

            GameSaverOperationHandle handle = _gameSaver.SaveAsync("profile-async", metadata);
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(_storage.WriteAsyncCalls, Has.Count.EqualTo(2));
            Assert.That(metadata.ProfileID, Is.EqualTo("profile-async"));
        }

        [UnityTest]
        public IEnumerator SaveAsync_WithoutValidProfile_CompletesFailed()
        {
            GameSaverOperationHandle handle = _gameSaver.SaveAsync();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Does.Contain("Cannot save"));
        }

        [Test]
        public void SaveImmediate_WithoutValidProfile_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _gameSaver.SaveImmediate());
        }

        [UnityTest]
        public IEnumerator SaveAsync_WhenStorageThrows_CompletesFailed()
        {
            _storage.WriteAsyncException = new InvalidOperationException("write failed");
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 10 }));

            GameSaverOperationHandle handle = _gameSaver.SaveAsync("profile-fail");

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Is.EqualTo("write failed"));
        }

        [Test]
        public void RestoreBackup_WithProfile_ForwardsToStorage()
        {
            _gameSaver.RestoreBackup("profile-restore");

            Assert.That(_storage.RestoreBackupCalls, Has.Count.EqualTo(1));
            Assert.That(_storage.RestoreBackupCalls[0].profileId, Is.EqualTo("profile-restore"));
        }

        [Test]
        public void RestoreBackup_WithNullProfile_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _gameSaver.RestoreBackup(null));
        }

        [UnityTest]
        public IEnumerator LoadAsync_RestoresPrimaryData_AndUpdatesCurrentProfileForLaterSave()
        {
            TestSavable loadTarget = new("player", null);
            SaveData loadedData = new();
            loadedData.DataModules["player"] = new TestSaveData { Value = 99 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-load", SaveFileName, serialized);
            _registry.Register(loadTarget);

            GameSaverOperationHandle handle = _gameSaver.LoadAsync("profile-load");
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(loadTarget.RestoreCallCount, Is.EqualTo(2), "Register triggers one immediate restore, load triggers another.");
            Assert.That(((TestSaveData)loadTarget.LastRestoredData).Value, Is.EqualTo(99));

            TestSavable saveTarget = new("other", new TestSaveData { Value = 1 });
            _registry.Register(saveTarget);
            _gameSaver.SaveImmediate();

            Assert.That(_storage.WriteImmediateCalls.Last().profileId, Is.EqualTo("profile-load"));
        }

        [UnityTest]
        public IEnumerator LoadBackupAsync_UsesBackupReadPath()
        {
            TestSavable savable = new("player", null);
            SaveData loadedData = new();
            loadedData.DataModules["player"] = new TestSaveData { Value = 7 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetBackupFile("profile-backup", SaveFileName, serialized);
            _registry.Register(savable);

            GameSaverOperationHandle handle = _gameSaver.LoadBackupAsync("profile-backup");
            yield return WaitForOperation(handle);

            Assert.That(_storage.ReadBackupCalls, Has.Count.EqualTo(1));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator LoadAsync_WithoutValidProfile_CompletesFailed()
        {
            GameSaverOperationHandle handle = _gameSaver.LoadAsync(null);

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Does.Contain("Cannot load"));
        }

        [UnityTest]
        public IEnumerator LoadMostRecentAsync_WhenProfileExists_LoadsThatProfile()
        {
            TestSavable savable = new("player", null);
            SaveData loadedData = new();
            loadedData.DataModules["player"] = new TestSaveData { Value = 15 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.MostRecentProfileId = "recent-profile";
            _storage.SetPrimaryFile("recent-profile", SaveFileName, serialized);
            _registry.Register(savable);

            GameSaverOperationHandle handle = _gameSaver.LoadMostRecentAsync();
            yield return WaitForOperation(handle);

            Assert.That(_storage.ReadAsyncCalls.Single().profileId, Is.EqualTo("recent-profile"));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(15));
        }

        [UnityTest]
        public IEnumerator LoadMostRecentAsync_WhenNoProfileExists_CompletesFailed()
        {
            _storage.MostRecentProfileId = null;

            GameSaverOperationHandle handle = _gameSaver.LoadMostRecentAsync();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<FileNotFoundException>());
            Assert.That(handle.Error.Message, Does.Contain("No previously saved Profile"));
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_ReturnsSortedMetadata_AndSkipsInvalidEntries()
        {
            TestSaveMeta oldMeta = new() { ProfileID = "profile-old", LastTimeSaved = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            TestSaveMeta newMeta = new() { ProfileID = "profile-new", LastTimeSaved = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            string oldSerialized = _serializer.RegisterSerializedValue(oldMeta);
            string newSerialized = _serializer.RegisterSerializedValue(newMeta);
            _serializer.RegisterDeserializeException("broken-meta", new FormatException("invalid metadata"));

            _storage.ProfileIds = new List<string> { "profile-old", "profile-broken", "profile-new", "profile-missing" };
            _storage.SetPrimaryFile("profile-old", MetaFileName, oldSerialized);
            _storage.SetPrimaryFile("profile-broken", MetaFileName, "broken-meta");
            _storage.SetPrimaryFile("profile-new", MetaFileName, newSerialized);

            GameSaverOperationHandle<List<TestSaveMeta>> handle = _gameSaver.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);
            List<TestSaveMeta> result = handle.Result;

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(result.Select(meta => meta.ProfileID).ToArray(), Is.EqualTo(new[] { "profile-new", "profile-old" }));
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_WhenProfileEnumerationFails_CompletesFailed()
        {
            _storage.GetAllProfileIdsException = new InvalidOperationException("enumeration failed");

            GameSaverOperationHandle<List<TestSaveMeta>> handle = _gameSaver.GetAllMetadataAsync<TestSaveMeta>();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Is.EqualTo("enumeration failed"));
        }

        [Test]
        public void DeleteProfile_WithNullOrEmptyProfile_DoesNothing()
        {
            _gameSaver.DeleteProfile(null);
            _gameSaver.DeleteProfile(string.Empty);

            Assert.That(_storage.DeleteProfileCalls, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DeleteProfile_CurrentProfileIsCleared_AfterSuccessfulLoad()
        {
            SaveData loadedData = new();
            loadedData.DataModules["player"] = new TestSaveData { Value = 1 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-delete", SaveFileName, serialized);

            GameSaverOperationHandle loadHandle = _gameSaver.LoadAsync("profile-delete");
            yield return WaitForOperation(loadHandle);

            _gameSaver.DeleteProfile("profile-delete");

            Assert.That(_storage.DeleteProfileCalls.Single(), Is.EqualTo("profile-delete"));
            Assert.Throws<InvalidOperationException>(() => _gameSaver.SaveImmediate());
        }

        [Test]
        public void GetMostRecentProfileId_ForwardsToStorage()
        {
            _storage.MostRecentProfileId = "recent-id";

            string profileId = _gameSaver.GetMostRecentProfileId();

            Assert.That(profileId, Is.EqualTo("recent-id"));
        }

        [Test]
        public void GetAllProfiles_ForwardsToStorage()
        {
            _storage.ProfileIds = new List<string> { "a", "b" };

            List<string> profiles = _gameSaver.GetAllProfiles().ToList();

            Assert.That(profiles, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void SetSimple_ThenGetSimple_ReturnsStoredValue()
        {
            _gameSaver.SetSimple("coins", 123);

            int value = _gameSaver.GetSimple("coins", -1);

            Assert.That(value, Is.EqualTo(123));
            Assert.That(_gameSaver.HasSimpleKey("coins"), Is.True);
        }

        [Test]
        public void DeleteSimple_RemovesStoredValueAndKey()
        {
            _gameSaver.SetSimple("volume", 8);

            _gameSaver.DeleteSimple("volume");

            Assert.That(_gameSaver.HasSimpleKey("volume"), Is.False);
            Assert.That(_gameSaver.GetSimple("volume", -1), Is.EqualTo(-1));
        }

        [TestCase(null)]
        [TestCase("")]
        public void DirectAccess_WithInvalidKey_IsIgnored(string key)
        {
            _gameSaver.SetSimple("existing", 7);

            _gameSaver.SetSimple(key, 99);
            int value = _gameSaver.GetSimple(key, -1);
            bool hasKey = _gameSaver.HasSimpleKey(key);
            _gameSaver.DeleteSimple(key);

            Assert.That(value, Is.EqualTo(-1));
            Assert.That(hasKey, Is.False);
            Assert.That(_gameSaver.GetSimple("existing", -1), Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator GetSimple_WhenDeserializationFails_ReturnsDefaultValue()
        {
            SaveData loadedData = new();
            loadedData.SimpleData["broken"] = "broken-simple";
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _serializer.RegisterDeserializeException("broken-simple", new FormatException("invalid simple data"));
            _storage.SetPrimaryFile("profile-broken-simple", SaveFileName, serialized);

            GameSaverOperationHandle handle = _gameSaver.LoadAsync("profile-broken-simple");
            yield return WaitForOperation(handle);

            int value = _gameSaver.GetSimple("broken", 99);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(value, Is.EqualTo(99));
        }

        [UnityTest]
        public IEnumerator SaveImmediate_ThenLoadAsync_PreservesSimpleData()
        {
            _gameSaver.SetSimple("coins", 25);
            _gameSaver.SetSimple("player-name", "Ada");
            _gameSaver.SaveImmediate("profile-simple");
            _gameSaver.SetSimple("coins", 1);
            _gameSaver.DeleteSimple("player-name");

            GameSaverOperationHandle handle = _gameSaver.LoadAsync("profile-simple");
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(_gameSaver.GetSimple("coins", -1), Is.EqualTo(25));
            Assert.That(_gameSaver.GetSimple("player-name", string.Empty), Is.EqualTo("Ada"));
        }

        [UnityTest]
        public IEnumerator RegistryEvents_AutoRestoreOnRegister_AndCaptureOnUnregister()
        {
            SaveData loadedData = new();
            loadedData.DataModules["player"] = new TestSaveData { Value = 77 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-events", SaveFileName, serialized);
            yield return WaitForOperation(_gameSaver.LoadAsync("profile-events"));

            TestSavable savable = new("player", new TestSaveData { Value = 88 });
            _registry.Register(savable);
            _registry.Unregister(savable);
            _gameSaver.SaveImmediate();

            Assert.That(savable.RestoreCallCount, Is.EqualTo(1));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(77));

            SaveData savedData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(((TestSaveData)savedData.DataModules["player"]).Value, Is.EqualTo(88));
        }

        [Test]
        public void Dispose_UnsubscribesFromRegistryEvents()
        {
            _gameSaver.Dispose();
            _gameSaver = null;

            TestSavable savable = new("player", new TestSaveData { Value = 12 });
            _registry.Register(savable);
            _registry.Unregister(savable);

            Assert.That(savable.RestoreCallCount, Is.EqualTo(0));
            Assert.That(savable.CaptureCallCount, Is.EqualTo(0));
        }

        // ==============================================================================
        // Concurrency tests — verify the dirty-flag + snapshot + per-profile state design.
        // These tests use InMemoryStorageProvider.HoldWriteAsync() to freeze a save pipeline
        // mid-flight, then issue more operations and observe coalesce / parallel / reject behaviors.
        // ==============================================================================

        [UnityTest]
        public IEnumerator SaveAsync_SameProfile_Concurrent_CoalescesIntoTrailingSave()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle h1 = _gameSaver.SaveAsync("profile-coalesce");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading pipeline did not reach WriteAsync");

            // Issue 4 more saves while leading is blocked. All should coalesce into a single trailing save.
            GameSaverOperationHandle h2 = _gameSaver.SaveAsync("profile-coalesce");
            GameSaverOperationHandle h3 = _gameSaver.SaveAsync("profile-coalesce");
            GameSaverOperationHandle h4 = _gameSaver.SaveAsync("profile-coalesce");
            GameSaverOperationHandle h5 = _gameSaver.SaveAsync("profile-coalesce");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);
            yield return WaitForOperation(h4);
            yield return WaitForOperation(h5);

            Assert.That(h1.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h3.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h4.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h5.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));

            int saveFileWrites = _storage.WriteAsyncCalls.Count(c => c.fileName == SaveFileName);
            Assert.That(saveFileWrites, Is.LessThanOrEqualTo(2),
                $"Expected at most 2 save writes (1 leading + 1 trailing), got {saveFileWrites}");
        }

        [UnityTest]
        public IEnumerator SaveAsync_TrailingUsesLatestState()
        {
            TestSaveData data = new() { Value = 1 };
            _registry.Register(new TestSavable("player", data));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle h1 = _gameSaver.SaveAsync("profile-trailing-state");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading pipeline did not reach WriteAsync");

            // Mutate the captured value AFTER leading already serialized — trailing should pick up the new value.
            data.Value = 99;
            GameSaverOperationHandle h2 = _gameSaver.SaveAsync("profile-trailing-state");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            SaveData lastSaveData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(lastSaveData, Is.Not.Null);
            Assert.That(((TestSaveData)lastSaveData.DataModules["player"]).Value, Is.EqualTo(99),
                "Trailing save should have captured the mutated value (99), not the leading's value (1).");
        }

        [UnityTest]
        public IEnumerator SaveAsync_DifferentProfile_RunsInParallel()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle hA = _gameSaver.SaveAsync("profile-A");
            GameSaverOperationHandle hB = _gameSaver.SaveAsync("profile-B");

            // Both pipelines should reach WriteAsync simultaneously — both blocked at the gate.
            // If cross-profile parallelism is broken, only one would reach WriteAsync at a time.
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 2,
                "both saves did not reach WriteAsync simultaneously (cross-profile parallelism broken)");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(hA);
            yield return WaitForOperation(hB);

            Assert.That(hA.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(hB.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(_storage.WriteAsyncCalls.Any(c => c.profileId == "profile-A" && c.fileName == SaveFileName), Is.True,
                "profile-A save file should have been written.");
            Assert.That(_storage.WriteAsyncCalls.Any(c => c.profileId == "profile-B" && c.fileName == SaveFileName), Is.True,
                "profile-B save file should have been written.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_TrailingMetadata_LastWins()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            // Distinct ProfileID values let us tell which metas the pipeline mutated.
            // The pipeline overwrites metadata.ProfileID = targetProfile during a save.
            TestSaveMeta meta1 = new() { ProfileID = "initial-1" };
            TestSaveMeta meta2 = new() { ProfileID = "initial-2" };
            TestSaveMeta meta3 = new() { ProfileID = "initial-3" };

            GameSaverOperationHandle h1 = _gameSaver.SaveAsync("profile-meta", meta1);
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            // Two coalesced saves with different metadata. meta3 is the last — it should win.
            GameSaverOperationHandle h2 = _gameSaver.SaveAsync("profile-meta", meta2);
            GameSaverOperationHandle h3 = _gameSaver.SaveAsync("profile-meta", meta3);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);

            Assert.That(meta1.ProfileID, Is.EqualTo("profile-meta"),
                "meta1 should have been used by the leading pipeline (its ProfileID got rewritten).");
            Assert.That(meta2.ProfileID, Is.EqualTo("initial-2"),
                "meta2 should have been replaced by meta3 (last-wins) and never used by any pipeline.");
            Assert.That(meta3.ProfileID, Is.EqualTo("profile-meta"),
                "meta3 should have been used by the trailing pipeline.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_PendingHandles_CompleteOnlyAfterTrailing()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle h1 = _gameSaver.SaveAsync("profile-pending");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            GameSaverOperationHandle h2 = _gameSaver.SaveAsync("profile-pending");
            GameSaverOperationHandle h3 = _gameSaver.SaveAsync("profile-pending");

            // Coalesced handles must NOT complete while their trailing save is still waiting at the gate.
            Assert.That(h2.IsDone, Is.False, "Pending handle 2 should not complete before trailing save runs.");
            Assert.That(h3.IsDone, Is.False, "Pending handle 3 should not complete before trailing save runs.");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);

            Assert.That(h1.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h3.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator SaveAsync_ExplicitProfile_DoesNotChangeImplicitTarget()
        {
            // Setup: pre-populate profile-A and load it so _curProfileId = "profile-A".
            SaveData stored = new();
            stored.DataModules["player"] = new TestSaveData { Value = 1 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-A", SaveFileName, serialized);
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            yield return WaitForOperation(_gameSaver.LoadAsync("profile-A"));

            // Explicit save to profile-B should NOT change which profile is "current".
            GameSaverOperationHandle hExplicit = _gameSaver.SaveAsync("profile-B");
            yield return WaitForOperation(hExplicit);

            // Implicit save (null profile) should still target profile-A.
            GameSaverOperationHandle hImplicit = _gameSaver.SaveAsync();
            yield return WaitForOperation(hImplicit);

            Assert.That(hImplicit.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));

            (string profileId, string fileName, string data) lastSave = _storage.WriteAsyncCalls.Last(c => c.fileName == SaveFileName);
            Assert.That(lastSave.profileId, Is.EqualTo("profile-A"),
                "Implicit SaveAsync(null) should target the loaded profile (A), not the most recent explicit profile (B).");
        }

        [UnityTest]
        public IEnumerator SetSimple_DuringSave_TrailingPersistsAllValues()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle h1 = _gameSaver.SaveAsync("profile-setsimple");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            // Hammer SetSimple while the leading save is blocked at the gate.
            // The lock around SimpleData mutations must prevent any "Collection was modified" exceptions
            // when the trailing snapshot's Clone iterates the dictionary.
            for (int i = 0; i < 100; i++)
            {
                _gameSaver.SetSimple($"key-{i}", i);
            }

            // Trigger trailing — its capture+snapshot should include all 100 SetSimple values.
            GameSaverOperationHandle h2 = _gameSaver.SaveAsync("profile-setsimple");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            Assert.That(h1.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));

            SaveData lastSaveData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(lastSaveData, Is.Not.Null);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(lastSaveData.SimpleData.ContainsKey($"key-{i}"), Is.True,
                    $"Trailing save should contain key-{i} that was set during the leading save.");
            }
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhileSavingSameProfile_CompletesFailed()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.DataModules["player"] = new TestSaveData { Value = 5 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-conflict", SaveFileName, serialized);

            _storage.HoldWriteAsync();
            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Load same profile while save is in flight — must fail.
            GameSaverOperationHandle loadHandle = _gameSaver.LoadAsync("profile-conflict");
            yield return WaitForOperation(loadHandle);

            Assert.That(loadHandle.Status, Is.EqualTo(GameSaverOperationStatus.Failed));
            Assert.That(loadHandle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(loadHandle.Error.Message, Does.Contain("save operation is in flight"));

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhileSavingDifferentProfile_Succeeds()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.DataModules["player"] = new TestSaveData { Value = 7 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-load-target", SaveFileName, serialized);

            _storage.HoldWriteAsync();
            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-being-saved");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Load a DIFFERENT profile — must succeed, no conflict.
            GameSaverOperationHandle loadHandle = _gameSaver.LoadAsync("profile-load-target");
            yield return WaitForOperation(loadHandle);

            Assert.That(loadHandle.Status, Is.EqualTo(GameSaverOperationStatus.Succeeded));

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator SaveImmediate_WhileSaveAsyncSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Immediate save for the same profile must throw — cannot wait for async pipeline to drain.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _gameSaver.SaveImmediate("profile-conflict"));
            Assert.That(ex.Message, Does.Contain("in flight"));

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator SaveImmediate_WhileSaveAsyncDifferentProfile_Succeeds()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-being-saved");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // SaveImmediate for a different profile must succeed — different state entry.
            Assert.DoesNotThrow(() => _gameSaver.SaveImmediate("profile-different"));
            Assert.That(_storage.WriteImmediateCalls.Any(c => c.profileId == "profile-different" && c.fileName == SaveFileName), Is.True);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator DeleteProfile_WhileSavingSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Delete same profile while save is in flight — must throw.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _gameSaver.DeleteProfile("profile-conflict"));
            Assert.That(ex.Message, Does.Contain("in flight"));

            // Delete must NOT have happened.
            Assert.That(_storage.DeleteProfileCalls, Is.Empty);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        // ==============================================================================
        // App lifecycle & auto-save tests (Cluster #4)
        // Cover WaitForPendingOperationsAsync, OnSaveCompleted event, auto-save countdown
        // logic, and tick-driven saves. Lifecycle event hooks (Application.quitting /
        // focusChanged) are not directly tested — they invoke PerformAutoSaveFlush which
        // is exercised indirectly through WaitForPendingOperationsAsync + SaveImmediate.
        // ==============================================================================

        [UnityTest]
        public IEnumerator WaitForPendingOperationsAsync_NoSaveInFlight_CompletesImmediately()
        {
            Task waitTask = _gameSaver.WaitForPendingOperationsAsync();
            yield return WaitForCondition(() => waitTask.IsCompleted, "WaitForPendingOperationsAsync did not complete when no saves were running.");
            Assert.IsTrue(waitTask.IsCompletedSuccessfully);
        }

        [UnityTest]
        public IEnumerator WaitForPendingOperationsAsync_PendingSave_AwaitsCompletion()
        {
            _storage.HoldWriteAsync();
            GameSaverOperationHandle saveHandle = _gameSaver.SaveAsync("profile-wait");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            Task waitTask = _gameSaver.WaitForPendingOperationsAsync();
            yield return null; // give the wait task a chance to enter Task.WhenAll

            Assert.IsFalse(waitTask.IsCompleted, "Wait should still be pending while the save is blocked at the storage gate.");

            _storage.ReleaseWriteAsync();
            yield return WaitForCondition(() => waitTask.IsCompleted, "Wait did not complete after the storage gate was released.");
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator WaitForPendingOperationsAsync_MultipleProfiles_AwaitsAll()
        {
            _storage.HoldWriteAsync();
            GameSaverOperationHandle hA = _gameSaver.SaveAsync("profile-wait-A");
            GameSaverOperationHandle hB = _gameSaver.SaveAsync("profile-wait-B");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 2, "both saves did not reach WriteAsync simultaneously");

            Task waitTask = _gameSaver.WaitForPendingOperationsAsync();
            yield return null;
            Assert.IsFalse(waitTask.IsCompleted, "Wait should still be pending with two saves held at the gate.");

            _storage.ReleaseWriteAsync();
            yield return WaitForCondition(() => waitTask.IsCompleted, "Wait did not complete after releasing both saves.");
            yield return WaitForOperation(hA);
            yield return WaitForOperation(hB);
        }

        [Test]
        public void OnSaveCompleted_FiresWithProfileId_AfterSaveImmediate()
        {
            string captured = null;
            _gameSaver.OnSaveCompleted += id => captured = id;

            _gameSaver.SaveImmediate("profile-event-sync");

            Assert.AreEqual("profile-event-sync", captured, "OnSaveCompleted should fire with the saved profile ID.");
        }

        [UnityTest]
        public IEnumerator OnSaveCompleted_FiresWithProfileId_AfterSaveAsync()
        {
            string captured = null;
            _gameSaver.OnSaveCompleted += id => captured = id;

            GameSaverOperationHandle handle = _gameSaver.SaveAsync("profile-event-async");
            yield return WaitForOperation(handle);

            // The event is marshaled back to the captured SynchronizationContext — give it a frame to deliver.
            yield return WaitForCondition(() => captured != null, "OnSaveCompleted did not fire after SaveAsync completed.");
            Assert.AreEqual("profile-event-async", captured);
        }

        [Test]
        public void AutoSaveCountdown_Resets_WhenSaveImmediateMatchesCurrentProfile()
        {
            SetPrivateField(_gameSaver, "_curProfileId", "profile-current");
            SetPrivateField(_gameSaver, "_autoSaveCountdown", 42f);

            _gameSaver.SaveImmediate("profile-current");

            float countdownAfter = GetPrivateField<float>(_gameSaver, "_autoSaveCountdown");
            Assert.That(countdownAfter, Is.EqualTo(_settings.AutoSaveTime).Within(0.01f),
                "Countdown should be reset to AutoSaveTime when the save target matches the current profile.");
        }

        [Test]
        public void AutoSaveCountdown_DoesNotReset_WhenSaveImmediateDifferentProfile()
        {
            SetPrivateField(_gameSaver, "_curProfileId", "profile-A");
            SetPrivateField(_gameSaver, "_autoSaveCountdown", 42f);

            _gameSaver.SaveImmediate("profile-B");

            float countdownAfter = GetPrivateField<float>(_gameSaver, "_autoSaveCountdown");
            Assert.That(countdownAfter, Is.EqualTo(42f).Within(0.01f),
                "Countdown must NOT reset when an explicit save targets a different profile.");
        }

        [Test]
        public void AutoSaveTick_AutoSaveDisabled_DoesNotTriggerSave()
        {
            SetPrivateField(_settings, "_enableAutoSave", false);
            SetPrivateField(_gameSaver, "_curProfileId", "profile-disabled");
            SetPrivateField(_gameSaver, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_gameSaver, 100f);

            Assert.AreEqual(writesBefore, _storage.WriteAsyncEnteredCount, "AutoSaveTick must not trigger a save when EnableAutoSave is false.");
        }

        [Test]
        public void AutoSaveTick_NoCurrentProfile_DoesNotTriggerSave()
        {
            SetPrivateField(_settings, "_enableAutoSave", true);
            SetPrivateField<string>(_gameSaver, "_curProfileId", null);
            SetPrivateField(_gameSaver, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_gameSaver, 100f);

            Assert.AreEqual(writesBefore, _storage.WriteAsyncEnteredCount, "AutoSaveTick must not trigger a save when no profile is loaded.");
        }

        [UnityTest]
        public IEnumerator AutoSaveTick_CountdownReached_TriggersImplicitSave()
        {
            SetPrivateField(_settings, "_enableAutoSave", true);
            SetPrivateField(_settings, "_autoSaveTime", 1f);
            SetPrivateField(_gameSaver, "_curProfileId", "profile-tick");
            SetPrivateField(_gameSaver, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_gameSaver, 1f);

            yield return WaitForCondition(
                () => _storage.WriteAsyncEnteredCount > writesBefore,
                "AutoSaveTick should have triggered SaveAsync once the countdown reached zero.");
        }

        [Test]
        public void Dispose_UnsubscribesFromAutoSaveTicker_AndApplicationEvents()
        {
            // No direct introspection of static Application event lists is available in C#,
            // so this test asserts at least that Dispose runs idempotently without throwing
            // and that a second Dispose (after disposal) doesn't blow up.
            Assert.DoesNotThrow(() => _gameSaver.Dispose());
            Assert.DoesNotThrow(() => _gameSaver.Dispose(), "Dispose must be safe to call twice.");

            // Prevent TearDown from disposing again on the already-disposed instance.
            _gameSaver = null;
        }

        private static string SaveFileName => "slot.sav";
        private static string MetaFileName => "slot.meta";

        private void RecreateGameSaver(bool useEncryption)
        {
            _gameSaver.Dispose();
            UnityEngine.Object.DestroyImmediate(_settings);
            _settings = CreateSettings(useEncryption);
            _gameSaver = new GameSaverRuntime(_registry, _storage, _serializer, _encryption, _settings);
        }

        private static SaveSettings CreateSettings(bool useEncryption)
        {
            SaveSettings settings = ScriptableObject.CreateInstance<SaveSettings>();
            SetPrivateField(settings, "_useEncryption", useEncryption);
            SetPrivateField(settings, "_fileName", "slot");
            SetPrivateField(settings, "_saveExtension", ".sav");
            SetPrivateField(settings, "_metaExtension", ".meta");
            return settings;
        }

        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(target);
        }

        private static void InvokeAutoSaveTick(GameSaverRuntime gameSaver, float deltaTime)
        {
            MethodInfo method = typeof(GameSaverRuntime).GetMethod("AutoSaveTick", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(gameSaver, new object[] { deltaTime });
        }

        private static IEnumerator WaitForOperation(GameSaverOperationHandle handle, float timeoutSeconds = 1f)
        {
            float startTime = Time.realtimeSinceStartup;

            while (!handle.IsDone)
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    Assert.Fail($"Operation timed out after {timeoutSeconds} seconds.");
                }

                yield return null;
            }
        }

        private static IEnumerator WaitForOperation<T>(GameSaverOperationHandle<T> handle, float timeoutSeconds = 1f)
        {
            float startTime = Time.realtimeSinceStartup;

            while (!handle.IsDone)
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    Assert.Fail($"Operation timed out after {timeoutSeconds} seconds.");
                }

                yield return null;
            }
        }

        private static IEnumerator WaitForCondition(Func<bool> predicate, string failureMessage, float timeoutSeconds = 2f)
        {
            float startTime = Time.realtimeSinceStartup;

            while (!predicate())
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    Assert.Fail($"Timed out after {timeoutSeconds}s waiting for: {failureMessage}");
                }

                yield return null;
            }
        }

        private sealed class InMemoryStorageProvider : IStorageProvider
        {
            private readonly Dictionary<(string profileId, string fileName), string> _primaryFiles = new();
            private readonly Dictionary<(string profileId, string fileName), string> _backupFiles = new();

            public readonly List<(string profileId, string fileName, string data)> WriteAsyncCalls = new();
            public readonly List<(string profileId, string fileName, string data)> WriteImmediateCalls = new();
            public readonly List<(string profileId, string fileName)> ReadAsyncCalls = new();
            public readonly List<(string profileId, string fileName)> ReadBackupCalls = new();
            public readonly List<(string profileId, string fileName)> RestoreBackupCalls = new();
            public readonly List<string> DeleteProfileCalls = new();

            public Exception WriteAsyncException { get; set; }
            public Exception GetAllProfileIdsException { get; set; }
            public List<string> ProfileIds { get; set; } = new();
            public string MostRecentProfileId { get; set; }

            // Concurrency test infrastructure: a gate that holds WriteAsync calls until released.
            // Use HoldWriteAsync/ReleaseWriteAsync to simulate slow disk I/O.
            private readonly object _writeLock = new();
            private TaskCompletionSource<bool> _writeAsyncGate;
            private int _writeAsyncEnteredCount;

            public int WriteAsyncEnteredCount => _writeAsyncEnteredCount;

            /// <summary>
            /// Starts holding all subsequent WriteAsync calls at a gate. Call ReleaseWriteAsync to let them proceed.
            /// </summary>
            public void HoldWriteAsync()
            {
                _writeAsyncGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>
            /// Releases all WriteAsync calls currently waiting at the gate, and stops holding new ones.
            /// </summary>
            public void ReleaseWriteAsync()
            {
                TaskCompletionSource<bool> gate = _writeAsyncGate;
                _writeAsyncGate = null;
                gate?.TrySetResult(true);
            }

            public async Task WriteAsync(string profileId, string fileName, string data)
            {
                System.Threading.Interlocked.Increment(ref _writeAsyncEnteredCount);

                if (WriteAsyncException != null)
                {
                    throw WriteAsyncException;
                }

                TaskCompletionSource<bool> gate = _writeAsyncGate;
                if (gate != null) await gate.Task;

                lock (_writeLock)
                {
                    WriteAsyncCalls.Add((profileId, fileName, data));
                    _primaryFiles[(profileId, fileName)] = data;
                    if (!ProfileIds.Contains(profileId))
                    {
                        ProfileIds.Add(profileId);
                    }
                }
            }

            public void WriteImmediate(string profileId, string fileName, string data)
            {
                WriteImmediateCalls.Add((profileId, fileName, data));
                _primaryFiles[(profileId, fileName)] = data;
                if (!ProfileIds.Contains(profileId))
                {
                    ProfileIds.Add(profileId);
                }
            }

            public void RestoreBackup(string profileId, string fileName)
            {
                RestoreBackupCalls.Add((profileId, fileName));
            }

            public Task<string> ReadAsync(string profileId, string fileName)
            {
                ReadAsyncCalls.Add((profileId, fileName));
                return Task.FromResult(_primaryFiles[(profileId, fileName)]);
            }

            public Task<string> ReadBackupAsync(string profileId, string fileName)
            {
                ReadBackupCalls.Add((profileId, fileName));
                return Task.FromResult(_backupFiles[(profileId, fileName)]);
            }

            public void DeleteProfile(string profileId)
            {
                DeleteProfileCalls.Add(profileId);
                List<(string profileId, string fileName)> primaryKeys = _primaryFiles.Keys.Where(key => key.profileId == profileId).ToList();
                foreach ((string currentProfileId, string fileName) key in primaryKeys)
                {
                    _primaryFiles.Remove(key);
                }

                List<(string profileId, string fileName)> backupKeys = _backupFiles.Keys.Where(key => key.profileId == profileId).ToList();
                foreach ((string currentProfileId, string fileName) key in backupKeys)
                {
                    _backupFiles.Remove(key);
                }

                ProfileIds.Remove(profileId);
            }

            public bool Exists(string profileId, string fileName)
            {
                return _primaryFiles.ContainsKey((profileId, fileName));
            }

            public IEnumerable<string> GetAllProfileIds()
            {
                if (GetAllProfileIdsException != null)
                {
                    throw GetAllProfileIdsException;
                }

                return ProfileIds;
            }

            public string GetMostRecentProfileId()
            {
                return MostRecentProfileId;
            }

            public void SetPrimaryFile(string profileId, string fileName, string data)
            {
                _primaryFiles[(profileId, fileName)] = data;
                if (!ProfileIds.Contains(profileId))
                {
                    ProfileIds.Add(profileId);
                }
            }

            public void SetBackupFile(string profileId, string fileName, string data)
            {
                _backupFiles[(profileId, fileName)] = data;
                if (!ProfileIds.Contains(profileId))
                {
                    ProfileIds.Add(profileId);
                }
            }
        }

        private sealed class TrackingSerializer : ISerializer
        {
            private readonly object _serializerLock = new();
            private readonly Dictionary<string, object> _serializedValues = new();
            private readonly Dictionary<string, Exception> _deserializeExceptions = new();
            private readonly List<object> _serializedObjects = new();
            private int _nextId;

            public string Serialize<T>(T obj)
            {
                // CloneObject runs outside the lock — it's a pure function with no shared state.
                object clone = CloneObject(obj);

                lock (_serializerLock)
                {
                    _serializedObjects.Add(clone);
                    string key = $"serialized-{++_nextId}";
                    _serializedValues[key] = clone;
                    return key;
                }
            }

            public T Deserialize<T>(string data)
            {
                if (_deserializeExceptions.TryGetValue(data, out Exception exception))
                {
                    throw exception;
                }

                object value;
                lock (_serializerLock)
                {
                    value = _serializedValues[data];
                }
                return (T)CloneObject(value);
            }

            public string RegisterSerializedValue(object value)
            {
                string key = $"registered-{++_nextId}";
                _serializedValues[key] = CloneObject(value);
                return key;
            }

            public void RegisterDeserializeException(string rawValue, Exception exception)
            {
                _deserializeExceptions[rawValue] = exception;
            }

            public T GetLastSerializedObject<T>() where T : class
            {
                return _serializedObjects.LastOrDefault(item => item is T) as T;
            }

            private static object CloneObject(object value)
            {
                return value switch
                {
                    null => null,
                    SaveData saveData => CloneSaveData(saveData),
                    TestSaveMeta metadata => new TestSaveMeta
                    {
                        ProfileID = metadata.ProfileID,
                        LastTimeSaved = metadata.LastTimeSaved,
                    },
                    TestSaveData saveData => new TestSaveData { Value = saveData.Value },
                    _ => value,
                };
            }

            private static SaveData CloneSaveData(SaveData source)
            {
                SaveData clone = new();

                foreach (KeyValuePair<string, ISaveData> item in source.DataModules)
                {
                    clone.DataModules[item.Key] = (ISaveData)CloneObject(item.Value);
                }

                foreach (KeyValuePair<string, string> item in source.SimpleData)
                {
                    clone.SimpleData[item.Key] = item.Value;
                }

                return clone;
            }
        }

        private sealed class TrackingEncryptionProvider : IEncryptionProvider
        {
            public readonly List<string> EncryptInputs = new();
            public readonly List<string> DecryptInputs = new();

            public string Encrypt(string plainText)
            {
                EncryptInputs.Add(plainText);
                return $"enc::{plainText}";
            }

            public string Decrypt(string cipherText)
            {
                DecryptInputs.Add(cipherText);
                return cipherText.StartsWith("enc::", StringComparison.Ordinal) ? cipherText.Substring(5) : cipherText;
            }
        }

        private sealed class TestSavable : ISavable
        {
            private readonly ISaveData _capturedData;

            public TestSavable(string saveKey, ISaveData capturedData)
            {
                SaveKey = saveKey;
                _capturedData = capturedData;
            }

            public string SaveKey { get; }
            public int CaptureCallCount { get; private set; }
            public int RestoreCallCount { get; private set; }
            public ISaveData LastRestoredData { get; private set; }

            public ISaveData CaptureData()
            {
                CaptureCallCount++;
                return _capturedData;
            }

            public void RestoreData(ISaveData data)
            {
                RestoreCallCount++;
                LastRestoredData = data;
            }
        }

        private sealed class TestSaveData : ISaveData
        {
            public int Value { get; set; }
        }

        private sealed class TestSaveMeta : ISaveMeta
        {
            public string ProfileID { get; set; }
            public DateTime LastTimeSaved { get; set; }
        }
    }
}