using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StringAsASword.Combat
{
    /// <summary>How a summoned piece hits: lands on one point, or runs along a path.</summary>
    public enum StrikeMotion { Slam, Sweep }

    /// <summary>A point on the board in cell units: (0,0) is the bottom-left cell's centre, (7,7) the top-right one's. Block centres fall between cells.</summary>
    public readonly struct BoardPoint : IEquatable<BoardPoint>
    {
        public readonly float X, Y;
        public BoardPoint(float x, float y) { X = x; Y = y; }
        public bool OnBoard => X > -.01f && X < 7.01f && Y > -.01f && Y < 7.01f;
        public bool Equals(BoardPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is BoardPoint p && Equals(p);
        public override int GetHashCode() => X.GetHashCode() * 31 + Y.GetHashCode();
        /// <summary>Same convention as the drawing: (row from the top, column from the left), 1-based.</summary>
        public override string ToString() => $"({8 - Y:0.#},{X + 1:0.#})";
    }

    /// <summary>One summoned piece's part in an attack (presentation only: the cells and the judgement come from the shape).</summary>
    public sealed class PieceStrike
    {
        public ChessPiece Piece { get; }
        public StrikeMotion Motion { get; }
        /// <summary>Slam: the single landing point. Sweep: the cells in travel order.</summary>
        public IReadOnlyList<BoardPoint> Path { get; }
        /// <summary>The rule that chose it (Docs/BossChart.md, 기물 규칙), or "지정" for a piece= override.</summary>
        public string Rule { get; }
        public PieceStrike(ChessPiece piece, StrikeMotion motion, IReadOnlyList<BoardPoint> path, string rule)
        { Piece = piece; Motion = motion; Path = path; Rule = rule; }
        public PieceStrike WithPiece(ChessPiece piece, string rule) => new PieceStrike(piece, Motion, Path, rule);
    }

    /// <summary>
    /// Picks the chess pieces a boss chart shape summons, from the shape alone, so a new or edited shape gets pieces without
    /// anyone assigning them. The rules (agreed 2026-11-28) are listed in Docs/BossChart.md under 기물 규칙, numbered as here.
    /// A chart can still name the piece itself with piece= on the attack or the shape line.
    /// </summary>
    public static class PieceStrikePlanner
    {
        // ── Entry points ──

        /// <summary>The pieces for a drawn shape (cells = 1s and 2s), in drawing coordinates.</summary>
        public static List<PieceStrike> Plan(ulong cells)
        {
            var strikes = new List<PieceStrike>();
            if (cells == 0) return strikes;
            // Rule 3 / 18: a wide solid area. A queen-and-knight star (amazon) is solid around its centre too, but is lines and knights.
            int star = StarCenter(cells);
            if (HasSolidSquare(cells, 4) && star < 0)
            { strikes.Add(Slam(ChessPiece.Queen, Centroid(cells), "3·18 넓은 장판")); return strikes; }
            // Rules 7, 13: lines through one centre plus knight jumps from it (chancellor, archbishop, amazon, knight with its centre).
            // Each line of four or more is a rook or bishop; every other cell, the centre included, gets a knight.
            if (star >= 0) return Star(cells, star % 8, star / 8);
            // Rule 6 / 9: a full 3x3, or a + or X (five or more cells) inside one 3x3.
            var (x0, y0, x1, y1) = Bounds(cells);
            if (x1 - x0 < 3 && y1 - y0 < 3 && Count(cells) >= 5)
            { strikes.Add(Slam(ChessPiece.King, new BoardPoint((x0 + x1) * .5f, (y0 + y1) * .5f), "6·9 3x3")); return strikes; }
            var components = Components(cells);
            // Rule 19 (and #74 구석): separate solid 2x2 or 3x3 blocks, a king on each block's centre.
            if (components.Count >= 2 && components.All(IsSolidBlock))
            {
                foreach (var block in Ordered(components))
                {
                    var (bx0, by0, bx1, by1) = Bounds(block);
                    strikes.Add(Slam(ChessPiece.King, new BoardPoint((bx0 + bx1) * .5f, (by0 + by1) * .5f), "19 블록"));
                }
                return strikes;
            }
            // Rules 7, 13: knight jumps from one centre (camel and zebra too) or a nightrider lattice, a knight on every cell.
            if (IsKnightFamily(cells))
            {
                foreach (var (x, y) in CellsOf(cells)) strikes.Add(Slam(ChessPiece.Knight, new BoardPoint(x, y), "7·13 나이트 칸"));
                return strikes;
            }
            ulong rest = cells;
            foreach (var component in Ordered(components))
            {
                // Rule 15: a rectangle's outline, four rooks from its corners, clockwise.
                if (IsRing(component)) { strikes.AddRange(Ring(component)); rest &= ~component; continue; }
                // Rule 10 / 17: a bent line one cell wide. Few turns: one rook rides it. A spiral: one rook on its longest straight.
                var path = BentPath(component);
                if (path != null)
                {
                    int turns = Turns(path);
                    if (turns <= 2) strikes.Add(Sweep(ChessPiece.Rook, path, "10 꺾인 줄"));
                    else strikes.Add(Sweep(ChessPiece.Rook, LongestStraight(path), "17 나선"));
                    rest &= ~component;
                }
            }
            // Rules 1, 2, 8, 12, 13, 15: straight lines of four or more cells. Rows and columns are rooks, diagonals bishops.
            var lines = Lines(rest, cells);
            ulong covered = 0;
            foreach (var line in lines) foreach (var c in line.Cells) covered |= Bit(c.x, c.y);
            ulong left = rest & ~covered;
            if (strikes.Count == 0 && lines.Count == 1 && lines[0].Axis && left == 0 && lines[0].Span < 8)
                strikes.Add(Sweep(ChessPiece.King, lines[0].Path, "12 4칸 이상 줄"));
            else strikes.AddRange(LineStrikes(lines));
            // Rules 4, 5, 7, 11, 14, 16: the cells left over, one piece each. Knights when they are knight jumps, pawns otherwise.
            if (left != 0)
            {
                bool knights = IsKnightFamily(left);
                foreach (var (x, y) in CellsOf(left))
                    strikes.Add(Slam(knights ? ChessPiece.Knight : ChessPiece.Pawn, new BoardPoint(x, y), knights ? "7·13 나이트 칸" : "4·5·11·16 칸"));
            }
            return strikes;
        }

        /// <summary>
        /// A piece= override. Pawns and knights land on every cell, a queen on the centre; a rook, bishop or king takes over
        /// what the rules planned (each sweep or slam keeps its path). None summons nothing.
        /// </summary>
        public static List<PieceStrike> Override(IReadOnlyList<PieceStrike> planned, ulong cells, ChessPiece piece)
        {
            const string rule = "지정";
            switch (piece)
            {
                case ChessPiece.Pawn:
                case ChessPiece.Knight:
                    return CellsOf(cells).Select(c => Slam(piece, new BoardPoint(c.x, c.y), rule)).ToList();
                case ChessPiece.Queen:
                    return new List<PieceStrike> { Slam(piece, Centroid(cells), rule) };
                default:
                    return planned.Select(s => s.WithPiece(piece, rule)).ToList();
            }
        }

        /// <summary>Moves strikes by (dx, dy) for an anchored shape and drops what leaves the board.</summary>
        public static List<PieceStrike> Place(IReadOnlyList<PieceStrike> strikes, int dx, int dy)
        {
            var placed = new List<PieceStrike>();
            foreach (var s in strikes)
            {
                var path = s.Path.Select(p => new BoardPoint(p.X + dx, p.Y + dy)).Where(p => p.OnBoard).ToList();
                if (path.Count > 0) placed.Add(new PieceStrike(s.Piece, path.Count == 1 ? StrikeMotion.Slam : s.Motion, path, s.Rule));
            }
            return placed;
        }

        private static readonly string[] PieceNames = { "폰", "룩", "비숍", "나이트", "킹", "퀸" };
        public static string Name(ChessPiece piece) => PieceNames[(int)piece];

        /// <summary>"룩 휩쓸기 2 · 나이트 찍기 8", for the validation report.</summary>
        public static string Describe(IReadOnlyList<PieceStrike> strikes)
        {
            if (strikes.Count == 0) return "없음";
            var text = new StringBuilder();
            foreach (var group in strikes.GroupBy(s => (s.Piece, s.Motion)))
            {
                if (text.Length > 0) text.Append(" · ");
                text.Append(Name(group.Key.Piece)).Append(group.Key.Motion == StrikeMotion.Sweep ? " 휩쓸기 " : " 찍기 ").Append(group.Count());
            }
            return text.ToString();
        }

        // ── Shapes ──

        private static ulong Bit(int x, int y) => 1UL << (y * 8 + x);
        private static bool In(ulong m, int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8 && (m & Bit(x, y)) != 0;
        private static int Count(ulong m) { int n = 0; for (; m != 0; m &= m - 1) n++; return n; }
        /// <summary>Top row first, left to right: the order a reader of the drawing meets them.</summary>
        private static IEnumerable<(int x, int y)> CellsOf(ulong m)
        {
            for (int y = 7; y >= 0; y--) for (int x = 0; x < 8; x++) if (In(m, x, y)) yield return (x, y);
        }
        private static (int x0, int y0, int x1, int y1) Bounds(ulong m)
        {
            int x0 = 8, y0 = 8, x1 = -1, y1 = -1;
            foreach (var (x, y) in CellsOf(m)) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
            return (x0, y0, x1, y1);
        }
        private static BoardPoint Centroid(ulong m)
        {
            float sx = 0, sy = 0; int n = 0;
            foreach (var (x, y) in CellsOf(m)) { sx += x; sy += y; n++; }
            return new BoardPoint(sx / n, sy / n);
        }
        private static bool HasSolidSquare(ulong m, int size)
        {
            for (int y = 0; y + size <= 8; y++)
                for (int x = 0; x + size <= 8; x++)
                {
                    bool solid = true;
                    for (int dy = 0; dy < size && solid; dy++) for (int dx = 0; dx < size && solid; dx++) solid = In(m, x + dx, y + dy);
                    if (solid) return true;
                }
            return false;
        }
        private static readonly (int dx, int dy)[] KnightJumps = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
        /// <summary>A centre (y * 8 + x) whose eight knight jumps are all attacked, every other cell being on a rook or bishop
        /// line through it. -1 when there is none.</summary>
        private static int StarCenter(ulong m)
        {
            for (int c = 0; c < 64; c++)
            {
                int cx = c % 8, cy = c / 8;
                if (!KnightJumps.All(j => In(m, cx + j.dx, cy + j.dy))) continue;
                bool ok = true;
                foreach (var (x, y) in CellsOf(m))
                {
                    int dx = Math.Abs(x - cx), dy = Math.Abs(y - cy);
                    if (!(dx == 0 || dy == 0 || dx == dy || dx + dy == 3)) { ok = false; break; }
                }
                if (ok) return c;
            }
            return -1;
        }
        private static List<PieceStrike> Star(ulong m, int cx, int cy)
        {
            var lines = new List<Line>();
            foreach (var dir in Directions)
            {
                // The whole board line through the centre, trimmed to its first and last attacked cells.
                var cells = new List<(int x, int y)>();
                int x = cx, y = cy;
                while (x - dir.dx >= 0 && x - dir.dx < 8 && y - dir.dy >= 0 && y - dir.dy < 8) { x -= dir.dx; y -= dir.dy; }
                for (; x >= 0 && x < 8 && y >= 0 && y < 8; x += dir.dx, y += dir.dy) if (In(m, x, y)) cells.Add((x, y));
                if (cells.Count >= 4) lines.Add(new Line { Cells = cells, Direction = dir });
            }
            var strikes = LineStrikes(lines).ToList();
            ulong covered = 0;
            foreach (var line in lines) foreach (var c in line.Cells) covered |= Bit(c.x, c.y);
            foreach (var (x, y) in CellsOf(m & ~covered)) strikes.Add(Slam(ChessPiece.Knight, new BoardPoint(x, y), "7·13 나이트 칸"));
            return strikes;
        }
        /// <summary>Groups of cells joined by edges (not corners).</summary>
        private static List<ulong> Components(ulong m)
        {
            var result = new List<ulong>();
            ulong left = m;
            while (left != 0)
            {
                int start = 0; while ((left & (1UL << start)) == 0) start++;
                ulong component = 0; var stack = new Stack<int>(); stack.Push(start); left &= ~(1UL << start);
                while (stack.Count > 0)
                {
                    int i = stack.Pop(); component |= 1UL << i; int x = i % 8, y = i / 8;
                    foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                        if (In(left, nx, ny)) { left &= ~Bit(nx, ny); stack.Push(ny * 8 + nx); }
                }
                result.Add(component);
            }
            return result;
        }
        private static IEnumerable<ulong> Ordered(IEnumerable<ulong> components)
            => components.OrderByDescending(c => Bounds(c).y1).ThenBy(c => Bounds(c).x0);
        private static bool IsSolidBlock(ulong component)
        {
            var (x0, y0, x1, y1) = Bounds(component);
            int w = x1 - x0 + 1, h = y1 - y0 + 1;
            return w == h && (w == 2 || w == 3) && Count(component) == w * h;
        }
        private static bool IsRing(ulong component)
        {
            var (x0, y0, x1, y1) = Bounds(component);
            if (x1 - x0 < 2 || y1 - y0 < 2) return false;
            ulong outline = 0;
            for (int x = x0; x <= x1; x++) { outline |= Bit(x, y0) | Bit(x, y1); }
            for (int y = y0; y <= y1; y++) { outline |= Bit(x0, y) | Bit(x1, y); }
            return component == outline;
        }
        private static IEnumerable<PieceStrike> Ring(ulong component)
        {
            var (x0, y0, x1, y1) = Bounds(component);
            const string rule = "15 고리";
            // Clockwise as drawn: top-left to top-right, down the right, back along the bottom, up the left.
            yield return Sweep(ChessPiece.Rook, Span((x0, y1), (x1, y1)), rule);
            yield return Sweep(ChessPiece.Rook, Span((x1, y1), (x1, y0)), rule);
            yield return Sweep(ChessPiece.Rook, Span((x1, y0), (x0, y0)), rule);
            yield return Sweep(ChessPiece.Rook, Span((x0, y0), (x0, y1)), rule);
        }
        /// <summary>The cells of a one-cell-wide path with at least one turn, from its upper (then left) end. Null when it is not one.</summary>
        private static List<(int x, int y)> BentPath(ulong component)
        {
            if (Count(component) < 4) return null;
            var ends = new List<(int x, int y)>();
            foreach (var (x, y) in CellsOf(component))
            {
                int degree = Neighbours(component, x, y).Count();
                if (degree > 2) return null;
                if (degree == 1) ends.Add((x, y));
            }
            if (ends.Count != 2) return null;
            var path = new List<(int x, int y)> { ends[0] };
            ulong seen = Bit(ends[0].x, ends[0].y);
            while (true)
            {
                var (x, y) = path[path.Count - 1];
                var next = Neighbours(component, x, y).Where(n => (seen & Bit(n.x, n.y)) == 0).ToList();
                if (next.Count == 0) break;
                path.Add(next[0]); seen |= Bit(next[0].x, next[0].y);
            }
            if (path.Count != Count(component) || Turns(path) == 0) return null;
            return path;
        }
        private static IEnumerable<(int x, int y)> Neighbours(ulong m, int x, int y)
        {
            foreach (var n in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) }) if (In(m, n.Item1, n.Item2)) yield return n;
        }
        private static int Turns(List<(int x, int y)> path)
        {
            int turns = 0;
            for (int i = 2; i < path.Count; i++)
                if (path[i].x - path[i - 1].x != path[i - 1].x - path[i - 2].x || path[i].y - path[i - 1].y != path[i - 1].y - path[i - 2].y) turns++;
            return turns;
        }
        private static List<(int x, int y)> LongestStraight(List<(int x, int y)> path)
        {
            int bestStart = 0, bestLength = 1, start = 0;
            for (int i = 1; i < path.Count; i++)
            {
                bool straight = i < 2 || (path[i].x - path[i - 1].x == path[i - 1].x - path[i - 2].x && path[i].y - path[i - 1].y == path[i - 1].y - path[i - 2].y);
                if (!straight) start = i - 1;
                if (i - start + 1 > bestLength) { bestLength = i - start + 1; bestStart = start; }
            }
            return path.GetRange(bestStart, bestLength);
        }

        // ── Lines ──

        private sealed class Line
        {
            public List<(int x, int y)> Cells;
            public (int dx, int dy) Direction;
            public bool Axis => Direction.dx == 0 || Direction.dy == 0;
            /// <summary>From the first cell to the last, gap included.</summary>
            public List<(int x, int y)> Path => Span(Cells[0], Cells[Cells.Count - 1]);
            public int Span => Math.Max(Math.Abs(Cells[Cells.Count - 1].x - Cells[0].x), Math.Abs(Cells[Cells.Count - 1].y - Cells[0].y)) + 1;
        }
        // Rows left to right, columns and diagonals top to bottom.
        private static readonly (int dx, int dy)[] Directions = { (1, 0), (0, -1), (1, -1), (-1, -1) };
        private static List<(int x, int y)> Span((int x, int y) from, (int x, int y) to)
        {
            int dx = Math.Sign(to.x - from.x), dy = Math.Sign(to.y - from.y);
            var cells = new List<(int x, int y)> { from };
            var c = from;
            while (c != to) { c = (c.x + dx, c.y + dy); cells.Add(c); }
            return cells;
        }
        /// <summary>
        /// Greedy: the straight run (four or more cells, one single-cell gap allowed) that covers the most uncovered cells,
        /// until no run adds three. A diagonal must be thin: a checkerboard is made of diagonals but reads as single cells (rule 4).
        /// </summary>
        private static List<Line> Lines(ulong rest, ulong all)
        {
            var candidates = new List<Line>();
            foreach (var dir in Directions)
                foreach (var (sx, sy) in CellsOf(rest))
                {
                    if (In(rest, sx - dir.dx, sy - dir.dy)) continue; // not the start of a run
                    var cells = new List<(int x, int y)> { (sx, sy) };
                    int x = sx, y = sy; bool gapped = false;
                    while (true)
                    {
                        if (In(rest, x + dir.dx, y + dir.dy)) { x += dir.dx; y += dir.dy; }
                        else if (!gapped && In(rest, x + 2 * dir.dx, y + 2 * dir.dy) && Crossed(all, x + dir.dx, y + dir.dy, dir))
                        { x += 2 * dir.dx; y += 2 * dir.dy; gapped = true; }
                        else break;
                        cells.Add((x, y));
                    }
                    if (cells.Count < 4) continue;
                    var line = new Line { Cells = cells, Direction = dir };
                    // A bishop's diagonal is five or more long; four scattered cells can touch corner to corner by chance.
                    if (!line.Axis && (line.Span < 5 || !Thin(line, all))) continue;
                    candidates.Add(line);
                }
            var chosen = new List<Line>();
            ulong covered = 0;
            while (true)
            {
                Line best = null; int bestNew = 0;
                foreach (var line in candidates)
                {
                    int fresh = line.Cells.Count(c => (covered & Bit(c.x, c.y)) == 0);
                    if (fresh > bestNew || fresh == bestNew && best != null && line.Axis && !best.Axis) { best = line; bestNew = fresh; }
                }
                if (best == null || bestNew < 3) break;
                chosen.Add(best); candidates.Remove(best);
                foreach (var c in best.Cells) covered |= Bit(c.x, c.y);
            }
            return chosen;
        }
        /// <summary>A one-cell gap is bridged only where another line crosses it (the empty centre of an X, like 오프닝3-Bc5).</summary>
        private static bool Crossed(ulong all, int x, int y, (int dx, int dy) dir)
        {
            foreach (var (cx, cy) in Directions)
                if ((cx, cy) != dir && (-cx, -cy) != dir && In(all, x + cx, y + cy) && In(all, x - cx, y - cy)) return true;
            return false;
        }
        private static bool Thin(Line line, ulong all)
        {
            int px = line.Direction.dx, py = -line.Direction.dy; // the other diagonal
            int thick = line.Cells.Count(c => In(all, c.x + px, c.y + py) || In(all, c.x - px, c.y - py));
            return thick <= 2; // the crossing of an X
        }
        /// <summary>Parallel lines alternate direction (rule 2): rows from the top, columns from the left.</summary>
        private static IEnumerable<PieceStrike> LineStrikes(List<Line> lines)
        {
            var rows = lines.Where(l => l.Direction.dy == 0).OrderByDescending(l => l.Cells[0].y).ToList();
            var columns = lines.Where(l => l.Direction.dx == 0).OrderBy(l => l.Cells[0].x).ToList();
            var diagonals = lines.Where(l => !l.Axis).ToList();
            string rule = lines.Count == 1 ? "1 한 줄" : "2·15 여러 줄";
            for (int i = 0; i < rows.Count; i++) yield return Sweep(ChessPiece.Rook, Alternate(rows[i].Path, i), rule);
            for (int i = 0; i < columns.Count; i++) yield return Sweep(ChessPiece.Rook, Alternate(columns[i].Path, i), rule);
            foreach (var line in diagonals) yield return Sweep(ChessPiece.Bishop, line.Path, "8 대각선");
        }
        private static List<(int x, int y)> Alternate(List<(int x, int y)> path, int index)
        {
            if (index % 2 == 1) path.Reverse();
            return path;
        }

        // ── Knights ──

        /// <summary>
        /// Knight cells (rules 7, 13): jumps from one centre (knight, nightrider, camel, zebra; six or more cells),
        /// or a set that knight moves join together (four or more cells, like the nightrider lattice).
        /// </summary>
        private static bool IsKnightFamily(ulong m)
        {
            int n = Count(m);
            if (n < 4) return false;
            var cells = CellsOf(m).ToList();
            for (int c = 0; c < 64; c++)
            {
                int cx = c % 8, cy = c / 8, jumps = 0; bool ok = true;
                foreach (var (x, y) in cells)
                {
                    if (x == cx && y == cy) continue;
                    int a = Math.Min(Math.Abs(x - cx), Math.Abs(y - cy)), b = Math.Max(Math.Abs(x - cx), Math.Abs(y - cy));
                    if (a == 1 && b == 2 || a == 2 && b == 4 || a == 1 && b == 3 || a == 2 && b == 3) jumps++; else { ok = false; break; }
                }
                if (ok && jumps >= 6) return true;
            }
            // A nightrider lattice: scattered cells (none touching another, so bars and rows do not count), every cell on a
            // run of three or more knight jumps in one direction.
            foreach (var (x, y) in cells)
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && In(m, x + dx, y + dy)) return false;
            ulong onRuns = 0;
            foreach (var (sx, sy) in cells)
                foreach (var (dx, dy) in KnightJumps)
                {
                    if (In(m, sx - dx, sy - dy)) continue;
                    ulong run = 0; int length = 0;
                    for (int x = sx, y = sy; In(m, x, y); x += dx, y += dy) { run |= Bit(x, y); length++; }
                    if (length >= 3) onRuns |= run;
                }
            return onRuns == m;
        }

        private static PieceStrike Slam(ChessPiece piece, BoardPoint at, string rule) => new PieceStrike(piece, StrikeMotion.Slam, new[] { at }, rule);
        private static PieceStrike Sweep(ChessPiece piece, List<(int x, int y)> cells, string rule)
            => new PieceStrike(piece, StrikeMotion.Sweep, cells.Select(c => new BoardPoint(c.x, c.y)).ToList(), rule);
    }
}
