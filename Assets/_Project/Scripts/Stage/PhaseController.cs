using System;
using StringAsASword.Combat;

namespace StringAsASword.Stage
{
    /// <summary>Three melody steals unlock four HP segments; checks occur only after a beat resolves.</summary>
    public sealed class PhaseController
    {
        private readonly BossHealthGate health;
        private readonly NoisePickupSystem noise;
        private readonly int[] targets;
        private int lastResolvedBeat = -1;
        public int PhaseId => health.ActiveSegment + 1;
        public int Steals => health.ActiveSegment;
        public bool Completed => health.Defeated;
        public event Action<int> PhaseUnlocked;
        public PhaseController(BossHealthGate health, NoisePickupSystem noise, int[] targets)
        {
            if (targets == null || targets.Length != 3) throw new ArgumentException("Three noise goals are required.");
            this.health = health; this.noise = noise; this.targets = (int[])targets.Clone();
            foreach (int target in targets) if (target < 1) throw new ArgumentOutOfRangeException(nameof(targets));
        }
        public void Reset(int sessionId, int seed)
        {
            health.Reset(); noise.Reset(sessionId, seed, targets[0]); lastResolvedBeat = -1;
        }
        public bool ResolveBeat(int beat, int nextMusicBeat = -1)
        {
            if (beat <= lastResolvedBeat) return false;
            lastResolvedBeat = beat;
            if (PhaseId >= 4 || !health.CurrentDepleted || !noise.GoalReached) return false;
            health.UnlockNext();
            noise.BeginPhase(PhaseId, PhaseId < 4 ? targets[PhaseId - 1] : 0, nextMusicBeat >= 0 ? nextMusicBeat : beat + 1);
            PhaseUnlocked?.Invoke(PhaseId);
            return true;
        }
        internal void AdvanceAnalysisPhase(int nextMusicBeat)
        {
            if (PhaseId >= 4) return;
            health.ApplyDamage(health.CurrentHp, health.ActiveSegment);
            health.UnlockNext();
            noise.BeginPhase(PhaseId, PhaseId < 4 ? targets[PhaseId - 1] : 0, nextMusicBeat);
            PhaseUnlocked?.Invoke(PhaseId);
        }
    }
}
