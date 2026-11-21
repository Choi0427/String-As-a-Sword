#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    /// <summary>The prologue's "whole board" background (intro review A5-B), filled in memory with tinted stand-ins.</summary>
    public sealed class PrologueBackgroundTests
    {
        private const string ArtSetPath = "Assets/_Project/Art/ArtSet.asset";
        private ArtSet art;
        private string original;
        private Sprite back, front;

        [SetUp] public void SetUp()
        {
            art = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtSet>(ArtSetPath);
            Assert.That(art, Is.Not.Null);
            original = JsonUtility.ToJson(art);
            back = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
            front = Sprite.Create(Texture2D.grayTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            if (game != null) game.PauseSession();
            JsonUtility.FromJsonOverwrite(original, art);
            foreach (var item in new Object[] { back, front }) if (item != null) Object.Destroy(item);
            yield return null;
        }
        private static IEnumerator OpenThePrologue()
        {
            yield return SceneManager.LoadSceneAsync(GameFlow.MenuScene);
            yield return null;
            GameFlow.StartBattle(prologue: true);
            float until = Time.realtimeSinceStartup + 10;
            while (SceneManager.GetActiveScene().name != GameFlow.BattleScene && Time.realtimeSinceStartup < until) yield return null;
            yield return null;
        }
        private static float Alpha(string name) => GameObject.Find(name).GetComponent<UnityEngine.UI.Image>().color.a;

        [UnityTest] public IEnumerator TheWholeBoardShowsUntilTheTearThenGivesWayFrontLayerFirst()
        {
            art.prologueBackgroundLayers = new[]
            {
                new BackgroundLayer { sprite = back },
                new BackgroundLayer { sprite = front, sway = new Vector2(10, 6), period = 5 },
            };
            yield return OpenThePrologue();
            var intro = Object.FindAnyObjectByType<PrototypeGame>().Intro;
            Assert.That(GameObject.Find("Whole Board"), Is.Not.Null);
            Assert.That(GameObject.Find("Whole Board Layer 1").GetComponent<BackgroundSway>(), Is.Not.Null, "It floats like the battle's layers.");
            intro.Seek(30); yield return null;
            Assert.That(intro.WholeBoardAlpha, Is.EqualTo(new[] { 1f, 1f }), "The whole board, before the tear.");
            intro.Seek((float)IntroTimeline.Tear + .6f); yield return null;
            var fading = intro.WholeBoardAlpha;
            Assert.That(fading[1], Is.LessThan(fading[0]), "The front layer goes first, the opaque back last.");
            Assert.That(fading[0], Is.InRange(.01f, .99f));
            intro.Seek((float)IntroTimeline.Tear + 4); yield return null;
            Assert.That(intro.WholeBoardAlpha, Is.EqualTo(new[] { 0f, 0f }), "After the tear only the battle's broken world is left.");
            Assert.That(GameObject.Find("Background Layer 0"), Is.Not.Null, "The battle's layers are underneath.");
            LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator WithoutTheSetTheVoidStandsForIt()
        {
            art.prologueBackgroundLayers = null;
            yield return OpenThePrologue();
            var intro = Object.FindAnyObjectByType<PrototypeGame>().Intro;
            Assert.That(GameObject.Find("Whole Board"), Is.Null);
            Assert.That(intro.WholeBoardAlpha, Is.Empty);
            intro.Seek(30); yield return null;
            Assert.That(Alpha("Void"), Is.EqualTo(1f), "Black void before the tear, as before the set existed.");
            intro.Seek((float)IntroTimeline.Tear + 4); yield return null;
            Assert.That(Alpha("Void"), Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
#endif
