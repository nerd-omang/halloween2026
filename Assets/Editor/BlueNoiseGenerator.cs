using System.IO;
using UnityEditor;
using UnityEngine;

public static class BlueNoiseGenerator
{
    public const string TexturePath = "Assets/Textures/BlueNoise128.png";
    const string MaterialPath = "Assets/Materials/DitherPixel.mat";
    const int Size = 128;
    const int Radius = 6;
    const float Sigma = 1.9f;

    [MenuItem("Tools/Generate Blue Noise")]
    public static void Generate()
    {
        int[] rank = VoidAndCluster(Size, 12345);
        int total = Size * Size;

        var pixels = new Color32[total];
        for (int i = 0; i < total; i++)
        {
            int v = Mathf.RoundToInt(rank[i] * 65535f / (total - 1));
            pixels[i] = new Color32((byte)(v >> 8), (byte)(v & 255), 0, 255);
        }

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
        tex.SetPixels32(pixels);
        Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
        File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null)
        {
            mat.SetTexture("_NoiseTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
        }
        Debug.Log("BlueNoiseGenerator: " + TexturePath);
    }

    // Ulichney void-and-cluster. Returns a rank 0..n*n-1 per cell, toroidal.
    static int[] VoidAndCluster(int n, int seed)
    {
        int total = n * n;
        var kernel = new float[(2 * Radius + 1) * (2 * Radius + 1)];
        for (int dy = -Radius; dy <= Radius; dy++)
            for (int dx = -Radius; dx <= Radius; dx++)
                kernel[(dy + Radius) * (2 * Radius + 1) + dx + Radius] =
                    Mathf.Exp(-(dx * dx + dy * dy) / (2f * Sigma * Sigma));

        var pattern = new bool[total];
        var energy = new float[total];

        void Splat(int idx, float sign)
        {
            int x0 = idx % n, y0 = idx / n;
            for (int dy = -Radius; dy <= Radius; dy++)
            {
                int y = (y0 + dy + n) % n;
                for (int dx = -Radius; dx <= Radius; dx++)
                {
                    int x = (x0 + dx + n) % n;
                    energy[y * n + x] += sign * kernel[(dy + Radius) * (2 * Radius + 1) + dx + Radius];
                }
            }
        }

        int Extreme(bool wantOnes, bool tightest)
        {
            int best = -1;
            float bestE = 0f;
            for (int i = 0; i < total; i++)
            {
                if (pattern[i] != wantOnes) continue;
                float e = energy[i];
                if (best < 0 || (tightest ? e > bestE : e < bestE)) { best = i; bestE = e; }
            }
            return best;
        }

        var rng = new System.Random(seed);
        int ones = total / 10;
        for (int placed = 0; placed < ones;)
        {
            int i = rng.Next(total);
            if (pattern[i]) continue;
            pattern[i] = true;
            Splat(i, 1f);
            placed++;
        }

        // Relax the initial pattern until the tightest cluster is also the largest void.
        for (int guard = 0; guard < total * 4; guard++)
        {
            int tight = Extreme(true, true);
            pattern[tight] = false;
            Splat(tight, -1f);
            int hole = Extreme(false, false);
            pattern[hole] = true;
            Splat(hole, 1f);
            if (hole == tight) break;
        }

        var rank = new int[total];

        // Phase 1: remove ones, tightest cluster first.
        var work = (bool[])pattern.Clone();
        var workEnergy = (float[])energy.Clone();
        for (int r = ones - 1; r >= 0; r--)
        {
            int tight = Extreme(true, true);
            rank[tight] = r;
            pattern[tight] = false;
            Splat(tight, -1f);
        }

        // Phase 2: restore initial pattern, then fill largest voids.
        pattern = work;
        energy = workEnergy;
        for (int r = ones; r < total; r++)
        {
            int hole = Extreme(false, false);
            rank[hole] = r;
            pattern[hole] = true;
            Splat(hole, 1f);
        }
        return rank;
    }
}
