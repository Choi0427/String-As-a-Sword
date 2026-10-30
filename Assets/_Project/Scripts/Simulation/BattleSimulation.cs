using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;

namespace StringAsASword.Simulation
{
    public enum StrategyKind { CollectFirst, AttackHold, MovementUse }

    public readonly struct BattleDecision
    {
        public PlayerActionKind Kind { get; }
        public MoveDirection Direction { get; }
        public BattleDecision(PlayerActionKind kind, MoveDirection direction = MoveDirection.Up)
        { Kind = kind; Direction = direction; }
    }

    // No session, seed, settings, or unrevealed pattern is available to a strategy.
    public static class BattleStrategies
    {
        private static readonly BoardState Board = new BoardState();
        private static readonly MoveDirection[] Directions =
            { MoveDirection.Up, MoveDirection.Left, MoveDirection.Down, MoveDirection.Right };

        public static BattleDecision Decide(StrategyKind kind, BattleObservation view)
        {
            var decision = Choose(kind, view);
            // Waiting out a required response is not a guard. Standing still is the same either way, so an unthreatened
            // cell spends the note on an attack; from below row 8 that is a miss rather than an inflated guard count.
            if (decision.Kind == PlayerActionKind.Guard && !Threatened(view.Position, view.NoteIndex, view.Telegraphs) &&
                Array.Exists(view.Telegraphs, t => t.Response != AttackResponse.Either))
                return new BattleDecision(PlayerActionKind.Attack);
            return decision;
        }

        private static BattleDecision Choose(StrategyKind kind, BattleObservation view)
        {
            bool danger = Threatened(view.Position, view.NoteIndex, view.Telegraphs);
            if (GuardRequired(view))
                return new BattleDecision(PlayerActionKind.Guard);
            bool urgent = view.RemainingNotes <= CollectionBudget(view);
            bool collect = view.GoalRemaining > 0 &&
                (kind == StrategyKind.CollectFirst || urgent || view.CurrentHp <= 0);
            var pickup = collect || kind == StrategyKind.MovementUse ? BestPickup(view) : default;
            if (kind == StrategyKind.MovementUse && pickup.Found && pickup.Cost <= view.MaxCollectionCost / 2)
                collect = true;
            if (Array.Exists(view.Telegraphs, t => t.Response == AttackResponse.DodgeOnly))
            {
                // Every planner route outlives the pending DodgeOnly hits, so a useful pickup trip is also a dodge.
                if (pickup.Found && (collect || danger && kind == StrategyKind.MovementUse))
                    return new BattleDecision(pickup.FirstAction, pickup.FirstDirection);
                if (TryDodge(kind, view, danger, out var dodge)) return dodge;
            }
            if (danger && kind == StrategyKind.AttackHold)
                return new BattleDecision(PlayerActionKind.Guard);
            if (danger && kind == StrategyKind.MovementUse && pickup.Found && pickup.FirstAction == PlayerActionKind.Move)
                return new BattleDecision(pickup.FirstAction, pickup.FirstDirection);
            if (collect && pickup.Found)
                return new BattleDecision(pickup.FirstAction, pickup.FirstDirection);

            if (danger)
            {
                // A visible pickup makes a dodge useful; otherwise choose a safe adjacent cell.
                if (kind == StrategyKind.MovementUse && pickup.Found)
                    return new BattleDecision(pickup.FirstAction, pickup.FirstDirection);
                foreach (var direction in Directions)
                    if (Board.TryGetDestination(view.Position, direction, out var cell) &&
                        !Threatened(cell, view.NoteIndex, view.Telegraphs))
                        return new BattleDecision(PlayerActionKind.Move, direction);
                return new BattleDecision(PlayerActionKind.Guard);
            }

            if (view.Position.Y < 7)
            {
                var route = SafeRoutePlanner.Find(view.Position, new GridPosition(view.Position.X, 7),
                    view.NoteIndex, view.Telegraphs);
                if (route.Found && route.Cost > 0)
                    return new BattleDecision(route.FirstAction, route.FirstDirection);
                return new BattleDecision(PlayerActionKind.Guard);
            }
            return new BattleDecision(PlayerActionKind.Attack);
        }

