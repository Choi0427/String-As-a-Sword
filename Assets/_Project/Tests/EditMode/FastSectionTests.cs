using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;

namespace StringAsASword.Tests
{
    public sealed class FastSectionTests
    {
        private const int Notes = 80;
        private static readonly GridPosition Player = new GridPosition(3, 7);
        // Notes 30..33 follow a sub-beat gap (a sixteenth run).
        private static bool[] Fast()
        {
            var fast = new bool[Notes];
            for (int n = 30; n <= 33; n++) fast[n] = true;
            return fast;
        }
        private static PatternDefinition[] Patterns() => new[] {
            new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2 },
            new PatternDefinition { Name = "Lock", Kind = PatternKind.LockedCell, TelegraphNotes = 1 },
            new PatternDefinition { Name = "Chord", Kind = PatternKind.FullBoard, Response = AttackResponse.GuardOnly, TelegraphNotes = 2, Hits = 3 }
        };

        [Test] public void NoAttackWarnsOrHitsAcrossASubBeatGapAndAttacksResumeAfterIt([Range(0, 40)] int seed)
        {
            var system = new BossPatternSystem(Patterns(), new RandomAttackSchedule { Enabled = true }, seed, Fast());
            system.BeginPhase(1, 0);
            int closed = -1;
            var published = new List<(int closedAt, BossTelegraph attack)>();
            system.Changed += (type, attack) => { if (type == "publish") published.Add((closed, attack)); };
            for (closed = -1; closed < Notes; closed++)
            {
                system.Resolve(closed);
                system.Publish(closed, Player, Notes, false);
            }
            Assert.That(published, Is.Not.Empty);
            foreach (var (at, attack) in published)
                for (int n = at + 1; n <= attack.ImpactNote; n++)
                    Assert.That(n < 30 || n > 33, $"{attack.Name} published after note {at} hits {attack.ImpactNote} across fast note {n}.");
            Assert.That(published.Exists(p => p.attack.ImpactNote > 33), "Attacks resume once the fast run is over.");
        }
        [Test] public void FixedScheduleDefersAcrossAFastRunToo()
        {
            var line = new PatternDefinition { Name = "Line", Kind = PatternKind.Row, TelegraphNotes = 2, FirstImpact = 32, RepeatNotes = 1000 };
            var system = new BossPatternSystem(new[] { line }, null, 0, Fast());
            system.BeginPhase(1, 0);
            var impacts = new List<int>();
            system.Changed += (type, attack) => { if (type == "publish") impacts.Add(attack.ImpactNote); };
            for (int closed = -1; closed < Notes; closed++) { system.Resolve(closed); system.Publish(closed, Player, Notes, false); }
            Assert.That(impacts, Is.EqualTo(new[] { 35 }), "First impact at 31 is pushed past the fast notes 30..33, then needs two clean warning notes.");
        }
        [Test] public void SettingsMarkOnlySubBeatGapsAsFast()
        {
            double beat = 60.0 / 87;
            var settings = new BattleSettings { Bpm = 87, NoteCount = 4, NoteTimes = new[] { 1.0, 1 + beat, 1 + beat * 1.25, 1 + beat * 2.25 } };
            Assert.That(settings.FastNotes(), Is.EqualTo(new[] { false, false, true, false }));
            Assert.That(new BattleSettings { Bpm = 87, NoteCount = 4 }.FastNotes(), Is.Null);
        }
    }
}
