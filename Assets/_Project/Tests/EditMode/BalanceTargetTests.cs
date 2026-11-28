using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    // Guards the battle-11 tuning (Docs/Battle11Balance.md) against drift. Loose bands on a small sample: the measured
    // values at 300 seeds x 5 strategies were 어려움 intermediate clear 81%, stun median 235; 쉬움 novice clear 77%.
    public class BalanceTargetTests
    {
        private static BattleSettings Stage() => StringAsASword.Editor.BattleAnalysis.LoadSettings();

        [Test] public void StageCarriesTheBattle11Values()
        {
            var s = Stage();
            Assert.That(s.Version, Is.EqualTo("battle-11"));
            Assert.That(s.Health, Is.EqualTo(new[] { 85, 51, 15, 19 }));
            Assert.That(new[] { s.ResonanceNotes, s.ResonancePerfect, s.ResonanceGood, s.ResonanceCadence, s.GhostDamage },
                Is.EqualTo(new[] { 8, 2, 2, 2, 0 }), "Perfect = Good: accuracy shows in the cadence and survival, not in tempo.");
            Assert.That(s.PlayerMaxHp, Is.EqualTo(6));
        }

        [Test] public void IntermediatePlayerOnHardStaysInTheTargetBand()
        {
            var settings = Stage();
            var runs = Enumerable.Range(0, 40).Select(seed =>
                SkilledSimulator.RunSkilled(settings, 87, seed, StrategyKind.CollectFirst, SkillProfile.Intermediate)).ToList();
            double clear = runs.Count(r => r.Run.Cleared) / (double)runs.Count;
            var stuns = runs.Where(r => r.StunnedNote >= 0).Select(r => r.StunnedNote).OrderBy(n => n).ToList();
            int median = stuns[stuns.Count / 2];
            Assert.That(clear, Is.InRange(.6, .97), "intermediate clear");
            Assert.That(median, Is.InRange(205, 270), "intermediate stun note median");
        }

        [Test] public void NoviceOnEasyClearsAboutHalfTheTime()
        {
            var settings = Stage();
            var runs = Enumerable.Range(0, 40).Select(seed =>
                SkilledSimulator.RunSkilled(settings, 87, seed, StrategyKind.CollectFirst, SkillProfile.Novice, healOnNoise: true)).ToList();
            double clear = runs.Count(r => r.Run.Cleared) / (double)runs.Count;
            Assert.That(clear, Is.InRange(.45, .95), "novice clear on 쉬움");
        }

        [Test] public void ExpertDoesNotFinishFarAheadOfTheIntermediate()
        {
            var settings = Stage();
            int Median(SkillProfile tier)
            {
                var stuns = Enumerable.Range(0, 30).Select(seed => SkilledSimulator.RunSkilled(settings, 87, seed, StrategyKind.CollectFirst, tier))
                    .Where(r => r.StunnedNote >= 0).Select(r => r.StunnedNote).OrderBy(n => n).ToList();
                return stuns[stuns.Count / 2];
            }
            int expert = Median(SkillProfile.Expert);
            Assert.That(expert, Is.GreaterThanOrEqualTo(205), "expert stun note median (battle-10 was 192)");
            Assert.That(Median(SkillProfile.Intermediate) - expert, Is.LessThanOrEqualTo(30), "intermediate behind expert");
        }

        [Test] public void APerfectPlayerAlwaysClears()
        {
            var settings = Stage();
            for (int seed = 0; seed < 5; seed++)
                Assert.That(SkilledSimulator.RunSkilled(settings, 87, seed, StrategyKind.CollectFirst, SkillProfile.Perfect).Run.Cleared,
                    Is.True, "seed " + seed);
        }
    }
}
