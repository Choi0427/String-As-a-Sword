using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringAsASword.Combat;
using StringAsASword.Simulation;
using UnityEngine;

namespace StringAsASword.Editor
{
    // Measures only: nothing here tunes settings. LoadSettings needs the Editor thread; the settings overload does not.
    public static class BattleActionMixReport
    {
        private const string AllStrategies = "ALL";
        private const int ChunkSeeds = 32, ReplaySeeds = 2;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Responses = Enum.GetNames(typeof(AttackResponse));

        [Serializable] public sealed class NamedRate { public string name; public double mean; }
        [Serializable] public sealed class GroupResult
        {
            public string group, strategy;
            public int runs, clears, deaths, timeouts, invariantFailures;
            public double clearRate, deathRate, attackShare, moveShare, guardShare;
            public List<BattlePacingComparison.Distribution> distributions = new List<BattlePacingComparison.Distribution>();
            public List<NamedRate> patterns = new List<NamedRate>();
            public BattlePacingComparison.Distribution Metric(string name) => distributions.First(d => d.metric == name);
        }
        [Serializable] public sealed class Criterion { public string name, actual, target; public bool passed; }
        [Serializable] public sealed class Summary
        {
            public string version, chartHash;
            public int noteCount, seedsPerGroup, executedRuns, invariantFailures, replayed, replayFailures;
            public double elapsedSeconds;
            public bool passed;
            public List<string> replayErrors = new List<string>();
            public List<Criterion> criteria = new List<Criterion>();
            public List<GroupResult> groups = new List<GroupResult>();
        }
        private sealed class Sample
        {
            public int seed, attack, move, guard, other, guardSuccess, guardBroken, hits, leftover, invariantFailures;
            public StrategyKind strategy;
            public string outcome;
            public readonly int[] published = new int[Responses.Length], fired = new int[Responses.Length];
            public readonly Dictionary<string, int> patterns = new Dictionary<string, int>();
        }

        public static string Run(string outputDir, int seedsPerGroup = 1000)
            => Run(BattleAnalysis.LoadSettings(), outputDir, seedsPerGroup);

