using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace StringAsASword.Combat
{
    public static class BattleLog
    {
        // v2: inputs can be key releases (hold notes).
        private const string RuntimeVersion = "battle-6/xorshift32-v2";
        [Serializable] private sealed class Line
        {
            public string type, chartHash, configHash, runtimeVersion = RuntimeVersion;
            public int seed, processedNotes, remainingNotes;
            public double endTime;
            public string termination, finalStateHash;
            public int[] analysisEnds;
            public BattleSettings settings;
            public BattleInput input;
            public BattleEvent battleEvent;
            public NoteRecord note;
        }
        public static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string ChartHash(BattleSettings s) => Hash(s.ChartId + "|" + s.NoteCount + "|" +
            s.Bpm.ToString("R", CultureInfo.InvariantCulture) + "|" + s.PerfectWindow.ToString("R", CultureInfo.InvariantCulture) + "|" +
            s.GoodWindow.ToString("R", CultureInfo.InvariantCulture) + "|" + s.InputOffset.ToString("R", CultureInfo.InvariantCulture) +
            (s.HasAuthoredChart ? "|" + Join(s.NoteTimes) : "") + (s.HasHolds ? "|holds:" + Join(s.HoldEndTimes) : "") + (s.HasFinale ? "|finish:" + s.FinishNote : ""));
        private static string Join(double[] values) => string.Join(",", Array.ConvertAll(values, t => t.ToString("R", CultureInfo.InvariantCulture)));
        public static string StateHash(BattleSession b)
        {
            var text = new StringBuilder().Append(b.LastClosedNote).Append('|').Append(b.Session.Position).Append('|')
                .Append(b.PlayerHp).Append('|').Append(b.Phases.PhaseId).Append('|').Append(b.Health.TotalHp).Append('|')
                .Append(b.Noise.Collected).Append('|').Append(b.Outcome);
            foreach (var n in b.Noise.Pickups) text.Append('|').Append(n.Id).Append(':').Append(n.Cell);
            foreach (var p in b.Patterns.Active) text.Append('|').Append(p.Id).Append(':').Append(p.Cells).Append(':').Append(p.ImpactNote);
            return Hash(text.ToString());
        }
        public static void Write(string path, BattleSession battle, string termination = "Completed")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (var file = File.Create(path))
            using (var stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? (Stream)new GZipStream(file, System.IO.Compression.CompressionLevel.Fastest) : file)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.WriteLine(JsonUtility.ToJson(new Line { type = "header", seed = battle.Seed, settings = battle.Settings,
                    chartHash = ChartHash(battle.Settings), configHash = Hash(JsonUtility.ToJson(battle.Settings)), analysisEnds = battle.AnalysisEnds }));
                foreach (var input in battle.Inputs) writer.WriteLine(JsonUtility.ToJson(new Line { type = "input", input = input }));
                foreach (var e in battle.Events) writer.WriteLine(JsonUtility.ToJson(new Line { type = "event", battleEvent = e }));
                foreach (var n in battle.Records) writer.WriteLine(JsonUtility.ToJson(new Line { type = "note", note = n }));
                writer.WriteLine(JsonUtility.ToJson(new Line { type = battle.Outcome.ToString(), processedNotes = battle.Records.Count,
                    remainingNotes = battle.Settings.NoteCount - battle.Records.Count, endTime = battle.AdvancedTime,
                    termination = termination, finalStateHash = StateHash(battle) }));
            }
        }
        public static BattleSession Replay(string path)
        {
            string[] lines;
            using (var file = File.OpenRead(path))
            using (var stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
            using (var reader = new StreamReader(stream)) lines = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var header = JsonUtility.FromJson<Line>(lines[0]);
            // Unity's native JSON double reader can shift a beat-boundary time by one ULP.
            // Restore timing scalars from the round-trip decimal text with the managed parser.
            header.settings.Bpm = ExactDouble(lines[0], "Bpm");
            header.settings.PerfectWindow = ExactDouble(lines[0], "PerfectWindow");
            header.settings.GoodWindow = ExactDouble(lines[0], "GoodWindow");
            header.settings.InputOffset = ExactDouble(lines[0], "InputOffset");
            header.settings.NoteTimes = ExactDoubles(lines[0], "NoteTimes");
            header.settings.HoldEndTimes = ExactDoubles(lines[0], "HoldEndTimes");
            if (header.type != "header" || header.runtimeVersion != RuntimeVersion ||
                header.chartHash != ChartHash(header.settings) || header.configHash != Hash(JsonUtility.ToJson(header.settings)))
                throw new InvalidDataException("Replay version or content hash mismatch.");
            var battle = new BattleSession(header.settings, header.seed, header.analysisEnds == null || header.analysisEnds.Length == 0 ? null : header.analysisEnds);
            foreach (string text in lines)
            {
                var line = JsonUtility.FromJson<Line>(text);
                if (line.type != "input") continue;
                if (line.input.release) battle.Release(line.input.action, line.input.direction, ExactDouble(text, "time"));
                else battle.Request(line.input.action, line.input.direction, ExactDouble(text, "time"));
            }
            var footer = JsonUtility.FromJson<Line>(lines[lines.Length - 1]);
            battle.Advance(ExactDouble(lines[lines.Length - 1], "endTime"));
            int notes = 0, events = 0;
            foreach (string text in lines)
            {
                var line = JsonUtility.FromJson<Line>(text);
                if (line.type == "event")
                {
                    if (events >= battle.Events.Count || JsonUtility.ToJson(line.battleEvent) != JsonUtility.ToJson(battle.Events[events]))
                        throw new InvalidDataException("Replay diverged at event " + (events + 1));
                    events++;
                }
                if (line.type != "note") continue;
                if (notes >= battle.Records.Count || JsonUtility.ToJson(line.note) != JsonUtility.ToJson(battle.Records[notes]))
                    throw new InvalidDataException("Replay diverged at note " + (notes + 1));
                notes++;
            }
            if (notes != battle.Records.Count) throw new InvalidDataException("Replay note count mismatch.");
            if (events != battle.Events.Count) throw new InvalidDataException("Replay event count mismatch.");
            if (footer.processedNotes != notes || footer.remainingNotes != header.settings.NoteCount - notes ||
                footer.type != battle.Outcome.ToString() || footer.finalStateHash != StateHash(battle))
                throw new InvalidDataException("Replay final state mismatch.");
            return battle;
        }
        private static double[] ExactDoubles(string json, string field)
        {
            var match = Regex.Match(json, "\"" + field + "\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!match.Success || match.Groups[1].Value.Trim().Length == 0) return new double[0];
            return Array.ConvertAll(match.Groups[1].Value.Split(','), v => double.Parse(v.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture));
        }
        private static double ExactDouble(string json, string field)
        {
            var match = Regex.Match(json, "\"" + field + "\"\\s*:\\s*([-+0-9.eE]+)");
            if (!match.Success) throw new InvalidDataException("Missing replay timing field: " + field);
            return double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