        public static int CollectionBudget(BattleObservation view)
        {
            // Reserve all remaining attacks and a worst-case bounded trip per remaining pickup.
            int goals = view.GoalRemaining + view.FutureNoiseGoals;
            return view.RemainingBossHp + goals * view.MaxCollectionCost +
                Math.Max(0, goals - view.Pickups.Length) * view.SpawnInterval;
        }

        // Decides only what a pending DodgeOnly sequence forces; false leaves the note to the strategy's normal work.
        private static bool TryDodge(StrategyKind kind, BattleObservation view, bool danger, out BattleDecision decision)
        {
            var at = view.Position;
            int note = view.NoteIndex;
            var telegraphs = view.Telegraphs;
            int impact = int.MaxValue, last = note;
            foreach (var t in telegraphs)
                if (t.Response == AttackResponse.DodgeOnly) { impact = Math.Min(impact, t.ImpactNote); last = Math.Max(last, t.ImpactNote); }
            ulong next = SafeRoutePlanner.Unblockable(telegraphs, impact);
            // After the last hit a route walks back from anywhere, so it exists exactly when the hits can be survived.
            int horizon = last - note + 17;
            bool Outlives(GridPosition cell) =>
                SafeRoutePlanner.Find(cell, new GridPosition(cell.X, 7), note + 1, telegraphs, horizon, returnToTop: true).Found;

            if (kind == StrategyKind.MovementUse && (next & SafeRoutePlanner.Bit(at)) != 0)
                foreach (var direction in Directions)
                    if (Board.TryGetDestination(at, direction, out var cell) && (next & SafeRoutePlanner.Bit(cell)) == 0 &&
                        !Threatened(cell, note, telegraphs) && Outlives(cell))
                    { decision = new BattleDecision(PlayerActionKind.Move, direction); return true; }

            if ((SafeRoutePlanner.Unblockable(telegraphs, note) & SafeRoutePlanner.Bit(at)) != 0 || !Outlives(at))
            {
                // Not staying is the latest safe departure. No route remains only after a missed input.
                var route = SafeRoutePlanner.Find(at, new GridPosition(at.X, 7), note, telegraphs, horizon, returnToTop: true);
                decision = new BattleDecision(route.FirstAction, route.FirstDirection);
                return route.Found;
            }
            decision = new BattleDecision(danger ? PlayerActionKind.Guard : PlayerActionKind.Attack);
            if (danger) return true;
            // Climbing into the next hit is pointless when no attack fits before leaving again; MovementUse never does.
            return at.Y < 7 && (next & SafeRoutePlanner.Bit(new GridPosition(at.X, at.Y + 1))) != 0 &&
                (kind == StrategyKind.MovementUse || impact <= note + 7 - at.Y);
        }

        private static bool GuardRequired(BattleObservation view)
        {
            ulong bit = SafeRoutePlanner.Bit(view.Position);
            foreach (var t in view.Telegraphs)
                if (t.Response == AttackResponse.GuardOnly && t.ImpactNote == view.NoteIndex && (t.Cells & bit) != 0) return true;
            return false;
        }

        private static RouteResult BestPickup(BattleObservation view)
        {
            var best = default(RouteResult);
            int bestId = int.MaxValue;
            if (view.GoalRemaining <= 0) return best;
            foreach (var item in view.Pickups)
            {
                var route = SafeRoutePlanner.Find(view.Position, item.Cell, view.NoteIndex,
                    view.Telegraphs, returnToTop: true);
                if (route.Found && (!best.Found || route.Cost < best.Cost || route.Cost == best.Cost && item.Id < bestId))
                { best = route; bestId = item.Id; }
            }
            return best;
        }

