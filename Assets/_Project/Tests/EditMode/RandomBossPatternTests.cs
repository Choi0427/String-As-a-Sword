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
        public void PlannerSidestepsAnUnblockableCellInsteadOfWaitingOnIt()
        {
            var start = new GridPosition(3, 3); var target = new GridPosition(3, 4);
            BossTelegraph[] Attacks(AttackResponse response) => new[] {
                new BossTelegraph(1, "Target", SafeRoutePlanner.Bit(target), 0),
                new BossTelegraph(2, "Start", SafeRoutePlanner.Bit(start), 0, response: response) };
            var guarded = SafeRoutePlanner.Find(start, target, 0, Attacks(AttackResponse.Either));
            Assert.That(guarded.Cost, Is.EqualTo(2));
            Assert.That(guarded.FirstAction, Is.EqualTo(PlayerActionKind.Guard));
            var dodged = SafeRoutePlanner.Find(start, target, 0, Attacks(AttackResponse.DodgeOnly));
            Assert.That(dodged.Found, Is.True);
            Assert.That(dodged.Cost, Is.EqualTo(3));
            Assert.That(dodged.FirstAction, Is.EqualTo(PlayerActionKind.Move));
        }

        [Test]
        public void PlannerFindsNoRouteWhenEveryChoiceLandsOnAnUnblockableCell()
        {
            var start = new GridPosition(3, 3);
            ulong star = SafeRoutePlanner.Bit(start) | Neighbors(start);
            var attacks = new[] { new BossTelegraph(1, "Star", star, 0, response: AttackResponse.DodgeOnly) };
            Assert.That(SafeRoutePlanner.Find(start, new GridPosition(0, 0), 0, attacks).Found, Is.False);
            Assert.That(SafeRoutePlanner.Find(start, new GridPosition(0, 0), 0, new[] { new BossTelegraph(1, "Star", star, 0) }).Found, Is.True);
        }

        [Test]
        public void RouteThatArrivesBeforeAnUnblockableImpactMustOutliveIt()
        {
            var start = new GridPosition(3, 6);
            BossTelegraph[] Wipe(AttackResponse response) => new[] { new BossTelegraph(1, "Top Wipe", 0xFFUL << 56, 2, response: response) };
            Assert.That(SafeRoutePlanner.Find(start, new GridPosition(3, 7), 0, Wipe(AttackResponse.Either), returnToTop: true).Cost, Is.EqualTo(1));
            Assert.That(SafeRoutePlanner.Find(start, new GridPosition(3, 7), 0, Wipe(AttackResponse.DodgeOnly), returnToTop: true).Cost, Is.EqualTo(4),
                "Row 8 is forbidden on the impact note, so the route may only finish a note later.");
        }

        [Test]
        public void DodgeCheckCannotWaitThroughADisclosedUnblockableAttack()
        {
            var player = new GridPosition(3, 3);
            ulong star = SafeRoutePlanner.Bit(player) | Neighbors(player);
            var far = new BossTelegraph(2, "Far row", 0xFFUL, 2);
            Assert.That(BossPatternSystem.DodgeActions(player, far, 0, new[] { new BossTelegraph(1, "Star", star, 1) }), Is.EqualTo(1),
                "One guarded wait is enough: the far row misses the player's cell.");
            Assert.That(BossPatternSystem.DodgeActions(player, far, 0, new[] { new BossTelegraph(1, "Star", star, 1, response: AttackResponse.DodgeOnly) }), Is.EqualTo(-1));
        }

        [Test]
        public void TopWipeIsOnlyValidUnderAScheduleWhenUnblockableAndAlwaysLeavesARoute()
        {
            var settings = Settings();
            settings.Patterns = new[] { new PatternDefinition { Name = "Top Wipe", Kind = PatternKind.TopPressure, ColumnCount = 8, TelegraphNotes = 1 } };
            Assert.Throws<ArgumentException>(settings.Validate);
            settings.Patterns[0].Response = AttackResponse.GuardOnly;
            Assert.Throws<ArgumentException>(settings.Validate);
            settings.Patterns[0].Response = AttackResponse.DodgeOnly;
            Assert.DoesNotThrow(settings.Validate);
            var attacks = Published(SystemFor(settings, 5), settings.NoteCount);
            Assert.That(attacks, Is.Not.Empty);
            Assert.That(attacks.All(a => a.Response == AttackResponse.DodgeOnly && a.Cells == 0xFFUL << 56 && a.RequiredDodgeActions == 1), Is.True);
        }

        private static PatternDefinition Dodge(string name, PatternKind kind, int warning = 1, int hits = 1, int columns = 4) => new PatternDefinition {
            Name = name, Kind = kind, Response = AttackResponse.DodgeOnly, TelegraphNotes = warning, Hits = hits, ColumnCount = columns };
        private static PatternDefinition Guard(string name, int hits, int spacing = 1) => new PatternDefinition {
            Name = name, Kind = PatternKind.FullBoard, Response = AttackResponse.GuardOnly, TelegraphNotes = 2, Hits = hits, HitSpacing = spacing };
        private static PatternDefinition[] DodgeSet() => new[] {
            Dodge("Top Wipe", PatternKind.TopPressure, columns: 8), Dodge("Upper Crush", PatternKind.UpperHalf, 4),
            Dodge("Checker", PatternKind.Checker, 2, 2), Dodge("Ring", PatternKind.Ring, 2, 2) };

        private static BattleSettings Variety(int notes = 128)
        {
            var settings = Settings(notes);
            settings.PlayerMaxHp = 1000; settings.Schedule.MaxWarning = 4;
            settings.Patterns = settings.Patterns.Concat(DodgeSet()).Concat(new[] {
                new PatternDefinition { Name = "Cross", Kind = PatternKind.Cross, TelegraphNotes = 2 },
                Guard("Chord", 2), Guard("Tremolo", 4), Guard("Syncopation", 4, 2) }).ToArray();
            return settings;
        }

        // Independent of DodgeActions: the actions per note (0-3 move, 4 stay) that are off every hit when it lands.
        private static List<int> SurvivalPlan(GridPosition start, int firstNote, IReadOnlyList<BossTelegraph> hits)
        {
            int last = hits.Max(h => h.ImpactNote);
            var layer = new Dictionary<int, List<int>> { { start.Y * 8 + start.X, new List<int>() } };
            for (int note = firstNote; note <= last; note++)
            {
                ulong danger = SafeRoutePlanner.Danger(hits, note);
                var next = new Dictionary<int, List<int>>();
                foreach (var entry in layer.OrderBy(e => e.Key))
                    for (int action = 0; action < 5; action++)
                    {
                        int x = entry.Key % 8, y = entry.Key / 8;
                        if (action == 0) y++; else if (action == 1) x--; else if (action == 2) y--; else if (action == 3) x++;
                        if (x < 0 || y < 0 || x > 7 || y > 7 || (danger & (1UL << (y * 8 + x))) != 0 || next.ContainsKey(y * 8 + x)) continue;
                        next[y * 8 + x] = new List<int>(entry.Value) { action };
                    }
                layer = next;
            }
            return layer.Count == 0 ? null : layer.OrderBy(e => e.Key).First().Value;
        }

        [Test]
        public void ShapesCoverTheirCellsForEveryHit()
        {
            int Count(ulong mask) => Enumerable.Range(0, 64).Count(i => (mask & (1UL << i)) != 0);
            var player = new GridPosition(2, 5);
            ulong cross = BossPatternSystem.Cells(new PatternDefinition { Kind = PatternKind.Cross }, player);
            Assert.That(Count(cross), Is.EqualTo(15));
            Assert.That(cross & SafeRoutePlanner.Bit(new GridPosition(3, 4)), Is.Zero);
            Assert.That(BossPatternSystem.Cells(new PatternDefinition { Kind = PatternKind.FullBoard }, player), Is.EqualTo(ulong.MaxValue));
            Assert.That(BossPatternSystem.Cells(new PatternDefinition { Kind = PatternKind.UpperHalf }, player), Is.EqualTo(0xFFFFFFFF00000000UL));
            var checker = new PatternDefinition { Kind = PatternKind.Checker };
            ulong even = BossPatternSystem.Cells(checker, player), odd = BossPatternSystem.Cells(checker, player, 1);
            Assert.That(Count(even), Is.EqualTo(32));
            Assert.That(even ^ odd, Is.EqualTo(ulong.MaxValue));
            var ring = new PatternDefinition { Kind = PatternKind.Ring };
            ulong outer = BossPatternSystem.Cells(ring, player), inner = BossPatternSystem.Cells(ring, player, 1);
            Assert.That(Count(outer), Is.EqualTo(28));
            Assert.That(Count(inner), Is.EqualTo(20));
            Assert.That(outer & inner, Is.Zero);
            Assert.That(outer & SafeRoutePlanner.Bit(new GridPosition(0, 7)), Is.Not.Zero);
            Assert.That(inner & SafeRoutePlanner.Bit(new GridPosition(1, 6)), Is.Not.Zero);
        }

        [Test]
        public void HitRangesWarningCeilingAndFullBoardResponseAreValidated()
        {
            void Rejected(Action<PatternDefinition> change)
            { var s = Variety(); change(s.Patterns[0]); Assert.Throws<ArgumentException>(s.Validate); }
            Assert.DoesNotThrow(Variety().Validate);
            Rejected(p => p.Hits = 0); Rejected(p => p.Hits = PatternDefinition.MaxHits + 1);
            Rejected(p => p.HitSpacing = 0); Rejected(p => p.HitSpacing = PatternDefinition.MaxHitSpacing + 1);
            Rejected(p => p.Kind = PatternKind.FullBoard);
            Rejected(p => { p.Kind = PatternKind.FullBoard; p.Response = AttackResponse.DodgeOnly; });
            Rejected(p => { p.Kind = PatternKind.Row; p.Response = AttackResponse.GuardOnly; });
            var settings = Variety(); settings.Schedule.MaxWarning = RandomAttackSchedule.WarningCeiling + 1;
            Assert.Throws<ArgumentException>(settings.Validate);
        }

        [TestCase(PlayerActionKind.Guard)]
        [TestCase(PlayerActionKind.Move)]
        public void FullBoardIsBlockedByGuardOnEveryHitAndHitsWhoeverMoves(PlayerActionKind action)
        {
            var settings = Settings(32); settings.Patterns = new[] { Guard("Chord", 2) };
            var battle = new BattleSession(settings, 71);
            battle.Noise.BeginPhase(1, 0, 0);
            for (int note = 0; note < 24 && battle.Patterns.Active.Count == 0; note++) battle.Advance(CloseTime(settings, note));
            var hits = battle.Patterns.Active.ToArray();
            Assert.That(hits.Select(h => h.Hit), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(hits.All(h => h.Response == AttackResponse.GuardOnly && h.Cells == ulong.MaxValue && h.RequiredDodgeActions == 0), Is.True);
            int start = settings.PlayerMaxHp;
            foreach (var hit in hits)
            {
                battle.Advance(CloseTime(settings, hit.ImpactNote - 1));
                var direction = hit.Hit == 0 ? MoveDirection.Up : MoveDirection.Down;
                Assert.That(battle.Request(action, direction, hit.ImpactNote * 60.0 / settings.Bpm).Accepted, Is.True);
                battle.Advance(CloseTime(settings, hit.ImpactNote));
            }
            bool guarded = action == PlayerActionKind.Guard;
            Assert.That(battle.PlayerHp, Is.EqualTo(guarded ? start : start - 2));
            Assert.That(battle.Events.Count(e => e.type == "guard-success" && e.response == "GuardOnly"), Is.EqualTo(guarded ? 2 : 0));
            Assert.That(battle.Events.Count(e => e.type == "player-hit" && e.response == "GuardOnly"), Is.EqualTo(guarded ? 0 : 2));
            Assert.That(battle.Events.Any(e => e.type == "guard-broken"), Is.False);
        }

        [Test]
        public void GuardOnlyPublishesFromEveryCellWithoutARouteCheck()
        {
            var settings = Settings(); settings.Patterns = new[] { Guard("Chord", 2) };
            for (int cell = 0; cell < 64; cell++)
            {
                var system = SystemFor(settings, cell);
                var published = new List<BossTelegraph>();
                system.Changed += (type, attack) => { if (type == "publish") published.Add(attack); };
                for (int closed = -1; closed < 16 && published.Count == 0; closed++) system.Publish(closed, new GridPosition(cell % 8, cell / 8), 96, false);
                Assert.That(published.Count, Is.EqualTo(2), "Cell " + cell);
            }
        }

        [Test]
        public void NextPatternIsPlannedOnlyAfterTheLastHitOfASequence()
        {
            var settings = Settings();
            settings.Patterns = new[] { Guard("Syncopation", 4, 4), new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 1 } };
            for (int seed = 0; seed < 64; seed++)
            {
                var system = SystemFor(settings, seed);
                var published = new List<(int Closed, BossTelegraph Attack)>();
                int closedNote = -1;
                system.Changed += (type, attack) => { if (type == "publish") published.Add((closedNote, attack)); };
                for (closedNote = -1; closedNote < settings.NoteCount; closedNote++)
                { system.Resolve(closedNote); system.Publish(closedNote, Player, settings.NoteCount, false); }
                var starts = published.Where(p => p.Attack.Hit == 0).ToArray();
                Assert.That(starts.Length, Is.GreaterThan(2), "Seed " + seed);
                foreach (var entry in published.Where(p => p.Attack.Name == "Syncopation"))
                {
                    var first = starts.Last(s => s.Attack.Id <= entry.Attack.Id);
                    Assert.That(entry.Closed, Is.EqualTo(first.Closed), "All hits are disclosed together.");
                    Assert.That(entry.Attack.ImpactNote, Is.EqualTo(first.Attack.ImpactNote + entry.Attack.Hit * 4));
                    Assert.That(entry.Attack.Remaining(entry.Closed), Is.EqualTo(entry.Attack.WarningNotes));
                }
                for (int i = 1; i < starts.Length; i++)
                {
                    int end = published.Where(p => p.Attack.Id < starts[i].Attack.Id).Max(p => p.Attack.ImpactNote);
                    Assert.That(starts[i].Closed, Is.GreaterThanOrEqualTo(end), "Seed " + seed);
                    Assert.That(starts[i].Attack.ImpactNote - end, Is.EqualTo(starts[i].Attack.SelectedInterval), "The interval runs from the last hit.");
                    Assert.That(starts[i].Attack.ActualInterval, Is.EqualTo(starts[i].Attack.ImpactNote - end));
                }
            }
        }

        [Test]
        public void PhaseChangeAfterTheFirstHitCancelsTheRestAndPlansTheNextPattern()
        {
            var settings = Settings(); settings.Patterns = new[] { Guard("Tremolo", 4) };
            var system = SystemFor(settings, 9);
            var events = new List<(string Type, BossTelegraph Attack)>();
            system.Changed += (type, attack) => events.Add((type, attack));
            int note = -1;
            for (; note < 32 && system.Active.Count == 0; note++) system.Publish(note, Player, 96, false);
            int impact = system.Active[0].ImpactNote;
            for (; note <= impact; note++) { system.Resolve(note); system.Publish(note, Player, 96, false); }
            Assert.That(system.Active.Count, Is.EqualTo(3));
            Assert.That(events.Count(e => e.Type == "publish"), Is.EqualTo(4), "Nothing new is planned mid-sequence.");
            system.BeginPhase(2, impact + 1);
            Assert.That(system.Active, Is.Empty);
            Assert.That(events.Count(e => e.Type == "cancel"), Is.EqualTo(3));
            for (note = impact; note < impact + 32 && system.Active.Count == 0; note++) { system.Resolve(note); system.Publish(note, Player, 96, false); }
            var next = system.Active[0];
            Assert.That(next.Hit, Is.Zero);
            Assert.That(next.Id, Is.GreaterThan(4));
            Assert.That(next.ActualInterval, Is.EqualTo(next.ImpactNote - impact), "The cancelled hits never set the interval anchor.");
            Assert.That(next.ImpactNote - impact, Is.EqualTo(next.SelectedInterval));
        }

        [Test]
        public void SequenceWhoseLastHitLeavesTheChartIsSkippedNotPublished()
        {
            var settings = Settings(20);
            settings.Patterns = new[] { Guard("Long", 8, 4) };
            var system = SystemFor(settings, 3);
            var events = new List<(string Type, BossTelegraph Attack)>();
            system.Changed += (type, attack) => events.Add((type, attack));
            for (int closed = -1; closed < settings.NoteCount; closed++) { system.Resolve(closed); system.Publish(closed, Player, settings.NoteCount, false); }
            Assert.That(events.Any(e => e.Type == "publish"), Is.False);
            Assert.That(events.Single().Type, Is.EqualTo("skip"));
            Assert.That(events.Single().Attack.Reason, Is.EqualTo("chart-end"));
        }

        [Test]
        public void ChartEndSkipOfALongSequenceStillLetsAShorterPatternFire()
        {
            var settings = Settings(40);
            settings.Patterns = new[] { Guard("Long", 8, 4), new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2 } };
            int resumed = 0;
            for (int seed = 0; seed < 32; seed++)
            {
                var system = SystemFor(settings, seed);
                var events = new List<(string Type, BossTelegraph Attack)>();
                system.Changed += (type, attack) => events.Add((type, attack));
                for (int closed = -1; closed < settings.NoteCount; closed++) { system.Resolve(closed); system.Publish(closed, Player, settings.NoteCount, false); }
                int skip = events.FindIndex(e => e.Type == "skip" && e.Attack.Reason == "chart-end" && e.Attack.Name == "Long");
                if (skip >= 0 && events.Skip(skip).Any(e => e.Type == "publish")) resumed++;
            }
            Assert.That(resumed, Is.GreaterThan(0));
        }

        [Test]
        public void EveryPublishedDodgeOnlySequenceHasARoutePastEveryHitAcrossManySeeds()
        {
            var settings = Settings(160); settings.Schedule.MaxWarning = 4; settings.Patterns = DodgeSet();
            var kinds = new HashSet<PatternKind>(); int sequences = 0, longRoutes = 0;
            for (int seed = 0; seed < 400; seed++)
            {
                var system = SystemFor(settings, seed);
                var positions = new SeededRandom(seed + 1);
                var published = new List<BossTelegraph>();
                system.Changed += (type, attack) => { if (type == "publish") published.Add(attack); };
                for (int closed = -1; closed < settings.NoteCount; closed++)
                {
                    system.Resolve(closed);
                    var player = new GridPosition(positions.Next(8), positions.Next(8));
                    published.Clear();
                    system.Publish(closed, player, settings.NoteCount, false);
                    if (published.Count == 0) continue;
                    sequences++; kinds.Add(published[0].Kind);
                    int last = published[published.Count - 1].ImpactNote;
                    Assert.That(last, Is.LessThan(settings.NoteCount));
                    Assert.That(published.All(h => h.Response == AttackResponse.DodgeOnly && h.WarningNotes == h.ImpactNote - closed), Is.True);
                    Assert.That(published[0].WarningNotes, Is.LessThanOrEqualTo(4));
                    Assert.That(SurvivalPlan(player, closed + 1, published), Is.Not.Null, "Seed " + seed + ", note " + closed + ", " + published[0].Name + " from " + player);
                    int required = published[0].RequiredDodgeActions;
                    Assert.That(required, Is.InRange(0, last - closed));
                    if (required > published[0].WarningNotes) longRoutes++;
                }
            }
            Assert.That(sequences, Is.GreaterThan(1000));
            Assert.That(kinds.Count, Is.EqualTo(4));
            Assert.That(longRoutes, Is.GreaterThan(0), "The action count must reach past the first hit.");
        }

        [Test]
        public void SecondHitBlocksAnOtherwiseSafeDestinationInTheSequenceCheck()
        {
            // Only the far row hits last, so a check of the last hit alone would pass; the first hit closes the star.
            var player = new GridPosition(3, 3);
            ulong star = SafeRoutePlanner.Bit(player) | Neighbors(player);
            var last = new BossTelegraph(2, "Far row", 0xFFUL, 2, response: AttackResponse.DodgeOnly);
            Assert.That(BossPatternSystem.DodgeActions(player, last, 0, Array.Empty<BossTelegraph>()), Is.Zero);
            Assert.That(BossPatternSystem.DodgeActions(player, last, 0, new[] { new BossTelegraph(1, "Star", star, 1, response: AttackResponse.DodgeOnly) }), Is.EqualTo(-1));
            var checker = new PatternDefinition { Kind = PatternKind.Checker };
            var hits = new[] { new BossTelegraph(1, "A", BossPatternSystem.Cells(checker, player), 1, response: AttackResponse.DodgeOnly),
                new BossTelegraph(2, "B", BossPatternSystem.Cells(checker, player, 1), 2, response: AttackResponse.DodgeOnly) };
            Assert.That(BossPatternSystem.DodgeActions(player, hits[1], 0, new[] { hits[0] }), Is.EqualTo(2), "Both parities are covered, so the route runs to the last hit.");
            Assert.That(BossPatternSystem.DodgeActions(player, new BossTelegraph(3, "Long", 0xFFUL, 32), 0, Array.Empty<BossTelegraph>()), Is.Zero);
            Assert.That(BossPatternSystem.DodgeActions(player, new BossTelegraph(3, "Too long", 0xFFUL, 33), 0, Array.Empty<BossTelegraph>()), Is.EqualTo(-1));
        }

        [Test]
        public void UpperCrushLengthensWarningToFourFromTheTopRowOnly()
        {
            var settings = Settings(); settings.Schedule.MaxWarning = 4;
            settings.Patterns = new[] { Dodge("Upper Crush", PatternKind.UpperHalf) };
            BossTelegraph First(GridPosition player)
            {
                var system = SystemFor(settings, 4);
                for (int closed = -1; closed < 32; closed++) { system.Publish(closed, player, 96, false); if (system.Active.Count > 0) return system.Active[0]; }
                return null;
            }
            var top = First(new GridPosition(3, 7));
            Assert.That(top.WarningNotes, Is.EqualTo(4));
            Assert.That(top.RequiredDodgeActions, Is.EqualTo(4));
            var safe = First(new GridPosition(3, 3));
            Assert.That(safe.WarningNotes, Is.EqualTo(1));
            Assert.That(safe.RequiredDodgeActions, Is.Zero);
            settings.Schedule.MaxWarning = 3;
            Assert.That(First(new GridPosition(3, 7)), Is.Null, "Three moves cannot clear four rows, so it never publishes.");
            settings.Schedule.MaxWarning = 5; settings.Patterns[0].TelegraphNotes = 5;
            var slack = First(new GridPosition(3, 7));
            Assert.That(slack.RequiredDodgeActions, Is.LessThan(slack.WarningNotes), "A fifth warning note leaves time to react from row 8.");
        }

        [TestCase(PatternKind.TopPressure, 8, 1, 1)]
        [TestCase(PatternKind.UpperHalf, 4, 4, 1)]
        [TestCase(PatternKind.Checker, 4, 2, 2)]
        [TestCase(PatternKind.Ring, 4, 2, 2)]
        public void FollowingThePublishedRouteInASessionTakesNoDamageAndGuardingInPlaceDoes(PatternKind kind, int columns, int warning, int hits)
        {
            var settings = Settings(64); settings.Schedule.MaxWarning = 4; settings.PlayerMaxHp = 100;
            settings.Schedule.MinInterval = 16; settings.Schedule.IntervalWeights = new[] { 1 };
            settings.Patterns = new[] { Dodge(kind.ToString(), kind, warning, hits, columns) };
            foreach (bool follow in new[] { true, false })
            {
                var battle = new BattleSession(settings, 19);
                battle.Noise.BeginPhase(1, 0, 0);
                int note = 0;
                for (; note < 48 && battle.Patterns.Active.Count == 0; note++)
                {
                    double time = note * 60.0 / settings.Bpm;
                    if (battle.Session.Position.Y < 7) battle.Request(PlayerActionKind.Move, MoveDirection.Up, time);
                    battle.Advance(CloseTime(settings, note));
                }
                var telegraphs = battle.Patterns.Active.ToArray();
                Assert.That(telegraphs.Length, Is.EqualTo(hits));
                var plan = SurvivalPlan(battle.Session.Position, note, telegraphs);
                Assert.That(plan, Is.Not.Null);
                foreach (int action in plan)
                {
                    double time = note * 60.0 / settings.Bpm;
                    if (follow && action < 4) Assert.That(battle.Request(PlayerActionKind.Move, (MoveDirection)action, time).Accepted, Is.True);
                    else battle.Request(PlayerActionKind.Guard, MoveDirection.Up, time);
                    battle.Advance(CloseTime(settings, note++));
                }
                Assert.That(battle.Events.Count(e => e.type == "pattern-fire"), Is.EqualTo(hits));
                if (follow)
                {
                    Assert.That(battle.PlayerHp, Is.EqualTo(settings.PlayerMaxHp), kind.ToString());
                    Assert.That(battle.Events.Any(e => e.type == "guard-broken" || e.type == "player-hit"), Is.False);
                }
                else
                {
                    int broken = battle.Events.Count(e => e.type == "guard-broken");
                    Assert.That(broken, Is.GreaterThan(0), "Standing on row 8 and guarding must break for " + kind);
                    Assert.That(battle.PlayerHp, Is.EqualTo(settings.PlayerMaxHp - broken));
                }
            }
        }

        [Test]
        public void ThePreviousPatternRepeatsOnlyWhenNothingElseIsEligible()
        {
            var settings = Settings();
            settings.Patterns = new[] { Guard("Chord", 1), new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 1 } };
            for (int seed = 0; seed < 64; seed++)
            {
                var names = Published(SystemFor(settings, seed), settings.NoteCount).Select(a => a.Name).ToArray();
                Assert.That(names.Length, Is.GreaterThan(3));
                for (int i = 1; i < names.Length; i++) Assert.That(names[i], Is.Not.EqualTo(names[i - 1]), "Seed " + seed);
            }
            settings.Patterns[1].Enabled = false;
            Assert.That(Published(SystemFor(settings, 1), settings.NoteCount).Count, Is.GreaterThan(3));
        }

        [Test]
        public void PickupRouteCheckSeesEveryHitOfTheSequence()
        {
            var settings = Settings(); settings.Patterns = new[] { Dodge("Ring", PatternKind.Ring, 2, 2) }; settings.Schedule.MaxWarning = 4;
            var system = SystemFor(settings, 6);
            var seen = new List<int>();
            system.CanPublish = hits => { seen.Add(hits.Count); return true; };
            for (int closed = -1; closed < 32; closed++) { system.Resolve(closed); system.Publish(closed, Player, 96, false); }
            Assert.That(seen, Is.Not.Empty);
            Assert.That(seen.All(count => count == 2), Is.True);

            int checkedWarnings = 0;
            for (int seed = 0; seed < 24; seed++)
            {
                var variety = Variety(128); variety.MaxCollectionCost = seed % 2 == 0 ? 6 : 12;
                var battle = new BattleSession(variety, seed);
                battle.Patterns.Changed += (type, attack) =>
                {
                    if (type != "publish" || attack.Hit != 0 || battle.Noise.Pickups.Count == 0) return;
                    checkedWarnings++;
                    int next = Math.Max(battle.LastClosedNote + 1, battle.Session.Gate.LastConsumedBeat + 1);
                    var targets = battle.Noise.Pickups.Select(p => p.Cell).ToArray();
                    int last = battle.Patterns.Active.Max(a => a.ImpactNote);
                    var route = SafeRoutePlanner.FindAll(battle.Session.Position, targets, next, battle.Patterns.Active,
                        Math.Min(variety.NoteCount - next, variety.MaxCollectionCost * targets.Length));
                    Assert.That(route.Found, Is.True, "Seed " + seed + ", note " + next + ", " + attack.Name);
                    if (attack.Response == AttackResponse.DodgeOnly) Assert.That(route.Cost, Is.GreaterThan(last - next), "A route outlives unblockable hits.");
                };
                for (int note = 0; note < variety.NoteCount && battle.Outcome == BattleOutcome.Running; note++)
                {
                    double time = note * 60.0 / variety.Bpm;
                    battle.Advance(time);
                    var decision = BattleStrategies.Decide(StrategyKind.CollectFirst, battle.Observe());
                    battle.Request(decision.Kind, decision.Direction, time);
                    battle.Advance(CloseTime(variety, note));
                }
            }
            Assert.That(checkedWarnings, Is.GreaterThan(20));
        }

        [Test]
        public void SessionPickupBudgetCountsTheNotesLostToEveryGuardedHit()
        {
            var settings = Settings(96); settings.Patterns = Array.Empty<PatternDefinition>();
            var battle = new BattleSession(settings, 8);
            for (int note = 0; note < 32 && battle.Noise.Pickups.Count == 0; note++) battle.Advance(CloseTime(settings, note));
            Assert.That(battle.Noise.Pickups.Count, Is.EqualTo(1));
            int next = battle.LastClosedNote + 1;
            var open = SafeRoutePlanner.FindAll(battle.Session.Position, new[] { battle.Noise.Pickups[0].Cell }, next, Array.Empty<BossTelegraph>(), 96);
            battle.Settings.MaxCollectionCost = open.Cost + 1;
            var hits = new[] { new BossTelegraph(1, "A", ulong.MaxValue, next, response: AttackResponse.GuardOnly),
                new BossTelegraph(2, "B", ulong.MaxValue, next + 1, response: AttackResponse.GuardOnly, hit: 1) };
            Assert.That(battle.Patterns.CanPublish(new[] { hits[0] }), Is.True, "One guarded note fits the budget.");
            Assert.That(battle.Patterns.CanPublish(hits), Is.False, "The second guarded note does not.");
        }

        [TestCase(StrategyKind.CollectFirst)]
        [TestCase(StrategyKind.MovementUse)]
        public void SameSeedReplaysEveryResponseIdenticallyThroughTheLog(StrategyKind strategy)
        {
            var settings = Variety(160);
            var run = BattleSimulator.RunOne(settings, 411, strategy, null, .05).Battle;
            var again = BattleSimulator.RunOne(settings, 411, strategy, null, .05).Battle;
            Assert.That(again.Events.Select(UnityEngine.JsonUtility.ToJson), Is.EqualTo(run.Events.Select(UnityEngine.JsonUtility.ToJson)));
            Assert.That(again.Records.Select(r => r.stateHash), Is.EqualTo(run.Records.Select(r => r.stateHash)));
            var responses = run.Events.Where(e => e.type == "pattern-publish").Select(e => e.response).Distinct().ToArray();
            Assert.That(responses, Is.EquivalentTo(new[] { "Either", "GuardOnly", "DodgeOnly" }));
            string path = Path.Combine(Path.GetTempPath(), "variety-battle-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
            try
            {
                BattleLog.Write(path, run);
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Records.Select(r => r.stateHash), Is.EqualTo(run.Records.Select(r => r.stateHash)));
                Assert.That(replay.Events.Select(UnityEngine.JsonUtility.ToJson), Is.EqualTo(run.Events.Select(UnityEngine.JsonUtility.ToJson)));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static ulong Neighbors(GridPosition p) => SafeRoutePlanner.Bit(new GridPosition(p.X, p.Y + 1)) | SafeRoutePlanner.Bit(new GridPosition(p.X - 1, p.Y)) |
            SafeRoutePlanner.Bit(new GridPosition(p.X, p.Y - 1)) | SafeRoutePlanner.Bit(new GridPosition(p.X + 1, p.Y));

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
