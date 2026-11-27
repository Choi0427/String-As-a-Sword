#if UNITY_EDITOR
// DEV-AUTOPLAY (editor only, compiled out of builds).
using System;
using System.Collections.Generic;
using StringAsASword.Combat;
using StringAsASword.Player;
using StringAsASword.Rhythm;
using StringAsASword.Simulation;

namespace StringAsASword.Core
{
    /// <summary>
    /// Developer mode's auto play: plays the bound battle on its own so its features can be watched without playing.
    /// It picks each note's action like the balance bots (<see cref="BattleStrategies"/>), but plays like a decent human,
    /// not a perfect one: its presses scatter around the note (mostly Perfect, some early or late Goods, the odd Miss),
    /// it sometimes skips a note, sometimes overlooks an attack (so HIT shows as well as BLOCK), and sometimes lets go
    /// of a hold too early. It presses through the battle's own input path, so everything shows as for a player.
    /// </summary>
    public sealed class DevAutoPlayer
    {
        // About an intermediate player (SkillProfile.Intermediate is 45 ms / 3% / 8%), a little looser so Goods,
        // FAST / SLOW and Misses come up often enough to check.
        public const double TimingSigma = .05, DropRate = .04, ReadMissRate = .08;
        // Holds: the release scatters a little less than a press; now and then the string is let go halfway.
        public const double ReleaseSigma = .04, EarlyReleaseRate = .12;
        public const StrategyKind Strategy = StrategyKind.CollectFirst;

        private readonly Random rng;
        private readonly int seed;
        private readonly HashSet<int> unread = new HashSet<int>();
        private int nextNote, plannedNote = -1;
        private double plannedTime;
        private bool plannedDrop;
        private bool releasePending;
        private double releaseAt;
        private PlayerActionKind releaseKind;
        private MoveDirection releaseDirection;

        public int Presses { get; private set; }
        public int Dropped { get; private set; }
        public int Releases { get; private set; }

        public DevAutoPlayer(int seed) { this.seed = seed; rng = new Random(seed); }

        /// <summary>
        /// Plays every note whose press time has come by <paramref name="songTime"/>, and a due hold release. Call each
        /// frame before the battle is advanced to <paramref name="songTime"/>. Only notes after the last closed one are
        /// played, so after a resume the rewound (already judged) notes are left alone.
        /// </summary>
        public void Step(BattleSession battle, IBeatTimeline timeline, double songTime)
        {
            var settings = battle.Settings;
            var session = battle.Session;
            for (int guard = 0; guard < 8 && battle.Outcome == BattleOutcome.Running; guard++)
            {
                // A release due before the next press goes first, so the two happen in time order.
                int note = Math.Max(nextNote, Math.Max(battle.LastClosedNote, session.Gate.LastConsumedBeat) + 1);
                if (note >= settings.NoteCount) { TryRelease(battle, songTime, double.PositiveInfinity); return; }
                if (note != plannedNote) Plan(note, timeline.TimeOf(note) + settings.InputOffset);
                if (TryRelease(battle, songTime, plannedTime)) continue;
                if (songTime < plannedTime) return;
                nextNote = note + 1;
                // Still holding the last note's string: a player's hands are busy, so this note is let go.
                if (releasePending || session.ActiveHold.HasValue) continue;
                if (plannedDrop) { Dropped++; continue; }
                Press(battle, timeline, Math.Max(plannedTime, battle.AdvancedTime));
            }
        }

        private void Plan(int note, double noteTime)
        {
            plannedNote = note;
            plannedDrop = rng.NextDouble() < DropRate;
            plannedTime = noteTime + TimingSigma * Normal();
        }

        private void Press(BattleSession battle, IBeatTimeline timeline, double time)
        {
            var view = battle.Observe();
            view = SkilledSimulator.FilterUnread(view, seed, ReadMissRate, unread);
            var decision = BattleStrategies.Decide(Strategy, view);
            var result = battle.Request(decision.Kind, decision.Direction, time);
            Presses++;
            var hold = battle.Session.ActiveHold;
            if (!result.Accepted || !hold.HasValue) return;
            // Its own hold: let go near the tail, or now and then partway through.
            double offset = battle.Settings.InputOffset;
            double start = timeline.TimeOf(hold.Value.Beat) + offset, end = hold.Value.EndTime + offset;
            releaseAt = rng.NextDouble() < EarlyReleaseRate
                ? start + (end - start) * (.25 + .5 * rng.NextDouble())
                : end + ReleaseSigma * Normal();
            releaseAt = Math.Max(releaseAt, time);
            releaseKind = hold.Value.Kind; releaseDirection = hold.Value.Direction;
            releasePending = true;
        }

        // Releases the held key when its time has come by songTime and it is due before the next press.
        private bool TryRelease(BattleSession battle, double songTime, double nextPress)
        {
            if (!releasePending || releaseAt > songTime || releaseAt > nextPress) return false;
            releasePending = false;
            if (battle.Session.ActiveHold.HasValue)
            {
                battle.Release(releaseKind, releaseDirection, Math.Max(releaseAt, battle.AdvancedTime));
                Releases++;
            }
            return true;
        }

        private double Normal()
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
#endif
