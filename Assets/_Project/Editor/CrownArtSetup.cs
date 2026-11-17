using System.IO;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Bakes the boss crown's region map from Art/UI/Boss/Crown.png, builds Crown.mat on the Crown UI shader and wires
    /// both into ArtSet.asset. Re-run it whenever Crown.png or CrownLayout changes. It also writes a grey-scale picture of
    /// the parts to Logs/CrownRegions.png for checking the split by eye.
    /// </summary>
    public static class CrownArtSetup
    {
        private const string Crown = "Assets/_Project/Art/UI/Boss/Crown.png", Regions = "Assets/_Project/Art/UI/Boss/Crown_Regions.png";
        private const string MaterialPath = "Assets/_Project/Art/Materials/Crown.mat", ShaderName = "String As a Sword/Crown UI";
        public const string DebugPicture = "Logs/CrownRegions.png";
        /// <summary>The knight's black: the mean of the darker half of Knight_BaseColor.jpg (23, 22, 20).</summary>
        public static readonly Color KnightBlack = new Color32(23, 22, 20, 255);

        [MenuItem("String As a Sword/Art/Set Up Boss Crown")]
        public static void Run()
        {
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
            var crownImporter = (TextureImporter)AssetImporter.GetAtPath(Crown);
            // The glow is a long soft alpha ramp; block compression would band it.
            crownImporter.textureCompression = TextureImporterCompression.CompressedHQ; crownImporter.SaveAndReimport();

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find(ShaderName)); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = Shader.Find(ShaderName);
            material.SetTexture("_RegionTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Regions));
            material.SetColor("_KnightBlack", KnightBlack);
            EditorUtility.SetDirty(material);

            var art = PrototypeSceneSetup.EnsureArtSet();
            art.crown = AssetDatabase.LoadAssetAtPath<Sprite>(Crown);
            art.crownRegions = AssetDatabase.LoadAssetAtPath<Texture2D>(Regions);
            art.crownMaterial = material;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Boss crown set up: {Regions} ({width}x{height}), Crown.mat. Part check picture: {DebugPicture}.");
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
