using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace StringAsASword.Combat
{
    public static class BattleLog
    {
        [Serializable] private sealed class Line
        {
            public string type, chartHash, configHash, runtimeVersion = "battle-1/xorshift32-v1";
            public int seed, processedNotes, remainingNotes;
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
            s.GoodWindow.ToString("R", CultureInfo.InvariantCulture) + "|" + s.InputOffset.ToString("R", CultureInfo.InvariantCulture));
        public static string StateHash(BattleSession b)
        {
            var text = new StringBuilder().Append(b.LastClosedNote).Append('|').Append(b.Session.Position).Append('|')
                .Append(b.PlayerHp).Append('|').Append(b.Phases.PhaseId).Append('|').Append(b.Health.TotalHp).Append('|')
                .Append(b.Noise.Collected).Append('|').Append(b.Outcome);
            foreach (var n in b.Noise.Pickups) text.Append('|').Append(n.Id).Append(':').Append(n.Cell);
            foreach (var p in b.Patterns.Active) text.Append('|').Append(p.Id).Append(':').Append(p.Cells).Append(':').Append(p.ImpactNote);
            return Hash(text.ToString());
        }
        public static void Write(string path, BattleSession battle)
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
                    remainingNotes = battle.Settings.NoteCount - battle.Records.Count }));
            }
        }
        public static BattleSession Replay(string path)
        {
            string[] lines;
            using (var file = File.OpenRead(path))
            using (var stream = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? (Stream)new GZipStream(file, CompressionMode.Decompress) : file)
            using (var reader = new StreamReader(stream)) lines = reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var header = JsonUtility.FromJson<Line>(lines[0]);
            if (header.type != "header" || header.runtimeVersion != "battle-1/xorshift32-v1" ||
                header.chartHash != ChartHash(header.settings) || header.configHash != Hash(JsonUtility.ToJson(header.settings)))
                throw new InvalidDataException("Replay version or content hash mismatch.");
            var battle = new BattleSession(header.settings, header.seed, header.analysisEnds == null || header.analysisEnds.Length == 0 ? null : header.analysisEnds);
            foreach (string text in lines)
            {
                var line = JsonUtility.FromJson<Line>(text);
                if (line.type == "input") battle.Request(line.input.action, line.input.direction, line.input.time);
            }
            battle.Advance((header.settings.NoteCount - 1) * 60.0 / header.settings.Bpm + header.settings.GoodWindow + header.settings.InputOffset + .001);
            int notes = 0;
            foreach (string text in lines)
            {
                var line = JsonUtility.FromJson<Line>(text);
                if (line.type != "note") continue;
                if (notes >= battle.Records.Count || line.note.stateHash != battle.Records[notes].stateHash)
                    throw new InvalidDataException("Replay diverged at note " + (notes + 1));
                notes++;
            }
            if (notes != battle.Records.Count) throw new InvalidDataException("Replay note count mismatch.");
            return battle;
        }
    }
}
