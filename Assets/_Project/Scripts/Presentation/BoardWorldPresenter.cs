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
        private Transform player, knightBody, selection, sword;
        private Renderer[] knightRenderers; // the knight's own (not the sword swing or the glow quad)
        // How white the knight turns at the start of a guard (1 = solid white); it fades back over the guard's .34 s.
        private const float GuardWhite = .5f;
        private KnightGlow knightGlow;
        private SpriteRenderer slashRenderer;
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
        private Renderer[] outlineEdges = new Renderer[0];
        private bool outlineShown = true;
        // Two tile kinds: normal (guard or leave) and strong (a guard does not protect: AttackResponse.DodgeOnly).
        // A full-board GuardOnly attack is a normal attack with nowhere to go, so it shows the normal tile.
        private const int NormalTile = 0, StrongTile = 1;
        private readonly GameObject[][] warnings = { new GameObject[64], new GameObject[64] };        private int lastClosedNote = -1;
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
        // The board breaks in steps: cracks after chorus 1, sunken cells and chips breaking off after chorus 2,
        // light through the centre once the king falls, and the centre bursting open when the song ends.
        private Material crackMaterial;
        private GameObject crackOverlay;
        private Vector3[] cellHome, cellTilt, cellScale, cellSpin;
        private float[] cellSink, cellChance;
        // Where a piece lies, from the outside in: untouched, tipping at the hole's edge, thrown late, thrown first.
        private const int Outer = 0, Rim = 1, Ring = 2, Centre = 3;
        private int[] cellZone;
        private readonly HashSet<Transform> borderPieces = new HashSet<Transform>();
        // Which side of the cut a border piece lies on (x, z: -1, 0 or 1). A corner piece has both.
        private readonly Dictionary<Transform, Vector2> borderSides = new Dictionary<Transform, Vector2>();
        private bool[] cellFalls;
        // Light under the centre (seen through the gaps), a glow over it, and rays across the board when it bursts.
        private Transform lightFloor, lightGlow, lightRays;
        private float lightFloorY;
        // The lowest point of the stone's playing surface in the middle; the light lies just under it.
        private float stoneLow;
        private Material glowMaterial, rayMaterial;
        public bool LightBelow => lightFloor != null && lightFloor.gameObject.activeSelf;
        private Material tileLight, tileDark;
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
        // Chess pieces the boss summons for chart attacks, one per strike (PieceStrikePlanner). Just before the hit a sweeping
        // piece rises on its path's first cell and a slamming one comes to hover over its point; on the hit the sweeper runs
        // the path and the slammer drops straight down. Then they hold a moment and sink into the board.
        private sealed class StrikeActor
        {
            public Transform Body; public ChessPiece Piece; public PieceStrike Strike; public int TelegraphId;
            /// <summary>Song time of the hit.</summary>
            public double Impact;
            /// <summary>A slammer's shadow on its cell while it is in the air. Null for sweepers.</summary>
            public SpriteRenderer Shadow;
            /// <summary>The hit has jolted the view.</summary>
            public bool Struck;
        }
        // The board view's jolt when a piece hits (see PoseActor): pixels, and seconds since.
        private float slamPower, slamAge = 1;
        private readonly Stack<SpriteRenderer> shadowPool = new Stack<SpriteRenderer>();
        private SpriteRenderer TakeShadow()
        {
            if (shadowPool.Count > 0) { var pooled = shadowPool.Pop(); pooled.gameObject.SetActive(true); return pooled; }
            var shadow = new GameObject("Boss Piece Shadow", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            shadow.transform.SetParent(world.transform, false);
            shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow.sprite = ShadowSprite(); shadow.sharedMaterial = BoardSpriteMaterial();
            return shadow;
        }
        private readonly List<StrikeActor> strikeActors = new List<StrikeActor>();
        private readonly Dictionary<ChessPiece, Stack<Transform>> piecePool = new Dictionary<ChessPiece, Stack<Transform>>();
        private readonly HashSet<int> summonedTelegraphs = new HashSet<int>();
        private Material pieceFace, pieceSide;
        // Lead: how long before the hit a piece shows up. Enter: its rise or descent into place. Drop: the slam's fall, ending on the hit.
        // WindUp: the draw-back before the hit (a sweeper pulls back, a slammer rises). Drop: the slam's fall, ending on the hit.
        public const float LeadSeconds = .45f, EnterSeconds = .2f, WindUpSeconds = .2f, DropSeconds = .07f, HoldSeconds = .3f, SinkSeconds = .3f;
        // Hover: a slammer's height over its cell. EnterDrop: how far above that it comes down from. SinkDepth: how far below the board it hides.
        // WindUpHeight / PullBack: how far it draws back. Bounce / Overrun: how far it jolts up off a slam / runs past a sweep's end.
        public const float HoverHeight = 1f, EnterDrop = 1.2f, SinkDepth = 2f, WindUpHeight = .4f, PullBack = .3f, Bounce = .07f, Overrun = .18f;
        public int LiveSummons => strikeActors.Count;
        /// <summary>Ending: the knight is placed by the caller instead of following the grid.</summary>
        public bool FreeRoam { get; set; }
        public Camera BoardCamera { get; private set; }
        public RenderTexture Texture { get; private set; }
        public Transform PlayerTransform => player;
        /// <summary>
        /// The outline of the knight's cell. It only shows (orange) while a move-away attack is aimed at that cell; there is no
        /// plain mint outline. The prologue hides it until it hands over to the battle.
        /// </summary>
        public Transform CellOutline => selection;
        /// <summary>The root every board object hangs under (cells are its local space).</summary>
        public Transform WorldRoot => world.transform;
        public int TileCount { get; private set; }
        /// <summary>Board camera angle above the board; upright silhouettes tilt by it to face the camera.</summary>
        public float Elevation { get; private set; }
        private Color clearColor;
        // Noise always draws on top: its floating picture lives on its own layer, which the board camera skips. A second
        // camera at the same view draws only that layer, with no depth from the board, into its own texture, and that
        // texture is shown over the whole battle HUD (BuildNoiseOverlay). So no piece, mark, number or string covers it.
        private const int NoiseLayer = 30;
        private Camera noiseCamera;
        private RenderTexture noiseTexture;
        private UnityEngine.UI.RawImage boardOutput, noiseOverlay;

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
            cameraHome = BoardCamera.transform.localPosition;
            BoardCamera.clearFlags = CameraClearFlags.SolidColor;
            // With background art the board view is see-through, so the board sits in the scene instead of on a panel.
            // See-through uses black: anti-aliased edges blend toward the clear colour, and a tinted one leaves a fringe.
            clearColor = art.HasBackground ? new Color(0, 0, 0, 0) : new Color(.035f, .043f, .052f, 1);
            BoardCamera.backgroundColor = clearColor;
            BoardCamera.targetTexture = Texture; BoardCamera.allowHDR = false;
            BoardCamera.cullingMask &= ~(1 << NoiseLayer);
            boardOutput = output;
            noiseTexture = new RenderTexture(Texture.width, Texture.height, 24) { name = "Noise View", antiAliasing = 4 };
            noiseTexture.Create(); resources.Add(noiseTexture);
            noiseCamera = new GameObject("Noise Camera", typeof(Camera)).GetComponent<Camera>();
            noiseCamera.transform.SetParent(BoardCamera.transform, false);
            noiseCamera.clearFlags = CameraClearFlags.SolidColor; noiseCamera.backgroundColor = Color.clear;
            noiseCamera.cullingMask = 1 << NoiseLayer; noiseCamera.allowHDR = false;
            noiseCamera.targetTexture = noiseTexture;
            SyncNoiseCamera();
            box = BuildBox(); resources.Add(box);
            edgeMaterial = art.boardEdge != null ? art.boardEdge : Material(new Color(.075f, .095f, .105f));
            var ivory = art.lightTile != null ? art.lightTile : Material(new Color(.72f, .74f, .70f));
            var dark = art.darkTile != null ? art.darkTile : Material(new Color(.15f, .20f, .22f));
            tileLight = ivory; tileDark = dark;
            if (art.useLighting)
            {
                var light = new GameObject("Board Light", typeof(Light)).GetComponent<Light>(); light.transform.SetParent(world.transform, false);
                light.type = LightType.Directional; light.transform.localRotation = Quaternion.Euler(50, -30, 0);
                light.color = art.lightColor; light.intensity = art.lightIntensity;
                // The scene has no baked lighting, so its ambient comes from the default sky and tints the grey stone blue.
                // A flat grey keeps the stone neutral; the probe is set as well, since a runtime mode change does not rebuild it.
                var ambient = art.ambientColor.a > 0 ? art.ambientColor : new Color(.32f, .32f, .32f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = ambient;
                var probe = new UnityEngine.Rendering.SphericalHarmonicsL2(); probe.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = probe;
                RenderSettings.reflectionIntensity = 0;
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
                TileCount++;
            }
            selection = new GameObject("Player Cell Outline").transform; selection.SetParent(world.transform, false);
            Frame(selection, .91f, .026f, .042f, accent);
            outlineEdges = selection.GetComponentsInChildren<Renderer>(true);
            ShowOutline(false);
            BuildPlayer();
            // Built before the sword swing is added, so only the knight itself glows.
            knightRenderers = player.GetComponentsInChildren<Renderer>(true);
            knightGlow = new KnightGlow(BoardCamera, player, knightRenderers, art);
            if (art.kingPrefab != null)
            {
                king = Instantiate(art.kingPrefab, world.transform, false); king.name = "King";
                king.transform.localPosition = art.kingAnchor; king.transform.localScale = Vector3.one * art.kingScale;
            }
            BuildNoise();
            BuildTelegraphs();
            BuildSwordSwing(gold);
            BuildBreak();
            ResetVisuals();
        }
        private const float SlashFps = 24f;

        private void BuildSwordSwing(Material fallbackMaterial)
        {
            sword = new GameObject("Sword Swing").transform;
            sword.SetParent(player, false);
            if (art.attackSlashFrames != null && art.attackSlashFrames.Length > 0)
            {
                slashRenderer = sword.gameObject.AddComponent<SpriteRenderer>();
                slashRenderer.sharedMaterial = BoardSpriteMaterial();
                slashRenderer.sortingOrder = 2;
                sword.localPosition = Vector3.up * .96f + BoardCamera.transform.localRotation * Vector3.back * .2f;
                sword.localRotation = BoardCamera.transform.localRotation;
            }
            else
            {
                var arc = sword.gameObject.AddComponent<LineRenderer>();
                arc.useWorldSpace = false; arc.sharedMaterial = fallbackMaterial; arc.startWidth = .10f; arc.endWidth = .018f;
                arc.positionCount = 18; arc.numCapVertices = 3;
                for (int i = 0; i < 18; i++)
                {
                    float angle = Mathf.Lerp(-100, 95, i / 17f) * Mathf.Deg2Rad;
                    arc.SetPosition(i, new Vector3(Mathf.Sin(angle) * .72f, .52f + i * .022f, Mathf.Cos(angle) * .68f));
                }
            }
            sword.gameObject.SetActive(false);
        }

        private void RenderSwordSwing()
        {
            if (!sword.gameObject.activeSelf) return;
            if (slashRenderer != null)
            {
                int frame = (int)(attackAge * SlashFps);
                bool visible = frame < art.attackSlashFrames.Length;
                sword.gameObject.SetActive(visible);
                if (visible) slashRenderer.sprite = art.attackSlashFrames[frame];
                return;
            }

            sword.gameObject.SetActive(attackAge < .23f);
            sword.localRotation = Quaternion.Euler(0, -80 + attackAge / .23f * 125, 0);
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
                lightFloor = Sheet("Light Below", 3, 2985, out _); lightFloorY = stoneLow - .008f; lightFloor.localPosition = new Vector3(0, lightFloorY, 0);
                // A little smaller than the cut: its edge stays under the border pieces, whose outer edge never moves.
                lightFloor.localScale = new Vector3((FragmentHalf - .12f) / 4, 1, (FragmentHalf - .12f) / 4);
                lightRays = Sheet("Light Rays", 2, 3010, out rayMaterial); lightRays.localPosition = new Vector3(0, .03f, 0);
                // The glow faces the camera and washes over whatever is in front of it.
                lightGlow = Sheet("Light Glow", 1, 3020, out glowMaterial);
                lightGlow.localRotation = BoardCamera.transform.localRotation * Quaternion.Euler(-90, 0, 0);
                glowMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                if (boardShards != null) BuildLightBlades();
            }
            var random = new System.Random(87);
            float Next() => (float)random.NextDouble();
            cellHome = cellPieces.Select(p => p.localPosition).ToArray();
            cellScale = cellPieces.Select(p => p.localScale).ToArray();
            int count = cellHome.Length;
            cellSink = new float[count]; cellTilt = new Vector3[count];
            cellZone = new int[count]; cellFalls = new bool[count]; cellChance = new float[count]; cellSpin = new Vector3[count];
            int firstFragment = count - fragmentCount;
            for (int i = 0; i < count; i++)
            {
                // About one piece in five settles. The sink is deeper than a tilted corner rises, so nothing pokes through the tiles,
                // and shallow enough that the wall it bares stays inside the crack drawn along its edge.
                bool settles = Next() < .2f; float sink = .025f + .015f * Next(); var tilt = new Vector3(Next() * 2.4f - 1.2f, 0, Next() * 2.4f - 1.2f);
                // Fragments follow the rule the crack map is baked with, so every edge of one that settles lies in a crack.
                if (i >= firstFragment) settles = FragmentSettles(i - firstFragment);
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
            BuildChips();
        }
        // ---- Chips: bits of the surface that break off along the cracks at the second break and slide aside ----
        private sealed class Chip { public Transform Root, Scar; public Vector3 From, To; public Quaternion Turn; public float Delay; }
        private readonly List<Chip> chips = new List<Chip>();
        public int ChipCount => chips.Count;
        /// <summary>Chips shown now that have left the spot they broke from.</summary>
        public int ChipsMoved => chips.Count(c => c.Root.gameObject.activeSelf && (c.Root.localPosition - c.From).sqrMagnitude > .0004f);
        private struct ChipPlan
        {
            public List<Vector2> Outline; public Vector2 Centre, Destination, Low, High;
            public float Thickness, Yaw, Delay; public Vector2 Tilt;
        }
        private void BuildChips()
        {
            var plans = ChipLayout();
            var surfaces = CutChipSurfaces(plans);
            var parent = new GameObject("Board Chips").transform; parent.SetParent(world.transform, false);
            // The stone under the surface: darker than a light tile and lighter than a dark one, like the lip of a crack.
            var stone = Material(new Color(.42f, .43f, .42f));
            // The spot a chip left is a shallow pit in shadow, a shade darker than the stone.
            var pit = Material(new Color(.27f, .28f, .28f));
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i]; var surface = surfaces[i];
                var origin = new Vector3(plan.Centre.x, 0, plan.Centre.y);
                var root = new GameObject($"Chip_{i}").transform; root.SetParent(parent, false); root.localPosition = origin;
                float top;
                if (surface.Parts.Count > 0)
                {
                    // The chip's face is the board's own surface, cut out where it broke off.
                    foreach (var (mesh, materials) in surface.Parts)
                    {
                        var face = new GameObject("Face", typeof(MeshFilter), typeof(MeshRenderer));
                        face.transform.SetParent(root, false);
                        face.GetComponent<MeshFilter>().sharedMesh = mesh; face.GetComponent<MeshRenderer>().sharedMaterials = materials;
                    }
                    top = surface.Low;
                    // The broken sides are bare stone, lighter than the board's dark core, so the flake's edge catches the eye.
                    AddPrism(root, "Core", plan.Outline, origin, top - .0005f, top - .002f, top - plan.Thickness, stone);
                }
                else
                {
                    // A board of plain cells: the chip is a slab of the tile it came off.
                    top = 0;
                    int x = Mathf.FloorToInt(plan.Centre.x + 4), z = Mathf.FloorToInt(plan.Centre.y + 4);
                    AddPrism(root, "Core", plan.Outline, origin, top, top, top - plan.Thickness, (x + z) % 2 == 0 ? tileDark : tileLight);
                }
                // Where it broke off, the stone underneath shows: a little smaller than the chip, just over the surface.
                float scarY = (surface.Parts.Count > 0 ? surface.High : top) + .0015f;
                var scarOutline = plan.Outline.Select(p => plan.Centre + (p - plan.Centre) * .85f).ToList();
                var scar = new GameObject($"Scar_{i}").transform; scar.SetParent(parent, false); scar.localPosition = origin;
                AddPrism(scar, "Scar", scarOutline, origin, scarY, scarY, scarY - .001f, pit);
                // It lands with most of its thickness above the surface and lies nearly flat, so no edge hangs in the air.
                var destination = new Vector3(plan.Destination.x, plan.Thickness * .65f, plan.Destination.y);
                var turn = Quaternion.Euler(0, plan.Yaw, 0) * Quaternion.Euler(plan.Tilt.x, 0, plan.Tilt.y);
                chips.Add(new Chip { Root = root, Scar = scar, From = origin, To = destination, Turn = turn, Delay = plan.Delay });
            }
        }
        private void AddPrism(Transform parent, string label, List<Vector2> outline, Vector3 origin, float wallTop, float lidTop, float bottom, Material material)
        {
            var mesh = Prism($"{label} {parent.name}", outline, origin, wallTop, lidTop, bottom); resources.Add(mesh);
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh; item.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        /// <summary>
        /// Where chips break off: on cracks the crack map always draws by the second break (the outlines near the impact
        /// and the seams of fragments that settle), mostly near the impact. A few large chips, many small ones.
        /// </summary>
        private static List<ChipPlan> ChipLayout()
        {
            var random = new System.Random(61);
            float Next() => (float)random.NextDouble();
            var outlines = FragmentOutlines();
            var centroids = outlines.Select(o => o.Aggregate(Vector2.zero, (sum, p) => sum + p) / o.Count).ToList();
            long Key(Vector2 p) => (long)Mathf.RoundToInt((p.x + 8) * 500) * 100000 + Mathf.RoundToInt((p.y + 8) * 500);
            var sides = new Dictionary<long, (Vector2 a, Vector2 b, List<int> fragments)>();
            for (int f = 0; f < outlines.Count; f++)
                for (int i = 0; i < outlines[f].Count; i++)
                {
                    Vector2 a = outlines[f][i], b = outlines[f][(i + 1) % outlines[f].Count], middle = (a + b) / 2;
                    if (Mathf.Max(Mathf.Abs(middle.x), Mathf.Abs(middle.y)) > FragmentHalf - .01f || (b - a).sqrMagnitude < .01f) continue;
                    long ka = Key(a), kb = Key(b), key = System.Math.Min(ka, kb) * 10000000000L + System.Math.Max(ka, kb);
                    if (!sides.TryGetValue(key, out var edge)) sides[key] = edge = (a, b, new List<int>());
                    edge.fragments.Add(f);
                }
            var candidates = new List<(Vector2 a, Vector2 b, int settling, float weight)>();
            foreach (var (a, b, fragments) in sides.Values)
            {
                var settling = fragments.Where(FragmentSettles).ToList();
                float radius = ((a + b) / 2).magnitude;
                if (settling.Count == fragments.Count || (radius >= 1.5f && settling.Count == 0)) continue;
                candidates.Add((a, b, settling.Count > 0 ? settling[0] : -1, Mathf.Exp(-radius / 1.2f) * (b - a).magnitude));
            }
            float total = candidates.Sum(c => c.weight);
            var plans = new List<ChipPlan>();
            for (int n = 0; n < 36; n++)
            {
                float pick = Next() * total; var edge = candidates[candidates.Count - 1];
                foreach (var c in candidates) { if (pick < c.weight) { edge = c; break; } pick -= c.weight; }
                Vector2 along = (edge.b - edge.a).normalized, across = new Vector2(-along.y, along.x);
                var at = Vector2.Lerp(edge.a, edge.b, .15f + .7f * Next());
                // Away from a fragment that settles: a chip on it would be left in the air.
                float side = edge.settling >= 0 ? -Mathf.Sign(Vector2.Dot(centroids[edge.settling] - at, across)) : Next() < .5f ? -1 : 1;
                float radius = .028f + .1f * Mathf.Pow(Next(), 2), stretch = Mathf.Sqrt(1 + .8f * Next());
                var centre = at + across * side * radius * .5f;
                var corners = new List<Vector2>();
                int count = 5 + random.Next(3); float turn = Next() * Mathf.PI * 2;
                for (int k = 0; k < count; k++)
                {
                    float angle = turn + (k + (Next() - .5f) * .5f) * Mathf.PI * 2 / count, reach = radius * (.75f + .25f * Next());
                    // Longer along the crack than across it: a chip splits off the edge.
                    corners.Add(centre + along * (Mathf.Cos(angle) * reach * stretch) + across * (Mathf.Sin(angle) * reach / stretch));
                }
                var outline = ConvexHull(corners);
                float lean = 3 + 4 * Next(), lean2 = Next() * Mathf.PI * 2;
                plans.Add(new ChipPlan
                {
                    Outline = outline, Centre = centre,
                    Destination = centre + across * side * (.04f + .11f * Next()) + along * ((Next() - .5f) * .06f),
                    Low = new Vector2(outline.Min(p => p.x), outline.Min(p => p.y)), High = new Vector2(outline.Max(p => p.x), outline.Max(p => p.y)),
                    // Flakes: thin for their size.
                    Thickness = Mathf.Max(.01f, radius * (.28f + .17f * Next())),
                    Yaw = (Next() - .5f) * 140, Tilt = new Vector2(Mathf.Cos(lean2), Mathf.Sin(lean2)) * lean,
                    // Near the impact first.
                    Delay = .5f * Mathf.Clamp01(centre.magnitude / 2.5f),
                });
            }
            return plans;
        }
        // Counter-clockwise seen from above, as Clip and Prism expect.
        private static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var sorted = points.OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (var p in sorted)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
                sorted.Reverse();
            }
            return hull;
        }
        private sealed class ChipSurface { public readonly List<(Mesh mesh, Material[] materials)> Parts = new List<(Mesh, Material[])>(); public float Low = float.MaxValue, High = float.MinValue; }
        /// <summary>Cuts each chip's outline out of the delivered board's playing surface, with its own UVs and materials.</summary>
        private ChipSurface[] CutChipSurfaces(List<ChipPlan> plans)
        {
            var result = plans.Select(_ => new ChipSurface()).ToArray();
            if (boardModel == null) return result;
            var polygon = new List<ShardVertex>(12); var scratch = new List<ShardVertex>(12);
            foreach (var filter in boardModel.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
                if (source == null || !source.isReadable || renderer == null) continue;
                var toBoard = world.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var normalToBoard = toBoard.inverse.transpose;
                var positions = source.vertices; var normals = source.normals; var uvs = source.uv;
                bool hasNormals = normals.Length == positions.Length, hasUv = uvs.Length == positions.Length;
                for (int v = 0; v < positions.Length; v++)
                {
                    positions[v] = toBoard.MultiplyPoint3x4(positions[v]);
                    if (hasNormals) normals[v] = normalToBoard.MultiplyVector(normals[v]).normalized;
                }
                ShardVertex Vertex(int index) => new ShardVertex { Position = positions[index],
                    Normal = hasNormals ? normals[index] : Vector3.up, Uv = hasUv ? uvs[index] : Vector2.zero };
                var builders = new ShardBuilder[plans.Count];
                for (int sub = 0; sub < source.subMeshCount; sub++)
                {
                    var triangles = source.GetTriangles(sub);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        ShardVertex a = Vertex(triangles[t]), b = Vertex(triangles[t + 1]), c = Vertex(triangles[t + 2]);
                        // The playing surface only: not the rim's sides, nor faces deep in the board that also look up.
                        if (a.Normal.y < .5f || b.Normal.y < .5f || c.Normal.y < .5f) continue;
                        if (Mathf.Min(a.Position.y, Mathf.Min(b.Position.y, c.Position.y)) < -PlateDepth) continue;
                        float minX = Mathf.Min(a.Position.x, Mathf.Min(b.Position.x, c.Position.x)), maxX = Mathf.Max(a.Position.x, Mathf.Max(b.Position.x, c.Position.x));
                        float minZ = Mathf.Min(a.Position.z, Mathf.Min(b.Position.z, c.Position.z)), maxZ = Mathf.Max(a.Position.z, Mathf.Max(b.Position.z, c.Position.z));
                        for (int p = 0; p < plans.Count; p++)
                        {
                            var plan = plans[p];
                            if (maxX < plan.Low.x || minX > plan.High.x || maxZ < plan.Low.y || minZ > plan.High.y) continue;
                            polygon.Clear(); polygon.Add(a); polygon.Add(b); polygon.Add(c);
                            var outline = plan.Outline;
                            for (int e = 0; e < outline.Count && polygon.Count >= 3; e++)
                            {
                                Vector2 from = outline[e], along = outline[(e + 1) % outline.Count] - from;
                                Clip(polygon, scratch, along.y, -along.x, along.y * from.x - along.x * from.y);
                            }
                            if (polygon.Count < 3) continue;
                            foreach (var corner in polygon) { result[p].Low = Mathf.Min(result[p].Low, corner.Position.y); result[p].High = Mathf.Max(result[p].High, corner.Position.y); }
                            (builders[p] ?? (builders[p] = new ShardBuilder(source.subMeshCount))).Add(polygon, sub, new Vector3(plan.Centre.x, 0, plan.Centre.y));
                        }
                    }
                }
                for (int p = 0; p < plans.Count; p++)
                {
                    if (builders[p] == null) continue;
                    var mesh = builders[p].Build($"Chip {p} {source.name}", hasUv); resources.Add(mesh);
                    result[p].Parts.Add((mesh, renderer.sharedMaterials));
                }
            }
            return result;
        }
        private GameObject lightBlades;
        private Material bladeMaterial;
        /// <summary>The light that stands in the cracks of the centre is shown (from the king's fall on).</summary>
        public bool LightInCracks => lightBlades != null && lightBlades.activeSelf;
        // A thin sheet of light stands on every crack between two centre fragments and leans away from the centre,
        // bright at the crack and fading upward. Together they read as light leaking out along the cracks.
        private void BuildLightBlades()
        {
            var seen = new HashSet<long>();
            var positions = new List<Vector3>(); var lifts = new List<Vector3>(); var uvs = new List<Vector2>(); var traits = new List<Vector2>();
            var triangles = new List<int>();
            var random = new System.Random(53);
            long Key(Vector2 p) => (long)Mathf.RoundToInt((p.x + 8) * 500) * 100000 + Mathf.RoundToInt((p.y + 8) * 500);
            foreach (var outline in FragmentOutlines())
                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Count], middle = (a + b) / 2;
                    // Two fragments share each crack: one blade for both. None on the border of the cut, which would draw its square.
                    long ka = Key(a), kb = Key(b);
                    if (!seen.Add(System.Math.Min(ka, kb) * 10000000000L + System.Math.Max(ka, kb))) continue;
                    if (Mathf.Max(Mathf.Abs(middle.x), Mathf.Abs(middle.y)) > FragmentHalf - .01f || (b - a).sqrMagnitude < .0004f) continue;
                    float chance = (float)random.NextDouble(), distance = middle.magnitude;
                    Vector3 outward = distance > .05f ? new Vector3(middle.x, 0, middle.y) / distance : Vector3.zero;
                    // The upper edge is lifted in the shader by this much per unit of reach: up and about 30 degrees outward.
                    Vector3 lift = (Vector3.up * .87f + outward * .5f) * (.55f + .9f * chance);
                    int first = positions.Count;
                    positions.Add(new Vector3(a.x, stoneLow - .004f, a.y)); positions.Add(new Vector3(b.x, stoneLow - .004f, b.y));
                    positions.Add(positions[first]); positions.Add(positions[first + 1]);
                    lifts.Add(Vector3.zero); lifts.Add(Vector3.zero); lifts.Add(lift); lifts.Add(lift);
                    uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
                    // x: when it lights up, from the centre outward. y: its own strength and flicker.
                    var trait = new Vector2(Mathf.Clamp01(distance / FragmentHalf), chance);
                    for (int k = 0; k < 4; k++) traits.Add(trait);
                    triangles.AddRange(new[] { first, first + 2, first + 1, first + 1, first + 2, first + 3 });
                }
            var mesh = new Mesh { name = "Light Blades" };
            mesh.SetVertices(positions); mesh.SetNormals(lifts); mesh.SetUVs(0, uvs); mesh.SetUVs(1, traits); mesh.SetTriangles(triangles, 0);
            // The shader moves the upper edges: bounds that hold the tallest blade.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(16, 12, 16)); resources.Add(mesh);
            bladeMaterial = new Material(crackMaterial) { name = "Light Blades", renderQueue = 3005 }; resources.Add(bladeMaterial);
            bladeMaterial.SetFloat("_Mode", 4);
            lightBlades = new GameObject("Light Blades", typeof(MeshFilter), typeof(MeshRenderer));
            lightBlades.transform.SetParent(world.transform, false);
            lightBlades.GetComponent<MeshFilter>().sharedMesh = mesh; lightBlades.GetComponent<MeshRenderer>().sharedMaterial = bladeMaterial;
            lightBlades.SetActive(false);
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
                // Pieces that sank at stage 2 are still coming back up: the light starts below the deepest of them
                // and rises with them, or it would lie over them as flat white patches.
                lightFloor.localPosition = new Vector3(0, lightFloorY - .16f * (1 - gap), 0);
                if (lightBlades != null)
                {
                    lightBlades.SetActive(lit);
                    // The blades come up from the centre outward, grow to the end of the song, jump at the burst and are lost in its light.
                    bladeMaterial.SetFloat("_Light", leak + burst);
                    bladeMaterial.SetFloat("_Reach", .25f + .75f * Mathf.SmoothStep(0, 1, leak) + 1.4f * Mathf.Clamp01(burst * 3));
                    bladeMaterial.SetColor("_LightColor", new Color(1, 1, 1, 1 - Mathf.InverseLerp(.45f, .85f, burst)));
                }
                // Until the burst the glow only marks the hole: the light belongs to the cracks.
                float glow = (.7f + 1.1f * leak + 16 * burst * burst) / 8;
                lightGlow.localScale = new Vector3(glow, 1, glow);
                glowMaterial.SetFloat("_Light", Mathf.Clamp01(.28f * leak + 1.3f * burst));
                float rays = (5 + 20 * burst) / 8;
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
                else if (cellZone[i] == Ring && (leak > 0 || burst > 0))
                {
                    // Around the centre the pieces only stir, then tip away from it and follow the centre out.
                    // The pieces on the border of the cut (Rim) are never pushed out: a gap along it would draw the cut's square in light.
                    float stir = Mathf.SmoothStep(0, 1, Mathf.Clamp01((leak - .5f) / .5f)), push = Mathf.SmoothStep(0, 1, Mathf.Clamp01(burst * 2));
                    position += Vector3.up * (.02f * stir + .3f * push) + outward * (.25f * push);
                    rotation = Quaternion.AngleAxis(stir * (2 + 4 * chance) + push * (18 + 30 * chance), hinge) * rotation;
                    float flight = Mathf.Clamp01((burst - .2f - .2f * chance) / .6f);
                    flight = 1 - (1 - flight) * (1 - flight);
                    position += (outward * .8f + toEye).normalized * (flight * (4 + 3 * chance));
                    rotation = Quaternion.AngleAxis(flight * 240, cellSpin[i]) * rotation;
                    shown = burst < 1;
                }
                else if (cellZone[i] == Rim && burst > 0 && borderSides.TryGetValue(cellPieces[i], out var side) && side.x * side.y == 0)
                {
                    // A border piece swings up like a flap about the border itself: its outer edge stays shut,
                    // so no light draws the square of the cut. Corner pieces and about a quarter of the rest stay flat.
                    float flap = Mathf.SmoothStep(0, 1, Mathf.Clamp01(burst * 2)) * 44 * Mathf.Clamp01((chance - .25f) / .75f);
                    var pivot = side.x != 0 ? new Vector3(side.x * FragmentHalf, home.y, home.z) : new Vector3(home.x, home.y, side.y * FragmentHalf);
                    var swing = side.x != 0 ? Quaternion.AngleAxis(-side.x * flap, Vector3.forward) : Quaternion.AngleAxis(side.y * flap, Vector3.right);
                    position = pivot + swing * (position - pivot); rotation = swing * rotation;
                }
                cellPieces[i].localPosition = position; cellPieces[i].localRotation = rotation;
                cellPieces[i].localScale = cellScale[i] * scale;
                cellPieces[i].gameObject.SetActive(shown);
            }
            // Chips break out of the cracks near the impact first, hop and slide aside. They all lie in the middle,
            // which comes apart once the king falls: there they go with it.
            float chipKeep = 1 - Mathf.Max(gap, burst > 0 ? 1 : 0);
            foreach (var chip in chips)
            {
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((settle - chip.Delay) / .35f));
                bool shown = k > 0 && chipKeep > 0;
                chip.Root.gameObject.SetActive(shown); chip.Scar.gameObject.SetActive(shown);
                if (!shown) continue;
                chip.Root.localPosition = Vector3.Lerp(chip.From, chip.To, k) + Vector3.up * (.05f * Mathf.Sin(Mathf.PI * k));
                chip.Root.localRotation = Quaternion.Slerp(Quaternion.identity, chip.Turn, k);
                chip.Root.localScale = Vector3.one * chipKeep;
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
        // Where the battle camera stands; the death close-up moves it and the next session puts it back.
        private Vector3 cameraHome;
        private bool cameraFocused;
        /// <summary>Battle camera position (board-local).</summary>
        public Vector3 CameraHome => cameraHome;
        /// <summary>
        /// Moves the board camera along its own view toward <paramref name="localFocus"/> (board-local), without turning it:
        /// 0 = the battle view, 1 = <paramref name="closeDistance"/> in front of the focus.
        /// </summary>
        public void FocusCamera(Vector3 localFocus, float closeDistance, float t)
        {
            var cam = BoardCamera.transform;
            Vector3 close = localFocus - cam.localRotation * Vector3.forward * closeDistance;
            cam.localPosition = Vector3.LerpUnclamped(cameraHome, close, t);
            cameraFocused = true;
        }
        /// <summary>
        /// The knight tipped over like a chess piece: it turns about the rim of its base on the side it falls to.
        /// <paramref name="side"/> is +1 (falls toward +x) or -1; <paramref name="degrees"/> is the tilt from upright;
        /// <paramref name="roll"/> turns the fallen piece about its own long axis. Needs FreeRoam, or Render puts it back.
        /// </summary>
        public void SetKnightDown(Vector3 basePosition, float side, float degrees, float roll, float baseRadius)
        {
            var tilt = Quaternion.AngleAxis(-side * degrees, Vector3.forward) * Quaternion.AngleAxis(roll, Vector3.up);
            Vector3 rim = new Vector3(side * baseRadius, 0, 0);
            player.localRotation = tilt;
            player.localPosition = basePosition + rim - tilt * rim;
            player.localScale = Vector3.one;
            sword.gameObject.SetActive(false);
            if (knightGlow != null) knightGlow.Whiten = 0;
        }
        /// <summary>Jolts the board view for a quarter second, as a break of the board does.</summary>
        public void Jolt() => shakeAge = 0;
        // A layer no other object uses: while the knight dies the board camera draws it alone, on a clear background.
        private const int KnightAloneLayer = 31;
        private readonly List<(GameObject go, int layer)> knightLayers = new List<(GameObject, int)>();
        private int cullingHome;
        /// <summary>The board camera draws only the knight (the death close-up), until the next session.</summary>
        public bool KnightIsolated { get; private set; }
        public void IsolateKnight()
        {
            if (KnightIsolated) return;
            KnightIsolated = true;
            cullingHome = BoardCamera.cullingMask;
            knightLayers.Clear();
            foreach (var part in player.GetComponentsInChildren<Transform>(true))
            { knightLayers.Add((part.gameObject, part.gameObject.layer)); part.gameObject.layer = KnightAloneLayer; }
            BoardCamera.cullingMask = 1 << KnightAloneLayer;
            BoardCamera.backgroundColor = Color.clear;
        }
        private void ReleaseKnight()
        {
            if (!KnightIsolated) return;
            KnightIsolated = false;
            foreach (var (go, layer) in knightLayers) if (go != null) go.layer = layer;
            knightLayers.Clear();
            BoardCamera.cullingMask = cullingHome;
            ResetVoid();
        }
        private const float StepSeconds = .13f, StepHeight = .13f, LiftHeight = .55f;
        private float moveSeconds = StepSeconds, moveHeight = StepHeight, liftNext, liftUntil;
        /// <summary>
        /// The knight's next change of cell, if it comes in the next quarter second, is not a step: it is lifted and carried
        /// there over <paramref name="seconds"/>, as a piece is set back up (the tutorial's end, when the song's battle puts it
        /// on its start cell). Without a change it lapses, so a later step is never slowed.
        /// </summary>
        public void LiftNextMove(float seconds) { liftNext = seconds; liftUntil = Time.unscaledTime + .25f; }
        public void SetVoidColor(Color color) => BoardCamera.backgroundColor = color;
        /// <summary>Back to the battle's clear colour (transparent when background art is assigned).</summary>
        public void ResetVoid() => BoardCamera.backgroundColor = clearColor;
        public static Vector3 CellPosition(GridPosition cell) => new Vector3(cell.X - 3.5f, 0, cell.Y - 3.5f);
        // The delivered board's painted squares are not an exact 1-unit grid: each one is 0.98-1.04 wide. These are its
        // square edges in board space (x for files A-H, z for ranks 1-8), measured 2026-11-28 by sampling
        // ChessBoard_BaseColor through the prefab's mesh and fitting a line to each edge (the edges are straight and
        // square to the axes within 0.02). Re-measure if the model, its texture or BoardArtSetup's fit changes.
        private static readonly float[] ModelEdgesX = { -4.010f, -2.999f, -2.010f, -1.017f, -.022f, .964f, 1.964f, 2.977f, 4.005f };
        private static readonly float[] ModelEdgesZ = { -4.005f, -2.970f, -1.956f, -.963f, .033f, 1.030f, 2.011f, 3.004f, 4.038f };
        /// <summary>
        /// The cell's painted square in board space (x, z): on the delivered board its measured square, otherwise the 1-unit cell.
        /// </summary>
        public Rect CellRect(int x, int y) => art.boardPrefab != null
            ? Rect.MinMaxRect(ModelEdgesX[x], ModelEdgesZ[y], ModelEdgesX[x + 1], ModelEdgesZ[y + 1])
            : new Rect(x - 4, y - 4, 1, 1);
        /// <summary>The top of the knight's head (the highest point of its own renderers) in board-view coordinates.</summary>
        public Vector2 ProjectKnightHead(Vector2 rectSize)
        {
            var bounds = new Bounds(); bool any = false;
            if (knightRenderers != null)
                foreach (var part in knightRenderers)
                {
                    if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                    if (any) bounds.Encapsulate(part.bounds); else { bounds = part.bounds; any = true; }
                }
            // Without renderers: a knight-sized guess above its cell.
            Vector3 head = any ? world.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z))
                : player.localPosition + Vector3.up * 1.2f;
            return ProjectToBoard(head, rectSize);
        }
        public Vector2 ProjectToBoard(Vector3 local, Vector2 rectSize)
        {
            Vector3 point = BoardCamera.WorldToViewportPoint(world.transform.TransformPoint(local));
            return new Vector2((point.x - .5f) * rectSize.x, (point.y - .5f) * rectSize.y);
        }
        public void ResetVisuals()
        {
            moveAge = attackAge = guardAge = 1;
            moveSeconds = StepSeconds; moveHeight = StepHeight; liftNext = 0;
            guardNote = -1;
            moveFrom = moveTarget = CellPosition(game.Session.Position);
            player.localPosition = moveTarget; selection.localPosition = moveTarget;
            player.localScale = Vector3.one; player.localRotation = Quaternion.identity;
            sword.gameObject.SetActive(false);
            if (knightGlow != null) knightGlow.Whiten = 0;
            if (cameraFocused) { BoardCamera.transform.localPosition = cameraHome; cameraFocused = false; }
            ReleaseKnight();
            impactAge = 1; impactCells = 0; lastClosedNote = -1;
            lastTelegraphId = -1; lastCollected = game.Noise.Collected;
            for (int i = 0; i < 64; i++)
            {
                foreach (var pool in warnings) pool[i].SetActive(false);
            }
            ShowOutline(false);
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
            if (result.Kind == PlayerActionKind.Attack)
            {
                attackAge = 0;
                sword.gameObject.SetActive(true);
                RenderSwordSwing();
                Cue("Attack", art.attackVfx);
            }
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
        // The board view's render target follows the view's size on screen, one texel per pixel:
        // a fixed size is shrunk without mipmaps (grain shimmers) or stretched (blurs) at any other resolution.
        private void MatchTargetToScreen()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            Vector2 size = ((RectTransform)transform).rect.size * canvas.rootCanvas.scaleFactor;
            int width = Mathf.Clamp(Mathf.RoundToInt(size.x), 256, 2560), height = Mathf.Clamp(Mathf.RoundToInt(size.y), 256, 2560);
            if (width == Texture.width && height == Texture.height) return;
            // The same object is resized, so every view showing it (the epilogue, the verification) keeps working.
            Texture.Release(); Texture.width = width; Texture.height = height; Texture.Create();
            BoardCamera.ResetAspect();
            noiseTexture.Release(); noiseTexture.width = width; noiseTexture.height = height; noiseTexture.Create();
            noiseCamera.ResetAspect();
        }
        /// <summary>
        /// The noise layer's view, drawn over everything in the battle HUD under <paramref name="parent"/>. Build it after the
        /// HUD it must cover; it follows the board view's place, size, shake and tint every frame.
        /// </summary>
        public void BuildNoiseOverlay(RectTransform parent)
        {
            var rect = new GameObject("Noise Overlay", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            noiseOverlay = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            noiseOverlay.texture = noiseTexture; noiseOverlay.raycastTarget = false;
            var shader = Shader.Find("StringAsASword/UI/NoiseOverlay");
            if (shader != null) { var material = new Material(shader) { name = "Noise Overlay" }; resources.Add(material); noiseOverlay.material = material; }
            SyncNoiseOverlay();
        }
        private void SyncNoiseCamera()
        {
            noiseCamera.orthographic = BoardCamera.orthographic; noiseCamera.orthographicSize = BoardCamera.orthographicSize;
            noiseCamera.fieldOfView = BoardCamera.fieldOfView;
            noiseCamera.nearClipPlane = BoardCamera.nearClipPlane; noiseCamera.farClipPlane = BoardCamera.farClipPlane;
            // The death close-up draws the knight alone.
            noiseCamera.enabled = BoardCamera.enabled && !KnightIsolated;
        }
        private void SyncNoiseOverlay()
        {
            if (noiseOverlay == null) return;
            var board = (RectTransform)transform; var rect = noiseOverlay.rectTransform;
            bool shown = isActiveAndEnabled && boardOutput.enabled && !KnightIsolated;
            if (noiseOverlay.gameObject.activeSelf != shown) noiseOverlay.gameObject.SetActive(shown);
            if (!shown) return;
            // Same parent as the board view in the battle HUD; otherwise match it through world space.
            if (rect.parent == board.parent)
            {
                rect.anchorMin = board.anchorMin; rect.anchorMax = board.anchorMax; rect.pivot = board.pivot;
                rect.anchoredPosition = board.anchoredPosition; rect.sizeDelta = board.sizeDelta;
                rect.localRotation = board.localRotation; rect.localScale = board.localScale;
            }
            else
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = board.pivot;
                rect.position = board.position; rect.rotation = board.rotation;
                rect.sizeDelta = board.rect.size; rect.localScale = Vector3.one;
                var scale = board.lossyScale; var parentScale = rect.parent.lossyScale;
                rect.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, 1);
            }
            noiseOverlay.color = boardOutput.color;
        }
        private void LateUpdate()
        {
            if (noiseCamera == null) return;
            SyncNoiseCamera();
            SyncNoiseOverlay();
        }
        private void OnDisable() { if (noiseOverlay != null) noiseOverlay.gameObject.SetActive(false); }
        public void Render()
        {
            MatchTargetToScreen();
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
            slamAge += dt;
            float jolt = Mathf.Clamp01(1 - shakeAge / .25f) * 7 + Mathf.Clamp01(BreakStage - 2) * 2 + Mathf.Clamp01(1 - slamAge / .18f) * slamPower;
            ((RectTransform)transform).anchoredPosition = viewHome + new Vector2(Mathf.Sin(shakeAge * 95), Mathf.Cos(shakeAge * 71)) * jolt;
            if (FreeRoam) { ShowOutline(false); return; }
            RenderTelegraphs(dt);
            Vector3 target = CellPosition(game.Session.Position);
            if (target != moveTarget)
            {
                moveFrom = player.localPosition; moveTarget = target; moveAge = 0;
                // A step hops; a piece set back up is lifted clear of the board and carried.
                bool lifted = liftNext > 0 && Time.unscaledTime <= liftUntil;
                moveSeconds = lifted ? liftNext : StepSeconds; moveHeight = lifted ? LiftHeight : StepHeight; liftNext = 0;
                if (!lifted) ArtCue.Trigger(player.gameObject, "Move");
            }
            if (game.Noise.Collected > lastCollected) ArtCue.Spawn(art.pickupVfx, world.transform, target);
            lastCollected = game.Noise.Collected;
            moveAge += dt; guardAge += dt;
            float t = Mathf.Clamp01(moveAge / moveSeconds);
            player.localPosition = Vector3.Lerp(moveFrom, moveTarget, Mathf.SmoothStep(0, 1, t)) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * moveHeight);
            selection.localPosition = target;
            RenderSwordSwing();
            attackAge += dt;
            bool guarding = guardAge < .34f && game.Battle.LastClosedNote < guardNote;
            // A guard shows as the knight turning white for a moment (fading back) and bracing; no shield icon pops out.
            if (knightGlow != null) knightGlow.Whiten = guarding ? GuardWhite * (1 - Mathf.Clamp01(guardAge / .34f)) : 0;
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
                if (noiseSprites != null)
                {
                    pickups[i].localPosition = CellPosition(game.Noise.Pickups[i].Cell);
                    RenderNoiseSprite(i, game.Noise.GoalReached);
                    continue;
                }
                pickups[i].localPosition = CellPosition(game.Noise.Pickups[i].Cell) + Vector3.up * (.12f + .025f * Mathf.Sin(Time.unscaledTime * 3 + i));
            }
            lastNoiseDormant = game.Noise.GoalReached;
        }
        private void BuildTelegraphs()
        {
            var dark = Material(new Color(.045f, .055f, .065f));
            var white = Material(new Color(.97f, .97f, .94f));
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
            // No mark at the moment of impact: the summoned chess pieces show the hit (decided 2026-11-28).
        }
        // A mark covers its whole cell, edge to edge, like a tile of the board.
        private const float MarkHalf = .5f;
        // The delivered board model's playing surface sits at y ≈ -0.033 (measured on ChessBoard.fbx through its Fit
        // transform), not at 0 like the fallback tiles. Marks drawn at their fallback height would float ~0.055 above
        // it, and the tilted camera would show them shifted off their cell. They are lowered onto the stone instead,
        // the lowest layer (underlay, y .015) just clear of its highest points (about -0.032).
        private const float ModelMarkDrop = -.044f;
        private float MarkDrop => art.boardPrefab != null ? ModelMarkDrop : 0;
        /// <summary>The highest render queue among the attack marks; what is drawn on a mark goes right after it.</summary>
        public int MarkQueue
        {
            get
            {
                int queue = 2000;
                foreach (var kind in warnings) foreach (var mark in kind)
                    if (mark != null) foreach (var material in mark.GetComponent<MeshRenderer>().sharedMaterials)
                        if (material != null) queue = Mathf.Max(queue, material.renderQueue);
                return queue;
            }
        }
        // Just above the marks (y .024), still well under the knight's base.
        private const float CountdownHeight = .03f;
        /// <summary>
        /// The countdown number for one cell, lying flat on its attack mark in the board world, so whatever stands in front
        /// of the cell (the knight, a summoned piece) covers it. It is sized and placed per cell so that on screen it reads
        /// like a flat label of <paramref name="fontSize"/> board-view units: centred halfway between the cell's projected
        /// near and far edges, less <paramref name="drop"/>, and stretched along the board's depth to undo the tilt.
        /// </summary>
        public TMPro.TMP_Text BuildCountdownLabel(int index, TMPro.TMP_FontAsset font, Material material, float fontSize, float drop, Vector2 viewSize)
        {
            int x = index % 8, y = index / 8;
            var label = new GameObject($"Countdown_{x}_{y}").AddComponent<TMPro.TextMeshPro>();
            label.transform.SetParent(world.transform, false);
            label.font = font; label.fontSharedMaterial = material;
            label.fontSize = fontSize; label.color = Color.black; label.fontStyle = TMPro.FontStyles.Bold;
            label.alignment = TMPro.TextAlignmentOptions.MidlineGeoAligned; label.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            label.rectTransform.sizeDelta = new Vector2(10, 5);
            var square = CellRect(x, y);
            float height = MarkDrop + CountdownHeight, cx = square.center.x;
            Vector2 At(float px, float pz) => ProjectToBoard(new Vector3(px, height, pz), viewSize);
            float nearY = At(cx, square.yMin).y, farY = At(cx, square.yMax).y, target = (nearY + farY) * .5f - drop;
            // The depth on the board that projects to the target (screen height rises with depth).
            float low = square.yMin, high = square.yMax;
            for (int step = 0; step < 24; step++) { float mid = (low + high) * .5f; if (At(cx, mid).y < target) low = mid; else high = mid; }
            float z = (low + high) * .5f;
            // Board-view units per board unit at that spot, across and in depth.
            float across = At(cx + .5f, z).x - At(cx - .5f, z).x;
            float deep = (At(cx, z + .05f).y - At(cx, z - .05f).y) / .1f;
            label.transform.localPosition = new Vector3(cx, height, z);
            // Face up, text's up pointing away from the camera. A 3D TextMeshPro draws fontSize at 0.1 units per point,
            // a board-view label at 1: scaling by 10 / units-per-board-unit gives the same size on screen.
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.transform.localScale = new Vector3(10 / Mathf.Max(1, across), 10 / Mathf.Max(1, deep), 1);
            label.gameObject.SetActive(false);
            return label;
        }
        private Mesh MarkQuad()
        {
            const float h = MarkHalf;
            var mesh = new Mesh { name = "Telegraph Mark",
                vertices = new[] { new Vector3(-h,.024f,-h), new Vector3(-h,.024f,h), new Vector3(h,.024f,h), new Vector3(h,.024f,-h) },
                uv = new[] { new Vector2(0,0), new Vector2(0,1), new Vector2(1,1), new Vector2(1,0) }, triangles = new[] { 0,1,2,0,2,3 } };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh); return mesh;
        }
        private GameObject TelegraphCell(string name, int index, Mesh mesh, Material[] materials)
        {
            var item = new GameObject($"{name}_{index % 8}_{index / 8}", typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(world.transform, false);
            // Stretched over the cell's own painted square, so every mark meets the square's edges.
            var square = CellRect(index % 8, index / 8);
            item.transform.localPosition = new Vector3(square.center.x, MarkDrop, square.center.y);
            item.transform.localScale = new Vector3(square.width, 1, square.height);
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
            RenderSummons(battle);
            bool published = false;
            foreach (var telegraph in battle.Patterns.Active)
                if (telegraph.Id > lastTelegraphId) { lastTelegraphId = telegraph.Id; published = true; }
            if (published) ArtCue.Trigger(king, "Telegraph");
            if (game.State != PrototypeState.Paused) impactAge += dt;
            TelegraphHud.MapCells(battle.Patterns.Active, battle.LastClosedNote, cellCounts, cellSoonest, cellResponses);
            // A cell shows its mark whenever an attack is still coming at it, even in the moment another one just landed
            // there. (Cells used to blank for 0.3 s after an impact, from when an impact cross was drawn; with hits on the
            // same cell less than ~0.3 s apart, the next attack's mark then never showed while its number did.)
            for (int i = 0; i < 64; i++)
            {
                bool warning = cellCounts[i] > 0;
                int tile = cellResponses[i] == AttackResponse.DodgeOnly ? StrongTile : NormalTile;
                for (int kind = 0; kind < warnings.Length; kind++)
                    warnings[kind][i].SetActive(warning && kind == tile);
            }
            // The knight's pedestal covers its own cell's mark, so a move-away attack on that cell shows as an orange outline.
            // Any other time the cell has no outline (the plain mint one is gone).
            var position = game.Session.Position;
            int cell = position.Y * 8 + position.X;
            bool dodge = cellCounts[cell] > 0 && cellResponses[cell] == AttackResponse.DodgeOnly;
            Color outline = dodge ? TelegraphHud.ResponseColor(AttackResponse.DodgeOnly, outlineAccent) : outlineAccent;
            if (outlineColor != outline)
            {
                outlineMaterial.color = outlineColor = outline;
                if (outlineMaterial.HasProperty("_BaseColor")) outlineMaterial.SetColor("_BaseColor", outline);
            }
            ShowOutline(dodge);
        }
        /// <summary>Turns the cell outline's edges on or off. The prologue's SetActive on <see cref="CellOutline"/> still applies on top.</summary>
        private void ShowOutline(bool show)
        {
            if (outlineShown == show) return;
            outlineShown = show;
            foreach (var edge in outlineEdges) if (edge != null) edge.enabled = show;
        }
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
            // Whole cell: underlay to the cell's edge, frame just inside it, stripes inside the frame.
            const float edge = MarkHalf, frame = MarkHalf - .012f, inner = MarkHalf - .024f;
            void Stripe(float intercept, float slope, float width)
            {
                float low = Mathf.Max(-inner, slope > 0 ? -inner - intercept : intercept - inner);
                float high = Mathf.Min(inner, slope > 0 ? inner - intercept : intercept + inner);
                if (high <= low) return;
                int start = vertices.Count;
                vertices.Add(new Vector3(low, .023f, slope * low + intercept));
                vertices.Add(new Vector3(low, .023f, Mathf.Min(inner, slope * low + intercept + width)));
                vertices.Add(new Vector3(high, .023f, Mathf.Min(inner, slope * high + intercept + width)));
                vertices.Add(new Vector3(high, .023f, slope * high + intercept));
                markings.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            Quad(-edge, -edge, edge, edge, .015f, underlay);
            foreach (float side in new[] { -frame, frame })
            {
                Quad(-edge, side - .012f, edge, side + .012f, .024f, markings);
                Quad(side - .012f, -edge, side + .012f, edge, .024f, markings);
            }
            if (impact) { Stripe(-.035f, 1, .07f); Stripe(-.035f, -1, .07f); }
            else if (striped) for (int stripe = -3; stripe <= 3; stripe++) Stripe(stripe * .24f, 1, .035f);
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
                var sides = Vector2.zero;
                foreach (var corner in outlines[i])
                {
                    if (Mathf.Abs(corner.x) > FragmentHalf - .001f) sides.x = Mathf.Sign(corner.x);
                    if (Mathf.Abs(corner.y) > FragmentHalf - .001f) sides.y = Mathf.Sign(corner.y);
                }
                if (sides != Vector2.zero) { borderPieces.Add(fragments[i]); borderSides[fragments[i]] = sides; }
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
            cellPieces.AddRange(fragments); fragmentCount = fragments.Length;
            boardShards.SetActive(false);
            return true;
        }
        /// <summary>Half the side of the middle square that is cut into fragments (board units).</summary>
        public const float FragmentHalf = 3;
        private const float PlateDepth = .2f;
        // The fragments are the last pieces in cellPieces (none without a cut board).
        private int fragmentCount;
        /// <summary>Whether a fragment (by its index in FragmentOutlines) settles at the second break. Shared with the crack map baker.</summary>
        public static bool FragmentSettles(int fragment) => new System.Random(7919 * (fragment + 1)).NextDouble() < .2;
        private static bool Central(int index) => index >= 1 && index <= 6;
        /// <summary>
        /// Convex outlines that tile the middle 6x6 cells: small near the centre, larger toward its edge.
        /// Each is the square cut down by the bisectors between one seed point and every other.
        /// The crack map baker draws its cracks along these, so the seams of sinking fragments lie in a crack.
        /// </summary>
        public static List<List<Vector2>> FragmentOutlines()
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
        private GameObject PiecePrefab(ChessPiece piece)
        {
            switch (piece)
            {
                case ChessPiece.Pawn: return art.bossPawn;
                case ChessPiece.Rook: return art.bossRook;
                case ChessPiece.Bishop: return art.bossBishop;
                case ChessPiece.Knight: return art.bossKnight;
                case ChessPiece.King: return art.bossKing;
                default: return art.bossQueen;
            }
        }
        private Transform TakePiece(ChessPiece piece)
        {
            if (piecePool.TryGetValue(piece, out var pool) && pool.Count > 0) { var pooled = pool.Pop(); pooled.gameObject.SetActive(true); return pooled; }
            var root = new GameObject("Boss " + piece).transform; root.SetParent(world.transform, false);
            var model = new GameObject("Model").transform; model.SetParent(root, false);
            var prefab = PiecePrefab(piece);
            if (prefab != null) { Instantiate(prefab, model, false); model.localScale = Vector3.one * art.bossPieceScale; }
            else
            {
                // No model delivered: the flat silhouette, tilted to face the camera.
                if (pieceFace == null) { pieceFace = Material(new Color(.78f, .79f, .84f)); pieceSide = Material(new Color(.10f, .11f, .13f)); }
                var body = new GameObject("Silhouette").transform; body.SetParent(model, false);
                body.localRotation = Quaternion.Euler(Elevation, 0, 0); body.localScale = Vector3.one * 1.5f;
                foreach (var outline in PieceOutlines(piece)) Extrude(body, piece.ToString(), pieceFace, pieceSide, outline);
            }
            return root;
        }
        private void ReleasePiece(StrikeActor actor)
        {
            if (actor.Shadow != null) { actor.Shadow.gameObject.SetActive(false); shadowPool.Push(actor.Shadow); actor.Shadow = null; }
            actor.Body.gameObject.SetActive(false);
            if (!piecePool.TryGetValue(actor.Piece, out var pool)) piecePool.Add(actor.Piece, pool = new Stack<Transform>());
            pool.Push(actor.Body);
        }
        private static Vector3 BoardLocal(BoardPoint p) => new Vector3(p.X - 3.5f, 0, p.Y - 3.5f);
        private static float SweepSeconds(PieceStrike strike) => Mathf.Clamp(.07f * (strike.Path.Count - 1), .35f, .7f);
        /// <summary>From the impact until the piece stops: a slam lands on the impact, a sweep starts on it.</summary>
        private static float StrikeSeconds(PieceStrike strike) => strike.Motion == StrikeMotion.Slam ? 0 : SweepSeconds(strike);
        /// <summary>A point along a sweep path, by share of its length (0..1), and the travel direction there.</summary>
        private static (Vector3 at, Vector3 heading) AlongPath(IReadOnlyList<BoardPoint> path, float share)
        {
            float total = 0;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(BoardLocal(path[i - 1]), BoardLocal(path[i]));
            float left = share * total;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = BoardLocal(path[i - 1]), b = BoardLocal(path[i]);
                float length = Vector3.Distance(a, b);
                if (left <= length || i == path.Count - 1) return (Vector3.Lerp(a, b, length > 0 ? Mathf.Clamp01(left / length) : 1), b - a);
                left -= length;
            }
            return (BoardLocal(path[0]), Vector3.back);
        }
        private void RenderSummons(BattleSession battle)
        {
            // Driven by the song clock, so the pieces keep time with the music (and stop when it is paused).
            if (game.Timeline == null) return;
            double now = game.SongTime;
            // Summon a hit's pieces only just before it lands.
            foreach (var telegraph in battle.Patterns.Active)
            {
                if (telegraph.Strikes.Count == 0 || summonedTelegraphs.Contains(telegraph.Id)) continue;
                double impact = game.Timeline.TimeOf(telegraph.ImpactNote);
                if (now < impact - LeadSeconds) continue;
                summonedTelegraphs.Add(telegraph.Id);
                foreach (var strike in telegraph.Strikes)
                    strikeActors.Add(new StrikeActor { Body = TakePiece(strike.Piece), Piece = strike.Piece, Strike = strike, TelegraphId = telegraph.Id, Impact = impact,
                        Shadow = strike.Motion == StrikeMotion.Slam ? TakeShadow() : null });
            }
            for (int i = strikeActors.Count - 1; i >= 0; i--)
            {
                var actor = strikeActors[i];
                float t = (float)(now - actor.Impact);
                // Cancelled before landing (the boss stunned, a phase change), or done.
                bool cancelled = t < -.05f && !IsActive(battle, actor.TelegraphId);
                if (cancelled || t > StrikeSeconds(actor.Strike) + HoldSeconds + SinkSeconds) { ReleasePiece(actor); strikeActors.RemoveAt(i); continue; }
                PoseActor(actor, t);
            }
            summonedTelegraphs.RemoveWhere(id => !IsActive(battle, id) && !strikeActors.Exists(a => a.TelegraphId == id));
        }
        private static bool IsActive(BattleSession battle, int id)
        {
            foreach (var telegraph in battle.Patterns.Active) if (telegraph.Id == id) return true;
            return false;
        }
        /// <summary>
        /// Rigid moves only (a chess piece does not stretch). <paramref name="t"/> is song seconds from the impact.
        /// Sweep: rises out of the board on its first cell, runs the path from the impact, holds, sinks back in.
        /// Slam: comes down from above to hover over its point, drops to land on the impact, holds, sinks into the board.
        /// </summary>
        // Easing: out-back overshoots the end a little and settles, out-cubic starts fast and eases in, in-quart starts slow and slams.
        private static float OutBack(float x, float s = 1.7f) { x -= 1; return 1 + (s + 1) * x * x * x + s * x * x; }
        private static float OutCubic(float x) { x = 1 - x; return 1 - x * x * x; }
        /// <summary>A rigid wobble after a hit: degrees, fading out over a few tenths of a second.</summary>
        private static float Wobble(float t, float degrees) => t < 0 ? 0 : degrees * Mathf.Sin(t * 38) * Mathf.Exp(-t * 11);
        private void PoseActor(StrikeActor actor, float t)
        {
            var strike = actor.Strike;
            Vector3 ground = BoardLocal(strike.Path[0]), position, heading = Vector3.back;
            float enter = Mathf.Clamp01((t + LeadSeconds) / EnterSeconds), lean = 0, tilt = 0;
            if (strike.Motion == StrikeMotion.Sweep)
            {
                // Pops up out of the board, a touch too far, and settles.
                float rise = -SinkDepth * (1 - OutBack(enter, 1.2f));
                var (start, firstDir) = AlongPath(strike.Path, 0);
                var forward = firstDir.normalized;
                if (t < 0)
                {
                    // Wind-up: draws back and leans back, ready to spring.
                    float wind = OutCubic(Mathf.Clamp01((t + WindUpSeconds) / WindUpSeconds));
                    position = start - forward * PullBack * wind + Vector3.up * rise; heading = firstDir; lean = -9 * wind;
                }
                else
                {
                    // Bursts off fast and eases to the end, leaning into the run, then runs a little past and comes back.
                    float run = Mathf.Clamp01(t / SweepSeconds(strike));
                    var (at, dir) = AlongPath(strike.Path, OutCubic(run));
                    float past = Mathf.Clamp01((t - SweepSeconds(strike)) / .16f);
                    position = at + dir.normalized * (Overrun * Mathf.Sin(past * Mathf.PI)); heading = dir;
                    lean = 14 * (1 - run) - 6 * Mathf.Sin(past * Mathf.PI);
                }
            }
            else
            {
                float height;
                if (t < -DropSeconds)
                {
                    // Comes down to hover, dips a little below and settles; then winds up, rising before the strike.
                    float arrive = HoverHeight + EnterDrop * (1 - OutBack(enter));
                    float wind = OutCubic(Mathf.Clamp01((t + WindUpSeconds) / (WindUpSeconds - DropSeconds)));
                    height = arrive + WindUpHeight * wind;
                }
                else if (t < 0)
                {
                    // The strike: from the top of the wind-up, gathering speed all the way down.
                    float drop = (t + DropSeconds) / DropSeconds;
                    height = (HoverHeight + WindUpHeight) * (1 - drop * drop * drop * drop);
                }
                else height = Bounce * Mathf.Sin(Mathf.Clamp01(t / .14f) * Mathf.PI); // a short jolt off the board
                position = ground + Vector3.up * height;
                tilt = Wobble(t, 6);
                // The shadow sits on the cell while the piece is up: fainter and wider the higher it is, gone once it lands.
                float lift = Mathf.Clamp01(height / (HoverHeight + EnterDrop));
                actor.Shadow.gameObject.SetActive(t < 0);
                actor.Shadow.transform.localPosition = ground + Vector3.up * .012f;
                actor.Shadow.transform.localScale = Vector3.one * Mathf.Lerp(.7f, .95f, lift) / ShadowSpriteSize;
                actor.Shadow.color = new Color(0, 0, 0, Mathf.Lerp(.75f, .35f, lift) * Mathf.Clamp01(enter * 2));
            }
            // The board view jolts once when the piece hits: hardest for a queen.
            if (t >= 0 && !actor.Struck)
            {
                actor.Struck = true;
                float power = strike.Motion == StrikeMotion.Sweep ? 2 : actor.Piece == ChessPiece.Queen ? 7 : actor.Piece == ChessPiece.King ? 5 : 3;
                if (slamAge > .05f || power > slamPower) { slamPower = power; slamAge = 0; }
            }
            float sink = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - StrikeSeconds(strike) - HoldSeconds) / SinkSeconds));
            actor.Body.localPosition = position + Vector3.down * sink * SinkDepth;
            actor.Body.localScale = Vector3.one;
            // Knights slam side-on so their profile reads; sweeping pieces face where they go; the rest face the camera.
            // Leaning and wobbling turn the piece about its base, so it never changes shape.
            Quaternion facing;
            if (actor.Piece == ChessPiece.Knight && strike.Motion == StrikeMotion.Slam) facing = Quaternion.Euler(0, 90, 0);
            else if (strike.Motion == StrikeMotion.Sweep && new Vector2(heading.x, heading.z).sqrMagnitude > 1e-6f) facing = Quaternion.LookRotation(new Vector3(heading.x, 0, heading.z));
            else facing = Quaternion.identity;
            actor.Body.localRotation = facing * Quaternion.Euler(lean, 0, tilt);
        }
        private void HideSummons()
        {
            foreach (var actor in strikeActors) ReleasePiece(actor);
            strikeActors.Clear(); summonedTelegraphs.Clear();
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
                else if (art.noiseSprite != null)
                {
                    item = BuildNoiseSprite(i);
                    pickups[i] = item;
                    // Tinted per frame instead of swapping materials, so the sprite keeps its sprite material.
                    pickupRenderers[i] = new Renderer[0]; pickupActive[i] = pickupDormant[i] = new Material[0][];
                    item.gameObject.SetActive(false);
                    continue;
                }
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
                SetLayer(item, NoiseLayer);
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
        private static void SetLayer(Transform root, int layer)
        {
            foreach (var part in root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
        }
        private Material BoardSpriteMaterial()
        {
            if (art.noiseMaterial != null) return art.noiseMaterial;
            if (spriteMaterial == null)
            {
                spriteMaterial = new Material(Shader.Find(NoiseShader)) { name = "Board Sprite" };
                resources.Add(spriteMaterial);
            }
            return spriteMaterial;
        }

        // The picture hovers above the cell while its shadow stays on the board.
        private Transform BuildNoiseSprite(int index)
        {
            if (noiseSprites == null)
            {
                noiseSprites = new SpriteRenderer[pickups.Length]; noiseShadows = new SpriteRenderer[pickups.Length]; noiseFloats = new Transform[pickups.Length];
                noiseBackings = new SpriteRenderer[pickups.Length]; noiseGlows = new SpriteRenderer[pickups.Length];
                spriteMaterial = BoardSpriteMaterial();
                noiseGlowSprite = GlowSprite(art.noiseSprite);
            }
            var item = new GameObject($"Noise_{index}").transform; item.SetParent(world.transform, false);
            var shadow = new GameObject("Noise Shadow", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            shadow.transform.SetParent(item, false);
            shadow.transform.localPosition = Vector3.up * .012f; shadow.transform.localRotation = Quaternion.Euler(90, 0, 0);
            shadow.sprite = ShadowSprite(); shadow.sharedMaterial = spriteMaterial; shadow.color = new Color(0, 0, 0, 0);
            var hover = new GameObject("Noise Float").transform; hover.SetParent(item, false);
            var picture = new GameObject("Noise Sprite", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            picture.transform.SetParent(hover, false);
            picture.sprite = art.noiseSprite; picture.sharedMaterial = spriteMaterial; picture.sortingOrder = 1;
            // Within one cell, dark backing included (1.1x the picture), whatever the picture's pixels-per-unit.
            float scale = NoiseWidth / Mathf.Max(.0001f, art.noiseSprite.bounds.size.x);
            picture.transform.localScale = Vector3.one * scale;
            // The picture is white and the tiles are light: a dark, slightly larger copy right behind it gives the line
            // an outline and a dark cloud to sit on, so it reads on any tile, not only over a darker attack mark.
            var backing = new GameObject("Noise Backing", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            backing.transform.SetParent(hover, false);
            backing.transform.localPosition = Vector3.forward * .02f; backing.transform.localScale = Vector3.one * scale * 1.1f;
            backing.sprite = art.noiseSprite; backing.sharedMaterial = spriteMaterial; backing.sortingOrder = 0;
            // A soft white outer glow that follows the picture's shape, behind the dark backing.
            if (noiseGlowSprite != null)
            {
                var glow = new GameObject("Noise Glow", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
                glow.transform.SetParent(hover, false);
                glow.transform.localPosition = Vector3.forward * .04f; glow.transform.localScale = Vector3.one * scale;
                glow.sprite = noiseGlowSprite; glow.sharedMaterial = spriteMaterial; glow.sortingOrder = -1;
                noiseGlows[index] = glow;
            }
            noiseSprites[index] = picture; noiseShadows[index] = shadow; noiseFloats[index] = hover; noiseBackings[index] = backing;
            // The floating picture draws over everything; its shadow stays a board decal, under the marks and numbers.
            SetLayer(hover, NoiseLayer);
            return item;
        }
        // The picture is about .41 cells tall (pivot at its centre): at .26 it floats just clear of its own cell, low
        // enough that it reads as on that cell rather than on the one behind it. The bob stays clear of the tile too.
        private const float NoiseWidth = .85f, NoiseHover = .26f, NoiseBob = .04f;
        public const string NoiseShader = "Universal Render Pipeline/2D/Sprite-Unlit-Default";
        /// <summary>Height above its cell at which a noise pickup is drawn (where collected noise starts its flight).</summary>
        public float NoiseHeight => noiseSprites != null ? NoiseHover : .12f;
        public bool NoiseUsesSprite => noiseSprites != null;
        private void RenderNoiseSprite(int index, bool dormant)
        {
            float time = Time.unscaledTime, wave = Mathf.Sin(time * 2.2f + index * 1.7f);
            float height = NoiseHover + NoiseBob * wave;
            var hover = noiseFloats[index];
            hover.localPosition = Vector3.up * height;
            // Face the board camera, with a slow sway so it never sits still like a decal.
            hover.rotation = BoardCamera.transform.rotation * Quaternion.Euler(0, 0, 3 * Mathf.Sin(time * 1.3f + index));
            // Higher = wider, fainter shadow; lower = tighter, darker.
            float lift = Mathf.InverseLerp(NoiseHover - NoiseBob, NoiseHover + NoiseBob, height);
            // Most of the cell, so it reads on dark tiles as well as light ones.
            noiseShadows[index].transform.localScale = new Vector3(.9f, .62f, 1) * Mathf.Lerp(.9f, 1.05f, lift) / ShadowSpriteSize;
            noiseShadows[index].color = new Color(0, 0, 0, Mathf.Lerp(.85f, .65f, lift) * (dormant ? .55f : 1));
            noiseSprites[index].color = dormant ? new Color(.42f, .42f, .42f, .7f) : Color.white;
            noiseBackings[index].color = new Color(.03f, .03f, .035f, dormant ? .45f : .85f);
            if (noiseGlows[index] != null) noiseGlows[index].color = new Color(1, 1, 1, dormant ? NoiseGlowDormant : NoiseGlowAlpha);
        }
        private SpriteRenderer[] noiseSprites, noiseShadows, noiseBackings, noiseGlows;
        private Sprite noiseGlowSprite;
        // Outer glow: spread as a share of the picture's width, and its strength (dimmer while the noise is dormant).
        private const float NoiseGlowSpread = .14f, NoiseGlowAlpha = .9f, NoiseGlowDormant = .25f;
        /// <summary>
        /// A white glow in the shape of <paramref name="source"/>: its alpha, widened and blurred, on a canvas padded so the
        /// glow is not cut off. Same pixels-per-unit and pivot as the source, so it lines up at the same scale. The texture is
        /// read through a render texture, so it works even when the source is not marked readable.
        /// </summary>
        private Sprite GlowSprite(Sprite source)
        {
            if (source == null) return null;
            var texture = source.texture; var rect = source.textureRect;
            int w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
            if (w <= 0 || h <= 0) return null;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            var read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0); read.Apply();
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
            var source32 = read.GetPixels32(); Destroy(read);

            int radius = Mathf.Max(2, Mathf.RoundToInt(w * NoiseGlowSpread)), pad = radius * 2;
            int W = w + 2 * pad, H = h + 2 * pad;
            var alpha = new float[W * H];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) alpha[(y + pad) * W + x + pad] = source32[y * w + x].a / 255f;
            // Three box blurs each way approximate a gaussian; the result is lifted so the glow is bright near the edge.
            var scratch = new float[W * H];
            for (int pass = 0; pass < 3; pass++) { BoxBlur(alpha, scratch, W, H, radius / 2 + 1, true); BoxBlur(scratch, alpha, W, H, radius / 2 + 1, false); }
            var pixels = new Color32[W * H];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(alpha[i] * 2.2f)));
            var glow = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Noise Glow", wrapMode = TextureWrapMode.Clamp };
            glow.SetPixels32(pixels); glow.Apply(); resources.Add(glow);
            var pivot = (source.pivot - source.textureRectOffset + new Vector2(pad, pad)) / new Vector2(W, H);
            var sprite = Sprite.Create(glow, new Rect(0, 0, W, H), pivot, source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "Noise Glow"; resources.Add(sprite);
            return sprite;
        }
        private static void BoxBlur(float[] from, float[] to, int width, int height, int radius, bool horizontal)
        {
            int lines = horizontal ? height : width, length = horizontal ? width : height;
            float scale = 1f / (2 * radius + 1);
            for (int line = 0; line < lines; line++)
            {
                int start = horizontal ? line * width : line, step = horizontal ? 1 : width;
                float sum = 0;
                for (int k = -radius; k <= radius; k++) sum += from[start + Mathf.Clamp(k, 0, length - 1) * step];
                for (int i = 0; i < length; i++)
                {
                    to[start + i * step] = sum * scale;
                    sum += from[start + Mathf.Min(i + radius + 1, length - 1) * step] - from[start + Mathf.Max(i - radius, 0) * step];
                }
            }
        }
        private Transform[] noiseFloats;
        private Material spriteMaterial;
        private static Sprite shadowSprite;
        private const float ShadowSpriteSize = 1;
        // A soft round shadow: solid in the middle, fading to nothing over the outer part. One unit across.
        private static Sprite ShadowSprite()
        {
            if (shadowSprite != null) return shadowSprite;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                    float fade = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.35f, 1, Mathf.Sqrt(dx * dx + dy * dy)));
                    texture.SetPixel(x, y, new Color(1, 1, 1, fade));
                }
            texture.Apply();
            shadowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), size);
            shadowSprite.hideFlags = HideFlags.HideAndDontSave;
            return shadowSprite;
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
            knightGlow?.Dispose();
            if (BoardCamera != null) BoardCamera.targetTexture = null;
            if (noiseCamera != null) noiseCamera.targetTexture = null;
            if (Texture != null) Texture.Release();
            if (noiseTexture != null) noiseTexture.Release();
            if (noiseOverlay != null) Destroy(noiseOverlay.gameObject);
            if (world != null) Destroy(world);
            foreach (var resource in resources) if (resource != null) Destroy(resource);
        }
    }
}
