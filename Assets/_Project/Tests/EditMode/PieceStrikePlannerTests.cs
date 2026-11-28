using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Combat;

namespace StringAsASword.Tests
{
    /// <summary>The automatic piece rules (Docs/BossChart.md 기물 규칙), pinned on the boss chart's drawings as agreed on 2026-11-28.</summary>
    public sealed class PieceStrikePlannerTests
    {
        // Drawings copied from GeomEulHyeon.bosschart.txt (top row first), so chart edits do not move these expectations.
        private static readonly object[] Shapes =
        {
            new object[] { "X", "20000002/02000020/00100100/00011000/00011000/00100100/02000020/20000002", "비숍 휩쓸기 2" },
            new object[] { "전체", "11111111/11111111/11111111/11111111/11111111/11111111/11111111/11111111", "퀸 찍기 1" },
            new object[] { "폰줄-7", "00000000/11111111/00000000/00000000/00000000/00000000/00000000/00000000", "룩 휩쓸기 1" },
            new object[] { "룩-e열강", "00002000/00002000/00002000/00002000/00002000/00002000/00002000/00002000", "룩 휩쓸기 1" },
            new object[] { "폰대각-bdfh", "00000000/00000000/10101010/00000000/00000000/00000000/00000000/00000000", "폰 찍기 4" },
            new object[] { "비숍-흰칸", "10101010/01010101/10101010/01010101/10101010/01010101/10101010/01010101", "폰 찍기 32" },
            new object[] { "킹손바닥강", "00000000/00000000/00000000/00111000/00121000/00111000/00000000/00000000", "킹 찍기 1" },
            new object[] { "킹고리강", "00000000/00000000/00000000/00222000/00202000/00222000/00000000/00000000", "킹 찍기 1" },
            new object[] { "나이트훈수+중심", "00000000/00000000/00101000/01000100/00010000/01000100/00101000/00000000", "나이트 찍기 9" },
            new object[] { "각잡은악수-오른", "00000000/00000000/00000000/00000000/00011111/00010000/00010000/00010000", "룩 휩쓸기 1" },
            new object[] { "오프닝1-e5", "00000000/00001000/00001000/00001000/00000000/00000000/00000000/00000000", "폰 찍기 3" },
            new object[] { "오프닝2-Nf6", "00001010/00010001/00000000/00010001/00001010/00000000/00000000/00000000", "나이트 찍기 8" },
            new object[] { "오프닝3-Bc5", "00000100/10001000/01010000/00000000/01010000/10001000/00000100/00000010", "비숍 휩쓸기 2" },
            new object[] { "오프닝4-캐슬링", "00001111/00000000/00000000/00000000/00000000/00000000/00000000/00000000", "킹 휩쓸기 1" },
            new object[] { "핀강", "00000000/00000000/00000000/00000000/00020000/00000000/00000000/00000000", "폰 찍기 1" },
            new object[] { "비숍X추적", "10000010/01000100/00101000/00010000/00101000/01000100/10000010/00000001", "비숍 휩쓸기 2" },
            new object[] { "핀+십자", "00000000/00000000/00000000/00010000/00111000/00010000/00000000/00000000", "킹 찍기 1" },
            new object[] { "핀+대각", "00000000/00000000/00000000/00101000/00010000/00101000/00000000/00000000", "킹 찍기 1" },
            new object[] { "챈슬러", "00010000/00010000/00212000/02010200/11111111/02010200/00212000/00010000", "룩 휩쓸기 2 · 나이트 찍기 8" },
            new object[] { "아치비숍", "00000001/10000010/01202100/02101200/00010000/02101200/01202100/10000010", "비숍 휩쓸기 2 · 나이트 찍기 8" },
            new object[] { "아마존", "00010001/10010010/01212100/02111200/11111111/02111200/01212100/10010010", "룩 휩쓸기 2 · 비숍 휩쓸기 2 · 나이트 찍기 8" },
            new object[] { "나이트라이더", "01000100/00000000/00101001/01000100/00020000/01000100/00101001/00000000", "나이트 찍기 13" },
            new object[] { "낙타얼룩말", "00000000/01101100/10000010/10000010/00020000/10000010/10000010/01101100", "나이트 찍기 17" },
            new object[] { "알필다바바", "00000000/00000000/01010100/00000000/01020100/00000000/01010100/00000000", "폰 찍기 9" },
            new object[] { "베롤리나-76", "00000000/10101010/01010101/00000000/00000000/00000000/00000000/00000000", "폰 찍기 8" },
            new object[] { "나이트라이더격자", "02000020/00020000/20000200/00200002/00002000/02000020/00020000/20000200", "나이트 찍기 13" },
            new object[] { "뒤치기", "00000000/00000000/20000002/20000002/20000002/20000002/22222222/22222222", "룩 휩쓸기 4" },
            new object[] { "고리-바깥", "11111111/10000001/10000001/10000001/10000001/10000001/10000001/11111111", "룩 휩쓸기 4" },
            new object[] { "고리-안강", "00000000/00000000/00222200/00200200/00200200/00222200/00000000/00000000", "룩 휩쓸기 4" },
            new object[] { "여러핀강", "00000000/00200020/00000000/20000200/00020000/00000002/02000200/00000000", "폰 찍기 8" },
            new object[] { "이중고리", "22222222/20000002/20222202/20200202/20200202/20222202/20000002/22222222", "룩 휩쓸기 8" },
            new object[] { "세로창살-홀", "20202020/20202020/20202020/20202020/20202020/20202020/20202020/20202020", "룩 휩쓸기 4" },
            new object[] { "가로창살", "00000000/22222222/00000000/22222222/00000000/22222222/00000000/22222222", "룩 휩쓸기 4" },
            new object[] { "나선", "22222222/00000002/02222202/02000202/02022202/02000002/02222222/00000000", "룩 휩쓸기 1" },
            new object[] { "반쪽-왼", "22220000/22220000/22220000/22220000/22220000/22220000/22220000/22220000", "퀸 찍기 1" },
            new object[] { "깨진판-가", "01000100/20010002/00200100/01002000/10000021/00210000/02000102/10020010", "폰 찍기 20" },
            new object[] { "깨진판-나", "00100010/10002000/00010001/02000200/00201000/10000012/00020100/01000001", "폰 찍기 17" },
            new object[] { "외길", "22222222/22222222/22222222/22222222/00000000/22222222/22222222/22222222", "퀸 찍기 1" },
            new object[] { "가운데폭발", "00000000/02222220/02222220/02222220/02222220/02222220/02222220/00000000", "퀸 찍기 1" },
            new object[] { "블록체커-가", "22002200/22002200/00220022/00220022/22002200/22002200/00220022/00220022", "킹 찍기 8" },
            new object[] { "구석", "22200222/22200222/22200222/00000000/00000000/22200222/22200222/22200222", "킹 찍기 4" },
        };

