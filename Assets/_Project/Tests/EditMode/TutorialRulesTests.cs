using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class TutorialRulesTests
    {
        private static readonly GridPosition Home = new GridPosition(6, 0);
        private static ulong Row(int y) => 0xFFUL << (y * 8);
        private static ulong Column(int x) { ulong m = 0; for (int y = 0; y < 8; y++) m |= 1UL << (y * 8 + x); return m; }
        private static BossTelegraph Attack(int id, ulong cells, AttackResponse response, int impact = 5)
            => new BossTelegraph(id, "t", cells, impact, PatternKind.Drawn, 2, response: response);
        private static TutorialState State(int note = 1, GridPosition? knight = null, bool moved = true, params BossTelegraph[] telegraphs)
            => new TutorialState { Note = note, Knight = knight ?? Home, Noise = new GridPosition[0], ResonanceLength = 8, Telegraphs = telegraphs, Moved = moved };
        private static TutorialHint Hint(TutorialState s, TutorialProgress p)
        {
            var hint = TutorialRules.Next(s, p);
            Assert.That(hint.HasValue, Is.True, "a prompt is expected");
            return hint.Value;
        }

        [Test] public void BeforeTheFirstMoveItSaysHowToMove()
        {
            var p = new TutorialProgress();
            Assert.That(Hint(State(0, moved: false), p).Key, Is.EqualTo("WASD"));
            Assert.That(TutorialRules.Next(State(1, moved: true), p), Is.Null, "nothing to teach between lessons");
        }
        [Test] public void NormalAttacksTeachGuardThenStepAwayThenStop()
        {
            var p = new TutorialProgress();
            var first = Hint(State(1, null, true, Attack(1, Row(0), AttackResponse.Either)), p);
            Assert.That((first.Key, first.Text), Is.EqualTo(("K", "막는다 · 비켜도 된다")));
            Assert.That(Hint(State(2, null, true, Attack(1, Row(0), AttackResponse.Either)), p).Key, Is.EqualTo("K"), "the same attack keeps its prompt");
            var second = Hint(State(4, null, true, Attack(2, Row(0), AttackResponse.Either)), p);
            Assert.That((second.Key, second.Text), Is.EqualTo(("W", "비킨다 · 막아도 된다")));
            Assert.That(Hint(State(4, new GridPosition(6, 7), true, Attack(2, Row(7), AttackResponse.Either)), p).Key, Is.EqualTo("S"), "the top row steps down");
            Assert.That(TutorialRules.Next(State(7, null, true, Attack(3, Row(0), AttackResponse.Either)), p), Is.Null, "two of a kind are enough");
            Assert.That(p.NormalsTaught, Is.EqualTo(2));
        }
        [Test] public void AnAttackThatMissesTheKnightIsNotCounted()
        {
            var p = new TutorialProgress();
            Assert.That(TutorialRules.Next(State(1, null, true, Attack(1, Row(3), AttackResponse.Either)), p), Is.Null);
            Assert.That(p.NormalsTaught, Is.Zero);
        }
        [Test] public void SlashesSendTheKnightSidewaysToAFreeCell()
        {
            var p = new TutorialProgress();
            var slash = Hint(State(7, null, true, Attack(5, Column(6), AttackResponse.DodgeOnly, 8)), p);
            Assert.That((slash.Key, slash.Text), Is.EqualTo(("A", "빗금은 막을 수 없다 · 옆으로")), "towards the middle");
            Assert.That(TutorialRules.SideStep(new GridPosition(0, 0), Column(0)), Is.EqualTo("D"));
            Assert.That(TutorialRules.SideStep(new GridPosition(1, 0), Column(0) | Column(1) | Column(2)), Is.EqualTo("W"), "no side free: up");
        }
        [Test] public void FullBoardAttacksAreGuardedTwiceAcrossTheFirstVerse()
        {
            var p = new TutorialProgress();
            Assert.That(Hint(State(28, null, true, Attack(7, ulong.MaxValue, AttackResponse.GuardOnly, 30)), p).Text, Is.EqualTo("판 전체 · 막는다"));
            Assert.That(Hint(State(37, null, true, Attack(9, ulong.MaxValue, AttackResponse.GuardOnly, 38)), p).Key, Is.EqualTo("K"), "after 간주1 too");
            Assert.That(TutorialRules.Next(State(80, null, true, Attack(11, ulong.MaxValue, AttackResponse.GuardOnly, 81)), p), Is.Null);
            var late = new TutorialProgress();
            Assert.That(TutorialRules.Next(State(62, null, true, Attack(1, ulong.MaxValue, AttackResponse.GuardOnly, 63)), late), Is.Null, "nothing after 벌스1");
        }
        [Test] public void NoiseThenTheTopRowThenTheResonance()
        {
            var p = new TutorialProgress();
            var s = State(11); s.Noise = new[] { new GridPosition(6, 2) };
            Assert.That(Hint(s, p).ToString(), Is.EqualTo("W 잡음 위로"));
            s.Noise = new[] { new GridPosition(2, 1) };
            Assert.That(Hint(s, p).Key, Is.EqualTo("A"), "the longer way first");
            s.Noise = new GridPosition[0]; s.Charged = true; s.Knight = new GridPosition(6, 2);
            Assert.That(Hint(s, p).ToString(), Is.EqualTo("W 현이 울린다 · 맨 윗줄로"));
            s.Knight = new GridPosition(6, 7);
            Assert.That(Hint(s, p).ToString(), Is.EqualTo("J 맨 윗줄에서 벤다"));
            s.Charged = false; s.ResonanceLeft = 5;
            Assert.That(Hint(s, p).ToString(), Is.EqualTo("J 공명 · 8번 동안 강화 공격 (5)"));
            s.ResonanceLeft = 0;
            Assert.That(TutorialRules.Next(s, p), Is.Null);
            s.Charged = true;
            Assert.That(Hint(s, p).Text, Is.Empty, "the second time, the key alone");
            s.Charged = false; s.ResonanceLeft = 8;
            Assert.That(Hint(s, p).Text, Is.Empty);
        }
        [Test] public void AfterTheLessonSectionOnlyGuardAndSlashRemain()
        {
            var p = new TutorialProgress();
            var s = State(TutorialRules.LessonEnd + 1); s.Noise = new[] { new GridPosition(6, 2) };
            Assert.That(TutorialRules.Next(s, p), Is.Null);
            s.Telegraphs = new[] { Attack(1, Column(6), AttackResponse.DodgeOnly, 40) };
            Assert.That(Hint(s, p).Key, Is.EqualTo("A"));
        }
        [Test] public void HintsFollowTheFirstClearUntilTheyAreSet()
        {
            const string cleared = "StringAsASword.Cleared", hints = "StringAsASword.TutorialHints";
            int savedCleared = PlayerPrefs.GetInt(cleared, -1), savedHints = PlayerPrefs.GetInt(hints, -1);
            try
            {
                GameFlow.ResetTutorialHints();
                GameFlow.ResetCleared(); Assert.That(GameFlow.TutorialHints, Is.True, "on before the first clear");
                GameFlow.MarkCleared(); Assert.That(GameFlow.TutorialHints, Is.False, "off after it");
                GameFlow.TutorialHints = true; Assert.That(GameFlow.TutorialHints, Is.True, "a chosen value wins");
            }
            finally
            {
                if (savedCleared < 0) PlayerPrefs.DeleteKey(cleared); else PlayerPrefs.SetInt(cleared, savedCleared);
                if (savedHints < 0) PlayerPrefs.DeleteKey(hints); else PlayerPrefs.SetInt(hints, savedHints);
                PlayerPrefs.Save();
            }
        }
    }
}
