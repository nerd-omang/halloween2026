using UnityEditor;
using UnityEngine;

public class HandSpriteImporter : AssetPostprocessor
{
    const string Root = "Assets/Art/";

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
    }
}