        private static ulong Cells(string drawing)
        {
            var rows = drawing.Split('/');
            ulong cells = 0;
            for (int r = 0; r < 8; r++) for (int x = 0; x < 8; x++) if (rows[r][x] != '0') cells |= 1UL << ((7 - r) * 8 + x);
            return cells;
        }
        private static List<PieceStrike> Plan(string drawing) => PieceStrikePlanner.Plan(Cells(drawing));
        private static List<(float x, float y)> Path(PieceStrike s) => s.Path.Select(p => (p.X, p.Y)).ToList();

        [TestCaseSource(nameof(Shapes))]
        public void ChartShapesGetTheAgreedPieces(string name, string drawing, string expected)
            => Assert.That(PieceStrikePlanner.Describe(Plan(drawing)), Is.EqualTo(expected), name);

        [Test] public void EveryCellOfAPerCellRuleGetsItsOwnPiece()
        {
            // Rule 7: the knight's centre gets a knight too.
            var knights = Plan("00000000/00000000/00101000/01000100/00010000/01000100/00101000/00000000");
            Assert.That(knights.Select(s => (s.Path[0].X, s.Path[0].Y)), Does.Contain((3f, 3f)));
            Assert.That(knights.All(s => s.Motion == StrikeMotion.Slam && s.Path.Count == 1), Is.True);
        }

