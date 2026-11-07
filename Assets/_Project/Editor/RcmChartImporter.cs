using System;
using System.IO;
using System.Linq;
using StringAsASword.Rhythm;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>Copies one track of an RCM chart JSON (formatVersion 1) into a SongDefinition.</summary>
    public static class RcmChartImporter
    {
        public const string DefaultTrack = "Note_3"; // the J lane
        private const int TicksPerBeat = 480;
        [Serializable] private sealed class RcmNote { public int tick; public string track, type; public int endTick; }
        [Serializable] private sealed class RcmChart { public int formatVersion; public string name; public int level; public RcmNote[] notes; }

        public static int Import(SongDefinition song, string json, string track = DefaultTrack)
        {
            var chart = JsonUtility.FromJson<RcmChart>(json);
            if (chart == null || chart.formatVersion != 1 || chart.notes == null) throw new InvalidDataException("Not an RCM chart (formatVersion 1).");
            var notes = chart.notes.Where(n => n.track == track).OrderBy(n => n.tick).ToArray();
            if (notes.Length == 0) throw new InvalidDataException($"Track '{track}' has no notes.");
            for (int i = 0; i < notes.Length; i++)
            {
                if (notes[i].tick < 0 || i > 0 && notes[i].tick == notes[i - 1].tick)
                    throw new InvalidDataException($"Note {i}: tick {notes[i].tick} is negative or duplicated on track '{track}'.");
                if (notes[i].type == "hold" && notes[i].endTick <= notes[i].tick)
                    throw new InvalidDataException($"Note {i}: hold at tick {notes[i].tick} ends before it starts.");
                if (notes[i].type == "hold" && i + 1 < notes.Length && notes[i].endTick >= notes[i + 1].tick)
                    throw new InvalidDataException($"Note {i}: hold at tick {notes[i].tick} runs into the next note at tick {notes[i + 1].tick}.");
            }
            var so = new SerializedObject(song);
            so.FindProperty("chartName").stringValue = $"{chart.name}{chart.level}-{track}";
            so.FindProperty("ticksPerBeat").intValue = TicksPerBeat;
            Fill(so.FindProperty("noteTicks"), notes.Select(n => n.tick).ToArray());
            Fill(so.FindProperty("noteHoldEndTicks"), notes.Select(n => n.type == "hold" ? n.endTick : 0).ToArray());
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(song);
            return notes.Length;
        }
        private static void Fill(SerializedProperty array, int[] values)
        {
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).intValue = values[i];
        }

        [MenuItem("String As a Sword/Import RCM Chart (J lane)...")]
        private static void ImportFromMenu()
        {
            var song = Selection.activeObject as SongDefinition
                ?? AssetDatabase.FindAssets("t:SongDefinition").Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAssetAtPath<SongDefinition>).FirstOrDefault();
            if (song == null) { EditorUtility.DisplayDialog("RCM import", "No SongDefinition found.", "OK"); return; }
            string path = EditorUtility.OpenFilePanel("RCM chart JSON", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/Downloads", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                int count = Import(song, File.ReadAllText(path), DefaultTrack);
                // Keep the source next to the song so the chart can be re-imported after editing it in RCM.
                string dir = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(song)), "Charts");
                Directory.CreateDirectory(dir);
                File.Copy(path, Path.Combine(dir, Path.GetFileName(path)), true);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
                Debug.Log($"Imported {count} notes from '{Path.GetFileName(path)}' into {song.name}. Check noteCount-based balance (Health, patterns).", song);
            }
            catch (Exception error) when (error is InvalidDataException || error is IOException || error is ArgumentException)
            { EditorUtility.DisplayDialog("RCM import failed", error.Message, "OK"); }
        }
    }
}
