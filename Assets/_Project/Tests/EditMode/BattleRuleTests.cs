using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Player;

namespace StringAsASword.Tests
{
    public sealed class BattleRuleTests
    {
        private static BattleSettings Settings(int notes = 344) => new BattleSettings
        {
            Bpm = 87,
            NoteCount = notes,
            Patterns = Array.Empty<PatternDefinition>()
        };

        private static double NoteTime(BattleSettings settings, int note)
            => note * 60.0 / settings.Bpm;

        private static double CloseTime(BattleSettings settings, int note)
            => NoteTime(settings, note) + settings.GoodWindow + 0.000001;

        [TestCase(PlayerActionKind.Guard)]
        [TestCase(PlayerActionKind.Move)]
        public void LateGoodAtInclusiveBoundaryCanGuardOrDodge(PlayerActionKind action)
        {
            var settings = Settings(8);
            settings.Patterns = new[]
            {
                new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell,
                    TelegraphNotes = 1, FirstImpact = 2, RepeatNotes = 24 }
            };
            var battle = new BattleSession(settings, 87);
            battle.Noise.BeginPhase(1, 0, 0);

            battle.Advance(CloseTime(settings, 0));
            Assert.That(battle.Patterns.Active.Any(p => p.ImpactNote == 1), Is.True);

            double lateGood = NoteTime(settings, 1) + settings.GoodWindow;
            var result = battle.Request(action, MoveDirection.Up, lateGood);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Timing.ErrorSeconds, Is.EqualTo(settings.GoodWindow).Within(1e-8));
            battle.Advance(CloseTime(settings, 1));

