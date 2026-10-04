using System.Collections;
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
        private bool originalBackground;
        private InputSettings.BackgroundBehavior originalInputBackground;

        [UnitySetUp] public IEnumerator SetUp()
        {
            originalBackground=Application.runInBackground;
            originalInputBackground=InputSystem.settings.backgroundBehavior;
            Application.runInBackground=true;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
            game=Object.FindFirstObjectByType<PrototypeGame>();
            keyboard=InputSystem.AddDevice<Keyboard>();
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if(game!=null) game.PauseSession();
            if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=originalInputBackground;
            Application.runInBackground=originalBackground;
            yield return null;
        }
        private void Queue(Key key,double songTime,bool down)
        {
            InputSystem.QueueStateEvent(keyboard,down?new KeyboardState(key):new KeyboardState(),InputState.currentTime+songTime-game.SongTime);
        }
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
            game.SendMessage("OnApplicationFocus",false);
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
            var glyphs=Object.FindObjectsByType<PrototypeGlyph>(FindObjectsSortMode.None);
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
            LogAssert.NoUnexpectedReceived();
        }
    }
}
