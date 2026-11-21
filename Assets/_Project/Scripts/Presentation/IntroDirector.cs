using System.Collections.Generic;
using StringAsASword.Combat;
using StringAsASword.Core;
using TMPro;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The prologue (decisions D1-C, D3-B, D4-A, S4-A): the king directs the board one move per beat, the knight refuses
    /// the order and leaps, six strings pin it down, the player tears the lowest one with J, the other five become the
    /// lanes, a strike without noise passes through the king, the king's wave pushes the knight home, and one noise lands
    /// where the battle's first noise will spawn. Its last downbeat is the song's start.
    ///
    /// The picture is a pure function of the story beat (<see cref="IntroTimeline"/>). An intro camera renders the board
    /// world full screen; on the handover its pose and projection equal the battle's (board camera through the board
    /// view's rect), and the boss and crown stand in the world exactly where the battle draws them on the canvas, so the
    /// last frame is the battle screen. Rules stay in BattleSession; this only reads them.
    /// </summary>
    public sealed class IntroDirector : MonoBehaviour
    {
        // Story beats of the shots.
        private const float Plies = 6, Order1 = 8, Order2 = 10, Threat = 12, Leap = 13.75f, Land = 15.5f, Bind = 20, Pinned = 21.2f;
        private const float Struggle = 26, Grip = 30, TearStory = 36, Lanes = 38.3f, Cut = 42, Swing = 42.6f, Wave = 46, Push = 46.2f,
            Fall = 50.2f, Settle = 50, Handover = 54, End = 56;
        private const float BossDepth = 7f, PieceScale = .75f;
        private static readonly string[] Melody = { "king_D4", "king_F4", "king_A4", "king_G4", "king_F4", "king_D4" };
        private static readonly (string from, string to)[] PlyMoves = { ("e7", "e5"), ("e2", "e4"), ("d7", "d6"), ("g2", "g3"), ("c8", "g4"), ("d2", "d3") };
        // Legal squares of the piece making each ply, on the board as it stands then.
        private static readonly string[][] PlyCandidates = {
            new[] { "e6", "e5" }, new[] { "e3", "e4" }, new[] { "d6", "d5" }, new[] { "g3", "g4" },
            new[] { "d7", "e6", "f5", "g4", "h3" }, new[] { "d3", "d4" } };
        private static readonly string[] KnightL = { "e2", "f3", "h3" }, KnightCross = { "f1", "g2", "h1" };

        private PrototypeGame game;
        private PrototypeView view;
        private BoardWorldPresenter board;
        private PrologueDefinition data;
        private Transform world, root;
        private Camera cam;
        private RenderTexture target;
        private Light rimLight;
        private RectTransform layer;
        private UnityEngine.UI.RawImage introView;
        private UnityEngine.UI.Image voidCover, flash, barTop, barBottom;
        private TMP_Text skipHint;
        private KeyPromptCard prompt;
        private CanvasGroup contentGroup, bossGroup;
        private Material flatTemplate;
        private Texture2D whiteMarble, blackMarble;
        private readonly List<Object> resources = new List<Object>();

        private double spb, origin, tearIntroBeat = -1;
        private float seek = -1, story;
        private bool playing, built;
        private Vector3 battlePos; private Quaternion battleRot;
        private Vector3 noiseCell = BoardWorldPresenter.CellPosition(new Board.GridPosition(6, 2));

        public bool Playing => playing;
        /// <summary>The story beat shown now (see <see cref="IntroTimeline"/>).</summary>
        public float StoryBeat => story;
        public bool Torn => tearIntroBeat >= 0;
        /// <summary>DSP time of the handover once the string is torn, else -1.</summary>
        public double HandoverDsp => Torn ? origin + (tearIntroBeat + IntroTimeline.AfterTear) * spb : -1;
        /// <summary>Board cell (local position) the surviving noise lands on: where the battle's first noise spawns.</summary>
        public Vector3 NoiseLanding => noiseCell;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView, PrologueDefinition definition)
        {
            game = owner; view = prototypeView; board = view.BoardWorld; data = definition;
            spb = game.Timeline.SecondsPerBeat;
        }
        // Built on first use so a battle that never shows the prologue pays nothing for it.
        private void Build()
        {
            if (built) return;
            built = true;
            world = board.WorldRoot;
            root = new GameObject("Prologue").transform; root.SetParent(world, false);
            battlePos = board.BoardCamera.transform.localPosition; battleRot = board.BoardCamera.transform.localRotation;
            cam = new GameObject("Prologue Camera", typeof(Camera)).GetComponent<Camera>();
            cam.transform.SetParent(world, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = .1f; cam.farClipPlane = 80; cam.allowHDR = false; cam.enabled = false;
            rimLight = new GameObject("Prologue Rim Light", typeof(Light)).GetComponent<Light>();
            rimLight.transform.SetParent(world, false);
            rimLight.type = LightType.Directional; rimLight.color = Color.white;
            rimLight.transform.localRotation = Quaternion.Euler(28, 180, 0); rimLight.enabled = false;
            // The noise sprite's material: unlit, alpha-blended and tinted by colour, which every flat prologue shape needs.
            flatTemplate = view.Art.noiseMaterial != null ? view.Art.noiseMaterial
                : new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            whiteMarble = Marble(new Color(.86f, .85f, .82f), new Color(.55f, .55f, .56f), 7);
            blackMarble = Marble(new Color(.075f, .075f, .085f), new Color(.30f, .30f, .32f), 11);
            BuildLayer(); BuildPieces(); BuildMarks(); BuildStrings(); BuildBossAndCrown(); BuildNoise(); BuildEffects();
            BuildAudio();
            PredictNoise();
        }

        // ---------------------------------------------------------------- control
        public void Play()
        {
            Build();
            playing = true; seek = -1; tearIntroBeat = -1; story = 0;
            origin = AudioSettings.dspTime + .35;
            nextEvent = 0; nextBar = 0; nextAfter = 0;
            root.gameObject.SetActive(true); layer.gameObject.SetActive(true);
            cam.enabled = true; rimLight.enabled = true;
            board.FreeRoam = true;
            Render(0);
        }
        /// <summary>Takes everything down; the battle's own picture is shown again.</summary>
        public void Stop() { StopInternal(keepNoise: false); }
        private void StopInternal(bool keepNoise)
        {
            if (!built) return;
            playing = false; seek = -1;
            foreach (var source in sources) source.Stop();
            layer.gameObject.SetActive(false);
            cam.enabled = false; rimLight.enabled = false;
            for (int i = 0; i < root.childCount; i++) root.GetChild(i).gameObject.SetActive(keepNoise && root.GetChild(i) == landingNoise.parent);
            root.gameObject.SetActive(keepNoise);
            noiseKept = keepNoise; keptAt = AudioSettings.dspTime;
            SetGroups(1);
            board.CellOutline.gameObject.SetActive(true);
            board.PlayerTransform.localRotation = Quaternion.identity;
            board.FreeRoam = false;
        }
        /// <summary>
        /// J pressed (at a DSP time): the string tears on the beat. Without a tutorial after the prologue the song is scheduled
        /// from that beat; with one, the handover beat starts the tutorial instead and the song waits for its end.
        /// </summary>
        public void Tear(double dsp)
        {
            if (!playing || Torn) return;
            double beat = IntroTimeline.TearBeat((dsp - origin) / spb);
            if (beat < 0) return;
            tearIntroBeat = beat;
            // Bars of the waiting loop already queued past the tear are cut off there.
            double tearDsp = origin + beat * spb;
            foreach (var source in loopSources) if (source != null) source.SetScheduledEndTime(tearDsp + .03);
            loopSources.Clear();
            if (!game.TutorialFollowsPrologue) game.ScheduleSongFromPrologue(HandoverDsp, IntroTimeline.HandoverSongTime(game.Timeline));
        }
        /// <summary>Captures and tests: show this story beat (no sound) instead of the running one; a negative value resumes.</summary>
        public void Seek(float storyBeat) { seek = storyBeat; }
        /// <summary>Tests only: move the running intro clock so it reads this intro beat now (cues before it are dropped).</summary>
        internal void JumpForTests(double introBeat)
        {
            origin = AudioSettings.dspTime - introBeat * spb;
            while (nextEvent < before.Count && before[nextEvent].Beat < introBeat) nextEvent++;
            nextBar = (int)System.Math.Max(0, System.Math.Floor((introBeat - IntroTimeline.LoopStart) / IntroTimeline.LoopBeats) + 1);
        }

        private void LateUpdate()
        {
            if (noiseKept && !playing)
            {
                // After the handover the landed noise waits for the battle's own first noise, on the same cell. With the
                // tutorial next it sinks into the board over one beat instead, so nothing sits there that cannot be picked
                // up; the tutorial's noise lesson puts one on the same cell (two above the knight's start) later.
                bool spawned = game.Noise.Pickups.Count > 0 || !noiseWaits && game.State != PrototypeState.Playing;
                float sunk = noiseWaits ? (float)((AudioSettings.dspTime - keptAt) / spb) : 0;
                if (spawned || sunk >= 1) { landingNoise.parent.gameObject.SetActive(false); root.gameObject.SetActive(false); noiseKept = false; }
                else if (noiseWaits) Billboard(landingNoise, noiseCell + Vector3.up * (.5f - .5f * sunk), 1.4f, 1 - Smooth01(sunk));
                else RenderLandingNoise(End);
                return;
            }
            if (!playing) return;
            double now = AudioSettings.dspTime;
            double introBeat = (now - origin) / spb;
            story = seek >= 0 ? seek : (float)IntroTimeline.StoryBeat(introBeat, tearIntroBeat);
            board.FreeRoam = true;
            if (seek < 0) ScheduleAudio(now);
            Render(story);
            if (seek < 0 && Torn && now >= HandoverDsp)
            {
                double handover = HandoverDsp;
                StopInternal(keepNoise: true);
                if (game.TutorialFollowsPrologue) game.PrologueIntoTutorial(handover); else game.FinishPrologue();
            }
        }

        // ---------------------------------------------------------------- the picture
        private void Render(float s)
        {
            EnsureTarget();
            RenderCamera(s);
            RenderPieces(s);
            RenderKnight(s);
            RenderMarks(s);
            RenderStrings(s);
            RenderBoss(s);
            RenderNoise(s);
            RenderEffects(s);
            RenderLayer(s);
        }

        // Camera keys: board-local position, look point, vertical field of view. The king stays within ±25° of front (A6-A).
        private struct Key { public Vector3 Pos, Look; public float Fov; public Key(Vector3 p, Vector3 l, float f) { Pos = p; Look = l; Fov = f; } }
        private Vector3 crownAt, bossMid;
        private void RenderCamera(float s)
        {
            Vector3 g1 = Cell("g1"), knight = board.PlayerTransform.localPosition;
            Key k; float shake = 0;
            if (s < 5) k = Lerp(new Key(new Vector3(0, 12.5f, -6.5f), new Vector3(0, 0, .2f), 42), new Key(new Vector3(0, 9.2f, -9.6f), new Vector3(0, .2f, .6f), 44), Smooth(s, 0, 5));
            else if (s < Order1)
            {
                // Crane up from the board to the king: cloak, mask, crown — the whole crown inside the frame.
                float u = Smooth(s, 5, 7.6f);
                k = Lerp(new Key(new Vector3(0, 9.2f, -9.6f), new Vector3(0, .2f, .6f), 44), new Key(new Vector3(0, 3.2f, -8.6f), Vector3.Lerp(bossMid, crownAt, .45f), 46), u);
            }
            else if (s < Leap)
            {
                // The king's gaze: from over the crown, down at the knight and the order; it narrows after each refusal.
                float fov = Mathf.Lerp(Mathf.Lerp(46, 39, Smooth(s, 10.6f, 11.4f)), 31, Smooth(s, 12.6f, 13.4f));
                k = new Key(new Vector3(1.4f, 5.6f, 1.6f), g1 + new Vector3(0, .5f, -.1f), fov);
            }
            else if (s < Bind) { k = new Key(new Vector3(6.0f, 1.6f, -6.0f), new Vector3(1.8f, 1.0f + knight.y * .6f, -1.8f), 46); shake = .02f; }
            else if (s < Struggle)
            {
                float u = Smooth(s, Pinned, 24.5f);
                k = Lerp(new Key(new Vector3(0, 3.6f, -10.5f), new Vector3(0, 2.8f, 4), 48), new Key(new Vector3(3.8f, 5.0f, -7.4f), g1 + new Vector3(-.1f, .2f, .7f), 38), u);
            }
            else if (s < Grip) { k = new Key(new Vector3(3.2f, 1.6f, -1.3f), g1 + new Vector3(0, .65f, 0), 40); shake = .02f; }
            else if (s < TearStory) k = new Key(new Vector3(3.6f, 1.8f, -1.0f), g1 + new Vector3(0, .6f, 0), 43 - 1.5f * Smooth(s, Grip, TearStory));
            else if (s < Lanes) { k = new Key(new Vector3(3.3f, 1.5f, -1.6f), g1 + new Vector3(0, .8f, 0), 42); shake = .05f * (1 - Smooth(s, TearStory, TearStory + 1.2f)); }
            else if (s < Cut)
            {
                float u = Smooth(s, Lanes, 41.6f);
                k = Lerp(new Key(new Vector3(3.6f, 2.4f, -5.4f), new Vector3(2.6f, .8f, -2.8f), 44), new Key(new Vector3(0, 9.0f, -12.0f), new Vector3(0, .6f, -1.0f), 50), u);
            }
            else if (s < Wave) { k = new Key(new Vector3(4.9f, 2.4f, -1.4f), new Vector3(1.2f, 1.7f, 6.2f), 58); shake = .01f; }
            else if (s < Settle) k = new Key(new Vector3(0, 11.5f, -8.0f), new Vector3(0, 3.0f, 4.6f), 52);
            else k = new Key(new Vector3(0, 11.0f, -9.0f), new Vector3(0, .4f, .6f), 50);
            if (shake > 0) { float t = s * (float)spb; k.Pos += new Vector3(Mathf.Sin(t * 13.1f) + .4f * Mathf.Sin(t * 29.7f), Mathf.Sin(t * 17.3f + 1), 0) * shake; }

            var pos = k.Pos; var rot = Quaternion.LookRotation(k.Look - k.Pos, Vector3.up);
            float aspect = (float)target.width / target.height;
            var projection = Matrix4x4.Perspective(k.Fov, aspect, cam.nearClipPlane, cam.farClipPlane);
            // From 6c the view settles into the battle camera, ending on its exact pose and projection.
            float settle = Smooth(s, Settle, Handover);
            if (settle > 0)
            {
                pos = Vector3.Lerp(pos, battlePos, settle); rot = Quaternion.Slerp(rot, battleRot, settle);
                projection = LerpMatrix(projection, BattleProjection(), settle);
            }
            cam.transform.localPosition = pos; cam.transform.localRotation = rot;
            cam.projectionMatrix = projection;
        }
        /// <summary>The board camera's projection seen through the board view's rect on a full-screen target.</summary>
        private Matrix4x4 BattleProjection()
        {
            var corners = new Vector3[4]; view.BoardViewRect.GetWorldCorners(corners);
            float W = target.width, H = target.height, x0 = corners[0].x, y0 = corners[0].y, w = corners[2].x - x0, h = corners[2].y - y0;
            var map = Matrix4x4.identity;
            map.m00 = w / W; map.m03 = (2 * x0 + w) / W - 1;
            map.m11 = h / H; map.m13 = (2 * y0 + h) / H - 1;
            return map * board.BoardCamera.projectionMatrix;
        }
        private void EnsureTarget()
        {
            int w = Mathf.Max(16, Screen.width), h = Mathf.Max(16, Screen.height);
            if (target != null && target.width == w && target.height == h) return;
            if (target != null) { cam.targetTexture = null; target.Release(); Destroy(target); }
            target = new RenderTexture(w, h, 24) { name = "Prologue View", antiAliasing = 4 };
            target.Create(); cam.targetTexture = target; introView.texture = target;
            PlaceBossAndCrown();
        }

        // ---------------------------------------------------------------- pieces (A2-C, A3-A)
        private sealed class Piece { public Transform Root; public Vector3 Home; public int Ply = -1; public Vector3 To; }
        private readonly List<Piece> pieces = new List<Piece>();
        private void BuildPieces()
        {
            var whiteMat = Lit(whiteMarble); var blackMat = Lit(blackMarble);
            var setup = new List<(string cell, string type)>
            { ("a1","rook"),("c1","bishop"),("d1","queen"),("f1","bishop"),("h1","rook"),("a8","rook"),("c8","bishop"),("d8","queen"),("f8","bishop"),("h8","rook") };
            foreach (char f in "abcdefgh") { setup.Add(($"{f}2", "pawn")); setup.Add(($"{f}7", "pawn")); }
            var parent = new GameObject("Pieces").transform; parent.SetParent(root, false);
            foreach (var (cell, type) in setup)
            {
                bool white = cell[1] >= '7';
                var piece = new Piece { Root = MakePiece(type, white ? whiteMat : blackMat, parent), Home = Cell(cell) };
                for (int k = 0; k < PlyMoves.Length; k++) if (PlyMoves[k].from == cell) { piece.Ply = k; piece.To = Cell(PlyMoves[k].to); }
                piece.Root.name = (white ? "White " : "Black ") + type + " " + cell;
                pieces.Add(piece);
            }
        }
        private void RenderPieces(float s)
        {
            // When the strings become lanes the other pieces sink into the board: it is the battle board now.
            float sink = Smooth(s, Lanes, 41);
            foreach (var piece in pieces)
            {
                Vector3 p = piece.Home;
                if (piece.Ply >= 0)
                {
                    float u = Mathf.Clamp01((s - (piece.Ply + .7f)) / .3f);
                    p = Vector3.Lerp(piece.Home, piece.To, Smooth01(u)); p.y = Mathf.Sin(Mathf.PI * u) * .16f;
                }
                p.y -= 1.2f * sink;
                piece.Root.localPosition = p;
                piece.Root.gameObject.SetActive(sink < 1);
            }
        }

        // ---------------------------------------------------------------- the knight
        private void RenderKnight(float s)
        {
            Vector3 g1 = Cell("g1"), f3 = Cell("f3"), g8 = Cell("g8");
            Vector3 p = g1; float yaw = 0, tilt = 0, squash = 1;
            float faceF3 = Mathf.Atan2(f3.x - g1.x, f3.z - g1.z) * Mathf.Rad2Deg;
            if (s < Leap)
            {
                foreach (float refuse in new[] { 9f, 11f }) { float u = (s - refuse) / .45f; if (u > 0 && u < 1) tilt = Mathf.Sin(u * Mathf.PI * 2) * 2; }
                squash = 1 - .12f * Smooth(s, Threat, Leap);
                yaw = Mathf.Lerp(0, faceF3, Smooth(s, Threat + .5f, Leap));
            }
            else if (s < Bind)
            {
                // Its own move, off the beat (13.75): an L over the f2 pawn, landing on f3 once.
                float u = Mathf.Clamp01((s - Leap) / (Land - Leap));
                p = Vector3.Lerp(g1, f3, Smooth01(u)); p.y = Mathf.Sin(Mathf.PI * u) * 1.5f;
                yaw = faceF3;
                squash = s < Leap + .3f ? 1.12f : 1 - .14f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((s - Land) / .5f));
                if (s > Land) yaw = Mathf.Lerp(faceF3, 0, Smooth(s, Land + 1, Bind));
            }
            else if (s < Pinned)
            {
                // The strings drag it back to g1.
                float u = Mathf.Clamp01((s - Bind - .4f) / (Pinned - Bind - .4f));
                p = Vector3.Lerp(f3, g1, u * u * u); p.y = Mathf.Sin(Mathf.PI * u) * .35f;
            }
            else if (s < TearStory)
            {
                squash = 1 - .2f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((s - Pinned) / .5f));
                foreach (float jerk in Jerks) { float u = (s - jerk) / .55f; if (u > 0 && u < 1) { p.y += Mathf.Sin(Mathf.PI * u) * .3f; tilt = Mathf.Sin(u * Mathf.PI * 3) * 7; } }
                if (s >= Grip) { tilt = -6 * Smooth(s, Grip, Grip + 1) + Mathf.Sin(s * 40) * .8f; squash = .94f; }
            }
            else if (s < Cut)
            {
                float u = Mathf.Clamp01((s - TearStory) / .4f);
                tilt = -6 + 10 * Mathf.Sin(Mathf.PI * u);
            }
            else if (s < Push) { p = g8; }
            else
            {
                float u = Mathf.Clamp01((s - Push) / 1.5f);
                p = Vector3.Lerp(g8, g1, 1 - Mathf.Pow(1 - u, 3)); yaw = Mathf.Sin(u * Mathf.PI) * 40; tilt = Mathf.Sin(u * Mathf.PI) * 9;
                squash = 1 - .15f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((s - Push - 1.5f) / .4f));
            }
            var knight = board.PlayerTransform;
            knight.localPosition = p;
            knight.localRotation = Quaternion.Euler(0, yaw, tilt);
            knight.localScale = new Vector3(1, squash, 1);
        }
        private static readonly float[] Jerks = { 26.5f, 27.5f, 28.5f };

        // ---------------------------------------------------------------- orders, candidate squares, marks
        private Transform[] plyArrow, knightArrow;
        private readonly List<Transform> rings = new List<Transform>();
        private Transform cross, threatTile, beam;
        private void BuildMarks()
        {
            var parent = new GameObject("Marks").transform; parent.SetParent(root, false);
            plyArrow = MakeArrow(parent, "Order"); knightArrow = MakeArrow(parent, "Order To The Knight");
            for (int i = 0; i < 6; i++) rings.Add(MakeRing(parent));
            cross = MakeCrossMark(parent);
            threatTile = MakeFrame(parent, "Threat", .94f, .07f);
            beam = MakeQuad(parent, "Order Beam", Color.white).transform;
        }
        private void RenderMarks(float s)
        {
            int ringUsed = 0;
            // One ply per tick: the arrow is drawn half a beat before, the piece lands on the tick.
            int k = Mathf.FloorToInt(s - .5f);
            if (k >= 0 && k < Plies && s < Order1)
            {
                float a = k + .5f, op = .9f * (1 - Mathf.Clamp01((s - (k + 1)) / .25f));
                SetArrow(plyArrow, Cell(PlyMoves[k].from), Cell(PlyMoves[k].to), .17f, op, Smooth(s, a, a + .2f));
                float rop = Smooth(s, a, a + .15f) * (1 - Mathf.Clamp01((s - (k + 1)) / .25f));
                foreach (var c in PlyCandidates[k]) SetRing(rings[ringUsed++], Cell(c), rop, c == PlyMoves[k].to, false);
            }
            else SetArrow(plyArrow, Vector3.zero, Vector3.forward, .17f, 0, 0);
            // The order to the knight: one square forward, like a pawn — outside its own moves.
            Vector3 g1 = Cell("g1"), g2 = Cell("g2");
            if (s >= Order1 && s < Leap + .5f)
            {
                bool second = s >= Order2;
                float grow = second ? Smooth(s, Order2, Order2 + .25f) : Smooth(s, Order1, Order1 + .25f);
                float op = (second ? .85f + .15f * Mathf.Sin(s * 9) : .8f) * (1 - Mathf.Clamp01((s - Leap) / .5f));
                SetArrow(knightArrow, g1, g2, second ? .22f : .13f, op, grow);
                // The knight's own squares; the refusal is marked with an X on the ordered square.
                float lop = .85f * Smooth(s, Order1, Order1 + .3f) * (1 - Mathf.Clamp01((s - Leap) / .5f));
                bool pick = s >= Leap - 1.5f;
                foreach (var c in KnightL) SetRing(rings[ringUsed++], Cell(c), lop, pick && c == "f3", pick && c == "f3");
            }
            else SetArrow(knightArrow, g1, g2, .13f, 0, 0);
            float xop = s >= 9 && s < Leap + .5f ? Smooth(s, 9, 9.3f) * (1 - Mathf.Clamp01((s - Leap) / .5f)) : 0;
            SetFlat(cross, g2 + Vector3.up * .03f, xop);
            float top = s >= Threat && s < Leap + .3f ? .85f * Smooth(s, Threat, Threat + .3f) * (1 - Mathf.Clamp01((s - Leap) / .3f)) : 0;
            SetFlat(threatTile, g1 + Vector3.up * .018f, top);
            if (s >= Pinned && s < Lanes)
            {
                // Bound: the L squares are pulled in to one square up, down, left or right.
                float pull = Mathf.Pow(Mathf.Clamp01((s - Pinned) / 1.0f), 3), jerk = JerkAmount(s);
                float op = Mathf.Min(1, .9f * Mathf.Lerp(1, .4f, Smooth(s, 23, 25)) + .55f * jerk) * (1 - Smooth(s, TearStory + .5f, Lanes));
                for (int i = 0; i < 3; i++) SetRing(rings[ringUsed++], Vector3.Lerp(Cell(KnightL[i]), Cell(KnightCross[i]), pull), op, false, false, 1 + .18f * jerk);
            }
            for (int i = ringUsed; i < rings.Count; i++) rings[i].gameObject.SetActive(false);
            // A beam from the crown to the knight with the second order.
            float bop = s >= Order2 && s < Threat + .8f ? .5f * Smooth(s, Order2, Order2 + .2f) * (1 - Smooth(s, Threat + .2f, Threat + .8f)) * (.8f + .2f * Mathf.Sin(s * 40)) : 0;
            SetBeam(beam, crownAt, board.PlayerTransform.localPosition + Vector3.up * .7f, .07f, bop);
        }
        private float JerkAmount(float s) { float j = 0; foreach (float t in Jerks) { float u = (s - t) / .55f; if (u > 0 && u < 1) j = Mathf.Max(j, Mathf.Sin(Mathf.PI * u)); } return j; }

        // ---------------------------------------------------------------- strings and lanes
        private LineRenderer[] strings, lanes;
        private LineRenderer blade;
        private Transform sword;
        private Vector3[] kingBase, anchors;
        private const int Segments = 32;
        private static readonly float[] LaneZ = { -1.6f, -.8f, 0, .8f, 1.6f };
        private void BuildStrings()
        {
            var parent = new GameObject("Strings").transform; parent.SetParent(root, false);
            strings = new LineRenderer[6];
            for (int i = 0; i < 6; i++) strings[i] = MakeLine(parent, "String " + (i + 1), Segments + 1, .04f);
            // Where the six strings come from (under the king) and where they pin the knight down around g1. #5 is the lowest, torn.
            kingBase = new Vector3[6]; for (int i = 0; i < 6; i++) kingBase[i] = new Vector3(-1.5f + .6f * i, .45f, 5.6f);
            anchors = new[] { new Vector3(.8f, .02f, 4.4f), new Vector3(4.4f, .02f, -1f), new Vector3(1.2f, .02f, -4.4f),
                new Vector3(3.6f, .02f, -4.4f), new Vector3(-.6f, .02f, -4.4f), new Vector3(4.4f, .02f, -2.6f) };
            lanes = new LineRenderer[10];
            for (int i = 0; i < 10; i++) lanes[i] = MakeLine(parent, "Lane " + i, 2, i % 5 == 2 ? .05f : .035f);
            blade = MakeLine(parent, "String Blade", 2, .035f);
            if (view.Art.swordPrefab != null)
            {
                sword = Instantiate(view.Art.swordPrefab, parent, false).transform; sword.name = "Sword";
                sword.gameObject.SetActive(false);
            }
        }
        private void RenderStrings(float s)
        {
            var knight = board.PlayerTransform.localPosition;
            Vector3 attach = knight + Vector3.up * .55f, hand = knight + new Vector3(.18f, .62f, -.05f);
            float jerk = JerkAmount(s);
            for (int i = 0; i < 6; i++)
            {
                var line = strings[i];
                bool hidden = s < Bind || s >= 41.2f || (i == 5 && s >= TearStory + 1.5f);
                line.gameObject.SetActive(!hidden); if (hidden) continue;
                Vector3 a, b; float amp = .025f;
                if (s < Bind + .9f) { float u = 1 - Mathf.Pow(1 - Mathf.Clamp01((s - Bind) / .9f), 3); b = kingBase[i]; a = Vector3.Lerp(kingBase[i], attach, u); amp = .06f * (1 - u) + .02f; }
                else { a = attach; b = Vector3.Lerp(kingBase[i], anchors[i], Smooth(s, Pinned, Pinned + 1.4f)); }
                amp += .13f * jerk;
                if (i == 5 && s >= Grip)
                {
                    if (s < TearStory) amp = .05f + .03f * Mathf.Sin(s * 30);
                    else { float u = Mathf.Clamp01((s - TearStory) / 1.5f); b = Vector3.Lerp(anchors[5], hand + Vector3.up * .9f, 1 - Mathf.Pow(1 - u, 3)); amp = .38f * (1 - u); }
                }
                float alpha = .95f * (1 - Smooth(s, 38.6f, 41.2f));
                SetString(line, a, b, amp, i * 1.7f, s * (float)spb * 34, alpha);
            }
            // The five left become the lanes, shooting out one at a time, lowest first; they fade into the lane art at the handover.
            float fadeOut = 1 - Smooth(s, Handover + .6f, End - .2f);
            for (int i = 0; i < 10; i++)
            {
                int j = i % 5; float side = i < 5 ? -1 : 1;
                float start = Lanes + (4 - j) * .55f, length = 30 * (1 - Mathf.Pow(1 - Mathf.Clamp01((s - start) / 2.6f), 3));
                var lane = lanes[i];
                lane.gameObject.SetActive(length > .01f && fadeOut > .01f); if (!lane.gameObject.activeSelf) continue;
                lane.SetPosition(0, new Vector3(side * 4.45f, .03f, LaneZ[j]));
                lane.SetPosition(1, new Vector3(side * (4.45f + length), .03f, LaneZ[j]));
                SetLineAlpha(lane, (j == 2 ? .8f : .5f) * fadeOut);
            }
            // The torn string becomes the sword in the knight's hand: the string straightens into the blade from the hand out,
            // with a white flash along it, and the sword is put away when the king's wave pushes the knight home.
            float swing = 0; { float u = (s - Swing) / .32f; if (u > 0 && u < 1) swing = Mathf.Sin(Mathf.PI * u) * 2.2f; }
            var tilt = Quaternion.AngleAxis(-14 + swing * Mathf.Rad2Deg, Vector3.forward);
            Vector3 up = tilt * Vector3.up;
            float drawn = Smooth(s, TearStory + 1.35f, TearStory + 1.75f) * (1 - Smooth(s, Push, Push + .3f));
            if (sword != null)
            {
                sword.gameObject.SetActive(drawn > .001f);
                sword.localPosition = hand; sword.localRotation = tilt;
                sword.localScale = new Vector3(1, drawn, 1);
            }
            // Without the model the line is the blade; with it, the line is only the flash of the string becoming it.
            float glint = sword == null ? (s >= TearStory + 1.4f && s < Push ? Smooth(s, TearStory + 1.3f, TearStory + 1.6f) : 0)
                : Smooth(s, TearStory + 1.3f, TearStory + 1.5f) * (1 - Smooth(s, TearStory + 1.6f, TearStory + 2.4f));
            blade.gameObject.SetActive(glint > .001f);
            if (glint > .001f) { blade.SetPosition(0, hand); blade.SetPosition(1, hand + up * .95f); SetLineAlpha(blade, glint); }
        }
        private Vector3 BladeTip => board.PlayerTransform.localPosition + new Vector3(.18f, 1.55f, -.05f);

        // ---------------------------------------------------------------- the boss and the crown
        private Transform bossQuad, crownQuad;
        private Vector3 bossHome, crownHome, pixelUp;
        private void BuildBossAndCrown()
        {
            var parent = new GameObject("King").transform; parent.SetParent(root, false);
            if (view.BossPortrait != null)
            {
                var sprite = view.Art.bossImage;
                bossQuad = MakeQuad(parent, "King Picture", Color.white, sprite != null ? sprite.texture : null).transform;
            }
            if (view.BossCrown != null && view.Art.crown != null)
                crownQuad = MakeQuad(parent, "Crown Picture", Color.white, view.Art.crown.texture).transform;
        }
        /// <summary>
        /// Stands the king and crown in the world where the battle draws them: each UI rect's corners are cast from the
        /// battle camera onto a plane facing it, behind the board, so the battle camera sees them exactly where the canvas has them.
        /// </summary>
        private void PlaceBossAndCrown()
        {
            if (bossQuad == null) { crownAt = new Vector3(0, 6, 7); bossMid = new Vector3(0, 3, 7); return; }
            var saved = (cam.transform.localPosition, cam.transform.localRotation);
            cam.transform.localPosition = battlePos; cam.transform.localRotation = battleRot; cam.projectionMatrix = BattleProjection();
            var forward = world.TransformDirection(battleRot * Vector3.forward);
            var plane = new Plane(-forward, world.TransformPoint(new Vector3(0, 0, BossDepth)));
            Vector3[] Project(RectTransform rect)
            {
                var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                var points = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    var ray = cam.ScreenPointToRay(corners[i]);
                    plane.Raycast(ray, out float d);
                    points[i] = world.InverseTransformPoint(ray.GetPoint(d));
                }
                return points;
            }
            var bossRect = (RectTransform)view.BossPortrait.transform;
            var boss = Project(bossRect);
            // Pivot at the feet, like the portrait, so the breathing can be copied as a plain offset.
            bossHome = (boss[0] + boss[3]) * .5f;
            SetQuad(bossQuad, boss, bossHome);
            pixelUp = (boss[1] - boss[0]) / Mathf.Max(1, bossRect.rect.height);
            bossMid = (boss[0] + boss[2]) * .5f;
            if (crownQuad != null)
            {
                var crown = Project((RectTransform)view.BossCrown.transform);
                crownHome = (crown[0] + crown[2]) * .5f;
                SetQuad(crownQuad, crown, crownHome);
                crownAt = crownHome;
            }
            else crownAt = (boss[1] + boss[2]) * .5f;
            cam.transform.localPosition = saved.Item1; cam.transform.localRotation = saved.Item2;
        }
        private void RenderBoss(float s)
        {
            if (bossQuad == null) return;
            // The same breathing as the portrait (5 px at 1.3 rad/s), so the handover does not jump.
            var breath = pixelUp * (5 * Mathf.Sin(Time.unscaledTime * 1.3f));
            bossQuad.localPosition = bossHome + breath;
            if (crownQuad != null)
            {
                crownQuad.localPosition = crownHome + breath;
                // The crown swells on every order the king gives.
                crownQuad.localScale = Vector3.one * (1 + .035f * OrderPulse(s));
            }
        }
        private static float OrderPulse(float s)
        {
            float v = 0;
            for (int k = 0; k < Plies; k++) if (s >= k + .5f) v = Mathf.Max(v, Mathf.Exp(-(s - (k + .5f)) * 4));
            foreach (float order in new[] { Order1, Order2, Threat }) if (s >= order) v = Mathf.Max(v, Mathf.Exp(-(s - order) * 4));
            return v;
        }

        // ---------------------------------------------------------------- noise
        private Transform spark, landingNoise;
        // The tutorial comes before the battle's first noise: the landed one sinks away after the handover.
        private bool noiseWaits;
        private double keptAt;
        private readonly Transform[] bigNoise = new Transform[5];
        private bool noiseKept;
        private void BuildNoise()
        {
            var parent = new GameObject("Noise").transform; parent.SetParent(root, false);
            Texture texture = view.Art.noiseSprite != null ? view.Art.noiseSprite.texture : null;
            spark = MakeNoise(parent, texture);
            for (int i = 0; i < 5; i++) bigNoise[i] = MakeNoise(parent, texture);
            var landing = new GameObject("Landing").transform; landing.SetParent(root, false);
            landingNoise = MakeNoise(landing, texture);
        }
        private Transform MakeNoise(Transform parent, Texture texture)
        {
            var quad = MakeQuad(parent, "Noise", Color.white, texture).transform;
            float aspect = texture != null ? (float)texture.width / texture.height : 2;
            SetQuad(quad, new[] { new Vector3(-.5f, -.5f / aspect), new Vector3(-.5f, .5f / aspect), new Vector3(.5f, .5f / aspect), new Vector3(.5f, -.5f / aspect) }, Vector3.zero);
            return quad;
        }
        private void RenderNoise(float s)
        {
            // The off-beat leap makes a sound: the first noise is born with it.
            float so = s >= Leap && s < Bind + .3f ? Smooth(s, Leap, Leap + .4f) * (1 - Smooth(s, Bind, Bind + .3f)) : 0;
            Billboard(spark, board.PlayerTransform.localPosition + new Vector3(.7f, .95f, 0), 1.1f, so);
            // The strike without noise throws its sound at the king; the wave washes it out.
            for (int i = 0; i < 5; i++)
            {
                bool on = s >= Swing && s < Fall;
                if (!on) { Billboard(bigNoise[i], Vector3.zero, 1, 0); continue; }
                var to = new Vector3(-1.4f + i * 1.1f, 2.9f + .3f * Mathf.Sin(i * 1.7f), 5.0f);
                var p = Vector3.Lerp(BladeTip, to, 1 - Mathf.Pow(1 - Mathf.Clamp01((s - Swing) / 3.2f), 3));
                float washed = Smooth(s, Wave + .3f + .1f * i, Wave + .9f + .1f * i);
                p.y += Mathf.Sin(s * 2 + i) * .06f - washed * .4f;
                Billboard(bigNoise[i], p, Mathf.Lerp(1.6f, 1.0f, washed), Smooth(s, Swing, Swing + .4f) * (1 - .5f * washed) * (1 - Smooth(s, Fall - .6f, Fall)));
            }
            RenderLandingNoise(s);
        }
        /// <summary>One piece survives the wash and lands where the battle's first noise will spawn.</summary>
        private void RenderLandingNoise(float s)
        {
            landingNoise.parent.gameObject.SetActive(true);
            if (s < Fall) { Billboard(landingNoise, Vector3.zero, 1, 0); return; }
            var from = new Vector3(-1.4f + 2 * 1.1f, 2.5f, 5.0f);
            var home = noiseCell + Vector3.up * (.5f + .08f * Mathf.Sin(Time.unscaledTime * 2.2f));
            float u = Mathf.Clamp01((s - Fall) / 2.4f);
            var p = Vector3.Lerp(from, home, 1 - Mathf.Pow(1 - u, 3)); p.y += Mathf.Sin(Mathf.PI * u) * .8f;
            Billboard(landingNoise, p, 1.4f, Smooth(s, Fall, Fall + .4f));
        }
        private void PredictNoise()
        {
            // With the tutorial next, the noise lands two cells above the knight's start, where the noise lesson puts its own.
            noiseWaits = game.TutorialFollowsPrologue;
            var settings = game.Battle.Settings;
            if (noiseWaits) { noiseCell = BoardWorldPresenter.CellPosition(new Board.GridPosition(settings.StartX, settings.StartY + 2)); return; }
            // Otherwise the battle's noise is seeded: a dry run of the same battle tells where its first noise appears.
            try
            {
                var dry = new BattleSession(settings, game.Stage.NoiseSeed);
                dry.Reset(1, game.Stage.NoiseSeed);
                dry.Advance(game.Timeline.TimeOf(0) + .01);
                if (dry.Noise.Pickups.Count > 0) noiseCell = BoardWorldPresenter.CellPosition(dry.Noise.Pickups[0].Cell);
            }
            catch (System.Exception error) { Debug.LogWarning("Prologue could not predict the first noise: " + error.Message); }
        }

        // ---------------------------------------------------------------- waves, strike, light
        private LineRenderer[] waves;
        private LineRenderer ghost;
        private void BuildEffects()
        {
            var parent = new GameObject("Effects").transform; parent.SetParent(root, false);
            waves = new LineRenderer[3];
            for (int i = 0; i < 3; i++) { waves[i] = MakeLine(parent, "Wave " + i, 97, .05f); waves[i].loop = true; }
            ghost = MakeLine(parent, "Strike Without Noise", 2, .16f);
        }
        private void RenderEffects(float s)
        {
            // The king's melody as rings of light across the board.
            for (int i = 0; i < 3; i++)
            {
                float u = (s - (Wave + .8f * i)) / 2.6f;
                var ring = waves[i];
                ring.gameObject.SetActive(u > 0 && u < 1); if (!ring.gameObject.activeSelf) continue;
                float r = Mathf.Lerp(.6f, 16, 1 - Mathf.Pow(1 - u, 3));
                for (int p = 0; p < 97; p++) { float a = p / 97f * Mathf.PI * 2; ring.SetPosition(p, new Vector3(Mathf.Cos(a) * r, .06f, 5.6f + Mathf.Sin(a) * r)); }
                SetLineAlpha(ring, .9f * (1 - u));
            }
            // The strike without noise: a grey streak straight through the king, and the crown does not move.
            float g = s >= Swing && s < Swing + 1.3f ? 1 - Smooth(s, Swing + .7f, Swing + 1.3f) : 0;
            ghost.gameObject.SetActive(g > .01f);
            if (g > .01f)
            {
                float reach = Smooth(s, Swing, Swing + .25f);
                var from = BladeTip; var to = from + (bossMid + new Vector3(0, 1, 6) - from) * reach;
                ghost.SetPosition(0, from); ghost.SetPosition(1, to);
                ghost.startColor = ghost.endColor = new Color(.66f, .66f, .66f, .9f * g);
            }
            // A white rim on the knight, from the king's side (A4-B); gone by the handover so the last frame is the battle's.
            rimLight.intensity = .9f * (1 - Smooth(s, Settle, Handover));
        }

        // ---------------------------------------------------------------- canvas layer: view, void, letterbox, prompt
        private void BuildLayer()
        {
            layer = PrototypeView.MakeRect(view.CanvasRect, "Prologue Layer", Vector2.zero, Vector2.zero); Stretch(layer);
            voidCover = FullScreen("Void", Color.black);
            introView = PrototypeView.MakeRect(layer, "Prologue View", Vector2.zero, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            Stretch(introView.rectTransform); introView.raycastTarget = false;
            flash = FullScreen("Crack Flash", Color.white);
            barTop = Bar("Letterbox Top", 1); barBottom = Bar("Letterbox Bottom", 0);
            var font = UiFonts.Korean(view.Art.menuFont, view.LatinFont);
            BuildPrompt();
            skipHint = PrototypeView.MakeRect(barBottom.rectTransform, "Skip Hint", Vector2.zero, new Vector2(400, 40)).gameObject.AddComponent<TextMeshProUGUI>();
            var hintRect = skipHint.rectTransform; hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(1, .5f);
            hintRect.anchoredPosition = new Vector2(-40, 0);
            if (font != null) skipHint.font = font;
            skipHint.fontSize = 18; skipHint.alignment = TextAlignmentOptions.Right; skipHint.raycastTarget = false;
            skipHint.text = "ESC  건너뛰기"; skipHint.color = new Color(1, 1, 1, .45f);
            contentGroup = Group(view.ContentRect);
            if (view.BossPortrait != null) bossGroup = Group((RectTransform)view.BossPortrait.transform);
            layer.gameObject.SetActive(false);
        }
        /// <summary>
        /// The J prompt on a card like the pause screen's, so it reads over any part of the board. 5 px in the 1920 frame is
        /// about 4 on this 1600 canvas.
        /// </summary>
        private void BuildPrompt() => prompt = new KeyPromptCard(layer, "Prompt", view.Art, view.LatinFont, 84, 40, 36);
        private void RenderLayer(float s)
        {
            // The board is whole and the world around it empty until the string tears; then it cracks into the battle's
            // world of floating debris (A5-B; placeholder until the "whole board" background set exists).
            SetAlpha(voidCover, 1 - Smooth(s, TearStory, TearStory + 1.6f));
            SetAlpha(flash, s >= TearStory ? .75f * Mathf.Exp(-(s - TearStory) * 3.5f) : 0);
            // The letterbox is the king's board, with no control. It lifts on the handover.
            // With the tutorial next the bars stay: its letterbox takes over at the same height and narrows from there.
            float bars = s < Handover || game.TutorialFollowsPrologue ? Smooth(s, 0, 2) : 1 - Smooth(s, Handover, Handover + 1.5f);
            var size = layer.rect.size;
            float bar = Mathf.Max((size.y - size.x / CutscenePlayer.Aspect) / 2, size.y * .06f);
            barTop.rectTransform.sizeDelta = barBottom.rectTransform.sizeDelta = new Vector2(0, bar);
            barTop.rectTransform.anchoredPosition = new Vector2(0, bar * (1 - bars));
            barBottom.rectTransform.anchoredPosition = new Vector2(0, -bar * (1 - bars));
            skipHint.alpha = .45f * bars;
            // The handover: the battle's own picture comes up over one beat while the prologue's goes.
            float handover = Smooth(s, Handover + 1, End - .15f);
            SetGroups(handover);
            introView.color = new Color(1, 1, 1, 1 - handover);
            board.CellOutline.gameObject.SetActive(handover > .5f);
            // The one prompt: J, while the loop waits for it.
            bool waiting = s >= IntroTimeline.LoopStart && s < TearStory && !Torn;
            if (!waiting) prompt.Hide();
            else
            {
                prompt.Show("J", "줄을 끊는다");
                // The keycap kicks on every beat of the waiting bar.
                prompt.Pulse(s % 1);
                // Beside the knight's hand on the lowest string; the intro camera renders at screen size, so its screen point is a screen point.
                var screen = cam.WorldToScreenPoint(world.TransformPoint(board.PlayerTransform.localPosition + new Vector3(0, .6f, 0)));
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, null, out var local))
                {
                    // Kept inside the letterbox's picture, clear of its edges.
                    var card = prompt.Size;
                    float limitX = size.x * .5f - card.x * .5f - 40, limitY = size.y * .5f - bar - card.y * .5f - 24;
                    prompt.Rect.anchoredPosition = new Vector2(Mathf.Clamp(local.x + 60 + card.x * .5f, -limitX, limitX), Mathf.Clamp(local.y, -limitY, limitY));
                }
            }
        }
        private void SetGroups(float alpha)
        {
            if (contentGroup != null) contentGroup.alpha = alpha;
            if (bossGroup != null) bossGroup.alpha = alpha;
        }

        // ---------------------------------------------------------------- sound (placeholder clips, scheduled on the beat)
        private struct Cue { public float Beat; public string Clip; public float Volume; public Cue(float b, string c, float v = 1) { Beat = b; Clip = c; Volume = v; } }
        private readonly List<Cue> before = new List<Cue>(), after = new List<Cue>();
        private readonly List<AudioSource> sources = new List<AudioSource>(), loopSources = new List<AudioSource>();
        private int nextEvent, nextBar, nextAfter, nextSource;
        private void BuildAudio()
        {
            for (int i = 0; i < 24; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
                sources.Add(source);
            }
            // Section A, on intro beats. The king's beat is a chess clock (S2); the king's orders are sung notes.
            for (int b = 0; b < IntroTimeline.LoopStart; b++) before.Add(new Cue(b, "clock", b % 4 == 0 ? .45f : .3f));
            for (int k = 0; k < Plies; k++) { before.Add(new Cue(k + .5f, Melody[k])); before.Add(new Cue(k + 1.02f, "place")); }
            before.Add(new Cue(Order1, "king_order")); before.Add(new Cue(Order2, "king_order2"));
            before.Add(new Cue(Threat, "choir_threat"));
            before.Add(new Cue(Leap, "gliss")); before.Add(new Cue(Land, "place"));
            before.Add(new Cue(Bind, "tune")); before.Add(new Cue(Pinned, "thud"));
            // Bound: the six strings out of tune, clean (S3-B), until the tear.
            before.Add(new Cue(24, "strain_bar")); before.Add(new Cue(28, "strain_bar"));
            foreach (float jerk in Jerks) before.Add(new Cue(jerk, "jerk"));
            before.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            // Section B, in beats after the tear. From here the knight's sound is distorted (S3-B).
            after.Add(new Cue(0, "tear"));
            after.Add(new Cue(Lanes - TearStory, "whoosh_down"));
            string[] lane = { "lane_A2", "lane_D3", "lane_G3", "lane_B3", "lane_E4" };
            for (int j = 0; j < 5; j++) after.Add(new Cue(Lanes - TearStory + j * .55f, lane[j]));
            after.Add(new Cue(Swing - TearStory, "stroke_E5")); after.Add(new Cue(Swing - TearStory, "guitar_hold")); after.Add(new Cue(Swing - TearStory + .05f, "whoosh_ghost"));
            after.Add(new Cue(Wave - TearStory, "choir_wash"));
            for (int i = 0; i < 3; i++) after.Add(new Cue(Wave - TearStory + .8f * i, "whoosh_wave"));
            after.Add(new Cue(Fall - TearStory, "shatter"));
            after.Add(new Cue(Fall + .5f - TearStory, "ring5"));
            after.Sort((a, b) => a.Beat.CompareTo(b.Beat));
        }
        private void ScheduleAudio(double now)
        {
            const double ahead = .25;
            if (!Torn)
            {
                while (nextEvent < before.Count && origin + before[nextEvent].Beat * spb <= now + ahead)
                { Play(before[nextEvent], origin + before[nextEvent].Beat * spb, now, null); nextEvent++; }
                // The waiting bar: the clock on each beat and the bound strings, again and again until J.
                double bar = origin + (IntroTimeline.LoopStart + nextBar * IntroTimeline.LoopBeats) * spb;
                if (bar <= now + ahead)
                {
                    Play(new Cue(0, "strain_bar"), bar, now, loopSources);
                    for (int b = 0; b < 4; b++) Play(new Cue(0, "clock", b == 0 ? .45f : .3f), bar + b * spb, now, loopSources);
                    nextBar++;
                }
            }
            else
            {
                double tear = origin + tearIntroBeat * spb;
                while (nextAfter < after.Count && tear + after[nextAfter].Beat * spb <= now + ahead)
                { Play(after[nextAfter], tear + after[nextAfter].Beat * spb, now, null); nextAfter++; }
            }
        }
        private void Play(Cue cue, double at, double now, List<AudioSource> track)
        {
            var clip = data.Clip(cue.Clip);
            if (clip == null) return;
            var source = sources[nextSource]; nextSource = (nextSource + 1) % sources.Count;
            source.Stop(); source.clip = clip;
            source.volume = data.volume * GameFlow.MusicVolume * cue.Volume;
            source.PlayScheduled(System.Math.Max(at, now + .01));
            track?.Add(source);
        }

        // ---------------------------------------------------------------- building blocks
        private static Vector3 Cell(string name) => new Vector3(name[0] - 'a' - 3.5f, 0, name[1] - '1' - 3.5f);
        private static float Smooth(float s, float a, float b) => Smooth01(Mathf.Clamp01((s - a) / (b - a)));
        private static float Smooth01(float u) => u * u * (3 - 2 * u);
        private static Key Lerp(Key a, Key b, float u) => new Key(Vector3.Lerp(a.Pos, b.Pos, u), Vector3.Lerp(a.Look, b.Look, u), Mathf.Lerp(a.Fov, b.Fov, u));
        private static Matrix4x4 LerpMatrix(Matrix4x4 a, Matrix4x4 b, float u)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = Mathf.Lerp(a[i], b[i], u);
            return m;
        }
        private Material Flat(Color color, Texture texture = null)
        {
            var material = new Material(flatTemplate) { name = "Prologue Flat" };
            material.SetTexture("_MainTex", texture != null ? texture : Texture2D.whiteTexture);
            material.color = color; resources.Add(material);
            return material;
        }
        private Material Lit(Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = "Prologue Marble" };
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", .15f); material.SetFloat("_Metallic", 0);
            resources.Add(material);
            return material;
        }
        private GameObject MakeMesh(Transform parent, string name, Mesh mesh, Material material)
        {
            var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(parent, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return item;
        }
        private GameObject MakeQuad(Transform parent, string name, Color color, Texture texture = null)
        {
            var mesh = new Mesh { name = name, vertices = new Vector3[4], uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            resources.Add(mesh);
            return MakeMesh(parent, name, mesh, Flat(color, texture));
        }
        private static void SetQuad(Transform quad, Vector3[] corners, Vector3 pivot)
        {
            var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
            var local = new Vector3[4]; for (int i = 0; i < 4; i++) local[i] = corners[i] - pivot;
            mesh.vertices = local; mesh.RecalculateBounds(); mesh.RecalculateNormals();
            quad.localPosition = pivot;
        }
        private void Billboard(Transform quad, Vector3 at, float width, float alpha)
        {
            quad.gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            quad.localPosition = at;
            quad.rotation = cam.transform.rotation;
            quad.localScale = Vector3.one * width;
            SetMaterialAlpha(quad, alpha);
        }
        private static void SetMaterialAlpha(Transform item, float alpha)
        {
            var material = item.GetComponent<MeshRenderer>().sharedMaterial;
            var c = material.color; c.a = alpha; material.color = c;
        }
        private void SetFlat(Transform item, Vector3 at, float alpha)
        {
            item.gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            item.localPosition = at;
            foreach (var renderer in item.GetComponentsInChildren<MeshRenderer>())
            { var c = renderer.sharedMaterial.color; c.a = alpha * (renderer.name.StartsWith("Under") ? .6f : 1); renderer.sharedMaterial.color = c; }
        }
        // Flat shapes lie on the board (y up). Built in the XZ plane.
        private static Mesh FlatMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            var uv = new Vector2[vertices.Length]; mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        private static Mesh RingMesh(float inner, float outer, int steps = 40)
        {
            var v = new Vector3[steps * 2]; var t = new int[steps * 6];
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2 / steps; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
                int n = (i + 1) % steps;
                t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
            }
            return FlatMesh("Ring", v, t);
        }
        private static Mesh DiscMesh(float radius, int steps = 28)
        {
            var v = new Vector3[steps + 1]; var t = new int[steps * 3];
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2 / steps; v[i + 1] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % steps + 1; t[i * 3 + 2] = i + 1;
            }
            return FlatMesh("Disc", v, t);
        }
        private Transform MakeRing(Transform parent)
        {
            var ring = new GameObject("Candidate").transform; ring.SetParent(parent, false);
            var under = RingMesh(.27f, .43f); var line = RingMesh(.31f, .38f); var dot = DiscMesh(.1f);
            resources.Add(under); resources.Add(line); resources.Add(dot);
            var u = MakeMesh(ring, "Under", under, Flat(Color.black)); u.transform.localPosition = Vector3.up * .014f;
            var l = MakeMesh(ring, "Ring", line, Flat(Color.white)); l.transform.localPosition = Vector3.up * .016f;
            var d = MakeMesh(ring, "Dot", dot, Flat(Color.white)); d.transform.localPosition = Vector3.up * .018f;
            foreach (var r in new[] { u, l, d }) r.GetComponent<MeshRenderer>().sortingOrder = 2;
            ring.gameObject.SetActive(false);
            return ring;
        }
        private void SetRing(Transform ring, Vector3 at, float alpha, bool target, bool dot, float scale = 1)
        {
            ring.gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            ring.localPosition = new Vector3(at.x, 0, at.z);
            ring.localScale = Vector3.one * scale * (target ? 1.1f : 1);
            foreach (Transform part in ring)
            {
                var material = part.GetComponent<MeshRenderer>().sharedMaterial;
                float a = part.name == "Under" ? alpha * .55f : part.name == "Ring" ? Mathf.Min(1, alpha * (target ? 1.15f : .8f)) : alpha;
                part.gameObject.SetActive(part.name != "Dot" || dot);
                var c = material.color; c.a = a; material.color = c;
            }
        }
        // An order: a dark underlay so it reads on light squares, then the white arrow; a dot on the target square.
        private Transform[] MakeArrow(Transform parent, string name)
        {
            var layers = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var item = new GameObject(name + (i == 0 ? " Under" : "")).transform; item.SetParent(parent, false);
                var color = i == 0 ? Color.black : Color.white;
                var shaft = FlatMesh("Shaft", new[] { new Vector3(-.5f, 0, 0), new Vector3(-.5f, 0, 1), new Vector3(.5f, 0, 1), new Vector3(.5f, 0, 0) }, new[] { 0, 1, 2, 0, 2, 3 });
                var head = FlatMesh("Head", new[] { new Vector3(-.2f, 0, 0), new Vector3(0, 0, .34f), new Vector3(.2f, 0, 0) }, new[] { 0, 1, 2 });
                var dot = DiscMesh(.1f);
                resources.Add(shaft); resources.Add(head); resources.Add(dot);
                MakeMesh(item, "Shaft", shaft, Flat(color)); MakeMesh(item, "Head", head, Flat(color));
                MakeMesh(parent, name + " Dot" + (i == 0 ? " Under" : ""), dot, Flat(color));
                layers[i] = item;
            }
            return layers;
        }
        private void SetArrow(Transform[] layers, Vector3 from, Vector3 to, float width, float alpha, float grow)
        {
            var d = to - from; d.y = 0; float length = d.magnitude;
            for (int i = 0; i < 2; i++)
            {
                var item = layers[i];
                var dot = item.parent.Find(item.name.Replace(" Under", "") + " Dot" + (i == 0 ? " Under" : ""));
                bool on = alpha > .01f && length > .01f;
                item.gameObject.SetActive(on); if (dot != null) dot.gameObject.SetActive(on && grow >= .99f);
                if (!on) continue;
                float w = i == 0 ? width + .07f : width, pad = i == 0 ? .05f : 0, y = i == 0 ? .02f : .024f;
                item.localPosition = from + Vector3.up * y; item.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                const float start = .28f; float full = Mathf.Max(.05f, length - start - .34f), shaftLength = Mathf.Max(.02f, full * grow);
                var shaft = item.Find("Shaft"); shaft.localPosition = new Vector3(0, 0, start); shaft.localScale = new Vector3(w, 1, shaftLength + pad);
                var head = item.Find("Head"); head.localPosition = new Vector3(0, 0, start + shaftLength - pad); head.localScale = new Vector3(w / .14f, 1, 1 + pad * 3);
                float a = i == 0 ? alpha * .6f : alpha;
                foreach (var r in item.GetComponentsInChildren<MeshRenderer>()) { var c = r.sharedMaterial.color; c.a = a; r.sharedMaterial.color = c; }
                if (dot != null)
                {
                    dot.localPosition = to + Vector3.up * (y + .002f); dot.localScale = Vector3.one * (1 + w * 2.5f);
                    var m = dot.GetComponent<MeshRenderer>().sharedMaterial; var dc = m.color; dc.a = a; m.color = dc;
                }
            }
        }
        // The refused square: an X over the order's dot.
        private Transform MakeCrossMark(Transform parent)
        {
            var item = new GameObject("Refused").transform; item.SetParent(parent, false);
            for (int layerIndex = 0; layerIndex < 2; layerIndex++)
                for (int k = 0; k < 2; k++)
                {
                    float w = layerIndex == 0 ? .17f : .09f, l = layerIndex == 0 ? .76f : .68f;
                    var bar = FlatMesh("Bar", new[] { new Vector3(-w / 2, 0, -l / 2), new Vector3(-w / 2, 0, l / 2), new Vector3(w / 2, 0, l / 2), new Vector3(w / 2, 0, -l / 2) }, new[] { 0, 1, 2, 0, 2, 3 });
                    resources.Add(bar);
                    var g = MakeMesh(item, layerIndex == 0 ? "Under" : "Bar", bar, Flat(layerIndex == 0 ? Color.black : Color.white));
                    g.transform.localRotation = Quaternion.Euler(0, k == 0 ? 45 : -45, 0); g.transform.localPosition = Vector3.up * (.002f * layerIndex);
                }
            item.gameObject.SetActive(false);
            return item;
        }
        private Transform MakeFrame(Transform parent, string name, float size, float thickness)
        {
            var item = new GameObject(name).transform; item.SetParent(parent, false);
            float h = size / 2, i = h - thickness;
            var frame = FlatMesh("Frame", new[] {
                new Vector3(-h,0,-h), new Vector3(-h,0,h), new Vector3(h,0,h), new Vector3(h,0,-h),
                new Vector3(-i,0,-i), new Vector3(-i,0,i), new Vector3(i,0,i), new Vector3(i,0,-i) },
                new[] { 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7 });
            var fill = FlatMesh("Fill", new[] { new Vector3(-i,0,-i), new Vector3(-i,0,i), new Vector3(i,0,i), new Vector3(i,0,-i) }, new[] { 0,1,2, 0,2,3 });
            resources.Add(frame); resources.Add(fill);
            MakeMesh(item, "Under", fill, Flat(Color.black));
            MakeMesh(item, "Frame", frame, Flat(Color.white)).transform.localPosition = Vector3.up * .002f;
            item.gameObject.SetActive(false);
            return item;
        }
        private void SetBeam(Transform item, Vector3 from, Vector3 to, float width, float alpha)
        {
            item.gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            var d = to - from;
            // A thin quad from the crown to the knight, turned to face the camera around its own length.
            var mesh = item.GetComponent<MeshFilter>().sharedMesh;
            var side = Vector3.Cross(d.normalized, (cam.transform.localPosition - (from + to) * .5f).normalized).normalized * width * .5f;
            mesh.vertices = new[] { from - side, to - side, to + side, from + side }; mesh.RecalculateBounds();
            item.localPosition = Vector3.zero; item.localRotation = Quaternion.identity;
            SetMaterialAlpha(item, alpha);
        }
        private LineRenderer MakeLine(Transform parent, string name, int points, float width)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false; line.positionCount = points; line.widthMultiplier = width;
            line.numCapVertices = 2; line.sharedMaterial = Flat(Color.white);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            line.gameObject.SetActive(false);
            return line;
        }
        private static void SetLineAlpha(LineRenderer line, float alpha) { var c = new Color(1, 1, 1, alpha); line.startColor = line.endColor = c; }
        private static void SetString(LineRenderer line, Vector3 a, Vector3 b, float amp, float phase, float time, float alpha)
        {
            var across = Vector3.Cross((b - a).normalized, Vector3.up).normalized;
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments, w = Mathf.Sin(Mathf.PI * u) * amp * Mathf.Sin(time + phase + u * 9);
                line.SetPosition(i, Vector3.Lerp(a, b, u) + across * w + Vector3.up * (w * .7f));
            }
            SetLineAlpha(line, alpha);
        }

        // Chess pieces: the previz silhouettes, turned on a lathe, in marble (A3-A), three quarters the size (A2-C).
        private Transform MakePiece(string type, Material material, Transform parent)
        {
            var item = new GameObject(type).transform; item.SetParent(parent, false);
            void Add(Mesh mesh, Vector3 at, Vector3 scale) { if (!resources.Contains(mesh)) resources.Add(mesh); var g = MakeMesh(item, type, mesh, material); g.transform.localPosition = at; g.transform.localScale = scale; g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            Mesh ball = Lathe(Semicircle(1));
            switch (type)
            {
                case "pawn":
                    Add(Lathe(new[] { V(0,0),V(.3f,0),V(.3f,.06f),V(.24f,.1f),V(.2f,.13f),V(.11f,.36f),V(.09f,.44f),V(.17f,.47f),V(.17f,.5f),V(0,.5f) }), Vector3.zero, Vector3.one);
                    Add(ball, new Vector3(0, .6f, 0), Vector3.one * .13f); break;
                case "bishop":
                    Add(Lathe(new[] { V(0,0),V(.32f,0),V(.32f,.07f),V(.25f,.11f),V(.2f,.16f),V(.12f,.5f),V(.1f,.62f),V(.19f,.66f),V(.19f,.7f),V(0,.7f) }), Vector3.zero, Vector3.one);
                    Add(ball, new Vector3(0, .88f, 0), new Vector3(.15f, .22f, .15f)); Add(ball, new Vector3(0, 1.12f, 0), Vector3.one * .05f); break;
                case "rook":
                    Add(Lathe(new[] { V(0,0),V(.33f,0),V(.33f,.07f),V(.27f,.12f),V(.22f,.17f),V(.2f,.6f),V(.27f,.64f),V(.27f,.82f),V(0,.82f) }), Vector3.zero, Vector3.one);
                    for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; Add(Box(), new Vector3(Mathf.Cos(a) * .2f, .87f, Mathf.Sin(a) * .2f), Vector3.one * .11f); }
                    break;
                default:
                    Add(Lathe(new[] { V(0,0),V(.35f,0),V(.35f,.07f),V(.28f,.12f),V(.22f,.18f),V(.12f,.72f),V(.1f,.86f),V(.22f,.9f),V(.18f,.96f),V(.2f,1.08f),V(0,1.08f) }), Vector3.zero, Vector3.one);
                    for (int i = 0; i < 8; i++) { float a = i * 45 * Mathf.Deg2Rad; Add(ball, new Vector3(Mathf.Cos(a) * .19f, 1.11f, Mathf.Sin(a) * .19f), Vector3.one * .045f); }
                    Add(ball, new Vector3(0, 1.2f, 0), Vector3.one * .07f); break;
            }
            item.localScale = Vector3.one * PieceScale;
            return item;
        }
        private static Vector2 V(float r, float y) => new Vector2(r, y);
        private static Vector2[] Semicircle(float r) { var p = new Vector2[13]; for (int i = 0; i <= 12; i++) { float a = -Mathf.PI / 2 + i * Mathf.PI / 12; p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); } return p; }
        private static Mesh Lathe(Vector2[] profile, int steps = 32)
        {
            int rows = profile.Length, cols = steps + 1;
            var v = new Vector3[rows * cols]; var uv = new Vector2[rows * cols]; var t = new List<int>();
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
            {
                float a = c * Mathf.PI * 2 / steps;
                v[r * cols + c] = new Vector3(Mathf.Cos(a) * profile[r].x, profile[r].y, Mathf.Sin(a) * profile[r].x);
                uv[r * cols + c] = new Vector2(c / (float)steps, profile[r].y);
            }
            for (int r = 0; r < rows - 1; r++) for (int c = 0; c < steps; c++)
            {
                int i = r * cols + c;
                t.Add(i); t.Add(i + cols); t.Add(i + 1); t.Add(i + 1); t.Add(i + cols); t.Add(i + cols + 1);
            }
            var mesh = new Mesh { name = "Lathe", vertices = v, uv = uv, triangles = t.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
        private static Mesh Box()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var mesh = Instantiate(probe.GetComponent<MeshFilter>().sharedMesh);
            DestroyImmediate(probe);
            return mesh;
        }
        // Marble: a pale or dark stone with soft veins, so the pieces sit with the scratched stone board.
        private Texture2D Marble(Color stone, Color vein, int seed)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Prologue Marble", wrapMode = TextureWrapMode.Repeat };
            var random = new System.Random(seed); float ox = (float)random.NextDouble() * 50, oy = (float)random.NextDouble() * 50;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, w = y / (float)size;
                float turbulence = 0, amplitude = 1, frequency = 3;
                for (int o = 0; o < 4; o++) { turbulence += amplitude * Mathf.PerlinNoise(ox + u * frequency, oy + w * frequency); amplitude *= .5f; frequency *= 2; }
                float veins = Mathf.Pow(1 - Mathf.Abs(Mathf.Sin((u * 3 + w * 1.5f + turbulence * 2.2f) * Mathf.PI)), 7);
                float speck = Mathf.PerlinNoise(ox * 2 + u * 40, oy * 2 + w * 40) * .08f;
                pixels[y * size + x] = Color.Lerp(stone, vein, veins * .8f) * (1 - speck + .04f);
            }
            texture.SetPixels(pixels); texture.Apply(true);
            resources.Add(texture);
            return texture;
        }
        private UnityEngine.UI.Image FullScreen(string name, Color color)
        {
            var rect = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero); Stretch(rect);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }
        private UnityEngine.UI.Image Bar(string name, float edge)
        {
            var rect = PrototypeView.MakeRect(layer, name, Vector2.zero, Vector2.zero);
            rect.anchorMin = new Vector2(0, edge); rect.anchorMax = new Vector2(1, edge); rect.pivot = new Vector2(.5f, edge);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.black; image.raycastTarget = false;
            return image;
        }
        private static CanvasGroup Group(RectTransform rect)
        {
            var group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            return group;
        }
        private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero; }
        private static void SetAlpha(UnityEngine.UI.Graphic g, float alpha) { var c = g.color; c.a = alpha; g.color = c; }
        private void OnDestroy()
        {
            if (target != null) { if (cam != null) cam.targetTexture = null; target.Release(); Destroy(target); }
            foreach (var item in resources) if (item != null) Destroy(item);
        }
    }
}
