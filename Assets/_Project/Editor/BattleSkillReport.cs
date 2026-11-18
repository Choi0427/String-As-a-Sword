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
using UnityEngine;

namespace StringAsASword.Editor
{
    // Measures skilled-player runs (SkillProfile tiers x StrategyKind) on the stage. Measures only: nothing here tunes settings.
    public static class BattleSkillReport
    {
        private const int LogEvery = 500;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly StrategyKind[] Strategies = (StrategyKind[])Enum.GetValues(typeof(StrategyKind));

        private sealed class Job
        {
            public string group;
            public double scale;
            public BattleSettings settings;
            public StrategyKind strategy;
            public SkillProfile tier;
            public int battleSeed, skillSeed;
        }

        /// <summary>Batch mode: Unity -batchmode -executeMethod StringAsASword.Editor.BattleSkillReport.RunFromCommandLine
        /// [-skillOutput Docs/Balance/battle-9] [-skillSeeds 300] [-skillHpScales 0.94,0.92] [-stageSeed 87].</summary>
        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            try
            {
                string output = Arg("-skillOutput", "Docs/Balance/battle-9");
                int seeds = int.Parse(Arg("-skillSeeds", "300"), Invariant);
                int stageSeed = int.Parse(Arg("-stageSeed", "87"), Invariant);
                string scales = Arg("-skillHpScales", "");
                double[] hpScales = scales.Length == 0 ? new double[0]
                    : scales.Split(',').Select(text => double.Parse(text, Invariant)).ToArray();
                Run(BattleAnalysis.LoadSettings(), output, seeds, stageSeed, hpScales);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[SkillReport] failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        public static void Run(BattleSettings settings, string outputDir, int seeds, int stageSeed, double[] hpScales)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("An output directory is required.");
            if (seeds < 1 || seeds > 100000) throw new ArgumentOutOfRangeException(nameof(seeds));
            hpScales = hpScales ?? new double[0];
            var baseSettings = settings.Copy();
            baseSettings.Validate();
            Directory.CreateDirectory(outputDir);
            int half = seeds / 2;
            var jobs = new List<Job>();

            // main: fixed stage seed, skill seeds 0..N-1.
            foreach (var strategy in Strategies) foreach (var tier in SkillProfile.Tiers)
                for (int s = 0; s < seeds; s++)
                    jobs.Add(new Job { group = "main", scale = 1, settings = baseSettings, strategy = strategy, tier = tier,
                        battleSeed = stageSeed, skillSeed = s });

            // varied: battle seed s, skill seed 1000+s, for s in 0..N/2-1.
            foreach (var strategy in Strategies) foreach (var tier in SkillProfile.Tiers)
                for (int s = 0; s < half; s++)
                    jobs.Add(new Job { group = "varied", scale = 1, settings = baseSettings, strategy = strategy, tier = tier,
                        battleSeed = s, skillSeed = 1000 + s });

            // hp: each scaled copy (ratio kept, as BattleActionMixReport) at the stage seed with N/2 skill seeds.
            foreach (double scale in hpScales)
            {
                var scaled = baseSettings.Copy();
                scaled.Health = Array.ConvertAll(baseSettings.Health, h => Math.Max(1, (int)Math.Round(h * scale)));
                scaled.Validate();
                UnityEngine.Debug.Log($"[SkillReport] HP scale {scale.ToString("0.00", Invariant)}: {string.Join("/", scaled.Health)}");
                foreach (var strategy in Strategies) foreach (var tier in SkillProfile.Tiers)
                    for (int s = 0; s < half; s++)
                        jobs.Add(new Job { group = "hp", scale = scale, settings = scaled, strategy = strategy, tier = tier,
                            battleSeed = stageSeed, skillSeed = s });
            }

            var timer = Stopwatch.StartNew();
            var results = new SkilledRun[jobs.Count];
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
            // Chunks keep the progress log on the calling thread (Unity's log API is main-thread only).
            for (int first = 0; first < jobs.Count; first += LogEvery)
            {
                int start = first, end = Math.Min(jobs.Count, first + LogEvery);
                Parallel.For(start, end, parallel, i =>
                {
                    var job = jobs[i];
                    results[i] = SkilledSimulator.RunSkilled(job.settings, job.battleSeed, job.skillSeed, job.strategy, job.tier, keepBattle: false);
                });
                UnityEngine.Debug.Log($"[SkillReport] {end}/{jobs.Count} runs, {Number(timer.Elapsed.TotalSeconds, "F1")}s");
            }

            var pairs = jobs.Select((job, i) => (job, sr: results[i])).ToList();
            WriteRuns(Path.Combine(outputDir, "runs.csv"), pairs);
            WriteSummary(Path.Combine(outputDir, "summary.csv"), pairs);
            UnityEngine.Debug.Log($"[SkillReport] {jobs.Count} runs in {Number(timer.Elapsed.TotalSeconds, "F1")}s -> {outputDir}");
        }

