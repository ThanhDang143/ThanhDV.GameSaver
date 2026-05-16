using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ThanhDV.GameSaver.Core;

namespace ThanhDV.GameSaver.Infrastructure
{
    public class JsonSerializer : ISerializer
    {
        private readonly JsonSerializerSettings _settings;

        /// <summary>
        /// Whitelist binder for manually registering types loaded after construction (AssetBundles, DLC, mods).
        /// Register before serializing/deserializing those types.
        /// </summary>
        public SafeTypeBinder Binder { get; }

        /// <summary>
        /// Creates a serializer with a fresh <see cref="SafeTypeBinder"/> that auto-discovers
        /// all <see cref="ISaveData"/> / <see cref="ISaveMeta"/> types in currently-loaded assemblies.
        /// </summary>
        public JsonSerializer() : this(new SafeTypeBinder()) { }

        public JsonSerializer(SafeTypeBinder binder)
        {
            Binder = binder ?? throw new ArgumentNullException(nameof(binder));

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

        /// <summary>
        /// Serializes the specified object to a JSON string.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A JSON string representing the serialized object.</returns>
        public string Serialize<T>(T obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj), "The object to serialize is null.");
            return JsonConvert.SerializeObject(obj, _settings);
        }

        /// <summary>
        /// Deserializes the specified JSON string back into an object of type T.
        /// </summary>
        /// <typeparam name="T">The type of the object to deserialize.</typeparam>
        /// <param name="data">The JSON string containing the serialized data.</param>
        /// <returns>The deserialized object of type T.</returns>
        public T Deserialize<T>(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) throw new ArgumentNullException(nameof(data), "The data to deserialize is null or empty.");
            return JsonConvert.DeserializeObject<T>(data, _settings);
        }
    }
}
