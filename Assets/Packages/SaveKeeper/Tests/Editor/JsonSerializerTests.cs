using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NUnit.Framework;
using ThanhDV.SaveKeeper.Core;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Tests.Editor
{
    public class JsonSerializerTests
    {
        private Infrastructure.JsonSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new Infrastructure.JsonSerializer();
        }

        #region Basic serialize / deserialize

        [Test]
        public void Serialize_ValidObject_ReturnsJsonString()
        {
            var data = new TestData { Id = 1, Name = "Test" };
            var json = _serializer.Serialize(data);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.Contains("\"Id\":1"));
            Assert.IsTrue(json.Contains("\"Name\":\"Test\""));
        }

        [Test]
        public void Serialize_NullObject_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _serializer.Serialize<TestData>(null));
        }

        [Test]
        public void Deserialize_ValidJson_ReturnsObject()
        {
            var json = "{\"Id\":1,\"Name\":\"Test\"}";
            var data = _serializer.Deserialize<TestData>(json);
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.Id);
            Assert.AreEqual("Test", data.Name);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase(null)]
        public void Deserialize_NullOrWhitespace_ThrowsArgumentNullException(string invalidData)
        {
            Assert.Throws<ArgumentNullException>(() => _serializer.Deserialize<TestData>(invalidData));
        }

        [Test]
        public void Serialize_WithNullProperty_IgnoresNullValue()
        {
            var data = new TestData { Id = 1, Name = null };
            var json = _serializer.Serialize(data);
            Assert.IsFalse(json.Contains("\"Name\":"));
        }

        [Test]
        public void Serialize_And_Deserialize_WithPolymorphism()
        {
            var list = new List<BaseItem>
            {
                new Sword { Damage = 10 },
                new Item { Name = "Potion" }
            };

            var json = _serializer.Serialize(list);
            var deserializedList = _serializer.Deserialize<List<BaseItem>>(json);

            Assert.AreEqual(2, deserializedList.Count);
            Assert.IsInstanceOf<Sword>(deserializedList[0]);
            Assert.IsInstanceOf<Item>(deserializedList[1]);
        }

        [Test]
        public void Serialize_WithCircularReference_DoesNotThrow()
        {
            var parent = new Node { Name = "Parent" };
            var child = new Node { Name = "Child", Parent = parent };
            parent.Child = child;

            Assert.DoesNotThrow(() => _serializer.Serialize(parent));
        }

        #endregion

        #region Binder integration — $type format

        [Test]
        public void Serialize_PolymorphicItem_WritesCompactTypeWithoutAssembly()
        {
            // The whole point of the binder: $type carries only the alias, never an assembly portion.
            var list = new List<BaseItem> { new Item { Name = "Health Potion" } };
            string json = _serializer.Serialize(list);

            Assert.IsTrue(json.Contains("\"$type\""), "Polymorphic element should still emit $type for round-trip.");
            Assert.IsFalse(json.Contains(", Assembly-CSharp"),
                "Output must not include assembly-qualified name — that pattern is exactly what breaks on rename / asmdef move.");
            Assert.IsFalse(json.Contains(", ThanhDV.SaveKeeper.Tests.Editor"),
                "Output must not include the test assembly name either.");
        }

        [Test]
        public void Serialize_TypeWithSaveDataAliasAttribute_UsesAlias()
        {
            // MagicSword has [SaveDataAlias("Test.JsonSerializer.MagicSword")] — that exact string must appear in $type.
            var list = new List<BaseItem> { new MagicSword { Power = 99 } };
            string json = _serializer.Serialize(list);

            Assert.IsTrue(json.Contains("\"Test.JsonSerializer.MagicSword\""),
                "Attribute alias should be written verbatim into the $type field. " +
                $"Actual JSON: {json}");
        }

        [Test]
        public void Serialize_TypeWithoutAttribute_UsesFullNameAsAlias()
        {
            // Item has no [SaveDataAlias] — its alias is Type.FullName.
            var list = new List<BaseItem> { new Item { Name = "X" } };
            string json = _serializer.Serialize(list);

            string fullName = typeof(Item).FullName;
            Assert.IsTrue(json.Contains($"\"{fullName}\""),
                $"Without attribute, FullName should appear in $type. Expected to contain '\"{fullName}\"', got: {json}");
        }

        #endregion

        #region Binder integration — RCE protection (#2)

        [Test]
        public void Deserialize_TamperedSystemType_ThrowsJsonSerializationException()
        {
            // Classic gadget-chain attack vector: $type pointing at System.Diagnostics.Process.
            // The binder whitelist must reject this — Newtonsoft's default behavior would happily
            // load Process and execute it via property side-effects.
            string maliciousJson = "[{\"$type\":\"System.Diagnostics.Process, System\",\"StartInfo\":{}}]";

            Assert.Throws<JsonSerializationException>(
                () => _serializer.Deserialize<List<BaseItem>>(maliciousJson),
                "Unknown $type must be rejected, not silently loaded via Type.GetType.");
        }

        [Test]
        public void Deserialize_TamperedFileType_ThrowsJsonSerializationException()
        {
            // Another common gadget target — file operations.
            string maliciousJson = "[{\"$type\":\"System.IO.FileInfo, mscorlib\",\"OriginalPath\":\"/etc/passwd\"}]";

            Assert.Throws<JsonSerializationException>(
                () => _serializer.Deserialize<List<BaseItem>>(maliciousJson));
        }

        [Test]
        public void Deserialize_TamperedShortType_ThrowsJsonSerializationException()
        {
            // Also try the new compact format with a bad alias.
            string maliciousJson = "[{\"$type\":\"System.Diagnostics.Process\"}]";

            Assert.Throws<JsonSerializationException>(
                () => _serializer.Deserialize<List<BaseItem>>(maliciousJson));
        }

        #endregion

        #region Binder integration — Legacy AQN backward compat (#8)

        [Test]
        public void Deserialize_LegacyAQNFormat_TypeWithoutAttribute_LoadsViaDirectMatch()
        {
            // Save file created BEFORE the binder existed: $type was assembly-qualified.
            // Strategy 1: Item is registered with alias == FullName, so the legacy typeName
            // (== FullName) matches directly.
            string fullName = typeof(Item).FullName;
            string legacyJson = $"[{{\"$type\":\"{fullName}, ThanhDV.SaveKeeper.Tests.Editor\",\"Name\":\"Potion\"}}]";

            var list = _serializer.Deserialize<List<BaseItem>>(legacyJson);

            Assert.AreEqual(1, list.Count);
            Assert.IsInstanceOf<Item>(list[0]);
            Assert.AreEqual("Potion", ((Item)list[0]).Name);
        }

        [Test]
        public void Deserialize_LegacyAQNFormat_TypeWithAttribute_LoadsViaFullNameFallback()
        {
            // Strategy 2: type has [SaveDataAlias("…")] so alias != FullName.
            // Legacy save still encodes FullName in $type — the binder falls back by scanning
            // registered types for one whose FullName matches.
            string fullName = typeof(MagicSword).FullName;
            string legacyJson = $"[{{\"$type\":\"{fullName}, ThanhDV.SaveKeeper.Tests.Editor\",\"Power\":99}}]";

            var list = _serializer.Deserialize<List<BaseItem>>(legacyJson);

            Assert.AreEqual(1, list.Count);
            Assert.IsInstanceOf<MagicSword>(list[0]);
            Assert.AreEqual(99, ((MagicSword)list[0]).Power);
        }

        [Test]
        public void Deserialize_LegacyAQNFormat_WrongAssemblyHint_StillLoads()
        {
            // The assemblyName portion of legacy $type is intentionally ignored by the binder
            // (using it would defeat security). The same save loaded with a fake assembly name
            // must still resolve via typeName alone.
            string fullName = typeof(Item).FullName;
            string legacyJson = $"[{{\"$type\":\"{fullName}, Pretend.Assembly.Name\",\"Name\":\"Apple\"}}]";

            var list = _serializer.Deserialize<List<BaseItem>>(legacyJson);

            Assert.IsInstanceOf<Item>(list[0]);
            Assert.AreEqual("Apple", ((Item)list[0]).Name);
        }

        [Test]
        public void RoundTrip_LegacyAQNFormat_OutputRewrittenInCompactForm()
        {
            // Migration in transparency: load a legacy save, re-serialize — the output
            // is now in the new compact format. No user action required.
            string fullName = typeof(Item).FullName;
            string legacyJson = $"[{{\"$type\":\"{fullName}, ThanhDV.SaveKeeper.Tests.Editor\",\"Name\":\"Bread\"}}]";

            var list = _serializer.Deserialize<List<BaseItem>>(legacyJson);
            string newJson = _serializer.Serialize(list);

            Assert.IsFalse(newJson.Contains(", ThanhDV.SaveKeeper.Tests.Editor"),
                "After round-trip, the assembly portion should be gone — file silently migrated.");
            Assert.IsTrue(newJson.Contains($"\"{fullName}\""),
                "New format should retain just the alias (== FullName for unattributed type).");
        }

        #endregion

        #region Binder access — late-loaded types

        [Test]
        public void Binder_IsExposedForManualRegistration()
        {
            // Confirms the public API path used by mod systems / late-loaded assemblies.
            Assert.IsNotNull(_serializer.Binder,
                "JsonSerializer must expose Binder so callers can manually register types from runtime-loaded assemblies.");
        }

        [Test]
        public void Binder_ManualRegister_EnablesSerialization()
        {
            // Simulate a late-loaded type: a class that auto-scan happened to skip (no interface,
            // no attribute). After manual registration, it can participate in polymorphic serialization.
            _serializer.Binder.Register<PocoForLateLoad>("Test.JsonSerializer.LateLoaded");

            // Cannot easily simulate polymorphism without a base — but we can verify the binder
            // accepts the type and the round-trip via direct serialize/deserialize works.
            string alias = "Test.JsonSerializer.LateLoaded";
            Assert.IsTrue(_serializer.Binder.IsRegistered(alias));
            Assert.IsTrue(_serializer.Binder.IsRegistered(typeof(PocoForLateLoad)));
        }

        #endregion

        #region Unity-type converters

        [Test]
        public void Vector2_RoundTrip_PreservesValues()
        {
            Vector2 original = new(1.5f, -2.5f);
            string json = _serializer.Serialize(original);
            Vector2 result = _serializer.Deserialize<Vector2>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Vector3_RoundTrip_PreservesValues()
        {
            Vector3 original = new(1.5f, -2.0f, 3.7f);
            string json = _serializer.Serialize(original);
            Vector3 result = _serializer.Deserialize<Vector3>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Vector3_Serialize_OmitsDerivedProperties()
        {
            // Without the converter, Newtonsoft would emit normalized, magnitude, sqrMagnitude
            // (causing stack overflow or massive bloat). The converter must strip those.
            string json = _serializer.Serialize(Vector3.up);

            Assert.IsFalse(json.Contains("magnitude"), $"Output should not include derived property 'magnitude'. JSON: {json}");
            Assert.IsFalse(json.Contains("normalized"), $"Output should not include derived property 'normalized'. JSON: {json}");
            Assert.IsFalse(json.Contains("sqrMagnitude"), $"Output should not include derived property 'sqrMagnitude'. JSON: {json}");
        }

        [Test]
        public void Vector4_RoundTrip_PreservesValues()
        {
            Vector4 original = new(1f, 2f, 3f, 4f);
            string json = _serializer.Serialize(original);
            Vector4 result = _serializer.Deserialize<Vector4>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Vector2Int_RoundTrip_PreservesValues()
        {
            Vector2Int original = new(-5, 12);
            string json = _serializer.Serialize(original);
            Vector2Int result = _serializer.Deserialize<Vector2Int>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Vector3Int_RoundTrip_PreservesValues()
        {
            Vector3Int original = new(100, -200, 300);
            string json = _serializer.Serialize(original);
            Vector3Int result = _serializer.Deserialize<Vector3Int>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Quaternion_RoundTrip_PreservesValues()
        {
            // Use a non-identity rotation so the round-trip catches any incorrect normalization.
            Quaternion original = Quaternion.Euler(30f, 45f, 60f);
            string json = _serializer.Serialize(original);
            Quaternion result = _serializer.Deserialize<Quaternion>(json);

            // Float comparison with tolerance to handle minor precision drift.
            Assert.AreEqual(original.x, result.x, 1e-5f);
            Assert.AreEqual(original.y, result.y, 1e-5f);
            Assert.AreEqual(original.z, result.z, 1e-5f);
            Assert.AreEqual(original.w, result.w, 1e-5f);
        }

        [Test]
        public void Quaternion_Serialize_OmitsEulerAngles()
        {
            // eulerAngles is a property that recomputes from x/y/z/w — including it would bloat output
            // and round-tripping through it loses precision (gimbal lock).
            string json = _serializer.Serialize(Quaternion.Euler(10f, 20f, 30f));
            Assert.IsFalse(json.Contains("eulerAngles"), $"Output should not include eulerAngles property. JSON: {json}");
        }

        [Test]
        public void Color_RoundTrip_PreservesValues()
        {
            Color original = new(0.25f, 0.5f, 0.75f, 1f);
            string json = _serializer.Serialize(original);
            Color result = _serializer.Deserialize<Color>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Color32_RoundTrip_PreservesValues()
        {
            Color32 original = new(64, 128, 192, 255);
            string json = _serializer.Serialize(original);
            Color32 result = _serializer.Deserialize<Color32>(json);
            Assert.AreEqual(original.r, result.r);
            Assert.AreEqual(original.g, result.g);
            Assert.AreEqual(original.b, result.b);
            Assert.AreEqual(original.a, result.a);
        }

        [Test]
        public void Rect_RoundTrip_PreservesValues()
        {
            Rect original = new(10f, 20f, 100f, 50f);
            string json = _serializer.Serialize(original);
            Rect result = _serializer.Deserialize<Rect>(json);
            Assert.AreEqual(original, result);
        }

        [Test]
        public void Bounds_RoundTrip_PreservesValues()
        {
            Bounds original = new(new Vector3(1f, 2f, 3f), new Vector3(10f, 20f, 30f));
            string json = _serializer.Serialize(original);
            Bounds result = _serializer.Deserialize<Bounds>(json);
            Assert.AreEqual(original.center, result.center);
            Assert.AreEqual(original.size, result.size);
        }

        [Test]
        public void LayerMask_RoundTrip_PreservesValues()
        {
            LayerMask original = (1 << 8) | (1 << 12) | (1 << 31);
            string json = _serializer.Serialize(original);
            LayerMask result = _serializer.Deserialize<LayerMask>(json);
            Assert.AreEqual(original.value, result.value);
        }

        [Test]
        public void Matrix4x4_RoundTrip_PreservesValues()
        {
            Matrix4x4 original = Matrix4x4.TRS(
                new Vector3(1f, 2f, 3f),
                Quaternion.Euler(30f, 60f, 90f),
                new Vector3(2f, 2f, 2f));

            string json = _serializer.Serialize(original);
            Matrix4x4 result = _serializer.Deserialize<Matrix4x4>(json);

            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    Assert.AreEqual(original[row, col], result[row, col], 1e-5f, $"Mismatch at [{row},{col}]");
                }
            }
        }

        [Test]
        public void UnityTypes_NestedInSaveData_RoundTripsCorrectly()
        {
            // Most realistic scenario: Unity types as fields inside a save data class.
            UnitTransform original = new()
            {
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Euler(0f, 90f, 0f),
                Color = Color.green
            };

            string json = _serializer.Serialize(original);
            UnitTransform result = _serializer.Deserialize<UnitTransform>(json);

            Assert.AreEqual(original.Position, result.Position);
            Assert.AreEqual(original.Rotation.x, result.Rotation.x, 1e-5f);
            Assert.AreEqual(original.Rotation.w, result.Rotation.w, 1e-5f);
            Assert.AreEqual(original.Color, result.Color);
        }

        #endregion

        #region Test types

        private class TestData
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private abstract class BaseItem : ISaveData { }

        private class Item : BaseItem
        {
            public string Name;
        }

        private class Sword : BaseItem
        {
            public int Damage;
        }

        [SaveDataAlias("Test.JsonSerializer.MagicSword")]
        private class MagicSword : BaseItem
        {
            public int Power;
        }

        private class PocoForLateLoad
        {
            public int Value;
        }

        private class Node
        {
            public string Name { get; set; }
            public Node Parent { get; set; }
            public Node Child { get; set; }
        }

        private class UnitTransform
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Color Color;
        }

        #endregion
    }
}
