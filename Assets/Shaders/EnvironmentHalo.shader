// Candle glow: additive camera-facing sprite. The mesh is a tiny quad at the glow center; the UVs carry the
// corner direction and the shader expands it in view space (size in meters, scaled by the object's scale).
// No texture: a squared radial falloff. Flickers with the same noise (and phase) as EnvironmentProp.
Shader "Fangame/EnvironmentHalo"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 0.45, 0.08, 1)
        _Intensity ("Intensity", Float) = 0.6
        _Size ("Radius (m)", Float) = 0.9
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.28
        _FlickerSpeed ("Flicker Speed", Float) = 9
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Intensity;
                float _Size;
                float _FlickerAmount;
                float _FlickerSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half flicker : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                float3 originWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float scale = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float2 corner = input.uv * 2.0 - 1.0;
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                centerVS.xy += corner * _Size * scale;

                Varyings output;
                output.positionCS = TransformWViewToHClip(centerVS);
                output.corner = corner;
                float t = _Time.y * _FlickerSpeed + dot(originWS, float3(1.7, 0.9, 2.3));
                float noise = 0.6 * sin(t) + 0.3 * sin(t * 2.3 + 1.7) + 0.1 * sin(t * 5.1 + 4.1);
                output.flicker = 1.0 + _FlickerAmount * noise;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float falloff = saturate(1.0 - length(input.corner));
                falloff *= falloff;
                return half4(_BaseColor.rgb * (_Intensity * falloff * input.flicker), 1.0);
            }
            ENDHLSL
        }
    }
}
