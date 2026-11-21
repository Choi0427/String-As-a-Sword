using StringAsASword.Board;
using StringAsASword.Player;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using StringAsASword.Stage;
using StringAsASword.Combat;
using UnityEngine;

namespace StringAsASword.Core
{
    // Finale: the battle is won, the song plays on with no judgement. Epilogue: free movement and the ending.
    // Prologue: the opening cutscene; its last downbeat is the song's start, and it hands over straight to Playing.
    public enum PrototypeState { Ready, CountIn, Playing, Paused, Finished, Finale, Epilogue, Prologue }

    [RequireComponent(typeof(PlayerInputAdapter), typeof(ScheduledSongAudio))]
    public sealed class PrototypeGame : MonoBehaviour
    {
        [SerializeField] private SongDefinition song;
        [SerializeField] private StageDefinition stage;
        [SerializeField] private bool pauseOnFocusLoss = true;
        private SongClock clock;
        private PlayerInputAdapter input;
        private ScheduledSongAudio audioPlayer;
        private PrototypeView view;
        private double previousTime;
        private bool runSaved;
        // The judge's input sync is fixed when the battle is built; a change in the pause settings needs a fresh scene.
        private int inputOffsetMs;
        public SongDefinition Song => song;
        public IBeatTimeline Timeline { get; private set; }
        public RhythmSession Session { get; private set; }
        public NoisePickupSystem Noise { get; private set; }
        public StageDefinition Stage => stage;
        public AttackResolver Attacks { get; private set; }
        public BossHealthGate Health { get; private set; }
        public CombatResolver Combat { get; private set; }
        public PhaseController Phases { get; private set; }
        public BattleSession Battle { get; private set; }
        public PrototypeState State { get; private set; } = PrototypeState.Ready;
        public double SongTime => clock?.SongTime ?? 0;
        public int CurrentBeat { get; private set; } = -1;
        public int SessionId { get; private set; }
        public bool MetronomeMuted => audioPlayer.Muted;
        public double VisualOffset => song.VisualOffset + GameFlow.VisualOffsetMs / 1000.0;
        /// <summary>Song time at which the finale hands over to the epilogue: just after the last note (or its hold) ends.</summary>
        public double FinaleEndTime { get; private set; }
        /// <summary>Song time of the finishing triplet's last strike: the king falls and the board is free to walk.</summary>
        public double FinaleStrikeTime { get; private set; }
        public event System.Action FinaleStarted, EpilogueStarted;
        public event System.Action<PlayerActionKind> FinaleTap;
        public ScheduledSongAudio Audio => audioPlayer;
        public PrototypeView View => view;
        private IntroDirector intro;
        /// <summary>The prologue cutscene; null when its data (Resources/Prologue) is missing.</summary>
        public IntroDirector Intro => intro;
        /// <summary>DSP time to song time on the battle clock (valid once the clock runs).</summary>
        public double SongClockOrigin => clock.Origin;
        /// <summary>After a resume the song rewinds a count-in; actions count again from here (song time). -inf otherwise.</summary>
        public double ResumeAt { get; private set; } = double.NegativeInfinity;