        private static bool Threatened(GridPosition cell, int note, BossTelegraph[] telegraphs)
        {
            ulong bit = 1UL << (cell.Y * 8 + cell.X);
            foreach (var telegraph in telegraphs)
                if (telegraph.ImpactNote == note && (telegraph.Cells & bit) != 0) return true;
            return false;
        }
    }

    [Serializable]
    public sealed class PhaseSample
    {
        public int phase, move, guard, effectiveAttack, other, attackOpportunity, goalRemaining;
        public int transitionNote = -1;
    }

    public sealed class BattleRun
    {
        public int Seed { get; internal set; }
        public StrategyKind Strategy { get; internal set; }
        public BattleSession Battle { get; internal set; }
        public PhaseSample[] Phases { get; internal set; }
        public int InputMisses { get; internal set; }
        public string Outcome { get; internal set; }
        public int FinalPhase { get; internal set; }
        public int FinalHp { get; internal set; }
        public int GoalRemaining { get; internal set; }
        public int BossHp { get; internal set; }
        public int LeftoverNotes { get; internal set; }
        public double ElapsedMilliseconds { get; internal set; }
        public readonly Dictionary<string, int> EventReasons = new Dictionary<string, int>();
        public readonly Dictionary<string, int> InvariantFailures = new Dictionary<string, int>();
        public int InvariantFailureCount => InvariantFailures.Values.Sum();
        public bool Cleared => Outcome == "Won";
        public string Failure => Cleared ? "" : Outcome;
        public bool AnalysisGoalFailed => Phases.Any(p => p.goalRemaining > 0);
    }

    public static class BattleSimulator
    {
        public static readonly int[] AnalysisEnds = { 100, 148, 216, 344 };

        public static BattleRun Run(BattleSettings settings, int seed, StrategyKind strategy,
            bool analysis = false, double missRate = 0)
            => RunOne(settings, seed, strategy, analysis ? AnalysisEnds : null, missRate);

