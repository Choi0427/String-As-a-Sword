using System;
using System.Collections.Generic;
using StringAsASword.Board;

namespace StringAsASword.Combat
{
    public sealed class BossTelegraph
    {
        public int Id { get; }
        public string Name { get; }
        public ulong Cells { get; }
        public int ImpactNote { get; }
        public BossTelegraph(int id, string name, ulong cells, int impactNote)
        { Id = id; Name = name; Cells = cells; ImpactNote = impactNote; }
        public int Remaining(int lastClosed) => ImpactNote - lastClosed;
    }
    public sealed class BossPatternSystem
    {
        private readonly PatternDefinition[] definitions;
        private readonly List<BossTelegraph> active = new List<BossTelegraph>(), fired = new List<BossTelegraph>();
        private int[] nextImpact;
        private int phase, nextId;
        public IReadOnlyList<BossTelegraph> Active => active.AsReadOnly();
        public IReadOnlyList<BossTelegraph> LastFired => fired.AsReadOnly();
        public Func<BossTelegraph, bool> CanPublish;
        public event Action<string, BossTelegraph> Changed;
        public BossPatternSystem(PatternDefinition[] definitions) { this.definitions = definitions; }
        public void BeginPhase(int phaseId, int firstNote)
        {
            foreach (var a in active) Changed?.Invoke("cancel", a);
            active.Clear(); phase = phaseId; nextImpact = new int[definitions.Length];
            for (int i = 0; i < definitions.Length; i++) nextImpact[i] = firstNote + definitions[i].FirstImpact - 1;
        }
        public void Publish(int lastClosed, GridPosition player, int noteCount, bool hold)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                var p = definitions[i];
                if (!p.Enabled || (p.PhaseMask & (1 << (phase - 1))) == 0 || nextImpact[i] >= noteCount ||
                    lastClosed < nextImpact[i] - p.TelegraphNotes) continue;
                var proposal = new BossTelegraph(nextId + 1, p.Name, Cells(p, player), nextImpact[i]);
                // Preserve every already disclosed safe cell at an existing deadline, not just one lucky route.
                bool invalidates = false;
                foreach (var a in active)
                    if (proposal.ImpactNote <= a.ImpactNote && (proposal.Cells & ~SafeRoutePlanner.Danger(active, proposal.ImpactNote)) != 0)
                        invalidates = true;
                bool noDodge = p.Kind == PatternKind.TopPressure && !CanDodge(player, proposal, lastClosed + 1);
                if (hold || invalidates || noDodge || CanPublish != null && !CanPublish(proposal))
                { nextImpact[i]++; Changed?.Invoke("defer", proposal); continue; }
                active.Add(proposal); nextId++; Changed?.Invoke("publish", proposal);
                nextImpact[i] += p.RepeatNotes;
            }
        }
        private static bool CanDodge(GridPosition player, BossTelegraph attack, int nextNote)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if ((attack.Cells & (1UL << (y * 8 + x))) == 0 && Math.Abs(x - player.X) + Math.Abs(y - player.Y) <= attack.ImpactNote - nextNote + 1)
                    return true;
            return false;
        }
        public static ulong Cells(PatternDefinition p, GridPosition player)
        {
            if (p.Kind == PatternKind.LockedCell) return SafeRoutePlanner.Bit(player);
            ulong result = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if (p.Kind == PatternKind.Row && y == p.Coordinate || p.Kind == PatternKind.Column && x == p.Coordinate ||
                    p.Kind == PatternKind.TopPressure && y == 7 && x >= p.FirstColumn && x < p.FirstColumn + p.ColumnCount)
                    result |= 1UL << (y * 8 + x);
            return result;
        }
        public ulong Resolve(int note)
        {
            fired.Clear(); ulong cells = 0;
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].ImpactNote == note)
                { var a = active[i]; fired.Add(a); cells |= a.Cells; active.RemoveAt(i); Changed?.Invoke("fire", a); }
            return cells;
        }
    }
}
