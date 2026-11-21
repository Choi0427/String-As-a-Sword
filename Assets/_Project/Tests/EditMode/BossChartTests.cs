using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Editor;

namespace StringAsASword.Tests
{
    public sealed class BossChartTests
    {
        private static ulong Bit(int x, int y) => 1UL << (y * 8 + x);
        private const string X =
            "shape X\n20000002\n02000020\n00100100\n00011000\n00011000\n00100100\n02000020\n20000002\n";
        private const string Full = "shape 전체\n" + "11111111\n11111111\n11111111\n11111111\n11111111\n11111111\n11111111\n11111111\n";
        private const string Cross = "shape 십자 anchor=4,4\n00010000\n00010000\n00010000\n11121111\n00010000\n00010000\n00010000\n00010000\n";
        // Everything but the bottom-left corner, strong: only cells within two steps of it can dodge with a 2-note warning.
        private const string Corner = "shape 구석\n22222222\n22222222\n22222222\n22222222\n22222222\n22222222\n22222222\n02222222\n";

        private static BattleSettings Settings(int notes = 80, params int[] fastNotes)
        {
            double beat = 60.0 / 87, t = 1;
            var times = new double[notes];
            for (int i = 0; i < notes; i++) { times[i] = t; t += fastNotes.Contains(i + 1) ? beat / 4 : beat; }
            return new BattleSettings { Bpm = 87, NoteCount = notes, NoteTimes = times };
        }

