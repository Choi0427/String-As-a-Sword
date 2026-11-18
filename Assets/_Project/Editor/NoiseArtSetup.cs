using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>Wires Art/UI/Noise/Noise.png into ArtSet.noiseSprite, so noise pickups float as that picture.</summary>
    public static class NoiseArtSetup
    {
        private const string Noise = "Assets/_Project/Art/UI/Noise/Noise.png";

        [MenuItem("String As a Sword/Art/Set Up Noise Sprite")]
        public static void Run()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Noise);
            // The grainy glow is a long soft alpha ramp; block compression would band it.
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.noiseSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Noise);
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("Noise sprite set up: " + Noise);
        }
    }
}
