using System;
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
        private static readonly Color Muted = new Color(.58f, .62f, .66f);
        private static readonly Color Accent = new Color(.78f, .91f, .86f);
        [SerializeField] private TMP_FontAsset font;
        private PrototypeGame game;
        private RectTransform root;
        private TMP_Text feedback, timing, combo, stats, beatLabel, position, mode, overlayTitle, overlayBody, buttonLabel, countIn;
        private GameObject overlay;
        private NotePresenter notes;
        private BoardWorldPresenter boardWorld;
        private TMP_Text noiseLabel;
        private TMP_Text attackHint;
        private readonly TMP_Text[] healthSlots = new TMP_Text[4];
        private TMP_Text phaseLabel;
        private UnityEngine.UI.Image pulseLine;
        private float feedbackAge = 10, pulseAge = 10;

        public void Initialize(PrototypeGame owner)
        {
            game = owner;
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
            Image(background, new Color(.035f,.043f,.052f));
            root = MakeRect((RectTransform)transform, "Content", Vector2.zero, new Vector2(1600,900));
            Label("PROJECT  /  01", new Vector2(-660,394), new Vector2(200,30), 16, Muted);
            Label("GEOM EUL HYEON", new Vector2(0,385), new Vector2(650,50), 34, InkWhite);
            phaseLabel = Label("", new Vector2(0,343), new Vector2(650,30), 14, Muted);
            Label("RHYTHM PROTOTYPE", new Vector2(655,394), new Vector2(220,30), 14, Muted);
            Bar("HeaderRule", 0, 317, 1460, 1, new Color(.23f,.26f,.29f));
            BuildBoard();
            BuildLanes();

            Label("TEMPO", new Vector2(-573,235), new Vector2(250,30), 14, Muted);
            Label($"{game.Song.Bpm:0} <size=18>BPM</size>", new Vector2(-573,187), new Vector2(250,65), 52, InkWhite);
            Label($"ONE NOTE / {(60f / game.Song.Bpm):0.000} SEC", new Vector2(-573,139), new Vector2(300,26), 13, Muted);
            mode = Label("", new Vector2(-573,-16), new Vector2(300,30), 13, Muted);
            noiseLabel = Label("", new Vector2(-573,-71), new Vector2(330,65), 18, Accent);
            Label("MOVE ON THE BEAT", new Vector2(-573,-132), new Vector2(300,30), 17, InkWhite);
            Label("W", new Vector2(-573,-190), new Vector2(44,44), 22, InkWhite, true);
            Label("A", new Vector2(-625,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("S", new Vector2(-573,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("D", new Vector2(-521,-240), new Vector2(44,44), 22, InkWhite, true);
            Label("WASD  MOVE    J  ATTACK    K  GUARD\nOne action per beat.", new Vector2(-573,-311), new Vector2(380,55), 14, Muted);

            Label("COMBO", new Vector2(573,235), new Vector2(250,30), 14, Muted);
            combo = Label("000", new Vector2(573,181), new Vector2(300,80), 64, InkWhite);
            feedback = Label("FIND YOUR RHYTHM", new Vector2(573,-35), new Vector2(330,45), 23, InkWhite);
            timing = Label("Follow the two mirrored notes", new Vector2(573,-78), new Vector2(330,40), 14, Muted);
            stats = Label("", new Vector2(573,-190), new Vector2(300,130), 18, Muted);
            Label("PERFECT  +/-70 ms\nGOOD  +/-140 ms", new Vector2(573,-298), new Vector2(300,55), 15, Muted);

            for (int i = 0; i < 4; i++)
                healthSlots[i] = Label("", new Vector2((i-1.5f)*136,285), new Vector2(130,45), 16, InkWhite, true);
            beatLabel = Label("BEAT  --", new Vector2(0,-292), new Vector2(320,26), 13, Accent);
            countIn = Label("", new Vector2(0,-319), new Vector2(430,40), 21, InkWhite);
            position = Label("D4", new Vector2(0,-367), new Vector2(400,30), 14, Muted);
            attackHint = Label("", new Vector2(0,-342), new Vector2(600,25), 13, Accent);
            Bar("FooterRule", 0,-397,1460,1,new Color(.23f,.26f,.29f));
            Label("SPACE  START     R  RESTART     ESC  PAUSE     M  CLICK SOUND", new Vector2(0,-423), new Vector2(1200,30), 14, Muted);
            BuildOverlay();
        }
        private void BuildBoard()
        {
            Vector2 size = new Vector2(600,550);
            var board = MakeRect(root,"Board View",new Vector2(0,-4),size);
            var output = board.gameObject.AddComponent<UnityEngine.UI.RawImage>(); output.raycastTarget = false;
            boardWorld = board.gameObject.AddComponent<BoardWorldPresenter>(); boardWorld.Initialize(game, output);
            for (int i = 0; i < 8; i++)
            {
                Vector2 column = boardWorld.ProjectToBoard(new Vector3(i-3.5f,0,-4.43f),size);
                Vector2 row = boardWorld.ProjectToBoard(new Vector3(-4.43f,0,i-3.5f),size);
                Text(board,"Column",((char)('A'+i)).ToString(),column,new Vector2(22,22),12,Muted);
                Text(board,"Row",(i+1).ToString(),row,new Vector2(22,22),12,Muted);
            }
            Label("ATTACK ZONE / ROW 8",new Vector2(0,248),new Vector2(400,24),12,new Color(.84f,.70f,.40f));
        }
        private void BuildLanes()
        {
            for(int side=-1;side<=1;side+=2)
            {
                Bar("Lane",side*517,60,418,2,new Color(.28f,.31f,.33f));
                Bar("JudgeMarker",side*310,60,3,72,InkWhite);
                Label(side<0 ? "IN  >" : "<  IN",new Vector2(side*688,97),new Vector2(95,22),12,Muted);
                Label("HIT",new Vector2(side*310,112),new Vector2(55,22),11,Muted);
            }
            notes = new NotePresenter(root);
            pulseLine=Bar("BeatPulse",0,-280,530,3,Accent);
        }
        private void BuildOverlay()
        {
            var dim=MakeRect(root,"MenuOverlay",Vector2.zero,new Vector2(1600,900)); overlay=dim.gameObject;
            var dimImage=Image(dim,new Color(.015f,.02f,.025f,.87f)); dimImage.raycastTarget=true;
            var card=MakeRect(dim,"MenuCard",Vector2.zero,new Vector2(660,365)); Image(card,new Color(.07f,.085f,.10f));
            overlayTitle=Text(card,"Title","FIND YOUR RHYTHM",new Vector2(0,110),new Vector2(610,55),32,InkWhite);
            overlayBody=Text(card,"Instructions","",new Vector2(0,22),new Vector2(590,115),18,Muted);
            var buttonRect=MakeRect(card,"StartButton",new Vector2(0,-105),new Vector2(340,55));
            var buttonImage=Image(buttonRect,InkWhite); buttonImage.raycastTarget=true;
            var button=buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic=buttonImage;
            button.onClick.AddListener(()=>game.StartSession());
            buttonLabel=Text(buttonRect,"Label","START / SPACE",Vector2.zero,new Vector2(330,50),17,new Color(.06f,.08f,.09f));
        }
        public void ResetFeedback() { feedback.text="FIND YOUR RHYTHM"; timing.text="Follow the two mirrored notes"; feedbackAge=10; pulseAge=10; boardWorld.ResetVisuals(); }
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
                if (result.Kind == PlayerActionKind.Guard) timing.text = "GUARD / POSE ONLY";
            }
            else
            {
                feedback.text=result.Rejection==ActionRejection.OutsideBoard?"BOARD EDGE":result.Rejection==ActionRejection.BeatAlreadyUsed?"BEAT USED":"OFF BEAT";
                timing.text=result.Rejection==ActionRejection.OutsideBoard?"Try another direction on this beat":result.Rejection==ActionRejection.BeatAlreadyUsed?"Wait for the next note":"Act when notes reach the markers";
            }
        }
        public void ShowMiss(int beat) { feedbackAge=0; feedback.text="MISS"; feedback.color=Muted; timing.text="Listen. Reset. Find the next beat."; }
        public void ShowDamage(int damage)
        {
            timing.text = damage > 0 ? $"BOSS HP  -{damage}" : game.Health.Defeated ? "ALL FOUR SEGMENTS CLEARED" : game.Health.CurrentDepleted ? "HP FLOOR / COLLECT NOISE" : "SWING / 0 DAMAGE";
        }
        public void ShowPhase(int phase)
        {
            feedbackAge = 0; feedback.color = Accent;
            feedback.text = $"PHASE {phase} OPEN";
            timing.text = $"MELODY STOLEN  {phase - 1} / 3";
        }
        public void Render()
        {
            feedbackAge+=Time.unscaledDeltaTime; pulseAge+=Time.unscaledDeltaTime;
            var cell=game.Session.Position;
            boardWorld.Render();
            attackHint.text = !game.Attacks.IsInRange(cell) ? "J / SWING ONLY - DAMAGE FROM ROW 8" : game.Health.Defeated ? "J / SWING - ALL HP CLEARED" : game.Health.CurrentDepleted ? "J / 0 DAMAGE - COLLECT NOISE" : "J / ATTACK - DAMAGE ENABLED";
            combo.text=game.Session.Combo.ToString("000");
            phaseLabel.text = game.Phases.Completed ? "FOUR SEGMENTS CLEARED" : $"PHASE {game.Phases.PhaseId} / 4     -     MELODY STOLEN  {game.Phases.Steals} / 3";
            for (int i = 0; i < healthSlots.Length; i++)
            {
                bool active = i == game.Health.ActiveSegment;
                healthSlots[i].text = i > game.Health.ActiveSegment ? $"{i+1} / LOCKED" : game.Health.Remaining(i) == 0 ? $"{i+1} / EMPTY" : $"{i+1} / {game.Health.Remaining(i)} HP";
                healthSlots[i].color = active ? Accent : Muted;
            }
            stats.text=$"PERFECT     {game.Session.PerfectCount:000}\nGOOD            {game.Session.GoodCount:000}\nMISS              {game.Session.MissCount:000}\nBEST              {game.Session.BestCombo:000}";
            position.text=$"POSITION  {(char)('A'+cell.X)}{cell.Y+1}     /     8 x 8";
            beatLabel.text=game.CurrentBeat<0?"PREPARE":$"BEAT  {game.CurrentBeat+1:0000}   /   {game.CurrentBeat%4+1}:4";
            mode.text=game.Song.Music!=null?"SONG AUDIO":game.MetronomeMuted?"METRONOME / MUTED":"METRONOME / ON";
            pulseLine.color=new Color(Accent.r,Accent.g,Accent.b,.15f+.85f*Mathf.Exp(-8*pulseAge));
            notes.Render(game);
            noiseLabel.text = game.Phases.PhaseId == 4 ? "FINAL PHASE\nALL MELODIES RECOVERED" : $"NOISE  {game.Noise.Collected} / {game.Noise.Required}" + (game.Noise.GoalReached ? "\nEMPTY HP TO UNLOCK" : game.Health.CurrentDepleted ? "\nCOLLECT TO UNLOCK NEXT HP" : "\nLAND ON A WAVE TO COLLECT");
            countIn.text=game.State==PrototypeState.CountIn?$"GET READY  {Mathf.Clamp((int)Math.Ceiling(-game.SongTime/game.Timeline.SecondsPerBeat),1,game.Song.CountInBeats)}": "TWO NOTES. ONE BEAT. ONE ACTION.";
            bool show=game.State==PrototypeState.Ready||game.State==PrototypeState.Paused||game.State==PrototypeState.Finished;
            overlay.SetActive(show);
            if(show)
            {
                overlayTitle.text=game.State==PrototypeState.Paused?"SESSION PAUSED":game.State==PrototypeState.Finished?"SESSION COMPLETE":"FIND YOUR RHYTHM";
                overlayBody.text=game.State==PrototypeState.Ready?"87 BPM / WASD move / J attack / K guard pose\nAttack from row 8. Land on waves to collect noise.\nEmpty HP + noise goal unlocks the next phase.":$"Perfect {game.Session.PerfectCount}    Good {game.Session.GoodCount}    Miss {game.Session.MissCount}\nPhase {game.Phases.PhaseId} / 4    Melody stolen {game.Phases.Steals} / 3\nStart again with a fresh four-beat count-in.";
                buttonLabel.text=game.State==PrototypeState.Ready?"START / SPACE":"RESTART / R OR SPACE";
            }
        }
        public static RectTransform MakeRect(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
        }
        private static UnityEngine.UI.Image Image(RectTransform rect,Color color)
        { var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=color; image.raycastTarget=false; return image; }
        private UnityEngine.UI.Image Bar(string name,float x,float y,float width,float height,Color color)=>Image(MakeRect(root,name,new Vector2(x,y),new Vector2(width,height)),color);
        private TMP_Text Label(string content,Vector2 point,Vector2 size,int fontSize,Color tint,bool box=false)
        {
            if(box) Image(MakeRect(root,"KeyBackground",point,size),new Color(.12f,.15f,.18f));
            return Text(root,"Label",content,point,size,fontSize,tint);
        }
        private TMP_Text Text(Transform parent,string name,string content,Vector2 point,Vector2 size,int fontSize,Color tint)
        {
            var rect=MakeRect(parent,name,point,size); var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font=font; label.text=content; label.fontSize=fontSize; label.color=tint; label.alignment=TextAlignmentOptions.Center;
            label.textWrappingMode=TextWrappingModes.NoWrap; label.raycastTarget=false; return label;
        }
    }
}
