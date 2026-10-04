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
