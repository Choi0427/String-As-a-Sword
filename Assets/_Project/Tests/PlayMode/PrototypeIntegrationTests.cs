using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    public sealed class PrototypeIntegrationTests
    {
        private PrototypeGame game;
        private Keyboard keyboard;
        private readonly List<Keyboard> externalKeyboards = new List<Keyboard>();
        private bool originalBackground;
        private InputSettings.BackgroundBehavior originalInputBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInput;
#endif

        [UnitySetUp] public IEnumerator SetUp()
        {
            // Desktop typing must not enter the synthetic keyboard's gameplay test.
            externalKeyboards.Clear();
            foreach (var device in InputSystem.devices)
                if (device is Keyboard existing && existing.enabled)
                { externalKeyboards.Add(existing); InputSystem.DisableDevice(existing); }
            originalBackground=Application.runInBackground;
            originalInputBackground=InputSystem.settings.backgroundBehavior;
            Application.runInBackground=true;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            originalEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
            game=Object.FindAnyObjectByType<PrototypeGame>();
            // The test runner changes Editor windows. Test focus loss explicitly, not accidentally.
            SetFocusPause(false);
            foreach (var pattern in game.Battle.Settings.Patterns) pattern.Enabled = false;
            keyboard=InputSystem.AddDevice<Keyboard>();
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if(game!=null) game.PauseSession();
            if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
            foreach (var existing in externalKeyboards) if (existing.added) InputSystem.EnableDevice(existing);
            externalKeyboards.Clear();
            InputSystem.settings.backgroundBehavior=originalInputBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInput;
#endif
            Application.runInBackground=originalBackground;
            yield return null;
        }
        private void Queue(Key key,double songTime,bool down)
        {
            InputSystem.QueueStateEvent(keyboard,down?new KeyboardState(key):new KeyboardState(),InputState.currentTime+songTime-game.SongTime);
        }
        private void SetFocusPause(bool value) => typeof(PrototypeGame).GetField("pauseOnFocusLoss",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(game, value);
        private IEnumerator UntilSongTime(double target)
        {
            double timeout=Time.realtimeSinceStartupAsDouble+8;
            while(game.SongTime<target && Time.realtimeSinceStartupAsDouble<timeout) yield return null;
            Assert.That(game.SongTime,Is.GreaterThanOrEqualTo(target),"Song clock stopped or timed out.");
        }
        [UnityTest] public IEnumerator RealKeyboardMovesFourDirectionsAndHoldDoesNotRepeat()
        {
            game.StartSession();
            yield return UntilSongTime(0);
            Queue(Key.W,0,true);
            yield return UntilSongTime(game.Timeline.TimeOf(1)+.20);
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(3,4)));
            Assert.That(game.Session.AcceptedMoves,Is.EqualTo(1),"Holding W must not create repeated actions.");
            Queue(Key.W,game.SongTime,false);
            yield return UntilSongTime(game.Timeline.TimeOf(2));
            Queue(Key.A,game.Timeline.TimeOf(2),true);
            yield return UntilSongTime(game.Timeline.TimeOf(2)+.05); Queue(Key.A,game.SongTime,false);
            yield return UntilSongTime(game.Timeline.TimeOf(3));
            Queue(Key.S,game.Timeline.TimeOf(3),true);
            yield return UntilSongTime(game.Timeline.TimeOf(3)+.05); Queue(Key.S,game.SongTime,false);
            yield return UntilSongTime(game.Timeline.TimeOf(4));
            Queue(Key.D,game.Timeline.TimeOf(4),true);
            yield return UntilSongTime(game.Timeline.TimeOf(4)+.05); Queue(Key.D,game.SongTime,false);
            yield return UntilSongTime(game.Timeline.TimeOf(4)+.15);
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(3,3)));
            Assert.That(game.Session.PerfectCount,Is.EqualTo(4));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SimultaneousKeysUseOneActionAndPauseRestartClearsSession()
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Assert.That(game.State,Is.EqualTo(PrototypeState.CountIn));
            yield return UntilSongTime(0);
            double at=InputState.currentTime-game.SongTime;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.D),at);
            yield return UntilSongTime(.05);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return UntilSongTime(.15);
            Assert.That(game.Session.AcceptedMoves,Is.EqualTo(1));
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(3,4)),"Equal timestamps use W before D.");
            SetFocusPause(true);
            game.SendMessage("OnApplicationFocus",false);
            SetFocusPause(false);
            Assert.That(game.State,Is.EqualTo(PrototypeState.Paused));
            double stopped=game.SongTime;
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(game.SongTime,Is.EqualTo(stopped));
            int oldSession=game.SessionId;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.R));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Assert.That(game.SessionId,Is.EqualTo(oldSession+1));
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(3,3)));
            Assert.That(game.Session.AcceptedMoves,Is.Zero); Assert.That(game.Session.MissCount,Is.Zero);
            Assert.That(game.State,Is.EqualTo(PrototypeState.CountIn));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MirroredNotesAndKnightHaveRenderableGeometry()
        {
            game.StartSession();
            yield return UntilSongTime(-1);
            Canvas.ForceUpdateCanvases();
            var glyphs=Object.FindObjectsByType<PrototypeGlyph>();
            int left=0,right=0;
            foreach(var glyph in glyphs)
            {
                Assert.That(glyph.GetComponent<CanvasRenderer>(),Is.Not.Null);
                var mesh=glyph.canvasRenderer.GetMesh();
                Assert.That(mesh,Is.Not.Null,glyph.name);
                Assert.That(mesh.vertexCount,Is.GreaterThan(0),glyph.name);
                if(glyph.name.StartsWith("Note_0_")) left++;
                if(glyph.name.StartsWith("Note_1_")) right++;
            }
            Assert.That(left,Is.GreaterThan(0)); Assert.That(right,Is.EqualTo(left));
            var board = Object.FindAnyObjectByType<BoardWorldPresenter>();
            var view = Object.FindAnyObjectByType<PrototypeView>();
            Assert.That(view.BoardMaterial, Is.Not.Null, "Builds must retain a serialized shader dependency.");
            Assert.That(view.BoardMaterial.shader.isSupported, Is.True);
            Assert.That(board.TileCount, Is.EqualTo(64));
            Assert.That(board.BoardCamera.orthographic, Is.True);
            Assert.That(board.BoardCamera.transform.eulerAngles.x, Is.EqualTo(60).Within(.01));
            Assert.That(board.Texture.IsCreated(), Is.True);
            Assert.That(board.PlayerTransform.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThan(4));
            Assert.That(board.PlayerTransform.localPosition.x, Is.EqualTo(-.5f).Within(.01));
            LogAssert.NoUnexpectedReceived();
        }
        private IEnumerator TapAtBeat(Key key, int beat)
        {
            yield return UntilSongTime(game.Timeline.TimeOf(beat));
            Queue(key,game.Timeline.TimeOf(beat),true);
            yield return UntilSongTime(game.Timeline.TimeOf(beat)+.05);
            Queue(key,game.SongTime,false);
            yield return UntilSongTime(game.Timeline.TimeOf(beat)+.16);
        }
        [UnityTest] public IEnumerator AttackGuardAndMovementRespectTiePriorityAndHoldEdges()
        {
            game.StartSession(); yield return UntilSongTime(0);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.K,Key.J),InputState.currentTime-game.SongTime);
            yield return UntilSongTime(.16);
            Assert.That(game.Session.AcceptedAttacks,Is.EqualTo(1));
            Assert.That(game.Session.AcceptedGuards,Is.Zero);
            Assert.That(game.Session.AcceptedMoves,Is.Zero);
            Assert.That(game.Health.TotalHp,Is.EqualTo(System.Linq.Enumerable.Sum(game.Stage.HealthSegments)),"Out-of-range swing is allowed but harmless.");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return UntilSongTime(game.Timeline.TimeOf(1));
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.K),InputState.currentTime+game.Timeline.TimeOf(1)-game.SongTime);
            yield return UntilSongTime(game.Timeline.TimeOf(3)+.16);
            Assert.That(game.Session.AcceptedGuards,Is.EqualTo(1),"Holding K must not repeat.");
            Assert.That(game.Session.AcceptedMoves,Is.Zero);
            Assert.That(game.Health.TotalHp,Is.EqualTo(System.Linq.Enumerable.Sum(game.Stage.HealthSegments)));
            Assert.That(game.Noise.Collected,Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator TopRowAttacksStopAtHealthFloorUntilNoiseGoalIsMet()
        {
            game.StartSession();
            for(int beat=0;beat<4;beat++) yield return TapAtBeat(Key.W,beat);
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(3,7)));
            int afterAttacks = 5 + game.Health.Maximum(0);
            for(int beat=4;beat<afterAttacks;beat++) yield return TapAtBeat(Key.J,beat);
            Assert.That(game.Health.CurrentHp,Is.Zero);
            Assert.That(game.Health.TotalHp,Is.EqualTo(System.Linq.Enumerable.Sum(game.Stage.HealthSegments) - game.Health.Maximum(0)));
            Assert.That(game.Phases.PhaseId,Is.EqualTo(1));
            yield return TapAtBeat(Key.K,afterAttacks);
            Assert.That(game.Health.TotalHp,Is.EqualTo(System.Linq.Enumerable.Sum(game.Stage.HealthSegments) - game.Health.Maximum(0)));
            Assert.That(game.Session.AcceptedGuards,Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator QuitButtonIsAvailableOnReadyPausedAndFinishedMenus()
        {
            var buttons = game.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            var start = System.Array.Find(buttons, b => b.name == "StartButton");
            var quit = System.Array.Find(buttons, b => b.name == "QuitButton");
            Assert.That(start, Is.Not.Null);
            Assert.That(quit, Is.Not.Null);
            Assert.That(quit.gameObject.activeInHierarchy, Is.True);
            start.onClick.Invoke();
            yield return null;
            Assert.That(quit.gameObject.activeInHierarchy, Is.False);
            game.PauseSession();
            yield return null;
            Assert.That(quit.gameObject.activeInHierarchy, Is.True);
            game.StartSession();
            game.Battle.Advance(game.Timeline.TimeOf(game.Song.NoteCount - 1) + game.Song.GoodWindow + .001);
            yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Finished));
            Assert.That(quit.gameObject.activeInHierarchy, Is.True);
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator LandingCollectsNoiseAndRestartClearsIt()
        {
            game.StartSession(); yield return UntilSongTime(.05);
            Assert.That(game.Noise.Pickups.Count,Is.EqualTo(1));
            var target = game.Noise.Pickups[0].Cell;
            int beat=1;
            while(!game.Session.Position.Equals(target))
            {
                Assert.That(beat, Is.LessThan(30), "Input-driven collection must finish in a bounded number of notes.");
                var cell=game.Session.Position;
                Key direction=cell.X<target.X?Key.D:cell.X>target.X?Key.A:cell.Y<target.Y?Key.W:Key.S;
                yield return TapAtBeat(direction,beat++);
            }
            Assert.That(game.Noise.Collected,Is.EqualTo(1));
            foreach(var pickup in game.Noise.Pickups) Assert.That(pickup.Cell,Is.Not.EqualTo(target));
            int oldSession=game.SessionId;
            game.StartSession();
            Assert.That(game.Noise.Collected,Is.Zero);
            Assert.That(game.Noise.Pickups,Is.Empty);
            Assert.That(game.Noise.SessionId,Is.EqualTo(oldSession+1));
            Assert.That(game.Phases.PhaseId,Is.EqualTo(1));
            Assert.That(game.Health.TotalHp,Is.EqualTo(System.Linq.Enumerable.Sum(game.Stage.HealthSegments)));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
