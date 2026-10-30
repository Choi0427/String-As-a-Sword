using System;
using StringAsASword.Rhythm;
using StringAsASword.Stage;

namespace StringAsASword.Combat
{
    public enum PatternKind { Row, Column, LockedCell, TopPressure, Cross, FullBoard, UpperHalf, Checker, Ring }
    // Either: guard or leave. GuardOnly: dodge-route checks are skipped. DodgeOnly: a guard does not protect.
    public enum AttackResponse { Either, GuardOnly, DodgeOnly }
    [Serializable]
    public sealed class PatternDefinition
    {
        public string Name = "Row";
        public PatternKind Kind;
        public AttackResponse Response;
        public int Coordinate = 6, FirstColumn, ColumnCount = 4;
        public int TelegraphNotes = 3, FirstImpact = 7, RepeatNotes = 24;
        public int PhaseMask = 15, SelectionWeight = 1;
        // Random schedules publish every hit together; fixed mode fires the first hit of each repeat only.
        public int Hits = 1, HitSpacing = 1;
        public const int MaxHits = 8, MaxHitSpacing = 4;
        public int Span => (Hits - 1) * HitSpacing;
        public bool Enabled = true;
        public PatternDefinition Copy() => (PatternDefinition)MemberwiseClone();
        public static PatternDefinition[] Defaults() => new[] {
            new PatternDefinition(),
            new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 2, FirstImpact = 15 },
            new PatternDefinition { Name = "Top", Kind = PatternKind.TopPressure, TelegraphNotes = 4, FirstImpact = 23, PhaseMask = 14 }
        };
    }
    [Serializable]
    public sealed class RandomAttackSchedule
    {
        public bool Enabled;
        public int MinInterval = 4;
        public int[] IntervalWeights = { 1, 1, 5, 5, 5, 1, 1, 1, 1, 1, 1, 1, 1 };
        public const int WarningCeiling = 4;
        public int MinWarning = 1, MaxWarning = 3, CandidateAttempts = 16, MaxDeferrals = 8;
        public RandomAttackSchedule Copy()
        { var copy = (RandomAttackSchedule)MemberwiseClone(); copy.IntervalWeights = (int[])IntervalWeights.Clone(); return copy; }
        public void Validate()
        {
            if (MinWarning < 1 || MaxWarning > WarningCeiling || MinWarning > MaxWarning || MinInterval < 4 ||
                IntervalWeights == null || IntervalWeights.Length < 1 || MinInterval + IntervalWeights.Length - 1 > 16 ||
                CandidateAttempts < 1 || CandidateAttempts > 64 || MaxDeferrals < 0 || MaxDeferrals > 64)
                throw new ArgumentException("Invalid random attack schedule bounds.");
            int total = 0;
            foreach (int weight in IntervalWeights)
            { if (weight < 0 || weight > 10000) throw new ArgumentException("Invalid interval weight."); total += weight; }
            if (total == 0) throw new ArgumentException("An interval must have positive weight.");
        }
    }
    [Serializable]
    public sealed class BattleSettings
    {
        public string Version = "battle-3", ChartId = "EvenNotes";
        public int NoteCount;
        public double Bpm, PerfectWindow = .07, GoodWindow = .14, InputOffset;
        public int PlayerMaxHp = 3, SpawnInterval = 8, MaxNoise = 2, MaxCollectionCost = 12;
        public int[] Health = { 6, 6, 6, 6 }, NoiseTargets = { 3, 4, 5 };
        public PatternDefinition[] Patterns = PatternDefinition.Defaults();
        public RandomAttackSchedule Schedule = new RandomAttackSchedule();
        public BattleSettings Copy()
        {
            var copy = (BattleSettings)MemberwiseClone();
            copy.Health = (int[])Health.Clone(); copy.NoiseTargets = (int[])NoiseTargets.Clone();
            copy.Patterns = Array.ConvertAll(Patterns, p => p.Copy());
            copy.Schedule = Schedule.Copy();
            return copy;
        }
        public static BattleSettings From(SongDefinition song, StageDefinition stage) => new BattleSettings {
            NoteCount = song.NoteCount, Bpm = song.Bpm, PerfectWindow = song.PerfectWindow,
            GoodWindow = song.GoodWindow, InputOffset = song.InputOffset,
            Health = stage.HealthSegments, NoiseTargets = stage.NoiseTargets,
            PlayerMaxHp = stage.PlayerMaxHp, SpawnInterval = stage.NoiseSpawnInterval,
            MaxNoise = stage.MaxActiveNoise, MaxCollectionCost = stage.MaxCollectionCost,
            Patterns = stage.Patterns, Version = stage.CombatVersion, Schedule = stage.AttackSchedule
        };
        public void Validate()
        {
            if (NoteCount < 1 || Bpm <= 0 || double.IsNaN(Bpm) || double.IsInfinity(Bpm) ||
                PlayerMaxHp < 1 || SpawnInterval < 1 || MaxNoise < 1 || MaxNoise > 2 || MaxCollectionCost < 2)
                throw new ArgumentException("Invalid battle settings.");
            if (Health == null || Health.Length != 4 || NoiseTargets == null || NoiseTargets.Length != 3)
                throw new ArgumentException("Four health segments and three collection goals required.");
            foreach (int h in Health) if (h < 1) throw new ArgumentException("HP must be positive.");
            foreach (int n in NoiseTargets) if (n < 1) throw new ArgumentException("Goals must be positive.");
            if (Patterns == null || Patterns.Length > 64) throw new ArgumentException("Pattern list must contain at most 64 entries.");
            if (Schedule == null) throw new ArgumentException("Attack schedule is missing.");
            Schedule.Validate();
            foreach (var p in Patterns)
                if (p == null || p.TelegraphNotes < 1 || p.FirstImpact <= p.TelegraphNotes || p.RepeatNotes < 1 ||
                    p.Coordinate < 0 || p.Coordinate > 7 || p.FirstColumn < 0 || p.ColumnCount < 1 || p.FirstColumn + p.ColumnCount > 8 ||
                    p.SelectionWeight < 0 || p.SelectionWeight > 10000 || !Enum.IsDefined(typeof(PatternKind), p.Kind) ||
                    !Enum.IsDefined(typeof(AttackResponse), p.Response) || p.Hits < 1 || p.Hits > PatternDefinition.MaxHits ||
                    p.HitSpacing < 1 || p.HitSpacing > PatternDefinition.MaxHitSpacing || p.Kind == PatternKind.FullBoard && p.Response != AttackResponse.GuardOnly ||
                    Schedule.Enabled && p.Kind == PatternKind.TopPressure && p.ColumnCount >= 8 && p.Response != AttackResponse.DodgeOnly)
                    throw new ArgumentException("Invalid pattern range, warning or schedule.");
        }
    }
    public sealed class SeededRandom
    {
        private uint state;
        public SeededRandom(int seed) { state = unchecked((uint)seed) ^ 0x9e3779b9u; if (state == 0) state = 1; }
        private uint Next() { uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; return state = x; }
        public int Next(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            uint bound = (uint)count, threshold = unchecked(0u - bound) % bound, value;
            do { value = Next(); } while (value < threshold);
            return (int)(value % bound);
        }
    }
}
