using System;
using StringAsASword.Rhythm;
using UnityEngine;

namespace StringAsASword.Presentation
{
    public sealed class ScheduledSongAudio : MonoBehaviour
    {
        private readonly AudioSource[] clicks = new AudioSource[8];
        private readonly AudioSource[] countdownClicks = new AudioSource[8];
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
            for (int i = 0; i < countdownClicks.Length; i++) { countdownClicks[i] = AddSource(); countdownClicks[i].clip = clickClip; }
        }
        private AudioSource AddSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
            return source;
        }
        /// <summary>Loads the song's samples now, so scheduling it later does not stall (the prologue schedules it mid-scene).</summary>
        public static void Preload(SongDefinition definition)
        {
            if (definition != null && definition.Music != null) definition.Music.LoadAudioData();
        }
        /// <summary>Schedules the song from its start. There are no count-in clicks: the song's own intro leads in.</summary>
        public void Begin(SongDefinition definition, SongClock songClock, IBeatTimeline beatTimeline)
        {
            Stop(); song = definition; clock = songClock; timeline = beatTimeline;
            nextClick = 0;
            musicSource.clip = song.Music;
            musicSource.volume = song.Volume * Core.GameFlow.MusicVolume;
            if (song.Music != null)
            {
                double start = clock.Origin - song.AudioStartOffset, at = AudioSettings.dspTime + .25;
                musicSource.timeSamples = 0;
                if (start >= at) musicSource.PlayScheduled(start);
                else
                {
                    // The clock starts inside the song (developer mode): start the music from the matching sample.
                    long sample = (long)((at - start) * song.Music.frequency);
                    if (sample < song.Music.samples) { musicSource.timeSamples = (int)sample; musicSource.PlayScheduled(at); }
                }
            }
            Schedule();
        }
        /// <summary>
        /// The resume count: one click on each of <paramref name="beats"/> beats from song time <paramref name="from"/>,
        /// with the song playing or not, so every number of the 4-3-2-1 sounds as it shows. Call after <see cref="Begin"/>.
        /// </summary>
        public void ScheduleCountdown(double from, int beats, double secondsPerBeat)
        {
            if (clock == null || !clock.Running) return;
            double now = AudioSettings.dspTime;
            for (int k = 0; k < beats; k++)
            {
                double target = clock.Origin + from + k * secondsPerBeat;
                if (target < now + .005) continue;
                // Sources of its own, so the metronome (songs without music) never takes over a scheduled count click.
                var source = countdownClicks[k % countdownClicks.Length];
                source.volume = 1f;
                source.PlayScheduled(target);
            }
        }
        /// <summary>Ending fade: 1 = the song's configured volume, 0 = silent.</summary>
        public void SetMusicFade(float scale)
        {
            if (song != null) musicSource.volume = song.Volume * Core.GameFlow.MusicVolume * Mathf.Clamp01(scale);
        }
        public void ToggleMute()
        {
            Muted = !Muted;
            foreach (var source in clicks) source.mute = Muted;
            foreach (var source in countdownClicks) source.mute = Muted;
        }
        public void Schedule()
        {
            if (clock == null || !clock.Running) return;
            double now = AudioSettings.dspTime;
            while (nextClick < song.NoteCount && clock.Origin + timeline.TimeOf(nextClick) <= now + .20)
            {
                double target = clock.Origin + timeline.TimeOf(nextClick);
                if (target >= now + .005 && song.Music == null)
                {
                    var source = clicks[nextClick % clicks.Length];
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
            foreach (var source in countdownClicks) if (source != null) source.Stop();
            clock = null;
        }
        private void OnDestroy() { Stop(); if (clickClip != null) Destroy(clickClip); }
    }
}
