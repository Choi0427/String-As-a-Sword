using System;
using System.Collections.Generic;
using StringAsASword.Board;

namespace StringAsASword.Stage
{
    public readonly struct NoisePickup
    {
        public int Id { get; }
        public int SessionId { get; }
        public int PhaseId { get; }
        public int SpawnBeat { get; }
        public GridPosition Cell { get; }
        public NoisePickup(int id, int sessionId, int phaseId, int spawnBeat, GridPosition cell)
        { Id = id; SessionId = sessionId; PhaseId = phaseId; SpawnBeat = spawnBeat; Cell = cell; }
    }

    /// <summary>Seeded logical pickups. No colliders, despawn timers or scene dependencies.</summary>
    public sealed class NoisePickupSystem
    {
        private readonly BoardState board;
        private readonly int interval;
        private readonly int capacity;
        private readonly List<NoisePickup> pickups = new List<NoisePickup>();
        private readonly List<GridPosition> candidates = new List<GridPosition>();
        private Random random;
        private int nextId;
        private int lastBeat;
        private int initialSpawnBeat;
        public IReadOnlyList<NoisePickup> Pickups { get; }
        public int SessionId { get; private set; }
        public int PhaseId { get; private set; }
        public int Seed { get; private set; }
        public int Required { get; private set; }
        public int Collected { get; private set; }
        public bool GoalReached => Collected >= Required;
        public event Action<NoisePickup> Spawned;
        public event Action<NoisePickup> CollectedPickup;

        public NoisePickupSystem(BoardState board, int interval = 8, int capacity = 2)
        {
            if (interval < 1 || capacity < 1) throw new ArgumentOutOfRangeException(nameof(interval));
            this.board = board; this.interval = interval; this.capacity = capacity;
            Pickups = pickups.AsReadOnly();
        }
        public void Reset(int sessionId, int seed, int required)
        {
            SessionId = sessionId; Seed = seed; random = new Random(seed); nextId = 0;
            BeginPhase(1, required, 0);
        }
        public void BeginPhase(int phaseId, int required, int firstSpawnBeat)
        {
            if (required < 0 || firstSpawnBeat < 0) throw new ArgumentOutOfRangeException(nameof(required));
            PhaseId = phaseId; Required = required; Collected = 0;
            pickups.Clear(); initialSpawnBeat = firstSpawnBeat; lastBeat = firstSpawnBeat - 1;
        }
        public void AdvanceToBeat(int beat, GridPosition player)
        {
            if (random == null) return;
            while (lastBeat < beat)
            {
                int current = ++lastBeat;
                if (!GoalReached && (current == initialSpawnBeat || current % interval == 0)) Spawn(current, player);
            }
        }
        private void Spawn(int beat, GridPosition player)
        {
            if (pickups.Count >= capacity) return; // Skipped opportunities never accumulate.
            candidates.Clear();
            for (int y = 0; y < board.Height; y++) for (int x = 0; x < board.Width; x++)
            {
                var cell = new GridPosition(x, y);
                if (cell.Equals(player)) continue;
                bool occupied = false;
                foreach (var pickup in pickups) if (pickup.Cell.Equals(cell)) { occupied = true; break; }
                if (!occupied) candidates.Add(cell);
            }
            if (candidates.Count == 0) return;
            var item = new NoisePickup(++nextId, SessionId, PhaseId, beat, candidates[random.Next(candidates.Count)]);
            pickups.Add(item); Spawned?.Invoke(item);
        }
        public bool TryCollect(GridPosition landing, int sessionId, int phaseId)
        {
            if (sessionId != SessionId || phaseId != PhaseId || GoalReached) return false;
            for (int i = 0; i < pickups.Count; i++)
            {
                if (!pickups[i].Cell.Equals(landing)) continue;
                var pickup = pickups[i]; pickups.RemoveAt(i); Collected++;
                CollectedPickup?.Invoke(pickup); return true;
            }
            return false;
        }
    }
}