        private static void WriteRuns(string path, List<(Job job, SkilledRun sr)> pairs)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("group,scale,strategy,tier,battleSeed,skillSeed,outcome,finalPhase,finalHp,bossHp,leftoverNotes,stunnedNote," +
                    "perfects,goods,misses,dropped,unread,ignitions,cadences,ghosts,resonanceNotes,closedNotes,uptime,damage,hitsTaken,invariantFailures,invariantKeys");
                foreach (var (job, sr) in pairs)
                {
                    var run = sr.Run;
                    WriteRow(writer, job.group, Number(job.scale, "F2"), job.strategy, job.tier.Name, job.battleSeed, job.skillSeed,
                        run.Outcome, run.FinalPhase, run.FinalHp, run.BossHp, run.LeftoverNotes, sr.StunnedNote,
                        sr.Perfects, sr.Goods, sr.Misses, sr.Dropped, sr.UnreadTelegraphs, sr.Ignitions, sr.Cadences, sr.GhostStrikes,
                        sr.ResonanceNotes, sr.ClosedNotes, Number(sr.Uptime, "F4"), sr.DamageDealt, sr.HitsTaken, run.InvariantFailureCount, string.Join("|", run.InvariantFailures.Select(kv => kv.Key + ":" + kv.Value)));
                }
            }
        }

        private static void WriteSummary(string path, List<(Job job, SkilledRun sr)> pairs)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("group,scale,strategy,tier,runs,clearRate,deathRate,timeoutRate,medianLeftoverCleared,medianBossHpNotCleared," +
                    "meanUptime,meanIgnitions,meanDamagePerIgnition,perfectShare,meanHitsTaken,p10FinalHp,medianFinalHp,invariantFailures");
                // GroupBy keeps first-appearance order, so rows follow main, varied, hp.
                var groups = pairs.GroupBy(p => (p.job.group, p.job.scale, p.job.strategy, p.job.tier.Name));
                foreach (var g in groups)
                {
                    var runs = g.Select(p => p.sr).ToArray();
                    var first = g.First().job;
                    int count = runs.Length;
                    double clearRate = runs.Count(r => r.Run.Cleared) / (double)count;
                    double deathRate = runs.Count(r => r.Run.Outcome == "Dead") / (double)count;
                    double timeoutRate = runs.Count(r => r.Run.Outcome == "Timeout") / (double)count;
                    double medianLeftover = Median(runs.Where(r => r.Run.Cleared).Select(r => (double)r.Run.LeftoverNotes));
                    double medianBoss = Median(runs.Where(r => !r.Run.Cleared).Select(r => (double)r.Run.BossHp));
                    double meanUptime = Mean(runs.Select(r => r.Uptime));
                    double meanIgnitions = Mean(runs.Select(r => (double)r.Ignitions));
                    double meanPerIgnition = Mean(runs.Where(r => r.Ignitions > 0).Select(r => r.DamagePerIgnition));
                    long perfects = runs.Sum(r => (long)r.Perfects);
                    long judged = runs.Sum(r => (long)(r.Perfects + r.Goods + r.Misses));
                    double perfectShare = judged == 0 ? double.NaN : perfects / (double)judged;
                    double meanHits = Mean(runs.Select(r => (double)r.HitsTaken));
                    var finalHp = runs.Select(r => r.Run.FinalHp).ToArray();
                    int p10Hp = BattleCalibration.Percentile(finalHp, .1);
                    double medianHp = Median(finalHp.Select(h => (double)h));
                    int invariantFailures = runs.Sum(r => r.Run.InvariantFailureCount);

                    WriteRow(writer, first.group, Number(first.scale, "F2"), first.strategy, first.tier.Name, count,
                        Number(clearRate, "F4"), Number(deathRate, "F4"), Number(timeoutRate, "F4"),
                        Number(medianLeftover, "F1"), Number(medianBoss, "F1"), Number(meanUptime, "F4"), Number(meanIgnitions, "F3"),
                        Number(meanPerIgnition, "F2"), Number(perfectShare, "F4"), Number(meanHits, "F3"),
                        p10Hp, Number(medianHp, "F1"), invariantFailures);
                }
            }
        }

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

        private static void WriteRow(StreamWriter writer, params object[] values)
            => writer.WriteLine(string.Join(",", values.Select(v =>
            {
                string s = Convert.ToString(v, Invariant) ?? "";
                return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            })));
    }
}
