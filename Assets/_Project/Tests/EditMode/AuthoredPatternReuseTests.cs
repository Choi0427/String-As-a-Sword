using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Rhythm;
using UnityEditor;

namespace StringAsASword.Tests
{
    // Spike: can the existing fairness checks judge a hand-drawn 8x8 shape at an authored note,
    // without the random scheduler or a PatternKind? No production code is changed here.
    public sealed class AuthoredPatternReuseTests
    {
        // Rows are written top first: the first string is board row y = 7 (the player's start row).
        private static ulong Mask(params string[] rows)
        {
            Assert.That(rows.Length, Is.EqualTo(8));
            ulong mask = 0;
            for (int r = 0; r < 8; r++)
            {
                Assert.That(rows[r].Length, Is.EqualTo(8), $"row {r}");
                for (int x = 0; x < 8; x++) if (rows[r][x] == '#') mask |= 1UL << ((7 - r) * 8 + x);
            }
            return mask;
        }
        private static readonly ulong XShape = Mask(
            "#......#",
            ".#....#.",
            "..#..#..",
            "...##...",
            "...##...",
            "..#..#..",
            ".#....#.",
            "#......#");
        // Everything but the bottom-left corner: only reachable from nearby cells.
        private static readonly ulong CornerOnly = ~Mask(
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "#.......");

        private static bool[] ChartFastNotes(out int noteCount)
        {
            var song = AssetDatabase.LoadAssetAtPath<SongDefinition>("Assets/_Project/Data/Songs/GeomEulHyeon87.asset");
            Assert.That(song, Is.Not.Null);
            var settings = new BattleSettings { Bpm = song.Bpm, NoteCount = song.NoteCount, NoteTimes = song.NoteTimes() };
            noteCount = song.NoteCount;
            return settings.FastNotes();
        }
        private static BossTelegraph Authored(ulong cells, int impact, int warning, AttackResponse response = AttackResponse.Either)
            => new BossTelegraph(1, "authored", cells, impact, PatternKind.Row, warning, response: response);

        [Test] public void DodgeCheckJudgesADrawnShapeFromEveryStartCell()
        {
            const int impact = 120, warning = 3;
            var attack = Authored(XShape, impact, warning);
            var none = new List<BossTelegraph>();
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            {
                int needed = BossPatternSystem.DodgeActions(new GridPosition(x, y), attack, impact - warning, none);
                bool onShape = (XShape & (1UL << (y * 8 + x))) != 0;
                Assert.That(needed, Is.EqualTo(onShape ? 1 : 0), $"start ({x},{y})");
            }
        }
        [Test] public void DodgeCheckRejectsAnUnfairDrawnShapeAndNamesTheCells()
        {
            const int impact = 120, warning = 2;
            var attack = Authored(CornerOnly, impact, warning, AttackResponse.DodgeOnly);
            var none = new List<BossTelegraph>();
            var unfair = new List<string>();
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
                if (BossPatternSystem.DodgeActions(new GridPosition(x, y), attack, impact - warning, none) < 0) unfair.Add($"({x},{y})");
            // Manhattan distance to (0,0) above 2 cannot make it in two notes.
            Assert.That(unfair.Count, Is.EqualTo(64 - 6), string.Join(" ", unfair));
            Assert.That(BossPatternSystem.DodgeActions(new GridPosition(1, 1), attack, impact - warning, none), Is.EqualTo(2));
        }
        [Test] public void DodgeCheckSeesEarlierAuthoredHitsInASequence()
        {
            // Hit 1 at 120 sweeps the player's row; hit 2 at 121 the left half. Alone, hit 2 takes one step right.
            // With hit 1 disclosed, that step is blocked at 120, so the route goes up (or down) first, then right: two actions.
            ulong left = Mask("####....", "####....", "####....", "####....", "####....", "####....", "####....", "####....");
            ulong row3 = Mask("........", "........", "........", "........", "########", "........", "........", "........");
            var first = Authored(row3, 120, 1, AttackResponse.DodgeOnly);
            var second = Authored(left, 121, 2, AttackResponse.DodgeOnly);
            var player = new GridPosition(3, 3);
            Assert.That(BossPatternSystem.DodgeActions(player, second, 119, new List<BossTelegraph>()), Is.EqualTo(1));
            Assert.That(BossPatternSystem.DodgeActions(player, second, 119, new List<BossTelegraph> { first }), Is.EqualTo(2));
        }
        [Test] public void FastSectionRuleWorksOnAuthoredNotesOfTheRealChart()
        {
            var fast = ChartFastNotes(out int noteCount);
            Assert.That(fast, Is.Not.Null);
            var system = new BossPatternSystem(new PatternDefinition[0], null, 0, fast);
            int firstFast = System.Array.IndexOf(fast, true);
            Assert.That(firstFast, Is.GreaterThan(3), "the chart has a fast run to test against");
            Assert.That(system.CrossesFastSection(firstFast - 3, firstFast), Is.True, $"an attack hitting note {firstFast} crosses it");
            int clean = -1;
            for (int n = 4; n < noteCount && clean < 0; n++) if (!system.CrossesFastSection(n - 3, n)) clean = n;
            Assert.That(clean, Is.GreaterThan(0), "some note can take a 3-note warning");
            TestContext.WriteLine($"notes {noteCount}, first fast note {firstFast}, first clean impact for warning 3: {clean}");
        }
        [Test] public void BotRoutePlannerAvoidsADrawnShape()
        {
            var attack = Authored(XShape, 10, 3);
            var start = new GridPosition(0, 7); // On the X: the planner must step off before note 10.
            var route = SafeRoutePlanner.FindAll(start, new[] { new GridPosition(7, 7) }, 7, new List<BossTelegraph> { attack }, 32, false);
            Assert.That(route.Found, Is.True);
        }
    }
}
