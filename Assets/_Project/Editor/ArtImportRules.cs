using UnityEditor;

namespace StringAsASword.Editor
{
    /// <summary>First-import defaults for delivered art. Later Inspector changes are kept.</summary>
    public sealed class ArtImportRules : AssetPostprocessor
    {
        private const string Models = "Assets/_Project/Art/Models/", UI = "Assets/_Project/Art/UI/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Models) || !assetImporter.importSettingsMissing) return;
            var model = (ModelImporter)assetImporter;
            // 1 unit = 1 cell: ignore the DCC file's unit scale so a 1 m knight is one cell wide.
            model.globalScale = 1; model.useFileScale = false;
            model.importCameras = false; model.importLights = false;
            model.animationType = ModelImporterAnimationType.Generic;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(UI) || !assetImporter.importSettingsMissing) return;
            var texture = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_Regions.png")) { SetUpRegionMap(texture); return; }
            texture.textureType = TextureImporterType.Sprite; texture.spriteImportMode = SpriteImportMode.Single;
            texture.mipmapEnabled = false; texture.alphaIsTransparency = true;
            texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            // Full-screen art is delivered at 4K; the 2048 default would halve it on a 1080p screen.
            texture.maxTextureSize = 4096;
        }
        /// <summary>A baked lookup map (part ids and gradients), not a picture: exact values, no colour space, no blending between texels.</summary>
        public static void SetUpRegionMap(TextureImporter texture)
        {
            texture.textureType = TextureImporterType.Default; texture.sRGBTexture = false;
            texture.mipmapEnabled = false; texture.alphaIsTransparency = false;
            texture.filterMode = UnityEngine.FilterMode.Point; texture.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            texture.textureCompression = TextureImporterCompression.Uncompressed; texture.maxTextureSize = 4096;
        }
    }
}
