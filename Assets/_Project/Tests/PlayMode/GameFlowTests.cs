using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    public sealed class GameFlowTests
    {
        private bool hadCleared, hadPrologue, originalBackground;
        private InputSettings.BackgroundBehavior originalInputBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInput;
#endif
        private Keyboard keyboard;
        private readonly List<Keyboard> externalKeyboards = new List<Keyboard>();

        [UnitySetUp] public IEnumerator SetUp()
        {
            // The player's saved clear must survive the tests.
            hadCleared = GameFlow.Cleared; GameFlow.ResetCleared();
            hadPrologue = GameFlow.PrologueSeen;
            originalBackground = Application.runInBackground; Application.runInBackground = true;
            originalInputBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            originalEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            externalKeyboards.Clear();
            foreach (var device in InputSystem.devices)
                if (device is Keyboard existing && existing.enabled) { externalKeyboards.Add(existing); InputSystem.DisableDevice(existing); }
            keyboard = InputSystem.AddDevice<Keyboard>();
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            if (game != null) game.PauseSession();
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            foreach (var existing in externalKeyboards) if (existing.added) InputSystem.EnableDevice(existing);
            if (hadCleared) GameFlow.MarkCleared(); else GameFlow.ResetCleared();
            if (hadPrologue) GameFlow.MarkPrologueSeen(); else GameFlow.ResetPrologueSeen();
            InputSystem.settings.backgroundBehavior = originalInputBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInput;
#endif
            Application.runInBackground = originalBackground;
            yield return null;
        }
        private static IEnumerator LoadAndWait(string scene)
        {
            yield return SceneManager.LoadSceneAsync(scene);
            yield return null;
        }
        private static IEnumerator UntilScene(string scene, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (SceneManager.GetActiveScene().name != scene && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene));
            yield return null;
        }

        [UnityTest] public IEnumerator MenuFollowsTheFigmaLayoutAndHoverAndTurnsWhiteOnTheLeftAfterAClear()
        {
            yield return LoadAndWait(GameFlow.MenuScene);
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            Assert.That(menu, Is.Not.Null);
            var start = GameObject.Find("Menu_시작").GetComponent<RectTransform>();
            Assert.That(start.anchoredPosition.x, Is.EqualTo(184.5f).Within(.01f));
            Assert.That(start.anchoredPosition.y, Is.EqualTo(-631.21f).Within(.01f));
            Assert.That(menu.White, Is.False);
            menu.Select(0);
            Assert.That(menu.LabelColorHex(0), Is.EqualTo("0C0C0C"), "Hovered: dark text on the grey box.");
            Assert.That(menu.LabelColorHex(1), Is.EqualTo("D9D9D9"));
            var art = GameObject.Find("MainMenu_Logo_BG").GetComponent<UnityEngine.UI.Image>();
            Assert.That(art.sprite, Is.Not.Null, "The Figma background is assigned.");
            Assert.That(art.material.GetFloat("_Invert"), Is.Zero);

            GameFlow.MarkCleared(); menu.ApplyTheme();
            Assert.That(menu.White);
            Assert.That(art.material.GetFloat("_Invert"), Is.EqualTo(1), "Only the left side is inverted by the shader.");
            Assert.That(menu.LabelColorHex(1), Is.EqualTo("262626"));
            LogAssert.NoUnexpectedReceived();
        }
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        [UnityTest] public IEnumerator StartOpensWithThePrologueTheFirstTimeAndEscSkipsToTheTutorialThenTheCountIn()
        {
            GameFlow.ResetPrologueSeen();
            yield return LoadAndWait(GameFlow.MenuScene);
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            Assert.That(menu.ItemCount, Is.EqualTo(3), "시작, 설정, 종료.");
            menu.Activate(MainMenuView.StartItem);
            yield return UntilScene(GameFlow.BattleScene, 10);
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            Assert.That(game.State, Is.EqualTo(PrototypeState.Prologue));
            Assert.That(game.Intro.Playing);
            Assert.That(game.View.ContentRect.GetComponent<CanvasGroup>().alpha, Is.Zero, "The battle screen waits under the prologue.");
            yield return Press(Key.Escape);
            Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial), "Skipping the prologue goes on to the tutorial the first time.");
            Assert.That(game.Intro.Playing, Is.False);
            Assert.That(game.Tutorial.Playing);
            Assert.That(game.InTutorialBattle);
            Assert.That(game.View.ContentRect.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            Assert.That(GameFlow.PrologueSeen);
            yield return Press(Key.Escape);
            Assert.That(game.State, Is.EqualTo(PrototypeState.CountIn), "Skipping the tutorial goes to the usual count-in.");
            Assert.That(game.Tutorial.Playing, Is.False);
            Assert.That(game.InTutorialBattle, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator OnceSeenStartGoesStraightToTheBattleAndSettingsReplayTheTutorial()
        {
            GameFlow.MarkPrologueSeen();
            yield return LoadAndWait(GameFlow.MenuScene);
            Object.FindAnyObjectByType<MainMenuView>().Activate(MainMenuView.StartItem);
            yield return UntilScene(GameFlow.BattleScene, 10);
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            Assert.That(game.State, Is.EqualTo(PrototypeState.CountIn), "No prologue and no tutorial the second time.");
            // From the pause screen's settings: the run is dropped for the tutorial.
            game.PauseSession(); yield return null;
            game.View.Screens.Settings.RequestTutorial(); yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial));
            Assert.That(game.View.Screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.None));
            // From the menu's settings: the battle scene opens with the tutorial.
            yield return LoadAndWait(GameFlow.MenuScene);
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            menu.Activate(MainMenuView.SettingsItem);
            menu.Settings.RequestTutorial();
            yield return UntilScene(GameFlow.BattleScene, 10);
            Assert.That(Object.FindAnyObjectByType<PrototypeGame>().State, Is.EqualTo(PrototypeState.Tutorial));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator JTearsTheStringOnTheBeatAndTheTutorialTakesOverOnTheDownbeat()
        {
            GameFlow.ResetPrologueSeen();
            yield return LoadAndWait(GameFlow.MenuScene);
            GameFlow.StartBattle(prologue: true);
            yield return UntilScene(GameFlow.BattleScene, 10);
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var intro = game.Intro;
            yield return Press(Key.J);
            Assert.That(intro.Torn, Is.False, "J does nothing before the knight grips the string.");
            intro.JumpForTests(IntroTimeline.LoopStart + 1.4);
            yield return null;
            Assert.That(intro.StoryBeat, Is.InRange(32f, 36f), "The waiting bar.");
            yield return Press(Key.J);
            Assert.That(intro.Torn);
            // With the tutorial next the song waits: the handover beat starts the tutorial, a bar before its first note.
            double handover = intro.HandoverDsp;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Prologue));
            float deadline = Time.realtimeSinceStartup + 18;
            while (game.State == PrototypeState.Prologue && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial), "The last downbeat hands over to the tutorial.");
            Assert.That(game.Tutorial.Playing);
            Assert.That(game.InTutorialBattle);
            Assert.That(game.SongClockOrigin, Is.EqualTo(handover + TutorialDirector.LeadBeats * game.Timeline.SecondsPerBeat).Within(1e-6));
            Assert.That(game.Tutorial.Letterbox.Visible, "The letterbox stays on into the tutorial.");
            Assert.That(game.View.ContentRect.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            Assert.That(GameFlow.PrologueSeen);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator EpilogueLetsTheKnightRoamToTheNoiseThenTheCutsceneReturnsToAWhiteMenu()
        {
            yield return LoadAndWait(GameFlow.MenuScene);
            GameFlow.StartBattle();
            yield return UntilScene(GameFlow.BattleScene, 10);
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var knight = Object.FindAnyObjectByType<BoardWorldPresenter>().PlayerTransform;
            var director = Object.FindAnyObjectByType<FinaleDirector>();
            game.JumpToEpilogueForTests();
            yield return new WaitForSecondsRealtime(.3f);
            float before = knight.localPosition.z;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D));
            yield return new WaitForSecondsRealtime(1.2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float moved = knight.localPosition.z - before;
            Assert.That(moved, Is.GreaterThan(1.2f), "Free movement is off the grid and continuous.");
            Assert.That(moved, Is.LessThan(2.6f * 1.25f), "Diagonals are not faster.");
            Assert.That(game.State, Is.EqualTo(PrototypeState.Epilogue));

            // The white world has no time limit: nothing happens until the knight reaches the noise.
            float deadline = Time.realtimeSinceStartup + 10;
            while (!director.InWhiteWorld && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(director.InWhiteWorld, "The burst gives way to the white world.");
            yield return new WaitForSecondsRealtime(2.5f);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameFlow.BattleScene));
            Assert.That(director.CutscenePlaying, Is.False);
            float far = director.NoiseVolume, loudest = far;
            var keys = new List<Key>();
            deadline = Time.realtimeSinceStartup + 20;
            while (!director.CutscenePlaying && Time.realtimeSinceStartup < deadline)
            {
                Vector3 gap = director.NoisePosition - knight.localPosition;
                keys.Clear();
                if (gap.x < -.1f) keys.Add(Key.A); else if (gap.x > .1f) keys.Add(Key.D);
                if (gap.z < -.1f) keys.Add(Key.S); else if (gap.z > .1f) keys.Add(Key.W);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys.ToArray()));
                loudest = Mathf.Max(loudest, director.NoiseVolume);
                yield return null;
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(director.CutscenePlaying, "Touching the noise starts the cutscene.");
            if (GameFlow.MusicVolume > 0) Assert.That(loudest, Is.GreaterThan(far * 3), "The noise grows louder as the knight comes near.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameFlow.BattleScene));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(director.CutscenePlaying, Is.False, "Space skips the cutscene.");
            yield return UntilScene(GameFlow.MenuScene, 10);
            Assert.That(GameFlow.Cleared);
            Assert.That(Object.FindAnyObjectByType<MainMenuView>().White);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
