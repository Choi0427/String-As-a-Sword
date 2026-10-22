using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Core;
using StringAsASword.Player;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Replaceable 3D presentation. All cell, pickup and combat rules live in the domain.</summary>
    public sealed class BoardWorldPresenter : MonoBehaviour
    {
        private PrototypeGame game;
        private GameObject world;
        private Transform player, knightBody, selection, sword, shield;
        private Transform[] pickups;
        private Renderer[][] pickupRenderers;
        private Material noiseMaterial, dormantMaterial;
        private Material[] activeNoiseMaterials, dormantNoiseMaterials;
        private bool lastNoiseDormant;
        private readonly List<Object> resources = new List<Object>();
        private Mesh box;
        private Material edgeMaterial;
        private Material materialTemplate;
        private readonly GameObject[] warnings = new GameObject[64];
        private readonly GameObject[] impacts = new GameObject[64];
        private int lastClosedNote = -1;
        private float impactAge = 1;
        private ulong impactCells;
        private Vector3 moveFrom, moveTarget;
        private float moveAge = 1, attackAge = 1, guardAge = 1;
        private int guardNote = -1;
        private int lastSession = -1;
        public Camera BoardCamera { get; private set; }
        public RenderTexture Texture { get; private set; }
        public Transform PlayerTransform => player;
        public int TileCount { get; private set; }

        public void Initialize(PrototypeGame owner, UnityEngine.UI.RawImage output, Material template)
        {
            if (template == null || template.shader == null)
                throw new System.InvalidOperationException("Assign the board material to PrototypeView before building. Use String As a Sword > Prepare Windows Build.");
            materialTemplate = template;
            game = owner;
            world = new GameObject("Board World");
            // A separate render target keeps the board projection independent of HUD/screen aspect ratio.
            world.transform.position = new Vector3(1000, 0, 1000);
            Texture = new RenderTexture(960, 880, 24) { name = "Board View", antiAliasing = 4 };
            Texture.Create(); output.texture = Texture; resources.Add(Texture);
            BoardCamera = new GameObject("Board Camera", typeof(Camera)).GetComponent<Camera>();
            BoardCamera.transform.SetParent(world.transform, false);
            float elevation = game.Stage.CameraElevation;
            BoardCamera.transform.localRotation = Quaternion.Euler(elevation, 0, 0);
            BoardCamera.transform.localPosition = -(BoardCamera.transform.localRotation * Vector3.forward) * 14;
            BoardCamera.orthographic = true; BoardCamera.orthographicSize = 4.6f;
            BoardCamera.nearClipPlane = .1f; BoardCamera.farClipPlane = 30;
            BoardCamera.clearFlags = CameraClearFlags.SolidColor;
            BoardCamera.backgroundColor = new Color(.035f, .043f, .052f);
            BoardCamera.targetTexture = Texture; BoardCamera.allowHDR = false;
            box = BuildBox(); resources.Add(box);
            edgeMaterial = Material(new Color(.075f, .095f, .105f));
            var ivory = Material(new Color(.72f, .74f, .70f));
            var dark = Material(new Color(.15f, .20f, .22f));
            var gold = Material(new Color(.84f, .66f, .32f));
            var accent = Material(new Color(.40f, .85f, .73f));
            Box("Board Plinth", world.transform, new Vector3(0, -.30f, 0), new Vector3(8.40f, .38f, 8.40f), edgeMaterial);
            Box("Board Rim", world.transform, new Vector3(0, -.13f, 0), new Vector3(8.20f, .14f, 8.20f), gold);
            Box("Board Deck", world.transform, new Vector3(0, -.115f, 0), new Vector3(8, .08f, 8), edgeMaterial);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                Box($"Cell_{x}_{y}", world.transform, CellPosition(new GridPosition(x, y)) - Vector3.up * .05f,
                    new Vector3(.975f, .10f, .975f), (x + y) % 2 == 0 ? dark : ivory);
                if (y == 7)
                    Box($"AttackEdge_{x}", world.transform, new Vector3(x - 3.5f, .014f, 3.90f), new Vector3(.83f, .028f, .065f), gold);
                TileCount++;
            }
            selection = new GameObject("Player Cell Outline").transform; selection.SetParent(world.transform, false);
            Frame(selection, .91f, .026f, .042f, accent);
            BuildPlayer();
            BuildNoise();
            BuildTelegraphs();
            sword = new GameObject("Sword Swing").transform; sword.SetParent(player, false);
            var arc = sword.gameObject.AddComponent<LineRenderer>();
            arc.useWorldSpace = false; arc.sharedMaterial = gold; arc.startWidth = .10f; arc.endWidth = .018f;
            arc.positionCount = 18; arc.numCapVertices = 3;
            for (int i = 0; i < 18; i++)
            {
                float angle = Mathf.Lerp(-100, 95, i / 17f) * Mathf.Deg2Rad;
                arc.SetPosition(i, new Vector3(Mathf.Sin(angle) * .72f, .52f + i * .022f, Mathf.Cos(angle) * .68f));
            }
            sword.gameObject.SetActive(false);
            BuildShield();
            ResetVisuals();
        }
        public static Vector3 CellPosition(GridPosition cell) => new Vector3(cell.X - 3.5f, 0, cell.Y - 3.5f);
        public Vector2 ProjectToBoard(Vector3 local, Vector2 rectSize)
        {
            Vector3 point = BoardCamera.WorldToViewportPoint(world.transform.TransformPoint(local));
            return new Vector2((point.x - .5f) * rectSize.x, (point.y - .5f) * rectSize.y);
        }
        public void ResetVisuals()
        {
            moveAge = attackAge = guardAge = 1;
            guardNote = -1;
            moveFrom = moveTarget = CellPosition(game.Session.Position);
            player.localPosition = moveTarget; selection.localPosition = moveTarget;
            player.localScale = Vector3.one; sword.gameObject.SetActive(false); shield.gameObject.SetActive(false);
            impactAge = 1; impactCells = 0; lastClosedNote = -1;
            for (int i = 0; i < warnings.Length; i++) { warnings[i].SetActive(false); impacts[i].SetActive(false); }
            lastSession = game.SessionId;
        }
        public void ShowAction(ActionResult result)
        {
            if (!result.Accepted) return;
            if (result.Kind == PlayerActionKind.Attack) attackAge = 0;
            if (result.Kind == PlayerActionKind.Guard) { guardAge = 0; guardNote = result.Timing.BeatId; }
        }
        public void Render()
        {
            if (lastSession != game.SessionId) ResetVisuals();
            float dt = Time.unscaledDeltaTime;
            RenderTelegraphs(dt);
            Vector3 target = CellPosition(game.Session.Position);
            if (target != moveTarget) { moveFrom = player.localPosition; moveTarget = target; moveAge = 0; }
            moveAge += dt; attackAge += dt; guardAge += dt;
            float t = Mathf.Clamp01(moveAge / .13f);
            player.localPosition = Vector3.Lerp(moveFrom, moveTarget, Mathf.SmoothStep(0, 1, t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .13f);
            selection.localPosition = target;
            sword.gameObject.SetActive(attackAge < .23f);
            sword.localRotation = Quaternion.Euler(0, -80 + attackAge / .23f * 125, 0);
            bool guarding = guardAge < .34f && game.Battle.LastClosedNote < guardNote;
            shield.gameObject.SetActive(guarding);
            shield.localScale = Vector3.one * (.85f + .15f * Mathf.Sin(Mathf.Clamp01(guardAge / .34f) * Mathf.PI));
            player.localScale = new Vector3(1, guarding && guardAge < .18f ? .9f : 1, 1);
            for (int i = 0; i < pickups.Length; i++)
            {
                if (lastNoiseDormant != game.Noise.GoalReached)
                    foreach (var renderer in pickupRenderers[i]) renderer.sharedMaterials = game.Noise.GoalReached ? dormantNoiseMaterials : activeNoiseMaterials;
                bool active = i < game.Noise.Pickups.Count;
                pickups[i].gameObject.SetActive(active);
                if (!active) continue;
                pickups[i].localPosition = CellPosition(game.Noise.Pickups[i].Cell) + Vector3.up * (.12f + .025f * Mathf.Sin(Time.unscaledTime * 3 + i));
            }
            lastNoiseDormant = game.Noise.GoalReached;
        }
        private void BuildTelegraphs()
        {
            var dark = Material(new Color(.045f, .055f, .065f));
            var white = Material(new Color(.97f, .97f, .94f));
            Mesh warning = TelegraphMesh(false), impact = TelegraphMesh(true);
            resources.Add(warning); resources.Add(impact);
            var materials = new[] { dark, white };
            for (int i = 0; i < 64; i++)
            {
                warnings[i] = TelegraphCell("Warning", i, warning, materials);
                impacts[i] = TelegraphCell("Impact", i, impact, materials);
            }
        }
        private GameObject TelegraphCell(string name, int index, Mesh mesh, Material[] materials)
        {
            var item = new GameObject($"{name}_{index % 8}_{index / 8}", typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(world.transform, false);
            item.transform.localPosition = CellPosition(new GridPosition(index % 8, index / 8));
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            item.GetComponent<MeshRenderer>().sharedMaterials = materials;
            item.SetActive(false); return item;
        }
        private void RenderTelegraphs(float dt)
        {
            var battle = game.Battle;
            if (battle.LastClosedNote != lastClosedNote)
            {
                lastClosedNote = battle.LastClosedNote; impactCells = 0;
                foreach (var fired in battle.Patterns.LastFired) impactCells |= fired.Cells;
                impactAge = 0;
            }
            if (game.State != PrototypeState.Paused) impactAge += dt;
            ulong active = 0;
            foreach (var pattern in battle.Patterns.Active) active |= pattern.Cells;
            for (int i = 0; i < 64; i++)
            {
                ulong bit = 1UL << i;
                bool fired = impactAge < .3f && (impactCells & bit) != 0;
                warnings[i].SetActive(!fired && (active & bit) != 0);
                impacts[i].SetActive(fired);
            }
        }
        private static Mesh TelegraphMesh(bool impact)
        {
            var vertices = new List<Vector3>();
            var underlay = new List<int>(); var markings = new List<int>();
            void Quad(float x1, float z1, float x2, float z2, float height, List<int> triangles)
            {
                int start = vertices.Count;
                vertices.Add(new Vector3(x1, height, z1)); vertices.Add(new Vector3(x1, height, z2));
                vertices.Add(new Vector3(x2, height, z2)); vertices.Add(new Vector3(x2, height, z1));
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            void Stripe(float intercept, float slope, float width)
            {
                float low = Mathf.Max(-.43f, slope > 0 ? -.43f - intercept : intercept - .43f);
                float high = Mathf.Min(.43f, slope > 0 ? .43f - intercept : intercept + .43f);
                if (high <= low) return;
                int start = vertices.Count;
                vertices.Add(new Vector3(low, .023f, slope * low + intercept));
                vertices.Add(new Vector3(low, .023f, Mathf.Min(.43f, slope * low + intercept + width)));
                vertices.Add(new Vector3(high, .023f, Mathf.Min(.43f, slope * high + intercept + width)));
                vertices.Add(new Vector3(high, .023f, slope * high + intercept));
                markings.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            Quad(-.46f, -.46f, .46f, .46f, .015f, underlay);
            foreach (float side in new[] { -.45f, .45f })
            {
                Quad(-.46f, side - .012f, .46f, side + .012f, .024f, markings);
                Quad(side - .012f, -.46f, side + .012f, .46f, .024f, markings);
            }
            if (impact) { Stripe(-.035f, 1, .07f); Stripe(-.035f, -1, .07f); }
            else for (int stripe = -3; stripe <= 3; stripe++) Stripe(stripe * .22f, 1, .035f);
            var mesh = new Mesh { name = impact ? "Impact Cross" : "Warning Hatching", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetTriangles(underlay, 0); mesh.SetTriangles(markings, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void BuildPlayer()
        {
            player = new GameObject("Knight").transform; player.SetParent(world.transform, false);
            var cream = Material(new Color(.97f, .93f, .80f));
            var shade = Material(new Color(.45f, .49f, .45f));
            Box("Pedestal", player, new Vector3(0, .06f, 0), new Vector3(.68f, .12f, .52f), cream);
            Box("Base", player, new Vector3(0, .17f, 0), new Vector3(.55f, .10f, .38f), cream);
            knightBody = new GameObject("Knight Silhouette").transform; knightBody.SetParent(player, false);
            knightBody.localPosition = new Vector3(0, .08f, 0);
            knightBody.localRotation = Quaternion.Euler(game.Stage.CameraElevation, 0, 0);
            Extrude("Neck", cream, shade, new Vector2(.24f,.22f), new Vector2(.82f,.22f), new Vector2(.67f,.64f), new Vector2(.49f,.60f));
            Extrude("Head", cream, shade, new Vector2(.35f,.50f), new Vector2(.68f,.64f), new Vector2(.72f,.80f), new Vector2(.43f,.91f), new Vector2(.22f,.72f));
            Extrude("Muzzle", cream, shade, new Vector2(.08f,.59f), new Vector2(.35f,.52f), new Vector2(.45f,.75f), new Vector2(.22f,.73f));
            Extrude("Ear", cream, shade, new Vector2(.43f,.88f), new Vector2(.59f,.83f), new Vector2(.48f,1f));
            Box("Eye", knightBody, new Vector3(-.108f, .84f, -.133f), new Vector3(.047f, .052f, .024f), edgeMaterial);
        }
        private void BuildNoise()
        {
            noiseMaterial = Material(new Color(.28f, .96f, .74f));
            dormantMaterial = Material(new Color(.26f, .34f, .32f));
            activeNoiseMaterials = new[] { noiseMaterial, noiseMaterial };
            dormantNoiseMaterials = new[] { dormantMaterial, dormantMaterial };
            pickups = new Transform[game.Stage.MaxActiveNoise]; pickupRenderers = new Renderer[pickups.Length][];
            for (int i = 0; i < pickups.Length; i++)
            {
                var item = new GameObject($"Noise_{i}").transform; item.SetParent(world.transform, false); pickups[i] = item;
                var wave = new GameObject("Wave Silhouette").transform; wave.SetParent(item, false);
                wave.localRotation = Quaternion.Euler(game.Stage.CameraElevation, 0, 0);
                Box("Wave", wave, new Vector3(0, .25f, 0), new Vector3(.65f, .055f, .10f), noiseMaterial);
                for (int j = 0; j < 3; j++)
                    Box($"WaveBar{j}", wave, new Vector3((j - 1) * .20f, .25f, 0), new Vector3(.075f, j == 1 ? .58f : .32f, .12f), noiseMaterial);
                Frame(item, .64f, -.075f, .025f, noiseMaterial);
                pickupRenderers[i] = item.GetComponentsInChildren<Renderer>();
                foreach (var renderer in pickupRenderers[i]) renderer.sharedMaterials = activeNoiseMaterials;
                item.gameObject.SetActive(false);
            }
        }
        private void BuildShield()
        {
            // The domain resolves note-based guarding; this shield only presents the accepted guard action.
            shield = new GameObject("Guard Pose").transform; shield.SetParent(player, false);
            shield.localPosition = new Vector3(.13f, .22f, -.32f);
            shield.localRotation = Quaternion.Euler(game.Stage.CameraElevation, 0, 0);
            var outline = shield.gameObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = false; outline.loop = true; outline.positionCount = 5;
            outline.sharedMaterial = Material(new Color(.45f, .85f, 1f));
            outline.startWidth = outline.endWidth = .045f; outline.numCornerVertices = 3;
            outline.SetPositions(new[] { new Vector3(-.34f,.64f,0),new Vector3(.34f,.64f,0),new Vector3(.29f,.22f,0),new Vector3(0,0,0),new Vector3(-.29f,.22f,0) });
            Box("Shield Center", shield, new Vector3(0,.33f,0),new Vector3(.055f,.30f,.035f),outline.sharedMaterial);
            shield.gameObject.SetActive(false);
        }
        private void Frame(Transform parent, float width, float height, float thickness, Material material)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Box("Outline", parent, new Vector3(0, height, side * width / 2), new Vector3(width, .018f, thickness), material);
                Box("Outline", parent, new Vector3(side * width / 2, height, 0), new Vector3(thickness, .018f, width), material);
            }
        }
        private Material Material(Color color)
        {
            // A serialized material reference keeps the shader and its used variant in standalone builds.
            var material = new Material(materialTemplate) { name = "Prototype " + color, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            resources.Add(material); return material;
        }
        private void Box(string label, Transform parent, Vector3 position, Vector3 scale, Material top)
        {
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false); item.transform.localPosition = position; item.transform.localScale = scale;
            item.GetComponent<MeshFilter>().sharedMesh = box;
            item.GetComponent<MeshRenderer>().sharedMaterials = new[] { top, edgeMaterial };
        }
        private Mesh BuildBox()
        {
            var mesh = new Mesh { name = "Two Tone Box" };
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),
                new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f) };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 4,7,6,4,6,5 }, 0);
            mesh.SetTriangles(new[] { 0,1,2,0,2,3, 0,4,5,0,5,1, 1,5,6,1,6,2, 2,6,7,2,7,3, 3,7,4,3,4,0 }, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void Extrude(string label, Material face, Material side, params Vector2[] outline)
        {
            int count = outline.Length;
            var vertices = new Vector3[count * 2]; var front = new List<int>(); var sides = new List<int>();
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3((outline[i].x - .5f) * .8f, outline[i].y + .08f, -.12f);
                vertices[i + count] = new Vector3(vertices[i].x, vertices[i].y, .12f);
            }
            for (int i = 1; i < count - 1; i++)
            { front.AddRange(new[] { 0,i+1,i, count,count+i,count+i+1 }); }
            for (int i = 0; i < count; i++)
            { int next = (i + 1) % count; sides.AddRange(new[] { i,next,i+count, next,next+count,i+count }); }
            var mesh = new Mesh { name = label, vertices = vertices, subMeshCount = 2 };
            mesh.SetTriangles(front, 0); mesh.SetTriangles(sides, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
            var part = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(knightBody, false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh; part.GetComponent<MeshRenderer>().sharedMaterials = new[] { face, side };
        }
        private void OnDestroy()
        {
            if (BoardCamera != null) BoardCamera.targetTexture = null;
            if (Texture != null) Texture.Release();
            if (world != null) Destroy(world);
            foreach (var resource in resources) if (resource != null) Destroy(resource);
        }
    }
}
