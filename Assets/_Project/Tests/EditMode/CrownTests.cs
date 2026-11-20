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
            // The side hooks, the ring sweeping under a side seam, and the gap between the inner blades away from the
            // middle seam's line are body.
            Assert.That(CrownLayout.Classify(.05f, .7f), Is.EqualTo(CrownLayout.Body));
            Assert.That(CrownLayout.Classify(.30f, .9f), Is.EqualTo(CrownLayout.Body));
            Assert.That(CrownLayout.Classify(.50f, .45f), Is.EqualTo(CrownLayout.Body));
        }
        [Test] public void FaintPixelsSeedOnlyTheSeams()
        {
            // An opaque line through the second blade and a faint line along the middle seam; the rest is nearly clear.
            const int width = 400, height = 200;
            var picture = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float u = (x + .5f) / width, v = 1 - (y + .5f) / height;
                    byte alpha = 3;
                    if (x >= 156 && x < 164) alpha = 255;
                    else if (u > .45f && u < .54f && Mathf.Abs(v - CrownLayout.SeamLine(1, u)) < .01f) alpha = 40;
                    else if (u > .2f && u < .3f && v > .4f && v < .5f) alpha = 40; // faint glow away from any seam
                    picture[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            var map = CrownLayout.Bake(picture, width, height, out int mapWidth, out int mapHeight);
            int At(float u, float v) => CrownLayout.DecodeId(map[(int)((1 - v) * mapHeight) * mapWidth + (int)(u * mapWidth)].r);
            Assert.That(At(.5f, CrownLayout.SeamLine(1, .5f)), Is.EqualTo(CrownLayout.FirstSeam + 1), "A faint seam line is still the seam.");
            Assert.That(At(.25f, .45f), Is.Not.EqualTo(CrownLayout.FirstSeam + 2), "Faint glow away from a seam line does not become a seam.");
        }
        [Test] public void AlignOntoFindsTheShiftThatOverlapsTheHooks()
        {
            // A 300x120 reference with hooks at both edges, and the same picture cropped 7 rows at the bottom and 5 at the top.
            const int width = 300, height = 120, cropBottom = 7, cropTop = 5;
            var reference = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // A hashed pattern that never repeats, so only one shift fits.
                    bool hook = (x < 30 || x >= width - 30) && (((x * 73856093) ^ (y * 19349663)) & 0x7fffffff) % 5 < 2;
                    reference[y * width + x] = new Color32(255, 255, 255, (byte)(hook ? 255 : 0));
                }
            int croppedHeight = height - cropBottom - cropTop;
            var cropped = new Color32[width * croppedHeight];
            for (int y = 0; y < croppedHeight; y++)
                for (int x = 0; x < width; x++) cropped[y * width + x] = reference[(y + cropBottom) * width + x];
            var aligned = CrownLayout.AlignOnto(reference, width, height, cropped, width, croppedHeight, out int dx, out int dy);
            Assert.That(dx, Is.Zero); Assert.That(dy, Is.EqualTo(cropBottom));
            Assert.That(aligned.Length, Is.EqualTo(reference.Length));
            for (int y = cropBottom; y < height - cropTop; y++)
                for (int x = 0; x < width; x += 7) Assert.That(aligned[y * width + x].a, Is.EqualTo(reference[y * width + x].a));
        }
        [Test] public void SpentCrownLinesUpWithTheWholeCrown()
        {
            var art = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtSet>("Assets/_Project/Art/ArtSet.asset");
            Assert.That(art.crownSpent, Is.Not.Null, "Run String As a Sword > Art > Set Up Boss Crown.");
            Assert.That(art.crownSpent.width, Is.EqualTo(art.crown.texture.width));
            Assert.That(art.crownSpent.height, Is.EqualTo(art.crown.texture.height));
            // Outside the blades the two pictures are the same drawing: compare the hooks, read straight from the files.
            var whole = new Texture2D(2, 2); whole.LoadImage(System.IO.File.ReadAllBytes("Assets/_Project/Art/UI/Boss/Crown.png"));
            var spent = new Texture2D(2, 2); spent.LoadImage(System.IO.File.ReadAllBytes("Assets/_Project/Art/UI/Boss/Crown_Spent.png"));
            var a = whole.GetPixels32(); var b = spent.GetPixels32(); int w = whole.width, h = whole.height, worst = 0;
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                    if (x < w * CrownLayout.HookBand || x >= w * (1 - CrownLayout.HookBand)) worst = Mathf.Max(worst, Mathf.Abs(a[y * w + x].a - b[y * w + x].a));
            Object.DestroyImmediate(whole); Object.DestroyImmediate(spent);
            Assert.That(worst, Is.LessThan(8), "The spent crown's hooks sit exactly on the whole crown's hooks.");
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
            // A 200x100 picture with one opaque vertical line through the second blade's band; the rest is glow too faint
            // to seed even a seam.
            const int width = 200, height = 100;
            var picture = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    picture[y * width + x] = new Color32(255, 255, 255, (byte)(x >= 76 && x < 84 ? 255 : 5));
            var map = CrownLayout.Bake(picture, width, height, out int mapWidth, out int mapHeight);
            Assert.That(mapWidth, Is.EqualTo(100)); Assert.That(mapHeight, Is.EqualTo(50));
            foreach (var texel in map) Assert.That(CrownLayout.DecodeId(texel.r), Is.EqualTo(CrownLayout.FirstBlade + 1));
            // Rows run bottom to top: the top row is nearer the tip (smaller G) than the bottom row.
            byte top = map[(mapHeight - 1) * mapWidth + 40].g, bottom = map[40].g;
            Assert.That(top, Is.LessThan(bottom));
        }
        [Test] public void ImportedRegionMapKeepsItsBakedSizeAndExactTexels()
        {
            var art = UnityEditor.AssetDatabase.LoadAssetAtPath<ArtSet>("Assets/_Project/Art/ArtSet.asset");
            Assert.That(art.HasCrown, Is.True, "Run String As a Sword > Art > Set Up Boss Crown.");
            // Half the crown picture, unscaled: any resampling would blend part ids where two parts meet.
            Assert.That(art.crownRegions.width, Is.EqualTo(art.crown.texture.width / 2));
            Assert.That(art.crownRegions.height, Is.EqualTo(art.crown.texture.height / 2));
            Assert.That(art.crownRegions.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(art.crownRegions.isDataSRGB, Is.False);
        }
    }
}
