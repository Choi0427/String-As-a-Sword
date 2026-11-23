using StringAsASword.Core;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The UI sounds (Docs/Sfx.md): the prologue's metronome on a move, its chess piece on a confirm. A small pool of 2D sources on an object that
    /// survives scene loads, so a menu's confirm keeps ringing into the battle scene. Every sound follows the saved 음량.
    /// An empty clip is silent.
    /// </summary>
    public static class Sfx
    {
        private const int Voices = 8;
        private static AudioSource[] sources;
        private static int next;
        /// <summary>Clips played since the game started, for tests.</summary>
        public static int PlayCount { get; private set; }
        /// <summary>The last clip played (null when none or empty), for tests.</summary>
        public static AudioClip LastClip { get; private set; }

        public static void UiMove(ArtSet art) { if (art != null) Play(art.uiMoveSound, art.uiMoveVolume); }
        public static void UiConfirm(ArtSet art) { if (art != null) Play(art.uiConfirmSound, art.uiConfirmVolume); }
        public static void Play(AudioClip clip, float volume = 1)
        {
            if (clip == null) return;
            PlayCount++; LastClip = clip;
            if (sources == null || sources[0] == null) Build();
            var source = sources[next]; next = (next + 1) % Voices;
            source.PlayOneShot(clip, Mathf.Clamp01(volume) * GameFlow.MusicVolume);
        }
        private static void Build()
        {
            var host = new GameObject("Sfx");
            Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideInHierarchy;
            sources = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
                // Above the song (128) so a dense chart never steals these voices.
                source.priority = 32;
            }
            sources = host.GetComponents<AudioSource>();
            next = 0;
        }
    }
}
