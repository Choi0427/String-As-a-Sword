using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Editor;
using V = StringAsASword.Editor.Phase1PatternVerification;

namespace StringAsASword.Tests
{
    /// <summary>
    /// The phase-1 boss shapes played in real BattleSessions with the project's song: every shape keeps its drawn cells
    /// (moved and clipped for anchored ones), discloses at note - warning, counts down, fires on its note, and answers
    /// guard / wait / move as the 0/1/2 rules say. See Docs/BossChart/phase1-verification.md.
    /// </summary>
    public sealed class Phase1PatternTests
    {
        private static BattleSettings settings;
        private static BattleSettings Settings => settings ?? (settings = BattleAnalysis.LoadSettings());
        private static ulong Bit(string cell) => 1UL << ((cell[1] - '1') * 8 + (cell[0] - 'a'));
        private static ulong Bits(params string[] cells) => cells.Aggregate(0UL, (m, c) => m | Bit(c));

        public static IEnumerable<string> Shapes() => BossChart.Parse(File.ReadAllText(V.ChartPath)).Attacks.Select(a => a.Shape.Name);

        [TestCaseSource(nameof(Shapes))]
        public void ShapePlaysAsDrawn(string shape)
        {
            var probes = V.Run(Settings, File.ReadAllText(V.ChartPath), shape);
            var failed = probes.Where(p => p.Status == V.Failed).Select(p => $"{p.Scenario} {p.Start}→{p.Target}: {p.Detail}").ToList();
            Assert.That(failed, Is.Empty, string.Join("\n", failed));
            Assert.That(probes.Count(p => p.Status == V.Passed), Is.GreaterThanOrEqualTo(2), "at least one danger and one safe check ran");
        }

        [Test]
        public void VerificationChartsPassTheValidator()
        {
            foreach (var path in new[] { V.ChartPath, V.CombosPath, BossChartValidator.DefaultChartPath })
            {
                var result = BossChartValidator.Check(File.ReadAllText(path), Settings);
                Assert.That(result.Passed, Is.True, path + "\n" + string.Join("\n", result.Findings.Select(f => f.Message)));
                Assert.That(result.Warned, Is.Zero, path);
            }
        }

        [Test]
        public void CombinationsPlayAsDrawn()
        {
            var probes = V.Run(Settings, File.ReadAllText(V.CombosPath));
            var failed = probes.Where(p => p.Status == V.Failed).Select(p => $"{p.Shape} {p.Scenario}: {p.Detail}").ToList();
            Assert.That(failed, Is.Empty, string.Join("\n", failed));
            // The full board of 1 is guard-only: a guard anywhere succeeds on both hits.
            Assert.That(probes.Any(p => p.Shape == "전체" && p.Scenario == "1칸 가드" && p.Status == V.Passed));
        }

        // The stage's boss chart (battle-7): notes 0-123 as drawn, then the random scheduler from 124.
        [Test]
        public void StageChartPlaysAsDrawnThenHandsOverAt124()
        {
            string text = File.ReadAllText(BossChartValidator.DefaultChartPath);
            var failed = V.Run(Settings, text).Where(p => p.Status == V.Failed).Select(p => $"{p.Shape} {p.Scenario}: {p.Detail}").ToList();
            Assert.That(failed, Is.Empty, string.Join("\n", failed));
            Assert.That(BossChart.Parse(text).RandomFrom, Is.EqualTo(124));
            var settings = Settings.Copy(); settings.BossChartText = text;
            var battle = new BattleSession(settings, 7);
            battle.Advance(settings.CreateTimeline().TimeOf(160) + settings.GoodWindow + .001);
            var published = battle.Events.Where(e => e.type == "pattern-publish").ToList();
            Assert.That(published.Where(e => e.kind == "Drawn").Max(e => e.impactNote), Is.LessThan(124));
            Assert.That(published.Where(e => e.kind != "Drawn").Min(e => e.impactNote), Is.GreaterThanOrEqualTo(124));
        }

        // Hand-computed cells, checked against the runtime (not the fixture's own expectation).
        [Test]
        public void AnchoredShapesMoveAndClipAtTheEdges()
        {
            var shapes = BossChart.Parse(File.ReadAllText(V.ChartPath)).Shapes;
            (ulong n, ulong s) At(string shape, string cell) => BossChart.Cells(shapes[shape], V.Parse(cell));
            Assert.That(At("룩가로", "a4").n, Is.EqualTo(Bits("a4", "b4", "c4", "d4", "e4")), "the row loses f4-h4 at the left edge");
            Assert.That(At("룩세로", "d4").n, Is.EqualTo(Bits("d1", "d2", "d3", "d4", "d5", "d6", "d7")), "anchored on d5, so d8 is missing from d4");
            Assert.That(At("킹고리", "h1").n, Is.EqualTo(Bits("g1", "g2", "h2")));
            Assert.That(At("앞길막기", "d8").n, Is.EqualTo(Bits("d8")));
            var pin = At("핀강", "a8");
            Assert.That(pin.n, Is.Zero); Assert.That(pin.s, Is.EqualTo(Bits("a8")));
        }

        // A one-note warning leaves no move before the impact: stay and the ring misses, step and it hits.
        [Test]
        public void KingRingPunishesAnyStepButKnightAdviceCannotReach()
        {
            var probes = V.Run(Settings, File.ReadAllText(V.ChartPath)).Where(p => p.Status == V.Passed).ToList();
            var ringSteps = probes.Where(p => p.Shape == "킹고리" && p.Scenario.Contains("이동")).ToList();
            Assert.That(ringSteps, Is.Not.Empty);
            Assert.That(ringSteps.All(p => p.Expected == "player-hit"));
            var knightSteps = probes.Where(p => p.Shape == "나이트훈수" && p.Scenario.Contains("이동")).ToList();
            Assert.That(knightSteps, Is.Not.Empty);
            Assert.That(knightSteps.All(p => p.Expected == "없음"), "L cells are three steps away; one step is always safe");
        }
    }
}
