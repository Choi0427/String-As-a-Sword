using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StringAsASword.Board;
using StringAsASword.Combat;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Checks a hand-written boss chart with the same fairness rules the random scheduler uses at runtime
    /// (DodgeActions, CrossesFastSection, Invalidates), but from all 64 start cells, since the player's
    /// position is unknown when the chart is written. See Docs/BossChart.md.
    /// </summary>
    public static class BossChartValidator
    {
        public const string DefaultChartPath = "Assets/_Project/Data/Bosses/GeomEulHyeon.bosschart.txt";
        public const string ReportDir = "Docs/BossChart";
        private const int ListedCells = 12;

        public sealed class Finding
        {
            public int Note, Line;
            public string Shape, Section, Message;
            public bool Error;
        }
        public sealed class Result
        {
            public readonly List<Finding> Findings = new List<Finding>();
            public int Attacks;
            public string ParseError;
            public int Failed => Findings.Where(f => f.Error).Select(f => f.Line).Distinct().Count() + (ParseError != null ? 1 : 0);
            public int Warned => Findings.Where(f => !f.Error).Select(f => f.Line).Distinct().Count();
            public bool Passed => Failed == 0;
        }

        public static Result Check(string chartText, BattleSettings settings)
        {
            var result = new Result();
            BossChart chart;
            try { chart = BossChart.Parse(chartText); }
            catch (BossChartException e) { result.ParseError = e.Message; return result; }
            return Check(chart, settings, result);
        }

        public static Result Check(BossChart chart, BattleSettings settings, Result result = null)
        {
            result = result ?? new Result();
            var fast = settings.FastNotes();
            var system = new BossPatternSystem(new PatternDefinition[0], null, 0, fast);
            var attacks = chart.Attacks;
            result.Attacks = attacks.Count;
            foreach (var a in attacks)
            {
                void Report(bool error, string message) => result.Findings.Add(new Finding { Note = a.Note, Line = a.Line,
                    Shape = a.Shape.Name, Section = chart.SectionAt(a.Note), Message = message, Error = error });
                if (a.Publish < 0) Report(true, $"경고 {a.Warning}노트가 곡 시작(노트 0) 전에 시작돼요.");
                if (a.LastHit >= settings.NoteCount) { Report(true, $"마지막 타격(노트 {a.LastHit})이 곡 노트 수({settings.NoteCount})를 넘어요."); continue; }
                if (chart.RandomFrom >= 0 && a.LastHit >= chart.RandomFrom)
                    Report(true, $"마지막 타격(노트 {a.LastHit})이 랜덤 인계 노트({chart.RandomFrom}) 이후예요. 채보와 랜덤은 겹칠 수 없어요.");
                if (settings.HasFinale && a.LastHit >= settings.FinishNote)
                    Report(false, $"피날레 노트({settings.FinishNote}) 이후예요. 보스가 스턴되면 나오지 않아요.");
                if (a.Publish >= 0 && system.CrossesFastSection(a.Publish, a.LastHit))
                {
                    int note = a.Publish + 1; while (note <= a.LastHit && !(note < fast.Length && fast[note])) note++;
                    Report(true, $"빠른 구간(노트 {note}, 한 박보다 짧은 간격)에 경고나 타격이 걸려요.");
                }
                foreach (var other in attacks)
                    if (other != a && other.Note == a.Note && other.Line > a.Line)
                        Report(false, $"{other.Line}번째 줄 공격과 같은 노트에 떨어져요.");
                if (a.Publish < 0) continue;

                var noDodge = new List<GridPosition>();
                var breaksSafe = new List<GridPosition>();
                for (int y = 7; y >= 0; y--) for (int x = 0; x < 8; x++)
                {
                    var start = new GridPosition(x, y);
                    var hits = system.AuthoredHits(a, start);
                    // Other chart attacks still showing when this one is disclosed. Anchored ones are placed from the same start
                    // cell: the player's position at their own disclosure is unknown, so this is an approximation.
                    var shown = new List<BossTelegraph>();
                    foreach (var b in attacks)
                        if (b != a && b.Publish <= a.Publish && b.LastHit > a.Publish && b.Publish >= 0)
                            foreach (var t in system.AuthoredHits(b, start)) if (t.ImpactNote > a.Publish) shown.Add(t);
                    if (BossPatternSystem.Invalidates(shown, hits)) breaksSafe.Add(start);
                    if (a.Shape.GuardOnly) continue;
                    ulong last = 0;
                    foreach (var t in hits) if (t.Hit == a.Hits - 1) last |= t.Cells;
                    if (last == 0) continue;
                    var disclosed = new List<BossTelegraph>(shown);
                    foreach (var t in hits) if (t.Hit < a.Hits - 1) disclosed.Add(t);
                    var proposal = new BossTelegraph(0, a.Shape.Name, last, a.LastHit, PatternKind.Drawn, a.Warning);
                    if (BossPatternSystem.DodgeActions(start, proposal, a.Publish, disclosed) < 0) noDodge.Add(start);
                }
                if (noDodge.Count > 0) Report(true, $"피할 길 없음. 출발 칸 {noDodge.Count}개: {Cells(noDodge)}");
                const string breaks = "먼저 보여준 공격이 끝나기 전(또는 같은 노트)에, 그 공격이 안전하다고 보여준 칸을 공격해요.";
                if (breaksSafe.Count > 0)
                    Report(true, a.Shape.HasAnchor ? $"{breaks} 출발 칸 {breaksSafe.Count}개: {Cells(breaksSafe)}" : breaks);
            }
            return result;
        }

        // Same convention as the drawing: (row from the top, column from the left), both 1-based.
        private static string Cells(List<GridPosition> cells)
        {
            var text = string.Join(" ", cells.Take(ListedCells).Select(c => $"({8 - c.Y},{c.X + 1})"));
            return cells.Count > ListedCells ? text + $" 외 {cells.Count - ListedCells}칸" : text;
        }

        public static string Format(Result result, string chartPath, BattleSettings settings)
        {
            var text = new StringBuilder();
            text.AppendLine($"보스 채보 검증: {chartPath}");
            text.AppendLine($"곡: {settings.ChartId}, 노트 {settings.NoteCount}개" + (settings.HasFinale ? $", 피날레 노트 {settings.FinishNote}" : ""));
            text.AppendLine("칸 표기: (줄,칸) = 그림과 같은 기준. 위에서 몇 번째 줄, 왼쪽에서 몇 번째 칸.");
            text.AppendLine();
            if (result.ParseError != null)
            {
                text.AppendLine("[오류] " + result.ParseError);
                text.AppendLine();
                text.AppendLine("결과: 실패 (채보를 읽지 못했어요)");
                return text.ToString();
            }
            foreach (var f in result.Findings.OrderBy(f => f.Note).ThenBy(f => f.Line).ThenByDescending(f => f.Error))
            {
                string section = f.Section.Length > 0 ? $" [{f.Section}]" : "";
                text.AppendLine($"[{(f.Error ? "실패" : "경고")}] 노트 {f.Note}{section} {f.Shape} ({f.Line}번째 줄): {f.Message}");
            }
            if (result.Findings.Count > 0) text.AppendLine();
            text.AppendLine($"결과: {(result.Passed ? "통과" : "실패")}. 공격 {result.Attacks}개 중 실패 {result.Failed}, 경고 {result.Warned}.");
            return text.ToString();
        }

        /// <summary>Validates, writes Docs/BossChart/validation.txt, and if it passes and seeds > 0, runs the bot simulation.</summary>
        public static (bool passed, string report) Run(string chartPath, int seeds, string outputDir = ReportDir)
        {
            string chartText = File.ReadAllText(chartPath);
            var settings = BattleAnalysis.LoadSettings();
            settings.BossChartText = chartText;
            var result = Check(chartText, settings);
            string report = Format(result, chartPath, settings);
            Directory.CreateDirectory(outputDir);
            if (result.Passed && seeds > 0)
            {
                settings.Version += "+chart";
                report += Environment.NewLine + "봇 시뮬레이션 (" + seeds.ToString(CultureInfo.InvariantCulture) + " 시드/그룹)" + Environment.NewLine +
                    BattleActionMixReport.Run(settings, Path.Combine(outputDir, "sim"), seeds);
            }
            File.WriteAllText(Path.Combine(outputDir, "validation.txt"), report);
            return (result.Passed, report);
        }

        private static string SelectedChartPath()
        {
            string selected = Selection.activeObject is TextAsset ? AssetDatabase.GetAssetPath(Selection.activeObject) : "";
            return selected.EndsWith(".bosschart.txt", StringComparison.Ordinal) ? selected : DefaultChartPath;
        }
        [MenuItem("String As a Sword/Boss Chart/Validate")]
        private static void ValidateMenu() => RunMenu(0);
        [MenuItem("String As a Sword/Boss Chart/Validate and Simulate (200 seeds)")]
        private static void SimulateMenu() => RunMenu(200);
        private static void RunMenu(int seeds)
        {
            string path = SelectedChartPath();
            var (passed, report) = Run(path, seeds);
            if (passed) Debug.Log("[BossChart] " + report); else Debug.LogWarning("[BossChart] " + report);
            EditorUtility.DisplayDialog("보스 채보 검증", report.Length > 1500 ? report.Substring(0, 1500) + "\n…\n전체: " + ReportDir + "/validation.txt" : report, "확인");
        }

        /// <summary>Batch mode: Unity -batchmode -quit -executeMethod StringAsASword.Editor.BossChartValidator.RunFromCommandLine
        /// [-bossChart path] [-bossChartSeeds 0] [-bossChartOutput Docs/BossChart]. Exits 1 when the chart fails.</summary>
        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            var (passed, report) = Run(Arg("-bossChart", DefaultChartPath), int.Parse(Arg("-bossChartSeeds", "0"), CultureInfo.InvariantCulture),
                Arg("-bossChartOutput", ReportDir));
            Debug.Log("[BossChart] " + report);
            if (!passed) EditorApplication.Exit(1);
        }
    }
}
