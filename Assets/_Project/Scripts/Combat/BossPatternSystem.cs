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
        public PatternKind Kind { get; }
        public int WarningNotes { get; }
        public int RequiredDodgeActions { get; }
        public int SelectedInterval { get; }
        public int ActualInterval { get; }
        public string Reason { get; }
        public AttackResponse Response { get; }
        public BossTelegraph(int id, string name, ulong cells, int impactNote, PatternKind kind = PatternKind.Row,
            int warningNotes = 0, int requiredDodgeActions = 0, int selectedInterval = 0, int actualInterval = 0, string reason = "",
            AttackResponse response = AttackResponse.Either)
        {
            Id = id; Name = name; Cells = cells; ImpactNote = impactNote; Kind = kind;
            WarningNotes = warningNotes; RequiredDodgeActions = requiredDodgeActions;
            SelectedInterval = selectedInterval; ActualInterval = actualInterval; Reason = reason; Response = response;
        }
        public int Remaining(int lastClosed) => ImpactNote - lastClosed;
    }
    public sealed class BossPatternSystem
    {
        private readonly PatternDefinition[] definitions;
        private readonly RandomAttackSchedule schedule;
        private readonly SeededRandom random;
        private readonly List<BossTelegraph> active = new List<BossTelegraph>(), fired = new List<BossTelegraph>();
        private int[] nextImpact;
        private int phase, nextId;
        private PatternDefinition pending;
        private int targetImpact, warning, selectedInterval, deferrals, lastImpact = -1;
        public IReadOnlyList<BossTelegraph> Active => active.AsReadOnly();
        public IReadOnlyList<BossTelegraph> LastFired => fired.AsReadOnly();
        public ulong LastFiredUnblockable { get; private set; }
        public Func<BossTelegraph, bool> CanPublish;
        public event Action<string, BossTelegraph> Changed;
        public BossPatternSystem(PatternDefinition[] definitions, RandomAttackSchedule schedule = null, int seed = 0)
        {
            this.definitions = definitions; this.schedule = schedule ?? new RandomAttackSchedule();
            this.schedule.Validate(); random = new SeededRandom(unchecked(seed ^ 0x36d9f1ab));
        }
        public void BeginPhase(int phaseId, int firstNote)
        {
            foreach (var a in active) Changed?.Invoke("cancel", a);
            active.Clear(); phase = phaseId; nextImpact = new int[definitions.Length];
            if (schedule.Enabled) { Plan(lastImpact < 0 ? firstNote - 1 : lastImpact, firstNote - 1); return; }
            for (int i = 0; i < definitions.Length; i++) nextImpact[i] = firstNote + definitions[i].FirstImpact - 1;
        }
        public void Publish(int lastClosed, GridPosition player, int noteCount, bool hold)
        {
            if (schedule.Enabled) { PublishRandom(lastClosed, player, noteCount, hold); return; }
            for (int i = 0; i < definitions.Length; i++)
            {
                var p = definitions[i];
                if (!p.Enabled || (p.PhaseMask & (1 << (phase - 1))) == 0 || nextImpact[i] >= noteCount ||
                    lastClosed < nextImpact[i] - p.TelegraphNotes) continue;
                var proposal = new BossTelegraph(nextId + 1, p.Name, Cells(p, player), nextImpact[i], p.Kind,
                    p.TelegraphNotes, 0, p.RepeatNotes, lastImpact < 0 ? 0 : nextImpact[i] - lastImpact, "", p.Response);
                // Preserve every already disclosed safe cell at an existing deadline, not just one lucky route.
                bool invalidates = false;
                foreach (var a in active)
                    if (proposal.ImpactNote <= a.ImpactNote && (proposal.Cells & ~SafeRoutePlanner.Danger(active, proposal.ImpactNote)) != 0)
                        invalidates = true;
                bool noDodge = (p.Kind == PatternKind.TopPressure || p.Response == AttackResponse.DodgeOnly) && p.Response != AttackResponse.GuardOnly &&
                    !CanDodge(player, proposal, lastClosed + 1);
                if (hold || invalidates || noDodge || CanPublish != null && !CanPublish(proposal))
                { nextImpact[i]++; Changed?.Invoke("defer", proposal); continue; }
                active.Add(proposal); nextId++; Changed?.Invoke("publish", proposal);
                nextImpact[i] += p.RepeatNotes;
            }
        }
        private void Plan(int anchor, int lastClosed)
        {
            pending = null; deferrals = 0;
            int total = 0;
            foreach (var p in definitions) if (Eligible(p)) total += p.SelectionWeight;
            if (total == 0) return;
            int pick = random.Next(total);
            foreach (var p in definitions)
                if (Eligible(p)) { pick -= p.SelectionWeight; if (pick < 0) { pending = p; break; } }
            total = 0; foreach (int weight in schedule.IntervalWeights) total += weight;
            pick = random.Next(total); selectedInterval = schedule.MinInterval;
            foreach (int weight in schedule.IntervalWeights)
            { pick -= weight; if (pick < 0) break; selectedInterval++; }
            warning = Math.Max(schedule.MinWarning, Math.Min(schedule.MaxWarning, pending.TelegraphNotes));
            targetImpact = Math.Max(anchor + selectedInterval, lastClosed + warning);
        }
        private bool Eligible(PatternDefinition p)
            => p.Enabled && p.SelectionWeight > 0 && (p.PhaseMask & (1 << (phase - 1))) != 0;

        private BossTelegraph Proposal(PatternDefinition p, GridPosition player, int required = -1, string reason = "")
            => new BossTelegraph(nextId + 1, p.Name, Cells(p, player), targetImpact, p.Kind,
                warning, required, selectedInterval, lastImpact < 0 ? 0 : targetImpact - lastImpact, reason, p.Response);

        private void PublishRandom(int lastClosed, GridPosition player, int noteCount, bool hold)
        {
            if (pending != null && !Eligible(pending)) Plan(lastImpact < 0 ? lastClosed : lastImpact, lastClosed);
            if (pending == null) return;
            if (targetImpact >= noteCount)
            { Changed?.Invoke("skip", Proposal(pending, player, reason: "chart-end")); pending = null; return; }
            if (lastClosed < targetImpact - warning) return;
            // Late callers still receive the full warning; live sessions visit every note in order.
            targetImpact = Math.Max(targetImpact, lastClosed + warning);
            if (targetImpact >= noteCount)
            { Changed?.Invoke("skip", Proposal(pending, player, reason: "chart-end")); pending = null; return; }
            string reason = "noise-wait";
            if (!hold)
            {
                for (int attempt = 0; attempt < schedule.CandidateAttempts; attempt++)
                {
                    var p = pending.Copy();
                    if (p.Kind == PatternKind.Row || p.Kind == PatternKind.Column)
                    {
                        p.Kind = random.Next(2) == 0 ? PatternKind.Row : PatternKind.Column; p.Coordinate = random.Next(8);
                        p.Name = p.Kind == PatternKind.Row ? "ROW " + (p.Coordinate + 1) : "COLUMN " + (char)('A' + p.Coordinate);
                    }
                    else if (p.Kind == PatternKind.TopPressure) p.FirstColumn = random.Next(9 - p.ColumnCount);
                    var candidate = Proposal(p, player);
                    int required = p.Response == AttackResponse.GuardOnly ? 0 : DodgeActions(player, candidate, lastClosed, active);
                    while (required < 0 && candidate.WarningNotes < schedule.MaxWarning)
                    {
                        int longer = candidate.WarningNotes + 1, impact = lastClosed + longer;
                        candidate = new BossTelegraph(nextId + 1, p.Name, candidate.Cells, impact, p.Kind,
                            longer, -1, selectedInterval, lastImpact < 0 ? 0 : impact - lastImpact, "", p.Response);
                        required = DodgeActions(player, candidate, lastClosed, active);
                    }
                    reason = candidate.ImpactNote >= noteCount ? "chart-end" : required < 0 ? "no-dodge-within-warning" : Invalidates(candidate) ? "disclosed-route" :
                        CanPublish != null && !CanPublish(candidate) ? "pickup-route" : "";
                    candidate = new BossTelegraph(candidate.Id, candidate.Name, candidate.Cells, candidate.ImpactNote, candidate.Kind,
                        candidate.WarningNotes, required, selectedInterval, candidate.ActualInterval, reason, p.Response);
                    if (reason.Length != 0) { Changed?.Invoke("reject", candidate); continue; }
                    active.Add(candidate); nextId++; pending = null; Changed?.Invoke("publish", candidate); return;
                }
            }
            if (deferrals >= schedule.MaxDeferrals)
            { Changed?.Invoke("skip", Proposal(pending, player, reason: "retry-limit:" + reason)); Plan(lastClosed, lastClosed); }
            else { Changed?.Invoke("defer", Proposal(pending, player, reason: reason)); deferrals++; targetImpact++; }
        }
        private bool Invalidates(BossTelegraph proposal)
        {
            foreach (var a in active)
                if (proposal.ImpactNote <= a.ImpactNote && (proposal.Cells & ~SafeRoutePlanner.Danger(active, proposal.ImpactNote)) != 0)
                    return true;
            return false;
        }

        // At most three time layers and 64 cells. Waiting/guarding costs a note, except that a disclosed
        // DodgeOnly attack removes its cells from waiting too; the final cell must dodge.
        public static int DodgeActions(GridPosition player, BossTelegraph proposal, int lastClosed, IReadOnlyList<BossTelegraph> disclosed)
        {
            int length = proposal.ImpactNote - lastClosed;
            if (length < 1 || length > 3) return -1;
            ulong reachable = SafeRoutePlanner.Bit(player);
            for (int step = 0; step <= length; step++)
            {
                ulong futureDanger = proposal.Cells;
                foreach (var a in disclosed) if (a.ImpactNote > lastClosed + step && a.ImpactNote <= proposal.ImpactNote) futureDanger |= a.Cells;
                if ((reachable & ~futureDanger) != 0) return step;
                if (step == length) break;
                int note = lastClosed + step + 1;
                ulong danger = SafeRoutePlanner.Danger(disclosed, note), next = reachable & ~SafeRoutePlanner.Unblockable(disclosed, note);
                for (int cell = 0; cell < 64; cell++)
                {
                    if ((reachable & (1UL << cell)) == 0) continue;
                    int x = cell % 8, y = cell / 8;
                    if (y < 7) next |= (1UL << (cell + 8)) & ~danger;
                    if (x > 0) next |= (1UL << (cell - 1)) & ~danger;
                    if (y > 0) next |= (1UL << (cell - 8)) & ~danger;
                    if (x < 7) next |= (1UL << (cell + 1)) & ~danger;
                }
                reachable = note == proposal.ImpactNote ? next & ~(danger | proposal.Cells) : next;
            }
            return -1;
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
            fired.Clear(); ulong cells = 0; LastFiredUnblockable = 0;
            for (int i = active.Count - 1; i >= 0; i--)
                if (active[i].ImpactNote == note)
                {
                    var a = active[i]; fired.Add(a); cells |= a.Cells; active.RemoveAt(i); lastImpact = note;
                    if (a.Response == AttackResponse.DodgeOnly) LastFiredUnblockable |= a.Cells;
                    Changed?.Invoke("fire", a);
                }
            if (schedule.Enabled && fired.Count > 0) Plan(note, note);
            return cells;
        }
    }
}