        // ── Parser ──
        [Test] public void DigitsSplitIntoNormalAndStrongCells()
        {
            var shape = BossChart.Parse(X).Shapes["X"];
            Assert.That(shape.Strong & Bit(0, 7), Is.Not.Zero, "top-left 2 is strong");
            Assert.That(shape.Strong & Bit(7, 0), Is.Not.Zero, "bottom-right 2 is strong");
            Assert.That(shape.Normal & Bit(2, 5), Is.Not.Zero, "third row's 1 is normal");
            Assert.That(shape.Normal & shape.Strong, Is.Zero);
            Assert.That(shape.GuardOnly, Is.False);
            Assert.That(BossChart.Parse(Full).Shapes["전체"].GuardOnly, Is.True);
        }
        [Test] public void ErrorsNameTheLine()
        {
            void Fails(string text, int line, string fragment)
            {
                var e = Assert.Throws<BossChartException>(() => BossChart.Parse(text));
                Assert.That(e.Line, Is.EqualTo(line), e.Message);
                StringAssert.Contains(fragment, e.Message);
            }
            Fails(X + "\n10 없는모양 3", 11, "모르는 모양");
            Fails("shape A\n0000000\n", 2, "8칸");
            Fails("shape A\n0000000x\n", 2, "0, 1, 2");
            Fails(X + "10 X 9", 10, "경고");
            Fails(X + "10 X 3 hits=9", 10, "hits");
            Fails("shape A\n" + string.Concat(Enumerable.Repeat("22222222\n", 8)), 1, "막을 수도 피할 수도");
            Fails("hello", 1, "알 수 없는 줄");
        }
        [Test] public void NoiseLinesAreReadInNoteOrderAndCheckTheirForm()
        {
            var chart = BossChart.Parse(X + "noise 12 1,-2\nnoise 10 0,2\n");
            Assert.That(chart.NoiseSpawns.Select(n => n.Note), Is.EqualTo(new[] { 10, 12 }));
            Assert.That((chart.NoiseSpawns[0].Dx, chart.NoiseSpawns[0].Dy), Is.EqualTo((0, 2)));
            Assert.That((chart.NoiseSpawns[1].Dx, chart.NoiseSpawns[1].Dy), Is.EqualTo((1, -2)));
            Assert.That(BossChart.Parse(X).NoiseSpawns, Is.Empty);
            void Fails(string text, int line, string fragment)
            {
                var e = Assert.Throws<BossChartException>(() => BossChart.Parse(text));
                Assert.That(e.Line, Is.EqualTo(line), e.Message);
                StringAssert.Contains(fragment, e.Message);
            }
            Fails("noise 10", 1, "형식");
            Fails("noise 10 0,0", 1, "기사 칸");
            Fails("noise 10 a,2", 1, "정수");
            Fails("noise 10 9,0", 1, "-7~7");
            Fails("noise 10 0,2\nnoise 10 1,1", 2, "이미");
        }
        [Test] public void TheStageChartTeachesTheAttacksThenPlacesTheFirstNoise()
        {
            // 간주1 is the tutorial: two normal attacks on the knight's row, a slash down its column, then the first noise two
            // cells above it, nothing while the resonance is practised, and a full-board attack to guard.
            var settings = BattleAnalysis.LoadSettings();
            var lesson = settings.Chart.Attacks.Where(a => a.Note <= 31).Select(a => (a.Note, a.Shape.Name, a.Warning)).ToArray();
            Assert.That(lesson, Is.EqualTo(new[] { (2, "룩가로", 2), (5, "룩가로", 2), (8, "룩세로강", 2), (30, "전체", 3) }));
            Assert.That(settings.Chart.NoiseSpawns.Single().Note, Is.EqualTo(10));
            var battle = new BattleSession(settings, 87);
            var timeline = settings.CreateTimeline();
            var shown = new List<(int note, AttackResponse response, bool onKnight)>();
            for (int note = 0; note <= 9; note++)
            {
                battle.Advance(timeline.TimeOf(note) + .2);
                foreach (var t in battle.Patterns.Active)
                    if (!shown.Exists(s => s.note == t.ImpactNote && s.response == t.Response))
                        shown.Add((t.ImpactNote, t.Response, (t.Cells & Bit(6, 0)) != 0));
                Assert.That(battle.Noise.Pickups, Is.Empty, $"no noise before its lesson (note {note})");
            }
            Assert.That(shown, Is.EqualTo(new[] { (2, AttackResponse.Either, true), (5, AttackResponse.Either, true), (8, AttackResponse.DodgeOnly, true) }),
                "every lesson attack reaches a knight who stays on g1");
            battle.Advance(timeline.TimeOf(10) + .2);
            Assert.That(battle.Noise.Pickups.Single().Cell, Is.EqualTo(new GridPosition(6, 2)), "g3, two cells above g1");
        }
        [Test] public void PieceOptionNamesTheSummonedPiecesAndReachesTheTelegraph()
        {
            const string rows = "\n00010000\n00010000\n00010000\n11111111\n00010000\n00010000\n00010000\n00010000\n";
            var chart = BossChart.Parse("shape 챈슬러 piece=룩+나이트 anchor=4,4" + rows + "shape 룩 anchor=4,4 piece=룩" + rows + X + "10 챈슬러 2");
            Assert.That(chart.Shapes["챈슬러"].Pieces, Is.EqualTo(new[] { ChessPiece.Rook, ChessPiece.Knight }));
            Assert.That(chart.Shapes["챈슬러"].HasAnchor, Is.True, "options are read in either order");
            Assert.That(chart.Shapes["룩"].Pieces, Is.EqualTo(new[] { ChessPiece.Rook }));
            Assert.That(chart.Shapes["X"].Pieces, Is.Empty);
            var hits = new BossPatternSystem(new PatternDefinition[0]).AuthoredHits(chart.Attacks[0], new GridPosition(3, 3));
            Assert.That(hits[0].Pieces, Is.EqualTo(new[] { ChessPiece.Rook, ChessPiece.Knight }));
            var e = Assert.Throws<BossChartException>(() => BossChart.Parse("shape A piece=용" + rows));
            StringAssert.Contains("모르는 기물", e.Message);
        }
        [Test] public void AnchoredShapeMovesToThePlayerAndClips()
        {
            var shape = BossChart.Parse(Cross).Shapes["십자"];
            Assert.That(shape.Anchor, Is.EqualTo(new GridPosition(3, 4)));
            var (normal, strong) = BossChart.Cells(shape, new GridPosition(0, 7));
            Assert.That(strong, Is.EqualTo(Bit(0, 7)));
            ulong expected = Bit(1, 7) | Bit(2, 7) | Bit(3, 7) | Bit(4, 7) | Bit(0, 6) | Bit(0, 5) | Bit(0, 4) | Bit(0, 3);
            Assert.That(normal, Is.EqualTo(expected));
        }
        [Test] public void SectionsNameNotes()
        {
            var chart = BossChart.Parse("section 후렴 97-140\n" + X);
            Assert.That(chart.SectionAt(100), Is.EqualTo("후렴"));
            Assert.That(chart.SectionAt(50), Is.Empty);
        }

