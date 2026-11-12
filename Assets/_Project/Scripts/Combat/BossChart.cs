using System;
using System.Collections.Generic;
using System.Globalization;
using StringAsASword.Board;

namespace StringAsASword.Combat
{
    /// <summary>A boss chart line the parser could not read. Line is 1-based.</summary>
    public sealed class BossChartException : ArgumentException
    {
        public int Line { get; }
        public BossChartException(int line, string message) : base($"{line}번째 줄: {message}") { Line = line; }
    }
    public sealed class BossChartSection
    {
        public string Name { get; }
        public int From { get; }
        public int To { get; }
        public BossChartSection(string name, int from, int to) { Name = name; From = from; To = to; }
    }
    /// <summary>An 8x8 drawing: 0 = no attack, 1 = normal (guard or dodge), 2 = strong (guard breaks, must dodge).</summary>
    public sealed class BossChartShape
    {
        public string Name { get; }
        public ulong Normal { get; }
        public ulong Strong { get; }
        public bool HasAnchor { get; }
        public GridPosition Anchor { get; }
        public int Line { get; }
        /// <summary>A board full of 1 has no safe cell: it is guarded, never dodged (the FullBoard rule).</summary>
        public bool GuardOnly => !HasAnchor && Normal == ulong.MaxValue;
        public BossChartShape(string name, ulong normal, ulong strong, bool hasAnchor, GridPosition anchor, int line)
        { Name = name; Normal = normal; Strong = strong; HasAnchor = hasAnchor; Anchor = anchor; Line = line; }
    }
    public sealed class BossChartAttack
    {
        public int Note { get; }
        public BossChartShape Shape { get; }
        public int Warning { get; }
        public int Hits { get; }
        public int Every { get; }
        public int MinPhase { get; }
        public int Line { get; }
        /// <summary>The note whose close discloses the attack.</summary>
        public int Publish => Note - Warning;
        public int LastHit => Note + (Hits - 1) * Every;
        public BossChartAttack(int note, BossChartShape shape, int warning, int hits, int every, int minPhase, int line)
        { Note = note; Shape = shape; Warning = warning; Hits = hits; Every = every; MinPhase = minPhase; Line = line; }
    }

    /// <summary>
    /// The boss's attacks written by hand against the song's note numbers. See Docs/BossChart.md for the format.
    /// Drawings are written top row first: the first line is board row y = 7, the player's start row.
    /// </summary>
    public sealed class BossChart
    {
        private readonly List<BossChartSection> sections = new List<BossChartSection>();
        private readonly Dictionary<string, BossChartShape> shapes = new Dictionary<string, BossChartShape>();
        private readonly List<BossChartAttack> attacks = new List<BossChartAttack>();
        public IReadOnlyList<BossChartSection> Sections => sections;
        public IReadOnlyDictionary<string, BossChartShape> Shapes => shapes;
        /// <summary>Ordered by publish note, then by line.</summary>
        public IReadOnlyList<BossChartAttack> Attacks => attacks;

        public string SectionAt(int note)
        {
            foreach (var s in sections) if (note >= s.From && note <= s.To) return s.Name;
            return "";
        }

        /// <summary>Shape cells on the board. Anchored shapes move so the anchor sits on the player; cells off the board drop.</summary>
        public static (ulong normal, ulong strong) Cells(BossChartShape shape, GridPosition player)
        {
            if (!shape.HasAnchor) return (shape.Normal, shape.Strong);
            int dx = player.X - shape.Anchor.X, dy = player.Y - shape.Anchor.Y;
            return (Shift(shape.Normal, dx, dy), Shift(shape.Strong, dx, dy));
        }
        private static ulong Shift(ulong mask, int dx, int dy)
        {
            ulong result = 0;
            for (int cell = 0; cell < 64; cell++)
            {
                if ((mask & (1UL << cell)) == 0) continue;
                int x = cell % 8 + dx, y = cell / 8 + dy;
                if (x >= 0 && x < 8 && y >= 0 && y < 8) result |= 1UL << (y * 8 + x);
            }
            return result;
        }

