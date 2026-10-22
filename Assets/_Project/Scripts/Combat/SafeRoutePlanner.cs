using System;
using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Player;

namespace StringAsASword.Combat
{
    public readonly struct RouteResult
    {
        public readonly bool Found, SearchLimit;
        public readonly int Cost;
        public readonly PlayerActionKind FirstAction;
        public readonly MoveDirection FirstDirection;
        public RouteResult(bool found, int cost, PlayerActionKind action, MoveDirection direction, bool limit = false)
        { Found = found; Cost = cost; FirstAction = action; FirstDirection = direction; SearchLimit = limit; }
    }
    public static class SafeRoutePlanner
    {
        private struct Node { public int Cell, Mask, Cost; public PlayerActionKind First; public MoveDirection Direction; }
        public static ulong Bit(GridPosition p) => 1UL << (p.Y * 8 + p.X);
        public static ulong Danger(IReadOnlyList<BossTelegraph> attacks, int note)
        { ulong mask = 0; foreach (var a in attacks) if (a.ImpactNote == note) mask |= a.Cells; return mask; }
        public static RouteResult Find(GridPosition start, GridPosition target, int nextNote,
            IReadOnlyList<BossTelegraph> attacks, int maxSteps = 32, bool returnToTop = false)
            => FindAll(start, new[] { target }, nextNote, attacks, maxSteps, returnToTop);

        // Time-expanded BFS counts a dodge that lands on a pickup only once. Guard can wait safely.
        public static RouteResult FindAll(GridPosition start, IReadOnlyList<GridPosition> targets, int nextNote,
            IReadOnlyList<BossTelegraph> attacks, int maxSteps, bool returnToTop = true)
        {
            if (targets.Count > 3 || maxSteps < 0) return default;
            maxSteps = Math.Min(maxSteps, 96);
            bool danger = false;
            foreach (var a in attacks) if (a.ImpactNote >= nextNote && a.ImpactNote < nextNote + maxSteps) { danger = true; break; }
            if (!danger) return OpenRoute(start, targets, maxSteps, returnToTop);
            int masks = 1 << targets.Count, goal = masks - 1;
            var visited = new bool[(maxSteps + 1) * 64 * masks];
            var queue = new Queue<Node>();
            queue.Enqueue(new Node { Cell = start.Y * 8 + start.X });
            int expanded = 0;
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (++expanded > 100000) return new RouteResult(false, 0, 0, 0, true);
                if (n.Mask == goal && (!returnToTop || n.Cell / 8 == 7))
                    return new RouteResult(true, n.Cost, n.First, n.Direction);
                if (n.Cost == maxSteps) continue;
                ulong dangerMask = Danger(attacks, nextNote + n.Cost);
                for (int action = 0; action < 5; action++)
                {
                    int x = n.Cell % 8, y = n.Cell / 8;
                    if (action == 0) y++; else if (action == 1) x--; else if (action == 2) y--; else if (action == 3) x++;
                    if (x < 0 || y < 0 || x > 7 || y > 7) continue;
                    int cell = y * 8 + x;
                    if (action < 4 && (dangerMask & (1UL << cell)) != 0) continue;
                    int mask = n.Mask;
                    if (action < 4) for (int i = 0; i < targets.Count; i++)
                        if (targets[i].X == x && targets[i].Y == y) mask |= 1 << i;
                    int key = ((n.Cost + 1) * 64 + cell) * masks + mask;
                    if (visited[key]) continue;
                    visited[key] = true;
                    queue.Enqueue(new Node { Cell = cell, Mask = mask, Cost = n.Cost + 1,
                        First = n.Cost == 0 ? (action < 4 ? PlayerActionKind.Move : PlayerActionKind.Guard) : n.First,
                        Direction = n.Cost == 0 ? (MoveDirection)Math.Min(action, 3) : n.Direction });
                }
            }
            return default;
        }
        private static RouteResult OpenRoute(GridPosition start, IReadOnlyList<GridPosition> targets, int maxSteps, bool returnToTop)
        {
            int best = int.MaxValue;
            MoveDirection first = MoveDirection.Up;
            void Visit(GridPosition cell, int mask, int cost, MoveDirection direction)
            {
                if (cost > maxSteps || cost >= best) return;
                if (mask == (1 << targets.Count) - 1)
                {
                    int total = cost + (returnToTop ? 7 - cell.Y : 0);
                    if (total <= maxSteps && total < best) { best = total; first = direction; }
                    return;
                }
                for (int i = 0; i < targets.Count; i++)
                {
                    if ((mask & (1 << i)) != 0) continue;
                    var t = targets[i];
                    int distance = Math.Abs(t.X - cell.X) + Math.Abs(t.Y - cell.Y);
                    MoveDirection move = t.Y > cell.Y ? MoveDirection.Up : t.X < cell.X ? MoveDirection.Left : t.Y < cell.Y ? MoveDirection.Down : MoveDirection.Right;
                    // Collection requires landing, including when a later observation starts on that cell.
                    if (distance == 0) { distance = 2; move = cell.Y < 7 ? MoveDirection.Up : MoveDirection.Down; }
                    int collected = mask;
                    for (int j = 0; j < targets.Count; j++) if (targets[j].Equals(t)) collected |= 1 << j;
                    Visit(t, collected, cost + distance, cost == 0 ? move : direction);
                }
            }
            Visit(start, 0, 0, MoveDirection.Up);
            return best == int.MaxValue ? default : new RouteResult(true, best, best > 0 ? PlayerActionKind.Move : PlayerActionKind.Guard, first);
        }
    }
}
