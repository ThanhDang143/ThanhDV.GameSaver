using System;
using System.Collections.Generic;
using NUnit.Framework;
using ThanhDV.GameSaver.Infrastructure;

namespace ThanhDV.GameSaver.Tests.Editor
{
    public class JsonSerializerTests
    {
        private JsonSerializer _serializer;

        [SetUp]
        public void SetUp()
        {
            _serializer = new JsonSerializer();
        }

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

        private class TestData
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private abstract class BaseItem { }
        private class Item : BaseItem { public string Name; }
        private class Sword : BaseItem { public int Damage; }

        private class Node
        {
            public string Name { get; set; }
            public Node Parent { get; set; }
            public Node Child { get; set; }
        }
    }
}