            var record = battle.Records.Single(r => r.noteIndex == 1);
            Assert.That(record.hit, Is.False);
            Assert.That(record.guardSuccess, Is.EqualTo(action == PlayerActionKind.Guard));
            Assert.That(battle.PlayerHp, Is.EqualTo(settings.PlayerMaxHp));
        }

        [Test]
        public void OverlappingPatternsDamagePlayerOnlyOnceOnTheirSharedNote()
        {
            var settings = Settings(8);
            settings.Patterns = new[]
            {
                new PatternDefinition { Name = "Lock A", Kind = PatternKind.LockedCell,
                    TelegraphNotes = 1, FirstImpact = 2, RepeatNotes = 24 },
                new PatternDefinition { Name = "Lock B", Kind = PatternKind.LockedCell,
                    TelegraphNotes = 1, FirstImpact = 2, RepeatNotes = 24 }
            };
            var battle = new BattleSession(settings, 87);
            battle.Noise.BeginPhase(1, 0, 0);
            battle.Advance(CloseTime(settings, 0));
            Assert.That(battle.Patterns.Active.Count, Is.EqualTo(2));

            battle.Advance(CloseTime(settings, 1));

            Assert.That(battle.Patterns.LastFired.Count, Is.EqualTo(2));
            Assert.That(battle.Records.Single(r => r.noteIndex == 1).hit, Is.True);
            Assert.That(battle.PlayerHp, Is.EqualTo(settings.PlayerMaxHp - 1));
        }

        [Test]
        public void BossWinTakesPriorityWhenFinalBlowAndLethalPatternResolveTogether()
        {
            var settings = Settings(32);
            settings.PlayerMaxHp = 1;
            settings.Health = new[] { 1, 1, 1, 1 };
            settings.NoiseTargets = new[] { 1, 1, 1 };
            settings.MaxCollectionCost = 12;
            settings.Patterns = new[]
            {
                new PatternDefinition { Name = "Final lock", Kind = PatternKind.LockedCell,
                    TelegraphNotes = 1, FirstImpact = 21, RepeatNotes = 24 }
            };
            var battle = new BattleSession(settings, 87);
            battle.Noise.CandidateAllowed = null;
            battle.Advance(0);
            for (int phase = 0; phase < 3; phase++)
            {
                battle.Noise.AdvanceToBeat(phase, battle.Session.Position);
                Assert.That(battle.Noise.Pickups.Count, Is.GreaterThan(0));
                var pickup = battle.Noise.Pickups[0];
                Assert.That(battle.Noise.TryCollect(pickup.Cell, battle.Noise.SessionId, battle.Noise.PhaseId), Is.True);
                Assert.That(battle.Health.ApplyDamage(1, phase), Is.EqualTo(1));
                Assert.That(battle.Phases.ResolveBeat(phase, phase + 1), Is.True);
            }
            Assert.That(battle.Phases.PhaseId, Is.EqualTo(4));
            battle.Patterns.BeginPhase(4, 0);

            for (int note = 0; note < 4; note++)
            {
                var move = battle.Request(PlayerActionKind.Move, MoveDirection.Up, NoteTime(settings, note));
                Assert.That(move.Accepted, Is.True);
                battle.Advance(CloseTime(settings, note));
            }
            Assert.That(battle.Session.Position, Is.EqualTo(new GridPosition(3, 7)));
            for (int note = 4; note < 20; note++) battle.Advance(CloseTime(settings, note));

            var attack = battle.Request(PlayerActionKind.Attack, MoveDirection.Up, NoteTime(settings, 20));
            Assert.That(attack.Accepted, Is.True);
            battle.Advance(CloseTime(settings, 20));

            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
            Assert.That(battle.PlayerHp, Is.Zero);
            Assert.That(battle.Health.Defeated, Is.True);
            Assert.That(battle.Records.Single(r => r.noteIndex == 20).hit, Is.True);
        }

        [Test]
        public void FinalTimeoutClosesExactlyNoteCountNotesAndRejectsLaterGuard()
        {
            var settings = Settings(3);
            var battle = new BattleSession(settings, 7);
            battle.Advance(CloseTime(settings, settings.NoteCount - 1));

            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Timeout));
            Assert.That(battle.Records.Count, Is.EqualTo(settings.NoteCount));
            Assert.That(battle.LastClosedNote, Is.EqualTo(settings.NoteCount - 1));
            var lateGuard = battle.Request(PlayerActionKind.Guard, MoveDirection.Up, NoteTime(settings, settings.NoteCount));
            Assert.That(lateGuard.Rejection, Is.EqualTo(ActionRejection.BeatExpired));
            Assert.That(battle.Session.AcceptedGuards, Is.Zero);
        }

        [Test]
        public void AnalysisUsesExactBoundariesWhileRuntimeDoesNotForceTransitions()
        {
            var settings = Settings();
            int[] ends = { 100, 148, 216, 344 };
            var analysis = new BattleSession(settings, 87, ends);
            analysis.Advance(CloseTime(settings, ends[0] - 1));
            Assert.That(analysis.Records.Count, Is.EqualTo(100));
            Assert.That(analysis.Phases.PhaseId, Is.EqualTo(2));
            analysis.Advance(CloseTime(settings, ends[1] - 1));
            Assert.That(analysis.Records.Count, Is.EqualTo(148));
            Assert.That(analysis.Phases.PhaseId, Is.EqualTo(3));
            analysis.Advance(CloseTime(settings, ends[2] - 1));
            Assert.That(analysis.Records.Count, Is.EqualTo(216));
            Assert.That(analysis.Phases.PhaseId, Is.EqualTo(4));
            analysis.Advance(CloseTime(settings, ends[3] - 1));
            Assert.That(analysis.Records.Count, Is.EqualTo(344));
            Assert.That(analysis.Events.Where(e => e.type == "phase").Select(e => e.noteIndex),
                Is.EqualTo(new[] { 99, 147, 215 }));
            Assert.That(analysis.Health.Remaining(3), Is.EqualTo(6), "Analysis boundaries must not consume the fourth HP segment.");

            var runtime = new BattleSession(settings, 87);
            runtime.Advance(CloseTime(settings, ends[3] - 1));
            Assert.That(runtime.Outcome, Is.EqualTo(BattleOutcome.Timeout));
            Assert.That(runtime.Records.Count, Is.EqualTo(344));
            Assert.That(runtime.Phases.PhaseId, Is.EqualTo(1));
            Assert.That(runtime.Events.Any(e => e.type == "phase"), Is.False);
        }

        [Test]
        public void LegacyStageCapacityIsClampedToTwoAndSettingsAreCopied()
        {
            var stage = UnityEngine.ScriptableObject.CreateInstance<StringAsASword.Stage.StageDefinition>();
            try
            {
                typeof(StringAsASword.Stage.StageDefinition).GetField("maxActiveNoise",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(stage, 8);
                Assert.That(stage.MaxActiveNoise, Is.EqualTo(2));
                var settings = Settings(8);
                var battle = new BattleSession(settings, 1);
                settings.Health[0] = 999; settings.NoiseTargets[0] = 999;
                Assert.That(battle.Settings.Health[0], Is.EqualTo(6));
                Assert.That(battle.Settings.NoiseTargets[0], Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(stage); }
        }

        [Test]
        public void GuardExpiresWithItsNoteAndMissStillClosesCountdown()
        {
            var settings = Settings(8);
            settings.Patterns = new[] { new PatternDefinition { Name = "Repeated lock", Kind = PatternKind.LockedCell,
                TelegraphNotes = 1, FirstImpact = 2, RepeatNotes = 1 } };
            var battle = new BattleSession(settings, 2);
            battle.Noise.BeginPhase(1, 0, 0);
            battle.Advance(CloseTime(settings, 0));
            battle.Request(PlayerActionKind.Guard, MoveDirection.Up, NoteTime(settings, 1));
            battle.Advance(CloseTime(settings, 1));
            Assert.That(battle.Records[1].guardSuccess, Is.True);
            // No Advance while paused: the same observation cannot consume a note.
            var remaining = battle.Patterns.Active.Select(p => p.Remaining(battle.LastClosedNote)).ToArray();
            battle.Observe(); battle.Observe();
            Assert.That(battle.Patterns.Active.Select(p => p.Remaining(battle.LastClosedNote)), Is.EqualTo(remaining));
            battle.Advance(CloseTime(settings, 2));
            Assert.That(battle.Records[2].hit, Is.True);
            Assert.That(battle.PlayerHp, Is.EqualTo(2));
            Assert.That(battle.Records[2].judgement, Is.EqualTo("Miss"));
            battle.Reset(2, 2);
            Assert.That(battle.LastClosedNote, Is.EqualTo(-1));
            Assert.That(battle.Records, Is.Empty);
        }

        [Test]
        public void PartialCompressedLogReplaysOnlyProcessedNotesAndAcceptedMovement()
        {
            string path = Path.Combine(Path.GetTempPath(), "partial-battle-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
            try
            {
                var settings = Settings(16);
                var live = new BattleSession(settings, 123);
                live.Advance(CloseTime(settings, 0));
                live.Request(PlayerActionKind.Move, MoveDirection.Up, NoteTime(settings, 1));
                BattleLog.Write(path, live, "Restarted");
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Records.Count, Is.EqualTo(1));
                Assert.That(replay.Outcome, Is.EqualTo(BattleOutcome.Running));
                Assert.That(replay.Session.Position, Is.EqualTo(live.Session.Position));
                Assert.That(replay.Session.AcceptedMoves, Is.EqualTo(1));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void SameSeedProducesTheSameSerializedBattleLogHash()
        {
            string directory = Path.Combine(Path.GetTempPath(), "string-sword-log-tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = Settings(16);
                var first = new BattleSession(settings, 1234);
                var second = new BattleSession(settings, 1234);
                first.Advance(CloseTime(settings, settings.NoteCount - 1));
                second.Advance(CloseTime(settings, settings.NoteCount - 1));
                string firstPath = Path.Combine(directory, "first.jsonl");
                string secondPath = Path.Combine(directory, "second.jsonl");
                BattleLog.Write(firstPath, first);
                BattleLog.Write(secondPath, second);

                Assert.That(BattleLog.Hash(File.ReadAllText(firstPath)), Is.EqualTo(BattleLog.Hash(File.ReadAllText(secondPath))));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
