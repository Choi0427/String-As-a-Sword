using System;

namespace StringAsASword.Combat
{
    /// <summary>
    /// Resonance: a collected noise charges the string; the first top-row attack while charged ignites
    /// <see cref="Length"/> notes during which attacks hurt the boss. Without resonance an attack passes through.
    /// Counted in note indices, so an early input for the next note cannot shift the window. Length 0 = rule off.
    /// </summary>
    public sealed class ResonanceState
    {
        public const int PerfectDamage = 2, GoodDamage = 1, CadenceBonus = 2;
        public int Length { get; }
        public bool Enabled => Length > 0;
        public bool Charged { get; private set; }
        /// <summary>The last note of the running resonance; -1 when none has been ignited.</summary>
        public int EndNote { get; private set; } = -1;
        /// <summary>Finale: the king is stunned, so nothing charges or ignites any more.</summary>
        public bool Halted { get; private set; }
        public event Action ChargeGained;
        /// <summary>Ignition note.</summary>
        public event Action<int> Ignited;
        /// <summary>An attack in range without resonance: no damage.</summary>
        public event Action<int> GhostStrike;
        /// <summary>A Perfect attack on the resonance's last note.</summary>
        public event Action<int> Cadence;

        public ResonanceState(int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length = length;
        }
        public void Reset() { Charged = false; EndNote = -1; Halted = false; }
        public void Halt() { Charged = false; EndNote = -1; Halted = true; }
        public bool ActiveAt(int note) => EndNote >= 0 && note <= EndNote;
        /// <summary>Resonance notes left counting <paramref name="note"/> itself.</summary>
        public int Remaining(int note) => ActiveAt(note) ? EndNote - note + 1 : 0;
        /// <summary>Charged or resonating at this note: no new noise is wanted, spawned or collected.</summary>
        public bool Busy(int note) => Charged || ActiveAt(note);
        public bool TryCharge(int note)
        {
            if (!Enabled || Halted || Busy(note)) return false;
            Charged = true; ChargeGained?.Invoke();
            return true;
        }
        /// <summary>Damage requested by an accepted attack in range on <paramref name="note"/>.</summary>
        public int Strike(int note, bool perfect, int ruleOffDamage)
        {
            if (!Enabled) return ruleOffDamage;
            if (Halted) return 0;
            if (!ActiveAt(note))
            {
                if (!Charged) { GhostStrike?.Invoke(note); return 0; }
                Charged = false; EndNote = note + Length - 1; Ignited?.Invoke(note);
            }
            int damage = perfect ? PerfectDamage : GoodDamage;
            if (perfect && note == EndNote) { damage += CadenceBonus; Cadence?.Invoke(note); }
            return damage;
        }
    }
}
