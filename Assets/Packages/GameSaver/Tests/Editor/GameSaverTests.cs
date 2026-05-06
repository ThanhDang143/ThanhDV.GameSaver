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

            public Task WriteAsync(string profileId, string fileName, string data)
            {
                if (WriteAsyncException != null)
                {
                    throw WriteAsyncException;
                }

                WriteAsyncCalls.Add((profileId, fileName, data));
                _primaryFiles[(profileId, fileName)] = data;
                if (!ProfileIds.Contains(profileId))
                {
                    ProfileIds.Add(profileId);
                }

                return Task.CompletedTask;
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
            private readonly Dictionary<string, object> _serializedValues = new();
            private readonly Dictionary<string, Exception> _deserializeExceptions = new();
            private readonly List<object> _serializedObjects = new();
            private int _nextId;

            public string Serialize<T>(T obj)
            {
                object clone = CloneObject(obj);
                _serializedObjects.Add(clone);

                string key = $"serialized-{++_nextId}";
                _serializedValues[key] = clone;
                return key;
            }

            public T Deserialize<T>(string data)
            {
                if (_deserializeExceptions.TryGetValue(data, out Exception exception))
                {
                    throw exception;
                }

                object value = _serializedValues[data];
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