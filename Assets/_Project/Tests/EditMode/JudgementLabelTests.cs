using NUnit.Framework;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;

namespace StringAsASword.Tests
{
    public sealed class JudgementLabelTests
    {
        [Test]
        public void EveryGradeHasItsWord()
        {
            Assert.That(JudgementLabel.TextFor(Judgement.Perfect), Is.EqualTo(JudgementLabel.Perfect));
            Assert.That(JudgementLabel.TextFor(Judgement.Good), Is.EqualTo(JudgementLabel.Good));
            Assert.That(JudgementLabel.TextFor(Judgement.Miss), Is.EqualTo(JudgementLabel.Miss));
            Assert.That(JudgementLabel.TextFor(Judgement.None), Is.Null, "no judgement, nothing to write");
        }
    }
}
