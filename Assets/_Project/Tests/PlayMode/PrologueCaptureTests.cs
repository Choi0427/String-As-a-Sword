using System.Collections;
using System.IO;
using NUnit.Framework;
using StringAsASword.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    /// <summary>
    /// Stills of the prologue for checking its staging: the intro camera's picture (the board world, without the letterbox,
    /// the void and the J card, which are canvas layers) at chosen story beats, as PNGs named by beat and shot. Explicit:
    /// run it on its own, in batch mode too: -testFilter StringAsASword.Tests.PrologueCaptureTests -prologueCaptureDir &lt;folder&gt;
    /// </summary>
    public sealed class PrologueCaptureTests
    {
        private static readonly float[] Beats =
        {
            1, 5, 9.6f, 11.6f,              // rule: strings, a ply, up the strings to the king
            12.8f, 13.9f, 15.4f,             // offbeat: the knight's beat, the leap, off the edge
            16.25f, 16.8f, 18.6f, 22.4f, 26.5f, // bind: caught, dragged back, pinned, struggles
            30, 33,                          // the lit string, the waiting bar
            36.4f, 37.9f, 39.6f,             // the tear and the sword
            41, 43.5f, 46.5f,                // lanes, pieces lifted away
            49.5f, 52.5f, 55.5f              // face, settle, the handover
        };
        [UnityTest, Explicit("Writes about 25 PNGs of the prologue's shots.")]
        public IEnumerator CapturePrologueShots()
        {
            string dir = Arg("-prologueCaptureDir", Path.Combine(Application.temporaryCachePath, "PrologueCapture"));
            Directory.CreateDirectory(dir);
            Screen.SetResolution(1920, 1080, false);
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            GameFlow.StartBattle(prologue: true);
            float until = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != GameFlow.BattleScene && Time.realtimeSinceStartup < until) yield return null;
            yield return null; yield return null;
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var intro = game.Intro;
            Assert.That(intro.Playing);
            var camera = GameObject.Find("Prologue Camera").GetComponent<Camera>();
            // 16:9 whatever the screen (batch mode has a 4:3 one): the shot's own pose and vertical field of view, into a
            // 1920x1080 target. The settle into the battle camera (from beat 52) is approximate this way.
            var wide = new RenderTexture(1920, 1080, 24) { antiAliasing = 4 };
            foreach (float beat in Beats)
            {
                intro.Seek(beat);
                yield return null; yield return null;
                // No WaitForEndOfFrame in batch mode: the intro camera is rendered on demand.
                var own = camera.targetTexture; var projection = camera.projectionMatrix;
                float fov = 2 * Mathf.Atan(1 / projection.m11) * Mathf.Rad2Deg;
                camera.targetTexture = wide;
                camera.projectionMatrix = Matrix4x4.Perspective(fov, 16f / 9, camera.nearClipPlane, camera.farClipPlane);
                camera.Render();
                camera.targetTexture = own; camera.projectionMatrix = projection;
                var previous = RenderTexture.active; RenderTexture.active = wide;
                var image = new Texture2D(wide.width, wide.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, wide.width, wide.height), 0, 0); image.Apply();
                RenderTexture.active = previous;
                string shot = Presentation.IntroTimeline.Shot(beat);
                File.WriteAllBytes(Path.Combine(dir, $"prologue-{beat:00.00}-{shot}.png"), image.EncodeToPNG());
                Object.Destroy(image);
            }
            Debug.Log($"[PrologueCapture] {Beats.Length} stills at 1920x1080 in {dir}");
            wide.Release(); Object.Destroy(wide);
            game.PauseSession();
        }
        private static string Arg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
    }
}
