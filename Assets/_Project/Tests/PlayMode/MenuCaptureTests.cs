using System.Collections;
using System.IO;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    /// <summary>Renders the main menu to PNGs for comparison with the Figma frame. Run on demand:
    /// -testFilter StringAsASword.Tests.MenuCaptureTests -menuCaptureDir &lt;folder&gt;</summary>
    [Explicit]
    public sealed class MenuCaptureTests
    {
        [UnityTest] public IEnumerator CaptureDarkHoverAndWhiteMenus()
        {
            string dir = Arg("-menuCaptureDir", Path.Combine(Application.temporaryCachePath, "MenuCapture"));
            Directory.CreateDirectory(dir);
            bool hadCleared = GameFlow.Cleared;
            GameFlow.ResetCleared();
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenuView>();
            var canvas = menu.GetComponent<Canvas>();
            var texture = new RenderTexture(1920, 1080, 24);
            var camera = new GameObject("Capture Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 5;
            try
            {
                menu.Select(-1); yield return Capture(camera, texture, Path.Combine(dir, "menu-dark.png"));
                menu.Select(0); yield return Capture(camera, texture, Path.Combine(dir, "menu-dark-hover-start.png"));
                GameFlow.MarkCleared(); menu.ApplyTheme(); menu.Select(-1);
                yield return Capture(camera, texture, Path.Combine(dir, "menu-white.png"));
                menu.Select(0); yield return Capture(camera, texture, Path.Combine(dir, "menu-white-hover-start.png"));
                menu.Select(-1); menu.Activate(1); yield return Capture(camera, texture, Path.Combine(dir, "menu-white-settings.png"));
            }
            finally { if (hadCleared) GameFlow.MarkCleared(); else GameFlow.ResetCleared(); }
            Debug.Log("[MenuCapture] " + dir);
        }
        [UnityTest] public IEnumerator CaptureEpilogue()
        {
            string dir = Arg("-menuCaptureDir", Path.Combine(Application.temporaryCachePath, "MenuCapture"));
            Directory.CreateDirectory(dir);
            bool hadCleared = GameFlow.Cleared;
            yield return SceneManager.LoadSceneAsync(GameFlow.BattleScene);
            yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var canvas = Object.FindAnyObjectByType<PrototypeView>().GetComponent<Canvas>();
            var texture = new RenderTexture(1920, 1080, 24);
            var camera = new GameObject("Capture Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 5;
            try
            {
                game.JumpToEpilogueForTests();
                foreach (float at in new[] { .5f, 1f, 1.4f, 1.8f, 3.6f })
                {
                    while (Object.FindAnyObjectByType<FinaleDirector>().EpilogueAge < at) yield return null;
                    yield return Capture(camera, texture, Path.Combine(dir, $"epilogue-{at:0.0}s.png"));
                }
            }
            finally { if (hadCleared) GameFlow.MarkCleared(); else GameFlow.ResetCleared(); }
        }
        /// <summary>The battle screen with the boss picture, then the first summoned chess piece (the pawn row at note 6).</summary>
        [UnityTest] public IEnumerator CaptureBattleBossAndSummon()
        {
            string dir = Arg("-menuCaptureDir", Path.Combine(Application.temporaryCachePath, "MenuCapture"));
            Directory.CreateDirectory(dir);
            yield return SceneManager.LoadSceneAsync(GameFlow.BattleScene);
            yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var canvas = Object.FindAnyObjectByType<PrototypeView>().GetComponent<Canvas>();
            var board = Object.FindAnyObjectByType<BoardWorldPresenter>();
            var texture = new RenderTexture(1920, 1080, 24);
            var camera = new GameObject("Capture Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 5;
            game.StartSession();
            yield return Capture(camera, texture, Path.Combine(dir, "battle-boss.png"));
            float deadline = Time.realtimeSinceStartup + 30;
            while (board.LiveSummons == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(board.LiveSummons, Is.GreaterThan(0), "no chess piece was summoned within 30 s");
            float shown = Time.realtimeSinceStartup + .4f;
            while (Time.realtimeSinceStartup < shown) yield return null;
            yield return Capture(camera, texture, Path.Combine(dir, "battle-summon.png"));
            // The board's break steps: cracked, crumbling, light through the centre.
            for (int stage = 1; stage <= 3; stage++)
            {
                board.BreakOverride = stage;
                yield return Capture(camera, texture, Path.Combine(dir, $"battle-break-{stage}.png"));
            }
            // The centre coming apart from the king's fall to the end of the song, then bursting.
            foreach (float leak in new[] { .3f, .7f })
            {
                board.BreakOverride = 2 + leak;
                yield return Capture(camera, texture, Path.Combine(dir, $"battle-leak-{leak:0.0}.png"));
            }
            board.BreakOverride = 3;
            foreach (float burst in new[] { .25f, .5f, .8f })
            {
                board.SetBurst(burst);
                yield return Capture(camera, texture, Path.Combine(dir, $"battle-burst-{burst:0.00}.png"));
            }
            board.SetBurst(0);
            board.BreakOverride = -1;
            game.PauseSession();
        }
        /// <summary>Figma "Game Play" key guide, "Pause" (plain and hovered) and "Game Over" over the running battle.</summary>
        [UnityTest] public IEnumerator CaptureBattleScreens()
        {
            string dir = Arg("-menuCaptureDir", Path.Combine(Application.temporaryCachePath, "MenuCapture"));
            Directory.CreateDirectory(dir);
            yield return SceneManager.LoadSceneAsync(GameFlow.BattleScene);
            yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var canvas = Object.FindAnyObjectByType<PrototypeView>().GetComponent<Canvas>();
            var texture = new RenderTexture(1920, 1080, 24);
            var camera = new GameObject("Capture Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = texture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 5;
            game.StartSession();
            float playing = Time.realtimeSinceStartup + 4;
            while (game.State != PrototypeState.Playing && Time.realtimeSinceStartup < playing) yield return null;
            yield return Capture(camera, texture, Path.Combine(dir, "battle-keyguide.png"));
            var screens = game.View.Screens;
            game.PauseSession(); yield return null;
            yield return Capture(camera, texture, Path.Combine(dir, "battle-pause.png"));
            screens.Select(0); yield return Capture(camera, texture, Path.Combine(dir, "battle-pause-hover.png"));
            screens.Press(0); yield return null;
            game.Battle.Advance(game.Timeline.TimeOf(game.Song.NoteCount - 1) + game.Song.GoodWindow + .001);
            yield return null; yield return null;
            yield return Capture(camera, texture, Path.Combine(dir, "battle-gameover.png"));
            screens.Select(1); yield return Capture(camera, texture, Path.Combine(dir, "battle-gameover-hover.png"));
        }
        /// <summary>
        /// The boss crown in each HP state, for comparison with Art/UI/Boss/CrownStates/0-4.png: crown-0 .. crown-4 have
        /// that many segments spent (phases opened with the developer phase jump), crown-half has half of the first one gone.
        /// Each is cropped to the crown.
        /// </summary>
        [UnityTest] public IEnumerator CaptureCrownStates()
        {
            string dir = Arg("-menuCaptureDir", Path.Combine(Application.temporaryCachePath, "MenuCapture"));
            Directory.CreateDirectory(dir);
            bool devMode = GameFlow.DevMode; int startPhase = GameFlow.StartPhase;
            GameFlow.DevMode = true;
            try
            {
                for (int spent = 0; spent <= 5; spent++)
                {
                    // 0-3: open phase spent + 1 (the earlier segments are empty). 4: empty the last one too. 5: half of the first.
                    GameFlow.StartPhase = spent >= 4 ? (spent == 4 ? 4 : 1) : spent + 1;
                    yield return SceneManager.LoadSceneAsync(GameFlow.BattleScene);
                    yield return null;
                    var game = Object.FindAnyObjectByType<PrototypeGame>();
                    var view = Object.FindAnyObjectByType<PrototypeView>();
                    foreach (var pattern in game.Battle.Settings.Patterns) pattern.Enabled = false;
                    var canvas = view.GetComponent<Canvas>();
                    var texture = new RenderTexture(1920, 1080, 24);
                    var camera = new GameObject("Capture Camera").AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.targetTexture = texture;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 5;
                    game.StartSession();
                    yield return null;
                    if (spent == 4) game.Health.ApplyDamage(game.Health.CurrentHp, game.Health.ActiveSegment);
                    if (spent == 5) game.Health.ApplyDamage(game.Health.Maximum(0) / 2, 0);
                    // Let the carve and the glow settle, and the hit flash fade.
                    yield return new WaitForSecondsRealtime(1.2f);
                    string name = spent == 5 ? "crown-half" : "crown-" + spent;
                    yield return Capture(camera, texture, Path.Combine(dir, name + "-screen.png"));
                    CropTo(Path.Combine(dir, name + "-screen.png"), (RectTransform)view.BossCrown.transform, camera, texture, Path.Combine(dir, name + ".png"));
                    Debug.Log($"[CrownCapture] {name}: phase {game.Phases.PhaseId}, blades {view.BossCrown.Blade(0):0.00} {view.BossCrown.Blade(1):0.00} {view.BossCrown.Blade(2):0.00} {view.BossCrown.Blade(3):0.00}");
                    game.PauseSession();
                    Object.Destroy(camera.gameObject); texture.Release();
                }
            }
            finally { GameFlow.StartPhase = startPhase; GameFlow.DevMode = devMode; }
            Debug.Log("[CrownCapture] " + dir);
        }
        // Cuts the crown's rectangle (with a margin) out of a saved screen capture.
        private static void CropTo(string screenPath, RectTransform rect, Camera camera, RenderTexture texture, string path)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 min = camera.WorldToScreenPoint(corners[0]), max = camera.WorldToScreenPoint(corners[2]);
            float margin = (max.x - min.x) * .06f;
            int x0 = Mathf.Clamp((int)(min.x - margin), 0, texture.width - 1), y0 = Mathf.Clamp((int)(min.y - margin), 0, texture.height - 1);
            int x1 = Mathf.Clamp((int)(max.x + margin), x0 + 1, texture.width), y1 = Mathf.Clamp((int)(max.y + margin), y0 + 1, texture.height);
            var screen = new Texture2D(2, 2); screen.LoadImage(File.ReadAllBytes(screenPath));
            var crop = new Texture2D(x1 - x0, y1 - y0, TextureFormat.RGB24, false);
            crop.SetPixels(screen.GetPixels(x0, y0, x1 - x0, y1 - y0)); crop.Apply();
            File.WriteAllBytes(path, crop.EncodeToPNG());
            Object.Destroy(screen); Object.Destroy(crop);
        }
        private static IEnumerator Capture(Camera camera, RenderTexture texture, string path)
        {
            // No WaitForEndOfFrame: it never fires in batch mode. The camera renders the canvas on demand.
            yield return null; Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = texture;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.Destroy(image);
        }
        private static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
