using System;
using Newtonsoft.Json;
using NUnit.Framework;
using ThanhDV.GameSaver.Core;
using ThanhDV.GameSaver.Infrastructure;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class SafeTypeBinderTests
    {
        private SafeTypeBinder _binder;

        [SetUp]
        public void SetUp()
        {
            _binder = new SafeTypeBinder();
        }

        #region Auto-scan

        [Test]
        public void AutoScan_DiscoversISaveDataImplementor()
        {
            Assert.IsTrue(_binder.IsRegistered(typeof(PlainSaveData)),
                "ISaveData implementor should be auto-registered.");
        }

        [Test]
        public void AutoScan_DiscoversISaveMetaImplementor()
        {
            Assert.IsTrue(_binder.IsRegistered(typeof(PlainSaveMeta)),
                "ISaveMeta implementor should be auto-registered.");
        }

        [Test]
        public void AutoScan_UsesFullNameWhenNoAttribute()
        {
            string fullName = typeof(PlainSaveData).FullName;
            Assert.IsTrue(_binder.IsRegistered(fullName),
                $"Type without [SaveDataType] should be registered with FullName as alias. Expected alias: '{fullName}'.");
        }

        [Test]
        public void AutoScan_UsesAttributeAliasWhenPresent()
        {
            Assert.IsTrue(_binder.IsRegistered("Test.SafeTypeBinder.Aliased"),
                "Type with [SaveDataType] should be registered using the attribute's alias.");
        }

        [Test]
        public void AutoScan_AttributeAliasOverridesFullName()
        {
            string fullName = typeof(AliasedSaveData).FullName;
            Assert.IsFalse(_binder.IsRegistered(fullName),
                "When [SaveDataType] is present, FullName should NOT also be registered as a separate alias.");
        }

        [Test]
        public void AutoScan_SkipsAbstractClass()
        {
            Assert.IsFalse(_binder.IsRegistered(typeof(AbstractSaveDataBase)),
                "Abstract classes should not be auto-registered (cannot be instantiated during deserialize).");
        }

        [Test]
        public void AutoScan_SkipsInterface()
        {
            Assert.IsFalse(_binder.IsRegistered(typeof(ISaveData)),
                "Interfaces should not be auto-registered.");
        }

        [Test]
        public void AutoScan_SkipsTypeWithoutInterfaceOrAttribute()
        {
            Assert.IsFalse(_binder.IsRegistered(typeof(UnrelatedPoco)),
                "Plain POCO without ISaveData / ISaveMeta / [SaveDataType] should not be auto-registered.");
        }

        [Test]
        public void AutoScan_DiscoversStandaloneAttributedType()
        {
            // Has [SaveDataType] but does NOT implement ISaveData — still picked up.
            Assert.IsTrue(_binder.IsRegistered("Test.SafeTypeBinder.Standalone"),
                "Type with [SaveDataType] but no interface should still be auto-registered.");
        }

        #endregion

        #region Register

        [Test]
        public void Register_Generic_AddsBothDirections()
        {
            _binder.Register<UnrelatedPoco>("Test.Manual.Poco");

            Assert.IsTrue(_binder.IsRegistered("Test.Manual.Poco"));
            Assert.IsTrue(_binder.IsRegistered(typeof(UnrelatedPoco)));
        }

        [Test]
        public void Register_NonGeneric_AddsBothDirections()
        {
            _binder.Register(typeof(UnrelatedPoco), "Test.Manual.Poco.NonGeneric");

            Assert.IsTrue(_binder.IsRegistered("Test.Manual.Poco.NonGeneric"));
            Assert.IsTrue(_binder.IsRegistered(typeof(UnrelatedPoco)));
        }

        [Test]
        public void Register_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _binder.Register(null, "Test.Manual.Null"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void Register_NullOrWhitespaceAlias_ThrowsArgumentException(string invalidAlias)
        {
            Assert.Throws<ArgumentException>(() => _binder.Register<UnrelatedPoco>(invalidAlias));
        }

        [Test]
        public void Register_SameMappingTwice_Idempotent()
        {
            _binder.Register<UnrelatedPoco>("Test.Idempotent");

            Assert.DoesNotThrow(() => _binder.Register<UnrelatedPoco>("Test.Idempotent"),
                "Re-registering the exact same mapping should be a no-op, not an error.");
        }

        [Test]
        public void Register_SameAliasToDifferentType_ThrowsArgumentException()
        {
            _binder.Register<UnrelatedPoco>("Test.AliasCollision");

            Assert.Throws<ArgumentException>(
                () => _binder.Register<AnotherPoco>("Test.AliasCollision"),
                "Registering two different types under the same alias must throw — aliases must be unique.");
        }

        [Test]
        public void Register_SameTypeWithDifferentAlias_ThrowsArgumentException()
        {
            _binder.Register<UnrelatedPoco>("Test.TypeCollision.First");

            Assert.Throws<ArgumentException>(
                () => _binder.Register<UnrelatedPoco>("Test.TypeCollision.Second"),
                "Registering the same type twice with different aliases must throw — each type has exactly one alias.");
        }

        #endregion

        #region IsRegistered

        [Test]
        public void IsRegistered_UnknownAlias_ReturnsFalse()
        {
            Assert.IsFalse(_binder.IsRegistered("nonexistent.alias.xyz"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void IsRegistered_NullOrWhitespaceAlias_ReturnsFalse(string alias)
        {
            Assert.IsFalse(_binder.IsRegistered(alias));
        }

        [Test]
        public void IsRegistered_NullType_ReturnsFalse()
        {
            Assert.IsFalse(_binder.IsRegistered((Type)null));
        }

        #endregion

        #region BindToName (serialize side)

        [Test]
        public void BindToName_RegisteredType_ReturnsAlias()
        {
            _binder.BindToName(typeof(AliasedSaveData), out _, out string typeName);

            Assert.AreEqual("Test.SafeTypeBinder.Aliased", typeName);
        }

        [Test]
        public void BindToName_RegisteredType_AssemblyIsNull()
        {
            _binder.BindToName(typeof(AliasedSaveData), out string assemblyName, out _);

            Assert.IsNull(assemblyName,
                "Assembly portion of $type must always be null — embedding assembly names defeats the rename-resilience purpose.");
        }

        [Test]
        public void BindToName_AutoScannedPlainType_UsesFullName()
        {
            _binder.BindToName(typeof(PlainSaveData), out _, out string typeName);

            Assert.AreEqual(typeof(PlainSaveData).FullName, typeName);
        }

        [Test]
        public void BindToName_UnregisteredType_ThrowsJsonSerializationException()
        {
            Assert.Throws<JsonSerializationException>(
                () => _binder.BindToName(typeof(UnrelatedPoco), out _, out _));
        }

        [Test]
        public void BindToName_NullType_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => _binder.BindToName(null, out _, out _));
        }

        #endregion

        #region BindToType (deserialize side)

        [Test]
        public void BindToType_DirectAliasMatch_ReturnsRegisteredType()
        {
            Type result = _binder.BindToType(null, "Test.SafeTypeBinder.Aliased");

            Assert.AreEqual(typeof(AliasedSaveData), result);
        }

        [Test]
        public void BindToType_FullNameAsAlias_ReturnsType()
        {
            // Plain type without attribute: alias == FullName, Strategy 1 hits directly.
            string fullName = typeof(PlainSaveData).FullName;
            Type result = _binder.BindToType(null, fullName);

            Assert.AreEqual(typeof(PlainSaveData), result);
        }

        [Test]
        public void BindToType_FullNameFallback_ReturnsType()
        {
            // AliasedSaveData is registered with alias "Test.SafeTypeBinder.Aliased", NOT with FullName.
            // Legacy AQN saves passed FullName as typeName — Strategy 2 (FullName scan) should find it.
            string fullName = typeof(AliasedSaveData).FullName;
            Type result = _binder.BindToType("AnyAssembly", fullName);

            Assert.AreEqual(typeof(AliasedSaveData), result,
                "Strategy 2 (FullName fallback) should match legacy AQN typeName against registered type's FullName.");
        }

        [Test]
        public void BindToType_UnknownTypeName_ThrowsJsonSerializationException()
        {
            Assert.Throws<JsonSerializationException>(
                () => _binder.BindToType(null, "definitely.not.registered.zzz"));
        }

        [Test]
        public void BindToType_SystemDangerousType_Rejected()
        {
            // RCE protection — gadget-chain types must NOT resolve, regardless of input format.
            Assert.Throws<JsonSerializationException>(
                () => _binder.BindToType("System", "System.Diagnostics.Process"));
        }

        [Test]
        public void BindToType_ArbitrarySystemAQNFormat_Rejected()
        {
            // Tampered save attempting to load via AQN format.
            Assert.Throws<JsonSerializationException>(
                () => _binder.BindToType("System.IO.FileSystem", "System.IO.File"));
        }

        [TestCase(null)]
        [TestCase("")]
        public void BindToType_NullOrEmptyTypeName_ThrowsArgumentException(string invalidTypeName)
        {
            Assert.Throws<ArgumentException>(
                () => _binder.BindToType("any", invalidTypeName));
        }

        [Test]
        public void BindToType_AssemblyNameDoesNotAffectLookup()
        {
            // assemblyName is informational only (used in error messages).
            // Same typeName should resolve to same type regardless of assemblyName value.
            Type fromNull = _binder.BindToType(null, "Test.SafeTypeBinder.Aliased");
            Type fromEmpty = _binder.BindToType("", "Test.SafeTypeBinder.Aliased");
            Type fromFake = _binder.BindToType("Pretend.Assembly", "Test.SafeTypeBinder.Aliased");

            Assert.AreEqual(typeof(AliasedSaveData), fromNull);
            Assert.AreEqual(typeof(AliasedSaveData), fromEmpty);
            Assert.AreEqual(typeof(AliasedSaveData), fromFake);
        }

        #endregion

        #region Test types (kept private nested to scope them locally; reflection still finds private types)

        private class PlainSaveData : ISaveData
        {
            public int Value;
        }

        [SaveDataType("Test.SafeTypeBinder.Aliased")]
        private class AliasedSaveData : ISaveData
        {
            public int Value;
        }

        [SaveDataType("Test.SafeTypeBinder.Standalone")]
        private class StandaloneAttributed
        {
            public int Value;
        }

        private class PlainSaveMeta : ISaveMeta
        {
            public string ProfileID { get; set; }
            public DateTime LastTimeSaved { get; set; }
        }

        private abstract class AbstractSaveDataBase : ISaveData
        {
            public int Common;
        }

        private class UnrelatedPoco
        {
            public int X;
        }

        private class AnotherPoco
        {
            public int Y;
        }

        #endregion
    }
}
