using System;

namespace StringAsASword.Rhythm
{
    public enum Judgement { None, Perfect, Good, Miss }

    public readonly struct TimingResult
    {
        public int BeatId { get; }
        public double ErrorSeconds { get; }
        public Judgement Grade { get; }
        public bool IsHit => Grade == Judgement.Perfect || Grade == Judgement.Good;
        public TimingResult(int beatId, double errorSeconds, Judgement grade)
        { BeatId = beatId; ErrorSeconds = errorSeconds; Grade = grade; }
    }

    public sealed class RhythmJudge
    {
        private readonly IBeatTimeline timeline;
        private readonly double inputOffset;
        public double PerfectWindow { get; }
        public double GoodWindow { get; }
        public RhythmJudge(IBeatTimeline timeline, double perfectWindow = .070, double goodWindow = .140, double inputOffset = 0)
        {
            if (perfectWindow < 0 || goodWindow < perfectWindow) throw new ArgumentOutOfRangeException(nameof(goodWindow));
            this.timeline = timeline; this.inputOffset = inputOffset;
            GoodWindow = Math.Min(goodWindow, timeline.SecondsPerBeat * .45);
            PerfectWindow = Math.Min(perfectWindow, GoodWindow);
        }
        public TimingResult Evaluate(double rawSongTime)
        {
            double corrected = rawSongTime - inputOffset;
            int beat = timeline.NearestBeat(corrected);
            double error = corrected - timeline.TimeOf(beat);
            double distance = Math.Abs(error);
            var grade = beat < 0 || distance > GoodWindow + 1e-9 ? Judgement.Miss
                : distance <= PerfectWindow + 1e-9 ? Judgement.Perfect : Judgement.Good;
            return new TimingResult(beat, error, grade);
        }
        public double CloseTime(int beat) => timeline.TimeOf(beat) + GoodWindow + inputOffset;
        /// <summary>Judges a hold release against its end time; earlier than the Good window is a Miss.</summary>
        public TimingResult EvaluateRelease(int beat, double rawSongTime, double endTime)
        {
            double error = rawSongTime - inputOffset - endTime, distance = Math.Abs(error);
            var grade = distance > GoodWindow + 1e-9 ? Judgement.Miss : distance <= PerfectWindow + 1e-9 ? Judgement.Perfect : Judgement.Good;
            return new TimingResult(beat, error, grade);
        }
        public double ReleaseCloseTime(double endTime) => endTime + GoodWindow + inputOffset;
    }

    /// <summary>One action per monotonically increasing beat; reusable by combat actions.</summary>
    public sealed class ActionGate
    {
        public int LastConsumedBeat { get; private set; } = -1;
        public bool CanConsume(int beat) => beat >= 0 && beat > LastConsumedBeat;
        public bool TryConsume(int beat)
        {
            if (!CanConsume(beat)) return false;
            LastConsumedBeat = beat; return true;
        }
        public void Reset() => LastConsumedBeat = -1;
    }
}
