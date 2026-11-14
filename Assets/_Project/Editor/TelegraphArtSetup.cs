using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the two attack tile materials from the Telegraph Tile shader and wires them into ArtSet.asset.
    /// The tiles are drawn in the shader, so there are no textures. Re-running resets both materials to these values.
    /// </summary>
    public static class TelegraphArtSetup
    {
        private const string Materials = "Assets/_Project/Art/Materials/", ShaderName = "String As a Sword/Telegraph Tile";

        [MenuItem("String As a Sword/Art/Set Up Telegraph Tiles")]
        public static void Run()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            // Normal: translucent white with a glow inside its border.
            // The board is lit and bright: a higher fill reads as solid white, a lower one disappears when
            // a full-board attack covers every cell, so the border glow carries most of the mark.
            art.normalMark = Build("TelegraphNormal", fill: .2f, glow: .9f, hatch: false);
            // Strong (cannot be guarded): a little more transparent, with diagonal hatching.
            art.strongMark = Build("TelegraphStrong", fill: .1f, glow: .7f, hatch: true);
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("Telegraph tiles set up: TelegraphNormal.mat, TelegraphStrong.mat.");
        }
        private static Material Build(string name, float fill, float glow, bool hatch)
        {
            string path = Materials + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(ShaderName)); AssetDatabase.CreateAsset(material, path); }
            material.shader = Shader.Find(ShaderName);
            material.SetColor("_Color", Color.white);
            material.SetFloat("_FillAlpha", fill);
            material.SetFloat("_GlowWidth", .18f); material.SetFloat("_GlowAlpha", glow);
            material.SetFloat("_Hatch", hatch ? 1 : 0);
            material.SetFloat("_HatchCount", 7); material.SetFloat("_HatchWidth", .3f); material.SetFloat("_HatchAlpha", .5f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
