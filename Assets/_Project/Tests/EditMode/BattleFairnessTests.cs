using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Simulation;
using StringAsASword.Stage;

namespace StringAsASword.Tests
{
    public sealed class BattleFairnessTests
    {
        private static readonly GridPosition Player = new GridPosition(3, 3);

        [Test]
        public void LatePhaseTwoStillSpawnsReachableNoiseAcrossOneThousandSeeds()
        {
            for (int seed = 0; seed < 1000; seed++)
            {
                var settings = new BattleSettings { Bpm = 87, NoteCount = 344,
                    Health = new[] { 62, 12, 24, 94 }, Patterns = Array.Empty<PatternDefinition>() };
                var battle = new BattleSession(settings, seed);
                int note = 0, delay = 60;
                for (; note < 220 && battle.Phases.PhaseId == 1; note++)
                {
                    double time = note * 60.0 / settings.Bpm;
                    battle.Advance(time);
                    var decision = BattleStrategies.Decide(StrategyKind.CollectFirst, battle.Observe());
                    if (battle.Noise.GoalReached && delay > 0)
                    { decision = new BattleDecision(PlayerActionKind.Guard); delay--; }
                    battle.Request(decision.Kind, decision.Direction, time);
                    battle.Advance(time + settings.GoodWindow + .000001);
                }
                Assert.That(battle.Phases.PhaseId, Is.EqualTo(2), "Late transition fixture, seed " + seed);
                Assert.That(delay, Is.Zero);
                for (int attempt = 0; attempt < 3; attempt++)
                    battle.Advance((note + attempt) * 60.0 / settings.Bpm + settings.GoodWindow + .000001);
                Assert.That(battle.Noise.Pickups, Is.Not.Empty, "Reachable fallback must spawn, seed " + seed);
                Assert.That(battle.Events.Any(e => e.phase == 2 && e.type == "noise-fallback"), Is.True);
                foreach (var pickup in battle.Noise.Pickups)
                    Assert.That(SafeRoutePlanner.Find(battle.Session.Position, pickup.Cell,
                        battle.LastClosedNote + 1, battle.Patterns.Active, settings.MaxCollectionCost, true).Found, Is.True);
            }
        }

