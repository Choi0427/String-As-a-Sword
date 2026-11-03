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
    /// <summary>Fills the scene's ArtSet in memory with greybox stand-ins, the way delivered art will arrive.</summary>
    public sealed class ArtSetIntegrationTests
    {
        private const string ArtSetPath = "Assets/_Project/Art/ArtSet.asset";
        private ArtSet art;
        private string original;
        private GameObject knight, king, noise, vfx;
        private Material mark;

        [UnitySetUp] public IEnumerator SetUp()
        {
            art = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtSet>(ArtSetPath);
            Assert.That(art, Is.Not.Null, "Run String As a Sword > Art > Create Art Set and Folders.");
            original = JsonUtility.ToJson(art);
            // Stand-ins act as prefabs: active, at the origin (instances keep the source's local pose), and kept across the scene load.
            knight = StandIn("Knight Stand-in", PrimitiveType.Capsule);
            king = StandIn("King Stand-in", PrimitiveType.Cube);
            noise = StandIn("Noise Stand-in", PrimitiveType.Sphere);
            vfx = StandIn("Vfx Stand-in", PrimitiveType.Quad);
            mark = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            art.knightPrefab = knight; art.kingPrefab = king; art.noisePrefab = noise;
            art.eitherMark = art.guardMark = art.dodgeMark = art.impactMark = mark;
            art.attackVfx = art.guardVfx = art.hitVfx = art.blockVfx = art.pickupVfx = art.bossFireVfx = art.phaseVfx = vfx;
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
        }
        [TearDown] public void TearDown()
        {
            // Restore the asset in memory; nothing here is saved to disk.
            JsonUtility.FromJsonOverwrite(original, art);
            foreach (var item in new Object[] { knight, king, noise, vfx, mark }) if (item != null) Object.Destroy(item);
        }
        [UnityTest] public IEnumerator DeliveredPrefabsReplaceProceduralVisualsAndCuesDoNotLog()
        {
            var game = Object.FindAnyObjectByType<PrototypeGame>();
            var board = Object.FindAnyObjectByType<BoardWorldPresenter>();
            var view = Object.FindAnyObjectByType<PrototypeView>();
            Assert.That(view.Art, Is.SameAs(art), "The scene must use the shared ArtSet asset.");
            var player = board.PlayerTransform;
            Assert.That(player.Find("Knight Model"), Is.Not.Null);
            Assert.That(player.Find("Knight Silhouette"), Is.Null, "The procedural knight must not be built under a delivered model.");
            Assert.That(GameObject.Find("King"), Is.Not.Null);
            // Telegraph cells start inactive, which GameObject.Find skips.
            var impact = GameObject.Find("Board World").transform.Find("Impact_0_0");
            Assert.That(impact.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(mark));
            game.StartSession();
            for (int i = 0; i < 10; i++) yield return null;
            // Every cue path, including ones that spawn VFX on the board, must tolerate art without an Animator.
            view.ShowDamage(1); view.ShowPhase(2);
            board.ShowOutcome(TelegraphHud.Blocked); board.ShowOutcome(TelegraphHud.Hit);
            yield return null;
            Assert.That(GameObject.Find("Vfx Stand-in(Clone)"), Is.Not.Null);
            game.PauseSession();
            LogAssert.NoUnexpectedReceived();
        }
        private static GameObject StandIn(string name, PrimitiveType shape)
        {
            var item = GameObject.CreatePrimitive(shape); item.name = name;
            Object.DestroyImmediate(item.GetComponent<Collider>());
            Object.DontDestroyOnLoad(item);
            return item;
        }
    }
}
#endif
