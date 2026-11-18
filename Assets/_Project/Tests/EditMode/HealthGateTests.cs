using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public class HealthGateTests
    {
        [Test] public void DamageCarriesOverIntoTheNextSegmentsAndStopsAtZero()
        {
            var hp = new BossHealthGate(6, 6, 6, 6);
            Assert.That(hp.ApplyDamage(8, 0), Is.EqualTo(8));
            Assert.That(hp.ActiveSegment, Is.EqualTo(1));
            Assert.That(hp.CurrentHp, Is.EqualTo(4));
            Assert.That(hp.ApplyDamage(1, 0), Is.Zero, "Damage aimed at an emptied segment is dropped.");
            Assert.That(hp.ApplyDamage(4, 1), Is.EqualTo(4));
            Assert.That(hp.ActiveSegment, Is.EqualTo(2), "An emptied segment hands over at once.");
            Assert.That(hp.Defeated, Is.False);
            Assert.That(hp.ApplyDamage(100, 2), Is.EqualTo(12));
            Assert.That(hp.TotalHp, Is.Zero);
            Assert.That(hp.Defeated, Is.True);
        }
        [Test] public void WrongSegmentAndRestartCannotChangeLockedHealth()
        {
            var hp = new BossHealthGate(6, 6, 6, 6);
            Assert.That(hp.ApplyDamage(4, 1), Is.Zero);
            hp.ApplyDamage(6, 0); hp.Reset();
            Assert.That(hp.TotalHp, Is.EqualTo(24));
            Assert.That(hp.CurrentHp, Is.EqualTo(6));
        }
        [Test] public void AcceptedAttackResolvesExactlyOnceAtItsBeatClose()
        {
            var board = new BoardState(); var hp = new BossHealthGate(6, 6, 6, 6);
            var combat = new CombatResolver(new AttackResolver(board), hp, 1);
            var session = new RhythmSession(board, new RhythmJudge(new ConstantBeatTimeline(87), .07, .14), new GridPosition(3, 7));
            session.ActionEvaluated += combat.Submit;
            session.BeatClosed += beat => combat.ResolveBeat(beat);
            session.RequestAttack(0);
            Assert.That(hp.TotalHp, Is.EqualTo(24));
            session.Advance(.15);
            Assert.That(hp.TotalHp, Is.EqualTo(23));
            combat.ResolveBeat(0); session.Advance(.3);
            Assert.That(hp.TotalHp, Is.EqualTo(23));
        }
        [Test] public void ResetDropsPendingDamage()
        {
            var board = new BoardState(); var hp = new BossHealthGate(6, 6, 6, 6);
            var combat = new CombatResolver(new AttackResolver(board), hp, 1);
            var session = new RhythmSession(board, new RhythmJudge(new ConstantBeatTimeline(87), .07, .14), new GridPosition(3, 7));
            combat.Submit(session.RequestAttack(0)); combat.Reset();
            Assert.That(combat.ResolveBeat(0), Is.Zero);
            Assert.That(hp.TotalHp, Is.EqualTo(24));
        }
    }
}
