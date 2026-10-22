using System;
using StringAsASword.Rhythm;
using UnityEngine;

namespace StringAsASword.Presentation
{
    public sealed class ScheduledSongAudio : MonoBehaviour
    {
        private readonly AudioSource[] clicks = new AudioSource[8];
        private AudioSource musicSource;
        private AudioClip clickClip;
        private SongClock clock;
        private SongDefinition song;
        private IBeatTimeline timeline;
        private int nextClick;
        public bool Muted { get; private set; }

        private void Awake()
        {
            // Original synthesized click: no dependency on a copyrighted song asset.
            const int sampleRate = 48000;
            var samples = new float[2400];
            for (int i = 0; i < samples.Length; i++)
            {
                double t = i / (double)sampleRate;
                samples[i] = (float)(Math.Sin(2 * Math.PI * 1400 * t) * Math.Exp(-100 * t) * .35);
            }
            clickClip = AudioClip.Create("87 BPM prototype click", samples.Length, 1, sampleRate, false);
            clickClip.SetData(samples, 0);
            musicSource = AddSource();
            for (int i = 0; i < clicks.Length; i++) { clicks[i] = AddSource(); clicks[i].clip = clickClip; }
        }
        private AudioSource AddSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
            return source;
        }
        public void Begin(SongDefinition definition, SongClock songClock, IBeatTimeline beatTimeline)
        {
            Stop(); song = definition; clock = songClock; timeline = beatTimeline;
            nextClick = -song.CountInBeats;
            musicSource.clip = song.Music;
            if (song.Music != null) musicSource.PlayScheduled(clock.Origin - song.AudioStartOffset);
            Schedule();
        }
        public void ToggleMute()
        {
            Muted = !Muted;
            foreach (var source in clicks) source.mute = Muted;
        }
        public void Schedule()
        {
            if (clock == null || !clock.Running) return;
            double now = AudioSettings.dspTime;
            while (nextClick < song.NoteCount && clock.Origin + timeline.TimeOf(nextClick) <= now + .20)
            {
                double target = clock.Origin + timeline.TimeOf(nextClick);
                if (target >= now + .005 && (song.Music == null || nextClick < 0))
                {
                    var source = clicks[(nextClick % clicks.Length + clicks.Length) % clicks.Length];
                    source.volume = nextClick % 4 == 0 ? 1f : .65f;
                    source.PlayScheduled(target);
                }
                nextClick++;
            }
        }
        public void Stop()
        {
            if (musicSource != null) musicSource.Stop();
            foreach (var source in clicks) if (source != null) source.Stop();
            clock = null;
        }
        private void OnDestroy() { Stop(); if (clickClip != null) Destroy(clickClip); }
    }
}