        public static string Run(BattleSettings settings, string outputDir, int seedsPerGroup = 1000)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (seedsPerGroup < 1 || seedsPerGroup > 100000) throw new ArgumentOutOfRangeException(nameof(seedsPerGroup));
            if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("An output directory is required.");
            settings = settings.Copy();
            settings.Validate();
            Directory.CreateDirectory(outputDir);
            var timer = Stopwatch.StartNew();
            var summary = new Summary { version = settings.Version, chartHash = BattleLog.ChartHash(settings),
                noteCount = settings.NoteCount, seedsPerGroup = seedsPerGroup };
            foreach (var (name, firstSeed, missRate) in new[] { ("development", 0, 0d), ("holdout", 100000, 0d), ("miss5", 100000, .05) })
            {
                var samples = Collect(settings, firstSeed, seedsPerGroup, missRate, name);
                summary.executedRuns += samples.Count;
                summary.invariantFailures += samples.Sum(s => s.invariantFailures);
                foreach (StrategyKind strategy in Enum.GetValues(typeof(StrategyKind)))
                    summary.groups.Add(Summarize(name, strategy.ToString(), samples.Where(s => s.strategy == strategy).ToArray()));
                summary.groups.Add(Summarize(name, AllStrategies, samples.ToArray()));
                CheckReplays(settings, outputDir, name, firstSeed, Math.Min(ReplaySeeds, seedsPerGroup), missRate, summary);
            }
            Evaluate(summary);
            summary.elapsedSeconds = timer.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(outputDir, "summary.json"), JsonUtility.ToJson(summary, true));
            WriteCsv(Path.Combine(outputDir, "action-mix.csv"), summary);
            return Report(summary);
        }

        // Batch drops each session after the sink, so only these counts outlive a run.
        private static List<Sample> Collect(BattleSettings settings, int firstSeed, int count, double missRate, string label)
        {
            var samples = new ConcurrentBag<Sample>();
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
            Parallel.For(0, (count + ChunkSeeds - 1) / ChunkSeeds, parallel, chunk =>
                BattleSimulator.Batch(settings, firstSeed + chunk * ChunkSeeds, Math.Min(ChunkSeeds, count - chunk * ChunkSeeds),
                    missRate: missRate, sink: (run, _) => samples.Add(Measure(run)), label: label));
            return samples.OrderBy(s => s.seed).ThenBy(s => s.strategy).ToList();
        }

        private static Sample Measure(BattleRun run)
        {
            var sample = new Sample { seed = run.Seed, strategy = run.Strategy, outcome = run.Outcome,
                leftover = run.LeftoverNotes, invariantFailures = run.InvariantFailureCount };
            foreach (var record in run.Battle.Records)
            {
                switch (record.category)
                {
                    case "EffectiveAttack": sample.attack++; break;
                    case "Move": sample.move++; break;
                    case "Guard": sample.guard++; break;
                    default: sample.other++; break;
                }
                if (record.guardSuccess) sample.guardSuccess++;
                if (record.hit) sample.hits++;
            }
            foreach (var e in run.Battle.Events)
            {
                if (e.type == "guard-broken") sample.guardBroken++;
                if (e.hit != 0 || e.type != "pattern-publish" && e.type != "pattern-fire") continue;
                int response = Array.IndexOf(Responses, e.response);
                if (e.type == "pattern-publish") { sample.published[response]++; continue; }
                sample.fired[response]++;
                // Row and column names carry a random coordinate; both come from one Line definition.
                string name = e.kind == nameof(PatternKind.Row) || e.kind == nameof(PatternKind.Column) ? "Line" : e.detail;
                sample.patterns.TryGetValue(name, out int count);
                sample.patterns[name] = count + 1;
            }
            return sample;
        }

        private static GroupResult Summarize(string group, string strategy, Sample[] samples)
        {
            double attack = samples.Average(s => s.attack), move = samples.Average(s => s.move), guard = samples.Average(s => s.guard);
            double total = Math.Max(attack + move + guard, 1e-9);
            var result = new GroupResult { group = group, strategy = strategy, runs = samples.Length,
                clears = samples.Count(s => s.outcome == "Won"), deaths = samples.Count(s => s.outcome == "Dead"),
                timeouts = samples.Count(s => s.outcome == "Timeout"), invariantFailures = samples.Sum(s => s.invariantFailures),
                attackShare = attack / total, moveShare = move / total, guardShare = guard / total };
            result.clearRate = result.clears / (double)result.runs;
            result.deathRate = result.deaths / (double)result.runs;
            void Add(string metric, Func<Sample, int> value)
            {
                var values = samples.Select(value).ToArray();
                result.distributions.Add(new BattlePacingComparison.Distribution { metric = metric, count = values.Length,
                    min = values.Min(), p10 = BattleCalibration.Percentile(values, .1), p50 = BattleCalibration.Percentile(values, .5),
                    p90 = BattleCalibration.Percentile(values, .9), max = values.Max(), mean = values.Average() });
            }
            Add("attack", s => s.attack); Add("move", s => s.move); Add("guard", s => s.guard); Add("other", s => s.other);
            Add("guard_success", s => s.guardSuccess); Add("guard_broken", s => s.guardBroken); Add("hits", s => s.hits);
            Add("leftover_notes", s => s.leftover);
            for (int i = 0; i < Responses.Length; i++)
            {
                int index = i;
                Add("published_" + Responses[i], s => s.published[index]);
                Add("fired_" + Responses[i], s => s.fired[index]);
            }
            foreach (string name in samples.SelectMany(s => s.patterns.Keys).Distinct().OrderBy(n => n, StringComparer.Ordinal))
                result.patterns.Add(new NamedRate { name = name,
                    mean = samples.Average(s => s.patterns.TryGetValue(name, out int count) ? count : 0) });
            return result;
        }

        private static void CheckReplays(BattleSettings settings, string outputDir, string group, int firstSeed, int seeds, double missRate, Summary summary)
        {
            string directory = Path.Combine(outputDir, "replay");
            foreach (StrategyKind strategy in Enum.GetValues(typeof(StrategyKind)))
                for (int seed = firstSeed; seed < firstSeed + seeds; seed++)
                {
                    string path = Path.Combine(directory, $"{group}-{strategy}-{seed}.jsonl.gz");
                    try
                    {
                        var live = BattleSimulator.Run(settings, seed, strategy, missRate: missRate).Battle;
                        BattleLog.Write(path, live);
                        var replay = BattleLog.Replay(path);
                        if (!replay.Records.Select(r => r.stateHash).SequenceEqual(live.Records.Select(r => r.stateHash)) ||
                            !replay.Events.Select(JsonUtility.ToJson).SequenceEqual(live.Events.Select(JsonUtility.ToJson)))
                            throw new InvalidDataException("Replay differs from the live run.");
                    }
                    catch (Exception ex)
                    {
                        summary.replayFailures++;
                        summary.replayErrors.Add(Path.GetFileName(path) + ": " + ex.Message);
                    }
                    summary.replayed++;
                }
        }

        // The plan's targets. Rates use the three-bot mean and the independent seeds, as the plan's section does.
        private static void Evaluate(Summary summary)
        {
            GroupResult Find(string group, string strategy) => summary.groups.First(g => g.group == group && g.strategy == strategy);
            void Check(string name, string actual, string target, bool passed)
                => summary.criteria.Add(new Criterion { name = name, actual = actual, target = target, passed = passed });
            void Shares(string label, GroupResult g, double tolerance)
            {
                string actual = $"{Percent(g.attackShare)}/{Percent(g.moveShare)}/{Percent(g.guardShare)}";
                bool passed = Math.Abs(g.attackShare - .43) <= tolerance && Math.Abs(g.moveShare - .43) <= tolerance && Math.Abs(g.guardShare - .14) <= tolerance;
                Check(label, actual, $"43/43/14 +-{Number(tolerance * 100, "F0")}pp", passed);
            }
            var holdout = Find("holdout", AllStrategies);
            Shares("share three-bot mean", holdout, .04);
            foreach (StrategyKind strategy in Enum.GetValues(typeof(StrategyKind)))
                Shares("share " + strategy, Find("holdout", strategy.ToString()), .08);
            Check("clear no-miss", Percent(holdout.clearRate), ">=90%", holdout.clearRate >= .9);
            var miss = Find("miss5", AllStrategies);
            Check("clear miss5", Percent(miss.clearRate), ">=80%", miss.clearRate >= .8);
            double leftover = holdout.Metric("leftover_notes").p50;
            Check("leftover median", Number(leftover, "F0"), "10..50", leftover >= 10 && leftover <= 50);
            foreach (var response in new[] { AttackResponse.DodgeOnly, AttackResponse.GuardOnly })
            {
                double p10 = holdout.Metric("fired_" + response).p10;
                Check(response + " fired P10", Number(p10, "F0"), ">=8", p10 >= 8);
            }
            Check("invariant failures", summary.invariantFailures.ToString(Invariant), "0", summary.invariantFailures == 0);
            Check("replay failures", $"{summary.replayFailures}/{summary.replayed}", "0", summary.replayFailures == 0);
            summary.passed = summary.criteria.All(c => c.passed);
        }

        private static void WriteCsv(string path, Summary summary)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("group,strategy,metric,count,mean,p10,p50,p90,min,max");
                void Row(params object[] values) => writer.WriteLine(string.Join(",", values.Select(v =>
                {
                    string s = Convert.ToString(v, Invariant) ?? "";
                    return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
                })));
                foreach (var g in summary.groups)
                {
                    foreach (var d in g.distributions) Row(g.group, g.strategy, d.metric, d.count, d.mean, d.p10, d.p50, d.p90, d.min, d.max);
                    foreach (var (metric, value) in new[] { ("attack_share", g.attackShare), ("move_share", g.moveShare), ("guard_share", g.guardShare),
                        ("clear_rate", g.clearRate), ("death_rate", g.deathRate), ("clears", g.clears), ("deaths", g.deaths),
                        ("timeouts", g.timeouts), ("invariant_failures", (double)g.invariantFailures) })
                        Row(g.group, g.strategy, metric, g.runs, value, "", "", "", "", "");
                    foreach (var p in g.patterns) Row(g.group, g.strategy, "pattern:" + p.name, g.runs, p.mean, "", "", "", "", "");
                }
            }
        }

        private static string Report(Summary summary)
        {
            var text = new StringBuilder();
            text.AppendLine($"Battle action mix: {summary.version}, {summary.noteCount} notes, {summary.seedsPerGroup} seeds per group, {summary.executedRuns} runs, {Number(summary.elapsedSeconds, "F1")}s.");
            text.AppendLine("Shares are attack/move/guard among those three actions. Counts are mean (P10/P50/P90) per run. Fired counts each attack's first hit.");
            foreach (var g in summary.groups)
            {
                string Spread(string metric)
                {
                    var d = g.Metric(metric);
                    return $"{Number(d.mean, "F1")} ({Number(d.p10, "F0")}/{Number(d.p50, "F0")}/{Number(d.p90, "F0")})";
                }
                string Fired(string response) => $"{Number(g.Metric("fired_" + response).p10, "F0")}/{Number(g.Metric("fired_" + response).mean, "F1")}";
                text.AppendLine($"{g.group} {g.strategy}: share {Percent(g.attackShare)}/{Percent(g.moveShare)}/{Percent(g.guardShare)}; " +
                    $"attack {Spread("attack")} move {Spread("move")} guard {Spread("guard")} other {Spread("other")}; guard ok {Number(g.Metric("guard_success").mean, "F1")}; " +
                    $"clear {Percent(g.clearRate)} won/dead/timeout {g.clears}/{g.deaths}/{g.timeouts}; hits {Number(g.Metric("hits").mean, "F2")}; leftover {Spread("leftover_notes")}; " +
                    $"fired P10/mean Either {Fired("Either")} Guard {Fired("GuardOnly")} Dodge {Fired("DodgeOnly")}; invariants {g.invariantFailures}");
                if (g.strategy == AllStrategies)
                    text.AppendLine("  patterns fired per run: " + string.Join(", ", g.patterns.Select(p => $"{p.name} {Number(p.mean, "F1")}")));
            }
            text.AppendLine("Plan criteria:");
            foreach (var c in summary.criteria)
                text.AppendLine($"  {(c.passed ? "PASS" : "FAIL")} {c.name}: {c.actual} (target {c.target})");
            foreach (string error in summary.replayErrors) text.AppendLine("  replay error: " + error);
            text.AppendLine(summary.passed ? "Overall: PASS" : "Overall: FAIL");
            return text.ToString();
        }

        private static string Percent(double share) => (share * 100).ToString("F1", Invariant);
        private static string Number(double value, string format) => value.ToString(format, Invariant);
    }
}
