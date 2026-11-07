using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Editor;
using StringAsASword.Player;
using StringAsASword.Rhythm;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class HoldNoteTests
    {
        // Note 1 is a hold from 2.0 to 3.0; the others are taps.
        private static readonly double[] Times = { 1.0, 2.0, 4.0 };
        private static readonly double[] Ends = { 0, 3.0, 0 };
        private RhythmSession session;
        private readonly List<TimingResult> results = new List<TimingResult>();

        [SetUp] public void SetUp()
        {
            var judge = new RhythmJudge(new AuthoredBeatTimeline(87, Times), .07, .14);
            session = new RhythmSession(new BoardState(), judge, new GridPosition(3, 3), Times.Length, Ends);
            results.Clear(); session.HoldResolved += results.Add;
        }
        private void HoldHead(PlayerActionKind kind = PlayerActionKind.Guard, MoveDirection direction = MoveDirection.Up)
        {
            Assert.That(session.RequestAction(kind, direction, 2.0).Accepted);
            Assert.That(session.ActiveHold.HasValue);
        }
        [Test] public void OnTimeReleaseIsPerfect()
        {
            HoldHead(); session.RequestRelease(PlayerActionKind.Guard, MoveDirection.Up, 3.01);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Grade, Is.EqualTo(Judgement.Perfect));
            Assert.That(results[0].BeatId, Is.EqualTo(1));
            Assert.That(session.HoldPerfectCount, Is.EqualTo(1));
            Assert.That(session.ActiveHold.HasValue, Is.False);
        }
        [Test] public void SlightlyLateReleaseIsGood()
        {
            HoldHead(); session.RequestRelease(PlayerActionKind.Guard, MoveDirection.Up, 3.1);
            Assert.That(results[0].Grade, Is.EqualTo(Judgement.Good));
        }
        [Test] public void EarlyReleaseBreaksHoldButKeepsTheAction()
        {
            HoldHead(); session.RequestRelease(PlayerActionKind.Guard, MoveDirection.Up, 2.5);
            Assert.That(results[0].Grade, Is.EqualTo(Judgement.Miss));
            Assert.That(results[0].ErrorSeconds, Is.LessThan(0));
            Assert.That(session.Combo, Is.Zero);
            Assert.That(session.AcceptedGuards, Is.EqualTo(1));
        }
        [Test] public void HoldingPastTheTailWindowIsAMiss()
        {
            HoldHead(); session.Advance(3.0 + .14 + .01);
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Grade, Is.EqualTo(Judgement.Miss));
            session.RequestRelease(PlayerActionKind.Guard, MoveDirection.Up, 3.5);
            Assert.That(results.Count, Is.EqualTo(1), "A late release after the miss is ignored.");
        }
        [Test] public void ReleasingAnotherKeyIsIgnored()
        {
            HoldHead(); session.RequestRelease(PlayerActionKind.Attack, MoveDirection.Up, 2.5);
            Assert.That(results, Is.Empty); Assert.That(session.ActiveHold.HasValue);
        }
        [Test] public void MoveHoldNeedsTheSameDirection()
        {
            HoldHead(PlayerActionKind.Move, MoveDirection.Left);
            session.RequestRelease(PlayerActionKind.Move, MoveDirection.Right, 3.0);
            Assert.That(results, Is.Empty);
            session.RequestRelease(PlayerActionKind.Move, MoveDirection.Left, 3.0);
            Assert.That(results[0].Grade, Is.EqualTo(Judgement.Perfect));
        }
        [Test] public void MissedHeadHasNoTailJudgement()
        {
            session.Advance(3.5);
            session.RequestRelease(PlayerActionKind.Guard, MoveDirection.Up, 3.0);
            Assert.That(results, Is.Empty);
            Assert.That(session.MissCount, Is.EqualTo(2));
        }
        [Test] public void TapNotesNeverStartAHold()
        {
            Assert.That(session.RequestAction(PlayerActionKind.Guard, MoveDirection.Up, 1.0).Accepted);
            Assert.That(session.ActiveHold.HasValue, Is.False);
        }

        private static BattleSettings Settings() =>
            new BattleSettings { Bpm = 87, NoteCount = Times.Length, NoteTimes = Times, HoldEndTimes = Ends, ChartId = "hold" };
        [Test] public void BattleLogsHoldEventsAndReplaysReleases()
        {
            var battle = new BattleSession(Settings(), 3);
            battle.Request(PlayerActionKind.Guard, MoveDirection.Up, 2.0);
            battle.Release(PlayerActionKind.Guard, MoveDirection.Up, 3.02);
            battle.Advance(4.5);
            Assert.That(battle.Events.Exists(e => e.type == "hold-perfect" && e.noteIndex == 1));
            string path = Path.Combine(Path.GetTempPath(), "hold-replay-" + System.Guid.NewGuid() + ".jsonl");
            try
            {
                BattleLog.Write(path, battle);
                var replay = BattleLog.Replay(path);
                Assert.That(replay.Session.HoldPerfectCount, Is.EqualTo(1));
            }
            finally { File.Delete(path); }
        }
        [Test] public void ReleasesAreNotLoggedForChartsWithoutHolds()
        {
            var settings = Settings(); settings.HoldEndTimes = null;
            var battle = new BattleSession(settings, 3);
            battle.Release(PlayerActionKind.Guard, MoveDirection.Up, 1.0);
            Assert.That(battle.Inputs, Is.Empty);
        }
        [Test] public void SettingsRejectBadHolds()
        {
            var shortList = Settings(); shortList.HoldEndTimes = new[] { 0.0, 3.0 };
            Assert.Throws<System.ArgumentException>(shortList.Validate);
            var backwards = Settings(); backwards.HoldEndTimes = new[] { 0.0, 1.5, 0 };
            Assert.Throws<System.ArgumentException>(backwards.Validate);
            var intoNext = Settings(); intoNext.HoldEndTimes = new[] { 0.0, 4.0, 0 };
            Assert.Throws<System.ArgumentException>(intoNext.Validate);
        }
        [Test] public void ImporterRejectsHoldIntoNextNoteAndFeedsHoldTimes()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            try
            {
                const string bad = "{\"formatVersion\":1,\"name\":\"N\",\"level\":1,\"notes\":[" +
                    "{\"tick\":480,\"track\":\"Note_3\",\"type\":\"hold\",\"endTick\":960}," +
                    "{\"tick\":960,\"track\":\"Note_3\",\"type\":\"tap\"}]}";
                Assert.Throws<InvalidDataException>(() => RcmChartImporter.Import(song, bad));
                const string good = "{\"formatVersion\":1,\"name\":\"N\",\"level\":1,\"notes\":[" +
                    "{\"tick\":480,\"track\":\"Note_3\",\"type\":\"hold\",\"endTick\":720}," +
                    "{\"tick\":960,\"track\":\"Note_3\",\"type\":\"tap\"}]}";
                RcmChartImporter.Import(song, good);
                var ends = song.HoldEndTimes();
                Assert.That(ends[0], Is.EqualTo(1.5 * 60.0 / 87).Within(1e-9));
                Assert.That(ends[1], Is.Zero);
            }
            finally { Object.DestroyImmediate(song); }
        }
    }
}
