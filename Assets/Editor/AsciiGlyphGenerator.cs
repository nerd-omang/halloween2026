using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes the glyph atlas used by the ASCII look of DitherPixel.shader.
//
// Layout (bottom-up like any Texture2D): one row of tiles per glyph set, GlyphCount tiles per row, left to right
// from empty to densest. Each tile is TileWidth x TileHeight; the shader reads it with point sampling, so the
// numbers here must match the constants in the shader. The set order must match Settings.GlyphSetCount.
public static class AsciiGlyphGenerator
{
    public const string TexturePath = "Assets/Textures/AsciiGlyphs.png";
    const string MaterialPath = "Assets/Materials/DitherPixel.mat";

    // Tiles per row. The hand-drawn sets have BaseCount steps and are stretched to GlyphCount; the standard set
    // is the 70 level ramp baked by Tools/Ascii/bake_standard_ramp.py.
    public const int GlyphCount = 70;
    const int BaseCount = 10;
    public const int TileWidth = 8;
    public const int TileHeight = 16;
    const int SetCount = 3; // fine (70 levels), text, blocks
    const string StandardRampPath = "Assets/Editor/AsciiStandardRamp.png";

    // 5x7 pixel glyphs, drawn 1 px wide and 2 px tall per pixel into the 8x16 tile.
    static readonly string[][] Letters =
    {
        null, // empty
        new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." }, // .
        new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." }, // -
        new[] { ".....", ".##..", ".##..", ".....", ".##..", ".##..", "....." }, // :
        new[] { ".....", ".....", "#####", ".....", "#####", ".....", "....." }, // =
        new[] { "..#..", "..#..", "..#..", "#####", "..#..", "..#..", "..#.." }, // +
        new[] { "##..#", "##.#.", "...#.", "..#..", ".#...", ".#.##", "#..##" }, // %
        new[] { ".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", "....." }, // *
        new[] { ".#.#.", ".#.#.", "#####", ".#.#.", "#####", ".#.#.", ".#.#." }, // #
        new[] { ".###.", "#...#", "#.###", "#.#.#", "#.###", "#....", ".###." }, // @
    };

    [MenuItem("Tools/Generate ASCII Glyphs")]
    public static void Generate()
    {
        // [set][glyph] -> TileWidth * TileHeight, row 0 at the bottom, ink 0..255. Set order = Settings glyph sets.
        var tiles = new byte[SetCount][][];
        tiles[0] = LoadStandard();
        tiles[1] = Stretch(BuildFrom5x7(Letters));
        tiles[2] = Stretch(BuildBlocks());

        int width = GlyphCount * TileWidth;
        int height = SetCount * TileHeight;
        var pixels = new Color32[width * height];
        for (int s = 0; s < SetCount; s++)
        {
            for (int g = 0; g < GlyphCount; g++)
            {
                for (int y = 0; y < TileHeight; y++)
                {
                    for (int x = 0; x < TileWidth; x++)
                    {
                        byte v = tiles[s][g][y * TileWidth + x];
                        pixels[(s * TileHeight + y) * width + g * TileWidth + x] = new Color32(v, v, v, 255);
                    }
                }
            }
        }

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
        File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null)
        {
            mat.SetTexture("_GlyphTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
        Debug.Log("AsciiGlyphGenerator: " + TexturePath);
    }

    static bool[] NewTile() => new bool[TileWidth * TileHeight];

    // Spreads a BaseCount-step set over GlyphCount steps by repeating the nearest tile.
    static byte[][] Stretch(bool[][] set)
    {
        var result = new byte[GlyphCount][];
        for (int g = 0; g < GlyphCount; g++)
        {
            var source = set[Mathf.RoundToInt(g * (BaseCount - 1) / (float)(GlyphCount - 1))];
            var tile = new byte[source.Length];
            for (int i = 0; i < tile.Length; i++) tile[i] = source[i] ? (byte)255 : (byte)0;
            result[g] = tile;
        }
        return result;
    }

    static byte[][] LoadStandard()
    {
        var strip = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!strip.LoadImage(File.ReadAllBytes(StandardRampPath)) || strip.width != GlyphCount * TileWidth || strip.height != TileHeight)
            throw new System.Exception("AsciiGlyphGenerator: " + StandardRampPath + " must be " + GlyphCount * TileWidth + "x" + TileHeight + "; run Tools/Ascii/bake_standard_ramp.py");
        var pixels = strip.GetPixels32();
        int stripWidth = strip.width;
        Object.DestroyImmediate(strip);

        var result = new byte[GlyphCount][];
        for (int g = 0; g < GlyphCount; g++)
        {
            var tile = new byte[TileWidth * TileHeight];
            for (int y = 0; y < TileHeight; y++)
                for (int x = 0; x < TileWidth; x++)
                    tile[y * TileWidth + x] = pixels[y * stripWidth + g * TileWidth + x].r;
            result[g] = tile;
        }
        return result;
    }

    // Pattern rows are listed top to bottom; the tile is stored bottom to top.
    static bool[][] BuildFrom5x7(string[][] glyphs)
    {
        var result = new bool[BaseCount][];
        for (int g = 0; g < BaseCount; g++)
        {
            var tile = NewTile();
            var rows = glyphs[g];
            if (rows != null)
            {
                for (int r = 0; r < 7; r++)
                {
                    for (int c = 0; c < 5; c++)
                    {
                        if (rows[r][c] != '#') continue;
                        for (int dy = 0; dy < 2; dy++)
                        {
                            int topRow = 1 + r * 2 + dy;
                            tile[(TileHeight - 1 - topRow) * TileWidth + 1 + c] = true;
                        }
                    }
                }
            }
            result[g] = tile;
        }
        return result;
    }

    // Shade blocks: coverage grows with an 8x8 Bayer order, like the light/medium/dark shades, up to a full block.
    static bool[][] BuildBlocks()
    {
        var result = new bool[BaseCount][];
        for (int g = 0; g < BaseCount; g++)
        {
            var tile = NewTile();
            float coverage = g / (float)(BaseCount - 1);
            for (int y = 0; y < TileHeight; y++)
            {
                for (int x = 0; x < TileWidth; x++)
                {
                    tile[y * TileWidth + x] = coverage >= 1f || Bayer8(x % 8, y % 8) < coverage * 64f;
                }
            }
            result[g] = tile;
        }
        return result;
    }

    static int Bayer8(int x, int y)
    {
        int a = x ^ y;
        int v = 0;
        for (int i = 0; i < 3; i++) v = (v << 2) | (((a >> i) & 1) << 1) | ((y >> i) & 1);
        return v;
    }
}
