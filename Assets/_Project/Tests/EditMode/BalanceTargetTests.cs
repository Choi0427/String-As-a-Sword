using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    // Guards the battle-10 tuning (Docs/Battle10Balance.md) against drift. Loose bands on a small sample: the measured
    // values at 1,000 seeds were intermediate clear 80%, stun median 208; the bands only catch a real change.
    // battle-11 (Docs/Battle11Balance.md) only rewrote 간주1 as the tutorial: intermediate clear 75%, stun median 220.
    public class BalanceTargetTests
    {
        private static BattleSettings Stage() => StringAsASword.Editor.BattleAnalysis.LoadSettings();

        [Test] public void StageCarriesTheBattle10Values()
        {
            var s = Stage();
            Assert.That(s.Version, Is.EqualTo("battle-11"));
            Assert.That(s.Health, Is.EqualTo(new[] { 65, 15, 26, 100 }));
            Assert.That(new[] { s.ResonanceNotes, s.ResonancePerfect, s.ResonanceGood, s.ResonanceCadence, s.GhostDamage },
                Is.EqualTo(new[] { 8, 3, 1, 2, 0 }));
            Assert.That(s.PlayerMaxHp, Is.EqualTo(5), "Player HP waits for the human playtest.");
        }

        [Test] public void IntermediatePlayerStaysInTheTargetBand()
        {
            var settings = Stage();
            var runs = Enumerable.Range(0, 40).Select(seed =>
                SkilledSimulator.RunSkilled(settings, 87, seed, StrategyKind.CollectFirst, SkillProfile.Intermediate)).ToList();
            double clear = runs.Count(r => r.Run.Cleared) / (double)runs.Count;
            var stuns = runs.Where(r => r.StunnedNote >= 0).Select(r => r.StunnedNote).OrderBy(n => n).ToList();
            int median = stuns[stuns.Count / 2];
            Assert.That(clear, Is.InRange(.6, .97), "intermediate clear");
            Assert.That(median, Is.InRange(170, 250), "intermediate stun note median");
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
