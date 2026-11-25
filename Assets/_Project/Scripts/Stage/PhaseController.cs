using System;
using StringAsASword.Combat;

namespace StringAsASword.Stage
{
    /// <summary>Reports the boss moving into its next HP segment; checks occur only after a beat resolves.</summary>
    public sealed class PhaseController
    {
        private readonly BossHealthGate health;
        private readonly NoisePickupSystem noise;
        private int lastResolvedBeat = -1, reportedPhase = 1;
        public int PhaseId => health.ActiveSegment + 1;
        public bool Completed => health.Defeated;
        public event Action<int> PhaseUnlocked;
        public PhaseController(BossHealthGate health, NoisePickupSystem noise)
        { this.health = health; this.noise = noise; }
        public void Reset(int sessionId, int seed)
        {
            health.Reset(); noise.Reset(sessionId, seed, NoisePickupSystem.Unlimited); lastResolvedBeat = -1; reportedPhase = 1;
        }
        public bool ResolveBeat(int beat)
        {
            if (beat <= lastResolvedBeat) return false;
            lastResolvedBeat = beat;
            if (PhaseId == reportedPhase) return false;
            reportedPhase = PhaseId;
            PhaseUnlocked?.Invoke(PhaseId);
            return true;
        }
        /// <summary>Analysis runs and developer mode: empties the active segment and opens the next.</summary>
        internal void AdvanceAnalysisPhase()
        {
            if (PhaseId >= 4) return;
            health.ApplyDamage(health.CurrentHp, health.ActiveSegment);
            reportedPhase = PhaseId;
            PhaseUnlocked?.Invoke(PhaseId);
        }
    }
}
