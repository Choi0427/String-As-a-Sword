using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Editor;
using StringAsASword.Simulation;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class BattleActionMixReportTests
    {
        [Test]
        public void ReportCountsEveryGroupWritesItsFilesAndCanRerunInPlace()
        {
            var settings = new BattleSettings
            {
                Bpm = 87, NoteCount = 72, Health = new[] { 8, 2, 2, 8 }, PlayerMaxHp = 5,
                Schedule = new RandomAttackSchedule { Enabled = true, MaxWarning = 4 },
                Patterns = new[] {
                    new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2 },
                    new PatternDefinition { Name = "Chord", Kind = PatternKind.FullBoard, Response = AttackResponse.GuardOnly, TelegraphNotes = 2, Hits = 2 },
                    new PatternDefinition { Name = "Checker", Kind = PatternKind.Checker, Response = AttackResponse.DodgeOnly, TelegraphNotes = 2, Hits = 2 }
                }
            };
            string directory = Path.Combine(Path.GetTempPath(), "battle-action-mix-" + Guid.NewGuid().ToString("N"));
            try
            {
                string text = BattleActionMixReport.Run(settings, directory, 2);
                Assert.That(BattleActionMixReport.Run(settings, directory, 2), Does.Contain("Plan criteria:"), "Reruns must not need an empty directory.");
                Assert.That(text, Does.Contain("share three-bot mean").And.Contain("leftover median").And.Contain("Overall:"));

                var summary = JsonUtility.FromJson<BattleActionMixReport.Summary>(File.ReadAllText(Path.Combine(directory, "summary.json")));
                Assert.That(summary.executedRuns, Is.EqualTo(18), "Three groups, three strategies, two seeds.");
                Assert.That(summary.groups.Count, Is.EqualTo(12), "Each group also has its three-bot row.");
                Assert.That(summary.invariantFailures, Is.Zero);
                Assert.That(summary.replayFailures, Is.Zero, string.Join("; ", summary.replayErrors));
                Assert.That(summary.replayed, Is.EqualTo(18));

                var live = new[] { 0, 1 }.Select(seed => BattleSimulator.Run(settings, seed, StrategyKind.AttackHold).Battle).ToArray();
                var row = summary.groups.First(g => g.group == "development" && g.strategy == nameof(StrategyKind.AttackHold));
                double Mean(string metric) => row.distributions.First(d => d.metric == metric).mean;
                Assert.That(Mean("guard"), Is.EqualTo(live.Average(b => b.Records.Count(r => r.category == "Guard"))));
                Assert.That(Mean("attack"), Is.EqualTo(live.Average(b => b.Records.Count(r => r.category == "EffectiveAttack"))));
                Assert.That(Mean("hits"), Is.EqualTo(live.Average(b => b.Records.Count(r => r.hit))));
                Assert.That(row.attackShare + row.moveShare + row.guardShare, Is.EqualTo(1).Within(1e-9));
                Assert.That(row.runs, Is.EqualTo(2));

                var csv = File.ReadAllLines(Path.Combine(directory, "action-mix.csv"));
                Assert.That(csv[0], Is.EqualTo("group,strategy,metric,count,mean,p10,p50,p90,min,max"));
                Assert.That(csv.Any(l => l.StartsWith("miss5,ALL,guard_share,", StringComparison.Ordinal)), Is.True);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
