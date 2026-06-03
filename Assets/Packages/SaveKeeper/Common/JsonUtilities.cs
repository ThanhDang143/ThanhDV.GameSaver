using System.Collections.Generic;
using Newtonsoft.Json;

namespace ThanhDV.SaveKeeper.Common
{
    /// <summary>
    /// Convenience helpers for Unity-aware JSON serialization with Newtonsoft.Json.
    /// </summary>
    public static class JsonUtilities
    {
        /// <summary>
        /// Default set of Unity-type converters. Add these to your <see cref="JsonSerializerSettings.Converters"/>
        /// to enable correct serialization of UnityEngine types (Vector*, Quaternion, Color, etc.).
        /// </summary>
        public static readonly IList<JsonConverter> UnityConverter = new JsonConverter[]
        {
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new Vector2IntConverter(),
            new Vector3IntConverter(),
            new QuaternionConverter(),
            new ColorConverter(),
            new Color32Converter(),
            new RectConverter(),
            new BoundsConverter(),
            new LayerMaskConverter(),
            new Matrix4x4Converter()
        };
    }
}
