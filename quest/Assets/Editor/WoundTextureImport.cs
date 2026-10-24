using UnityEditor;

// Wound-closure textures (Assets/Art/Textures/WoundClosure): normal maps import as NormalMap, the rest as
// colour textures with mipmaps and anisotropic filtering so the skin detail holds up at grazing angles.
sealed class WoundTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Art/Textures/WoundClosure/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        importer.maxTextureSize = 1024;
        if (assetPath.Contains("Normal"))
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = false;
        }
    }
}