        public static BattleRun RunOne(BattleSettings settings, int seed, StrategyKind strategy,
            int[] analysisEnds = null, double missRate = 0)
        {
            var timer = Stopwatch.StartNew();
            if (missRate < 0 || missRate > 1) throw new ArgumentOutOfRangeException(nameof(missRate));
            var battle = new BattleSession(settings, seed, analysisEnds);
            var result = new BattleRun
            {
                Seed = seed, Strategy = strategy, Battle = battle,
                Phases = Enumerable.Range(1, 4).Select(p => new PhaseSample { phase = p }).ToArray()
            };
            // A separate RNG changes only omitted inputs, never the battle's spawn/pattern stream.
            var inputRandom = new Random(unchecked(seed ^ 0x4f1bbcdc));
            double secondsPerBeat = 60.0 / settings.Bpm;
            double good = Math.Min(settings.GoodWindow, secondsPerBeat * .45);
            int previousPhase = 1;
            for (int note = 0; note < settings.NoteCount && battle.Outcome.ToString() == "Running"; note++)
            {
                double time = note * secondsPerBeat + settings.InputOffset;
                battle.Advance(time);
                var decision = BattleStrategies.Decide(strategy, battle.Observe());
                if (missRate == 0 || inputRandom.NextDouble() >= missRate)
                    battle.Request(decision.Kind, decision.Direction, time);
                else result.InputMisses++;
                int goalsBeforeClose = battle.Observe().GoalRemaining;
                battle.Advance(time + good + .000001);
                int phase = battle.Phases.PhaseId;
                if (phase > previousPhase)
                {
                    result.Phases[previousPhase - 1].transitionNote = note;
                    result.Phases[previousPhase - 1].goalRemaining = goalsBeforeClose;
                }
                previousPhase = phase;
            }
            foreach (var record in battle.Records)
            {
                var phase = result.Phases[record.phase - 1];
                switch (record.category.ToString())
                {
                    case "Move": phase.move++; break;
                    case "Guard": phase.guard++; break;
                    case "EffectiveAttack": phase.effectiveAttack++; break;
                    default: phase.other++; break;
                }
                if (record.attackOpportunity) phase.attackOpportunity++;
            }
            var final = battle.Observe();
            result.Outcome = battle.Outcome.ToString();
            result.FinalPhase = final.PhaseId;
            result.FinalHp = final.PlayerHp;
            result.GoalRemaining = final.GoalRemaining;
            result.BossHp = final.CurrentHp;
            result.LeftoverNotes = Math.Max(0, settings.NoteCount - battle.LastClosedNote - 1);
            result.Phases[result.FinalPhase - 1].goalRemaining = result.GoalRemaining;
            if (result.Cleared) result.Phases[3].transitionNote = battle.LastClosedNote;
            foreach (var e in battle.Events)
                if (e.type.StartsWith("noise-", StringComparison.Ordinal) || e.type == "pattern-defer")
                    Increment(result.EventReasons, e.type);
            int previousHp = settings.PlayerMaxHp;
            for (int i = 0; i < battle.Records.Count; i++)
            {
                var record = battle.Records[i];
                if (record.noteIndex != i) Increment(result.InvariantFailures, "note-order");
                if (record.phase < 1 || record.phase > 4) Increment(result.InvariantFailures, "phase-range");
                if (record.x < 0 || record.x > 7 || record.y < 0 || record.y > 7)
                    Increment(result.InvariantFailures, "board-bounds");
                int distance = Math.Abs(record.x - record.beforeX) + Math.Abs(record.y - record.beforeY);
                if (distance != (record.category == "Move" ? 1 : 0)) Increment(result.InvariantFailures, "move-distance");
                if (record.damage < 0 || record.damage > 1 || record.damage > 0 && record.y != 7)
                    Increment(result.InvariantFailures, "attack-damage");
                if ((record.category == "EffectiveAttack") != (record.damage > 0))
                    Increment(result.InvariantFailures, "attack-category");
                if (record.playerHp < 0 || record.playerHp > previousHp)
                    Increment(result.InvariantFailures, "player-hp");
                if (record.bossHp < 0) Increment(result.InvariantFailures, "boss-hp");
                previousHp = record.playerHp;
            }
            if (result.Cleared && !battle.Health.Defeated) Increment(result.InvariantFailures, "win-before-defeat");
            if (battle.Records.Count > settings.NoteCount) Increment(result.InvariantFailures, "chart-overrun");
            timer.Stop(); result.ElapsedMilliseconds = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        { counts.TryGetValue(key, out int current); counts[key] = current + 1; }

        public static List<BattleRun> Batch(BattleSettings settings, int firstSeed, int count,
            bool analysis = false, double missRate = 0, Action<BattleRun, string> sink = null, string label = "batch")
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            var runs = new List<BattleRun>(count * 3);
            for (int seed = firstSeed; seed < firstSeed + count; seed++)
                foreach (StrategyKind strategy in Enum.GetValues(typeof(StrategyKind)))
                {
                    var run = Run(settings, seed, strategy, analysis, missRate);
                    sink?.Invoke(run, label);
                    // Calibration keeps summaries, rather than millions of replay records in memory.
                    run.Battle = null;
                    runs.Add(run);
                }
            return runs;
        }
    }

    public sealed class CalibrationResult
    {
        public int[] Health;
        public List<BattleRun> Analysis;
        public List<BattleRun> Actual;
        public List<BattleRun> Holdout;
        public List<BattleRun> Sensitivity;
        public readonly List<string> Iterations = new List<string>();
        public bool MeetsTarget;
        public string InvalidCandidate;
        public double ElapsedSeconds;
        public int ExecutedRuns, TotalInvariantFailures;
    }

