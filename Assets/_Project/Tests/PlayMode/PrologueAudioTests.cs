using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace StringAsASword.Tests
{
    /// <summary>
    /// The prologue's sound: what its scheduling relies on in Unity, and (explicit, about a minute of real time each)
    /// recordings of the running game's output for listening and for IntroGuideRenderer.CompareCapture.
    /// </summary>
    public sealed class PrologueAudioTests
    {
        private bool background;
        [SetUp] public void SetUp() { background = Application.runInBackground; Application.runInBackground = true; }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            if (game != null) game.PauseSession();
            Application.runInBackground = background;
            yield return null;
        }
        private static IEnumerator UntilDsp(double dsp, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (AudioSettings.dspTime < dsp && Time.realtimeSinceStartup < end) yield return null;
        }
        private static AudioClip Constant(string name, float seconds, float value = .1f)
        {
            int n = (int)(seconds * 48000);
            var clip = AudioClip.Create(name, n, 1, 48000, false);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = value;
            clip.SetData(data, 0);
            return clip;
        }
        private static IEnumerator OpenThePrologue()
        {
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            GameFlow.StartBattle(prologue: true);
            float until = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != GameFlow.BattleScene && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
        }

        [UnityTest] public IEnumerator ComposedPiecesPlayOnTheirOwnSourcesInPlaceOfThePlaceholderMusic()
        {
            // Silent stand-ins for Cue_A, Cue_Wait and Cue_B (S1-B), each with a short tail, in the asset's memory only.
            var data = Resources.Load<PrologueDefinition>(PrologueDefinition.ResourceName);
            string original = JsonUtility.ToJson(data);
            float spb = 60f / 87;
            var a = Constant("Cue_A", 32 * spb + .5f, 0); var w = Constant("Cue_Wait", 4 * spb + .5f, 0); var b = Constant("Cue_B", 20 * spb + .5f, 0);
            try
            {
                data.cueA = a; data.cueWait = w; data.cueB = b;
                yield return OpenThePrologue();
                var game = Object.FindAnyObjectByType<PrototypeGame>();
                var intro = game.Intro;
                Assert.That(intro.StemASource, Is.Not.Null);
                Assert.That(intro.StemASource.clip, Is.SameAs(a));
                Assert.That(intro.StemASource.isPlaying, "Cue_A is scheduled on the prologue's first beat.");
                // Around the king's first order (beat 8) only the sound effects are scheduled; the order itself is in Cue_A.
                intro.JumpForTests(7.8);
                for (int f = 0; f < 20; f++)
                {
                    yield return null;
                    foreach (var source in intro.PoolSources)
                        Assert.That(source.isPlaying && source.clip != null && PrologueDefinition.IsMusic(source.clip.name), Is.False, source.clip != null ? source.clip.name : "");
                }
                // The waiting bar: two sources take turns, so a bar's tail is not cut by the next bar.
                intro.JumpForTests(IntroTimeline.LoopStart - .1);
                yield return null; yield return null;
                Assert.That(intro.WaitSources[0].isPlaying && intro.WaitSources[0].clip == w, "The first waiting bar.");
                intro.JumpForTests(IntroTimeline.LoopStart + 3.9);
                yield return null; yield return null;
                Assert.That(intro.WaitSources[1].isPlaying, "The next bar on the other source.");
                Assert.That(intro.WaitSources[0].isPlaying, "Starting it does not cut the first one.");
                // J: the waiting bar stops 30 ms into the tear and Cue_B starts on the tear's beat.
                intro.Tear(AudioSettings.dspTime);
                Assert.That(intro.Torn);
                Assert.That(intro.StemBSource.isPlaying && intro.StemBSource.clip == b);
                double tear = intro.HandoverDsp - IntroTimeline.AfterTear * spb;
                yield return UntilDsp(System.Math.Max(tear, AudioSettings.dspTime) + .12, 3);
                Assert.That(intro.WaitSources[0].isPlaying || intro.WaitSources[1].isPlaying, Is.False, "The waiting bar is cut.");
                Assert.That(intro.StemASource.isPlaying, Is.False);
                Assert.That(intro.StemBSource.isPlaying);
                // The handover: Cue_B's end rings on into the tutorial, whose clock starts with the accent on that downbeat.
                intro.JumpForTests(intro.TearIntroBeat + IntroTimeline.AfterTear - .3);
                float deadline = Time.realtimeSinceStartup + 3;
                while (game.State == PrototypeState.Prologue && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial));
                yield return null;
                Assert.That(intro.StemBSource.isPlaying, "Cue_B is not cut on the handover.");
                bool accent = false;
                foreach (var source in intro.PoolSources) accent |= source.isPlaying && source.clip == data.Tick(true);
                Assert.That(accent, "The prologue put the tutorial's first accent on the handover, and it was not cut there.");
            }
            finally
            {
                var game = Object.FindAnyObjectByType<PrototypeGame>();
                if (game != null) game.PauseSession();
                JsonUtility.FromJsonOverwrite(original, data);
                foreach (var clip in new[] { a, w, b }) Object.Destroy(clip);
            }
        }

        [UnityTest] public IEnumerator ASourceCutWithAScheduledEndPlaysItsNextSoundInFull()
        {
            // The prologue cuts the waiting bar's sources with SetScheduledEndTime, and section B plays on the same pool later.
            var probe = new GameObject("Scheduled end probe").AddComponent<AudioSource>();
            probe.ignoreListenerPause = true;
            var clip = Constant("probe", 1);
            try
            {
                double t = AudioSettings.dspTime + .1;
                probe.clip = clip; probe.PlayScheduled(t); probe.SetScheduledEndTime(t + .03);
                yield return UntilDsp(t + .2, 3);
                Assert.That(probe.isPlaying, Is.False, "Cut 30 ms in.");
                probe.Stop(); probe.clip = clip;
                double t2 = AudioSettings.dspTime + .1;
                probe.PlayScheduled(t2);
                yield return UntilDsp(t2 + .3, 3);
                Assert.That(probe.isPlaying, "The old end time does not carry over to the source's next sound.");
            }
            finally { Object.Destroy(probe.gameObject); Object.Destroy(clip); }
        }

        // ---------------------------------------------------------------- recordings
        [UnityTest, Explicit("About 45 s: records the whole prologue, J on the second waiting bar's second beat (output/intro-assets/capture_full.wav).")]
        public IEnumerator RecordTheWholePrologue() => Record("capture_full", jumpTo: -1, tearOn: 37);
        [UnityTest, Explicit("About 20 s: records the prologue from beat 27.25, J on the first waiting bar's last beat (output/intro-assets/capture_into_tutorial.wav).")]
        public IEnumerator RecordThePrologueIntoTheTutorial() => Record("capture_into_tutorial", jumpTo: 27.25, tearOn: 35);

        /// <summary>The prologue as the game plays it, J pressed 50 ms after <paramref name="tearOn"/>, into the tutorial's clock.</summary>
        private static IEnumerator Record(string name, double jumpTo, double tearOn)
        {
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            GameFlow.StartBattle(prologue: true);
            float until = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != GameFlow.BattleScene && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var intro = game.Intro;
            Assert.That(game.TutorialFollowsPrologue);
            // Start again with the recorder running, so the first beat is on the recording.
            intro.Stop();
            var recorder = OutputRecorder.Attach(70);
            try
            {
                yield return null; yield return null;
                game.StartPrologue();
                if (jumpTo >= 0) intro.JumpForTests(jumpTo);
                double spb = game.Timeline.SecondsPerBeat, origin = intro.OriginDsp;
                // J a little after the beat, as a player would press it.
                yield return UntilDsp(origin + (tearOn + .05) * spb, 60);
                double press = AudioSettings.dspTime;
                intro.Tear(press);
                Assert.That(intro.TearIntroBeat, Is.EqualTo(tearOn), "J tore on the planned beat.");
                double handover = intro.HandoverDsp;
                yield return UntilDsp(handover + 4, 30);
                Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial));
                recorder.Recording = false;
                yield return null;
                Assert.That(recorder.Gaps, Is.Zero, "The recording is continuous.");
                Write(name, recorder, origin, intro.TearIntroBeat, press, handover, jumpTo);
            }
            finally { Object.Destroy(recorder); }
        }
        private static void Write(string name, OutputRecorder r, double origin, double tear, double press, double handover, double jumpTo)
        {
            string dir = Path.GetFullPath("output/intro-assets");
            Directory.CreateDirectory(dir);
            int n = r.Count; float peak = 0;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(r.Samples[i]));
            float scale = peak > 0 ? .891f / peak : 1;
            using (var stream = new FileStream(Path.Combine(dir, name + ".wav"), FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 2 * n); writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(r.Rate); writer.Write(r.Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(2 * n);
                for (int i = 0; i < n; i++) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(r.Samples[i] * scale * 32767), -32768, 32767));
            }
            File.WriteAllText(Path.Combine(dir, name + ".json"), string.Format(CultureInfo.InvariantCulture,
                "{{\"rate\":{0},\"frames\":{1},\"gaps\":{2},\"startDsp\":{3:R},\"origin\":{4:R},\"tearIntroBeat\":{5:R},\"pressDsp\":{6:R},\"handoverDsp\":{7:R},\"jumpTo\":{8:R},\"scale\":{9:R},\"musicVolume\":{10:R} }}",
                r.Rate, n, r.Gaps, r.StartDsp, origin, tear, press, handover, jumpTo, (double)scale, (double)GameFlow.MusicVolume));
        }
    }
}
