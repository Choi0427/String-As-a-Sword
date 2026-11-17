using NUnit.Framework;
using StringAsASword.Combat;
using StringAsASword.Presentation;
using UnityEngine;

namespace StringAsASword.Tests
{
    public sealed class CrownTests
    {
        [Test] public void FirstHpSegmentIsTheRightmostBlade()
        {
            Assert.That(CrownLayout.BladeOfSegment(0), Is.EqualTo(3));
            Assert.That(CrownLayout.BladeOfSegment(3), Is.EqualTo(0));
            var health = new BossHealthGate(35, 8, 14, 54);
            health.ApplyDamage(7, 0);
            Assert.That(CrownState.BladeDepletion(health, 3), Is.EqualTo(.2f).Within(1e-5));
            for (int blade = 0; blade < 3; blade++) Assert.That(CrownState.BladeDepletion(health, blade), Is.Zero);
            health.ApplyDamage(28, 0);
            Assert.That(CrownState.BladeDepletion(health, 3), Is.EqualTo(1));
        }
        [Test] public void EachNoisePhaseFillsItsOwnSeamFromTheRight()
        {
            // Phase 1 fills seam 0 (rightmost) by its collected share; later seams stay clean.
            Assert.That(CrownState.SeamFill(0, 1, 2, 4), Is.EqualTo(.5f));
            Assert.That(CrownState.SeamFill(1, 1, 2, 4), Is.Zero);
            Assert.That(CrownState.SeamFill(2, 1, 2, 4), Is.Zero);
            // Past phases stay full; the final phase needs no noise and every seam is full.
            Assert.That(CrownState.SeamFill(0, 2, 0, 5), Is.EqualTo(1));
            Assert.That(CrownState.SeamFill(1, 2, 5, 5), Is.EqualTo(1));
            Assert.That(CrownState.SeamFill(2, 4, 0, 0), Is.EqualTo(1));
            Assert.That(CrownState.SeamFill(0, 1, 9, 4), Is.EqualTo(1), "More than required never overfills.");
            Assert.That(CrownState.SeamFill(0, 1, -1, 4), Is.Zero, "Noise still in flight never makes a negative fill.");
        }
        [Test] public void SeamCracklesOnceItsGoalIsMetAndKeepsCrackling()
        {
            Assert.That(CrownState.SeamCrackles(0, 1, false), Is.False);
            Assert.That(CrownState.SeamCrackles(0, 1, true), Is.True);
            Assert.That(CrownState.SeamCrackles(1, 1, true), Is.False);
            Assert.That(CrownState.SeamCrackles(0, 3, false), Is.True);
            for (int seam = 0; seam < 3; seam++) Assert.That(CrownState.SeamCrackles(seam, 4, true), Is.True);
        }
        [Test] public void LayoutSortsBladesSeamsAndBody()
        {
            for (int blade = 0; blade < 4; blade++)
            {
                float u = (CrownLayout.BladeLeft[blade] + CrownLayout.BladeRight[blade]) * .5f;
                Assert.That(CrownLayout.Classify(u, .4f), Is.EqualTo(CrownLayout.FirstBlade + blade));
            }
            for (int seam = 0; seam < 3; seam++)
            {
                var middle = CrownLayout.SeamPoint(seam, .5f);
                Assert.That(CrownLayout.Classify(middle.x, middle.y), Is.EqualTo(CrownLayout.FirstSeam + seam));
            }
            // The side hooks, and the ring crossing under an outer blade, are body.
            Assert.That(CrownLayout.Classify(.05f, .7f), Is.EqualTo(CrownLayout.Body));
            Assert.That(CrownLayout.Classify(.22f, .9f), Is.EqualTo(CrownLayout.Body));
            Assert.That(CrownLayout.Classify(.30f, .9f), Is.EqualTo(CrownLayout.Body));
        }
        [Test] public void SeamPointsRunFromTheRightEndToTheLeftEnd()
        {
            for (int seam = 0; seam < 3; seam++)
            {
                var line = CrownLayout.Seams[seam];
                Assert.That(CrownLayout.SeamPoint(seam, 0), Is.EqualTo(line[0]));
                Assert.That(CrownLayout.SeamPoint(seam, 1).x, Is.EqualTo(line[line.Length - 1].x).Within(1e-5));
                Assert.That(CrownLayout.SeamPoint(seam, .5f).x, Is.LessThan(line[0].x));
            }
        }
        [Test] public void BakedMapGivesGlowTheNearestPartAndBladesATipToBaseGradient()
        {
            // A 200x100 picture with one opaque vertical line through the second blade's band (inner blades run to the base); the rest is faint glow.
            const int width = 200, height = 100;
            var picture = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    picture[y * width + x] = new Color32(255, 255, 255, (byte)(x >= 76 && x < 84 ? 255 : 40));
            var map = CrownLayout.Bake(picture, width, height, out int mapWidth, out int mapHeight);
            Assert.That(mapWidth, Is.EqualTo(100)); Assert.That(mapHeight, Is.EqualTo(50));
            foreach (var texel in map) Assert.That(CrownLayout.DecodeId(texel.r), Is.EqualTo(CrownLayout.FirstBlade + 1));
            // Rows run bottom to top: the top row is nearer the tip (smaller G) than the bottom row.
            byte top = map[(mapHeight - 1) * mapWidth + 40].g, bottom = map[40].g;
            Assert.That(top, Is.LessThan(bottom));
        }
    }
}
