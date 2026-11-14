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
        private GameObject knight, king, noise, vfx, boardModel;
        private Sprite layer;
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
            boardModel = StandIn("Board Stand-in", PrimitiveType.Cube);
            layer = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
            mark = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            art.knightPrefab = knight; art.kingPrefab = king; art.noisePrefab = noise; art.boardPrefab = boardModel;
            art.backgroundLayers = new[] { new BackgroundLayer { sprite = layer, sway = new Vector2(6, 4), period = 3 } };
            art.normalMark = art.strongMark = art.impactMark = mark;
            art.attackVfx = art.guardVfx = art.hitVfx = art.blockVfx = art.pickupVfx = art.bossFireVfx = art.phaseVfx = vfx;
            yield return SceneManager.LoadSceneAsync("RhythmPrototype");
        }
        [TearDown] public void TearDown()
        {
            // Restore the asset in memory; nothing here is saved to disk.
            JsonUtility.FromJsonOverwrite(original, art);
            foreach (var item in new Object[] { knight, king, noise, vfx, boardModel, layer, mark }) if (item != null) Object.Destroy(item);
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
            var boardWorld = GameObject.Find("Board World").transform;
            Assert.That(boardWorld.Find("Board Model"), Is.Not.Null);
            Assert.That(boardWorld.Find("Cell_0_0"), Is.Null, "Procedural cells must not be built under a delivered board.");
            Assert.That(board.BoardCamera.backgroundColor.a, Is.Zero, "With background art the board view is see-through.");
            var floating = GameObject.Find("Background Layer 0");
            Assert.That(floating, Is.Not.Null);
            Assert.That(floating.GetComponent<BackgroundSway>(), Is.Not.Null);
            game.StartSession();
            for (int i = 0; i < 10; i++) yield return null;
            // Every cue path, including ones that spawn VFX on the board, must tolerate art without an Animator.
            view.ShowDamage(1); view.ShowPhase(2);
            board.ShowOutcome(TelegraphHud.Blocked); board.ShowOutcome(TelegraphHud.Hit);
            yield return null;
            Assert.That(GameObject.Find("Vfx Stand-in(Clone)"), Is.Not.Null);
            // The finale collapse breaks a delivered board into one shard per cell, and a reset puts the model back.
            Assert.That(board.BoardShatters, Is.True, "a readable board mesh is cut into shards");
            board.SetCollapse(1);
            Assert.That(boardWorld.Find("Board Model").gameObject.activeSelf, Is.False);
            Assert.That(boardWorld.Find("Board Shards").childCount, Is.EqualTo(64));
            board.SetCollapse(0);
            Assert.That(boardWorld.Find("Board Model").gameObject.activeSelf, Is.True);
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
