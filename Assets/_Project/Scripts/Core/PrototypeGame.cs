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
    // Prologue: the opening cutscene; its last downbeat hands over to the tutorial (or straight to Playing without one).
    // Tutorial: a practice battle of its own, on an even beat with no song; its end hands over to the song's start.
    // Dying: the last hit landed; the board is frozen while the knight topples (DeathDirector), then Finished.
    public enum PrototypeState { Ready, CountIn, Playing, Paused, Finished, Finale, Epilogue, Prologue, Tutorial, Dying }

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
        private TutorialDirector tutorial;
        /// <summary>The tutorial; null when the prologue's data (its clock tick) is missing.</summary>
        public TutorialDirector Tutorial => tutorial;
        private DeathDirector death;
        /// <summary>The knight's fall after the last hit.</summary>
        public DeathDirector Death => death;
        // The song's battle, and the tutorial's: a battle of its own on an even beat, bound in its place while it runs.
        private BattleSession mainBattle, tutorialBattle;
        private IBeatTimeline mainTimeline, tutorialTimeline;
        // DSP time the tutorial hands over to the song (-1 = none pending).
        private double tutorialHandover = -1;
        /// <summary>Notes on the tutorial's even beat: far more than it ever takes.</summary>
        public const int TutorialNotes = 4000;
        /// <summary>The tutorial's battle is the one bound now.</summary>
        public bool InTutorialBattle => Battle != null && Battle == tutorialBattle;
        /// <summary>DSP time to song time on the battle clock (valid once the clock runs).</summary>
        public double SongClockOrigin => clock.Origin;
        /// <summary>After a resume the song rewinds a count-in; actions count again from here (song time). -inf otherwise.</summary>
        public double ResumeAt { get; private set; } = double.NegativeInfinity;
        /// <summary>
        /// After a resume: the song's beat at or before <see cref="ResumeAt"/>, where the 4-3-2-1 count lands. The count runs
        /// on the music's own beats from here, not from the moment of the pause. -inf otherwise.
        /// </summary>
        public double CountdownEnd { get; private set; } = double.NegativeInfinity;

        private void Awake()
        {
            if (song == null) { Debug.LogError("Assign a SongDefinition to PrototypeGame.", this); enabled = false; return; }
            clock = new SongClock(() => AudioSettings.dspTime);
            if (stage == null) stage = ScriptableObject.CreateInstance<StageDefinition>();
            var settings = BattleSettings.From(song, stage);
            inputOffsetMs = GameFlow.InputOffsetMs;
            settings.InputOffset += inputOffsetMs / 1000.0;
            mainTimeline = settings.CreateTimeline();
            mainBattle = new BattleSession(settings, stage.NoiseSeed);
            SetBattle(mainBattle, mainTimeline);
            input = GetComponent<PlayerInputAdapter>(); audioPlayer = GetComponent<ScheduledSongAudio>();
            view = GetComponentInChildren<PrototypeView>(true);
            if (view == null) { Debug.LogError("Prototype view is missing.", this); enabled = false; return; }
            view.Initialize(this);
            input.ActionPressed += OnAction; input.ActionReleased += OnRelease; input.CommandPressed += OnCommand;
            Attach();
            int last = song.NoteCount - 1;
            double end = Timeline.TimeOf(last), hold = Session.HoldEnd(last);
            FinaleEndTime = System.Math.Max(end, hold) + .8;
            FinaleStrikeTime = Timeline.TimeOf(System.Math.Min(settings.FinishNote + 2, last));
            view.gameObject.AddComponent<FinaleDirector>().Initialize(this, view);
            death = view.gameObject.AddComponent<DeathDirector>(); death.Initialize(this, view);
            var prologue = Resources.Load<PrologueDefinition>(PrologueDefinition.ResourceName);
            if (prologue != null) { intro = view.gameObject.AddComponent<IntroDirector>(); intro.Initialize(this, view, prologue); }
            if (prologue != null) { tutorial = view.gameObject.AddComponent<TutorialDirector>(); tutorial.Initialize(this, view, prologue); }
        }
        // Every presenter reads the bound battle each frame; these are the event hooks that have to move with it.
        private void SetBattle(BattleSession battle, IBeatTimeline timeline)
        {
            Battle = battle; Timeline = timeline;
            Noise = battle.Noise; Attacks = battle.Attacks; Health = battle.Health; Combat = battle.Combat;
            Phases = battle.Phases; Session = battle.Session;
        }
        private void Attach()
        {
            Battle.MusicBeat += OnMusicBeat;
            Session.HoldResolved += view.ShowHold;
            Session.ActionEvaluated += OnActionEvaluated; Session.NoteMissed += view.ShowMiss;
            Combat.DamageResolved += view.ShowDamage;
            Phases.PhaseUnlocked += view.ShowPhase;
            Noise.CollectedPickup += view.ShowNoiseCollected;
            Battle.Resonance.Ignited += view.ShowIgnition; Battle.Resonance.GhostStrike += view.ShowGhostStrike;
        }
        private void Detach()
        {
            Battle.MusicBeat -= OnMusicBeat;
            Session.HoldResolved -= view.ShowHold;
            Session.ActionEvaluated -= OnActionEvaluated; Session.NoteMissed -= view.ShowMiss;
            Combat.DamageResolved -= view.ShowDamage;
            Phases.PhaseUnlocked -= view.ShowPhase;
            Noise.CollectedPickup -= view.ShowNoiseCollected;
            Battle.Resonance.Ignited -= view.ShowIgnition; Battle.Resonance.GhostStrike -= view.ShowGhostStrike;
        }
        /// <summary>Puts <paramref name="battle"/> on the board: the presenters follow it from the next frame.</summary>
        private void Bind(BattleSession battle, IBeatTimeline timeline)
        {
            if (Battle == battle) return;
            Detach(); SetBattle(battle, timeline); Attach();
            SessionId++; view.ResetFeedback();
        }
        // The judge lines answer judgements, not the beat (PrototypeView.Judge).
        private void OnMusicBeat(int beat) => CurrentBeat = beat;
        private void Start()
        {
            if (!enabled || !GameFlow.AutoStartBattle) return;
            bool prologue = GameFlow.PlayPrologue, practice = GameFlow.PlayTutorial;
            GameFlow.ConsumeAutoStart();
            // A developer start in a later phase has no prologue and no tutorial.
            if (GameFlow.StartPhase != 1) StartSession();
            else if (prologue && intro != null) StartPrologue();
            else if (practice && tutorial != null) StartTutorial();
            else StartSession();
        }
        /// <summary>
        /// The opening cutscene. The battle is reset but its clock waits: the director fixes the song's start once the player
        /// tears the string (J), and hands over on that downbeat. Esc or Space skips to the usual count-in.
        /// </summary>
        public void StartPrologue()
        {
            if (intro == null) { StartSession(); return; }
            SaveRun("Restarted");
            StopTutorial(); Bind(mainBattle, mainTimeline);
            input.Clear(); audioPlayer.Stop(); clock.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false; ResumeAt = CountdownEnd = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            ApplySwitches();
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
            input.Clear();
            previousTime = SongTime;
            State = PrototypeState.Playing;
        }
        /// <summary>
        /// The prologue is always followed by the tutorial, like it is always played (Esc skips either): its last downbeat goes
        /// there, not to the song. Only without a tutorial in the scene does it hand over to the song itself.
        /// </summary>
        public bool TutorialFollowsPrologue => tutorial != null;
        /// <summary>The prologue's last downbeat, with the tutorial after it: the letterbox stays and the practice battle starts.</summary>
        public void PrologueIntoTutorial(double handoverDsp)
        {
            if (State != PrototypeState.Prologue) return;
            StartTutorial(handoverDsp, afterPrologue: true);
        }
        /// <summary>Esc in the prologue: on to the tutorial when it follows this prologue, else the song.</summary>
        public void SkipPrologue()
        {
            if (State != PrototypeState.Prologue) return;
            intro.Stop();
            if (TutorialFollowsPrologue) StartTutorial(); else StartSession();
        }
        /// <summary>Settings > 튜토리얼, or after the prologue: the tutorial now, then the song.</summary>
        public void StartTutorial() => StartTutorial(AudioSettings.dspTime + .35);
        /// <summary>
        /// The tutorial's practice battle is bound in place of the song's and its even beat starts at <paramref name="startDsp"/>
        /// (the director gives it a bar before the first note). It is never logged.
        /// </summary>
        public void StartTutorial(double startDsp, bool afterPrologue = false)
        {
            if (tutorial == null) { StartSession(); return; }
            SaveRun("Restarted");
            if (intro != null && intro.Playing) intro.Stop();
            input.Clear(); audioPlayer.Stop();
            if (tutorialBattle == null) (tutorialBattle, tutorialTimeline) = CreateTutorialBattle();
            Bind(tutorialBattle, tutorialTimeline);
            CurrentBeat = -1; SessionId++; runSaved = true; ResumeAt = CountdownEnd = double.NegativeInfinity; tutorialHandover = -1;
            Battle.Reset(SessionId, stage.NoiseSeed);
            Battle.Invincible = true;
            clock.StartAt(startDsp + TutorialDirector.LeadBeats * Timeline.SecondsPerBeat);
            previousTime = SongTime;
            State = PrototypeState.Tutorial; view.ResetFeedback();
            ScheduledSongAudio.Preload(song);
            tutorial.Play(startDsp, afterPrologue);
        }
        private (BattleSession, IBeatTimeline) CreateTutorialBattle()
        {
            var settings = mainBattle.Settings.Copy();
            settings.ChartId = "Tutorial"; settings.NoteTimes = null; settings.HoldEndTimes = null;
            settings.NoteCount = TutorialNotes; settings.FinishNote = -1;
            // Nothing attacks or spawns by itself: the director shows each lesson's attack and noise when the player is ready.
            settings.BossChartText = null; settings.Patterns = new PatternDefinition[0]; settings.Schedule.Enabled = false;
            var battle = new BattleSession(settings, stage.NoiseSeed);
            battle.Noise.AutoSpawn = false;
            return (battle, settings.CreateTimeline());
        }
        /// <summary>
        /// The tutorial is done: the song's battle is bound again, fresh, and the song is scheduled so that its intro starts on
        /// <paramref name="downbeatDsp"/> (a tutorial beat), four beats before the first note, as after the prologue.
        /// </summary>
        public void FinishTutorial(double downbeatDsp)
        {
            if (State != PrototypeState.Tutorial || !InTutorialBattle) return;
            Bind(mainBattle, mainTimeline);
            CurrentBeat = -1; runSaved = false; ResumeAt = CountdownEnd = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            ApplySwitches();
            clock.StartAt(downbeatDsp - IntroTimeline.HandoverSongTime(Timeline));
            previousTime = SongTime; input.Clear();
            audioPlayer.Begin(song, clock, Timeline, countInClicks: false);
            tutorialHandover = downbeatDsp;
        }
        /// <summary>Esc in the tutorial: straight into the song.</summary>
        public void SkipTutorial()
        {
            if (State != PrototypeState.Tutorial) return;
            StartSession();
        }
        private void StopTutorial()
        {
            if (tutorial != null && tutorial.Playing) tutorial.Stop();
            tutorialHandover = -1;
        }
        public void StartSession()
        {
            SaveRun("Restarted");
            StopTutorial(); Bind(mainBattle, mainTimeline);
            if (intro != null && intro.Playing) intro.Stop();
            input.Clear(); audioPlayer.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false; ResumeAt = CountdownEnd = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            Battle.SkipToPhase(GameFlow.StartPhase);
            // No count-in: the song starts at once and its own intro (four beats before the first note) leads in, as after
            // the tutorial. Developer mode: a later phase starts later in the song, the same four beats before its note.
            double lead = song.CountInBeats * Timeline.SecondsPerBeat;
            double from = Timeline.TimeOf(DevPhaseNote(GameFlow.StartPhase)) - lead;
            if (from > 0)
            {
                // The skipped notes pass unplayed and unhurt, so the attacks, the noise and the board are where the song is.
                Battle.Invincible = true; Battle.Advance(from);
                clock.Start(.30 - from);
            }
            else clock.Start(.30 + song.AudioStartOffset);
            ApplySwitches();
            previousTime = SongTime;
            State = PrototypeState.Playing; view.ResetFeedback();
            audioPlayer.Begin(song, clock, Timeline, countInClicks: false);
        }
