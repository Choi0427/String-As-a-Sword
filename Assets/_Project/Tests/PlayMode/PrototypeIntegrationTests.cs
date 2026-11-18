using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Combat;
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
        // The song asset may hold an authored chart, so the first note is not necessarily at song time 0.
        private double FirstNote => game.Timeline.TimeOf(0);
        private IEnumerator UntilSongTime(double target, double timeoutSeconds = 8)
        {
            double timeout=Time.realtimeSinceStartupAsDouble+timeoutSeconds;
            while(game.SongTime<target && Time.realtimeSinceStartupAsDouble<timeout) yield return null;
            Assert.That(game.SongTime,Is.GreaterThanOrEqualTo(target),"Song clock stopped or timed out.");
        }
        [UnityTest] public IEnumerator RealKeyboardMovesFourDirectionsAndHoldDoesNotRepeat()
        {
            game.StartSession();
            yield return UntilSongTime(FirstNote);
            Queue(Key.W,FirstNote,true);
            yield return UntilSongTime(game.Timeline.TimeOf(1)+.20);
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(6,1)));
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
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(6,0)));
            Assert.That(game.Session.PerfectCount,Is.EqualTo(4));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator SimultaneousKeysUseOneActionAndPauseRestartClearsSession()
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Assert.That(game.State,Is.EqualTo(PrototypeState.CountIn));
            yield return UntilSongTime(FirstNote);
            double at=InputState.currentTime+FirstNote-game.SongTime;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.D),at);
            yield return UntilSongTime(FirstNote+.05);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return UntilSongTime(FirstNote+.15);
            Assert.That(game.Session.AcceptedMoves,Is.EqualTo(1));
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(6,1)),"Equal timestamps use W before D.");
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
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(6,0)));
            Assert.That(game.Session.AcceptedMoves,Is.Zero); Assert.That(game.Session.MissCount,Is.Zero);
            Assert.That(game.State,Is.EqualTo(PrototypeState.CountIn));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator MirroredNotesAndKnightHaveRenderableGeometry()
        {
            game.StartSession();
            yield return UntilSongTime(FirstNote-1);
            Canvas.ForceUpdateCanvases();
            // Procedural glyphs or a delivered note sprite: either way each note must produce UI geometry.
            var glyphs=Object.FindObjectsByType<UnityEngine.UI.Graphic>();
            int left=0,right=0;
            foreach(var glyph in glyphs)
            {
                if(!glyph.name.StartsWith("Note_")) continue;
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
            // The stage's 60° orthographic view unless the ArtSet sets its own look-down angle or perspective.
            Assert.That(board.BoardCamera.orthographic, Is.EqualTo(view.Art.cameraFieldOfView <= 0));
            Assert.That(board.BoardCamera.transform.eulerAngles.x, Is.EqualTo(view.Art.cameraElevation > 0 ? view.Art.cameraElevation : 60).Within(.01));
            Assert.That(board.Texture.IsCreated(), Is.True);
            // Renderer, not MeshRenderer: a delivered skinned knight has none of the procedural parts.
            Assert.That(board.PlayerTransform.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
            Assert.That(board.PlayerTransform.localPosition.x, Is.EqualTo(2.5f).Within(.01), "The knight starts on g1.");
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
        // Impact is note 2 and publishes when note 0 closes. Settings are this session's copy, not the stage asset.
        private const int ImpactNote = 2;
        private static PatternDefinition RequiredGuard() => new PatternDefinition { Name = "Guard Fixture", Kind = PatternKind.FullBoard,
            Response = AttackResponse.GuardOnly, FirstImpact = ImpactNote + 1, TelegraphNotes = ImpactNote, RepeatNotes = 1000 };
        private static PatternDefinition RequiredDodge() => new PatternDefinition { Name = "Dodge Fixture", Kind = PatternKind.Row, Coordinate = 0, // row 1, where the knight starts
            Response = AttackResponse.DodgeOnly, FirstImpact = ImpactNote + 1, TelegraphNotes = ImpactNote, RepeatNotes = 1000 };
        private IEnumerator StartWith(PatternDefinition pattern, AttackResponse response)
        {
            game.Battle.Settings.Schedule.Enabled = false;
            game.Battle.Settings.BossChartText = null; // the stage's boss chart would replace the fixture pattern
            game.Battle.Settings.Patterns = new[] { pattern };
            game.StartSession();
            game.Noise.BeginPhase(1, 0, 0); // no noise: an attack is held back while it would cut off the way to a pickup
            yield return UntilSongTime(game.Timeline.TimeOf(0)+.16);
            yield return null;
            Assert.That(game.Battle.Patterns.Active.Count, Is.EqualTo(1), "The fixture attack must be published before anyone responds.");
            Assert.That(game.Battle.Patterns.Active[0].Response, Is.EqualTo(response));
        }
        private IEnumerator UntilImpactResolved()
        {
            yield return UntilSongTime(game.Timeline.TimeOf(ImpactNote)+.16);
            yield return null;
            Assert.That(game.Battle.LastClosedNote, Is.GreaterThanOrEqualTo(ImpactNote));
            Assert.That(game.Battle.Patterns.Active, Is.Empty);
        }
        private int EventCount(string type)
        {
            int count = 0;
            foreach (var entry in game.Battle.Events) if (entry.type == type) count++;
            return count;
        }
        private string ActiveOutcome => Object.FindAnyObjectByType<PrototypeView>().ActiveOutcome;
        [UnityTest] public IEnumerator GuardOnlyAttackIsBlockedByARealGuardKey()
        {
            yield return StartWith(RequiredGuard(), AttackResponse.GuardOnly);
            yield return TapAtBeat(Key.K, ImpactNote);
            yield return UntilImpactResolved();
            Assert.That(game.Session.AcceptedGuards, Is.EqualTo(1));
            Assert.That(game.Battle.PlayerHp, Is.EqualTo(game.Stage.PlayerMaxHp));
            Assert.That(EventCount("guard-success"), Is.EqualTo(1));
            Assert.That(EventCount("player-hit"), Is.Zero);
            Assert.That(ActiveOutcome, Is.EqualTo("BLOCKED"));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator GuardOnlyAttackHitsWithoutAGuard()
        {
            yield return StartWith(RequiredGuard(), AttackResponse.GuardOnly);
            yield return UntilImpactResolved();
            Assert.That(game.Session.AcceptedGuards, Is.Zero);
            Assert.That(game.Battle.PlayerHp, Is.EqualTo(game.Stage.PlayerMaxHp - 1));
            Assert.That(EventCount("player-hit"), Is.EqualTo(1));
            Assert.That(EventCount("guard-success"), Is.Zero);
            Assert.That(ActiveOutcome, Is.EqualTo("HIT"));
            yield return null; // the HUD redraws after the hit
            var view = Object.FindAnyObjectByType<PrototypeView>();
            Assert.That(view.HpIconCount, Is.EqualTo(game.Stage.PlayerMaxHp), "One HP icon per max HP under the knight.");
            Assert.That(view.HpIconsOn, Is.EqualTo(game.Stage.PlayerMaxHp - 1), "The hit turns one HP icon off.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator DodgeOnlyAttackBreaksARealGuardKey()
        {
            yield return StartWith(RequiredDodge(), AttackResponse.DodgeOnly);
            yield return TapAtBeat(Key.K, ImpactNote);
            yield return UntilImpactResolved();
            Assert.That(game.Session.AcceptedGuards, Is.EqualTo(1));
            Assert.That(game.Battle.PlayerHp, Is.EqualTo(game.Stage.PlayerMaxHp - 1));
            Assert.That(EventCount("guard-broken"), Is.EqualTo(1));
            Assert.That(EventCount("player-hit"), Is.EqualTo(1));
            Assert.That(EventCount("guard-success"), Is.Zero);
            Assert.That(ActiveOutcome, Is.EqualTo("GUARD BROKEN"));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator DodgeOnlyAttackMissesAfterARealMoveKey()
        {
            yield return StartWith(RequiredDodge(), AttackResponse.DodgeOnly);
            yield return TapAtBeat(Key.W, 1);
            Assert.That(game.Session.Position, Is.EqualTo(new GridPosition(6,1)));
            yield return UntilImpactResolved();
            Assert.That(game.Battle.PlayerHp, Is.EqualTo(game.Stage.PlayerMaxHp));
            Assert.That(EventCount("player-hit"), Is.Zero);
            Assert.That(EventCount("guard-broken"), Is.Zero);
            Assert.That(ActiveOutcome, Is.Empty, "Nothing happened to the player, so nothing flashes.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator OutcomeFlashExpiresAndRestartClearsIt()
        {
            yield return StartWith(RequiredGuard(), AttackResponse.GuardOnly);
            yield return UntilImpactResolved();
            Assert.That(ActiveOutcome, Is.EqualTo("HIT"));
            game.StartSession();
            Assert.That(ActiveOutcome, Is.Empty, "A restart must not carry the last run's outcome.");
            game.Noise.BeginPhase(1, 0, 0);
            yield return UntilSongTime(game.Timeline.TimeOf(0)+.16);
            yield return UntilImpactResolved();
            Assert.That(ActiveOutcome, Is.EqualTo("HIT"));
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(ActiveOutcome, Is.Empty, "The flash is short-lived.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator AttackGuardAndMovementRespectTiePriorityAndHoldEdges()
        {
            game.StartSession(); yield return UntilSongTime(FirstNote);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.K,Key.J),InputState.currentTime+FirstNote-game.SongTime);
            yield return UntilSongTime(FirstNote+.16);
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
        [UnityTest] public IEnumerator NoiseChargesTheStringAndOnlyResonanceHurtsTheBoss()
        {
            // No boss attacks, so the stay on row 8 cannot end the run. The noise is pinned one step above the knight.
            game.Battle.Settings.Schedule.Enabled = false;
            game.Battle.Settings.BossChartText = null;
            game.Battle.Settings.Patterns = new PatternDefinition[0];
            var noiseCell = new GridPosition(6, 1);
            game.Noise.CandidateAllowed = (cell, fallback) => cell.Equals(noiseCell);
            game.StartSession();
            var hud = Object.FindAnyObjectByType<PrototypeView>().ResonanceHud;
            int full = System.Linq.Enumerable.Sum(game.Stage.HealthSegments);
            yield return TapAtBeat(Key.W,0); // g1 to g2: lands on the noise
            Assert.That(game.Battle.Resonance.Charged,Is.True);
            Assert.That(hud.ShownCharged,Is.True,"The lanes show the charge.");
            for(int beat=1;beat<6;beat++) yield return TapAtBeat(Key.W,beat); // g2 to g7
            Assert.That(game.Noise.Pickups,Is.Empty,"No noise spawns while the string is charged.");
            noiseCell = new GridPosition(0, 0); // the next one appears far away, after the resonance
            yield return TapAtBeat(Key.W,6); // g8
            Assert.That(game.Session.Position,Is.EqualTo(new GridPosition(6,7)));
            Assert.That(game.Battle.Resonance.Charged,Is.True,"The charge waits for the first attack.");
            yield return TapAtBeat(Key.J,7); // ignition
            Assert.That(game.Battle.Resonance.EndNote,Is.EqualTo(14));
            Assert.That(hud.ShownRemaining,Is.EqualTo(7));
            Assert.That(game.Health.TotalHp,Is.LessThan(full),"The igniting attack already hurts.");
            for(int beat=8;beat<15;beat++) yield return TapAtBeat(Key.J,beat);
            Assert.That(hud.ShownRemaining,Is.Zero);
            int afterResonance = game.Health.TotalHp;
            Assert.That(full - afterResonance,Is.InRange(8,18),"Eight notes at 1 or 2 each, plus a cadence of 2 at most.");
            yield return TapAtBeat(Key.J,15); // nothing left on the string
            Assert.That(game.Health.TotalHp,Is.EqualTo(afterResonance),"Without resonance the blade passes through.");
            Assert.That(game.Battle.Records[15].reason,Is.EqualTo("Ghost"));
            Assert.That(game.Noise.Pickups.Count,Is.EqualTo(1),"Noise returns once the resonance is over.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator RealKeyboardHoldReleasedOnTheTailIsPerfectAndDrawsAString()
        {
            int hold = 0;
            while (hold < game.Song.NoteCount && !game.Session.IsHold(hold)) hold++;
            if (hold == game.Song.NoteCount) Assert.Ignore("The song asset has no hold notes.");
            double head = game.Timeline.TimeOf(hold), end = game.Session.HoldEnd(hold);
            game.StartSession();
            yield return UntilSongTime(head - .3, head + 15);
            Queue(Key.K, head, true);
            yield return UntilSongTime((head + end) * .5);
            Assert.That(game.Session.ActiveHold.HasValue, "A pressed hold head starts the hold.");
            Canvas.ForceUpdateCanvases();
            int strings = 0;
            foreach (var g in Object.FindObjectsByType<UnityEngine.UI.Image>())
                if (g.name.StartsWith("HoldString_") && g.gameObject.activeInHierarchy) strings++;
            Assert.That(strings, Is.GreaterThanOrEqualTo(2), "The held string is drawn on both lanes.");
            yield return UntilSongTime(end);
            Queue(Key.K, end, false);
            yield return UntilSongTime(end + .2);
            Assert.That(game.Session.HoldPerfectCount, Is.EqualTo(1));
            Assert.That(game.Battle.Events.Exists(e => e.type == "hold-perfect" && e.noteIndex == hold));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator ReadyCardThenPauseAndGameOverScreens()
        {
            var screens = game.GetComponentInChildren<BattleScreens>(true);
            var buttons = game.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            var quit = System.Array.Find(buttons, b => b.name == "QuitButton");
            Assert.That(quit.gameObject.activeInHierarchy, Is.True, "Played without the menu, the battle opens on the ready card.");
            Assert.That(screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.None));
            System.Array.Find(buttons, b => b.name == "StartButton").onClick.Invoke();
            yield return null;
            Assert.That(quit.gameObject.activeInHierarchy, Is.False);
            Assert.That(screens.KeyGuideVisible, Is.EqualTo(game.View.Art.keyGuide != null), "The key guide stays under the board in play.");

            // Esc pauses, Esc again resumes a count-in before the paused place; settings close before a resume.
            yield return UntilSongTime(FirstNote + .5);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Paused));
            Assert.That(screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.Pause));
            double paused = game.SongTime;
            screens.Press(1);
            Assert.That(screens.SettingsOpen);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(screens.SettingsOpen, Is.False);
            Assert.That(game.State, Is.EqualTo(PrototypeState.Paused), "The first Esc only closed the settings.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(screens.Selected, Is.EqualTo(2), "S moves down from 설정, the item last pressed.");
            screens.Press(0);
            yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Playing));
            Assert.That(screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.None));
            Assert.That(game.ResumeAt, Is.EqualTo(paused).Within(1e-6));
            Assert.That(game.SongTime, Is.LessThan(paused - game.Timeline.SecondsPerBeat), "The song rewinds before the paused place.");

            // Running out of notes ends on the game-over screen; 첫 수부터 restarts.
            game.Battle.Advance(game.Timeline.TimeOf(game.Song.NoteCount - 1) + game.Song.GoodWindow + .001);
            yield return null; yield return null;
            Assert.That(game.State, Is.EqualTo(PrototypeState.Finished));
            Assert.That(screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.GameOver));
            Assert.That(screens.GameOverTitle, Is.EqualTo("FALLEN"));
            int session = game.SessionId;
            screens.Press(0);
            yield return null;
            Assert.That(game.SessionId, Is.EqualTo(session + 1));
            Assert.That(game.State, Is.EqualTo(PrototypeState.CountIn));
            Assert.That(screens.Showing, Is.EqualTo(BattleScreens.ScreenKind.None));
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator LandingCollectsNoiseAndRestartClearsIt()
        {
            game.StartSession(); yield return UntilSongTime(FirstNote+.05);
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
