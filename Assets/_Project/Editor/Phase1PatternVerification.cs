using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Plays every attack of a boss chart in real BattleSessions and checks it against the drawing: disclosed cells
    /// (anchored shapes moved and clipped), disclosure note, countdown, impact note, and the guard / wait / move outcome.
    /// The checks need no view, so EditMode tests run them too. Show() puts one moment on the live Play Mode view for
    /// screenshots. The chart text is injected into a settings copy; no stage asset is saved. See Docs/BossChart/phase1-verification.md.
    /// </summary>
    public static class Phase1PatternVerification
    {
        public const string ChartPath = "Assets/_Project/Data/Bosses/Phase1Verification.bosschart.txt";
        public const string CombosPath = "Assets/_Project/Data/Bosses/Phase1Combos.bosschart.txt";
        public const string ReportPath = "Docs/BossChart/phase1-ingame.txt";
        public const string Passed = "통과", Failed = "실패", NotApplicable = "해당 없음";
        public static readonly GridPosition Center = new GridPosition(3, 3);
        /// <summary>Where anchored shapes are disclosed: center d4, left edge a4, top row d8, corner h1.</summary>
        public static readonly GridPosition[] AnchorStarts = { new GridPosition(3, 3), new GridPosition(0, 3), new GridPosition(3, 7), new GridPosition(7, 0) };

        public enum Final { Wait, Guard, Move }
        public sealed class Probe
        {
            public string Shape, Scenario, Start, Target, Expected, Status, Detail = "";
            public int Note, Publish, Warning;
        }
        public sealed class Drawing
        {
            public string Name;
            public ulong Normal, Strong;
            public bool HasAnchor;
            public int AnchorX, AnchorY;
            public bool GuardOnly => !HasAnchor && Normal == ulong.MaxValue;
        }

        public static BattleSettings Prepare(BattleSettings baseSettings, string chartText)
        {
            var settings = baseSettings.Copy();
            settings.BossChartText = chartText;
            settings.SpawnInterval = 100000; // no noise pickups
            settings.PlayerMaxHp = 1000;     // walking through earlier attacks never ends the run
            return settings;
        }

        // ── Expectation, read straight from the drawing (independent of BossChart.Cells) ──
        public static Dictionary<string, Drawing> Drawings(string text)
        {
            var result = new Dictionary<string, Drawing>();
            var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => (l.IndexOf('#') >= 0 ? l.Substring(0, l.IndexOf('#')) : l).Trim()).ToArray();
            for (int i = 0; i < lines.Length; i++)
            {
                var tokens = lines[i].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 2 || tokens[0] != "shape") continue;
                var d = new Drawing { Name = tokens[1] };
                if (tokens.Length > 2)
                {
                    var rc = tokens[2].Substring("anchor=".Length).Split(',');
                    d.HasAnchor = true; d.AnchorX = int.Parse(rc[1]) - 1; d.AnchorY = 8 - int.Parse(rc[0]);
                }
                for (int row = 0; row < 8;)
                {
                    string r = lines[++i];
                    if (r.Length == 0) continue;
                    for (int x = 0; x < 8; x++)
                    {
                        ulong bit = 1UL << ((7 - row) * 8 + x);
                        if (r[x] == '1') d.Normal |= bit; else if (r[x] == '2') d.Strong |= bit;
                    }
                    row++;
                }
                result[d.Name] = d;
            }
            return result;
        }
        /// <summary>For each board cell, looks up the drawing cell that lands there; anything from outside the drawing is empty.</summary>
        public static (ulong normal, ulong strong) Expected(Drawing d, GridPosition player)
        {
            if (!d.HasAnchor) return (d.Normal, d.Strong);
            ulong normal = 0, strong = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int sx = x - (player.X - d.AnchorX), sy = y - (player.Y - d.AnchorY);
                if (sx < 0 || sx > 7 || sy < 0 || sy > 7) continue;
                ulong source = 1UL << (sy * 8 + sx), cell = 1UL << (y * 8 + x);
                if ((d.Normal & source) != 0) normal |= cell;
                if ((d.Strong & source) != 0) strong |= cell;
            }
            return (normal, strong);
        }

        // ── Probes ──
        public static List<Probe> Run(BattleSettings baseSettings, string chartText, string onlyShape = null)
        {
            var settings = Prepare(baseSettings, chartText);
            var drawings = Drawings(chartText);
            var probes = new List<Probe>();
            foreach (var a in settings.Chart.Attacks)
            {
                if (onlyShape != null && a.Shape.Name != onlyShape) continue;
                var d = drawings[a.Shape.Name];
                foreach (var origin in d.HasAnchor ? AnchorStarts : new[] { Center }) probes.AddRange(Plan(settings, a, d, origin));
            }
            return probes;
        }

        private static IEnumerable<Probe> Plan(BattleSettings settings, BossChartAttack a, Drawing d, GridPosition origin)
        {
            var (normal, strong) = Expected(d, origin);
            ulong danger = normal | strong;
            // Fixed shapes do not care where the player was at disclosure, so the player walks to the target first.
            // Anchored shapes are disclosed at the origin and the target has to be reached inside the warning.
            int reach = d.HasAnchor ? a.Warning - 1 : a.Publish + 1;
            string where = d.HasAnchor ? Cell(origin) + " 공개, " : "";
            var categories = new[] { ("1칸", normal), ("2칸", strong), ("안전칸", ~danger) };
            foreach (var (label, cells) in categories)
            {
                if (cells == 0) continue;
                var target = Nearest(origin, cells, reach);
                if (target == null)
                {
                    yield return new Probe { Shape = a.Shape.Name, Note = a.Note, Publish = a.Publish, Warning = a.Warning, Start = Cell(origin),
                        Target = "-", Scenario = where + label, Expected = "-", Status = NotApplicable, Detail = $"경고 {a.Warning}노트 안에 {label}에 갈 수 없음" };
                    continue;
                }
                var start = d.HasAnchor ? origin : target.Value;
                if (label == "1칸")
                {
                    yield return Play(settings, a, d, start, target.Value, Final.Guard, MoveDirection.Up, where + "1칸 가드", new[] { "guard-success" });
                    yield return Play(settings, a, d, start, target.Value, Final.Wait, MoveDirection.Up, where + "1칸 대기", new[] { "player-hit" });
                }
                else if (label == "2칸")
                {
                    yield return Play(settings, a, d, start, target.Value, Final.Guard, MoveDirection.Up, where + "2칸 가드", new[] { "guard-broken", "player-hit" });
                    yield return Play(settings, a, d, start, target.Value, Final.Wait, MoveDirection.Up, where + "2칸 대기", new[] { "player-hit" });
                }
                else yield return Play(settings, a, d, start, target.Value, Final.Wait, MoveDirection.Up, where + "안전칸 대기", new string[0]);
            }
            // Shapes that leave the player's own cell empty: stepping to each neighbour on the impact note.
            if (d.HasAnchor && a.Hits == 1 && (danger & Bit(origin)) == 0)
                foreach (MoveDirection direction in Enum.GetValues(typeof(MoveDirection)))
                {
                    if (!new BoardState().TryGetDestination(origin, direction, out var next)) continue;
                    bool hit = (danger & Bit(next)) != 0;
                    yield return Play(settings, a, d, origin, origin, Final.Move, direction, $"{where}발동 노트에 {Cell(next)}로 이동",
                        hit ? new[] { "player-hit" } : new string[0]);
                }
        }

        private static Dictionary<int, (PlayerActionKind kind, MoveDirection direction)> Inputs(BossChartAttack a, GridPosition start, GridPosition target,
            Final final, MoveDirection direction)
        {
            var plan = new Dictionary<int, (PlayerActionKind, MoveDirection)>();
            var walk = Path(Center, start);
            for (int i = 0; i < walk.Count; i++) plan[a.Publish - walk.Count + 1 + i] = (PlayerActionKind.Move, walk[i]);
            var step = Path(start, target);
            for (int i = 0; i < step.Count; i++) plan[a.Publish + 1 + i] = (PlayerActionKind.Move, step[i]);
            for (int h = 0; h < a.Hits; h++)
            {
                int note = a.Note + h * a.Every;
                if (final == Final.Guard) plan[note] = (PlayerActionKind.Guard, MoveDirection.Up);
                else if (final == Final.Move) plan[note] = (PlayerActionKind.Move, direction);
            }
            return plan;
        }

        /// <summary>Plays notes 0..lastNote with each input pressed exactly on its note time. Returns rejected inputs.</summary>
        private static List<string> PlayTo(BattleSession battle, Dictionary<int, (PlayerActionKind kind, MoveDirection direction)> plan, int lastNote,
            Action<int> closed = null)
        {
            var problems = new List<string>();
            var settings = battle.Settings;
            var timeline = settings.CreateTimeline();
            for (int note = 0; note <= lastNote; note++)
            {
                double time = timeline.TimeOf(note) + settings.InputOffset;
                if (plan.TryGetValue(note, out var input))
                {
                    var result = battle.Request(input.kind, input.direction, time);
                    if (!result.Accepted) problems.Add($"노트 {note} {input.kind} 입력 거부({result.Rejection})");
                }
                closed?.Invoke(-note - 1); // before the close
                battle.Advance(time + settings.GoodWindow + .001);
                if (battle.LastClosedNote != note) problems.Add($"노트 {note}가 닫히지 않음(마지막 {battle.LastClosedNote})");
                closed?.Invoke(note);
            }
            return problems;
        }

        private static Probe Play(BattleSettings settings, BossChartAttack a, Drawing d, GridPosition start, GridPosition target, Final final,
            MoveDirection direction, string scenario, string[] expect)
        {
            var probe = new Probe { Shape = a.Shape.Name, Note = a.Note, Publish = a.Publish, Warning = a.Warning, Start = Cell(start),
                Target = Cell(target), Scenario = scenario, Expected = expect.Length == 0 ? "없음" : string.Join("+", expect) };
            if (a.Publish - Distance(Center, start) + 1 < 0) { probe.Status = NotApplicable; probe.Detail = "공개 전에 출발 칸까지 걸어갈 노트가 부족"; return probe; }
            var hitNotes = Enumerable.Range(0, a.Hits).Select(h => a.Note + h * a.Every).ToList();
            var battle = new BattleSession(settings, 0);
            var countdown = new List<int>();
            var hpBefore = new Dictionary<int, int>();
            var hpAfter = new Dictionary<int, int>();
            GridPosition atPublish = default;
            var problems = PlayTo(battle, Inputs(a, start, target, final, direction), a.LastHit, note =>
            {
                if (note < 0) { if (hitNotes.Contains(-note - 1)) hpBefore[-note - 1] = battle.PlayerHp; return; }
                if (hitNotes.Contains(note)) hpAfter[note] = battle.PlayerHp;
                if (note == a.Publish) atPublish = battle.Session.Position;
                if (note >= a.Publish && note < a.Note)
                    countdown.Add(battle.Patterns.Active.Where(t => t.ImpactNote == a.Note && t.Name == a.Shape.Name)
                        .Select(t => t.Remaining(battle.LastClosedNote)).DefaultIfEmpty(-1).Min());
            });

            var events = battle.Events;
            var published = events.Where(e => e.type == "pattern-publish" && e.detail == a.Shape.Name && hitNotes.Contains(e.impactNote)).ToList();
            if (published.Count == 0) problems.Add("공개되지 않음");
            foreach (var e in published)
            {
                int offset = e.impactNote - a.Note;
                if (e.noteIndex != a.Publish) problems.Add($"공개 노트 {e.noteIndex} (기대 {a.Publish})");
                if (e.warningNotes != a.Warning + offset) problems.Add($"경고 {e.warningNotes} (기대 {a.Warning + offset})");
            }
            if (d.HasAnchor && !atPublish.Equals(start)) problems.Add($"공개 순간 위치 {Cell(atPublish)} (기대 {Cell(start)})");
            var (expectedNormal, expectedStrong) = Expected(d, start);
            foreach (int note in hitNotes)
            {
                var hit = published.Where(e => e.impactNote == note).ToList();
                ulong gotNormal = Or(hit.Where(e => e.response != "DodgeOnly")), gotStrong = Or(hit.Where(e => e.response == "DodgeOnly"));
                if (gotNormal != expectedNormal || gotStrong != expectedStrong)
                    problems.Add($"노트 {note} 칸 다름: 기대 1[{Cells(expectedNormal)}] 2[{Cells(expectedStrong)}], 실제 1[{Cells(gotNormal)}] 2[{Cells(gotStrong)}]");
                if (d.GuardOnly && hit.Any(e => e.response != "GuardOnly")) problems.Add("가드 전용으로 공개되지 않음");
                int fires = events.Count(e => e.type == "pattern-fire" && e.detail == a.Shape.Name && e.impactNote == note);
                if (fires != hit.Count) problems.Add($"노트 {note} 발동 {fires}개 (공개 {hit.Count}개)");
                var got = events.Where(e => e.noteIndex == note && (e.type == "player-hit" || e.type == "guard-success" || e.type == "guard-broken"))
                    .Select(e => e.type).ToList();
                if (!got.SequenceEqual(expect)) problems.Add($"노트 {note} 결과 {(got.Count == 0 ? "없음" : string.Join("+", got))}");
                int lost = hpBefore.TryGetValue(note, out int before) && hpAfter.TryGetValue(note, out int after) ? before - after : -1;
                if (lost != expect.Count(t => t == "player-hit")) problems.Add($"노트 {note} HP 변화 {lost}");
            }
            var countdownExpected = Enumerable.Range(0, a.Warning).Select(i => a.Warning - i).ToList();
            if (!countdown.SequenceEqual(countdownExpected)) problems.Add($"카운트다운 {string.Join(",", countdown)} (기대 {string.Join(",", countdownExpected)})");
            if (events.Any(e => (e.type == "pattern-skip" || e.type == "pattern-cancel") && e.detail == a.Shape.Name && hitNotes.Contains(e.impactNote)))
                problems.Add("건너뜀 또는 취소됨");
            probe.Status = problems.Count == 0 ? Passed : Failed;
            probe.Detail = string.Join("; ", problems);
            return probe;
        }

        // ── Report ──
        public static string Format(string title, List<Probe> probes)
        {
            var text = new StringBuilder();
            text.AppendLine(title);
            text.AppendLine($"확인 {probes.Count}건: 통과 {probes.Count(p => p.Status == Passed)}, 실패 {probes.Count(p => p.Status == Failed)}, 해당 없음 {probes.Count(p => p.Status == NotApplicable)}");
            text.AppendLine("모양\t노트(공개-발동/경고)\t확인\t출발→목표\t기대\t결과\t비고");
            foreach (var p in probes)
                text.AppendLine($"{p.Shape}\t{p.Publish}-{p.Note}/{p.Warning}\t{p.Scenario}\t{p.Start}→{p.Target}\t{p.Expected}\t{p.Status}\t{p.Detail}");
            return text.ToString();
        }
        /// <summary>Runs both charts against the project's song and stage and writes Docs/BossChart/phase1-ingame.txt.</summary>
        public static string RunAll()
        {
            var settings = BattleAnalysis.LoadSettings();
            var shapes = Run(settings, File.ReadAllText(ChartPath));
            var combos = Run(settings, File.ReadAllText(CombosPath));
            string report = Format("1페이즈 모양 인게임 판정: " + ChartPath, shapes) + Environment.NewLine + Format("조합 검사 인게임 판정: " + CombosPath, combos);
            File.WriteAllText(ReportPath, report);
            string Summary(List<Probe> p) => $"통과 {p.Count(x => x.Status == Passed)}, 실패 {p.Count(x => x.Status == Failed)}, 해당 없음 {p.Count(x => x.Status == NotApplicable)}";
            return $"모양: {Summary(shapes)} / 조합: {Summary(combos)}";
        }

        // ── Play Mode view ──
        /// <summary>Shows one attack on the live view, waiting at the start cell. remaining = countdown number on screen; 0 = the impact.</summary>
        public static string Show(string shape, string startCell = "d4", int remaining = 0, bool combos = false)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
            var game = UnityEngine.Object.FindAnyObjectByType<PrototypeGame>() ?? throw new InvalidOperationException("The live game is missing.");
            string text = File.ReadAllText(combos ? CombosPath : ChartPath);
            var settings = Prepare(BattleSettings.From(game.Song, game.Stage).Copy(), text);
            var a = settings.Chart.Attacks.First(x => x.Shape.Name == shape);
            if (remaining < 0 || remaining > a.Warning) throw new ArgumentOutOfRangeException(nameof(remaining));
            var start = Parse(startCell);
            var battle = new BattleSession(settings, 0);
            var problems = PlayTo(battle, Inputs(a, start, start, Final.Wait, MoveDirection.Up), a.Note - remaining);
            if (problems.Count > 0) throw new InvalidOperationException(string.Join("; ", problems));
            return BattleVisualVerification.Present(battle, $"{shape} @ {startCell}, {(remaining == 0 ? "impact" : remaining + " left")}");
        }
        public static string Restore() => BattleVisualVerification.Restore();

        // ── Helpers ──
        private static GridPosition? Nearest(GridPosition from, ulong cells, int reach)
        {
            GridPosition? best = null; int bestDistance = int.MaxValue;
            for (int cell = 0; cell < 64; cell++)
            {
                if ((cells & (1UL << cell)) == 0) continue;
                var p = new GridPosition(cell % 8, cell / 8);
                int distance = Distance(from, p);
                if (distance <= reach && distance < bestDistance) { best = p; bestDistance = distance; }
            }
            return best;
        }
        private static List<MoveDirection> Path(GridPosition from, GridPosition to)
        {
            var path = new List<MoveDirection>();
            for (int x = from.X; x != to.X; x += Math.Sign(to.X - from.X)) path.Add(to.X > from.X ? MoveDirection.Right : MoveDirection.Left);
            for (int y = from.Y; y != to.Y; y += Math.Sign(to.Y - from.Y)) path.Add(to.Y > from.Y ? MoveDirection.Up : MoveDirection.Down);
            return path;
        }
        private static int Distance(GridPosition a, GridPosition b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        private static ulong Bit(GridPosition p) => 1UL << (p.Y * 8 + p.X);
        private static ulong Or(IEnumerable<BattleEvent> events)
        {
            ulong cells = 0;
            foreach (var e in events) cells |= ulong.Parse(e.cells, System.Globalization.NumberStyles.HexNumber);
            return cells;
        }
        /// <summary>Chess names: file a-h = x, rank 1-8 = y + 1. d4 = (3,3).</summary>
        public static string Cell(GridPosition p) => ((char)('a' + p.X)).ToString() + (p.Y + 1);
        public static GridPosition Parse(string cell) => new GridPosition(cell[0] - 'a', cell[1] - '1');
        public static string Cells(ulong mask)
        {
            var names = new List<string>();
            for (int cell = 0; cell < 64; cell++) if ((mask & (1UL << cell)) != 0) names.Add(Cell(new GridPosition(cell % 8, cell / 8)));
            return string.Join(",", names);
        }
    }
}