#if UNITY_EDITOR
        // DEV-ENDING-SKIP (editor only, compiled out of builds; remove before release).
        private const double DevEndingLeadSeconds = 10;
        /// <summary>
        /// Editor test (F9): restarts the song's battle 10 s before the finishing run. The skipped notes pass unplayed and
        /// unhurt; then the boss has 1 HP left and the string is charged, as if a noise was just collected on this beat.
        /// </summary>
        public void DevSkipToEnding()
        {
            var settings = mainBattle.Settings;
            if (!settings.HasFinale) return;
            SaveRun("Restarted");
            StopTutorial(); Bind(mainBattle, mainTimeline);
            if (intro != null && intro.Playing) intro.Stop();
            input.Clear(); audioPlayer.Stop();
            CurrentBeat = -1; SessionId++;
            runSaved = false; ResumeAt = CountdownEnd = double.NegativeInfinity;
            Battle.Reset(SessionId, stage.NoiseSeed);
            Battle.SkipToPhase(4);
            double from = System.Math.Max(0, Timeline.TimeOf(settings.FinishNote) - DevEndingLeadSeconds);
            Battle.Invincible = true; Battle.Advance(from);
            Battle.DevPrimeFinishingBlow();
            clock.Start(.30 - from);
            ApplySwitches();
            previousTime = SongTime;
            State = PrototypeState.Playing; view.ResetFeedback();
            audioPlayer.Begin(song, clock, Timeline, countInClicks: false);
        }
