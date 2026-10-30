Shader "Fangame/DitherPixel"
{
    Properties
    {
        _PixelSize ("Pixel Size", Float) = 4
        _Levels ("Color Levels", Float) = 6
        _DitherStrength ("Dither Strength", Float) = 1
        _MatrixSize ("Dither Matrix Size (1 = blue noise, 2, 3, 4 or 8)", Float) = 4
        _DarkBias ("Dark Bias (1 = uniform, higher = coarser shadows)", Float) = 1.5
        _DarkLevels ("Dark Levels (coarse steps in shadows, >= Levels disables)", Float) = 5
        _DarkCutoff ("Dark Cutoff (luma where fine levels take over)", Float) = 0.35
        [NoScaleOffset] _NoiseTex ("Blue Noise (128x128, 16-bit in RG)", 2D) = "gray" {}
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

            TEXTURE2D(_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _Levels;
                float _DitherStrength;
                float _MatrixSize;
                float _DarkBias;
                float _DarkLevels;
                float _DarkCutoff;
            CBUFFER_END

            float Bayer(int2 p, int bits)
            {
                int a = p.x ^ p.y;
                int v = 0;
                for (int i = 0; i < bits; i++)
                {
                    v = (v << 2) | (((a >> i) & 1) << 1) | ((p.y >> i) & 1);
                }
                return (v + 0.5) / (float)(1 << (2 * bits));
            }

            static const float matrix3[9] = { 0, 7, 3, 6, 5, 2, 4, 1, 8 };

            float Threshold(int2 cell, float size)
            {
                if (size < 1.5)
                {
                    float2 n = LOAD_TEXTURE2D(_NoiseTex, cell & 127).rg;
                    return (n.x * 255.0 * 256.0 + n.y * 255.0 + 0.5) / 65536.0;
                }
                if (size > 2.5 && size < 3.5)
                {
                    int2 q = int2(cell.x % 3, cell.y % 3);
                    return (matrix3[q.y * 3 + q.x] + 0.5) / 9.0;
                }
                int bits = size > 5.0 ? 3 : (size > 3.5 ? 2 : 1);
                return Bayer(cell & ((1 << bits) - 1), bits);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 res = max(_ScaledScreenParams.xy / max(_PixelSize, 1.0), 1.0);
                float2 cell = floor(input.texcoord * res);
                float2 uv = (cell + 0.5) / res;
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                float thr = lerp(0.5, Threshold(int2(cell), _MatrixSize), _DitherStrength);

                float steps = max(_Levels - 1.0, 1.0);
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                float bias = max(_DarkBias, 1.0);
                float lf = pow(max(luma, 0.0), bias);
                float dsteps = clamp(_DarkLevels - 1.0, 1.0, steps);
                float qf = floor(lf * steps + thr) / steps;
                float qc = floor(lf * dsteps + thr) / dsteps;
                float q = pow(saturate(lerp(qc, qf, smoothstep(0.0, max(_DarkCutoff, 0.001), luma))), 1.0 / bias);
                float3 result = c * (q / max(luma, 0.001));
                result /= max(1.0, max(result.r, max(result.g, result.b)));
                return half4(saturate(result), 1.0);
            }
            ENDHLSL
        }
    }
}
