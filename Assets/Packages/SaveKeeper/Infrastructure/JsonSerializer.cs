using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;

namespace ThanhDV.SaveKeeper.Infrastructure
{
    public class JsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;

        /// <summary>
        /// Whitelist binder. Use <c>Binder.Register&lt;T&gt;("alias")</c> for types loaded after construction
        /// (AssetBundles, DLC, mods) before serializing/deserializing them.
        /// </summary>
        public SafeTypeBinder Binder { get; }

        public JsonSerializer()
        {
            Binder = new SafeTypeBinder();

            _settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,               // Supports polymorphism without bloating the JSON
                SerializationBinder = Binder,                           // Whitelist that blocks unsafe deserialization attacks
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,   // Prevents crashes from circular object references
                NullValueHandling = NullValueHandling.Ignore,           // Reduces file size by skipping null fields
                Formatting = Formatting.None,                           // Minifies JSON for faster I/O and smaller storage
                Converters = new List<JsonConverter>(JsonUtilities.UnityConverter)  // Unity-aware converters: handles Vector*, Quaternion, Color, Rect, Bounds, etc.
            };
        }

        /// <summary>Serializes <paramref name="obj"/> to a JSON string.</summary>
        /// <exception cref="ArgumentNullException">obj is null.</exception>
        public string Serialize<T>(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj), "The object to serialize is null.");
            return JsonConvert.SerializeObject(obj, _settings);
        }

        /// <summary>Deserializes a JSON string back into an instance of <typeparamref name="T"/>.</summary>
        /// <exception cref="ArgumentNullException">data is null or whitespace.</exception>
        public T Deserialize<T>(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new ArgumentNullException(nameof(data), "The data to deserialize is null or empty.");
            return JsonConvert.DeserializeObject<T>(data, _settings);
        }
    }
}
