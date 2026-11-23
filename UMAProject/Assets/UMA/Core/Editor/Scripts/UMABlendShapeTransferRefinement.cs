using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors
{
    public static partial class UMABlendShapeTransfer
    {
        private static readonly Vector3[] ClearanceSamples =
        {
            new Vector3(1f/3f, 1f/3f, 1f/3f), new Vector3(.5f,.5f,0f),
            new Vector3(0f,.5f,.5f), new Vector3(.5f,0f,.5f),
        };

        private static void BindSurfaceSamples(UMABlendShapeSource source, UMABlendShapeSlotSpace target, Binding binding)
        {
            binding.TriangleGroups = new int[target.Triangles.Length];
            for (int i = 0; i < target.Triangles.Length; i++) binding.TriangleGroups[i] = binding.Groups[target.Triangles[i]];
            binding.SampleClearance = new float[target.Triangles.Length / 3 * ClearanceSamples.Length];
            for (int t = 0; t < binding.TriangleGroups.Length; t += 3)
            {
                int a = binding.TriangleGroups[t], b = binding.TriangleGroups[t + 1], c = binding.TriangleGroups[t + 2];
                for (int s = 0; s < ClearanceSamples.Length; s++)
                {
                    Vector3 w = ClearanceSamples[s];
                    Vector3 p = binding.Positions[a] * w.x + binding.Positions[b] * w.y + binding.Positions[c] * w.z;
                    binding.SampleClearance[t / 3 * ClearanceSamples.Length + s] = SurfaceGap(source.Lookup, source.Positions, source.RestNormals, source.Triangles, p, out _);
                }
            }
        }

        // Covariance is sum(deformed edge * rest edge transpose). Normalising edge lengths
        // avoids letting a few long triangles dominate a neighbourhood's rotation.
        private static Matrix4x4 Covariance(Vector3[] rest, Vector3[] moved, List<int>[] neighbours, int i)
        {
            Matrix4x4 c = Matrix4x4.zero;
            var adjacent = neighbours[i];
            if (adjacent == null) return c;
            foreach (int j in adjacent)
            {
                Vector3 x = rest[j] - rest[i], y = moved[j] - moved[i];
                AddOuter(ref c, y, x, 1f / Mathf.Max(1e-12f, x.sqrMagnitude));
            }
            if (adjacent.Count > 0)
                for (int k = 0; k < 16; k++) c[k] /= adjacent.Count;
            return c;
        }

        private static void AddOuter(ref Matrix4x4 c, Vector3 y, Vector3 x, float weight = 1f)
        {
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++) c[row, col] += y[row] * x[col] * weight;
        }

        /// <summary>Proper best-fit rotation, including planar stencils. A symmetric quaternion
        /// eigensolve avoids inverse/polar failures on the rank-two neighbourhoods of cloth.</summary>
        private static Quaternion FitRotation(Matrix4x4 c)
        {
            Matrix4x4 a = Matrix4x4.zero;
            a.m00 = c.m00 - c.m11 - c.m22; a.m11 = -c.m00 + c.m11 - c.m22;
            a.m22 = -c.m00 - c.m11 + c.m22; a.m33 = c.m00 + c.m11 + c.m22;
            a.m01 = a.m10 = c.m01 + c.m10; a.m02 = a.m20 = c.m02 + c.m20;
            a.m03 = a.m30 = c.m21 - c.m12; a.m12 = a.m21 = c.m12 + c.m21;
            a.m13 = a.m31 = c.m02 - c.m20; a.m23 = a.m32 = c.m10 - c.m01;
            float scale = 0f;
            for (int i = 0; i < 16; i++) scale = Mathf.Max(scale, Mathf.Abs(a[i]));
            if (scale < 1e-20f) return Quaternion.identity;
            for (int i = 0; i < 16; i++) a[i] /= scale;
            Matrix4x4 eigenvectors = Matrix4x4.identity;
            for (int sweep = 0; sweep < 32; sweep++)
            {
                int p = 0, q = 1; float largest = 0f;
                for (int row = 0; row < 4; row++)
                    for (int col = row + 1; col < 4; col++)
                        if (Mathf.Abs(a[row, col]) > largest) { largest = Mathf.Abs(a[row, col]); p = row; q = col; }
                if (largest < 1e-7f) break;
                float angle = 0.5f * Mathf.Atan2(2f * a[p, q], a[q, q] - a[p, p]);
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                float app = a[p, p], aqq = a[q, q], apq = a[p, q];
                for (int k = 0; k < 4; k++)
                {
                    if (k != p && k != q)
                    {
                        float kp = a[k, p], kq = a[k, q];
                        a[k, p] = a[p, k] = cos * kp - sin * kq;
                        a[k, q] = a[q, k] = sin * kp + cos * kq;
                    }
                    float vp = eigenvectors[k, p], vq = eigenvectors[k, q];
                    eigenvectors[k, p] = cos * vp - sin * vq;
                    eigenvectors[k, q] = sin * vp + cos * vq;
                }
                a[p, p] = cos * cos * app - 2f * sin * cos * apq + sin * sin * aqq;
                a[q, q] = sin * sin * app + 2f * sin * cos * apq + cos * cos * aqq;
                a[p, q] = a[q, p] = 0f;
            }
            // Prefer identity in ambiguous zero/tiny deformations.
            int best = 3;
            for (int k = 0; k < 3; k++) if (a[k, k] > a[best, best] + 1e-7f) best = k;
            Vector4 v = eigenvectors.GetColumn(best);
            return new Quaternion(v.x, v.y, v.z, v.w).normalized;
        }

        private static Quaternion[] SmoothSourceRotations(UMABlendShapeSource source, Vector3[] deltas, int passes)
        {
            int count = source.GroupCount;
            var rest = new Vector3[count]; var moved = new Vector3[count];
            for (int v = 0; v < source.Positions.Length; v++)
            { int g = source.Groups[v]; rest[g] = source.Positions[v]; moved[g] = rest[g] + deltas[v]; }
            var neighbours = new List<int>[count];
            for (int t = 0; t < source.Triangles.Length; t += 3)
            {
                int a = source.Groups[source.Triangles[t]], b = source.Groups[source.Triangles[t + 1]], c = source.Groups[source.Triangles[t + 2]];
                Link(neighbours, a, b); Link(neighbours, b, c); Link(neighbours, c, a);
            }
            var covariance = new Matrix4x4[count]; var work = new Matrix4x4[count];
            for (int g = 0; g < count; g++) covariance[g] = Covariance(rest, moved, neighbours, g);
            for (int pass = 0; pass < Mathf.Clamp(passes, 0, 16); pass++)
            {
                for (int g = 0; g < count; g++)
                {
                    var adjacent = neighbours[g]; work[g] = covariance[g];
                    if (adjacent == null || adjacent.Count == 0) continue;
                    for (int k = 0; k < 16; k++)
                    {
                        float sum = 0f; foreach (int j in adjacent) sum += covariance[j][k];
                        work[g][k] = 0.5f * (covariance[g][k] + sum / adjacent.Count);
                    }
                }
                var swap = covariance; covariance = work; work = swap;
            }
            var rotation = new Quaternion[count];
            for (int g = 0; g < count; g++) rotation[g] = FitRotation(covariance[g]);
            var result = new Quaternion[source.Positions.Length];
            for (int v = 0; v < result.Length; v++) result[v] = rotation[source.Groups[v]];
            return result;
        }

        private static Vector3[] SmoothPositions(Vector3[] input, List<int>[] neighbours, int iterations, float strength)
        {
            var current = (Vector3[])input.Clone(); var work = new Vector3[current.Length];
            for (int pass = 0; pass < Mathf.Clamp(iterations, 0, 60); pass++)
            {
                for (int g = 0; g < current.Length; g++)
                {
                    var adjacent = neighbours[g]; work[g] = current[g];
                    if (adjacent == null || adjacent.Count == 0) continue;
                    Vector3 average = Vector3.zero;
                    foreach (int j in adjacent) average += current[j];
                    work[g] = Vector3.Lerp(current[g], average / adjacent.Count, strength);
                }
                var swap = current; current = work; work = swap;
            }
            return current;
        }

        private static float[] DeltaMush(Binding binding, Vector3[] deltas, UMABlendShapeTransferSettings settings)
        {
            int count = deltas.Length;
            var mask = new float[count];
            if (settings.smoothingIterations <= 0 || settings.smoothingStrength <= 0f || settings.strength == 0f) return mask;
            var moved = new Vector3[count];
            bool any = false;
            for (int g = 0; g < count; g++) { moved[g] = binding.Positions[g] + deltas[g]; any |= deltas[g].sqrMagnitude > 1e-20f; }
            if (!any) return mask;
            var restSmooth = SmoothPositions(binding.Positions, binding.Neighbours, settings.smoothingIterations, settings.smoothingStrength);
            var movedSmooth = SmoothPositions(moved, binding.Neighbours, settings.smoothingIterations, settings.smoothingStrength);
            var weights = new Vector3[count];
            for (int g = 0; g < count; g++)
            {
                float worst = 0f;
                if (binding.Neighbours[g] != null)
                    foreach (int j in binding.Neighbours[g])
                    {
                        float length = (binding.Positions[g] - binding.Positions[j]).magnitude;
                        if (length < WeldTolerance) continue;
                        float ratio = Mathf.Max(0.001f, (moved[g] - moved[j]).magnitude / length);
                        worst = Mathf.Max(worst, Mathf.Abs(Mathf.Log(ratio)));
                    }
                float x = Mathf.Clamp01((worst - settings.repairStart) / Mathf.Max(0.0001f, settings.repairFull - settings.repairStart));
                weights[g].x = settings.adaptiveSmoothing ? x * x * (3f - 2f * x) : 1f;
            }
            var feather = SmoothPositions(weights, binding.Neighbours, 3, 0.5f);
            for (int g = 0; g < count; g++)
            {
                if (binding.IslandRigid[binding.Island[g]] || binding.Influence[g] <= 0f) continue;
                mask[g] = Mathf.Max(weights[g].x, feather[g].x) * Mathf.Clamp01(binding.Influence[g]);
                if (mask[g] <= 0f) continue;
                Quaternion rotation = FitRotation(Covariance(restSmooth, movedSmooth, binding.Neighbours, g));
                Vector3 detail = binding.Positions[g] - restSmooth[g];
                Vector3 corrected = movedSmooth[g] + rotation * detail - binding.Positions[g];
                deltas[g] = Vector3.Lerp(deltas[g], corrected, mask[g]);
            }
            return mask;
        }

        private static void FitRigidParts(Binding binding, Vector3[] deltas)
        {
            for (int island = 0; island < binding.IslandCount; island++)
            {
                if (!binding.IslandRigid[island]) continue;
                var members = new List<int>(); Vector3 center = Vector3.zero;
                for (int g = 0; g < deltas.Length; g++)
                    if (binding.Island[g] == island) { members.Add(g); center += binding.Positions[g]; }
                if (members.Count == 0) continue;
                center /= members.Count;
                // A small, deterministic stencil from the surrounding cloth. Components with no
                // support nearby retain their own best-fit motion, rather than jumping to a remote panel.
                var nearest = new List<int>(6); var distances = new List<float>(6);
                float size = 0f;
                foreach (int g in members) size = Mathf.Max(size, (binding.Positions[g] - center).magnitude);
                float reach = Mathf.Max(0.02f, size * 8f);
                for (int g = 0; g < deltas.Length; g++)
                {
                    if (binding.IslandRigid[binding.Island[g]] || binding.Influence[g] <= 0f) continue;
                    float distance = (binding.Positions[g] - center).sqrMagnitude;
                    if (distance > reach * reach) continue;
                    int at = 0; while (at < distances.Count && distances[at] <= distance) at++;
                    if (at >= 6) continue;
                    nearest.Insert(at, g); distances.Insert(at, distance);
                    if (nearest.Count > 6) { nearest.RemoveAt(6); distances.RemoveAt(6); }
                }
                var support = nearest.Count >= 3 ? nearest : members;
                Vector3 from = Vector3.zero, to = Vector3.zero;
                foreach (int g in support) { from += binding.Positions[g]; to += binding.Positions[g] + deltas[g]; }
                from /= support.Count; to /= support.Count;
                Matrix4x4 covariance = Matrix4x4.zero;
                foreach (int g in support) AddOuter(ref covariance, binding.Positions[g] + deltas[g] - to, binding.Positions[g] - from);
                Quaternion turn = FitRotation(covariance);
                foreach (int g in members) deltas[g] = to + turn * (binding.Positions[g] - from) - binding.Positions[g];
            }
        }

        private static float SurfaceGap(UMATriangleLookup lookup, Vector3[] positions, Vector3[] normals, int[] triangles, Vector3 point, out Vector3 normal)
        {
            normal = Vector3.zero;
            if (!lookup.FindNearest(point, Vector3.zero, 0f, out int tri, out Vector3 closest, out _)) return float.PositiveInfinity;
            int a = triangles[tri * 3], b = triangles[tri * 3 + 1], c = triangles[tri * 3 + 2];
            Vector3 weights = Barycentric(closest, positions[a], positions[b], positions[c]);
            normal = (normals[a] * weights.x + normals[b] * weights.y + normals[c] * weights.z).normalized;
            return Vector3.Dot(point - closest, normal);
        }

        private static int CheckDeformedSurface(UMABlendShapeSource source, Vector3[] sourceDeltas, Vector3[] normals,
            Binding binding, Vector3[] deltas, UMABlendShapeTransferSettings settings, bool[] under, out int samplesUnder)
        {
            var moved = new Vector3[source.Positions.Length]; bool sourceMoves = false;
            for (int i = 0; i < moved.Length; i++) { moved[i] = source.Positions[i] + sourceDeltas[i]; sourceMoves |= sourceDeltas[i].sqrMagnitude > 1e-20f; }
            var lookup = new UMATriangleLookup(moved, normals, source.Triangles);
            var corrected = new bool[deltas.Length];
            if (settings.keepClearance && settings.strength != 0f && sourceMoves)
            {
                // Repeat to handle a contact whose nearest face changes after correction. Translate
                // an entire rigid island by its deepest correction so button geometry stays rigid.
                for (int pass = 0; pass < (binding.SampleClearance != null ? 8 : 4); pass++)
                {
                    var rigidPush = new Vector3[binding.IslandCount]; bool any = false;
                    for (int g = 0; g < deltas.Length; g++)
                    {
                        if (binding.Influence[g] <= 0f || binding.Clearance[g] < 0f) continue;
                        float gap = SurfaceGap(lookup, moved, normals, source.Triangles, binding.Positions[g] + deltas[g], out Vector3 normal);
                        float want = Mathf.Min(binding.Clearance[g], Mathf.Max(0f, settings.clearance));
                        if (gap >= want - 1e-6f) continue;
                        Vector3 push = normal * (want - gap);
                        int island = binding.Island[g];
                        if (binding.IslandRigid[island])
                        { if (push.sqrMagnitude > rigidPush[island].sqrMagnitude) rigidPush[island] = push; }
                        else { deltas[g] += push; corrected[g] = true; }
                        any = true;
                    }
                    for (int g = 0; g < deltas.Length; g++)
                    {
                        Vector3 push = rigidPush[binding.Island[g]];
                        if (push.sqrMagnitude > 0f) { deltas[g] += push; corrected[g] = true; }
                    }
                    if (binding.SampleClearance != null)
                    {
                        for (int t = 0; t < binding.TriangleGroups.Length; t += 3)
                        {
                            int a = binding.TriangleGroups[t], b = binding.TriangleGroups[t + 1], c = binding.TriangleGroups[t + 2];
                            if (binding.Influence[a] <= 0f || binding.Influence[b] <= 0f || binding.Influence[c] <= 0f) continue;
                            for (int s = 0; s < ClearanceSamples.Length; s++)
                            {
                                float restGap = binding.SampleClearance[t / 3 * ClearanceSamples.Length + s];
                                if (restGap < 0f) continue;
                                Vector3 w = ClearanceSamples[s];
                                Vector3 point = (binding.Positions[a] + deltas[a]) * w.x + (binding.Positions[b] + deltas[b]) * w.y + (binding.Positions[c] + deltas[c]) * w.z;
                                float gap = SurfaceGap(lookup, moved, normals, source.Triangles, point, out Vector3 normal);
                                float want = Mathf.Min(restGap, Mathf.Max(0f, settings.clearance));
                                if (gap >= want - 1e-6f) continue;
                                Vector3 push = normal * (want - gap); int island = binding.Island[a];
                                if (binding.IslandRigid[island])
                                {
                                    for (int g = 0; g < deltas.Length; g++)
                                        if (binding.Island[g] == island) { deltas[g] += push; corrected[g] = true; }
                                }
                                else
                                {
                                    // Least-change correction of this barycentric point constraint.
                                    push /= w.sqrMagnitude;
                                    deltas[a] += push * w.x; deltas[b] += push * w.y; deltas[c] += push * w.z;
                                    corrected[a] = corrected[b] = corrected[c] = true;
                                }
                                any = true;
                            }
                        }
                    }
                    if (!any) break;
                }
            }
            int count = 0;
            for (int g = 0; g < deltas.Length; g++)
            {
                if (corrected[g]) count++;
                if (under != null && binding.Influence[g] > 0f && binding.Clearance[g] >= 0f)
                    under[g] = SurfaceGap(lookup, moved, normals, source.Triangles, binding.Positions[g] + deltas[g], out _) < -1e-5f;
            }
            samplesUnder = 0;
            if (binding.SampleClearance != null && under != null)
                for (int t = 0; t < binding.TriangleGroups.Length; t += 3)
                {
                    int a = binding.TriangleGroups[t], b = binding.TriangleGroups[t + 1], c = binding.TriangleGroups[t + 2];
                    if (binding.Influence[a] <= 0f || binding.Influence[b] <= 0f || binding.Influence[c] <= 0f) continue;
                    for (int s = 0; s < ClearanceSamples.Length; s++)
                    {
                        if (binding.SampleClearance[t / 3 * ClearanceSamples.Length + s] < 0f) continue;
                        Vector3 w = ClearanceSamples[s];
                        Vector3 point = (binding.Positions[a] + deltas[a]) * w.x + (binding.Positions[b] + deltas[b]) * w.y + (binding.Positions[c] + deltas[c]) * w.z;
                        if (SurfaceGap(lookup, moved, normals, source.Triangles, point, out _) < -1e-5f) samplesUnder++;
                    }
                }
            return count;
        }
    }
}
