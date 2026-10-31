using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Withme.Auth;

namespace Withme.Character
{
    [JsonConverter(typeof(RigJointsJsonConverter))]
    public sealed class RigJoints
    {
        [JsonProperty("skeletonVersion")] public int SkeletonVersion = 1;
        [JsonProperty("space")] public string Space = "normalized";
        [JsonProperty("joints")] public Dictionary<string, float[]> Points = new Dictionary<string, float[]>();
        public static readonly string[] Names = { "nose", "neck", "tail_base", "tail_tip", "front_paw_l", "shoulder_l", "hind_paw_l", "hip_l", "ear_tip_l", "ear_base_l" };
        public void Validate()
        {
            if (SkeletonVersion != 1 || Space != "normalized" || Points == null || Names.Any(n => !Points.ContainsKey(n))) throw new ApiFailure("JOINTS_INVALID", 0);
            foreach (var p in Points.Values)
                if (p == null || p.Length != 3 || p.Any(v => float.IsNaN(v) || float.IsInfinity(v))) throw new ApiFailure("JOINTS_INVALID", 0);
        }
        public RigJoints Copy() { Validate(); return new RigJoints { SkeletonVersion = SkeletonVersion, Space = Space, Points = Points.ToDictionary(p => p.Key, p => (float[])p.Value.Clone()) }; }
        public static Vector3 ToUnity(float[] p) => new Vector3(-p[0], p[1], p[2]);
        public static float[] FromUnity(Vector3 p) => new[] { -p.x, p.y, p.z };
    }
    public sealed class RigJointsJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;
        public override bool CanConvert(Type type) => type == typeof(RigJoints);
        public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var json = Newtonsoft.Json.Linq.JObject.Load(reader);
            var value = new RigJoints();
            var version = json["skeletonVersion"];
            if (version != null && (version.Type != Newtonsoft.Json.Linq.JTokenType.Integer || version.Value<int>() != 1)) throw new ApiFailure("JOINTS_INVALID", 0);
            var space = json["space"];
            if (space != null && (space.Type != Newtonsoft.Json.Linq.JTokenType.String || space.Value<string>() != "normalized")) throw new ApiFailure("JOINTS_INVALID", 0);
            if (!(json["joints"] is Newtonsoft.Json.Linq.JObject points)) throw new ApiFailure("JOINTS_INVALID", 0);
            foreach (var point in points.Properties())
            {
                if (!(point.Value is Newtonsoft.Json.Linq.JArray xyz) || xyz.Count != 3 || xyz.Any(v => v.Type != Newtonsoft.Json.Linq.JTokenType.Integer && v.Type != Newtonsoft.Json.Linq.JTokenType.Float)) throw new ApiFailure("JOINTS_INVALID", 0);
                value.Points[point.Name] = xyz.Select(v => v.Value<float>()).ToArray();
            }
            value.Validate(); return value;
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
