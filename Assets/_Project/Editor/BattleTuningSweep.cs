using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringAsASword.Combat;
using StringAsASword.Simulation;
using UnityEditor;

namespace StringAsASword.Editor
{
    // Compares resonance rule candidates at equal pacing: each candidate's boss HP is scaled so the Intermediate
    // players' median stun note hits the same target, then all skill tiers x strategies are measured.
    // Measures only: nothing here writes settings assets.
    public static class BattleTuningSweep
    {
        private const int LogEvery = 500;
        private const int BattleSeed = 87;
        private const int NormalizeSeeds = 60;
        private const int NormalizeIterations = 8;
        private const double ScaleMin = .4, ScaleMax = 3.5;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly StrategyKind[] Strategies = (StrategyKind[])Enum.GetValues(typeof(StrategyKind));
        private static readonly string[] TierPrefixes = { "perf_", "exp_", "int_", "nov_" };

        public sealed class Candidate
        {
            public string Label;
            public int L, Perfect, Good, Cadence, Ghost;
        }

        private sealed class Job
        {
            public BattleSettings settings;
            public StrategyKind strategy;
            public SkillProfile tier;
            public int battleSeed, skillSeed;
            public bool keepBattle;
        }

        private sealed class Evaluation
        {
            public string stage, label;
            public Candidate candidate;
            public double hpScale;
            public int[] health;
            public string summaryLine;
            public List<string> cellLines = new List<string>();
        }

        /// <summary>Batch mode: Unity -batchmode -executeMethod StringAsASword.Editor.BattleTuningSweep.RunFromCommandLine
        /// -tuneStage L|damage|ghost|hp|custom [-tuneOutput dir] [-tuneSeeds 150] [-tuneL 8] [-tuneDamage 2/1/2]
        /// [-tuneGhost 0] [-tuneTarget 268].</summary>
        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            try
            {
                string stage = Arg("-tuneStage", "custom");
                string output = Arg("-tuneOutput", "Docs/Balance/battle-10/" + stage);
                int seeds = int.Parse(Arg("-tuneSeeds", "150"), Invariant);
                double target = double.Parse(Arg("-tuneTarget", "268"), Invariant);
                var settings = BattleAnalysis.LoadSettings();
                string lText = Arg("-tuneL", "");
                int fixedL = lText.Length == 0 ? settings.ResonanceNotes : int.Parse(lText, Invariant);
                int fixedPerfect = settings.ResonancePerfect, fixedGood = settings.ResonanceGood, fixedCadence = settings.ResonanceCadence;
                string damage = Arg("-tuneDamage", "");
                if (damage.Length > 0)
                {
                    var parts = damage.Split('/');
                    if (parts.Length != 3) throw new ArgumentException("-tuneDamage must look like 2/1/2.");
                    fixedPerfect = int.Parse(parts[0], Invariant);
                    fixedGood = int.Parse(parts[1], Invariant);
                    fixedCadence = int.Parse(parts[2], Invariant);
                }
                int fixedGhost = int.Parse(Arg("-tuneGhost", "0"), Invariant);
                Run(settings, stage, output, seeds, target, fixedL, fixedPerfect, fixedGood, fixedCadence, fixedGhost);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Tune] failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Run(BattleSettings settings, string stage, string outputDir, int seeds, double target,
            int fixedL, int fixedPerfect, int fixedGood, int fixedCadence, int fixedGhost)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("An output directory is required.");
            if (seeds < 1 || seeds > 100000) throw new ArgumentOutOfRangeException(nameof(seeds));
            var baseSettings = settings.Copy();
            baseSettings.Validate();
            Directory.CreateDirectory(outputDir);

            Candidate Make(string label, int l, int p, int g, int c, int ghost)
                => new Candidate { Label = label, L = l, Perfect = p, Good = g, Cadence = c, Ghost = ghost };
            var candidates = new List<Candidate>();
            string key = (stage ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "l":
                    foreach (int l in new[] { 4, 6, 8, 12, 16 })
                        candidates.Add(Make("L" + l, l, fixedPerfect, fixedGood, fixedCadence, fixedGhost));
                    break;
                case "damage":
                    foreach (var d in new[] { new[] { 2, 1, 2 }, new[] { 3, 1, 2 }, new[] { 3, 2, 2 }, new[] { 4, 1, 2 }, new[] { 3, 1, 0 }, new[] { 3, 1, 4 } })
                        candidates.Add(Make(d[0] + "/" + d[1] + "/" + d[2], fixedL, d[0], d[1], d[2], fixedGhost));
                    break;
                case "ghost":
                    foreach (int ghost in new[] { 0, 1 })
                        candidates.Add(Make("ghost" + ghost, fixedL, fixedPerfect, fixedGood, fixedCadence, ghost));
                    break;
                case "custom":
                case "hp":
                    candidates.Add(Make("L" + fixedL + "_" + fixedPerfect + "/" + fixedGood + "/" + fixedCadence + "_g" + fixedGhost,
                        fixedL, fixedPerfect, fixedGood, fixedCadence, fixedGhost));
                    break;
                default:
                    throw new ArgumentException("Unknown -tuneStage '" + stage + "' (L|damage|ghost|hp|custom).");
            }

