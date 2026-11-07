using System;

namespace StringAsASword.Rhythm
{
    /// <summary>Replace with an authored beat map later without changing judge or note views.</summary>
    public interface IBeatTimeline
    {
        double SecondsPerBeat { get; }
        double TimeOf(int beatId);
        int NearestBeat(double songTime);
    }

    public sealed class ConstantBeatTimeline : IBeatTimeline
    {
        public double SecondsPerBeat { get; }
        public ConstantBeatTimeline(double bpm)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
            SecondsPerBeat = 60.0 / bpm;
        }
        public double TimeOf(int beatId) => beatId * SecondsPerBeat;
        // Equidistant inputs belong to the earlier beat.
        public int NearestBeat(double songTime) => (int)Math.Ceiling(songTime / SecondsPerBeat - 0.5);
    }

    /// <summary>Authored chart: beatId is the note index and TimeOf is that note's time in seconds.</summary>
    public sealed class AuthoredBeatTimeline : IBeatTimeline
    {
        private readonly double[] times;
        /// <summary>Used only for count-in, approach distance and extrapolation outside the chart.</summary>
        public double SecondsPerBeat { get; }
        public AuthoredBeatTimeline(double bpm, double[] noteTimes)
        {
            if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
            if (noteTimes == null || noteTimes.Length == 0) throw new ArgumentException("A chart needs at least one note.", nameof(noteTimes));
            for (int i = 0; i < noteTimes.Length; i++)
                if (double.IsNaN(noteTimes[i]) || double.IsInfinity(noteTimes[i]) || noteTimes[i] < 0 || i > 0 && noteTimes[i] <= noteTimes[i - 1])
                    throw new ArgumentException("Note times must be finite, non-negative and strictly increasing.", nameof(noteTimes));
            SecondsPerBeat = 60.0 / bpm; times = (double[])noteTimes.Clone();
        }
        public double TimeOf(int beatId)
        {
            if (beatId < 0) return beatId * SecondsPerBeat;
            int last = times.Length - 1;
            return beatId <= last ? times[beatId] : times[last] + (beatId - last) * SecondsPerBeat;
        }
        // Equidistant inputs belong to the earlier note, as in ConstantBeatTimeline.
        public int NearestBeat(double songTime)
        {
            int last = times.Length - 1;
            if (songTime < 0) return (int)Math.Ceiling(songTime / SecondsPerBeat - 0.5);
            if (songTime >= times[last]) return last + (int)Math.Ceiling((songTime - times[last]) / SecondsPerBeat - 0.5);
            int lo = 0, hi = last; // first index with times[index] >= songTime
            while (lo < hi) { int mid = (lo + hi) >> 1; if (times[mid] < songTime) lo = mid + 1; else hi = mid; }
            return lo > 0 && songTime - times[lo - 1] <= times[lo] - songTime ? lo - 1 : lo;
        }
    }

    public sealed class BeatScheduler
    {
        private readonly IBeatTimeline timeline;
        public int NextBeat { get; private set; }
        public BeatScheduler(IBeatTimeline timeline) { this.timeline = timeline; }
        public void Reset() => NextBeat = 0;
        public void Advance(double songTime, Action<int> onBeat)
        {
            while (timeline.TimeOf(NextBeat) <= songTime) onBeat(NextBeat++);
        }
    }
}
