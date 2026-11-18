using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    public sealed class FinaleTests
    {
        private static BattleSettings Settings(int finish) => new BattleSettings
        {
            Bpm = 87, NoteCount = 160, FinishNote = finish, Health = new[] { 1, 1, 1, 1 }, NoiseTargets = new[] { 1, 1, 1 },
            PlayerMaxHp = 5, Patterns = PatternDefinition.Defaults()
        };
        private static double Time(int note) => note * 60.0 / 87;
        private static void Play(BattleSession battle, int untilNote)
        {
            for (int note = 0; note <= untilNote && battle.Outcome == BattleOutcome.Running; note++)
            {
                battle.Advance(Time(note));
                var decision = BattleStrategies.Decide(StrategyKind.CollectFirst, battle.Observe());
                battle.Request(decision.Kind, decision.Direction, Time(note));
                battle.Advance(Time(note) + .2);
            }
        }

        [Test] public void EmptyingTheBossOnlyStunsTheKingUntilTheFinishingRun()
        {
            var battle = new BattleSession(Settings(150), 7);
            Play(battle, 148);
            Assert.That(battle.KingStunned, "The fixture must empty the boss before the finishing run.");
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Running));
            Assert.That(battle.Patterns.Active, Is.Empty, "A stunned king shows no more attacks.");
            Assert.That(battle.Events.Exists(e => e.type == "king-stunned"));
            battle.Advance(Time(149) + .2);
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won), "Decided as the finishing run (note 150) begins.");
            Assert.That(battle.LastClosedNote, Is.EqualTo(149));
        }
        [Test] public void BossLeftStandingAtTheFinishingRunIsAFailure()
        {
            var battle = new BattleSession(Settings(5), 7);
            battle.Advance(Time(4) + .2);
            Assert.That(battle.KingStunned, Is.False);
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Timeout));
            Assert.That(battle.LastClosedNote, Is.EqualTo(4));
        }
        [Test] public void WithoutAFinaleTheOldRulesHold()
        {
            var battle = new BattleSession(Settings(-1), 7);
            Play(battle, 159);
            Assert.That(battle.KingStunned, Is.False);
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
            Assert.That(battle.LastClosedNote, Is.LessThan(159), "Without a finale the boss falls on the spot.");
        }
        [Test] public void DeveloperSkipOpensALaterPhaseAndNeverGoesBack()
        {
            var battle = new BattleSession(Settings(150), 7);
            battle.SkipToPhase(3);
            Assert.That(battle.Phases.PhaseId, Is.EqualTo(3));
            Assert.That(battle.Health.ActiveSegment, Is.EqualTo(2));
            battle.SkipToPhase(1);
            Assert.That(battle.Phases.PhaseId, Is.EqualTo(3));
            battle.SkipToPhase(9);
            Assert.That(battle.Phases.PhaseId, Is.EqualTo(4));
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Running));
        }
        [Test] public void InvincibleHitsLandButCostNoHp()
        {
            var hurt = new BattleSession(Settings(150), 7);
            hurt.Advance(Time(120));
            Assert.That(hurt.PlayerHp, Is.LessThan(5), "The fixture must hit a knight that stands still.");
            var safe = new BattleSession(Settings(150), 7) { Invincible = true };
            safe.Advance(Time(120));
            Assert.That(safe.Records.Exists(r => r.hit));
            Assert.That(safe.PlayerHp, Is.EqualTo(5));
            Assert.That(safe.Outcome, Is.EqualTo(BattleOutcome.Running));
        }
        [Test] public void WithBossHpOffTheFinishingRunWinsWithTheBossStanding()
        {
            var battle = new BattleSession(Settings(5), 7) { IgnoreBossHp = true };
            battle.Advance(Time(4) + .2);
            Assert.That(battle.KingStunned, Is.False);
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
        }
        [Test] public void FinishNoteMustLieInsideTheChart()
        {
            Assert.Throws<System.ArgumentException>(Settings(160).Validate);
            Assert.Throws<System.ArgumentException>(Settings(0).Validate);
            Assert.DoesNotThrow(Settings(-1).Validate);
        }
    }
}