#endif
        /// <summary>Tests only: skip the battle and the finale and start the epilogue now.</summary>
        internal void JumpToEpilogueForTests()
        {
            if (!clock.Running) StartSession();
            State = PrototypeState.Epilogue; EpilogueStarted?.Invoke();
        }
        /// <summary>The knight's fall is over (or skipped): the game-over card shows while the song fades out (DeathDirector).</summary>
        public void FinishDeath()
        {
            if (State != PrototypeState.Dying) return;
            State = PrototypeState.Finished;
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
            double at = clock.SongTime, beat = Timeline.SecondsPerBeat, rewind = song.CountInBeats * beat;
            ResumeAt = at;
            // Snap back to the music's beat grid (the chart's first note is on a beat). The pause can fall anywhere in a
            // beat; counting from there would put each number between the song's beats instead of on them.
            double anchor = Timeline.TimeOf(0);
            CountdownEnd = anchor + System.Math.Floor((at - anchor) / beat + 1e-6) * beat;
            clock.Start(.30 + rewind - CountdownEnd);
            previousTime = SongTime; input.Clear();
            State = at < 0 ? PrototypeState.CountIn : PrototypeState.Playing;
            // Its own click on every number of the count, on the beats the numbers change; no count-in clicks besides.
            audioPlayer.Begin(song, clock, Timeline, countInClicks: false);
            audioPlayer.ScheduleCountdown(CountdownEnd - rewind, song.CountInBeats, beat);
        }
        /// <summary>Game over "첫 수부터": a fresh battle, in a fresh scene when the input sync changed since this one was built.</summary>
        public void RestartFromMenu()
        {
            if (inputOffsetMs != GameFlow.InputOffsetMs && GameFlow.HasMenu) { SaveRun("Restarted"); GameFlow.StartBattle(); return; }
            StartSession();
        }
        /// <summary>The pause settings closed: apply what can change mid-battle (developer switches, difficulty; volume and sync apply on resume).</summary>
        public void ApplySettings() => ApplySwitches();
        /// <summary>The run's switches from the saved settings: the developer switches and the difficulty.</summary>
        private void ApplySwitches()
        {
            Battle.Invincible = GameFlow.Invincible; Battle.IgnoreBossHp = GameFlow.BossHpOff;
            Battle.HealOnNoise = GameFlow.Difficulty == GameDifficulty.Easy;
#if UNITY_EDITOR
            // DEV-F8-INVINCIBLE (editor only, compiled out of builds; remove before release).
            if (devF8Invincible) Battle.Invincible = true;
#endif
        }
