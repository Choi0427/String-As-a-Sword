using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    public sealed class RandomBossPatternTests
    {
        private static readonly GridPosition Player = new GridPosition(3, 7);

        private static BattleSettings Settings(int notes = 96) => new BattleSettings
        {
            Bpm = 87, NoteCount = notes, Health = new[] { 31, 6, 12, 47 },
            Schedule = new RandomAttackSchedule { Enabled = true },
            Patterns = new[] {
                new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2 },
                new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 1 },
                new PatternDefinition { Name = "Top", Kind = PatternKind.TopPressure, TelegraphNotes = 3 }
            }
        };

        private static double CloseTime(BattleSettings settings, int note)
            => note * 60.0 / settings.Bpm + settings.GoodWindow + .000001;

        private static BossPatternSystem SystemFor(BattleSettings settings, int seed)
        {
            var system = new BossPatternSystem(settings.Patterns, settings.Schedule, seed);
            system.BeginPhase(1, 0);
            return system;
        }

        private static List<BossTelegraph> Published(BossPatternSystem system, int notes)
        {
            var published = new List<BossTelegraph>();
            system.Changed += (type, attack) => { if (type == "publish") published.Add(attack); };
            for (int closed = -1; closed < notes; closed++)
            {
                system.Resolve(closed);
                system.Publish(closed, Player, notes, false);
                Assert.That(system.Active.Count, Is.LessThanOrEqualTo(1), "The global schedule must replace independent repeats.");
            }
            return published;
        }

        private static string Signature(BossTelegraph attack)
            => attack.Kind + ":" + attack.Cells + ":" + attack.ImpactNote + ":" + attack.WarningNotes + ":" + attack.SelectedInterval;

        [Test]
        public void DefaultIntervalWeightsHaveSixtyPercentCentralMassAndEightPointFiveSixMean()
        {
            var schedule = Settings().Schedule;
            Assert.That(schedule.MinInterval, Is.EqualTo(4));
            Assert.That(schedule.IntervalWeights.Length, Is.EqualTo(13));
            int total = schedule.IntervalWeights.Sum();
            Assert.That(schedule.IntervalWeights.Skip(2).Take(3).Sum() / (double)total, Is.EqualTo(.6));
            double mean = schedule.IntervalWeights.Select((weight, index) => weight * (schedule.MinInterval + index)).Sum() / (double)total;
            Assert.That(mean, Is.EqualTo(8.56).Within(1e-10));
        }

        [Test]
        public void OneThousandSeedsRepeatAndExposeSafeDiverseRangesWithWeightedGlobalIntervals()
        {
            var settings = Settings();
            var intervals = new List<int>();
            var ranges = new Dictionary<PatternKind, HashSet<ulong>>();
            var warnings = new HashSet<int>();
            for (int seed = 0; seed < 1000; seed++)
            {
                var attacks = Published(SystemFor(settings, seed), settings.NoteCount);
                Assert.That(attacks.Select(Signature), Is.EqualTo(Published(SystemFor(settings, seed), settings.NoteCount).Select(Signature)), "Seed " + seed);
                Assert.That(attacks, Is.Not.Empty, "Seed " + seed);
                BossTelegraph previous = null;
                foreach (var attack in attacks)
                {
                    Assert.That(attack.ImpactNote, Is.InRange(0, settings.NoteCount - 1));
                    Assert.That(attack.SelectedInterval, Is.InRange(4, 16));
                    Assert.That(attack.WarningNotes, Is.InRange(1, 3));
                    Assert.That(attack.RequiredDodgeActions, Is.InRange(0, attack.WarningNotes));
                    if (previous != null)
                    {
                        Assert.That(attack.ImpactNote - previous.ImpactNote, Is.EqualTo(attack.SelectedInterval));
                        Assert.That(attack.ActualInterval, Is.EqualTo(attack.ImpactNote - previous.ImpactNote));
                    }
                    AssertShapeAndDodge(attack, Player);
                    if (!ranges.TryGetValue(attack.Kind, out var cells)) ranges.Add(attack.Kind, cells = new HashSet<ulong>());
                    cells.Add(attack.Cells); warnings.Add(attack.WarningNotes); intervals.Add(attack.SelectedInterval);
                    previous = attack;
                }
            }
            Assert.That(intervals.Distinct().OrderBy(i => i), Is.EqualTo(Enumerable.Range(4, 13)));
            Assert.That(intervals.Count(i => i >= 6 && i <= 8) / (double)intervals.Count, Is.InRange(.57, .63));
            Assert.That(warnings.OrderBy(i => i), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(ranges[PatternKind.Row].Count, Is.EqualTo(8));
            Assert.That(ranges[PatternKind.Column].Count, Is.EqualTo(8));
            Assert.That(ranges[PatternKind.TopPressure].Count, Is.EqualTo(5));
            Assert.That(ranges[PatternKind.LockedCell].Single(), Is.EqualTo(SafeRoutePlanner.Bit(Player)));
        }

        private static void AssertShapeAndDodge(BossTelegraph attack, GridPosition player)
        {
            var cells = Enumerable.Range(0, 64).Where(i => (attack.Cells & (1UL << i)) != 0).ToArray();
            Assert.That(cells, Is.Not.Empty);
            if (attack.Kind == PatternKind.Row)
            { Assert.That(cells.Length, Is.EqualTo(8)); Assert.That(cells.Select(i => i / 8).Distinct().Count(), Is.EqualTo(1)); }
            else if (attack.Kind == PatternKind.Column)
            { Assert.That(cells.Length, Is.EqualTo(8)); Assert.That(cells.Select(i => i % 8).Distinct().Count(), Is.EqualTo(1)); }
            else if (attack.Kind == PatternKind.TopPressure)
            {
                Assert.That(cells.Length, Is.EqualTo(4));
                Assert.That(cells.All(i => i / 8 == 7), Is.True);
                Assert.That(cells.Last() - cells.First(), Is.EqualTo(3));
            }
            else Assert.That(cells.Single(), Is.EqualTo(player.Y * 8 + player.X));
            int minimum = Enumerable.Range(0, 64).Where(i => (attack.Cells & (1UL << i)) == 0)
                .Min(i => Math.Abs(i % 8 - player.X) + Math.Abs(i / 8 - player.Y));
            Assert.That(attack.RequiredDodgeActions, Is.EqualTo(minimum));
            Assert.That(minimum, Is.LessThanOrEqualTo(attack.WarningNotes));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LockedTargetAndDeadlineStayFrozenAfterPublicationAndMissesCloseCountdownOnce(int warning)
        {
            var settings = Settings();
            settings.Patterns = new[] { settings.Patterns[1] };
            settings.Patterns[0].TelegraphNotes = warning;
            var system = SystemFor(settings, 97);
            int closed = -1;
            for (; closed < settings.NoteCount && system.Active.Count == 0; closed++)
            { system.Resolve(closed); system.Publish(closed, Player, settings.NoteCount, false); }
            var attack = system.Active.Single();
            int publishedAt = closed - 1;
            ulong cells = attack.Cells; int impact = attack.ImpactNote;
            Assert.That(attack.Remaining(publishedAt), Is.EqualTo(attack.WarningNotes));
            system.Publish(publishedAt, new GridPosition(7, 0), settings.NoteCount, false);
            Assert.That(system.Active.Single(), Is.SameAs(attack));
            for (int note = publishedAt + 1; note < impact; note++)
            {
                system.Resolve(note);
                system.Publish(note, new GridPosition(7, 0), settings.NoteCount, false);
                Assert.That(attack.Cells, Is.EqualTo(cells));
                Assert.That(attack.ImpactNote, Is.EqualTo(impact));
                Assert.That(attack.Remaining(note), Is.EqualTo(impact - note));
            }
            Assert.That(system.Resolve(impact), Is.EqualTo(cells));
            Assert.That(system.Resolve(impact), Is.Zero, "A closed note cannot fire the same attack twice.");
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DodgeCheckCountsRequiredMovesAndRejectsInsufficientWarning(int distance)
        {
            var player = new GridPosition(3, 3);
            ulong cells = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if (Math.Abs(x - player.X) + Math.Abs(y - player.Y) < distance)
                    cells |= SafeRoutePlanner.Bit(new GridPosition(x, y));
            var attack = new BossTelegraph(1, "Synthetic multi-step range", cells, distance);
            Assert.That(BossPatternSystem.DodgeActions(player, attack, 0, Array.Empty<BossTelegraph>()), Is.EqualTo(distance));
            var tooLate = new BossTelegraph(1, "Too little warning", cells, distance - 1);
            Assert.That(BossPatternSystem.DodgeActions(player, tooLate, 0, Array.Empty<BossTelegraph>()), Is.EqualTo(-1));
        }

        [Test]
        public void DodgeCheckIncludesGuardWaitForDisclosedAttackAndRejectsBeyondThreeMoves()
        {
            var player = new GridPosition(3, 3);
            ulong neighbors = SafeRoutePlanner.Bit(new GridPosition(3, 4)) | SafeRoutePlanner.Bit(new GridPosition(2, 3)) |
                SafeRoutePlanner.Bit(new GridPosition(3, 2)) | SafeRoutePlanner.Bit(new GridPosition(4, 3));
            var disclosed = new[] { new BossTelegraph(1, "Neighbors attacked first", neighbors, 1) };
            var lockAttack = new BossTelegraph(2, "Lock after wait", SafeRoutePlanner.Bit(player), 2);
            Assert.That(BossPatternSystem.DodgeActions(player, lockAttack, 0, disclosed), Is.EqualTo(2),
                "The first opportunity guards in place; the second moves out after the disclosed ring fires.");
            ulong wide = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if (Math.Abs(x - player.X) + Math.Abs(y - player.Y) < 4) wide |= SafeRoutePlanner.Bit(new GridPosition(x, y));
            Assert.That(BossPatternSystem.DodgeActions(player, new BossTelegraph(3, "Four moves", wide, 3), 0,
                Array.Empty<BossTelegraph>()), Is.EqualTo(-1));
        }

        [Test]
        public void InvalidRandomConfigurationFailsBeforeACombatRun()
        {
            var settings = Settings();
            settings.Schedule = null;
            Assert.Throws<ArgumentException>(settings.Validate);
            settings = Settings(); settings.Schedule.IntervalWeights = new int[13];
            Assert.Throws<ArgumentException>(settings.Validate);
            settings = Settings(); settings.Schedule.IntervalWeights[0] = -1;
            Assert.Throws<ArgumentException>(settings.Validate);
            settings = Settings(); settings.Schedule.MinInterval = settings.Schedule.MaxWarning;
            Assert.Throws<ArgumentException>(settings.Validate);
        }

        [Test]
        public void PhaseChangeCancelsOldWarningAndNewPhaseOnlyPublishesEligiblePatterns()
        {
            var settings = Settings();
            settings.Patterns[0].PhaseMask = 1;
            settings.Patterns[1].PhaseMask = 2;
            settings.Patterns[2].Enabled = false;
            var system = SystemFor(settings, 31);
            var cancelled = new List<int>();
            system.Changed += (type, attack) => { if (type == "cancel") cancelled.Add(attack.Id); };
            int closed = -1;
            for (; closed < 32 && system.Active.Count == 0; closed++) system.Publish(closed, Player, 96, false);
            var old = system.Active.Single();
            system.BeginPhase(2, closed);
            Assert.That(cancelled, Does.Contain(old.Id));
            Assert.That(system.Active, Is.Empty);
            var later = new List<BossTelegraph>();
            system.Changed += (type, attack) => { if (type == "publish") later.Add(attack); };
            for (int note = closed - 1; note < 64; note++)
            { system.Resolve(note); system.Publish(note, Player, 96, false); }
            Assert.That(later, Is.Not.Empty);
            Assert.That(later.All(a => a.Kind == PatternKind.LockedCell && a.ImpactNote >= closed), Is.True);
            Assert.That(later.Any(a => a.Id == old.Id), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectedProposalsAreBoundedLoggedAndRecoverWithoutUnannouncedAttacks(bool waitingForNoise)
        {
            var settings = Settings();
            settings.Schedule.CandidateAttempts = 3; settings.Schedule.MaxDeferrals = 2;
            var system = SystemFor(settings, 12);
            var events = new List<(string Type, BossTelegraph Attack)>();
            system.Changed += (type, attack) => events.Add((type, attack));
            system.CanPublish = attack => waitingForNoise;
            for (int closed = -1; closed < 48; closed++)
            { Assert.That(system.Resolve(closed), Is.Zero); system.Publish(closed, Player, 96, waitingForNoise); }
            Assert.That(system.Active, Is.Empty);
            Assert.That(events.Any(e => e.Type == "skip"), Is.True);
            Assert.That(events.Count, Is.LessThanOrEqualTo(49 * (settings.Schedule.CandidateAttempts + 2)));
            Assert.That(events.Where(e => e.Type == "defer" || e.Type == "skip").All(e => !string.IsNullOrEmpty(e.Attack.Reason)), Is.True);
            system.CanPublish = attack => true;
            for (int closed = 48; closed < 80; closed++)
            { system.Resolve(closed); system.Publish(closed, Player, 96, false); }
            Assert.That(events.Any(e => e.Type == "publish"), Is.True);
            Assert.That(events.Where(e => e.Type == "publish").All(e => e.Attack.WarningNotes >= 1), Is.True);
        }

        [Test]
        public void DisabledAndOutOfChartPatternsCannotFire()
        {
            var settings = Settings(3);
            Assert.That(Published(SystemFor(settings, 0), settings.NoteCount), Is.Empty);
            settings.NoteCount = 96;
            foreach (var pattern in settings.Patterns) pattern.Enabled = false;
            Assert.That(Published(SystemFor(settings, 0), settings.NoteCount), Is.Empty);
        }

        [Test]
        public void PhaseTransitionKeepsThisNotesImpactForRendering()
        {
            var settings = Settings();
            var system = SystemFor(settings, 2);
            for (int note = -1; note < 32; note++)
            {
                ulong fired = system.Resolve(note);
                if (fired != 0)
                {
                    var impacts = system.LastFired.ToArray();
                    system.BeginPhase(2, note + 1);
                    Assert.That(system.LastFired, Is.EqualTo(impacts));
                    system.Resolve(note + 1);
                    Assert.That(system.LastFired, Is.Empty);
                    return;
                }
                system.Publish(note, Player, 96, false);
            }
            Assert.Fail("Fixture must reach an actual impact.");
        }

        [Test]
        public void SafetyDeferralReportsActualGapBeyondSelectedSixteenNotes()
        {
            var settings = Settings(96);
            settings.Schedule.MinInterval = 16; settings.Schedule.IntervalWeights = new[] { 1 };
            var system = SystemFor(settings, 2);
            var impacts = new List<BossTelegraph>();
            system.Changed += (type, attack) => { if (type == "fire") impacts.Add(attack); };
            for (int closed = -1; closed < 64; closed++)
            {
                system.Resolve(closed);
                bool hold = impacts.Count == 1 && closed < impacts[0].ImpactNote + 17;
                system.Publish(closed, Player, settings.NoteCount, hold);
            }
            Assert.That(impacts.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(impacts[1].SelectedInterval, Is.EqualTo(16));
            Assert.That(impacts[1].ActualInterval, Is.GreaterThan(16));
            Assert.That(impacts[1].ActualInterval, Is.EqualTo(impacts[1].ImpactNote - impacts[0].ImpactNote));
            settings.Schedule.IntervalWeights = new[] { 1, 1 };
            Assert.Throws<ArgumentException>(() => settings.Validate());
        }

        [TestCase(PlayerActionKind.Guard)]
        [TestCase(PlayerActionKind.Move)]
        public void RandomOneNoteWarningHonorsInclusiveLateGoodAndRejectsSecondAction(PlayerActionKind action)
        {
            var settings = Settings(32); settings.Patterns = new[] { settings.Patterns[1] };
            var battle = new BattleSession(settings, 71);
            battle.Noise.BeginPhase(1, 0, 0);
            for (int note = 0; note < 24 && battle.Patterns.Active.Count == 0; note++) battle.Advance(CloseTime(settings, note));
            var attack = battle.Patterns.Active.Single();
            Assert.That(attack.WarningNotes, Is.EqualTo(1));
            double lateGood = attack.ImpactNote * 60.0 / settings.Bpm + settings.GoodWindow;
            var result = battle.Request(action, MoveDirection.Up, lateGood);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Timing.ErrorSeconds, Is.EqualTo(settings.GoodWindow).Within(1e-8));
            Assert.That(battle.Request(PlayerActionKind.Attack, MoveDirection.Up, lateGood).Accepted, Is.False);
            battle.Advance(CloseTime(settings, attack.ImpactNote));
            var record = battle.Records.Single(r => r.noteIndex == attack.ImpactNote);
            Assert.That(record.hit, Is.False);
            Assert.That(record.guardSuccess, Is.EqualTo(action == PlayerActionKind.Guard));
        }

        [Test]
        public void PatternDrawsDoNotConsumeNoiseRandomness()
        {
            List<string> Spawns(bool rapid)
            {
                var settings = Settings(96); settings.PlayerMaxHp = 100;
                settings.NoiseTargets = new[] { 100, 100, 100 };
                settings.Schedule.IntervalWeights = rapid ? new[] { 1 } : new int[13];
                if (!rapid) settings.Schedule.IntervalWeights[12] = 1;
                settings.Patterns = rapid ? new[] { settings.Patterns[1] } : new[] { settings.Patterns[0] };
                var battle = new BattleSession(settings, 132);
                battle.Noise.CandidateAllowed = (cell, fallback) => true;
                for (int note = 0; note < settings.NoteCount; note++)
                {
                    battle.Advance(note * 60.0 / settings.Bpm);
                    foreach (var pickup in battle.Noise.Pickups.ToArray())
                        battle.Noise.TryCollect(pickup.Cell, battle.Noise.SessionId, battle.Noise.PhaseId);
                    battle.Request(PlayerActionKind.Guard, MoveDirection.Up, note * 60.0 / settings.Bpm);
                    battle.Advance(CloseTime(settings, note));
                }
                return battle.Events.Where(e => e.type == "noise-spawn").Select(e => e.musicBeat + ":" + e.x + ":" + e.y).ToList();
            }
            var fast = Spawns(true); var slow = Spawns(false);
            Assert.That(fast.Count, Is.GreaterThan(10));
            Assert.That(slow, Is.EqualTo(fast));
        }

        [Test]
        public void EveryPublishedPatternPreservesExistingPickupCollectionAndReturnRoutes()
        {
            int checkedWarnings = 0;
            for (int seed = 0; seed < 32; seed++)
            {
                var settings = Settings(128);
                var battle = new BattleSession(settings, seed);
                battle.Patterns.Changed += (type, attack) =>
                {
                    if (type != "publish" || battle.Noise.Pickups.Count == 0) return;
                    checkedWarnings++;
                    int next = Math.Max(battle.LastClosedNote + 1, battle.Session.Gate.LastConsumedBeat + 1);
                    var targets = battle.Noise.Pickups.Select(p => p.Cell).ToArray();
                    var route = SafeRoutePlanner.FindAll(battle.Session.Position, targets, next, battle.Patterns.Active,
                        Math.Min(settings.NoteCount - next, settings.MaxCollectionCost * targets.Length));
                    Assert.That(route.Found, Is.True, "Seed " + seed + ", note " + next);
                };
                for (int note = 0; note < settings.NoteCount && battle.Outcome == BattleOutcome.Running; note++)
                {
                    double time = note * 60.0 / settings.Bpm;
                    battle.Advance(time);
                    var decision = BattleStrategies.Decide(StrategyKind.CollectFirst, battle.Observe());
                    battle.Request(decision.Kind, decision.Direction, time);
                    battle.Advance(CloseTime(settings, note));
                }
            }
            Assert.That(checkedWarnings, Is.GreaterThan(20));
        }

        [Test]
        public void RandomBattleResetAndCompressedReplayPreserveEventsAndEveryNoteHash()
        {
            var settings = Settings(128);
            var run = BattleSimulator.RunOne(settings, 193, StrategyKind.MovementUse, null, .05).Battle;
            string path = Path.Combine(Path.GetTempPath(), "random-battle-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
            try
            {
                BattleLog.Write(path, run);
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Records.Select(r => r.stateHash), Is.EqualTo(run.Records.Select(r => r.stateHash)));
                Assert.That(replay.Events.Select(UnityEngine.JsonUtility.ToJson), Is.EqualTo(run.Events.Select(UnityEngine.JsonUtility.ToJson)));
                var inputs = run.Inputs.ToArray(); var hashes = run.Records.Select(r => r.stateHash).ToArray();
                double end = run.AdvancedTime;
                run.Reset(1, 193);
                foreach (var input in inputs) run.Request(input.action, input.direction, input.time);
                run.Advance(end);
                Assert.That(run.Records.Select(r => r.stateHash), Is.EqualTo(hashes));
                Assert.That(run.Records.Last().moves + run.Records.Last().guards + run.Records.Last().effectiveAttacks + run.Records.Last().others,
                    Is.EqualTo(run.Records.Count));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void BattleOwnsItsScheduleWeightsAndObservationDoesNotExposeFutureChoices()
        {
            var settings = Settings();
            var battle = new BattleSession(settings, 1);
            settings.Schedule.IntervalWeights[0] = 999;
            Assert.That(battle.Settings.Schedule.IntervalWeights[0], Is.EqualTo(1));
            Assert.That(typeof(BattleObservation).GetProperties().Select(p => p.Name), Does.Not.Contain("Seed"));
            Assert.That(typeof(BattleObservation).GetProperties().Select(p => p.Name), Does.Not.Contain("Schedule"));
            Assert.That(battle.Observe().Telegraphs.Select(a => a.Id), Is.EqualTo(battle.Patterns.Active.Select(a => a.Id)));
        }
    }
}