        [Test]
        public void FallbackStillRejectsACollectionRouteThatCannotFinishBeforeTheLastNote()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 3, Patterns = Array.Empty<PatternDefinition>() };
            var battle = new BattleSession(settings, 0);
            battle.Advance(2 * 60.0 / settings.Bpm + settings.GoodWindow + .000001);
            Assert.That(battle.Noise.Pickups, Is.Empty);
            Assert.That(battle.Events.Any(e => e.type == "noise-fallback-deferred"), Is.True);
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Timeout));
        }

        [Test]
        public void SafeRouteMovesAroundDangerOnTheExactImpactNote()
        {
            var danger = new[] { new BossTelegraph(1, "Block", SafeRoutePlanner.Bit(new GridPosition(1, 0)), 0) };

            var route = SafeRoutePlanner.Find(new GridPosition(0, 0), new GridPosition(1, 0), 0, danger,
                maxSteps: 3, returnToTop: false);

            Assert.That(route.Found, Is.True);
            Assert.That(route.Cost, Is.EqualTo(2), "Waiting safely then moving beats a three-move detour.");
            Assert.That(route.FirstAction, Is.EqualTo(PlayerActionKind.Guard));
        }

        [Test]
        public void SafeRouteGuardsThroughThreatThenCollectsAndCountsTwoPickupsOnce()
        {
            ulong neighbors = SafeRoutePlanner.Bit(new GridPosition(3, 4)) |
                SafeRoutePlanner.Bit(new GridPosition(2, 3)) |
                SafeRoutePlanner.Bit(new GridPosition(3, 2)) |
                SafeRoutePlanner.Bit(new GridPosition(4, 3));
            var threat = new[] { new BossTelegraph(1, "Ring", neighbors, 0) };
            var route = SafeRoutePlanner.Find(Player, new GridPosition(3, 4), 0, threat,
                maxSteps: 2, returnToTop: false);
            Assert.That(route.Found, Is.True);
            Assert.That(route.Cost, Is.EqualTo(2));
            Assert.That(route.FirstAction, Is.EqualTo(PlayerActionKind.Guard));

            var colocated = SafeRoutePlanner.FindAll(Player,
                new[] { new GridPosition(3, 4), new GridPosition(3, 4) }, 0,
                Array.Empty<BossTelegraph>(), maxSteps: 1, returnToTop: false);
            Assert.That(colocated.Found, Is.True);
            Assert.That(colocated.Cost, Is.EqualTo(1), "One landing collects both pickups on the same cell.");
        }

        [Test]
        public void LockedCellTelegraphKeepsItsPublishedLocation()
        {
            var pattern = new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell,
                TelegraphNotes = 1, FirstImpact = 4, RepeatNotes = 24 };
            var system = new BossPatternSystem(new[] { pattern });
            system.BeginPhase(1, 0);
            system.Publish(2, new GridPosition(2, 2), 20, false);
            var original = system.Active.Single();

            system.Publish(3, new GridPosition(6, 6), 20, false);

            Assert.That(original.Cells, Is.EqualTo(SafeRoutePlanner.Bit(new GridPosition(2, 2))));
            Assert.That(system.Active.Single().Cells, Is.EqualTo(original.Cells));
            Assert.That(original.Remaining(2), Is.EqualTo(1));
        }

        [Test]
        public void NewPatternDefersIfItWouldRemoveAnAlreadyShownSafeCell()
        {
            var row = new PatternDefinition { Name = "Row", Kind = PatternKind.Row, Coordinate = 6,
                TelegraphNotes = 1, FirstImpact = 8, RepeatNotes = 24 };
            var column = new PatternDefinition { Name = "Column", Kind = PatternKind.Column, Coordinate = 3,
                TelegraphNotes = 1, FirstImpact = 8, RepeatNotes = 24 };
            var system = new BossPatternSystem(new[] { row, column });
            var events = new List<(string Type, BossTelegraph Attack)>();
            system.Changed += (type, attack) => events.Add((type, attack));
            system.BeginPhase(1, 0);

            system.Publish(6, Player, 20, false);

            Assert.That(system.Active.Count, Is.EqualTo(1));
            Assert.That(system.Active[0].Name, Is.EqualTo("Row"));
            Assert.That((system.Active[0].Cells & SafeRoutePlanner.Bit(Player)) == 0, Is.True);
            Assert.That(events.Any(e => e.Type == "defer" && e.Attack.Name == "Column" && e.Attack.ImpactNote == 7), Is.True);
        }

        [Test]
        public void TelegraphCountdownShowsThreeTwoThenOneNotesRemaining()
        {
            var attack = new BossTelegraph(1, "Row", 0, 5);

            Assert.That(attack.Remaining(2), Is.EqualTo(3));
            Assert.That(attack.Remaining(3), Is.EqualTo(2));
            Assert.That(attack.Remaining(4), Is.EqualTo(1));
        }

        [Test]
        public void NoiseSpawnDefersTwiceThenUsesTheBoundedFallback()
        {
            var noise = new NoisePickupSystem(new BoardState(), interval: 8, capacity: 2);
            var statuses = new List<string>();
            noise.CandidateAllowed = (cell, fallback) => fallback;
            noise.SpawnStatus += (status, beat) => statuses.Add(status);
            noise.Reset(1, 87, 3);

            noise.AdvanceToBeat(2, Player);

            CollectionAssert.AreEqual(new[] { "deferred", "deferred", "fallback" }, statuses);
            Assert.That(noise.WaitingForSafeSpawn, Is.False);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1));
            Assert.That(noise.Pickups[0].SpawnBeat, Is.EqualTo(2));
        }

        [Test]
        public void FullNoiseCapacityDoesNotMoveExistingPickups()
        {
            var noise = new NoisePickupSystem(new BoardState(), interval: 1, capacity: 2);
            noise.Reset(1, 87, 5);
            noise.AdvanceToBeat(1, Player);
            var first = noise.Pickups[0];
            var second = noise.Pickups[1];

            noise.AdvanceToBeat(2, new GridPosition(7, 7));

            Assert.That(noise.Pickups.Count, Is.EqualTo(2));
            Assert.That(noise.Pickups[0].Id, Is.EqualTo(first.Id));
            Assert.That(noise.Pickups[0].Cell, Is.EqualTo(first.Cell));
            Assert.That(noise.Pickups[1].Id, Is.EqualTo(second.Id));
            Assert.That(noise.Pickups[1].Cell, Is.EqualTo(second.Cell));
        }

        [Test]
        public void NoiseRandomnessRepeatsForASeedAndVariesAcrossSeeds()
        {
            GridPosition FirstSpawn(int seed)
            {
                var noise = new NoisePickupSystem(new BoardState());
                noise.Reset(1, seed, 3);
                noise.AdvanceToBeat(0, Player);
                return noise.Pickups[0].Cell;
            }

            Assert.That(FirstSpawn(123), Is.EqualTo(FirstSpawn(123)));
            Assert.That(Enumerable.Range(1, 16).Select(FirstSpawn).Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void LiveSimulationLogReplaysToTheSameOutcomeAndNoteHashes()
        {
            string directory = Path.Combine(Path.GetTempPath(), "string-sword-replay-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new BattleSettings
                {
                    Bpm = 87,
                    NoteCount = 48,
                    Patterns = PatternDefinition.Defaults()
                };
                var live = BattleSimulator.Run(settings, 91, StrategyKind.MovementUse).Battle;
                string path = Path.Combine(directory, "live.jsonl");
                BattleLog.Write(path, live);

                var replay = BattleLog.Replay(path);

                Assert.That(replay.Outcome, Is.EqualTo(live.Outcome));
                Assert.That(replay.Records.Select(r => r.stateHash), Is.EqualTo(live.Records.Select(r => r.stateHash)));
                Assert.That(replay.Inputs.Count, Is.EqualTo(live.Inputs.Count));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