#if UNITY_EDITOR
        // DEV-F8-INVINCIBLE (editor only, compiled out of builds; remove before release).
        // Kept across restarts until F8 is pressed again or play mode ends; no developer mode needed.
        private static bool devF8Invincible;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDevF8Invincible() => devF8Invincible = false;
        public void DevToggleF8Invincible()
        {
            devF8Invincible = !devF8Invincible;
            // Off: back to what the run would have without F8 (the developer switch, or the always-safe tutorial).
            Battle.Invincible = devF8Invincible || GameFlow.Invincible || InTutorialBattle;
            Debug.Log($"[DEV] F8 invincible: {(devF8Invincible ? "ON" : "OFF")}");
        }
#endif
#if UNITY_EDITOR
        // DEV-AUTOPLAY (editor only, compiled out of builds).
        private DevAutoPlayer autoPlayer;
        private int autoPlaySession = -1;
        /// <summary>The auto player of the battle being played now, or null while auto play is off.</summary>
        public DevAutoPlayer AutoPlayer => GameFlow.AutoPlay ? autoPlayer : null;
        // Only the song's battle while it is judged: not the prologue, the tutorial (its lessons wait for the player),
        // the finale, a pause or the fall. A fresh player per run, so a restart starts its notes over.
        private void StepAutoPlay(double time)
        {
            // Off: dropped, so turning it back on (key or settings) picks up from the next note, not a stale plan.
            if (!GameFlow.AutoPlay) { autoPlayer = null; return; }
            if (State != PrototypeState.Playing || InTutorialBattle) return;
            if (autoPlayer == null || autoPlaySession != SessionId) { autoPlayer = new DevAutoPlayer(SessionId); autoPlaySession = SessionId; }
            autoPlayer.Step(Battle, Timeline, time);
        }
        public void DevSetAutoPlay(bool on)
        {
            if (!GameFlow.DevMode) return;
            GameFlow.AutoPlay = on;
            Debug.Log($"[DEV] Auto play: {(on ? "ON" : "OFF")}");
        }
