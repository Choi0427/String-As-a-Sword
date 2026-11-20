using System.Collections.Generic;
using UnityEngine;

namespace StringAsASword.Presentation
{
    /// <summary>
    /// Where the parts of Art/UI/Boss/Crown.png lie, in UV with v measured from the top (0 = top edge).
    /// Four blades (left to right) are the boss HP segments, right blade first; the three seams between them are the
    /// three noise phases, right seam first; everything else (the outer ring and the side hooks) is the body.
    /// <see cref="Bake"/> turns the picture into the region map the Crown UI shader reads.
    /// </summary>
    public static class CrownLayout
    {
        public const int BladeCount = 4, SeamCount = 3;
        /// <summary>Region ids in the map's red channel: 0 body, 1-4 blades left to right, 5-7 seams right to left.</summary>
        public const int Body = 0, FirstBlade = 1, FirstSeam = 5;
        /// <summary>Pixels at least this opaque are the drawn line; fainter ones are its glow.</summary>
        public const float CoreAlpha = .85f;
        /// <summary>The seams are drawn faintly; inside a seam band, pixels this opaque already count as seam.</summary>
        public const float SeamAlpha = .04f;
        // Measured on Crown.png (0.png of the HP-state set, 2832x1472): the blades' column bands and their tip-to-base heights.
        // Outside the blades the full and the spent pictures are identical, so a generous band costs nothing.
        public static readonly float[] BladeLeft = { .165f, .345f, .545f, .705f };
        public static readonly float[] BladeRight = { .28f, .445f, .655f, .845f };
        public static readonly float[] BladeTop = { .31f, .13f, .13f, .31f };
        public static readonly float[] BladeBottom = { .93f, .76f, .76f, .92f };
        /// <summary>The outer blades reach down into the ring; the ring under them is the same in both pictures, so no floor.</summary>
        public const float OuterBladeFloor = 1;
        /// <summary>The side seams lie between these heights; below them the ring sweeps under.</summary>
        public const float SeamTop = .55f, SeamFloor = .78f;
        /// <summary>Seam centre lines, right to left; each runs from its right end to its left end.</summary>
        public static readonly Vector2[][] Seams =
        {
            new[] { new Vector2(.705f, .698f), new Vector2(.68f, .689f), new Vector2(.655f, .684f) },
            new[] { new Vector2(.545f, .672f), new Vector2(.53f, .670f), new Vector2(.50f, .668f), new Vector2(.47f, .669f), new Vector2(.445f, .668f) },
            new[] { new Vector2(.345f, .692f), new Vector2(.33f, .694f), new Vector2(.31f, .698f), new Vector2(.28f, .701f) },
        };

        /// <summary>Blade (0 = leftmost) that shows HP segment <paramref name="segment"/>: the first segment is the rightmost blade.</summary>
        public static int BladeOfSegment(int segment) => BladeCount - 1 - segment;
        /// <summary>Region of a drawn (core) pixel at (u, v from top).</summary>
        public static int Classify(float u, float v)
        {
            for (int i = 0; i < BladeCount; i++)
                if (u >= BladeLeft[i] && u < BladeRight[i] && (i == 1 || i == 2 || v < OuterBladeFloor)) return FirstBlade + i;
            // A seam is a thin line: only pixels close to its centre line belong to it, so the glow that the
            // neighbouring blades throw into the gap stays with those blades.
            for (int seam = 0; seam < SeamCount; seam++)
            {
                var line = Seams[seam];
                if (u > line[0].x || u < line[line.Length - 1].x) continue;
                if (v >= SeamTop && v < SeamFloor && Mathf.Abs(v - SeamLine(seam, u)) < SeamHalfWidth) return FirstSeam + seam;
            }
            return Body;
        }
        /// <summary>Half the height of the band around a seam's centre line that counts as the seam.</summary>
        public const float SeamHalfWidth = .03f;
        /// <summary>Height (v from top) of seam <paramref name="seam"/>'s centre line at <paramref name="u"/>.</summary>
        public static float SeamLine(int seam, float u)
        {
            var line = Seams[seam];
            for (int i = 1; i < line.Length; i++)
                if (u >= line[i].x) return Mathf.Lerp(line[i - 1].y, line[i].y, Mathf.InverseLerp(line[i - 1].x, line[i].x, u));
            return line[line.Length - 1].y;
        }
        private static bool IsSeam(int id) => id >= FirstSeam;
        public static int DecodeId(byte red) => (red + 16) / 32;

