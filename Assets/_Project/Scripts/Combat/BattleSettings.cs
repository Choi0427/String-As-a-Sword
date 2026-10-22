using System;
using StringAsASword.Rhythm;
using StringAsASword.Stage;

namespace StringAsASword.Combat
{
    public enum PatternKind { Row, Column, LockedCell, TopPressure }
    [Serializable]
    public sealed class PatternDefinition
    {
        public string Name = "Row";
        public PatternKind Kind;
        public int Coordinate = 6, FirstColumn, ColumnCount = 4;
        public int TelegraphNotes = 3, FirstImpact = 7, RepeatNotes = 24;
        public int PhaseMask = 15;
        public bool Enabled = true;
        public PatternDefinition Copy() => (PatternDefinition)MemberwiseClone();
        public static PatternDefinition[] Defaults() => new[] {
            new PatternDefinition(),
            new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 2, FirstImpact = 15 },
            new PatternDefinition { Name = "Top", Kind = PatternKind.TopPressure, TelegraphNotes = 4, FirstImpact = 23, PhaseMask = 14 }
        };
    }
    [Serializable]
    public sealed class BattleSettings
    {
        public string Version = "battle-2", ChartId = "EvenNotes";
        public int NoteCount;
        public double Bpm, PerfectWindow = .07, GoodWindow = .14, InputOffset;
        public int PlayerMaxHp = 3, SpawnInterval = 8, MaxNoise = 2, MaxCollectionCost = 12;
        public int[] Health = { 6, 6, 6, 6 }, NoiseTargets = { 3, 4, 5 };
        public PatternDefinition[] Patterns = PatternDefinition.Defaults();
        public BattleSettings Copy()
        {
            var copy = (BattleSettings)MemberwiseClone();
            copy.Health = (int[])Health.Clone(); copy.NoiseTargets = (int[])NoiseTargets.Clone();
            copy.Patterns = Array.ConvertAll(Patterns, p => p.Copy());
            return copy;
        }
        public static BattleSettings From(SongDefinition song, StageDefinition stage) => new BattleSettings {
            NoteCount = song.NoteCount, Bpm = song.Bpm, PerfectWindow = song.PerfectWindow,
            GoodWindow = song.GoodWindow, InputOffset = song.InputOffset,
            Health = stage.HealthSegments, NoiseTargets = stage.NoiseTargets,
            PlayerMaxHp = stage.PlayerMaxHp, SpawnInterval = stage.NoiseSpawnInterval,
            MaxNoise = stage.MaxActiveNoise, MaxCollectionCost = stage.MaxCollectionCost,
            Patterns = stage.Patterns, Version = stage.CombatVersion
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
            if (Patterns == null) throw new ArgumentException("Pattern list is missing.");
            foreach (var p in Patterns)
                if (p == null || p.TelegraphNotes < 1 || p.FirstImpact <= p.TelegraphNotes || p.RepeatNotes < 1 ||
                    p.Coordinate < 0 || p.Coordinate > 7 || p.FirstColumn < 0 || p.ColumnCount < 1 || p.FirstColumn + p.ColumnCount > 8)
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
