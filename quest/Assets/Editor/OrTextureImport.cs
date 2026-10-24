using UnityEditor;

// Operating-room PBR textures (Assets/Art/Textures/OR, CC0 sets from ambientCG): normal maps import as NormalMap,
// metallic-gloss and AO as linear data, colour maps as sRGB. All with mipmaps and anisotropic filtering.
sealed class OrTextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Art/Textures/OR/")) return;
        var importer = (TextureImporter)assetImporter;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 8;
        importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        importer.maxTextureSize = 1024;
        importer.alphaIsTransparency = false;
        if (assetPath.Contains("_Normal"))
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = assetPath.Contains("_Color");
        }
    }
}
