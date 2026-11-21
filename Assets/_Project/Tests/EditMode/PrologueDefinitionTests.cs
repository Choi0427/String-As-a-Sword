using NUnit.Framework;
using StringAsASword.Presentation;
using UnityEngine;

namespace StringAsASword.Tests
{
    /// <summary>The prologue's sound slots (intro review S1-B, S2-A): what plays when a delivered file is or is not there.</summary>
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
        [Test] public void AComposedCueReplacesThePlaceholderMusicButNotTheSoundEffects()
        {
            foreach (var music in new[] { "king_order", "king_order2", "choir_threat", "choir_wash", "lane_A2", "gliss", "tune", "strain_bar", "stroke_E5", "guitar_hold", "ring5" })
                Assert.That(PrologueDefinition.IsMusic(music), music);
            foreach (var effect in new[] { "clock", "place", "thud", "jerk", "tear", "whoosh_down", "whoosh_ghost", "whoosh_wave", "shatter" })
                Assert.That(PrologueDefinition.IsMusic(effect), Is.False, effect);
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
