// Cobweb drawn procedurally on a quarter-disc mesh (apex at the origin, spanning +x/+y; UV = position):
// radial strands plus sagging rings, alpha-clipped so it needs no texture or sorting. The free edge sways in
// the vertex shader; the phase follows the object's world position. Flip a web with negative scale.
Shader "Fangame/EnvironmentCobweb"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.62, 0.6, 0.7, 1)
        _FogAmount ("Fog Amount", Range(0, 1)) = 0.3
        _Strands ("Strands", Float) = 5
        _Rings ("Rings", Float) = 4
        _Thickness ("Line Thickness", Range(0.01, 0.3)) = 0.07
        _SwayAmount ("Sway Amount (m)", Float) = 0.06
        _SwaySpeed ("Sway Speed", Float) = 1.1
    }
    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _FogAmount;
                float _Strands;
                float _Rings;
                float _Thickness;
                float _SwayAmount;
                float _SwaySpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                float3 originWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float phase = dot(originWS, float3(1.3, 0.7, 1.9));
                float3 positionOS = input.positionOS.xyz;
                float reach = length(input.uv);
                positionOS.z += sin(_Time.y * _SwaySpeed * PI + phase) * _SwayAmount * reach;
                positionOS.x += sin(_Time.y * _SwaySpeed * 1.7 + phase * 1.3) * _SwayAmount * 0.5 * reach * reach;

                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(positionOS);
                output.positionCS = pos.positionCS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                float spokes = atan2(input.uv.y, input.uv.x) / (PI * 0.5) * (_Strands - 1.0);
                float strand = abs(spokes - round(spokes)) / (_Strands - 1.0) * r;
                float sag = 1.0 - 0.12 * sin(frac(spokes) * PI);
                float ring = abs(frac(r * sag * _Rings + 0.5) - 0.5) / _Rings;
                clip(_Thickness * 0.35 - min(strand, ring));
                clip(1.0 - r);
                half3 fogged = MixFog(_BaseColor.rgb, input.fogFactor);
                return half4(lerp(_BaseColor.rgb, fogged, _FogAmount), 1.0);
            }
            ENDHLSL
        }
    }
}
