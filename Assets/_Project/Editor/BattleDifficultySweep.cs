using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Simulation;
using UnityEditor;

namespace StringAsASword.Editor
{
    // battle-11: measures balance candidates on both difficulties (어려움 = plain rules, 쉬움 = noise heals 1 HP).
    // Each config line may fit the boss HP so one tier's median stun note hits a target, then every tier x strategy
    // is measured on both difficulties. Measures only: nothing here writes settings assets.
    //
    // Config file, one candidate per line ('#' comments):
    //   label damage=3/1/2 hp=65/15/26/100 playerHp=5 [fit=Expert:hard:255] [sens=1] [attacks=1]
    public static class BattleDifficultySweep
    {
        private const int LogEvery = 1000;
        private const int BattleSeed = 87;
        private const int FitSeeds = 40;
        private const int FitIterations = 8;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly StrategyKind[] Strategies = (StrategyKind[])Enum.GetValues(typeof(StrategyKind));
        private static readonly (string name, bool heal)[] Difficulties = { ("hard", false), ("easy", true) };

        public sealed class Config
        {
            public string Label;
            public int Perfect, Good, Cadence, PlayerHp;
            public int[] Health;
            public string FitTier, FitDifficulty;
            public double FitTarget = double.NaN;
            public bool Sensitivity, Attacks;
        }

        private struct Job
        {
            public BattleSettings settings;
            public StrategyKind strategy;
            public SkillProfile tier;
            public int skillSeed;
            public bool heal, keep;
        }

        /// <summary>Batch mode: Unity -batchmode -executeMethod StringAsASword.Editor.BattleDifficultySweep.RunFromCommandLine
        /// -dsConfigs file -dsOutput dir [-dsSeeds 150].</summary>
        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
            try
            {
                var settings = BattleAnalysis.LoadSettings();
                var configs = Parse(File.ReadAllLines(Arg("-dsConfigs", "")), settings);
                Run(settings, configs, Arg("-dsOutput", "Docs/Balance/battle-11/sweep"), int.Parse(Arg("-dsSeeds", "150"), Invariant));
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[Difficulty] failed: " + ex);
                EditorApplication.Exit(1);
            }
        }

