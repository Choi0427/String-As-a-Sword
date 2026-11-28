using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the board crack material from the Board Crack shader and its baked crack map, and assigns it to the ArtSet.
    /// The material is referenced by an asset so the shader is included in standalone builds.
    /// </summary>
    public static class BoardCrackSetup
    {
        private const string Path = "Assets/_Project/Art/Materials/BoardCrack.mat", ShaderName = "String As a Sword/Board Crack";

        [MenuItem("String As a Sword/Art/Set Up Board Cracks")]
        public static void Run()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            var material = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (material == null) { material = new Material(Shader.Find(ShaderName)); AssetDatabase.CreateAsset(material, Path); }
            material.shader = Shader.Find(ShaderName);
            // The game drives these three on its own copy; the asset stays at "intact".
            material.SetFloat("_Crack", 0); material.SetFloat("_Light", 0); material.SetFloat("_Hole", 0);
            // The cracks themselves: grown once and baked, so every run breaks the same way. The copies the game makes keep it.
            material.SetTexture("_CrackMap", BoardCrackBaker.Bake());
            EditorUtility.SetDirty(material);
            art.boardCrack = material;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("Board cracks set up: BoardCrack.mat.");
        }
    }
}
