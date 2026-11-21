using System;
using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Combat;

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
        private SeededRandom random;
        private int deferredBeat = -1, attempts;
        public Func<GridPosition, bool, bool> CandidateAllowed;
        /// <summary>While true nothing spawns; a spawn that fell due in that time happens on the first beat after it.</summary>
        public Func<bool> SpawnBlocked;
        /// <summary>A goal that is never reached: noise keeps spawning for the whole battle.</summary>
        public const int Unlimited = int.MaxValue;
        private bool held;
        public event Action<string, int> SpawnStatus;
        public bool WaitingForSafeSpawn => deferredBeat >= 0;
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
            SessionId = sessionId; Seed = seed; random = new SeededRandom(seed); nextId = 0;
            BeginPhase(1, required, 0);
        }
        public void BeginPhase(int phaseId, int required, int firstSpawnBeat)
        {
            if (required < 0 || firstSpawnBeat < 0) throw new ArgumentOutOfRangeException(nameof(required));
            PhaseId = phaseId; Required = required; Collected = 0;
            pickups.Clear(); initialSpawnBeat = firstSpawnBeat; lastBeat = firstSpawnBeat - 1;
            deferredBeat = -1; attempts = 0; held = false;
        }
        public void AdvanceToBeat(int beat, GridPosition player)
        {
            if (random == null) return;
            while (lastBeat < beat)
            {
                int current = ++lastBeat;
                if (!AutoSpawn) continue;
                bool due = current == initialSpawnBeat || current % interval == 0 || deferredBeat >= 0 && current > deferredBeat;
                if (GoalReached) continue;
                if (SpawnBlocked != null && SpawnBlocked()) { held |= due; continue; }
                if (due || held) { held = false; Spawn(current, player); }
            }
        }
        private void Spawn(int beat, GridPosition player)
        {
            if (pickups.Count >= capacity) { deferredBeat = -1; attempts = 0; SpawnStatus?.Invoke("capacity", beat); return; }
            candidates.Clear();
            for (int y = 0; y < board.Height; y++) for (int x = 0; x < board.Width; x++)
            {
                var cell = new GridPosition(x, y);
                if (cell.Equals(player)) continue;
                bool occupied = false;
                foreach (var pickup in pickups) if (pickup.Cell.Equals(cell)) { occupied = true; break; }
                if (!occupied && (CandidateAllowed == null || CandidateAllowed(cell, attempts >= 2))) candidates.Add(cell);
            }
            if (candidates.Count == 0)
            {
                // Bounded retries. Future regular opportunities remain on the original music-beat grid.
                if (++attempts > interval + 2) { deferredBeat = -1; attempts = 0; SpawnStatus?.Invoke("no-safe-candidate", beat); }
                else { deferredBeat = beat; SpawnStatus?.Invoke(attempts > 2 ? "fallback-deferred" : "deferred", beat); }
                return;
            }
            if (attempts >= 2) SpawnStatus?.Invoke("fallback", beat);
            var item = new NoisePickup(++nextId, SessionId, PhaseId, beat, candidates[random.Next(candidates.Count)]);
            deferredBeat = -1; attempts = 0;
            pickups.Add(item); Spawned?.Invoke(item);
        }
        /// <summary>Off: nothing spawns by itself (the tutorial places its noise with <see cref="SpawnAt"/>). Kept across resets.</summary>
        public bool AutoSpawn { get; set; } = true;
        /// <summary>Places a noise on <paramref name="cell"/> now. False when the cell is off the board, taken, or the board is full.</summary>
        public bool SpawnAt(GridPosition cell, GridPosition player)
        {
            if (random == null || pickups.Count >= capacity || !Free(cell, player)) return false;
            var item = new NoisePickup(++nextId, SessionId, PhaseId, lastBeat, cell);
            pickups.Add(item); Spawned?.Invoke(item);
            return true;
        }
        /// <summary>
        /// The cell to place a noise near <paramref name="wanted"/>: that cell if it is free, else the same distance the other
        /// way vertically, else the free cell nearest to it (scan order breaks ties).
        /// </summary>
        public GridPosition NearestFree(GridPosition wanted, GridPosition player)
        {
            if (Free(wanted, player)) return wanted;
            var mirrored = new GridPosition(wanted.X, 2 * player.Y - wanted.Y);
            if (Free(mirrored, player)) return mirrored;
            GridPosition best = player; int bestDistance = int.MaxValue;
            for (int y = 0; y < board.Height; y++) for (int x = 0; x < board.Width; x++)
            {
                var cell = new GridPosition(x, y);
                int distance = Math.Abs(x - wanted.X) + Math.Abs(y - wanted.Y);
                if (distance < bestDistance && Free(cell, player)) { best = cell; bestDistance = distance; }
            }
            return best;
        }
        private bool Free(GridPosition cell, GridPosition player)
        {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= board.Width || cell.Y >= board.Height || cell.Equals(player)) return false;
            foreach (var pickup in pickups) if (pickup.Cell.Equals(cell)) return false;
            return true;
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