#endif
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
#if UNITY_EDITOR
            // DEV-ENDING-SKIP (editor only, compiled out of builds; remove before release).
            if (command == "dev-ending") { DevSkipToEnding(); return; }
            // DEV-F8-INVINCIBLE (editor only, compiled out of builds; remove before release).
            if (command == "dev-f8-invincible") { DevToggleF8Invincible(); return; }
#endif
            // The prologue and the tutorial have no pause menu: Esc, Space or Enter skip on. Nothing else applies yet.
            if (State == PrototypeState.Prologue || State == PrototypeState.Tutorial)
            {
                if (command == "pause" || command == "start") { if (State == PrototypeState.Prologue) SkipPrologue(); else SkipTutorial(); }
                else if (command == "mute") audioPlayer.ToggleMute();
                return;
            }
            // The fall plays without a menu: Esc, Space or Enter go straight to the game-over card.
            if (State == PrototypeState.Dying)
            {
                if (command == "pause" || command == "start") death.Skip();
                else if (command == "mute") audioPlayer.ToggleMute();
                else if (command == "restart") StartSession();
                return;
            }
            if (command.StartsWith("dev-phase")) { DevGoToPhase(command[command.Length - 1] - '0'); return; }
            if (command == "dev-invincible") { DevSetInvincible(!GameFlow.Invincible); return; }
#if UNITY_EDITOR
            if (command == "dev-autoplay") { DevSetAutoPlay(!GameFlow.AutoPlay); return; }
#endif
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
                // The tutorial's last downbeat is the song's start: the battle goes live, as after the prologue.
                if (State == PrototypeState.Tutorial && tutorialHandover >= 0 && AudioSettings.dspTime >= tutorialHandover)
                {
                    StopTutorial(); input.Clear(); previousTime = SongTime; State = PrototypeState.Playing;
                }
#if UNITY_EDITOR
                StepAutoPlay(time);
#endif
                audioPlayer.Schedule();
                AdvanceWorld(time);
                if (State == PrototypeState.Finale && time >= FinaleEndTime)
                { State = PrototypeState.Epilogue; EpilogueStarted?.Invoke(); }
                else if (Battle.Outcome == BattleOutcome.Won && Battle.Settings.HasFinale && (State == PrototypeState.Playing || State == PrototypeState.CountIn))
                {
                    // Won on the finishing run: keep the song and the notes going, without judgement.
                    State = PrototypeState.Finale; SaveRun("Completed"); FinaleStarted?.Invoke();
                }
                else if (Battle.Outcome == BattleOutcome.Dead && !InTutorialBattle && (State == PrototypeState.Playing || State == PrototypeState.CountIn))
                {
                    // The last hit freezes the board; the song plays on quietly under the knight's fall (DeathDirector), then FinishDeath.
                    clock.Stop(); input.Clear(); State = PrototypeState.Dying;
                    SaveRun("Completed");
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
            if (Battle == null || SessionId == 0 || runSaved || InTutorialBattle) return;
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
            if (Session != null && view != null) Detach();
            clock?.Stop(); if (audioPlayer != null) audioPlayer.Stop();
        }
    }
}
