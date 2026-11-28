using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the TMP font assets for the delivered fonts and assigns them to the ArtSet. Re-running rebuilds them from the files.
    /// Menu and story: 학교안심 곧은제목 B (Figma "Hakgyoansim Godeunjemok B").
    /// Battle pause / game over: Orbit Regular (items) and Media Sans Extended Black (titles), plus the key guide sprite.
    /// </summary>
    public static class MenuFontSetup
    {
        private const string Fonts = "Assets/_Project/Art/Fonts/";
        private const string KeyGuide = "Assets/_Project/Art/UI/HUD/KeyGuide.png";
        private const string JAttackOff = "Assets/_Project/Art/UI/HUD/J_Attack_Off.png", JAttackOn = "Assets/_Project/Art/UI/HUD/J_Attack_On.png";

        [MenuItem("String As a Sword/Art/Set Up Menu Font")]
        public static void Run()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.menuFont = Build("HakgyoansimGodeunjemokB.ttf", "HakgyoansimGodeunjemokB SDF");
            Save(art);
        }

        [MenuItem("String As a Sword/Art/Set Up Battle Screens")]
        public static void RunBattleScreens()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.screenFont = Build("Orbit-Regular.ttf", "Orbit-Regular SDF");
            art.screenTitleFont = Build("MediaSansExtended-Black.otf", "MediaSansExtended-Black SDF");
            art.keyGuide = AssetDatabase.LoadAssetAtPath<Sprite>(KeyGuide);
            if (art.keyGuide == null) Debug.LogError($"Missing {KeyGuide} (import it as a Sprite).");
            art.jAttackOff = AssetDatabase.LoadAssetAtPath<Sprite>(JAttackOff);
            art.jAttackOn = AssetDatabase.LoadAssetAtPath<Sprite>(JAttackOn);
            Save(art);
        }

        [MenuItem("String As a Sword/Art/Set Up Outcome Font")]
        public static void RunOutcomeFont()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.outcomeFont = Build("ChakraPetch-Regular.ttf", "ChakraPetch-Regular SDF");
            Save(art);
        }

        [MenuItem("String As a Sword/Art/Set Up Countdown Font")]
        public static void RunCountdownFont()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            // Wider SDF padding: the countdown thickens the glyphs and draws a heavy stroke, which needs room in the field.
            art.countdownFont = Build("ChakraPetch-Bold.ttf", "ChakraPetch-Bold SDF", 14);
            Save(art);
        }

        // Builds the HIT / BLOCK font once, on the first editor load after the font file arrives.
        [InitializeOnLoadMethod]
        private static void SetUpOutcomeFontIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var art = AssetDatabase.LoadAssetAtPath<StringAsASword.Presentation.ArtSet>("Assets/_Project/Art/ArtSet.asset");
                if (art == null) return;
                if (art.outcomeFont == null && AssetDatabase.LoadAssetAtPath<Font>(Fonts + "ChakraPetch-Regular.ttf") != null) RunOutcomeFont();
                if (art.countdownFont == null && AssetDatabase.LoadAssetAtPath<Font>(Fonts + "ChakraPetch-Bold.ttf") != null) RunCountdownFont();
            };
        }

        private static TMP_FontAsset Build(string file, string name, int padding = 6)
        {
            string source = Fonts + file, path = Fonts + name + ".asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(source);
            if (font == null) { Debug.LogError($"Missing {source}."); return null; }
            AssetDatabase.DeleteAsset(path);
            // Dynamic: Korean has too many glyphs to bake, so the atlas fills from the font file as text appears.
            // 1024 pages with multi-atlas: menu text uses a few hundred glyphs, and the asset stays small in git.
            var asset = TMP_FontAsset.CreateFontAsset(font, 64, padding, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
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
            EditorUtility.SetDirty(asset);
            Debug.Log($"Font set up: {path}");
            return asset;
        }
        private static void Save(Object art) { EditorUtility.SetDirty(art); AssetDatabase.SaveAssets(); }
    }
}
