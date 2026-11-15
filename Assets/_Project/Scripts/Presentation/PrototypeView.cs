using System;
using System.Text;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using StringAsASword.Rhythm;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace StringAsASword.Presentation
{
    public sealed class PrototypeView : MonoBehaviour
    {
        public static readonly Color InkWhite = new Color(.94f, .93f, .88f);
        public static readonly Color Muted = new Color(.58f, .62f, .66f);
        public static readonly Color Accent = new Color(.78f, .91f, .86f);
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private Material boardMaterial;
        [SerializeField] private ArtSet art;
        [Tooltip("Shows the prototype's full text HUD (tempo, controls, stats, telegraph list) for balancing work.")]
        [SerializeField] private bool detailedHud;
        /// <summary>Board view size on the 1600x900 canvas; keeps the board render texture's 960:880 aspect.</summary>
        public static readonly Vector2 BoardSize = new Vector2(800, 733);
        private static readonly Vector2 BoardCenter = new Vector2(0, -60);
        private const float StringGap = 34, ScreenEdge = 820;
        // Label and Bar build into this layer: the root, or the detail layer that is hidden by default.
        private RectTransform layer;
        private UnityEngine.UI.Image[] judgeLines;
        // White glow behind the delivered lane art's judge lines; null with the prototype lanes.
        private UnityEngine.UI.Image[] judgeGlows;
        private const float JudgeGlowWidth = 44;
        private static Sprite glowSprite;
        public Material BoardMaterial => boardMaterial;
        public ArtSet Art => art;
        public BoardWorldPresenter BoardWorld => boardWorld;
        /// <summary>The canvas itself, for full-screen overlays drawn above the HUD.</summary>
        public RectTransform CanvasRect => (RectTransform)transform;
        public TMP_FontAsset LatinFont => art.font != null ? art.font : font;
        private PrototypeGame game;
        private RectTransform root;
        private TMP_Text feedback, timing, combo, stats, beatLabel, position, mode, overlayTitle, overlayBody, buttonLabel, countIn;
        private GameObject overlay;
        private NotePresenter notes;
        private BoardWorldPresenter boardWorld;
        private BossPortrait bossPortrait;
        public BossPortrait BossPortrait => bossPortrait;
        private TMP_Text noiseLabel;
        private TMP_Text attackHint;
        private TMP_Text playerHp, patternList;
        private readonly TMP_Text[] dangerLabels = new TMP_Text[64];
        private readonly GameObject[] dangerBadges = new GameObject[64];
        private readonly UnityEngine.UI.Image[] dangerBackgrounds = new UnityEngine.UI.Image[64];
        private static readonly Color BadgeDark = new Color(.025f,.035f,.045f,.97f);
        private readonly Vector2[] dangerPoints = new Vector2[64];
        private readonly RectTransform[] dangerConnectors = new RectTransform[64];
        private readonly Rect[] badgeBounds = new Rect[64];
        private readonly bool[] badgePlaced = new bool[64];
        private readonly int[] dangerCounts = new int[64];
        private readonly int[] dangerRemaining = new int[64];
        private readonly AttackResponse[] dangerResponses = new AttackResponse[64];
        private readonly StringBuilder patternText = new StringBuilder(256);
        private int lastClosedNote = -1;
        private float impactAge = 1;
        private ulong impactCells;
        private readonly TMP_Text[] healthSlots = new TMP_Text[4];
        private readonly UnityEngine.UI.Image[] crowns = new UnityEngine.UI.Image[4];
        private TMP_Text phaseLabel;
        private float feedbackAge = 10, pulseAge = 10;
        private const float OutcomeSeconds = .6f;
        private static readonly Color BlockedTint = new Color(.45f, .85f, 1f), HitTint = new Color(1f, .42f, .36f);
        private RectTransform outcomeFlash;
        private CanvasGroup outcomeGroup;
        private UnityEngine.UI.Image outcomePlate;
        private TMP_Text outcomeLabel;
        private float outcomeAge = 10;
        private int seenRecords;
        public string ActiveOutcome => outcomeFlash.gameObject.activeSelf ? outcomeLabel.text : "";

        public void Initialize(PrototypeGame owner)
        {
            game = owner;
            // A missing ArtSet is the same as an empty one: every slot falls back to the prototype visual.
            if (art == null) { art = ScriptableObject.CreateInstance<ArtSet>(); art.hideFlags = HideFlags.DontSave; }
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            if (EventSystem.current == null)
            { var eventObject = new GameObject("PrototypeEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); eventObject.transform.SetParent(transform, false); }

            var background = MakeRect((RectTransform)transform, "Background", Vector2.zero, Vector2.zero);
            background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one; background.sizeDelta = Vector2.zero;
            var backgroundImage = Image(background, new Color(.035f,.043f,.052f));
            if (art.background != null) { backgroundImage.sprite = art.background; backgroundImage.color = Color.white; }
            for (int i = 0; art.backgroundLayers != null && i < art.backgroundLayers.Length; i++)
            {
                var layer = art.backgroundLayers[i];
                if (layer.sprite == null) continue;
                var rect = MakeRect((RectTransform)transform, $"Background Layer {i}", Vector2.zero, Vector2.zero);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                // Oversized by the sway so a floating layer never shows its edge.
                rect.sizeDelta = 2 * new Vector2(Mathf.Abs(layer.sway.x), Mathf.Abs(layer.sway.y));
                Image(rect, Color.white).sprite = layer.sprite;
                if (layer.sway != Vector2.zero) rect.gameObject.AddComponent<BackgroundSway>().Set(layer.sway, layer.period, i);
            }
            root = MakeRect((RectTransform)transform, "Content", Vector2.zero, new Vector2(1600,900));
            // The full prototype readout lives on a detail layer, hidden unless detailedHud is on.
            var detail = MakeRect(root, "Detail HUD", Vector2.zero, new Vector2(1600,900));
            layer = root;
            Label("GEOM EUL HYEON", new Vector2(0,410), new Vector2(650,40), 24, InkWhite);
            phaseLabel = Label("", new Vector2(0,380), new Vector2(650,24), 13, Muted);
            BuildBoard();
            BuildLanes();
            // Kept on screen: player state top left, combo top right, judgement under the right strings.
            playerHp = Label("", new Vector2(-600,300), new Vector2(330,36), 24, InkWhite);
            noiseLabel = Label("", new Vector2(-600,245), new Vector2(330,65), 16, Accent);
            Label("COMBO", new Vector2(600,320), new Vector2(250,26), 14, Muted);
            combo = Label("000", new Vector2(600,272), new Vector2(300,70), 56, InkWhite);
            feedback = Label("FIND YOUR RHYTHM", new Vector2(600,-210), new Vector2(330,45), 23, InkWhite);
            timing = Label("Follow the two mirrored notes", new Vector2(600,-250), new Vector2(330,40), 14, Muted);
            countIn = Label("", new Vector2(0,-405), new Vector2(430,40), 21, InkWhite);
            for (int i = 0; i < 4; i++)
            {
                healthSlots[i] = Label("", new Vector2((i-1.5f)*136,340), new Vector2(130,40), 16, InkWhite, true);
                if (!art.HasCrown) continue;
                crowns[i] = Image(MakeRect(root, $"Crown_{i}", new Vector2((i-1.5f)*136-48,340), new Vector2(30,30)), Color.white);
                crowns[i].preserveAspect = true; healthSlots[i].margin = new Vector4(30,0,0,0);
            }

            layer = detail;
            detail.gameObject.SetActive(detailedHud);
            Label("PROJECT  /  01", new Vector2(-660,394), new Vector2(200,30), 16, Muted);
            Label("RHYTHM PROTOTYPE", new Vector2(655,394), new Vector2(220,30), 14, Muted);
            Label("TEMPO", new Vector2(-573,235), new Vector2(250,30), 14, Muted);
            Label($"{game.Song.Bpm:0} <size=18>BPM</size>", new Vector2(-573,187), new Vector2(250,65), 52, InkWhite);
            Label($"ONE NOTE / {(60f / game.Song.Bpm):0.000} SEC", new Vector2(-573,139), new Vector2(300,26), 13, Muted);
            mode = Label("", new Vector2(-573,105), new Vector2(300,30), 13, Muted);
            Label("MOVE ON THE BEAT", new Vector2(-573,-132), new Vector2(300,30), 17, InkWhite);
            Label("W", new Vector2(-573,-190), new Vector2(44,44), 22, InkWhite, true);
            Label("A", new Vector2(-625,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("S", new Vector2(-573,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("D", new Vector2(-521,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("WASD  MOVE    J  ATTACK    K  GUARD\nOne action per beat.", new Vector2(-573,-311), new Vector2(380,55), 14, Muted);
            Label("BOSS TELEGRAPHS", new Vector2(573,-118), new Vector2(330,25), 13, Muted);
            patternList = Label("", new Vector2(573,-198), new Vector2(330,130), 15, InkWhite);
            patternList.alignment = TextAlignmentOptions.TopLeft;
            stats = Label("", new Vector2(573,-318), new Vector2(330,85), 14, Muted);
            Label("PERFECT +/-70 ms   /   GOOD +/-140 ms", new Vector2(573,-375), new Vector2(370,22), 12, Muted);
            Label("GLOW NORMAL   /   /// STRONG, NO GUARD   /   X IMPACT\nNUMBER = NOTES LEFT   /   +N = EXTRA ATTACKS", new Vector2(-573,-365), new Vector2(400,45), 12, Muted);
            beatLabel = Label("BEAT  --", new Vector2(0,-292), new Vector2(320,26), 13, Accent);
            position = Label("D4", new Vector2(0,-367), new Vector2(400,30), 14, Muted);
            attackHint = Label("", new Vector2(0,-342), new Vector2(600,25), 13, Accent);
            Bar("FooterRule", 0,-397,1460,1,new Color(.23f,.26f,.29f));
            Label("SPACE  START     R  RESTART     ESC  PAUSE     M  CLICK SOUND", new Vector2(0,-423), new Vector2(1200,30), 14, Muted);
            layer = root;
            BuildOverlay();
        }
        private void BuildBoard()
        {
            Vector2 size = BoardSize;
            var board = MakeRect(root,"Board View",BoardCenter,size);
            var output = board.gameObject.AddComponent<UnityEngine.UI.RawImage>(); output.raycastTarget = false;
            boardWorld = board.gameObject.AddComponent<BoardWorldPresenter>(); boardWorld.Initialize(game, output, boardMaterial, art);
            if (art.bossImage != null && art.kingPrefab == null)
            {
                // Under the content layer, so the board and the HUD draw over the picture.
                var boss = MakeRect((RectTransform)transform, "Boss Portrait", Vector2.zero, Vector2.zero);
                boss.SetSiblingIndex(root.GetSiblingIndex());
                float farEdge = BoardCenter.y + boardWorld.ProjectToBoard(new Vector3(0, 0, 4), size).y;
                bossPortrait = boss.gameObject.AddComponent<BossPortrait>();
                bossPortrait.Initialize(game, art.bossImage, new Vector2(BoardCenter.x + art.bossImageSink.x, farEdge - art.bossImageSink.y), Mathf.Max(1, art.bossImageHeight));
            }
            var connectors = MakeRect(board, "DangerConnectors", Vector2.zero, size);
            for (int i = 0; i < 64; i++)
            {
                Vector2 point = boardWorld.ProjectToBoard(new Vector3(i % 8 - 3.5f, .035f, i / 8 - 3.80f), size);
                dangerPoints[i] = point;
                dangerConnectors[i] = MakeRect(connectors, "CellConnector", point, Vector2.one);
                Image(dangerConnectors[i], new Color(.94f,.93f,.88f,.65f));
                dangerConnectors[i].gameObject.SetActive(false);
                var badge = MakeRect(board, $"DangerBadge_{i % 8}_{i / 8}", point, new Vector2(40,20));
                dangerBackgrounds[i] = Image(badge, BadgeDark); dangerBadges[i] = badge.gameObject;
                dangerLabels[i] = Text(badge, "Countdown", "", Vector2.zero, new Vector2(40,20), 17, InkWhite);
                dangerLabels[i].fontStyle = FontStyles.Bold;
                badge.gameObject.SetActive(false);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector2 column = boardWorld.ProjectToBoard(new Vector3(i-3.5f,0,-4.43f),size);
                Vector2 row = boardWorld.ProjectToBoard(new Vector3(-4.43f,0,i-3.5f),size);
                Text(board,"Column",((char)('A'+i)).ToString(),column,new Vector2(22,22),12,Muted);
                Text(board,"Row",(i+1).ToString(),row,new Vector2(22,22),12,Muted);
            }
            // Built after the badges so it reads above them; the menu overlay is still built later and covers it.
            outcomeFlash = MakeRect(board, "OutcomeFlash", Vector2.zero, new Vector2(264,48));
            outcomeGroup = outcomeFlash.gameObject.AddComponent<CanvasGroup>();
            outcomeGroup.blocksRaycasts = false; outcomeGroup.interactable = false;
            outcomePlate = Image(outcomeFlash, new Color(.025f,.035f,.045f,.97f));
            outcomeLabel = Text(outcomeFlash, "Outcome", "", Vector2.zero, new Vector2(240,44), 30, InkWhite);
            outcomeLabel.fontStyle = FontStyles.Bold;
            outcomeFlash.gameObject.SetActive(false);
        }
        private void BuildLanes()
        {
            // Five strings run from the screen edge to judge lines on the board's sides, at the board's middle height,
            // so the strings read as tied to the board. The judge lines follow the board camera, not a fixed x.
            float laneY = BoardCenter.y + boardWorld.ProjectToBoard(Vector3.zero, BoardSize).y;
            if (art.laneSprite != null) { BuildLaneArt(); return; }
            float judgeX = BoardCenter.x + boardWorld.ProjectToBoard(new Vector3(4.9f, 0, 0), BoardSize).x + 10; // clear of the 1-8 row labels at x 4.43
            float span = StringGap * 4 + 34;
            judgeLines = new UnityEngine.UI.Image[2];
            for(int side=-1;side<=1;side+=2)
            {
                for (int s = 0; s < 5; s++)
                    Bar("String", side * (judgeX + ScreenEdge) * .5f, laneY + (s - 2) * StringGap, ScreenEdge - judgeX, s == 2 ? 2 : 1.5f,
                        new Color(InkWhite.r, InkWhite.g, InkWhite.b, s == 2 ? .55f : .3f));
                judgeLines[(side + 1) / 2] = Bar("JudgeLine", side * judgeX, laneY, 4, span, InkWhite);
            }
            notes = new NotePresenter(root, art.noteSprite, art.holdBodySprite, art.holdTailSprite, judgeX, ScreenEdge, laneY, span);
        }
        // Delivered lane art: one sprite with strings on both sides and a gap for the board. Its height is fitted to the
        // board rows in ArtSet.laneRows and its width so the middle string ends on the board's side edges, so the two
        // scales differ. The judge lines lie along those edges over the same rows, tilted with the board's perspective.
        private void BuildLaneArt()
        {
            var sprite = art.laneSprite;
            float edge = art.laneBoardEdge;
            // Row r covers board z from r - 5 to r - 4: the strings run from the near side of the first row to the far side of the last.
            Vector2 near = boardWorld.ProjectToBoard(new Vector3(edge, 0, art.laneRows.x - 5), BoardSize);
            Vector2 far = boardWorld.ProjectToBoard(new Vector3(edge, 0, art.laneRows.y - 4), BoardSize);
            float scaleY = (far.y - near.y) / (art.laneStrings.z - art.laneStrings.x);
            float laneY = BoardCenter.y + far.y - (art.laneStrings.y - art.laneStrings.x) * scaleY;
            // Where the board's edge crosses the middle string: the art's width is fitted there.
            float judgeX = Mathf.Lerp(near.x, far.x, Mathf.InverseLerp(near.y, far.y, laneY - BoardCenter.y));
            float scaleX = 2 * judgeX / (art.laneBoardGap.y - art.laneBoardGap.x);
            var lane = MakeRect(root, "Lane Art", new Vector2(
                BoardCenter.x + (sprite.rect.width - art.laneBoardGap.x - art.laneBoardGap.y) * .5f * scaleX,
                laneY + (art.laneStrings.y - sprite.rect.height * .5f) * scaleY), new Vector2(sprite.rect.width * scaleX, sprite.rect.height * scaleY));
            Image(lane, Color.white).sprite = sprite;
            float tilt = Mathf.Atan2(near.x - far.x, far.y - near.y);
            // The notes ride their own string; the judge point is where the board's edge crosses it.
            float noteY = BoardCenter.y + far.y - (art.laneNoteString - art.laneStrings.x) * scaleY;
            float noteX = Mathf.Lerp(near.x, far.x, Mathf.InverseLerp(near.y, far.y, noteY - BoardCenter.y));
            Vector2 middle = (near + far) * .5f;
            judgeLines = new UnityEngine.UI.Image[2]; judgeGlows = new UnityEngine.UI.Image[2];
            for (int side = -1; side <= 1; side += 2)
            {
                var turn = Quaternion.Euler(0, 0, side * tilt * Mathf.Rad2Deg);
                float x = BoardCenter.x + side * middle.x, y = BoardCenter.y + middle.y, length = (far - near).magnitude + 8;
                // The glow is built first so the line draws over it.
                var glow = Bar("JudgeGlow", x, y, JudgeGlowWidth, length + 12, Color.white);
                glow.sprite = GlowSprite(); glow.rectTransform.localRotation = turn;
                var line = Bar("JudgeLine", x, y, 4, length, Color.white);
                line.rectTransform.localRotation = turn;
                judgeLines[(side + 1) / 2] = line; judgeGlows[(side + 1) / 2] = glow;
            }
            // No scrolling bar lines here: the art has its own frets, and each note carries a stem.
            notes = new NotePresenter(root, art.noteSprite, art.holdBodySprite, art.holdTailSprite, noteX, ScreenEdge, noteY, 0, tilt);
        }
        // A soft white band: opaque along its centre line, fading out to both sides.
        private static Sprite GlowSprite()
        {
            if (glowSprite != null) return glowSprite;
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < width; x++)
            {
                float fromCentre = Mathf.Abs((x + .5f) / width * 2 - 1);
                texture.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Pow(1 - fromCentre, 2)));
            }
            texture.Apply();
            glowSprite = Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(.5f, .5f));
            glowSprite.hideFlags = HideFlags.HideAndDontSave;
            return glowSprite;
        }
        private void BuildOverlay()
        {
            var dim=MakeRect(root,"MenuOverlay",Vector2.zero,new Vector2(1600,900)); overlay=dim.gameObject;
            var dimImage=Image(dim,new Color(.015f,.02f,.025f,.87f)); dimImage.raycastTarget=true;
            var card=MakeRect(dim,"MenuCard",Vector2.zero,new Vector2(660,365)); Image(card,new Color(.07f,.085f,.10f));
            overlayTitle=Text(card,"Title","FIND YOUR RHYTHM",new Vector2(0,110),new Vector2(610,55),32,InkWhite);
            overlayBody=Text(card,"Instructions","",new Vector2(0,22),new Vector2(590,115),18,Muted);
            var buttonRect=MakeRect(card,"StartButton",new Vector2(-150,-105),new Vector2(280,55));
            var buttonImage=Image(buttonRect,InkWhite); buttonImage.raycastTarget=true;
            var button=buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic=buttonImage;
            button.onClick.AddListener(()=>game.StartSession());
            buttonLabel=Text(buttonRect,"Label","START / SPACE",Vector2.zero,new Vector2(270,50),17,new Color(.06f,.08f,.09f));
            var quitRect=MakeRect(card,"QuitButton",new Vector2(150,-105),new Vector2(280,55));
            var quitImage=Image(quitRect,new Color(.17f,.20f,.23f)); quitImage.raycastTarget=true;
            var quit=quitRect.gameObject.AddComponent<UnityEngine.UI.Button>(); quit.targetGraphic=quitImage;
            quit.onClick.AddListener(game.QuitGame);
            Text(quitRect,"Label",Core.GameFlow.HasMenu?"MAIN MENU":"QUIT GAME",Vector2.zero,new Vector2(270,50),17,InkWhite);
            // Developer mode: restart in a phase, toggle invincibility. Shown under the card while the mode is on.
            devStrip=MakeRect(dim,"DevStrip",new Vector2(0,-240),new Vector2(660,90)).gameObject; Image((RectTransform)devStrip.transform,new Color(.07f,.085f,.10f));
            Text(devStrip.transform,"Title","DEVELOPER MODE    F1-F4 PHASE / F5 INVINCIBLE / F6 BOSS HP",new Vector2(0,28),new Vector2(640,24),13,Muted);
            for(int i=0;i<6;i++)
            {
                int index=i;
                var rect=MakeRect(devStrip.transform,i<4?"Phase"+(i+1):i==4?"Invincible":"BossHp",new Vector2(i<4?-282+90*i:i==4?118:262,-12),new Vector2(i<4?84:i==4?150:130,40));
                devButtons[i]=Image(rect,InkWhite); devButtons[i].raycastTarget=true;
                var dev=rect.gameObject.AddComponent<UnityEngine.UI.Button>(); dev.targetGraphic=devButtons[i];
                dev.onClick.AddListener(()=>{ if(index<4) game.DevGoToPhase(index+1); else if(index==4) game.DevSetInvincible(!Core.GameFlow.Invincible); else game.DevSetBossHp(Core.GameFlow.BossHpOff); });
                devLabels[i]=Text(rect,"Label",i<4?"PHASE "+(i+1):"",Vector2.zero,new Vector2(146,36),14,InkWhite);
            }
        }
        private GameObject devStrip;
        private readonly UnityEngine.UI.Image[] devButtons=new UnityEngine.UI.Image[6];
        private readonly TMP_Text[] devLabels=new TMP_Text[6];
        public void ResetFeedback()
        {
            feedback.text="FIND YOUR RHYTHM"; timing.text="Follow the two mirrored notes";
            feedbackAge=10; pulseAge=10; impactAge=1; impactCells=0; lastClosedNote=-1;
            outcomeAge=10; seenRecords=game.Battle.Records.Count; outcomeFlash.gameObject.SetActive(false);
            Array.Clear(dangerCounts, 0, dangerCounts.Length);
            for (int i = 0; i < dangerBadges.Length; i++)
            { dangerBadges[i].SetActive(false); dangerConnectors[i].gameObject.SetActive(false); }
            boardWorld.ResetVisuals();
        }
        public void Pulse() => pulseAge=0;
        public void ShowAction(ActionResult result)
        {
            boardWorld.ShowAction(result);
            feedbackAge=0;
            feedback.color=result.Accepted?Accent:InkWhite;
            if(result.Accepted)
            {
                feedback.text=result.Timing.Grade.ToString().ToUpperInvariant();
                timing.text=$"{result.Timing.ErrorSeconds*1000:+0;-0;0} ms  /  {(result.Timing.ErrorSeconds<0?"EARLY":"LATE")}";
                if (result.Kind == PlayerActionKind.Attack)
                {
                    timing.text = game.Attacks.IsInRange(result.Position) ? "ATTACK / TOP ROW" : "SWING / OUT OF RANGE";
                }
                if (result.Kind == PlayerActionKind.Guard) timing.text = "GUARD / THIS NOTE ONLY";
            }
            else
            {
                feedback.text=result.Rejection==ActionRejection.OutsideBoard?"BOARD EDGE":result.Rejection==ActionRejection.BeatAlreadyUsed?"BEAT USED":"OFF BEAT";
                timing.text=result.Rejection==ActionRejection.OutsideBoard?"Try another direction on this beat":result.Rejection==ActionRejection.BeatAlreadyUsed?"Wait for the next note":"Act when notes reach the markers";
            }
        }
        public void ShowHold(TimingResult result)
        {
            feedbackAge = 0;
            feedback.text = result.IsHit ? "RELEASE " + result.Grade.ToString().ToUpperInvariant() : "HOLD BREAK";
            feedback.color = result.IsHit ? Accent : Muted;
            timing.text = result.IsHit ? $"{result.ErrorSeconds * 1000:+0;-0;0} ms  /  RELEASE" :
                result.ErrorSeconds < 0 ? "Released too early" : "Let go at the end of the string";
        }
        public void ShowMiss(int beat) { feedbackAge=0; feedback.text="MISS"; feedback.color=Muted; timing.text="Listen. Reset. Find the next beat."; }
        public void ShowDamage(int damage)
        {
            boardWorld.ShowDamage(damage);
            if (damage > 0 && bossPortrait != null) bossPortrait.Hurt();
            timing.text = damage > 0 ? $"BOSS HP  -{damage}" : game.Health.Defeated ? "ALL FOUR SEGMENTS CLEARED" : game.Health.CurrentDepleted ? "HP FLOOR / COLLECT NOISE" : "SWING / 0 DAMAGE";
        }
        // Beside the knight, because that is where the eye already is. Near the top it drops below instead.
        private void ShowOutcome(string text)
        {
            outcomeAge = 0;
            boardWorld.ShowOutcome(text);
            outcomeLabel.text = text;
            outcomeFlash.sizeDelta = new Vector2(text.Length * 19 + 36, 48);
            outcomeLabel.color = text == TelegraphHud.Blocked ? BlockedTint : text == TelegraphHud.Hit ? HitTint : InkWhite;
            outcomePlate.color = text == TelegraphHud.GuardBroken ? new Color(.62f,.12f,.10f,.97f) : new Color(.025f,.035f,.045f,.97f);
            Vector2 knight = boardWorld.ProjectToBoard(boardWorld.PlayerTransform.localPosition, BoardSize);
            float y = knight.y + 106;
            if (y > BoardSize.y * .5f - 35) y = knight.y - 44;
            outcomeFlash.anchoredPosition = new Vector2(Mathf.Clamp(knight.x, 150 - BoardSize.x * .5f, BoardSize.x * .5f - 150), y);
            outcomeFlash.gameObject.SetActive(true);
        }
        private void RenderOutcome()
        {
            var records = game.Battle.Records;
            for (; seenRecords < records.Count; seenRecords++)
            {
                string text = TelegraphHud.OutcomeText(records[seenRecords]);
                if (text != null) ShowOutcome(text);
            }
            if (game.State != PrototypeState.Paused) outcomeAge += Time.unscaledDeltaTime;
            bool visible = outcomeAge < OutcomeSeconds;
            if (outcomeFlash.gameObject.activeSelf != visible) outcomeFlash.gameObject.SetActive(visible);
            if (!visible) return;
            // Hold, then fade. The pop restarts on every note so a run of BLOCKED still reads as separate blocks.
            outcomeGroup.alpha = Mathf.Clamp01((OutcomeSeconds - outcomeAge) / .2f);
            outcomeFlash.localScale = Vector3.one * (1 + .18f * Mathf.Clamp01(1 - outcomeAge / .1f));
        }
        public void ShowPhase(int phase)
        {
            boardWorld.ShowPhase();
            feedbackAge = 0; feedback.color = Accent;
            feedback.text = $"PHASE {phase} OPEN";
            timing.text = $"MELODY STOLEN  {phase - 1} / 3";
        }
        public void Render()
        {
            feedbackAge+=Time.unscaledDeltaTime; pulseAge+=Time.unscaledDeltaTime;
            var cell=game.Session.Position;
            boardWorld.Render();
            RenderTelegraphs();
            RenderOutcome();
            playerHp.text = $"PLAYER HP  {game.Battle.PlayerHp} / {game.Stage.PlayerMaxHp}";
            attackHint.text = !game.Attacks.IsInRange(cell) ? "J / SWING ONLY - DAMAGE FROM ROW 8" : game.Health.Defeated ? "J / SWING - ALL HP CLEARED" : game.Health.CurrentDepleted ? "J / 0 DAMAGE - COLLECT NOISE" : "J / ATTACK - DAMAGE ENABLED";
            combo.text=game.Session.Combo.ToString("000");
            phaseLabel.text = game.Phases.Completed ? "FOUR SEGMENTS CLEARED" : $"PHASE {game.Phases.PhaseId} / 4     -     MELODY STOLEN  {game.Phases.Steals} / 3";
            for (int i = 0; i < healthSlots.Length; i++)
            {
                bool active = i == game.Health.ActiveSegment;
                healthSlots[i].text = $"P{i+1} {game.Health.Remaining(i)}/{game.Health.Maximum(i)}" +
                    (i > game.Health.ActiveSegment ? "<size=11> LOCKED</size>" : game.Health.Remaining(i) == 0 ? "<size=11> EMPTY</size>" : "");
                healthSlots[i].color = active ? Accent : Muted;
                if (crowns[i] != null)
                    crowns[i].sprite = game.Health.Remaining(i) == 0 ? art.crownBroken : i > game.Health.ActiveSegment ? art.crownLocked : art.crownFull;
            }
            stats.text=$"PERFECT     {game.Session.PerfectCount:000}\nGOOD            {game.Session.GoodCount:000}\nMISS              {game.Session.MissCount:000}\nBEST              {game.Session.BestCombo:000}";
            int cellIndex = cell.Y * 8 + cell.X;
            position.text=$"POSITION  {(char)('A'+cell.X)}{cell.Y+1}     /     " + (dangerCounts[cellIndex] > 0 ? TelegraphHud.ThreatText(dangerResponses[cellIndex], dangerRemaining[cellIndex], dangerCounts[cellIndex]) : "SAFE");
            beatLabel.text=game.CurrentBeat<0?"PREPARE":$"BEAT  {game.CurrentBeat+1:0000}   /   {game.CurrentBeat%4+1}:4";
            mode.text=game.Song.Music!=null?"SONG AUDIO":game.MetronomeMuted?"METRONOME / MUTED":"METRONOME / ON";
            // The judge lines flash on the beat pulse, then settle back to white.
            // With the glow, the lines stay white and the glow swells on the beat pulse instead.
            if (judgeGlows != null)
                foreach (var glow in judgeGlows) glow.color = new Color(1, 1, 1, Mathf.Lerp(.55f, 1, Mathf.Exp(-8 * pulseAge)));
            else foreach (var line in judgeLines) line.color = Color.Lerp(InkWhite, Accent, Mathf.Exp(-8 * pulseAge));
            notes.Render(game);
            noiseLabel.text = game.Phases.PhaseId == 4 ? "FINAL PHASE\nALL MELODIES RECOVERED" : $"NOISE  {game.Noise.Collected} / {game.Noise.Required}" + (game.Noise.GoalReached ? "\nEMPTY HP TO UNLOCK" : game.Health.CurrentDepleted ? "\nCOLLECT TO UNLOCK NEXT HP" : "\nLAND ON A WAVE TO COLLECT");
            countIn.text=game.State==PrototypeState.CountIn?$"GET READY  {Mathf.Clamp((int)Math.Ceiling(-game.SongTime/game.Timeline.SecondsPerBeat),1,game.Song.CountInBeats)}": game.Battle.Invincible || game.Battle.IgnoreBossHp ? "DEV MODE" + (game.Battle.Invincible ? " / INVINCIBLE" : "") + (game.Battle.IgnoreBossHp ? " / BOSS HP OFF" : "") : "TWO NOTES. ONE BEAT. ONE ACTION.";
            bool show=game.State==PrototypeState.Ready||game.State==PrototypeState.Paused||game.State==PrototypeState.Finished;
            overlay.SetActive(show);
            if(show)
            {
                string result = game.Battle.Outcome == BattleOutcome.Won ? "WON / BOSS DEFEATED" : game.Battle.Outcome == BattleOutcome.Dead ? "DEAD / PLAYER HP EMPTY" : "TIMEOUT / NOTES EXHAUSTED";
                overlayTitle.text=game.State==PrototypeState.Paused?"SESSION PAUSED":game.State==PrototypeState.Finished?result:"FIND YOUR RHYTHM";
                overlayBody.text=game.State==PrototypeState.Ready?"WASD move / J attack / K guard this note\nHatched: guard or move. Shield: guard. Arrows: move.\nNumbers count down to impact.\nAttack from row 8. Collect waves to unlock each phase.":$"Player HP {game.Battle.PlayerHp} / {game.Stage.PlayerMaxHp}    Phase {game.Phases.PhaseId} / 4\nPerfect {game.Session.PerfectCount}    Good {game.Session.GoodCount}    Miss {game.Session.MissCount}\nStart again with a fresh four-beat count-in.";
                buttonLabel.text=game.State==PrototypeState.Ready?"START / SPACE":"RESTART / R OR SPACE";
                devStrip.SetActive(Core.GameFlow.DevMode);
                if(devStrip.activeSelf)
                    for(int i=0;i<6;i++)
                    {
                        bool on=i<4?Core.GameFlow.StartPhase==i+1:i==4?Core.GameFlow.Invincible:!Core.GameFlow.BossHpOff;
                        devButtons[i].color=on?InkWhite:new Color(.17f,.20f,.23f); devLabels[i].color=on?new Color(.06f,.08f,.09f):InkWhite;
                        if(i==4) devLabels[i].text=on?"INVINCIBLE ON":"INVINCIBLE OFF"; if(i==5) devLabels[i].text=on?"BOSS HP ON":"BOSS HP OFF";
                    }
            }
        }
        private void RenderTelegraphs()
        {
            var battle = game.Battle;
            if (battle.LastClosedNote != lastClosedNote)
            {
                lastClosedNote = battle.LastClosedNote; impactCells = 0;
                foreach (var fired in battle.Patterns.LastFired) impactCells |= fired.Cells;
                impactAge = 0;
            }
            if (game.State != PrototypeState.Paused) impactAge += Time.unscaledDeltaTime;
            var active = battle.Patterns.Active;
            TelegraphHud.MapCells(active, battle.LastClosedNote, dangerCounts, dangerRemaining, dangerResponses);
            patternText.Clear();
            int shown = 0;
            bool flashing = impactAge < .3f;
            if (flashing)
                foreach (var fired in battle.Patterns.LastFired)
                {
                    if (shown++ < 5) patternText.Append("X  ").Append(fired.Name).Append("  /  IMPACT\n");
                }
            shown = TelegraphHud.AppendPatternLines(patternText, active, battle.LastClosedNote, shown, 5);
            if (shown > 5) patternText.Append('+').Append(shown - 5).Append(" patterns");
            patternList.text = patternText.Length == 0 ? "CLEAR / NO ATTACK QUEUED" : patternText.ToString();
            Vector2 knightPoint = boardWorld.ProjectToBoard(boardWorld.PlayerTransform.localPosition, BoardSize);
            var knightBounds = new Rect(knightPoint + art.knightBadgeBox.position, art.knightBadgeBox.size);
            ulong marked = flashing ? impactCells : 0;
            for (int i = 0; i < 64; i++) if (dangerCounts[i] > 0) marked |= 1UL << i;
            var player = game.Session.Position;
            ulong badges = TelegraphHud.BadgeCells(marked, dangerRemaining, player.Y * 8 + player.X);
            for (int i = 0; i < 64; i++)
            {
                bool fired = flashing && (impactCells & (1UL << i)) != 0;
                bool visible = (badges & (1UL << i)) != 0;
                dangerBadges[i].SetActive(visible);
                badgeBounds[i] = new Rect(dangerPoints[i] - new Vector2(20,10), new Vector2(40,20));
                badgePlaced[i] = visible && !knightBounds.Overlaps(badgeBounds[i]);
                if (dangerCounts[i] > 0)
                    // A sequence fires and counts down in the same frame; the badge only fits one suffix, and X is the news.
                    dangerLabels[i].text = dangerRemaining[i] + (fired ? "<size=11> X</size>" : dangerCounts[i] > 1 ? $"<size=11> +{dangerCounts[i] - 1}</size>" : "");
                else if (fired) dangerLabels[i].text = "X";
                if (!visible) continue;
                // Impact-only badges have no response left and keep the dark style.
                var response = dangerCounts[i] > 0 ? dangerResponses[i] : AttackResponse.Either;
                dangerBackgrounds[i].color = TelegraphHud.ResponseColor(response, BadgeDark);
                // Only a strong attack's badge is tinted; every other badge is dark and needs the light number.
                dangerLabels[i].color = response == AttackResponse.DodgeOnly ? BadgeDark : InkWhite;
            }
            for (int i = 0; i < 64; i++)
            {
                bool visible = dangerBadges[i].activeSelf;
                Vector2 point = dangerPoints[i];
                if (visible && !badgePlaced[i]) point = PlaceCountdown(i, knightBounds);
                ((RectTransform)dangerBadges[i].transform).anchoredPosition = point;
                Vector2 delta = point - dangerPoints[i];
                var line = dangerConnectors[i]; line.gameObject.SetActive(visible && delta.sqrMagnitude > 1);
                if (!line.gameObject.activeSelf) continue;
                line.anchoredPosition = (point + dangerPoints[i]) * .5f;
                line.sizeDelta = new Vector2(delta.magnitude, 1);
                line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
        }
        private Vector2 PlaceCountdown(int index, Rect knightBounds)
        {
            Vector2 origin = dangerPoints[index], best = origin;
            float bestDistance = float.MaxValue;
            // Keep every countdown readable: move only badges crossing the knight, with a line back to the cell.
            for (int y = -6; y <= 6; y++) for (int x = -6; x <= 6; x++)
            {
                Vector2 offset = new Vector2(x * 48, y * 24);
                if (offset.sqrMagnitude >= bestDistance) continue;
                Vector2 point = origin + offset;
                var candidate = new Rect(point - new Vector2(20,10), new Vector2(40,20));
                Vector2 half = BoardSize * .5f - new Vector2(10, 10);
                if (candidate.xMin < -half.x || candidate.xMax > half.x || candidate.yMin < -half.y || candidate.yMax > half.y || candidate.Overlaps(knightBounds)) continue;
                bool free = true;
                for (int i = 0; i < 64; i++)
                    if (badgePlaced[i] && candidate.Overlaps(badgeBounds[i])) { free = false; break; }
                if (!free) continue;
                best = point; bestDistance = offset.sqrMagnitude;
            }
            Debug.Assert(bestDistance < float.MaxValue, "Danger countdown needs a free label position.");
            badgeBounds[index] = new Rect(best - new Vector2(20,10), new Vector2(40,20));
            badgePlaced[index] = true; return best;
        }
        public static RectTransform MakeRect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
        }
        private static UnityEngine.UI.Image Image(RectTransform rect,Color color)
        { var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=color; image.raycastTarget=false; return image; }
        private UnityEngine.UI.Image Bar(string name,float x,float y,float width,float height,Color color)=>Image(MakeRect(layer,name,new Vector2(x,y),new Vector2(width,height)),color);
        private TMP_Text Label(string content,Vector2 point,Vector2 size,int fontSize,Color tint,bool box=false)
        {
            if(box) Image(MakeRect(layer,"KeyBackground",point,size),new Color(.12f,.15f,.18f));
            return Text(layer,"Label",content,point,size,fontSize,tint);
        }
        private TMP_Text Text(Transform parent,string name,string content,Vector2 point,Vector2 size,int fontSize,Color tint)
        {
            var rect=MakeRect(parent,name,point,size); var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=art.font!=null?art.font:font; label.text=content; label.fontSize=fontSize; label.color=tint; label.alignment=TextAlignmentOptions.Center;
            label.textWrappingMode=TextWrappingModes.NoWrap; label.raycastTarget=false; return label;
        }
    }
}
