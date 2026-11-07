using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Editor;
using StringAsASword.Rhythm;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class AuthoredChartTests
    {
        private static readonly double[] Times = { 1.0, 1.5, 1.6, 3.0 };
        [Test] public void NearestNoteUsesAuthoredTimesAndPrefersEarlierOnTies()
        {
            var t = new AuthoredBeatTimeline(87, Times);
            Assert.That(t.TimeOf(2), Is.EqualTo(1.6));
            Assert.That(t.NearestBeat(1.54), Is.EqualTo(1));
            Assert.That(t.NearestBeat(1.56), Is.EqualTo(2));
            Assert.That(t.NearestBeat(1.55), Is.EqualTo(1));
            Assert.That(t.NearestBeat(.2), Is.EqualTo(0));
            Assert.That(t.NearestBeat(10), Is.GreaterThanOrEqualTo(3));
            Assert.That(t.NearestBeat(-1), Is.LessThanOrEqualTo(0));
        }
        [Test] public void RejectsUnorderedTimes() =>
            Assert.Throws<System.ArgumentException>(() => new AuthoredBeatTimeline(87, new[] { 1.0, 1.0 }));
        [Test] public void ImportReadsOnlyRequestedTrackAndKeepsHolds()
        {
            const string json = "{\"formatVersion\":1,\"name\":\"NORMAL\",\"level\":1,\"notes\":[" +
                "{\"tick\":960,\"track\":\"Note_3\",\"type\":\"hold\",\"endTick\":1200}," +
                "{\"tick\":480,\"track\":\"Note_3\",\"type\":\"tap\"}," +
                "{\"tick\":480,\"track\":\"Note_1\",\"type\":\"tap\"}]}";
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            try
            {
                Assert.That(RcmChartImporter.Import(song, json), Is.EqualTo(2));
                Assert.That(song.HasChart && song.NoteCount == 2);
                Assert.That(song.NoteTick(0), Is.EqualTo(480));
                Assert.That(song.NoteHoldEndTick(0), Is.Zero);
                Assert.That(song.NoteHoldEndTick(1), Is.EqualTo(1200));
                // 480 ticks = one beat at 87 BPM.
                Assert.That(song.NoteTimes()[0], Is.EqualTo(60.0 / 87).Within(1e-9));
            }
            finally { Object.DestroyImmediate(song); }
        }
        [Test] public void BattleRunsOnAuthoredChartAndHashDiffersFromEvenNotes()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = Times.Length, NoteTimes = Times, ChartId = "x" };
            var battle = new BattleSession(settings, 1);
            battle.Advance(Times[3] + settings.GoodWindow + .01);
            Assert.That(battle.Records.Count, Is.EqualTo(Times.Length));
            var even = settings.Copy(); even.NoteTimes = null;
            Assert.That(BattleLog.ChartHash(settings), Is.Not.EqualTo(BattleLog.ChartHash(even)));
        }
        [Test] public void NoteCountMustMatchChartLength()
        {
            var settings = new BattleSettings { Bpm = 87, NoteCount = 3, NoteTimes = Times };
            Assert.Throws<System.ArgumentException>(settings.Validate);
        }
    }
}
