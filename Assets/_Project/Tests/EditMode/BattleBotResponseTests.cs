using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    public sealed class BattleBotResponseTests
    {
        private const int Seeds = 100;

        private static PatternDefinition Dodge(string name, PatternKind kind, int warning, int hits = 1, int columns = 4) => new PatternDefinition {
            Name = name, Kind = kind, Response = AttackResponse.DodgeOnly, TelegraphNotes = warning, Hits = hits, ColumnCount = columns };
        private static PatternDefinition Guard(string name, int hits, int spacing = 1) => new PatternDefinition {
            Name = name, Kind = PatternKind.FullBoard, Response = AttackResponse.GuardOnly, TelegraphNotes = 2, Hits = hits, HitSpacing = spacing };

        private static PatternDefinition[] EitherSet() => new[] {
            new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2 },
            new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 1 },
            new PatternDefinition { Name = "Top", Kind = PatternKind.TopPressure, TelegraphNotes = 3 },
            new PatternDefinition { Name = "Cross", Kind = PatternKind.Cross, TelegraphNotes = 2 } };
        private static PatternDefinition[] GuardSet() => new[] { Guard("Chord", 2), Guard("Tremolo", 4), Guard("Syncopation", 4, 2) };
        private static PatternDefinition[] DodgeSet() => new[] {
            Dodge("Top Wipe", PatternKind.TopPressure, 2, columns: 8), Dodge("Upper Crush", PatternKind.UpperHalf, 4),
            Dodge("Checker", PatternKind.Checker, 2, 2), Dodge("Ring", PatternKind.Ring, 2, 2) };

        private static BattleSettings Fixture(params PatternDefinition[] patterns) => new BattleSettings
        {
            Bpm = 87, NoteCount = 200, Health = new[] { 30, 6, 12, 40 }, PlayerMaxHp = 5, Patterns = patterns,
            Schedule = new RandomAttackSchedule { Enabled = true, MaxWarning = 4 }
        };

        private static BattleSettings ForSet(string set) => Fixture(
            set == "Either" ? EitherSet() : set == "GuardOnly" ? GuardSet() : set == "DodgeOnly" ? DodgeSet() :
            EitherSet().Concat(GuardSet()).Concat(DodgeSet()).ToArray());

        private static IEnumerable<BattleSession> Runs(BattleSettings settings, StrategyKind strategy, int seeds = Seeds)
        {
            for (int seed = 0; seed < seeds; seed++) yield return BattleSimulator.Run(settings, seed, strategy).Battle;
        }

        private static IEnumerable<int> FirstImpacts(BattleSession battle, string name)
            => battle.Events.Where(e => e.type == "pattern-fire" && e.hit == 0 && e.detail == name).Select(e => e.impactNote);

        [Test]
        public void EveryStrategySurvivesEveryResponseWithoutAnyMissedInput(
            [Values] StrategyKind strategy, [Values("Either", "GuardOnly", "DodgeOnly", "All")] string set)
        {
            var settings = ForSet(set);
            var fired = new HashSet<string>();
            foreach (var battle in Runs(settings, strategy))
            {
                var hit = battle.Records.FirstOrDefault(r => r.hit);
                Assert.That(hit, Is.Null, $"Seed {battle.Seed}, note {hit?.noteIndex}: " + string.Join(", ",
                    battle.Events.Where(e => e.type == "pattern-fire" && e.impactNote == hit?.noteIndex).Select(e => e.detail)));
                Assert.That(battle.Events.Any(e => e.type == "guard-broken"), Is.False, "Seed " + battle.Seed);
                Assert.That(battle.Outcome, Is.Not.EqualTo(BattleOutcome.Dead), "Seed " + battle.Seed);
                foreach (var e in battle.Events) if (e.type == "pattern-fire") fired.Add(e.response + ":" + e.kind);
            }
            var expected = set == "Either" ? new[] { "Either" } : set == "All" ? new[] { "Either", "GuardOnly", "DodgeOnly" } : new[] { set };
            Assert.That(fired.Select(f => f.Split(':')[0]).Distinct(), Is.EquivalentTo(expected), "The fixture must exercise the response.");
            if (set == "All")
                Assert.That(fired.Select(f => f.Split(':')[1]).Distinct(), Is.EquivalentTo(
                    new[] { "Row", "Column", "LockedCell", "TopPressure", "Cross", "FullBoard", "UpperHalf", "Checker", "Ring" }));
        }

        [Test]
        public void GuardOnlyIsGuardedExactlyOnImpactAndAttacksBetweenSyncopationHits([Values] StrategyKind strategy)
        {
            int between = 0;
            foreach (var battle in Runs(Fixture(Guard("Syncopation", 4, 2)), strategy, 40))
            {
                var impacts = new HashSet<int>(battle.Events.Where(e => e.type == "pattern-fire").Select(e => e.impactNote));
                foreach (var record in battle.Records)
                {
                    Assert.That(record.category == "Guard", Is.EqualTo(impacts.Contains(record.noteIndex)),
                        "A guard belongs on an impact note and nowhere else; seed " + battle.Seed + ", note " + record.noteIndex);
                    // A note between two hits of the same sequence, spent on row 8.
                    if (impacts.Contains(record.noteIndex - 1) && impacts.Contains(record.noteIndex + 1) && record.y == 7 && record.beforeY == 7)
                    {
                        Assert.That(record.category == "EffectiveAttack" || record.category == "Move" || record.reason == "HpFloor", Is.True, "Seed " + battle.Seed + ", note " + record.noteIndex);
                        if (record.category == "EffectiveAttack") between++;
                    }
                }
            }
            Assert.That(between, Is.GreaterThan(0), "The fixture must reach a gap between hits on row 8.");
        }

        [Test]
        public void AttackHoldStaysOnRowEightUntilTheLatestDeparture()
        {
            int attacked = 0;
            foreach (var battle in Runs(Fixture(Dodge("Top Wipe", PatternKind.TopPressure, 3, columns: 8)), StrategyKind.AttackHold, 40))
                foreach (int impact in FirstImpacts(battle, "Top Wipe"))
                {
                    var before = battle.Records[impact - 1];
                    // Arriving or leaving by move means a pickup trip, which is not the decision under test.
                    if (before.y != 7 || before.category == "Move") continue;
                    Assert.That(before.category, Is.EqualTo("EffectiveAttack").Or.EqualTo("Other"), "Seed " + battle.Seed + ", note " + impact);
                    Assert.That(battle.Records[impact].y, Is.Not.EqualTo(7), "Seed " + battle.Seed + ", note " + impact);
                    if (before.category == "EffectiveAttack") attacked++;
                }
            Assert.That(attacked, Is.GreaterThan(0));
        }

        [Test]
        public void MovementUseLeavesBeforeTheLatestDeparture()
        {
            int early = 0, late = 0;
            foreach (var battle in Runs(Fixture(Dodge("Top Wipe", PatternKind.TopPressure, 3, columns: 8)), StrategyKind.MovementUse, 40))
                foreach (int impact in FirstImpacts(battle, "Top Wipe"))
                {
                    if (battle.Records[impact - 1].y == 7 && battle.Records[impact - 1].category != "Move") late++;
                    else early++;
                }
            Assert.That(early, Is.GreaterThan(0));
            Assert.That(late, Is.Zero, "A three-note warning leaves row 8 before the last note.");
        }

        [Test]
        public void DodgeOnlyWaitingIsNeverCountedAsAGuard([Values] StrategyKind strategy)
        {
            foreach (var battle in Runs(ForSet("DodgeOnly"), strategy, 40))
                Assert.That(battle.Records.Count(r => r.category == "Guard"), Is.Zero, "Seed " + battle.Seed);
        }

        [Test]
        public void OldDefaultPatternsStillGuardAsTheyDid()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 96, Patterns = PatternDefinition.Defaults() };
            var battle = BattleSimulator.Run(settings, 3, StrategyKind.AttackHold).Battle;
            Assert.That(battle.Records.Count(r => r.guardSuccess), Is.GreaterThan(0));
            Assert.That(battle.Records.Any(r => r.hit), Is.False);
        }

        [Test]
        public void MissedInputsMayCostHitsButNeverBreakTheSession([Values] StrategyKind strategy)
        {
            var settings = ForSet("All");
            for (int seed = 0; seed < 40; seed++)
            {
                var run = BattleSimulator.RunOne(settings, seed, strategy, null, .05);
                Assert.That(run.InvariantFailureCount, Is.Zero, "Seed " + seed + ": " + string.Join(",", run.InvariantFailures.Keys));
            }
        }
    }
}
