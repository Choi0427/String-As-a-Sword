using System;

namespace StringAsASword.Rhythm
{
    /// <summary>Absolute DSP origin; neither frame time nor timeScale can introduce drift.</summary>
    public sealed class SongClock
    {
        private readonly Func<double> dspTime;
        private double frozenTime;
        public double Origin { get; private set; }
        public bool Running { get; private set; }
        public double SongTime => Running ? dspTime() - Origin : frozenTime;
        public SongClock(Func<double> dspTime) { this.dspTime = dspTime; }
        public void Start(double leadIn) { Origin = dspTime() + leadIn; frozenTime = -leadIn; Running = true; }
        /// <summary>Starts with song time 0 at the given DSP time (the prologue hands over on a beat it fixed itself).</summary>
        public void StartAt(double origin) { Origin = origin; frozenTime = dspTime() - origin; Running = true; }
        public void Stop() { frozenTime = SongTime; Running = false; }
        public double ToSongTime(double inputDspTime) => inputDspTime - Origin;
    }
}
