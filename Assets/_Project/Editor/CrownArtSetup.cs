using System.IO;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the boss crown from the HP-state pictures in Art/UI/Boss/CrownStates (0.png = whole crown, 4.png = every blade
    /// spent; 1-3 are the steps in between and are only used for checking). Writes Crown.png (0.png) and Crown_Spent.png
    /// (4.png moved onto 0.png's canvas: the exports are cropped differently), bakes the region map, builds Crown.mat on the
    /// Crown UI shader and wires all of it into ArtSet.asset. Re-run it whenever the pictures or CrownLayout change. It also
    /// writes a grey-scale picture of the parts to Logs/CrownRegions.png for checking the split by eye.
    /// </summary>
    public static class CrownArtSetup
    {
        private const string Crown = "Assets/_Project/Art/UI/Boss/Crown.png", Regions = "Assets/_Project/Art/UI/Boss/Crown_Regions.png";
        private const string Spent = "Assets/_Project/Art/UI/Boss/Crown_Spent.png", States = "Assets/_Project/Art/UI/Boss/CrownStates/";
        private const string MaterialPath = "Assets/_Project/Art/Materials/Crown.mat", ShaderName = "String As a Sword/Crown UI";
        public const string DebugPicture = "Logs/CrownRegions.png";
        /// <summary>The knight's black: the mean of the darker half of Knight_BaseColor.jpg (23, 22, 20).</summary>
        public static readonly Color KnightBlack = new Color32(23, 22, 20, 255);

        [MenuItem("String As a Sword/Art/Set Up Boss Crown")]
        public static void Run()
        {
            if (File.Exists(States + "0.png") && File.Exists(States + "4.png")) BuildFromStates();
            var source = new Texture2D(2, 2);
            source.LoadImage(File.ReadAllBytes(Crown));
            var map = CrownLayout.Bake(source.GetPixels32(), source.width, source.height, out int width, out int height);
            var crownPixels = source.GetPixels32(); int crownWidth = source.width;
            Object.DestroyImmediate(source);

            var baked = new Texture2D(width, height, TextureFormat.RGBA32, false);
            baked.SetPixels32(map); baked.Apply();
            File.WriteAllBytes(Regions, baked.EncodeToPNG());
            Object.DestroyImmediate(baked);
            WriteDebugPicture(map, crownPixels, crownWidth, width, height);

            AssetDatabase.ImportAsset(Regions, ImportAssetOptions.ForceUpdate);
            var regionImporter = (TextureImporter)AssetImporter.GetAtPath(Regions);
            ArtImportRules.SetUpRegionMap(regionImporter); regionImporter.SaveAndReimport();
            foreach (var path in new[] { Crown, Spent })
            {
                if (!File.Exists(path)) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                // The glow is a long soft alpha ramp; block compression would band it.
                importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find(ShaderName)); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = Shader.Find(ShaderName);
            material.SetTexture("_RegionTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Regions));
            material.SetTexture("_SpentTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Spent));
            material.SetColor("_KnightBlack", KnightBlack);
            EditorUtility.SetDirty(material);

            var art = PrototypeSceneSetup.EnsureArtSet();
            art.crown = AssetDatabase.LoadAssetAtPath<Sprite>(Crown);
            art.crownRegions = AssetDatabase.LoadAssetAtPath<Texture2D>(Regions);
            art.crownSpent = AssetDatabase.LoadAssetAtPath<Texture2D>(Spent);
            art.crownMaterial = material;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Boss crown set up: {Regions} ({width}x{height}), {Spent}, Crown.mat. Part check picture: {DebugPicture}.");
        }
        // Crown.png = 0.png as it is; Crown_Spent.png = 4.png shifted onto 0.png's canvas so the two overlap exactly.
        private static void BuildFromStates()
        {
            var full = new Texture2D(2, 2); full.LoadImage(File.ReadAllBytes(States + "0.png"));
            var spent = new Texture2D(2, 2); spent.LoadImage(File.ReadAllBytes(States + "4.png"));
            var aligned = CrownLayout.AlignOnto(full.GetPixels32(), full.width, full.height, spent.GetPixels32(), spent.width, spent.height, out int dx, out int dy);
            File.Copy(States + "0.png", Crown, true);
            var output = new Texture2D(full.width, full.height, TextureFormat.RGBA32, false);
            output.SetPixels32(aligned); output.Apply();
            File.WriteAllBytes(Spent, output.EncodeToPNG());
            Debug.Log($"Crown states: 4.png ({spent.width}x{spent.height}) moved by ({dx}, {dy}) onto 0.png ({full.width}x{full.height}).");
            Object.DestroyImmediate(full); Object.DestroyImmediate(spent); Object.DestroyImmediate(output);
        }
        // Body grey, blades in rising greys with the tip-to-base gradient, seams light with the right-to-left gradient.
        private static void WriteDebugPicture(Color32[] map, Color32[] crown, int crownWidth, int width, int height)
        {
            var pixels = new Color32[map.Length];
            for (int i = 0; i < map.Length; i++)
            {
                int id = CrownLayout.DecodeId(map[i].r);
                float level = id == CrownLayout.Body ? .25f : id < CrownLayout.FirstSeam ? .35f + .1f * id - .15f * map[i].g / 255f : .95f - .3f * map[i].b / 255f;
                float alpha = crown[Mathf.Min(crown.Length - 1, (i / width) * 2 * crownWidth + (i % width) * 2)].a / 255f;
                byte value = (byte)(255 * level * (.3f + .7f * alpha));
                // Seams get a faint warm cast only in this check picture, so they cannot be mistaken for blades.
                pixels[i] = id >= CrownLayout.FirstSeam ? new Color32(value, value, (byte)(value * .55f), 255) : new Color32(value, value, value, 255);
            }
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            picture.SetPixels32(pixels); picture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(DebugPicture));
            File.WriteAllBytes(DebugPicture, picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
        }
    }
}