        public static List<Config> Parse(IEnumerable<string> lines, BattleSettings defaults)
        {
            var list = new List<Config>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var c = new Config
                {
                    Label = parts[0], Perfect = defaults.ResonancePerfect, Good = defaults.ResonanceGood,
                    Cadence = defaults.ResonanceCadence, PlayerHp = defaults.PlayerMaxHp, Health = (int[])defaults.Health.Clone()
                };
                foreach (var part in parts.Skip(1))
                {
                    int eq = part.IndexOf('=');
                    string key = part.Substring(0, eq), value = part.Substring(eq + 1);
                    switch (key)
                    {
                        case "damage":
                            var d = value.Split('/').Select(v => int.Parse(v, Invariant)).ToArray();
                            c.Perfect = d[0]; c.Good = d[1]; c.Cadence = d[2]; break;
                        case "hp": c.Health = value.Split('/').Select(v => int.Parse(v, Invariant)).ToArray(); break;
                        case "playerHp": c.PlayerHp = int.Parse(value, Invariant); break;
                        case "fit":
                            var f = value.Split(':');
                            c.FitTier = f[0]; c.FitDifficulty = f[1]; c.FitTarget = double.Parse(f[2], Invariant); break;
                        case "sens": c.Sensitivity = value == "1"; break;
                        case "attacks": c.Attacks = value == "1"; break;
                        default: throw new ArgumentException("Unknown key '" + key + "' in: " + line);
                    }
                }
                list.Add(c);
            }
            return list;
        }

        public static void Run(BattleSettings baseSettings, List<Config> configs, string outputDir, int seeds)
        {
            Directory.CreateDirectory(outputDir);
            var timer = Stopwatch.StartNew();
            var summary = new List<string>();
            var cells = new List<string>();
            var attacks = new List<string>();
            var trace = new StringBuilder();
            foreach (var config in configs)
            {
                var settings = baseSettings.Copy();
                settings.ResonancePerfect = config.Perfect; settings.ResonanceGood = config.Good; settings.ResonanceCadence = config.Cadence;
                settings.PlayerMaxHp = config.PlayerHp; settings.Health = (int[])config.Health.Clone();
                settings.Validate();
                if (!double.IsNaN(config.FitTarget)) settings.Health = Fit(settings, config, trace);
                var variants = new List<(string name, int[] health)> { ("norm", settings.Health) };
                if (config.Sensitivity)
                {
                    variants.Add(("-10%", Scale(settings.Health, .9)));
                    variants.Add(("+10%", Scale(settings.Health, 1.1)));
                }
                foreach (var (variant, health) in variants)
                {
                    var s = settings.Copy(); s.Health = health; s.Validate();
                    Evaluate(s, config, variant, seeds, summary, cells, config.Attacks && variant == "norm" ? attacks : null, timer);
                }
            }
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(outputDir, "summary.csv"),
                "label,variant,damage,playerHp,health,totalHp,difficulty,tier,runs,clear,death,timeout,stunP10,stunP50,stunP90,leftoverP50,deathNoteP50,hitsMean,healsMean,dpi,cadenceRate,ignitions,perfectRate,stratGap,bestStrategy,worstStrategy,invariantFailures\n"
                + string.Join("\n", summary) + "\n", utf8);
            File.WriteAllText(Path.Combine(outputDir, "cells.csv"),
                "label,variant,difficulty,tier,strategy,runs,clear,death,stunP50\n" + string.Join("\n", cells) + "\n", utf8);
            if (attacks.Count > 0)
                File.WriteAllText(Path.Combine(outputDir, "attacks.csv"),
                    "label,difficulty,tier,impactNote,attack,hits,hitsPerRun\n" + string.Join("\n", attacks) + "\n", utf8);
            File.WriteAllText(Path.Combine(outputDir, "fit.log.txt"), trace.ToString(), utf8);
            UnityEngine.Debug.Log($"[Difficulty] {configs.Count} configs in {timer.Elapsed.TotalSeconds:F1}s -> {outputDir}");
        }

        // Bisects a uniform scale of the config's segments so the fit tier's median stun note (never stunned = +inf)
        // on the fit difficulty is about the target.
        private static int[] Fit(BattleSettings settings, Config config, StringBuilder trace)
        {
            var tier = SkillProfile.Tiers.First(t => t.Name.Equals(config.FitTier, StringComparison.OrdinalIgnoreCase));
            bool heal = config.FitDifficulty.Equals("easy", StringComparison.OrdinalIgnoreCase);
            double lo = .3, hi = 3.5;
            for (int i = 0; i < FitIterations; i++)
            {
                double mid = (lo + hi) / 2;
                var s = settings.Copy(); s.Health = Scale(settings.Health, mid); s.Validate();
                var jobs = new List<Job>();
                foreach (var strategy in Strategies)
                    for (int seed = 0; seed < FitSeeds; seed++)
                        jobs.Add(new Job { settings = s, strategy = strategy, tier = tier, skillSeed = seed, heal = heal });
                var runs = RunJobs(jobs);
                double median = Quantile(runs.Select(r => r.StunnedNote < 0 ? 1e9 : r.StunnedNote), .5);
                trace.AppendLine($"{config.Label} iter {i + 1}: scale {mid:F4} hp {string.Join("/", s.Health)} median {(median >= 1e9 ? "never" : median.ToString("F1", Invariant))}");
                if (median > config.FitTarget) hi = mid; else lo = mid;
            }
            var result = Scale(settings.Health, (lo + hi) / 2);
            trace.AppendLine($"{config.Label} fit -> {string.Join("/", result)} ({result.Sum()})");
            UnityEngine.Debug.Log($"[Difficulty] {config.Label} fit -> {string.Join("/", result)}");
            return result;
        }

        private static void Evaluate(BattleSettings settings, Config config, string variant, int seeds,
            List<string> summary, List<string> cells, List<string> attacks, Stopwatch timer)
        {
            var tiers = SkillProfile.Tiers;
            foreach (var (difficulty, heal) in Difficulties)
            {
                var jobs = new List<Job>();
                foreach (var tier in tiers) foreach (var strategy in Strategies)
                    for (int seed = 0; seed < seeds; seed++)
                        jobs.Add(new Job { settings = settings, strategy = strategy, tier = tier, skillSeed = seed, heal = heal,
                            keep = attacks != null && (tier == SkillProfile.Novice || tier == SkillProfile.Intermediate) });
                var hitLists = new List<(int note, string name)>[jobs.Count];
                var runs = RunJobs(jobs, hitLists);
                for (int t = 0; t < tiers.Length; t++)
                {
                    var tierRuns = new List<SkilledRun>();
                    var clears = new List<(StrategyKind strategy, double clear)>();
                    for (int k = 0; k < Strategies.Length; k++)
                    {
                        var cell = runs.Skip((t * Strategies.Length + k) * seeds).Take(seeds).ToArray();
                        tierRuns.AddRange(cell);
                        double clear = cell.Count(r => r.Run.Cleared) / (double)seeds;
                        clears.Add((Strategies[k], clear));
                        cells.Add(Line(config.Label, variant, difficulty, tiers[t].Name, Strategies[k], seeds, F(clear),
                            F(cell.Count(r => r.Run.Outcome == "Dead") / (double)seeds), F1(Quantile(cell.Where(r => r.StunnedNote >= 0).Select(r => (double)r.StunnedNote), .5))));
                    }
                    var all = tierRuns.ToArray();
                    var stuns = all.Where(r => r.StunnedNote >= 0).Select(r => (double)r.StunnedNote).ToArray();
                    long ignitions = all.Sum(r => (long)r.Ignitions);
                    long judged = all.Sum(r => (long)(r.Perfects + r.Goods + r.Misses));
                    var best = clears.OrderByDescending(c => c.clear).First();
                    var worst = clears.OrderBy(c => c.clear).First();
                    summary.Add(Line(config.Label, variant, $"{config.Perfect}/{config.Good}/{config.Cadence}", config.PlayerHp,
                        string.Join("/", settings.Health), settings.Health.Sum(), difficulty, tiers[t].Name, all.Length,
                        F(clears.Average(c => c.clear)), F(all.Count(r => r.Run.Outcome == "Dead") / (double)all.Length),
                        F(all.Count(r => r.Run.Outcome == "Timeout") / (double)all.Length),
                        F1(Quantile(stuns, .1)), F1(Quantile(stuns, .5)), F1(Quantile(stuns, .9)),
                        F1(Quantile(all.Where(r => r.Run.Cleared).Select(r => (double)r.Run.LeftoverNotes), .5)),
                        F1(Quantile(all.Where(r => r.Run.Outcome == "Dead").Select(r => (double)r.ClosedNotes), .5)),
                        F2(all.Average(r => (double)r.HitsTaken)), F2(all.Average(r => (double)r.Heals)),
                        F2(Mean(all.Where(r => r.Ignitions > 0).Select(r => r.DamagePerIgnition))),
                        F(ignitions == 0 ? double.NaN : all.Sum(r => (long)r.Cadences) / (double)ignitions),
                        F2(all.Average(r => (double)r.Ignitions)),
                        F(judged == 0 ? double.NaN : all.Sum(r => (long)r.Perfects) / (double)judged),
                        F(best.clear - worst.clear), best.strategy, worst.strategy, all.Sum(r => r.Run.InvariantFailureCount),
                        F1(PhaseBegin(all, 0)), F1(PhaseBegin(all, 1)), F1(PhaseBegin(all, 2))));
                    if (attacks != null && (tiers[t] == SkillProfile.Novice || tiers[t] == SkillProfile.Intermediate))
                        CountAttackHits(config.Label, difficulty, tiers[t].Name, all.Length,
                            hitLists.Skip(t * Strategies.Length * seeds).Take(Strategies.Length * seeds), attacks);
                }
                UnityEngine.Debug.Log($"[Difficulty] {config.Label} {variant} {difficulty} done, {timer.Elapsed.TotalSeconds:F1}s");
            }
        }

        // Each player-hit is charged to the published attack whose impact is that note and whose cells cover the
        // player's cell.
        private static List<(int note, string name)> AttackHits(BattleSession battle)
        {
            var list = new List<(int, string)>();
            var published = battle.Events.Where(e => e.type == "pattern-publish").ToList();
            foreach (var hit in battle.Events.Where(e => e.type == "player-hit"))
            {
                int note = hit.noteIndex; string name = "?";
                ulong cell = SafeRoutePlanner.Bit(new GridPosition(hit.x, hit.y));
                foreach (var p in published)
                {
                    if (p.impactNote != hit.noteIndex) continue;
                    if (!ulong.TryParse(p.cells, NumberStyles.HexNumber, Invariant, out ulong bits) || (bits & cell) == 0) continue;
                    name = p.detail; break;
                }
                list.Add((note, name));
            }
            return list;
        }

        // Sorted by hits, top 25.
        private static void CountAttackHits(string label, string difficulty, string tier, int runCount,
            IEnumerable<List<(int note, string name)>> hitLists, List<string> output)
        {
            var counts = new Dictionary<(int note, string name), int>();
            foreach (var list in hitLists)
                if (list != null) foreach (var key in list) { counts.TryGetValue(key, out int c); counts[key] = c + 1; }
            foreach (var kv in counts.OrderByDescending(kv => kv.Value).Take(25))
                output.Add(Line(label, difficulty, tier, kv.Key.note, kv.Key.name, kv.Value, F(kv.Value / (double)runCount)));
        }

        private static SkilledRun[] RunJobs(List<Job> jobs, List<(int note, string name)>[] hitLists = null)
        {
            var results = new SkilledRun[jobs.Count];
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
            Parallel.For(0, jobs.Count, parallel, i =>
            {
                var job = jobs[i];
                var run = SkilledSimulator.RunSkilled(job.settings, BattleSeed, job.skillSeed, job.strategy, job.tier, job.keep, job.heal);
                if (job.keep && hitLists != null)
                {
                    hitLists[i] = AttackHits(run.Run.Battle);
                    run = SkilledSimulator.RunSkilled(job.settings, BattleSeed, job.skillSeed, job.strategy, job.tier, false, job.heal);
                }
                results[i] = run;
            });
            return results;
        }

        // Phases[p].transitionNote is the note at which phase p+1 ended, so phase p+2 begins there.
        private static double PhaseBegin(SkilledRun[] runs, int index)
            => Quantile(runs.Select(r => r.Run.Phases[index].transitionNote).Where(n => n >= 0).Select(n => (double)n), .5);

        private static int[] Scale(int[] health, double scale) => Array.ConvertAll(health, h => Math.Max(1, (int)Math.Round(h * scale)));

        private static double Quantile(IEnumerable<double> values, double q)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return double.NaN;
            double pos = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        private static double Mean(IEnumerable<double> values) { var a = values.ToArray(); return a.Length == 0 ? double.NaN : a.Average(); }
        private static string F(double v) => double.IsNaN(v) ? "" : v.ToString("F4", Invariant);
        private static string F1(double v) => double.IsNaN(v) ? "" : v.ToString("F1", Invariant);
        private static string F2(double v) => double.IsNaN(v) ? "" : v.ToString("F2", Invariant);
        private static string Line(params object[] values)
            => string.Join(",", values.Select(v =>
            {
                string s = Convert.ToString(v, Invariant) ?? "";
                return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            }));
    }
}
