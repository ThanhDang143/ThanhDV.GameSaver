using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.GameSaver.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class SaveRegistryTests
    {
        private SaveRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _registry = new SaveRegistry();
        }

        // ===================================================================
        // Basic Register / Unregister
        // ===================================================================

        [Test]
        public void Register_NewSavable_AppearsInSavables()
        {
            ISavable savable = new TestPocoSavable("k1");

            _registry.Register(savable);

            Assert.AreEqual(1, _registry.Savables.Count);
            Assert.Contains(savable, (System.Collections.ICollection)_registry.Savables);
        }

        [Test]
        public void Register_DuplicateSavable_NotAddedAgain()
        {
            ISavable savable = new TestPocoSavable("k1");

            _registry.Register(savable);
            _registry.Register(savable);

            Assert.AreEqual(1, _registry.Savables.Count);
        }

        [Test]
        public void Register_DuplicateSavable_DoesNotFireEventAgain()
        {
            ISavable savable = new TestPocoSavable("k1");
            int eventCount = 0;
            _registry.OnSavableRegistered += _ => eventCount++;

            _registry.Register(savable);
            _registry.Register(savable);

            Assert.AreEqual(1, eventCount);
        }

        [Test]
        public void Register_NullSavable_NoOp()
        {
            Assert.DoesNotThrow(() => _registry.Register(null));
            Assert.AreEqual(0, _registry.Savables.Count);
        }

        [TestCase("")]
        [TestCase(null)]
        public void Register_NullOrEmptySaveKey_NoOp(string saveKey)
        {
            ISavable savable = new TestPocoSavable(saveKey);

            _registry.Register(savable);

            Assert.AreEqual(0, _registry.Savables.Count);
        }

        [Test]
        public void Unregister_RegisteredSavable_RemovesFromSavables()
        {
            ISavable savable = new TestPocoSavable("k1");
            _registry.Register(savable);

            _registry.Unregister(savable);

            Assert.AreEqual(0, _registry.Savables.Count);
        }

        [Test]
        public void Unregister_NotRegistered_NoOp()
        {
            ISavable savable = new TestPocoSavable("k1");

            Assert.DoesNotThrow(() => _registry.Unregister(savable));
        }

        [Test]
        public void Unregister_NullSavable_NoOp()
        {
            Assert.DoesNotThrow(() => _registry.Unregister(null));
        }

        // ===================================================================
        // Events
        // ===================================================================

        [Test]
        public void Register_FiresOnSavableRegisteredEvent()
        {
            ISavable savable = new TestPocoSavable("k1");
            ISavable captured = null;
            _registry.OnSavableRegistered += s => captured = s;

            _registry.Register(savable);

            Assert.AreSame(savable, captured);
        }

        [Test]
        public void Unregister_FiresOnSavableUnregisteredEvent()
        {
            ISavable savable = new TestPocoSavable("k1");
            _registry.Register(savable);

            ISavable captured = null;
            _registry.OnSavableUnregistered += s => captured = s;

            _registry.Unregister(savable);

            Assert.AreSame(savable, captured);
        }

        [Test]
        public void Unregister_NotRegistered_DoesNotFireEvent()
        {
            ISavable savable = new TestPocoSavable("k1");
            int eventCount = 0;
            _registry.OnSavableUnregistered += _ => eventCount++;

            _registry.Unregister(savable);

            Assert.AreEqual(0, eventCount);
        }

        // ===================================================================
        // Snapshot semantics (#11)
        // ===================================================================

        [Test]
        public void Savables_ReturnsSnapshot_NotLiveView()
        {
            ISavable s1 = new TestPocoSavable("k1");
            _registry.Register(s1);

            var snapshot = _registry.Savables;
            Assert.AreEqual(1, snapshot.Count, "Sanity precondition.");

            _registry.Register(new TestPocoSavable("k2"));

            Assert.AreEqual(1, snapshot.Count,
                "Snapshot phải giữ nguyên dù registry đã thêm savable mới sau đó.");
        }

        [Test]
        public void Savables_DifferentAccesses_ReturnIndependentSnapshots()
        {
            _registry.Register(new TestPocoSavable("k1"));

            var snapshot1 = _registry.Savables;

            _registry.Register(new TestPocoSavable("k2"));

            var snapshot2 = _registry.Savables;

            Assert.AreEqual(1, snapshot1.Count);
            Assert.AreEqual(2, snapshot2.Count);
            Assert.AreNotSame(snapshot1, snapshot2);
        }

        // ===================================================================
        // Dead reference handling (#17)
        // ===================================================================

        [Test]
        public void Register_DestroyedMonoBehaviour_NoOp()
        {
            GameObject go = new("dead-on-register");
            TestMonoSavable mono = go.AddComponent<TestMonoSavable>();
            UnityEngine.Object.DestroyImmediate(go);

            _registry.Register(mono);

            Assert.AreEqual(0, _registry.Savables.Count,
                "Đăng ký Unity object đã destroy phải bị reject để chống dangling reference.");
        }

        [Test]
        public void Savables_AutoPrunesDestroyedMonoBehaviour_AndRemovesFromRegistry()
        {
            GameObject go = new("destroy-after-register");
            TestMonoSavable mono = go.AddComponent<TestMonoSavable>();
            _registry.Register(mono);
            Assert.AreEqual(1, _registry.Savables.Count, "Sanity precondition.");

            UnityEngine.Object.DestroyImmediate(go);

            var snapshot = _registry.Savables;
            Assert.AreEqual(0, snapshot.Count,
                "Savables getter phải tự dọn destroyed Unity object trước khi snapshot.");

            // Truy cập lần 2 — registry đã được prune lần trước, không cần dọn nữa.
            Assert.AreEqual(0, _registry.Savables.Count);
        }

        [Test]
        public void Unregister_DestroyedMonoBehaviour_StillRemovesReference()
        {
            GameObject go = new("destroy-then-unregister");
            TestMonoSavable mono = go.AddComponent<TestMonoSavable>();
            _registry.Register(mono);

            UnityEngine.Object.DestroyImmediate(go);

            // HashSet.Remove dùng reference equality (không qua Unity overloaded ==) → tìm thấy được.
            Assert.DoesNotThrow(() => _registry.Unregister(mono));
        }

        // ===================================================================
        // Thread safety (#11)
        // ===================================================================

        [Test]
        public void ConcurrentRegisterAndUnregister_FromMultipleThreads_NoExceptions()
        {
            const int threadCount = 8;
            const int opsPerThread = 200;

            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < opsPerThread; i++)
                    {
                        ISavable s = new TestPocoSavable($"t{threadIndex}-i{i}");
                        _registry.Register(s);
                        _registry.Unregister(s);
                    }
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
            Assert.AreEqual(0, _registry.Savables.Count,
                "Sau khi đăng ký + huỷ đăng ký đối xứng từ nhiều thread, registry phải sạch.");
        }

        [Test]
        public void ConcurrentSnapshotAndRegister_NoCollectionModifiedException()
        {
            // Reader/writer race: snapshot phải an toàn dù Register/Unregister chen vào liên tục.
            int durationMs = 200;
            bool stop = false;
            Exception caughtException = null;

            Task registerTask = Task.Run(() =>
            {
                try
                {
                    int i = 0;
                    while (!stop)
                    {
                        ISavable s = new TestPocoSavable($"r-{i++}");
                        _registry.Register(s);
                        _registry.Unregister(s);
                    }
                }
                catch (Exception ex) { caughtException = ex; }
            });

            Task snapshotTask = Task.Run(() =>
            {
                try
                {
                    while (!stop)
                    {
                        var snapshot = _registry.Savables;
                        foreach (var _ in snapshot) { /* iterate snapshot */ }
                    }
                }
                catch (Exception ex) { caughtException = ex; }
            });

            Thread.Sleep(durationMs);
            stop = true;
            Task.WaitAll(registerTask, snapshotTask);

            Assert.IsNull(caughtException,
                $"Đồng thời Register/Unregister + iterate snapshot phải an toàn. Exception: {caughtException}");
        }

        // ===================================================================
        // Test helpers
        // ===================================================================

        /// <summary>
        /// ISavable thuần POCO, không phải Unity object. Dùng cho test cơ bản và thread safety.
        /// </summary>
        private class TestPocoSavable : ISavable
        {
            public string SaveKey { get; }
            public TestPocoSavable(string saveKey) { SaveKey = saveKey; }
            public ISaveData CaptureData() => null;
            public void RestoreData(ISaveData data) { }
        }

        /// <summary>
        /// MonoBehaviour ISavable. Dùng cho test dead reference (destroy GameObject → C# ref còn nhưng Unity null).
        /// </summary>
        public class TestMonoSavable : MonoBehaviour, ISavable
        {
            public string SaveKey => "mono-savable";
            public ISaveData CaptureData() => null;
            public void RestoreData(ISaveData data) { }
        }
    }
}
