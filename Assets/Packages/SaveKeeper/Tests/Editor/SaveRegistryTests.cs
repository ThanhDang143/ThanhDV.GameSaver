using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ThanhDV.SaveKeeper.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace ThanhDV.SaveKeeper.Tests.Editor
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
            Assert.That(_registry.Savables, Does.Contain(savable));
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
        // Duplicate key handling (#6)
        // ===================================================================

        [Test]
        public void Register_DifferentSavablesSameKey_ThrowsDuplicateSaveKeyException()
        {
            ISavable a = new TestPocoSavable("shared");
            ISavable b = new TestPocoSavable("shared");
            _registry.Register(a);

            Assert.Throws<DuplicateSaveKeyException>(() => _registry.Register(b));
        }

        [Test]
        public void Register_Duplicate_ExceptionInheritsInvalidOperationException()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);

            // Assert.Catch<T> match T HOẶC subclass — đúng semantic "DuplicateSaveKeyException IS-A InvalidOperationException".
            // (Assert.Throws<T> chỉ match exact type, không pass dù inherit đúng.)
            Assert.Catch<InvalidOperationException>(() => _registry.Register(b),
                "DuplicateSaveKeyException phải inherit InvalidOperationException để user catch theo base type được.");
        }

        [Test]
        public void Register_Duplicate_ExceptionPopulatesKeyAndTypes()
        {
            ISavable a = new TestPocoSavable("my-key");
            ISavable b = new AnotherTestSavable("my-key");
            _registry.Register(a);

            DuplicateSaveKeyException ex = Assert.Throws<DuplicateSaveKeyException>(() => _registry.Register(b));

            Assert.AreEqual("my-key", ex.SaveKey);
            Assert.AreEqual(typeof(TestPocoSavable), ex.ExistingType);
            Assert.AreEqual(typeof(AnotherTestSavable), ex.IncomingType);
        }

        [Test]
        public void Register_Duplicate_ExceptionMessageContainsKeyAndTypeNames()
        {
            ISavable a = new TestPocoSavable("my-key");
            ISavable b = new AnotherTestSavable("my-key");
            _registry.Register(a);

            DuplicateSaveKeyException ex = Assert.Throws<DuplicateSaveKeyException>(() => _registry.Register(b));

            Assert.That(ex.Message, Does.Contain("my-key"));
            Assert.That(ex.Message, Does.Contain(nameof(TestPocoSavable)));
            Assert.That(ex.Message, Does.Contain(nameof(AnotherTestSavable)));
        }

        [Test]
        public void Register_DuplicateKey_DoesNotFireEventForIncoming()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);

            int eventCount = 0;
            _registry.OnSavableRegistered += _ => eventCount++;

            try { _registry.Register(b); } catch (DuplicateSaveKeyException) { /* expected */ }

            Assert.AreEqual(0, eventCount, "Register throw → không fire event cho instance bị reject.");
        }

        [Test]
        public void Register_DuplicateKey_ExistingInstanceUntouched()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);

            try { _registry.Register(b); } catch (DuplicateSaveKeyException) { /* expected */ }

            Assert.AreEqual(1, _registry.Savables.Count);
            Assert.That(_registry.Savables, Does.Contain(a), "Instance đã đăng ký không bị ảnh hưởng bởi register thất bại.");
            Assert.That(_registry.Savables, Has.No.Member(b));
        }

        [Test]
        public void Register_DifferentKeys_BothRegistered()
        {
            ISavable a = new TestPocoSavable("ka");
            ISavable b = new TestPocoSavable("kb");

            _registry.Register(a);
            _registry.Register(b);

            Assert.AreEqual(2, _registry.Savables.Count);
            Assert.That(_registry.Savables, Does.Contain(a));
            Assert.That(_registry.Savables, Does.Contain(b));
        }

        [Test]
        public void Register_AfterUnregisterSameKey_NewInstanceSucceeds()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);
            _registry.Unregister(a);

            Assert.DoesNotThrow(() => _registry.Register(b));
            Assert.AreEqual(1, _registry.Savables.Count);
            Assert.That(_registry.Savables, Does.Contain(b));
        }

        [Test]
        public void Register_SameInstanceTwice_DoesNotThrow()
        {
            ISavable savable = new TestPocoSavable("k");
            _registry.Register(savable);

            Assert.DoesNotThrow(() => _registry.Register(savable),
                "Re-register cùng instance là idempotent no-op (không throw, không double-fire event).");
        }

        // ===================================================================
        // Auto-replace dead reference (#6)
        // ===================================================================

        [Test]
        public void Register_NewInstanceWhenExistingIsDead_AutoReplaces()
        {
            GameObject go = new("auto-replace-target");
            TestMonoSavable dead = go.AddComponent<TestMonoSavable>();
            dead.SetSaveKey("replace-key");
            _registry.Register(dead);
            UnityEngine.Object.DestroyImmediate(go);

            ISavable replacement = new TestPocoSavable("replace-key");
            LogAssert.Expect(LogType.Warning, new Regex(".*auto-replaced.*"));
            _registry.Register(replacement);

            Assert.AreEqual(1, _registry.Savables.Count);
            Assert.That(_registry.Savables, Does.Contain(replacement),
                "Sau auto-replace, snapshot phải chứa instance mới (dead ref đã bị ghi đè).");
        }

        [Test]
        public void Register_AutoReplaceDeadRef_FiresEventForNewInstance()
        {
            GameObject go = new("auto-replace-event");
            TestMonoSavable dead = go.AddComponent<TestMonoSavable>();
            dead.SetSaveKey("replace-event-key");
            _registry.Register(dead);
            UnityEngine.Object.DestroyImmediate(go);

            ISavable replacement = new TestPocoSavable("replace-event-key");
            ISavable eventArg = null;
            _registry.OnSavableRegistered += s => eventArg = s;

            LogAssert.Expect(LogType.Warning, new Regex(".*auto-replaced.*"));
            _registry.Register(replacement);

            Assert.AreSame(replacement, eventArg,
                "OnSavableRegistered fire cho instance mới khi auto-replace dead ref.");
        }

        [Test]
        public void Register_AutoReplaceDeadRef_LogsWarningWithKey()
        {
            GameObject go = new("auto-replace-warn");
            TestMonoSavable dead = go.AddComponent<TestMonoSavable>();
            dead.SetSaveKey("warn-key");
            _registry.Register(dead);
            UnityEngine.Object.DestroyImmediate(go);

            // Pattern phải khớp cả "auto-replaced" và key cụ thể → đảm bảo warning có thông tin debug
            LogAssert.Expect(LogType.Warning, new Regex(".*auto-replaced.*warn-key.*"));
            _registry.Register(new TestPocoSavable("warn-key"));
        }

        // ===================================================================
        // SaveKey contract violations (slow-path Unregister)
        // ===================================================================

        [Test]
        public void Unregister_AfterSaveKeyChanged_StillRemovesByReference()
        {
            TestMutableKeyPocoSavable savable = new("original");
            _registry.Register(savable);
            savable.ChangeKey("changed");

            LogAssert.Expect(LogType.Warning, new Regex(".*SaveKey was changed.*"));
            _registry.Unregister(savable);

            Assert.AreEqual(0, _registry.Savables.Count,
                "Slow path walk by reference vẫn dọn được entry dù SaveKey đã đổi.");
        }

        [Test]
        public void Unregister_AfterSaveKeyChanged_LogsWarning()
        {
            TestMutableKeyPocoSavable savable = new("k1");
            _registry.Register(savable);
            savable.ChangeKey("k2");

            LogAssert.Expect(LogType.Warning, new Regex(".*SaveKey was changed.*"));
            _registry.Unregister(savable);
        }

        [Test]
        public void Unregister_SlowPath_FiresUnregisteredEvent()
        {
            TestMutableKeyPocoSavable savable = new("k1");
            _registry.Register(savable);
            savable.ChangeKey("k2");

            ISavable captured = null;
            _registry.OnSavableUnregistered += s => captured = s;

            LogAssert.Expect(LogType.Warning, new Regex(".*SaveKey was changed.*"));
            _registry.Unregister(savable);

            Assert.AreSame(savable, captured);
        }

        [Test]
        public void Unregister_DifferentInstanceSameKey_DoesNotRemoveRegistered()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);

            _registry.Unregister(b);

            Assert.AreEqual(1, _registry.Savables.Count);
            Assert.That(_registry.Savables, Does.Contain(a),
                "Unregister(b) cùng key nhưng khác instance → không động vào a.");
        }

        [Test]
        public void Unregister_DifferentInstanceSameKey_DoesNotFireEvent()
        {
            ISavable a = new TestPocoSavable("k");
            ISavable b = new TestPocoSavable("k");
            _registry.Register(a);

            int eventCount = 0;
            _registry.OnSavableUnregistered += _ => eventCount++;

            _registry.Unregister(b);

            Assert.AreEqual(0, eventCount);
        }

        // ===================================================================
        // Thread safety with collision throw (#6)
        // ===================================================================

        [Test]
        public void ConcurrentRegisterDifferentKeys_AllSucceed()
        {
            const int threadCount = 8;
            const int perThread = 50;
            Task[] tasks = new Task[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int idx = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        _registry.Register(new TestPocoSavable($"t{idx}-i{i}"));
                    }
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
            Assert.AreEqual(threadCount * perThread, _registry.Savables.Count,
                "Mỗi thread đăng ký key riêng → không collision, tất cả thành công.");
        }

        [Test]
        public void ConcurrentRegisterSameKey_OneWinsOthersThrow()
        {
            const int threadCount = 8;
            Task[] tasks = new Task[threadCount];
            int exceptionCount = 0;

            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    try
                    {
                        _registry.Register(new TestPocoSavable("contested"));
                    }
                    catch (DuplicateSaveKeyException)
                    {
                        Interlocked.Increment(ref exceptionCount);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(1, _registry.Savables.Count, "Đúng 1 thread thắng race, registry chỉ có 1 entry.");
            Assert.AreEqual(threadCount - 1, exceptionCount, $"{threadCount - 1} thread còn lại phải throw DuplicateSaveKeyException.");
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
        /// SaveKey default là "mono-savable" để tương thích các test cũ; gọi <see cref="SetSaveKey"/> nếu cần custom.
        /// </summary>
        public class TestMonoSavable : MonoBehaviour, ISavable
        {
            private string _saveKey = "mono-savable";
            public string SaveKey => _saveKey;
            public void SetSaveKey(string saveKey) => _saveKey = saveKey;
            public ISaveData CaptureData() => null;
            public void RestoreData(ISaveData data) { }
        }

        /// <summary>
        /// POCO ISavable type thứ hai — dùng cho tests cần phân biệt ExistingType vs IncomingType
        /// trong DuplicateSaveKeyException.
        /// </summary>
        private class AnotherTestSavable : ISavable
        {
            public string SaveKey { get; }
            public AnotherTestSavable(string saveKey) { SaveKey = saveKey; }
            public ISaveData CaptureData() => null;
            public void RestoreData(ISaveData data) { }
        }

        /// <summary>
        /// POCO ISavable với SaveKey mutable — dùng cho tests slow-path Unregister khi user vi phạm
        /// contract "SaveKey immutable" sau Register.
        /// </summary>
        private class TestMutableKeyPocoSavable : ISavable
        {
            private string _saveKey;
            public string SaveKey => _saveKey;
            public TestMutableKeyPocoSavable(string initial) { _saveKey = initial; }
            public void ChangeKey(string newKey) => _saveKey = newKey;
            public ISaveData CaptureData() => null;
            public void RestoreData(ISaveData data) { }
        }
    }
}
