using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Simulation;

namespace StringAsASword.Tests
{
    public sealed class BattleSimulationTests
    {
        private static BattleSettings Settings() => new BattleSettings
        {
            Bpm = 87, NoteCount = 344, Patterns = Array.Empty<PatternDefinition>()
        };

        [Test]
        public void NearestRankP10KeepsTheLowerHundredthObservation()
        {
            Assert.That(BattleCalibration.Percentile(Enumerable.Range(1, 1000).Reverse(), .1), Is.EqualTo(100));
            Assert.That(BattleCalibration.Percentile(new[] { 0, 0, 0, 7, 8 }, .1), Is.Zero,
                "Failed or unvisited phases must remain in the percentile input.");
        }

        [Test]
        public void AnalysisPartitionsAll344NotesAndEveryCategoryIsExclusive()
        {
            var run = BattleSimulator.RunOne(Settings(), 87, StrategyKind.CollectFirst, BattleSimulator.AnalysisEnds);
            Assert.That(run.Battle.Records.Count, Is.EqualTo(344));
            Assert.That(run.Phases.Select(p => p.move + p.guard + p.effectiveAttack + p.other),
                Is.EqualTo(new[] { 100, 48, 68, 128 }));
            Assert.That(run.Phases.Take(3).Select(p => p.transitionNote), Is.EqualTo(new[] { 99, 147, 215 }));
            Assert.That(run.Battle.Health.CurrentHp, Is.EqualTo(6), "Analysis must not consume the fourth HP segment.");
        }

        [Test]
        public void RawRequestsReproduceEveryClosedNoteIncludingOffsets()
        {
            var settings = Settings();
            settings.InputOffset = .037;
            settings.Patterns = PatternDefinition.Defaults();
            var run = BattleSimulator.RunOne(settings, 11, StrategyKind.MovementUse, null, .05);
            var replay = new BattleSession(settings, 11);
            int input = 0;
            double seconds = 60.0 / settings.Bpm;
            for (int note = 0; note < settings.NoteCount && replay.Outcome == BattleOutcome.Running; note++)
            {
                double time = note * seconds + settings.InputOffset;
                replay.Advance(time);
                while (input < run.Battle.Inputs.Count && run.Battle.Inputs[input].time <= time + 1e-9)
                {
                    var raw = run.Battle.Inputs[input++];
                    replay.Request(raw.action, raw.direction, raw.time);
                }
                replay.Advance(time + settings.GoodWindow + .000001);
            }
            Assert.That(input, Is.EqualTo(run.Battle.Inputs.Count));
            Assert.That(replay.Outcome, Is.EqualTo(run.Battle.Outcome));
            Assert.That(replay.Records.Select(r => r.stateHash), Is.EqualTo(run.Battle.Records.Select(r => r.stateHash)));
            Assert.That(replay.Events.Select(e => e.type + ":" + e.detail + ":" + e.cells),
                Is.EqualTo(run.Battle.Events.Select(e => e.type + ":" + e.detail + ":" + e.cells)));
        }

        [Test]
        public void JsonLinesReplayChecksEveryNoteAndRejectsChangedConfiguration()
        {
            var settings = Settings();
            settings.Patterns = PatternDefinition.Defaults();
            var original = BattleSimulator.RunOne(settings, 97, StrategyKind.CollectFirst, null, .05);
            string path = Path.Combine(Path.GetTempPath(), "battle-replay-" + Guid.NewGuid().ToString("N") + ".jsonl");
            try
            {
                BattleLog.Write(path, original.Battle);
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Records.Select(r => r.stateHash),
                    Is.EqualTo(original.Battle.Records.Select(r => r.stateHash)));
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"bpm\":87", "\"bpm\":88")
                    .Replace("\"Bpm\":87", "\"Bpm\":88"));
                Assert.Throws<InvalidDataException>(() => BattleLog.Replay(path));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        [TestCase(0d, 87d)]
        [TestCase(.037, 87.1)]
        public void LongAnalysisReplayPreservesExactBeatBoundaryDoubles(double offset, double bpm)
        {
            var settings = Settings(); settings.InputOffset = offset; settings.Bpm = bpm;
            settings.Patterns = PatternDefinition.Defaults();
            var run = BattleSimulator.Run(settings, 0, StrategyKind.CollectFirst, analysis: true);
            string path = Path.Combine(Path.GetTempPath(), "battle-precision-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
            try
            {
                BattleLog.Write(path, run.Battle);
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Records.Count, Is.EqualTo(344));
                Assert.That(replay.Records.Select(r => r.stateHash), Is.EqualTo(run.Battle.Records.Select(r => r.stateHash)));
                Assert.That(replay.Inputs.Select(i => i.time), Is.EqualTo(run.Battle.Inputs.Select(i => i.time)));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void InputOmissionsAreSeparateFromBattleRandomnessAndCountOnce()
        {
            var settings = Settings();
            var run = BattleSimulator.RunOne(settings, 5, StrategyKind.CollectFirst, null, 1);
            Assert.That(run.InputMisses, Is.EqualTo(344));
            Assert.That(run.Battle.Inputs, Is.Empty);
            Assert.That(run.Battle.Session.MissCount, Is.EqualTo(344));
            Assert.That(run.Battle.PlayerHp, Is.EqualTo(settings.PlayerMaxHp));
            Assert.That(run.Battle.Records.All(r => r.category == "Other"), Is.True);
            Assert.That(run.Outcome, Is.EqualTo("Timeout"));
        }

        [TestCase(StrategyKind.CollectFirst)]
        [TestCase(StrategyKind.AttackHold)]
        [TestCase(StrategyKind.MovementUse)]
        public void SameSeedAndObservationStrategyProduceIdenticalNotes(StrategyKind strategy)
        {
            var first = BattleSimulator.RunOne(Settings(), 19, strategy, BattleSimulator.AnalysisEnds, .05);
            var second = BattleSimulator.RunOne(Settings(), 19, strategy, BattleSimulator.AnalysisEnds, .05);
            Assert.That(second.Battle.Records.Select(r => r.stateHash),
                Is.EqualTo(first.Battle.Records.Select(r => r.stateHash)));
            Assert.That(second.InputMisses, Is.EqualTo(first.InputMisses));
        }
    }
}