            var timer = Stopwatch.StartNew();
            var trace = new StringBuilder();
            var summaryRows = new List<string>();
            var cellRows = new List<string>();
            string header = null;
            foreach (var candidate in candidates)
            {
                var candidateSettings = Apply(baseSettings, candidate);
                trace.AppendLine($"== {candidate.Label} (target stun note {Number(target, "F1")}) ==");
                var (health, scale) = Normalize(baseSettings, candidate, target, trace);
                UnityEngine.Debug.Log($"[Tune] {candidate.Label}: hpScale {Number(scale, "F4")} -> {string.Join("/", health)}");

                var evaluations = new List<Evaluation>();
                if (key == "hp")
                {
                    var scales = new[] { ("norm", scale), ("-10%", scale * .9), ("+10%", scale * 1.1) };
                    foreach (var (name, hpScale) in scales)
                    {
                        var scaledHealth = ScaleHealth(baseSettings, hpScale);
                        evaluations.Add(Evaluate(key, name, candidateSettings, candidate, hpScale, scaledHealth, seeds, timer,
                            phaseCheck: name == "norm"));
                    }
                }
                else evaluations.Add(Evaluate(key, candidate.Label, candidateSettings, candidate, scale, health, seeds, timer, phaseCheck: false));

                foreach (var e in evaluations)
                {
                    summaryRows.Add(e.summaryLine);
                    cellRows.AddRange(e.cellLines);
                }
                header = SummaryHeader;
            }

