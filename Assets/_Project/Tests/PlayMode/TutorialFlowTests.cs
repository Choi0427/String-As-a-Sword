using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
using StringAsASword.Core;
using StringAsASword.Player;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    public sealed class TutorialFlowTests
    {
        private PrototypeGame game;
        private bool originalBackground, hadTutorial;

        [UnitySetUp] public IEnumerator SetUp()
        {
            originalBackground = Application.runInBackground;
            Application.runInBackground = true;
            // Finishing or skipping the tutorial marks it done; the player's own record must survive the tests.
            hadTutorial = GameFlow.TutorialSeen;
            yield return SceneManager.LoadSceneAsync(GameFlow.BattleScene);
            game = Object.FindAnyObjectByType<PrototypeGame>();
            typeof(PrototypeGame).GetField("pauseOnFocusLoss", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(game, false);
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            Application.runInBackground = originalBackground;
            if (hadTutorial) GameFlow.MarkTutorialSeen(); else GameFlow.ResetTutorialSeen();
            yield return null;
        }

        /// <summary>
        /// Plays the tutorial as a player following its cards would: three moves, a guard against the row attack, a step out of
        /// the slash, the noise, the climb and the resonance. Each lesson waits for its action; the song then starts on a
        /// downbeat with the song's battle fresh.
        /// </summary>
        [UnityTest] public IEnumerator EachLessonWaitsForItsActionAndTheSongStartsAfterTheLast()
        {
            game.StartTutorial();
            var tutorial = game.Tutorial;
            yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial));
            Assert.That(game.InTutorialBattle);
            Assert.That(tutorial.Letterbox.Visible);
            Assert.That(tutorial.Letterbox.Subtitle, Does.Contain("박자에 맞춰야지만"));
            Assert.That(game.View.Screens.KeyGuideAlpha, Is.Zero, "the cards show the keys; the key guide is hidden");

            var seen = new List<TutorialStep>();
            int note = 0;
            float deadline = Time.realtimeSinceStartup + 120;
            while (!tutorial.Steps.Finished && Time.realtimeSinceStartup < deadline)
            {
                double at = game.Timeline.TimeOf(note);
                while (game.SongTime < at - .03 && Time.realtimeSinceStartup < deadline) yield return null;
                if (!tutorial.Steps.Finished && !seen.Contains(tutorial.Step)) seen.Add(tutorial.Step);
                if (tutorial.LessonActive) Act(tutorial, note, at);
                note++;
            }
            Assert.That(tutorial.Steps.Finished, "the tutorial ends when its lessons are done");
            Assert.That(seen, Is.EqualTo(new[] { TutorialStep.Move, TutorialStep.Attack, TutorialStep.Slash, TutorialStep.Noise, TutorialStep.Strike }));
            Assert.That(game.Health.TotalHp, Is.LessThan(game.Stage.HealthSegments.Sum()), "the resonance cut the crown");

            while (game.State != PrototypeState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Playing), "the last downbeat starts the song");
            Assert.That(game.InTutorialBattle, Is.False);
            Assert.That(game.Session.Position, Is.EqualTo(new GridPosition(6, 0)), "the song's battle starts fresh on g1");
            Assert.That(game.Health.TotalHp, Is.EqualTo(game.Stage.HealthSegments.Sum()));
            Assert.That(game.SongTime, Is.EqualTo(IntroTimeline.HandoverSongTime(game.Timeline)).Within(.15));
            Assert.That(tutorial.Playing, Is.False);
            Assert.That(game.View.Screens.KeyGuideAlpha, Is.EqualTo(1f));
            Assert.That(GameFlow.TutorialSeen, "done once: later prologues lead to the song");
            LogAssert.NoUnexpectedReceived();
        }
        /// <summary>
        /// A player who does not wait for the last card: right after the noise they climb to the top row and strike while the
        /// praise and the next line are still up. That strike spends the charge and no other noise comes, so it must count.
        /// </summary>
        [UnityTest] public IEnumerator StrikingBeforeTheLastCardStillPassesTheLesson()
        {
            game.StartTutorial();
            var tutorial = game.Tutorial;
            yield return null;
            bool early = false;
            game.Battle.Resonance.Ignited += n => early |= !tutorial.LessonActive;
            int note = 0;
            float deadline = Time.realtimeSinceStartup + 90;
            while (!tutorial.Steps.Finished && Time.realtimeSinceStartup < deadline)
            {
                double at = game.Timeline.TimeOf(note);
                while (game.SongTime < at - .03 && Time.realtimeSinceStartup < deadline) yield return null;
                if (tutorial.LessonActive || tutorial.Step == TutorialStep.Strike) Act(tutorial, note, at);
                note++;
            }
            Assert.That(early, "The resonance was set off before the strike lesson's card came up.");
            Assert.That(tutorial.Steps.Finished, "The early strike counts, and the tutorial ends.");
            while (game.State != PrototypeState.Playing && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Playing), "The song starts after it.");
            LogAssert.NoUnexpectedReceived();
        }
        // What a player reading the card does on this note.
        private void Act(TutorialDirector tutorial, int note, double at)
        {
            var battle = game.Battle;
            var knight = battle.Session.Position;
            bool Hits(BossTelegraph t) => t.ImpactNote == note && (t.Cells & (1UL << (knight.Y * 8 + knight.X))) != 0;
            switch (tutorial.Step)
            {
                case TutorialStep.Move:
                    battle.Request(PlayerActionKind.Move, knight.Y < 7 ? MoveDirection.Up : MoveDirection.Down, at); break;
                case TutorialStep.Attack:
                    if (battle.Patterns.Active.Any(Hits)) battle.Request(PlayerActionKind.Guard, MoveDirection.Up, at);
                    break;
                case TutorialStep.Slash:
                    if (battle.Patterns.Active.Any(Hits)) battle.Request(PlayerActionKind.Move, knight.X > 0 ? MoveDirection.Left : MoveDirection.Right, at);
                    break;
                case TutorialStep.Noise:
                    var target = tutorial.NoiseCell;
                    var step = target.X < knight.X ? MoveDirection.Left : target.X > knight.X ? MoveDirection.Right : target.Y > knight.Y ? MoveDirection.Up : MoveDirection.Down;
                    battle.Request(PlayerActionKind.Move, step, at); break;
                case TutorialStep.Strike:
                    if (battle.Resonance.Charged && knight.Y < 7) battle.Request(PlayerActionKind.Move, MoveDirection.Up, at);
                    else battle.Request(PlayerActionKind.Attack, MoveDirection.Up, at);
                    break;
            }
        }
        [UnityTest] public IEnumerator EscSkipsTheTutorialToTheCountIn()
        {
            game.StartTutorial();
            yield return null;
            game.SkipTutorial();
            Assert.That(game.State, Is.EqualTo(PrototypeState.CountIn));
            Assert.That(game.InTutorialBattle, Is.False);
            Assert.That(game.Tutorial.Playing, Is.False);
            Assert.That(game.Tutorial.Letterbox.Visible, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
