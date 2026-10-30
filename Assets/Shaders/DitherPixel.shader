Shader "Fangame/DitherPixel"
{
    Properties
    {
        _PixelSize ("Pixel Size", Float) = 4
        _Levels ("Color Levels", Float) = 6
        _DitherStrength ("Dither Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "DitherPixel"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _Levels;
                float _DitherStrength;
            CBUFFER_END

            static const float bayer2[4] = { 0, 2, 3, 1 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 res = max(_ScreenParams.xy / max(_PixelSize, 1.0), 1.0);
                float2 cell = floor(input.texcoord * res);
                float2 uv = (cell + 0.5) / res;
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                int2 p = int2(cell) % 2;
                float thr = lerp(0.5, (bayer2[p.y * 2 + p.x] + 0.5) / 4.0, _DitherStrength);

                float steps = max(_Levels - 1.0, 1.0);
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                float q = floor(luma * steps + thr) / steps;
                float3 result = c * (q / max(luma, 0.001));
                return half4(saturate(result), 1.0);
            }
            ENDHLSL
        }
    }
}
