// Matte cloth for the ghost-sheet disguise. The hem flutters in the vertex shader (no Unity Cloth,
// which is CPU-only and too slow on single-threaded WebGL): the bottom moves most, the crown not at
// all. The wave phase follows the object's world position, so the flutter quickens naturally while
// the visitor walks and every visitor in line waves out of step.
// Ghost eyes: two dark ovals drawn procedurally on the front (+Z) of the sheet in its own object space,
// so they stick to the cloth when it leans or tumbles. The same eyes on every sheet (decoration, not a
// clue); _EyeSize 0 turns them off (used by the bump material over protruding parts).
Shader "Fangame/VisitorSheet"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.9, 0.9, 0.88, 1)
        _FlutterAmount ("Flutter Amount (m)", Float) = 0.03
        _FlutterSpeed ("Flutter Speed", Float) = 1.6
        _FlutterTop ("Flutter Starts Below (m)", Float) = 0.6
        _Wrap ("Light Wrap", Range(0, 1)) = 0.6
        _EyeColor ("Eye Color", Color) = (0.04, 0.04, 0.05, 1)
        _EyeHeight ("Eye Height (m)", Float) = 0.6
        _EyeSpacing ("Eye Spacing (m, center to center)", Float) = 0.22
        _EyeSize ("Eye Radius (m, 0 = none)", Float) = 0.045
        _EyeAspect ("Eye Height / Width", Float) = 1.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            float _FlutterAmount;
            float _FlutterSpeed;
            float _FlutterTop;
            half _Wrap;
            half4 _EyeColor;
            float _EyeHeight;
            float _EyeSpacing;
            float _EyeSize;
            float _EyeAspect;
        CBUFFER_END

        // Object-space displacement: outward/inward ripples plus a little swirl around the hem.
        float3 Flutter(float3 positionOS)
        {
            float weight = saturate(1.0 - positionOS.y / _FlutterTop);
            weight *= weight;
            float3 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
            float phase = _Time.y * _FlutterSpeed * TWO_PI + dot(originWS.xz, float2(3.1, 2.3));
            float angle = atan2(positionOS.x, positionOS.z);
            float2 radial = normalize(positionOS.xz + 1e-4);
            float ripple = sin(phase + angle * 3.0 + positionOS.y * 8.0)
                         + 0.5 * sin(phase * 1.7 - angle * 5.0);
            float swirl = sin(phase * 0.8 + angle * 2.0);
            float2 tangent = float2(-radial.y, radial.x);
            float2 offset = (radial * ripple + tangent * swirl * 0.5) * _FlutterAmount * weight;
            return positionOS + float3(offset.x, 0.0, offset.y);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(Flutter(input.positionOS.xyz));
                output.positionOS = input.positionOS.xyz;
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate((dot(normalWS, mainLight.direction) + _Wrap) / (1.0 + _Wrap));
                half3 lighting = mainLight.color * ndl * mainLight.shadowAttenuation + SampleSH(normalWS);
                half3 albedo = _BaseColor.rgb;
                if (_EyeSize > 0.0 && input.positionOS.z > 0.1)
                {
                    // Front view (object XY): distance to the nearer eye, in eye radii.
                    float2 local = float2(abs(input.positionOS.x) - _EyeSpacing * 0.5, input.positionOS.y - _EyeHeight);
                    float d = length(local / float2(1.0, _EyeAspect)) / _EyeSize;
                    float edge = max(fwidth(d), 1e-4);
                    albedo = lerp(albedo, _EyeColor.rgb, 1.0 - smoothstep(1.0 - edge, 1.0 + edge, d));
                }
                half3 color = MixFog(albedo * lighting, input.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(Flutter(input.positionOS.xyz));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(Flutter(positionOS.xyz));
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
