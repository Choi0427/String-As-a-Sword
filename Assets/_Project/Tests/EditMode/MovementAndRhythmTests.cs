using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Rhythm;
using StringAsASword.Player;

namespace StringAsASword.Tests
{
    public sealed class MovementAndRhythmTests
    {
        private ConstantBeatTimeline timeline;
        private RhythmJudge judge;
        [SetUp] public void SetUp() { timeline = new ConstantBeatTimeline(87); judge = new RhythmJudge(timeline); }

        [TestCase(MoveDirection.Up, 3, 4)]
        [TestCase(MoveDirection.Left, 2, 3)]
        [TestCase(MoveDirection.Down, 3, 2)]
        [TestCase(MoveDirection.Right, 4, 3)]
        public void CardinalMoveIsExactlyOneCell(MoveDirection direction, int x, int y)
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3));
            Assert.That(session.RequestMove(direction, 0).Accepted, Is.True);
            Assert.That(session.Position, Is.EqualTo(new GridPosition(x, y)));
        }
        [TestCase(-.070, Judgement.Perfect)] [TestCase(.070, Judgement.Perfect)]
        [TestCase(-.140, Judgement.Good)] [TestCase(.140, Judgement.Good)]
        [TestCase(.14001, Judgement.Miss)] [TestCase(-.14001, Judgement.Miss)]
        public void InclusiveJudgementWindows(double error, Judgement expected) => Assert.That(judge.Evaluate(timeline.TimeOf(8) + error).Grade, Is.EqualTo(expected));
        [Test] public void PositiveOffsetCorrectsLateInput() => Assert.That(new RhythmJudge(timeline, .07, .14, .03).Evaluate(.03).ErrorSeconds, Is.EqualTo(0).Within(1e-9));
        [Test] public void HalfwayInputChoosesEarlierBeat() => Assert.That(timeline.NearestBeat(timeline.SecondsPerBeat * 2.5), Is.EqualTo(2));
        [Test] public void HighTempoWindowsCannotOverlap() => Assert.That(new RhythmJudge(new ConstantBeatTimeline(600)).GoodWindow, Is.EqualTo(.045).Within(1e-9));
        [Test] public void OffBeatDoesNotMoveOrUseAction()
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3));
            Assert.That(session.RequestMove(MoveDirection.Up, .25).Rejection, Is.EqualTo(MoveRejection.OutsideWindow));
            Assert.That(session.Position, Is.EqualTo(new GridPosition(3, 3)));
            Assert.That(session.Gate.LastConsumedBeat, Is.EqualTo(-1));
        }
        [Test] public void InvalidBoundaryAllowsRetryWithinSameBeat()
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(0, 0));
            Assert.That(session.RequestMove(MoveDirection.Left, 0).Rejection, Is.EqualTo(MoveRejection.OutsideBoard));
            Assert.That(session.RequestMove(MoveDirection.Up, .04).Accepted, Is.True);
            Assert.That(session.RequestMove(MoveDirection.Right, .05).Rejection, Is.EqualTo(MoveRejection.BeatAlreadyUsed));
        }
        [Test] public void TopAndRightBoundsAreRejected()
        {
            var board = new BoardState();
            Assert.That(board.TryGetDestination(new GridPosition(7, 7), MoveDirection.Up, out _), Is.False);
            Assert.That(board.TryGetDestination(new GridPosition(7, 7), MoveDirection.Right, out _), Is.False);
        }
        [Test] public void UnplayedNotesMissExactlyOnceAcrossFrameGap()
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3));
            session.RequestMove(MoveDirection.Up, 0);
            session.Advance(timeline.TimeOf(3) + .15);
            session.Advance(timeline.TimeOf(3) + .2);
            Assert.That(session.MissCount, Is.EqualTo(3));
            Assert.That(session.Combo, Is.Zero);
        }
        [Test] public void LateDeliveryCannotReviveExpiredNote()
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3));
            session.Advance(1);
            Assert.That(session.RequestMove(MoveDirection.Up, 0).Rejection, Is.EqualTo(MoveRejection.BeatExpired));
        }
        [Test] public void SchedulerCatchesUpWithoutDuplicates()
        {
            var scheduler = new BeatScheduler(timeline); var beats = new List<int>();
            scheduler.Advance(-.01, beats.Add); scheduler.Advance(timeline.TimeOf(4) + .01, beats.Add);
            scheduler.Advance(timeline.TimeOf(4) + .02, beats.Add);
            CollectionAssert.AreEqual(new[] {0, 1, 2, 3, 4}, beats);
        }
        [Test] public void RestartClearsPositionGateAndStatistics()
        {
            var session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3));
            session.RequestMove(MoveDirection.Up, 0); session.Advance(3); session.Reset();
            Assert.That(session.Position, Is.EqualTo(new GridPosition(3, 3)));
            Assert.That(session.MissCount, Is.Zero); Assert.That(session.BestCombo, Is.Zero);
            Assert.That(session.RequestMove(MoveDirection.Down, 0).Accepted, Is.True);
        }
        [Test] public void Constant87BpmDoesNotAccumulateFrameDrift() => Assert.That(timeline.TimeOf(8700), Is.EqualTo(6000).Within(1e-9));
    }
}
