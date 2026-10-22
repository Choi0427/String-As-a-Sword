using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using StringAsASword.Combat;
using StringAsASword.Simulation;
using UnityEngine;

namespace StringAsASword.Editor
{
    // LoadSettings uses AssetDatabase on the Editor thread; Run only consumes copied plain data.
    public static class BattlePacingComparison
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Configurations = { "A-original", "B-half-hp", "C-random-half-hp" };

        [Serializable] public sealed class Distribution
        {
            public string metric;
            public int phase, count;
            public double min, p10, p50, p90, max, mean;
        }
        [Serializable] public sealed class GroupSummary
        {
            public string set, configuration, strategy, configHash;
            public int runs, clears, deaths, timeouts, goalUnfinished, invariantFailures;
            public List<Distribution> distributions = new List<Distribution>();
        }
        [Serializable] public sealed class Summary
        {
            public string version, chartHash, startedUtc, completedUtc;
            public int seedsPerGroup, executedRuns, invariantFailures, savedLogs, replayedLogs, replayFailures;
            public double elapsedSeconds;
            public List<string> replayErrors = new List<string>();
            public List<GroupSummary> groups = new List<GroupSummary>();
        }
        private sealed class PhaseMetrics
        {
            public int move, guard, attack, other, opportunity, damage, hits, guardSuccess, dodgeCollect;
            public int effectiveStreak, attackStreak, transition = -1, goalRemaining;
        }
        private sealed class Sample
        {
            public RunTotals run;
            public PhaseMetrics[] phases = Enumerable.Range(0, 4).Select(_ => new PhaseMetrics()).ToArray();
            public int processed, hits, guardSuccess, dodgeCollect, effectiveStreak, attackStreak, fires;
            public int patternDeferred, patternRejected, patternSkipped, noiseDeferred, noiseFallback, noiseUnavailable;
            public int longGaps, shortGapStreak, longGapStreak, minGap = -1, maxGap = -1;
        }
        private sealed class RunTotals
        {
            public int Seed, FinalPhase, FinalHp, BossHp, GoalRemaining, LeftoverNotes, InputMisses, InvariantFailureCount;
            public string Outcome;
            public double ElapsedMilliseconds;
            public bool Cleared => Outcome == "Won";
        }
        private sealed class Group
        {
            public string Set, Configuration;
            public StrategyKind Strategy;
            public BattleSettings Settings;
            public List<Sample> Samples = new List<Sample>();
            public Dictionary<string, int> Frequencies = new Dictionary<string, int>();
        }

        public static string Run(string output, int count = 1000)
            => Run(BattleAnalysis.LoadSettings(), output, count);

