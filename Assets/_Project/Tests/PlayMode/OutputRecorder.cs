using System;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StringAsASword.Tests
{
    /// <summary>
    /// The game's whole output, mixed to mono, as the listener hears it, with the DSP time of its first sample, so a test can
    /// find what sounded on a given beat.
    /// </summary>
    internal sealed class OutputRecorder : MonoBehaviour
    {
        public float[] Samples;
        public int Count, Rate, Gaps;
        public double StartDsp = -1;
        public volatile bool Recording;
        private double next;

        /// <summary>A recorder on the active listener, recording up to <paramref name="seconds"/> from now.</summary>
        public static OutputRecorder Attach(float seconds)
        {
            AudioListener listener = null;
            foreach (var candidate in Object.FindObjectsByType<AudioListener>()) if (candidate.isActiveAndEnabled) listener = candidate;
            Assert.That(listener, Is.Not.Null, "An active AudioListener to record from.");
            var recorder = listener.gameObject.AddComponent<OutputRecorder>();
            recorder.Rate = AudioSettings.outputSampleRate;
            recorder.Samples = new float[(int)(recorder.Rate * seconds)];
            recorder.Recording = true;
            return recorder;
        }
        /// <summary>The loudest sample in [<paramref name="fromDsp"/>, <paramref name="toDsp"/>).</summary>
        public float Peak(double fromDsp, double toDsp)
        {
            int a = Math.Max(0, (int)((fromDsp - StartDsp) * Rate)), b = Math.Min(Count, (int)((toDsp - StartDsp) * Rate));
            float peak = 0;
            for (int i = a; i < b; i++) peak = Math.Max(peak, Math.Abs(Samples[i]));
            return peak;
        }
        /// <summary>Writes the recording to output/intro-assets/<paramref name="name"/>.wav (peak -1 dBFS) with a note beside it.</summary>
        public void Save(string name, string note)
        {
            string dir = System.IO.Path.GetFullPath("output/intro-assets");
            System.IO.Directory.CreateDirectory(dir);
            float peak = 0;
            for (int i = 0; i < Count; i++) peak = Math.Max(peak, Math.Abs(Samples[i]));
            float scale = peak > 0 ? .891f / peak : 1;
            using (var writer = new System.IO.BinaryWriter(System.IO.File.Create(System.IO.Path.Combine(dir, name + ".wav"))))
            {
                var ascii = System.Text.Encoding.ASCII;
                writer.Write(ascii.GetBytes("RIFF")); writer.Write(36 + 2 * Count); writer.Write(ascii.GetBytes("WAVE"));
                writer.Write(ascii.GetBytes("fmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(ascii.GetBytes("data")); writer.Write(2 * Count);
                for (int i = 0; i < Count; i++) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(Samples[i] * scale * 32767), -32768, 32767));
            }
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".txt"), note);
        }
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!Recording || Samples == null) return;
            double dsp = AudioSettings.dspTime;
            int frames = data.Length / channels;
            if (StartDsp < 0) StartDsp = dsp;
            else if (Math.Abs(dsp - next) * Rate > .5) Gaps++;
            next = dsp + (double)frames / Rate;
            int n = Count;
            for (int i = 0; i < frames && n < Samples.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += data[i * channels + c];
                Samples[n++] = sum / channels;
            }
            Count = n;
        }
    }
}
