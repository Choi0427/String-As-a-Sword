using System;
using System.Collections.Generic;
using StringAsASword.Combat;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>Pure telegraph reading rules shared by the HUD and the board marks.</summary>
    public static class TelegraphHud
    {
        // A failed guard reads as a plain HIT, the same as any other hit.
        public const string Blocked = "BLOCK", Hit = "HIT";

        // Two kinds on screen: a strong attack (a guard does not protect) takes the gold of the attack edge;
        // a normal one, guard-only included, keeps the caller's own colour.
        public static Color ResponseColor(AttackResponse response, Color normal)
            => response == AttackResponse.DodgeOnly ? new Color(1f, .72f, .25f) : normal;

        // A guard that was ignored is both a hit and a guard record, so it has to be read before the plain hit.
        public static string OutcomeText(NoteRecord record)
            => record.hit ? Hit : record.guardSuccess ? Blocked : null;

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

    }
}