        /// <summary>The side hooks are the same in every HP state; columns this close to either edge are used to line pictures up.</summary>
        public const float HookBand = .115f;
        /// <summary>
        /// Moves <paramref name="picture"/> onto a canvas the size of <paramref name="reference"/> so their side hooks overlap:
        /// the state exports are cropped differently. Tries every shift and keeps the one with the least alpha difference over
        /// the hooks. Rows run bottom to top; row y of the picture lands on row y + dy of the canvas, column x on x + dx.
        /// </summary>
        public static Color32[] AlignOnto(Color32[] reference, int referenceWidth, int referenceHeight,
            Color32[] picture, int pictureWidth, int pictureHeight, out int dx, out int dy, int slack = 40)
        {
            long best = long.MaxValue; dx = dy = 0;
            int hook = Mathf.RoundToInt(pictureWidth * HookBand);
            for (int sx = -8; sx <= 8; sx++)
                for (int sy = -slack; sy <= referenceHeight - pictureHeight + slack; sy++)
                {
                    long error = 0; int samples = 0;
                    for (int y = 0; y < pictureHeight; y += 3)
                    {
                        int ry = y + sy; if (ry < 0 || ry >= referenceHeight) continue;
                        for (int x = 0; x < pictureWidth; x += 3)
                        {
                            if (x >= hook && x < pictureWidth - hook) continue;
                            int rx = x + sx; if (rx < 0 || rx >= referenceWidth) continue;
                            int d = picture[y * pictureWidth + x].a - reference[ry * referenceWidth + rx].a;
                            error += d * d; samples++;
                        }
                    }
                    if (samples == 0) continue;
                    error /= samples;
                    if (error < best) { best = error; dx = sx; dy = sy; }
                }
            var canvas = new Color32[referenceWidth * referenceHeight];
            for (int y = 0; y < pictureHeight; y++)
            {
                int ry = y + dy; if (ry < 0 || ry >= referenceHeight) continue;
                for (int x = 0; x < pictureWidth; x++)
                {
                    int rx = x + dx; if (rx < 0 || rx >= referenceWidth) continue;
                    canvas[ry * referenceWidth + rx] = picture[y * pictureWidth + x];
                }
            }
            return canvas;
        }

        /// <summary>
        /// Region map at half the picture's size. Drawn pixels get their part by <see cref="Classify"/>; every other pixel,
        /// the glow included, takes the part of the nearest drawn pixel, so a blade's glow goes out with the blade.
        /// R = id * 32, G = height within a blade (0 tip, 1 base), B = place along a seam (0 right end, 1 left end).
        /// Rows run bottom to top, as in Texture2D.
        /// </summary>
        public static Color32[] Bake(Color32[] source, int sourceWidth, int sourceHeight, out int width, out int height)
        {
            width = Mathf.Max(1, sourceWidth / 2); height = Mathf.Max(1, sourceHeight / 2);
            int count = width * height;
            var ids = new int[count];
            var queue = new Queue<int>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int sum = 0, samples = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int sx = Mathf.Min(2 * x + dx, sourceWidth - 1), sy = Mathf.Min(2 * y + dy, sourceHeight - 1);
                            sum += source[sy * sourceWidth + sx].a; samples++;
                        }
                    int index = y * width + x;
                    ids[index] = -1;
                    float alpha = sum / (255f * samples);
                    if (alpha < SeamAlpha) continue;
                    int id = Classify((x + .5f) / width, 1 - (y + .5f) / height);
                    // Faint pixels seed only the seams (drawn faintly); everything else needs to be the drawn line.
                    if (alpha < CoreAlpha && !IsSeam(id)) continue;
                    ids[index] = id;
                    queue.Enqueue(index);
                }
            if (queue.Count == 0) for (int i = 0; i < count; i++) { ids[i] = Body; }
            // Breadth-first growth from the drawn pixels: each pixel joins the nearest part (4-neighbour distance).
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % width, y = i / width;
                if (x > 0) Grow(i - 1); if (x < width - 1) Grow(i + 1);
                if (y > 0) Grow(i - width); if (y < height - 1) Grow(i + width);
                void Grow(int next) { if (ids[next] >= 0) return; ids[next] = ids[i]; queue.Enqueue(next); }
            }
            var map = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                float u = (i % width + .5f) / width, v = 1 - (i / width + .5f) / height;
                int id = ids[i];
                float along = 0, across = 0;
                if (id >= FirstBlade && id < FirstSeam)
                {
                    int blade = id - FirstBlade;
                    along = Mathf.InverseLerp(BladeTop[blade], BladeBottom[blade], v);
                }
                else if (id >= FirstSeam)
                {
                    var line = Seams[id - FirstSeam];
                    across = Mathf.InverseLerp(line[0].x, line[line.Length - 1].x, u);
                }
                map[i] = new Color32((byte)(id * 32), (byte)Mathf.RoundToInt(along * 255), (byte)Mathf.RoundToInt(across * 255), 255);
            }
            return map;
        }

        /// <summary>Point on seam <paramref name="seam"/>'s centre line, <paramref name="fill"/> of the way from its right end. UV, v from top.</summary>
        public static Vector2 SeamPoint(int seam, float fill)
        {
            var line = Seams[seam];
            float u = Mathf.Lerp(line[0].x, line[line.Length - 1].x, Mathf.Clamp01(fill));
            for (int i = 1; i < line.Length; i++)
                if (u >= line[i].x) return Vector2.Lerp(line[i - 1], line[i], Mathf.InverseLerp(line[i - 1].x, line[i].x, u));
            return line[line.Length - 1];
        }
        /// <summary>Where the dark front of a blade is once <paramref name="depletion"/> of it is gone. UV, v from top.</summary>
        public static Vector2 BladeFront(int blade, float depletion) =>
            new Vector2((BladeLeft[blade] + BladeRight[blade]) * .5f, Mathf.Lerp(BladeTop[blade], BladeBottom[blade], Mathf.Clamp01(depletion)));
    }
}
