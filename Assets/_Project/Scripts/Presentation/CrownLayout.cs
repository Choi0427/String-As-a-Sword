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
        // Measured on Crown.png (2718x1409): the blades' column bands and their tip-to-base heights.
        public static readonly float[] BladeLeft = { .185f, .365f, .565f, .745f };
        public static readonly float[] BladeRight = { .255f, .435f, .635f, .815f };
        public static readonly float[] BladeTop = { .195f, .075f, .075f, .195f };
        public static readonly float[] BladeBottom = { .84f, .721f, .721f, .84f };
        /// <summary>The outer blades stop above the ring that crosses their base, so the ring stays body.</summary>
        public const float OuterBladeFloor = .84f;
        /// <summary>The side seams stop above the ring that sweeps under them.</summary>
        public const float SeamFloor = .6f;
        /// <summary>Seam centre lines, right to left; each runs from its right end to its left end.</summary>
        public static readonly Vector2[][] Seams =
        {
            new[] { new Vector2(.745f, .595f), new Vector2(.73f, .577f), new Vector2(.70f, .55f), new Vector2(.67f, .529f), new Vector2(.635f, .524f) },
            new[] { new Vector2(.565f, .525f), new Vector2(.53f, .511f), new Vector2(.50f, .508f), new Vector2(.47f, .514f), new Vector2(.435f, .526f) },
            new[] { new Vector2(.365f, .523f), new Vector2(.35f, .526f), new Vector2(.32f, .549f), new Vector2(.29f, .571f), new Vector2(.255f, .596f) },
        };

        /// <summary>Blade (0 = leftmost) that shows HP segment <paramref name="segment"/>: the first segment is the rightmost blade.</summary>
        public static int BladeOfSegment(int segment) => BladeCount - 1 - segment;
        /// <summary>Region of a drawn (core) pixel at (u, v from top).</summary>
        public static int Classify(float u, float v)
        {
            for (int i = 0; i < BladeCount; i++)
                if (u >= BladeLeft[i] && u < BladeRight[i] && (i == 1 || i == 2 || v < OuterBladeFloor)) return FirstBlade + i;
            if (u >= BladeRight[2] && u < BladeLeft[3] && v < SeamFloor) return FirstSeam;
            if (u >= BladeRight[1] && u < BladeLeft[2]) return FirstSeam + 1;
            if (u >= BladeRight[0] && u < BladeLeft[1] && v < SeamFloor) return FirstSeam + 2;
            return Body;
        }
        public static int DecodeId(byte red) => (red + 16) / 32;

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
                    if (sum / (255f * samples) < CoreAlpha) continue;
                    ids[index] = Classify((x + .5f) / width, 1 - (y + .5f) / height);
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
