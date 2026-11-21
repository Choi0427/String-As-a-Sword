using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Stage;

namespace StringAsASword.Tests
{
    public sealed class NoisePickupTests
    {
        private readonly GridPosition player = new GridPosition(3, 3);
        private NoisePickupSystem Make(int target = 3)
        { var noise = new NoisePickupSystem(new BoardState()); noise.Reset(1, 87, target); return noise; }
        [Test] public void ScriptedNoiseWaitsForItsNoteAndLandsBesideTheKnight()
        {
            var noise = Make(NoisePickupSystem.Unlimited);
            noise.SetScripted(BossChart.Parse("noise 10 0,2").NoiseSpawns);
            noise.AdvanceToBeat(9, player);
            Assert.That(noise.Pickups, Is.Empty, "notes 0 and 8 would spawn at random without the script");
            noise.AdvanceToBeat(10, player);
            Assert.That(noise.Pickups.Single().Cell, Is.EqualTo(new GridPosition(3, 5)));
            Assert.That(noise.TryCollect(new GridPosition(3, 5), 1, 1), Is.True);
            noise.AdvanceToBeat(15, player);
            Assert.That(noise.Pickups, Is.Empty);
            noise.AdvanceToBeat(16, player);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1), "after the script the 8-note grid carries on");
        }
        [Test] public void ScriptedNoiseMirrorsOrMovesWhenItsCellIsOffTheBoard()
        {
            var noise = Make(NoisePickupSystem.Unlimited);
            Assert.That(noise.ScriptedCell(new GridPosition(6, 7), 0, 2), Is.EqualTo(new GridPosition(6, 5)), "two above the top row: two below");
            Assert.That(noise.ScriptedCell(new GridPosition(7, 7), 2, 2), Is.EqualTo(new GridPosition(7, 6)), "nothing fits: the nearest free cell");
            Assert.That(noise.ScriptedCell(new GridPosition(0, 0), 0, 2), Is.EqualTo(new GridPosition(0, 2)));
        }
        [Test] public void SpawnExcludesPlayerAndExistingPickups()
        {
            var noise = Make(); noise.AdvanceToBeat(0, player);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1));
            noise.AdvanceToBeat(1000, player);
            Assert.That(noise.Pickups.Count, Is.EqualTo(2));
            Assert.That(noise.Pickups[0].Cell, Is.Not.EqualTo(player));
            Assert.That(noise.Pickups[1].Cell, Is.Not.EqualTo(player));
            Assert.That(noise.Pickups[0].Cell, Is.Not.EqualTo(noise.Pickups[1].Cell));
        }
        [Test] public void PickupPersistsWithoutTimeoutAndCollectsOnlyOnce()
        {
            var noise = Make(); noise.AdvanceToBeat(0, player); var pickup = noise.Pickups[0];
            noise.AdvanceToBeat(800, player);
            Assert.That(noise.Pickups[0].Id, Is.EqualTo(pickup.Id));
            Assert.That(noise.TryCollect(pickup.Cell, 1, 1), Is.True);
            Assert.That(noise.TryCollect(pickup.Cell, 1, 1), Is.False);
            Assert.That(noise.Collected, Is.EqualTo(1));
        }
        [Test] public void GoalCapsCollectionAndKeepsRemainingPickupInactive()
        {
            var noise = Make(1); noise.AdvanceToBeat(8, player);
            noise.TryCollect(noise.Pickups[0].Cell, 1, 1);
            var remaining = noise.Pickups[0]; noise.AdvanceToBeat(40, player);
            Assert.That(noise.TryCollect(remaining.Cell, 1, 1), Is.False);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1)); Assert.That(noise.Collected, Is.EqualTo(1));
        }
        [Test] public void RestartReplaysSeedAndRejectsOldSession()
        {
            var noise = Make(); noise.AdvanceToBeat(0, player); var cell = noise.Pickups[0].Cell;
            noise.Reset(2, 87, 3); noise.AdvanceToBeat(0, player);
            Assert.That(noise.Pickups[0].Cell, Is.EqualTo(cell));
            Assert.That(noise.TryCollect(cell, 1, 1), Is.False);
        }
        [Test] public void PhaseStartCoincidingWithRegularBeatSpawnsOnce()
        {
            var noise = Make(); noise.BeginPhase(2, 4, 8); noise.AdvanceToBeat(8, player);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1));
            Assert.That(noise.TryCollect(noise.Pickups[0].Cell, 1, 1), Is.False);
        }
        [Test] public void NoCandidateDefersSpawn()
        {
            var noise = new NoisePickupSystem(new BoardState(1, 1)); noise.Reset(1, 87, 3);
            noise.AdvanceToBeat(0, new GridPosition(0, 0)); Assert.That(noise.Pickups.Count, Is.Zero);
        }
        [Test] public void CapacityDoesNotCreateCatchupBurst()
        {
            var noise = Make(); noise.AdvanceToBeat(80, player);
            noise.TryCollect(noise.Pickups[0].Cell, 1, 1); noise.AdvanceToBeat(81, player);
            Assert.That(noise.Pickups.Count, Is.EqualTo(1));
            noise.AdvanceToBeat(88, player); Assert.That(noise.Pickups.Count, Is.EqualTo(2));
        }
    }
}
