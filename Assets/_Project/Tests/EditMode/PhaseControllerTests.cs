using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Stage;

namespace StringAsASword.Tests
{
    public class PhaseControllerTests
    {
        private BossHealthGate health;
        private NoisePickupSystem noise;
        private PhaseController phases;
        [SetUp] public void Setup()
        {
            health = new BossHealthGate(6, 6, 6, 6);
            noise = new NoisePickupSystem(new BoardState());
            phases = new PhaseController(health, noise, new[] { 3, 4, 5 });
            phases.Reset(1, 87);
        }
        private int CollectGoal(int firstBeat)
        {
            int beat = firstBeat;
            while (!noise.GoalReached)
            {
                noise.AdvanceToBeat(beat, new GridPosition(3, 3));
                if (noise.Pickups.Count > 0) noise.TryCollect(noise.Pickups[0].Cell, noise.SessionId, noise.PhaseId);
                if (!noise.GoalReached) beat++;
            }
            return beat;
        }
        [TestCase(true)] [TestCase(false)]
        public void BothConditionsAreRequiredInEitherOrder(bool damageFirst)
        {
            int beat;
            if (damageFirst)
            {
                health.ApplyDamage(99, 0);
                Assert.That(phases.ResolveBeat(0), Is.False);
                Assert.That(health.ApplyDamage(1, 0), Is.Zero);
                beat = CollectGoal(1);
            }
            else
            {
                beat = CollectGoal(0);
                Assert.That(phases.ResolveBeat(beat++), Is.False);
                health.ApplyDamage(99, 0);
            }
            Assert.That(phases.ResolveBeat(beat), Is.True);
            Assert.That(phases.PhaseId, Is.EqualTo(2));
            Assert.That(health.CurrentHp, Is.EqualTo(6));
            Assert.That(health.TotalHp, Is.EqualTo(18));
            Assert.That(noise.Collected, Is.Zero);
            Assert.That(noise.Required, Is.EqualTo(4));
            Assert.That(noise.Pickups, Is.Empty);
            Assert.That(phases.ResolveBeat(beat), Is.False);
            Assert.That(health.ApplyDamage(1, 0), Is.Zero, "Stale damage must not enter the new segment.");
        }
        [Test] public void ExactlyThreeUnlocksAndNoFourthPhaseNoise()
        {
            int beat = 0, events = 0;
            phases.PhaseUnlocked += _ => events++;
            for (int segment = 0; segment < 3; segment++)
            {
                beat = CollectGoal(beat);
                health.ApplyDamage(100, segment);
                Assert.That(phases.ResolveBeat(beat++), Is.True);
            }
            Assert.That(events, Is.EqualTo(3));
            Assert.That(phases.PhaseId, Is.EqualTo(4));
            noise.AdvanceToBeat(1000, new GridPosition(0, 0));
            Assert.That(noise.Pickups, Is.Empty);
            Assert.That(noise.Required, Is.Zero);
            Assert.That(phases.Completed, Is.False);
            health.ApplyDamage(100, 3);
            Assert.That(phases.ResolveBeat(1000), Is.False);
            Assert.That(phases.Completed, Is.True);
            Assert.That(health.TotalHp, Is.Zero);
        }
        [Test] public void UnlockSpawnsNextBeatOnceAndRestartRestoresPhaseOne()
        {
            int beat = CollectGoal(0);
            health.ApplyDamage(6, 0); phases.ResolveBeat(beat);
            noise.AdvanceToBeat(beat, new GridPosition(0, 0));
            Assert.That(noise.Pickups, Is.Empty);
            noise.AdvanceToBeat(beat + 1, new GridPosition(0, 0));
            Assert.That(noise.Pickups.Count, Is.EqualTo(1));
            phases.Reset(2, 87);
            Assert.That(phases.PhaseId, Is.EqualTo(1));
            Assert.That(health.TotalHp, Is.EqualTo(24));
            Assert.That(noise.Required, Is.EqualTo(3));
            Assert.That(noise.SessionId, Is.EqualTo(2));
            Assert.That(noise.Pickups, Is.Empty);
        }
    }
}
