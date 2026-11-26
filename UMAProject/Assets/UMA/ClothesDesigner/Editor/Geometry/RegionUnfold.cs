using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// A patch of surface to make into pattern pieces: positions (metres) with triangles facing out, and for each
    /// position the id of the surface vertex it is (patches that meet share ids along their common edge, which become
    /// seams). The part it lies on decides where it is cut open and how its pieces are laid out.
    /// </summary>
    public sealed class RegionPatch
    {
        public string Name = "Region";
        public BodyPart Part;
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<int> Ids = new List<int>();
        public readonly List<int> Triangles = new List<int>();
        /// <summary>The part's axis, towards its top (up the torso, head and legs; towards the shoulder on an arm).</summary>
        public Vector3 Up = Vector3.up;
        /// <summary>A point on the part's axis.</summary>
        public Vector3 AxisPoint;
        /// <summary>The side a cut is put on, out from the axis: the back of the torso and head, under an arm, inside a leg.</summary>
        public Vector3 CutSide = Vector3.back;
        /// <summary>Where the part's angle 0 is, out from the axis (the front; the top of an arm).</summary>
        public Vector3 Front = Vector3.forward;
        /// <summary>Where the part's angle 90 is, out from the axis (the body's left; the front of an arm).</summary>
        public Vector3 Ninety = Vector3.left;

        public int VertexCount => Positions.Count;

        public RegionPatch CopyFrame(string name)
        {
            return new RegionPatch { Name = name, Part = Part, Up = Up, AxisPoint = AxisPoint, CutSide = CutSide, Front = Front, Ninety = Ninety };
        }
    }

    /// <summary>One piece unrolled from a patch: its outline as drawn, and where each outline point came from.</summary>
    public sealed class FlatPiece
    {
        public string Name;
        public BodyPart Part;
        /// <summary>The outline as drawn (cm), counterclockwise seen from outside, its y up the part.</summary>
        public readonly List<Vector2> Outline = new List<Vector2>();
        /// <summary>The surface vertex id of each outline point.</summary>
        public readonly List<int> OutlineIds = new List<int>();
        /// <summary>Each outline point on the body (metres).</summary>
        public readonly List<Vector3> Outline3D = new List<Vector3>();
        /// <summary>How much the flattening stretched or squashed the cloth (fraction, the 95th percentile over its triangles).</summary>
        public float Distortion;
        /// <summary>The patch it came from, for its axis and part.</summary>
        public RegionPatch Frame;
        /// <summary>The middle of the piece on the body (metres).</summary>
        public Vector3 Centre;
        /// <summary>True when the piece was cut open from a tube (it goes all the way round its part).</summary>
        public bool WentRound;
        /// <summary>The flattened source triangles: as drawn (cm, in the outline's frame), on the body (metres) with its normals.</summary>
        public Vector2[] SourceFlat;
        public Vector3[] Source3D, SourceNormals;
        public int[] SourceTriangles;
    }

    /// <summary>
    /// Unrolls surface patches into flat pattern pieces, as Simply Cloth Studio's "Paint Cloth" makes cloth from the
    /// body, but as flat pieces with seams: a patch that goes round its part is cut open along the side it prefers
    /// (the back, under the arm, inside the leg), flattened as rigidly as it can be (ARAP: a cotangent Laplacian solved
    /// by conjugate gradients, from a harmonic map with the boundary on a circle), and split into gores round its axis
    /// where flattening would stretch the cloth more than <see cref="MaxDistortion"/>. Edges shared by two pieces (or
    /// both sides of a cut) become seams; the rest are openings.
    /// </summary>
    public static class RegionUnfold
    {
        /// <summary>Flattening may stretch or squash the cloth this much (the 95th percentile) before the patch is split into gores.</summary>
        public const float MaxDistortion = 0.06f;
        /// <summary>The most gores a patch is split into.</summary>
        public const int MaxGores = 6;
        private const int ArapIterations = 12;

        /// <summary>Unrolls a patch into one or more flat pieces. Null, with the reason, when it cannot be unrolled.</summary>
        public static List<FlatPiece> Unfold(RegionPatch patch, out string problem)
        {
            problem = null;
            var result = new List<FlatPiece>();
            if (patch == null || patch.Triangles.Count < 3) { problem = "The region is empty."; return null; }
            FlatPiece whole = UnfoldOne(patch, out problem);
            if (whole != null && whole.Distortion <= MaxDistortion) { result.Add(whole); return result; }
            string firstProblem = problem;
            // Too much stretch (a dome, a bust): gores round the axis, as many as it takes.
            foreach (int gores in new[] { 2, 3, 4, 5, MaxGores })
            {
                List<RegionPatch> sectors = SplitIntoSectors(patch, gores);
                if (sectors.Count < 2) continue;
                var pieces = new List<FlatPiece>();
                bool ok = true;
                float worst = 0f;
                foreach (RegionPatch sector in sectors)
                {
                    FlatPiece piece = UnfoldOne(sector, out string why);
                    if (piece == null) { ok = false; problem = gores + " gores: " + sector.Name + ": " + why; break; }
                    pieces.Add(piece);
                    worst = Mathf.Max(worst, piece.Distortion);
                }
                if (!ok) continue;
                if (worst <= MaxDistortion || gores == MaxGores)
                {
                    problem = worst > MaxDistortion ? "Even in " + gores + " gores the cloth stretches " + (worst * 100f).ToString("0") + "% to lie flat." : null;
                    return pieces;
                }
            }
            if (whole != null)
            {
                problem = "The cloth stretches " + (whole.Distortion * 100f).ToString("0") + "% to lie flat" + (problem != null ? " (in gores: " + problem + ")" : "") + ".";
                result.Add(whole);
                return result;
            }
            problem = firstProblem ?? problem;
            return null;
        }

        // ---- One piece ---------------------------------------------------------------------------------

        private static FlatPiece UnfoldOne(RegionPatch patch, out string problem)
        {
            problem = null;
            var mesh = new WorkMesh(patch);
            if (mesh.Triangles.Count < 3) { problem = "The region is too small."; return null; }
            List<List<int>> loops = mesh.BoundaryLoops(out bool manifold);
            if (!manifold) { problem = "The region's surface folds onto itself (paint fewer, or larger, areas)."; return null; }
            int edges = mesh.EdgeCount();
            int euler = mesh.Positions.Count - edges + mesh.Triangles.Count / 3;
            if (loops.Count == 0) { problem = "The region closes on itself: leave an opening."; return null; }
            if (euler != 2 - loops.Count) { problem = "The region wraps round in a way that cannot be unrolled: paint it in parts."; return null; }
            bool wentRound = loops.Count > 1;
            // Cut every opening to the next until there is one: then it is a disk.
            int guard = 0;
            while (loops.Count > 1 && guard++ < 16)
            {
                if (!mesh.CutBetween(loops, patch.CutSide)) { problem = "The region could not be cut open."; return null; }
                loops = mesh.BoundaryLoops(out manifold);
                if (!manifold) { problem = "Cutting the region open left a bad surface."; return null; }
            }
            if (loops.Count != 1) { problem = "The region could not be cut open."; return null; }
            List<int> loop = loops[0];
            Vector2[] flat = Flatten(mesh, loop, out float distortion);
            if (flat == null) { problem = "The region could not be flattened."; return null; }

            // Turn it so the part's top is up, and make sure it is seen from outside.
            float signed = 0f;
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                Vector2 a = flat[mesh.Triangles[t]], b = flat[mesh.Triangles[t + 1]], c = flat[mesh.Triangles[t + 2]];
                signed += (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            }
            if (signed < 0f)
            {
                // Seen from inside: mirror it, and run the outline the other way so it stays counterclockwise.
                for (int i = 0; i < flat.Length; i++) flat[i].x = -flat[i].x;
                loop.Reverse();
            }
            Vector2 up = Vector2.zero;
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                int ia = mesh.Triangles[t], ib = mesh.Triangles[t + 1], ic = mesh.Triangles[t + 2];
                up += MapDirection(mesh.Positions[ia], mesh.Positions[ib], mesh.Positions[ic], flat[ia], flat[ib], flat[ic], patch.Up);
            }
            float turn = up.sqrMagnitude > 1e-12f ? 90f - Mathf.Atan2(up.y, up.x) * Mathf.Rad2Deg : 0f;
            Quaternion q = Quaternion.Euler(0f, 0f, turn);
            var piece = new FlatPiece { Name = patch.Name, Part = patch.Part, Distortion = distortion, Frame = patch, WentRound = wentRound };
            Vector3 centre = Vector3.zero;
            foreach (Vector3 p in mesh.Positions) centre += p;
            piece.Centre = centre / Mathf.Max(1, mesh.Positions.Count);
            foreach (int v in loop)
            {
                Vector3 r = q * new Vector3(flat[v].x, flat[v].y, 0f);
                piece.Outline.Add(new Vector2(r.x, r.y) * 100f);
                piece.OutlineIds.Add(mesh.Ids[v]);
                piece.Outline3D.Add(mesh.Positions[v]);
            }
            // Keep where every part of the piece lay on the body, to lay it there again.
            int count = mesh.Positions.Count;
            piece.SourceFlat = new Vector2[count];
            piece.Source3D = mesh.Positions.ToArray();
            piece.SourceNormals = new Vector3[count];
            piece.SourceTriangles = mesh.Triangles.ToArray();
            for (int v = 0; v < count; v++)
            {
                Vector3 r = q * new Vector3(flat[v].x, flat[v].y, 0f);
                piece.SourceFlat[v] = new Vector2(r.x, r.y) * 100f;
            }
            for (int t = 0; t < mesh.Triangles.Count; t += 3)
            {
                Vector3 f = Vector3.Cross(mesh.Positions[mesh.Triangles[t + 1]] - mesh.Positions[mesh.Triangles[t]], mesh.Positions[mesh.Triangles[t + 2]] - mesh.Positions[mesh.Triangles[t]]);
                for (int k = 0; k < 3; k++) piece.SourceNormals[mesh.Triangles[t + k]] += f;
            }
            for (int v = 0; v < count; v++) piece.SourceNormals[v] = piece.SourceNormals[v].sqrMagnitude > 1e-20f ? piece.SourceNormals[v].normalized : Vector3.up;
            return piece;
        }

        /// <summary>Where a 3D direction on a triangle goes in its flat image (scaled by the triangle's area).</summary>
        private static Vector2 MapDirection(Vector3 a, Vector3 b, Vector3 c, Vector2 fa, Vector2 fb, Vector2 fc, Vector3 direction)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area = n.magnitude;
            if (area < 1e-12f) return Vector2.zero;
            n /= area;
            Vector3 d = direction - Vector3.Dot(direction, n) * n;
            // Solve d = s (b - a) + t (c - a) in the triangle's plane, then map with the flat edges.
            Vector3 e1 = b - a, e2 = c - a;
            float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2);
            float r1 = Vector3.Dot(d, e1), r2 = Vector3.Dot(d, e2);
            float det = d11 * d22 - d12 * d12;
            if (Mathf.Abs(det) < 1e-20f) return Vector2.zero;
            float s = (r1 * d22 - r2 * d12) / det, t = (r2 * d11 - r1 * d12) / det;
            return ((fb - fa) * s + (fc - fa) * t) * area;
        }

        // ---- Gores -------------------------------------------------------------------------------------

        /// <summary>
        /// Splits a patch into sectors round its axis (each triangle by the angle of its middle), the first boundary on
        /// the cut side so the seams fall there and evenly round.
        /// </summary>
        public static List<RegionPatch> SplitIntoSectors(RegionPatch patch, int count)
        {
            Vector3 up = patch.Up.normalized;
            Vector3 reference = Vector3.ProjectOnPlane(patch.CutSide, up);
            if (reference.sqrMagnitude < 1e-8f) reference = Vector3.ProjectOnPlane(Vector3.forward, up);
            reference.Normalize();
            Vector3 side = Vector3.Cross(up, reference);
            var sectors = new List<RegionPatch>();
            var maps = new List<Dictionary<int, int>>();
            for (int k = 0; k < count; k++)
            {
                sectors.Add(patch.CopyFrame(patch.Name + " " + (k + 1)));
                maps.Add(new Dictionary<int, int>());
            }
            int triangles = patch.Triangles.Count / 3;
            var sectorOf = new int[triangles];
            for (int t = 0; t < triangles; t++)
            {
                Vector3 centre = (patch.Positions[patch.Triangles[3 * t]] + patch.Positions[patch.Triangles[3 * t + 1]] + patch.Positions[patch.Triangles[3 * t + 2]]) / 3f - patch.AxisPoint;
                float angle = Mathf.Atan2(Vector3.Dot(centre, side), Vector3.Dot(centre, reference));
                if (angle < 0f) angle += 2f * Mathf.PI;
                sectorOf[t] = Mathf.Clamp(Mathf.FloorToInt(angle / (2f * Mathf.PI) * count), 0, count - 1);
            }
            // By angle alone a sector's edge is ragged, and two sectors can meet at a single point (the piece would
            // pinch there). A triangle with most of its neighbours in another sector moves there, a few times over.
            var byEdge = new Dictionary<long, List<int>>();
            for (int t = 0; t < triangles; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = patch.Triangles[3 * t + k], b = patch.Triangles[3 * t + (k + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!byEdge.TryGetValue(key, out List<int> list)) byEdge[key] = list = new List<int>(2);
                    list.Add(t);
                }
            }
            var near = new List<int>[triangles];
            for (int t = 0; t < triangles; t++) near[t] = new List<int>(3);
            foreach (List<int> list in byEdge.Values) if (list.Count == 2) { near[list[0]].Add(list[1]); near[list[1]].Add(list[0]); }
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                for (int t = 0; t < triangles; t++)
                {
                    int same = 0, best = -1, bestCount = 0;
                    foreach (int o in near[t]) if (sectorOf[o] == sectorOf[t]) same++;
                    if (same >= 2 || near[t].Count == 0) continue;
                    foreach (int o in near[t])
                    {
                        int c = 0;
                        foreach (int o2 in near[t]) if (sectorOf[o2] == sectorOf[o]) c++;
                        if (c > bestCount) { bestCount = c; best = sectorOf[o]; }
                    }
                    if (best >= 0 && best != sectorOf[t] && bestCount > same) { sectorOf[t] = best; moved = true; }
                }
                if (!moved) break;
            }
            // A sector's small separate scraps (a few triangles cut off at the crotch) join the sector round them.
            var seenScrap = new bool[triangles];
            for (int seed = 0; seed < triangles; seed++)
            {
                if (seenScrap[seed]) continue;
                var scrap = new List<int>();
                var stack = new Stack<int>();
                stack.Push(seed);
                seenScrap[seed] = true;
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    scrap.Add(t);
                    foreach (int o in near[t]) if (!seenScrap[o] && sectorOf[o] == sectorOf[seed]) { seenScrap[o] = true; stack.Push(o); }
                }
                if (scrap.Count >= 40) continue;
                var votes = new Dictionary<int, int>();
                foreach (int t in scrap) foreach (int o in near[t]) if (sectorOf[o] != sectorOf[seed]) { votes.TryGetValue(sectorOf[o], out int c); votes[sectorOf[o]] = c + 1; }
                int into = -1, most = 0;
                foreach (KeyValuePair<int, int> kv in votes) if (kv.Value > most) { most = kv.Value; into = kv.Key; }
                if (into >= 0) foreach (int t in scrap) sectorOf[t] = into;
            }
            for (int t3 = 0; t3 + 2 < patch.Triangles.Count; t3 += 3)
            {
                int t = t3;
                int k = sectorOf[t3 / 3];
                for (int corner = 0; corner < 3; corner++)
                {
                    int v = patch.Triangles[t + corner];
                    if (!maps[k].TryGetValue(v, out int local))
                    {
                        local = sectors[k].Positions.Count;
                        maps[k][v] = local;
                        sectors[k].Positions.Add(patch.Positions[v]);
                        sectors[k].Ids.Add(patch.Ids[v]);
                    }
                    sectors[k].Triangles.Add(local);
                }
            }
            sectors.RemoveAll(s => s.Triangles.Count < 9);
            // A wedge can catch two separate stretches (one by the shoulder, one by the hip): each is a piece of its own.
            var parts = new List<RegionPatch>();
            foreach (RegionPatch sector in sectors) parts.AddRange(Components(sector));
            parts.RemoveAll(s => s.Triangles.Count < 9);
            return parts;
        }

        /// <summary>A patch's connected stretches (across shared edges), each as a patch of its own.</summary>
        public static List<RegionPatch> Components(RegionPatch patch)
        {
            int triangles = patch.Triangles.Count / 3;
            var byEdge = new Dictionary<long, List<int>>();
            for (int t = 0; t < triangles; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = patch.Triangles[3 * t + k], b = patch.Triangles[3 * t + (k + 1) % 3];
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!byEdge.TryGetValue(key, out List<int> list)) byEdge[key] = list = new List<int>(2);
                    list.Add(t);
                }
            }
            var group = new int[triangles];
            for (int t = 0; t < triangles; t++) group[t] = -1;
            int groups = 0;
            for (int seed = 0; seed < triangles; seed++)
            {
                if (group[seed] >= 0) continue;
                var stack = new Stack<int>();
                stack.Push(seed);
                group[seed] = groups;
                while (stack.Count > 0)
                {
                    int t = stack.Pop();
                    for (int k = 0; k < 3; k++)
                    {
                        int a = patch.Triangles[3 * t + k], b = patch.Triangles[3 * t + (k + 1) % 3];
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        foreach (int o in byEdge[key]) if (group[o] < 0) { group[o] = groups; stack.Push(o); }
                    }
                }
                groups++;
            }
            if (groups <= 1) return new List<RegionPatch> { patch };
            var result = new List<RegionPatch>();
            for (int g = 0; g < groups; g++)
            {
                RegionPatch part = patch.CopyFrame(patch.Name + (g > 0 ? "." + (g + 1) : ""));
                var map = new Dictionary<int, int>();
                for (int t = 0; t < triangles; t++)
                {
                    if (group[t] != g) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = patch.Triangles[3 * t + k];
                        if (!map.TryGetValue(v, out int local))
                        {
                            local = part.Positions.Count;
                            map[v] = local;
                            part.Positions.Add(patch.Positions[v]);
                            part.Ids.Add(patch.Ids[v]);
                        }
                        part.Triangles.Add(local);
                    }
                }
                result.Add(part);
            }
            return result;
        }

        // ---- Flattening (ARAP) -------------------------------------------------------------------------

        /// <summary>Flattens a disk: harmonic start with the boundary on a circle, then as-rigid-as-possible steps.</summary>
        private static Vector2[] Flatten(WorkMesh mesh, List<int> loop, out float distortion)
        {
            int n = mesh.Positions.Count;
            distortion = 1f;
            var onBoundary = new bool[n];
            foreach (int v in loop) onBoundary[v] = true;
            // Boundary on a circle by arc length, its length kept.
            var flat = new Vector2[n];
            float perimeter = 0f;
            for (int i = 0; i < loop.Count; i++) perimeter += Vector3.Distance(mesh.Positions[loop[i]], mesh.Positions[loop[(i + 1) % loop.Count]]);
            if (perimeter <= 0f) return null;
            float radius = perimeter / (2f * Mathf.PI), along = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                float a = along / perimeter * 2f * Mathf.PI;
                flat[loop[i]] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                along += Vector3.Distance(mesh.Positions[loop[i]], mesh.Positions[loop[(i + 1) % loop.Count]]);
            }
            // Interior: each point the mean of its neighbours (a Tutte map: never folds).
            List<int>[] neighbours = mesh.Neighbours();
            var tutte = new SparseSystem(n);
            var bx = new double[n]; var by = new double[n];
            for (int v = 0; v < n; v++)
            {
                if (onBoundary[v]) { tutte.Add(v, v, 1.0); bx[v] = flat[v].x; by[v] = flat[v].y; continue; }
                tutte.Add(v, v, neighbours[v].Count);
                foreach (int w in neighbours[v])
                {
                    if (onBoundary[w]) { bx[v] += flat[w].x; by[v] += flat[w].y; }
                    else tutte.Add(v, w, -1.0);
                }
            }
            tutte.Finish();
            var x = new double[n]; var y = new double[n];
            for (int v = 0; v < n; v++) { x[v] = flat[v].x; y[v] = flat[v].y; }
            tutte.Solve(bx, x, 2000);
            tutte.Solve(by, y, 2000);

            // ARAP: each triangle's rest shape in its own plane, cotangent weights.
            int triangles = mesh.Triangles.Count / 3;
            var rest = new Vector2[triangles * 3];
            var cot = new double[triangles * 3];
            for (int t = 0; t < triangles; t++)
            {
                Vector3 p0 = mesh.Positions[mesh.Triangles[3 * t]], p1 = mesh.Positions[mesh.Triangles[3 * t + 1]], p2 = mesh.Positions[mesh.Triangles[3 * t + 2]];
                Vector3 e1 = p1 - p0;
                float l1 = e1.magnitude;
                Vector3 ax = l1 > 1e-12f ? e1 / l1 : Vector3.right;
                Vector3 nrm = Vector3.Cross(e1, p2 - p0);
                Vector3 ay = Vector3.Cross(nrm.sqrMagnitude > 1e-24f ? nrm.normalized : Vector3.forward, ax);
                rest[3 * t] = Vector2.zero;
                rest[3 * t + 1] = new Vector2(l1, 0f);
                rest[3 * t + 2] = new Vector2(Vector3.Dot(p2 - p0, ax), Vector3.Dot(p2 - p0, ay));
                for (int k = 0; k < 3; k++)
                {
                    // The cotangent of the angle at corner k, which faces the edge (k+1, k+2).
                    Vector2 a = rest[3 * t + (k + 1) % 3] - rest[3 * t + k], b = rest[3 * t + (k + 2) % 3] - rest[3 * t + k];
                    double cross = a.x * b.y - a.y * b.x, dot = a.x * b.x + a.y * b.y;
                    double c = Math.Abs(cross) > 1e-20 ? dot / Math.Abs(cross) : 0.0;
                    // Obtuse angles give negative weights; keep them small and positive so the system stays solvable.
                    cot[3 * t + k] = Math.Max(c, 1e-3) * 0.5;
                }
            }
            var laplace = new SparseSystem(n);
            for (int t = 0; t < triangles; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    int i = mesh.Triangles[3 * t + (k + 1) % 3], j = mesh.Triangles[3 * t + (k + 2) % 3];
                    double w = cot[3 * t + k];
                    laplace.Add(i, i, w); laplace.Add(j, j, w); laplace.Add(i, j, -w); laplace.Add(j, i, -w);
                }
            }
            // Pin one point: the rest is free to rotate and move only as the triangles ask.
            int pinned = loop[0];
            laplace.Pin(pinned);
            laplace.Finish();
            var rotations = new double[triangles * 4];
            for (int iteration = 0; iteration < ArapIterations; iteration++)
            {
                // Local step: the rotation that best takes each triangle's rest shape to its current one.
                for (int t = 0; t < triangles; t++)
                {
                    double s00 = 0, s01 = 0, s10 = 0, s11 = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        int ci = (k + 1) % 3, cj = (k + 2) % 3;
                        int i = mesh.Triangles[3 * t + ci], j = mesh.Triangles[3 * t + cj];
                        double w = cot[3 * t + k];
                        double ux = x[i] - x[j], uy = y[i] - y[j];
                        double rx = rest[3 * t + ci].x - rest[3 * t + cj].x, ry = rest[3 * t + ci].y - rest[3 * t + cj].y;
                        s00 += w * ux * rx; s01 += w * ux * ry; s10 += w * uy * rx; s11 += w * uy * ry;
                    }
                    double angle = Math.Atan2(s10 - s01, s00 + s11);
                    rotations[4 * t] = Math.Cos(angle); rotations[4 * t + 1] = -Math.Sin(angle);
                    rotations[4 * t + 2] = Math.Sin(angle); rotations[4 * t + 3] = Math.Cos(angle);
                }
                // Global step: the positions that best fit every triangle's rotated rest shape.
                Array.Clear(bx, 0, n); Array.Clear(by, 0, n);
                for (int t = 0; t < triangles; t++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int ci = (k + 1) % 3, cj = (k + 2) % 3;
                        int i = mesh.Triangles[3 * t + ci], j = mesh.Triangles[3 * t + cj];
                        double w = cot[3 * t + k];
                        double rx = rest[3 * t + ci].x - rest[3 * t + cj].x, ry = rest[3 * t + ci].y - rest[3 * t + cj].y;
                        double gx = rotations[4 * t] * rx + rotations[4 * t + 1] * ry, gy = rotations[4 * t + 2] * rx + rotations[4 * t + 3] * ry;
                        bx[i] += w * gx; by[i] += w * gy;
                        bx[j] -= w * gx; by[j] -= w * gy;
                    }
                }
                bx[pinned] = x[pinned]; by[pinned] = y[pinned];
                laplace.Solve(bx, x, 400);
                laplace.Solve(by, y, 400);
            }
            for (int v = 0; v < n; v++) flat[v] = new Vector2((float)x[v], (float)y[v]);

            // Distortion: each triangle's worst edge-length change, the 95th percentile by area.
            var samples = new List<(float d, float area)>(triangles);
            float totalArea = 0f;
            for (int t = 0; t < triangles; t++)
            {
                float worst = 0f;
                for (int k = 0; k < 3; k++)
                {
                    int i = mesh.Triangles[3 * t + k], j = mesh.Triangles[3 * t + (k + 1) % 3];
                    float l3 = Vector3.Distance(mesh.Positions[i], mesh.Positions[j]);
                    if (l3 < 1e-9f) continue;
                    worst = Mathf.Max(worst, Mathf.Abs((flat[i] - flat[j]).magnitude / l3 - 1f));
                }
                float area = Mathf.Abs(rest[3 * t + 1].x * rest[3 * t + 2].y) * 0.5f;
                samples.Add((worst, area));
                totalArea += area;
            }
            samples.Sort((p, q) => p.d.CompareTo(q.d));
            float sum = 0f;
            distortion = samples.Count > 0 ? samples[samples.Count - 1].d : 0f;
            foreach (var s in samples)
            {
                sum += s.area;
                if (sum >= totalArea * 0.95f) { distortion = s.d; break; }
            }
            foreach (Vector2 p in flat) if (float.IsNaN(p.x) || float.IsNaN(p.y)) return null;
            return flat;
        }

        // ---- A mesh being cut open ---------------------------------------------------------------------

        private sealed class WorkMesh
        {
            public readonly List<Vector3> Positions;
            public readonly List<int> Ids;
            public readonly List<int> Triangles;
            private readonly Vector3 axisPoint, up;

            public WorkMesh(RegionPatch patch)
            {
                Positions = new List<Vector3>(patch.Positions);
                Ids = new List<int>(patch.Ids);
                Triangles = new List<int>(patch.Triangles.Count);
                // Drop triangles with a repeated corner.
                for (int t = 0; t + 2 < patch.Triangles.Count; t += 3)
                {
                    int a = patch.Triangles[t], b = patch.Triangles[t + 1], c = patch.Triangles[t + 2];
                    if (a == b || b == c || a == c) continue;
                    Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
                }
                axisPoint = patch.AxisPoint;
                up = patch.Up.normalized;
                SplitPinches();
            }

            /// <summary>
            /// Where triangles meet at only a point (a painted edge that pinches), splits the point: each fan of
            /// triangles joined by edges gets its own copy (the same id), so the boundary runs cleanly past it.
            /// </summary>
            private void SplitPinches()
            {
                var around = new Dictionary<int, List<int>>();
                for (int t = 0; t < Triangles.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        if (!around.TryGetValue(Triangles[t + k], out List<int> list)) around[Triangles[t + k]] = list = new List<int>(6);
                        list.Add(t);
                    }
                foreach (KeyValuePair<int, List<int>> kv in around)
                {
                    int v = kv.Key;
                    List<int> fan = kv.Value;
                    var group = new Dictionary<int, int>();
                    int groups = 0;
                    foreach (int start in fan)
                    {
                        if (group.ContainsKey(start)) continue;
                        var stack = new Stack<int>();
                        stack.Push(start);
                        group[start] = groups;
                        while (stack.Count > 0)
                        {
                            int t = stack.Pop();
                            foreach (int other in fan)
                            {
                                if (group.ContainsKey(other) || SharedOtherCorner(t, other, v) < 0) continue;
                                group[other] = groups;
                                stack.Push(other);
                            }
                        }
                        groups++;
                    }
                    for (int g = 1; g < groups; g++)
                    {
                        int copy = Positions.Count;
                        Positions.Add(Positions[v]);
                        Ids.Add(Ids[v]);
                        foreach (KeyValuePair<int, int> tg in group)
                        {
                            if (tg.Value != g) continue;
                            for (int k = 0; k < 3; k++) if (Triangles[tg.Key + k] == v) Triangles[tg.Key + k] = copy;
                        }
                    }
                }
            }

            private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

            public int EdgeCount()
            {
                var edges = new HashSet<long>();
                for (int t = 0; t < Triangles.Count; t += 3)
                    for (int k = 0; k < 3; k++) edges.Add(Key(Triangles[t + k], Triangles[t + (k + 1) % 3]));
                return edges.Count;
            }

            public List<int>[] Neighbours()
            {
                var list = new List<int>[Positions.Count];
                for (int i = 0; i < list.Length; i++) list[i] = new List<int>(6);
                for (int t = 0; t < Triangles.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = Triangles[t + k], b = Triangles[t + (k + 1) % 3];
                        if (!list[a].Contains(b)) list[a].Add(b);
                        if (!list[b].Contains(a)) list[b].Add(a);
                    }
                }
                return list;
            }

            /// <summary>The boundary as loops of vertices, each running with the triangles (interior on the left seen from outside).</summary>
            public List<List<int>> BoundaryLoops(out bool manifold)
            {
                manifold = true;
                var uses = new Dictionary<long, int>();
                for (int t = 0; t < Triangles.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        long key = Key(Triangles[t + k], Triangles[t + (k + 1) % 3]);
                        uses.TryGetValue(key, out int u);
                        uses[key] = u + 1;
                    }
                }
                var next = new Dictionary<int, int>();
                for (int t = 0; t < Triangles.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = Triangles[t + k], b = Triangles[t + (k + 1) % 3];
                        int count = uses[Key(a, b)];
                        if (count > 2) manifold = false;
                        if (count != 1) continue;
                        if (next.ContainsKey(a)) { manifold = false; continue; }
                        next[a] = b;
                    }
                }
                var loops = new List<List<int>>();
                var seen = new HashSet<int>();
                foreach (int start in next.Keys)
                {
                    if (seen.Contains(start)) continue;
                    var loop = new List<int>();
                    int v = start, guard = 0;
                    while (!seen.Contains(v) && guard++ < 1000000)
                    {
                        seen.Add(v);
                        loop.Add(v);
                        if (!next.TryGetValue(v, out v)) { manifold = false; break; }
                    }
                    if (v != start) manifold = false;
                    loops.Add(loop);
                }
                return loops;
            }

            /// <summary>
            /// Cuts from the first loop to the nearest other one along the cheapest path, which prefers the cut side
            /// (each step costs its length, up to four times more on the far side). The path's points are split in two.
            /// </summary>
            public bool CutBetween(List<List<int>> loops, Vector3 cutSide)
            {
                int n = Positions.Count;
                List<int>[] neighbours = Neighbours();
                var normal = new Vector3[n];
                for (int t = 0; t < Triangles.Count; t += 3)
                {
                    Vector3 f = Vector3.Cross(Positions[Triangles[t + 1]] - Positions[Triangles[t]], Positions[Triangles[t + 2]] - Positions[Triangles[t]]);
                    normal[Triangles[t]] += f; normal[Triangles[t + 1]] += f; normal[Triangles[t + 2]] += f;
                }
                Vector3 side = Vector3.ProjectOnPlane(cutSide, up).normalized;
                float Preference(int v)
                {
                    Vector3 outward = Vector3.ProjectOnPlane(Positions[v] - axisPoint, up);
                    if (outward.sqrMagnitude < 1e-12f) outward = Vector3.ProjectOnPlane(normal[v], up);
                    return outward.sqrMagnitude < 1e-12f ? 0.5f : 0.5f + 0.5f * Vector3.Dot(outward.normalized, side);
                }
                var loopOf = new int[n];
                for (int i = 0; i < n; i++) loopOf[i] = -1;
                for (int l = 0; l < loops.Count; l++) foreach (int v in loops[l]) loopOf[v] = l;
                var distance = new float[n];
                var from = new int[n];
                for (int i = 0; i < n; i++) { distance[i] = float.MaxValue; from[i] = -1; }
                var queue = new SortedSet<(float, int)>();
                foreach (int v in loops[0]) { distance[v] = 0f; queue.Add((0f, v)); }
                int reached = -1;
                while (queue.Count > 0)
                {
                    var (d, v) = queue.Min;
                    queue.Remove(queue.Min);
                    if (d > distance[v]) continue;
                    if (loopOf[v] > 0) { reached = v; break; }
                    foreach (int w in neighbours[v])
                    {
                        if (loopOf[w] == 0) continue;
                        float step = Vector3.Distance(Positions[v], Positions[w]) * (1f + 3f * (1f - 0.5f * (Preference(v) + Preference(w))));
                        float nd = d + step;
                        if (nd < distance[w])
                        {
                            if (distance[w] < float.MaxValue) queue.Remove((distance[w], w));
                            distance[w] = nd; from[w] = v;
                            queue.Add((nd, w));
                        }
                    }
                }
                if (reached < 0) return false;
                var path = new List<int>();
                for (int v = reached; v >= 0; v = from[v]) path.Add(v);
                path.Reverse();
                if (path.Count < 2) return false;
                var cut = new HashSet<long>();
                for (int i = 0; i + 1 < path.Count; i++) cut.Add(Key(path[i], path[i + 1]));
                SplitAlong(cut, path);
                return true;
            }

            /// <summary>
            /// Splits the points of a path: round each one, the triangles fall into groups that meet only across the cut
            /// (or the boundary); every group after the first gets a copy of the point (the same id: the two sides are sewn).
            /// </summary>
            private void SplitAlong(HashSet<long> cut, List<int> path)
            {
                var around = new Dictionary<int, List<int>>();
                foreach (int v in path) around[v] = new List<int>();
                for (int t = 0; t < Triangles.Count; t += 3)
                    for (int k = 0; k < 3; k++)
                        if (around.TryGetValue(Triangles[t + k], out List<int> list)) list.Add(t);
                foreach (int v in path)
                {
                    List<int> fan = around[v];
                    var group = new Dictionary<int, int>();
                    int groups = 0;
                    foreach (int start in fan)
                    {
                        if (group.ContainsKey(start)) continue;
                        var stack = new Stack<int>();
                        stack.Push(start);
                        group[start] = groups;
                        while (stack.Count > 0)
                        {
                            int t = stack.Pop();
                            foreach (int other in fan)
                            {
                                if (group.ContainsKey(other)) continue;
                                // Triangles round v meet across an edge (v, w) both use, unless it is cut.
                                int shared = SharedOtherCorner(t, other, v);
                                if (shared < 0 || cut.Contains(Key(v, shared))) continue;
                                group[other] = groups;
                                stack.Push(other);
                            }
                        }
                        groups++;
                    }
                    for (int g = 1; g < groups; g++)
                    {
                        int copy = Positions.Count;
                        Positions.Add(Positions[v]);
                        Ids.Add(Ids[v]);
                        foreach (KeyValuePair<int, int> kv in group)
                        {
                            if (kv.Value != g) continue;
                            for (int k = 0; k < 3; k++) if (Triangles[kv.Key + k] == v) Triangles[kv.Key + k] = copy;
                        }
                    }
                }
            }

            /// <summary>The corner (other than v) two triangles share, or -1.</summary>
            private int SharedOtherCorner(int t, int u, int v)
            {
                for (int a = 0; a < 3; a++)
                {
                    int x = Triangles[t + a];
                    if (x == v) continue;
                    for (int b = 0; b < 3; b++) if (Triangles[u + b] == x) return x;
                }
                return -1;
            }
        }

        // ---- A small sparse solver -------------------------------------------------------------------

        /// <summary>A symmetric positive definite sparse system, solved by conjugate gradients with a diagonal preconditioner.</summary>
        private sealed class SparseSystem
        {
            private readonly int n;
            private readonly Dictionary<long, double>[] rows;
            private int[] start, column;
            private double[] value, diagonal;
            private int pinned = -1;
            /// <summary>The other rows' entries in the pinned unknown's column (moved to the right-hand side).</summary>
            private readonly List<KeyValuePair<int, double>> pinnedColumn = new List<KeyValuePair<int, double>>();

            public SparseSystem(int n)
            {
                this.n = n;
                rows = new Dictionary<long, double>[n];
                for (int i = 0; i < n; i++) rows[i] = new Dictionary<long, double>();
            }

            public void Add(int i, int j, double v)
            {
                rows[i].TryGetValue(j, out double old);
                rows[i][j] = old + v;
            }

            /// <summary>Holds one unknown at its current value (its row becomes the identity).</summary>
            public void Pin(int i) => pinned = i;

            public void Finish()
            {
                start = new int[n + 1];
                var columns = new List<int>();
                var values = new List<double>();
                diagonal = new double[n];
                for (int i = 0; i < n; i++)
                {
                    start[i] = columns.Count;
                    if (i == pinned) { columns.Add(i); values.Add(1.0); diagonal[i] = 1.0; continue; }
                    foreach (KeyValuePair<long, double> kv in rows[i])
                    {
                        int j = (int)kv.Key;
                        if (j == pinned) { pinnedColumn.Add(new KeyValuePair<int, double>(i, kv.Value)); continue; }
                        columns.Add(j); values.Add(kv.Value);
                        if (j == i) diagonal[i] = kv.Value;
                    }
                    if (diagonal[i] == 0.0) diagonal[i] = 1.0;
                }
                start[n] = columns.Count;
                column = columns.ToArray();
                value = values.ToArray();
            }

            private void Multiply(double[] x, double[] into)
            {
                for (int i = 0; i < n; i++)
                {
                    double s = 0.0;
                    for (int k = start[i]; k < start[i + 1]; k++) s += value[k] * x[column[k]];
                    into[i] = s;
                }
            }

            /// <summary>Solves A x = b, starting from x as given (a warm start).</summary>
            public void Solve(double[] b, double[] x, int maxIterations)
            {
                var rhs = (double[])b.Clone();
                // A pinned unknown's neighbours see it as a known value.
                if (pinned >= 0)
                {
                    rhs[pinned] = x[pinned];
                    foreach (KeyValuePair<int, double> kv in pinnedColumn) rhs[kv.Key] -= kv.Value * x[pinned];
                }
                var r = new double[n]; var z = new double[n]; var p = new double[n]; var q = new double[n];
                Multiply(x, q);
                double bnorm = 0.0;
                for (int i = 0; i < n; i++) { r[i] = rhs[i] - q[i]; bnorm += rhs[i] * rhs[i]; }
                if (bnorm < 1e-30) bnorm = 1.0;
                for (int i = 0; i < n; i++) { z[i] = r[i] / diagonal[i]; p[i] = z[i]; }
                double rz = 0.0;
                for (int i = 0; i < n; i++) rz += r[i] * z[i];
                for (int iteration = 0; iteration < maxIterations; iteration++)
                {
                    Multiply(p, q);
                    double pq = 0.0;
                    for (int i = 0; i < n; i++) pq += p[i] * q[i];
                    if (Math.Abs(pq) < 1e-300) break;
                    double alpha = rz / pq, rr = 0.0;
                    for (int i = 0; i < n; i++) { x[i] += alpha * p[i]; r[i] -= alpha * q[i]; rr += r[i] * r[i]; }
                    if (rr < 1e-16 * bnorm) break;
                    double rzNew = 0.0;
                    for (int i = 0; i < n; i++) { z[i] = r[i] / diagonal[i]; rzNew += r[i] * z[i]; }
                    double beta = rzNew / rz;
                    rz = rzNew;
                    for (int i = 0; i < n; i++) p[i] = z[i] + beta * p[i];
                }
            }

        }
    }
}
