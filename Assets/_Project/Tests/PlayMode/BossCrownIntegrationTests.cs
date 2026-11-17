using System.Collections;
using NUnit.Framework;
using StringAsASword.Board;
using StringAsASword.Core;
using StringAsASword.Presentation;
using StringAsASword.Stage;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    public sealed class BossCrownIntegrationTests
    {
        private PrototypeGame game;
        private PrototypeView view;

        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
            game = Object.FindAnyObjectByType<PrototypeGame>();
            view = Object.FindAnyObjectByType<PrototypeView>();
            foreach (var pattern in game.Battle.Settings.Patterns) pattern.Enabled = false;
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (game != null) game.PauseSession();
            yield return null;
        }

        [UnityTest] public IEnumerator PlayShowsNoTextHudOnlyTheCountIn()
        {
            game.StartSession();
            for (int i = 0; i < 5; i++) yield return null;
            string[] removed = { "GEOM EUL HYEON", "COMBO", "FIND YOUR RHYTHM", "NOISE", "PHASE", "TWO NOTES", "Follow the two", "PLAYER HP", "BOSS HP" };
            bool countIn = false;
            foreach (var label in view.GetComponentsInChildren<TMP_Text>())
            {
                foreach (var text in removed) Assert.That(label.text, Does.Not.Contain(text), label.name);
                countIn |= label.text.StartsWith("GET READY");
            }
            Assert.That(countIn, Is.True, "The count-in stays on screen.");
        }
        [UnityTest] public IEnumerator CrownSitsOnTheBossAndShowsHitsAndNoise()
        {
            var crown = view.BossCrown;
            Assert.That(crown, Is.Not.Null, "Run String As a Sword > Art > Set Up Boss Crown.");
            Assert.That(crown.transform.parent, Is.SameAs(view.BossPortrait.transform));
            Assert.That(crown.Material.shader.isSupported, Is.True);
            game.StartSession();
            yield return null;
            game.Health.ApplyDamage(5, 0);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(crown.Blade(3), Is.GreaterThan(.1f), "The first HP segment is the rightmost blade.");
            for (int blade = 0; blade < 3; blade++) Assert.That(crown.Blade(blade), Is.Zero);
            Assert.That(crown.BladeGlow(3), Is.EqualTo(1), "A blade keeps its glow until its segment is spent.");

            var noise = game.Noise;
            view.ShowNoiseCollected(new NoisePickup(99, noise.SessionId, 1, 0, new GridPosition(3, 3)));
            Assert.That(crown.Flying, Is.EqualTo(1));
            Assert.That(GameObject.Find("Noise Mote"), Is.Not.Null);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(crown.Flying, Is.Zero, "The noise lands on the seam.");
            Assert.That(GameObject.Find("Noise Mote"), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
