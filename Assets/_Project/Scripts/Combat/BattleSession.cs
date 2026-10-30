using System;
using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Player;
using StringAsASword.Rhythm;
using StringAsASword.Stage;

namespace StringAsASword.Combat
{
    public enum BattleOutcome { Running, Won, Dead, Timeout }
    [Serializable] public sealed class BattleEvent
    {
        public string type, detail, cells;
        public int noteIndex, musicBeat, phase, id, impactNote, x, y;
        public int warningNotes, requiredDodgeActions, selectedInterval, actualInterval;
        public string reason, kind, response;
    }
    [Serializable] public sealed class NoteRecord
    {
        public int noteIndex, phase, musicBeat, beforeX, beforeY, x, y, damage, playerHp, bossHp, collected;
        public int moves, guards, effectiveAttacks, others;
        public string category, reason, judgement, stateHash;
        public bool attackOpportunity, guardSuccess, hit;
    }
    [Serializable] public sealed class BattleInput
    {
        public double time;
        public PlayerActionKind action;
        public MoveDirection direction;
    }
    public sealed class BattleObservation
    {
        public GridPosition Position { get; internal set; }
        public int NoteIndex { get; internal set; }
        public int RemainingNotes { get; internal set; }
        public int PhaseId { get; internal set; }
        public int PlayerHp { get; internal set; }
        public int CurrentHp { get; internal set; }
        public int GoalRemaining { get; internal set; }
        public int RemainingBossHp { get; internal set; }
        public int FutureNoiseGoals { get; internal set; }
        public int MaxCollectionCost { get; internal set; }
        public int SpawnInterval { get; internal set; }
        public NoisePickup[] Pickups { get; internal set; }
        public BossTelegraph[] Telegraphs { get; internal set; }
    }
    /// <summary>The only gameplay event loop. Live input, offline agents and replay use this same class.</summary>
    public sealed class BattleSession
    {
        public BattleSettings Settings { get; }
        public RhythmSession Session { get; }
        public BossHealthGate Health { get; }
        public CombatResolver Combat { get; }
        public AttackResolver Attacks { get; }
        public PhaseController Phases { get; }
        public NoisePickupSystem Noise { get; }
        public BossPatternSystem Patterns { get; private set; }
        public BattleOutcome Outcome { get; private set; }
        public int PlayerHp { get; private set; }
        public int LastClosedNote { get; private set; } = -1;
        public int CurrentMusicBeat { get; private set; } = -1;
        public int Seed { get; private set; }
        public List<NoteRecord> Records { get; } = new List<NoteRecord>();
        public List<BattleEvent> Events { get; } = new List<BattleEvent>();
        public List<BattleInput> Inputs { get; } = new List<BattleInput>();
        public event Action<int> MusicBeat;
        public event Action<NoteRecord> NoteResolved;
        private readonly IBeatTimeline timeline;
        private readonly BeatScheduler music;
        private readonly int[] analysisEnds;
        public int[] AnalysisEnds => analysisEnds == null ? null : (int[])analysisEnds.Clone();
        private ActionResult? accepted;
        private GridPosition before;
        private double advancedTime = double.NegativeInfinity;
        // Also records a stopped/partial run without inventing unplayed notes during replay.
        public double AdvancedTime => double.IsNegativeInfinity(advancedTime) ? -1e9 : advancedTime;
        public BattleSession(BattleSettings settings, int seed, int[] analysisEnds = null)
        {
            settings.Validate(); settings = settings.Copy(); Settings = settings; this.analysisEnds = analysisEnds == null ? null : (int[])analysisEnds.Clone();
            if (analysisEnds != null && (analysisEnds.Length != 4 || analysisEnds[3] != settings.NoteCount || analysisEnds[0] < 1 ||
                analysisEnds[1] <= analysisEnds[0] || analysisEnds[2] <= analysisEnds[1] || analysisEnds[3] <= analysisEnds[2]))
                throw new ArgumentException("Analysis boundaries must partition the whole chart.");
            timeline = new ConstantBeatTimeline(settings.Bpm); music = new BeatScheduler(timeline);
            var board = new BoardState();
            Session = new RhythmSession(board, new RhythmJudge(timeline, settings.PerfectWindow, settings.GoodWindow, settings.InputOffset), new GridPosition(3, 3), settings.NoteCount);
            Noise = new NoisePickupSystem(board, settings.SpawnInterval, settings.MaxNoise);
            Health = new BossHealthGate(settings.Health);
            Attacks = new AttackResolver(board); Combat = new CombatResolver(Attacks, Health, 1);
            Phases = new PhaseController(Health, Noise, settings.NoiseTargets);
            Session.ActionEvaluated += OnAction; Session.BeatClosed += Close;
            Noise.CandidateAllowed = CandidateAllowed;
            Noise.Spawned += n => AddEvent("noise-spawn", n.Id.ToString(), n.Cell, n.Id);
            Noise.CollectedPickup += n => AddEvent("noise-collect", n.Id.ToString(), n.Cell, n.Id);
            Noise.SpawnStatus += (reason, beat) => Events.Add(new BattleEvent { type = "noise-" + reason, musicBeat = beat, noteIndex = LastClosedNote, phase = Phases.PhaseId });
            Reset(1, seed);
        }
        public void Reset(int sessionId, int seed)
        {
            Seed = seed; Records.Clear(); Events.Clear(); Inputs.Clear(); accepted = null;
            Session.Reset(); music.Reset(); Combat.Reset(); Phases.Reset(sessionId, seed);
            PlayerHp = Settings.PlayerMaxHp; Outcome = BattleOutcome.Running;
            LastClosedNote = CurrentMusicBeat = -1; advancedTime = double.NegativeInfinity; before = Session.Position;
            Patterns = new BossPatternSystem(Settings.Patterns, Settings.Schedule, seed);
            Patterns.Changed += (type, a) => Events.Add(new BattleEvent { type = "pattern-" + type,
                noteIndex = LastClosedNote, musicBeat = CurrentMusicBeat, phase = Phases.PhaseId,
                id = a.Id, impactNote = a.ImpactNote, cells = a.Cells.ToString("X16"), detail = a.Name,
                warningNotes = a.WarningNotes, requiredDodgeActions = a.RequiredDodgeActions,
                selectedInterval = a.SelectedInterval, actualInterval = a.ActualInterval, reason = a.Reason, kind = a.Kind.ToString(), response = a.Response.ToString() });
            Patterns.CanPublish = CanPublish;
            Patterns.BeginPhase(1, 0); Patterns.Publish(-1, Session.Position, Settings.NoteCount, false);
        }
        public ActionResult Request(PlayerActionKind kind, MoveDirection direction, double songTime)
        {
            Inputs.Add(new BattleInput { action = kind, direction = direction, time = songTime });
            Advance(songTime);
            return Session.RequestAction(kind, direction, songTime);
        }
        public void Advance(double time)
        {
            if (Outcome != BattleOutcome.Running || time < advancedTime) return;
            music.Advance(Math.Min(time, timeline.TimeOf(Settings.NoteCount - 1)), beat => {
                if (Outcome != BattleOutcome.Running) return;
                Session.Advance(timeline.TimeOf(beat));
                if (Outcome != BattleOutcome.Running) return;
                CurrentMusicBeat = beat; Noise.AdvanceToBeat(beat, Session.Position); MusicBeat?.Invoke(beat);
            });
            Session.Advance(time); advancedTime = time;
        }
        private void OnAction(ActionResult action)
        {
            AddEvent(action.Accepted ? "input-accepted" : "input-rejected", action.Kind + ":" + action.Rejection, action.Position);
            Events[Events.Count - 1].noteIndex = action.Timing.BeatId;
            if (!action.Accepted) return;
            accepted = action;
            if (action.Kind == PlayerActionKind.Move) Noise.TryCollect(action.Position, Noise.SessionId, Noise.PhaseId);
            if (analysisEnds == null) Combat.Submit(action);
        }
        private void Close(int note)
        {
            int phase = Phases.PhaseId;
            bool has = accepted.HasValue && accepted.Value.Timing.BeatId == note;
            var action = accepted.GetValueOrDefault();
            bool opportunity = has && action.Kind == PlayerActionKind.Attack && Attacks.IsInRange(action.Position);
            int damage = analysisEnds == null ? Combat.ResolveBeat(note) : opportunity ? 1 : 0;
            ulong danger = Patterns.Resolve(note);
            ulong cell = SafeRoutePlanner.Bit(Session.Position);
            bool threatened = (danger & cell) != 0;
            bool guarded = threatened && has && action.Kind == PlayerActionKind.Guard;
            bool broken = guarded && (Patterns.LastFiredUnblockable & cell) != 0;
            bool guard = guarded && !broken;
            bool hit = threatened && !guard;
            if (hit) PlayerHp = Math.Max(0, PlayerHp - 1);
            LastClosedNote = note;
            var record = new NoteRecord { noteIndex = note, phase = phase, musicBeat = CurrentMusicBeat,
                beforeX = before.X, beforeY = before.Y, x = Session.Position.X, y = Session.Position.Y,
                attackOpportunity = opportunity, damage = damage, guardSuccess = guard, hit = hit,
                playerHp = PlayerHp, bossHp = Health.CurrentHp, collected = Noise.Collected,
                judgement = has ? action.Timing.Grade.ToString() : "Miss",
                category = has && action.Kind == PlayerActionKind.Move ? "Move" : has && action.Kind == PlayerActionKind.Guard ? "Guard" : damage > 0 ? "EffectiveAttack" : "Other",
                reason = !has ? "MissOrWait" : opportunity && damage == 0 ? "HpFloor" : has && action.Kind == PlayerActionKind.Attack && !opportunity ? "OutOfRange" : action.Kind.ToString() };
            Records.Add(record);
            var previous = Records.Count > 1 ? Records[Records.Count - 2] : null;
            record.moves = (previous?.moves ?? 0) + (record.category == "Move" ? 1 : 0);
            record.guards = (previous?.guards ?? 0) + (record.category == "Guard" ? 1 : 0);
            record.effectiveAttacks = (previous?.effectiveAttacks ?? 0) + (record.category == "EffectiveAttack" ? 1 : 0);
            record.others = (previous?.others ?? 0) + (record.category == "Other" ? 1 : 0);
            // player-hit stays one event per lost HP; guard-broken only adds why the guard did not count.
            if (broken) AddResponseEvent("guard-broken", danger, cell);
            if (hit || guard) AddResponseEvent(hit ? "player-hit" : "guard-success", danger, cell);
            bool transition = false;
            if (analysisEnds == null)
            {
                if (Health.Defeated) Outcome = BattleOutcome.Won;
                else if (PlayerHp == 0) Outcome = BattleOutcome.Dead;
                else transition = Phases.ResolveBeat(note, CurrentMusicBeat + 1);
            }
            else
            {
                if (PlayerHp == 0) Outcome = BattleOutcome.Dead;
                else if (phase < 4 && note + 1 == analysisEnds[phase - 1])
                {
                    AddEvent("analysis-goal", Noise.GoalReached ? "met" : "missed", Session.Position);
                    Phases.AdvanceAnalysisPhase(CurrentMusicBeat + 1); transition = true;
                }
            }
            if (transition)
            { AddEvent("phase", Phases.PhaseId.ToString(), Session.Position); Patterns.BeginPhase(Phases.PhaseId, note + 1); }
            if (Outcome == BattleOutcome.Running && note == Settings.NoteCount - 1) Outcome = BattleOutcome.Timeout;
            accepted = null; before = Session.Position;
            if (Outcome == BattleOutcome.Running)
                Patterns.Publish(note, Session.Position, Settings.NoteCount, Noise.WaitingForSafeSpawn);
            else { Session.Stopped = true; AddEvent("end", Outcome.ToString(), Session.Position); }
            record.stateHash = BattleLog.StateHash(this);
            NoteResolved?.Invoke(record);
        }
        private int NextAvailable => Math.Max(LastClosedNote + 1, Session.Gate.LastConsumedBeat + 1);
        private int FutureGoals()
        { int count = 0; for (int p = Phases.PhaseId; p < 3; p++) count += Settings.NoiseTargets[p]; return count; }
        private bool CandidateAllowed(GridPosition candidate, bool fallback)
        {
            int next = NextAvailable, remaining = Settings.NoteCount - next;
            if (fallback && Math.Abs(candidate.X - Session.Position.X) + Math.Abs(candidate.Y - Session.Position.Y) != 1) return false;
            var targets = new List<GridPosition>();
            foreach (var n in Noise.Pickups) targets.Add(n.Cell);
            targets.Add(candidate);
            var route = SafeRoutePlanner.FindAll(Session.Position, targets, next, Patterns.Active,
                Math.Min(remaining, Settings.MaxCollectionCost * targets.Count));
            if (!route.Found) return false;
            int otherGoals = Math.Max(0, Noise.Required - Noise.Collected - targets.Count);
            // Future positions are unknown. Reserve the advertised per-pickup service bound, not lucky RNG.
            int attackBudget = analysisEnds == null ? Health.TotalHp : 0;
            int reserve = route.Cost + (otherGoals + FutureGoals()) * Settings.MaxCollectionCost + attackBudget;
            // A pessimistic whole-song estimate must not starve a locally safe fallback after a slow phase.
            return fallback || reserve <= remaining;
        }
        private bool CanPublish(BossTelegraph proposal)
        {
            var all = new List<BossTelegraph>(Patterns.Active) { proposal };
            var targets = new List<GridPosition>(); foreach (var n in Noise.Pickups) targets.Add(n.Cell);
            if (targets.Count == 0) return true;
            return SafeRoutePlanner.FindAll(Session.Position, targets, NextAvailable, all,
                Math.Min(Settings.NoteCount - NextAvailable, Settings.MaxCollectionCost * targets.Count)).Found;
        }
        private void AddEvent(string type, string detail, GridPosition cell, int id = 0)
            => Events.Add(new BattleEvent { type = type, detail = detail, noteIndex = LastClosedNote,
                musicBeat = CurrentMusicBeat, phase = Phases.PhaseId, x = cell.X, y = cell.Y, id = id });
        // Overlapping fixed patterns report the strictest response that reached the player's cell.
        private void AddResponseEvent(string type, ulong danger, ulong cell)
        {
            var response = AttackResponse.Either;
            foreach (var f in Patterns.LastFired) if ((f.Cells & cell) != 0 && f.Response > response) response = f.Response;
            AddEvent(type, danger.ToString("X16"), Session.Position);
            Events[Events.Count - 1].response = response.ToString();
        }
        public BattleObservation Observe()
        {
            var pickups = new NoisePickup[Noise.Pickups.Count]; for (int i = 0; i < pickups.Length; i++) pickups[i] = Noise.Pickups[i];
            var warnings = new BossTelegraph[Patterns.Active.Count]; for (int i = 0; i < warnings.Length; i++) warnings[i] = Patterns.Active[i];
            return new BattleObservation { Position = Session.Position, NoteIndex = LastClosedNote + 1,
                RemainingNotes = Settings.NoteCount - LastClosedNote - 1, PhaseId = Phases.PhaseId,
                PlayerHp = PlayerHp, CurrentHp = Health.CurrentHp, GoalRemaining = Math.Max(0, Noise.Required - Noise.Collected),
                RemainingBossHp = analysisEnds == null ? Health.TotalHp : 0, FutureNoiseGoals = FutureGoals(),
                MaxCollectionCost = Settings.MaxCollectionCost, SpawnInterval = Settings.SpawnInterval,
                Pickups = pickups, Telegraphs = warnings };
        }
    }
}