        // ── Chart mode at runtime ──
        private static (BossPatternSystem system, List<(string type, BossTelegraph t)> log) Run(string chart, int toNote, int phase = 1)
        {
            var system = new BossPatternSystem(new PatternDefinition[0], null, 0, null, BossChart.Parse(chart));
            var log = new List<(string, BossTelegraph)>();
            system.Changed += (type, t) => log.Add((type, t));
            system.BeginPhase(phase, 0);
            for (int closed = -1; closed <= toNote; closed++) { system.Resolve(closed); system.Publish(closed, new GridPosition(3, 7), 80, false); }
            return (system, log);
        }
        [Test] public void PublishesAtNoteMinusWarningWithEveryHitAndBothDigits()
        {
            var (system, log) = Run(X + "10 X 3 hits=2 every=2", 7);
            var published = log.Where(e => e.type == "publish").Select(e => e.t).ToList();
            Assert.That(published.Count, Is.EqualTo(4), "two hits, each split into a normal and a strong telegraph");
            Assert.That(published.Select(t => t.ImpactNote).Distinct(), Is.EquivalentTo(new[] { 10, 12 }));
            Assert.That(published.Count(t => t.Response == AttackResponse.DodgeOnly), Is.EqualTo(2));
            Assert.That(published.All(t => t.Kind == PatternKind.Drawn && t.Reason.Length == 0));
            var (early, earlyLog) = Run(X + "10 X 3", 6);
            Assert.That(earlyLog.Any(e => e.type == "publish"), Is.False, "nothing before note 10 - 3 closes");
        }
        [Test] public void StrongCellsBreakAGuardAndNormalCellsDoNot()
        {
            var (system, _) = Run(X + "10 X 3", 9);
            ulong fired = system.Resolve(10);
            var shape = BossChart.Parse(X).Shapes["X"];
            Assert.That(fired, Is.EqualTo(shape.Normal | shape.Strong));
            // BattleSession breaks a guard on LastFiredUnblockable cells only.
            Assert.That(system.LastFiredUnblockable, Is.EqualTo(shape.Strong));
        }
        [Test] public void FullBoardOfOnesIsGuardOnly()
        {
            var (_, log) = Run(Full + "10 전체 3", 7);
            var t = log.Single(e => e.type == "publish").t;
            Assert.That(t.Response, Is.EqualTo(AttackResponse.GuardOnly));
            Assert.That(t.Cells, Is.EqualTo(ulong.MaxValue));
        }
        [Test] public void PhaseFilterSkipsAndPhaseChangeDoesNotCancel()
        {
            var (_, phase1) = Run(X + "10 X 3 phase>=2", 9, phase: 1);
            Assert.That(phase1.Single().type, Is.EqualTo("skip"));
            Assert.That(phase1.Single().t.Reason, Is.EqualTo("phase"));
            var (system, log) = Run(X + "10 X 3", 8);
            system.BeginPhase(2, 9);
            Assert.That(log.Any(e => e.type == "cancel"), Is.False);
            Assert.That(system.Active.Count, Is.EqualTo(2));
            system.Halt();
            Assert.That(system.Active, Is.Empty, "the finale stun still clears the board");
        }

