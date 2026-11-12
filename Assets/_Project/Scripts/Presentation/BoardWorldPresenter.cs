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
        // One pool per AttackResponse, indexed by its value.
        private readonly GameObject[][] warnings = { new GameObject[64], new GameObject[64], new GameObject[64] };
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
                boardPieces.Add(model.transform);
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
            selection.gameObject.SetActive(intact);
            foreach (var pickup in pickups) if (!intact) pickup.gameObject.SetActive(false);
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
            // Each response is already its own shape; the tint only backs it up.
            var guard = Material(TelegraphHud.ResponseColor(AttackResponse.GuardOnly, Color.white));
            var dodge = Material(TelegraphHud.ResponseColor(AttackResponse.DodgeOnly, Color.white));
            Mesh impact = TelegraphMesh(true);
            resources.Add(impact);
            var materials = new[] { dark, white };
            Mesh quad = null;
            for (int response = 0; response < warnings.Length; response++)
            {
                Mesh warning = TelegraphMesh((AttackResponse)response);
                resources.Add(warning);
                var marks = new[] { dark, response == (int)AttackResponse.GuardOnly ? guard : response == (int)AttackResponse.DodgeOnly ? dodge : white };
                var delivered = response == (int)AttackResponse.GuardOnly ? art.guardMark : response == (int)AttackResponse.DodgeOnly ? art.dodgeMark : art.eitherMark;
                if (delivered != null) { warning = quad ?? (quad = MarkQuad()); marks = new[] { delivered }; }
                for (int i = 0; i < 64; i++) warnings[response][i] = TelegraphCell((AttackResponse)response + "Warning", i, warning, marks);
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
            if (battle.LastClosedNote != lastClosedNote)
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
                for (int response = 0; response < warnings.Length; response++)
                    warnings[response][i].SetActive(warning && (int)cellResponses[i] == response);
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
        // Marks sit above the cell's lower strip, where a countdown badge covers them.
        private static Mesh TelegraphMesh(AttackResponse response)
        {
            if (response == AttackResponse.Either) return TelegraphMesh(false);
            var vertices = new List<Vector3>();
            var underlay = new List<int>(); var markings = new List<int>();
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float height, List<int> triangles)
            {
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x, height, a.y)); vertices.Add(new Vector3(b.x, height, b.y));
                vertices.Add(new Vector3(c.x, height, c.y)); vertices.Add(new Vector3(d.x, height, d.y));
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            void Line(Vector2 from, Vector2 to, float width)
            {
                Vector2 along = (to - from).normalized * width / 2, across = new Vector2(-along.y, along.x);
                from -= along; to += along;
                Quad(from - across, from + across, to + across, to - across, .024f, markings);
            }
            Quad(new Vector2(-.46f, -.46f), new Vector2(-.46f, .46f), new Vector2(.46f, .46f), new Vector2(.46f, -.46f), .015f, underlay);
            foreach (float side in new[] { -.45f, .45f })
            {
                Line(new Vector2(-.45f, side), new Vector2(.45f, side), .024f);
                Line(new Vector2(side, -.45f), new Vector2(side, .45f), .024f);
            }
            if (response == AttackResponse.GuardOnly)
            {
                // The same silhouette as the knight's guard pose.
                var outline = new[] { new Vector2(-.25f, .37f), new Vector2(.25f, .37f), new Vector2(.21f, .05f), new Vector2(0, -.13f), new Vector2(-.21f, .05f) };
                for (int i = 0; i < outline.Length; i++) Line(outline[i], outline[(i + 1) % outline.Length], .055f);
                Line(new Vector2(0, .27f), new Vector2(0, .03f), .045f);
            }
            else
            {
                // Four ways out: the safe direction depends on the attack, so no single arrow can be honest.
                var center = new Vector2(0, .13f);
                foreach (var way in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left })
                {
                    Vector2 tip = center + way * .27f, wing = new Vector2(-way.y, way.x) * .09f, back = center + way * .17f;
                    Line(center, tip, .045f); Line(back + wing, tip, .045f); Line(back - wing, tip, .045f);
                }
            }
            var mesh = new Mesh { name = response + " Warning", subMeshCount = 2 };
            mesh.SetVertices(vertices); mesh.SetTriangles(underlay, 0); mesh.SetTriangles(markings, 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
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
