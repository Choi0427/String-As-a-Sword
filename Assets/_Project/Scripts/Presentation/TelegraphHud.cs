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
        // A failed guard reads as a plain HIT, the same as any other hit.
        public const string Blocked = "BLOCK", Hit = "HIT";

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

    }
}
