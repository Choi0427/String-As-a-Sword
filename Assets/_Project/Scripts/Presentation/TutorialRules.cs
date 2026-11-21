using System.Collections.Generic;
using StringAsASword.Board;
using StringAsASword.Combat;

namespace StringAsASword.Presentation
{
    /// <summary>One prompt: a key (W, J, K, WASD …) and a line of text; either may be empty.</summary>
    public readonly struct TutorialHint
    {
        public string Key { get; }
        public string Text { get; }
        public TutorialHint(string key, string text) { Key = key; Text = text; }
        public override string ToString() => Key + " " + Text;
    }

    /// <summary>What the tutorial looks at, read from the battle each frame.</summary>
    public struct TutorialState
    {
        /// <summary>The note the player plays next (last closed + 1).</summary>
        public int Note;
        public GridPosition Knight;
        public IReadOnlyList<GridPosition> Noise;
        public bool Charged;
        /// <summary>Resonance notes left including <see cref="Note"/>; 0 = not resonating.</summary>
        public int ResonanceLeft;
        public int ResonanceLength;
        public IReadOnlyList<BossTelegraph> Telegraphs;
        /// <summary>The knight has moved at least once this run.</summary>
        public bool Moved;
    }

    /// <summary>What the tutorial has already taught this run.</summary>
    public sealed class TutorialProgress
    {
        // Attack sequences (telegraph Id - Hit) a hint was shown for, by response, in the order they came.
        internal readonly List<int> Guards = new List<int>(), Dodges = new List<int>(), Normals = new List<int>();
        internal int Ignitions;
        internal bool Resonating;
        public int GuardsTaught => Guards.Count;
        public int DodgesTaught => Dodges.Count;
        public int NormalsTaught => Normals.Count;
        public void Reset() { Guards.Clear(); Dodges.Clear(); Normals.Clear(); Ignitions = 0; Resonating = false; }
    }

    /// <summary>
    /// The tutorial prompts of 간주1 (intro review D2-B), decided from the board as it is rather than a timetable, so they stay
    /// right when the player strays. The boss chart's 간주1 teaches in order: a normal attack (guard or step away), a slash
    /// (dodge), the first noise, the climb to the top row and the 8-note resonance; a full-board attack closes it. Guard,
    /// dodge and normal-attack prompts are given for the first two attacks of each kind only (the second guard and slash
    /// come in 벌스1); everything else is taught in 간주1 only.
    /// </summary>
    public static class TutorialRules
    {
        /// <summary>Last note of 간주1 (the lesson section) and of 벌스1 (where the second guard and slash are).</summary>
        public const int LessonEnd = 31, HintEnd = 61;
        public const int Repeats = 2;
        public const string Guard = "K", Up = "W", Down = "S", Left = "A", Right = "D", Strike = "J", Move = "WASD";

        public static TutorialHint? Next(TutorialState s, TutorialProgress p)
        {
            if (s.Note > HintEnd) return null;
            Track(s, p);
            var telegraphs = s.Telegraphs ?? new BossTelegraph[0];
            int knightBit = s.Knight.Y * 8 + s.Knight.X;
            bool Hits(BossTelegraph t) => (t.Cells & (1UL << knightBit)) != 0;
            // 1-3: the boss's attacks, first two of each kind.
            foreach (var t in telegraphs)
                if (t.Response == AttackResponse.GuardOnly && Teach(p.Guards, t))
                    return new TutorialHint(Guard, "판 전체 · 막는다");
            foreach (var t in telegraphs)
                if (t.Response == AttackResponse.DodgeOnly && Hits(t) && Teach(p.Dodges, t))
                    return new TutorialHint(SideStep(s.Knight, t.Cells), "빗금은 막을 수 없다 · 옆으로");
            foreach (var t in telegraphs)
                if (t.Response == AttackResponse.Either && Hits(t) && Teach(p.Normals, t))
                    return p.Normals.IndexOf(Sequence(t)) == 0
                        ? new TutorialHint(Guard, "막는다 · 비켜도 된다")
                        : new TutorialHint(UpOrDown(s.Knight, t.Cells), "비킨다 · 막아도 된다");
            if (s.Note > LessonEnd) return null;
            // 4-5: the string sounding, and how to set it off. From the second time on, the key alone.
            if (s.ResonanceLeft > 0)
                return new TutorialHint(Strike, p.Ignitions <= 1 ? $"공명 · {s.ResonanceLength}번 동안 강화 공격 ({s.ResonanceLeft})" : "");
            bool top = s.Knight.Y == 7;
            if (s.Charged && top) return new TutorialHint(Strike, p.Ignitions == 0 ? "맨 윗줄에서 벤다" : "");
            if (s.Charged) return new TutorialHint(Up, "현이 울린다 · 맨 윗줄로");
            // 6-7: the noise, and moving at all.
            if (s.Noise != null && s.Noise.Count > 0) return new TutorialHint(Toward(s.Knight, Nearest(s.Knight, s.Noise)), "잡음 위로");
            if (!s.Moved && telegraphs.Count == 0) return new TutorialHint(Move, "노트가 판정선에 닿을 때 한 칸");
            return null;
        }
        // The ignition count, so the second resonance is shown with the key only.
        private static void Track(TutorialState s, TutorialProgress p)
        {
            bool resonating = s.ResonanceLeft > 0;
            if (resonating && !p.Resonating) p.Ignitions++;
            p.Resonating = resonating;
        }
        private static int Sequence(BossTelegraph t) => t.Id - t.Hit;
        private static bool Teach(List<int> taught, BossTelegraph t)
        {
            int sequence = Sequence(t);
            if (taught.Contains(sequence)) return true;
            if (taught.Count >= Repeats) return false;
            taught.Add(sequence); return true;
        }
        private static bool Open(int x, int y, ulong cells) => x >= 0 && x < 8 && y >= 0 && y < 8 && (cells & (1UL << (y * 8 + x))) == 0;
        /// <summary>Sideways out of a slash: the free side, towards the middle when both are; up or down if neither is.</summary>
        public static string SideStep(GridPosition k, ulong cells)
        {
            bool left = Open(k.X - 1, k.Y, cells), right = Open(k.X + 1, k.Y, cells);
            if (left && right) return k.X < 4 ? Right : Left;
            if (left) return Left;
            if (right) return Right;
            return UpOrDown(k, cells);
        }
        /// <summary>Out of a row attack: up when free (towards the top row), else down.</summary>
        public static string UpOrDown(GridPosition k, ulong cells) => Open(k.X, k.Y + 1, cells) || !Open(k.X, k.Y - 1, cells) ? Up : Down;
        /// <summary>The first step towards a cell: along the longer distance, up/down on a tie.</summary>
        public static string Toward(GridPosition from, GridPosition to)
        {
            int dx = to.X - from.X, dy = to.Y - from.Y;
            if (System.Math.Abs(dy) >= System.Math.Abs(dx) && dy != 0) return dy > 0 ? Up : Down;
            return dx > 0 ? Right : Left;
        }
        private static GridPosition Nearest(GridPosition from, IReadOnlyList<GridPosition> cells)
        {
            var best = cells[0]; int bestDistance = int.MaxValue;
            foreach (var c in cells)
            {
                int d = System.Math.Abs(c.X - from.X) + System.Math.Abs(c.Y - from.Y);
                if (d < bestDistance) { best = c; bestDistance = d; }
            }
            return best;
        }
    }
}
