using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    public sealed class SkillSimulationTests
    {
        private static BattleSettings Settings() => StringAsASword.Editor.BattleAnalysis.LoadSettings();

        [Test] public void PerfectProfileReproducesRunOne()
        {
            var settings = Settings();
            foreach (int seed in new[] { 0, 87 })
                foreach (var strategy in BattleStrategies.Legacy)
                {
                    var skilled = SkilledSimulator.RunSkilled(settings, seed, 12345, strategy, SkillProfile.Perfect, keepBattle: true);
                    var baseline = BattleSimulator.RunOne(settings, seed, strategy);
                    string label = $"{strategy} seed {seed}";
                    Assert.That(skilled.Run.Outcome, Is.EqualTo(baseline.Outcome), label);
                    Assert.That(skilled.Run.Battle.Records.Count, Is.EqualTo(baseline.Battle.Records.Count), label);
                    Assert.That(skilled.Run.Battle.Records.Select(r => r.stateHash),
                        Is.EqualTo(baseline.Battle.Records.Select(r => r.stateHash)), label);
                    Assert.That(skilled.Run.Battle.Inputs.Count, Is.EqualTo(baseline.Battle.Inputs.Count), label);
                }
        }

        [Test] public void TimingSigmaSpreadsPerfectsAndGoods()
        {
            var settings = Settings();
            var profile = new SkillProfile { Name = "TimingOnly", TimingSigma = .045 };
            int perfects = 0, goods = 0, misses = 0;
            for (int skillSeed = 1; skillSeed <= 5; skillSeed++)
            {
                var run = SkilledSimulator.RunSkilled(settings, 87, skillSeed, StrategyKind.CollectFirst, profile);
                perfects += run.Perfects; goods += run.Goods; misses += run.Misses;
            }
            int total = perfects + goods + misses;
            Assert.That(total, Is.GreaterThan(0));
            double share = perfects / (double)total;
            // Theory is about .88, but strategy idle notes (recorded as Miss) and inputs clamped early pull it down.
            Assert.That(share, Is.InRange(.70, .97), $"perfect {perfects} good {goods} miss {misses}");

            var perfect = SkilledSimulator.RunSkilled(settings, 87, 1, StrategyKind.CollectFirst, SkillProfile.Perfect);
            Assert.That(perfect.Goods, Is.Zero, "Perfect timing never lands a Good.");
        }

        [Test] public void SameSeedsAreDeterministicAndSkillSeedMatters()
        {
            var settings = Settings();
            var a = SkilledSimulator.RunSkilled(settings, 87, 7, StrategyKind.CollectFirst, SkillProfile.Novice, keepBattle: true);
            var b = SkilledSimulator.RunSkilled(settings, 87, 7, StrategyKind.CollectFirst, SkillProfile.Novice, keepBattle: true);
            Assert.That(b.Run.Battle.Records.Select(r => r.stateHash), Is.EqualTo(a.Run.Battle.Records.Select(r => r.stateHash)));
            Assert.That(b.UnreadTelegraphs, Is.EqualTo(a.UnreadTelegraphs));

            bool anyDiffers = false;
            foreach (int alt in new[] { 8, 9, 10 })
            {
                var c = SkilledSimulator.RunSkilled(settings, 87, alt, StrategyKind.CollectFirst, SkillProfile.Novice, keepBattle: true);
                if (!c.Run.Battle.Records.Select(r => r.stateHash).SequenceEqual(a.Run.Battle.Records.Select(r => r.stateHash)))
                    anyDiffers = true;
            }
            Assert.That(anyDiffers, Is.True, "A different skill seed should change at least one run.");
        }

        [Test] public void NoviceDropsInputsAndMissesTelegraphs()
        {
            var settings = Settings();
            int dropped = 0, unread = 0;
            for (int skillSeed = 1; skillSeed <= 3; skillSeed++)
            {
                var run = SkilledSimulator.RunSkilled(settings, 87, skillSeed, StrategyKind.CollectFirst, SkillProfile.Novice);
                dropped += run.Dropped; unread += run.UnreadTelegraphs;
            }
            Assert.That(dropped, Is.GreaterThan(0));
            Assert.That(unread, Is.GreaterThan(0));
        }

        [Test] public void TankAndPatientRunWithoutInvariantFailures()
        {
            var settings = Settings();
            foreach (var strategy in new[] { StrategyKind.Tank, StrategyKind.Patient })
                foreach (var skill in new[] { SkillProfile.Perfect, SkillProfile.Novice })
                    for (int seed = 0; seed < 3; seed++)
                    {
                        var run = SkilledSimulator.RunSkilled(settings, seed, seed + 100, strategy, skill);
                        string keys = string.Join(", ", run.Run.InvariantFailures.Select(p => p.Key + "=" + p.Value));
                        Assert.That(run.Run.InvariantFailureCount, Is.Zero, $"{strategy} {skill.Name} seed {seed}: {keys}");
                        if (skill == SkillProfile.Perfect)
                            Assert.That(run.Ignitions, Is.GreaterThan(0), $"{strategy} Perfect seed {seed} never ignited.");
                    }
        }

        [Test] public void PerfectCollectFirstMeasurementSmoke()
        {
            var run = SkilledSimulator.RunSkilled(Settings(), 87, 1, StrategyKind.CollectFirst, SkillProfile.Perfect);
            Assert.Pass($"Perfect x CollectFirst seed 87: outcome {run.Run.Outcome}, ignitions {run.Ignitions}, " +
                $"uptime {run.Uptime:P0}, damage {run.DamageDealt}");
        }
    }
}
