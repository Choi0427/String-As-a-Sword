using System.Reflection;
using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Presentation;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class TelegraphPresentationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void TelegraphGeometryStaysInsideCellAndFacesCamera(bool impact)
        {
            var build = typeof(BoardWorldPresenter).GetMethod("TelegraphMesh", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool), typeof(bool) }, null);
            AssertMeshInsideCellAndFacingCamera((Mesh)build.Invoke(null, new object[] { impact, true }));
        }

        [Test]
        public void JudgeLinesFlashBrighterForBetterJudgements()
        {
            float perfect = PrototypeView.JudgeGlowPeak(StringAsASword.Rhythm.Judgement.Perfect);
            float good = PrototypeView.JudgeGlowPeak(StringAsASword.Rhythm.Judgement.Good);
            float miss = PrototypeView.JudgeGlowPeak(StringAsASword.Rhythm.Judgement.Miss);
            Assert.That(perfect, Is.EqualTo(1f));
            Assert.That(good, Is.LessThan(perfect).And.GreaterThan(miss));
            Assert.That(miss, Is.GreaterThan(0f), "a miss still shows, faintly");
            Assert.That(PrototypeView.JudgeGlowPeak(StringAsASword.Rhythm.Judgement.None), Is.Zero);
        }

        [Test]
        public void FallbackTilesComeInTwoKindsAndOnlyTheStrongOneIsHatched()
        {
            var build = typeof(BoardWorldPresenter).GetMethod("TelegraphMesh", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool), typeof(bool) }, null);
            Mesh normal = (Mesh)build.Invoke(null, new object[] { false, false }), strong = (Mesh)build.Invoke(null, new object[] { false, true });
            try
            {
                // The frame is four quads of markings; hatching adds to it.
                Assert.That(normal.GetTriangles(1).Length, Is.EqualTo(24));
                Assert.That(strong.GetTriangles(1).Length, Is.GreaterThan(24));
                // A mark covers its whole cell, up to the cell's edge and never past it.
                foreach (var vertex in normal.vertices)
                {
                    Assert.That(Mathf.Abs(vertex.x), Is.LessThanOrEqualTo(.5f));
                    Assert.That(Mathf.Abs(vertex.z), Is.LessThanOrEqualTo(.5f));
                }
            }
            finally { Object.DestroyImmediate(normal); Object.DestroyImmediate(strong); }
        }

        private static void AssertMeshInsideCellAndFacingCamera(Mesh mesh)
        {
            try
            {
                Assert.That(mesh.subMeshCount, Is.EqualTo(2));
                foreach (var vertex in mesh.vertices)
                {
                    Assert.That(Mathf.Abs(vertex.x), Is.LessThanOrEqualTo(.5f));
                    Assert.That(Mathf.Abs(vertex.z), Is.LessThanOrEqualTo(.5f));
                    Assert.That(vertex.y, Is.GreaterThan(0));
                }
                var vertices = mesh.vertices;
                for (int part = 0; part < 2; part++)
                {
                    var triangles = mesh.GetTriangles(part);
                    Assert.That(triangles.Length, Is.GreaterThan(0));
                    float area = 0;
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        var normal = Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]], vertices[triangles[i + 2]] - vertices[triangles[i]]);
                        Assert.That(normal.y, Is.GreaterThanOrEqualTo(-.00001f), "Backfaces disappear from the board camera.");
                        area += normal.y;
                    }
                    Assert.That(area, Is.GreaterThan(.1f));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private static BossTelegraph Attack(int id, string name, ulong cells, int impact, AttackResponse response, int hit = 0)
            => new BossTelegraph(id, name, cells, impact, PatternKind.Row, response: response, hit: hit);

        [Test]
        public void ResponseColorTintsOnlyStrongAttacks()
        {
            Assert.That(TelegraphHud.ResponseColor(AttackResponse.DodgeOnly, Color.black), Is.EqualTo(new Color(1f, .72f, .25f)));
            Assert.That(TelegraphHud.ResponseColor(AttackResponse.GuardOnly, Color.black), Is.EqualTo(Color.black), "Guard-only is a normal attack on screen.");
            Assert.That(TelegraphHud.ResponseColor(AttackResponse.Either, Color.magenta), Is.EqualTo(Color.magenta));
        }

        [Test]
        public void SharedCellReportsTheSoonestAttacksResponse()
        {
            var counts = new int[64]; var soonest = new int[64]; var responses = new AttackResponse[64];
            var attacks = new[] {
                Attack(1, "Later", 0b11UL, 9, AttackResponse.DodgeOnly),
                Attack(2, "Sooner", 0b01UL, 6, AttackResponse.GuardOnly),
                Attack(3, "Tied", 0b100UL, 6, AttackResponse.Either),
                Attack(4, "Tied strict", 0b100UL, 6, AttackResponse.DodgeOnly)
            };
            TelegraphHud.MapCells(attacks, 4, counts, soonest, responses);
            Assert.That(counts[0], Is.EqualTo(2)); Assert.That(soonest[0], Is.EqualTo(2)); Assert.That(responses[0], Is.EqualTo(AttackResponse.GuardOnly));
            Assert.That(counts[1], Is.EqualTo(1)); Assert.That(soonest[1], Is.EqualTo(5)); Assert.That(responses[1], Is.EqualTo(AttackResponse.DodgeOnly));
            Assert.That(responses[2], Is.EqualTo(AttackResponse.DodgeOnly));
            Assert.That(counts[3], Is.Zero); Assert.That(soonest[3], Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void EachCellCountsDownToItsSoonestHit()
        {
            var counts = new int[64]; var soonest = new int[64]; var responses = new AttackResponse[64];
            var longWarning = new BossTelegraph(1, "Long", 0b0110UL, 6, warningNotes: 3);
            var overlappingWarnings = new[] {
                longWarning,
                new BossTelegraph(2, "Short", 0b1011UL, 6, warningNotes: 1),
                new BossTelegraph(3, "Later", 0b1000UL, 8, warningNotes: 3)
            };
            for (int lastClosed = 3; lastClosed <= 5; lastClosed++)
            {
                TelegraphHud.MapCells(lastClosed == 5 ? overlappingWarnings : new[] { longWarning }, lastClosed, counts, soonest, responses);
                Assert.That(soonest[1], Is.EqualTo(6 - lastClosed));
            }
            Assert.That(counts[0], Is.EqualTo(1), "The short warning still marks the cell as dangerous.");
            Assert.That(soonest[3], Is.EqualTo(1), "A later impact does not mask the immediate threat.");
        }

        [Test]
        public void LaterHitThreatensACellTheNextHitMisses()
        {
            var counts = new int[64]; var soonest = new int[64]; var responses = new AttackResponse[64];
            var checker = new[] { Attack(1, "Checker", 0b01UL, 6, AttackResponse.DodgeOnly), Attack(2, "Checker", 0b10UL, 7, AttackResponse.DodgeOnly, 1) };
            TelegraphHud.MapCells(checker, 4, counts, soonest, responses);
            Assert.That(responses[1], Is.EqualTo(AttackResponse.DodgeOnly));
            Assert.That(soonest[1], Is.EqualTo(3));
            Assert.That(counts[1], Is.EqualTo(1));
        }

        [Test]
        public void OutcomeTextShowsEveryHitAsHitAndAGuardAsBlock()
        {
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Guard" }), Is.EqualTo("HIT"), "A broken guard is a plain HIT.");
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Move" }), Is.EqualTo("HIT"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Other" }), Is.EqualTo("HIT"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { guardSuccess = true, category = "Guard" }), Is.EqualTo("BLOCK"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { category = "Guard" }), Is.Null, "A guard with nothing to block is not an outcome.");
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { category = "EffectiveAttack" }), Is.Null);
        }

    }
}
