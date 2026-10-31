using System;
using Newtonsoft.Json;
using Withme.Auth;

namespace Withme.Character
{
    [Serializable]
    public sealed class AppearanceParameters
    {
        [JsonProperty("earScale")] public float EarScale=1;
        [JsonProperty("earAngle")] public float EarAngle;
        [JsonProperty("tailScale")] public float TailScale=1;
        [JsonProperty("legLength")] public float LegLength=1;
        [JsonProperty("bodyLength")] public float BodyLength=1;
        [JsonProperty("headScale")] public float HeadScale=1;
        [JsonProperty("hueShift")] public float HueShift;
        [JsonProperty("brightness")] public float Brightness;
        public AppearanceParameters Copy() => (AppearanceParameters)MemberwiseClone();
        public void Validate()
        {
            Range(EarScale,.5f,1.5f);Range(EarAngle,-45,45);Range(TailScale,.5f,1.5f);Range(LegLength,.7f,1.3f);
            Range(BodyLength,.7f,1.3f);Range(HeadScale,.8f,1.2f);Range(HueShift,-30,30);Range(Brightness,-.3f,.3f);
        }
        static void Range(float v,float min,float max) { if(!float.IsFinite(v)||v<min||v>max)throw new ApiFailure("APPEARANCE_INVALID",0); }
    }
}