        [Test] public void ParallelLinesAlternateDirection()
        {
            var bars = Plan("20202020/20202020/20202020/20202020/20202020/20202020/20202020/20202020");
            Assert.That(bars.Select(s => (s.Path.First().Y, s.Path.Last().Y)), Is.EqualTo(new[] { (7f, 0f), (0f, 7f), (7f, 0f), (0f, 7f) }),
                "columns from the left: down, up, down, up");
            var rows = Plan("00000000/22222222/00000000/22222222/00000000/22222222/00000000/22222222");
            Assert.That(rows.Select(s => (s.Path.First().X, s.Path.Last().X)), Is.EqualTo(new[] { (0f, 7f), (7f, 0f), (0f, 7f), (7f, 0f) }),
                "rows from the top: right, left, right, left");
        }

        [Test] public void RingRooksRunClockwiseFromTheCorners()
        {
            var ring = Plan("11111111/10000001/10000001/10000001/10000001/10000001/10000001/11111111");
            Assert.That(ring.Select(s => (Path(s).First(), Path(s).Last())), Is.EqualTo(new[] {
                ((0f, 7f), (7f, 7f)), ((7f, 7f), (7f, 0f)), ((7f, 0f), (0f, 0f)), ((0f, 0f), (0f, 7f)) }));
        }

        [Test] public void SweepsFollowTheirPath()
        {
            var bent = Plan("00000000/00000000/00000000/00000000/00011111/00010000/00010000/00010000").Single();
            Assert.That(Path(bent).First(), Is.EqualTo((7f, 3f)), "from the upper end");
            Assert.That(Path(bent).Last(), Is.EqualTo((3f, 0f)));
            Assert.That(bent.Path.Count, Is.EqualTo(8), "through the turn");
            var spiral = Plan("22222222/00000002/02222202/02000202/02022202/02000002/02222222/00000000").Single();
            Assert.That(Path(spiral), Is.EqualTo(Enumerable.Range(0, 8).Select(x => ((float)x, 7f)).ToList()), "only its longest straight, the top row");
            var castle = Plan("00001111/00000000/00000000/00000000/00000000/00000000/00000000/00000000").Single();
            Assert.That(castle.Piece, Is.EqualTo(ChessPiece.King));
            Assert.That(castle.Motion, Is.EqualTo(StrikeMotion.Sweep));
            var bc5 = Plan("00000100/10001000/01010000/00000000/01010000/10001000/00000100/00000010");
            Assert.That(bc5.All(s => s.Path.Contains(new BoardPoint(2, 4))), Is.True, "both bishops pass the empty centre");
        }

        [Test] public void SlamsLandOnCentres()
        {
            Assert.That(Plan("00000000/00000000/00000000/00111000/00121000/00111000/00000000/00000000").Single().Path[0], Is.EqualTo(new BoardPoint(3, 3)));
            Assert.That(Plan("22220000/22220000/22220000/22220000/22220000/22220000/22220000/22220000").Single().Path[0], Is.EqualTo(new BoardPoint(1.5f, 3.5f)));
            var blocks = Plan("22002200/22002200/00220022/00220022/22002200/22002200/00220022/00220022");
            Assert.That(blocks.Select(s => s.Path[0]), Does.Contain(new BoardPoint(.5f, 6.5f)));
        }

        [Test] public void OverridesKeepPathsOrSpreadOverCells()
        {
            var ring = Plan("11111111/10000001/10000001/10000001/10000001/10000001/10000001/11111111");
            ulong cells = Cells("11111111/10000001/10000001/10000001/10000001/10000001/10000001/11111111");
            Assert.That(PieceStrikePlanner.Override(ring, cells, ChessPiece.Bishop).Select(Path), Is.EqualTo(ring.Select(Path)));
            Assert.That(PieceStrikePlanner.Override(ring, cells, ChessPiece.Pawn).Count, Is.EqualTo(28));
            Assert.That(PieceStrikePlanner.Override(ring, cells, ChessPiece.Queen).Single().Path[0], Is.EqualTo(new BoardPoint(3.5f, 3.5f)));
        }

        [Test] public void PlacedStrikesDropWhatLeavesTheBoard()
        {
            var row = Plan("00000000/00000000/00000000/11111111/00000000/00000000/00000000/00000000");
            Assert.That(PieceStrikePlanner.Place(row, 3, 0).Single().Path.Count, Is.EqualTo(5));
            Assert.That(PieceStrikePlanner.Place(row, 0, 9), Is.Empty);
        }
    }
}
