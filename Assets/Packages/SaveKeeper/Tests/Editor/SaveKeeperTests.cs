using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.Core;
using SaveKeeperRuntime = ThanhDV.SaveKeeper.Core.SaveKeeper;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;

namespace ThanhDV.SaveKeeper.Tests.Editor
{
    public class SaveKeeperTests
    {
        private SaveRegistry _registry;
        private InMemoryStorageProvider _storage;
        private TrackingSerializer _serializer;
        private TrackingEncryptionProvider _encryption;
        private SaveSettings _settings;
        private SaveKeeperRuntime _SaveKeeper;

        [SetUp]
        public void SetUp()
        {
            _registry = new SaveRegistry();
            _storage = new InMemoryStorageProvider();
            _serializer = new TrackingSerializer();
            _encryption = new TrackingEncryptionProvider();
            _settings = CreateSettings(useEncryption: true);
            _SaveKeeper = new SaveKeeperRuntime(_registry, _storage, _serializer, _encryption, _settings);
        }

        [TearDown]
        public void TearDown()
        {
            _SaveKeeper?.Dispose();
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

            Assert.Throws<ArgumentNullException>(() => new SaveKeeperRuntime(registry, storage, serializer, encryption, settings));
        }

        [Test]
        public void SaveImmediate_WithMetadata_CapturesSavables_WritesSaveAndMetadata()
        {
            TestSavable savable = new("player", new TestSaveData { Value = 42 });
            _registry.Register(savable);
            // Caller-supplied metadata is used as-is; lib does not modify ProfileID or LastTimeSaved.
            DateTime callerTime = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            TestSaveMeta metadata = new() { ProfileID = "profile-1", LastTimeSaved = callerTime };

            _SaveKeeper.SaveImmediate("profile-1", metadata);

            Assert.That(savable.CaptureCallCount, Is.EqualTo(1));
            Assert.That(metadata.ProfileID, Is.EqualTo("profile-1"), "Caller's ProfileID preserved (lib does not override).");
            Assert.That(metadata.LastTimeSaved, Is.EqualTo(callerTime), "Caller's LastTimeSaved preserved (lib does not override).");
            Assert.That(_encryption.EncryptInputs, Has.Count.EqualTo(1), "Only .sav is encrypted; .meta is plain JSON.");
            Assert.That(_storage.WriteImmediateCalls, Has.Count.EqualTo(2));
            Assert.That(_storage.WriteImmediateCalls[0].fileName, Is.Not.EqualTo(_storage.WriteImmediateCalls[1].fileName), "Save data and metadata should be written to separate files.");
        }

