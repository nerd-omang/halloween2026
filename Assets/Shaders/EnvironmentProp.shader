// Background props: flat unlit color, optionally lit by the main light, with optional fog, flicker and wing flap.
// No shadows are cast or received and everything lives in UnityPerMaterial, so one shared material per look
// keeps the SRP Batcher happy. Animation phases come from the object's world position (_Time is global), so
// objects sharing a material still move out of step.
//   _LightAmount  0 = flat color (silhouettes, glow), 1 = half-lambert main light + ambient
//   _FogAmount    0 = ignore fog (far silhouettes), 1 = full linear fog
//   _FlickerAmount/_FlickerSpeed  candle-like brightness noise (pumpkin faces, house windows)
//   _FlapAmount/_FlapSpeed        wing tips (object |x|) rise and fall in object y (bats)
Shader "Fangame/EnvironmentProp"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.05, 0.03, 0.08, 1)
        _Intensity ("Intensity", Float) = 1
        _LightAmount ("Light Amount", Range(0, 1)) = 0
        _FogAmount ("Fog Amount", Range(0, 1)) = 0
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0
        _FlickerSpeed ("Flicker Speed", Float) = 8
        _FlapAmount ("Flap Amount (m per m of span)", Float) = 0
        _FlapSpeed ("Flap Speed", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Intensity;
                half _LightAmount;
                half _FogAmount;
                float _FlickerAmount;
                float _FlickerSpeed;
                float _FlapAmount;
                float _FlapSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                half flicker : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                float3 originWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float phase = dot(originWS, float3(1.7, 0.9, 2.3));
                float3 positionOS = input.positionOS.xyz;
                positionOS.y += sin(_Time.y * _FlapSpeed + phase) * abs(positionOS.x) * _FlapAmount;

                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                output.positionCS = pos.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                float t = _Time.y * _FlickerSpeed + phase;
                float noise = 0.6 * sin(t) + 0.3 * sin(t * 2.3 + 1.7) + 0.1 * sin(t * 5.1 + 4.1);
                output.flicker = 1.0 + _FlickerAmount * noise;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = _BaseColor.rgb * _Intensity * input.flicker;
                half3 color = albedo;
                if (_LightAmount > 0.0)
                {
                    float3 normalWS = normalize(input.normalWS);
                    Light mainLight = GetMainLight();
                    half ndl = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                    half3 lit = albedo * (mainLight.color * ndl + SampleSH(normalWS));
                    color = lerp(albedo, lit, _LightAmount);
                }
                half3 fogged = MixFog(color, input.fogFactor);
                return half4(lerp(color, fogged, _FogAmount), 1.0);
            }
            ENDHLSL
        }
    }
}
