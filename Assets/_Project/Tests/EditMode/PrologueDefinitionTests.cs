using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Presentation;
using UnityEngine;

namespace StringAsASword.Tests
{
    /// <summary>The prologue's sounds (intro review S1-B, S2-A, prologue revision R6-A): what plays when a delivered file is or is not there.</summary>
    public sealed class PrologueDefinitionTests
    {
        private PrologueDefinition data;
        private AudioClip placeholder, tick, accent;
        [SetUp] public void SetUp()
        {
            placeholder = AudioClip.Create("clock", 480, 1, 48000, false);
            tick = AudioClip.Create("Clock_Tick", 480, 1, 48000, false);
            accent = AudioClip.Create("Clock_Accent", 480, 1, 48000, false);
            data = ScriptableObject.CreateInstance<PrologueDefinition>();
            data.clips = new[] { placeholder };
        }
        [TearDown] public void TearDown() { foreach (var o in new Object[] { data, placeholder, tick, accent }) Object.DestroyImmediate(o); }

        [Test] public void TheClockIsTheRecordedTakeWhenThereIsOneAndThePlaceholderOtherwise()
        {
            Assert.That(PrologueDefinition.IsClock(placeholder.name));
            Assert.That(data.Tick(false), Is.SameAs(placeholder));
            Assert.That(data.Tick(true), Is.SameAs(placeholder));
            Assert.That(data.TickVolume(true), Is.GreaterThan(data.TickVolume(false)), "The placeholder is made louder on bar lines.");
            data.clockTick = tick;
            Assert.That(data.Tick(true), Is.SameAs(tick), "Only the tick delivered: it plays on bar lines too.");
            data.clockAccent = accent;
            Assert.That(data.Tick(false), Is.SameAs(tick));
            Assert.That(data.Tick(true), Is.SameAs(accent));
            Assert.That(data.TickVolume(true), Is.EqualTo(data.TickVolume(false)), "A recorded accent carries its own accent.");
            data.clockTick = null;
            Assert.That(data.Tick(false), Is.SameAs(accent), "Only the accent delivered: it plays on every beat.");
        }
        [Test] public void TheStringRecordingsComeFromTheirSlotsAndAreMissingUntilDelivered()
        {
            Assert.That(data.Clip(PrologueDefinition.StrainName), Is.Null);
            Assert.That(data.Clip(PrologueDefinition.PluckName), Is.Null);
            var pluck = AudioClip.Create("String_Pluck", 480, 1, 48000, false);
            try
            {
                data.stringPluck = pluck;
                Assert.That(data.Clip(PrologueDefinition.PluckName), Is.SameAs(pluck));
                Assert.That(data.Clip("String_Pluck"), Is.Null, "Slots are found by cue name, not by file name.");
            }
            finally { Object.DestroyImmediate(pluck); }
        }
        [Test] public void OnlyTheFiveSoundsAreCuedAndThePluckHasAStandIn()
        {
            var palette = new[] { PrologueDefinition.ClockName, PrologueDefinition.PlaceName, PrologueDefinition.NoiseName,
                PrologueDefinition.StrainName, PrologueDefinition.PluckName };
            var all = new List<IntroDirector.Cue>();
            all.AddRange(IntroDirector.SectionACues()); all.AddRange(IntroDirector.WaitBarCues()); all.AddRange(IntroDirector.SectionBCues());
            foreach (var cue in all) Assert.That(palette, Does.Contain(cue.Clip), $"beat {cue.Beat}: {cue.Clip}");
            var b = IntroDirector.SectionBCues();
            Assert.That(b.Exists(c => c.Beat == 0 && c.Clip == PrologueDefinition.PluckName && c.Unless == null));
            Assert.That(b.FindAll(c => c.Unless == PrologueDefinition.PluckName), Has.Count.EqualTo(2), "The crackle and a heavy piece, until the pluck is delivered.");
        }
        [Test] public void TheKnightsSecondTryLandsBetweenTheBeatsOfAClockThatNeverStops()
        {
            var a = IntroDirector.SectionACues();
            bool Clock(List<IntroDirector.Cue> cues, float beat) => cues.Exists(c => c.Beat == beat && PrologueDefinition.IsClock(c.Clip));
            bool Place(float from, float to) => a.Exists(c => c.Beat >= from && c.Beat < to && c.Clip == PrologueDefinition.PlaceName);
            for (int beat = 0; beat < IntroTimeline.LoopStart; beat++) Assert.That(Clock(a, beat), $"The clock ticks on beat {beat}: an off beat is only heard against it.");
            Assert.That(a.FindAll(c => c.Clip == PrologueDefinition.PlaceName && c.Beat < 10), Has.Count.EqualTo(6), "One piece set down on each beat 4-9.");
            Assert.That(Place(11, 11.1f), "The first try lands on the beat (the wrong square).");
            Assert.That(Place(12, 12.1f) && Place(14, 14.1f), "The king puts it back on the beat, twice.");
            Assert.That(Place(13, 13.1f), Is.False, "Nothing is set down on the knight's beat.");
            Assert.That(Place(13.45f, 13.6f), "The second try lands half a beat off.");
            Assert.That(a.Exists(c => c.Beat == 13.5f && c.Clip == PrologueDefinition.NoiseName), "The noise is born of it.");
            Assert.That(Place(15, 15.1f), Is.False, "Nothing on beat 15 either: the third try jumps on the off beat.");
            Assert.That(a.Exists(c => c.Beat == 15.5f && c.Clip == PrologueDefinition.NoiseName));
            var b = IntroDirector.SectionBCues();
            for (int beat = 0; beat < 4; beat++) Assert.That(Clock(b, beat), Is.False, $"Silence while the string becomes the sword ({beat}).");
            Assert.That(Clock(b, 4), "The clock comes back with the lanes.");
        }
        [Test] public void APieceMayRunLongerThanItsBeatsButNotShorter()
        {
            double spb = 60.0 / 87;
            Assert.That(PrologueDefinition.CheckLength("Cue_B", 20 * spb, 20, spb), Is.Null, "Exactly its beats.");
            Assert.That(PrologueDefinition.CheckLength("Cue_B", 20 * spb + 3, 20, spb), Is.Null, "A tail is allowed.");
            Assert.That(PrologueDefinition.CheckLength("Cue_B", 20 * spb - .005, 20, spb), Is.Null, "Within 10 ms.");
            Assert.That(PrologueDefinition.CheckLength("Cue_B", 20 * spb - .1, 20, spb), Does.Contain("shorter"));
        }
    }
}
