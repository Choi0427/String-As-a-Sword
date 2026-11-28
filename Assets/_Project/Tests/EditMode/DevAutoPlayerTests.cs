using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Core;

namespace StringAsASword.Tests
{
    // The auto play is a developer's way to watch every feature without playing: it must play the real stage like a
    // decent player (not a perfect one), so the Goods, misses, hits, blocks and holds all come up.
    public sealed class DevAutoPlayerTests
    {
        private const double Frame = 1 / 60.0;

        // Frame by frame, as PrototypeGame does: the auto player first, then the battle catches up to the frame.
        private static (BattleSession battle, DevAutoPlayer bot, int holdsJudged, int holdPerfects) Play(int seed)
        {
            var settings = StringAsASword.Editor.BattleAnalysis.LoadSettings();
            var timeline = settings.CreateTimeline();
            var battle = new BattleSession(settings, 87) { Invincible = true };
            var bot = new DevAutoPlayer(seed);
            int holds = 0, holdPerfects = 0;
            battle.Session.HoldResolved += r => { holds++; if (r.Grade == Rhythm.Judgement.Perfect) holdPerfects++; };
            double end = timeline.TimeOf(settings.NoteCount - 1) + 2;
            for (double t = -1; t < end && battle.Outcome == BattleOutcome.Running; t += Frame)
            {
                bot.Step(battle, timeline, t);
                battle.Advance(t);
            }
            return (battle, bot, holds, holdPerfects);
        }

        [Test] public void PlaysTheStageWellButNotPerfectly()
        {
            var (battle, bot, holds, holdPerfects) = Play(1);
            var records = battle.Records;
            int perfect = records.Count(r => r.judgement == "Perfect"), good = records.Count(r => r.judgement == "Good");
            int miss = records.Count(r => r.judgement == "Miss");
            Assert.That(records.Count, Is.GreaterThan(100), "it plays through the song");
            Assert.That(perfect, Is.GreaterThan(good), "mostly Perfect");
            Assert.That(good, Is.GreaterThan(records.Count / 20), "but Goods (FAST / SLOW) come up often");
            Assert.That(miss, Is.GreaterThan(0), "and the odd Miss");
            Assert.That(miss, Is.LessThan(records.Count / 5), "not a bad player");
            Assert.That(bot.Dropped, Is.GreaterThan(0));
            Assert.That(records.Any(r => r.guardSuccess), "BLOCK shows");
            Assert.That(records.Any(r => r.hit), "HIT shows (an overlooked attack)");
            Assert.That(records.Any(r => r.category == "EffectiveAttack"), "it damages the boss");
            Assert.That(battle.Events.Any(e => e.type == "resonance-ignite"), "resonance ignites");
            if (battle.Settings.HasHolds)
            {
                Assert.That(bot.Releases, Is.GreaterThan(0), "it lets go of its holds");
                Assert.That(holdPerfects, Is.GreaterThan(0), "mostly on time");
                // With only a few holds every one can come out clean; with many, one in eight is let go early.
                if (holds >= 20) Assert.That(holds, Is.GreaterThan(holdPerfects), "but not every hold is clean");
            }
        }

        [Test] public void TheSameSeedPlaysTheSameRun()
        {
            var first = Play(5).battle.Records.Select(r => r.judgement + r.category).ToArray();
            var second = Play(5).battle.Records.Select(r => r.judgement + r.category).ToArray();
            Assert.That(second, Is.EqualTo(first));
        }
    }
}
