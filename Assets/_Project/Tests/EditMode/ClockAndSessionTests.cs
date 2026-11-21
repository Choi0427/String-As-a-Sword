using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Player;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public sealed class ClockAndSessionTests
    {
        [Test] public void ClockUsesDspOriginAndFreezesOnStop()
        {
            double dsp=20;
            var clock=new SongClock(()=>dsp); clock.Start(3);
            Assert.That(clock.SongTime,Is.EqualTo(-3));
            dsp=25; Assert.That(clock.SongTime,Is.EqualTo(2));
            clock.Stop(); dsp=50; Assert.That(clock.SongTime,Is.EqualTo(2));
            clock.Start(3); Assert.That(clock.Origin,Is.EqualTo(53));
        }
        [Test] public void ClockCanStartFromAnAbsoluteOrigin()
        {
            double dsp=100;
            var clock=new SongClock(()=>dsp); clock.StartAt(112);
            Assert.That(clock.Running); Assert.That(clock.SongTime,Is.EqualTo(-12));
            dsp=113; Assert.That(clock.SongTime,Is.EqualTo(1));
        }
        [Test] public void EarlyNextBeatDoesNotTurnPreviousHitIntoMiss()
        {
            var timeline=new ConstantBeatTimeline(87);
            var session=new RhythmSession(new BoardState(),new RhythmJudge(timeline),new GridPosition(3,3));
            session.RequestMove(MoveDirection.Up,0);
            session.RequestMove(MoveDirection.Down,timeline.TimeOf(1)-.1);
            session.Advance(timeline.TimeOf(1)+.2);
            Assert.That(session.MissCount,Is.Zero);
            Assert.That(session.Combo,Is.EqualTo(2));
        }
        [Test] public void UnplayedPreviousBeatClosesBeforeNextSuccessfulInput()
        {
            var timeline=new ConstantBeatTimeline(87);
            var session=new RhythmSession(new BoardState(),new RhythmJudge(timeline),new GridPosition(3,3));
            session.RequestMove(MoveDirection.Up,timeline.TimeOf(1));
            Assert.That(session.MissCount,Is.EqualTo(1)); Assert.That(session.Combo,Is.EqualTo(1));
        }
        [Test] public void PositiveInputOffsetExtendsNoteClosingTime()
        {
            var timeline=new ConstantBeatTimeline(87);
            var session=new RhythmSession(new BoardState(),new RhythmJudge(timeline,.07,.14,.1),new GridPosition(3,3));
            session.Advance(.2); Assert.That(session.MissCount,Is.Zero);
            Assert.That(session.RequestMove(MoveDirection.Up,.23).Accepted,Is.True);
        }
    }
}
