using System.Collections.Generic;
using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The prologue (prologue revision R1-A..R7-A: one meaning per shot, no captions). rule: every piece hangs on a string
    /// from the king, who lights the square it is to go to, and moves one square on a beat of the chess clock. offbeat:
    /// the king lights h3 for the knight; three times it jumps an L the other way, to e2. The first time on the beat (the
    /// wrong square), and the king's string puts it back; the second half a beat off (the noise is born of it), put back
    /// again; the third, half a beat off again, its own string slipping free. bind: six strings from the king catch it by
    /// the legs in the air, drag it down and pin it low to its square; the noise is torn off it and thrown away into the
    /// dark; the lowest string lights up and the knight takes it in its hand. The player plucks that string with J (현을
    /// 뜯는다). string: it becomes the sword. lanes: the five left stretch across the board and become the lanes;
    /// the king takes his pieces away. face: the knight and the king alone, the discarded noise settling on the board. Its
    /// last downbeat is the tutorial's (or the song's) start.
    ///
    /// The picture is a pure function of the story beat (<see cref="IntroTimeline"/>). An intro camera renders the board
    /// world full screen; on the handover its pose and projection equal the battle's (board camera through the board
    /// view's rect), and the boss and crown stand in the world exactly where the battle draws them on the canvas, so the
    /// last frame is the battle screen. Rules stay in BattleSession; this only reads them.
    /// </summary>
    public sealed class IntroDirector : MonoBehaviour
    {
        // Story beats of the shots (see IntroTimeline.Shot).
        private const float FirstPly = 4, Caught = 16, Pinned = 17, Glow = 28,
            TearStory = 36, Lanes = 40, Withdraw = 42, Face = 48, Settle = 52, Handover = 54, End = 56;
        // The knight's three tries (offbeat): each an L to the left, landing on the beat (11), then off it (13.5), then a
        // jump off the beat (15.5) that is caught in the air. After the first two the king puts it back on the beat.
        private const float Try1 = 11, Back1 = 12, Try2 = 13.5f, Back2 = 14, Try3 = 15.5f;
        // When the king lights the square he wants for the knight (h3): from each order to just after the try against it.
        private static readonly (float from, float to)[] KnightOrders = { (10, 11.3f), (12.1f, 13.7f), (14.1f, 15.7f) };
        private static readonly float[] Struggles = { 22, 26 };
        // Beat lists read every frame.
        private static readonly float[] Backs = { Back1, Back2 }, TryGathers = { Try1 - .5f, Try2 - .45f, Try3 },
            OrderEchoes = { Back1 - .45f, Back2 - .4f, Caught, Withdraw };
        private const float BossDepth = 7f, PieceScale = .75f;
        // One ply on each beat 4-9, white and black in turn: the king plays both sides.
        private static readonly (string from, string to)[] PlyMoves =
            { ("e7", "e5"), ("e2", "e4"), ("d7", "d6"), ("g2", "g3"), ("c8", "g4"), ("d2", "d3") };
        private static int Plies => PlyMoves.Length;

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
        // The prologue's whole-board background layers (null entries where the set has none); null without the set.
        private UnityEngine.UI.Image[] wholeBoard;
        /// <summary>The whole-board layers' opacity now, back to front (tests and captures).</summary>
        public float[] WholeBoardAlpha => wholeBoard == null ? new float[0] : System.Array.ConvertAll(wholeBoard, i => i == null ? 0 : i.color.a);
        private CanvasGroup skipHintGroup;
        private KeyPromptCard prompt;
        private CanvasGroup contentGroup, bossGroup;
        private Material flatTemplate;
        private Texture2D whiteMarble, blackMarble;
        private readonly List<Object> resources = new List<Object>();

        private double spb, origin, tearIntroBeat = -1;
        private float seek = -1, story;
        private bool playing, built;
        private Vector3 battlePos; private Quaternion battleRot;

        public bool Playing => playing;
        /// <summary>The story beat shown now (see <see cref="IntroTimeline"/>).</summary>
        public float StoryBeat => story;
        public bool Torn => tearIntroBeat >= 0;
        /// <summary>DSP time of the handover once the string is torn, else -1.</summary>
        public double HandoverDsp => Torn ? origin + (tearIntroBeat + IntroTimeline.AfterTear) * spb : -1;

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
            flatTemplate = view.Art.noiseMaterial;
            if (flatTemplate == null) { flatTemplate = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")); resources.Add(flatTemplate); }
            whiteMarble = Marble(new Color(.86f, .85f, .82f), new Color(.55f, .55f, .56f), 7);
            blackMarble = Marble(new Color(.075f, .075f, .085f), new Color(.30f, .30f, .32f), 11);
            BuildLayer(); BuildPieces(); BuildOrder(); BuildStrings(); BuildBossAndCrown(); BuildNoise();
            BuildAudio();
        }

        // ---------------------------------------------------------------- control
        public void Play()
        {
            Build();
            playing = true; seek = -1; tearIntroBeat = -1; story = 0;
            origin = AudioSettings.dspTime + .35;
            nextEvent = 0; nextBar = 0; nextAfter = 0; handoverTick = false;
            root.gameObject.SetActive(true); layer.gameObject.SetActive(true);
            cam.enabled = true; rimLight.enabled = true;
            // The battle's picture is hidden until the handover (SetGroups), so its camera rests until then.
            board.BoardCamera.enabled = false;
            board.FreeRoam = true;
            PlayStem(stemA, data.cueA, origin, 0);
            Render(0);
        }
        /// <summary>Takes everything down; the battle's own picture is shown again.</summary>
        public void Stop() { StopInternal(atHandover: false); }
        private void StopInternal(bool atHandover)
        {
            if (!built) return;
            playing = false; seek = -1;
            // Handing over on the last downbeat, nothing of the prologue is still to come: what still sounds (a tail, the
            // composed cue's end) rings out into the tutorial or the song instead of being cut off on a frame, which clicks.
            // Esc and the like stop it all.
            if (!atHandover) foreach (var source in sources) source.Stop();
            foreach (var stem in Stems()) if (stem != null && (!atHandover || stem != stemB)) stem.Stop();
            layer.gameObject.SetActive(false);
            cam.enabled = false; rimLight.enabled = false;
            board.BoardCamera.enabled = true;
            root.gameObject.SetActive(false);
            SetGroups(1);
            board.CellOutline.gameObject.SetActive(true);
            board.PlayerTransform.localPosition = Cell("g1");
            board.PlayerTransform.localRotation = Quaternion.identity;
            board.PlayerTransform.localScale = Vector3.one;
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
            // The waiting bar (and bars queued past the tear) stops 30 ms into the tear's sound, under it. Pressed a little
            // after the beat, the tear sounds at once (Play's +10 ms) rather than on the beat already gone, so the cut follows
            // it: cut on the beat, the bound strings would fall silent before anything tore them.
            double tearDsp = origin + beat * spb, cut = System.Math.Max(tearDsp, AudioSettings.dspTime + .01) + .03;
            foreach (var source in loopSources) if (source != null) source.SetScheduledEndTime(cut);
            loopSources.Clear();
            if (stemA != null && stemA.isPlaying) stemA.SetScheduledEndTime(cut);
            PlayStem(stemB, data.cueB, tearDsp, 0);
            if (!game.TutorialFollowsPrologue) game.ScheduleSongFromPrologue(HandoverDsp, IntroTimeline.HandoverSongTime(game.Timeline));
        }
        /// <summary>Captures and tests: show this story beat (no sound) instead of the running one; a negative value resumes.</summary>
        public void Seek(float storyBeat)
        {
            seek = storyBeat;
            if (storyBeat >= 0) foreach (var stem in Stems()) if (stem != null) stem.Stop();
        }
        /// <summary>Tests only: move the running intro clock so it reads this intro beat now (cues before it are dropped).</summary>
        internal void JumpForTests(double introBeat)
        {
            origin = AudioSettings.dspTime - introBeat * spb;
            if (!Torn) PlayStem(stemA, data.cueA, origin, 0);
            while (nextEvent < before.Count && before[nextEvent].Beat < introBeat) nextEvent++;
            nextBar = (int)System.Math.Max(0, System.Math.Floor((introBeat - IntroTimeline.LoopStart) / IntroTimeline.LoopBeats) + 1);
        }

        private void LateUpdate()
        {
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
                StopInternal(atHandover: true);
                if (game.TutorialFollowsPrologue) game.PrologueIntoTutorial(handover); else game.FinishPrologue();
            }
        }

        // ---------------------------------------------------------------- the picture
        private void Render(float s)
        {
            EnsureTarget();
            RenderKnight(s);
            RenderCamera(s);
            RenderPieces(s);
            RenderOrder(s);
            RenderStrings(s);
            RenderBoss(s);
            RenderNoise(s);
            rimLight.intensity = .9f * (1 - Smooth(s, Settle, Handover));
            RenderLayer(s);
        }

        // Camera keys: board-local position, look point, vertical field of view. The king stays within ±25° of front (A6-A).
        // A cut only at 10, 17 (the slam), 28, 40 and 48; nothing moves while the waiting bar repeats (32-36).
        private struct Key { public Vector3 Pos, Look; public float Fov; public Key(Vector3 p, Vector3 l, float f) { Pos = p; Look = l; Fov = f; } }
        private Vector3 crownAt, bossMid, kingHand;
        private void RenderCamera(float s)
        {
            Vector3 g1 = Cell("g1");
            Key k; float shake = 0;
            if (s < 7)
                // rule: high over the board, every string running up out of the top of the frame.
                k = Lerp(new Key(new Vector3(0, 12.5f, -6.5f), new Vector3(0, 0, .2f), 42), new Key(new Vector3(0, 10.0f, -9.2f), new Vector3(0, .3f, .8f), 44), Smooth(s, 0, 7));
            else if (s < 10)
                // Up along the strings to the one who holds them.
                k = Lerp(new Key(new Vector3(0, 10.0f, -9.2f), new Vector3(0, .3f, .8f), 44), new Key(new Vector3(0, 3.2f, -8.6f), Vector3.Lerp(bossMid, crownAt, .45f), 46), Smooth(s, 7, 9.6f));
            else if (s < Pinned)
                // offbeat: in front of the knight's corner, a little above: g1, e2 to its left and h3 to its right all in
                // frame, the board's left on the screen's left. It holds through the catch: the strings come down from the
                // top of the frame, from the king, onto the knight's legs.
                k = Lerp(new Key(new Vector3(1.8f, 4.1f, -8.5f), new Vector3(2.0f, .8f, -2.4f), 38), new Key(new Vector3(1.9f, 3.9f, -8.0f), new Vector3(2.0f, 1.1f, -2.4f), 36), Smooth(s, 10, Pinned));
            else if (s < Glow)
                // bind: cut on the slam, low and close on the pinned knight, the strings round its legs nailed into the board.
                k = Lerp(new Key(new Vector3(4.8f, 1.6f, -6.8f), new Vector3(2.4f, .35f, -3.2f), 36), new Key(new Vector3(4.4f, 1.4f, -6.2f), new Vector3(2.4f, .3f, -3.3f), 31), Smooth(s, Pinned + .5f, 27));
            else if (s < Lanes)
            {
                // The hand and the lit string, from the front right on a long lens so the strings run across the frame and
                // not at the lens; still through the waiting bar; after the tear, out a little to the sword.
                float fov = 30 - 4 * Smooth(s, Glow, (float)IntroTimeline.LoopStart) + 10 * Smooth(s, TearStory + 1.6f, Lanes);
                k = new Key(new Vector3(5.4f, 2.1f, -1.2f), g1 + new Vector3(0, .4f, 0), fov);
                shake = .05f * (s >= TearStory ? 1 - Smooth(s, TearStory, TearStory + 1.2f) : 0);
            }
            else if (s < Face)
                // lanes: from behind the knight up to the whole board, the lanes reaching out, the pieces lifted away.
                k = Lerp(new Key(new Vector3(2.2f, 3.4f, -7.6f), new Vector3(1.6f, .8f, -2.0f), 46), new Key(new Vector3(0, 9.0f, -12.0f), new Vector3(0, .6f, -1.0f), 50), Smooth(s, Lanes, 44));
            else
                // face: from behind the knight, the whole of it in the foreground and the king over the empty board.
                k = Lerp(new Key(new Vector3(3.6f, 2.6f, -8.0f), new Vector3(1.0f, 1.0f, 4.0f), 48), new Key(new Vector3(3.3f, 2.5f, -7.4f), new Vector3(1.0f, 1.1f, 4.0f), 45), Smooth(s, Face, Settle + 1));
            if (shake > 0) { float t = s * (float)spb; k.Pos += new Vector3(Mathf.Sin(t * 13.1f) + .4f * Mathf.Sin(t * 29.7f), Mathf.Sin(t * 17.3f + 1), 0) * shake; }

            var pos = k.Pos; var rot = Quaternion.LookRotation(k.Look - k.Pos, Vector3.up);
            float aspect = (float)target.width / target.height;
            var projection = Matrix4x4.Perspective(k.Fov, aspect, cam.nearClipPlane, cam.farClipPlane);
            // From the face shot the view settles into the battle camera, ending on its exact pose and projection.
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
        private readonly Vector3[] corners = new Vector3[4];
        private Matrix4x4 BattleProjection()
        {
            view.BoardViewRect.GetWorldCorners(corners);
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

        // ---------------------------------------------------------------- pieces on the king's strings (R2-A)
        private sealed class Piece { public Transform Root; public Vector3 Home, To; public int Ply = -1; public float Height, Delay; public LineRenderer String; }
        private readonly List<Piece> pieces = new List<Piece>();
        private LineRenderer knightString;
        private void BuildPieces()
        {
            var whiteMat = Lit(whiteMarble); var blackMat = Lit(blackMarble);
            var setup = new List<(string cell, string type)>
            { ("a1","rook"),("c1","bishop"),("d1","queen"),("f1","bishop"),("h1","rook"),("a8","rook"),("c8","bishop"),("d8","queen"),("f8","bishop"),("h8","rook") };
            foreach (char f in "abcdefgh") { setup.Add(($"{f}2", "pawn")); setup.Add(($"{f}7", "pawn")); }
            var parent = new GameObject("Pieces").transform; parent.SetParent(root, false);
            var strings = new GameObject("Piece Strings").transform; strings.SetParent(root, false);
            foreach (var (cell, type) in setup)
            {
                bool white = cell[1] >= '7';
                var home = Cell(cell);
                var piece = new Piece
                {
                    Root = MakePiece(type, white ? whiteMat : blackMat, parent), Home = home, To = home,
                    Height = PieceScale * (type == "pawn" ? .73f : type == "bishop" ? 1.17f : type == "rook" ? .93f : 1.27f),
                    // The king lifts them away one after another, in no order a player could follow.
                    Delay = Mathf.Repeat(home.x * .37f + home.z * .61f + 10, 1) * 1.6f,
                    String = MakeLine(strings, "String " + cell, 2, .012f),
                };
                for (int k = 0; k < PlyMoves.Length; k++) if (PlyMoves[k].from == cell) { piece.Ply = k; piece.To = Cell(PlyMoves[k].to); }
                piece.Root.name = (white ? "White " : "Black ") + type + " " + cell;
                pieces.Add(piece);
            }
            knightString = MakeLine(strings, "String g1 (knight)", 2, .014f);
        }
        /// <summary>Where a string meets the king, spread a little by the file it hangs over.</summary>
        private Vector3 Hand(float x) => kingHand + new Vector3(x * .12f, 0, 0);
        private void RenderPieces(float s)
        {
            // While the knight's hand and its string fill the frame, the other strings step back.
            float dim = Smooth(s, Glow, Glow + 1) * (1 - Smooth(s, TearStory + 1, TearStory + 2));
            foreach (var piece in pieces)
            {
                Vector3 p = piece.Home; float tug = 0;
                if (piece.Ply >= 0)
                {
                    // Its string draws taut half a beat before the clock; the piece is set down on the beat.
                    float land = FirstPly + piece.Ply, u = Mathf.Clamp01((s - (land - .3f)) / .3f);
                    p = Vector3.Lerp(piece.Home, piece.To, Smooth01(u)); p.y = Mathf.Sin(Mathf.PI * u) * .16f;
                    tug = Smooth(s, land - .5f, land - .3f) * (1 - Smooth(s, land, land + .5f));
                }
                // The board is the knight's now: the king takes his pieces away on their strings.
                float up = Mathf.Clamp01((s - (Withdraw + piece.Delay)) / 2.2f);
                p.y += 9 * up * up;
                piece.Root.localPosition = p;
                piece.Root.gameObject.SetActive(up < 1);
                float alpha = Mathf.Lerp(.3f, .9f, tug) * (1 - .6f * dim);
                if (up > 0) alpha = Mathf.Lerp(.3f, .7f, Smooth01(Mathf.Clamp01(up * 4))) * (1 - up);
                SetSegment(piece.String, p + Vector3.up * piece.Height, Hand(piece.Home.x), Mathf.Lerp(.012f, .022f, tug), alpha);
            }
            // The knight's own string: drawn taut with each order like the others', taut again when the king pulls the knight
            // back; it slips free on the third try.
            var knight = board.PlayerTransform.localPosition;
            Vector3 top = knight + Vector3.up * 1.05f, hand = Hand(knight.x);
            float pull = 0;
            foreach (var (from, _) in KnightOrders) pull = Mathf.Max(pull, Smooth(s, from + .1f, from + .3f) * (1 - Smooth(s, from + .5f, from + .9f)));
            foreach (float back in Backs) pull = Mathf.Max(pull, Smooth(s, back - .5f, back - .4f) * (1 - Smooth(s, back, back + .3f)));
            float free = Smooth(s, Try3, Try3 + .6f);
            float knightAlpha = Mathf.Lerp(.3f, .95f, pull) * (1 - free);
            SetSegment(knightString, Vector3.Lerp(top, hand, free * .85f), hand, Mathf.Lerp(.014f, .03f, pull), knightAlpha);
        }

        // ---------------------------------------------------------------- the king's order: the square he wants a piece on
        private Transform order;
        // Under, fill and frame, each with its share of the square's alpha.
        private (Material material, float share)[] orderParts;
        private void BuildOrder()
        {
            // A soft lit square on the board: a white frame on a dark one (so it reads on either square) round a faint fill.
            order = new GameObject("King's Order").transform; order.SetParent(root, false);
            var under = SquareMesh(.98f, .74f); var frame = SquareMesh(.9f, .82f); var fill = SquareMesh(.82f, 0);
            resources.Add(under); resources.Add(frame); resources.Add(fill);
            Material underMaterial = Flat(Color.black), fillMaterial = Flat(Color.white), frameMaterial = Flat(Color.white);
            orderParts = new[] { (underMaterial, .5f), (fillMaterial, .16f), (frameMaterial, .85f) };
            var u = MakeMesh(order, "Under", under, underMaterial);
            var f = MakeMesh(order, "Fill", fill, fillMaterial); f.transform.localPosition = Vector3.up * .002f;
            var l = MakeMesh(order, "Frame", frame, frameMaterial); l.transform.localPosition = Vector3.up * .004f;
            u.GetComponent<MeshRenderer>().sortingOrder = 2; f.GetComponent<MeshRenderer>().sortingOrder = 3; l.GetComponent<MeshRenderer>().sortingOrder = 3;
            order.gameObject.SetActive(false);
        }
        /// <summary>
        /// Every move the king orders is shown the same way: the square lights half a beat before the piece is set down on it.
        /// The knight's is h3, to its right; it goes left instead.
        /// </summary>
        private void RenderOrder(float s)
        {
            float a = 0; Vector3 at = Vector3.zero;
            for (int k = 0; k < Plies; k++)
            {
                float land = FirstPly + k, w = Smooth(s, land - .5f, land - .4f) * (1 - Smooth(s, land + .05f, land + .3f));
                if (w > a) { a = w; at = Cell(PlyMoves[k].to); }
            }
            foreach (var (from, to) in KnightOrders)
            {
                float w = Smooth(s, from, from + .2f) * (1 - Smooth(s, to - .2f, to));
                if (w > a) { a = w; at = Cell("h3"); }
            }
            order.gameObject.SetActive(a > .01f); if (a <= .01f) return;
            order.localPosition = at + Vector3.up * .014f;
            foreach (var (material, share) in orderParts) SetAlpha(material, a * share);
        }

        // ---------------------------------------------------------------- the knight
        /// <summary>The knight's L to the left, g1 to e2: two squares across, then the turn of the L in the air, one square up.</summary>
        private static Vector3 LeftL(float u, float height)
        {
            Vector3 g1 = Cell("g1"), e2 = Cell("e2");
            float across = Smooth01(Mathf.Clamp01(u / .62f)), up = Smooth01(Mathf.Clamp01((u - .62f) / .38f));
            return new Vector3(Mathf.Lerp(g1.x, e2.x, across), height * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)), Mathf.Lerp(g1.z, e2.z, up));
        }
        /// <summary>u from 0 to 1 while <paramref name="s"/> is in [a, b), else -1.</summary>
        private static float Span(float s, float a, float b) => s >= a && s < b ? (s - a) / (b - a) : -1;
        // The third try: higher and slower, caught at its top.
        private const float Try3Beats = 1.1f, Try3Height = 1.45f;
        private void RenderKnight(float s)
        {
            Vector3 g1 = Cell("g1"), e2 = Cell("e2");
            Vector3 p = g1; float yaw = 0, tilt = 0, squash = 1, u;
            const float left = -80;
            if (s < Caught)
            {
                bool home = false;
                if ((u = Span(s, Try1 - .5f, Try1)) >= 0) { p = LeftL(u, 1.0f); yaw = left * Smooth01(Mathf.Clamp01(u * 3)); squash = u < .2f ? 1.1f : 1; }
                else if ((u = Span(s, Try1, Back1 - .45f)) >= 0) { p = e2; yaw = left; squash = 1 - .12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 2.5f)); }
                // Put back: hauled up by its string and set down on its square, on the beat.
                else if ((u = Span(s, Back1 - .45f, Back1)) >= 0) { p = Vector3.Lerp(e2, g1, Smooth01(u)); p.y = .7f * Mathf.Sin(Mathf.PI * u); yaw = left * (1 - u); tilt = 16 * Mathf.Sin(Mathf.PI * u); }
                else if ((u = Span(s, Try2 - .45f, Try2)) >= 0) { p = LeftL(u, 1.0f); yaw = left * Smooth01(Mathf.Clamp01(u * 3)); squash = u < .2f ? 1.1f : 1; }
                else if ((u = Span(s, Try2, Back2 - .4f)) >= 0) { p = e2; yaw = left; squash = 1 - .12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 2.5f)); }
                // The second time rougher: higher, and spun.
                else if ((u = Span(s, Back2 - .4f, Back2)) >= 0) { p = Vector3.Lerp(e2, g1, Smooth01(u)); p.y = 1.0f * Mathf.Sin(Mathf.PI * u); yaw = left * (1 - u) + 40 * Mathf.Sin(Mathf.PI * u); tilt = 24 * Mathf.Sin(Mathf.PI * u); }
                else if ((u = Span(s, Try3, Caught)) >= 0) { p = LeftL(u * (Caught - Try3) / Try3Beats, Try3Height); yaw = left * Smooth01(Mathf.Clamp01(u * 2)); squash = u < .15f ? 1.12f : 1; tilt = -10 * u; }
                else home = true;
                // On its square between tries, the king's order lit to its right: it gathers itself to go left.
                if (home)
                    foreach (float next in TryGathers)
                        if (s < next) squash = Mathf.Min(squash, 1 - .1f * Smooth(s, next - .2f, next));
            }
            else if (s < Pinned)
            {
                // Caught by the legs at the top of the jump: a jolt, then dragged down to its square and set down hard on the beat.
                var from = LeftL((Caught - Try3) / Try3Beats, Try3Height);
                u = Mathf.Clamp01((s - Caught - .15f) / (Pinned - Caught - .15f));
                p = Vector3.Lerp(from, g1, u * u * u);
                p.y = Mathf.Lerp(from.y, 0, u * u);
                if (s < Caught + .15f) p.y += .12f * Mathf.Sin(Mathf.PI * (s - Caught) / .15f);
                yaw = Mathf.Lerp(left, 0, u); tilt = 18 * (1 - u);
            }
            else if (s < TearStory)
            {
                // Pinned low: pressed down to the board, crouched, until it takes the lit string in its hand.
                squash = Mathf.Min(1 - .22f * Mathf.Sin(Mathf.PI * Mathf.Clamp01((s - Pinned) / .45f)), Mathf.Lerp(1, .86f, Smooth(s, Pinned + .2f, Pinned + .6f)));
                foreach (float t in Struggles)
                {
                    u = (s - t) / .8f;
                    if (u > 0 && u < 1) { p.y += Mathf.Sin(Mathf.PI * u) * .12f; tilt = Mathf.Sin(u * Mathf.PI * 3) * 8; }
                }
                // Rising a little as it grips the lit string; the tremble repeats with the waiting bar (6 a beat).
                if (s >= Glow) { tilt = -6 * Smooth(s, Glow, Glow + 1) + Mathf.Sin(s * Mathf.PI * 12) * .8f; squash = Mathf.Lerp(.86f, .94f, Smooth(s, Glow, Glow + 1.5f)); }
            }
            else if (s < Face)
            {
                u = Mathf.Clamp01((s - TearStory) / .4f);
                tilt = Mathf.Lerp(-6, 0, Smooth(s, TearStory + .4f, TearStory + 1.4f)) + (u < 1 ? 10 * Mathf.Sin(Mathf.PI * u) : 0);
                squash = Mathf.Lerp(.94f, 1, Smooth(s, TearStory, TearStory + .6f));
            }
            var knight = board.PlayerTransform;
            knight.localPosition = p;
            knight.localRotation = Quaternion.Euler(0, yaw, tilt);
            knight.localScale = new Vector3(1, squash, 1);
        }
        private float JerkAmount(float s) { float j = 0; foreach (float t in Struggles) { float u = (s - t) / .8f; if (u > 0 && u < 1) j = Mathf.Max(j, Mathf.Sin(Mathf.PI * u)); } return j; }

        // ---------------------------------------------------------------- the binding strings, the sword and the lanes
        private LineRenderer[] strings, stringUnders, lanes;
        private Transform[] pins;
        // Each pin's dark ring and white head, faded together (the ring at .6).
        private Material[] pinUnders, pinHeads;
        // One string's points, shared by the string and its dark under-line.
        private readonly Vector3[] stringPoints = new Vector3[Segments + 1];
        private LineRenderer blade;
        private Transform sword;
        private Vector3[] kingBase, anchors;
        private const int Segments = 32;
        private static readonly float[] LaneZ = { -1.6f, -.8f, 0, .8f, 1.6f };
        /// <summary>The story beat lane <paramref name="j"/> starts out from the board (the lowest first).</summary>
        private static float LaneStart(int j) => Lanes + (4 - j) * .55f;
        private void BuildStrings()
        {
            var parent = new GameObject("Strings").transform; parent.SetParent(root, false);
            strings = new LineRenderer[6]; stringUnders = new LineRenderer[6]; pins = new Transform[6];
            pinUnders = new Material[6]; pinHeads = new Material[6];
            for (int i = 0; i < 6; i++)
            {
                // A dark line under each string, so it reads on the white squares as on the black.
                stringUnders[i] = MakeLine(parent, "String Under " + (i + 1), Segments + 1, .08f);
                strings[i] = MakeLine(parent, "String " + (i + 1), Segments + 1, .045f);
                stringUnders[i].sortingOrder = 3; strings[i].sortingOrder = 4;
                pins[i] = MakePin(parent, "Pin " + (i + 1), out pinUnders[i], out pinHeads[i]);
            }
            // Where the six strings come from (under the king) and where they pin the knight to the board around g1. #6 (index
            // 5) is the lowest, by the knight's hand: the one it tears.
            kingBase = new Vector3[6]; for (int i = 0; i < 6; i++) kingBase[i] = new Vector3(-1.5f + .6f * i, .45f, 5.6f);
            // Close round g1 and low, so the strings press the knight down by its legs.
            anchors = new[] { new Vector3(1.3f, .02f, -3.3f), new Vector3(1.6f, .02f, -2.4f), new Vector3(2.4f, .02f, -2.2f),
                new Vector3(3.3f, .02f, -2.5f), new Vector3(1.9f, .02f, -3.95f), new Vector3(3.6f, .02f, -3.4f) };
            for (int i = 0; i < 6; i++) pins[i].localPosition = anchors[i];
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
            // Tied at the legs; the hand stays free for the lit string.
            Vector3 attach = knight + Vector3.up * .15f, hand = knight + new Vector3(.18f, .62f, -.05f);
            float jerk = JerkAmount(s), glow = Smooth(s, Glow, Glow + 1.5f);
            for (int i = 0; i < 6; i++)
            {
                var line = strings[i];
                bool torn = i == 5, hidden = s < Caught || (torn && s >= TearStory + 1.5f) || (!torn && s >= LaneStart(i) + .6f);
                line.gameObject.SetActive(!hidden); stringUnders[i].gameObject.SetActive(!hidden);
                if (hidden) { pins[i].gameObject.SetActive(false); continue; }
                Vector3 a, b; float amp;
                if (s < Caught + .4f)
                {
                    // Shot from the king at the knight in the air.
                    float u = 1 - Mathf.Pow(1 - Mathf.Clamp01((s - Caught) / .4f), 3);
                    b = kingBase[i]; a = Vector3.Lerp(kingBase[i], attach, u); amp = .06f * (1 - u) + .02f;
                }
                else { a = attach; b = Vector3.Lerp(kingBase[i], anchors[i], Smooth(s, Pinned + .1f, Pinned + 1.1f)); amp = .015f; }
                amp += .06f * jerk;
                float alpha = .95f, width, pin = Smooth(s, Pinned + .9f, Pinned + 1.1f);
                if (torn)
                {
                    width = Mathf.Lerp(.045f, .075f, glow);
                    // Lit, the knight takes it up from its legs into its hand and holds it taut.
                    a = Vector3.Lerp(a, hand, glow);
                    if (s >= Glow && s < TearStory) amp = .05f + .03f * Mathf.Sin(s * Mathf.PI * 10);
                    else if (s >= TearStory)
                    {
                        // Torn: it whips up into the knight's hand and straightens into the blade.
                        float u = Mathf.Clamp01((s - TearStory) / 1.5f);
                        b = Vector3.Lerp(anchors[5], hand + Vector3.up * .9f, 1 - Mathf.Pow(1 - u, 3)); amp = .08f * (1 - u);
                        width = Mathf.Lerp(.075f, .035f, u);
                        pin *= 1 - Smooth(s, TearStory, TearStory + .2f);
                    }
                }
                else
                {
                    // The lit string is the brightest thing on screen; the others step back until the tear.
                    float back = glow * (1 - .5f * Smooth(s, TearStory, TearStory + 1));
                    alpha = Mathf.Lerp(.95f, .3f, back); width = Mathf.Lerp(.045f, .032f, glow);
                    // Then each lets go of the knight and the board and lies straight across the board as its lane.
                    float start = LaneStart(i), slide = Smooth(s, start - .5f, start);
                    a = Vector3.Lerp(a, new Vector3(4.45f, .03f, LaneZ[i]), slide);
                    b = Vector3.Lerp(b, new Vector3(-4.45f, .03f, LaneZ[i]), slide);
                    amp *= 1 - slide;
                    alpha *= 1 - Smooth(s, start, start + .6f);
                    pin *= 1 - Smooth(s, start - .5f, start - .3f);
                }
                // Four shivers a beat, so the waiting bar repeats without a jump.
                float time = s * Mathf.PI * 8;
                StringCurve(stringPoints, a, b, amp, i * 1.7f, time);
                line.SetPositions(stringPoints); SetLineColor(line, new Color(1, 1, 1, alpha));
                line.widthMultiplier = width;
                stringUnders[i].SetPositions(stringPoints); SetLineColor(stringUnders[i], new Color(0, 0, 0, alpha * .55f));
                stringUnders[i].widthMultiplier = width + .035f;
                SetPin(i, pin);
            }
            // The lanes start out from the board's edges, lowest first; they fade into the lane art at the handover.
            float fadeOut = 1 - Smooth(s, Handover + .6f, End - .2f);
            for (int i = 0; i < 10; i++)
            {
                int j = i % 5; float side = i < 5 ? -1 : 1;
                float length = 30 * (1 - Mathf.Pow(1 - Mathf.Clamp01((s - LaneStart(j)) / 2.6f), 3));
                var lane = lanes[i];
                lane.gameObject.SetActive(length > .01f && fadeOut > .01f); if (!lane.gameObject.activeSelf) continue;
                lane.SetPosition(0, new Vector3(side * 4.45f, .03f, LaneZ[j]));
                lane.SetPosition(1, new Vector3(side * (4.45f + length), .03f, LaneZ[j]));
                SetLineColor(lane, new Color(1, 1, 1, (j == 2 ? .8f : .5f) * fadeOut));
            }
            // The torn string becomes the sword in the knight's hand: the string straightens into the blade from the hand out,
            // with a white flash along it. Facing the king it is raised at him; it is put away for the battle.
            float raise = Smooth(s, Face, Face + 1.2f) * (1 - Smooth(s, Settle, Settle + 1.5f));
            var pose = Quaternion.AngleAxis(50 * raise, Vector3.right) * Quaternion.AngleAxis(-14, Vector3.forward);
            Vector3 up = pose * Vector3.up;
            float drawn = Smooth(s, TearStory + 1.35f, TearStory + 1.75f) * (1 - Smooth(s, Handover, Handover + .8f));
            if (sword != null)
            {
                sword.gameObject.SetActive(drawn > .001f);
                sword.localPosition = hand; sword.localRotation = pose;
                sword.localScale = new Vector3(1, drawn, 1);
            }
            // Without the model the line is the blade; with it, the line is only the flash of the string becoming it.
            float glint = sword == null ? (s >= TearStory + 1.4f ? Smooth(s, TearStory + 1.3f, TearStory + 1.6f) * (1 - Smooth(s, Handover, Handover + .8f)) : 0)
                : Smooth(s, TearStory + 1.3f, TearStory + 1.5f) * (1 - Smooth(s, TearStory + 1.6f, TearStory + 2.4f));
            blade.gameObject.SetActive(glint > .001f);
            if (glint > .001f) { blade.SetPosition(0, hand); blade.SetPosition(1, hand + up * .95f); SetLineColor(blade, new Color(1, 1, 1, glint)); }
        }

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
            if (bossQuad == null) { crownAt = new Vector3(0, 6, 7); bossMid = new Vector3(0, 3, 7); kingHand = Vector3.Lerp(bossMid, crownAt, .2f); return; }
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
            // The strings all meet a little above the middle of the king, a little in front of him.
            kingHand = Vector3.Lerp(bossMid, crownAt, .2f) + new Vector3(0, 0, -.4f);
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
                // The crown swells with every pull of a string.
                crownQuad.localScale = Vector3.one * (1 + .035f * OrderPulse(s));
            }
        }
        private static float OrderPulse(float s)
        {
            float v = 0;
            for (int k = 0; k < Plies; k++) { float t = FirstPly + k - .5f; if (s >= t) v = Mathf.Max(v, Mathf.Exp(-(s - t) * 4)); }
            foreach (var (from, _) in KnightOrders) if (s >= from) v = Mathf.Max(v, Mathf.Exp(-(s - from) * 4));
            foreach (float t in OrderEchoes) if (s >= t) v = Mathf.Max(v, Mathf.Exp(-(s - t) * 4));
            return v;
        }

        // ---------------------------------------------------------------- noise (R4-A)
        private Transform spark;
        private readonly Transform[] fragments = new Transform[5];
        // Where the discarded noise settles in the face shot: between the knight and the king, the first (g3) where the
        // tutorial's noise lesson puts its own.
        private static readonly string[] FragmentCells = { "g3", "d4", "f5", "b6", "e7" };
        // Up and away over the board into the dark, out of the top of the bind shot.
        private static readonly Vector3 Discarded = new Vector3(-1.5f, 3.5f, 3.0f);
        private void BuildNoise()
        {
            var parent = new GameObject("Noise").transform; parent.SetParent(root, false);
            Texture texture = view.Art.noiseSprite != null ? view.Art.noiseSprite.texture : null;
            spark = MakeNoise(parent, texture);
            for (int i = 0; i < fragments.Length; i++) fragments[i] = MakeNoise(parent, texture);
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
            var knight = board.PlayerTransform.localPosition;
            var offset = new Vector3(.55f, 1.0f, 0);
            // Born of the knight's off-beat landing and carried with it; torn off it when it is pinned and thrown into the dark.
            if (s >= Try2 && s < Pinned)
            {
                float burst = Mathf.Lerp(1.5f, 1.1f, Smooth(s, Try2, Try2 + .5f)) + .4f * Smooth(s, Try3, Try3 + .2f) * (1 - Smooth(s, Try3 + .3f, Caught));
                Billboard(spark, knight + offset, burst, Smooth(s, Try2, Try2 + .2f));
            }
            else if (s >= Pinned && s < Pinned + 1.8f)
            {
                float u = Mathf.Clamp01((s - Pinned) / 1.8f);
                var p = Vector3.Lerp(Cell("g1") + offset, Discarded, 1 - (1 - u) * (1 - u));
                float flicker = .75f + .25f * Mathf.Sin(s * 60);
                Billboard(spark, p, Mathf.Lerp(1.1f, .6f, u), (1 - Smooth(s, Pinned + 1, Pinned + 1.8f)) * flicker);
            }
            else Billboard(spark, Vector3.zero, 1, 0);
            // The discarded pieces of it come down out of the dark and settle on the board; at the handover they sink into it.
            float sink = Smooth(s, Handover, Handover + 1.4f);
            for (int i = 0; i < fragments.Length; i++)
            {
                float start = Face + .4f + .5f * i, u = Mathf.Clamp01((s - start) / 2.4f);
                if (s < start) { Billboard(fragments[i], Vector3.zero, 1, 0); continue; }
                var cell = Cell(FragmentCells[i]);
                var from = cell + new Vector3(1.2f - .5f * i, 4.2f, .6f);
                var p = Vector3.Lerp(from, cell + Vector3.up * .45f, 1 - (1 - u) * (1 - u));
                p.x += Mathf.Sin(s * 2.1f + i) * .15f * (1 - u);
                p.y += .06f * Mathf.Sin(s * 2.2f + i) - .5f * sink;
                Billboard(fragments[i], p, .75f, Smooth(s, start, start + .5f) * (1 - sink));
            }
        }

        // ---------------------------------------------------------------- canvas layer: view, void, letterbox, prompt
        private void BuildLayer()
        {
            layer = PrototypeView.MakeRect(view.CanvasRect, "Prologue Layer", Vector2.zero, Vector2.zero); Stretch(layer);
            voidCover = FullScreen("Void", Color.black);
            // The whole-board world (A5-B), above the void; without it the void alone stands for it.
            if (view.Art.HasPrologueBackground)
            {
                var whole = PrototypeView.MakeRect(layer, "Whole Board", Vector2.zero, Vector2.zero); Stretch(whole);
                wholeBoard = PrototypeView.BuildBackgroundLayers(whole, view.Art.prologueBackgroundLayers, "Whole Board Layer");
            }
            introView = PrototypeView.MakeRect(layer, "Prologue View", Vector2.zero, Vector2.zero).gameObject.AddComponent<UnityEngine.UI.RawImage>();
            Stretch(introView.rectTransform); introView.raycastTarget = false;
            flash = FullScreen("Crack Flash", Color.white);
            barTop = Bar("Letterbox Top", 1); barBottom = Bar("Letterbox Bottom", 0);
            var art = view.Art;
            var font = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont, view.LatinFont);
            BuildPrompt();
            skipHintGroup = Letterbox.MakeSkipHint(barBottom.rectTransform, font, art.skipKeyIcon);
            contentGroup = Group(view.ContentRect);
            if (view.BossPortrait != null) bossGroup = Group((RectTransform)view.BossPortrait.transform);
            layer.gameObject.SetActive(false);
        }
        /// <summary>
        /// The J prompt on a card like the pause screen's, so it reads over any part of the board. 5 px in the 1920 frame is
        /// about 4 on this 1600 canvas.
        /// </summary>
        private void BuildPrompt()
        {
            prompt = new KeyPromptCard(layer, "Prompt", view.Art, view.LatinFont, 84, 40, 36);
            // The J of '현을 뜯는다' is the delivered key art (ArtSet.promptJKeyIcon); without it, the plain keycap.
            prompt.SetKeyIcon("J", view.Art.promptJKeyIcon);
        }
        private void RenderLayer(float s)
        {
            // The board is whole and the world around it empty until the string tears; then it cracks into the battle's
            // world of floating debris (A5-B; placeholder until the "whole board" background set exists).
            SetAlpha(voidCover, 1 - Smooth(s, TearStory, TearStory + 1.6f));
            // Under the flash the whole board gives way layer by layer, front layers first and the opaque back last, to the
            // battle's broken twins underneath.
            for (int i = 0; wholeBoard != null && i < wholeBoard.Length; i++)
            {
                if (wholeBoard[i] == null) continue;
                float start = TearStory + .12f * (wholeBoard.Length - 1 - i);
                SetAlpha(wholeBoard[i], 1 - Smooth(s, start, start + 1.2f));
            }
            SetAlpha(flash, s >= TearStory ? .75f * Mathf.Exp(-(s - TearStory) * 3.5f) : 0);
            // The letterbox is the king's board, with no control. It lifts on the handover.
            // With the tutorial next the bars stay: its letterbox takes over at the same height and narrows from there.
            float bars = s < Handover || game.TutorialFollowsPrologue ? Smooth(s, 0, 2) : 1 - Smooth(s, Handover, Handover + 1.5f);
            var size = layer.rect.size;
            float bar = Mathf.Max((size.y - size.x / CutscenePlayer.Aspect) / 2, size.y * .06f);
            barTop.rectTransform.sizeDelta = barBottom.rectTransform.sizeDelta = new Vector2(0, bar);
            barTop.rectTransform.anchoredPosition = new Vector2(0, bar * (1 - bars));
            barBottom.rectTransform.anchoredPosition = new Vector2(0, -bar * (1 - bars));
            skipHintGroup.alpha = Letterbox.SkipHintOpacity * bars;
            // The handover: the battle's own picture comes up over one beat while the prologue's goes.
            float handover = Smooth(s, Handover + 1, End - .15f);
            board.BoardCamera.enabled = handover > 0;
            SetGroups(handover);
            introView.color = new Color(1, 1, 1, 1 - handover);
            board.CellOutline.gameObject.SetActive(handover > .5f);
            // The one prompt: J, while the loop waits for it (R7-A: the only words in the prologue).
            bool waiting = s >= IntroTimeline.LoopStart && s < TearStory && !Torn;
            if (!waiting) prompt.Hide();
            else
            {
                if (!prompt.Visible) prompt.Show("J", "현을 뜯는다");
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

        // ---------------------------------------------------------------- sound (R6-A: five sounds and silence, on the beat)
        /// <summary>
        /// A sound on a beat: a clip of <see cref="PrologueDefinition"/> by name, at a level. A cue with <see cref="Unless"/>
        /// stands in for that clip: it plays only while that clip is not delivered.
        /// </summary>
        public struct Cue
        {
            public float Beat; public string Clip; public float Volume; public string Unless;
            public Cue(float b, string c, float v = 1, string unless = null) { Beat = b; Clip = c; Volume = v; Unless = unless; }
        }
        /// <summary>The noise's crackle at its level: about as loud as a piece set down.</summary>
        public const float NoiseVolume = .3f;
        /// <summary>A piece set down hard: the knight pinned to its square.</summary>
        public const float HeavyPlace = 1.8f;
        /// <summary>Section A's sounds, on story beats 0-32 (the guide tracks render these too).</summary>
        public static List<Cue> SectionACues()
        {
            var cues = new List<Cue>();
            // The king's beat is a chess clock (S2) on every beat: the knight's off-beat tries are only heard against it.
            for (int b = 0; b < IntroTimeline.LoopStart; b++) cues.Add(new Cue(b, PrologueDefinition.ClockName, b % 4 == 0 ? .45f : .3f));
            // A piece set down on each beat 4-9; the knight's first try (11) and the king putting it back (12, 14) on the beat too.
            for (int k = 0; k < Plies; k++) cues.Add(new Cue(FirstPly + k + .02f, PrologueDefinition.PlaceName));
            foreach (float t in new[] { Try1, Back1, Back2 }) cues.Add(new Cue(t + .02f, PrologueDefinition.PlaceName));
            // The second try lands between the beats, and the noise is born of it; nothing is set down on 13 or 15.
            cues.Add(new Cue(Try2, PrologueDefinition.NoiseName, NoiseVolume));
            cues.Add(new Cue(Try2 + .02f, PrologueDefinition.PlaceName));
            cues.Add(new Cue(Try3, PrologueDefinition.NoiseName, NoiseVolume * 1.4f));
            cues.Add(new Cue(Caught + .2f, PrologueDefinition.StrainName));
            cues.Add(new Cue(Pinned + .02f, PrologueDefinition.PlaceName, HeavyPlace));
            foreach (float t in Struggles) cues.Add(new Cue(t, PrologueDefinition.StrainName));
            cues.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            return cues;
        }
        /// <summary>One waiting bar's sounds, in beats from its bar line.</summary>
        public static List<Cue> WaitBarCues()
        {
            var cues = new List<Cue> { new Cue(0, PrologueDefinition.StrainName) };
            for (int b = 0; b < IntroTimeline.LoopBeats; b++) cues.Add(new Cue(b, PrologueDefinition.ClockName, b == 0 ? .45f : .3f));
            cues.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            return cues;
        }
        /// <summary>Section B's sounds, in beats after the tear (20 beats to the handover).</summary>
        public static List<Cue> SectionBCues()
        {
            // The pluck, then silence while the string becomes the sword; the clock comes back with the lanes (story 40).
            var cues = new List<Cue>
            {
                new Cue(0, PrologueDefinition.PluckName),
                new Cue(0, PrologueDefinition.NoiseName, NoiseVolume, PrologueDefinition.PluckName),
                new Cue(.02f, PrologueDefinition.PlaceName, HeavyPlace, PrologueDefinition.PluckName),
            };
            for (int b = (int)(Lanes - TearStory); b < IntroTimeline.AfterTear; b++)
                cues.Add(new Cue(b, PrologueDefinition.ClockName, (TearStory + b) % 4 == 0 ? .45f : .3f));
            cues.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            return cues;
        }
        private readonly List<Cue> before = new List<Cue>(), after = new List<Cue>(), waitBar = new List<Cue>();
        private readonly List<AudioSource> sources = new List<AudioSource>(), loopSources = new List<AudioSource>();
        private int nextEvent, nextBar, nextAfter, nextSource;
        private bool handoverTick;
        private void BuildAudio()
        {
            for (int i = 0; i < 24; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
                sources.Add(source);
            }
            // The composed cue (S1-B): a source of its own for each piece; two take turns for the waiting bar so a bar's tail
            // is not cut by the next. It plays under the sounds.
            stemA = StemSource(data.cueA); stemB = StemSource(data.cueB);
            if (data.cueWait != null) waitSources = new[] { StemSource(data.cueWait), StemSource(data.cueWait) };
            foreach (var clip in new[] { data.Tick(false), data.Tick(true) }) if (clip != null) clip.LoadAudioData();
            before.AddRange(SectionACues());
            after.AddRange(SectionBCues());
            waitBar.AddRange(WaitBarCues());
        }
        private AudioSource stemA, stemB;
        private AudioSource[] waitSources;
        private int nextWait;
        private IEnumerable<AudioSource> Stems()
        {
            yield return stemA; yield return stemB;
            if (waitSources != null) foreach (var w in waitSources) yield return w;
        }
        private AudioSource StemSource(AudioClip clip)
        {
            if (clip == null) return null;
            clip.LoadAudioData();
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
            return source;
        }
        /// <summary>
        /// A composed piece whose first sample belongs on <paramref name="at"/> (DSP time). Late, it starts now from the
        /// matching sample, so it still ends where it should (on the song's start, for section B).
        /// </summary>
        private void PlayStem(AudioSource source, AudioClip clip, double at, double now)
        {
            if (source == null || clip == null) return;
            if (now <= 0) now = AudioSettings.dspTime;
            source.Stop(); source.clip = clip;
            source.volume = data.cueVolume * GameFlow.MusicVolume;
            double start = System.Math.Max(at, now + .02);
            int skip = (int)((start - at) * clip.frequency);
            if (skip >= clip.samples) return;
            source.timeSamples = skip;
            source.PlayScheduled(start);
        }
        /// <summary>Tests: the composed pieces' sources (null where none is assigned).</summary>
        internal AudioSource StemASource => stemA;
        internal AudioSource StemBSource => stemB;
        internal AudioSource[] WaitSources => waitSources;
        /// <summary>Tests: the sounds' sources.</summary>
        internal IReadOnlyList<AudioSource> PoolSources => sources;
        /// <summary>Tests: the DSP time of intro beat 0.</summary>
        internal double OriginDsp => origin;
        /// <summary>Tests: the intro beat the string tore on (-1 before).</summary>
        internal double TearIntroBeat => tearIntroBeat;
        private void ScheduleAudio(double now)
        {
            const double ahead = .25;
            if (!Torn)
            {
                while (nextEvent < before.Count && origin + before[nextEvent].Beat * spb <= now + ahead)
                { Play(before[nextEvent], origin + before[nextEvent].Beat * spb, now, null); nextEvent++; }
                // The waiting bar: the clock on each beat and the strained string, again and again until J.
                double bar = origin + (IntroTimeline.LoopStart + nextBar * IntroTimeline.LoopBeats) * spb;
                if (bar <= now + ahead)
                {
                    if (waitSources != null)
                    {
                        var wait = waitSources[nextWait++ % waitSources.Length];
                        PlayStem(wait, data.cueWait, bar, now);
                        if (!loopSources.Contains(wait)) loopSources.Add(wait);
                    }
                    foreach (var cue in waitBar) Play(cue, bar + cue.Beat * spb, now, loopSources);
                    nextBar++;
                }
            }
            else
            {
                double tear = origin + tearIntroBeat * spb;
                while (nextAfter < after.Count && tear + after[nextAfter].Beat * spb <= now + ahead)
                { Play(after[nextAfter], tear + after[nextAfter].Beat * spb, now, null); nextAfter++; }
                // With the tutorial next, its chess clock begins with an accent on the handover downbeat. The tutorial only
                // starts on the frame after that beat, too late to schedule it, so the prologue puts it there (it rings on).
                if (!handoverTick && game.TutorialFollowsPrologue && HandoverDsp <= now + ahead)
                { Play(new Cue(0, PrologueDefinition.ClockName, .45f), HandoverDsp, now, null); handoverTick = true; }
            }
        }
        private void Play(Cue cue, double at, double now, List<AudioSource> track)
        {
            if (cue.Unless != null && data.Clip(cue.Unless) != null) return;
            // The clock is the recorded take when delivered (S2-A): an accent on the bar line (the louder placeholder cue).
            bool clock = PrologueDefinition.IsClock(cue.Clip), accent = clock && cue.Volume >= .45f;
            var clip = clock ? data.Tick(accent) : data.Clip(cue.Clip);
            if (clip == null) return;
            var source = sources[nextSource]; nextSource = (nextSource + 1) % sources.Count;
            source.Stop(); source.clip = clip;
            source.volume = data.volume * GameFlow.MusicVolume * (clock ? data.TickVolume(accent) : cue.Volume);
            source.PlayScheduled(System.Math.Max(at, now + .01));
            if (track != null && !track.Contains(source)) track.Add(source);
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
            var material = quad.GetComponent<MeshRenderer>().sharedMaterial;
            var c = material.color; c.a = alpha; material.color = c;
        }
        private void SetPin(int i, float alpha)
        {
            pins[i].gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            SetAlpha(pinUnders[i], alpha * .6f); SetAlpha(pinHeads[i], alpha);
        }
        private static void SetAlpha(Material material, float alpha) { var c = material.color; c.a = alpha; material.color = c; }
        // Flat shapes lie on the board (y up). Built in the XZ plane.
        private static Mesh FlatMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            var uv = new Vector2[vertices.Length]; mesh.uv = uv;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
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
        /// <summary>A square (inner 0) or a square frame lying on the board, centred, sides in board units.</summary>
        private static Mesh SquareMesh(float outer, float inner)
        {
            float h = outer / 2, i = inner / 2;
            if (inner <= 0)
                return FlatMesh("Square", new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) }, new[] { 0, 1, 2, 0, 2, 3 });
            return FlatMesh("Frame", new[] {
                new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h),
                new Vector3(-i, 0, -i), new Vector3(-i, 0, i), new Vector3(i, 0, i), new Vector3(i, 0, -i) },
                new[] { 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 });
        }
        /// <summary>Where a string is nailed into the board: a white head on a dark ring, so it reads on either square.</summary>
        private Transform MakePin(Transform parent, string name, out Material underMaterial, out Material headMaterial)
        {
            var pin = new GameObject(name).transform; pin.SetParent(parent, false);
            var under = DiscMesh(.12f); var head = DiscMesh(.07f);
            resources.Add(under); resources.Add(head);
            var u = MakeMesh(pin, "Under", under, underMaterial = Flat(Color.black)); u.transform.localPosition = Vector3.up * .012f;
            var h = MakeMesh(pin, "Head", head, headMaterial = Flat(Color.white)); h.transform.localPosition = Vector3.up * .016f;
            u.GetComponent<MeshRenderer>().sortingOrder = 2; h.GetComponent<MeshRenderer>().sortingOrder = 3;
            pin.gameObject.SetActive(false);
            return pin;
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
        private static void SetLineColor(LineRenderer line, Color c) { line.startColor = line.endColor = c; }
        /// <summary>A straight string from <paramref name="a"/> to <paramref name="b"/>, hidden when it is all but transparent.</summary>
        private static void SetSegment(LineRenderer line, Vector3 a, Vector3 b, float width, float alpha)
        {
            line.gameObject.SetActive(alpha > .01f); if (alpha <= .01f) return;
            line.SetPosition(0, a); line.SetPosition(1, b);
            line.widthMultiplier = width;
            SetLineColor(line, new Color(1, 1, 1, alpha));
        }
        private static void StringCurve(Vector3[] points, Vector3 a, Vector3 b, float amp, float phase, float time)
        {
            var across = Vector3.Cross((b - a).normalized, Vector3.up).normalized;
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments, w = Mathf.Sin(Mathf.PI * u) * amp * Mathf.Sin(time + phase + u * 9);
                points[i] = Vector3.Lerp(a, b, u) + across * w + Vector3.up * (w * .7f);
            }
        }

        // Chess pieces: the previz silhouettes, turned on a lathe, in marble (A3-A), three quarters the size (A2-C).
        private Mesh pieceBall, pieceBox;
        private Transform MakePiece(string type, Material material, Transform parent)
        {
            var item = new GameObject(type).transform; item.SetParent(parent, false);
            void Add(Mesh mesh, Vector3 at, Vector3 scale) { if (!resources.Contains(mesh)) resources.Add(mesh); var g = MakeMesh(item, type, mesh, material); g.transform.localPosition = at; g.transform.localScale = scale; g.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
            if (pieceBall == null) { pieceBall = Lathe(Semicircle(1)); pieceBox = Box(); resources.Add(pieceBall); resources.Add(pieceBox); }
            Mesh ball = pieceBall;
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
                    for (int i = 0; i < 4; i++) { float a = (i * 90 + 45) * Mathf.Deg2Rad; Add(pieceBox, new Vector3(Mathf.Cos(a) * .2f, .87f, Mathf.Sin(a) * .2f), Vector3.one * .11f); }
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
