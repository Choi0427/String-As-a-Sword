using StringAsASword.Board;
using StringAsASword.Player;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using StringAsASword.Stage;
using StringAsASword.Combat;
using UnityEngine;

namespace StringAsASword.Core
{
    public enum PrototypeState { Ready, CountIn, Playing, Paused, Finished }

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

        private void Awake()
        {
            if (song == null) { Debug.LogError("Assign a SongDefinition to PrototypeGame.", this); enabled = false; return; }
            Timeline = new ConstantBeatTimeline(song.Bpm);
            clock = new SongClock(() => AudioSettings.dspTime);
            if (stage == null) stage = ScriptableObject.CreateInstance<StageDefinition>();
            Battle = new BattleSession(BattleSettings.From(song, stage), stage.NoiseSeed);
            Noise = Battle.Noise; Attacks = Battle.Attacks; Health = Battle.Health; Combat = Battle.Combat;
            Phases = Battle.Phases; Session = Battle.Session;
            Battle.MusicBeat += beat => { CurrentBeat = beat; view.Pulse(); };
            input = GetComponent<PlayerInputAdapter>(); audioPlayer = GetComponent<ScheduledSongAudio>();
            view = GetComponentInChildren<PrototypeView>(true);
            if (view == null) { Debug.LogError("Prototype view is missing.", this); enabled = false; return; }
            view.Initialize(this);
            input.ActionPressed += OnAction; input.CommandPressed += OnCommand;
            Session.ActionEvaluated += OnActionEvaluated; Session.NoteMissed += view.ShowMiss;
            Combat.DamageResolved += view.ShowDamage;
            Phases.PhaseUnlocked += view.ShowPhase;
        }
        public void StartSession()
        {
            SaveRun("Restarted");
            input.Clear(); audioPlayer.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false;
            Battle.Reset(SessionId, stage.NoiseSeed);
            clock.Start(.30 + song.CountInBeats * Timeline.SecondsPerBeat + song.AudioStartOffset);
            previousTime = SongTime;
            State = PrototypeState.CountIn; view.ResetFeedback();
            audioPlayer.Begin(song, clock, Timeline);
        }
        public void PauseSession()
        {
            if (State != PrototypeState.Playing && State != PrototypeState.CountIn) return;
            clock.Stop(); audioPlayer.Stop(); input.Clear(); State = PrototypeState.Paused;
        }
        private void OnCommand(string command)
        {
            if (command == "restart" || command == "start" && State != PrototypeState.Playing && State != PrototypeState.CountIn) StartSession();
            else if (command == "pause") PauseSession();
            else if (command == "mute") audioPlayer.ToggleMute();
        }
        private void OnAction(PlayerInputAdapter.TimedAction request)
        {
            if (!clock.Running) return;
            double time = clock.ToSongTime(request.DspTime);
            // The first note has the same early Good window as all later notes.
            if (time < -song.GoodWindow + song.InputOffset) return;
            Battle.Request(request.Kind, request.Direction, time);
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
                if (time >= 0) State = PrototypeState.Playing;
                audioPlayer.Schedule();
                AdvanceWorld(time);
                if (Battle.Outcome != BattleOutcome.Running)
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
            if (input != null) { input.ActionPressed -= OnAction; input.CommandPressed -= OnCommand; }
            if (Session != null && view != null)
            {
                Session.ActionEvaluated -= OnActionEvaluated; Session.NoteMissed -= view.ShowMiss;
                Combat.DamageResolved -= view.ShowDamage;
                Phases.PhaseUnlocked -= view.ShowPhase;
            }
            clock?.Stop(); if (audioPlayer != null) audioPlayer.Stop();
        }
    }
}
