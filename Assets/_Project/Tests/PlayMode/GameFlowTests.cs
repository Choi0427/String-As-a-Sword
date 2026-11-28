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
        private bool hadCleared, originalBackground;
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

        [UnityTest] public IEnumerator MenuFollowsTheFigmaLayoutAndHoverAndTurnsWhiteAfterAClear()
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
            Assert.That(art.sprite.name, Is.EqualTo("MainMenu_Logo_BG_White"), "Figma 'Main Menu_White' replaces the dark art.");
            Assert.That(art.material.GetFloat("_Invert"), Is.Zero, "The hand-made white art is not inverted again.");
            Assert.That(menu.LabelColorHex(1), Is.EqualTo("262626"));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SettingsFollowTheFigmaCardAndStepTheSavedValues()
        {
            float volume = GameFlow.MusicVolume; int input = GameFlow.InputOffsetMs, visual = GameFlow.VisualOffsetMs;
            var difficulty = GameFlow.Difficulty; bool dev = GameFlow.DevMode;
            try
            {
                GameFlow.MusicVolume = .8f; GameFlow.InputOffsetMs = 0; GameFlow.VisualOffsetMs = 0;
                GameFlow.Difficulty = GameDifficulty.Hard; GameFlow.DevMode = false;
                yield return LoadAndWait(GameFlow.MenuScene);
                var menu = Object.FindAnyObjectByType<MainMenuView>();
                menu.Activate(MainMenuView.SettingsItem);
                var settings = menu.Settings;
                Assert.That(settings.IsOpen);
                var card = (RectTransform)menu.transform.Find("Settings/Frame/Card");
                Assert.That(card.rect.size.x, Is.EqualTo(1266.17f).Within(.01f));
                Assert.That(card.rect.size.y, Is.EqualTo(950.64f).Within(.01f));
                // Only the four Figma rows: no tutorial, close, sync-on-screen, HP style or developer buttons on the card.
                foreach (string gone in new[] { "Tutorial", "Close", "화면 싱크", "체력 표시", "개발자 모드" })
                    Assert.That(card.Find(gone), Is.Null, gone);
                Assert.That(menu.transform.Find("Settings/Frame/Dev").gameObject.activeSelf, Is.False);
                TMPro.TMP_Text Value(string row) => card.Find(row + " Value").GetComponent<TMPro.TMP_Text>();
                Assert.That(Value("음량").text, Is.EqualTo("80%"));
                var easyNote = card.Find("Note 쉬움").GetComponent<UnityEngine.UI.Image>();
                var hardNote = card.Find("Note 어려움").GetComponent<UnityEngine.UI.Image>();
                Assert.That(easyNote.sprite.name, Is.EqualTo("Settings_DifficultyEasyNote"));
                Assert.That(hardNote.sprite.name, Is.EqualTo("Settings_DifficultyHardNote"));
                Assert.That(easyNote.gameObject.activeSelf, Is.False);
                Assert.That(hardNote.gameObject.activeSelf, Is.True);

                settings.Step(SettingsPanel.VolumeRow, 1);
                Assert.That(GameFlow.MusicVolume, Is.EqualTo(.85f).Within(.001f));
                settings.Step(SettingsPanel.InputRow, -1);
                Assert.That(GameFlow.InputOffsetMs, Is.EqualTo(-SettingsPanel.OffsetStep));
                Assert.That(Value("입력 판정 보정").text, Is.EqualTo("- 1 ms"));
                settings.Step(SettingsPanel.MusicRow, 1);
                Assert.That(SettingsPanel.MusicSyncMs, Is.EqualTo(SettingsPanel.OffsetStep), "늦춤 plays the music later...");
                Assert.That(GameFlow.VisualOffsetMs, Is.EqualTo(-SettingsPanel.OffsetStep), "...so the notes are drawn earlier.");
                Assert.That(Value("음악 싱크 보정").text, Is.EqualTo("+ 1 ms"));
                settings.Select(SettingsPanel.DifficultyRow);
                yield return Press(Key.A);
                Assert.That(GameFlow.Difficulty, Is.EqualTo(GameDifficulty.Easy));
                Assert.That(easyNote.gameObject.activeSelf, Is.True);
                Assert.That(hardNote.gameObject.activeSelf, Is.False);

                card.Find("Option 어려움").GetComponent<MenuButton>().OnPointerClick(null);
                Assert.That(GameFlow.Difficulty, Is.EqualTo(GameDifficulty.Hard));
                Assert.That(easyNote.gameObject.activeSelf, Is.False);
                Assert.That(hardNote.gameObject.activeSelf, Is.True);
                settings.Choose("개발자 모드", 1);
                Assert.That(menu.transform.Find("Settings/Frame/Dev").gameObject.activeSelf, "Shift+D's switch shows the developer strip.");
                settings.Close();
                Assert.That(settings.IsOpen, Is.False);
            }
            finally
            {
                GameFlow.MusicVolume = volume; GameFlow.InputOffsetMs = input; GameFlow.VisualOffsetMs = visual;
                GameFlow.Difficulty = difficulty; GameFlow.DevMode = dev;
            }
        }
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }
        [UnityTest] public IEnumerator StartOpensWithThePrologueThenTheTutorialAndEscSkipsEachIntoTheSong()
        {
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
            Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial), "Skipping the prologue goes on to the tutorial.");
            Assert.That(game.Intro.Playing, Is.False);
            Assert.That(game.Tutorial.Playing);
            Assert.That(game.InTutorialBattle);
            Assert.That(game.View.ContentRect.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1));
            yield return Press(Key.Escape);
            Assert.That(game.State, Is.EqualTo(PrototypeState.Playing), "Skipping the tutorial starts the song at once, with no count-in.");
            Assert.That(game.Tutorial.Playing, Is.False);
            Assert.That(game.InTutorialBattle, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator EveryStartGoesThroughTheTutorialAndSettingsReplayIt()
        {
            // Twice from the menu: the tutorial follows the prologue every time, not only the first, and Esc skips both again.
            PrototypeGame game = null;
            for (int run = 0; run < 2; run++)
            {
                yield return LoadAndWait(GameFlow.MenuScene);
                Object.FindAnyObjectByType<MainMenuView>().Activate(MainMenuView.StartItem);
                yield return UntilScene(GameFlow.BattleScene, 10);
                game = Object.FindAnyObjectByType<PrototypeGame>();
                Assert.That(game.State, Is.EqualTo(PrototypeState.Prologue), "시작 always opens with the prologue.");
                Assert.That(game.TutorialFollowsPrologue, $"Run {run + 1}: the tutorial follows it.");
                yield return Press(Key.Escape);
                Assert.That(game.State, Is.EqualTo(PrototypeState.Tutorial), $"Run {run + 1}: Esc skips to the tutorial.");
                yield return Press(Key.Escape);
                Assert.That(game.State, Is.EqualTo(PrototypeState.Playing), $"Run {run + 1}: Esc skips the tutorial too.");
            }
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
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ANewRunAfterTheEndingPutsTheBoardBackWithoutACountIn()
        {
            yield return LoadAndWait(GameFlow.MenuScene);
            GameFlow.StartBattle();
            yield return UntilScene(GameFlow.BattleScene, 10);
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var board = Object.FindAnyObjectByType<BoardWorldPresenter>();
            var director = Object.FindAnyObjectByType<FinaleDirector>();
            game.JumpToEpilogueForTests();
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(board.FreeRoam, "The white world: the knight roams.");
            game.StartSession();
            yield return null; yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Playing), "Straight into the song.");
            Assert.That(board.FreeRoam, Is.False, "The board is back for the new run.");
            Assert.That(director.EpilogueAge, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator EpilogueLetsTheKnightRoamToTheNoiseThenTheEndingPictureReturnsToAWhiteMenu()
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
            Assert.That(director.EndingPlaying, Is.False);
            float far = director.NoiseVolume, loudest = far;
            var keys = new List<Key>();
            deadline = Time.realtimeSinceStartup + 20;
            while (!director.EndingPlaying && Time.realtimeSinceStartup < deadline)
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
            Assert.That(director.EndingPlaying, "Touching the noise brings in the ending picture.");
            if (GameFlow.MusicVolume > 0) Assert.That(loudest, Is.GreaterThan(far * 3), "The noise grows louder as the knight comes near.");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(GameFlow.BattleScene));
            Assert.That(director.EndingShown, Is.False, "First the noise fades away and the screen goes white.");
            yield return new WaitForSecondsRealtime(FinaleDirector.EndingStart - .5f);
            Assert.That(director.EndingShown, Is.False, "Plain white before the picture.");
            yield return new WaitForSecondsRealtime(1);
            Assert.That(director.EndingShown, "Then the picture cuts in.");
            Assert.That(director.WhiteWorldVisible, Is.False, "The knight is gone behind the white before the picture.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(director.EndingPlaying, Is.False, "Space skips the ending.");
            for (int frame = 0; frame < 10; frame++) { yield return null; Assert.That(director.WhiteWorldVisible, Is.False, "Nothing of the knight shows through while the picture goes white."); }
            yield return UntilScene(GameFlow.MenuScene, 10);
            Assert.That(GameFlow.Cleared);
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            Assert.That(menu.White);
            Assert.That(menu.ArrivalCover, Is.GreaterThan(.5f), "The menu opens under the ending's white.");
            yield return new WaitForSecondsRealtime(MainMenuView.ArrivalHoldSeconds + MainMenuView.ArrivalFadeSeconds + .3f);
            Assert.That(menu.ArrivalCover, Is.Zero, "The white clears into the menu.");
            Assert.That(GameFlow.ArrivingFromEnding, Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
