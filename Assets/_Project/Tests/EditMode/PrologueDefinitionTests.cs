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
            Assert.That(data.Clip(PrologueDefinition.SnapName), Is.Null);
            var snap = AudioClip.Create("String_Snap", 480, 1, 48000, false);
            try
            {
                data.stringSnap = snap;
                Assert.That(data.Clip(PrologueDefinition.SnapName), Is.SameAs(snap));
                Assert.That(data.Clip("String_Snap"), Is.Null, "Slots are found by cue name, not by file name.");
            }
            finally { Object.DestroyImmediate(snap); }
        }
        [Test] public void OnlyTheFiveSoundsAreCuedAndTheSnapHasAStandIn()
        {
            var palette = new[] { PrologueDefinition.ClockName, PrologueDefinition.PlaceName, PrologueDefinition.NoiseName,
                PrologueDefinition.StrainName, PrologueDefinition.SnapName };
            var all = new List<IntroDirector.Cue>();
            all.AddRange(IntroDirector.SectionACues()); all.AddRange(IntroDirector.WaitBarCues()); all.AddRange(IntroDirector.SectionBCues());
            foreach (var cue in all) Assert.That(palette, Does.Contain(cue.Clip), $"beat {cue.Beat}: {cue.Clip}");
            var b = IntroDirector.SectionBCues();
            Assert.That(b.Exists(c => c.Beat == 0 && c.Clip == PrologueDefinition.SnapName && c.Unless == null));
            Assert.That(b.FindAll(c => c.Unless == PrologueDefinition.SnapName), Has.Count.EqualTo(2), "The crackle and a heavy piece, until the snap is delivered.");
        }
        [Test] public void TheClockStopsWhileTheKnightIsInTheAirAndAfterTheTear()
        {
            var a = IntroDirector.SectionACues();
            bool Clock(List<IntroDirector.Cue> cues, float beat) => cues.Exists(c => c.Beat == beat && PrologueDefinition.IsClock(c.Clip));
            Assert.That(Clock(a, 13), "The knight's beat still ticks.");
            Assert.That(a.Exists(c => c.Beat > 12.9f && c.Beat < 13.4f && c.Clip == PrologueDefinition.PlaceName), Is.False, "Nothing is set down on it.");
            Assert.That(Clock(a, 14) || Clock(a, 15), Is.False, "Silence in the air.");
            Assert.That(Clock(a, 16), "The clock comes back with the strings.");
            Assert.That(a.FindAll(c => c.Clip == PrologueDefinition.PlaceName && c.Beat < 12), Has.Count.EqualTo(8), "One piece set down on each beat 4-11.");
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
