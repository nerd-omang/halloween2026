Shader "Fangame/DitherPixel"
{
    Properties
    {
        _PixelSize ("Pixel Size", Float) = 4
        _Levels ("Color Levels", Float) = 6
        _DitherStrength ("Dither Strength", Float) = 1
        _MatrixSize ("Dither Matrix Size (1 = blue noise, 2, 3, 4 or 8)", Float) = 4
        _DarkBias ("Dark Bias (1 = uniform, higher = coarser shadows)", Float) = 1.5
        _DarkLevels ("Dark Levels (steps in shadows; < Levels coarser, > Levels softer, = Levels off)", Float) = 5
        _DarkCutoff ("Dark Cutoff (luma where fine levels take over)", Float) = 0.35
        _FlatThreshold ("Flat Threshold (local contrast below this skips dithering, 0 = off)", Float) = 0
        [NoScaleOffset] _NoiseTex ("Blue Noise (128x128, 16-bit in RG)", 2D) = "gray" {}
        _AsciiMode ("ASCII Mode (1 = letters instead of dithering)", Float) = 0
        _AsciiSet ("ASCII Glyph Set (row of the glyph atlas)", Float) = 0
        _AsciiCell ("ASCII Cell Width (px of the scaled screen; height is twice this)", Float) = 8
        _AsciiEmpty ("ASCII Empty (perceptual brightness below which a cell stays empty)", Float) = 0.08
        _AsciiTop ("ASCII Top (perceptual brightness that gets the densest glyph)", Float) = 0.7
        _AsciiBackground ("ASCII Background (share of the cell color behind the letters, 0 = black)", Float) = 0
        [NoScaleOffset] _GlyphTex ("Glyph Atlas (AsciiGlyphGenerator)", 2D) = "black" {}
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
            TEXTURE2D(_GlyphTex);

            // Must match AsciiGlyphGenerator: tiles per set row, and the size of one tile in texels.
            #define GLYPH_COUNT 70
            #define GLYPH_W 8
            #define GLYPH_H 16

            CBUFFER_START(UnityPerMaterial)
                float _AsciiMode;
                float _AsciiSet;
                float _AsciiCell;
                float _AsciiEmpty;
                float _AsciiTop;
                float _AsciiBackground;
                float _PixelSize;
                float _Levels;
                float _DitherStrength;
                float _MatrixSize;
                float _DarkBias;
                float _DarkLevels;
                float _DarkCutoff;
                float _FlatThreshold;
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

            // Every cell becomes one letter: brighter cells get denser glyphs, cells darker than _AsciiEmpty stay
            // empty, and the letter takes the cell's own color.
            half4 Ascii(Varyings input)
            {
                float cellWidth = max(_AsciiCell, 2.0);
                float2 res = max(_ScaledScreenParams.xy / float2(cellWidth, cellWidth * 2.0), 1.0);
                float2 grid = input.texcoord * res;
                float2 cell = floor(grid);
                float2 local = grid - cell;
                float2 q = 0.25 / res;
                float2 mid = (cell + 0.5) / res;

                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, mid + float2(-q.x, -q.y)).rgb
                         + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, mid + float2(q.x, -q.y)).rgb
                         + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, mid + float2(-q.x, q.y)).rgb
                         + SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, mid + float2(q.x, q.y)).rgb;
                c *= 0.25;

                // The screen is linear, so a dim-looking scene has small values; judge brightness by eye (gamma).
                float luma = dot(c, float3(0.299, 0.587, 0.114));
                float bright = pow(max(luma, 0.0), 1.0 / 2.2);
                float t = saturate((bright - _AsciiEmpty) / max(_AsciiTop - _AsciiEmpty, 0.001));
                int glyph = bright <= _AsciiEmpty ? 0 : 1 + (int)floor(t * (GLYPH_COUNT - 1.001));

                int2 px = int2(min((int)(local.x * GLYPH_W), GLYPH_W - 1), min((int)(local.y * GLYPH_H), GLYPH_H - 1));
                float ink = LOAD_TEXTURE2D(_GlyphTex, int2(glyph * GLYPH_W + px.x, (int)(_AsciiSet + 0.5) * GLYPH_H + px.y)).r;

                // The letter carries the cell's color (dim cells give dim letters); the cell behind it stays dark.
                float3 letter = c / max(max(c.r, max(c.g, c.b)), 0.3);
                return half4(lerp(c * _AsciiBackground, letter, ink), 1.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_AsciiMode > 0.5) return Ascii(input);

                float2 res = max(_ScaledScreenParams.xy / max(_PixelSize, 1.0), 1.0);
                float2 cell = floor(input.texcoord * res);
                float2 uv = (cell + 0.5) / res;
                float3 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
                float3 lw = float3(0.299, 0.587, 0.114);

                float thr = lerp(0.5, Threshold(int2(cell), _MatrixSize), _DitherStrength);

                float steps = max(_Levels - 1.0, 1.0);
                float luma = dot(c, lw);
                float bias = max(_DarkBias, 1.0);
                float lf = pow(max(luma, 0.0), bias);
                float dsteps = max(_DarkLevels - 1.0, 1.0);
                float qf = floor(lf * steps + thr) / steps;
                float qc = floor(lf * dsteps + thr) / dsteps;
                float q = pow(saturate(lerp(qc, qf, smoothstep(0.0, max(_DarkCutoff, 0.001), luma))), 1.0 / bias);
                float3 result = c * (q / max(luma, 0.001));
                result /= max(1.0, max(result.r, max(result.g, result.b)));

                // Flat areas keep their original color instead of turning into dot noise.
                if (_FlatThreshold > 0.0)
                {
                    float2 d = 1.0 / res;
                    float contrast = 0.0;
                    contrast = max(contrast, abs(dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + float2(d.x, 0)).rgb, lw) - luma));
                    contrast = max(contrast, abs(dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv - float2(d.x, 0)).rgb, lw) - luma));
                    contrast = max(contrast, abs(dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv + float2(0, d.y)).rgb, lw) - luma));
                    contrast = max(contrast, abs(dot(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv - float2(0, d.y)).rgb, lw) - luma));
                    float detail = smoothstep(_FlatThreshold * 0.5, _FlatThreshold, contrast);
                    result = lerp(c, result, detail);
                }
                return half4(saturate(result), 1.0);
            }
            ENDHLSL
        }
    }
}
