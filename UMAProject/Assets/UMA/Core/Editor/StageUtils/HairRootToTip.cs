using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Works out where each vertex of a hair slot lies along its card, from 0 at the root (where the card meets the
    /// head) to 1 at the tip. The vertex editor stage writes the result as vertex colors, black at the root and
    /// white at the tip, which is what the hair shader's sway (UMAHairControl) and its root-to-tip shading read.
    ///
    /// The value is the distance walked over the card from its root edge, so a card that curves or bends keeps
    /// an even gradient, and every vertex across the width of a card gets the same value at the same height.
    /// Measured against the hand-made colors of the UMA hair slots, this differs by 0.02 to 0.03 on average.
    /// </summary>
    public static class HairRootToTip
    {
        /// <summary>The value of a vertex the calculation did not reach. Such vertices keep their color.</summary>
        public const float Unset = float.NaN;

        /// <summary>
        /// The cards of a hair mesh as a graph: vertices joined along triangle edges, with coincident vertices
        /// (split for UVs or normals) joined at no distance, so a card split at a seam is still one card.
        /// </summary>
        public sealed class CardGraph
        {
            private readonly int[] edgeStart;
            private readonly int[] edgeTarget;
            private readonly float[] edgeLength;

            /// <summary>Number of vertices.</summary>
            public int VertexCount { get; }

            /// <summary>The card each vertex belongs to, or -1 for a vertex that is in no triangle.</summary>
            public int[] Island { get; }

            /// <summary>Number of cards (connected pieces of the mesh).</summary>
            public int IslandCount { get; }

            /// <param name="positions">Vertex positions, all in one space.</param>
            /// <param name="triangles">Triangle list (three indices per triangle) of the hair.</param>
            /// <param name="weldDistance">Vertices closer than this count as one point.</param>
            public CardGraph(Vector3[] positions, int[] triangles, float weldDistance = 1e-5f)
            {
                if (positions == null) throw new ArgumentNullException(nameof(positions));
                if (triangles == null) throw new ArgumentNullException(nameof(triangles));
                int count = positions.Length;
                VertexCount = count;

                var neighbors = new List<int>[count];
                var used = new bool[count];
                void Link(int a, int b)
                {
                    if (a == b) return;
                    List<int> list = neighbors[a] ?? (neighbors[a] = new List<int>(6));
                    if (!list.Contains(b))
                    {
                        list.Add(b);
                        (neighbors[b] ?? (neighbors[b] = new List<int>(6))).Add(a);
                    }
                }

                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    if ((uint)a >= (uint)count || (uint)b >= (uint)count || (uint)c >= (uint)count) continue;
                    used[a] = used[b] = used[c] = true;
                    Link(a, b);
                    Link(b, c);
                    Link(c, a);
                }

                // Coincident vertices: joined at no distance.
                float cell = Mathf.Max(weldDistance, 1e-7f);
                var buckets = new Dictionary<Vector3Int, List<int>>();
                for (int i = 0; i < count; i++)
                {
                    if (!used[i]) continue;
                    Vector3 p = positions[i];
                    var key = new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                    for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (!buckets.TryGetValue(new Vector3Int(key.x + x, key.y + y, key.z + z), out List<int> near)) continue;
                        for (int n = 0; n < near.Count; n++)
                        {
                            if ((positions[near[n]] - p).sqrMagnitude <= weldDistance * weldDistance) Link(i, near[n]);
                        }
                    }
                    if (!buckets.TryGetValue(key, out List<int> own)) buckets[key] = own = new List<int>(2);
                    own.Add(i);
                }

                edgeStart = new int[count + 1];
                for (int i = 0; i < count; i++) edgeStart[i + 1] = edgeStart[i] + (neighbors[i]?.Count ?? 0);
                edgeTarget = new int[edgeStart[count]];
                edgeLength = new float[edgeStart[count]];
                for (int i = 0; i < count; i++)
                {
                    List<int> list = neighbors[i];
                    if (list == null) continue;
                    for (int n = 0; n < list.Count; n++)
                    {
                        edgeTarget[edgeStart[i] + n] = list[n];
                        edgeLength[edgeStart[i] + n] = (positions[list[n]] - positions[i]).magnitude;
                    }
                }

                Island = new int[count];
                for (int i = 0; i < count; i++) Island[i] = -1;
                var stack = new Stack<int>();
                int islands = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!used[i] || Island[i] >= 0) continue;
                    Island[i] = islands;
                    stack.Push(i);
                    while (stack.Count > 0)
                    {
                        int v = stack.Pop();
                        for (int e = edgeStart[v]; e < edgeStart[v + 1]; e++)
                        {
                            int w = edgeTarget[e];
                            if (Island[w] >= 0) continue;
                            Island[w] = islands;
                            stack.Push(w);
                        }
                    }
                    islands++;
                }
                IslandCount = islands;
            }

            /// <summary>The vertices of each card.</summary>
            public List<int>[] GetIslandMembers()
            {
                var members = new List<int>[IslandCount];
                for (int i = 0; i < IslandCount; i++) members[i] = new List<int>();
                for (int v = 0; v < VertexCount; v++)
                {
                    if (Island[v] >= 0) members[Island[v]].Add(v);
                }
                return members;
            }

            /// <summary>
            /// The shortest distance over the mesh from the nearest of <paramref name="seeds"/> to every vertex;
            /// <see cref="float.PositiveInfinity"/> where no seed can be reached.
            /// </summary>
            public float[] DistancesFrom(IEnumerable<int> seeds)
            {
                var distance = new float[VertexCount];
                for (int i = 0; i < distance.Length; i++) distance[i] = float.PositiveInfinity;
                Walk(seeds, distance, null);
                return distance;
            }

            /// <summary>
            /// Like <see cref="DistancesFrom"/>, into <paramref name="distance"/>, which must hold
            /// <see cref="float.PositiveInfinity"/> for every vertex the walk can reach. The vertices given a
            /// distance are added to <paramref name="reached"/>, so the caller can reset only those.
            /// </summary>
            internal void Walk(IEnumerable<int> seeds, float[] distance, List<int> reached)
            {
                var heap = heapScratch ?? (heapScratch = new MinHeap(64));
                heap.Clear();
                foreach (int seed in seeds)
                {
                    if ((uint)seed >= (uint)VertexCount || distance[seed] == 0f) continue;
                    if (float.IsPositiveInfinity(distance[seed])) reached?.Add(seed);
                    distance[seed] = 0f;
                    heap.Push(0f, seed);
                }
                while (heap.Count > 0)
                {
                    heap.Pop(out float d, out int v);
                    if (d > distance[v]) continue;
                    for (int e = edgeStart[v]; e < edgeStart[v + 1]; e++)
                    {
                        int w = edgeTarget[e];
                        float next = d + edgeLength[e];
                        if (next < distance[w])
                        {
                            if (float.IsPositiveInfinity(distance[w])) reached?.Add(w);
                            distance[w] = next;
                            heap.Push(next, w);
                        }
                    }
                }
            }

            private MinHeap heapScratch;
        }

        /// <summary>
        /// The surface of the head the hair grows from, for finding how far a point is from it. It can be made of
        /// several meshes (a UMA 3 head is two body tiles, the cranium and the face). The distance is negative
        /// below the surface (behind the faces), where card roots usually end.
        /// </summary>
        public sealed class HeadSurface
        {
            /// <summary>
            /// How far behind the surface a point still counts as inside the head. A head mesh is open (at the neck,
            /// the eyes, between tiles), and a point beyond an open edge is "behind" the faces nearest to it without
            /// being in the head: hair hanging below the head would look as if it grew there. No hair root goes
            /// this deep, so a point further behind is taken to be outside.
            /// </summary>
            public const float MaxDepth = 0.03f;

            private readonly Vector3[] positions;
            private readonly int[] triangles;
            private readonly Vector3[] faceNormals;
            private readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            private readonly float cellSize;
            private readonly Bounds bounds;

            /// <summary>Number of triangles of the surface.</summary>
            public int TriangleCount => faceNormals.Length;

            public HeadSurface(Vector3[] positions, int[] triangles)
                : this(new[] { positions }, new[] { triangles })
            {
            }

            /// <summary>A surface made of several meshes, all in the same space as the hair.</summary>
            public HeadSurface(IList<Vector3[]> meshPositions, IList<int[]> meshTriangles)
            {
                if (meshPositions == null) throw new ArgumentNullException(nameof(meshPositions));
                if (meshTriangles == null || meshTriangles.Count != meshPositions.Count) throw new ArgumentException("One triangle list is needed per mesh.", nameof(meshTriangles));
                var allPositions = new List<Vector3>();
                var allTriangles = new List<int>();
                for (int m = 0; m < meshPositions.Count; m++)
                {
                    Vector3[] meshVertices = meshPositions[m];
                    int[] meshTris = meshTriangles[m];
                    if (meshVertices == null || meshTris == null) continue;
                    int offset = allPositions.Count;
                    allPositions.AddRange(meshVertices);
                    for (int t = 0; t + 2 < meshTris.Length; t += 3)
                    {
                        int a = meshTris[t], b = meshTris[t + 1], c = meshTris[t + 2];
                        if ((uint)a >= (uint)meshVertices.Length || (uint)b >= (uint)meshVertices.Length || (uint)c >= (uint)meshVertices.Length) continue;
                        allTriangles.Add(a + offset);
                        allTriangles.Add(b + offset);
                        allTriangles.Add(c + offset);
                    }
                }
                positions = allPositions.ToArray();
                triangles = allTriangles.ToArray();
                int count = triangles.Length / 3;
                faceNormals = new Vector3[count];
                double edgeSum = 0d;
                int valid = 0;
                bool first = true;
                for (int t = 0; t < count; t++)
                {
                    Vector3 a = positions[triangles[t * 3]], b = positions[triangles[t * 3 + 1]], c = positions[triangles[t * 3 + 2]];
                    faceNormals[t] = Vector3.Cross(b - a, c - a).normalized;
                    edgeSum += (b - a).magnitude + (c - b).magnitude + (a - c).magnitude;
                    valid++;
                    if (first) { bounds = new Bounds(a, Vector3.zero); first = false; }
                    bounds.Encapsulate(a);
                    bounds.Encapsulate(b);
                    bounds.Encapsulate(c);
                }
                // Cells about twice the mean edge: few triangles per cell, few cells per query.
                cellSize = valid > 0 ? Mathf.Max((float)(edgeSum / (valid * 3)) * 2f, 1e-4f) : 1f;
                for (int t = 0; t < count; t++)
                {
                    Vector3 a = positions[triangles[t * 3]], b = positions[triangles[t * 3 + 1]], c = positions[triangles[t * 3 + 2]];
                    Vector3Int min = Cell(Vector3.Min(a, Vector3.Min(b, c)));
                    Vector3Int max = Cell(Vector3.Max(a, Vector3.Max(b, c)));
                    for (int x = min.x; x <= max.x; x++)
                    for (int y = min.y; y <= max.y; y++)
                    for (int z = min.z; z <= max.z; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>(4);
                        list.Add(t);
                    }
                }
            }

            private Vector3Int Cell(Vector3 p) =>
                new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));

            /// <summary>
            /// Distance from <paramref name="point"/> to the nearest point of the surface: negative behind the
            /// surface (up to <see cref="MaxDepth"/>), positive in front of it. Points further than
            /// <paramref name="maxDistance"/> from the surface return <see cref="float.PositiveInfinity"/>.
            /// </summary>
            public float SignedDistance(Vector3 point, float maxDistance = 1f)
            {
                if (faceNormals.Length == 0) return float.PositiveInfinity;
                Bounds grown = bounds;
                grown.Expand(maxDistance * 2f);
                if (!grown.Contains(point)) return float.PositiveInfinity;

                Vector3Int center = Cell(point);
                float best = float.PositiveInfinity;
                float bestSign = 1f;
                int maxRing = Mathf.CeilToInt(maxDistance / cellSize) + 1;
                for (int ring = 0; ring <= maxRing; ring++)
                {
                    // Every point in ring r is at least (r - 1) cells away.
                    if ((ring - 1) * cellSize > best) break;
                    for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                    for (int z = -ring; z <= ring; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) != ring) continue;
                        if (!cells.TryGetValue(new Vector3Int(center.x + x, center.y + y, center.z + z), out List<int> list)) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            int t = list[i];
                            Vector3 closest = ClosestPointOnTriangle(point, positions[triangles[t * 3]], positions[triangles[t * 3 + 1]], positions[triangles[t * 3 + 2]]);
                            float distance = (point - closest).magnitude;
                            if (distance < best - 1e-7f)
                            {
                                best = distance;
                                bestSign = Vector3.Dot(point - closest, faceNormals[t]) < 0f ? -1f : 1f;
                            }
                        }
                    }
                }
                if (best > maxDistance) return float.PositiveInfinity;
                return bestSign < 0f && best <= MaxDepth ? -best : best;
            }
        }

        /// <summary>
        /// How to tell the root end of a card from its tip when the card does not go into the head and both its
        /// ends are about as near the head (hair lying along the skull).
        /// </summary>
        public enum EndRule
        {
            /// <summary>
            /// The root is at the top of the texture (the higher UV v), as hair textures are drawn. On the UMA hair
            /// slots this picked the right end on every card.
            /// </summary>
            TextureTop,
            /// <summary>The root is the higher end. Right for hanging hair, wrong for hair that sticks up.</summary>
            HigherEnd,
            /// <summary>The root is the end nearer the head, however small the difference.</summary>
            NearerHead,
        }

        /// <summary>Settings for <see cref="FromHead"/>.</summary>
        public struct HeadSettings
        {
            /// <summary>Cards whose nearest vertex is further from the head than this are skipped.</summary>
            public float maxHeadDistance;
            /// <summary>
            /// A card that does not go into the head is rooted at the end nearer the head when its two ends differ
            /// in distance from the head by more than this.
            /// </summary>
            public float clearEndDifference;
            /// <summary>How the root end is chosen when the ends do not differ by that much.</summary>
            public EndRule tieRule;
            /// <summary>The up direction, in the space of the positions, for <see cref="EndRule.HigherEnd"/>.</summary>
            public Vector3 up;
            /// <summary>
            /// How deep a vertex has to be in the head for its card to count as growing from there. Hair that only
            /// brushes the head is a little under its surface in places, and must not be rooted there.
            /// </summary>
            public float intersectionDepth;
            /// <summary>The part of a card, from each end, in which an intersection or a root edge is looked for.</summary>
            public float endFraction;
            /// <summary>
            /// With <see cref="EndRule.TextureTop"/>: a card that goes into the head at the end the texture says is
            /// its tip is not rooted there (hair tucked under the jaw or against the neck), and is decided like a card
            /// that does not go into the head.
            /// </summary>
            public bool textureOverridesTipContact;

            /// <summary>Settings that suit the UMA hair slots.</summary>
            public static HeadSettings Default => new HeadSettings
            {
                maxHeadDistance = 0.05f,
                clearEndDifference = 0.01f,
                tieRule = EndRule.TextureTop,
                up = Vector3.up,
                intersectionDepth = 0.0005f,
                endFraction = 0.25f,
                textureOverridesTipContact = true,
            };
        }

        /// <summary>What <see cref="FromHead"/> found.</summary>
        public struct HeadReport
        {
            /// <summary>Cards that got values.</summary>
            public int Cards;
            /// <summary>Cards rooted where one of their ends goes into the head.</summary>
            public int CardsIntoHead;
            /// <summary>Cards rooted at the end clearly nearer the head.</summary>
            public int CardsByNearerEnd;
            /// <summary>Cards whose ends are about as near the head, rooted by the tie rule. Worth checking.</summary>
            public int CardsByTieRule;
            /// <summary>Cards left unchanged, because no part of them is near the head.</summary>
            public int CardsTooFarFromHead;
            /// <summary>Vertices that got values.</summary>
            public int Vertices;
            /// <summary>For each vertex, how its card was rooted: 0 not set, 1 into head, 2 nearer end, 3 tie rule.</summary>
            public byte[] VertexRule;
        }

        /// <summary>
        /// Values from 0 at the root to 1 at the tip for every card of a hair mesh, finding each root on the head.
        /// When a card goes into the head (deeper than <see cref="HeadSettings.intersectionDepth"/>) at one of its
        /// ends, the part of it in the head is the root. Otherwise the two ends of the card's longest walk are
        /// compared: the end clearly nearer the head is the root, and when neither is,
        /// <see cref="HeadSettings.tieRule"/> decides. From the root the card is walked to the vertex furthest
        /// from it, which becomes the tip.
        ///
        /// A card that goes into the head only in its middle (hair curling under the jaw) is not rooted there: from
        /// a root the card reaches its full length, from the middle only about half of it.
        /// </summary>
        /// <param name="graph">The cards.</param>
        /// <param name="positions">Vertex positions, in the same space as the head surface.</param>
        /// <param name="uvs">Texture coordinates, for <see cref="EndRule.TextureTop"/>; may be null.</param>
        /// <param name="head">The head the hair grows from.</param>
        public static float[] FromHead(CardGraph graph, Vector3[] positions, Vector2[] uvs, HeadSurface head, HeadSettings settings, out HeadReport report)
        {
            report = default;
            int count = graph.VertexCount;
            report.VertexRule = new byte[count];
            var values = new float[count];
            for (int i = 0; i < count; i++) values[i] = Unset;

            float[] height = new float[count];
            for (int i = 0; i < count; i++)
            {
                height[i] = graph.Island[i] >= 0 ? head.SignedDistance(positions[i], settings.maxHeadDistance) : float.PositiveInfinity;
            }
            bool haveUVs = uvs != null && uvs.Length == count;
            Vector3 up = settings.up.sqrMagnitude > 0f ? settings.up.normalized : Vector3.up;

            float[] fromA = NewInfinite(count), fromB = NewInfinite(count), fromRoot = NewInfinite(count);
            var reachedA = new List<int>();
            var reachedB = new List<int>();
            var reachedRoot = new List<int>();
            var seed = new int[1];
            var roots = new List<int>();
            List<int>[] islands = graph.GetIslandMembers();
            for (int island = 0; island < islands.Length; island++)
            {
                List<int> members = islands[island];
                float lowest = float.PositiveInfinity;
                for (int m = 0; m < members.Count; m++) lowest = Mathf.Min(lowest, height[members[m]]);
                if (float.IsPositiveInfinity(lowest))
                {
                    report.CardsTooFarFromHead++;
                    continue;
                }

                roots.Clear();
                byte rule = 0;

                // A card that goes into the head at one end grows from there.
                if (lowest <= -settings.intersectionDepth)
                {
                    for (int m = 0; m < members.Count; m++)
                    {
                        if (height[members[m]] <= -settings.intersectionDepth) roots.Add(members[m]);
                    }
                    graph.Walk(roots, fromA, reachedA);
                    int far = Furthest(members, fromA);
                    float reach = fromA[far];
                    seed[0] = far;
                    graph.Walk(seed, fromB, reachedB);
                    float cardLength = fromB[Furthest(members, fromB)];
                    bool atEnd = cardLength > 0f && reach >= cardLength * InsideEndReach;
                    if (atEnd && settings.textureOverridesTipContact && settings.tieRule == EndRule.TextureTop && haveUVs)
                    {
                        // The texture's top is the root: the part in the head should not be clearly lower in it.
                        float inside = 0f;
                        for (int r = 0; r < roots.Count; r++) inside += uvs[roots[r]].y;
                        inside /= roots.Count;
                        float lowV = float.PositiveInfinity, highV = float.NegativeInfinity;
                        for (int m = 0; m < members.Count; m++)
                        {
                            lowV = Mathf.Min(lowV, uvs[members[m]].y);
                            highV = Mathf.Max(highV, uvs[members[m]].y);
                        }
                        if (uvs[far].y - inside > (highV - lowV) * 0.5f) atEnd = false;
                    }
                    if (atEnd)
                    {
                        rule = 1;
                        // The whole of that end that is in the head, also the vertices only just under its surface.
                        roots.Clear();
                        for (int m = 0; m < members.Count; m++)
                        {
                            int v = members[m];
                            if (height[v] <= 0f && fromA[v] <= cardLength * settings.endFraction) roots.Add(v);
                        }
                    }
                    else
                    {
                        roots.Clear();
                    }
                    Reset(fromA, reachedA);
                    Reset(fromB, reachedB);
                }

                if (rule == 0)
                {
                    // The two ends of the card: the ends of its longest walk.
                    seed[0] = members[0];
                    graph.Walk(seed, fromA, reachedA);
                    int a = Furthest(members, fromA);
                    Reset(fromA, reachedA);
                    seed[0] = a;
                    graph.Walk(seed, fromA, reachedA);
                    int b = Furthest(members, fromA);
                    float length = fromA[b];
                    seed[0] = b;
                    graph.Walk(seed, fromB, reachedB);
                    float endLength = length * settings.endFraction;

                    // How near each end comes to the head.
                    float aNearest = float.PositiveInfinity, bNearest = float.PositiveInfinity;
                    for (int m = 0; m < members.Count; m++)
                    {
                        int v = members[m];
                        if (fromA[v] <= endLength) aNearest = Mathf.Min(aNearest, height[v]);
                        if (fromB[v] <= endLength) bNearest = Mathf.Min(bNearest, height[v]);
                    }

                    bool rootIsA;
                    if (Mathf.Abs(aNearest - bNearest) > settings.clearEndDifference)
                    {
                        rule = 2;
                        rootIsA = aNearest < bNearest;
                    }
                    else
                    {
                        rule = 3;
                        switch (settings.tieRule)
                        {
                            case EndRule.HigherEnd:
                                rootIsA = Vector3.Dot(positions[a] - positions[b], up) >= 0f;
                                break;
                            case EndRule.NearerHead:
                                rootIsA = aNearest <= bNearest;
                                break;
                            default:
                                rootIsA = haveUVs ? uvs[a].y >= uvs[b].y : aNearest <= bNearest;
                                break;
                        }
                    }

                    int rootEnd = rootIsA ? a : b;
                    float[] fromRootEnd = rootIsA ? fromA : fromB;
                    roots.Add(rootEnd);
                    if (haveUVs)
                    {
                        AddTextureRow(members, uvs, rootEnd, fromRootEnd, endLength, roots);
                    }
                    Reset(fromA, reachedA);
                    Reset(fromB, reachedB);
                }

                if (SetFromRoots(graph, members, roots, values, fromRoot, reachedRoot))
                {
                    report.Cards++;
                    report.Vertices += members.Count;
                    if (rule == 1) report.CardsIntoHead++;
                    else if (rule == 2) report.CardsByNearerEnd++;
                    else report.CardsByTieRule++;
                    for (int m = 0; m < members.Count; m++) report.VertexRule[members[m]] = rule;
                }
            }
            return values;
        }

        /// <summary>
        /// From the part of a card in the head, the card has to reach at least this much of its length for that
        /// part to be an end of it, rather than the middle of the card touching the head.
        /// </summary>
        private const float InsideEndReach = 0.7f;

        /// <summary>
        /// The rest of a card's root edge: the vertices on the same row of the texture as its root end. A hair card
        /// runs along one axis of its texture, so its root edge is one row of it. Walking over the mesh cannot find
        /// it: a corner across the card can be further from the root end than the next row along it.
        /// </summary>
        private static void AddTextureRow(List<int> members, Vector2[] uvs, int rootEnd, float[] fromRootEnd, float endLength, List<int> roots)
        {
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity, minV = float.PositiveInfinity, maxV = float.NegativeInfinity;
            for (int m = 0; m < members.Count; m++)
            {
                Vector2 uv = uvs[members[m]];
                minU = Mathf.Min(minU, uv.x);
                maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxV = Mathf.Max(maxV, uv.y);
            }
            // The card runs along the texture axis it spans most of.
            bool alongV = maxV - minV >= maxU - minU;
            float span = alongV ? maxV - minV : maxU - minU;
            if (span <= 0f) return;
            float row = alongV ? uvs[rootEnd].y : uvs[rootEnd].x;
            float tolerance = span * TextureRowTolerance;
            for (int m = 0; m < members.Count; m++)
            {
                int v = members[m];
                if (v == rootEnd || fromRootEnd[v] > endLength) continue;
                float along = alongV ? uvs[v].y : uvs[v].x;
                if (Mathf.Abs(along - row) <= tolerance) roots.Add(v);
            }
        }

        /// <summary>How far, as a part of the card's span in the texture, a vertex can be from the root row.</summary>
        private const float TextureRowTolerance = 0.03f;

        private static float[] NewInfinite(int count)
        {
            var array = new float[count];
            for (int i = 0; i < count; i++) array[i] = float.PositiveInfinity;
            return array;
        }

        private static void Reset(float[] distance, List<int> reached)
        {
            for (int i = 0; i < reached.Count; i++) distance[reached[i]] = float.PositiveInfinity;
            reached.Clear();
        }

        private static int Furthest(List<int> members, float[] distance)
        {
            int best = members[0];
            float bestDistance = -1f;
            for (int m = 0; m < members.Count; m++)
            {
                float d = distance[members[m]];
                if (!float.IsPositiveInfinity(d) && d > bestDistance)
                {
                    bestDistance = d;
                    best = members[m];
                }
            }
            return best;
        }

        /// <summary>
        /// Values for the cards that contain one of <paramref name="roots"/>: 0 at the roots, rising with the
        /// distance walked from them, 1 at the vertex furthest from them. Other cards get <see cref="Unset"/>.
        /// </summary>
        public static float[] FromRoots(CardGraph graph, ICollection<int> roots)
        {
            var values = new float[graph.VertexCount];
            for (int i = 0; i < values.Length; i++) values[i] = Unset;
            List<int>[] islands = graph.GetIslandMembers();
            var islandRoots = new Dictionary<int, List<int>>();
            foreach (int root in roots)
            {
                if ((uint)root >= (uint)graph.VertexCount || graph.Island[root] < 0) continue;
                if (!islandRoots.TryGetValue(graph.Island[root], out List<int> list)) islandRoots[graph.Island[root]] = list = new List<int>();
                list.Add(root);
            }
            float[] distance = NewInfinite(graph.VertexCount);
            var reached = new List<int>();
            foreach (KeyValuePair<int, List<int>> pair in islandRoots)
            {
                SetFromRoots(graph, islands[pair.Key], pair.Value, values, distance, reached);
            }
            return values;
        }

        private static bool SetFromRoots(CardGraph graph, List<int> members, List<int> roots, float[] values, float[] distance, List<int> reached)
        {
            if (roots.Count == 0) return false;
            graph.Walk(roots, distance, reached);
            float furthest = 0f;
            for (int m = 0; m < members.Count; m++)
            {
                float d = distance[members[m]];
                if (!float.IsPositiveInfinity(d)) furthest = Mathf.Max(furthest, d);
            }
            if (furthest <= 0f)
            {
                Reset(distance, reached);
                return false;
            }
            for (int m = 0; m < members.Count; m++)
            {
                float d = distance[members[m]];
                values[members[m]] = float.IsPositiveInfinity(d) ? Unset : Mathf.Clamp01(d / furthest);
            }
            Reset(distance, reached);
            return true;
        }

        /// <summary>
        /// Values between marked roots and tips, for every card that has both: 0 at the roots, 1 at the tips, and
        /// in between by where a vertex lies on the way from root to tip. Vertices before the roots get 0 and past
        /// the tips 1. Cards without both get <see cref="Unset"/>. Marking a whole edge (all of a card's root
        /// vertices) gives the cleanest result; one vertex at each end works too.
        /// </summary>
        public static float[] BetweenRootsAndTips(CardGraph graph, ICollection<int> roots, ICollection<int> tips, out int cards)
        {
            cards = 0;
            var values = new float[graph.VertexCount];
            for (int i = 0; i < values.Length; i++) values[i] = Unset;

            var rootIslands = new HashSet<int>();
            foreach (int r in roots) if ((uint)r < (uint)graph.VertexCount && graph.Island[r] >= 0) rootIslands.Add(graph.Island[r]);
            var tipIslands = new HashSet<int>();
            foreach (int t in tips) if ((uint)t < (uint)graph.VertexCount && graph.Island[t] >= 0) tipIslands.Add(graph.Island[t]);
            rootIslands.IntersectWith(tipIslands);
            if (rootIslands.Count == 0) return values;

            float[] fromRoot = graph.DistancesFrom(roots);
            float[] fromTip = graph.DistancesFrom(tips);
            // Root-to-tip length of each card: the shortest way from a root to a tip.
            var span = new Dictionary<int, float>();
            foreach (int t in tips)
            {
                if ((uint)t >= (uint)graph.VertexCount) continue;
                int island = graph.Island[t];
                if (!rootIslands.Contains(island)) continue;
                float d = fromRoot[t];
                if (!span.TryGetValue(island, out float current) || d < current) span[island] = d;
            }

            for (int v = 0; v < graph.VertexCount; v++)
            {
                int island = graph.Island[v];
                if (island < 0 || !span.TryGetValue(island, out float length) || length <= 0f) continue;
                // On the way from root to tip this is the distance from the root over the length; beside it, the
                // difference of the two distances still says how far along a vertex is.
                values[v] = Mathf.Clamp01(0.5f + (fromRoot[v] - fromTip[v]) / (2f * length));
            }
            foreach (KeyValuePair<int, float> pair in span) if (pair.Value > 0f) cards++;
            return values;
        }

        /// <summary>The color for a value: black at the root, white at the tip. Alpha is kept.</summary>
        public static Color32 ToColor(float value, byte alpha)
        {
            byte shade = (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            return new Color32(shade, shade, shade, alpha);
        }

        /// <summary>Closest point to <paramref name="p"/> on the triangle a, b, c.</summary>
        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            // Real-Time Collision Detection (Ericson), 5.1.5.
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denominator = 1f / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        private sealed class MinHeap
        {
            private float[] keys;
            private int[] values;
            public int Count { get; private set; }

            public void Clear() => Count = 0;

            public MinHeap(int capacity)
            {
                keys = new float[capacity];
                values = new int[capacity];
            }

            public void Push(float key, int value)
            {
                if (Count == keys.Length)
                {
                    Array.Resize(ref keys, Count * 2);
                    Array.Resize(ref values, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (keys[parent] <= key) break;
                    keys[i] = keys[parent];
                    values[i] = values[parent];
                    i = parent;
                }
                keys[i] = key;
                values[i] = value;
            }

            public void Pop(out float key, out int value)
            {
                key = keys[0];
                value = values[0];
                Count--;
                if (Count == 0) return;
                float lastKey = keys[Count];
                int lastValue = values[Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && keys[child + 1] < keys[child]) child++;
                    if (keys[child] >= lastKey) break;
                    keys[i] = keys[child];
                    values[i] = values[child];
                    i = child;
                }
                keys[i] = lastKey;
                values[i] = lastValue;
            }
        }
    }
}
