using System.Collections.Generic;
using System.IO;
using System.Linq;
using StringAsASword.Presentation;
using UnityEditor;
using UnityEngine;

namespace StringAsASword.Editor
{
    /// <summary>
    /// Grows the board's cracks once on the CPU and bakes them into the map the Board Crack shader reads.
    /// What makes a crack read as real, and how each is made here:
    /// - an impact point: main cracks fan out from the centre and the board is shattered most around it;
    /// - a hierarchy: a few long main cracks, thinner branches at acute angles, hairlines;
    /// - taper: wide where a crack starts, a hairline at its tip;
    /// - two scales of wander: a gentle bend over its length and a small zigzag;
    /// - later cracks stop where they meet an earlier one (T junctions) instead of closing into a net;
    /// - depth: a dark core with a light chipped lip on one side and flakes broken off beside it,
    ///   so a crack shows on light and dark tiles alike.
    /// In the middle the cracks run along the fragment outlines the board is cut into, so the seam of a sinking
    /// fragment and the light that later stands in the seams both lie in a crack.
    /// Channels: R core, G when the crack reaches the pixel (0..0.5 first chorus, 0.5..1 second),
    /// B light lip and flakes, A rank (1 main crack, 0.6 branch, 0.3 hairline).
    /// </summary>
    public static class BoardCrackBaker
    {
        public const string TexturePath = "Assets/_Project/Art/Textures/BoardCrackMap.png";
        private const int Size = 2048;
        private const float Half = 4, PixelsPerUnit = Size / (2 * Half), Step = .025f;
        private const float FirstEnd = .47f, SecondStart = .52f, SecondEnd = .97f;
        // The side of a crack that catches the light: toward the upper left of the board view.
        private static readonly Vector2 LightSide = new Vector2(-.6f, .8f);

        private sealed class Crack
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public readonly List<float> Widths = new List<float>(), Times = new List<float>();
            public float Rank; public bool Second; public int Id;
        }

        private static System.Random random;
        private static float Next() => (float)random.NextDouble();
        private static float Range(float a, float b) => a + (b - a) * Next();

        // Points already laid down, by grid cell, for the T-junction test.
        private const float GridCell = .05f;
        private const int GridSize = (int)(2 * Half / GridCell) + 1;
        private static List<(Vector2 point, int id)>[] grid;
        private static readonly List<Crack> cracks = new List<Crack>();
        private const int Mosaic = -1;