        // ── Handover to the random scheduler ──
        private static List<(string type, BossTelegraph t)> RunWithRandom(string chart, int toNote)
        {
            var system = new BossPatternSystem(PatternDefinition.Defaults(), new RandomAttackSchedule { Enabled = true }, 3, null, BossChart.Parse(chart));
            var log = new List<(string, BossTelegraph)>();
            system.Changed += (type, t) => log.Add((type, t));
            system.BeginPhase(1, 0);
            for (int closed = -1; closed <= toNote; closed++) { system.Resolve(closed); system.Publish(closed, new GridPosition(3, 3), 80, false); }
            return log;
        }
        [Test] public void RandomTakesOverAfterTheChartAndNeverBefore()
        {
            Assert.That(BossChart.Parse(X + "random 30").RandomFrom, Is.EqualTo(30));
            var log = RunWithRandom(X + "10 X 3\nrandom 30", 70);
            var randoms = log.Where(e => e.type == "publish" && e.t.Kind != PatternKind.Drawn).Select(e => e.t).ToList();
            Assert.That(randoms, Is.Not.Empty, "the random scheduler publishes after the handover");
            Assert.That(randoms.Min(t => t.ImpactNote), Is.GreaterThanOrEqualTo(30));
            Assert.That(log.Where(e => e.type == "publish" && e.t.Kind == PatternKind.Drawn).Max(e => e.t.ImpactNote), Is.LessThan(30));
            var chartOnly = RunWithRandom(X + "10 X 3", 70);
            Assert.That(chartOnly.Any(e => e.t.Kind != PatternKind.Drawn), Is.False, "no `random` line: the chart alone");
        }
        [Test] public void ValidatorRejectsAChartHitAtOrAfterTheHandover()
        {
            var result = BossChartValidator.Check(X + "30 X 3\nrandom 30", Settings());
            Assert.That(result.Passed, Is.False);
            StringAssert.Contains("랜덤 인계", result.Findings.Single(f => f.Error).Message);
            Assert.Throws<BossChartException>(() => BossChart.Parse(X + "random 30\nrandom 40"));
        }

        // ── Validator ──
        [Test] public void ValidatorFailsAnUndodgeableShapeAndListsTheCells()
        {
            var result = BossChartValidator.Check(Corner + "20 구석 2", Settings());
            Assert.That(result.Passed, Is.False);
            var finding = result.Findings.Single(f => f.Error);
            StringAssert.Contains("피할 길 없음. 출발 칸 58개", finding.Message);
        }
        [Test] public void ValidatorFailsAFastSection()
        {
            var result = BossChartValidator.Check(X + "32 X 3", Settings(80, 30, 31, 32, 33));
            Assert.That(result.Passed, Is.False);
            StringAssert.Contains("빠른 구간(노트 30", result.Findings.Single(f => f.Error).Message);
        }
        [Test] public void ValidatorPassesAFairChartAndReportsParseErrors()
        {
            var fair = BossChartValidator.Check(X + Cross + Full + "10 X 3\n20 십자 2\n30 전체 3\n40 X 2 hits=3 every=2", Settings());
            Assert.That(fair.Passed, Is.True, string.Join("\n", fair.Findings.Select(f => f.Message)));
            Assert.That(fair.Attacks, Is.EqualTo(4));
            var broken = BossChartValidator.Check("shape A\n0000000\n", Settings());
            Assert.That(broken.Passed, Is.False);
            StringAssert.Contains("2번째 줄", broken.ParseError);
        }
        [Test] public void ValidatorRejectsAnAttackPastTheSong()
        {
            var result = BossChartValidator.Check(X + "79 X 3 hits=2 every=2", Settings());
            Assert.That(result.Passed, Is.False);
            StringAssert.Contains("곡 노트 수", result.Findings.Single(f => f.Error).Message);
        }
    }
}
