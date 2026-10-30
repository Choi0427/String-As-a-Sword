using System.Reflection;
using System.Text;
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
            var build = typeof(BoardWorldPresenter).GetMethod("TelegraphMesh", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
            AssertMeshInsideCellAndFacingCamera((Mesh)build.Invoke(null, new object[] { impact }));
        }

        [TestCase(AttackResponse.Either)]
        [TestCase(AttackResponse.GuardOnly)]
        [TestCase(AttackResponse.DodgeOnly)]
        public void ResponseGeometryStaysInsideCellAndFacesCamera(AttackResponse response)
            => AssertMeshInsideCellAndFacingCamera(ResponseMesh(response));

        [Test]
        public void EachResponseHasItsOwnMarkShapeAboveTheBadgeStrip()
        {
            Mesh either = ResponseMesh(AttackResponse.Either), guard = ResponseMesh(AttackResponse.GuardOnly), dodge = ResponseMesh(AttackResponse.DodgeOnly);
            try
            {
                Assert.That(guard.GetTriangles(1).Length, Is.Not.EqualTo(either.GetTriangles(1).Length));
                Assert.That(dodge.GetTriangles(1).Length, Is.Not.EqualTo(guard.GetTriangles(1).Length));
                Assert.That(dodge.GetTriangles(1).Length, Is.Not.EqualTo(either.GetTriangles(1).Length));
                foreach (var mesh in new[] { guard, dodge })
                {
                    var vertices = mesh.vertices;
                    // The frame is the first four quads of markings; the symbol follows it.
                    var triangles = mesh.GetTriangles(1);
                    for (int i = 24; i < triangles.Length; i++)
                    {
                        Assert.That(vertices[triangles[i]].z, Is.GreaterThan(-.2f), "A countdown badge covers the lower strip of a cell.");
                        Assert.That(Mathf.Abs(vertices[triangles[i]].x), Is.LessThan(.42f));
                    }
                }
            }
            finally { Object.DestroyImmediate(either); Object.DestroyImmediate(guard); Object.DestroyImmediate(dodge); }
        }

        private static Mesh ResponseMesh(AttackResponse response)
            => (Mesh)typeof(BoardWorldPresenter).GetMethod("TelegraphMesh", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(AttackResponse) }, null)
                .Invoke(null, new object[] { response });

        private static void AssertMeshInsideCellAndFacingCamera(Mesh mesh)
        {
            try
            {
                Assert.That(mesh.subMeshCount, Is.EqualTo(2));
                foreach (var vertex in mesh.vertices)
                {
                    Assert.That(Mathf.Abs(vertex.x), Is.LessThan(.5f));
                    Assert.That(Mathf.Abs(vertex.z), Is.LessThan(.5f));
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

        [TestCase(AttackResponse.GuardOnly, 2, 1, "GUARD IN 2")]
        [TestCase(AttackResponse.DodgeOnly, 1, 1, "MOVE IN 1")]
        [TestCase(AttackResponse.Either, 3, 1, "DANGER IN 3")]
        [TestCase(AttackResponse.GuardOnly, 2, 3, "GUARD IN 2 x3")]
        public void ThreatTextNamesTheRequiredResponse(AttackResponse response, int remaining, int hits, string expected)
            => Assert.That(TelegraphHud.ThreatText(response, remaining, hits), Is.EqualTo(expected));

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
        public void LaterHitThreatensACellTheNextHitMisses()
        {
            var counts = new int[64]; var soonest = new int[64]; var responses = new AttackResponse[64];
            var checker = new[] { Attack(1, "Checker", 0b01UL, 6, AttackResponse.DodgeOnly), Attack(2, "Checker", 0b10UL, 7, AttackResponse.DodgeOnly, 1) };
            TelegraphHud.MapCells(checker, 4, counts, soonest, responses);
            Assert.That(TelegraphHud.ThreatText(responses[1], soonest[1], counts[1]), Is.EqualTo("MOVE IN 3"));
        }

        [Test]
        public void SequenceTakesOneLineWithSoonestHitAndRemainingCount()
        {
            var text = new StringBuilder();
            var attacks = new[] {
                Attack(5, "Tremolo", ulong.MaxValue, 6, AttackResponse.GuardOnly),
                Attack(6, "Tremolo", ulong.MaxValue, 7, AttackResponse.GuardOnly, 1),
                Attack(7, "Tremolo", ulong.MaxValue, 8, AttackResponse.GuardOnly, 2),
                Attack(8, "Top Wipe", 1UL, 9, AttackResponse.DodgeOnly),
                Attack(9, "ROW 4", 2UL, 9, AttackResponse.Either)
            };
            int shown = TelegraphHud.AppendPatternLines(text, attacks, 4, 0, 5);
            Assert.That(shown, Is.EqualTo(3));
            Assert.That(text.ToString(), Is.EqualTo("/// GUARD  Tremolo  /  2 NOTES  x3\n/// MOVE  Top Wipe  /  5 NOTES\n/// EITHER  ROW 4  /  5 NOTES\n"));
        }

        [Test]
        public void SequenceWithItsFirstHitFiredStillGroupsTheRest()
        {
            var text = new StringBuilder();
            var attacks = new[] { Attack(6, "Tremolo", ulong.MaxValue, 7, AttackResponse.GuardOnly, 1), Attack(7, "Tremolo", ulong.MaxValue, 8, AttackResponse.GuardOnly, 2) };
            Assert.That(TelegraphHud.AppendPatternLines(text, attacks, 6, 0, 5), Is.EqualTo(1));
            Assert.That(text.ToString(), Is.EqualTo("/// GUARD  Tremolo  /  1 NOTES  x2\n"));
        }

        [Test]
        public void PatternLinesPastTheLimitAreCountedNotWritten()
        {
            var text = new StringBuilder();
            var attacks = new[] { Attack(1, "A", 1UL, 6, AttackResponse.Either), Attack(2, "B", 2UL, 6, AttackResponse.Either) };
            Assert.That(TelegraphHud.AppendPatternLines(text, attacks, 4, 4, 5), Is.EqualTo(6));
            Assert.That(text.ToString(), Is.EqualTo("/// EITHER  A  /  2 NOTES\n"));
        }

        [Test]
        public void OutcomeTextPrefersABrokenGuardOverAPlainHit()
        {
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Guard" }), Is.EqualTo("GUARD BROKEN"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Move" }), Is.EqualTo("HIT"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { hit = true, category = "Other" }), Is.EqualTo("HIT"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { guardSuccess = true, category = "Guard" }), Is.EqualTo("BLOCKED"));
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { category = "Guard" }), Is.Null, "A guard with nothing to block is not an outcome.");
            Assert.That(TelegraphHud.OutcomeText(new NoteRecord { category = "EffectiveAttack" }), Is.Null);
        }

        private static int[] Countdowns(int value)
        { var soonest = new int[64]; for (int i = 0; i < 64; i++) soonest[i] = value; return soonest; }
        private static int Count(ulong mask)
        { int count = 0; for (; mask != 0; mask &= mask - 1) count++; return count; }

        [Test]
        public void NarrowAttacksKeepEveryBadge()
        {
            ulong cross = 0xFFUL << 24;
            for (int y = 0; y < 8; y++) cross |= 1UL << (y * 8 + 3);
            Assert.That(TelegraphHud.BadgeCells(cross, Countdowns(2), 27), Is.EqualTo(cross));
        }

        [Test]
        public void FullBoardWithOneCountdownCollapsesToOneBadgePerBlock()
        {
            ulong badges = TelegraphHud.BadgeCells(ulong.MaxValue, Countdowns(2), 27);
            Assert.That(Count(badges), Is.EqualTo(16));
            Assert.That(badges & (1UL << 27 | 1UL << 35 | 1UL << 19 | 1UL << 26 | 1UL << 28), Is.Zero, "Cells around the knight add nothing when every cell reads the same.");
        }

        [Test]
        public void CheckerKeepsBothCountdownsNearbyAndAroundThePlayer()
        {
            var soonest = new int[64];
            for (int i = 0; i < 64; i++) soonest[i] = (i % 8 + i / 8) % 2 == 0 ? 1 : 2;
            ulong badges = TelegraphHud.BadgeCells(ulong.MaxValue, soonest, 27);
            Assert.That(Count(badges), Is.LessThan(40));
            foreach (int beside in new[] { 27, 35, 19, 26, 28 }) Assert.That(badges & (1UL << beside), Is.Not.Zero, $"Cell {beside} is one move away.");
            for (int y = 0; y < 8; y += 2) for (int x = 0; x < 8; x += 2)
            {
                bool one = false, two = false;
                foreach (int cell in new[] { y * 8 + x, y * 8 + x + 1, (y + 1) * 8 + x, (y + 1) * 8 + x + 1 })
                    if ((badges & (1UL << cell)) != 0) { one |= soonest[cell] == 1; two |= soonest[cell] == 2; }
                Assert.That(one && two, Is.True, $"Block {x},{y} must show both countdowns.");
            }
        }

        [Test]
        public void BadgesNeverAppearOnUnmarkedCells()
        {
            ulong upperHalf = ulong.MaxValue << 32;
            Assert.That(TelegraphHud.BadgeCells(upperHalf, Countdowns(1), 27) & ~upperHalf, Is.Zero);
            var soonest = Countdowns(1); for (int i = 32; i < 64; i += 2) soonest[i] = 3;
            Assert.That(TelegraphHud.BadgeCells(upperHalf, soonest, 27) & ~upperHalf, Is.Zero, "A player below the attack gets no badge on a safe neighbour.");
        }

        [Test]
        public void DenseCountdownsStayReadableAroundKnight()
        {
            var owner = new GameObject("Countdown placement test");
            try
            {
                var view = owner.AddComponent<PrototypeView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var points = (Vector2[])typeof(PrototypeView).GetField("dangerPoints", flags).GetValue(view);
                var bounds = (Rect[])typeof(PrototypeView).GetField("badgeBounds", flags).GetValue(view);
                var placed = (bool[])typeof(PrototypeView).GetField("badgePlaced", flags).GetValue(view);
                var knight = new Rect(-26, 5, 52, 76);
                for (int i = 0; i < 64; i++)
                {
                    points[i] = new Vector2((i % 8 - 3.5f) * 60, (i / 8 - 3.8f) * 52);
                    bounds[i] = new Rect(points[i] - new Vector2(20,10), new Vector2(40,20));
                    placed[i] = !bounds[i].Overlaps(knight);
                }
                var place = typeof(PrototypeView).GetMethod("PlaceCountdown", flags);
                for (int i = 0; i < 64; i++) if (!placed[i]) place.Invoke(view, new object[] { i, knight });
                for (int i = 0; i < 64; i++)
                {
                    Assert.That(placed[i], Is.True);
                    Assert.That(bounds[i].Overlaps(knight), Is.False, $"Cell {i} covers the knight.");
                    for (int j = i + 1; j < 64; j++)
                        Assert.That(bounds[i].Overlaps(bounds[j]), Is.False, $"Cells {i} and {j} overlap.");
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
