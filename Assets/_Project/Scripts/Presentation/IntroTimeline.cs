using System;
using StringAsASword.Rhythm;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// The prologue's beat plan, as pure numbers (decisions D1-C, D3-B, S4-A).
    ///
    /// Two clocks are involved. The intro beat counts from the prologue's start; it runs on until the player tears the
    /// string. The story beat is what the picture shows: section A (0-32), then one bar (32-36) repeated until J is
    /// pressed, then section B from the tear (36) to the handover (56), the last downbeat, which is the song's start.
    /// </summary>
    public static class IntroTimeline
    {
        /// <summary>Story beats.</summary>
        public const double LoopStart = 32, LoopBeats = 4, Tear = 36, Handover = 56;
        /// <summary>Beats from the tear to the handover.</summary>
        public const double AfterTear = Handover - Tear;
        /// <summary>A press this little after a beat still tears on that beat; later ones wait for the next.</summary>
        public const double LateGrace = .25;
        /// <summary>Beats between the handover and the first note: the song's own intro stands in for the count-in.</summary>
        public const int HandoverBeatsBeforeFirstNote = 4;

        public static double StoryBeat(double introBeat, double tearIntroBeat)
        {
            if (tearIntroBeat >= 0) return Tear + (introBeat - tearIntroBeat);
            if (introBeat < LoopStart) return introBeat;
            return LoopStart + Mod(introBeat - LoopStart, LoopBeats);
        }
        /// <summary>
        /// The intro beat the string tears on for a J press at <paramref name="introBeat"/>; -1 before the loop has begun.
        /// It is always a whole beat: a press on (or just after) a beat tears on it, an early one waits for the next.
        /// </summary>
        public static double TearBeat(double introBeat)
        {
            if (introBeat < LoopStart - LateGrace) return -1;
            return Math.Max(LoopStart, Math.Ceiling(introBeat - LateGrace));
        }
        /// <summary>Song time that falls on the handover: a beat on the note grid, the song's intro length before note 0.</summary>
        public static double HandoverSongTime(IBeatTimeline timeline)
            => timeline.TimeOf(0) - HandoverBeatsBeforeFirstNote * timeline.SecondsPerBeat;

        /// <summary>
        /// Which shot a story beat belongs to (prologue revision: one meaning per shot). rule: every piece hangs on the king's
        /// strings and moves one square a beat to the square he lights. offbeat: three times the knight jumps an L to the
        /// left instead of the right, on the beat, then off it, then off it again, and is put back by its string. bind: six
        /// strings catch it by the legs and pin it low to its square; the lowest one lights up and it takes it in its hand.
        /// wait: the bar repeated until J (현을 뜯는다). string: the string comes away and becomes the sword. lanes: the
        /// five left become the lanes, the king takes the pieces away. face: the knight and the king alone, the discarded
        /// noise settling on the board. handover: the battle camera.
        /// </summary>
        public static string Shot(double story)
        {
            if (story < 10) return "rule";
            if (story < 16) return "offbeat";
            if (story < LoopStart) return "bind";
            if (story < Tear) return "wait";
            if (story < 40) return "string";
            if (story < 48) return "lanes";
            if (story < 54) return "face";
            return "handover";
        }
        private static double Mod(double a, double m) => a - m * Math.Floor(a / m);
    }
}
