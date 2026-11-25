using System.Collections;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
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

        [UnityTest] public IEnumerator PlayShowsNoTextHudAndNoCountIn()
        {
            game.StartSession();
            for (int i = 0; i < 5; i++) yield return null;
            string[] removed = { "GEOM EUL HYEON", "COMBO", "FIND YOUR RHYTHM", "NOISE", "PHASE", "TWO NOTES", "Follow the two", "PLAYER HP", "BOSS HP" };
            foreach (var label in view.GetComponentsInChildren<TMP_Text>())
            {
                foreach (var text in removed) Assert.That(label.text, Does.Not.Contain(text), label.name);
                Assert.That(label.text, Does.Not.StartWith("GET READY"), "The song starts without a count-in.");
            }
        }
        /// <summary>
        /// The king's lower part stands behind the board. It is cut off at the board's far edge, so it never shows through
        /// when the board fades (the tutorial's handover dips the content to nothing).
        /// </summary>
        [UnityTest] public IEnumerator KingIsCutOffAtTheBoardsFarEdge()
        {
            yield return null;
            var portrait = view.BossPortrait;
            Assert.That(portrait, Is.Not.Null);
            var mask = portrait.Clip;
            Assert.That(mask, Is.Not.Null);
            Assert.That(portrait.transform.parent, Is.SameAs(mask.transform));
            float farEdge = view.BoardViewRect.anchoredPosition.y + view.BoardWorld.ProjectToBoard(new Vector3(0, 0, 4), PrototypeView.BoardSize).y;
            Assert.That(portrait.ClipLine, Is.LessThanOrEqualTo(farEdge).And.GreaterThan(farEdge - 8), "cut just under the far edge, behind the stone");
            // The mask covers the canvas, centred: its bottom padding lifts the cut from the canvas's bottom edge to the line.
            Assert.That(mask.padding.y - mask.rectTransform.rect.height * .5f, Is.EqualTo(portrait.ClipLine).Within(1), "the mask cuts where the line is");
            var feet = (RectTransform)portrait.transform;
            Assert.That(feet.anchoredPosition.y, Is.LessThan(portrait.ClipLine - 50), "the cut hides the part the board covers");
        }
        [UnityTest] public IEnumerator CrownSitsOnTheBossAndShowsBossHpOnly()
        {
            var crown = view.BossCrown;
            Assert.That(crown, Is.Not.Null, "Run String As a Sword > Art > Set Up Boss Crown.");
            Assert.That(crown.transform.parent, Is.SameAs(view.BossPortrait.transform));
            Assert.That(crown.Material.shader.isSupported, Is.True);
            game.StartSession();
            yield return null;
            // A fifth of the first segment, whatever the stage's HP.
            game.Health.ApplyDamage(Mathf.Max(1, game.Health.Maximum(0) / 5), 0);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(crown.Blade(3), Is.GreaterThan(.1f), "The first HP segment is the rightmost blade.");
            for (int blade = 0; blade < 3; blade++) Assert.That(crown.Blade(blade), Is.Zero);
            Assert.That(crown.BladeGlow(3), Is.EqualTo(1), "A blade keeps its glow until its segment is spent.");

            Assert.That(crown.Seam(0), Is.Zero, "A seam stays clean until the blade before it is spent.");
            game.Health.ApplyDamage(game.Health.CurrentHp, 0);
            yield return new WaitForSecondsRealtime(.8f);
            Assert.That(crown.Seam(0), Is.GreaterThan(.9f));
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
            Assert.That(hover.localPosition.y, Is.InRange(.2f, .32f), "The picture floats just above its own cell, not over the one behind.");
            var picture = hover.Find("Noise Sprite").GetComponent<SpriteRenderer>();
            Assert.That(picture.sprite, Is.SameAs(view.Art.noiseSprite));
            // The legacy Sprites/Default draws everything white in this pipeline; URP's sprite shader keeps the colours.
            Assert.That(picture.sharedMaterial.shader.name, Is.EqualTo(BoardWorldPresenter.NoiseShader));
            Assert.That(picture.sharedMaterial.shader.isSupported, Is.True);
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
