using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

namespace UMA.HairCards
{
    public enum HairGatherMode { Tips, PassThrough }
    public enum HairGatherLength { Preserve, ExtendToReach }
    public enum HairGatherSide { Both, Positive, Negative }

    [Serializable]
    public sealed class HairGatherTarget
    {
        public Vector2 radii = new Vector2(.018f, .018f);
        [Min(0)] public float spacing = .001f;
        [Range(0, 1)] public float scatter = .15f;
        public void EnsureIntegrity()
        {
            radii.x = HairGatherSettings.Finite(radii.x, 0, 1);
            radii.y = HairGatherSettings.Finite(radii.y, 0, 1);
            spacing = HairGatherSettings.Finite(spacing, 0, .1f);
            scatter = HairGatherSettings.Finite(scatter, 0, 1);
        }
    }

    [Serializable]
    public sealed class HairGatherSettings
    {
        public HairGatherMode mode;
        public HairGatherLength length;
        public HairGatherSide rootSide;
        public bool startInMeters;
        public float bendStart;
        [Range(0, 1)] public float flexibility = .8f;
        [Range(0, 1)] public float tension = .85f;
        public bool followScalp = true;
        public bool outwardFacing = true;
        [Min(0)] public float surfaceOffset = .0015f;
        public AnimationCurve offsetAlongStrand = AnimationCurve.Constant(0, 1, 1);
        [Range(.05f, .98f)] public float liftOff = .8f;
        [Min(0)] public float rootLift = .002f;
        public AnimationCurve rootBend = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.3f, 1), new Keyframe(1, 0));
        public AnimationCurve arrivalBend = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.6f, 1), new Keyframe(1, 0));
        public float arrivalLift = .005f;
        [Min(0)] public float approachLength = .025f;
        [Range(.1f, .95f)] public float passFraction = .65f;
        public string continuationHelperId;
        [Range(-180, 180)] public float twist;
        [Range(0, 1)] public float variation = .1f;
        public bool fullCardClearance = true;
        [Range(0, .005f)] public float clearance = .0006f;
        public void EnsureIntegrity()
        {
            bendStart = Finite(bendStart, 0, startInMeters ? 10 : .95f);
            flexibility = Finite(flexibility, 0, 1); tension = Finite(tension, 0, 1);
            surfaceOffset = Finite(surfaceOffset, 0, .1f); rootLift = Finite(rootLift, 0, .1f);
            approachLength = Finite(approachLength, 0, 1); liftOff = Finite(liftOff, .05f, .98f);
            passFraction = Finite(passFraction, .1f, .95f); twist = Finite(twist, -180, 180);
            variation = Finite(variation, 0, 1); clearance = Finite(clearance, 0, .005f);
            offsetAlongStrand ??= AnimationCurve.Constant(0, 1, 1);
            rootBend ??= AnimationCurve.Linear(0, 0, 1, 0);
            arrivalBend ??= AnimationCurve.Linear(0, 0, 1, 0);
            arrivalLift = Finite(arrivalLift, -.1f, .1f);
        }
        internal static float Finite(float v, float min, float max) => float.IsFinite(v) ? Mathf.Clamp(v, min, max) : min;
    }

    /// <summary>Source-space, deterministic gather. A welded surface graph prevents paths
    /// taking a chord through the head. Target packing is prepared on the full population,
    /// before Painted Scalp LOD thinning. No authored curve/helper is modified.</summary>
    internal sealed class HairGatherWorkspace
    {
        private static readonly ProfilerMarker Marker = new ProfilerMarker("HairCards.Gather");
        private sealed class Field
        {
            internal bool used;
            internal int version = -1;
            internal HairHelper helper;
            internal float[] distances;
            internal int[] next;
            internal Vector3 surfaceTarget;
            internal readonly Dictionary<string, Vector3> targets = new Dictionary<string, Vector3>();
            internal readonly Dictionary<string, Route> routes = new Dictionary<string, Route>();
            internal int unreachable, crowded, disconnected, affected, inside;
        }
        private sealed class Route
        {
            internal Vector3 root;
            internal readonly List<HairCurvePoint> points = new List<HairCurvePoint>(65);
        }
        private readonly Dictionary<string, Field> fields = new Dictionary<string, Field>();
        private readonly List<string> stale = new List<string>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<List<int>> edges = new List<List<int>>();
        private readonly List<int> triangles = new List<int>();
        private readonly Dictionary<Vector3Int, int> welded = new Dictionary<Vector3Int, int>();
        private int[] remap = Array.Empty<int>();
        private readonly List<KeyValuePair<int, float>> heap = new List<KeyValuePair<int, float>>();
        private readonly List<HairEvaluatedCurve> ordered = new List<HairEvaluatedCurve>();
        private readonly Dictionary<Vector2Int, List<Vector2>> bins = new Dictionary<Vector2Int, List<Vector2>>();
        private readonly List<List<Vector2>> binPool = new List<List<Vector2>>();
        private int usedBins;
        private readonly List<HairCurvePoint> path = new List<HairCurvePoint>(), smooth = new List<HairCurvePoint>();
        private readonly List<HairCurvePoint> desired = new List<HairCurvePoint>();
        private readonly List<Vector3> original = new List<Vector3>(), target = new List<Vector3>();
        private readonly List<HairGuidePoint> controls = new List<HairGuidePoint>();
        private float[] cumulative = Array.Empty<float>(), lengths = Array.Empty<float>();
        private HairMeshRaycaster surface;
        private int geometryVersion = -1;
        private int graphRevision;

        internal void Begin()
        {
            foreach (var field in fields.Values)
            { field.used = false; field.targets.Clear(); field.affected = field.unreachable = field.crowded = field.disconnected = field.inside = 0; }
        }
        internal void End()
        {
            foreach (var field in fields.Values)
            {
                stale.Clear(); foreach (var key in field.routes.Keys) if (!field.targets.ContainsKey(key)) stale.Add(key);
                foreach (var key in stale) field.routes.Remove(key);
            }
            stale.Clear(); foreach (var pair in fields) if (!pair.Value.used) stale.Add(pair.Key);
            foreach (var key in stale) fields.Remove(key); stale.Clear();
        }
        internal void Clear()
        {
            fields.Clear(); vertices.Clear(); edges.Clear(); triangles.Clear(); welded.Clear(); remap = Array.Empty<int>();
            heap.Clear(); ordered.Clear(); bins.Clear(); binPool.Clear(); path.Clear(); smooth.Clear(); desired.Clear();
            original.Clear(); target.Clear(); controls.Clear(); cumulative = lengths = Array.Empty<float>();
            surface = null; geometryVersion = -1;
        }

        private void BuildGraph(HairGroomEvaluator.SourceMeshReadCache mesh)
        {
            var query = mesh.Surface();
            if (query == null) { surface = null; geometryVersion = -1; return; }
            if (ReferenceEquals(surface, query) && geometryVersion == query.GeometryVersion) return;
            surface = query; geometryVersion = query.GeometryVersion;
            graphRevision++;
            vertices.Clear(); edges.Clear(); triangles.Clear(); welded.Clear();
            var positions = mesh.Vertices; remap = new int[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 p = positions[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000), Mathf.RoundToInt(p.y * 100000), Mathf.RoundToInt(p.z * 100000));
                if (!welded.TryGetValue(key, out int index))
                { index = vertices.Count; welded.Add(key, index); vertices.Add(p); edges.Add(new List<int>()); }
                remap[i] = index;
            }
            for (int sub = 0; sub < mesh.SubmeshCount; sub++)
            {
                var indices = mesh.Triangles(sub);
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    int a = remap[indices[i]], b = remap[indices[i + 1]], c = remap[indices[i + 2]];
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    Edge(a, b); Edge(b, c); Edge(c, a);
                }
            }
            void Edge(int a, int b)
            { if (a != b) { if (!edges[a].Contains(b)) edges[a].Add(b); if (!edges[b].Contains(a)) edges[b].Add(a); } }
        }

        internal void Prepare(HairModifierSettings modifier, IReadOnlyList<HairEvaluatedCurve> curves,
            HairGroomAsset groom, HairEvaluationWorkspace workspace)
        {
            var helper = groom.FindHelper(modifier.helperId);
            if (helper == null || helper.type != HairHelperType.Gather) return;
            BuildGraph(workspace.sourceMesh);
            if (!fields.TryGetValue(modifier.Id, out var field)) fields.Add(modifier.Id, field = new Field());
            field.helper = helper; field.used = true;
            Matrix4x4 matrix = helper.LocalToSource;
            if (!HairCoordinateUtility.IsInvertibleAffine(matrix)) return;
            Vector3 center = matrix.MultiplyPoint3x4(Vector3.zero);
            if (surface != null && surface.ClosestPoint(center, out var hit))
            {
                if (field.version != graphRevision || field.surfaceTarget != hit.Point) field.routes.Clear();
                field.version = graphRevision;
                field.surfaceTarget = hit.Point;
                field.distances = new float[vertices.Count]; field.next = new int[vertices.Count];
                Array.Fill(field.distances, float.PositiveInfinity); Array.Fill(field.next, -1); heap.Clear();
                for (int i = 0; i < 3; i++)
                {
                    int index = triangles[hit.TriangleIndex * 3 + i];
                    float d = Vector3.Distance(vertices[index], hit.Point);
                    field.distances[index] = d; Push(index, d);
                }
                while (heap.Count > 0)
                {
                    var entry = Pop(); int node = entry.Key;
                    if (entry.Value > field.distances[node]) continue;
                    foreach (int other in edges[node])
                    {
                        float distance = entry.Value + Vector3.Distance(vertices[node], vertices[other]);
                        if (distance >= field.distances[other]) continue;
                        field.distances[other] = distance; field.next[other] = node; Push(other, distance);
                    }
                }
            }
            else
            {
                // The source can be unassigned/replaced between evaluations in the same
                // workspace. Never reuse routes from a surface that is no longer valid.
                field.routes.Clear(); field.distances = null; field.next = null; field.version = -1;
            }
            ordered.Clear(); foreach (var curve in curves)
            {
                float side = curve.points.Count > 0 ? Vector3.Dot(curve.points[0].position - groom.SymmetryPlanePoint, groom.SymmetryPlaneNormal) : 0;
                if (curve.points.Count > 1 && (modifier.gather.rootSide == HairGatherSide.Both ||
                    (modifier.gather.rootSide == HairGatherSide.Positive ? side >= 0 : side < 0))) ordered.Add(curve);
            }
            ordered.Sort((a, b) => string.CompareOrdinal(a.curveId, b.curveId));
            bins.Clear(); usedBins = 0;
            Vector2 radii = helper.gather.radii;
            float spacing = helper.gather.spacing;
            Matrix4x4 inverse = matrix.inverse;
            foreach (var curve in ordered)
            {
                Vector3 localRoot = inverse.MultiplyPoint3x4(curve.points[0].position);
                float angle = Mathf.Atan2(localRoot.z, localRoot.x);
                var random = new HairDeterministicRandom(curve.seed ^ 0x31ab47);
                float radial = Mathf.Sqrt(Mathf.Lerp(.55f, 1, random.Next01()));
                Vector2 candidate = Vector2.zero; bool fits = false;
                for (int attempt = 0; attempt < 64; attempt++)
                {
                    float a = angle + random.NextSigned() * (helper.gather.scatter + attempt * .06f);
                    float r = attempt == 0 ? radial : Mathf.Sqrt(random.Next01());
                    candidate = new Vector2(Mathf.Cos(a) * radii.x, Mathf.Sin(a) * radii.y) * r;
                    if (spacing <= 0 || Fits(candidate, spacing)) { fits = true; break; }
                }
                if (!fits) field.crowded++;
                if (spacing > 0)
                {
                    var cell = Cell(candidate, spacing);
                    if (!bins.TryGetValue(cell, out var list))
                    {
                        if (usedBins == binPool.Count) binPool.Add(new List<Vector2>());
                        list = binPool[usedBins++]; list.Clear(); bins.Add(cell, list);
                    }
                    list.Add(candidate);
                }
                Vector3 endpoint = matrix.MultiplyPoint3x4(new Vector3(candidate.x, 0, candidate.y));
                field.targets[curve.curveId] = endpoint;
                if (modifier.gather.followScalp && surface != null && surface.ClosestPoint(endpoint, out var ringHit) && ringHit.Distance < .25f)
                {
                    surface.TryGetSurfaceNormal(ringHit.TriangleIndex, ringHit.Barycentric, out var ringNormal);
                    if (Vector3.Dot(endpoint - ringHit.Point, ringNormal) < -.0005f) field.inside++;
                }
            }
        }

        private static Vector2Int Cell(Vector2 p, float spacing) => new Vector2Int(Mathf.FloorToInt(p.x / spacing), Mathf.FloorToInt(p.y / spacing));
        private bool Fits(Vector2 p, float spacing)
        {
            Vector2Int key = Cell(p, spacing);
            for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                if (bins.TryGetValue(key + new Vector2Int(x, y), out var list))
                    foreach (var other in list) if ((p - other).sqrMagnitude < spacing * spacing) return false;
            return true;
        }

        private void Push(int node, float distance)
        {
            var entry = new KeyValuePair<int, float>(node, distance); int i = heap.Count; heap.Add(entry);
            while (i > 0) { int parent = (i - 1) / 2; if (heap[parent].Value <= distance) break; heap[i] = heap[parent]; i = parent; }
            heap[i] = entry;
        }
        private KeyValuePair<int, float> Pop()
        {
            var first = heap[0]; var last = heap[heap.Count - 1]; heap.RemoveAt(heap.Count - 1);
            if (heap.Count == 0) return first;
            int i = 0;
            while (i * 2 + 1 < heap.Count)
            { int child = i * 2 + 1; if (child + 1 < heap.Count && heap[child + 1].Value < heap[child].Value) child++;
                if (heap[child].Value >= last.Value) break; heap[i] = heap[child]; i = child; }
            heap[i] = last; return first;
        }

        private bool SurfacePath(string id, Vector3 root, Field field)
        {
            if (surface == null || field.distances == null) return false;
            if (field.routes.TryGetValue(id, out var route) && route.root == root)
            { path.Clear(); path.AddRange(route.points); return true; }
            path.Clear(); path.Add(new HairCurvePoint(root, 0, 0));
            if (!surface.ClosestPoint(root, out var hit)) return false;
            // Integrate on the continuous surface using interpolated vertex normals. Raw
            // shortest-edge paths funnel adjacent roots into visible mesh-edge channels.
            // Keep the welded graph as a downhill guide and a disconnected/concave fallback.
            bool continuous = ContinuousPath(hit, field);
            if (!continuous)
            {
            path.Clear(); path.Add(new HairCurvePoint(root, 0, 0));
            int next = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                int v = triangles[hit.TriangleIndex * 3 + i]; float d = field.distances[v] + Vector3.Distance(root, vertices[v]);
                if (d < best) { best = d; next = v; }
            }
            if (next < 0) return false;
            int guard = 0;
            while (next >= 0 && guard++ <= vertices.Count)
            { path.Add(new HairCurvePoint(vertices[next], 0, 0)); next = field.next[next]; }
            path.Add(new HairCurvePoint(field.surfaceTarget, 0, 0));
            }
            HairCurveUtility.ResampleInto(path, 65, smooth, ref cumulative);
            // Smooth stair-stepping of the graph, projecting each relaxation onto the scalp.
            for (int pass = 0; pass < 6; pass++)
            {
                path.Clear(); path.AddRange(smooth);
                for (int i = 1; i < smooth.Count - 1; i++)
                {
                    Vector3 p = (path[i - 1].position + path[i].position * 2 + path[i + 1].position) * .25f;
                    var point = smooth[i];
                    if (surface.ClosestPoint(p, out var h)) point.position = h.Point;
                    smooth[i] = point;
                }
            }
            path.Clear(); path.AddRange(smooth);
            if (route == null) field.routes.Add(id, route = new Route());
            route.root = root; route.points.Clear(); route.points.AddRange(path);
            return true;
        }

        private bool ContinuousPath(HairMeshRaycastHit hit, Field field)
        {
            float Potential(HairMeshRaycastHit h)
            {
                int k = h.TriangleIndex * 3;
                return field.distances[triangles[k]] * h.Barycentric.x + field.distances[triangles[k + 1]] * h.Barycentric.y + field.distances[triangles[k + 2]] * h.Barycentric.z;
            }
            float initial = Potential(hit);
            if (!float.IsFinite(initial)) return false;
            float traveled = 0;
            for (int step = 0; step < 512; step++)
            {
                Vector3 toTarget = field.surfaceTarget - hit.Point;
                float remaining = toTarget.magnitude;
                if (remaining < .002f) { path.Add(new HairCurvePoint(field.surfaceTarget, 0, 0)); return true; }
                surface.TryGetSurfaceNormal(hit.TriangleIndex, hit.Barycentric, out var normal);
                Vector3 direction = Vector3.ProjectOnPlane(toTarget, normal).normalized;
                float distance = Mathf.Min(.003f, remaining * .6f);
                bool valid = surface.ClosestPoint(hit.Point + direction * distance, out var next);
                if (!valid || direction.sqrMagnitude < .1f || Potential(next) > Potential(hit) + distance * .2f || (next.Point - hit.Point).sqrMagnitude < distance * distance * .01f)
                {
                    // A concavity or a near-normal target needs the surface graph's direction.
                    int k = hit.TriangleIndex * 3; direction = Vector3.zero;
                    for (int i = 0; i < 3; i++)
                    {
                        int v = triangles[k + i], n = field.next[v];
                        direction += (n >= 0 ? vertices[n] - vertices[v] : toTarget).normalized * hit.Barycentric[i];
                    }
                    direction = Vector3.ProjectOnPlane(direction, normal).normalized;
                    if (!surface.ClosestPoint(hit.Point + direction * distance, out next) || (next.Point - hit.Point).sqrMagnitude < distance * distance * .01f) return false;
                }
                traveled += Vector3.Distance(hit.Point, next.Point);
                if (traveled > Mathf.Max(.02f, initial * 2)) return false;
                path.Add(new HairCurvePoint(next.Point, 0, 0)); hit = next;
            }
            return false;
        }

        internal void Apply(HairEvaluatedCurve curve, HairModifierSettings modifier, HairGroomAsset groom, HairEvaluationWorkspace workspace)
        {
            if (curve.points.Count < 2 || !fields.TryGetValue(modifier.Id, out var field) || !field.used ||
                !field.targets.TryGetValue(curve.curveId, out Vector3 endpoint)) return;
            using var marker = Marker.Auto();
            var settings = modifier.gather; int count = curve.points.Count;
            var matrix = field.helper.LocalToSource;
            Vector3 axis = matrix.MultiplyVector(Vector3.up).normalized;
            float oldLength = curve.Length;
            float protectedLength = settings.startInMeters ? settings.bendStart : oldLength * settings.bendStart;
            int first = 0; float protectedDistance = 0;
            while (first < count - 2 && protectedDistance + Vector3.Distance(curve.points[first].position, curve.points[first + 1].position) <= protectedLength)
            { protectedDistance += Vector3.Distance(curve.points[first].position, curve.points[first + 1].position); first++; }
            int tie = settings.mode == HairGatherMode.PassThrough ? Mathf.Clamp(Mathf.RoundToInt(settings.passFraction * (count - 1)), first + 1, count - 1) : count - 1;
            original.Clear(); target.Clear(); desired.Clear();
            while (controls.Count < count) controls.Add(new HairGuidePoint());
            if (lengths.Length < count) Array.Resize(ref lengths, Mathf.NextPowerOfTwo(count));
            for (int i = 0; i < count; i++)
            {
                var point = curve.points[i]; original.Add(point.position); target.Add(point.position);
                controls[i].freeze = point.freeze; controls[i].stiffness = point.stiffness;
                if (i > 0) lengths[i] = Vector3.Distance(curve.points[i - 1].position, point.position);
            }
            Vector3 start = original[first];
            bool anyMovable = false;
            for (int i = first + 1; i < count; i++) anyMovable |= curve.points[i].freeze < .999f &&
                (modifier.rootToTip == null || modifier.rootToTip.Evaluate(i / (count - 1f)) > 0) &&
                (curve.points[i].stiffness < .999f || settings.flexibility > 0);
            if (!anyMovable) return;
            if (settings.followScalp)
            {
                if (!SurfacePath(curve.curveId, start, field)) { field.disconnected++; return; }
            }
            else { path.Clear(); path.Add(new HairCurvePoint(start, 0, 0)); path.Add(new HairCurvePoint(endpoint, 0, 0)); }
            float leave = settings.followScalp ? settings.liftOff : 0;
            if (settings.followScalp)
            {
                // A packed slot is not at the ring's center. Stop following the common
                // surface route before passing that slot, otherwise a wide ring produces
                // a U-turn immediately before the tie instead of a smooth S-bend.
                float nearest = float.PositiveInfinity; int nearestIndex = 0;
                for (int i = 0; i < path.Count; i++)
                {
                    float d = (path[i].position - endpoint).sqrMagnitude;
                    if (d < nearest) { nearest = d; nearestIndex = i; }
                }
                leave = Mathf.Min(leave, nearestIndex / (path.Count - 1f));
            }
            Vector3 launch = Sample(path, leave), previous = Sample(path, Mathf.Max(0, leave - .025f));
            Vector3 launchNormal = curve.rootNormal;
            if (settings.followScalp && surface.ClosestPoint(launch, out var launchHit)) launchNormal = launchHit.Normal;
            float SurfaceLift(float t) => settings.surfaceOffset * Mathf.Max(0, settings.offsetAlongStrand.Evaluate(t)) +
                settings.rootLift * settings.rootBend.Evaluate(Mathf.Clamp01(t / Mathf.Max(.001f, leave))) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / Mathf.Max(.001f, leave))) +
                (1 - settings.tension) * .012f * Mathf.Sin(t * Mathf.PI);
            if (settings.followScalp) launch += launchNormal * SurfaceLift(leave);
            Vector3 direction = settings.followScalp ? (launch - previous).normalized : (original[first + 1] - start).normalized;
            if (direction.sqrMagnitude < .01f) direction = (endpoint - launch).normalized;
            float distance = Vector3.Distance(launch, endpoint);
            float approach = Mathf.Min(settings.approachLength, distance * .75f);
            Vector3 controlA = launch + direction * distance * Mathf.Lerp(.5f, .2f, settings.tension);
            Vector3 controlB = endpoint - axis * approach;
            var random = new HairDeterministicRandom(curve.seed ^ modifier.seed);
            float variation = random.NextSigned() * settings.variation;
            for (int i = 0; i < count; i++)
            {
                var p = curve.points[i]; float t = Mathf.InverseLerp(first, tie, i);
                Vector3 normal = curve.rootNormal;
                if (i <= first) { desired.Add(p); continue; }
                if (i <= tie)
                {
                    if (settings.followScalp && t < leave)
                    {
                        Vector3 candidate = Sample(path, t);
                        if (surface.ClosestPoint(candidate, out var h)) { candidate = h.Point; normal = h.Normal; }
                        p.position = candidate + normal * SurfaceLift(t);
                    }
                    else
                    {
                        float u = Mathf.InverseLerp(leave, 1, t);
                        p.position = Bezier(launch, controlA, controlB, endpoint, u);
                        // Endpoint-zero envelope keeps both bend profiles from displacing anchors.
                        p.position += launchNormal * (settings.arrivalLift * settings.arrivalBend.Evaluate(u) * Mathf.Sin(Mathf.PI * u));
                        if (!settings.followScalp)
                            p.position += launchNormal * (settings.rootLift * settings.rootBend.Evaluate(u) * Mathf.Sin(Mathf.PI * u));
                        p.position += launchNormal * (variation * .002f * Mathf.Sin(Mathf.PI * u));
                        Vector3 radial = Vector3.ProjectOnPlane(p.position - matrix.MultiplyPoint3x4(Vector3.zero), axis).normalized;
                        if (radial.sqrMagnitude < .01f) radial = matrix.MultiplyVector(Vector3.forward).normalized;
                        normal = Vector3.Slerp(launchNormal, radial, Mathf.SmoothStep(0, 1, u));
                    }
                }
                else
                {
                    float u = (i - tie) / (count - tie - 1f);
                    var continuation = groom.FindHelper(settings.continuationHelperId);
                    if (continuation?.points?.Count > 1)
                        p.position = endpoint + HairFormUtility.RailPoint(continuation.points, u, true) - continuation.points[0];
                    else p.position = endpoint + axis * (oldLength * (1 - settings.passFraction) * u);
                    normal = Vector3.ProjectOnPlane(desired[tie].facingNormal, axis).normalized;
                }
                p.facingNormal = normal; p.facingWeight = settings.outwardFacing ? 1 : p.facingWeight;
                desired.Add(p);
            }
            float needed = 0, available = 0;
            for (int i = first + 1; i <= tie; i++) { needed += Vector3.Distance(desired[i - 1].position, desired[i].position); available += lengths[i]; }
            float extension = settings.length == HairGatherLength.ExtendToReach ? Mathf.Max(1, needed / Mathf.Max(available, 1e-6f)) : 1;
            if (settings.length == HairGatherLength.ExtendToReach)
                for (int i = first + 1; i <= tie; i++)
                    lengths[i] = Mathf.Lerp(lengths[i], Vector3.Distance(desired[i - 1].position, desired[i].position) * Mathf.Max(1, available / Mathf.Max(needed, 1e-6f)), modifier.weight);
            for (int i = first + 1; i < count; i++)
            {
                float t = i / (count - 1f);
                float ramp = modifier.rootToTip != null ? Mathf.Clamp01(modifier.rootToTip.Evaluate(t)) : 1;
                float response = Mathf.Clamp01(modifier.weight * ramp) * (1 - curve.points[i].freeze) *
                    Mathf.Lerp(1 - curve.points[i].stiffness, 1, settings.flexibility);
                float rootRamp = HairGuideShapeUtility.RootInfluence(Mathf.InverseLerp(first, tie, i), modifier.rootInfluence);
                target[i] = Vector3.Lerp(original[i], desired[i].position, response * rootRamp);
            }
            // Bounded position-based iterations: anchor roots/frozen controls and preserve each
            // incoming segment (scaled only by the explicit Extend option). Surface projection
            // participates in the solve, rather than stretching a completed result afterward.
            bool fullyReaching = modifier.weight >= .999f && modifier.rootInfluence >= .999f;
            for (int i = first + 1; i < count; i++)
                fullyReaching &= curve.points[i].freeze <= 0 && (curve.points[i].stiffness <= 0 || settings.flexibility >= .999f) &&
                    (modifier.rootToTip == null || modifier.rootToTip.Evaluate(i / (count - 1f)) >= .999f);
            // The requested shape already has exactly the explicitly extended segment lengths.
            // No iterative solve is necessary, and preserving it avoids gratuitous S-curve drift.
            bool directShape = fullyReaching && settings.length == HairGatherLength.ExtendToReach && needed >= available;
            for (int iteration = 0; !directShape && iteration < 48; iteration++)
            {
                if (fullyReaching && needed <= available * extension + .0001f && curve.points[tie].freeze <= 0)
                    target[tie] = desired[tie].position;
                for (int i = tie; i > first; i--)
                    if (i - 1 > first && curve.points[i - 1].freeze < .999f)
                        target[i - 1] = target[i] + SafeDirection(target[i - 1] - target[i], original[i - 1] - original[i]) * lengths[i];
                target[first] = original[first];
                for (int i = first + 1; i < count; i++)
                {
                    if (curve.points[i].freeze >= .999f) { target[i] = original[i]; continue; }
                    target[i] = target[i - 1] + SafeDirection(target[i] - target[i - 1], original[i] - original[i - 1]) * lengths[i];
                    if (iteration % 6 == 0 && settings.followScalp && surface.ClosestPoint(target[i], out var h))
                    {
                        float clearance = Mathf.Min(settings.surfaceOffset, settings.clearance);
                        float signed = Vector3.Dot(target[i] - h.Point, h.Normal);
                        if (signed < clearance && Vector3.Distance(target[i], h.Point) < .06f)
                            target[i] += h.Normal * (clearance - signed);
                    }
                }
            }
            // Exact final forward length pass. Fully frozen spans retain their incoming anchors.
            for (int i = first + 1; i < count; i++)
                if (!directShape && curve.points[i].freeze < .999f)
                    target[i] = target[i - 1] + SafeDirection(target[i] - target[i - 1], original[i] - original[i - 1]) * lengths[i];
            if (settings.length == HairGatherLength.Preserve)
            {
                for (int i = 0; i <= first; i++) controls[i].freeze = 1;
                HairGuideShapeUtility.PreserveSegmentLengths(original, target, controls);
            }
            Vector3 previousTangent = (target[Mathf.Min(first + 1, count - 1)] - target[Mathf.Max(0, first - 1)]).normalized;
            Vector3 previousNormal = Vector3.ProjectOnPlane(curve.rootNormal, previousTangent).normalized;
            if (previousNormal.sqrMagnitude < .01f) previousNormal = Vector3.ProjectOnPlane(matrix.MultiplyVector(Vector3.forward), previousTangent).normalized;
            if (previousNormal.sqrMagnitude < .01f) previousNormal = Vector3.ProjectOnPlane(matrix.MultiplyVector(Vector3.right), previousTangent).normalized;
            if (settings.outwardFacing && first == 0 && curve.points[0].freeze < .999f)
            {
                var rootPoint = curve.points[0]; rootPoint.facingNormal = previousNormal;
                rootPoint.facingWeight = Mathf.Clamp01(modifier.weight); curve.points[0] = rootPoint;
            }
            for (int i = first + 1; i < count; i++)
            {
                var p = curve.points[i]; p.position = target[i];
                if (settings.outwardFacing && p.freeze < .999f)
                {
                    Vector3 tangent = (target[Mathf.Min(count - 1, i + 1)] - target[Mathf.Max(0, i - 1)]).normalized;
                    Vector3 transported = Vector3.ProjectOnPlane(Quaternion.FromToRotation(previousTangent, tangent) * previousNormal, tangent).normalized;
                    Vector3 normal = transported;
                    // Conform on the scalp, then parallel-transport the last outward frame
                    // into the bundle. A radial bundle frame is singular at its center and
                    // can turn a ribbon inside-out when it crosses the ring axis.
                    if (settings.followScalp && Mathf.InverseLerp(first, tie, i) <= leave && surface.ClosestPoint(p.position, out var h))
                    {
                        surface.TryGetSurfaceNormal(h.TriangleIndex, h.Barycentric, out var scalpNormal);
                        Vector3 projected = Vector3.ProjectOnPlane(scalpNormal, tangent);
                        normal = projected.sqrMagnitude < .0225f ? transported : projected.normalized;
                        if (Vector3.Dot(normal, transported) < 0) normal = -normal;
                        // Limit twist per physical distance, not per control point. This
                        // keeps a coarse mesh from jumping across several abrupt fine frames.
                        float turn = Mathf.Clamp(Vector3.Distance(target[i - 1], target[i]) * 80, .01f, Mathf.PI / 6);
                        normal = Vector3.RotateTowards(transported, normal, turn, 1).normalized;
                    }
                    if (normal.sqrMagnitude > .01f) { p.facingNormal = normal; p.facingWeight = Mathf.Clamp01(modifier.weight); }
                    previousTangent = tangent; previousNormal = normal;
                }
                p.roll += settings.twist * Mathf.InverseLerp(first, tie, i) * modifier.weight * (1 - p.freeze);
                curve.points[i] = p;
            }
            if (settings.fullCardClearance)
                curve.gatherClearance = Mathf.Max(curve.gatherClearance, settings.clearance);
            curve.gatherFrameContinuity |= settings.outwardFacing;
            field.affected++;
            if ((target[tie] - endpoint).sqrMagnitude > .003f * .003f) field.unreachable++;
        }

        internal void Report(HairModifierSettings modifier, HairEvaluationResult result)
        {
            if (result == null) return;
            if (!fields.TryGetValue(modifier.Id, out var field) || !field.used)
            { result.warnings.Add($"{modifier.name}: assign a Gather helper."); return; }
            if (field.unreachable > 0) result.warnings.Add($"{modifier.name}: {field.unreachable} strands did not reach the ring within 3 mm. Check length, strength, root protection, frozen points, or enable Extend To Reach explicitly.");
            if (field.crowded > 0) result.warnings.Add($"{modifier.name}: {field.crowded} gather slots cannot meet minimum spacing. Enlarge the ring or reduce spacing/count.");
            if (field.disconnected > 0) result.warnings.Add($"{modifier.name}: {field.disconnected} roots have no connected scalp path to the target; unchanged.");
            if (field.inside > 0) result.warnings.Add($"{modifier.name}: {field.inside} gather slots are inside the scalp. Move the ring outward or reduce its radius; card clearance cannot make an interior target valid.");
        }
        private static Vector3 SafeDirection(Vector3 value, Vector3 fallback) => value.sqrMagnitude > 1e-16f ? value.normalized : fallback.normalized;
        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        { float u = 1 - t; return a * (u * u * u) + b * (3 * u * u * t) + c * (3 * u * t * t) + d * (t * t * t); }
        private static Vector3 Sample(List<HairCurvePoint> points, float t)
        {
            float index = Mathf.Clamp01(t) * (points.Count - 1); int a = Mathf.FloorToInt(index);
            return Vector3.Lerp(points[a].position, points[Mathf.Min(a + 1, points.Count - 1)].position, index - a);
        }
    }
}
