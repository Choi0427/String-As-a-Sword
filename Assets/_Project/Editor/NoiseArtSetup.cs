using StringAsASword.Presentation;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Wires Art/UI/Noise/Noise.png into ArtSet.noiseSprite and builds NoiseSprite.mat on URP's Sprite-Unlit-Default
    /// (ArtSet.noiseMaterial), so noise pickups float as that picture and a build keeps the shader.
    /// </summary>
    public static class NoiseArtSetup
    {
        private const string Noise = "Assets/_Project/Art/UI/Noise/Noise.png", MaterialPath = "Assets/_Project/Art/Materials/NoiseSprite.mat";

        [MenuItem("String As a Sword/Art/Set Up Noise Sprite")]
        public static void Run()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Noise);
            // The grainy glow is a long soft alpha ramp; block compression would band it.
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find(BoardWorldPresenter.NoiseShader)); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.shader = Shader.Find(BoardWorldPresenter.NoiseShader);
            EditorUtility.SetDirty(material);
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.noiseSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Noise);
            art.noiseMaterial = material;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("Noise sprite set up: " + Noise + ", " + MaterialPath);
        }
    }
}
