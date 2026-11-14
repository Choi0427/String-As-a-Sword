using System.Collections.Generic;
using System.Linq;
using StringAsASword.Board;
using StringAsASword.Combat;
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
        private Material noiseMaterial;
        // [pickup][renderer] material arrays, so a delivered prefab keeps its own materials while active.
        private Material[][][] pickupActive, pickupDormant;
        private bool lastNoiseDormant;
        private ArtSet art;
        private GameObject king;
        private int lastTelegraphId = -1, lastCollected;
        private readonly List<Object> resources = new List<Object>();
        private Mesh box;
        private Material edgeMaterial;
        private Material materialTemplate;
        private Material outlineMaterial;
        private Color outlineAccent, outlineColor;
        // Two tile kinds: normal (guard or leave) and strong (a guard does not protect: AttackResponse.DodgeOnly).
        // A full-board GuardOnly attack is a normal attack with nowhere to go, so it shows the normal tile.
        private const int NormalTile = 0, StrongTile = 1;
        private readonly GameObject[][] warnings = { new GameObject[64], new GameObject[64] };
        private readonly GameObject[] impacts = new GameObject[64];
        private int lastClosedNote = -1;
        private float impactAge = 1;
        private ulong impactCells;
        private readonly int[] cellCounts = new int[64], cellSoonest = new int[64];
        private readonly AttackResponse[] cellResponses = new AttackResponse[64];
        private Vector3 moveFrom, moveTarget;
        private float moveAge = 1, attackAge = 1, guardAge = 1;
        private int guardNote = -1;
        private int lastSession = -1;
        private readonly List<Transform> cellPieces = new List<Transform>(), edgePieces = new List<Transform>(), boardPieces = new List<Transform>();
        private Vector3[] cellHome, edgeHome, boardHome;
        private float[] cellDelay;
        // A delivered board model is cut into one shard per cell at load, so the finale can break it apart.
        private Transform boardModel;
        private GameObject boardShards;
        public bool BoardShatters => boardShards != null;
        // Chess pieces the boss summons for attacks drawn from a piece's moves, keyed by shape name.
        private sealed class Summon { public Transform Root; public Vector3 Home, Target; public float Age, StrikeAge = 1; public bool Live, Seen; }
        private readonly Dictionary<string, Summon> summons = new Dictionary<string, Summon>();
        private Material pieceFace, pieceSide;
        public const float SummonRow = 4.75f, StrikeSeconds = .35f;
        public int LiveSummons { get { int n = 0; foreach (var s in summons.Values) if (s.Live) n++; return n; } }
        /// <summary>Ending: the knight is placed by the caller instead of following the grid.</summary>
        public bool FreeRoam { get; set; }
        public Camera BoardCamera { get; private set; }
        public RenderTexture Texture { get; private set; }
        public Transform PlayerTransform => player;
        public int TileCount { get; private set; }
        /// <summary>Board camera angle above the board; upright silhouettes tilt by it to face the camera.</summary>
        public float Elevation { get; private set; }
        private Color clearColor;

        public void Initialize(PrototypeGame owner, UnityEngine.UI.RawImage output, Material template, ArtSet artSet)
        {
            if (template == null || template.shader == null)
                throw new System.InvalidOperationException("Assign the board material to PrototypeView before building. Use String As a Sword > Prepare Windows Build.");
            materialTemplate = template;
            game = owner; art = artSet;
            world = new GameObject("Board World");
            // A separate render target keeps the board projection independent of HUD/screen aspect ratio.
            world.transform.position = new Vector3(1000, 0, 1000);
            Texture = new RenderTexture(960, 880, 24) { name = "Board View", antiAliasing = 4 };
            Texture.Create(); output.texture = Texture; resources.Add(Texture);
            BoardCamera = new GameObject("Board Camera", typeof(Camera)).GetComponent<Camera>();
            BoardCamera.transform.SetParent(world.transform, false);
            Elevation = art.cameraElevation > 0 ? art.cameraElevation : game.Stage.CameraElevation;
            float size = art.cameraSize > 0 ? art.cameraSize : 4.6f;
            // A perspective camera stands back far enough that the same half-height fits at the board centre.
            bool perspective = art.cameraFieldOfView > 0;
            float distance = perspective ? size / Mathf.Tan(art.cameraFieldOfView * .5f * Mathf.Deg2Rad) : 14;
            BoardCamera.transform.localRotation = Quaternion.Euler(Elevation, 0, 0);
            BoardCamera.transform.localPosition = -(BoardCamera.transform.localRotation * Vector3.forward) * distance
                + BoardCamera.transform.localRotation * Vector3.up * art.cameraLift;
            BoardCamera.orthographic = !perspective; BoardCamera.orthographicSize = size;
            if (perspective) BoardCamera.fieldOfView = art.cameraFieldOfView;
            BoardCamera.nearClipPlane = .1f; BoardCamera.farClipPlane = distance + 16;
            BoardCamera.clearFlags = CameraClearFlags.SolidColor;
            // With background art the board view is see-through, so the board sits in the scene instead of on a panel.
            clearColor = new Color(.035f, .043f, .052f, art.HasBackground ? 0 : 1);
            BoardCamera.backgroundColor = clearColor;
            BoardCamera.targetTexture = Texture; BoardCamera.allowHDR = false;
            box = BuildBox(); resources.Add(box);
            edgeMaterial = art.boardEdge != null ? art.boardEdge : Material(new Color(.075f, .095f, .105f));
            var ivory = art.lightTile != null ? art.lightTile : Material(new Color(.72f, .74f, .70f));
            var dark = art.darkTile != null ? art.darkTile : Material(new Color(.15f, .20f, .22f));
            if (art.useLighting)
            {
                var light = new GameObject("Board Light", typeof(Light)).GetComponent<Light>(); light.transform.SetParent(world.transform, false);
                light.type = LightType.Directional; light.transform.localRotation = Quaternion.Euler(50, -30, 0);
            }
            var gold = Material(new Color(.84f, .66f, .32f));
            outlineAccent = outlineColor = new Color(.40f, .85f, .73f);
            var accent = outlineMaterial = Material(outlineAccent);
            if (art.boardPrefab != null)
            {
                var model = Instantiate(art.boardPrefab, world.transform, false); model.name = "Board Model";
                boardModel = model.transform;
                // Without readable meshes the model cannot be cut; it then falls as one piece as before.
                if (!BuildShards()) boardPieces.Add(boardModel);
            }
            else
            {
                boardPieces.Add(Box("Board Plinth", world.transform, new Vector3(0, -.30f, 0), new Vector3(8.40f, .38f, 8.40f), edgeMaterial));
                boardPieces.Add(Box("Board Rim", world.transform, new Vector3(0, -.13f, 0), new Vector3(8.20f, .14f, 8.20f), gold));
                boardPieces.Add(Box("Board Deck", world.transform, new Vector3(0, -.115f, 0), new Vector3(8, .08f, 8), edgeMaterial));
            }
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                if (art.boardPrefab == null)
                    cellPieces.Add(Box($"Cell_{x}_{y}", world.transform, CellPosition(new GridPosition(x, y)) - Vector3.up * .05f,
                        new Vector3(.975f, .10f, .975f), (x + y) % 2 == 0 ? dark : ivory));
                if (y == 7)
                    edgePieces.Add(Box($"AttackEdge_{x}", world.transform, new Vector3(x - 3.5f, .014f, 3.90f), new Vector3(.83f, .028f, .065f), gold));
                TileCount++;
            }
            selection = new GameObject("Player Cell Outline").transform; selection.SetParent(world.transform, false);
            Frame(selection, .91f, .026f, .042f, accent);
            BuildPlayer();
            if (art.kingPrefab != null)
            {
                king = Instantiate(art.kingPrefab, world.transform, false); king.name = "King";
                king.transform.localPosition = art.kingAnchor; king.transform.localScale = Vector3.one * art.kingScale;
            }
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
        /// <summary>Board collapse, 0 = intact, 1 = everything has fallen. Outer cells go first, the plinth last.</summary>
        public void SetCollapse(float progress)
        {
            if (cellHome == null)
            {
                cellHome = cellPieces.Select(p => p.localPosition).ToArray();
                edgeHome = edgePieces.Select(p => p.localPosition).ToArray();
                boardHome = boardPieces.Select(p => p.localPosition).ToArray();
                var random = new System.Random(87);
                cellDelay = cellHome.Select(p => .72f * (1 - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) / 3.5f) + .12f * (float)random.NextDouble()).ToArray();
            }
            for (int i = 0; i < cellPieces.Count; i++) Fall(cellPieces[i], cellHome[i], progress - cellDelay[i], i);
            for (int i = 0; i < edgePieces.Count; i++) Fall(edgePieces[i], edgeHome[i], progress, 100 + i);
            for (int i = 0; i < boardPieces.Count; i++) Fall(boardPieces[i], boardHome[i], progress - .82f, 200 + i);
            bool intact = progress <= 0;
            if (boardShards != null) { boardShards.SetActive(!intact); boardModel.gameObject.SetActive(intact); }
            selection.gameObject.SetActive(intact);
            if (!intact) foreach (var pickup in pickups) pickup.gameObject.SetActive(false);
        }
        private static void Fall(Transform piece, Vector3 home, float t, int seed)
        {
            t = Mathf.Max(0, t) * 2.2f; // seconds-like fall time once the piece lets go
            piece.localPosition = home + Vector3.down * (4.5f * t * t);
            piece.localRotation = Quaternion.Euler((seed * 37 % 23 - 11) * t * 6, 0, (seed * 53 % 19 - 9) * t * 6);
            piece.gameObject.SetActive(t < 2.4f);
        }
        /// <summary>0 = standing, 1 = fallen over. Delivered king prefabs also get the "Hurt" cue at the start.</summary>
        public void SetKingDown(float t)
        {
            if (king == null) return;
            king.transform.localRotation = Quaternion.Euler(-80 * Mathf.SmoothStep(0, 1, t), 0, 0);
        }
        public void SetVoidColor(Color color) => BoardCamera.backgroundColor = color;
        /// <summary>Back to the battle's clear colour (transparent when background art is assigned).</summary>
        public void ResetVoid() => BoardCamera.backgroundColor = clearColor;
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
            lastTelegraphId = -1; lastCollected = game.Noise.Collected;
            for (int i = 0; i < impacts.Length; i++)
            {
                impacts[i].SetActive(false);
                foreach (var pool in warnings) pool[i].SetActive(false);
            }
            lastSession = game.SessionId;
            HideSummons();
            FreeRoam = false; if (cellHome != null) SetCollapse(0); SetKingDown(0);
        }
        public void ShowAction(ActionResult result)
        {
            if (!result.Accepted) return;
            if (result.Kind == PlayerActionKind.Attack) { attackAge = 0; Cue("Attack", art.attackVfx); }
            if (result.Kind == PlayerActionKind.Guard) { guardAge = 0; guardNote = result.Timing.BeatId; Cue("Guard", art.guardVfx); }
        }
        public void ShowOutcome(string text)
        {
            if (text == TelegraphHud.Blocked) Cue("Block", art.blockVfx); else Cue("Hit", art.hitVfx);
        }
        public void ShowDamage(int damage) { if (damage > 0) ArtCue.Trigger(king, "Hurt"); }
        public void ShowPhase()
        {
            ArtCue.Trigger(king, "PhaseUp");
            ArtCue.Spawn(art.phaseVfx, world.transform, king != null ? art.kingAnchor : Vector3.zero);
        }
        // VFX stay on the board where the action happened instead of following the knight.
        private void Cue(string trigger, GameObject vfx)
        {
            ArtCue.Trigger(player.gameObject, trigger);
            ArtCue.Spawn(vfx, world.transform, moveTarget);
        }
        public void Render()
        {
            if (lastSession != game.SessionId) ResetVisuals();
            if (FreeRoam) return;
            float dt = Time.unscaledDeltaTime;
            RenderTelegraphs(dt);
            Vector3 target = CellPosition(game.Session.Position);
            if (target != moveTarget) { moveFrom = player.localPosition; moveTarget = target; moveAge = 0; ArtCue.Trigger(player.gameObject, "Move"); }
            if (game.Noise.Collected > lastCollected) ArtCue.Spawn(art.pickupVfx, world.transform, target);
            lastCollected = game.Noise.Collected;
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
                    for (int r = 0; r < pickupRenderers[i].Length; r++)
                        pickupRenderers[i][r].sharedMaterials = game.Noise.GoalReached ? pickupDormant[i][r] : pickupActive[i][r];
                bool active = i < game.Noise.Pickups.Count;
                pickups[i].gameObject.SetActive(active);
                if (!active) continue;
                // Re-sent every frame: an Animator forgets its parameters while the pickup is inactive.
                if (art.noisePrefab != null) ArtCue.SetBool(pickups[i].gameObject, "Dormant", game.Noise.GoalReached);
                pickups[i].localPosition = CellPosition(game.Noise.Pickups[i].Cell) + Vector3.up * (.12f + .025f * Mathf.Sin(Time.unscaledTime * 3 + i));
            }
            lastNoiseDormant = game.Noise.GoalReached;
        }
        private void BuildTelegraphs()
        {
            var dark = Material(new Color(.045f, .055f, .065f));
            var white = Material(new Color(.97f, .97f, .94f));
            Mesh impact = TelegraphMesh(true);
            resources.Add(impact);
            var materials = new[] { dark, white };
            Mesh quad = null;
            for (int kind = 0; kind < warnings.Length; kind++)
            {
                bool strong = kind == StrongTile;
                // Delivered tiles are one quad drawn by the Telegraph Tile shader. Without them: a bare frame, hatched when strong.
                var delivered = strong ? art.strongMark : art.normalMark;
                Mesh warning; Material[] marks;
                if (delivered != null) { warning = quad ?? (quad = MarkQuad()); marks = new[] { delivered }; }
                else { warning = TelegraphMesh(false, strong); resources.Add(warning); marks = materials; }
                for (int i = 0; i < 64; i++) warnings[kind][i] = TelegraphCell(strong ? "StrongWarning" : "NormalWarning", i, warning, marks);
            }
            if (art.impactMark != null) { impact = quad ?? (quad = MarkQuad()); materials = new[] { art.impactMark }; }
            for (int i = 0; i < 64; i++) impacts[i] = TelegraphCell("Impact", i, impact, materials);
        }
        private Mesh MarkQuad()
        {
            var mesh = new Mesh { name = "Telegraph Mark",
                vertices = new[] { new Vector3(-.46f,.024f,-.46f), new Vector3(-.46f,.024f,.46f), new Vector3(.46f,.024f,.46f), new Vector3(.46f,.024f,-.46f) },
                uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) }, triangles = new[] { 0,1,2,0,2,3 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh); return mesh;
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
            bool noteClosed = battle.LastClosedNote != lastClosedNote;
            if (noteClosed)
            {
                lastClosedNote = battle.LastClosedNote; impactCells = 0;
                foreach (var fired in battle.Patterns.LastFired) impactCells |= fired.Cells;
                impactAge = 0;
                if (impactCells != 0)
                {
                    ArtCue.Trigger(king, "Attack");
                    if (art.bossFireVfx != null)
                        for (int i = 0; i < 64; i++)
                            if ((impactCells & (1UL << i)) != 0) ArtCue.Spawn(art.bossFireVfx, world.transform, CellPosition(new GridPosition(i % 8, i / 8)));
                }
            }
            RenderSummons(battle, game.State == PrototypeState.Paused ? 0 : dt, noteClosed);
            bool published = false;
            foreach (var telegraph in battle.Patterns.Active)
                if (telegraph.Id > lastTelegraphId) { lastTelegraphId = telegraph.Id; published = true; }
            if (published) ArtCue.Trigger(king, "Telegraph");
            if (game.State != PrototypeState.Paused) impactAge += dt;
            TelegraphHud.MapCells(battle.Patterns.Active, battle.LastClosedNote, cellCounts, cellSoonest, cellResponses);
            for (int i = 0; i < 64; i++)
            {
                bool fired = impactAge < .3f && (impactCells & (1UL << i)) != 0;
                bool warning = !fired && cellCounts[i] > 0;
                int tile = cellResponses[i] == AttackResponse.DodgeOnly ? StrongTile : NormalTile;
                for (int kind = 0; kind < warnings.Length; kind++)
                    warnings[kind][i].SetActive(warning && kind == tile);
                impacts[i].SetActive(fired);
            }
            // The knight's pedestal covers its own cell's mark, so the outline carries that cell's response.
            var position = game.Session.Position;
            int cell = position.Y * 8 + position.X;
            Color outline = cellCounts[cell] > 0 ? TelegraphHud.ResponseColor(cellResponses[cell], outlineAccent) : outlineAccent;
            if (outlineColor != outline)
            {
                outlineMaterial.color = outlineColor = outline;
                if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor", outline);
            }
        }
        private static Mesh TelegraphMesh(bool impact) => TelegraphMesh(impact, true);
        // The fallback marks: a dark underlay and a frame, plus an X (impact) or diagonal hatching (strong) when striped.
        private static Mesh TelegraphMesh(bool impact, bool striped)
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
            else if (striped) for (int stripe = -3; stripe <= 3; stripe++) Stripe(stripe * .22f, 1, .035f);
            var mesh = new Mesh { name = impact ? "Impact Cross" : striped ? "Warning Hatching" : "Warning Frame", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetTriangles(underlay, 0); mesh.SetTriangles(markings, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private bool BuildShards()
        {
            var filters = boardModel.GetComponentsInChildren<MeshFilter>();
            foreach (var filter in filters)
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable || filter.GetComponent<MeshRenderer>() == null) return false;
            if (filters.Length == 0) return false;
            boardShards = new GameObject("Board Shards");
            boardShards.transform.SetParent(world.transform, false);
            var cells = new Transform[64];
            for (int i = 0; i < 64; i++)
            {
                cells[i] = new GameObject($"Shard_{i % 8}_{i / 8}").transform; cells[i].SetParent(boardShards.transform, false);
                cells[i].localPosition = CellPosition(new GridPosition(i % 8, i / 8));
            }
            float bottom = 0;
            foreach (var filter in filters)
            {
                var source = filter.sharedMesh;
                var toBoard = world.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var normalToBoard = toBoard.inverse.transpose;
                var positions = source.vertices; var normals = source.normals; var uvs = source.uv;
                bool hasNormals = normals.Length == positions.Length, hasUv = uvs.Length == positions.Length;
                for (int v = 0; v < positions.Length; v++)
                {
                    positions[v] = toBoard.MultiplyPoint3x4(positions[v]); bottom = Mathf.Min(bottom, positions[v].y);
                    if (hasNormals) normals[v] = normalToBoard.MultiplyVector(normals[v]).normalized;
                }
                ShardVertex Vertex(int index) => new ShardVertex { Position = positions[index],
                    Normal = hasNormals ? normals[index] : Vector3.up, Uv = hasUv ? uvs[index] : Vector2.zero };
                var builders = new ShardBuilder[64];
                var polygon = new List<ShardVertex>(12); var scratch = new List<ShardVertex>(12);
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var triangles = source.GetTriangles(sub);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        ShardVertex a = Vertex(triangles[t]), b = Vertex(triangles[t + 1]), c = Vertex(triangles[t + 2]);
                        int x0 = Cell(Mathf.Min(a.Position.x, Mathf.Min(b.Position.x, c.Position.x))), x1 = Cell(Mathf.Max(a.Position.x, Mathf.Max(b.Position.x, c.Position.x)));
                        int z0 = Cell(Mathf.Min(a.Position.z, Mathf.Min(b.Position.z, c.Position.z))), z1 = Cell(Mathf.Max(a.Position.z, Mathf.Max(b.Position.z, c.Position.z)));
                        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                        {
                            polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                            // The outermost cells keep everything beyond the playing field (rim, plinth).
                            if (x0 != x1 || z0 != z1)
                            {
                                if (x > 0) Clip(polygon, scratch, 0, x - 4, true);
                                if (x < 7) Clip(polygon, scratch, 0, x - 3, false);
                                if (z > 0) Clip(polygon, scratch, 2, z - 4, true);
                                if (z < 7) Clip(polygon, scratch, 2, z - 3, false);
                            }
                            if (polygon.Count < 3) continue;
                            int cell = z * 8 + x;
                            (builders[cell] ?? (builders[cell] = new ShardBuilder(source.subMeshCount))).Add(polygon, sub, cells[cell].localPosition);
                        }
                    }
                }
                var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
                for (int cell = 0; cell < 64; cell++)
                {
                    if (builders[cell] == null) continue;
                    var mesh = builders[cell].Build($"Shard {cell} {source.name}", hasUv); resources.Add(mesh);
                    var part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
                    part.transform.SetParent(cells[cell], false);
                    part.GetComponent<MeshFilter>().sharedMesh = mesh; part.GetComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }
            // The model is a shell: a dark core under each cell closes the cut faces once the shards drift apart.
            float depth = Mathf.Max(.1f, -bottom - .06f);
            for (int cell = 0; cell < 64; cell++)
                Box("Core", cells[cell], new Vector3(0, -.05f - depth / 2, 0), new Vector3(.985f, depth, .985f), edgeMaterial);
            cellPieces.AddRange(cells);
            boardShards.SetActive(false);
            return true;
        }
        private static int Cell(float coordinate) => Mathf.Clamp(Mathf.FloorToInt(coordinate + 4), 0, 7);
        private struct ShardVertex { public Vector3 Position, Normal; public Vector2 Uv; }
        // Sutherland-Hodgman against one axis-aligned plane; keeps the side at or above (or below) the limit.
        private static void Clip(List<ShardVertex> polygon, List<ShardVertex> scratch, int axis, float limit, bool keepAbove)
        {
            if (polygon.Count == 0) return;
            scratch.Clear();
            var previous = polygon[polygon.Count - 1];
            float previousSide = keepAbove ? previous.Position[axis] - limit : limit - previous.Position[axis];
            foreach (var current in polygon)
            {
                float side = keepAbove ? current.Position[axis] - limit : limit - current.Position[axis];
                if (side >= 0 != previousSide >= 0)
                {
                    float t = previousSide / (previousSide - side);
                    scratch.Add(new ShardVertex { Position = Vector3.Lerp(previous.Position, current.Position, t),
                        Normal = Vector3.Lerp(previous.Normal, current.Normal, t), Uv = Vector2.Lerp(previous.Uv, current.Uv, t) });
                }
                if (side >= 0) scratch.Add(current);
                previous = current; previousSide = side;
            }
            polygon.Clear(); polygon.AddRange(scratch);
        }
        private sealed class ShardBuilder
        {
            private readonly List<Vector3> positions = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int>[] triangles;
            public ShardBuilder(int subMeshes) { triangles = new List<int>[subMeshes]; for (int i = 0; i < subMeshes; i++) triangles[i] = new List<int>(); }
            public void Add(List<ShardVertex> polygon, int subMesh, Vector3 origin)
            {
                int start = positions.Count;
                foreach (var v in polygon) { positions.Add(v.Position - origin); normals.Add(v.Normal); uvs.Add(v.Uv); }
                for (int i = 1; i < polygon.Count - 1; i++) { triangles[subMesh].Add(start); triangles[subMesh].Add(start + i); triangles[subMesh].Add(start + i + 1); }
            }
            public Mesh Build(string name, bool hasUv)
            {
                var mesh = new Mesh { name = name, subMeshCount = triangles.Length };
                if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(positions); mesh.SetNormals(normals);
                if (hasUv) mesh.SetUVs(0, uvs);
                for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
                if (hasUv) mesh.RecalculateTangents();
                mesh.RecalculateBounds(); return mesh;
            }
        }

        // Outlines in the knight's 0..1 box, counter-clockwise, each one convex (Extrude fans from the first corner).
        private static Vector2[] Outline(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return points;
        }
        private static readonly Vector2[] PieceBase = Outline(.2f,0, .8f,0, .72f,.14f, .28f,.14f);
        private static readonly Vector2[] PieceBody = Outline(.35f,.14f, .65f,.14f, .59f,.6f, .41f,.6f);
        private static Vector2[][] PieceOutlines(ChessPiece piece)
        {
            switch (piece)
            {
                case ChessPiece.Pawn: return new[] { PieceBase, Outline(.37f,.14f, .63f,.14f, .56f,.48f, .44f,.48f),
                    Outline(.4f,.46f, .6f,.46f, .67f,.59f, .6f,.72f, .4f,.72f, .33f,.59f) };
                case ChessPiece.Rook: return new[] { PieceBase, Outline(.33f,.14f, .67f,.14f, .63f,.62f, .37f,.62f), Outline(.28f,.62f, .72f,.62f, .72f,.74f, .28f,.74f),
                    Outline(.28f,.74f, .38f,.74f, .38f,.86f, .28f,.86f), Outline(.45f,.74f, .55f,.74f, .55f,.86f, .45f,.86f), Outline(.62f,.74f, .72f,.74f, .72f,.86f, .62f,.86f) };
                case ChessPiece.Bishop: return new[] { PieceBase, Outline(.37f,.14f, .63f,.14f, .56f,.5f, .44f,.5f),
                    Outline(.5f,.46f, .66f,.67f, .5f,.94f, .34f,.67f), Outline(.46f,.91f, .54f,.91f, .5f,1.02f) };
                case ChessPiece.Knight: return new[] { PieceBase, Outline(.3f,.14f, .76f,.14f, .82f,.22f, .24f,.22f), Outline(.24f,.22f, .82f,.22f, .67f,.64f, .49f,.60f),
                    Outline(.35f,.50f, .68f,.64f, .72f,.80f, .43f,.91f, .22f,.72f), Outline(.08f,.59f, .35f,.52f, .45f,.75f, .22f,.73f), Outline(.43f,.88f, .59f,.83f, .48f,1f) };
                case ChessPiece.Queen: return new[] { PieceBase, PieceBody, Outline(.3f,.6f, .7f,.6f, .76f,.86f, .24f,.86f),
                    Outline(.24f,.84f, .36f,.84f, .29f,1f), Outline(.44f,.84f, .56f,.84f, .5f,1.03f), Outline(.64f,.84f, .76f,.84f, .71f,1f) };
                default: return new[] { PieceBase, PieceBody, Outline(.31f,.6f, .69f,.6f, .73f,.8f, .27f,.8f),
                    Outline(.47f,.8f, .53f,.8f, .53f,1.04f, .47f,1.04f), Outline(.4f,.9f, .6f,.9f, .6f,.96f, .4f,.96f) };
            }
        }
        private Summon BuildSummon(string shape, IReadOnlyList<ChessPiece> pieces)
        {
            if (pieceFace == null) { pieceFace = Material(new Color(.78f, .79f, .84f)); pieceSide = Material(new Color(.10f, .11f, .13f)); }
            var root = new GameObject("Summon " + shape).transform; root.SetParent(world.transform, false);
            for (int i = 0; i < pieces.Count; i++)
            {
                var body = new GameObject(pieces[i].ToString()).transform; body.SetParent(root, false);
                // A compound piece (chancellor = rook + knight) stands as its two halves side by side.
                body.localPosition = new Vector3((i - (pieces.Count - 1) * .5f) * .62f, 0, 0);
                body.localRotation = Quaternion.Euler(Elevation, 0, 0);
                body.localScale = Vector3.one * (pieces.Count == 1 ? 1.5f : 1.25f);
                foreach (var outline in PieceOutlines(pieces[i])) Extrude(body, pieces[i].ToString(), pieceFace, pieceSide, outline);
            }
            root.gameObject.SetActive(false);
            return new Summon { Root = root };
        }
        private static Vector3 Centroid(ulong cells)
        {
            Vector3 sum = Vector3.zero; int count = 0;
            for (int i = 0; i < 64; i++) if ((cells & (1UL << i)) != 0) { sum += CellPosition(new GridPosition(i % 8, i / 8)); count++; }
            return count == 0 ? Vector3.zero : sum / count;
        }
        // The piece rises behind row 8 in the attack's column while the warning counts down, then lunges onto the cells it hits.
        private void RenderSummons(BattleSession battle, float dt, bool noteClosed)
        {
            foreach (var summon in summons.Values) summon.Seen = false;
            foreach (var telegraph in battle.Patterns.Active)
            {
                if (telegraph.Pieces.Count == 0) continue;
                if (!summons.TryGetValue(telegraph.Name, out var summon)) summons.Add(telegraph.Name, summon = BuildSummon(telegraph.Name, telegraph.Pieces));
                if (summon.Seen) continue;
                summon.Seen = true;
                if (summon.StrikeAge < StrikeSeconds) continue;
                ulong cells = 0;
                foreach (var other in battle.Patterns.Active) if (other.Name == telegraph.Name && other.ImpactNote == telegraph.ImpactNote) cells |= other.Cells;
                summon.Target = Centroid(cells); summon.Home = new Vector3(summon.Target.x, 0, SummonRow);
                if (!summon.Live) { summon.Live = true; summon.Age = 0; summon.Root.gameObject.SetActive(true); }
            }
            if (noteClosed)
                foreach (var fired in battle.Patterns.LastFired)
                    if (fired.Pieces.Count > 0 && summons.TryGetValue(fired.Name, out var summon) && summon.Live && summon.StrikeAge >= StrikeSeconds)
                    {
                        ulong cells = 0;
                        foreach (var other in battle.Patterns.LastFired) if (other.Name == fired.Name) cells |= other.Cells;
                        summon.Target = Centroid(cells); summon.StrikeAge = 0;
                    }
            foreach (var summon in summons.Values)
            {
                if (!summon.Live) continue;
                summon.Age += dt; summon.StrikeAge += dt;
                bool striking = summon.StrikeAge < StrikeSeconds;
                if (!summon.Seen && !striking) { summon.Live = false; summon.Root.gameObject.SetActive(false); continue; }
                float rise = Mathf.SmoothStep(0, 1, summon.Age / .18f);
                float lunge = striking ? Mathf.SmoothStep(0, 1, summon.StrikeAge / .12f) : 0;
                summon.Root.localPosition = Vector3.Lerp(summon.Home, summon.Target, lunge)
                    + Vector3.up * ((1 - rise) * 1.4f + Mathf.Sin(lunge * Mathf.PI) * .7f);
                // After the hit the piece shrinks away instead of popping out.
                summon.Root.localScale = Vector3.one * (rise * (striking ? Mathf.Clamp01((StrikeSeconds - summon.StrikeAge) / .12f) : 1));
            }
        }
        private void HideSummons()
        {
            foreach (var summon in summons.Values) { summon.Live = false; summon.StrikeAge = 1; summon.Root.gameObject.SetActive(false); }
        }
        private void BuildPlayer()
        {
            player = new GameObject("Knight").transform; player.SetParent(world.transform, false);
            if (art.knightPrefab != null) { Instantiate(art.knightPrefab, player, false).name = "Knight Model"; return; }
            var cream = Material(new Color(.97f, .93f, .80f));
            var shade = Material(new Color(.45f, .49f, .45f));
            Box("Pedestal", player, new Vector3(0, .06f, 0), new Vector3(.68f, .12f, .52f), cream);
            Box("Base", player, new Vector3(0, .17f, 0), new Vector3(.55f, .10f, .38f), cream);
            knightBody = new GameObject("Knight Silhouette").transform; knightBody.SetParent(player, false);
            knightBody.localPosition = new Vector3(0, .08f, 0);
            knightBody.localRotation = Quaternion.Euler(Elevation, 0, 0);
            Extrude("Neck", cream, shade, new Vector2(.24f,.22f), new Vector2(.82f,.22f), new Vector2(.67f,.64f), new Vector2(.49f,.60f));
            Extrude("Head", cream, shade, new Vector2(.35f,.50f), new Vector2(.68f,.64f), new Vector2(.72f,.80f), new Vector2(.43f,.91f), new Vector2(.22f,.72f));
            Extrude("Muzzle", cream, shade, new Vector2(.08f,.59f), new Vector2(.35f,.52f), new Vector2(.45f,.75f), new Vector2(.22f,.73f));
            Extrude("Ear", cream, shade, new Vector2(.43f,.88f), new Vector2(.59f,.83f), new Vector2(.48f,1f));
            Box("Eye", knightBody, new Vector3(-.108f, .84f, -.133f), new Vector3(.047f, .052f, .024f), edgeMaterial);
        }
        private void BuildNoise()
        {
            noiseMaterial = Material(new Color(.28f, .96f, .74f));
            var dormantMaterial = art.noisePrefab != null ? art.noiseDormantMaterial : Material(new Color(.26f, .34f, .32f));
            pickups = new Transform[game.Stage.MaxActiveNoise]; pickupRenderers = new Renderer[pickups.Length][];
            pickupActive = new Material[pickups.Length][][]; pickupDormant = new Material[pickups.Length][][];
            for (int i = 0; i < pickups.Length; i++)
            {
                Transform item;
                if (art.noisePrefab != null) { item = Instantiate(art.noisePrefab, world.transform, false).transform; item.name = $"Noise_{i}"; }
                else
                {
                    item = new GameObject($"Noise_{i}").transform; item.SetParent(world.transform, false);
                    var wave = new GameObject("Wave Silhouette").transform; wave.SetParent(item, false);
                    wave.localRotation = Quaternion.Euler(Elevation, 0, 0);
                    Box("Wave", wave, new Vector3(0, .25f, 0), new Vector3(.65f, .055f, .10f), noiseMaterial);
                    for (int j = 0; j < 3; j++)
                        Box($"WaveBar{j}", wave, new Vector3((j - 1) * .20f, .25f, 0), new Vector3(.075f, j == 1 ? .58f : .32f, .12f), noiseMaterial);
                    Frame(item, .64f, -.075f, .025f, noiseMaterial);
                    foreach (var renderer in item.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = new[] { noiseMaterial, noiseMaterial };
                }
                pickups[i] = item;
                pickupRenderers[i] = item.GetComponentsInChildren<Renderer>();
                pickupActive[i] = new Material[pickupRenderers[i].Length][]; pickupDormant[i] = new Material[pickupRenderers[i].Length][];
                for (int r = 0; r < pickupRenderers[i].Length; r++)
                {
                    pickupActive[i][r] = pickupRenderers[i][r].sharedMaterials;
                    pickupDormant[i][r] = dormantMaterial == null ? pickupActive[i][r] : Enumerable.Repeat(dormantMaterial, pickupActive[i][r].Length).ToArray();
                }
                item.gameObject.SetActive(false);
            }
        }
        private void BuildShield()
        {
            // The domain resolves note-based guarding; this shield only presents the accepted guard action.
            shield = new GameObject("Guard Pose").transform; shield.SetParent(player, false);
            shield.localPosition = new Vector3(.13f, .22f, -.32f);
            shield.localRotation = Quaternion.Euler(Elevation, 0, 0);
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
        private Transform Box(string label, Transform parent, Vector3 position, Vector3 scale, Material top)
        {
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false); item.transform.localPosition = position; item.transform.localScale = scale;
            item.GetComponent<MeshFilter>().sharedMesh = box;
            item.GetComponent<MeshRenderer>().sharedMaterials = new[] { top, edgeMaterial };
            return item.transform;
        }
        private Mesh BuildBox()
        {
            var mesh = new Mesh { name = "Two Tone Box" };
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),new Vector3(-.5f,-.5f,.5f),
                new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f) };
            // Top-face UVs span 0..1 so a delivered tile texture covers one cell; side faces are left untextured.
            mesh.uv = System.Array.ConvertAll(mesh.vertices, v => new Vector2(v.x + .5f, v.z + .5f));
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 4,7,6,4,6,5 }, 0);
            mesh.SetTriangles(new[] { 0,1,2,0,2,3, 0,4,5,0,5,1, 1,5,6,1,6,2, 2,6,7,2,7,3, 3,7,4,3,4,0 }, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void Extrude(string label, Material face, Material side, params Vector2[] outline) => Extrude(knightBody, label, face, side, outline);
        private void Extrude(Transform parent, string label, Material face, Material side, Vector2[] outline)
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
            var part = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(parent, false);
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
