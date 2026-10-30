using UnityEditor;
using UnityEngine;

public class HandSpriteImporter : AssetPostprocessor
{
    const string Root = "Assets/Art/";

    // Pivots line up the starting fist of each clip with the idle fist (middle clip).
    static readonly (string folder, Vector2 pivot)[] Pivots =
    {
        ("Assets/Art/Hand/spread/", new Vector2(0.5263f, 0.5467f)),
        ("Assets/Art/Hand/thumbs/", new Vector2(0.4899f, 0.5244f)),
        ("Assets/Art/Hand/blast/", new Vector2(0.5390f, 0.5762f)),
    };

    public override uint GetVersion()
    {
        return 2;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root)) return;

        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 256f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Compressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        foreach (var (folder, pivot) in Pivots)
        {
            if (!assetPath.StartsWith(folder)) continue;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
        }
        importer.SetTextureSettings(settings);
    }
}
