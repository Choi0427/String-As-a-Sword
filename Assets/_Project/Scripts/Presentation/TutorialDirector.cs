using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using StringAsASword.Stage;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The tutorial: a practice battle of its own, kept apart from the song, under the letterbox. A chess clock ticks the even
    /// 87 BPM beat and notes run on the lanes. Each lesson first shows its line in the letterbox, a bar later its card beside
    /// the knight (beside the noise in the noise lesson) and its situation: an attack on the knight's row, a slash down its
    /// column, a noise two cells above it. It moves on only when the player has done it (<see cref="TutorialSteps"/>); a hit
    /// brings the attack back. When the resonance has played out, the closing line shows, a little larger; the lanes run
    /// empty; three seconds later the letterbox lifts as the song's battle is bound (the knight is lifted back to its start
    /// cell, the board stays in view) and the song starts on the beat after, as after the prologue.
    /// </summary>
    public sealed class TutorialDirector : MonoBehaviour
    {
        /// <summary>Beats before the tutorial's first note: one bar for the first line and the letterbox to settle.</summary>
        public const int LeadBeats = 4;
        private const int BarBeats = 4, LineBeats = 4, PraiseBeats = 2, Warning = 2, RetryGap = 2;
        // The tutorial's bars are thinner than the prologue's 2.39:1, so the knight's front row stays clear of them.
        private const float BarShare = .09f;
        /// <summary>Seconds the closing line holds before the letterbox lifts.</summary>
        public const double ClosingHoldSeconds = 3;
        /// <summary>The closing line's size: a little larger than the lessons' lines.</summary>
        public const float ClosingLineSize = 28;
        // Beats the letterbox takes to lift; the song starts on the first beat after it is gone.
        private const double LiftBeats = 1.5;
        // The lessons' attacks, as drawn in the boss chart: a rook across the knight's row (normal) and down its column (slash).
        public const string LessonShapes =
            "shape 룩가로 anchor=4,4 piece=룩\n00000000\n00000000\n00000000\n11111111\n00000000\n00000000\n00000000\n00000000\n" +
            "shape 룩세로강 anchor=4,4 piece=룩\n00020000\n00020000\n00020000\n00020000\n00020000\n00020000\n00020000\n00020000\n";

        private PrototypeGame game;
        private PrototypeView view;
        private PrologueDefinition data;
        private Letterbox letterbox;
        private KeyPromptCard card;
        private readonly List<AudioSource> sources = new List<AudioSource>();
        private BossChartShape rowShape, columnShape;
        private readonly TutorialSteps steps = new TutorialSteps();
        private BattleSession battle;
        private double origin, spb;
        private bool playing, built, afterPrologue;
        private int nextTick, nextSource;
        // The current lesson's line shows from lineBeat; its card and situation from activeBeat.
        private int lineBeat, activeBeat;
        private bool active;
        private string line = "";
        // Attack lessons: the note the shown attack falls on (-1 = none), and whether one is to be shown at the next note close.
        private int attackNote = -1, retryNote = -1;
        private bool attackPending;
        private GridPosition noiseCell;
        // The end: no tutorial note from laneEnd on; the letterbox lifts from liftBeat, the song's battle is bound on
        // finishBeat, and the song starts on handoverBeat.
        private int finishBeat = -1, handoverBeat = -1, fadeFrom = -1, laneEnd = int.MaxValue;
        private double liftBeat = double.MaxValue;
        private bool handedOver;

        public bool Playing => playing;
        public TutorialStep Step => steps.Step;
        public TutorialSteps Steps => steps;
        public KeyPromptCard Card => card;
        public Letterbox Letterbox => letterbox;
        /// <summary>Tutorial beats since its first note (negative in the lead-in).</summary>
        public double Beat => playing ? (AudioSettings.dspTime - origin) / spb : 0;
        /// <summary>The lesson's card is up (its situation is set).</summary>
        public bool LessonActive => playing && active && !steps.Finished;
        /// <summary>The cell of the noise lesson's noise.</summary>
        public GridPosition NoiseCell => noiseCell;
        /// <summary>The lanes show the tutorial's notes before this one only: after the last lesson they run empty for the song.</summary>
        public int LaneEnd => playing ? laneEnd : int.MaxValue;
        /// <summary>Tests: the DSP time of tutorial beat 0, the beat the song takes over (-1 before the end), the clock's fade start.</summary>
        internal double OriginDsp => origin;
        internal int HandoverBeat => handoverBeat;
        internal int FadeFromBeat => fadeFrom;

        public void Initialize(PrototypeGame owner, PrototypeView prototypeView, PrologueDefinition definition)
        {
            game = owner; view = prototypeView; data = definition;
            spb = game.Timeline.SecondsPerBeat;
            var chart = BossChart.Parse(LessonShapes);
            rowShape = chart.Shapes["룩가로"]; columnShape = chart.Shapes["룩세로강"];
            steps.Advanced += OnAdvanced;
        }
        private void Build()
        {
            built = true;
            var art = view.Art;
            var text = UiFonts.Korean(art.screenFont != null ? art.screenFont : art.menuFont, view.LatinFont);
            letterbox = new Letterbox(view.CanvasRect, "Tutorial Letterbox", text, art.skipKeyIcon);
            // The keys are cut from the HUD's key guide (WASD as its ㅗ cluster), so the cards teach the guide the song shows.
            card = new KeyPromptCard(view.BoardViewRect, "Tutorial Card", art, view.LatinFont, 72, 34, 30, art.keyGuide);
            for (int i = 0; i < 8; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
                sources.Add(source);
            }
        }
        /// <summary>
        /// Starts the lessons. <paramref name="startDsp"/> is the lead-in's first beat; the first note is a bar later.
        /// <paramref name="fromPrologue"/>: the prologue's 2.39:1 bars are already up and narrow down instead of sliding in.
        /// </summary>
        public void Play(double startDsp, bool fromPrologue)
        {
            if (!built) Build();
            spb = game.Timeline.SecondsPerBeat;
            origin = startDsp + LeadBeats * spb;
            afterPrologue = fromPrologue;
            Unsubscribe(); battle = game.Battle; Subscribe();
            steps.Reset();
            line = ""; letterbox.SetSubtitle("");
            // The first line is up at once, even before the lead-in's first beat; its card comes with the first note.
            lineBeat = int.MinValue / 2; activeBeat = 0; active = false;
            ResetSituation();
            nextTick = -LeadBeats; finishBeat = handoverBeat = fadeFrom = -1; handedOver = false;
            laneEnd = int.MaxValue; liftBeat = double.MaxValue;
            letterbox.Visible = true; card.Hide();
            playing = true;
            LateUpdate();
        }
        public void Stop()
        {
            if (!playing) return;
            playing = false;
            Unsubscribe();
            foreach (var source in sources) source.Stop();
            letterbox.Visible = false; card.Hide();
            if (view.Screens != null) view.Screens.KeyGuideAlpha = 1;
        }

        private void Subscribe()
        {
            if (battle == null) return;
            battle.Session.ActionEvaluated += OnAction;
            battle.NoteResolved += OnNote;
            battle.Noise.CollectedPickup += OnCollected;
            battle.Resonance.Ignited += OnIgnited;
        }
        private void Unsubscribe()
        {
            if (battle == null) return;
            battle.Session.ActionEvaluated -= OnAction;
            battle.NoteResolved -= OnNote;
            battle.Noise.CollectedPickup -= OnCollected;
            battle.Resonance.Ignited -= OnIgnited;
            battle = null;
        }
        private void ResetSituation() { attackNote = -1; retryNote = -1; attackPending = false; }

        // ---------------------------------------------------------------- the lessons
        private void OnAdvanced(TutorialStep step)
        {
            int now = Mathf.FloorToInt((float)Beat);
            active = false; ResetSituation(); card.Hide();
            if (step == TutorialStep.Done)
            {
                // The closing line on the next beat. No tutorial note enters the lanes after the ones already on them, so
                // they run empty before the song's battle is bound and nothing on them jumps.
                lineBeat = now + 1;
                laneEnd = now + game.Song.NoteApproachBeats + 1;
                // The letterbox lifts once the line has held; the song starts on the first beat after it is gone.
                liftBeat = lineBeat + ClosingHoldSeconds / spb;
                handoverBeat = (int)System.Math.Ceiling(liftBeat + LiftBeats);
                // Bound once the lanes are empty, two beats before the song so its audio is scheduled in good time.
                finishBeat = System.Math.Max(laneEnd, handoverBeat - 2);
                if (handoverBeat < finishBeat + 2) handoverBeat = finishBeat + 2;
                // From the next tick not yet queued, the clock dies away to the song's start.
                fadeFrom = nextTick;
                return;
            }
            lineBeat = now + PraiseBeats;
            activeBeat = lineBeat + LineBeats;
        }
        private void Activate()
        {
            active = true;
            var knight = battle.Session.Position;
            switch (steps.Step)
            {
                case TutorialStep.Attack:
                case TutorialStep.Slash:
                    attackPending = true; break;
                case TutorialStep.Noise:
                    noiseCell = battle.Noise.NearestFree(new GridPosition(knight.X, knight.Y + 2), knight);
                    battle.Noise.SpawnAt(noiseCell, knight);
                    break;
            }
        }
        private void OnAction(ActionResult result)
        {
            if (LessonActive && steps.Step == TutorialStep.Move && result.Accepted && result.Kind == PlayerActionKind.Move && result.Timing.IsHit)
                steps.Moved();
        }
        private void OnNote(NoteRecord record)
        {
            int note = record.noteIndex;
            // The strike lesson counts a resonance set off before its card came up (right after the noise, while the praise
            // and the line are still up): that strike spends the charge, and no other noise comes to charge the string again.
            if (playing && steps.Step == TutorialStep.Strike)
            {
                if (steps.Ignited && note >= battle.Resonance.EndNote) steps.ResonanceEnded();
                return;
            }
            if (!LessonActive) return;
            if (steps.Step == TutorialStep.Attack || steps.Step == TutorialStep.Slash)
            {
                if (note == attackNote)
                {
                    attackNote = -1;
                    if (steps.AttackFell(record.hit)) return;
                    attackPending = true; retryNote = note + RetryGap;
                }
                // Shown at a note close, as a chart line would be: anchored where the knight stands, falling two notes later.
                if (attackPending && note >= retryNote)
                {
                    var shown = battle.InjectAttack(steps.Step == TutorialStep.Attack ? rowShape : columnShape, Warning);
                    if (shown.Count > 0) { attackPending = false; attackNote = shown[0].ImpactNote; }
                }
            }
        }
        private void OnCollected(NoisePickup pickup) { if (LessonActive) steps.NoiseCollected(); }
        private void OnIgnited(int note) { if (playing && steps.Step == TutorialStep.Strike) steps.ResonanceStarted(); }

        // ---------------------------------------------------------------- each frame
        private void LateUpdate()
        {
            if (!playing) return;
            double now = AudioSettings.dspTime, beat = (now - origin) / spb;
            ScheduleTicks(now);
            if (!steps.Finished && !active && beat >= activeBeat && beat >= lineBeat) Activate();
            if (beat >= lineBeat && line != TutorialSteps.Guide(steps.Step))
            {
                line = TutorialSteps.Guide(steps.Step);
                letterbox.SetSubtitle(line, steps.Finished ? ClosingLineSize : Letterbox.SubtitleSize);
            }
            if (steps.Finished) Finish(beat);
            RenderLetterbox(beat);
            RenderCard(beat);
            if (view.Screens != null) view.Screens.KeyGuideAlpha = 0;
        }
        // The song's battle is bound on finishBeat, in full view: the lanes are empty by then and the knight is lifted back to
        // its start cell over a beat, like a piece set up again. The song starts on handoverBeat.
        private void Finish(double beat)
        {
            if (!handedOver && beat >= finishBeat)
            {
                handedOver = true;
                Unsubscribe();
                view.BoardWorld.LiftNextMove((float)spb);
                game.FinishTutorial(origin + handoverBeat * spb);
            }
        }
        private void RenderLetterbox(double beat)
        {
            var size = letterbox.Size;
            float cinema = Letterbox.CinemaBar(size), thin = size.y * BarShare;
            float settle = Smooth((float)beat, -LeadBeats, -LeadBeats + 1.5f);
            float bar = afterPrologue ? Mathf.Lerp(cinema, thin, settle) : thin;
            float amount = afterPrologue ? 1 : settle;
            if (handoverBeat >= 0) amount *= 1 - Smooth((float)beat, (float)liftBeat, (float)(liftBeat + LiftBeats));
            letterbox.Layout(bar, amount);
        }
        // What the lesson card shows now.
        private TutorialStep cardStep;
        private bool cardLit;
        private int cardLeft, cardLength;
        private void RenderCard(double beat)
        {
            if (!LessonActive || battle == null) { card.Hide(); return; }
            var resonance = battle.Resonance;
            int left = resonance.Remaining(battle.LastClosedNote + 1);
            bool lit = steps.Ignited && left > 0;
            // Built again only when the card's text changes (or it was hidden): the parts are new arrays and strings.
            if (!card.Visible || cardStep != steps.Step || cardLit != lit || cardLeft != left || cardLength != resonance.Length)
            {
                cardStep = steps.Step; cardLit = lit; cardLeft = left; cardLength = resonance.Length;
                card.Show(TutorialSteps.Card(steps.Step, lit, left, resonance.Length));
            }
            var board = view.BoardWorld;
            var anchor = steps.Step == TutorialStep.Noise ? BoardWorldPresenter.CellPosition(noiseCell) : board.PlayerTransform.localPosition;
            Place(board.ProjectToBoard(anchor, PrototypeView.BoardSize));
            card.Pulse((float)beat);
        }
        // Beside its anchor, on the right when that keeps clear of it (else the left), out of the lanes' band beside the board,
        // and inside the letterbox's opening.
        private void Place(Vector2 anchor)
        {
            float width = card.Size.x, height = card.Size.y;
            var area = view.ContentRect.rect.size;
            var center = (Vector2)view.BoardViewRect.localPosition;
            float halfRoom = area.x * .5f - 24;
            float min = -halfRoom - center.x + width * .5f, max = halfRoom - center.x - width * .5f;
            float right = Mathf.Clamp(anchor.x + 60 + width * .5f, min, max), left = Mathf.Clamp(anchor.x - 60 - width * .5f, min, max);
            bool rightClear = right - width * .5f >= anchor.x + 24;
            float x = rightClear ? right : left;
            float y = anchor.y + 40;
            if (LaneBand(out float bandMin, out float bandMax, out float judge) &&
                y + height * .5f > bandMin - 6 && y - height * .5f < bandMax + 6 && (x + width * .5f > judge - 6 || x - width * .5f < -judge + 6))
                y = anchor.y < (bandMin + bandMax) * .5f ? bandMin - height * .5f - 10 : bandMax + height * .5f + 10;
            float opening = area.y * .5f - letterbox.Bar;
            y = Mathf.Clamp(y, -opening - center.y + height * .5f + 12, opening - center.y - height * .5f - 12);
            card.Rect.anchoredPosition = new Vector2(x, y);
        }
        private readonly Vector3[] corners = new Vector3[4];
        private bool LaneBand(out float min, out float max, out float judge)
        {
            min = float.MaxValue; max = float.MinValue; judge = float.MaxValue;
            if (view.JudgeLines == null) return false;
            var judgeLines = view.JudgeLines;
            for (int i = 0; i < judgeLines.Count; i++)
            {
                var judgeLine = judgeLines[i];
                if (judgeLine == null) continue;
                judgeLine.rectTransform.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var p = view.BoardViewRect.InverseTransformPoint(corner);
                    min = Mathf.Min(min, p.y); max = Mathf.Max(max, p.y); judge = Mathf.Min(judge, Mathf.Abs(p.x));
                }
            }
            return judge < float.MaxValue;
        }
        // The chess clock on every beat, louder on the bar line, until the song takes over.
        private void ScheduleTicks(double now)
        {
            while (true)
            {
                if (handoverBeat >= 0 && nextTick >= handoverBeat) return;
                double at = origin + nextTick * spb;
                if (at > now + .25) return;
                bool accent = (nextTick % BarBeats + BarBeats) % BarBeats == 0;
                var tick = data.Tick(accent);
                if (at > now + .005 && tick != null)
                {
                    var source = sources[nextSource++ % sources.Count];
                    source.Stop(); source.clip = tick;
                    source.volume = data.volume * GameFlow.MusicVolume * data.TickVolume(accent) * ClockFade(nextTick, fadeFrom, handoverBeat);
                    source.PlayScheduled(at);
                }
                nextTick++;
            }
        }
        /// <summary>
        /// The clock's level on <paramref name="tick"/> as the tutorial ends: full until <paramref name="from"/>, then dying away
        /// tick by tick (squared, so it fades as the ear hears it) to nearly nothing on the last tick before
        /// <paramref name="handover"/>, where the song's intro starts. Full while the tutorial runs (<paramref name="from"/> &lt; 0).
        /// </summary>
        public static float ClockFade(int tick, int from, int handover)
        {
            if (from < 0 || handover <= from || tick < from) return 1;
            float u = Mathf.Clamp01((float)(handover - tick) / (handover - from + 1));
            return u * u;
        }
        private static float Smooth(float s, float a, float b) { float u = Mathf.Clamp01((s - a) / (b - a)); return u * u * (3 - 2 * u); }
        private void OnDestroy() { steps.Advanced -= OnAdvanced; Unsubscribe(); }
    }
}
