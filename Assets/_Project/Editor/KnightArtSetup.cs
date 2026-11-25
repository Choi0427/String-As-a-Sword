using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Wires the delivered knight (player) model into ArtSet.asset.
    /// Re-running rebuilds the material and prefab from the source files.
    /// </summary>
    public static class KnightArtSetup
    {
        private const string Knight = "Assets/_Project/Art/Models/Knight/";
        private const string PrefabPath = Knight + "Knight.prefab", MaterialPath = Knight + "Knight_Lit.mat", TexturePath = Knight + "Knight_BaseColor.jpg";
        // The knight stands in a 0.6 x 1.2 unit box; the model is scaled uniformly to this height.
        private const float Height = 1.2f;
        // Turn applied to the delivered model so it faces the far row (the boss).
        private const float Yaw = 0;

        [MenuItem("String As a Sword/Art/Set Up Knight Model")]
        public static void Run()
        {
            var texture = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            texture.textureType = TextureImporterType.Default; texture.sRGBTexture = true; texture.maxTextureSize = 2048;
            texture.SaveAndReimport();
            var prefab = BuildPrefab(BuildMaterial());
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.knightPrefab = prefab;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Knight art set up: {PrefabPath}.");
        }
        private static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .2f);
            EditorUtility.SetDirty(material);
            return material;
        }
        private static GameObject BuildPrefab(Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Knight + "Knight.fbx");
            var root = new GameObject("Knight");
            try
            {
                // The fit goes on a parent of the FBX root, which may carry its own axis-conversion rotation.
                var fit = new GameObject("Fit").transform; fit.SetParent(root.transform, false);
                fit.localRotation = Quaternion.Euler(0, Yaw, 0);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, fit);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                // Renderer bounds are stale right after instantiation; measure the vertices in the root's space instead.
                var bounds = new Bounds();
                bool first = true;
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    var toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var point = toRoot.MultiplyPoint3x4(vertex);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                    }
                }
                // Pivot at bottom centre: feet on y = 0, centred on the cell.
                float scale = Height / bounds.size.y;
                fit.localScale = Vector3.one * scale;
                fit.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                Debug.Log($"Knight model bounds before fit: {bounds.size}, scale {scale}.");
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
