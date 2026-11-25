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
        public int Hit { get; }
        /// <summary>Chess pieces summoned for this attack (boss chart `piece=`). Presentation only.</summary>
        public IReadOnlyList<ChessPiece> Pieces { get; set; } = new ChessPiece[0];
        public BossTelegraph(int id, string name, ulong cells, int impactNote, PatternKind kind = PatternKind.Row,
            int warningNotes = 0, int requiredDodgeActions = 0, int selectedInterval = 0, int actualInterval = 0, string reason = "",
            AttackResponse response = AttackResponse.Either, int hit = 0)
        {
            Id = id; Name = name; Cells = cells; ImpactNote = impactNote; Kind = kind;
            WarningNotes = warningNotes; RequiredDodgeActions = requiredDodgeActions;
            SelectedInterval = selectedInterval; ActualInterval = actualInterval; Reason = reason; Response = response; Hit = hit;
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
        private PatternDefinition pending, previous;
        private int targetImpact, warning, selectedInterval, deferrals, lastImpact = -1;
        // Read every frame by the presenters: one live read-only view per list, not a new wrapper per read.
        private IReadOnlyList<BossTelegraph> activeView, firedView;
        public IReadOnlyList<BossTelegraph> Active => activeView ??= active.AsReadOnly();
        public IReadOnlyList<BossTelegraph> LastFired => firedView ??= fired.AsReadOnly();
        public ulong LastFiredUnblockable { get; private set; }
        public Func<IReadOnlyList<BossTelegraph>, bool> CanPublish;
        public event Action<string, BossTelegraph> Changed;
        // fastNote[n]: the gap from note n-1 to note n is shorter than one beat. Null for evenly spaced charts.
        private readonly bool[] fastNote;
        // Chart mode: attacks come from the hand-written boss chart, in order, at their notes. The validator vouches
        // for fairness ahead of time, so nothing here defers or rerolls; phases only filter, they never cancel.
        private readonly BossChart chart;
        private int chartCursor;
        // `random N` hands over to the random scheduler once the chart is spent; the two never overlap, so neither
        // mode's fairness checks have to account for the other's telegraphs.
        private bool handedOver;
        private bool Authored => chart != null && !handedOver;
        public BossPatternSystem(PatternDefinition[] definitions, RandomAttackSchedule schedule = null, int seed = 0, bool[] fastNotes = null,
            BossChart chart = null)
        {
            this.definitions = definitions; this.schedule = schedule ?? new RandomAttackSchedule();
            this.schedule.Validate(); random = new SeededRandom(unchecked(seed ^ 0x36d9f1ab));
            fastNote = fastNotes == null ? null : (bool[])fastNotes.Clone();
            this.chart = chart;
        }
        /// <summary>Fast sections belong to the rhythm: no attack whose warning or hits span a sub-beat note gap.</summary>
        public bool CrossesFastSection(int lastClosed, int lastHit)
        {
            if (fastNote == null) return false;
            for (int n = Math.Max(1, lastClosed + 1); n <= lastHit && n < fastNote.Length; n++)
                if (fastNote[n]) return true;
            return false;
        }
        public void BeginPhase(int phaseId, int firstNote)
        {
            if (Authored) { phase = phaseId; return; }
            foreach (var a in active) Changed?.Invoke("cancel", a);
            active.Clear(); phase = phaseId; nextImpact = new int[definitions.Length];
            if (schedule.Enabled) { Plan(lastImpact < 0 ? firstNote - 1 : lastImpact, firstNote - 1); return; }
            for (int i = 0; i < definitions.Length; i++) nextImpact[i] = firstNote + definitions[i].FirstImpact - 1;
        }
        private bool halted;
        /// <summary>The king is stunned: cancel what is shown and publish nothing more.</summary>
        public void Halt()
        {
            foreach (var a in active) Changed?.Invoke("cancel", a);
            active.Clear(); pending = null; halted = true;
        }
        public void Publish(int lastClosed, GridPosition player, int noteCount, bool hold)
        {
            if (halted) return;
            if (Authored)
            {
                PublishAuthored(lastClosed, player, noteCount);
                if (chart.RandomFrom < 0 || lastClosed + 1 < chart.RandomFrom || chartCursor < chart.Attacks.Count || active.Count > 0) return;
                handedOver = true; BeginPhase(phase, lastClosed + 1);
            }
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
                if (hold || invalidates || noDodge || CrossesFastSection(lastClosed, proposal.ImpactNote) ||
                    CanPublish != null && !CanPublish(new[] { proposal }))
                { nextImpact[i]++; Changed?.Invoke("defer", proposal); continue; }
                active.Add(proposal); nextId++; Changed?.Invoke("publish", proposal);
                nextImpact[i] += p.RepeatNotes;
            }
        }
        private void PublishAuthored(int lastClosed, GridPosition player, int noteCount)
        {
            var attacks = chart.Attacks;
            for (; chartCursor < attacks.Count && attacks[chartCursor].Publish <= lastClosed; chartCursor++)
            {
                var a = attacks[chartCursor];
                var (normal, strong) = BossChart.Cells(a.Shape, player);
                var hits = AuthoredHits(a, player, a.Note <= lastClosed ? "late" : a.LastHit >= noteCount ? "chart-end" :
                    phase < a.MinPhase ? "phase" : (normal | strong) == 0 ? "off-board" : "");
                if (hits[0].Reason.Length != 0) { Changed?.Invoke("skip", hits[0]); continue; }
                active.AddRange(hits); nextId += hits.Count;
                foreach (var hit in hits) Changed?.Invoke("publish", hit);
            }
        }
        /// <summary>
        /// Shows one chart shape now, anchored on the player, to fall on <paramref name="impactNote"/>: the tutorial's lessons,
        /// which wait for the player instead of following the chart. Returns what was shown (empty when nothing lands).
        /// </summary>
        public IReadOnlyList<BossTelegraph> Inject(BossChartShape shape, int impactNote, int warning, GridPosition player, int lastClosed, int noteCount)
        {
            if (halted || impactNote <= lastClosed || impactNote >= noteCount) return new BossTelegraph[0];
            var hits = AuthoredHits(new BossChartAttack(impactNote, shape, warning, 1, 1, 1, 0), player);
            int ids = hits.Count;
            hits.RemoveAll(h => h.Cells == 0);
            nextId += ids;
            active.AddRange(hits);
            foreach (var hit in hits) Changed?.Invoke("publish", hit);
            return hits;
        }
        // Per hit: 1-cells as an Either telegraph and 2-cells as a DodgeOnly one at the same note (a full board of 1 is GuardOnly).
        // Danger, Unblockable and LastFiredUnblockable already merge telegraphs that share a note.
        public List<BossTelegraph> AuthoredHits(BossChartAttack a, GridPosition player, string reason = "")
        {
            var (normal, strong) = BossChart.Cells(a.Shape, player);
            var hits = new List<BossTelegraph>();
            for (int hit = 0; hit < a.Hits; hit++)
            {
                int offset = hit * a.Every, impact = a.Note + offset;
                int interval = hit > 0 ? a.Every : lastImpact < 0 ? 0 : a.Note - lastImpact;
                void Add(ulong cells, AttackResponse response) => hits.Add(new BossTelegraph(nextId + 1 + hits.Count, a.Shape.Name, cells, impact,
                    PatternKind.Drawn, a.Warning + offset, 0, hit == 0 ? 0 : a.Every, interval, reason, response, hit) { Pieces = a.Shape.Pieces });
                if (a.Shape.GuardOnly) { Add(normal, AttackResponse.GuardOnly); continue; }
                if (normal != 0) Add(normal, AttackResponse.Either);
                if (strong != 0) Add(strong, AttackResponse.DodgeOnly);
            }
            // An anchored shape can slide fully off the board; keep one empty telegraph so a skip can still be reported.
            if (hits.Count == 0) hits.Add(new BossTelegraph(nextId + 1, a.Shape.Name, 0, a.Note, PatternKind.Drawn, a.Warning, reason: reason));
            return hits;
        }
        private void Plan(int anchor, int lastClosed)
        {
            pending = null; deferrals = 0;
            int total = 0, others = 0;
            foreach (var p in definitions)
                if (Eligible(p)) { total += p.SelectionWeight; if (p != previous) others += p.SelectionWeight; }
            if (total == 0) return;
            // The previous pattern only repeats when nothing else is eligible.
            var excluded = others > 0 ? previous : null;
            int pick = random.Next(others > 0 ? others : total);
            foreach (var p in definitions)
                if (Eligible(p) && p != excluded) { pick -= p.SelectionWeight; if (pick < 0) { pending = p; break; } }
            previous = pending;
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
            => Telegraph(p, player, 0, targetImpact, warning, required, reason);

        // Later hits count their gap from the hit before, so a countdown and an interval read the same on every hit.
        private BossTelegraph Telegraph(PatternDefinition p, GridPosition player, int hit, int first, int warn, int required, string reason)
        {
            int offset = hit * p.HitSpacing;
            return new BossTelegraph(nextId + 1 + hit, p.Name, Cells(p, player, hit), first + offset, p.Kind, warn + offset, required,
                hit == 0 ? selectedInterval : p.HitSpacing, hit > 0 ? p.HitSpacing : lastImpact < 0 ? 0 : first - lastImpact, reason, p.Response, hit);
        }
        private BossTelegraph[] Sequence(PatternDefinition p, GridPosition player, int first, int warn, int required = -1, string reason = "")
        {
            var hits = new BossTelegraph[p.Hits];
            for (int i = 0; i < hits.Length; i++) hits[i] = Telegraph(p, player, i, first, warn, required, reason);
            return hits;
        }
        // Earlier hits are disclosed to the check on the last, so the route has to survive every hit.
        private int SequenceActions(PatternDefinition p, GridPosition player, BossTelegraph[] hits, int lastClosed)
        {
            if (p.Response == AttackResponse.GuardOnly) return 0;
            var disclosed = new List<BossTelegraph>(active);
            for (int i = 0; i < hits.Length - 1; i++) disclosed.Add(hits[i]);
            return DodgeActions(player, hits[hits.Length - 1], lastClosed, disclosed);
        }

        private void PublishRandom(int lastClosed, GridPosition player, int noteCount, bool hold)
        {
            if (pending != null && !Eligible(pending)) Plan(lastImpact < 0 ? lastClosed : lastImpact, lastClosed);
            if (pending == null) return;
            if (targetImpact + pending.Span >= noteCount) { SkipAtChartEnd(lastClosed, player, noteCount); return; }
            if (lastClosed < targetImpact - warning) return;
            // Late callers still receive the full warning; live sessions visit every note in order.
            targetImpact = Math.Max(targetImpact, lastClosed + warning);
            if (targetImpact + pending.Span >= noteCount) { SkipAtChartEnd(lastClosed, player, noteCount); return; }
            string reason = "noise-wait";
            // Deterministic for every candidate, so check once instead of spending attempts and randomness on it.
            if (!hold && CrossesFastSection(lastClosed, targetImpact + pending.Span)) { hold = true; reason = "fast-section"; }
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
                    int warn = warning, first = targetImpact;
                    var hits = Sequence(p, player, first, warn);
                    int required = SequenceActions(p, player, hits, lastClosed);
                    while (required < 0 && warn < schedule.MaxWarning)
                    {
                        warn++; first = lastClosed + warn;
                        hits = Sequence(p, player, first, warn);
                        required = SequenceActions(p, player, hits, lastClosed);
                    }
                    reason = first + p.Span >= noteCount ? "chart-end" : CrossesFastSection(lastClosed, first + p.Span) ? "fast-section" :
                        required < 0 ? "no-dodge-within-warning" : Invalidates(hits) ? "disclosed-route" :
                        CanPublish != null && !CanPublish(hits) ? "pickup-route" : "";
                    hits = Sequence(p, player, first, warn, required, reason);
                    if (reason.Length != 0) { Changed?.Invoke("reject", hits[0]); continue; }
                    active.AddRange(hits); nextId += hits.Length; pending = null;
                    foreach (var hit in hits) Changed?.Invoke("publish", hit);
                    return;
                }
            }
            if (deferrals >= schedule.MaxDeferrals)
            { Changed?.Invoke("skip", Proposal(pending, player, reason: "retry-limit:" + reason)); Plan(lastClosed, lastClosed); }
            else { Changed?.Invoke("defer", Proposal(pending, player, reason: reason)); deferrals++; targetImpact++; }
        }
        // A sequence too long for the chart must not end the attacks while a shorter pattern still fits.
        private void SkipAtChartEnd(int lastClosed, GridPosition player, int noteCount)
        {
            Changed?.Invoke("skip", Proposal(pending, player, reason: "chart-end"));
            bool shorter = false;
            foreach (var p in definitions) if (Eligible(p) && p.Span < pending.Span) shorter = true;
            bool retry = shorter && targetImpact < noteCount;
            pending = null;
            if (retry) Plan(lastClosed, lastClosed);
        }
        private bool Invalidates(BossTelegraph[] proposal) => Invalidates(active, proposal);
        /// <summary>True when a proposed hit would fire on a cell already shown as safe at an earlier-or-equal deadline.</summary>
        public static bool Invalidates(IReadOnlyList<BossTelegraph> active, IReadOnlyList<BossTelegraph> proposal)
        {
            foreach (var hit in proposal)
                foreach (var a in active)
                    if (hit.ImpactNote <= a.ImpactNote && (hit.Cells & ~SafeRoutePlanner.Danger(active, hit.ImpactNote)) != 0)
                        return true;
            return false;
        }

        // Bounded by the warning ceiling plus the longest sequence, over 64 cells. Waiting/guarding costs a note,
        // except that a disclosed DodgeOnly attack removes its cells from waiting too; the final cell must dodge.
        // For a sequence, pass the last hit as the proposal and the earlier hits as disclosed.
        public static int DodgeActions(GridPosition player, BossTelegraph proposal, int lastClosed, IReadOnlyList<BossTelegraph> disclosed)
        {
            int length = proposal.ImpactNote - lastClosed;
            if (length < 1 || length > RandomAttackSchedule.WarningCeiling + (PatternDefinition.MaxHits - 1) * PatternDefinition.MaxHitSpacing) return -1;
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
        public static ulong Cells(PatternDefinition p, GridPosition player, int hit = 0)
        {
            if (p.Kind == PatternKind.LockedCell) return SafeRoutePlanner.Bit(player);
            if (p.Kind == PatternKind.FullBoard) return ulong.MaxValue;
            ulong result = 0;
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if (p.Kind == PatternKind.Row && y == p.Coordinate || p.Kind == PatternKind.Column && x == p.Coordinate ||
                    p.Kind == PatternKind.TopPressure && y == 7 && x >= p.FirstColumn && x < p.FirstColumn + p.ColumnCount ||
                    p.Kind == PatternKind.Cross && (x == player.X || y == player.Y) || p.Kind == PatternKind.UpperHalf && y >= 4 ||
                    p.Kind == PatternKind.Checker && (x + y + hit) % 2 == 0 ||
                    p.Kind == PatternKind.Ring && Math.Min(Math.Min(x, y), Math.Min(7 - x, 7 - y)) == hit % 4)
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
            // Only one sequence is ever active, so an empty list means its last hit fired. A cancelled
            // sequence empties the list without firing and BeginPhase plans the replacement.
            if (!Authored && schedule.Enabled && fired.Count > 0 && active.Count == 0) Plan(note, note);
            return cells;
        }
    }
}
