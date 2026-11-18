using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StringAsASword.Combat;
using StringAsASword.Rhythm;
using StringAsASword.Simulation;
using StringAsASword.Stage;
using UnityEditor;

namespace StringAsASword.Editor
{
    public static class BattleAnalysis
    {
        // For background CLI work load settings on the Editor thread, then use the settings overload.
        public static string Run(string output, int count = 1000)
        {
            var settings = LoadSettings();
            return Run(settings, output, count);
        }

        public static BattleSettings LoadSettings()
        {
            var song = AssetDatabase.FindAssets("t:SongDefinition").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<SongDefinition>).First();
            var stage = AssetDatabase.FindAssets("t:StageDefinition").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<StageDefinition>).First();
            return BattleSettings.From(song, stage);
        }

        public static string Run(BattleSettings settings, string output, int count = 1000)
        {
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("An output directory is required.");
            Directory.CreateDirectory(output);
            string logs = Path.Combine(output, "logs");
            Directory.CreateDirectory(logs);
            CalibrationResult result;
            using (var diagnostics = new StreamWriter(Path.Combine(output, "run-diagnostics.csv"), false, Encoding.UTF8))
            {
                diagnostics.WriteLine("set,seed,strategy,kind,reason,count");
                result = BattleCalibration.Calibrate(settings, count, (run, set) =>
                {
                    BattleLog.Write(Path.Combine(logs, $"{set}-{run.Seed}-{run.Strategy}.jsonl.gz"), run.Battle);
                    foreach (var reason in run.EventReasons.OrderBy(p => p.Key))
                        diagnostics.WriteLine(string.Join(",", set, run.Seed, run.Strategy, "event", reason.Key, reason.Value));
                    foreach (var failure in run.InvariantFailures.OrderBy(p => p.Key))
                        diagnostics.WriteLine(string.Join(",", set, run.Seed, run.Strategy, "invariant", failure.Key, failure.Value));
                });
            }
            WriteRuns(Path.Combine(output, "phase-samples.csv"), result);
            WriteDistributions(Path.Combine(output, "phase-distributions.csv"), result);
            File.WriteAllLines(Path.Combine(output, "calibration-iterations.txt"), result.Iterations);
            var report = Report(settings, result, count);
            File.WriteAllText(Path.Combine(output, "BattleCalibration.md"), report);
            File.WriteAllText(Path.Combine(output, "health.txt"), string.Join(",", result.Health));
            return report;
        }

        private static IEnumerable<(string Name, List<BattleRun> Runs)> Sets(CalibrationResult result)
        {
            yield return ("analysis", result.Analysis);
            yield return ("calibration", result.Actual);
            yield return ("holdout", result.Holdout);
            yield return ("miss5", result.Sensitivity);
        }

        private static void WriteRuns(string path, CalibrationResult result)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("set,seed,strategy,phase,move,guard,effective_attack,other,attack_opportunity,transition_note,goal_remaining,outcome,final_phase,player_hp,boss_hp,leftover_notes,input_misses,noise_capacity,noise_deferred,noise_fallback,noise_fallback_deferred,noise_no_safe_candidate,pattern_defer,invariant_failures,simulation_ms");
                foreach (var set in Sets(result)) foreach (var run in set.Runs) foreach (var phase in run.Phases)
                    writer.WriteLine(string.Join(",", set.Name, run.Seed, run.Strategy, phase.phase,
                        phase.move, phase.guard, phase.effectiveAttack, phase.other, phase.attackOpportunity,
                        phase.transitionNote, phase.goalRemaining, run.Outcome, run.FinalPhase,
                        run.FinalHp, run.BossHp, run.LeftoverNotes, run.InputMisses,
                        ReasonCount(run, "noise-capacity"), ReasonCount(run, "noise-deferred"),
                        ReasonCount(run, "noise-fallback"), ReasonCount(run, "noise-fallback-deferred"),
                        ReasonCount(run, "noise-no-safe-candidate"), ReasonCount(run, "pattern-defer"),
                        run.InvariantFailureCount, run.ElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture)));
            }
        }

        private static int ReasonCount(BattleRun run, string reason)
            => run.EventReasons.TryGetValue(reason, out int count) ? count : 0;

        private static void WriteDistributions(string path, CalibrationResult result)
        {
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("set,strategy,phase,metric,count,min,p10,p50,p90,max,mean");
                foreach (var set in Sets(result)) foreach (StrategyKind strategy in BattleStrategies.Legacy)
                {
                    var runs = set.Runs.Where(r => r.Strategy == strategy).ToArray();
                    if (runs.Length == 0) continue;
                    for (int phase = 0; phase < 4; phase++)
                    {
                        int index = phase;
                        WriteMetric(writer, set.Name, strategy, phase + 1, "move", runs.Select(r => r.Phases[index].move));
                        WriteMetric(writer, set.Name, strategy, phase + 1, "guard", runs.Select(r => r.Phases[index].guard));
                        WriteMetric(writer, set.Name, strategy, phase + 1, "effective_attack", runs.Select(r => r.Phases[index].effectiveAttack));
                        WriteMetric(writer, set.Name, strategy, phase + 1, "other", runs.Select(r => r.Phases[index].other));
                        WriteMetric(writer, set.Name, strategy, phase + 1, "attack_opportunity", runs.Select(r => r.Phases[index].attackOpportunity));
                        WriteMetric(writer, set.Name, strategy, phase + 1, "transition_note", runs.Select(r => r.Phases[index].transitionNote));
                    }
                    WriteMetric(writer, set.Name, strategy, 0, "leftover_notes", runs.Select(r => r.LeftoverNotes));
                }
            }
        }

        private static void WriteMetric(StreamWriter writer, string set, StrategyKind strategy, int phase,
            string metric, IEnumerable<int> observations)
        {
            var values = observations.ToArray();
            writer.WriteLine(string.Join(",", set, strategy, phase, metric, values.Length,
                values.Min(), BattleCalibration.Percentile(values, .1), BattleCalibration.Percentile(values, .5),
                BattleCalibration.Percentile(values, .9), values.Max(), values.Average().ToString("F3", CultureInfo.InvariantCulture)));
        }

        private static string Report(BattleSettings settings, CalibrationResult result, int count)
        {
            var text = new StringBuilder();
            text.AppendLine("# Battle calibration").AppendLine();
            text.AppendLine($"Combat {settings.Version}; {settings.NoteCount} notes; {settings.Bpm.ToString(CultureInfo.InvariantCulture)} BPM; player HP {settings.PlayerMaxHp}.");
            text.AppendLine($"Analysis only intervals: [0,100), [100,148), [148,216), [216,344). Actual runs use HP and pickup gates.");
            text.AppendLine($"Calibration seeds 0..{count - 1}; independent holdout 100000..{100000 + count - 1}; three deterministic observation-only strategies.");
            text.AppendLine("CollectFirst alone supplies P10 and the 90% target; all three strategies are reported. P10 uses nearest rank ceil(0.10*N), including deaths and unfinished phases. HP = floor(P10*0.875); a candidate below one stops calibration. No second miss deduction. The 5% input omission experiment is separate.");
            text.AppendLine($"Final HP: **{string.Join(", ", result.Health)}**. Calibration target (CollectFirst actual clear >=90%): **{(result.MeetsTarget ? "met" : "not met")}**.");
            text.AppendLine($"Executed {result.ExecutedRuns} battles in {result.ElapsedSeconds.ToString("F2", CultureInfo.InvariantCulture)} seconds including compressed log export; invariant failures: **{result.TotalInvariantFailures}**.");
            if (result.InvalidCandidate != null) text.AppendLine("**Invalid HP candidate; actual and holdout runs were not started.** " + result.InvalidCandidate);
            text.AppendLine().AppendLine("| Set | Strategy | Runs | Clears | Deaths | Timeouts | Analysis goals unfinished | Leftover P10 / P50 / P90 |");
            text.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");
            foreach (var set in Sets(result)) foreach (StrategyKind strategy in BattleStrategies.Legacy)
            {
                var runs = set.Runs.Where(r => r.Strategy == strategy).ToArray();
                if (runs.Length == 0) continue;
                var left = runs.Select(r => r.LeftoverNotes).ToArray();
                string rate = (runs.Count(r => r.Cleared) / (double)runs.Length).ToString("P1", CultureInfo.InvariantCulture);
                text.AppendLine($"| {set.Name} | {strategy} | {runs.Length} | {rate} | {runs.Count(r => r.Failure == "Dead")} | {runs.Count(r => r.Failure == "Timeout")} | {runs.Count(r => r.AnalysisGoalFailed)} | {BattleCalibration.Percentile(left, .1)} / {BattleCalibration.Percentile(left, .5)} / {BattleCalibration.Percentile(left, .9)} |");
            }
            text.AppendLine().AppendLine("## Iterations").AppendLine();
            foreach (var iteration in result.Iterations) text.AppendLine("- " + iteration);
            text.AppendLine().AppendLine("## Data and interpretation").AppendLine();
            text.AppendLine("phase-samples.csv retains every seed and phase, including failures and unvisited phases (zero opportunities; transition -1). phase-distributions.csv gives min/P10/P50/P90/max/mean for move, guard, effective attack, other, attack opportunity, transition note and leftover notes. Transition notes are zero-based; -1 means no transition.");
            text.AppendLine("Every execution, including each tuning iteration, has a compressed JSONL file in logs/. Its set label, seed and strategy identify the run. run-diagnostics.csv preserves event reason counts and invariant failures for every execution; phase-samples.csv also carries capacity/defer/fallback counts and simulation time. Holdout results never tune HP.");
            text.AppendLine("CollectFirst prioritizes a disclosed pickup trip. AttackHold guards in place against imminent damage and attacks until the remaining attack/collection/spawn budget becomes urgent. MovementUse takes cheap disclosed pickup trips during ordinary play, uses safe evasive movement to collect, and uses the remaining budget for longer trips. All choices use copied current observations and fixed tie ordering; no strategy reads the future random stream.");
            text.AppendLine("HP-only reductions apply only to timeout runs with the current pickup goal complete and current HP remaining, at most 10% of that phase per iteration, at most eight actual runs. Death, route and pickup failures are retained and cannot be cured by arbitrary HP cuts. Integer HP below ten is not reduced when one HP would exceed ten percent.");
            return text.ToString();
        }
    }
}
