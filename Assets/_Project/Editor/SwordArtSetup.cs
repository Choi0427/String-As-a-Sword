using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Wires the delivered sword (Meshy "Obsidian Stiletto", the torn string in the prologue) into ArtSet.asset.
    /// Re-running rebuilds the mask texture, material and prefab from the source files.
    /// </summary>
    public static class SwordArtSetup
    {
        private const string Sword = "Assets/_Project/Art/Models/Sword/";
        private const string PrefabPath = Sword + "Sword.prefab", MaterialPath = Sword + "Sword_Lit.mat", MaskPath = Sword + "Sword_MetallicSmoothness.png";
        // Hilt to tip, in board units (a cell is 1, the knight 1.2 tall).
        private const float Length = 1.05f;

        [MenuItem("String As a Sword/Art/Set Up Sword Model")]
        public static void Run()
        {
            Import(Sword + "Sword_BaseColor.png", TextureImporterType.Default, true);
            Import(Sword + "Sword_Normal.png", TextureImporterType.NormalMap, false);
            BuildMask();
            var prefab = BuildPrefab(BuildMaterial());
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.swordPrefab = prefab;
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Sword art set up: {PrefabPath}.");
        }
        private static void Import(string path, TextureImporterType type, bool srgb, bool readable = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = type; importer.sRGBTexture = srgb; importer.maxTextureSize = 2048; importer.isReadable = readable;
            importer.SaveAndReimport();
        }
        /// <summary>URP Lit reads metallic from R and smoothness from A of one map: metallic into R, 1 - roughness into A.</summary>
        private static void BuildMask()
        {
            string metallicPath = Sword + "Sword_Metallic.png", roughnessPath = Sword + "Sword_Roughness.png";
            Import(metallicPath, TextureImporterType.Default, false, true);
            Import(roughnessPath, TextureImporterType.Default, false, true);
            var metallic = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath);
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughnessPath);
            int w = metallic.width, h = metallic.height;
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w, v = (y + .5f) / h;
                    pixels[y * w + x] = new Color(metallic.GetPixelBilinear(u, v).r, 0, 0, 1 - roughness.GetPixelBilinear(u, v).r);
                }
            var mask = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            mask.SetPixels(pixels); mask.Apply();
            File.WriteAllBytes(MaskPath, mask.EncodeToPNG());
            Object.DestroyImmediate(mask);
            Import(metallicPath, TextureImporterType.Default, false);
            Import(roughnessPath, TextureImporterType.Default, false);
            AssetDatabase.ImportAsset(MaskPath);
            Import(MaskPath, TextureImporterType.Default, false);
        }
        private static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Sword + "Sword_BaseColor.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Sword + "Sword_Normal.png"));
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1);
            EditorUtility.SetDirty(material);
            return material;
        }
        /// <summary>
        /// Pivot at the middle of the grip, blade up (+Y), flat of the blade facing Z. The grip is found from the mesh: the
        /// crossguard is the widest slice along the long axis, the pommel the nearer end, and the grip lies between them.
        /// </summary>
        private static GameObject BuildPrefab(Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Sword + "Sword.fbx");
            var root = new GameObject("Sword");
            try
            {
                var fit = new GameObject("Fit").transform; fit.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, fit);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var points = model.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                {
                    var toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    return filter.sharedMesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v));
                }).ToArray();
                var bounds = new Bounds(points[0], Vector3.zero);
                foreach (var p in points) bounds.Encapsulate(p);
                // The long axis, and which way along it the hilt lies.
                int axis = bounds.size.x >= bounds.size.y && bounds.size.x >= bounds.size.z ? 0 : bounds.size.y >= bounds.size.z ? 1 : 2;
                float min = bounds.min[axis], max = bounds.max[axis], length = max - min;
                const int Slices = 64;
                var width = new float[Slices];
                foreach (var p in points)
                {
                    int i = Mathf.Clamp((int)((p[axis] - min) / length * Slices), 0, Slices - 1);
                    for (int k = 0; k < 3; k++) if (k != axis) width[i] = Mathf.Max(width[i], Mathf.Abs(p[k] - bounds.center[k]));
                }
                int guard = System.Array.IndexOf(width, width.Max());
                bool hiltAtMax = guard >= Slices / 2;
                float guardAt = min + (guard + .5f) / Slices * length, pommelAt = hiltAtMax ? max : min;
                float gripAt = (guardAt + pommelAt) * .5f;
                var hiltDirection = Vector3.zero; hiltDirection[axis] = hiltAtMax ? 1 : -1;
                var grip = bounds.center; grip[axis] = gripAt;
                // Turn the blade (from hilt towards tip) onto +Y, then put the grip on the pivot and scale to length.
                var turn = Quaternion.FromToRotation(-hiltDirection, Vector3.up);
                float scale = Length / length;
                fit.localRotation = turn;
                fit.localScale = Vector3.one * scale;
                fit.localPosition = -(turn * grip) * scale;
                Debug.Log($"Sword model bounds before fit: {bounds.size}, long axis {axis}, hilt at {(hiltAtMax ? "max" : "min")}, guard slice {guard}/{Slices}, scale {scale}.");
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