    public static class BattleCalibration
    {
        public static int Percentile(IEnumerable<int> values, double percentile)
        {
            var sorted = values.OrderBy(value => value).ToArray();
            if (sorted.Length == 0) throw new ArgumentException("A percentile needs observations.");
            if (percentile <= 0 || percentile > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
            return sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
        }

        public static CalibrationResult Calibrate(BattleSettings settings, int count = 1000,
            Action<BattleRun, string> sink = null)
        {
            var timer = Stopwatch.StartNew();
            var result = new CalibrationResult();
            Action<BattleRun, string> capture = (run, label) =>
            {
                result.ExecutedRuns++; result.TotalInvariantFailures += run.InvariantFailureCount;
                sink?.Invoke(run, label);
            };
            result.Analysis = BattleSimulator.Batch(settings, 0, count, true, sink: capture, label: "analysis");
            result.Health = new int[4];
            for (int phase = 0; phase < 4; phase++)
            {
                int p10 = Percentile(result.Analysis.Where(r => r.Strategy == StrategyKind.CollectFirst)
                    .Select(r => r.Phases[phase].attackOpportunity), .1);
                result.Health[phase] = (int)Math.Floor(p10 * .875);
                result.Iterations.Add($"Initial phase {phase + 1}: P10={p10}, HP={result.Health[phase]} (floor(P10 * .875)).");
                if (result.Health[phase] < 1)
                    result.InvalidCandidate = (result.InvalidCandidate ?? "") +
                        $"Phase {phase + 1}: P10={p10}, floor(P10*.875)={result.Health[phase]}; no positive HP candidate. ";
            }
            if (result.InvalidCandidate != null)
            {
                result.Iterations.Add("Stopped: " + result.InvalidCandidate + "All failed/unvisited phase samples remain included.");
                result.Actual = new List<BattleRun>(); result.Holdout = new List<BattleRun>();
                result.Sensitivity = new List<BattleRun>();
                result.ElapsedSeconds = timer.Elapsed.TotalSeconds;
                return result;
            }
            settings.Health = (int[])result.Health.Clone();
            for (int iteration = 0; iteration < 8; iteration++)
            {
                result.Actual = BattleSimulator.Batch(settings, 0, count,
                    sink: capture, label: "calibration-" + (iteration + 1));
                var baseline = result.Actual.Where(r => r.Strategy == StrategyKind.CollectFirst).ToArray();
                double clearRate = baseline.Count(r => r.Cleared) / (double)baseline.Length;
                result.Iterations.Add($"Iteration {iteration + 1}: HP=[{string.Join(",", result.Health)}], actual clear={clearRate:P2}.");
                result.MeetsTarget = clearRate >= .9;
                if (result.MeetsTarget || iteration == 7) break;
                var failed = baseline.Where(r => !r.Cleared && r.Failure == "Timeout" &&
                    r.GoalRemaining == 0 && r.BossHp > 0).ToArray();
                bool changed = false;
                for (int phase = 0; phase < 4; phase++)
                {
                    int phaseId = phase + 1;
                    int failures = failed.Count(r => r.FinalPhase == phaseId);
                    if (failures == 0 || result.Health[phase] <= 1) continue;
                    int reduction = Math.Max(1, (int)Math.Floor(result.Health[phase] * .1));
                    // An integer HP below ten cannot be reduced by at most ten percent.
                    if (reduction > result.Health[phase] * .1) continue;
                    result.Health[phase] -= reduction;
                    result.Iterations.Add($"HP-only timeout: phase {phaseId}, failures={failures}, reduction={reduction}.");
                    changed = true;
                }
                if (!changed)
                {
                    result.Iterations.Add("Stopped: no phase has a reducible HP-only failure; movement/noise/death failures retained.");
                    break;
                }
                settings.Health = (int[])result.Health.Clone();
            }
            result.Holdout = BattleSimulator.Batch(settings, 100000, count, sink: capture, label: "holdout");
            result.Sensitivity = BattleSimulator.Batch(settings, 100000, count, false, .05, capture, "miss5");
            result.ElapsedSeconds = timer.Elapsed.TotalSeconds;
            return result;
        }
    }
}
