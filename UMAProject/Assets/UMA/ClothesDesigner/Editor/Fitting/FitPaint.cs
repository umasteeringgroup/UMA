using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Painted fit controls, as Divine Cut and Simply Cloth have them: paint where the cloth hugs the body, where it
    /// smooths out, where it puffs up or slims in. The paint is a value per cloth point in three channels, kept on
    /// the flat pattern in the fit (like pins) so it survives a new mesh level, and works through what the solver
    /// already has, on the GPU as on the CPU:
    /// <list type="bullet">
    /// <item>Hug cuts the painted cloth smaller (shorter rest lengths, up to <see cref="MaxHugShrink"/>) and draws
    /// it onto the body each frame (soft pins on the nearest body point, out by the clearance).</item>
    /// <item>Smooth draws each painted point towards its neighbours along the cloth's normal each frame, so bumps
    /// and wrinkles flatten while the cloth keeps its size.</item>
    /// <item>Puff and Slim add to the cloth's pressure (out from the body, or in towards it), point by point.</item>
    /// <item>Stiffen makes the cloth firmer: stretch and bend compliance fall by up to <see cref="StiffDecades"/>
    /// decades, and painted points are tied to the painted points two edges away, as interfacing ties a piece
    /// (bending alone cannot be made much firmer: it is held at a floor that keeps fine meshes stable).</item>
    /// <item>Weight makes the cloth heavier, up to <see cref="MaxExtraWeight"/> times its weight more.</item>
    /// </list>
    /// </summary>
    public static class FitPaint
    {
        /// <summary>How much smaller fully hugged cloth is cut.</summary>
        public const float MaxHugShrink = 0.15f;
        /// <summary>How far each substep moves fully hugged cloth towards the body (a pin weight).</summary>
        public const float HugPull = 0.02f;
        /// <summary>How far each substep moves fully smoothed cloth towards its neighbours' surface (a pin weight).</summary>
        public const float SmoothPull = 0.04f;
        /// <summary>How far from the body hugged cloth is still drawn to it (m).</summary>
        public const float HugReach = 0.08f;
        /// <summary>Painted values below this are not kept.</summary>
        public const float Keep = 0.03f;
        /// <summary>How many decades fully stiffened cloth's stretch and bend compliance fall.</summary>
        public const float StiffDecades = 2f;
        /// <summary>How many times its own weight fully weighted cloth gains.</summary>
        public const float MaxExtraWeight = 3f;

        /// <summary>The compliance of the interfacing-like ties of stiffened cloth: firm interfacing's at full paint, light's at half.</summary>
        public static float StiffTieCompliance(float stiff) => ClothModel.FirmInterfacingCompliance * Mathf.Pow(10f, 4f * (1f - Mathf.Clamp01(stiff)));

        /// <summary>What stiffening does to a constraint's compliance between two points.</summary>
        public static float StiffFactor(float stiffA, float stiffB) => Mathf.Pow(10f, -StiffDecades * 0.5f * (Mathf.Clamp01(stiffA) + Mathf.Clamp01(stiffB)));

        /// <summary>The values at each point of a mesh, from the paint kept on the flat pattern. False when there is none.</summary>
        public static bool Resolve(ClothesPattern pattern, PatternMesh mesh, List<FitPaintPoint> paint, out float[] hug, out float[] smooth, out float[] inflate) =>
            Resolve(pattern, mesh, paint, out hug, out smooth, out inflate, out _, out _);

        /// <summary>All five channels at each point of a mesh, from the paint kept on the flat pattern. False when there is none.</summary>
        public static bool Resolve(ClothesPattern pattern, PatternMesh mesh, List<FitPaintPoint> paint, out float[] hug, out float[] smooth, out float[] inflate, out float[] stiff, out float[] weight)
        {
            int n = mesh != null ? mesh.VertexCount : 0;
            hug = new float[n]; smooth = new float[n]; inflate = new float[n]; stiff = new float[n]; weight = new float[n];
            if (paint == null || paint.Count == 0 || pattern == null || n == 0) return false;
            var byPiece = new Dictionary<int, List<FitPaintPoint>>();
            foreach (FitPaintPoint point in paint)
            {
                if (point == null) continue;
                int p = pattern.IndexOfPiece(point.pieceId);
                if (p < 0) continue;
                if (!byPiece.TryGetValue(p, out List<FitPaintPoint> list)) byPiece[p] = list = new List<FitPaintPoint>();
                list.Add(point);
            }
            bool any = false;
            foreach (KeyValuePair<int, List<FitPaintPoint>> kv in byPiece)
            {
                int p = kv.Key;
                if (mesh.pieceStart == null || p >= mesh.pieceStart.Length) continue;
                List<FitPaintPoint> points = kv.Value;
                float[] h = hug, s = smooth, f = inflate, st = stiff, wt = weight;
                Parallel.For(mesh.pieceStart[p], mesh.pieceStart[p] + mesh.pieceCount[p], v =>
                {
                    Vector2 at = mesh.drawn[v];
                    float sum = 0f, sh = 0f, ss = 0f, sf = 0f, sst = 0f, swt = 0f;
                    foreach (FitPaintPoint point in points)
                    {
                        float reach = Mathf.Max(0.3f, point.spacingCm);
                        float d = (point.flatPosition - at).magnitude;
                        if (d >= reach) continue;
                        // A point painted where this one is gives exactly its value (the same mesh again).
                        float w = d < reach * 0.05f ? 1000f : 1f - d / reach;
                        sum += w; sh += w * point.hug; ss += w * point.smooth; sf += w * point.inflate; sst += w * point.stiff; swt += w * point.weight;
                    }
                    if (sum <= 0f) return;
                    // Against an unpainted background: a point at the edge of the paint gets only its share.
                    float k = 1f / Mathf.Max(1f, sum);
                    h[v] = sh * k; s[v] = ss * k; f[v] = sf * k; st[v] = sst * k; wt[v] = swt * k;
                });
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Keeps the values of a mesh's painted points on the flat pattern (the fit's paint is replaced), with the
        /// unpainted points round them as zeros: they hold the paint's edge where it was (without them, every reload
        /// spread it a ring further).
        /// </summary>
        public static void Store(ClothesPattern pattern, PatternMesh mesh, float[] hug, float[] smooth, float[] inflate, List<FitPaintPoint> into) =>
            Store(pattern, mesh, hug, smooth, inflate, null, null, into);

        /// <summary>All five channels; <paramref name="stiff"/> and <paramref name="weight"/> may be null (none painted).</summary>
        public static void Store(ClothesPattern pattern, PatternMesh mesh, float[] hug, float[] smooth, float[] inflate, float[] stiff, float[] weight, List<FitPaintPoint> into)
        {
            into.Clear();
            if (pattern == null || mesh == null || hug == null) return;
            float Stiff(int v) => stiff != null && v < stiff.Length ? stiff[v] : 0f;
            float Weight(int v) => weight != null && v < weight.Length ? weight[v] : 0f;
            bool Painted(int v) => hug[v] >= Keep || smooth[v] >= Keep || Mathf.Abs(inflate[v]) >= Keep || Stiff(v) >= Keep || Weight(v) >= Keep;
            var kept = new bool[mesh.VertexCount];
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                int a = mesh.triangles[t], b = mesh.triangles[t + 1], c = mesh.triangles[t + 2];
                if (!Painted(a) && !Painted(b) && !Painted(c)) continue;
                kept[a] = kept[b] = kept[c] = true;
            }
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if (!kept[v]) continue;
                int p = mesh.piece[v];
                into.Add(new FitPaintPoint
                {
                    pieceId = pattern.pieces[p].id,
                    flatPosition = mesh.drawn[v],
                    hug = hug[v] < Keep ? 0f : hug[v],
                    smooth = smooth[v] < Keep ? 0f : smooth[v],
                    inflate = Mathf.Abs(inflate[v]) < Keep ? 0f : inflate[v],
                    stiff = Stiff(v) < Keep ? 0f : Stiff(v),
                    weight = Weight(v) < Keep ? 0f : Weight(v),
                    spacingCm = mesh.EdgeCmOf(p)
                });
            }
        }

        /// <summary>Paints one point: <paramref name="amount"/> is the brush's strength times its falloff there.</summary>
        public static void Brush(FitPaintMode mode, bool erase, float amount, ref float hug, ref float smooth, ref float inflate)
        {
            float stiff = 0f, weight = 0f;
            Brush(mode, erase, amount, ref hug, ref smooth, ref inflate, ref stiff, ref weight);
        }

        /// <summary>All five channels: Erase takes every channel off; Ctrl (<paramref name="erase"/>) only the mode's own.</summary>
        public static void Brush(FitPaintMode mode, bool erase, float amount, ref float hug, ref float smooth, ref float inflate, ref float stiff, ref float weight)
        {
            float a = Mathf.Clamp01(amount);
            if (mode == FitPaintMode.Erase) { hug *= 1f - a; smooth *= 1f - a; inflate *= 1f - a; stiff *= 1f - a; weight *= 1f - a; return; }
            switch (mode)
            {
                case FitPaintMode.Hug:
                    hug = erase ? hug * (1f - a) : Mathf.Max(hug, a);
                    break;
                case FitPaintMode.Smooth:
                    smooth = erase ? smooth * (1f - a) : Mathf.Max(smooth, a);
                    break;
                case FitPaintMode.Puff:
                    inflate = erase ? (inflate > 0f ? inflate * (1f - a) : inflate) : Mathf.Max(inflate, a);
                    break;
                case FitPaintMode.Slim:
                    inflate = erase ? (inflate < 0f ? inflate * (1f - a) : inflate) : Mathf.Min(inflate, -a);
                    break;
                case FitPaintMode.Stiffen:
                    stiff = erase ? stiff * (1f - a) : Mathf.Max(stiff, a);
                    break;
                case FitPaintMode.Weight:
                    weight = erase ? weight * (1f - a) : Mathf.Max(weight, a);
                    break;
            }
        }

        /// <summary>The colour of a point's paint: grey unpainted, green hugged, blue smoothed, orange puffed, purple slimmed.</summary>
        public static Color Color(float hug, float smooth, float inflate) => Color(hug, smooth, inflate, 0f, 0f);

        /// <summary>All five channels: teal stiffened, brown weighted, as well.</summary>
        public static Color Color(float hug, float smooth, float inflate, float stiff, float weight) =>
            Color(hug, smooth, inflate, stiff, weight, new Color(0.72f, 0.72f, 0.72f));

        /// <summary>
        /// All five channels over a chosen colour for unpainted cloth: white when the paint is multiplied over the
        /// lit cloth, so unpainted cloth shows as it is lit.
        /// </summary>
        public static Color Color(float hug, float smooth, float inflate, float stiff, float weight, Color unpainted)
        {
            Color c = unpainted;
            c = UnityEngine.Color.Lerp(c, new Color(0.05f, 0.72f, 0.7f), Mathf.Clamp01(stiff));
            c = UnityEngine.Color.Lerp(c, new Color(0.5f, 0.3f, 0.12f), Mathf.Clamp01(weight));
            c = UnityEngine.Color.Lerp(c, new Color(0.15f, 0.8f, 0.25f), Mathf.Clamp01(hug));
            c = UnityEngine.Color.Lerp(c, new Color(0.2f, 0.55f, 1f), Mathf.Clamp01(smooth) * 0.8f);
            if (inflate > 0f) c = UnityEngine.Color.Lerp(c, new Color(1f, 0.55f, 0.1f), Mathf.Clamp01(inflate));
            else c = UnityEngine.Color.Lerp(c, new Color(0.65f, 0.25f, 0.95f), Mathf.Clamp01(-inflate));
            return c;
        }

        /// <summary>
        /// This frame's soft pins for hugged and smoothed cloth: hugged points towards the nearest body point, out by
        /// the clearance; smoothed points towards their neighbours, along the cloth's normal.
        /// </summary>
        public static void FramePins(Vector3[] x, ClothModel model, BodyCollider body, float clearance, float[] hug, float[] smooth, int[] hugged, int[] smoothed, List<int>[] around, List<SolverPin> into)
        {
            into.Clear();
            if (hugged != null && hugged.Length > 0 && body != null)
            {
                var targets = new Vector3[hugged.Length];
                var found = new bool[hugged.Length];
                Parallel.For(0, hugged.Length, k =>
                {
                    int i = hugged[k];
                    if (!body.ClosestNear(x[i], 0.02f, out int tri, out Vector3 point, out float sd) || sd > HugReach) return;
                    targets[k] = point + body.N[tri] * (clearance + 0.001f);
                    found[k] = true;
                });
                for (int k = 0; k < hugged.Length; k++)
                {
                    if (found[k]) into.Add(new SolverPin { particle = hugged[k], target = targets[k], weight = hug[hugged[k]] * HugPull });
                }
            }
            if (smoothed != null && smoothed.Length > 0 && around != null)
            {
                // Bi-Laplacian smoothing along the normal: each point moves by how much more it bulges than its
                // neighbours do. A plain Laplacian also flattens the cloth's curve round the body, drawing it in
                // (measured: a smoothed hoodie front came 4.5 mm closer to the body, wrinkles no fewer).
                int[] tris = model.Triangles;
                var laplacian = new Dictionary<int, Vector3>();
                Vector3 L(int i)
                {
                    if (laplacian.TryGetValue(i, out Vector3 known)) return known;
                    List<int> nb = model.Neighbours[i];
                    Vector3 mean = Vector3.zero;
                    if (nb != null && nb.Count > 0) { foreach (int j in nb) mean += x[j]; mean /= nb.Count; }
                    Vector3 value = nb != null && nb.Count > 0 ? mean - x[i] : Vector3.zero;
                    laplacian[i] = value;
                    return value;
                }
                foreach (int i in smoothed)
                {
                    List<int> neighbours = model.Neighbours[i];
                    if (neighbours == null || neighbours.Count < 3 || around[i] == null) continue;
                    Vector3 around2 = Vector3.zero;
                    foreach (int j in neighbours) around2 += L(j);
                    around2 /= neighbours.Count;
                    Vector3 step = L(i) - around2;
                    Vector3 normal = Vector3.zero;
                    foreach (int t in around[i]) normal += Vector3.Cross(x[tris[t + 1]] - x[tris[t]], x[tris[t + 2]] - x[tris[t]]);
                    if (normal.sqrMagnitude < 1e-16f) continue;
                    normal.Normalize();
                    Vector3 target = x[i] + normal * (Vector3.Dot(step, normal) * 0.5f);
                    into.Add(new SolverPin { particle = i, target = target, weight = smooth[i] * SmoothPull });
                }
            }
        }

        /// <summary>How much more a point bulges along the normal than its neighbours do (m): the size of a wrinkle there.</summary>
        public static float Bulge(Vector3[] x, ClothModel model, int i)
        {
            List<int> nb = model.Neighbours[i];
            if (nb == null || nb.Count < 3) return 0f;
            Vector3 Lap(int k)
            {
                List<int> n = model.Neighbours[k];
                if (n == null || n.Count == 0) return Vector3.zero;
                Vector3 m = Vector3.zero;
                foreach (int j in n) m += x[j];
                return m / n.Count - x[k];
            }
            Vector3 around = Vector3.zero;
            foreach (int j in nb) around += Lap(j);
            around /= nb.Count;
            return (Lap(i) - around).magnitude;
        }

        /// <summary>The triangles round each point (index of the triangle's first corner), for its normal.</summary>
        public static List<int>[] TrianglesAround(int count, int[] triangles)
        {
            var around = new List<int>[count];
            if (triangles == null) return around;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int c = 0; c < 3; c++)
                {
                    int v = triangles[t + c];
                    if (v < 0 || v >= count) continue;
                    (around[v] ??= new List<int>(6)).Add(t);
                }
            }
            return around;
        }
    }
}
