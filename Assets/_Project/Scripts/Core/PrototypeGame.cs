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
        private BeatScheduler scheduler;
        private PlayerInputAdapter input;
        private ScheduledSongAudio audioPlayer;
        private PrototypeView view;
        private double previousTime;
        public SongDefinition Song => song;
        public IBeatTimeline Timeline { get; private set; }
        public RhythmSession Session { get; private set; }
        public NoisePickupSystem Noise { get; private set; }
        public StageDefinition Stage => stage;
        public AttackResolver Attacks { get; private set; }
        public BossHealthGate Health { get; private set; }
        public CombatResolver Combat { get; private set; }
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
            scheduler = new BeatScheduler(Timeline);
            if (stage == null) stage = ScriptableObject.CreateInstance<StageDefinition>();
            Noise = new NoisePickupSystem(new BoardState(), stage.NoiseSpawnInterval, stage.MaxActiveNoise);
            Attacks = new AttackResolver(new BoardState());
            Health = new BossHealthGate(stage.HealthSegments);
            Combat = new CombatResolver(Attacks, Health, stage.AttackDamage);
            Session = new RhythmSession(new BoardState(), new RhythmJudge(Timeline, song.PerfectWindow, song.GoodWindow, song.InputOffset), new GridPosition(3, 3));
            input = GetComponent<PlayerInputAdapter>(); audioPlayer = GetComponent<ScheduledSongAudio>();
            view = GetComponentInChildren<PrototypeView>(true);
            if (view == null) { Debug.LogError("Prototype view is missing.", this); enabled = false; return; }
            view.Initialize(this);
            input.ActionPressed += OnAction; input.CommandPressed += OnCommand;
            Session.ActionEvaluated += OnActionEvaluated; Session.NoteMissed += view.ShowMiss;
            Session.BeatClosed += OnBeatClosed;
            Combat.DamageResolved += view.ShowDamage;
        }
        public void StartSession()
        {
            input.Clear(); audioPlayer.Stop(); Session.Reset(); scheduler.Reset();
            CurrentBeat = -1; SessionId++;
            Health.Reset(); Combat.Reset();
            Noise.Reset(SessionId, stage.NoiseSeed, stage.InitialNoiseTarget);
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
            Session.RequestAction(request.Kind, request.Direction, time);
        }
        private void OnActionEvaluated(ActionResult result)
        {
            if (result.Accepted && result.Kind == PlayerActionKind.Move)
                Noise.TryCollect(result.Position, SessionId, Noise.PhaseId);
            Combat.Submit(result);
            view.ShowAction(result);
        }
        private void OnBeatClosed(int beat) => Combat.ResolveBeat(beat);
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
                scheduler.Advance(time, beat => { CurrentBeat = beat; Noise.AdvanceToBeat(beat, Session.Position); view.Pulse(); });
                Session.Advance(time);
                if (song.Music != null && time >= song.Music.length - song.AudioStartOffset)
                { clock.Stop(); audioPlayer.Stop(); State = PrototypeState.Finished; }
                previousTime = time;
            }
            view.Render();
        }
        private void OnApplicationFocus(bool focused) { if (!focused && pauseOnFocusLoss && clock != null) PauseSession(); }
        private void OnApplicationPause(bool paused) { if (paused && clock != null) PauseSession(); }
        private void OnDestroy()
        {
            if (input != null) { input.ActionPressed -= OnAction; input.CommandPressed -= OnCommand; }
            if (Session != null && view != null)
            {
                Session.ActionEvaluated -= OnActionEvaluated; Session.NoteMissed -= view.ShowMiss;
                Session.BeatClosed -= OnBeatClosed; Combat.DamageResolved -= view.ShowDamage;
            }
            clock?.Stop(); if (audioPlayer != null) audioPlayer.Stop();
        }
    }
}