        [Test]
        public void SaveImmediate_WithoutMetadata_UsesExplicitProfile_AndSkipsEncryptionWhenDisabled()
        {
            RecreateSaveKeeper(useEncryption: false);
            TestSavable savable = new("player", new TestSaveData { Value = 5 });
            _registry.Register(savable);

            _SaveKeeper.SaveImmediate("profile-plain");

            // Cluster #7: .meta sidecar is always written (uses DefaultSaveMeta when metadata=null) → 2 writes total.
            Assert.That(_storage.WriteImmediateCalls, Has.Count.EqualTo(2));
            Assert.That(_storage.WriteImmediateCalls.Select(c => c.profileId), Is.All.EqualTo("profile-plain"));
            Assert.That(_storage.WriteImmediateCalls[0].fileName, Is.EqualTo(SaveFileName), "Save data must be written first (source of truth, atomic).");
            Assert.That(_storage.WriteImmediateCalls[1].fileName, Is.EqualTo(MetaFileName), "Meta sidecar must be written after .sav (meta.mtime >= sav.mtime invariant).");
            // .sav still encrypted/skipped per setting; .meta is always plain JSON regardless.
            Assert.That(_encryption.EncryptInputs, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SaveAsync_CompletesSuccessfully_AndWritesData()
        {
            TestSavable savable = new("player", new TestSaveData { Value = 10 });
            _registry.Register(savable);
            // Lib trusts caller-supplied metadata as-is — caller pre-fills fields.
            TestSaveMeta metadata = new() { ProfileID = "profile-async", LastTimeSaved = DateTime.UtcNow };

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-async", metadata);
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(_storage.WriteAsyncCalls, Has.Count.EqualTo(2), ".sav + .meta both written.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_WithoutValidProfile_CompletesFailed()
        {
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Does.Contain("Cannot save"));
        }

        [Test]
        public void SaveImmediate_WithoutValidProfile_ThrowsInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _SaveKeeper.SaveImmediate());
        }

        [UnityTest]
        public IEnumerator SaveAsync_WhenStorageThrows_CompletesFailed()
        {
            _storage.WriteAsyncException = new InvalidOperationException("write failed");
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 10 }));

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-fail");

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Is.EqualTo("write failed"));
        }

        [Test]
        public void RestoreBackup_WithProfile_ForwardsToStorage()
        {
            _SaveKeeper.RestoreBackup("profile-restore");

            Assert.That(_storage.RestoreBackupCalls, Has.Count.EqualTo(1));
            Assert.That(_storage.RestoreBackupCalls[0].profileId, Is.EqualTo("profile-restore"));
        }

        [Test]
        public void RestoreBackup_WithNullProfile_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _SaveKeeper.RestoreBackup(null));
        }

        [UnityTest]
        public IEnumerator LoadAsync_RestoresPrimaryData_AndUpdatesCurrentProfileForLaterSave()
        {
            TestSavable loadTarget = new("player", null);
            SaveData loadedData = new();
            loadedData.ObjectData["player"] = new TestSaveData { Value = 99 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-load", SaveFileName, serialized);
            _registry.Register(loadTarget);

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-load");
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(loadTarget.RestoreCallCount, Is.EqualTo(1),
                "Register-time restore is skipped because _curSaveData has no entry for 'player' yet (#21 fix). " +
                "Only the load triggers RestoreData, with the real loaded data.");
            Assert.That(((TestSaveData)loadTarget.LastRestoredData).Value, Is.EqualTo(99));

            TestSavable saveTarget = new("other", new TestSaveData { Value = 1 });
            _registry.Register(saveTarget);
            _SaveKeeper.SaveImmediate();

            Assert.That(_storage.WriteImmediateCalls.Last().profileId, Is.EqualTo("profile-load"));
        }

        [UnityTest]
        public IEnumerator LoadBackupAsync_UsesBackupReadPath()
        {
            TestSavable savable = new("player", null);
            SaveData loadedData = new();
            loadedData.ObjectData["player"] = new TestSaveData { Value = 7 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetBackupFile("profile-backup", SaveFileName, serialized);
            _registry.Register(savable);

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadBackupAsync("profile-backup");
            yield return WaitForOperation(handle);

            Assert.That(_storage.ReadBackupCalls, Has.Count.EqualTo(1));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator LoadAsync_WithoutValidProfile_CompletesFailed()
        {
            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync(null);

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Does.Contain("Cannot load"));
        }

        [UnityTest]
        public IEnumerator LoadMostRecentAsync_WhenProfileExists_LoadsThatProfile()
        {
            TestSavable savable = new("player", null);
            SaveData loadedData = new();
            loadedData.ObjectData["player"] = new TestSaveData { Value = 15 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.MostRecentProfileId = "recent-profile";
            _storage.SetPrimaryFile("recent-profile", SaveFileName, serialized);
            _registry.Register(savable);

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadMostRecentAsync();
            yield return WaitForOperation(handle);

            Assert.That(_storage.ReadAsyncCalls.Single().profileId, Is.EqualTo("recent-profile"));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(15));
        }

        [UnityTest]
        public IEnumerator LoadMostRecentAsync_WhenNoProfileExists_CompletesFailed()
        {
            _storage.MostRecentProfileId = null;

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadMostRecentAsync();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<FileNotFoundException>());
            Assert.That(handle.Error.Message, Does.Contain("No previously saved Profile"));
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_ReturnsSortedMetadata_AndSkipsProfilesWithoutSav()
        {
            TestSaveMeta oldMeta = new() { ProfileID = "profile-old", LastTimeSaved = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            TestSaveMeta newMeta = new() { ProfileID = "profile-new", LastTimeSaved = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            SaveData oldSav = new() { Meta = oldMeta };
            SaveData newSav = new() { Meta = newMeta };

            _storage.ProfileIds = new List<string> { "profile-old", "profile-no-sav", "profile-new", "profile-empty" };
            _storage.SetPrimaryFile("profile-old", SaveFileName, _serializer.RegisterSerializedValue(oldSav));
            _storage.SetPrimaryFile("profile-old", MetaFileName, _serializer.RegisterSerializedValue(oldMeta));
            _storage.SetPrimaryFile("profile-new", SaveFileName, _serializer.RegisterSerializedValue(newSav));
            _storage.SetPrimaryFile("profile-new", MetaFileName, _serializer.RegisterSerializedValue(newMeta));
            // profile-no-sav: only .meta (no .sav) — skipped at Exists(SaveName) check.
            _storage.SetPrimaryFile("profile-no-sav", MetaFileName, _serializer.RegisterSerializedValue(oldMeta));
            // profile-empty: in ProfileIds but no files at all — skipped.

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);
            List<TestSaveMeta> result = handle.Result;

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(result.Select(meta => meta.ProfileID).ToArray(), Is.EqualTo(new[] { "profile-new", "profile-old" }),
                "Only profiles with a .sav file are listed, sorted by LastTimeSaved descending.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_WhenProfileEnumerationFails_CompletesFailed()
        {
            _storage.GetAllProfileIdsException = new InvalidOperationException("enumeration failed");

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();

            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(handle.Error.Message, Is.EqualTo("enumeration failed"));
        }

        [Test]
        public void DeleteProfile_WithNullOrEmptyProfile_DoesNothing()
        {
            _SaveKeeper.DeleteProfile(null);
            _SaveKeeper.DeleteProfile(string.Empty);

            Assert.That(_storage.DeleteProfileCalls, Is.Empty);
        }

        [UnityTest]
        public IEnumerator DeleteProfile_CurrentProfileIsCleared_AfterSuccessfulLoad()
        {
            SaveData loadedData = new();
            loadedData.ObjectData["player"] = new TestSaveData { Value = 1 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-delete", SaveFileName, serialized);

            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-delete");
            yield return WaitForOperation(loadHandle);

            _SaveKeeper.DeleteProfile("profile-delete");

            Assert.That(_storage.DeleteProfileCalls.Single(), Is.EqualTo("profile-delete"));
            Assert.Throws<InvalidOperationException>(() => _SaveKeeper.SaveImmediate());
        }

        [Test]
        public void GetMostRecentProfileId_ForwardsToStorage()
        {
            _storage.MostRecentProfileId = "recent-id";

            string profileId = _SaveKeeper.GetMostRecentProfileId();

            Assert.That(profileId, Is.EqualTo("recent-id"));
        }

        [Test]
        public void GetAllProfiles_ForwardsToStorage()
        {
            _storage.ProfileIds = new List<string> { "a", "b" };

            List<string> profiles = _SaveKeeper.GetAllProfiles().ToList();

            Assert.That(profiles, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void SetSimple_ThenGetSimple_ReturnsStoredValue()
        {
            _SaveKeeper.SetSimple("coins", 123);

            int value = _SaveKeeper.GetSimple("coins", -1);

            Assert.That(value, Is.EqualTo(123));
            Assert.That(_SaveKeeper.HasSimpleKey("coins"), Is.True);
        }

        [Test]
        public void DeleteSimple_RemovesStoredValueAndKey()
        {
            _SaveKeeper.SetSimple("volume", 8);

            _SaveKeeper.DeleteSimple("volume");

            Assert.That(_SaveKeeper.HasSimpleKey("volume"), Is.False);
            Assert.That(_SaveKeeper.GetSimple("volume", -1), Is.EqualTo(-1));
        }

        [TestCase(null)]
        [TestCase("")]
        public void DirectAccess_WithInvalidKey_IsIgnored(string key)
        {
            _SaveKeeper.SetSimple("existing", 7);

            _SaveKeeper.SetSimple(key, 99);
            int value = _SaveKeeper.GetSimple(key, -1);
            bool hasKey = _SaveKeeper.HasSimpleKey(key);
            _SaveKeeper.DeleteSimple(key);

            Assert.That(value, Is.EqualTo(-1));
            Assert.That(hasKey, Is.False);
            Assert.That(_SaveKeeper.GetSimple("existing", -1), Is.EqualTo(7));
        }

        [UnityTest]
        public IEnumerator GetSimple_WhenDeserializationFails_ReturnsDefaultValue()
        {
            SaveData loadedData = new();
            loadedData.SimpleData["broken"] = "broken-simple";
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _serializer.RegisterDeserializeException("broken-simple", new FormatException("invalid simple data"));
            _storage.SetPrimaryFile("profile-broken-simple", SaveFileName, serialized);

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-broken-simple");
            yield return WaitForOperation(handle);

            int value = _SaveKeeper.GetSimple("broken", 99);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(value, Is.EqualTo(99));
        }

        [UnityTest]
        public IEnumerator SaveImmediate_ThenLoadAsync_PreservesSimpleData()
        {
            _SaveKeeper.SetSimple("coins", 25);
            _SaveKeeper.SetSimple("player-name", "Ada");
            _SaveKeeper.SaveImmediate("profile-simple");
            _SaveKeeper.SetSimple("coins", 1);
            _SaveKeeper.DeleteSimple("player-name");

            // SetSimple/DeleteSimple after save → IsSimpleDataDirty == true.
            // Need explicit discardUnsavedChanges=true to load over the modifications (#5 fix).
            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-simple", discardUnsavedChanges: true);
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(_SaveKeeper.GetSimple("coins", -1), Is.EqualTo(25));
            Assert.That(_SaveKeeper.GetSimple("player-name", string.Empty), Is.EqualTo("Ada"));
        }

        [UnityTest]
        public IEnumerator RegistryEvents_AutoRestoreOnRegister_AndCaptureOnUnregister()
        {
            SaveData loadedData = new();
            loadedData.ObjectData["player"] = new TestSaveData { Value = 77 };
            string serialized = _serializer.RegisterSerializedValue(loadedData);
            _storage.SetPrimaryFile("profile-events", SaveFileName, serialized);
            yield return WaitForOperation(_SaveKeeper.LoadAsync("profile-events"));

            TestSavable savable = new("player", new TestSaveData { Value = 88 });
            _registry.Register(savable);
            _registry.Unregister(savable);
            _SaveKeeper.SaveImmediate();

            Assert.That(savable.RestoreCallCount, Is.EqualTo(1));
            Assert.That(((TestSaveData)savable.LastRestoredData).Value, Is.EqualTo(77));

            SaveData savedData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(((TestSaveData)savedData.ObjectData["player"]).Value, Is.EqualTo(88));
        }

        [Test]
        public void Dispose_UnsubscribesFromRegistryEvents()
        {
            _SaveKeeper.Dispose();
            _SaveKeeper = null;

            TestSavable savable = new("player", new TestSaveData { Value = 12 });
            _registry.Register(savable);
            _registry.Unregister(savable);

            Assert.That(savable.RestoreCallCount, Is.EqualTo(0));
            Assert.That(savable.CaptureCallCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SaveAsync_SameProfile_Concurrent_CoalescesIntoTrailingSave()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-coalesce");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading pipeline did not reach WriteAsync");

            // Issue 4 more saves while leading is blocked. All should coalesce into a single trailing save.
            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-coalesce");
            SaveKeeperOperationHandle h3 = _SaveKeeper.SaveAsync("profile-coalesce");
            SaveKeeperOperationHandle h4 = _SaveKeeper.SaveAsync("profile-coalesce");
            SaveKeeperOperationHandle h5 = _SaveKeeper.SaveAsync("profile-coalesce");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);
            yield return WaitForOperation(h4);
            yield return WaitForOperation(h5);

            Assert.That(h1.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h3.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h4.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h5.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

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

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-trailing-state");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading pipeline did not reach WriteAsync");

            // Mutate the captured value AFTER leading already serialized — trailing should pick up the new value.
            data.Value = 99;
            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-trailing-state");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            SaveData lastSaveData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(lastSaveData, Is.Not.Null);
            Assert.That(((TestSaveData)lastSaveData.ObjectData["player"]).Value, Is.EqualTo(99),
                "Trailing save should have captured the mutated value (99), not the leading's value (1).");
        }

        [UnityTest]
        public IEnumerator SaveAsync_DifferentProfile_RunsInParallel()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle hA = _SaveKeeper.SaveAsync("profile-A");
            SaveKeeperOperationHandle hB = _SaveKeeper.SaveAsync("profile-B");

            // Both pipelines should reach WriteAsync simultaneously — both blocked at the gate.
            // If cross-profile parallelism is broken, only one would reach WriteAsync at a time.
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 2,
                "both saves did not reach WriteAsync simultaneously (cross-profile parallelism broken)");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(hA);
            yield return WaitForOperation(hB);

            Assert.That(hA.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(hB.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
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

            // Lib trusts caller's metadata as-is (no mutation). Distinct ProfileID values let us
            // identify which meta object actually got serialized into a .meta sidecar write.
            TestSaveMeta meta1 = new() { ProfileID = "meta-1" };
            TestSaveMeta meta2 = new() { ProfileID = "meta-2" };
            TestSaveMeta meta3 = new() { ProfileID = "meta-3" };

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-meta", meta1);
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            // Two coalesced saves with different metadata. meta3 is the last — it should win.
            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-meta", meta2);
            SaveKeeperOperationHandle h3 = _SaveKeeper.SaveAsync("profile-meta", meta3);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);

            // Inspect which TestSaveMeta got serialized into each .meta write.
            List<string> metaProfileIdsWritten = _storage.WriteAsyncCalls
                .Where(c => c.fileName == MetaFileName)
                .Select(c => _serializer.Deserialize<TestSaveMeta>(c.data).ProfileID)
                .ToList();

            Assert.That(metaProfileIdsWritten, Has.Count.EqualTo(2),
                "Coalescing produces exactly 2 meta writes: 1 leading + 1 trailing.");
            Assert.That(metaProfileIdsWritten, Has.Member("meta-1"),
                "Leading pipeline serialized meta1.");
            Assert.That(metaProfileIdsWritten, Has.Member("meta-3"),
                "Trailing pipeline serialized meta3 (last-wins among coalesced).");
            Assert.That(metaProfileIdsWritten, Has.No.Member("meta-2"),
                "meta2 was overwritten by meta3 in PendingMetadata before trailing ran — never serialized.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_PendingHandles_CompleteOnlyAfterTrailing()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-pending");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-pending");
            SaveKeeperOperationHandle h3 = _SaveKeeper.SaveAsync("profile-pending");

            // Coalesced handles must NOT complete while their trailing save is still waiting at the gate.
            Assert.That(h2.IsDone, Is.False, "Pending handle 2 should not complete before trailing save runs.");
            Assert.That(h3.IsDone, Is.False, "Pending handle 3 should not complete before trailing save runs.");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);
            yield return WaitForOperation(h3);

            Assert.That(h1.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h3.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator SaveAsync_ExplicitProfile_DoesNotChangeImplicitTarget()
        {
            // Setup: pre-populate profile-A and load it so _curProfileId = "profile-A".
            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 1 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-A", SaveFileName, serialized);
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            yield return WaitForOperation(_SaveKeeper.LoadAsync("profile-A"));

            // Explicit save to profile-B should NOT change which profile is "current".
            SaveKeeperOperationHandle hExplicit = _SaveKeeper.SaveAsync("profile-B");
            yield return WaitForOperation(hExplicit);

            // Implicit save (null profile) should still target profile-A.
            SaveKeeperOperationHandle hImplicit = _SaveKeeper.SaveAsync();
            yield return WaitForOperation(hImplicit);

            Assert.That(hImplicit.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

            (string profileId, string fileName, string data) lastSave = _storage.WriteAsyncCalls.Last(c => c.fileName == SaveFileName);
            Assert.That(lastSave.profileId, Is.EqualTo("profile-A"),
                "Implicit SaveAsync(null) should target the loaded profile (A), not the most recent explicit profile (B).");
        }

        [UnityTest]
        public IEnumerator SetSimple_DuringSave_TrailingPersistsAllValues()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-setsimple");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            // Hammer SetSimple while the leading save is blocked at the gate.
            // The lock around SimpleData mutations must prevent any "Collection was modified" exceptions
            // when the trailing snapshot's Clone iterates the dictionary.
            for (int i = 0; i < 100; i++)
            {
                _SaveKeeper.SetSimple($"key-{i}", i);
            }

            // Trigger trailing — its capture+snapshot should include all 100 SetSimple values.
            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-setsimple");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            Assert.That(h1.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(h2.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

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
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-conflict", SaveFileName, serialized);

            _storage.HoldWriteAsync();
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Load same profile while save is in flight — must fail.
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-conflict");
            yield return WaitForOperation(loadHandle);

            Assert.That(loadHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
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
            stored.ObjectData["player"] = new TestSaveData { Value = 7 };
            string serialized = _serializer.RegisterSerializedValue(stored);
            _storage.SetPrimaryFile("profile-load-target", SaveFileName, serialized);

            _storage.HoldWriteAsync();
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-being-saved");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Load a DIFFERENT profile — must succeed, no conflict.
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-load-target");
            yield return WaitForOperation(loadHandle);

            Assert.That(loadHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator SaveImmediate_WhileSaveAsyncSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Immediate save for the same profile must throw — cannot wait for async pipeline to drain.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _SaveKeeper.SaveImmediate("profile-conflict"));
            Assert.That(ex.Message, Does.Contain("in flight"));

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator SaveImmediate_WhileSaveAsyncDifferentProfile_Succeeds()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-being-saved");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // SaveImmediate for a different profile must succeed — different state entry.
            Assert.DoesNotThrow(() => _SaveKeeper.SaveImmediate("profile-different"));
            Assert.That(_storage.WriteImmediateCalls.Any(c => c.profileId == "profile-different" && c.fileName == SaveFileName), Is.True);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator DeleteProfile_WhileSavingSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-conflict");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Delete same profile while save is in flight — must throw.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _SaveKeeper.DeleteProfile("profile-conflict"));
            Assert.That(ex.Message, Does.Contain("in flight"));

            // Delete must NOT have happened.
            Assert.That(_storage.DeleteProfileCalls, Is.Empty);

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(saveHandle);
        }

        // ----- Save/Load mutual exclusion (a load claims the profile's pipeline slot) -----

        [UnityTest]
        public IEnumerator SaveAsync_WhileLoadingSameProfile_CompletesFailed()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-load-lock", SaveFileName, _serializer.RegisterSerializedValue(stored));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-load-lock");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 1, "load did not reach ReadAsync");

            // Save the same profile while a load is in flight — must fail (handle Failed), never capture mid-restore.
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-load-lock");
            yield return WaitForOperation(saveHandle);

            Assert.That(saveHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(saveHandle.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(saveHandle.Error.Message, Does.Contain("load operation is in flight"));

            // The rejected save must NOT release the load's slot — the load still completes.
            _storage.ReleaseReadAsync();
            yield return WaitForOperation(loadHandle);
            Assert.That(loadHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded),
                "Load should complete normally after a conflicting save was rejected.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_WhileLoadingDifferentProfile_Succeeds()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-loading", SaveFileName, _serializer.RegisterSerializedValue(stored));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-loading");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 1, "load did not reach ReadAsync");

            // Save a DIFFERENT profile while a load is in flight — no conflict, must succeed.
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-other");
            yield return WaitForOperation(saveHandle);

            Assert.That(saveHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

            _storage.ReleaseReadAsync();
            yield return WaitForOperation(loadHandle);
        }

        [UnityTest]
        public IEnumerator SaveImmediate_WhileLoadingSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-load-lock", SaveFileName, _serializer.RegisterSerializedValue(stored));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-load-lock");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 1, "load did not reach ReadAsync");

            // SaveImmediate for the same profile while a load is in flight — must throw.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _SaveKeeper.SaveImmediate("profile-load-lock"));
            Assert.That(ex.Message, Does.Contain("in flight"));

            _storage.ReleaseReadAsync();
            yield return WaitForOperation(loadHandle);
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhileLoadingSameProfile_CompletesFailed()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-double-load", SaveFileName, _serializer.RegisterSerializedValue(stored));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle firstLoad = _SaveKeeper.LoadAsync("profile-double-load");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 1, "first load did not reach ReadAsync");

            // Second load of the same profile while the first is in flight — must fail.
            SaveKeeperOperationHandle secondLoad = _SaveKeeper.LoadAsync("profile-double-load");
            yield return WaitForOperation(secondLoad);

            Assert.That(secondLoad.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(secondLoad.Error, Is.TypeOf<InvalidOperationException>());
            Assert.That(secondLoad.Error.Message, Does.Contain("another load operation is in flight"));

            // The rejected second load must NOT release the first load's slot.
            _storage.ReleaseReadAsync();
            yield return WaitForOperation(firstLoad);
            Assert.That(firstLoad.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhileLoadingDifferentProfile_Succeeds()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData a = new();
            a.ObjectData["player"] = new TestSaveData { Value = 5 };
            SaveData b = new();
            b.ObjectData["player"] = new TestSaveData { Value = 9 };
            _storage.SetPrimaryFile("profile-A", SaveFileName, _serializer.RegisterSerializedValue(a));
            _storage.SetPrimaryFile("profile-B", SaveFileName, _serializer.RegisterSerializedValue(b));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle loadA = _SaveKeeper.LoadAsync("profile-A");
            SaveKeeperOperationHandle loadB = _SaveKeeper.LoadAsync("profile-B");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 2, "both loads did not reach ReadAsync");

            _storage.ReleaseReadAsync();
            yield return WaitForOperation(loadA);
            yield return WaitForOperation(loadB);

            Assert.That(loadA.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(loadB.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator DeleteProfile_WhileLoadingSameProfile_Throws()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-load-lock", SaveFileName, _serializer.RegisterSerializedValue(stored));

            _storage.HoldReadAsync();
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-load-lock");
            yield return WaitForCondition(() => _storage.ReadAsyncEnteredCount >= 1, "load did not reach ReadAsync");

            // Delete the same profile while a load is in flight — must throw (new: previously only blocked on save).
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => _SaveKeeper.DeleteProfile("profile-load-lock"));
            Assert.That(ex.Message, Does.Contain("in flight"));
            Assert.That(_storage.DeleteProfileCalls, Is.Empty, "Delete must not happen while a load is in flight.");

            _storage.ReleaseReadAsync();
            yield return WaitForOperation(loadHandle);
        }

        [UnityTest]
        public IEnumerator LoadAsync_AfterCompletion_ReleasesSlot_AllowsSave()
        {
            _registry.Register(new TestSavable("player", new TestSaveData { Value = 1 }));

            SaveData stored = new();
            stored.ObjectData["player"] = new TestSaveData { Value = 5 };
            _storage.SetPrimaryFile("profile-seq", SaveFileName, _serializer.RegisterSerializedValue(stored));

            // Load fully completes (no read gate held) → the load slot must be released in the finally.
            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-seq");
            yield return WaitForOperation(loadHandle);
            Assert.That(loadHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));

            // A save for the same profile now succeeds, proving the slot was released.
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-seq");
            yield return WaitForOperation(saveHandle);
            Assert.That(saveHandle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
        }

        // ----- #5: user code (CaptureData/RestoreData) runs OFF _stateLock; capture at call site on the main thread -----

        [Test]
        public void SaveImmediate_WhenCaptureDataThrows_ReleasesSlot()
        {
            ThrowingCaptureSavable thrower = new("player");
            _registry.Register(thrower);

            // CaptureData throws → SaveImmediate (a sync API) propagates it.
            Assert.Throws<CaptureBoomException>(() => _SaveKeeper.SaveImmediate("profile-throw"));

            // ...but the ImmediateSave slot must have been released by the finally block. If it leaked, the next
            // call would throw "... while another save or load operation is in flight" instead of succeeding.
            thrower.ThrowOnCapture = false;
            Assert.DoesNotThrow(() => _SaveKeeper.SaveImmediate("profile-throw"));
        }

        [UnityTest]
        public IEnumerator SaveAsync_WhenCaptureDataThrows_CompletesFailed_DoesNotThrowSynchronously()
        {
            ThrowingCaptureSavable thrower = new("player");
            _registry.Register(thrower);

            // SaveAsync must NOT throw synchronously — the CaptureData error is routed into the handle.
            SaveKeeperOperationHandle handle = default;
            Assert.DoesNotThrow(() => handle = _SaveKeeper.SaveAsync("profile-throw"));
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Failed));
            Assert.That(handle.Error, Is.TypeOf<CaptureBoomException>());

            // Capture runs before the slot is claimed, so no slot leaked — a later save succeeds.
            thrower.ThrowOnCapture = false;
            SaveKeeperOperationHandle ok = _SaveKeeper.SaveAsync("profile-throw");
            yield return WaitForOperation(ok);
            Assert.That(ok.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
        }

        [UnityTest]
        public IEnumerator SaveAsync_CaptureRunsOnMainThread_IncludingCoalesced()
        {
            int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            ThreadRecordingSavable savable = new("player");
            _registry.Register(savable);

            _storage.HoldWriteAsync();
            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-thread");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-thread"); // coalesced — also captures at call site

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            Assert.That(savable.CaptureThreadIds, Is.Not.Empty);
            Assert.That(savable.CaptureThreadIds, Is.All.EqualTo(mainThreadId),
                "Every CaptureData() (leading + coalesced) must run on the main thread — never a threadpool continuation.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_CoalescedCapturesAtCallTime_NotTrailingExecution()
        {
            SnapshottingSavable savable = new("player") { CurrentValue = 1 };
            _registry.Register(savable);
            _storage.HoldWriteAsync();

            SaveKeeperOperationHandle h1 = _SaveKeeper.SaveAsync("profile-calltime"); // leading captures 1
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "leading did not reach WriteAsync");

            savable.CurrentValue = 50;
            SaveKeeperOperationHandle h2 = _SaveKeeper.SaveAsync("profile-calltime"); // coalesced captures 50 at call time
            savable.CurrentValue = 99;                                               // mutate AFTER the coalesced call

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(h1);
            yield return WaitForOperation(h2);

            SaveData lastSaveData = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(lastSaveData, Is.Not.Null);
            Assert.That(((TestSaveData)lastSaveData.ObjectData["player"]).Value, Is.EqualTo(50),
                "Trailing save reflects state captured at the coalesced call (50), not a later mutation (99) nor the leading value (1).");
        }

        [UnityTest]
        public IEnumerator WaitForPendingOperationsAsync_NoSaveInFlight_CompletesImmediately()
        {
            Task waitTask = _SaveKeeper.WaitForPendingOperationsAsync();
            yield return WaitForCondition(() => waitTask.IsCompleted, "WaitForPendingOperationsAsync did not complete when no saves were running.");
            Assert.IsTrue(waitTask.IsCompletedSuccessfully);
        }

        [UnityTest]
        public IEnumerator WaitForPendingOperationsAsync_PendingSave_AwaitsCompletion()
        {
            _storage.HoldWriteAsync();
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-wait");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            Task waitTask = _SaveKeeper.WaitForPendingOperationsAsync();
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
            SaveKeeperOperationHandle hA = _SaveKeeper.SaveAsync("profile-wait-A");
            SaveKeeperOperationHandle hB = _SaveKeeper.SaveAsync("profile-wait-B");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 2, "both saves did not reach WriteAsync simultaneously");

            Task waitTask = _SaveKeeper.WaitForPendingOperationsAsync();
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
            _SaveKeeper.OnSaveCompleted += id => captured = id;

            _SaveKeeper.SaveImmediate("profile-event-sync");

            Assert.AreEqual("profile-event-sync", captured, "OnSaveCompleted should fire with the saved profile ID.");
        }

        [UnityTest]
        public IEnumerator OnSaveCompleted_FiresWithProfileId_AfterSaveAsync()
        {
            string captured = null;
            _SaveKeeper.OnSaveCompleted += id => captured = id;

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-event-async");
            yield return WaitForOperation(handle);

            // The event is marshaled back to the captured SynchronizationContext — give it a frame to deliver.
            yield return WaitForCondition(() => captured != null, "OnSaveCompleted did not fire after SaveAsync completed.");
            Assert.AreEqual("profile-event-async", captured);
        }

        [Test]
        public void AutoSaveCountdown_Resets_WhenSaveImmediateMatchesCurrentProfile()
        {
            SetPrivateField(_SaveKeeper, "_curProfileId", "profile-current");
            SetPrivateField(_SaveKeeper, "_autoSaveCountdown", 42f);

            _SaveKeeper.SaveImmediate("profile-current");

            float countdownAfter = GetPrivateField<float>(_SaveKeeper, "_autoSaveCountdown");
            Assert.That(countdownAfter, Is.EqualTo(_settings.AutoSaveTime).Within(0.01f),
                "Countdown should be reset to AutoSaveTime when the save target matches the current profile.");
        }

        [Test]
        public void AutoSaveCountdown_DoesNotReset_WhenSaveImmediateDifferentProfile()
        {
            SetPrivateField(_SaveKeeper, "_curProfileId", "profile-A");
            SetPrivateField(_SaveKeeper, "_autoSaveCountdown", 42f);

            _SaveKeeper.SaveImmediate("profile-B");

            float countdownAfter = GetPrivateField<float>(_SaveKeeper, "_autoSaveCountdown");
            Assert.That(countdownAfter, Is.EqualTo(42f).Within(0.01f),
                "Countdown must NOT reset when an explicit save targets a different profile.");
        }

        [Test]
        public void AutoSaveTick_AutoSaveDisabled_DoesNotTriggerSave()
        {
            ReplaceSettings(CreateSettings(useEncryption: true, enableAutoSave: false));
            SetPrivateField(_SaveKeeper, "_curProfileId", "profile-disabled");
            SetPrivateField(_SaveKeeper, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_SaveKeeper, 100f);

            Assert.AreEqual(writesBefore, _storage.WriteAsyncEnteredCount, "AutoSaveTick must not trigger a save when EnableAutoSave is false.");
        }

        [Test]
        public void AutoSaveTick_NoCurrentProfile_DoesNotTriggerSave()
        {
            // SetUp creates _settings with EnableAutoSave=true (default) — no need to set it.
            SetPrivateField<string>(_SaveKeeper, "_curProfileId", null);
            SetPrivateField(_SaveKeeper, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_SaveKeeper, 100f);

            Assert.AreEqual(writesBefore, _storage.WriteAsyncEnteredCount, "AutoSaveTick must not trigger a save when no profile is loaded.");
        }

        [UnityTest]
        public IEnumerator AutoSaveTick_CountdownReached_TriggersImplicitSave()
        {
            ReplaceSettings(CreateSettings(useEncryption: true, enableAutoSave: true, autoSaveTime: 1f));
            SetPrivateField(_SaveKeeper, "_curProfileId", "profile-tick");
            SetPrivateField(_SaveKeeper, "_autoSaveCountdown", 0.1f);

            int writesBefore = _storage.WriteAsyncEnteredCount;
            InvokeAutoSaveTick(_SaveKeeper, 1f);

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
            Assert.DoesNotThrow(() => _SaveKeeper.Dispose());
            Assert.DoesNotThrow(() => _SaveKeeper.Dispose(), "Dispose must be safe to call twice.");

            // Prevent TearDown from disposing again on the already-disposed instance.
            _SaveKeeper = null;
        }

        [Test]
        public void OperationInternal_CompleteTwice_StatusNotOverwritten()
        {
            (object op, MethodInfo complete, PropertyInfo status, _) = CreateNonGenericOp();

            complete.Invoke(op, new object[] { null });                                   // first: success
            object statusAfterFirst = status.GetValue(op);

            complete.Invoke(op, new object[] { new InvalidOperationException("late") }); // attempt to fail
            object statusAfterSecond = status.GetValue(op);

            Assert.AreEqual(statusAfterFirst, statusAfterSecond,
                "Calling Complete twice must not change Status — first call wins.");
        }

        [Test]
        public void OperationInternal_CompleteTwice_ErrorPreserved()
        {
            (object op, MethodInfo complete, _, PropertyInfo error) = CreateNonGenericOp();

            Exception firstError = new InvalidOperationException("first");
            complete.Invoke(op, new object[] { firstError });
            complete.Invoke(op, new object[] { null });  // attempt to mark as success

            Assert.AreSame(firstError, error.GetValue(op),
                "Calling Complete twice must not overwrite the original Error.");
        }

        [Test]
        public void OperationInternal_CompleteTwice_ContinuationFiresOnce()
        {
            (object op, MethodInfo complete, _, _) = CreateNonGenericOp();
            Type opType = op.GetType();

            int callCount = 0;
            EventInfo continuationEvent = opType.GetEvent("ContinuationAction");
            Action handler = () => callCount++;
            continuationEvent.AddEventHandler(op, handler);

            complete.Invoke(op, new object[] { null });
            complete.Invoke(op, new object[] { null });

            Assert.AreEqual(1, callCount,
                "ContinuationAction must fire exactly once across multiple Complete calls.");
        }

        [Test]
        public void OperationInternalGeneric_CompleteTwice_ResultPreserved()
        {
            Type openType = typeof(SaveKeeperRuntime).Assembly
                .GetType("ThanhDV.SaveKeeper.Core.SaveKeeperOperationInternal`1");
            Type closedType = openType.MakeGenericType(typeof(int));

            object op = Activator.CreateInstance(closedType);
            MethodInfo complete = closedType.GetMethod("Complete", new[] { typeof(int), typeof(Exception) });
            PropertyInfo resultProp = closedType.GetProperty("Result");

            complete.Invoke(op, new object[] { 42, null });                                    // first: success with 42
            complete.Invoke(op, new object[] { 999, new InvalidOperationException("late") }); // attempt to overwrite

            int result = (int)resultProp.GetValue(op);
            Assert.AreEqual(42, result,
                "Generic Complete called twice must preserve the first result.");
        }

        [Test]
        public void SetSimple_NullReferenceValue_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _SaveKeeper.SetSimple<string>("name", null),
                "Storing null must not throw — it represents an explicit 'no value', distinct from missing.");
        }

        [Test]
        public void GetSimple_AfterStoredNull_ReturnsDefault_NotSuppliedDefaultValue()
        {
            _SaveKeeper.SetSimple<string>("name", null);
            string result = _SaveKeeper.GetSimple<string>("name", defaultValue: "fallback");

            Assert.IsNull(result,
                "Stored-null returns default(T) (null for reference types) — distinct from missing key which returns supplied defaultValue.");
        }

        [Test]
        public void GetSimple_MissingKey_ReturnsSuppliedDefaultValue()
        {
            string result = _SaveKeeper.GetSimple<string>("never-set-key", defaultValue: "fallback");

            Assert.AreEqual("fallback", result,
                "Missing key must return the caller-supplied defaultValue.");
        }

        [Test]
        public void HasSimpleKey_AfterStoredNull_ReturnsTrue()
        {
            _SaveKeeper.SetSimple<string>("name", null);

            Assert.IsTrue(_SaveKeeper.HasSimpleKey("name"),
                "HasSimpleKey must return true for keys with stored null — they are present, just empty.");
        }

        [Test]
        public void SetSimple_NullableInt_RoundTripsNull()
        {
            _SaveKeeper.SetSimple<int?>("score", null);
            int? result = _SaveKeeper.GetSimple<int?>("score", defaultValue: 99);

            Assert.IsNull(result,
                "Nullable<int> null must round-trip as null, not as supplied defaultValue.");
        }

        [Test]
        public void SetSimple_OverwriteValueWithNull_GetReturnsNull()
        {
            _SaveKeeper.SetSimple<string>("name", "Alice");
            _SaveKeeper.SetSimple<string>("name", null);  // overwrite

            string result = _SaveKeeper.GetSimple<string>("name", defaultValue: "fallback");
            Assert.IsNull(result,
                "Overwriting a value with null must yield null on read, not the previous value or defaultValue.");
        }

        [Test]
        public void SetSimple_ValueType_StoresAndReadsBack()
        {
            // Value type (int) cannot be null — must follow the normal serialize path.
            _SaveKeeper.SetSimple<int>("coins", 100);
            int result = _SaveKeeper.GetSimple<int>("coins", defaultValue: 0);
            Assert.AreEqual(100, result);
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingSavableKey_RestoreDataNotCalled()
        {
            // Save a profile with no savables registered → save file has empty ObjectData.
            _SaveKeeper.SaveImmediate("empty-profile");

            // Register a savable AFTER the save. Its key has no entry in the persisted file.
            TestSavable lateSavable = new("late-key", new TestSaveData { Value = 99 });
            _registry.Register(lateSavable);
            int restoreCountAfterRegister = lateSavable.RestoreCallCount;

            yield return WaitForOperation(_SaveKeeper.LoadAsync("empty-profile"));

            Assert.AreEqual(restoreCountAfterRegister, lateSavable.RestoreCallCount,
                "Load must NOT invoke RestoreData when the savable's key has no entry in the save file.");
        }

        [UnityTest]
        public IEnumerator LoadAsync_ExistingSavableKey_RestoreDataCalled()
        {
            // Set up: register a savable, save → save file contains data for this key.
            TestSavable savable = new("present-key", new TestSaveData { Value = 42 });
            _registry.Register(savable);
            _SaveKeeper.SaveImmediate("populated");

            int restoreCountBefore = savable.RestoreCallCount;

            yield return WaitForOperation(_SaveKeeper.LoadAsync("populated"));

            Assert.Greater(savable.RestoreCallCount, restoreCountBefore,
                "Load must invoke RestoreData when the savable's key has an entry in the save file.");
            Assert.IsNotNull(savable.LastRestoredData);
        }

        // ===================================================================
        // Cụm #5 — IsSimpleDataDirty tracking + LoadAsync discardUnsavedChanges
        // ===================================================================

        [Test]
        public void IsSimpleDataDirty_AfterConstruction_IsFalse()
        {
            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [Test]
        public void SetSimple_SetsDirtyFlag()
        {
            _SaveKeeper.SetSimple("coins", 100);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty);
        }

        [Test]
        public void DeleteSimple_ExistingKey_SetsDirtyFlag()
        {
            _SaveKeeper.SetSimple("coins", 100);
            _SaveKeeper.SaveImmediate("profile-prep");  // clear dirty via save
            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty, "Sanity: save should clear dirty.");

            _SaveKeeper.DeleteSimple("coins");

            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty);
        }

        [Test]
        public void DeleteSimple_NonExistingKey_DoesNotSetDirtyFlag()
        {
            _SaveKeeper.DeleteSimple("never-set-key");

            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty,
                "Deleting a key that doesn't exist leaves state unchanged — must not mark dirty.");
        }

        [Test]
        public void SaveImmediate_ClearsDirtyFlag()
        {
            _SaveKeeper.SetSimple("coins", 100);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            _SaveKeeper.SaveImmediate("profile-clear");

            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [UnityTest]
        public IEnumerator SaveAsync_ClearsDirtyFlag()
        {
            _SaveKeeper.SetSimple("coins", 100);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            yield return WaitForOperation(_SaveKeeper.SaveAsync("profile-clear-async"));

            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [UnityTest]
        public IEnumerator LoadAsync_ClearsDirtyFlag()
        {
            _SaveKeeper.SaveImmediate("profile-load-target");
            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            yield return WaitForOperation(_SaveKeeper.LoadAsync("profile-load-target", discardUnsavedChanges: true));

            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [Test]
        public void DeleteProfile_CurrentProfile_ClearsDirtyFlag()
        {
            SetPrivateField(_SaveKeeper, "_curProfileId", "current-profile");
            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            _SaveKeeper.DeleteProfile("current-profile");

            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [Test]
        public void DeleteProfile_OtherProfile_PreservesDirtyFlag()
        {
            SetPrivateField(_SaveKeeper, "_curProfileId", "current-profile");
            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            _SaveKeeper.DeleteProfile("other-profile");

            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty,
                "Deleting an unrelated profile must not touch the current state's dirty flag.");
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhenDirty_WithoutDiscardFlag_HandleCompletesFailed()
        {
            _SaveKeeper.SaveImmediate("profile-target");
            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-target");
            yield return WaitForOperation(handle);

            Assert.AreEqual(SaveKeeperOperationStatus.Failed, handle.Status);
            Assert.IsInstanceOf<InvalidOperationException>(handle.Error);
            Assert.That(handle.Error.Message, Does.Contain("unsaved").IgnoreCase);
        }

        [UnityTest]
        public IEnumerator LoadAsync_WhenDirty_WithDiscardFlag_HandleCompletesSucceeded()
        {
            _SaveKeeper.SaveImmediate("profile-target");
            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-target", discardUnsavedChanges: true);
            yield return WaitForOperation(handle);

            Assert.AreEqual(SaveKeeperOperationStatus.Succeeded, handle.Status);
            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty);
        }

        [UnityTest]
        public IEnumerator LoadAsync_NotDirty_HandleCompletesSucceeded()
        {
            _SaveKeeper.SaveImmediate("profile-target");
            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("profile-target");
            yield return WaitForOperation(handle);

            Assert.AreEqual(SaveKeeperOperationStatus.Succeeded, handle.Status);
        }

        [UnityTest]
        public IEnumerator LoadBackupAsync_WhenDirty_WithoutDiscardFlag_HandleCompletesFailed()
        {
            SaveData backupData = new();
            string serialized = _serializer.RegisterSerializedValue(backupData);
            _storage.SetBackupFile("profile-backup", SaveFileName, serialized);

            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadBackupAsync("profile-backup");
            yield return WaitForOperation(handle);

            Assert.AreEqual(SaveKeeperOperationStatus.Failed, handle.Status);
            Assert.IsInstanceOf<InvalidOperationException>(handle.Error);
        }

        [UnityTest]
        public IEnumerator LoadMostRecentAsync_WhenDirty_WithoutDiscardFlag_HandleCompletesFailed()
        {
            SaveData data = new();
            string serialized = _serializer.RegisterSerializedValue(data);
            _storage.SetPrimaryFile("recent", SaveFileName, serialized);
            _storage.MostRecentProfileId = "recent";

            _SaveKeeper.SetSimple("k", 1);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty, "Sanity precondition.");

            SaveKeeperOperationHandle handle = _SaveKeeper.LoadMostRecentAsync();
            yield return WaitForOperation(handle);

            Assert.AreEqual(SaveKeeperOperationStatus.Failed, handle.Status);
            Assert.IsInstanceOf<InvalidOperationException>(handle.Error);
        }

        [UnityTest]
        public IEnumerator SetSimple_BetweenCaptureAndPipelineEnd_RemarksDirty()
        {
            // Verifies the timing semantic: dirty is cleared AT CAPTURE time, not at pipeline end.
            // Any SetSimple between capture and pipeline-completion must re-mark dirty — those changes
            // are not in the snapshot being written.

            _SaveKeeper.SetSimple("before-capture", 1);

            _storage.HoldWriteAsync();
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-race");
            yield return WaitForCondition(() => _storage.WriteAsyncEnteredCount >= 1, "save did not reach WriteAsync");

            // Capture already happened inside the lock → dirty should be cleared by now.
            Assert.IsFalse(_SaveKeeper.IsSimpleDataDirty,
                "Dirty must be cleared at capture time (inside the snapshot lock).");

            // SetSimple AFTER capture: change not in snapshot → must re-mark dirty.
            _SaveKeeper.SetSimple("after-capture", 2);
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty,
                "SetSimple after capture must re-mark dirty — its change is not in the snapshot being written.");

            _storage.ReleaseWriteAsync();
            yield return WaitForOperation(handle);

            // After save completes, the post-capture SetSimple still represents an unsaved change.
            Assert.IsTrue(_SaveKeeper.IsSimpleDataDirty,
                "After save completes, the post-capture change remains as unsaved state.");
        }

        // ===================================================================
        // Cluster #7 — Atomicity .sav/.meta + sidecar cache + mtime self-heal
        // ===================================================================

        [UnityTest]
        public IEnumerator SaveAsync_WithoutMetadata_AlwaysWritesMetaSidecar()
        {
            // Cluster #7: meta sidecar is always written (uses DefaultSaveMeta if metadata=null).
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-default-meta");
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(_storage.WriteAsyncCalls, Has.Count.EqualTo(2));
            Assert.That(_storage.WriteAsyncCalls[0].fileName, Is.EqualTo(SaveFileName), ".sav must be written first (source of truth).");
            Assert.That(_storage.WriteAsyncCalls[1].fileName, Is.EqualTo(MetaFileName), ".meta sidecar must be written after .sav.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_WithoutMetadata_WritesDefaultSaveMetaWithLibraryFields()
        {
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-default-fields");
            yield return WaitForOperation(handle);

            // Last DefaultSaveMeta seen by the serializer is what was written into the .meta sidecar.
            DefaultSaveMeta lastMetaWritten = _serializer.GetLastSerializedObject<DefaultSaveMeta>();
            Assert.That(lastMetaWritten, Is.Not.Null, "Default meta must be serialized when caller passes null.");
            Assert.That(lastMetaWritten.ProfileID, Is.EqualTo("profile-default-fields"));
            Assert.That(lastMetaWritten.LastTimeSaved, Is.Not.EqualTo(default(DateTime)),
                "DefaultSaveMeta.LastTimeSaved must be populated with current UTC time.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_WithCustomMetadata_PreservesCallerFieldsAsIs()
        {
            // Design philosophy: lib does not mutate caller's metadata. Caller is responsible for
            // setting ProfileID and LastTimeSaved correctly. If they pass wrong data, that's their bug
            // (principle of least surprise — what you pass is what gets saved).
            DateTime callerTime = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            TestSaveMeta provided = new() { ProfileID = "caller-set-id", LastTimeSaved = callerTime };

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-target", provided);
            yield return WaitForOperation(handle);

            Assert.That(provided.ProfileID, Is.EqualTo("caller-set-id"),
                "Lib must not overwrite caller's ProfileID — even when it mismatches the target profile.");
            Assert.That(provided.LastTimeSaved, Is.EqualTo(callerTime),
                "Lib must not overwrite caller's LastTimeSaved.");
        }

        [UnityTest]
        public IEnumerator SaveAsync_EmbedsMetaIntoSaveData_SoSavContainsMetaHeader()
        {
            // Caller pre-fills meta (lib doesn't override). Test verifies the .sav serialized output
            // contains caller's meta inside SaveData.Meta — i.e. cluster #7's single-file design.
            TestSaveMeta provided = new() { ProfileID = "profile-embed", LastTimeSaved = DateTime.UtcNow };
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("profile-embed", provided);
            yield return WaitForOperation(handle);

            SaveData savContent = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(savContent, Is.Not.Null);
            Assert.That(savContent.Meta, Is.Not.Null, "SaveData.Meta must be populated before serialize.");
            Assert.That(savContent.Meta.ProfileID, Is.EqualTo("profile-embed"), "Meta embedded into .sav matches caller's data.");
        }

        [UnityTest]
        public IEnumerator LoadAsync_ThenSave_NewMetaReplacesLoadedMeta()
        {
            // Pre-write .sav with embedded meta (simulating a prior save session).
            TestSaveMeta savedMeta = new() { ProfileID = "profile-restore-meta", LastTimeSaved = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
            SaveData saved = new() { Meta = savedMeta };
            string serialized = _serializer.RegisterSerializedValue(saved);
            _storage.SetPrimaryFile("profile-restore-meta", SaveFileName, serialized);

            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("profile-restore-meta");
            yield return WaitForOperation(loadHandle);

            // After Load, re-save with a NEW caller-provided meta. Lib doesn't merge — caller's meta replaces.
            TestSaveMeta newMeta = new() { ProfileID = "profile-restore-meta", LastTimeSaved = DateTime.UtcNow };
            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("profile-restore-meta", newMeta);
            yield return WaitForOperation(saveHandle);

            // The .sav written must contain newMeta (caller's value), not the loaded savedMeta.
            SaveData savContent = _serializer.GetLastSerializedObject<SaveData>();
            Assert.That(savContent.Meta, Is.InstanceOf<TestSaveMeta>());
            Assert.That(savContent.Meta.ProfileID, Is.EqualTo("profile-restore-meta"));
            Assert.That(savContent.Meta.LastTimeSaved, Is.Not.EqualTo(savedMeta.LastTimeSaved),
                "New caller-supplied meta replaces previously loaded meta (α semantics).");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_FreshSidecar_FastPath_DoesNotReadSav()
        {
            // Setup: both .sav and .meta written via SetPrimaryFile — .meta written second so meta.mtime > sav.mtime → fresh.
            TestSaveMeta meta = new() { ProfileID = "profile-fast", LastTimeSaved = DateTime.UtcNow };
            SaveData sav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-fast", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.SetPrimaryFile("profile-fast", MetaFileName, _serializer.RegisterSerializedValue(meta));
            _storage.ProfileIds = new List<string> { "profile-fast" };

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(handle.Result, Has.Count.EqualTo(1));

            int savReads = _storage.ReadAsyncCalls.Count(c => c.fileName == SaveFileName);
            int metaReads = _storage.ReadAsyncCalls.Count(c => c.fileName == MetaFileName);
            Assert.That(savReads, Is.EqualTo(0), "Fast path must NOT read .sav when sidecar is fresh.");
            Assert.That(metaReads, Is.EqualTo(1), "Fast path reads .meta sidecar exactly once per profile.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_StaleMeta_RebuildsFromSav()
        {
            // Set both files, then simulate post-crash: .sav.mtime newer than .meta.mtime.
            TestSaveMeta meta = new() { ProfileID = "profile-stale", LastTimeSaved = DateTime.UtcNow };
            SaveData sav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-stale", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.SetPrimaryFile("profile-stale", MetaFileName, _serializer.RegisterSerializedValue(meta));
            _storage.SetMtime("profile-stale", MetaFileName, DateTime.UtcNow.AddMinutes(-5));
            _storage.SetMtime("profile-stale", SaveFileName, DateTime.UtcNow);
            _storage.ProfileIds = new List<string> { "profile-stale" };

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(handle.Result, Has.Count.EqualTo(1), "Stale-then-rebuild path must still return metadata.");

            int savReads = _storage.ReadAsyncCalls.Count(c => c.fileName == SaveFileName);
            Assert.That(savReads, Is.EqualTo(1), "Stale path must read .sav to rebuild metadata.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_AfterRebuild_SelfHealsSidecar()
        {
            // Setup stale meta — same as previous test.
            TestSaveMeta meta = new() { ProfileID = "profile-heal", LastTimeSaved = DateTime.UtcNow };
            SaveData sav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-heal", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.SetPrimaryFile("profile-heal", MetaFileName, _serializer.RegisterSerializedValue(meta));
            _storage.SetMtime("profile-heal", MetaFileName, DateTime.UtcNow.AddMinutes(-5));
            _storage.SetMtime("profile-heal", SaveFileName, DateTime.UtcNow);
            _storage.ProfileIds = new List<string> { "profile-heal" };

            int writesBefore = _storage.WriteAsyncCalls.Count;

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            int writesAfter = _storage.WriteAsyncCalls.Count;
            int metaWrites = _storage.WriteAsyncCalls.Skip(writesBefore).Count(c => c.fileName == MetaFileName && c.profileId == "profile-heal");

            Assert.That(metaWrites, Is.EqualTo(1), "Self-heal must write a fresh .meta sidecar after rebuilding from .sav.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_MissingMeta_RebuildsFromSav()
        {
            // Only .sav exists, no .meta sidecar at all → triggers rebuild.
            TestSaveMeta meta = new() { ProfileID = "profile-no-meta", LastTimeSaved = DateTime.UtcNow };
            SaveData sav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-no-meta", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.ProfileIds = new List<string> { "profile-no-meta" };

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            Assert.That(handle.Result, Has.Count.EqualTo(1));
            Assert.That(handle.Result[0].ProfileID, Is.EqualTo("profile-no-meta"));
            Assert.That(_storage.ReadAsyncCalls.Count(c => c.fileName == SaveFileName), Is.EqualTo(1),
                "Missing-meta path must read .sav to rebuild.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_CorruptMetaSidecar_FallsThroughToRebuild()
        {
            // Bug 2 regression test: with throw; removed from catch, corrupt sidecar must fall through to rebuild.
            TestSaveMeta meta = new() { ProfileID = "profile-corrupt-meta", LastTimeSaved = DateTime.UtcNow };
            SaveData sav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-corrupt-meta", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.SetPrimaryFile("profile-corrupt-meta", MetaFileName, "corrupt-json");
            _serializer.RegisterDeserializeException("corrupt-json", new FormatException("corrupt"));
            _storage.ProfileIds = new List<string> { "profile-corrupt-meta" };

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded),
                "Corrupt sidecar must not fail the whole list — should fall through to rebuild from .sav.");
            Assert.That(handle.Result, Has.Count.EqualTo(1));
            Assert.That(handle.Result[0].ProfileID, Is.EqualTo("profile-corrupt-meta"));
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_OneCorruptProfile_DoesNotFailOtherProfiles()
        {
            // Bug 2 regression test: corruption in one profile must not abort the entire list call.
            // Profile A: corrupt meta + corrupt sav (truly skipped).
            _storage.SetPrimaryFile("profile-broken", SaveFileName, "corrupt-sav");
            _storage.SetPrimaryFile("profile-broken", MetaFileName, "corrupt-meta");
            _serializer.RegisterDeserializeException("corrupt-sav", new FormatException("bad sav"));
            _serializer.RegisterDeserializeException("corrupt-meta", new FormatException("bad meta"));

            // Profile B: fully valid.
            TestSaveMeta goodMeta = new() { ProfileID = "profile-ok", LastTimeSaved = DateTime.UtcNow };
            SaveData goodSav = new() { Meta = goodMeta };
            _storage.SetPrimaryFile("profile-ok", SaveFileName, _serializer.RegisterSerializedValue(goodSav));
            _storage.SetPrimaryFile("profile-ok", MetaFileName, _serializer.RegisterSerializedValue(goodMeta));

            _storage.ProfileIds = new List<string> { "profile-broken", "profile-ok" };

            // Expect warnings/errors logged for the broken profile but no fatal failure.
            LogAssert.ignoreFailingMessages = true;

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            LogAssert.ignoreFailingMessages = false;

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded),
                "List call must succeed overall even when one profile is unrecoverable.");
            Assert.That(handle.Result.Select(m => m.ProfileID), Is.EquivalentTo(new[] { "profile-ok" }),
                "Valid profiles must still be returned; broken profile silently skipped.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_CorruptSav_FallsBackToSavBackup()
        {
            // .sav corrupt, .sav.bak valid → rebuild from backup.
            TestSaveMeta meta = new() { ProfileID = "profile-bak", LastTimeSaved = DateTime.UtcNow };
            SaveData backupSav = new() { Meta = meta };
            _storage.SetPrimaryFile("profile-bak", SaveFileName, "corrupt-primary-sav");
            _storage.SetBackupFile("profile-bak", SaveFileName, _serializer.RegisterSerializedValue(backupSav));
            _serializer.RegisterDeserializeException("corrupt-primary-sav", new FormatException("bad primary"));
            // No .meta sidecar at all → goes straight to rebuild path.
            _storage.ProfileIds = new List<string> { "profile-bak" };

            LogAssert.ignoreFailingMessages = true;
            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);
            LogAssert.ignoreFailingMessages = false;

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(handle.Result, Has.Count.EqualTo(1));
            Assert.That(handle.Result[0].ProfileID, Is.EqualTo("profile-bak"),
                "When primary .sav fails, lib must fall back to .sav.bak for metadata rebuild.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_BothSavPrimaryAndBackupCorrupt_SkipsProfile()
        {
            _storage.SetPrimaryFile("profile-dead", SaveFileName, "corrupt-primary");
            _storage.SetBackupFile("profile-dead", SaveFileName, "corrupt-backup");
            _serializer.RegisterDeserializeException("corrupt-primary", new FormatException("bad p"));
            _serializer.RegisterDeserializeException("corrupt-backup", new FormatException("bad b"));
            _storage.ProfileIds = new List<string> { "profile-dead" };

            LogAssert.ignoreFailingMessages = true;
            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);
            LogAssert.ignoreFailingMessages = false;

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(handle.Result, Is.Empty,
                "When both primary and backup .sav are unreadable, profile is silently skipped from the list.");
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_TypeMismatch_SkipsProfile()
        {
            // Profile saved with DefaultSaveMeta but caller queries TestSaveMeta → cast fails → null → skipped.
            DefaultSaveMeta savedMeta = new("profile-mismatch", DateTime.UtcNow);
            SaveData sav = new() { Meta = savedMeta };
            _storage.SetPrimaryFile("profile-mismatch", SaveFileName, _serializer.RegisterSerializedValue(sav));
            _storage.SetPrimaryFile("profile-mismatch", MetaFileName, _serializer.RegisterSerializedValue(savedMeta));
            _storage.ProfileIds = new List<string> { "profile-mismatch" };

            // Force rebuild path so the cast goes through saveData.Meta as T.
            _storage.SetMtime("profile-mismatch", MetaFileName, DateTime.UtcNow.AddMinutes(-5));
            _storage.SetMtime("profile-mismatch", SaveFileName, DateTime.UtcNow);

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded));
            Assert.That(handle.Result, Is.Empty,
                "DefaultSaveMeta is not castable to TestSaveMeta → profile skipped without error.");
        }

        [Test]
        public void DefaultSaveMeta_HasParameterlessConstructor()
        {
            // Bug 1 regression: Newtonsoft deserialization requires a parameterless ctor.
            // This test simply asserts the ctor exists and that DefaultSaveMeta is instantiable that way.
            DefaultSaveMeta meta = new();
            Assert.That(meta, Is.Not.Null);
            Assert.That(meta.ProfileID, Is.Null, "Parameterless ctor leaves ProfileID as default.");
            Assert.That(meta.LastTimeSaved, Is.EqualTo(default(DateTime)), "Parameterless ctor leaves LastTimeSaved as default.");
        }

        // ===================================================================
        // Cluster #15 — Clock skew detection (Option C: detect + log warning)
        // ===================================================================

        [UnityTest]
        public IEnumerator Save_FirstEverCall_UpdatesLastObservedTimeBaseline()
        {
            // Fresh SaveKeeper — baseline starts at DateTime.MinValue.
            DateTime beforeSave = DateTime.UtcNow;

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("p-baseline");
            yield return WaitForOperation(handle);

            DateTime baseline = GetPrivateField<DateTime>(_SaveKeeper, "_lastObservedTime");
            Assert.That(baseline, Is.GreaterThanOrEqualTo(beforeSave),
                "After save, _lastObservedTime must reflect the clock time the save was performed at.");
        }

        [UnityTest]
        public IEnumerator Save_BaselineInFutureBeyondThreshold_LogsClockSkewWarning()
        {
            // Simulate prior observation of a future timestamp (e.g., loaded from a save written
            // before user manually changed clock back).
            DateTime futureBaseline = DateTime.UtcNow.AddSeconds(5);
            SetPrivateField(_SaveKeeper, "_lastObservedTime", futureBaseline);

            // Lib's next clock read (DateTime.UtcNow) is ~5s less than baseline → trigger warning.
            LogAssert.Expect(LogType.Log, new Regex(".*Clock moved backward.*"));

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("p-skew");
            yield return WaitForOperation(handle);

            Assert.That(handle.Status, Is.EqualTo(SaveKeeperOperationStatus.Succeeded),
                "Clock skew warning does NOT fail the save — just logs.");
        }

        [UnityTest]
        public IEnumerator Save_BaselineInFutureBelowThreshold_BaselinePreserved()
        {
            // Skew of 200ms — below 1s threshold, no warning.
            DateTime smallSkewBaseline = DateTime.UtcNow.AddMilliseconds(200);
            SetPrivateField(_SaveKeeper, "_lastObservedTime", smallSkewBaseline);

            // No LogAssert.Expect — we don't expect any clock skew warning.
            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("p-small-skew");
            yield return WaitForOperation(handle);

            DateTime baselineAfter = GetPrivateField<DateTime>(_SaveKeeper, "_lastObservedTime");
            // Since now < baseline, baseline stays at the future value (lib only updates baseline upward).
            Assert.That(baselineAfter, Is.EqualTo(smallSkewBaseline),
                "Baseline tracks the max observed; small backward skew (below threshold) keeps baseline unchanged.");
        }

        [UnityTest]
        public IEnumerator Load_FutureMetaTimestamp_UpdatesBaselineWithoutWarning()
        {
            // Pre-write .sav with meta.LastTimeSaved in the future (could happen if previous session
            // had a different clock).
            DateTime futureTime = DateTime.UtcNow.AddHours(1);
            TestSaveMeta futureMeta = new() { ProfileID = "p-future", LastTimeSaved = futureTime };
            SaveData saved = new() { Meta = futureMeta };
            _storage.SetPrimaryFile("p-future", SaveFileName, _serializer.RegisterSerializedValue(saved));

            // Load is "observe external timestamp" semantics — observing past saves is normal,
            // even if their stored time is in our future. Lib should NOT warn here.
            SaveKeeperOperationHandle handle = _SaveKeeper.LoadAsync("p-future");
            yield return WaitForOperation(handle);

            DateTime baseline = GetPrivateField<DateTime>(_SaveKeeper, "_lastObservedTime");
            Assert.That(baseline, Is.EqualTo(futureTime),
                "Load must establish baseline from loaded meta's LastTimeSaved, without warning.");
        }

        [UnityTest]
        public IEnumerator LoadThenSave_FutureBaselineFromLoad_NextSaveLogsSkewWarning()
        {
            // Cross-session scenario: previous session saved with future time; current session
            // loads (sets baseline to future), then saves → save's UtcNow < baseline → warn.
            DateTime futureTime = DateTime.UtcNow.AddHours(1);
            TestSaveMeta futureMeta = new() { ProfileID = "p-cross", LastTimeSaved = futureTime };
            SaveData saved = new() { Meta = futureMeta };
            _storage.SetPrimaryFile("p-cross", SaveFileName, _serializer.RegisterSerializedValue(saved));

            SaveKeeperOperationHandle loadHandle = _SaveKeeper.LoadAsync("p-cross");
            yield return WaitForOperation(loadHandle);

            // Now save again — baseline is 1 hour in future, current UtcNow is ~now → skew detected.
            LogAssert.Expect(LogType.Log, new Regex(".*Clock moved backward.*"));

            SaveKeeperOperationHandle saveHandle = _SaveKeeper.SaveAsync("p-cross", new TestSaveMeta { ProfileID = "p-cross", LastTimeSaved = DateTime.UtcNow });
            yield return WaitForOperation(saveHandle);
        }

        [UnityTest]
        public IEnumerator GetAllMetadataAsync_ObservesTimestamps_UpdatesBaselineToMax()
        {
            DateTime oldTime = DateTime.UtcNow.AddMinutes(-10);
            DateTime newestTime = DateTime.UtcNow.AddHours(2);

            TestSaveMeta oldMeta = new() { ProfileID = "p-old", LastTimeSaved = oldTime };
            TestSaveMeta newMeta = new() { ProfileID = "p-new", LastTimeSaved = newestTime };
            SaveData oldSav = new() { Meta = oldMeta };
            SaveData newSav = new() { Meta = newMeta };

            _storage.ProfileIds = new List<string> { "p-old", "p-new" };
            _storage.SetPrimaryFile("p-old", SaveFileName, _serializer.RegisterSerializedValue(oldSav));
            _storage.SetPrimaryFile("p-old", MetaFileName, _serializer.RegisterSerializedValue(oldMeta));
            _storage.SetPrimaryFile("p-new", SaveFileName, _serializer.RegisterSerializedValue(newSav));
            _storage.SetPrimaryFile("p-new", MetaFileName, _serializer.RegisterSerializedValue(newMeta));

            SaveKeeperOperationHandle<List<TestSaveMeta>> handle = _SaveKeeper.GetAllMetadataAsync<TestSaveMeta>();
            yield return WaitForOperation(handle);

            DateTime baseline = GetPrivateField<DateTime>(_SaveKeeper, "_lastObservedTime");
            Assert.That(baseline, Is.EqualTo(newestTime),
                "ListMetadata must observe all timestamps and set baseline to the max — no false-positive warnings on past times.");
        }

        [UnityTest]
        public IEnumerator Save_LargeForwardJump_NoWarning_BaselineMovesForward()
        {
            // Simulate clock JUMPING FORWARD (NTP correction after long offline period, daylight saving, etc.).
            // Forward jump is NOT skew — baseline simply updates upward, no warning.
            DateTime pastBaseline = DateTime.UtcNow.AddHours(-2);
            SetPrivateField(_SaveKeeper, "_lastObservedTime", pastBaseline);

            SaveKeeperOperationHandle handle = _SaveKeeper.SaveAsync("p-forward");
            yield return WaitForOperation(handle);

            DateTime baselineAfter = GetPrivateField<DateTime>(_SaveKeeper, "_lastObservedTime");
            Assert.That(baselineAfter, Is.GreaterThan(pastBaseline),
                "Forward time movement is normal; baseline moves upward without warning.");
        }

        private static (object op, MethodInfo complete, PropertyInfo status, PropertyInfo error) CreateNonGenericOp()
        {
            Type opType = typeof(SaveKeeperRuntime).Assembly
                .GetType("ThanhDV.SaveKeeper.Core.SaveKeeperOperationInternal");
            object op = Activator.CreateInstance(opType);
            MethodInfo complete = opType.GetMethod("Complete", new[] { typeof(Exception) });
            PropertyInfo status = opType.GetProperty("Status");
            PropertyInfo error = opType.GetProperty("Error");
            return (op, complete, status, error);
        }

        private static string SaveFileName => "slot.sav";
        private static string MetaFileName => "slot.meta";

        private void RecreateSaveKeeper(bool useEncryption)
        {
            ReplaceSettings(CreateSettings(useEncryption));
        }

        /// <summary>
        /// Replaces <see cref="_settings"/> with the given instance and rebuilds <see cref="_SaveKeeper"/>.
        /// Used by tests that need to vary settings the SUT was constructed with (settings is immutable post-construction).
        /// </summary>
        private void ReplaceSettings(SaveSettings newSettings)
        {
            _SaveKeeper.Dispose();
            _settings = newSettings;
            _SaveKeeper = new SaveKeeperRuntime(_registry, _storage, _serializer, _encryption, _settings);
        }

        private static SaveSettings CreateSettings(
            bool useEncryption,
            bool enableAutoSave = true,
            float autoSaveTime = 300f) => new()
        {
            UseEncryption = useEncryption,
            EnableAutoSave = enableAutoSave,
            AutoSaveTime = autoSaveTime,
            FileName = "slot",
            SaveExtension = ".sav",
            MetaExtension = ".meta",
        };

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

        private static void InvokeAutoSaveTick(SaveKeeperRuntime SaveKeeper, float deltaTime)
        {
            MethodInfo method = typeof(SaveKeeperRuntime).GetMethod("AutoSaveTick", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(SaveKeeper, new object[] { deltaTime });
        }

        private static IEnumerator WaitForOperation(SaveKeeperOperationHandle handle, float timeoutSeconds = 1f)
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

        private static IEnumerator WaitForOperation<T>(SaveKeeperOperationHandle<T> handle, float timeoutSeconds = 1f)
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
            private readonly Dictionary<(string profileId, string fileName), DateTime> _mtimes = new();

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

            // Concurrency test infrastructure: a gate that holds ReadAsync/ReadBackupAsync calls until released.
            // Use HoldReadAsync/ReleaseReadAsync to park a load mid-flight (the read happens before _curProfileId/restore).
            private TaskCompletionSource<bool> _readAsyncGate;
            private int _readAsyncEnteredCount;

            public int ReadAsyncEnteredCount => _readAsyncEnteredCount;

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

            /// <summary>
            /// Starts holding all subsequent ReadAsync/ReadBackupAsync calls at a gate. Call ReleaseReadAsync to let them proceed.
            /// </summary>
            public void HoldReadAsync()
            {
                _readAsyncGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            /// <summary>
            /// Releases all read calls currently waiting at the gate, and stops holding new ones.
            /// </summary>
            public void ReleaseReadAsync()
            {
                TaskCompletionSource<bool> gate = _readAsyncGate;
                _readAsyncGate = null;
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
                    _mtimes[(profileId, fileName)] = DateTime.UtcNow;
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
                _mtimes[(profileId, fileName)] = DateTime.UtcNow;
                if (!ProfileIds.Contains(profileId))
                {
                    ProfileIds.Add(profileId);
                }
            }

            public void RestoreBackup(string profileId, string fileName)
            {
                RestoreBackupCalls.Add((profileId, fileName));
            }

            public async Task<string> ReadAsync(string profileId, string fileName)
            {
                System.Threading.Interlocked.Increment(ref _readAsyncEnteredCount);
                ReadAsyncCalls.Add((profileId, fileName));

                TaskCompletionSource<bool> gate = _readAsyncGate;
                if (gate != null) await gate.Task;

                return _primaryFiles[(profileId, fileName)];
            }

            public async Task<string> ReadBackupAsync(string profileId, string fileName)
            {
                System.Threading.Interlocked.Increment(ref _readAsyncEnteredCount);
                ReadBackupCalls.Add((profileId, fileName));

                TaskCompletionSource<bool> gate = _readAsyncGate;
                if (gate != null) await gate.Task;

                return _backupFiles[(profileId, fileName)];
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

                List<(string profileId, string fileName)> mtimeKeys = _mtimes.Keys.Where(key => key.profileId == profileId).ToList();
                foreach ((string currentProfileId, string fileName) key in mtimeKeys)
                {
                    _mtimes.Remove(key);
                }

                ProfileIds.Remove(profileId);
            }

            public bool Exists(string profileId, string fileName)
            {
                return _primaryFiles.ContainsKey((profileId, fileName));
            }

            public DateTime? GetLastWriteTimeUtc(string profileId, string fileName)
            {
                return _mtimes.TryGetValue((profileId, fileName), out DateTime mtime) ? mtime : (DateTime?)null;
            }

            public IEnumerable<string> GetAllProfileIds()
            {
                if (GetAllProfileIdsException != null)
                {
                    throw GetAllProfileIdsException;
                }

                return ProfileIds;
            }

            public string GetMostRecentProfileId(string fileName)
            {
                return MostRecentProfileId;
            }

            public void SetPrimaryFile(string profileId, string fileName, string data)
            {
                _primaryFiles[(profileId, fileName)] = data;
                _mtimes[(profileId, fileName)] = DateTime.UtcNow;
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

            /// <summary>
            /// Test helper: explicitly set mtime for a file. Used to simulate post-crash scenarios
            /// where .sav was written after .meta (stale cache).
            /// </summary>
            public void SetMtime(string profileId, string fileName, DateTime mtime)
            {
                _mtimes[(profileId, fileName)] = mtime;
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
                    DefaultSaveMeta defaultMeta => new DefaultSaveMeta(defaultMeta.ProfileID, defaultMeta.LastTimeSaved),
                    TestSaveData saveData => new TestSaveData { Value = saveData.Value },
                    _ => value,
                };
            }

            private static SaveData CloneSaveData(SaveData source)
            {
                SaveData clone = new()
                {
                    Meta = (ISaveMeta)CloneObject(source.Meta),
                };

                foreach (KeyValuePair<string, ISaveData> item in source.ObjectData)
                {
                    clone.ObjectData[item.Key] = (ISaveData)CloneObject(item.Value);
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

        /// <summary>Distinctive exception thrown by <see cref="ThrowingCaptureSavable"/> so tests can match it precisely.</summary>
        private sealed class CaptureBoomException : Exception { }

        /// <summary>Savable whose CaptureData() throws while <see cref="ThrowOnCapture"/> is true.</summary>
        private sealed class ThrowingCaptureSavable : ISavable
        {
            public ThrowingCaptureSavable(string saveKey) => SaveKey = saveKey;
            public string SaveKey { get; }
            public bool ThrowOnCapture = true;

            public ISaveData CaptureData()
            {
                if (ThrowOnCapture) throw new CaptureBoomException();
                return new TestSaveData { Value = 1 };
            }

            public void RestoreData(ISaveData data) { }
        }

        /// <summary>Records the managed thread id of every CaptureData() call so tests can assert main-thread execution.</summary>
        private sealed class ThreadRecordingSavable : ISavable
        {
            public ThreadRecordingSavable(string saveKey) => SaveKey = saveKey;
            public string SaveKey { get; }
            public readonly List<int> CaptureThreadIds = new();

            public ISaveData CaptureData()
            {
                lock (CaptureThreadIds) CaptureThreadIds.Add(System.Threading.Thread.CurrentThread.ManagedThreadId);
                return new TestSaveData { Value = 1 };
            }

            public void RestoreData(ISaveData data) { }
        }

        /// <summary>CaptureData() returns a fresh copy of <see cref="CurrentValue"/>, so a later mutation of the
        /// field does not retroactively change an already-captured snapshot (unlike a shared mutable reference).</summary>
        private sealed class SnapshottingSavable : ISavable
        {
            public SnapshottingSavable(string saveKey) => SaveKey = saveKey;
            public string SaveKey { get; }
            public int CurrentValue;

            public ISaveData CaptureData() => new TestSaveData { Value = CurrentValue };
            public void RestoreData(ISaveData data) { }
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