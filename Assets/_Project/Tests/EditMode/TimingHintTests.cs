using NUnit.Framework;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public sealed class TimingHintTests
    {
        [Test]
        public void OnlyAGoodSaysWhichWayItMissed()
        {
            // The judge's error is input minus note time after the input sync: negative is early.
            Assert.That(TimingHint.TextFor(Judgement.Good, -.1), Is.EqualTo(TimingHint.Fast));
            Assert.That(TimingHint.TextFor(Judgement.Good, .1), Is.EqualTo(TimingHint.Slow));
            Assert.That(TimingHint.TextFor(Judgement.Perfect, -.05), Is.Null, "a Perfect needs no correction");
            Assert.That(TimingHint.TextFor(Judgement.Perfect, .05), Is.Null, "a Perfect needs no correction");
            Assert.That(TimingHint.TextFor(Judgement.Miss, -.2), Is.Null, "a Miss already reads as one");
            Assert.That(TimingHint.TextFor(Judgement.Miss, .2), Is.Null, "a Miss already reads as one");
            Assert.That(TimingHint.TextFor(Judgement.None, 0), Is.Null);
        }
    }
}
