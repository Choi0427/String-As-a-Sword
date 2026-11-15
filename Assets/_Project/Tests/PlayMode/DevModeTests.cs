using System.Collections;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    public sealed class DevModeTests
    {
        private bool hadDevMode;
        [SetUp] public void SetUp() { hadDevMode = GameFlow.DevMode; }
        [TearDown] public void TearDown()
        {
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            if (game != null) game.PauseSession();
            GameFlow.Invincible = false; GameFlow.BossHpOff = false; GameFlow.StartPhase = 1; GameFlow.DevMode = hadDevMode;
        }

        [UnityTest] public IEnumerator SettingsChooseTheStartPhaseAndInvincibilityAndTheBattleFollows()
        {
            GameFlow.DevMode = false;
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            menu.Activate(1);
            Assert.That(menu.transform.Find("Settings/Card/무적").gameObject.activeSelf, Is.False, "Hidden until developer mode is on.");
            menu.ChooseSetting("무적", 1); menu.ChooseSetting("시작 페이즈", 2);
            Assert.That(GameFlow.Invincible, Is.False, "Without developer mode nothing applies.");
            Assert.That(GameFlow.StartPhase, Is.EqualTo(1));
            menu.ChooseSetting("개발자 모드", 1);
            Assert.That(GameFlow.BossHpOff, Is.False, "Boss HP is on unless switched off.");
            menu.ChooseSetting("보스 HP", 0);
            Assert.That(GameFlow.BossHpOff);
            Assert.That(menu.transform.Find("Settings/Card/무적").gameObject.activeSelf);
            Assert.That(GameFlow.Invincible); Assert.That(GameFlow.StartPhase, Is.EqualTo(3));

            GameFlow.StartBattle();
            yield return null; yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            Assert.That(game.Phases.PhaseId, Is.EqualTo(3), "The battle starts in the chosen phase.");
            Assert.That(game.Battle.Invincible); Assert.That(game.Battle.IgnoreBossHp);
            game.DevSetBossHp(true);
            Assert.That(game.Battle.IgnoreBossHp, Is.False);
            // The song is there too: a count-in before the phase's note, with the notes before it already gone.
            double At(int phase) => game.Timeline.TimeOf(game.DevPhaseNote(phase)) - game.Song.CountInBeats * game.Timeline.SecondsPerBeat;
            Assert.That(new[] { game.DevPhaseNote(1), game.DevPhaseNote(2), game.DevPhaseNote(3), game.DevPhaseNote(4) },
                Is.EqualTo(new[] { 0, 124, 234, 264 }), "간주2, Interlude, 싸비3 of the boss chart.");
            Assert.That(game.DevPhaseNote(4), Is.GreaterThan(game.DevPhaseNote(3)).And.LessThan(game.Battle.Settings.FinishNote));
            Assert.That(game.SongTime, Is.EqualTo(At(3)).Within(1.5));
            Assert.That(game.Battle.LastClosedNote, Is.GreaterThan(0).And.LessThan(game.DevPhaseNote(3)));
            Assert.That(game.Battle.PlayerHp, Is.EqualTo(game.Stage.PlayerMaxHp), "The skipped notes cost no HP.");
            Assert.That(game.Battle.Outcome, Is.EqualTo(StringAsASword.Combat.BattleOutcome.Running));
            game.DevGoToPhase(4);
            Assert.That(game.Phases.PhaseId, Is.EqualTo(4));
            Assert.That(game.SongTime, Is.EqualTo(At(4)).Within(1.5));
            game.DevGoToPhase(2);
            Assert.That(game.Phases.PhaseId, Is.EqualTo(2), "An earlier phase restarts the battle in it.");
            Assert.That(game.SongTime, Is.EqualTo(At(2)).Within(1.5));
            game.DevSetInvincible(false);
            Assert.That(game.Battle.Invincible, Is.False);

            GameFlow.DevMode = false;
            game.StartSession();
            Assert.That(game.Phases.PhaseId, Is.EqualTo(1), "With developer mode off a battle is a normal one.");
            Assert.That(game.SongTime, Is.LessThan(0), "It starts with the count-in before the first note.");
        }
    }
}
