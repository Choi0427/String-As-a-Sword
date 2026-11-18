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
            phases = new PhaseController(health, noise);
            phases.Reset(1, 87);
        }
        [Test] public void EmptyingASegmentOpensTheNextWithoutAnyNoise()
        {
            health.ApplyDamage(5, 0);
            Assert.That(phases.ResolveBeat(0), Is.False);
            health.ApplyDamage(1, 0);
            Assert.That(phases.ResolveBeat(1), Is.True);
            Assert.That(phases.PhaseId, Is.EqualTo(2));
            Assert.That(health.CurrentHp, Is.EqualTo(6));
            Assert.That(phases.ResolveBeat(1), Is.False, "A beat resolves once.");
            Assert.That(phases.ResolveBeat(2), Is.False, "A phase is reported once.");
        }
        [Test] public void ExactlyThreeUnlocksAndNoiseKeepsSpawning()
        {
            int events = 0;
            phases.PhaseUnlocked += _ => events++;
            for (int segment = 0; segment < 3; segment++)
            {
                health.ApplyDamage(6, segment);
                Assert.That(phases.ResolveBeat(segment), Is.True);
            }
            Assert.That(events, Is.EqualTo(3));
            Assert.That(phases.PhaseId, Is.EqualTo(4));
            noise.AdvanceToBeat(1000, new GridPosition(0, 0));
            Assert.That(noise.Pickups, Is.Not.Empty, "Noise is no longer tied to a phase goal.");
            Assert.That(phases.Completed, Is.False);
            health.ApplyDamage(100, 3);
            Assert.That(phases.ResolveBeat(1000), Is.False);
            Assert.That(phases.Completed, Is.True);
            Assert.That(health.TotalHp, Is.Zero);
        }
        [Test] public void RestartRestoresPhaseOne()
        {
            health.ApplyDamage(6, 0); phases.ResolveBeat(0);
            noise.AdvanceToBeat(0, new GridPosition(0, 0));
            phases.Reset(2, 87);
            Assert.That(phases.PhaseId, Is.EqualTo(1));
            Assert.That(health.TotalHp, Is.EqualTo(24));
            Assert.That(noise.SessionId, Is.EqualTo(2));
            Assert.That(noise.Pickups, Is.Empty);
            health.ApplyDamage(6, 0);
            Assert.That(phases.ResolveBeat(0), Is.True, "The reported phase restarts too.");
        }
    }
}