        [MenuItem("String As a Sword/Art/Bake Board Crack Map")]
        public static Texture2D Bake()
        {
            random = new System.Random(20241024);
            cracks.Clear();
            grid = new List<(Vector2, int)>[GridSize * GridSize];
            var flakes = new List<(Vector2[] outline, float time)>();

            BuildMiddle();
            // Outside the cut the main cracks carry on toward the board's edge, branching as they go.
            foreach (var main in cracks.Where(c => c.Rank >= 1 && !c.Second).ToList()) ContinueOutward(main);
            Normalize(false);

            SecondChorus();
            Normalize(true);

            foreach (var crack in cracks) AddFlakes(crack, flakes);
            var texture = Rasterize(flakes);
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            File.WriteAllBytes(TexturePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(TexturePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Debug.Log($"Board crack map baked: {cracks.Count} cracks, {flakes.Count} flakes.");
            return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        }

        // ---- The middle: cracks along the fragment outlines ----

        private static void BuildMiddle()
        {
            const float cut = BoardWorldPresenter.FragmentHalf;
            var keys = new Dictionary<long, int>(); var vertices = new List<Vector2>();
            int Vertex(Vector2 p)
            {
                long key = (long)Mathf.RoundToInt((p.x + 8) * 500) * 100000 + Mathf.RoundToInt((p.y + 8) * 500);
                if (!keys.TryGetValue(key, out int index)) { index = vertices.Count; keys[key] = index; vertices.Add(p); }
                return index;
            }
            var edges = new HashSet<(int, int)>();
            // Edges of a fragment that settles at the second break: its seam opens there, so a crack must lie on it.
            var settling = new HashSet<(int, int)>();
            var outlines = BoardWorldPresenter.FragmentOutlines();
            for (int f = 0; f < outlines.Count; f++)
            {
                var outline = outlines[f];
                for (int i = 0; i < outline.Count; i++)
                {
                    Vector2 a = outline[i], b = outline[(i + 1) % outline.Count], middle = (a + b) / 2;
                    // The border of the cut is a tile line, and drawing it would draw the cut's square.
                    if (Mathf.Max(Mathf.Abs(middle.x), Mathf.Abs(middle.y)) > cut - .01f || (b - a).sqrMagnitude < .0004f) continue;
                    int ia = Vertex(a), ib = Vertex(b);
                    var edge = ia < ib ? (ia, ib) : (ib, ia);
                    edges.Add(edge);
                    if (BoardWorldPresenter.FragmentSettles(f)) settling.Add(edge);
                }
            }
            // Distance from the impact through the cracks; an edge that runs across the radius costs more,
            // so the shortest paths out are the fan of main cracks.
            var links = vertices.Select(_ => new List<(int to, float cost)>()).ToArray();
            foreach (var (a, b) in edges)
            {
                Vector2 along = vertices[b] - vertices[a], middle = (vertices[a] + vertices[b]) / 2;
                float radial = middle.sqrMagnitude > .0001f ? Mathf.Abs(Vector2.Dot(along.normalized, middle.normalized)) : 1;
                float cost = along.magnitude * (1 + 2.2f * (1 - radial));
                links[a].Add((b, cost)); links[b].Add((a, cost));
            }
            var distance = Enumerable.Repeat(float.MaxValue, vertices.Count).ToArray();
            var previous = Enumerable.Repeat(-1, vertices.Count).ToArray();
            var open = new SortedSet<(float, int)>();
            for (int i = 0; i < vertices.Count; i++)
                if (vertices[i].magnitude < .45f) { distance[i] = vertices[i].magnitude; open.Add((distance[i], i)); }
            while (open.Count > 0)
            {
                var (d, v) = open.Min; open.Remove(open.Min);
                foreach (var (to, cost) in links[v])
                    if (d + cost < distance[to]) { open.Remove((distance[to], to)); distance[to] = d + cost; previous[to] = v; open.Add((distance[to], to)); }
            }
            // Along the cracks, without the detour penalty: how long the crack front takes to get there.
            var reach = new float[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                reach[i] = distance[i] < float.MaxValue ? PathLength(i, previous, vertices) : vertices[i].magnitude * 1.3f;

            // Eight main cracks out to the border of the cut at jittered angles.
            var border = Enumerable.Range(0, vertices.Count)
                .Where(i => Mathf.Max(Mathf.Abs(vertices[i].x), Mathf.Abs(vertices[i].y)) > cut - .01f && distance[i] < float.MaxValue).ToList();
            var mainEdges = new HashSet<(int, int)>();
            var used = new HashSet<int>();
            const int mains = 8;
            float turn = Next() * Mathf.PI * 2;
            for (int k = 0; k < mains; k++)
            {
                float angle = turn + (k + Range(-.3f, .3f)) * Mathf.PI * 2 / mains;
                var want = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                int end = border.Where(i => !used.Contains(i)).OrderByDescending(i => Vector2.Dot(vertices[i].normalized, want)).FirstOrDefault();
                used.Add(end);
                var path = new List<int>();
                for (int v = end; v >= 0; v = previous[v]) path.Add(v);
                path.Reverse();
                for (int i = 0; i + 1 < path.Count; i++) { int a = path[i], b = path[i + 1]; mainEdges.Add(a < b ? (a, b) : (b, a)); }
                // One crack along the whole path, so its width tapers along it and it carries on past the cut.
                var main = NewCrack(1, false);
                for (int i = 0; i + 1 < path.Count; i++)
                    AddJagged(main, vertices[path[i]], vertices[path[i + 1]], reach[path[i]], reach[path[i + 1]], .011f);
                float length = reach[end];
                for (int i = 0; i < main.Points.Count; i++)
                    main.Widths.Add(Mathf.Lerp(.034f, .017f, Mathf.Clamp01(main.Times[i] / Mathf.Max(length, .01f))) * Wobble(main, i));
            }
            // The rest of the outlines. Around the impact they break at once with the main cracks, and so do
            // a few that run around the centre and tie the main cracks together like a web. The others open
            // with the second chorus, from the centre outward.
            foreach (var edge in edges)
            {
                if (mainEdges.Contains(edge)) continue;
                var (a, b) = edge;
                Vector2 pa = vertices[a], pb = vertices[b], middle = (pa + pb) / 2;
                float radial = Mathf.Abs(Vector2.Dot((pb - pa).normalized, middle.normalized));
                bool impact = middle.magnitude < .75f;
                bool web = middle.magnitude > .9f && middle.magnitude < 2.3f && radial < .45f && Next() < .55f;
                bool first = impact || web;
                // Shattered all over near the impact; further out only some outlines open, or the middle reads as a net.
                bool seam = settling.Contains(edge);
                if (!first && !seam && middle.magnitude > 1.5f && Next() > .4f) continue;
                var crack = NewCrack(first ? .6f : .5f, !first);
                // Grows from its end nearer the impact.
                if (reach[a] > reach[b]) { (a, b) = (b, a); (pa, pb) = (pb, pa); }
                // A seam breaks early in the second chorus: the fragment starts to settle as soon as it begins.
                float early = seam && !first ? .3f : 1;
                AddJagged(crack, pa, pb, reach[a] * early, reach[b] * early, seam ? .004f : .005f);
                float width = first ? Range(.010f, .015f) : seam ? Range(.009f, .012f) : Range(.005f, .011f);
                for (int i = 0; i < crack.Points.Count; i++) crack.Widths.Add(width * Wobble(crack, i));
            }
            foreach (var crack in cracks) Mark(crack, Mosaic);
        }

        private static float PathLength(int vertex, int[] previous, List<Vector2> vertices)
        {
            float length = 0; int v = vertex;
            for (; previous[v] >= 0; v = previous[v]) length += (vertices[v] - vertices[previous[v]]).magnitude;
            // The path starts at a vertex near the impact, which is as far from it as it lies from the centre.
            return length + vertices[v].magnitude;
        }

        // A straight fragment edge as a crack: subdivided, with a small zigzag that leaves its ends where they are.
        // The zigzag stays inside the crack's width, so the straight seam of a sinking fragment never shows beside it.
        private static void AddJagged(Crack crack, Vector2 a, Vector2 b, float ta, float tb, float amplitude)
        {
            Vector2 along = b - a; float length = along.magnitude;
            Vector2 across = new Vector2(-along.y, along.x) / Mathf.Max(length, 1e-5f);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / Step));
            float seed = Next() * 100;
            for (int i = 0; i <= steps; i++)
            {
                // Joined to the edge before it: that edge already ended on this corner.
                if (i == 0 && crack.Points.Count > 0) continue;
                float s = (float)i / steps, envelope = Mathf.Sin(Mathf.PI * s);
                float offset = envelope * amplitude * (Fbm(s * length * 7 + seed) + (i > 0 && i < steps ? Range(-.4f, .4f) : 0));
                crack.Points.Add(a + along * s + across * offset);
                crack.Times.Add(Mathf.Lerp(ta, tb, s));
            }
        }

        // ---- Free cracks: grown step by step ----

        private static void ContinueOutward(Crack main)
        {
            Vector2 from = main.Points[main.Points.Count - 1];
            float time = main.Times[main.Times.Count - 1], width = main.Widths[main.Widths.Count - 1];
            float toEdge = Half - Mathf.Max(Mathf.Abs(from.x), Mathf.Abs(from.y));
            // Most run off the edge of the board, still a little open there; a few die out on the way.
            float length = toEdge * (Next() < .7f ? Range(1.6f, 2.3f) : Range(.45f, .9f));
            float heading = Mathf.Atan2(from.y, from.x) + Range(-.25f, .25f);
            Grow(main, from, heading, length, width, .0035f, time, true, 1, 0, .035f);
        }

        /// <summary>Walks a crack from a point: a gentle bend over its length, a small zigzag, now and then a kink.
        /// Stops at the board's edge, at its length, or where it meets another crack (a T junction).</summary>
        private static Crack Grow(Crack into, Vector2 from, float heading, float length, float startWidth, float tipWidth,
            float startTime, bool outward, float rank, int ignoreId, float branchChance, bool second = false)
        {
            var crack = into ?? NewCrack(rank, second);
            if (into == null) { crack.Points.Add(from); crack.Times.Add(startTime); crack.Widths.Add(startWidth); }
            float seed = Next() * 100, bend = Range(18, 40) * Mathf.Deg2Rad;
            var spine = from; float travelled = 0, baseHeading = heading;
            var branches = new List<(Vector2 at, float heading, float length, float width, float time)>();
            for (int step = 1; travelled < length; step++)
            {
                travelled += Step;
                if (Next() < .045f) baseHeading += Range(.17f, .45f) * (Next() < .5f ? -1 : 1);
                if (outward) baseHeading = Mathf.LerpAngle(baseHeading * Mathf.Rad2Deg, Mathf.Atan2(spine.y, spine.x) * Mathf.Rad2Deg, .025f) * Mathf.Deg2Rad;
                float angle = baseHeading + (Mathf.PerlinNoise(travelled * .9f, seed) - .5f) * 2 * bend;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                spine += direction * Step;
                var point = spine + new Vector2(-direction.y, direction.x) * Range(-.004f, .004f);
                if (Mathf.Abs(point.x) > Half - .005f || Mathf.Abs(point.y) > Half - .005f) break;
                float progress = Mathf.Clamp01(travelled / length);
                float width = Mathf.Lerp(startWidth, tipWidth, Mathf.Pow(progress, .75f));
                // Starting on another crack is allowed; meeting one further on ends this one there.
                bool hit = Hit(point, crack.Id, step < 5 ? ignoreId : int.MinValue, step < 5, out var at);
                crack.Points.Add(hit ? at : point); crack.Times.Add(startTime + travelled); crack.Widths.Add(width * Wobble(crack, crack.Points.Count - 1));
                if (hit) break;
                if (Next() < branchChance && width > tipWidth * 2.2f && step > 6)
                    branches.Add((point, angle + Range(.35f, .8f) * (Next() < .5f ? -1 : 1), (length - travelled) * Range(.25f, .6f), width * .62f, startTime + travelled));
            }
            Mark(crack, crack.Id);
            foreach (var b in branches)
                Grow(null, b.at, b.heading, b.length, b.width, tipWidth, b.time, false, rank * .6f, crack.Id, branchChance * .4f, crack.Second);
            return crack;
        }

        private static void SecondChorus()
        {
            var first = cracks.Where(c => !c.Second).ToList();
            // Times here are raw distances from the impact, like those of the fragment outlines; Normalize spreads them.
            // Branches off the cracks outside the cut, at acute angles or straight across.
            var outer = first.Where(c => c.Rank >= .6f).SelectMany(c => c.Points.Select((p, i) => (c, i)))
                .Where(x => Mathf.Max(Mathf.Abs(x.c.Points[x.i].x), Mathf.Abs(x.c.Points[x.i].y)) > BoardWorldPresenter.FragmentHalf + .1f).ToList();
            for (int n = 0; n < 12 && outer.Count > 0; n++)
            {
                var (c, i) = outer[random.Next(outer.Count)];
                int j = Mathf.Min(i + 1, c.Points.Count - 1), k = Mathf.Max(i - 1, 0);
                Vector2 along = c.Points[j] - c.Points[k];
                float parent = Mathf.Atan2(along.y, along.x), side = Next() < .5f ? -1 : 1;
                float angle = parent + side * (Next() < .6f ? Range(.4f, .75f) : Range(1.25f, 1.75f));
                Grow(null, c.Points[i], angle, Range(.6f, 1.9f), Mathf.Min(c.Widths[i] * .7f, .014f), .003f, c.Points[i].magnitude, false, .6f, c.Id, .02f, true);
            }
            // Long cracks in from the board's edge, as in a slab that was stressed all over.
            for (int n = 0; n < 7; n++)
            {
                int edge = random.Next(4); float along = Range(-3.6f, 3.6f);
                var from = edge switch { 0 => new Vector2(-Half + .01f, along), 1 => new Vector2(Half - .01f, along), 2 => new Vector2(along, -Half + .01f), _ => new Vector2(along, Half - .01f) };
                float inward = Mathf.Atan2(-from.y, -from.x) + Range(-.7f, .7f);
                Grow(null, from, inward, Range(.9f, 2.6f), Range(.010f, .016f), .003f, from.magnitude, false, .6f, int.MinValue, .03f, true);
            }
            // Hairlines: short and thin, off the cracks that are already there.
            var all = cracks.Where(c => c.Rank >= .5f).SelectMany(c => c.Points.Select((p, i) => (c, i))).ToList();
            for (int n = 0; n < 70; n++)
            {
                var (c, i) = all[random.Next(all.Count)];
                Vector2 p = c.Points[i];
                // Most of them near the impact, where the stone took the blow.
                if (p.magnitude > 2.5f && Next() < .55f) continue;
                Grow(null, p, Next() * Mathf.PI * 2, Range(.12f, .5f), Range(.0045f, .0065f), .0025f, p.magnitude + .2f, false, .3f, c.Id, 0, true);
            }
        }

        // Spreads the crack fronts of one chorus over its share of the map: the further a point is along
        // the cracks from the impact, the later it breaks.
        private static void Normalize(bool second)
        {
            var group = cracks.Where(c => c.Second == second).ToList();
            float start = second ? SecondStart : 0, end = second ? SecondEnd : FirstEnd;
            float low = group.SelectMany(c => c.Times).DefaultIfEmpty(0).Min(), high = group.SelectMany(c => c.Times).DefaultIfEmpty(1).Max();
            if (!second) low = 0;
            foreach (var crack in group)
                for (int i = 0; i < crack.Times.Count; i++)
                    crack.Times[i] = Mathf.Lerp(start, end, Mathf.Pow(Mathf.InverseLerp(low, high, crack.Times[i]), .85f));
        }

        // ---- Flakes: bits of the surface broken off beside the wider cracks ----

        private static void AddFlakes(Crack crack, List<(Vector2[] outline, float time)> flakes)
        {
            for (int i = 1; i < crack.Points.Count; i++)
            {
                float width = crack.Widths[i];
                if (width < .011f) continue;
                float nearImpact = Mathf.Clamp01(1.3f - crack.Points[i].magnitude / 3.2f);
                if (Next() > .035f * nearImpact * crack.Rank) continue;
                Vector2 along = (crack.Points[i] - crack.Points[i - 1]).normalized, across = new Vector2(-along.y, along.x);
                float radius = Range(.025f, .07f) * (.6f + nearImpact);
                Vector2 centre = crack.Points[i] + across * (Next() < .5f ? -1 : 1) * (width + radius * .45f);
                int corners = random.Next(6, 10);
                var outline = new Vector2[corners];
                float turn = Next() * Mathf.PI * 2;
                for (int k = 0; k < corners; k++)
                {
                    float angle = turn + (k + Range(-.3f, .3f)) * Mathf.PI * 2 / corners;
                    // Longer along the crack than across it: flakes split off the edge.
                    var r = new Vector2(Mathf.Cos(angle) * 1.4f, Mathf.Sin(angle) * .75f) * radius * Range(.55f, 1.05f);
                    outline[k] = centre + along * r.x + across * r.y;
                }
                flakes.Add((outline, crack.Times[i]));
            }
        }

        // ---- Helpers ----

        private static Crack NewCrack(float rank, bool second)
        {
            var crack = new Crack { Rank = rank, Second = second, Id = cracks.Count + 1 };
            cracks.Add(crack); return crack;
        }
        // Width varies a little along a crack, not only toward its tip.
        private static float Wobble(Crack crack, int i) => .75f + .5f * Mathf.PerlinNoise(i * .11f, crack.Id * 3.7f);
        private static float Fbm(float x) => (Mathf.PerlinNoise(x, 1.3f) - .5f) * 1.3f + (Mathf.PerlinNoise(x * 3.1f, 7.7f) - .5f) * .7f;

        private static int GridIndex(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x + Half) / GridCell), 0, GridSize - 1), y = Mathf.Clamp(Mathf.FloorToInt((p.y + Half) / GridCell), 0, GridSize - 1);
            return y * GridSize + x;
        }
        private static void Mark(Crack crack, int id)
        {
            foreach (var p in crack.Points)
            {
                int index = GridIndex(p);
                (grid[index] ?? (grid[index] = new List<(Vector2, int)>())).Add((p, id == Mosaic ? Mosaic : crack.Id));
            }
        }
        private static bool Hit(Vector2 p, int own, int ignore, bool ignoreMosaic, out Vector2 at)
        {
            at = p; float best = .022f * .022f; bool hit = false;
            int cx = Mathf.FloorToInt((p.x + Half) / GridCell), cy = Mathf.FloorToInt((p.y + Half) / GridCell);
            for (int y = cy - 1; y <= cy + 1; y++) for (int x = cx - 1; x <= cx + 1; x++)
            {
                if (x < 0 || y < 0 || x >= GridSize || y >= GridSize || grid[y * GridSize + x] == null) continue;
                foreach (var (point, id) in grid[y * GridSize + x])
                {
                    if (id == own || id == ignore || (ignoreMosaic && id == Mosaic)) continue;
                    float d = (point - p).sqrMagnitude;
                    if (d < best) { best = d; at = point; hit = true; }
                }
            }
            return hit;
        }

        // ---- Rasterizing ----

        private static float[] core, time, lip, rank;

        private static Texture2D Rasterize(List<(Vector2[] outline, float time)> flakes)
        {
            core = new float[Size * Size]; time = Enumerable.Repeat(1f, Size * Size).ToArray(); lip = new float[Size * Size]; rank = new float[Size * Size];
            // Lips and flakes first: the time of a core wins where they overlap.
            foreach (var crack in cracks)
                for (int i = 0; i + 1 < crack.Points.Count; i++)
                {
                    Vector2 a = crack.Points[i], b = crack.Points[i + 1];
                    float wa = crack.Widths[i], wb = crack.Widths[i + 1];
                    // Only the wider cracks have an edge broken enough to catch the light; on thin ones it reads as a double line.
                    if (Mathf.Max(wa, wb) < .013f) continue;
                    Vector2 along = (b - a).normalized, across = new Vector2(-along.y, along.x);
                    if (Vector2.Dot(across, LightSide) < 0) across = -across;
                    // A wide lit lip on one side, a hair of one on the other.
                    Capsule(a + across * wa * 1.25f, b + across * wb * 1.25f, wa * .55f, wb * .55f, crack.Times[i], crack.Times[i + 1], lip, .85f, 0);
                    Capsule(a - across * wa * 1.1f, b - across * wb * 1.1f, wa * .2f, wb * .2f, crack.Times[i], crack.Times[i + 1], lip, .45f, 0);
                }
            foreach (var (outline, t) in flakes) Polygon(outline, t);
            foreach (var crack in cracks)
                for (int i = 0; i + 1 < crack.Points.Count; i++)
                    Capsule(crack.Points[i], crack.Points[i + 1], crack.Widths[i], crack.Widths[i + 1], crack.Times[i], crack.Times[i + 1], core, 1, crack.Rank);
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(Byte(core[i]), Byte(core[i] > 0 || lip[i] > 0 ? time[i] : 1), Byte(lip[i]), Byte(rank[i]));
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            texture.SetPixels32(pixels); texture.Apply();
            core = time = lip = rank = null;
            return texture;
        }
        private static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255);

        // A segment with a width that changes along it, anti-aliased over one pixel.
        private static void Capsule(Vector2 a, Vector2 b, float wa, float wb, float ta, float tb, float[] target, float strength, float crackRank)
        {
            float reach = Mathf.Max(wa, wb) + 1.5f / PixelsPerUnit;
            int x0 = Pixel(Mathf.Min(a.x, b.x) - reach), x1 = Pixel(Mathf.Max(a.x, b.x) + reach);
            int y0 = Pixel(Mathf.Min(a.y, b.y) - reach), y1 = Pixel(Mathf.Max(a.y, b.y) + reach);
            Vector2 ab = b - a; float lengthSq = Mathf.Max(ab.sqrMagnitude, 1e-10f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2((x + .5f) / PixelsPerUnit - Half, (y + .5f) / PixelsPerUnit - Half);
                float h = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                float d = (p - a - ab * h).magnitude, w = Mathf.Lerp(wa, wb, h);
                // A crack thinner than a pixel is drawn one pixel wide and fainter, not broken into dots.
                float shown = Mathf.Max(w, .5f / PixelsPerUnit);
                float coverage = Mathf.Clamp01((shown - d) * PixelsPerUnit + .5f) * Mathf.Min(1, w / shown) * strength;
                if (coverage <= 0) continue;
                int index = y * Size + x;
                if (coverage > target[index]) target[index] = coverage;
                time[index] = Mathf.Min(time[index], Mathf.Lerp(ta, tb, h));
                if (crackRank > rank[index]) rank[index] = crackRank;
            }
        }
        private static void Polygon(Vector2[] outline, float t)
        {
            float minX = outline.Min(p => p.x), maxX = outline.Max(p => p.x), minY = outline.Min(p => p.y), maxY = outline.Max(p => p.y);
            for (int y = Pixel(minY); y <= Pixel(maxY); y++) for (int x = Pixel(minX); x <= Pixel(maxX); x++)
            {
                // Four samples per pixel for a soft edge.
                int inside = 0;
                for (int s = 0; s < 4; s++)
                    if (Inside(outline, new Vector2((x + .25f + .5f * (s & 1)) / PixelsPerUnit - Half, (y + .25f + .5f * (s >> 1)) / PixelsPerUnit - Half))) inside++;
                if (inside == 0) continue;
                int index = y * Size + x;
                lip[index] = Mathf.Max(lip[index], inside / 4f);
                time[index] = Mathf.Min(time[index], t);
            }
        }
        private static bool Inside(Vector2[] outline, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                if ((outline[i].y > p.y) != (outline[j].y > p.y) &&
                    p.x < (outline[j].x - outline[i].x) * (p.y - outline[i].y) / (outline[j].y - outline[i].y) + outline[i].x) inside = !inside;
            return inside;
        }
        private static int Pixel(float coordinate) => Mathf.Clamp(Mathf.FloorToInt((coordinate + Half) * PixelsPerUnit), 0, Size - 1);
    }
}
