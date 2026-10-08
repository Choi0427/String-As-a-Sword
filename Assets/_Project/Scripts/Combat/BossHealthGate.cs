using System;

namespace StringAsASword.Combat
{
    /// <summary>The only writer of boss HP. Damage cannot cross the active segment's floor.</summary>
    public sealed class BossHealthGate
    {
        private readonly int[] maximum;
        private readonly int[] remaining;
        public int SegmentCount => maximum.Length;
        public int ActiveSegment { get; private set; }
        public int CurrentHp => remaining[ActiveSegment];
        public bool CurrentDepleted => CurrentHp == 0;
        public bool Defeated => ActiveSegment == SegmentCount - 1 && CurrentDepleted;
        public int TotalHp { get { int sum = 0; foreach (int hp in remaining) sum += hp; return sum; } }
        public int Floor { get { int sum = 0; for (int i = ActiveSegment + 1; i < SegmentCount; i++) sum += remaining[i]; return sum; } }

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
        public int ApplyDamage(int requested, int expectedSegment)
        {
            if (requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
            if (expectedSegment != ActiveSegment) return 0;
            int applied = Math.Min(requested, CurrentHp);
            remaining[ActiveSegment] -= applied;
            return applied;
        }
        // The phase controller alone decides WHEN a depleted segment may unlock.
        internal bool UnlockNext()
        {
            if (!CurrentDepleted || ActiveSegment >= SegmentCount - 1) return false;
            ActiveSegment++;
            return true;
        }
    }
}
