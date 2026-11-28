using System.Linq;
using StringAsASword.Combat;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Builds the boss's summoned chess pieces (Meshy models) as prefabs: one tone matching the boss mask,
    /// pivot at the bottom centre, and heights in the real Staunton ratio (K 97 · Q 89 · B 79 · N 72 · R 66 · P 55 mm).
    /// Re-running rebuilds the materials and prefabs from the source files.
    /// </summary>
    public static class BossPieceArtSetup
    {
        private const string Root = "Assets/_Project/Art/Models/BossPieces/";
        // The boss knight stands as tall as the player's knight (KnightArtSetup.Height); the rest follow the real ratio.
        private const float UnitsPerMm = 1.2f / 72f;
        /// <summary>How solid the pieces are (1 = opaque, 0 = invisible): the material's alpha.</summary>
        public const float Opacity = .75f;
        private static readonly (ChessPiece piece, float mm)[] Pieces =
        {
            (ChessPiece.King, 97), (ChessPiece.Queen, 89), (ChessPiece.Bishop, 79),
            (ChessPiece.Knight, 72), (ChessPiece.Rook, 66), (ChessPiece.Pawn, 55),
        };

        public static string PrefabPath(ChessPiece piece) => $"{Root}{piece}/BossPiece_{piece}.prefab";
        public static float Height(ChessPiece piece) => Pieces.First(p => p.piece == piece).mm * UnitsPerMm;

        [MenuItem("String As a Sword/Art/Set Up Boss Chess Pieces")]
        public static void Run()
        {
            var art = PrototypeSceneSetup.EnsureArtSet();
            foreach (var (piece, _) in Pieces)
            {
                string dir = $"{Root}{piece}/", name = $"BossPiece_{piece}";
                var model = (ModelImporter)AssetImporter.GetAtPath(dir + name + ".fbx");
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                model.SaveAndReimport();
                Texture(dir + name + "_BaseColor.png", false);
                Texture(dir + name + "_Normal.png", true);
                var material = BuildMaterial(dir, name);
                var prefab = BuildPrefab(dir, name, material, Height(piece));
                switch (piece)
                {
                    case ChessPiece.Pawn: art.bossPawn = prefab; break;
                    case ChessPiece.Rook: art.bossRook = prefab; break;
                    case ChessPiece.Bishop: art.bossBishop = prefab; break;
                    case ChessPiece.Knight: art.bossKnight = prefab; break;
                    case ChessPiece.King: art.bossKing = prefab; break;
                    default: art.bossQueen = prefab; break;
                }
                Debug.Log($"Boss piece {piece}: {AssetDatabase.GetAssetPath(prefab)}, height {Height(piece):0.###}.");
            }
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
        }
        private static void Texture(string path, bool normal)
        {
            var texture = (TextureImporter)AssetImporter.GetAtPath(path);
            texture.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            texture.sRGBTexture = !normal; texture.maxTextureSize = 2048;
            texture.SaveAndReimport();
        }
        private static Material BuildMaterial(string dir, string name)
        {
            string path = dir + name + "_Lit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + name + "_BaseColor.png"));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + name + "_Normal.png"));
            material.EnableKeyword("_NORMALMAP");
            // Satin like the boss's mask: no metal, a soft highlight.
            material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .45f);
            // See-through, so the attack tiles and the knight stay readable behind a piece. Depth is still written so
            // a piece's own far side does not show through its near side.
            material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0); material.SetFloat("_AlphaClip", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 1);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", new Color(1, 1, 1, Opacity));
            EditorUtility.SetDirty(material);
            return material;
        }
        private static GameObject BuildPrefab(string dir, string name, Material material, float height)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(dir + name + ".fbx");
            var root = new GameObject(name);
            try
            {
                var fit = new GameObject("Fit").transform; fit.SetParent(root.transform, false);
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
                // Pivot at bottom centre: base on y = 0, centred on the cell.
                float scale = height / bounds.size.y;
                fit.localScale = Vector3.one * scale;
                fit.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                return PrefabUtility.SaveAsPrefabAsset(root, dir + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
