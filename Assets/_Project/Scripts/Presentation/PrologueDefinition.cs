using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Data for the prologue cutscene (Resources/Prologue): its sounds and their level. The timing lives in
    /// <see cref="IntroTimeline"/> and <see cref="IntroDirector"/>.
    ///
    /// The prologue has five sounds (prologue revision R6-A): the chess clock, a piece set down, the noise's crackle (the
    /// ending's white noise), and two recordings, a string under strain and a string snapping. The last two are slots: until
    /// a file is delivered (String As a Sword > Audio > Set Up Prologue Audio) the strain is silent and the snap is the
    /// crackle over a heavy piece. A composed cue (S1-B) plays on top of them when assigned; see Docs/IntroAssets.md.
    /// </summary>
    [CreateAssetMenu(menuName = "String As a Sword/Prologue", fileName = "Prologue")]
    public sealed class PrologueDefinition : ScriptableObject
    {
        public const string ResourceName = "Prologue";
        /// <summary>Beats of each composed piece (87 BPM): section A, the waiting bar, section B (the tear to the song).</summary>
        public const int CueABeats = 32, CueWaitBeats = 4, CueBBeats = 20;
        /// <summary>How far a piece may fall short of its beats and still count as on length.</summary>
        public const double LengthTolerance = .01;
        /// <summary>The cue names the prologue plays (<see cref="IntroDirector.Cue"/>).</summary>
        public const string ClockName = "clock", PlaceName = "place", NoiseName = "noise", StrainName = "strain", SnapName = "snap";

        [Tooltip("The clips found by name: clock, place, noise.")]
        public AudioClip[] clips;
        [Tooltip("Level of the clips at full music volume. The song plays at 0.03; the clips are mastered louder than it.")]
        [Range(0, 1)] public float volume = .12f;

        [Header("String recordings (R6-A): silent / stood in for until delivered")]
        [Tooltip("A taut string creaking under strain: the knight pinned to the board, struggling, the waiting bar.")]
        public AudioClip stringStrain;
        [Tooltip("A string snapping: the player's J. Until delivered, the noise's crackle over a heavy piece stands in.")]
        public AudioClip stringSnap;

        [Header("Composed cue (S1-B): music under the sounds when assigned")]
        [Tooltip("Section A: sample 0 = story beat 0; 32 beats (22.069 s), a tail may run on.")]
        public AudioClip cueA;
        [Tooltip("The waiting bar: sample 0 = its first beat; 4 beats (2.7586 s), played again every bar until J. Its tail overlaps the next.")]
        public AudioClip cueWait;
        [Tooltip("Section B: sample 0 = the tear's beat; 20 beats (13.793 s) to the song's start. Its tail may run under the song.")]
        public AudioClip cueB;
        [Tooltip("Level of the composed cue at full music volume. The song plays at 0.03: a cue mastered as loud as the song uses the same.")]
        [Range(0, 1)] public float cueVolume = .03f;

        [Header("Chess clock (S2-A): replaces the placeholder 'clock' when assigned")]
        [Tooltip("The clock on an ordinary beat.")]
        public AudioClip clockTick;
        [Tooltip("The clock on a bar line. Recorded in the same session as the tick: its own level carries the accent.")]
        public AudioClip clockAccent;

        /// <summary>The clip a cue names: the string recordings from their slots (null until delivered), the rest by clip name.</summary>
        public AudioClip Clip(string clipName)
        {
            if (clipName == StrainName) return stringStrain;
            if (clipName == SnapName) return stringSnap;
            if (clips == null) return null;
            foreach (var clip in clips) if (clip != null && clip.name == clipName) return clip;
            return null;
        }
        /// <summary>The clock for a beat: the recorded takes when delivered (the accent on bar lines), else the placeholder.</summary>
        public AudioClip Tick(bool accent)
        {
            if (accent && clockAccent != null) return clockAccent;
            if (clockTick != null) return clockTick;
            return clockAccent != null ? clockAccent : Clip(ClockName);
        }
        /// <summary>
        /// Level of a clock beat (times <see cref="volume"/>). The placeholder is one sound, made louder on bar lines; a
        /// recorded accent take carries its own accent, so both takes play at the same level.
        /// </summary>
        public float TickVolume(bool accent) => clockAccent != null || accent ? .45f : .3f;
        public static bool IsClock(string clipName) => clipName == ClockName;
        /// <summary>
        /// Checks a composed piece's length against its beats: null when it is long enough (a tail is allowed), otherwise
        /// what is wrong, for the setup menu's report.
        /// </summary>
        public static string CheckLength(string name, double seconds, int beats, double secondsPerBeat)
        {
            double wanted = beats * secondsPerBeat;
            if (seconds < wanted - LengthTolerance)
                return $"{name}: {seconds:0.000} s is shorter than {beats} beats ({wanted:0.000} s).";
            return null;
        }
    }
}
