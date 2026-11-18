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
        [UnityTest] public IEnumerator NoiseFloatsAsTheDeliveredSpriteAndTheBoardHasNoEdgeMarksOrLabels()
        {
            var board = Object.FindAnyObjectByType<BoardWorldPresenter>();
            Assert.That(view.Art.noiseSprite, Is.Not.Null, "Run String As a Sword > Art > Set Up Noise Sprite.");
            Assert.That(board.NoiseUsesSprite, Is.True);
            var world = GameObject.Find("Board World").transform;
            for (int x = 0; x < 8; x++) Assert.That(world.Find($"AttackEdge_{x}"), Is.Null, "The orange row-8 strips are gone.");
            foreach (var label in view.GetComponentsInChildren<TMP_Text>(true))
                Assert.That(label.name, Is.Not.EqualTo("Column").And.Not.EqualTo("Row"), "The board's A-H and 1-8 labels are gone.");
            game.StartSession();
            float waited = 0;
            while (game.Noise.Pickups.Count == 0 && waited < 10) { waited += Time.unscaledDeltaTime; yield return null; }
            Assert.That(game.Noise.Pickups.Count, Is.GreaterThan(0), "A noise pickup spawns early in the battle.");
            yield return null;
            var pickup = world.Find("Noise_0");
            Assert.That(pickup.gameObject.activeSelf, Is.True);
            var hover = pickup.Find("Noise Float");
            Assert.That(hover.localPosition.y, Is.GreaterThan(.35f), "The picture hovers well above its cell.");
            var picture = hover.Find("Noise Sprite").GetComponent<SpriteRenderer>();
            Assert.That(picture.sprite, Is.SameAs(view.Art.noiseSprite));
            // A dark copy drawn behind the white picture keeps it visible on light tiles.
            var backing = hover.Find("Noise Backing").GetComponent<SpriteRenderer>();
            Assert.That(backing.sortingOrder, Is.LessThan(picture.sortingOrder));
            Assert.That(backing.color.grayscale, Is.LessThan(.1f));
            Assert.That(backing.color.a, Is.GreaterThan(.5f));
            Assert.That(pickup.Find("Noise Shadow").GetComponent<SpriteRenderer>().color.a, Is.GreaterThan(0), "A shadow sits on the cell under it.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
