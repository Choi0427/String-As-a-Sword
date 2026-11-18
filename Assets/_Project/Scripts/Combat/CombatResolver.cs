using System;
using StringAsASword.Player;
using StringAsASword.Rhythm;

namespace StringAsASword.Combat
{
    /// <summary>Commits accepted attacks at the end of their beat, before phase evaluation.</summary>
    public sealed class CombatResolver
    {
        private readonly AttackResolver attacks;
        private readonly BossHealthGate health;
        private readonly ResonanceState resonance;
        private readonly int damage;
        private int pendingBeat = -1, pendingSegment, pendingDamage;
        public event Action<int> DamageResolved;
        public CombatResolver(AttackResolver attacks, BossHealthGate health, int damage, ResonanceState resonance = null)
        { this.attacks = attacks; this.health = health; this.damage = damage; this.resonance = resonance; }
        public void Reset() => pendingBeat = -1;
        public void Submit(ActionResult action)
        {
            if (!action.Accepted || action.Kind != PlayerActionKind.Attack) return;
            pendingBeat = action.Timing.BeatId;
            pendingSegment = health.ActiveSegment;
            pendingDamage = attacks.DamageRequested(action, damage);
            // Resonance decides what an in-range attack is worth: nothing without it, more for a Perfect.
            if (resonance != null && pendingDamage > 0)
                pendingDamage = resonance.Strike(pendingBeat, action.Timing.Grade == Judgement.Perfect, pendingDamage);
        }
        public int ResolveBeat(int beat)
        {
            if (beat != pendingBeat) return 0;
            pendingBeat = -1;
            int applied = health.ApplyDamage(pendingDamage, pendingSegment);
            DamageResolved?.Invoke(applied);
            return applied;
        }
    }
}