        public static string Run(BattleSettings source, string output, int count = 1000)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (count < 1 || count > 100000) throw new ArgumentOutOfRangeException(nameof(count));
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("An output directory is required.");
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Use a new or empty comparison output directory to preserve earlier results.");
            SelfCheck();
            source = source.Copy();
            if (!source.Schedule.Enabled) throw new ArgumentException("Configuration C must load an enabled random schedule.");
            if (!source.Health.SequenceEqual(new[] { 31, 6, 12, 47 }))
                throw new ArgumentException("Configuration C must load the requested fixed HP 31/6/12/47.");
            source.Validate();
            Directory.CreateDirectory(output);
            string logs = Path.Combine(output, "logs");
            Directory.CreateDirectory(logs);
            var summary = new Summary { version = source.Version, chartHash = BattleLog.ChartHash(source),
                startedUtc = DateTime.UtcNow.ToString("O", Invariant), seedsPerGroup = count };
            var timer = Stopwatch.StartNew();
            var groups = new List<Group>();
            using (var runs = CompressedWriter(Path.Combine(output, "runs.csv.gz")))
            using (var phases = CompressedWriter(Path.Combine(output, "phases.csv.gz")))
            using (var events = CompressedWriter(Path.Combine(output, "events.csv.gz")))
            {
                runs.WriteLine("set,configuration,strategy,seed,outcome,processed_notes,remaining_notes,clear_note,final_phase,player_hp,boss_hp,goal_remaining,input_omissions,hits,guard_success,dodge_collect,max_effective_attack_streak,max_attack_action_streak,boss_fires,min_actual_gap,max_actual_gap,gaps_over_16,max_short_gap_streak,max_long_gap_streak,pattern_deferred,pattern_rejected,pattern_skipped,noise_deferred,noise_fallback,noise_unavailable,invariant_failures,simulation_ms");
                phases.WriteLine("set,configuration,strategy,seed,phase,move,guard,effective_attack,other,attack_opportunity,actual_damage,hits,guard_success,dodge_collect,max_effective_attack_streak,max_attack_action_streak,transition_note,goal_remaining,outcome");
                events.WriteLine("set,configuration,strategy,seed,type,note_index,music_beat,phase,pattern_id,kind,cells,impact_note,warning_notes,required_dodge_actions,selected_interval,actual_interval,reason,detail");
                foreach (string set in new[] { "development", "holdout", "miss5" })
                foreach (string configuration in Configurations)
                foreach (StrategyKind strategy in Enum.GetValues(typeof(StrategyKind)))
                {
                    var group = new Group { Set = set, Configuration = configuration, Strategy = strategy,
                        Settings = Configuration(source, configuration) };
                    groups.Add(group);
                    int firstSeed = set == "development" ? 0 : 100000;
                    bool replayedFailure = false;
                    const int batchSize = 32;
                    var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount) };
                    for (int start = 0; start < count; start += batchSize)
                    {
                        int batchStart = start;
                        var batch = new BattleRun[Math.Min(batchSize, count - batchStart)];
                        Parallel.For(0, batch.Length, parallel, offset =>
                            batch[offset] = BattleSimulator.Run(group.Settings, firstSeed + batchStart + offset,
                                strategy, missRate: set == "miss5" ? .05 : 0));
                        // Serialization and aggregation remain ordered; at most 32 sessions survive a batch.
                        for (int offset = 0; offset < batch.Length; offset++)
                        {
                            int index = batchStart + offset;
                            var run = batch[offset];
                            var sample = Measure(run);
                            WriteRun(runs, phases, group, sample);
                            WriteEvents(events, group, run);
                            summary.executedRuns++;
                            summary.invariantFailures += run.InvariantFailureCount;
                            bool failed = !run.Cleared || run.InvariantFailureCount > 0;
                            bool representative = index < 3 || index % 100 == 0;
                            if (failed || representative)
                            {
                                string path = Path.Combine(logs, $"{set}-{configuration}-{strategy}-{run.Seed}.jsonl.gz");
                                BattleLog.Write(path, run.Battle);
                                summary.savedLogs++;
                                if (representative || failed && !replayedFailure)
                                {
                                    try
                                    {
                                        var replay = BattleLog.Replay(path);
                                        if (EventHash(replay) != EventHash(run.Battle))
                                            throw new InvalidDataException("Replay event stream differs.");
                                        summary.replayedLogs++;
                                    }
                                    catch (Exception ex)
                                    {
                                        summary.replayFailures++;
                                        summary.replayErrors.Add(Path.GetFileName(path) + ": " + ex.Message);
                                    }
                                    if (failed) replayedFailure = true;
                                }
                            }
                            group.Samples.Add(sample);
                            if (summary.executedRuns % 100 == 0)
                                File.WriteAllText(Path.Combine(output, "progress.txt"),
                                    $"{summary.executedRuns}/{27 * count}; {set}; {configuration}; {strategy}; seed={run.Seed}; seconds={timer.Elapsed.TotalSeconds:F1}");
                            batch[offset] = null;
                        }
                    }
                    summary.groups.Add(Summarize(group));
                }
            }
            summary.elapsedSeconds = timer.Elapsed.TotalSeconds;
            summary.completedUtc = DateTime.UtcNow.ToString("O", Invariant);
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonUtility.ToJson(summary, true));
            WriteDistributions(output, summary, groups);
            string report = Report(summary);
            File.WriteAllText(Path.Combine(output, "BattlePacingComparison.md"), report);
            File.WriteAllText(Path.Combine(output, "progress.txt"), $"Completed {summary.executedRuns}/{27 * count}; {summary.elapsedSeconds:F1} seconds");
            return report;
        }

        private static BattleSettings Configuration(BattleSettings source, string name)
        {
            var settings = source.Copy();
            if (name != Configurations[2])
            {
                settings.Schedule.Enabled = false;
                settings.Patterns = PatternDefinition.Defaults();
                settings.Health = name == Configurations[0] ? new[] { 62, 12, 24, 94 } : new[] { 31, 6, 12, 47 };
            }
            settings.Validate();
            return settings;
        }

        private static Sample Measure(BattleRun run)
        {
            var result = new Sample { processed = run.Battle.Records.Count, run = new RunTotals {
                Seed = run.Seed, FinalPhase = run.FinalPhase, FinalHp = run.FinalHp, BossHp = run.BossHp,
                GoalRemaining = run.GoalRemaining, LeftoverNotes = run.LeftoverNotes, InputMisses = run.InputMisses,
                InvariantFailureCount = run.InvariantFailureCount, Outcome = run.Outcome,
                ElapsedMilliseconds = run.ElapsedMilliseconds } };
            var danger = new Dictionary<int, ulong>();
            var fireNotes = new List<int>();
            foreach (var e in run.Battle.Events)
            {
                switch (e.type)
                {
                    case "pattern-fire":
                        result.fires++;
                        danger.TryGetValue(e.impactNote, out ulong cells);
                        danger[e.impactNote] = cells | ulong.Parse(e.cells, NumberStyles.HexNumber, Invariant);
                        fireNotes.Add(e.impactNote);
                        break;
                    case "pattern-defer": result.patternDeferred++; break;
                    case "pattern-reject": result.patternRejected++; break;
                    case "pattern-skip": result.patternSkipped++; break;
                    case "noise-deferred": case "noise-fallback-deferred": result.noiseDeferred++; break;
                    case "noise-fallback": result.noiseFallback++; break;
                    case "noise-no-safe-candidate": result.noiseUnavailable++; break;
                }
            }
            int previousPhase = 0, collected = 0, effective = 0, attack = 0;
            int phaseEffective = 0, phaseAttack = 0;
            foreach (var record in run.Battle.Records)
            {
                if (record.phase != previousPhase)
                { collected = phaseEffective = phaseAttack = 0; previousPhase = record.phase; }
                var phase = result.phases[record.phase - 1];
                switch (record.category)
                {
                    case "Move": phase.move++; break;
                    case "Guard": phase.guard++; break;
                    case "EffectiveAttack": phase.attack++; break;
                    default: phase.other++; break;
                }
                phase.opportunity += record.attackOpportunity ? 1 : 0;
                phase.damage += record.damage;
                if (record.hit) { result.hits++; phase.hits++; }
                if (record.guardSuccess) { result.guardSuccess++; phase.guardSuccess++; }
                bool isAttack = record.category == "EffectiveAttack" || record.reason == "Attack" ||
                    record.reason == "HpFloor" || record.reason == "OutOfRange";
                effective = record.category == "EffectiveAttack" ? effective + 1 : 0;
                phaseEffective = record.category == "EffectiveAttack" ? phaseEffective + 1 : 0;
                attack = isAttack ? attack + 1 : 0;
                phaseAttack = isAttack ? phaseAttack + 1 : 0;
                result.effectiveStreak = Math.Max(result.effectiveStreak, effective);
                result.attackStreak = Math.Max(result.attackStreak, attack);
                phase.effectiveStreak = Math.Max(phase.effectiveStreak, phaseEffective);
                phase.attackStreak = Math.Max(phase.attackStreak, phaseAttack);
                danger.TryGetValue(record.noteIndex, out ulong mask);
                if (record.category == "Move" && record.collected > collected &&
                    (mask & (1UL << (record.beforeY * 8 + record.beforeX))) != 0 &&
                    (mask & (1UL << (record.y * 8 + record.x))) == 0)
                { result.dodgeCollect++; phase.dodgeCollect++; }
                collected = record.collected;
            }
            for (int i = 0; i < 4; i++)
            {
                result.phases[i].transition = run.Phases[i].transitionNote < 0 ? -1 : run.Phases[i].transitionNote + 1;
                result.phases[i].goalRemaining = i + 1 > run.FinalPhase && i < 3
                    ? run.Battle.Settings.NoiseTargets[i] : run.Phases[i].goalRemaining;
            }
            int previous = -1, shortStreak = 0, longStreak = 0;
            foreach (int note in fireNotes.Distinct().OrderBy(n => n))
            {
                if (previous >= 0)
                {
                    int gap = note - previous;
                    result.minGap = result.minGap < 0 ? gap : Math.Min(result.minGap, gap);
                    result.maxGap = Math.Max(result.maxGap, gap);
                    if (gap > 16) result.longGaps++;
                    shortStreak = gap <= 5 ? shortStreak + 1 : 0;
                    longStreak = gap >= 12 ? longStreak + 1 : 0;
                    result.shortGapStreak = Math.Max(result.shortGapStreak, shortStreak);
                    result.longGapStreak = Math.Max(result.longGapStreak, longStreak);
                }
                previous = note;
            }
            if (result.phases.Sum(p => p.move + p.guard + p.attack + p.other) != result.processed)
                throw new InvalidDataException("Comparison action totals do not match processed notes.");
            return result;
        }

        private static void WriteRun(StreamWriter runs, StreamWriter phases, Group group, Sample s)
        {
            var r = s.run;
            WriteRow(runs, group.Set, group.Configuration, group.Strategy, r.Seed, r.Outcome,
                s.processed, r.LeftoverNotes, r.Cleared ? s.processed : -1, r.FinalPhase, r.FinalHp, r.BossHp,
                r.GoalRemaining, r.InputMisses, s.hits, s.guardSuccess, s.dodgeCollect, s.effectiveStreak,
                s.attackStreak, s.fires, s.minGap, s.maxGap, s.longGaps, s.shortGapStreak, s.longGapStreak,
                s.patternDeferred, s.patternRejected, s.patternSkipped, s.noiseDeferred, s.noiseFallback,
                s.noiseUnavailable, r.InvariantFailureCount, r.ElapsedMilliseconds);
            for (int i = 0; i < s.phases.Length; i++)
            {
                var p = s.phases[i];
                WriteRow(phases, group.Set, group.Configuration, group.Strategy, r.Seed, i + 1,
                    p.move, p.guard, p.attack, p.other, p.opportunity, p.damage, p.hits, p.guardSuccess,
                    p.dodgeCollect, p.effectiveStreak, p.attackStreak, p.transition, p.goalRemaining, r.Outcome);
            }
        }

        private static void WriteEvents(StreamWriter writer, Group group, BattleRun run)
        {
            int previousFire = -1;
            foreach (var e in run.Battle.Events)
            {
                if (!e.type.StartsWith("pattern-", StringComparison.Ordinal) &&
                    !e.type.StartsWith("noise-", StringComparison.Ordinal)) continue;
                WriteRow(writer, group.Set, group.Configuration, group.Strategy, run.Seed,
                    e.type, e.noteIndex, e.musicBeat, e.phase, e.id, e.kind, e.cells, e.impactNote,
                    e.warningNotes, e.requiredDodgeActions, e.selectedInterval, e.actualInterval, e.reason, e.detail);
                Frequency(group, e.phase, "event", e.type);
                if (!string.IsNullOrEmpty(e.reason)) Frequency(group, e.phase, "event_reason", e.type + ":" + e.reason);
                if (e.type == "pattern-publish")
                {
                    Frequency(group, e.phase, "published_kind", e.kind);
                    Frequency(group, e.phase, "published_range", e.kind + ":" + e.cells);
                    Frequency(group, e.phase, "warning_notes", e.warningNotes.ToString(Invariant));
                    Frequency(group, e.phase, "required_dodge_actions", e.requiredDodgeActions.ToString(Invariant));
                    if (e.selectedInterval > 0) Frequency(group, e.phase, "selected_interval", e.selectedInterval.ToString(Invariant));
                }
                if (e.type == "pattern-fire")
                {
                    Frequency(group, e.phase, "fired_kind", e.kind);
                    if (previousFire >= 0 && e.impactNote != previousFire)
                        Frequency(group, e.phase, "actual_interval", (e.impactNote - previousFire).ToString(Invariant));
                    previousFire = e.impactNote;
                }
            }
        }

        private static void Frequency(Group group, int phase, string metric, string value)
        {
            string key = phase + "|" + metric + "|" + value;
            group.Frequencies.TryGetValue(key, out int count);
            group.Frequencies[key] = count + 1;
        }

        private static GroupSummary Summarize(Group group)
        {
            var result = new GroupSummary { set = group.Set, configuration = group.Configuration,
                strategy = group.Strategy.ToString(), configHash = BattleLog.Hash(JsonUtility.ToJson(group.Settings)),
                runs = group.Samples.Count, clears = group.Samples.Count(s => s.run.Cleared),
                deaths = group.Samples.Count(s => s.run.Outcome == "Dead"),
                timeouts = group.Samples.Count(s => s.run.Outcome == "Timeout"),
                goalUnfinished = group.Samples.Count(s => s.phases.Any(p => p.goalRemaining > 0)),
                invariantFailures = group.Samples.Sum(s => s.run.InvariantFailureCount) };
            void Add(string name, Func<Sample, double> value, int phase = 0)
                => result.distributions.Add(Describe(name, phase, group.Samples.Select(value)));
            Add("processed_notes", s => s.processed);
            Add("remaining_notes", s => s.run.LeftoverNotes);
            Add("move", s => s.phases.Sum(p => p.move)); Add("guard", s => s.phases.Sum(p => p.guard));
            Add("effective_attack", s => s.phases.Sum(p => p.attack)); Add("other", s => s.phases.Sum(p => p.other));
            Add("attack_opportunity", s => s.phases.Sum(p => p.opportunity)); Add("actual_damage", s => s.phases.Sum(p => p.damage));
            Add("hits", s => s.hits); Add("guard_success", s => s.guardSuccess);
            Add("dodge_collect", s => s.dodgeCollect);
            Add("max_effective_attack_streak", s => s.effectiveStreak);
            Add("max_attack_action_streak", s => s.attackStreak);
            Add("boss_fires", s => s.fires); Add("gaps_over_16", s => s.longGaps);
            Add("min_actual_gap_minus1_none", s => s.minGap); Add("max_actual_gap_minus1_none", s => s.maxGap);
            Add("max_short_gap_streak", s => s.shortGapStreak); Add("max_long_gap_streak", s => s.longGapStreak);
            Add("pattern_deferred", s => s.patternDeferred); Add("pattern_rejected", s => s.patternRejected);
            Add("pattern_skipped", s => s.patternSkipped); Add("noise_deferred", s => s.noiseDeferred);
            Add("noise_fallback", s => s.noiseFallback); Add("noise_unavailable", s => s.noiseUnavailable);
            Add("simulation_ms", s => s.run.ElapsedMilliseconds);
            if (result.clears > 0) result.distributions.Add(Describe("clear_note_wins_only", 0,
                group.Samples.Where(s => s.run.Cleared).Select(s => (double)s.processed)));
            for (int phase = 1; phase <= 4; phase++)
            {
                int index = phase - 1;
                Add("move", s => s.phases[index].move, phase);
                Add("guard", s => s.phases[index].guard, phase);
                Add("effective_attack", s => s.phases[index].attack, phase);
                Add("other", s => s.phases[index].other, phase);
                Add("attack_opportunity", s => s.phases[index].opportunity, phase);
                Add("actual_damage", s => s.phases[index].damage, phase);
                Add("hits", s => s.phases[index].hits, phase);
                Add("guard_success", s => s.phases[index].guardSuccess, phase);
                Add("dodge_collect", s => s.phases[index].dodgeCollect, phase);
                Add("max_effective_attack_streak", s => s.phases[index].effectiveStreak, phase);
                Add("max_attack_action_streak", s => s.phases[index].attackStreak, phase);
                Add("transition_note_all_minus1_unreached", s => s.phases[index].transition, phase);
                Add("goal_remaining", s => s.phases[index].goalRemaining, phase);
            }
            return result;
        }

        private static Distribution Describe(string metric, int phase, IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) throw new ArgumentException("A distribution requires observations.");
            double Quantile(double q) => sorted[Math.Max(0, (int)Math.Ceiling(q * sorted.Length) - 1)];
            return new Distribution { metric = metric, phase = phase, count = sorted.Length, min = sorted[0],
                p10 = Quantile(.1), p50 = Quantile(.5), p90 = Quantile(.9), max = sorted[sorted.Length - 1], mean = sorted.Average() };
        }

        private static void WriteDistributions(string output, Summary summary, List<Group> groups)
        {
            using (var writer = new StreamWriter(Path.Combine(output, "distributions.csv"), false, Encoding.UTF8))
            {
                writer.WriteLine("set,configuration,strategy,phase,metric,count,min,p10,p50,p90,max,mean");
                foreach (var group in summary.groups) foreach (var d in group.distributions)
                    WriteRow(writer, group.set, group.configuration, group.strategy, d.phase, d.metric, d.count,
                        d.min, d.p10, d.p50, d.p90, d.max, d.mean);
            }
            using (var writer = new StreamWriter(Path.Combine(output, "frequencies.csv"), false, Encoding.UTF8))
            {
                writer.WriteLine("set,configuration,strategy,phase,metric,value,count");
                foreach (var group in groups) foreach (var entry in group.Frequencies.OrderBy(e => e.Key))
                {
                    var key = entry.Key.Split('|');
                    WriteRow(writer, group.Set, group.Configuration, group.Strategy, key[0], key[1],
                        string.Join("|", key.Skip(2)), entry.Value);
                }
            }
        }

        private static string Report(Summary summary)
        {
            var text = new StringBuilder("# 보스 공격 간격 비교\n\n");
            text.AppendLine($"버전 {summary.version}; 채보 해시 {summary.chartHash}.");
            text.AppendLine($"총 {summary.executedRuns}회, {summary.elapsedSeconds.ToString("F2", Invariant)}초. 불변식 오류 {summary.invariantFailures}; 저장 로그 {summary.savedLogs}, 재현 통과 {summary.replayedLogs}, 재현 실패 {summary.replayFailures}.");
            text.AppendLine($"각 구성·전략당 개발 시드 0~{summary.seedsPerGroup - 1}, 독립 시드 100000~{100000 + summary.seedsPerGroup - 1}. miss5는 독립 시드의 입력 5% 누락 실험입니다. 이 도구는 HP나 설정을 자동 조정하지 않습니다.");
            text.AppendLine("A는 새 런타임의 기존 고정 패턴 모드와 HP 62/12/24/94, B는 같은 고정 패턴과 HP 31/6/12/47, C는 저장된 랜덤 설정과 HP 31/6/12/47입니다. A는 이전 실행 파일을 별도로 실행한 결과가 아닙니다.");
            text.AppendLine("\n| 집합 | 구성 | 전략 | 성공/실행 | 사망 | 시간 초과 | 수집 목표 미달 | 클리어 노트 P10/P50/P90 (성공만) | 최장 유효 공격 평균 | 피격 평균 | 방어 성공 평균 |\n|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|");
            foreach (var g in summary.groups)
            {
                Distribution Metric(string name) => g.distributions.FirstOrDefault(d => d.phase == 0 && d.metric == name);
                var clear = Metric("clear_note_wins_only");
                string clearText = clear == null ? "—" : $"{clear.p10}/{clear.p50}/{clear.p90}";
                text.AppendLine($"| {g.set} | {g.configuration} | {g.strategy} | {g.clears}/{g.runs} | {g.deaths} | {g.timeouts} | {g.goalUnfinished} | {clearText} | {Metric("max_effective_attack_streak").mean.ToString("F2", Invariant)} | {Metric("hits").mean.ToString("F2", Invariant)} | {Metric("guard_success").mean.ToString("F2", Invariant)} |");
            }
            text.AppendLine("\n분위수는 nearest rank ceil(q×N)입니다. 실패·미진입 페이즈도 행동 통계에 포함됩니다(미진입 행동 0, 미전환 노트 -1). 클리어 노트는 성공한 실행만의 별도 지표이며 위 표의 실패 수와 함께 읽어야 합니다. processed_notes는 실패를 포함한 전체 실행의 종료 시점입니다.");
            text.AppendLine("runs.csv.gz는 모든 실행, phases.csv.gz는 모든 페이즈, events.csv.gz는 모든 패턴·잡음 이벤트입니다. distributions.csv에 전략·페이즈별 행동량 및 탐색 소요 시간 분포, frequencies.csv에 패턴/범위/예고/간격/이유 빈도가 있습니다. 로그는 그룹별 첫 3개 및 매 100번째 표본과 모든 실패를 보존합니다. 대표 표본과 그룹별 첫 실패는 노트 및 전체 이벤트를 재현 검증합니다.");
            text.AppendLine("공격 간격은 실제 발동 노트 차이입니다. 같은 노트의 중첩 발동은 하나의 시점으로 계산하며, 페이즈 전환을 가로지르는 휴식도 포함합니다. 16 초과는 gaps_over_16에 보존합니다. 짧은 간격 연속은 ≤5, 긴 간격 연속은 ≥12노트입니다. 가장 긴 공격은 유효 피해 공격과 헛공격을 포함한 공격 행동을 분리합니다. 회피+수집은 발동 노트에 출발 칸이 위험하고 도착 칸이 안전하며 실제 수집한 이동만 계산합니다.");
            text.AppendLine("탐색 비용은 simulation_ms 및 후보 거부·연기 횟수로 보고합니다. BFS 내부 전개 상태 수는 별도 계측하지 않았으며 후보 거부 수를 전개 수로 해석하면 안 됩니다. 전투가 일찍 끝나면 남은 채보 노트는 행동 통계에 채우지 않습니다. 표본 통과는 모든 경우의 안전성이나 사람의 재미를 증명하지 않습니다.");
            if (summary.replayErrors.Count > 0) foreach (string error in summary.replayErrors) text.AppendLine("- 재현 오류: " + error);
            return text.ToString();
        }

        private static string EventHash(BattleSession battle)
        {
            var text = new StringBuilder();
            foreach (var e in battle.Events) text.AppendLine(JsonUtility.ToJson(e));
            return BattleLog.Hash(text.ToString());
        }

        private static StreamWriter CompressedWriter(string path)
            => new StreamWriter(new GZipStream(File.Create(path), System.IO.Compression.CompressionLevel.Fastest), new UTF8Encoding(false));

        private static void WriteRow(StreamWriter writer, params object[] values)
            => writer.WriteLine(string.Join(",", values.Select(v =>
            {
                string s = Convert.ToString(v, Invariant) ?? "";
                return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
            })));

        public static string SelfCheck()
        {
            var d = Describe("known", 0, new double[] { 0, 0, 0, 7, 8 });
            if (d.p10 != 0 || d.p50 != 0 || d.p90 != 8 || d.count != 5 || d.mean != 3)
                throw new InvalidDataException("Comparison nearest-rank distribution self-check failed.");
            var settings = new BattleSettings { Bpm = 87, NoteCount = 24, Health = new[] { 1, 1, 1, 1 },
                Patterns = Array.Empty<PatternDefinition>() };
            settings.Schedule.Enabled = false;
            var run = BattleSimulator.Run(settings, 17, StrategyKind.CollectFirst);
            var concurrent = new BattleRun[2];
            Parallel.For(0, concurrent.Length, i => concurrent[i] = BattleSimulator.Run(settings, 17, StrategyKind.CollectFirst));
            foreach (var parallelRun in concurrent)
                if (!parallelRun.Battle.Records.Select(r => r.stateHash).SequenceEqual(run.Battle.Records.Select(r => r.stateHash)) ||
                    EventHash(parallelRun.Battle) != EventHash(run.Battle))
                    throw new InvalidDataException("Comparison parallel simulation self-check failed.");
            var sample = Measure(run);
            if (sample.processed + run.LeftoverNotes != settings.NoteCount || sample.hits != 0 || sample.fires != 0 ||
                sample.phases.Sum(p => p.damage) != run.Battle.Records.Sum(r => r.damage))
                throw new InvalidDataException("Comparison action/phase accounting self-check failed.");
            run.Battle.Records.Clear(); run.Battle.Events.Clear();
            run.Battle.Events.Add(new BattleEvent { type = "pattern-fire", impactNote = 0, cells = (1UL << 27).ToString("X16") });
            run.Battle.Records.Add(new NoteRecord { noteIndex = 0, phase = 1, category = "Move", beforeX = 3,
                beforeY = 3, x = 3, y = 4, collected = 1 });
            for (int i = 1; i <= 3; i++) run.Battle.Records.Add(new NoteRecord { noteIndex = i,
                phase = i < 3 ? 1 : 2, category = "EffectiveAttack", damage = 1, attackOpportunity = true });
            sample = Measure(run);
            if (sample.dodgeCollect != 1 || sample.effectiveStreak != 3 || sample.attackStreak != 3 ||
                sample.phases[0].effectiveStreak != 2 || sample.phases[1].effectiveStreak != 1)
                throw new InvalidDataException("Comparison streak/dodge-collection self-check failed.");
            using (var stream = new MemoryStream())
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                    WriteRow(writer, "a,b", "x\"y", 1.5);
                if (Encoding.UTF8.GetString(stream.ToArray()).TrimEnd('\r', '\n') != "\"a,b\",\"x\"\"y\",1.5")
                    throw new InvalidDataException("Comparison CSV quoting self-check failed.");
            }
            return "Comparison self-check passed: nearest-rank, parallel determinism, action/phase accounting, streak, dodge-collection, CSV quoting.";
        }
    }
}
