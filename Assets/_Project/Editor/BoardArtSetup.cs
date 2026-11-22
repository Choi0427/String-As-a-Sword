using System.Linq;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Wires the delivered stone chessboard and the gameplay background layers into ArtSet.asset.
    /// Re-running rebuilds the material and prefab from the source files.
    /// </summary>
    public static class BoardArtSetup
    {
        private const string Board = "Assets/_Project/Art/Models/Board/", Background = "Assets/_Project/Art/UI/Background/";
        private const string PrefabPath = Board + "ChessBoard.prefab", MaterialPath = Board + "ChessBoard_Lit.mat";
        // Share of the model's outer width (x) and depth (z) covered by the 8x8 squares; the rest is the stone rim.
        // Measured from a top-down render: the square grid is centred, but the model is not square.
        private static readonly Vector2 PlayShare = new Vector2(.913f, .894f);
        // The marks sit at y 0.015-0.024; sinking the stone slightly keeps its rough top from poking through them.
        private const float TopOffset = -.01f;
        // Back to front, as delivered: 0 1 4 5 2 3. Layers 1, 4 and 5 float.
        private static readonly (int file, Vector2 sway, float period)[] Layers =
        {
            (0, Vector2.zero, 0), (1, new Vector2(12, 8), 7), (4, new Vector2(18, 10), 9),
            (5, new Vector2(10, 14), 11), (2, Vector2.zero, 0), (3, Vector2.zero, 0),
        };

        [MenuItem("String As a Sword/Art/Set Up Chessboard and Background")]
        public static void Run()
        {
            ConfigureTexture(Board + "ChessBoard_BaseColor.png", false);
            ConfigureTexture(Board + "ChessBoard_Normal.png", true);
            var material = BuildMaterial();
            var prefab = BuildPrefab(material);
            var art = PrototypeSceneSetup.EnsureArtSet();
            art.boardPrefab = prefab;
            art.useLighting = true;
            // Fitted to the gameplay design (1920x1080): the squares' far edge at y 352 (616 px wide), the near edge at
            // y 857 (862 px wide), with PrototypeView.BoardCenter (0, -19).
            art.cameraElevation = 43; art.cameraFieldOfView = 31; art.cameraSize = 4.9f;
            art.backgroundLayers = Layers.Select(l => new BackgroundLayer
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Background}BG_{l.file}.png"), sway = l.sway, period = l.period,
            }).ToArray();
            // The prologue's whole-board twins (A5-B), when delivered: same order and float as their battle layers.
            art.prologueBackgroundLayers = Layers.Select(l => new BackgroundLayer
            {
                sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Background}BG_Whole_{l.file}.png"), sway = l.sway, period = l.period,
            }).ToArray();
            if (!art.HasPrologueBackground) art.prologueBackgroundLayers = new BackgroundLayer[0];
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log($"Board art set up: {PrefabPath}, {art.backgroundLayers.Count(l => l.sprite != null)} background layers, " +
                $"{art.prologueBackgroundLayers.Count(l => l.sprite != null)} whole-board layers.");
        }
        private static void ConfigureTexture(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            // Full source size and high-quality compression (BC7 colour / BC5 normals): the default BC1 adds blocks and a green-magenta
            // cast to the grey stone. Kaiser mips keep the grain crisp on the far rows, which the tilted camera sees at a slant.
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }
        private static Material BuildMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, MaterialPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Board + "ChessBoard_BaseColor.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Board + "ChessBoard_Normal.png"));
            material.EnableKeyword("_NORMALMAP");
            material.SetColor("_BaseColor", Color.white);
            // Matte stone. Environment reflections off: with no reflection probe they mirror the default blue sky.
            material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .12f);
            material.SetFloat("_EnvironmentReflections", 0); material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            EditorUtility.SetDirty(material);
            return material;
        }
        private static GameObject BuildPrefab(Material material)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Board + "ChessBoard.fbx");
            var root = new GameObject("ChessBoard");
            try
            {
                // The fit goes on an unrotated parent: the FBX root is rotated, and a per-axis scale on it would map to the wrong axes.
                var fit = new GameObject("Fit").transform; fit.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, fit);
                var renderers = model.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers) renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                // Renderer bounds are stale right after instantiation; measure the vertices in the root's space instead.
                // (The FBX root carries a Z-up to Y-up rotation, so the mesh's own axes are not the board's.)
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
                // Scale so the 8x8 squares span 8 units, then centre the squares on the origin with the playing surface at y = 0.
                float x = 8 / (PlayShare.x * bounds.size.x), z = 8 / (PlayShare.y * bounds.size.z), y = (x + z) / 2;
                fit.localScale = new Vector3(x, y, z);
                fit.localPosition = new Vector3(-bounds.center.x * x, TopOffset - bounds.max.y * y, -bounds.center.z * z);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
