using System.Collections;
using NUnit.Framework;
using StringAsASword.Core;
using StringAsASword.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace StringAsASword.Tests
{
    /// <summary>Docs/Sfx.md: the two UI sounds are the prologue's metronome and chess piece.</summary>
    public sealed class SfxTests
    {
        private ArtSet art;

        [UnitySetUp] public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
            art = Object.FindAnyObjectByType<PrototypeGame>().View.Art;
            yield return null;
        }

        [Test] public void UiSoundsAreThePrologueClips()
        {
            Assert.That(art.uiMoveSound, Is.Not.Null); Assert.That(art.uiMoveSound.name, Is.EqualTo("clock"));
            Assert.That(art.uiConfirmSound, Is.Not.Null); Assert.That(art.uiConfirmSound.name, Is.EqualTo("place"));
        }

        [Test] public void UiMoveAndConfirmPlayTheirClips()
        {
            int before = Sfx.PlayCount;
            Sfx.UiMove(art); Assert.That(Sfx.LastClip, Is.SameAs(art.uiMoveSound));
            Sfx.UiConfirm(art); Assert.That(Sfx.LastClip, Is.SameAs(art.uiConfirmSound));
            Assert.That(Sfx.PlayCount, Is.EqualTo(before + 2));
        }
    }
}
