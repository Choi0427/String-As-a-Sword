using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StringAsASword.Presentation;
using StringAsASword.Rhythm;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Renders the prologue's sound as the game plays it, for the composer (guide tracks) and for checking that its joins
    /// sound right: the same cues, levels and cuts as <see cref="IntroDirector"/> (and the composed pieces and recorded
    /// clock once assigned), mixed offline into WAV files in output/intro-assets, with a report (audio_check.txt) of what
    /// can be measured: silence at the start of a clip, every cut and what masks it, what carries over a join, levels
    /// against the song, and gaps. <see cref="CompareCapture"/> holds a recording of the running game (PrologueAudioTests)
    /// against the same model, so the model's findings are known to be the game's. Listening is still for a person
    /// (output/intro-assets/listen.py plays the files with their names spoken first).
    /// </summary>
    public static class IntroGuideRenderer
    {
        public const int Rate = 48000;
        public const string OutputFolder = "output/intro-assets";
        // The waiting bar's sources are cut 30 ms after the tear's beat (IntroDirector.Tear); cues are queued this far ahead.
        private const double TearCut = .03, Ahead = .25;
        private const float Headroom = .891f; // -1 dBFS

        [MenuItem("String As a Sword/Audio/Render Intro Guide Tracks")]
        public static void RunMenu() => Debug.Log(Render(Path.GetFullPath(OutputFolder)));
        [MenuItem("String As a Sword/Audio/Compare Prologue Recordings")]
        public static void CompareMenu()
        {
            string dir = Path.GetFullPath(OutputFolder);
            foreach (var name in new[] { "capture_into_song", "capture_into_tutorial" })
                if (File.Exists(Path.Combine(dir, name + ".json"))) Debug.Log(CompareCapture(dir, name));
        }

        // ---------------------------------------------------------------- the model
        private sealed class Sound { public double At, End, Cut; public string What; public float Gain; public float[] Clip; public bool Played => Cut > At; }
        private sealed class Mix
        {
            public readonly string Name;
            public readonly double Start;
            public readonly float[] Buffer;
            public readonly List<Sound> Sounds = new List<Sound>();
            public readonly List<(double time, string what)> Joins = new List<(double, string)>();
            public Mix(string name, double start, double end) { Name = name; Start = start; Buffer = new float[Math.Max(1, (int)Math.Ceiling((end - start) * Rate))]; }
            public int Index(double time) => (int)Math.Round((time - Start) * Rate);
            public double End => Start + (double)Buffer.Length / Rate;
            /// <summary>A clip whose first sample lands on <paramref name="at"/>, stopped hard at <paramref name="cut"/> (as a source is).</summary>
            /// A late start (<paramref name="from"/> samples) plays from the matching sample, as a stem does.
            public void Add(float[] clip, double at, float gain, string what, double cut = double.PositiveInfinity, int from = 0)
            {
                if (clip == null) return;
                double end = at + (double)clip.Length / Rate;
                int first = Index(at), stop = cut < end ? Index(cut) : first + clip.Length;
                for (int j = Math.Max(first + from, 0); j < Math.Min(stop, Buffer.Length); j++) Buffer[j] += clip[j - first] * gain;
                Sounds.Add(new Sound { At = at, End = end, Cut = Math.Min(cut, end), What = what, Gain = gain, Clip = clip });
            }
        }
        private sealed class Context
        {
            public PrologueDefinition Data;
            public SongDefinition Song;
            public float[] SongSamples;
            public double Spb, SongAfterHandover;
            /// <summary>
            /// A section's placeholder (or composed) sound, scheduled as the game does from <paramref name="at"/>. A sound due
            /// before <paramref name="now"/> (the J press) plays 10 ms after it, the composed piece 20 ms after it from the matching sample.
            /// </summary>
            public void Section(Mix mix, IEnumerable<IntroDirector.Cue> cues, AudioClip stem, double at, double cut, string section, double now = double.NegativeInfinity)
            {
                foreach (var cue in cues)
                {
                    if (stem != null && PrologueDefinition.IsMusic(cue.Clip)) continue;
                    bool clock = PrologueDefinition.IsClock(cue.Clip), accent = clock && cue.Volume >= .45f;
                    var clip = clock ? Data.Tick(accent) : Data.Clip(cue.Clip);
                    if (clip == null) continue;
                    float gain = Data.volume * (clock ? Data.TickVolume(accent) : cue.Volume);
                    mix.Add(Samples(clip), Math.Max(at + cue.Beat * Spb, now + .01), gain, $"{section} {cue.Beat:0.##} {(clock ? (accent ? "clock accent" : "clock") : clip.name)}", cut);
                }
                if (stem != null) mix.Add(Samples(stem), at, Data.cueVolume, $"{section} {stem.name}", cut, (int)Math.Round(Math.Max(0, now + .02 - at) * Rate));
            }
            /// <summary>The tutorial's chess clock from <paramref name="from"/> (the handover), an accent on each bar line.</summary>
            public void Ticks(Mix mix, double from, int beats)
            {
                for (int b = 0; b < beats; b++)
                {
                    bool accent = b % 4 == 0;
                    mix.Add(Samples(Data.Tick(accent)), from + b * Spb, Data.volume * Data.TickVolume(accent), $"tutorial {b} clock{(accent ? " accent" : "")}");
                }
            }
            public void SongFrom(Mix mix, double handover) => mix.Add(SongSamples, handover + SongAfterHandover, Song.Volume, "song");
            /// <summary>
            /// A whole prologue in intro-beat seconds: section A (from <paramref name="jumpTo"/> when jumped), the waiting bars
            /// queued before the J press at <paramref name="press"/> (tearing on <paramref name="tearIntroBeat"/>), section B, and
            /// the song or the tutorial's clock.
            /// </summary>
            public Mix Run(string name, double start, double end, double jumpTo, double tearIntroBeat, double press, bool tutorialNext)
            {
                var mix = new Mix(name, start, end);
                double tear = tearIntroBeat * Spb, handover = tear + IntroTimeline.AfterTear * Spb, cut = Math.Max(tear, press + .01) + TearCut;
                Section(mix, IntroDirector.SectionACues().Where(c => jumpTo < 0 || c.Beat >= jumpTo), Data.cueA, 0, double.PositiveInfinity, "A");
                for (int k = 0; ; k++)
                {
                    double bar = (IntroTimeline.LoopStart + k * IntroTimeline.LoopBeats) * Spb;
                    if (bar > press + Ahead) break;
                    Section(mix, IntroDirector.WaitBarCues(), Data.cueWait, bar, cut, "Wait" + (k + 1));
                }
                Section(mix, IntroDirector.SectionBCues(), Data.cueB, tear, double.PositiveInfinity, "B", press);
                if (tutorialNext) Ticks(mix, handover, (int)Math.Ceiling((end - handover) / Spb) + 1); else SongFrom(mix, handover);
                mix.Joins.Add((cut, "waiting bar cut"));
                mix.Joins.Add((handover, "handover"));
                return mix;
            }
        }
        private static Context Load()
        {
            cache.Clear();
            var data = AssetDatabase.LoadAssetAtPath<PrologueDefinition>(PrologueAudioSetup.DefinitionPath);
            var settings = BattleAnalysis.LoadSettings();
            var song = FirstSong();
            // The song's mp3 starts this long after the handover beat (negative: before it), as ScheduleSongFromPrologue
            // schedules it: the clock's origin is handover - S*, and the mp3 plays from origin - its start offset.
            return new Context
            {
                Data = data, Song = song, SongSamples = Samples(song.Music), Spb = 60.0 / settings.Bpm,
                SongAfterHandover = -IntroTimeline.HandoverSongTime(settings.CreateTimeline()) - song.AudioStartOffset,
            };
        }

        private static readonly Dictionary<AudioClip, float[]> cache = new Dictionary<AudioClip, float[]>();
        /// <summary>
        /// A clip as the mono mix of what it puts out at 48 kHz. A mono clip on a 2D source goes to both channels 3 dB down
        /// (Unity's pan law, measured against the recordings); a stereo one plays as it is.
        /// </summary>
        private static float[] Samples(AudioClip clip)
        {
            if (clip == null) return null;
            if (cache.TryGetValue(clip, out var cached)) return cached;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                clip.LoadAudioData();
                var until = DateTime.Now.AddSeconds(20);
                while (clip.loadState == AudioDataLoadState.Loading && DateTime.Now < until) System.Threading.Thread.Sleep(10);
            }
            var raw = new float[clip.samples * clip.channels];
            clip.GetData(raw, 0);
            var mono = new float[clip.samples];
            float pan = clip.channels == 1 ? .70710678f : 1f / clip.channels;
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < clip.channels; c++) sum += raw[i * clip.channels + c];
                mono[i] = sum * pan;
            }
            return cache[clip] = Resample(mono, clip.frequency);
        }
        private static float[] Resample(float[] x, int rate)
        {
            if (rate == Rate) return x;
            double ratio = (double)rate / Rate;
            var y = new float[(int)(x.Length / ratio)];
            for (int i = 0; i < y.Length; i++)
            {
                double p = i * ratio; int a = (int)p; float f = (float)(p - a);
                y[i] = a + 1 < x.Length ? x[a] * (1 - f) + x[a + 1] * f : x[Math.Min(a, x.Length - 1)];
            }
            return y;
        }

        // ---------------------------------------------------------------- guide tracks and joins
        /// <summary>Renders the guide tracks and join tests into <paramref name="dir"/> and returns the report.</summary>
        public static string Render(string dir)
        {
            Directory.CreateDirectory(dir);
            var ctx = Load();
            var data = ctx.Data;
            double spb = ctx.Spb, bar = 4 * spb, b20 = IntroTimeline.AfterTear * spb, inf = double.PositiveInfinity;
            var mixes = new List<Mix>();

            // Section A, 32 beats from story beat 0, with its tail.
            var a = new Mix("guide_A", 0, 32 * spb + 4);
            ctx.Section(a, IntroDirector.SectionACues(), data.cueA, 0, inf, "A");
            mixes.Add(a);
            // One waiting bar, with its tail.
            var w = new Mix("guide_Wait", 0, bar + 3);
            ctx.Section(w, IntroDirector.WaitBarCues(), data.cueWait, 0, inf, "Wait");
            mixes.Add(w);
            // Section B from the tear's beat; what still sounds at the handover rings on, and the song comes in.
            var bMix = new Mix("guide_B", 0, b20 + 10);
            ctx.Section(bMix, IntroDirector.SectionBCues(), data.cueB, 0, inf, "B");
            ctx.SongFrom(bMix, b20);
            bMix.Joins.Add((b20, "handover")); bMix.Joins.Add((b20 + ctx.SongAfterHandover, "song enters"));
            mixes.Add(bMix);
            // Joins: the waiting bar three times over.
            var loop = new Mix("seam_wait_x3", 0, 3 * bar + 3);
            for (int k = 0; k < 3; k++) ctx.Section(loop, IntroDirector.WaitBarCues(), data.cueWait, k * bar, inf, "Wait" + (k + 1));
            loop.Joins.Add((bar, "next bar")); loop.Joins.Add((2 * bar, "next bar"));
            mixes.Add(loop);
            // Joins: the tear on each beat of the waiting bar. The waiting sources (this bar and the last) stop 30 ms after the
            // tear's beat, and section B starts on it.
            for (int k = 0; k < 4; k++)
            {
                double tear = k * spb;
                var t = new Mix($"seam_tear_beat{k + 1}", -2 * spb, tear + 6 * spb);
                ctx.Section(t, IntroDirector.WaitBarCues(), data.cueWait, -bar, tear + TearCut, "Wait-1");
                ctx.Section(t, IntroDirector.WaitBarCues(), data.cueWait, 0, tear + TearCut, "Wait");
                ctx.Section(t, IntroDirector.SectionBCues(), data.cueB, tear, inf, "B");
                t.Joins.Add((tear + TearCut, "waiting bar cut"));
                mixes.Add(t);
            }
            // Joins: the end of section B into the song, and into the tutorial's clock (its first accent on the handover).
            var toSong = new Mix("seam_B_to_song", 14 * spb, b20 + 8);
            ctx.Section(toSong, IntroDirector.SectionBCues(), data.cueB, 0, inf, "B");
            ctx.SongFrom(toSong, b20);
            toSong.Joins.Add((b20, "handover")); toSong.Joins.Add((b20 + ctx.SongAfterHandover, "song enters"));
            mixes.Add(toSong);
            var toTutorial = new Mix("seam_B_to_tutorial", 14 * spb, b20 + 8 * spb);
            ctx.Section(toTutorial, IntroDirector.SectionBCues(), data.cueB, 0, inf, "B");
            ctx.Ticks(toTutorial, b20, 8);
            toTutorial.Joins.Add((b20, "handover"));
            mixes.Add(toTutorial);

            var report = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            report.AppendLine("Intro guide tracks and join check (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv) + ")");
            report.AppendLine($"87 BPM: beat {spb:0.0000} s, bar {bar:0.0000} s. The song's mp3 enters {-ctx.SongAfterHandover * 1000:0.0} ms before the handover beat.");
            report.AppendLine($"Pieces: Cue_A {Name(data.cueA)}, Cue_Wait {Name(data.cueWait)}, Cue_B {Name(data.cueB)}; clock {Name(data.clockTick)} / {Name(data.clockAccent)}.");
            report.AppendLine("Levels are as heard in game at full music volume, as the mono mix of the output (song x" + ctx.Song.Volume.ToString("0.###", inv) + ", placeholders x" +
                data.volume.ToString("0.###", inv) + " x cue, composed pieces x" + data.cueVolume.ToString("0.###", inv) + "; mono clips 3 dB down from panning). The WAV files are each scaled to peak at -1 dBFS.");
            report.AppendLine();

            // 1. Levels.
            var song = ctx.SongSamples; float sg = ctx.Song.Volume;
            double songRms = Rms(song, 0, Math.Min(song.Length, 60 * Rate)) * sg;
            int enter = bMix.Index(b20 + ctx.SongAfterHandover);
            report.AppendLine("1. Levels");
            report.AppendLine($"   Song file (mono mix): RMS {Db(Rms(song, 0, song.Length)):0.0} dBFS, peak {Db(Peak(song, 0, song.Length)):0.0} dBFS.");
            report.AppendLine($"   Song in game: RMS {Db(songRms):0.0} dB over its first minute, {Db(Rms(bMix.Buffer, bMix.Index(b20), bMix.Index(b20 + 10))):0.0} dB in the 10 s after the handover; peak {Db(Peak(song, 0, song.Length) * sg):0.0} dB.");
            foreach (var (mix, end) in new[] { (a, a.Buffer.Length), (w, w.Buffer.Length), (bMix, enter) })
            {
                double rms = Rms(mix.Buffer, 0, end), peak = Peak(mix.Buffer, 0, end);
                report.AppendLine($"   {mix.Name} in game: RMS {Db(rms):0.0} dB ({Db(rms) - Db(songRms):+0.0;-0.0} dB against the song), peak {Db(peak):0.0} dB" + (peak >= 1 ? "  !! clips" : ""));
            }
            report.AppendLine();

            // 2. The start of each clip: the game puts its sample 0 on the beat.
            report.AppendLine("2. Start of each clip (sample 0 is on the beat: silence before the first sound makes it late)");
            var clips = data.clips.Where(c => c != null).Concat(new[] { data.cueA, data.cueWait, data.cueB, data.clockTick, data.clockAccent }.Where(c => c != null)).Distinct();
            int late = 0; var slow = new List<string>();
            foreach (var clip in clips)
            {
                var s = Samples(clip); float peak = Peak(s, 0, s.Length);
                double silent = Array.FindIndex(s, x => Math.Abs(x) > peak * .01f) * 1000.0 / Rate; // -40 dB under its peak
                double rise = Array.FindIndex(s, x => Math.Abs(x) > peak * .1f) * 1000.0 / Rate;    // -20 dB under its peak
                if (silent > 5) { late++; report.AppendLine($"   {clip.name}: silent for {silent:0} ms, so it sounds that late"); }
                else if (rise > 30) slow.Add($"{clip.name} {rise:0} ms");
            }
            if (late == 0) report.AppendLine("   No clip starts with silence: every one sounds on its first samples.");
            if (slow.Count > 0) report.AppendLine("   Swells (no silence, a slow rise to -20 dB of their peak): " + string.Join(", ", slow) + ".");
            report.AppendLine();

            // 3. Every cut, and what masks it.
            report.AppendLine("3. Cuts (a source stopped while its clip still sounds)");
            int cuts = 0;
            foreach (var mix in mixes) foreach (var line in CutLines(mix)) { report.AppendLine("   " + line); cuts++; }
            if (cuts == 0) report.AppendLine("   None.");
            report.AppendLine();

            // 4. What carries over each join.
            report.AppendLine("4. What still sounds across each join");
            foreach (var mix in mixes)
                foreach (var (time, what) in mix.Joins)
                    report.AppendLine($"   {mix.Name} @ {time - mix.Start:0.000} s, {what}: {Carrying(mix, time)}");
            report.AppendLine();

            // 5. Gaps: beats with next to nothing in sections A and B.
            report.AppendLine("5. Gaps (a beat 40 dB or more below the song's level)");
            foreach (var (mix, beats) in new[] { (a, 32), (bMix, (int)IntroTimeline.AfterTear) })
            {
                var quiet = new List<int>();
                for (int b = 0; b < beats; b++)
                    if (Db(Rms(mix.Buffer, mix.Index(b * spb), mix.Index((b + 1) * spb))) < Db(songRms) - 40) quiet.Add(b);
                report.AppendLine($"   {mix.Name}: " + (quiet.Count == 0 ? "no gaps." : "quiet beats " + string.Join(", ", quiet)));
            }

            var events = new StringBuilder("file,time_s,beat,event\n");
            foreach (var mix in mixes)
            {
                WriteWav(Path.Combine(dir, mix.Name + ".wav"), mix.Buffer, Headroom / Math.Max(Peak(mix.Buffer, 0, mix.Buffer.Length), 1e-9f));
                foreach (var s in mix.Sounds.Where(s => s.Played).OrderBy(s => s.At))
                    events.AppendLine(string.Format(inv, "{0},{1:0.000},{2:0.###},{3}{4}", mix.Name, s.At - mix.Start, s.At / spb, s.What,
                        s.Cut < s.End ? string.Format(inv, " (cut at {0:0.000} s)", s.Cut - mix.Start) : ""));
                foreach (var (time, what) in mix.Joins)
                    events.AppendLine(string.Format(inv, "{0},{1:0.000},{2:0.###},join: {3}", mix.Name, time - mix.Start, time / spb, what));
            }
            File.WriteAllText(Path.Combine(dir, "events.csv"), events.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "audio_check.txt"), report.ToString(), new UTF8Encoding(true));
            return report.ToString();
        }

        /// <summary>
        /// Each moment where sounds are cut: how loud they were just before, the step the cut leaves, and how loud what keeps
        /// playing is. A cut 12 dB or more under what keeps playing is masked; louder, the stop is heard (a click if the step is large).
        /// </summary>
        private static IEnumerable<string> CutLines(Mix mix)
        {
            foreach (var group in mix.Sounds.Where(s => s.Played && s.Cut < s.End).GroupBy(s => mix.Index(s.Cut)))
            {
                int n = group.Key, span = Rate / 100; // 10 ms
                var cut = new double[span]; double step = 0;
                foreach (var s in group)
                {
                    int first = mix.Index(s.At);
                    for (int i = 0; i < span; i++) { int k = n - span + i - first; if (k >= 0 && k < s.Clip.Length) cut[i] += s.Clip[k] * s.Gain; }
                    if (n - first < s.Clip.Length) step += s.Clip[n - first] * s.Gain;
                }
                double cutRms = Math.Sqrt(cut.Sum(x => x * x) / span), rest = Rms(mix.Buffer, n, n + 2 * span);
                string verdict = Db(cutRms) < -75 ? "inaudible" : Db(cutRms) <= Db(rest) - 12 ? "masked by what keeps playing"
                    : Math.Abs(step) > rest ? "HEARD, a click" : "HEARD as an abrupt stop";
                yield return $"{mix.Name} @ {(double)n / Rate:0.000} s: {string.Join(", ", group.Select(s => s.What))}; " +
                    $"they were at {Db(cutRms):0.0} dB, step {Db(Math.Abs(step)):0.0} dB, what keeps playing {Db(rest):0.0} dB: {verdict}.";
            }
        }
        /// <summary>The sounds still playing at <paramref name="time"/>, with how much of each is left.</summary>
        private static string Carrying(Mix mix, double time)
        {
            var on = mix.Sounds.Where(s => s.Played && s.At < time - 1e-6 && s.Cut > time + 1e-6).ToList();
            if (on.Count == 0) return "nothing (a clean start).";
            return string.Join("; ", on.Select(s =>
            {
                int k = (int)Math.Round((time - s.At) * Rate);
                double level = Rms(s.Clip, k, k + Rate / 20) * s.Gain;
                return $"{s.What} rings {s.Cut - time:0.00} s more at {Db(level):0.0} dB";
            })) + ".";
        }

        // ---------------------------------------------------------------- against a recording of the game
        [Serializable] private class CaptureInfo { public int rate, frames, gaps; public double startDsp, origin, tearIntroBeat, pressDsp, handoverDsp, jumpTo, scale, musicVolume; public bool tutorialNext; }
        /// <summary>
        /// Compares a recording of the running game (output/intro-assets/<paramref name="name"/>.wav and .json, written by
        /// PrologueAudioTests) with the model of the same run: how closely they agree in each part, every sound's level against
        /// the model's, and the moments where they differ. Writes <paramref name="name"/>_check.txt and returns it.
        /// </summary>
        public static string CompareCapture(string dir, string name)
        {
            var ctx = Load();
            var info = JsonUtility.FromJson<CaptureInfo>(File.ReadAllText(Path.Combine(dir, name + ".json")));
            var cap = Resample(ReadWav(Path.Combine(dir, name + ".wav"), 1 / info.scale), info.rate);
            double spb = ctx.Spb, t0 = info.startDsp - info.origin;
            double press = info.pressDsp > 0 ? info.pressDsp - info.origin : info.tearIntroBeat * spb;
            var model = ctx.Run(name + "_model", t0, t0 + (double)cap.Length / Rate, info.jumpTo, info.tearIntroBeat, press, info.tutorialNext);
            var m = model.Buffer;
            double tear = info.tearIntroBeat * spb, handover = tear + IntroTimeline.AfterTear * spb;
            int n = Math.Min(m.Length, cap.Length);
            // Fine alignment (the recording's start is known to the DSP block), then the overall gain (music volume, panning).
            int lag = BestLag(cap, m, model.Index(tear - .5), model.Index(tear + 4), Rate / 100);
            float C(int i) => i + lag >= 0 && i + lag < cap.Length ? cap[i + lag] : 0;
            int fitEnd = Math.Min(n, model.Index(info.tutorialNext ? handover : handover + ctx.SongAfterHandover));
            double cm = 0, mm = 0;
            for (int i = 0; i < fitEnd; i++) { cm += C(i) * m[i]; mm += m[i] * m[i]; }
            double g = mm > 0 ? cm / mm : 1;
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = (float)(C(i) - g * m[i]);

            var inv = CultureInfo.InvariantCulture;
            var report = new StringBuilder();
            report.AppendLine($"{name}: the game's own output against the model of the same run ({DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv)})");
            report.AppendLine($"   {(double)cap.Length / Rate:0.0} s at {info.rate} Hz; recorded from intro beat {t0 / spb:0.00}; J pressed {(press - tear) * 1000:+0;-0} ms from intro beat {info.tearIntroBeat}; then " +
                (info.tutorialNext ? "the tutorial." : "the song.") + $" DSP gaps in the recording: {info.gaps}.");
            report.AppendLine($"   Alignment: {lag * 1000.0 / Rate:+0.00;-0.00} ms from the DSP clock; gain {Db(g):0.0} dB (music volume {Db(info.musicVolume):0.0} dB).");
            report.AppendLine("1. Agreement (difference against the recording; -20 dB or lower means the model is what the game plays)");
            foreach (var (label, from, to) in new[] { ("section A", t0, 32 * spb), ("waiting bars", 32 * spb, tear), ("section B", tear, handover), ("after the handover", handover, model.End) })
            {
                int i0 = Math.Max(0, model.Index(from)), i1 = Math.Min(n, model.Index(to));
                if (i1 - i0 < Rate / 10) continue;
                double rc = Rms(cap, i0 + lag, i1 + lag), rr = Rms(r, i0, i1);
                report.AppendLine($"   {label}: recording {Db(rc):0.0} dB, difference {Db(rr) - Db(rc):0.0} dB");
            }
            // Every sound: is it there, at its level? (Over its first 150 ms, so the cut ones are judged on what plays.)
            report.AppendLine("2. Sounds louder or quieter than the model by 3 dB or more over their first 150 ms (overlaps are judged together)");
            int odd = 0;
            foreach (var s in model.Sounds.Where(s => s.Played && s.At >= t0 && s.At < model.End - .2).OrderBy(s => s.At))
            {
                int i0 = model.Index(s.At), i1 = Math.Min(n, model.Index(Math.Min(s.Cut, s.At + .15)));
                if (i1 - i0 < Rate / 200) continue;
                double want = Rms(m, i0, i1) * g, got = Rms(cap, i0 + lag, i1 + lag);
                if (Db(want) < -80) continue;
                double d = Db(got) - Db(want);
                if (Math.Abs(d) >= 3) { odd++; report.AppendLine($"   intro beat {s.At / spb:0.00}: {s.What}: {d:+0.0;-0.0} dB"); }
            }
            if (odd == 0) report.AppendLine("   None: every sound is there at its level.");
            // Moments where the difference stands out from what plays around it.
            report.AppendLine("3. Moments where the recording differs (5 ms against the 50 ms around it; the largest first)");
            var spots = new List<(double ratio, int at)>();
            int win = Rate / 200;
            for (int i = 0; i + win < n; i += win)
            {
                double rr = Rms(r, i, i + win), around = Rms(cap, i + lag - 5 * win, i + lag + 5 * win);
                if (rr > 1e-4 && rr > .3 * around) spots.Add((rr / Math.Max(around, 1e-9), i));
            }
            var merged = new List<(double ratio, int at)>();
            foreach (var spot in spots.OrderBy(sp => sp.at))
            {
                int last = merged.Count - 1;
                if (last >= 0 && spot.at - merged[last].at <= 4 * win) { if (spot.ratio > merged[last].ratio) merged[last] = (spot.ratio, merged[last].at); }
                else merged.Add(spot);
            }
            foreach (var (ratio, at) in merged.OrderByDescending(sp => sp.ratio).Take(12))
            {
                double time = model.Start + (double)at / Rate;
                var near = model.Sounds.Where(s => s.Played && (Math.Abs(s.At - time) < .06 || Math.Abs(s.Cut - time) < .06)).Select(s => s.What).Take(3).ToList();
                report.AppendLine($"   intro beat {time / spb:0.00} ({time:0.000} s): difference {Db(ratio):+0.0;-0.0} dB against its surroundings; near " +
                    (near.Count > 0 ? string.Join(", ", near) : "nothing scheduled"));
            }
            if (merged.Count == 0) report.AppendLine("   None.");
            File.WriteAllText(Path.Combine(dir, name + "_check.txt"), report.ToString(), new UTF8Encoding(true));
            WriteWav(Path.Combine(dir, name + "_model.wav"), m, (float)(info.scale * g));
            // Every 100 ms, for a closer look: the recording, the model at the game's gain, and their difference.
            var profile = new StringBuilder("time_s,intro_beat,recording_db,model_db,difference_db\n");
            for (int i = 0; i + Rate / 10 <= n; i += Rate / 10)
                profile.AppendLine(string.Format(inv, "{0:0.0},{1:0.00},{2:0.0},{3:0.0},{4:0.0}", model.Start + (double)i / Rate, (model.Start + (double)i / Rate) / spb,
                    Db(Rms(cap, i + lag, i + lag + Rate / 10)), Db(Rms(m, i, i + Rate / 10) * g), Db(Rms(r, i, i + Rate / 10))));
            File.WriteAllText(Path.Combine(dir, name + "_profile.csv"), profile.ToString());
            return report.ToString();
        }
        private static int BestLag(float[] cap, float[] model, int from, int to, int range)
        {
            from = Math.Max(range, from); to = Math.Min(Math.Min(model.Length, cap.Length) - range, to);
            int best = 0; double bestScore = double.NegativeInfinity;
            for (int lag = -range; lag <= range; lag++)
            {
                double score = 0;
                for (int i = from; i < to; i += 2) score += cap[i + lag] * model[i];
                if (score > bestScore) { bestScore = score; best = lag; }
            }
            return best;
        }

        // ---------------------------------------------------------------- measuring and files
        private static double Rms(float[] x, int from, int to)
        {
            from = Math.Max(0, from); to = Math.Min(x.Length, to);
            if (to <= from) return 0;
            double sum = 0; for (int i = from; i < to; i++) sum += x[i] * x[i];
            return Math.Sqrt(sum / (to - from));
        }
        private static float Peak(float[] x, int from, int to)
        {
            float peak = 0; for (int i = Math.Max(0, from); i < Math.Min(x.Length, to); i++) peak = Math.Max(peak, Math.Abs(x[i]));
            return peak;
        }
        private static double Db(double v) => v <= 1e-9 ? -180 : 20 * Math.Log10(v);
        private static string Name(AudioClip clip) => clip != null ? clip.name : "(placeholder)";
        private static SongDefinition FirstSong()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:SongDefinition"))
            {
                var song = AssetDatabase.LoadAssetAtPath<SongDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (song != null && song.Music != null) return song;
            }
            throw new InvalidOperationException("No song with music found.");
        }
        /// <summary>16-bit mono 48 kHz WAV of <paramref name="samples"/> times <paramref name="scale"/>.</summary>
        private static void WriteWav(string path, float[] samples, float scale)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            int bytes = samples.Length * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
            foreach (float s in samples) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * scale * 32767), -32768, 32767));
        }
        /// <summary>A 16-bit mono WAV as floats times <paramref name="scale"/>.</summary>
        private static float[] ReadWav(string path, double scale)
        {
            var bytes = File.ReadAllBytes(path);
            int at = 12;
            while (at + 8 <= bytes.Length)
            {
                string id = Encoding.ASCII.GetString(bytes, at, 4); int size = BitConverter.ToInt32(bytes, at + 4);
                if (id == "data")
                {
                    var x = new float[size / 2];
                    for (int i = 0; i < x.Length; i++) x[i] = (float)(BitConverter.ToInt16(bytes, at + 8 + 2 * i) / 32767.0 * scale);
                    return x;
                }
                at += 8 + size;
            }
            throw new InvalidDataException("No data chunk in " + path);
        }
    }
}
