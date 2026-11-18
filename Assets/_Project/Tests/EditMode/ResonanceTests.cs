using System;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;

namespace StringAsASword.Tests
{
    public sealed class ResonanceTests
    {
        private static BattleSettings Settings(int startY) => new BattleSettings
        {
            Bpm = 87, NoteCount = 64, StartX = 3, StartY = startY, ResonanceNotes = 8, MaxNoise = 1,
            Patterns = Array.Empty<PatternDefinition>()
        };
        private static double NoteTime(BattleSettings settings, int note) => note * 60.0 / settings.Bpm;
        private static double CloseTime(BattleSettings settings, int note) => NoteTime(settings, note) + settings.GoodWindow + 0.000001;

        [Test] public void ChargeIgnitesOnTheFirstStrikeAndLastsItsLengthInNotes()
        {
            var resonance = new ResonanceState(8);
            int ghosts = 0, ignitions = 0, cadences = 0;
            resonance.GhostStrike += _ => ghosts++; resonance.Ignited += _ => ignitions++; resonance.Cadence += _ => cadences++;
            Assert.That(resonance.Strike(3, true, 1), Is.Zero, "No charge: the blade passes through.");
            Assert.That(ghosts, Is.EqualTo(1));
            Assert.That(resonance.TryCharge(4), Is.True);
            Assert.That(resonance.TryCharge(4), Is.False, "One charge at most.");
            Assert.That(resonance.Remaining(9), Is.Zero, "A charge waits; nothing counts down before ignition.");
            Assert.That(resonance.Strike(10, true, 1), Is.EqualTo(2), "The igniting attack already hurts.");
            Assert.That(resonance.Charged, Is.False);
            Assert.That(resonance.EndNote, Is.EqualTo(17));
            Assert.That(resonance.Remaining(11), Is.EqualTo(7));
            Assert.That(resonance.TryCharge(12), Is.False, "No charge while resonating.");
            Assert.That(resonance.Strike(12, false, 1), Is.EqualTo(1));
            Assert.That(resonance.Strike(17, false, 1), Is.EqualTo(1), "A Good last note has no cadence.");
            Assert.That(cadences, Is.Zero);
            Assert.That(resonance.Strike(18, true, 1), Is.Zero);
            Assert.That(ignitions, Is.EqualTo(1)); Assert.That(ghosts, Is.EqualTo(2));
        }
        [Test] public void PerfectOnTheLastNoteAddsTheCadence()
        {
            var resonance = new ResonanceState(8);
            resonance.TryCharge(0);
            resonance.Strike(0, false, 1);
            Assert.That(resonance.Strike(7, true, 1), Is.EqualTo(4));
        }
        [Test] public void HaltedOrSwitchedOffResonanceNeverCharges()
        {
            var off = new ResonanceState(0);
            Assert.That(off.TryCharge(0), Is.False);
            Assert.That(off.Strike(0, false, 1), Is.EqualTo(1), "Rule off: the old flat damage.");
            var halted = new ResonanceState(8);
            halted.TryCharge(0); halted.Halt();
            Assert.That(halted.Charged, Is.False);
            Assert.That(halted.TryCharge(1), Is.False);
            Assert.That(halted.Strike(1, true, 1), Is.Zero);
        }
        [Test] public void WithoutNoiseATopRowAttackIsAGhostStrike()
        {
            var settings = Settings(7);
            var battle = new BattleSession(settings, 87);
            battle.Noise.BeginPhase(1, 0, 0);
            battle.Request(PlayerActionKind.Attack, MoveDirection.Up, NoteTime(settings, 0));
            battle.Advance(CloseTime(settings, 0));
            var record = battle.Records.Single();
            Assert.That(record.attackOpportunity, Is.True);
            Assert.That(record.damage, Is.Zero);
            Assert.That(record.reason, Is.EqualTo("Ghost"));
            Assert.That(battle.Health.TotalHp, Is.EqualTo(24));
            Assert.That(battle.Events.Exists(e => e.type == "ghost-strike" && e.noteIndex == 0));
        }
        [Test] public void NoiseChargesTheStringAndEightNotesOfAttacksHurtByAccuracy()
        {
            var settings = Settings(6);
            var battle = new BattleSession(settings, 87);
            var allowed = new GridPosition(3, 7);
            battle.Noise.CandidateAllowed = (cell, fallback) => cell.Equals(allowed);
            battle.Advance(0);
            Assert.That(battle.Noise.Pickups.Single().Cell, Is.EqualTo(allowed));

            battle.Request(PlayerActionKind.Move, MoveDirection.Up, NoteTime(settings, 0));
            Assert.That(battle.Resonance.Charged, Is.True);
            Assert.That(battle.Noise.Pickups, Is.Empty);
            allowed = new GridPosition(0, 0);
            battle.Advance(CloseTime(settings, 0));

            // Note 1 ignites (Perfect 2), notes 2-7 are Good (1 each), note 8 is the Perfect cadence (2 + 2), note 9 is a ghost.
            int[] expected = { 0, 2, 1, 1, 1, 1, 1, 1, 4, 0 };
            for (int note = 1; note <= 9; note++)
            {
                bool perfect = note == 1 || note >= 8;
                battle.Request(PlayerActionKind.Attack, MoveDirection.Up, NoteTime(settings, note) + (perfect ? 0 : .1));
                battle.Advance(CloseTime(settings, note));
                Assert.That(battle.Records[note].damage, Is.EqualTo(expected[note]), "note " + note);
                if (note < 9) Assert.That(battle.Noise.Pickups, Is.Empty, "No noise while charged or resonating; note " + note);
            }
            Assert.That(battle.Records[1].resonance, Is.EqualTo(7));
            Assert.That(battle.Records[8].resonance, Is.Zero);
            Assert.That(battle.Records[9].reason, Is.EqualTo("Ghost"));
            Assert.That(battle.Health.TotalHp, Is.EqualTo(12), "Damage carries over from one HP segment into the next.");
            Assert.That(battle.Phases.PhaseId, Is.EqualTo(3));
            Assert.That(battle.Events.Count(e => e.type == "resonance-ignite"), Is.EqualTo(1));
            Assert.That(battle.Events.Count(e => e.type == "resonance-cadence"), Is.EqualTo(1));
            Assert.That(battle.Noise.Pickups.Single().Cell, Is.EqualTo(allowed), "The held spawn appears right after the resonance ends.");
        }
        // The stage as shipped, played by the bot. Balance is not asserted here (see Docs/ChartBalance.md); this only
        // proves the loop runs: every run fetches noise, ignites and hurts the boss, with no broken invariant.
        [Test] public void TheStageRunsTheResonanceLoopForTheBot()
        {
            var settings = StringAsASword.Editor.BattleAnalysis.LoadSettings();
            Assert.That(settings.ResonanceNotes, Is.EqualTo(8));
            Assert.That(settings.MaxNoise, Is.EqualTo(1));
            int total = 0; foreach (int hp in settings.Health) total += hp;
            int cleared = 0, dead = 0; var left = new System.Collections.Generic.List<int>(); double uptime = 0, ignitions = 0;
            const int runs = 40;
            for (int seed = 0; seed < runs; seed++)
            {
                var run = StringAsASword.Simulation.BattleSimulator.RunOne(settings, seed, StringAsASword.Simulation.StrategyKind.CollectFirst);
                var battle = run.Battle;
                Assert.That(run.InvariantFailureCount, Is.Zero, "seed " + seed + ": " + string.Join(",", run.InvariantFailures.Keys));
                int ignited = battle.Events.Count(e => e.type == "resonance-ignite");
                Assert.That(ignited, Is.GreaterThan(0), "seed " + seed);
                Assert.That(battle.Health.TotalHp, Is.LessThan(total), "seed " + seed);
                Assert.That(battle.Noise.Pickups.Count, Is.LessThanOrEqualTo(1));
                if (run.Cleared) cleared++; if (run.Outcome == "Dead") dead++;
                left.Add(battle.Health.TotalHp); ignitions += ignited;
                uptime += battle.Records.Count(r => r.resonance > 0 || r.damage > 0) / (double)battle.Records.Count;
            }
            left.Sort();
            Assert.Pass($"runs={runs} cleared={cleared} dead={dead} bossHpLeftMedian={left[runs / 2]}/{total} max={left[runs - 1]} ignitionsAvg={ignitions / runs:F1} uptime={uptime / runs:P0}");
        }
        [Test] public void NoiseSpawnsOnTheLowerFiveRows()
        {
            var settings = Settings(7); settings.NoteCount = 200;
            for (int seed = 0; seed < 20; seed++)
            {
                var battle = new BattleSession(settings, seed);
                battle.Advance(0);
                Assert.That(battle.Noise.Pickups.Single().Cell.Y, Is.LessThanOrEqualTo(BattleSession.ResonanceNoiseTopRow), "seed " + seed);
            }
        }
    }
}
