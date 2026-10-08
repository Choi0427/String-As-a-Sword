using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public sealed class PlayerActionTests
    {
        [Test] public void All64CellsCanSwingButOnlyTopEightRequestDamage()
        {
            var board = new BoardState(); var resolver = new AttackResolver(board);
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                var session = new RhythmSession(board, new RhythmJudge(new ConstantBeatTimeline(87)), new GridPosition(x, y));
                var attack = session.RequestAttack(0);
                Assert.That(attack.Accepted, Is.True);
                Assert.That(resolver.DamageRequested(attack, 1), Is.EqualTo(y == 7 ? 1 : 0), $"({x},{y})");
                Assert.That(session.RequestMove(MoveDirection.Up, .01).Rejection, Is.EqualTo(ActionRejection.BeatAlreadyUsed));
            }
        }
        [Test] public void AttackAndMoveShareOneGateInEitherOrder()
        {
            var session = new RhythmSession(new BoardState(), new RhythmJudge(new ConstantBeatTimeline(87)), new GridPosition(3, 6));
            session.RequestMove(MoveDirection.Up, 0);
            Assert.That(session.RequestAttack(.01).Accepted, Is.False);
            Assert.That(session.AcceptedAttacks, Is.Zero);
        }
        [Test] public void OffBeatAttackHasNoDamageOrAction()
        {
            var board = new BoardState(); var session = new RhythmSession(board, new RhythmJudge(new ConstantBeatTimeline(87)), new GridPosition(3, 7));
            var attack = session.RequestAttack(.25);
            Assert.That(attack.Accepted, Is.False);
            Assert.That(new AttackResolver(board).DamageRequested(attack, 1), Is.Zero);
        }
    }
}
