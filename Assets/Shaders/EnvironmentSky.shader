// Night-sky backdrop on a big arc behind everything. Vertical gradient by world height (fog color under the
// horizon, a warm glow just above it so dark silhouettes read, then up to the sky color) plus sparse
// twinkling stars from a hash of the view direction. Ignores fog; unlit.
Shader "Fangame/EnvironmentSky"
{
    Properties
    {
        _GroundColor ("Below Horizon", Color) = (0.12, 0.06, 0.18, 1)
        _HorizonColor ("Horizon Glow", Color) = (0.42, 0.18, 0.28, 1)
        _MidColor ("Mid Sky", Color) = (0.2, 0.09, 0.26, 1)
        _TopColor ("Top Sky", Color) = (0.12, 0.06, 0.18, 1)
        _GlowHeight ("Glow Peak Height (m)", Float) = 2.5
        _MidHeight ("Mid Height (m)", Float) = 12
        _TopHeight ("Top Height (m)", Float) = 28
        _StarDensity ("Star Density", Range(0, 0.1)) = 0.015
        _StarColor ("Star Color", Color) = (1, 0.95, 0.8, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GroundColor;
                half4 _HorizonColor;
                half4 _MidColor;
                half4 _TopColor;
                float _GlowHeight;
                float _MidHeight;
                float _TopHeight;
                float _StarDensity;
                half4 _StarColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                return output;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float y = input.positionWS.y;
                half3 color = lerp(_GroundColor.rgb, _HorizonColor.rgb, saturate((y + 1.0) / (_GlowHeight + 1.0)));
                color = lerp(color, _MidColor.rgb, saturate((y - _GlowHeight) / (_MidHeight - _GlowHeight)));
                color = lerp(color, _TopColor.rgb, saturate((y - _MidHeight) / (_TopHeight - _MidHeight)));

                float3 dir = normalize(input.positionWS - _WorldSpaceCameraPos);
                float2 sphere = float2(atan2(dir.x, dir.z), dir.y) * 90.0;
                float2 cell = floor(sphere);
                float h = Hash(cell);
                float2 jitter = float2(Hash(cell + 7.3), Hash(cell + 3.1)) * 0.5 + 0.25;
                float star = step(1.0 - _StarDensity, h) * smoothstep(0.32, 0.0, length(frac(sphere) - jitter));
                star *= 0.6 + 0.4 * sin(_Time.y * 2.0 + h * 60.0);
                star *= saturate((y - _MidHeight * 0.5) / _MidHeight);
                return half4(color + _StarColor.rgb * star, 1.0);
            }
            ENDHLSL
        }
    }
}
