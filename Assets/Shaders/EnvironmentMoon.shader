// Full moon on a camera-facing quad (UV in -1..1): a bright disc with soft maria and a wide additive halo.
// Unlit, ignores fog, drawn after the sky and behind nearer silhouettes via the depth test.
Shader "Fangame/EnvironmentMoon"
{
    Properties
    {
        _MoonColor ("Moon Color", Color) = (0.98, 0.93, 0.72, 1)
        _HaloColor ("Halo Color", Color) = (0.55, 0.42, 0.22, 1)
        _DiscRadius ("Disc Radius (of quad half size)", Range(0.05, 0.6)) = 0.26
        _HaloPower ("Halo Falloff", Float) = 2.5
        _Maria ("Maria Darkness", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "Moon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MoonColor;
                half4 _HaloColor;
                float _DiscRadius;
                float _HaloPower;
                float _Maria;
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
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv);
                float halo = pow(saturate(1.0 - r), _HaloPower);
                float edge = fwidth(r) * 1.5;
                float disc = 1.0 - smoothstep(_DiscRadius - edge, _DiscRadius + edge, r);
                float2 p = input.uv / _DiscRadius;
                float blotch = sin(p.x * 5.0 + sin(p.y * 4.0)) * sin(p.y * 6.0 - p.x * 2.0) + 0.5 * sin(p.x * 11.0 + p.y * 7.0);
                float maria = smoothstep(0.2, 0.9, blotch) * _Maria;
                half3 color = _HaloColor.rgb * halo + _MoonColor.rgb * (1.0 - maria) * disc;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
