using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the TMP font asset for the delivered menu and story font (학교안심 곧은제목 B, Figma "Hakgyoansim Godeunjemok B")
    /// and assigns it to ArtSet.menuFont. Re-running rebuilds the asset from the TTF.
    /// </summary>
    public static class MenuFontSetup
    {
        private const string Source = "Assets/_Project/Art/Fonts/HakgyoansimGodeunjemokB.ttf";
        private const string AssetPath = "Assets/_Project/Art/Fonts/HakgyoansimGodeunjemokB SDF.asset";

        [MenuItem("String As a Sword/Art/Set Up Menu Font")]
        public static void Run()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(Source);
            if (font == null) { Debug.LogError($"Missing {Source}."); return; }
            AssetDatabase.DeleteAsset(AssetPath);
            // Dynamic: Korean has too many glyphs to bake, so the atlas fills from the TTF as text appears.
            // 1024 pages with multi-atlas: menu and story text use a few hundred glyphs, and the asset stays small in git.
            var asset = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = "HakgyoansimGodeunjemokB SDF";
            AssetDatabase.CreateAsset(asset, AssetPath);
            // The atlas and material must live inside the asset, or they are lost on reload and in builds.
            asset.atlasTextures[0].name = asset.name + " Atlas";
            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            // Ship it empty: glyphs are added at runtime, and the editor's play sessions do not bake into builds.
            asset.ClearFontAssetData(true);
            var serialized = new SerializedObject(asset);
            var clearOnBuild = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if (clearOnBuild != null) { clearOnBuild.boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.menuFont = asset;
            EditorUtility.SetDirty(asset); EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Menu font set up: {AssetPath}");
        }
    }
}
