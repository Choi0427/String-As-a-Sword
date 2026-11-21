using NUnit.Framework;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public sealed class IntroTimelineTests
    {
        [Test] public void SectionARunsStraightThenOneBarRepeatsUntilTheTear()
        {
            Assert.That(IntroTimeline.StoryBeat(10.5, -1), Is.EqualTo(10.5));
            Assert.That(IntroTimeline.StoryBeat(31.9, -1), Is.EqualTo(31.9).Within(1e-9));
            Assert.That(IntroTimeline.StoryBeat(33, -1), Is.EqualTo(33));
            Assert.That(IntroTimeline.StoryBeat(37, -1), Is.EqualTo(33), "The loop bar comes round again.");
            Assert.That(IntroTimeline.StoryBeat(36, -1), Is.EqualTo(32));
        }
        [Test] public void JTearsOnTheBeatItLandsOnOrTheNextOne()
        {
            Assert.That(IntroTimeline.TearBeat(20), Is.EqualTo(-1), "Before the loop J does nothing.");
            Assert.That(IntroTimeline.TearBeat(31.8), Is.EqualTo(32), "Just before the loop counts for its first beat.");
            Assert.That(IntroTimeline.TearBeat(33.1), Is.EqualTo(33), "A little late still tears on the beat just passed.");
            Assert.That(IntroTimeline.TearBeat(33.4), Is.EqualTo(34), "Later presses wait for the next beat.");
            Assert.That(IntroTimeline.TearBeat(41), Is.EqualTo(41), "On the beat, any bar of the loop.");
        }
        [Test] public void AfterTheTearTheStoryRunsToTheHandoverTwentyBeatsLater()
        {
            Assert.That(IntroTimeline.StoryBeat(45, 45), Is.EqualTo(IntroTimeline.Tear));
            Assert.That(IntroTimeline.StoryBeat(45 + IntroTimeline.AfterTear, 45), Is.EqualTo(IntroTimeline.Handover));
            Assert.That(IntroTimeline.Shot(IntroTimeline.Handover - .1), Is.EqualTo("7"));
            Assert.That(IntroTimeline.Shot(0), Is.EqualTo("1"));
        }
        [Test] public void TheHandoverIsOnTheNoteGridFourBeatsBeforeTheFirstNote()
        {
            var timeline = new FirstNoteAt(2.586, 60.0 / 87);
            double handover = IntroTimeline.HandoverSongTime(timeline);
            Assert.That(handover, Is.EqualTo(2.586 - 4 * 60.0 / 87).Within(1e-9));
            Assert.That(handover, Is.LessThan(0), "The battle goes live just before song time 0, the song's intro carries the rest.");
        }
        private sealed class FirstNoteAt : IBeatTimeline
        {
            private readonly double first;
            public double SecondsPerBeat { get; }
            public FirstNoteAt(double firstNote, double spb) { first = firstNote; SecondsPerBeat = spb; }
            public double TimeOf(int beatId) => first + beatId * SecondsPerBeat;
            public int NearestBeat(double songTime) => (int)System.Math.Round((songTime - first) / SecondsPerBeat);
        }
    }
}