            File.WriteAllText(Path.Combine(outputDir, "summary.csv"), header + "\n" + string.Join("\n", summaryRows) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, "cells.csv"),
                "stage,label,tier,strategy,runs,clear,death,timeout,stunMedian,dpi\n" + string.Join("\n", cellRows) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDir, "normalize.log.txt"), trace.ToString(), new UTF8Encoding(false));
            UnityEngine.Debug.Log($"[Tune] {candidates.Count} candidates in {Number(timer.Elapsed.TotalSeconds, "F1")}s -> {outputDir}");
        }

        /// <summary>Settings copy with the candidate's resonance rule applied (Health left as in <paramref name="baseSettings"/>).</summary>
        public static BattleSettings Apply(BattleSettings baseSettings, Candidate candidate)
        {
            var copy = baseSettings.Copy();
            copy.ResonanceNotes = candidate.L;
            copy.ResonancePerfect = candidate.Perfect;
            copy.ResonanceGood = candidate.Good;
            copy.ResonanceCadence = candidate.Cadence;
            copy.GhostDamage = candidate.Ghost;
            return copy;
        }

        /// <summary>Bisects the boss HP scale so the median Intermediate stun note (all strategies, 60 skill seeds) is about
        /// <paramref name="target"/>. A run that never stuns counts as int.MaxValue.</summary>
        public static (int[] health, double scale) Normalize(BattleSettings baseSettings, Candidate candidate, double target, StringBuilder trace)
        {
            double lo = ScaleMin, hi = ScaleMax;
            for (int iteration = 1; iteration <= NormalizeIterations; iteration++)
            {
                double mid = (lo + hi) / 2;
                var settings = Apply(baseSettings, candidate);
                settings.Health = ScaleHealth(baseSettings, mid);
                settings.Validate();
                var jobs = new List<Job>();
                foreach (var strategy in Strategies)
                    for (int s = 0; s < NormalizeSeeds; s++)
                        jobs.Add(new Job { settings = settings, strategy = strategy, tier = SkillProfile.Intermediate, battleSeed = BattleSeed, skillSeed = s });
                var results = RunJobs(jobs, null, null);
                double median = Median(results.Select(r => r.StunnedNote < 0 ? (double)int.MaxValue : r.StunnedNote));
                string line = $"iter {iteration}: scale {Number(mid, "F4")} health {string.Join("/", settings.Health)} medianStun {(median >= int.MaxValue ? "never" : Number(median, "F1"))}";
                trace?.AppendLine(line);
                UnityEngine.Debug.Log("[Tune] normalize " + candidate.Label + " " + line);
                if (median > target) hi = mid; else lo = mid;
            }
            double scale = (lo + hi) / 2;
            var health = ScaleHealth(baseSettings, scale);
            trace?.AppendLine($"final: scale {Number(scale, "F4")} health {string.Join("/", health)}");
            return (health, scale);
        }

        private static int[] ScaleHealth(BattleSettings baseSettings, double scale)
            => Array.ConvertAll(baseSettings.Health, h => Math.Max(1, (int)Math.Round(h * scale)));

        private const string SummaryHeaderBase = "stage,label,L,perfect,good,cadence,ghost,hpScale,health,totalHp";
        private const string TierColumns = "clear,death,timeout,stunMedian,leftoverMedian,dpi,cadenceRate,uptime,ignitions,stratGap,bestStrategy,worstStrategy";
        private static string SummaryHeader
            => SummaryHeaderBase + "," + string.Join(",", TierPrefixes.SelectMany(p => TierColumns.Split(',').Select(c => p + c)))
               + ",dpiRatioExpNov,mixMove,mixGuard,mixAttack,invariantFailures,phase2Note,phase3Note,phase4Note";

        private static Evaluation Evaluate(string stage, string label, BattleSettings candidateSettings, Candidate candidate,
            double hpScale, int[] health, int seeds, Stopwatch timer, bool phaseCheck)
        {
            var settings = candidateSettings.Copy();
            settings.Health = (int[])health.Clone();
            settings.Validate();
            var tiers = SkillProfile.Tiers;
            var jobs = new List<Job>();
            foreach (var tier in tiers) foreach (var strategy in Strategies)
                for (int s = 0; s < seeds; s++)
                    jobs.Add(new Job { settings = settings, strategy = strategy, tier = tier, battleSeed = BattleSeed, skillSeed = s });
            var results = RunJobs(jobs, "evaluate " + label, timer);

            var evaluation = new Evaluation { stage = stage, label = label, candidate = candidate, hpScale = hpScale, health = health };
            var row = new List<object>
            {
                stage, label, candidate.L, candidate.Perfect, candidate.Good, candidate.Cadence, candidate.Ghost,
                Number(hpScale, "F4"), string.Join("/", health), health.Sum()
            };
            double expDpi = double.NaN, novDpi = double.NaN;
            SkilledRun[] intermediate = null;
            int invariantFailures = 0;
            for (int t = 0; t < tiers.Length; t++)
            {
                var tierRuns = new List<SkilledRun>();
                var clearByStrategy = new List<(StrategyKind strategy, double clear)>();
                for (int k = 0; k < Strategies.Length; k++)
                {
                    int offset = (t * Strategies.Length + k) * seeds;
                    var cell = new SkilledRun[seeds];
                    Array.Copy(results, offset, cell, 0, seeds);
                    tierRuns.AddRange(cell);
                    double clear = cell.Count(r => r.Run.Cleared) / (double)seeds;
                    clearByStrategy.Add((Strategies[k], clear));
                    evaluation.cellLines.Add(Line(stage, label, tiers[t].Name, Strategies[k], seeds,
                        Number(clear, "F4"), Number(cell.Count(r => r.Run.Outcome == "Dead") / (double)seeds, "F4"),
                        Number(cell.Count(r => r.Run.Outcome == "Timeout") / (double)seeds, "F4"),
                        Number(StunMedian(cell), "F1"), Number(MeanDpi(cell), "F2")));
                }
                var all = tierRuns.ToArray();
                invariantFailures += all.Sum(r => r.Run.InvariantFailureCount);
                double dpi = MeanDpi(all);
                if (t == 1) expDpi = dpi;
                if (t == 3) novDpi = dpi;
                if (t == 2) intermediate = all;
                long ignitions = all.Sum(r => (long)r.Ignitions);
                double cadenceRate = ignitions == 0 ? double.NaN : all.Sum(r => (long)r.Cadences) / (double)ignitions;
                var best = clearByStrategy.OrderByDescending(c => c.clear).First();
                var worst = clearByStrategy.OrderBy(c => c.clear).First();
                row.Add(Number(clearByStrategy.Average(c => c.clear), "F4"));
                row.Add(Number(all.Count(r => r.Run.Outcome == "Dead") / (double)all.Length, "F4"));
                row.Add(Number(all.Count(r => r.Run.Outcome == "Timeout") / (double)all.Length, "F4"));
                row.Add(Number(StunMedian(all), "F1"));
                row.Add(Number(Median(all.Where(r => r.Run.Cleared).Select(r => (double)r.Run.LeftoverNotes)), "F1"));
                row.Add(Number(dpi, "F2"));
                row.Add(Number(cadenceRate, "F4"));
                row.Add(Number(Mean(all.Select(r => r.Uptime)), "F4"));
                row.Add(Number(Mean(all.Select(r => (double)r.Ignitions)), "F3"));
                row.Add(Number(best.clear - worst.clear, "F4"));
                row.Add(best.strategy);
                row.Add(worst.strategy);
            }
            row.Add(Number(double.IsNaN(expDpi) || double.IsNaN(novDpi) || novDpi == 0 ? double.NaN : expDpi / novDpi, "F3"));
            long moves = intermediate.Sum(r => (long)r.Moves), guards = intermediate.Sum(r => (long)r.Guards),
                attacks = intermediate.Sum(r => (long)r.EffectiveAttacks), others = intermediate.Sum(r => (long)r.OtherActions);
            double total = moves + guards + attacks + others;
            row.Add(Number(total == 0 ? double.NaN : moves / total, "F4"));
            row.Add(Number(total == 0 ? double.NaN : guards / total, "F4"));
            row.Add(Number(total == 0 ? double.NaN : attacks / total, "F4"));
            row.Add(invariantFailures);

            string p2 = "", p3 = "", p4 = "";
            if (phaseCheck)
            {
                var phaseJobs = new List<Job>();
                for (int s = 0; s < NormalizeSeeds; s++)
                    phaseJobs.Add(new Job { settings = settings, strategy = StrategyKind.CollectFirst, tier = SkillProfile.Intermediate,
                        battleSeed = BattleSeed, skillSeed = s, keepBattle = true });
                var phaseRuns = RunJobs(phaseJobs, null, null);
                // Phases[p-1].transitionNote is the note at which phase p ended, so phase p+1 begins there.
                string PhaseBegin(int phaseIndex) => Number(Median(phaseRuns
                    .Select(r => r.Run.Phases[phaseIndex].transitionNote).Where(n => n >= 0).Select(n => (double)n)), "F1");
                p2 = PhaseBegin(0); p3 = PhaseBegin(1); p4 = PhaseBegin(2);
            }
            row.Add(p2); row.Add(p3); row.Add(p4);
            evaluation.summaryLine = Line(row.ToArray());
            UnityEngine.Debug.Log($"[Tune] evaluated {label} ({stage}) hpScale {Number(hpScale, "F4")}");
            return evaluation;
        }

        private static SkilledRun[] RunJobs(List<Job> jobs, string progress, Stopwatch timer)
        {
            var results = new SkilledRun[jobs.Count];
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
            // Chunks keep the progress log on the calling thread (Unity's log API is main-thread only).
            for (int first = 0; first < jobs.Count; first += LogEvery)
            {
                int start = first, end = Math.Min(jobs.Count, first + LogEvery);
                Parallel.For(start, end, parallel, i =>
                {
                    var job = jobs[i];
                    results[i] = SkilledSimulator.RunSkilled(job.settings, job.battleSeed, job.skillSeed, job.strategy, job.tier, job.keepBattle);
                });
                if (progress != null)
                    UnityEngine.Debug.Log($"[Tune] {progress}: {end}/{jobs.Count} runs, {Number(timer != null ? timer.Elapsed.TotalSeconds : 0, "F1")}s");
            }
            return results;
        }

        private static double StunMedian(IEnumerable<SkilledRun> runs)
            => Median(runs.Where(r => r.StunnedNote >= 0).Select(r => (double)r.StunnedNote));

        private static double MeanDpi(IEnumerable<SkilledRun> runs)
            => Mean(runs.Where(r => r.Ignitions > 0).Select(r => r.DamagePerIgnition));

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return double.NaN;
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        private static double Mean(IEnumerable<double> values)
        {
            var array = values.ToArray();
            return array.Length == 0 ? double.NaN : array.Average();
        }

        // Empty cell for "no observations" (NaN), so the CSV stays readable in spreadsheets.
        private static string Number(double value, string format) => double.IsNaN(value) ? "" : value.ToString(format, Invariant);

        private static string Line(params object[] values)
            => string.Join(",", values.Select(v =>
            {
                string s = Convert.ToString(v, Invariant) ?? "";
                return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            }));
    }
}
