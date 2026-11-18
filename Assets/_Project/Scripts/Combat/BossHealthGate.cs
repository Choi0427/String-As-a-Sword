using System;

namespace StringAsASword.Combat
{
    /// <summary>The only writer of boss HP. Four segments drain in order; damage carries over into the next one.</summary>
    public sealed class BossHealthGate
    {
        private readonly int[] maximum;
        private readonly int[] remaining;
        public int SegmentCount => maximum.Length;
        /// <summary>The segment taking damage. It moves on as soon as a segment is emptied, except the last.</summary>
        public int ActiveSegment { get; private set; }
        public int CurrentHp => remaining[ActiveSegment];
        public bool CurrentDepleted => CurrentHp == 0;
        public bool Defeated => ActiveSegment == SegmentCount - 1 && CurrentDepleted;
        public int TotalHp { get { int sum = 0; foreach (int hp in remaining) sum += hp; return sum; } }

        public BossHealthGate(params int[] segments)
        {
            if (segments == null || segments.Length != 4) throw new ArgumentException("Exactly four health segments are required.");
            maximum = (int[])segments.Clone(); remaining = new int[4];
            foreach (int hp in maximum) if (hp < 1) throw new ArgumentOutOfRangeException(nameof(segments));
            Reset();
        }
        public void Reset() { Array.Copy(maximum, remaining, maximum.Length); ActiveSegment = 0; }
        public int Remaining(int index) => remaining[index];
        public int Maximum(int index) => maximum[index];
        /// <summary>Damage submitted against a segment that has since been emptied is dropped.</summary>
        public int ApplyDamage(int requested, int expectedSegment)
        {
            if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
            if (expectedSegment != ActiveSegment) return 0;
            int applied = 0;
            while (requested > applied)
            {
                int taken = Math.Min(requested - applied, CurrentHp);
                remaining[ActiveSegment] -= taken; applied += taken;
                if (CurrentHp > 0 || ActiveSegment >= SegmentCount - 1) break;
                ActiveSegment++;
            }
            return applied;
        }
    }
}
