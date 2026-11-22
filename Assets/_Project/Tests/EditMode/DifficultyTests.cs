using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;

namespace StringAsASword.Tests
{
    /// <summary>Difficulty 쉬움: a collected noise heals 1 HP, never above the max. 어려움 is the plain rules.</summary>
    public sealed class DifficultyTests
    {
        private const int MaxHp = 100;

        // The player stands still and picks up every noise the moment it spawns, so hits and pickups both happen.
        private static BattleSession Run(bool heal)
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 96, Patterns = PatternDefinition.Defaults(), PlayerMaxHp = MaxHp };
            var battle = new BattleSession(settings, 7) { HealOnNoise = heal };
            battle.Noise.CandidateAllowed = (cell, fallback) => true;
            for (int note = 0; note < settings.NoteCount; note++)
            {
                battle.Advance(note * 60.0 / settings.Bpm);
                foreach (var pickup in battle.Noise.Pickups.ToArray())
                    battle.Noise.TryCollect(pickup.Cell, battle.Noise.SessionId, battle.Noise.PhaseId);
                Assert.That(battle.PlayerHp, Is.LessThanOrEqualTo(MaxHp));
            }
            battle.Advance(settings.NoteCount * 60.0 / settings.Bpm + 1);
            return battle;
        }

        [Test] public void EasyHealsOneHpPerNoiseUpToTheMax()
        {
            var hard = Run(false); var easy = Run(true);
            Assert.That(hard.Events.Count(e => e.type == "player-heal"), Is.Zero, "어려움 never heals.");
            Assert.That(hard.Noise.Collected, Is.EqualTo(easy.Noise.Collected), "Healing does not change the noise.");
            int heals = easy.Events.Count(e => e.type == "player-heal");
            Assert.That(MaxHp - hard.PlayerHp, Is.GreaterThan(0), "The fixture must take hits.");
            Assert.That(heals, Is.GreaterThan(0));
            Assert.That(easy.PlayerHp, Is.EqualTo(hard.PlayerHp + heals), "Each heal is exactly 1 HP.");
        }

        [Test] public void HealStopsAtTheMax()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 96, Patterns = new PatternDefinition[0], PlayerMaxHp = 5 };
            var battle = new BattleSession(settings, 7) { HealOnNoise = true };
            battle.Noise.CandidateAllowed = (cell, fallback) => true;
            for (int note = 0; note < 40; note++)
            {
                battle.Advance(note * 60.0 / settings.Bpm);
                foreach (var pickup in battle.Noise.Pickups.ToArray())
                    battle.Noise.TryCollect(pickup.Cell, battle.Noise.SessionId, battle.Noise.PhaseId);
            }
            Assert.That(battle.Noise.Collected, Is.GreaterThan(0));
            Assert.That(battle.PlayerHp, Is.EqualTo(5));
            Assert.That(battle.Events.Count(e => e.type == "player-heal"), Is.Zero, "No heal at full HP.");
        }
    }
}