        private void Awake()
        {
            if (song == null) { Debug.LogError("Assign a SongDefinition to PrototypeGame.", this); enabled = false; return; }
            clock = new SongClock(() => AudioSettings.dspTime);
            if (stage == null) stage = ScriptableObject.CreateInstance<StageDefinition>();
            var settings = BattleSettings.From(song, stage);
            inputOffsetMs = GameFlow.InputOffsetMs;
            settings.InputOffset += inputOffsetMs / 1000.0;
            Timeline = settings.CreateTimeline();
            Battle = new BattleSession(settings, stage.NoiseSeed);
            Noise = Battle.Noise; Attacks = Battle.Attacks; Health = Battle.Health; Combat = Battle.Combat;
            Phases = Battle.Phases; Session = Battle.Session;
            Battle.MusicBeat += beat => { CurrentBeat = beat; view.Pulse(); };
            input = GetComponent<PlayerInputAdapter>(); audioPlayer = GetComponent<ScheduledSongAudio>();
            view = GetComponentInChildren<PrototypeView>(true);
            if (view == null) { Debug.LogError("Prototype view is missing.", this); enabled = false; return; }
            view.Initialize(this);
            input.ActionPressed += OnAction; input.ActionReleased += OnRelease; input.CommandPressed += OnCommand;
            Session.HoldResolved += view.ShowHold;
            Session.ActionEvaluated += OnActionEvaluated; Session.NoteMissed += view.ShowMiss;
            Combat.DamageResolved += view.ShowDamage;
            Phases.PhaseUnlocked += view.ShowPhase;
            Noise.CollectedPickup += view.ShowNoiseCollected;
            Battle.Resonance.Ignited += view.ShowIgnition; Battle.Resonance.GhostStrike += view.ShowGhostStrike;
            int last = song.NoteCount - 1;
            double end = Timeline.TimeOf(last), hold = Session.HoldEnd(last);
            FinaleEndTime = System.Math.Max(end, hold) + .8;
            FinaleStrikeTime = Timeline.TimeOf(System.Math.Min(settings.FinishNote + 2, last));
            view.gameObject.AddComponent<FinaleDirector>().Initialize(this, view);
            var prologue = Resources.Load<PrologueDefinition>(PrologueDefinition.ResourceName);
            if (prologue != null) { intro = view.gameObject.AddComponent<IntroDirector>(); intro.Initialize(this, view, prologue); }
        }
        private void Start()
        {
            if (!enabled || !GameFlow.AutoStartBattle) return;
            bool prologue = GameFlow.PlayPrologue;
            GameFlow.ConsumeAutoStart();
            // A developer start in a later phase has no prologue.
            if (prologue && intro != null && GameFlow.StartPhase == 1) StartPrologue(); else StartSession();
        }
        /// <summary>
        /// The opening cutscene. The battle is reset but its clock waits: the director fixes the song's start once the player
        /// tears the string (J), and hands over on that downbeat. Esc or Space skips to the usual count-in.
        /// </summary>
        public void StartPrologue()
        {
            if (intro == null) { StartSession(); return; }
            SaveRun("Restarted");
            input.Clear(); audioPlayer.Stop(); clock.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false; ResumeAt = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            Battle.Invincible = GameFlow.Invincible; Battle.IgnoreBossHp = GameFlow.BossHpOff;
            State = PrototypeState.Prologue; view.ResetFeedback();
            ScheduledSongAudio.Preload(song);
            intro.Play();
        }
        /// <summary>The director has fixed the handover: song time <paramref name="songTime"/> falls on DSP time <paramref name="dsp"/>.</summary>
        public void ScheduleSongFromPrologue(double dsp, double songTime)
        {
            if (State != PrototypeState.Prologue) return;
            clock.StartAt(dsp - songTime);
            previousTime = SongTime;
            audioPlayer.Begin(song, clock, Timeline, countInClicks: false);
        }
        /// <summary>The prologue's last downbeat: the battle is live (the first note is still the song's intro away).</summary>
        public void FinishPrologue()
        {
            if (State != PrototypeState.Prologue) return;
            GameFlow.MarkPrologueSeen();
            input.Clear();
            previousTime = SongTime;
            State = PrototypeState.Playing;
        }
        public void SkipPrologue()
        {
            if (State != PrototypeState.Prologue) return;
            intro.Stop();
            GameFlow.MarkPrologueSeen();
            StartSession();
        }
        public void StartSession()
        {
            SaveRun("Restarted");
            if (intro != null && intro.Playing) intro.Stop();
            input.Clear(); audioPlayer.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false; ResumeAt = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            Battle.SkipToPhase(GameFlow.StartPhase);
            // Developer mode: a later phase also starts later in the song, a count-in's length before its note.
            double countIn = song.CountInBeats * Timeline.SecondsPerBeat;
            double from = Timeline.TimeOf(DevPhaseNote(GameFlow.StartPhase)) - countIn;
            if (from > 0)
            {
                // The skipped notes pass unplayed and unhurt, so the attacks, the noise and the board are where the song is.
                Battle.Invincible = true; Battle.Advance(from);
                clock.Start(.30 - from);
            }
            else clock.Start(.30 + countIn + song.AudioStartOffset);
            Battle.Invincible = GameFlow.Invincible; Battle.IgnoreBossHp = GameFlow.BossHpOff;
            previousTime = SongTime;
            State = PrototypeState.CountIn; view.ResetFeedback();
            audioPlayer.Begin(song, clock, Timeline);
        }
        /// <summary>Tests only: skip the battle and the finale and start the epilogue now.</summary>
        internal void JumpToEpilogueForTests()
        {
            if (!clock.Running) StartSession();
            State = PrototypeState.Epilogue; EpilogueStarted?.Invoke();
        }
        public void PauseSession()
        {
            if (State != PrototypeState.Playing && State != PrototypeState.CountIn) return;
            clock.Stop(); audioPlayer.Stop(); input.Clear(); State = PrototypeState.Paused;
        }
        /// <summary>
        /// Pause screen "판으로 복귀": the song picks up a count-in before where it stopped, so the player can find the beat.
        /// The notes in that stretch were already judged; nothing in it is judged again.
        /// </summary>
        public void ResumeSession()
        {
            if (State != PrototypeState.Paused) return;
            double at = clock.SongTime, rewind = song.CountInBeats * Timeline.SecondsPerBeat;
            ResumeAt = at;
            clock.Start(.30 + rewind - at);
            previousTime = SongTime; input.Clear();
            State = at < 0 ? PrototypeState.CountIn : PrototypeState.Playing;
            audioPlayer.Begin(song, clock, Timeline);
        }
        /// <summary>Game over "첫 수부터": a fresh battle, in a fresh scene when the input sync changed since this one was built.</summary>
        public void RestartFromMenu()
        {
            if (inputOffsetMs != GameFlow.InputOffsetMs && GameFlow.HasMenu) { SaveRun("Restarted"); GameFlow.StartBattle(); return; }
            StartSession();
        }
        /// <summary>The pause settings closed: apply what can change mid-battle (developer switches; volume and sync apply on resume).</summary>
        public void ApplySettings()
        {
            Battle.Invincible = GameFlow.Invincible; Battle.IgnoreBossHp = GameFlow.BossHpOff;
        }
        public void QuitGame()
        {
            PauseSession();
            SaveRun("Quit");
            if (GameFlow.HasMenu) { GameFlow.ReturnToMenu(); return; }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        /// <summary>Developer mode: restarts the battle in this phase (1..4), at the place in the song where it usually opens.</summary>
        public void DevGoToPhase(int phase)
        {
            if (!GameFlow.DevMode) return;
            GameFlow.StartPhase = phase; StartSession();
        }
        /// <summary>
        /// The note a phase is taken to start on. Phases open when the player empties the boss, not at fixed notes, so this is
        /// an estimate: the share of boss HP before the phase, as a share of the notes before the finishing run, moved back
        /// to the start of its chart section.
        /// </summary>
        private static readonly string[] DevPhaseSections = { "간주2", "Interlude", "싸비3" };
        public int DevPhaseNote(int phase)
        {
            var settings = Battle.Settings;
            if (phase <= 1) return 0;
            // Chosen by hand for the boss chart; the estimate below is for charts without these sections.
            if (settings.Chart != null && phase <= 4)
                foreach (var section in settings.Chart.Sections) if (section.Name == DevPhaseSections[phase - 2]) return section.From;
            int total = 0, before = 0;
            for (int i = 0; i < settings.Health.Length; i++) { total += settings.Health[i]; if (i < phase - 1) before += settings.Health[i]; }
            int note = (int)((long)(settings.HasFinale ? settings.FinishNote : settings.NoteCount) * before / total);
            if (settings.Chart != null)
                foreach (var section in settings.Chart.Sections) if (section.From <= note && note <= section.To) note = section.From;
            return note;
        }
        public void DevSetInvincible(bool on)
        {
            if (!GameFlow.DevMode) return;
            GameFlow.Invincible = on; Battle.Invincible = on;
        }
        public void DevSetBossHp(bool on)
        {
            if (!GameFlow.DevMode) return;
            GameFlow.BossHpOff = !on; Battle.IgnoreBossHp = !on;
        }
        private void OnCommand(string command)
        {
            if (State == PrototypeState.Finale || State == PrototypeState.Epilogue) return; // The ending plays through.
            // The prologue has no pause menu: Esc, Space or Enter skip it to the count-in. Nothing else applies yet.
            if (State == PrototypeState.Prologue)
            {
                if (command == "pause" || command == "start") SkipPrologue();
                else if (command == "mute") audioPlayer.ToggleMute();
                return;
            }
            if (command.StartsWith("dev-phase")) { DevGoToPhase(command[command.Length - 1] - '0'); return; }
            if (command == "dev-invincible") { DevSetInvincible(!GameFlow.Invincible); return; }
            if (command == "dev-bosshp") { DevSetBossHp(GameFlow.BossHpOff); return; }
            if (command == "restart") StartSession();
            // Pause and game over have their own menus (Enter/Space press the picked item); Space starts only from the ready card.
            else if (command == "start" && State == PrototypeState.Ready) StartSession();
            else if (command == "pause")
            {
                if (State != PrototypeState.Paused) PauseSession();
                else if (!view.Screens.Back()) ResumeSession();
            }
            else if (command == "mute") audioPlayer.ToggleMute();
        }
        private void OnAction(PlayerInputAdapter.TimedAction request)
        {
            // In the prologue J tears the string; the battle does not take actions yet.
            if (State == PrototypeState.Prologue) { if (request.Kind == PlayerActionKind.Attack) intro.Tear(request.DspTime); return; }
            if (!clock.Running) return;
            double time = clock.ToSongTime(request.DspTime);
            if (State == PrototypeState.Finale || State == PrototypeState.Epilogue)
            {
                FinaleTap?.Invoke(request.Kind);
                // After the finishing triplet the knight walks the board freely until the epilogue takes over.
                if (State == PrototypeState.Finale && request.Kind == PlayerActionKind.Move && time >= FinaleStrikeTime)
                    Session.MoveFreely(request.Direction);
                return;
            }
            // The first note has the same early Good window as all later notes.
            if (time < -song.GoodWindow + song.InputOffset) return;
            // After a resume the rewound notes are already judged.
            if (time < ResumeAt - song.GoodWindow) return;
            Battle.Request(request.Kind, request.Direction, time);
        }
        private void OnRelease(PlayerInputAdapter.TimedAction request)
        {
            if (!clock.Running || State == PrototypeState.Finale || State == PrototypeState.Epilogue || State == PrototypeState.Prologue) return;
            Battle.Release(request.Kind, request.Direction, clock.ToSongTime(request.DspTime));
        }
        private void OnActionEvaluated(ActionResult result)
        {
            view.ShowAction(result);
        }
        private void AdvanceWorld(double time)
        {
            Battle.Advance(time);
        }
        private void Update()
        {
            if (clock == null) return;
            if (clock.Running && SongTime - previousTime > 2)
                PauseSession(); // A long stall must not replay old queued actions against future notes.
            input.Drain();
            if (clock.Running)
            {
                double time = SongTime;
                if (time >= 0 && State == PrototypeState.CountIn) State = PrototypeState.Playing;
                audioPlayer.Schedule();
                AdvanceWorld(time);
                if (State == PrototypeState.Finale && time >= FinaleEndTime)
                { State = PrototypeState.Epilogue; EpilogueStarted?.Invoke(); }
                else if (Battle.Outcome == BattleOutcome.Won && Battle.Settings.HasFinale && (State == PrototypeState.Playing || State == PrototypeState.CountIn))
                {
                    // Won on the finishing run: keep the song and the notes going, without judgement.
                    State = PrototypeState.Finale; SaveRun("Completed"); FinaleStarted?.Invoke();
                }
                else if (Battle.Outcome != BattleOutcome.Running && (State == PrototypeState.Playing || State == PrototypeState.CountIn))
                {
                    clock.Stop(); audioPlayer.Stop(); State = PrototypeState.Finished;
                    SaveRun("Completed");
                }
                previousTime = time;
            }
            view.Render();
        }
        private void OnApplicationFocus(bool focused) { if (!focused && pauseOnFocusLoss && clock != null) PauseSession(); }
        private void OnApplicationPause(bool paused) { if (paused && clock != null) PauseSession(); }
        private void SaveRun(string termination)
        {
            if (Battle == null || SessionId == 0 || runSaved) return;
            try
            {
                BattleLog.Write(System.IO.Path.Combine(Application.persistentDataPath, "BattleLogs",
                    $"battle-{System.DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{SessionId}.jsonl"), Battle, termination);
                runSaved = true;
            }
            catch (System.IO.IOException error) { Debug.LogError("Could not write battle log: " + error.Message); }
            catch (System.UnauthorizedAccessException error) { Debug.LogError("Could not write battle log: " + error.Message); }
        }
        private void OnDestroy()
        {
            SaveRun("Interrupted");
            if (input != null) { input.ActionPressed -= OnAction; input.ActionReleased -= OnRelease; input.CommandPressed -= OnCommand; }
            if (Session != null && view != null)
            {
                Session.HoldResolved -= view.ShowHold;
                Session.ActionEvaluated -= OnActionEvaluated; Session.NoteMissed -= view.ShowMiss;
                Combat.DamageResolved -= view.ShowDamage;
                Phases.PhaseUnlocked -= view.ShowPhase;
                Noise.CollectedPickup -= view.ShowNoiseCollected;
                Battle.Resonance.Ignited -= view.ShowIgnition; Battle.Resonance.GhostStrike -= view.ShowGhostStrike;
            }
            clock?.Stop(); if (audioPlayer != null) audioPlayer.Stop();
        }
    }
}
