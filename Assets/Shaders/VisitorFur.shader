// Matte fur for the visitors' low-poly bodies. No specular; soft wrap lighting and a fuzzy rim.
// The surface is broken into round fur clumps with 3D cellular noise in object space (no UVs, so the
// marking atlas on _BaseMap is unaffected): each clump bends the normal like a soft pillow, the
// creases between clumps are lightly darkened, and a fine fiber grain keeps it from reading as stone.
// Clumps are kept large so they survive the pixel/dither pass.
// Partial dye (disguises): with _DyeCoverage > 0, whole clumps take _DyeColor instead of _BaseColor,
// chosen in large patches so it looks half-painted. Extremities (feet, lower belly, limbs, snout, tail)
// keep more of the original color, so a bit always shows through even at high coverage.
Shader "Fangame/VisitorFur"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _FurScale ("Clumps per Meter", Float) = 9
        _FurStrength ("Clump Bulge", Range(0, 2)) = 0.9
        _Crease ("Crease Darkening", Range(0, 1)) = 0.25
        _Fiber ("Fiber Grain", Range(0, 1)) = 0.25
        _ColorVariation ("Per-Clump Tint", Range(0, 0.5)) = 0.06
        _Wrap ("Light Wrap", Range(0, 1)) = 0.5
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.5
        _DyeColor ("Dye Color", Color) = (1, 1, 1, 1)
        _DyeCoverage ("Dye Coverage (0 = off)", Range(0, 1)) = 0
        _DyeProtect ("Extremities Keep Color", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _FurScale;
            half _FurStrength;
            half _Crease;
            half _Fiber;
            half _ColorVariation;
            half _Wrap;
            half _RimPower;
            half _RimStrength;
            half4 _DyeColor;
            half _DyeCoverage;
            half _DyeProtect;
        CBUFFER_END

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x),
                             lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x),
                             lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float3 Hash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        // Cellular noise: distance to the nearest and second-nearest clump center,
        // the offset from the nearest center to p, and a random id for that clump.
        void Clumps(float3 p, out float f1, out float f2, out float3 fromCenter, out float id)
        {
            float3 cell = floor(p);
            float3 local = frac(p);
            f1 = 8.0;
            f2 = 8.0;
            fromCenter = 0;
            id = 0;
            [unroll] for (int z = -1; z <= 1; z++)
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float3 g = float3(x, y, z);
                float3 toCenter = g + Hash33(cell + g) - local;
                float d = dot(toCenter, toCenter);
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    fromCenter = -toCenter;
                    id = Hash13(cell + g);
                }
                else if (d < f2)
                {
                    f2 = d;
                }
            }
            f1 = sqrt(f1);
            f2 = sqrt(f2);
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half3 WrapDiffuse(Light light, float3 normalWS)
            {
                half ndl = saturate((dot(normalWS, light.direction) + _Wrap) / (1.0 + _Wrap));
                return light.color * ndl * light.distanceAttenuation * light.shadowAttenuation;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Each clump is a soft pillow (gaussian bump): the normal tilts most halfway out and
                // flattens again at the border, so neighbouring clumps blend instead of meeting at a hard
                // rock-like edge. Clumps are stretched vertically (object Y) like hanging tufts.
                // The pattern sticks to the object, not the world.
                float f1, f2, id;
                float3 fromCenter;
                Clumps(input.positionOS * _FurScale * float3(1.0, 0.6, 1.0), f1, f2, fromCenter, id);
                float3 bulge = fromCenter * exp(-3.0 * f1 * f1) * 2.0;
                // Fine fiber grain on top, so the surface reads as hair rather than smooth stone.
                float3 q = input.positionOS * _FurScale * 5.0;
                float3 fiber = float3(ValueNoise(q), ValueNoise(q + 17.1), ValueNoise(q + 31.7)) * 2.0 - 1.0;
                float3 tilt = TransformObjectToWorldDir(_FurStrength * bulge + _Fiber * fiber, false);
                tilt -= normalWS * dot(tilt, normalWS);
                float3 furNormalWS = normalize(normalWS + tilt);

                half3 baseColor = _BaseColor.rgb;
                if (_DyeCoverage > 0.0)
                {
                    // Decide per clump (at its center): large blotches plus a little per-clump jitter.
                    float3 stretch = _FurScale * float3(1.0, 0.6, 1.0);
                    float3 centerOS = (input.positionOS * stretch - fromCenter) / stretch;
                    float blotch = saturate((ValueNoise(centerOS * 3.0 + 7.3) - 0.5) * 1.8 + 0.5);
                    float order = 0.7 * blotch + 0.3 * id;
                    // Extremities: feet / lower belly (low Y) and anything sticking out of the body ball
                    // (limbs, snout, tail; body center is at Y 0.42 with radius ~0.36 in object space).
                    float low = saturate((0.16 - centerOS.y) / 0.1);
                    float outside = saturate((length(centerOS - float3(0.0, 0.42, 0.0)) - 0.38) / 0.08);
                    float coverage = _DyeCoverage * (1.0 - _DyeProtect * max(low, outside));
                    baseColor = order < coverage ? _DyeColor.rgb : baseColor;
                }
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * baseColor;
                half crease = 1.0 - smoothstep(0.0, 0.5, f2 - f1);
                albedo *= (1.0 - _Crease * crease) * lerp(1.0 - _ColorVariation, 1.0, id);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting = WrapDiffuse(mainLight, furNormalWS);

                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    lighting += WrapDiffuse(GetAdditionalLight(lightIndex, input.positionWS), furNormalWS);
                LIGHT_LOOP_END
                #endif

                half3 ambient = SampleSH(furNormalWS);
                // Fuzzy rim: fur tips catching light at grazing angles. Uses the smooth normal for a clean falloff.
                half rim = pow(1.0 - saturate(dot(normalWS, viewWS)), _RimPower) * _RimStrength;
                half3 color = albedo * (lighting + ambient)
                            + albedo * rim * (mainLight.color * mainLight.shadowAttenuation * 0.5 + ambient);
                color = MixFog(color, input.fogFactor);
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
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
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
                return TransformObjectToHClip(positionOS.xyz);
            }

            half DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