        public static BossChart Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var chart = new BossChart();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var pending = new List<(int line, string[] tokens)>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = Strip(lines[i]);
                if (line.Length == 0) continue;
                var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens[0] == "section") chart.sections.Add(ParseSection(tokens, i + 1));
                else if (tokens[0] == "shape") i = chart.ParseShape(tokens, lines, i);
                else if (char.IsDigit(tokens[0][0]) || tokens[0][0] == '-') pending.Add((i + 1, tokens));
                else throw new BossChartException(i + 1, $"알 수 없는 줄 '{line}'. section, shape, 또는 노트 번호로 시작해야 해요.");
            }
            // Attacks may name a shape drawn further down the file.
            foreach (var (line, tokens) in pending) chart.attacks.Add(chart.ParseAttack(tokens, line));
            chart.attacks.Sort((a, b) => a.Publish != b.Publish ? a.Publish.CompareTo(b.Publish) : a.Line.CompareTo(b.Line));
            return chart;
        }
        private static string Strip(string line)
        {
            int comment = line.IndexOf('#');
            return (comment >= 0 ? line.Substring(0, comment) : line).Trim();
        }
        private static int Int(string text, int line, string what)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new BossChartException(line, $"{what}에는 정수를 써야 해요: '{text}'");
            return value;
        }
        private static BossChartSection ParseSection(string[] tokens, int line)
        {
            if (tokens.Length != 3) throw new BossChartException(line, "형식은 'section 이름 시작-끝'이에요.");
            var range = tokens[2].Split('-');
            if (range.Length != 2) throw new BossChartException(line, $"구간 범위는 '시작-끝' 형식이에요: '{tokens[2]}'");
            int from = Int(range[0], line, "구간 시작"), to = Int(range[1], line, "구간 끝");
            if (from < 0 || to < from) throw new BossChartException(line, $"구간 범위가 잘못됐어요: {from}-{to}");
            return new BossChartSection(tokens[1], from, to);
        }
        private int ParseShape(string[] tokens, string[] lines, int index)
        {
            int header = index + 1;
            if (tokens.Length < 2 || tokens.Length > 3) throw new BossChartException(header, "형식은 'shape 이름 [anchor=줄,칸]'이에요.");
            string name = tokens[1];
            if (shapes.ContainsKey(name)) throw new BossChartException(header, $"모양 '{name}'이 이미 있어요.");
            bool hasAnchor = false; var anchor = new GridPosition(0, 0);
            if (tokens.Length == 3)
            {
                if (!tokens[2].StartsWith("anchor=", StringComparison.Ordinal)) throw new BossChartException(header, $"알 수 없는 옵션 '{tokens[2]}'");
                var parts = tokens[2].Substring(7).Split(',');
                if (parts.Length != 2) throw new BossChartException(header, "anchor는 'anchor=줄,칸' 형식이에요.");
                int row = Int(parts[0], header, "anchor 줄"), column = Int(parts[1], header, "anchor 칸");
                if (row < 1 || row > 8 || column < 1 || column > 8) throw new BossChartException(header, "anchor는 1~8 사이여야 해요.");
                hasAnchor = true; anchor = new GridPosition(column - 1, 8 - row);
            }
            ulong normal = 0, strong = 0;
            int rows = 0, i = index;
            while (rows < 8)
            {
                if (++i >= lines.Length) throw new BossChartException(header, $"모양 '{name}'의 줄이 8개보다 적어요.");
                string row = Strip(lines[i]);
                if (row.Length == 0) continue;
                if (row.Length != 8) throw new BossChartException(i + 1, $"모양 '{name}'의 줄은 8칸이어야 해요: '{row}'");
                for (int x = 0; x < 8; x++)
                {
                    ulong bit = 1UL << ((7 - rows) * 8 + x);
                    if (row[x] == '1') normal |= bit;
                    else if (row[x] == '2') strong |= bit;
                    else if (row[x] != '0') throw new BossChartException(i + 1, $"모양 칸에는 0, 1, 2만 쓸 수 있어요: '{row[x]}'");
                }
                rows++;
            }
            if ((normal | strong) == 0) throw new BossChartException(header, $"모양 '{name}'에 공격 칸이 없어요.");
            if (strong == ulong.MaxValue) throw new BossChartException(header, $"모양 '{name}'은 판 전체가 2라서 막을 수도 피할 수도 없어요.");
            shapes.Add(name, new BossChartShape(name, normal, strong, hasAnchor, anchor, header));
            return i;
        }
        private BossChartAttack ParseAttack(string[] tokens, int line)
        {
            if (tokens.Length < 3) throw new BossChartException(line, "형식은 '노트 모양 경고 [hits=N every=N] [phase>=N]'이에요.");
            int note = Int(tokens[0], line, "노트");
            if (note < 0) throw new BossChartException(line, "노트는 0 이상이어야 해요.");
            if (!shapes.TryGetValue(tokens[1], out var shape)) throw new BossChartException(line, $"모르는 모양 '{tokens[1]}'");
            int warning = Int(tokens[2], line, "경고");
            if (warning < 1 || warning > RandomAttackSchedule.WarningCeiling)
                throw new BossChartException(line, $"경고는 1~{RandomAttackSchedule.WarningCeiling}노트예요: {warning}");
            int hits = 1, every = 1, minPhase = 1;
            for (int t = 3; t < tokens.Length; t++)
            {
                string option = tokens[t];
                if (option.StartsWith("hits=", StringComparison.Ordinal)) hits = Int(option.Substring(5), line, "hits");
                else if (option.StartsWith("every=", StringComparison.Ordinal)) every = Int(option.Substring(6), line, "every");
                else if (option.StartsWith("phase>=", StringComparison.Ordinal)) minPhase = Int(option.Substring(7), line, "phase");
                else throw new BossChartException(line, $"알 수 없는 옵션 '{option}'");
            }
            if (hits < 1 || hits > PatternDefinition.MaxHits) throw new BossChartException(line, $"hits는 1~{PatternDefinition.MaxHits}예요: {hits}");
            if (every < 1 || every > PatternDefinition.MaxHitSpacing) throw new BossChartException(line, $"every는 1~{PatternDefinition.MaxHitSpacing}예요: {every}");
            if (minPhase < 1 || minPhase > 4) throw new BossChartException(line, $"phase는 1~4예요: {minPhase}");
            return new BossChartAttack(note, shape, warning, hits, every, minPhase, line);
        }
    }
}
