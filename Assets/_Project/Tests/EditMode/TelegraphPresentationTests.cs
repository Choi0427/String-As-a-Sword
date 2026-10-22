using System.Reflection;
using NUnit.Framework;
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
            var build = typeof(BoardWorldPresenter).GetMethod("TelegraphMesh", BindingFlags.Static | BindingFlags.NonPublic);
            var mesh = (Mesh)build.Invoke(null, new object[] { impact });
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
