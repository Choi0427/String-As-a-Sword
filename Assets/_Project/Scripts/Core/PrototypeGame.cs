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
    public enum PrototypeState { Ready, CountIn, Playing, Paused, Finished, Finale, Epilogue }

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

        private void Awake()
        {
            if (song == null) { Debug.LogError("Assign a SongDefinition to PrototypeGame.", this); enabled = false; return; }
            clock = new SongClock(() => AudioSettings.dspTime);
            if (stage == null) stage = ScriptableObject.CreateInstance<StageDefinition>();
            var settings = BattleSettings.From(song, stage);
            settings.InputOffset += GameFlow.InputOffsetMs / 1000.0;
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
            int last = song.NoteCount - 1;
            double end = Timeline.TimeOf(last), hold = Session.HoldEnd(last);
            FinaleEndTime = System.Math.Max(end, hold) + .8;
            FinaleStrikeTime = Timeline.TimeOf(System.Math.Min(settings.FinishNote + 2, last));
            view.gameObject.AddComponent<FinaleDirector>().Initialize(this, view);
        }
        private void Start()
        {
            if (!enabled || !GameFlow.AutoStartBattle) return;
            GameFlow.ConsumeAutoStart(); StartSession();
        }
        public void StartSession()
        {
            SaveRun("Restarted");
            input.Clear(); audioPlayer.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false;
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
            if (command.StartsWith("dev-phase")) { DevGoToPhase(command[command.Length - 1] - '0'); return; }
            if (command == "dev-invincible") { DevSetInvincible(!GameFlow.Invincible); return; }
            if (command == "dev-bosshp") { DevSetBossHp(GameFlow.BossHpOff); return; }
            if (command == "restart" || command == "start" && State != PrototypeState.Playing && State != PrototypeState.CountIn) StartSession();
            else if (command == "pause") PauseSession();
            else if (command == "mute") audioPlayer.ToggleMute();
        }
        private void OnAction(PlayerInputAdapter.TimedAction request)
        {
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
            Battle.Request(request.Kind, request.Direction, time);
        }
        private void OnRelease(PlayerInputAdapter.TimedAction request)
        {
            if (!clock.Running || State == PrototypeState.Finale || State == PrototypeState.Epilogue) return;
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
            }
            clock?.Stop(); if (audioPlayer != null) audioPlayer.Stop();
        }
    }
}
