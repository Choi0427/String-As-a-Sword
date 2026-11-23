using System.Collections.Generic;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Wires the delivered prologue audio (intro review S1-B, S2-A, prologue revision R6-A; Docs/IntroAssets.md) into
    /// Resources/Prologue.asset: the composed cue (Audio/Prologue/Cue/Cue_A, Cue_Wait, Cue_B), the recorded chess clock
    /// (Audio/Prologue/Clock/Clock_Tick, Clock_Accent) and the string recordings (Audio/Prologue/String/String_Strain,
    /// String_Snap). Sets their import settings and checks each piece against its beats. A missing file leaves its slot
    /// empty, so its stand-in (or nothing) plays. Re-running is safe.
    /// </summary>
    public static class PrologueAudioSetup
    {
        public const string CueFolder = "Assets/_Project/Audio/Prologue/Cue/", ClockFolder = "Assets/_Project/Audio/Prologue/Clock/",
            StringFolder = "Assets/_Project/Audio/Prologue/String/";
        public const string DefinitionPath = "Assets/_Project/Resources/Prologue.asset";

        [MenuItem("String As a Sword/Audio/Set Up Prologue Audio")]
        public static void Run()
        {
            var definition = AssetDatabase.LoadAssetAtPath<PrologueDefinition>(DefinitionPath);
            if (definition == null) { Debug.LogError("Prologue data is missing: " + DefinitionPath); return; }
            double spb = SecondsPerBeat();
            var report = new List<string>();
            definition.cueA = Piece("Cue_A", PrologueDefinition.CueABeats, spb, report);
            definition.cueWait = Piece("Cue_Wait", PrologueDefinition.CueWaitBeats, spb, report);
            definition.cueB = Piece("Cue_B", PrologueDefinition.CueBBeats, spb, report);
            definition.clockTick = Clock("Clock_Tick", report);
            definition.clockAccent = Clock("Clock_Accent", report);
            definition.stringStrain = StringSound("String_Strain", 3, "nothing plays (the knight strains in silence)", report);
            definition.stringSnap = StringSound("String_Snap", 2, "the noise's crackle over a heavy piece stands in", report);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            string text = "Prologue audio set up:\n" + string.Join("\n", report);
            if (report.Exists(line => line.StartsWith("!"))) Debug.LogWarning(text); else Debug.Log(text);
        }
        /// <summary>Seconds per beat of the song the prologue hands over to (87 BPM).</summary>
        public static double SecondsPerBeat()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SongDefinition"))
            {
                var song = AssetDatabase.LoadAssetAtPath<SongDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (song != null && song.Bpm > 0) return 60.0 / song.Bpm;
            }
            return 60.0 / 87;
        }
        // A composed piece: stereo, as mastered (no normalising), decoded on load so it starts on the sample.
        private static AudioClip Piece(string name, int beats, double spb, List<string> report)
        {
            string path = CueFolder + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) { report.Add($"  {name}: not delivered, the placeholder music plays."); return null; }
            importer.forceToMono = false;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = .9f; settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            SetNormalize(importer, false);
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            string problem = PrologueDefinition.CheckLength(name, clip.length, beats, spb);
            report.Add(problem != null ? "! " + problem
                : $"  {name}: {clip.length:0.000} s, {beats} beats ({beats * spb:0.000} s) + tail {clip.length - beats * spb:0.000} s, {clip.channels} ch, {clip.frequency} Hz.");
            return clip;
        }
        // A clock take: mono and short, as recorded (no normalising, so the accent stays louder than the tick).
        private static AudioClip Clock(string name, List<string> report)
        {
            string path = ClockFolder + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) { report.Add($"  {name}: not delivered, the placeholder clock plays."); return null; }
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            SetNormalize(importer, false);
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            report.Add(clip.length > .3f ? $"! {name}: {clip.length:0.000} s is longer than a clock press should be (0.25 s)."
                : $"  {name}: {clip.length:0.000} s.");
            return clip;
        }
        // A string recording: mono, as recorded (no normalising), decoded on load so it starts on the sample. Its first sample
        // is the sound's start: it is scheduled on the beat.
        private static AudioClip StringSound(string name, float longest, string missing, List<string> report)
        {
            string path = StringFolder + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) { report.Add($"  {name}: not delivered, {missing}."); return null; }
            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad; settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            SetNormalize(importer, false);
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            report.Add(clip.length > longest ? $"! {name}: {clip.length:0.000} s is longer than {longest} s; trim it so it ends within the shot."
                : $"  {name}: {clip.length:0.000} s.");
            return clip;
        }
        // "Normalize" is only exposed through the serialized importer.
        private static void SetNormalize(AudioImporter importer, bool on)
        {
            var serialized = new SerializedObject(importer);
            var normalize = serialized.FindProperty("m_Normalize");
            if (normalize == null) return;
            normalize.boolValue = on;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
