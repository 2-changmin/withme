Shader "Withme/CoatPreview"
{
    Properties
    {
        _BaseMap("Coat",2D)="white"{}
        _BaseColor("Color",Color)=(1,1,1,1)
        _HueShift("Hue",Float)=0
        _Brightness("Brightness",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; };
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;float4 _BaseColor;float _HueShift;float _Brightness;
            CBUFFER_END
            Varyings Vert(Attributes input)
            { Varyings output;output.positionCS=TransformObjectToHClip(input.positionOS.xyz);output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.uv=TRANSFORM_TEX(input.uv,_BaseMap);return output; }
            float3 RgbToHsv(float3 c)
            {
                float4 k=float4(0,-1.0/3,2.0/3,-1);
                float4 p=lerp(float4(c.bg,k.wz),float4(c.gb,k.xy),step(c.b,c.g));
                float4 q=lerp(float4(p.xyw,c.r),float4(c.r,p.yzx),step(p.x,c.r));
                float d=q.x-min(q.w,q.y);return float3(abs(q.z+(q.w-q.y)/(6*d+1e-10)),d/(q.x+1e-10),q.x);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
                float3 hsv=RgbToHsv(base.rgb);hsv.x=frac(hsv.x+_HueShift);hsv.z=saturate(hsv.z+_Brightness);
                float3 rgb=hsv.z*lerp(float3(1,1,1),saturate(abs(frac(hsv.xxx+float3(0,2.0/3,1.0/3))*6-3)-1),hsv.y);
                float3 n=normalize(input.normalWS);Light light=GetMainLight();
                return half4(rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))),base.a);
            }
            ENDHLSL
        }
    }
}
