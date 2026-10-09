using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using StringAsASword.Board;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace StringAsASword.Core
{
    /// <summary>Opt-in local verification of the actual shipped executable, including stripped assets.</summary>
    public sealed class StandaloneVerification : MonoBehaviour
    {
        private static readonly List<string> errors = new List<string>();
        private PrototypeGame game;
        private Keyboard keyboard;
        private double deadline;
        private string output;
        private bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableIfRequested()
        {
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--verify-prototype") < 0) return;
            errors.Clear();
            Application.logMessageReceived += CaptureError;
            var probe = new GameObject("Standalone Verification").AddComponent<StandaloneVerification>();
            DontDestroyOnLoad(probe.gameObject);
            int index = Array.IndexOf(args, "--verification-output");
            probe.output = index >= 0 && index + 1 < args.Length ? args[index + 1] : Application.persistentDataPath;
            Directory.CreateDirectory(probe.output);
            probe.deadline = Time.realtimeSinceStartupAsDouble + 90;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }
        private static void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack);
        }
        private void Update()
        {
            if (!finished && Time.realtimeSinceStartupAsDouble > deadline) Finish("Verification timed out; game clock or input stopped.");
        }
        private IEnumerator Start()
        {
            yield return null;
            game = FindAnyObjectByType<PrototypeGame>();
            var board = FindAnyObjectByType<BoardWorldPresenter>();
            if (!Check(game != null && board != null && game.State == PrototypeState.Ready, "Startup did not reach Ready.")) yield break;
            if (!Check(board.TileCount == 64 && board.Texture.IsCreated(), "Board geometry or render target is missing.")) yield break;
            foreach (var renderer in board.PlayerTransform.GetComponentsInChildren<Renderer>())
                foreach (var material in renderer.sharedMaterials)
                    if (!Check(material != null && material.shader != null && material.shader.isSupported, "Player material is missing or unsupported.")) yield break;
            keyboard = InputSystem.AddDevice<Keyboard>();
            Queue(Key.Space, game.SongTime);
            yield return null; yield return null;
            Release();
            if (!Check(game.State == PrototypeState.CountIn, "Space did not start the session.")) yield break;
            yield return UntilTime(.05);
            if (!Check(game.State == PrototypeState.Playing && game.CurrentBeat >= 0 && game.Noise.Pickups.Count == 1, "Count-in did not advance into gameplay.")) yield break;
            if (game.Song.Music != null)
            {
                bool musicPlaying = false;
                foreach (var source in game.GetComponents<AudioSource>())
                    if (source.clip == game.Song.Music && source.isPlaying) musicPlaying = true;
                if (!Check(musicPlaying, "Scheduled music is not playing.")) yield break;
            }
            var target = game.Noise.Pickups[0].Cell;
            int beat = 1;
            while (!game.Session.Position.Equals(target))
            {
                var cell = game.Session.Position;
                var key = cell.X < target.X ? Key.D : cell.X > target.X ? Key.A : cell.Y < target.Y ? Key.W : Key.S;
                yield return Tap(key, beat++);
            }
            if (!Check(game.Noise.Collected == 1, "Movement landing did not collect noise.")) yield break;
            while (game.Session.Position.Y < 7) yield return Tap(Key.W, beat++);
            for (int i = 0; i < game.Stage.HealthSegments[0] + 1; i++) yield return Tap(Key.J, beat++);
            if (!Check(game.Health.CurrentHp == 0 && game.Health.ActiveSegment == 0 && game.Phases.PhaseId == 1, "Top-row damage did not stop at the HP floor.")) yield break;
            int hp = game.Health.TotalHp;
            yield return UntilTime(game.Timeline.TimeOf(beat));
            Queue(Key.K, game.Timeline.TimeOf(beat));
            yield return UntilTime(game.Timeline.TimeOf(beat) + .08);
            CaptureBoard(board.Texture);
            yield return UntilTime(game.Timeline.TimeOf(beat + 2) + .16);
            Release();
            if (!Check(game.Session.AcceptedGuards == 1 && game.Health.TotalHp == hp, "Guard changed HP or repeated while held.")) yield break;
            int session = game.SessionId;
            Queue(Key.R, game.SongTime);
            yield return null; yield return null;
            Release();
            if (!Check(game.SessionId == session + 1 && game.Noise.Collected == 0 && game.Noise.Pickups.Count == 0 && game.Phases.PhaseId == 1 && game.Health.TotalHp == Sum(game.Stage.HealthSegments), "Restart did not reset gameplay.")) yield break;
            game.PauseSession();
            yield return null;
            var button = game.GetComponentInChildren<UnityEngine.UI.Button>();
            if (!Check(button != null, "Start button is missing.")) yield break;
            button.onClick.Invoke();
            if (!Check(game.SessionId == session + 2 && game.State == PrototypeState.CountIn, "Start button did not restart the session.")) yield break;
            Finish(null);
        }
        private IEnumerator UntilTime(double target)
        {
            while (!finished && game.SongTime < target) yield return null;
        }
        private IEnumerator Tap(Key key, int beat)
        {
            yield return UntilTime(game.Timeline.TimeOf(beat));
            Queue(key, game.Timeline.TimeOf(beat));
            yield return UntilTime(game.Timeline.TimeOf(beat) + .05); Release();
            yield return UntilTime(game.Timeline.TimeOf(beat) + .16);
        }
        private void Queue(Key key, double songTime)
            => InputSystem.QueueStateEvent(keyboard, new KeyboardState(key), InputState.currentTime + songTime - game.SongTime);
        private void Release() => InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        private bool Check(bool condition, string message)
        {
            if (condition && errors.Count == 0) return true;
            Finish(condition ? "Runtime errors occurred during verification." : message); return false;
        }
        private void CaptureBoard(RenderTexture target)
        {
            // Read the board render target directly: hidden QA windows cannot capture the desktop backbuffer.
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(output, "standalone-board.png"), image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Destroy(image); }
        }
        private static int Sum(int[] values) { int sum = 0; foreach (int value in values) sum += value; return sum; }
        private void Finish(string failure)
        {
            if (finished) return;
            finished = true;
            if (failure != null) errors.Add(failure);
            bool passed = errors.Count == 0;
            var result = new VerificationResult { passed = passed, errors = errors.ToArray(), unityVersion = Application.unityVersion,
                executableData = Application.dataPath, checks = new[] { "startup", "64 tiles and shaders", "Space input", "count-in and beat clock", "music playback", "WASD landing collection", "J damage and HP floor", "K pose and hold", "R reset", "Start button" } };
            File.WriteAllText(Path.Combine(output, "standalone-verification.json"), JsonUtility.ToJson(result, true));
            Application.logMessageReceived -= CaptureError;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(passed ? 0 : 1);
        }
        [Serializable] private sealed class VerificationResult
        {
            public bool passed;
            public string unityVersion, executableData;
            public string[] checks, errors;
        }
    }
}
