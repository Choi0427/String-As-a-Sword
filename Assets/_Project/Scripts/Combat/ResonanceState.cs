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
        public const int DefaultPerfect = 2, DefaultGood = 1, DefaultCadence = 2;
        public int PerfectDamage { get; }
        public int GoodDamage { get; }
        public int CadenceBonus { get; }
        /// <summary>Damage of an attack in range without resonance (default 0).</summary>
        public int GhostDamage { get; }
        /// <summary>Largest damage one accepted strike can request.</summary>
        public int MaxStrike => Math.Max(PerfectDamage + CadenceBonus, Math.Max(GoodDamage, GhostDamage));
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

        public ResonanceState(int length, int perfectDamage = DefaultPerfect, int goodDamage = DefaultGood, int cadenceBonus = DefaultCadence, int ghostDamage = 0)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (perfectDamage < 0) throw new ArgumentOutOfRangeException(nameof(perfectDamage));
            if (goodDamage < 0) throw new ArgumentOutOfRangeException(nameof(goodDamage));
            if (cadenceBonus < 0) throw new ArgumentOutOfRangeException(nameof(cadenceBonus));
            if (ghostDamage < 0) throw new ArgumentOutOfRangeException(nameof(ghostDamage));
            Length = length; PerfectDamage = perfectDamage; GoodDamage = goodDamage; CadenceBonus = cadenceBonus; GhostDamage = ghostDamage;
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
                if (!Charged) { GhostStrike?.Invoke(note); return GhostDamage; }
                Charged = false; EndNote = note + Length - 1; Ignited?.Invoke(note);
            }
            int damage = perfect ? PerfectDamage : GoodDamage;
            if (perfect && note == EndNote) { damage += CadenceBonus; Cadence?.Invoke(note); }
            return damage;
        }
    }
}
