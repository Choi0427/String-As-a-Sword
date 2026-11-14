using System;
using System.Collections.Generic;
using System.Text;
using StringAsASword.Combat;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Pure telegraph reading rules shared by the HUD and the board marks.</summary>
    public static class TelegraphHud
    {
        // A row, column or cross always keeps its per-cell numbers; wider attacks would bury the board marks.
        public const int DenseBadgeLimit = 16;
        public const string Blocked = "BLOCKED", Hit = "HIT", GuardBroken = "GUARD BROKEN";

        public static string ResponseWord(AttackResponse response)
            => response == AttackResponse.GuardOnly ? "GUARD" : response == AttackResponse.DodgeOnly ? "MOVE" : "EITHER";

        // Two kinds on screen: a strong attack (a guard does not protect) takes the gold of the attack edge;
        // a normal one, guard-only included, keeps the caller's own colour.
        public static Color ResponseColor(AttackResponse response, Color normal)
            => response == AttackResponse.DodgeOnly ? new Color(1f, .72f, .25f) : normal;

        public static string ThreatText(AttackResponse response, int remaining, int hits)
        {
            string lead = response == AttackResponse.GuardOnly ? "GUARD IN " : response == AttackResponse.DodgeOnly ? "MOVE IN " : "DANGER IN ";
            return hits > 1 ? lead + remaining + " x" + hits : lead + remaining;
        }

        // A guard that was ignored is both a hit and a guard record, so it has to be read before the plain hit.
        public static string OutcomeText(NoteRecord record)
            => record.hit ? (record.category == "Guard" ? GuardBroken : Hit) : record.guardSuccess ? Blocked : null;

        // Later hits stay in the active list, so a cell the next hit misses still reports the hit that reaches it.
        // Equal impacts report the strictest response, as BattleSession's response events do.
        public static void MapCells(IReadOnlyList<BossTelegraph> active, int lastClosed, int[] counts, int[] soonest, AttackResponse[] responses)
        {
            Array.Clear(counts, 0, counts.Length);
            for (int i = 0; i < soonest.Length; i++) { soonest[i] = int.MaxValue; responses[i] = AttackResponse.Either; }
            for (int t = 0; t < active.Count; t++)
            {
                var attack = active[t];
                int remaining = attack.Remaining(lastClosed);
                for (int i = 0; i < 64; i++)
                {
                    if ((attack.Cells & (1UL << i)) == 0) continue;
                    counts[i]++;
                    if (remaining < soonest[i] || remaining == soonest[i] && attack.Response > responses[i])
                    { soonest[i] = remaining; responses[i] = attack.Response; }
                }
            }
        }

        // Id - Hit is shared by every hit of a sequence, so one attack takes one line however many hits remain.
        // Returns the number of lines wanted, which can exceed the limit; only lines under it are written.
        public static int AppendPatternLines(StringBuilder text, IReadOnlyList<BossTelegraph> active, int lastClosed, int shown, int limit)
        {
            for (int i = 0; i < active.Count; i++)
            {
                var attack = active[i];
                int sequence = attack.Id - attack.Hit, hits = 1, remaining = attack.Remaining(lastClosed);
                bool repeated = false;
                for (int j = 0; j < active.Count; j++)
                {
                    if (j == i || active[j].Id - active[j].Hit != sequence) continue;
                    if (j < i) { repeated = true; break; }
                    hits++; remaining = Math.Min(remaining, active[j].Remaining(lastClosed));
                }
                if (repeated || shown++ >= limit) continue;
                text.Append("/// ").Append(ResponseWord(attack.Response)).Append("  ").Append(attack.Name).Append("  /  ").Append(remaining).Append(" NOTES");
                if (hits > 1) text.Append("  x").Append(hits);
                text.Append('\n');
            }
            return shown;
        }

        // Wide attacks keep one number per countdown in each 2x2 block. When cells fire at different times, the
        // player's cell and its neighbours keep theirs too, because that is where the next move has to read them.
        public static ulong BadgeCells(ulong marked, int[] soonest, int playerCell)
        {
            int count = 0, first = -1;
            bool mixed = false;
            for (int i = 0; i < 64; i++)
            {
                if ((marked & (1UL << i)) == 0) continue;
                count++;
                if (first < 0) first = i;
                else if (soonest[i] != soonest[first]) mixed = true;
            }
            if (count <= DenseBadgeLimit) return marked;
            ulong shown = 0;
            for (int i = 0; i < 64; i++)
            {
                if ((marked & (1UL << i)) == 0) continue;
                int block = (i / 8 & ~1) * 8 + (i % 8 & ~1);
                bool repeated = SameCountdown(shown, block, i, soonest) || SameCountdown(shown, block + 1, i, soonest) ||
                    SameCountdown(shown, block + 8, i, soonest) || SameCountdown(shown, block + 9, i, soonest);
                if (!repeated) shown |= 1UL << i;
            }
            if (!mixed) return shown;
            int x = playerCell % 8, y = playerCell / 8;
            ulong beside = 1UL << playerCell;
            if (y < 7) beside |= 1UL << (playerCell + 8);
            if (y > 0) beside |= 1UL << (playerCell - 8);
            if (x < 7) beside |= 1UL << (playerCell + 1);
            if (x > 0) beside |= 1UL << (playerCell - 1);
            return shown | (beside & marked);
        }
        private static bool SameCountdown(ulong shown, int other, int cell, int[] soonest)
            => (shown & (1UL << other)) != 0 && soonest[other] == soonest[cell];
    }
}
