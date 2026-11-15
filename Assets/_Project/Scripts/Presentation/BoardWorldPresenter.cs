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
        private readonly List<Transform> cellPieces = new List<Transform>();
        // A delivered board model is cut into one shard per cell at load, so the board can break apart.
        private Transform boardModel;
        private GameObject boardShards;
        public bool BoardShatters => boardShards != null;
        // The board breaks in steps: cracks after chorus 1, sunken cells and rubble after chorus 2,
        // light through the centre once the king falls, and the centre bursting open when the song ends.
        private Material crackMaterial;
        private GameObject crackOverlay;
        private Vector3[] cellHome, cellTilt, cellScale, cellSpin, rubbleScale;
        private float[] cellSink, cellChance;
        // Where a piece lies, from the outside in: untouched, tipping at the hole's edge, thrown late, thrown first.
        private const int Outer = 0, Rim = 1, Ring = 2, Centre = 3;
        private int[] cellZone;
        private readonly HashSet<Transform> borderPieces = new HashSet<Transform>();
        private bool[] cellFalls;
        // Light under the centre (seen through the gaps), a glow over it, and rays across the board when it bursts.
        private Transform lightFloor, lightGlow, lightRays;
        // The lowest point of the stone's playing surface in the middle; the light lies just under it.
        private float stoneLow;
        private Material glowMaterial, rayMaterial;
        public bool LightBelow => lightFloor != null && lightFloor.gameObject.activeSelf;
        private readonly List<Transform> rubble = new List<Transform>();
        private float breakEased, burst, shakeAge = 1;
        private Vector2 viewHome;
        private bool whiteWorld;
        private readonly List<GameObject> hiddenForWhite = new List<GameObject>();
        private Transform endingNoise;
        /// <summary>How many choruses have ended (0..2): the break step the board is heading to.</summary>
        public int BreakLevel { get; private set; }
        /// <summary>The break shown now: 0 intact, 1 cracked, 2 crumbling, 3 light through the centre.</summary>
        public float BreakStage { get; private set; }
        /// <summary>A value of 0 or more replaces the stage the song position gives: the ending (2..3), captures and tests.</summary>
        public float BreakOverride { get; set; } = -1;
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
                // Without readable meshes the model cannot be cut; it then only cracks and stays in one piece.
                BuildShards();
            }
            else
            {
                Box("Board Plinth", world.transform, new Vector3(0, -.30f, 0), new Vector3(8.40f, .38f, 8.40f), edgeMaterial);
                Box("Board Rim", world.transform, new Vector3(0, -.13f, 0), new Vector3(8.20f, .14f, 8.20f), gold);
                Box("Board Deck", world.transform, new Vector3(0, -.115f, 0), new Vector3(8, .08f, 8), edgeMaterial);
            }
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                if (art.boardPrefab == null)
                    cellPieces.Add(Box($"Cell_{x}_{y}", world.transform, CellPosition(new GridPosition(x, y)) - Vector3.up * .05f,
                        new Vector3(.975f, .10f, .975f), (x + y) % 2 == 0 ? dark : ivory));
                if (y == 7)
                    Box($"AttackEdge_{x}", world.transform, new Vector3(x - 3.5f, .014f, 3.90f), new Vector3(.83f, .028f, .065f), gold);
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
            BuildBreak();
            ResetVisuals();
        }
        private void BuildBreak()
        {
            viewHome = ((RectTransform)transform).anchoredPosition;
            // A build finds the shader through the ArtSet's material; Shader.Find covers a project that has not run the setup menu.
            var shader = Shader.Find("String As a Sword/Board Crack");
            if (art.boardCrack != null) crackMaterial = new Material(art.boardCrack);
            else if (shader != null) crackMaterial = new Material(shader);
            if (crackMaterial != null)
            {
                resources.Add(crackMaterial);
                // Just above the stone, below the attack tiles (y .024).
                var mesh = new Mesh { name = "Board Cracks",
                    vertices = new[] { new Vector3(-4,.004f,-4), new Vector3(-4,.004f,4), new Vector3(4,.004f,4), new Vector3(4,.004f,-4) },
                    uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) }, triangles = new[] { 0,1,2,0,2,3 } };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
                crackOverlay = new GameObject("Board Cracks", typeof(MeshFilter), typeof(MeshRenderer));
                crackOverlay.transform.SetParent(world.transform, false);
                crackOverlay.GetComponent<MeshFilter>().sharedMesh = mesh;
                crackOverlay.GetComponent<MeshRenderer>().sharedMaterial = crackMaterial;
                // The same shader in its light modes, on the same quad laid out three ways.
                Transform Sheet(string label, int mode, int queue, out Material material)
                {
                    material = new Material(crackMaterial) { name = label, renderQueue = queue }; resources.Add(material);
                    material.SetFloat("_Mode", mode);
                    var sheet = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
                    sheet.transform.SetParent(world.transform, false);
                    sheet.GetComponent<MeshFilter>().sharedMesh = mesh; sheet.GetComponent<MeshRenderer>().sharedMaterial = material;
                    sheet.SetActive(false);
                    return sheet.transform;
                }
                // Just under the stone, so it shows even through a narrow seam seen from the side.
                lightFloor = Sheet("Light Below", 3, 2985, out _); lightFloor.localPosition = new Vector3(0, stoneLow - .008f, 0); lightFloor.localScale = new Vector3(FragmentHalf / 4, 1, FragmentHalf / 4);
                lightRays = Sheet("Light Rays", 2, 3010, out rayMaterial); lightRays.localPosition = new Vector3(0, .03f, 0);
                // The glow faces the camera and washes over whatever is in front of it.
                lightGlow = Sheet("Light Glow", 1, 3020, out glowMaterial);
                lightGlow.localRotation = BoardCamera.transform.localRotation * Quaternion.Euler(-90, 0, 0);
                glowMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            var random = new System.Random(87);
            float Next() => (float)random.NextDouble();
            cellHome = cellPieces.Select(p => p.localPosition).ToArray();
            cellScale = cellPieces.Select(p => p.localScale).ToArray();
            int count = cellHome.Length;
            cellSink = new float[count]; cellTilt = new Vector3[count];
            cellZone = new int[count]; cellFalls = new bool[count]; cellChance = new float[count]; cellSpin = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                // About one cell in five settles. The sink is deeper than a tilted corner rises, so nothing pokes through the tiles.
                bool settles = Next() < .2f; float sink = .04f + .04f * Next(); var tilt = new Vector3(Next() * 5 - 2.5f, 0, Next() * 5 - 2.5f);
                if (settles) { cellSink[i] = sink; cellTilt[i] = tilt; }
                float reach = Mathf.Max(Mathf.Abs(cellHome[i].x), Mathf.Abs(cellHome[i].z));
                float radius = new Vector2(cellHome[i].x, cellHome[i].z).magnitude;
                bool cut = boardShards != null;
                cellZone[i] = reach >= 3 ? Outer : radius < 1.9f ? Centre : (cut ? borderPieces.Contains(cellPieces[i]) : radius >= 2.75f) ? Rim : Ring;
                cellChance[i] = Next();
                cellSpin[i] = new Vector3(Next() - .5f, Next() - .5f, Next() - .5f).normalized;
            }
            // The four pieces nearest the centre drop out first and leave the hole.
            foreach (int i in Enumerable.Range(0, count).OrderBy(i => new Vector2(cellHome[i].x, cellHome[i].z).sqrMagnitude).Take(4)) cellFalls[i] = true;
            var stone = Material(new Color(.42f, .43f, .42f));
            rubbleScale = new Vector3[14];
            for (int i = 0; i < rubbleScale.Length; i++)
            {
                rubbleScale[i] = new Vector3(.05f + .08f * Next(), .03f + .04f * Next(), .05f + .08f * Next());
                var chip = Box($"Rubble_{i}", world.transform, new Vector3(Next() * 7.2f - 3.6f, .02f, Next() * 7.2f - 3.6f), rubbleScale[i], i % 2 == 0 ? stone : edgeMaterial);
                chip.localRotation = Quaternion.Euler(Next() * 30 - 15, Next() * 360, Next() * 30 - 15);
                rubble.Add(chip);
            }
        }
        /// <summary>Shows a break stage at once (0..3). During play Render sets it from the song position.</summary>
        public void SetBreak(float stage)
        {
            BreakStage = Mathf.Clamp(stage, 0, 3);
            if (whiteWorld) return;
            float settle = Mathf.SmoothStep(0, 1, Mathf.Clamp01(BreakStage - 1));
            // 0..1 from the king's fall to the end of the song: the centre comes apart over the light below.
            float leak = Mathf.Clamp01(BreakStage - 2);
            float gap = Mathf.SmoothStep(0, 1, leak / .25f);
            if (crackMaterial != null)
            {
                crackMaterial.SetFloat("_Crack", Mathf.Clamp01(BreakStage / 2));
                crackMaterial.SetFloat("_Light", leak);
                crackMaterial.SetFloat("_Hole", burst > 0 ? 1 : gap);
                // Up to the burst only the pieces inside the border move; then the whole cut does.
                crackMaterial.SetFloat("_HoleRadius", burst > 0 ? FragmentHalf * 1.45f : FragmentHalf - .3f);
                crackOverlay.SetActive(BreakStage > 0 || burst > 0);
                bool lit = leak > 0 || burst > 0;
                lightFloor.gameObject.SetActive(lit); lightGlow.gameObject.SetActive(lit); lightRays.gameObject.SetActive(burst > 0);
                float glow = (1.2f + 2.2f * leak + 9 * burst * burst) / 8;
                lightGlow.localScale = new Vector3(glow, 1, glow);
                glowMaterial.SetFloat("_Light", Mathf.Clamp01(.45f * leak + 1.3f * burst));
                // Both stay inside the board view: its edge would cut them off in a straight line.
                float rays = (4 + 8 * burst) / 8;
                lightRays.localScale = new Vector3(rays, 1, rays);
                rayMaterial.SetFloat("_Light", Mathf.Clamp01(burst * 2.5f));
            }
            bool broken = settle > 0 || burst > 0;
            if (boardShards != null) { boardShards.SetActive(broken); boardModel.gameObject.SetActive(!broken); }
            Vector3 eye = BoardCamera.transform.localPosition;
            for (int i = 0; i < cellPieces.Count; i++)
            {
                // A sunken piece in the centre comes back up first: it would lie under the light.
                Vector3 home = cellHome[i], position = home + Vector3.down * (cellSink[i] * settle * (cellZone[i] != Outer ? 1 - gap : 1));
                var rotation = Quaternion.Euler(cellTilt[i] * (settle * (cellZone[i] != Outer ? 1 - gap : 1)));
                float scale = 1, chance = cellChance[i]; bool shown = true;
                var outward = new Vector3(home.x, 0, home.z); float distance = outward.magnitude;
                outward = distance > .01f ? outward / distance : Vector3.forward;
                // Tipping about this axis lifts the edge that faces the centre.
                Vector3 hinge = Vector3.Cross(outward, Vector3.up), toEye = (eye - home).normalized;
                // Every loose piece of the middle shrinks a little at once, so light shows in every seam.
                if (cellZone[i] >= Ring) scale = 1 - .05f * gap;
                if (cellZone[i] == Centre && (leak > 0 || burst > 0))
                {
                    // Pieces nearer the centre start first.
                    float order = Mathf.Clamp01(distance / 2.8f);
                    float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp01((leak - order * .6f) / .4f));
                    position += Vector3.up * (lift * (.04f + .2f * (1 - order))) + outward * (.05f * lift);
                    rotation = Quaternion.AngleAxis(lift * (6 + 20 * chance) * (1 - .5f * order), (hinge + cellSpin[i] * .5f).normalized) * rotation;
                    if (cellFalls[i])
                    {
                        float fall = Mathf.Clamp01((leak - .7f) / .3f);
                        position += Vector3.down * (1.4f * fall * fall);
                        rotation = Quaternion.AngleAxis(fall * 70, cellSpin[i]) * rotation;
                        shown = burst <= 0;
                    }
                    else if (burst > 0)
                    {
                        // Thrown out of the hole and toward the camera, so the pieces grow as they come.
                        float start = order * .25f, flight = Mathf.Clamp01((burst - start) / (1 - start));
                        flight = 1 - (1 - flight) * (1 - flight);
                        position += (outward * .6f + toEye + cellSpin[i] * .25f).normalized * (flight * (4 + 5 * chance));
                        rotation = Quaternion.AngleAxis(flight * (200 + 300 * chance), cellSpin[i]) * rotation;
                        shown = burst < 1;
                    }
                }
                else if (cellZone[i] != Outer && (leak > 0 || burst > 0))
                {
                    // Around the hole the pieces only stir, then are pushed back and tip away from it. The nearer ones follow the centre out.
                    float stir = cellZone[i] == Ring ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((leak - .5f) / .5f)) : 0, push = Mathf.SmoothStep(0, 1, Mathf.Clamp01(burst * 2));
                    position += Vector3.up * (.02f * stir + .3f * push) + outward * (.25f * push);
                    rotation = Quaternion.AngleAxis(stir * (2 + 4 * chance) + push * (18 + 30 * chance), hinge) * rotation;
                    if (cellZone[i] == Ring)
                    {
                        float flight = Mathf.Clamp01((burst - .2f - .2f * chance) / .6f);
                        flight = 1 - (1 - flight) * (1 - flight);
                        position += (outward * .8f + toEye).normalized * (flight * (4 + 3 * chance));
                        rotation = Quaternion.AngleAxis(flight * 240, cellSpin[i]) * rotation;
                        shown = burst < 1;
                    }
                }
                cellPieces[i].localPosition = position; cellPieces[i].localRotation = rotation;
                cellPieces[i].localScale = cellScale[i] * scale;
                cellPieces[i].gameObject.SetActive(shown);
            }
            for (int i = 0; i < rubble.Count; i++)
            {
                // Chips lying on pieces that move would be left in the air.
                Vector3 at = rubble[i].localPosition;
                float keep = Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.z)) < FragmentHalf + .1f ? 1 - Mathf.Max(gap, burst > 0 ? 1 : 0) : 1;
                rubble[i].localScale = rubbleScale[i] * (settle * keep);
                rubble[i].gameObject.SetActive(settle * keep > 0);
            }
        }
        /// <summary>Song end: 0..1, the centre of the board is thrown out and the hole of light opens.</summary>
        public void SetBurst(float progress) { burst = Mathf.Clamp01(progress); SetBreak(BreakStage); }
        /// <summary>After the burst: nothing but white, the knight, and one noise at the given board position.</summary>
        public void EnterWhiteWorld(Vector3 noiseAt)
        {
            if (whiteWorld) return;
            whiteWorld = true;
            foreach (Transform child in world.transform)
            {
                if (child == player || child.GetComponent<Camera>() != null || child.GetComponent<Light>() != null || !child.gameObject.activeSelf) continue;
                child.gameObject.SetActive(false); hiddenForWhite.Add(child.gameObject);
            }
            BoardCamera.backgroundColor = Color.white;
            if (endingNoise == null) endingNoise = BuildEndingNoise();
            endingNoise.localPosition = noiseAt; endingNoise.gameObject.SetActive(true);
        }
        private void LeaveWhiteWorld()
        {
            if (!whiteWorld) return;
            whiteWorld = false;
            foreach (var item in hiddenForWhite) if (item != null) item.SetActive(true);
            hiddenForWhite.Clear();
            endingNoise.gameObject.SetActive(false);
            ResetVoid();
        }
        private Transform BuildEndingNoise()
        {
            if (art.noisePrefab != null) { var made = Instantiate(art.noisePrefab, world.transform, false).transform; made.name = "Ending Noise"; return made; }
            // The battle's wave silhouette in near-black: the one thing left on the white.
            var ink = Material(new Color(.07f, .07f, .08f));
            var item = new GameObject("Ending Noise").transform; item.SetParent(world.transform, false);
            var wave = new GameObject("Wave Silhouette").transform; wave.SetParent(item, false);
            wave.localRotation = Quaternion.Euler(Elevation, 0, 0);
            Box("Wave", wave, new Vector3(0, .25f, 0), new Vector3(.65f, .055f, .10f), ink);
            for (int j = 0; j < 3; j++)
                Box($"WaveBar{j}", wave, new Vector3((j - 1) * .20f, .25f, 0), new Vector3(.075f, j == 1 ? .58f : .32f, .12f), ink);
            foreach (var renderer in item.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = new[] { ink, ink };
            return item;
        }
        // The steps follow the boss chart's sections, the same boundaries where the boss's attacks change.
        private int BreakLevelNow()
        {
            var chart = game.Battle.Settings.Chart;
            if (chart == null) return 0;
            int level = 0, closed = game.Battle.LastClosedNote;
            for (int chorus = 1; chorus <= 2; chorus++) { int end = chart.ChorusEnd(chorus); if (end >= 0 && closed >= end) level++; }
            return level;
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
            FreeRoam = false; SetKingDown(0);
            LeaveWhiteWorld();
            BreakLevel = 0; breakEased = 0; BreakOverride = -1; burst = 0; shakeAge = 1;
            ((RectTransform)transform).anchoredPosition = viewHome;
            SetBreak(0);
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
            float dt = Time.unscaledDeltaTime;
            int level = BreakLevelNow();
            if (level > BreakLevel) shakeAge = 0;
            BreakLevel = level;
            breakEased = Mathf.MoveTowards(breakEased, level, dt / .6f);
            SetBreak(BreakOverride >= 0 ? BreakOverride : breakEased);
            // Each new break jolts the board view for a quarter second.
            shakeAge += dt;
            // While the centre comes apart the view keeps trembling, more as the light grows.
            float jolt = Mathf.Clamp01(1 - shakeAge / .25f) * 7 + Mathf.Clamp01(BreakStage - 2) * 2;
            ((RectTransform)transform).anchoredPosition = viewHome + new Vector2(Mathf.Sin(shakeAge * 95), Mathf.Cos(shakeAge * 71)) * jolt;
            if (FreeRoam) return;
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
            // The outermost cells stay one shard each. The middle 6x6 is cut into angular fragments of uneven size instead.
            var cells = new Transform[64];
            for (int i = 0; i < 64; i++)
            {
                if (Central(i % 8) && Central(i / 8)) continue;
                cells[i] = new GameObject($"Shard_{i % 8}_{i / 8}").transform; cells[i].SetParent(boardShards.transform, false);
                cells[i].localPosition = CellPosition(new GridPosition(i % 8, i / 8));
            }
            var outlines = FragmentOutlines();
            var fragments = new Transform[outlines.Count];
            for (int i = 0; i < fragments.Length; i++)
            {
                Vector2 middle = Vector2.zero; foreach (var corner in outlines[i]) middle += corner;
                middle /= outlines[i].Count;
                fragments[i] = new GameObject($"Fragment_{i}").transform; fragments[i].SetParent(boardShards.transform, false);
                fragments[i].localPosition = new Vector3(middle.x, 0, middle.y);
                // Fragments on the border of the cut stay put until the burst, so the cut's square outline never shows.
                foreach (var corner in outlines[i])
                    if (Mathf.Max(Mathf.Abs(corner.x), Mathf.Abs(corner.y)) > FragmentHalf - .001f) { borderPieces.Add(fragments[i]); break; }
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
                var builders = new ShardBuilder[64]; var fragmentBuilders = new ShardBuilder[fragments.Length];
                var polygon = new List<ShardVertex>(12); var scratch = new List<ShardVertex>(12); var inMiddle = new List<ShardVertex>(12);
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var triangles = source.GetTriangles(sub);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        ShardVertex a = Vertex(triangles[t]), b = Vertex(triangles[t + 1]), c = Vertex(triangles[t + 2]);
                        int x0 = Cell(Mathf.Min(a.Position.x, Mathf.Min(b.Position.x, c.Position.x))), x1 = Cell(Mathf.Max(a.Position.x, Mathf.Max(b.Position.x, c.Position.x)));
                        int z0 = Cell(Mathf.Min(a.Position.z, Mathf.Min(b.Position.z, c.Position.z))), z1 = Cell(Mathf.Max(a.Position.z, Mathf.Max(b.Position.z, c.Position.z)));
                        // Fragments are plates: only the stone near the surface goes with them, not the underside of the board.
                        bool surface = Mathf.Max(a.Position.y, Mathf.Max(b.Position.y, c.Position.y)) > -PlateDepth;
                        if (surface && x1 >= 1 && x0 <= 6 && z1 >= 1 && z0 <= 6)
                        {
                            // The part of the triangle over the middle, shared out among the fragments.
                            inMiddle.Clear(); inMiddle.Add(a); inMiddle.Add(b); inMiddle.Add(c);
                            Clip(inMiddle, scratch, 0, -FragmentHalf, true); Clip(inMiddle, scratch, 0, FragmentHalf, false);
                            Clip(inMiddle, scratch, 2, -FragmentHalf, true); Clip(inMiddle, scratch, 2, FragmentHalf, false);
                            for (int f = 0; f < fragments.Length && inMiddle.Count >= 3; f++)
                            {
                                polygon.Clear(); polygon.AddRange(inMiddle);
                                var outline = outlines[f];
                                for (int e = 0; e < outline.Count && polygon.Count >= 3; e++)
                                {
                                    // Outline corners run counter-clockwise seen from above, so the inside is on the left of each edge.
                                    Vector2 from = outline[e], along = outline[(e + 1) % outline.Count] - from;
                                    Clip(polygon, scratch, along.y, -along.x, along.y * from.x - along.x * from.y);
                                }
                                if (polygon.Count < 3) continue;
                                foreach (var corner in polygon) if (corner.Normal.y > .5f) stoneLow = Mathf.Min(stoneLow, corner.Position.y);
                                (fragmentBuilders[f] ?? (fragmentBuilders[f] = new ShardBuilder(source.subMeshCount))).Add(polygon, sub, fragments[f].localPosition);
                            }
                        }
                        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++)
                        {
                            if (Central(x) && Central(z)) continue;
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
                for (int f = 0; f < fragments.Length; f++)
                {
                    if (fragmentBuilders[f] == null) continue;
                    var mesh = fragmentBuilders[f].Build($"Fragment {f} {source.name}", hasUv); resources.Add(mesh);
                    var part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
                    part.transform.SetParent(fragments[f], false);
                    part.GetComponent<MeshFilter>().sharedMesh = mesh; part.GetComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }
            // The model is a shell: a dark core under each cell closes the cut faces once the shards drift apart.
            // Its top stays well under the stone: the carved surface dips in places and the core would show through as a black patch.
            float depth = Mathf.Max(.1f, -bottom - .13f);
            for (int cell = 0; cell < 64; cell++)
                if (cells[cell] != null) Box("Core", cells[cell], new Vector3(0, -.12f - depth / 2, 0), new Vector3(.985f, depth, .985f), edgeMaterial);
            for (int f = 0; f < fragments.Length; f++)
            {
                // A fragment is seen from the side once it lifts, so its core has walls right up to the stone.
                var mesh = Prism($"Fragment Core {f}", outlines[f], fragments[f].localPosition, stoneLow - .004f, -.12f, -PlateDepth); resources.Add(mesh);
                var core = new GameObject("Core", typeof(MeshFilter), typeof(MeshRenderer));
                core.transform.SetParent(fragments[f], false);
                core.GetComponent<MeshFilter>().sharedMesh = mesh; core.GetComponent<MeshRenderer>().sharedMaterial = edgeMaterial;
            }
            foreach (var cell in cells) if (cell != null) cellPieces.Add(cell);
            cellPieces.AddRange(fragments);
            boardShards.SetActive(false);
            return true;
        }
        private const float FragmentHalf = 3, PlateDepth = .2f;
        private static bool Central(int index) => index >= 1 && index <= 6;
        /// <summary>
        /// Convex outlines that tile the middle 6x6 cells: small near the centre, larger toward its edge.
        /// Each is the square cut down by the bisectors between one seed point and every other.
        /// </summary>
        private static List<List<Vector2>> FragmentOutlines()
        {
            var random = new System.Random(31);
            float Next() => (float)random.NextDouble();
            var seeds = new List<Vector2>();
            for (int attempt = 0; attempt < 4000 && seeds.Count < 48; attempt++)
            {
                float angle = Next() * Mathf.PI * 2, radius = 4.1f * Mathf.Pow(Next(), 1.3f);
                var seed = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (Mathf.Abs(seed.x) > FragmentHalf - .1f || Mathf.Abs(seed.y) > FragmentHalf - .1f) continue;
                // Seeds too close together would leave slivers.
                float room = .2f + .2f * radius;
                bool crowded = false;
                foreach (var other in seeds) if ((other - seed).sqrMagnitude < room * room) { crowded = true; break; }
                if (!crowded) seeds.Add(seed);
            }
            var outlines = new List<List<Vector2>>();
            var scratch = new List<Vector2>();
            foreach (var seed in seeds)
            {
                const float h = FragmentHalf;
                var outline = new List<Vector2> { new Vector2(-h, -h), new Vector2(h, -h), new Vector2(h, h), new Vector2(-h, h) };
                foreach (var other in seeds)
                {
                    if (other == seed || outline.Count < 3) continue;
                    Vector2 normal = other - seed, middle = (other + seed) / 2;
                    float limit = Vector2.Dot(normal, middle);
                    scratch.Clear();
                    var previous = outline[outline.Count - 1]; float previousSide = limit - Vector2.Dot(normal, previous);
                    foreach (var current in outline)
                    {
                        float side = limit - Vector2.Dot(normal, current);
                        if (side >= 0 != previousSide >= 0) scratch.Add(Vector2.Lerp(previous, current, previousSide / (previousSide - side)));
                        if (side >= 0) scratch.Add(current);
                        previous = current; previousSide = side;
                    }
                    outline.Clear(); outline.AddRange(scratch);
                }
                if (outline.Count >= 3) outlines.Add(outline);
            }
            return outlines;
        }
        /// <summary>A closed column under an outline: walls from wallTop down to bottom, a lid at lidTop, a floor at bottom.</summary>
        private static Mesh Prism(string name, List<Vector2> outline, Vector3 origin, float wallTop, float lidTop, float bottom)
        {
            var positions = new List<Vector3>(); var normals = new List<Vector3>(); var triangles = new List<int>();
            void Face(Vector3 normal, params Vector3[] corners)
            {
                int start = positions.Count;
                foreach (var corner in corners) { positions.Add(corner - origin); normals.Add(normal); }
                // Unity's front faces run clockwise; turn the fan around when it does not face along the normal.
                bool flip = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]), normal) < 0;
                for (int i = 1; i < corners.Length - 1; i++) { triangles.Add(start); triangles.Add(start + (flip ? i + 1 : i)); triangles.Add(start + (flip ? i : i + 1)); }
            }
            Vector3 At(Vector2 corner, float y) => new Vector3(corner.x, y, corner.y);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count], along = b - a;
                Face(new Vector3(along.y, 0, -along.x).normalized, At(a, wallTop), At(b, wallTop), At(b, bottom), At(a, bottom));
            }
            Face(Vector3.up, outline.Select(corner => At(corner, lidTop)).ToArray());
            Face(Vector3.down, outline.Select(corner => At(corner, bottom)).ToArray());
            var mesh = new Mesh { name = name };
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds(); return mesh;
        }
        private static int Cell(float coordinate) => Mathf.Clamp(Mathf.FloorToInt(coordinate + 4), 0, 7);
        private struct ShardVertex { public Vector3 Position, Normal; public Vector2 Uv; }
        // Keeps the side at or above (or below) the limit on one axis.
        private static void Clip(List<ShardVertex> polygon, List<ShardVertex> scratch, int axis, float limit, bool keepAbove)
        {
            float sign = keepAbove ? -1 : 1;
            Clip(polygon, scratch, axis == 0 ? sign : 0, axis == 2 ? sign : 0, sign * limit);
        }
        // Sutherland-Hodgman against one upright plane; keeps the side where nx * x + nz * z is at most the limit.
        private static void Clip(List<ShardVertex> polygon, List<ShardVertex> scratch, float nx, float nz, float limit)
        {
            if (polygon.Count == 0) return;
            scratch.Clear();
            var previous = polygon[polygon.Count - 1];
            float previousSide = limit - nx * previous.Position.x - nz * previous.Position.z;
            foreach (var current in polygon)
            {
                float side = limit - nx * current.Position.x - nz * current.Position.z;
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
