using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.TexturePaint
{
    public enum TexturePaintProjectionMode { Planar, Wrapped, Cylindrical }
    public enum TexturePaintProjectionFade { None, Ellipse, Rectangle, AlphaOutline }

    [Serializable]
    public sealed class TexturePaintProjectionChannelSource
    {
        public TexturePaintChannel channel;
        public Texture2D texture;
        public Sprite sprite;
        public TexturePaintNormalConvention normalConvention;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Texture;
        public OverlayDataAsset overlay;
        public Color color = Color.white;
        public Color multiplier = Color.white;
        public Color additive = Color.clear;
        [Range(0, 16)] public int blur;
        public bool invert;
        public bool HasSource => source == TexturePaintBrushSource.Color ||
            (source == TexturePaintBrushSource.Overlay ? overlay != null : texture != null || sprite != null);
        public TexturePaintProjectionChannelSource Clone() => (TexturePaintProjectionChannelSource)MemberwiseClone();
        public Texture Resolve(TextureSet set = null)
        {
            if (source == TexturePaintBrushSource.Color) return Texture2D.whiteTexture;
            if (source == TexturePaintBrushSource.Overlay)
                return set != null && set.TryResolveOverlaySource(overlay, channel, normalConvention, invert,
                    out Texture resolved, out _) ? resolved : null;
            return TexturePaintSpriteSource.Resolve(texture, sprite, channel, normalConvention, invert, blur);
        }
        public TexturePaintChannelSourceSettings ToChannelSettings() => new TexturePaintChannelSourceSettings
        {
            source = source, sourceTexture = texture, sourceSprite = sprite, sourceOverlay = overlay,
            color = color, multiplier = multiplier, additive = additive, blur = blur, invert = invert, normalConvention = normalConvention
        };
        public void ApplyChannelSettings(TexturePaintChannelSourceSettings settings)
        {
            source = settings.source; texture = settings.sourceTexture; sprite = settings.sourceSprite;
            overlay = settings.sourceOverlay; color = settings.color; invert = settings.invert;
            multiplier = settings.multiplier; additive = settings.additive;
            blur = settings.blur;
            normalConvention = settings.normalConvention;
        }
    }

    [Serializable]
    public sealed class TexturePaintProjectionSettings
    {
        public TexturePaintGarmentSettings garment;
        public TexturePaintBrushSource source = TexturePaintBrushSource.Texture;
        public Texture2D texture;
        public Sprite sprite;
        public OverlayDataAsset overlay;
        public TexturePaintChannel channel = TexturePaintChannel.Albedo;
        public TexturePaintNormalConvention normalConvention;
        // Version zero retains the public legacy single-image fields until channel authoring begins.
        public int channelSourcesVersion;
        public List<TexturePaintProjectionChannelSource> channelSources = new List<TexturePaintProjectionChannelSource>();
        public bool HasChannelSources => channelSourcesVersion > 0 || channelSources?.Count > 0;

        public IEnumerable<TexturePaintProjectionChannelSource> GetChannelSources()
        {
            if (HasChannelSources)
            {
                if (channelSources != null)
                    foreach (var item in channelSources) if (item != null) yield return item;
            }
            else yield return new TexturePaintProjectionChannelSource
                { channel = channel, texture = texture, sprite = sprite, normalConvention = normalConvention };
        }

        public void UpgradeChannelSources()
        {
            channelSources ??= new List<TexturePaintProjectionChannelSource>();
            if (!HasChannelSources)
                channelSources.Add(new TexturePaintProjectionChannelSource
                    { channel = channel, texture = texture, sprite = sprite, normalConvention = normalConvention });
            channelSourcesVersion = 1;
        }

        public TexturePaintProjectionChannelSource GetChannelSource(TexturePaintChannel target, bool create = false)
        {
            if (create) UpgradeChannelSources();
            foreach (var item in GetChannelSources()) if (item.channel == target) return item;
            if (!create) return null;
            var added = new TexturePaintProjectionChannelSource { channel = target, normalConvention = normalConvention };
            channelSources.Add(added);
            return added;
        }

        public TexturePaintProjectionChannelSource PreferredSource()
        {
            TexturePaintProjectionChannelSource first = null;
            foreach (var item in GetChannelSources())
            {
                if (!item.HasSource) continue;
                if (item.channel == TexturePaintChannel.Albedo) return item;
                if (first == null) first = item;
            }
            return first;
        }

        // Material maps share this silhouette. Textured Normal Control uses its own alpha.
        // Without albedo, the first assigned map owns the shared silhouette.
        public Texture ResolveCoverage() => ResolveCoverage(0);

        public Texture ResolveCoverage(int blur)
        {
            if (source == TexturePaintBrushSource.Overlay) return TextureSet.GetOverlayFillCoverage(overlay);
            var preferred = PreferredSource();
            if (preferred?.source == TexturePaintBrushSource.Texture)
                return TexturePaintSpriteSource.Resolve(preferred.texture, preferred.sprite, preferred.channel,
                    preferred.normalConvention, preferred.invert, blur);
            return preferred?.source == TexturePaintBrushSource.Overlay
                ? TextureSet.GetOverlayFillCoverage(preferred.overlay) : preferred?.Resolve();
        }

        public TexturePaintChannelSourceSettings GetChannelSourceSettings(TexturePaintChannel target)
        {
            if (source == TexturePaintBrushSource.Overlay)
                return new TexturePaintChannelSourceSettings { source = source, sourceOverlay = overlay,
                    normalConvention = normalConvention };
            return GetChannelSource(target)?.ToChannelSettings() ?? new TexturePaintChannelSourceSettings();
        }

        public bool placed;
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
        public float width = 0.1f, height = 0.1f, depth = 0.04f;
        public bool lockAspect = true;
        // False preserves the legacy total-depth convention. Surface placement uses depth as
        // the maximum distance on either side of the clicked plane, including nearby raised faces.
        public bool depthFromSurface;
        public float FrontDepth => depthFromSurface ? depth : depth * 0.5f;
        public float BackDepth => depthFromSurface ? -depth : -depth * 0.5f;
        public bool flipX, flipY;
        public TexturePaintProjectionMode mode;
        // X/Y are fractions of width/height; Z is a world-unit offset from the frame.
        public Vector3[] points = DefaultPoints();
        public int gridSize = 3;
        public bool[] pinned = new bool[9];
        [Range(10f, 360f)] public float cylinderAngle = 360f;
        public int PatchDivisions => mode == TexturePaintProjectionMode.Planar ? 1 :
            mode == TexturePaintProjectionMode.Cylindrical ? 16 : 8;
        public TexturePaintProjectionFade fade = TexturePaintProjectionFade.Rectangle;
        [Range(0f, 0.5f)] public float edgeWidth = 0.1f;
        public AnimationCurve falloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Range(0f, 1f)] public float depthFade = 0.15f;
        public bool frontFacesOnly = true;
        [Range(0f, 180f)] public float angleStart = 60f, angleEnd = 85f;
        public bool firstSurfaceOnly = true;
        public float visibilityTolerance = 0.0002f;
        public int visibilityResolution = 256;
        public bool connectedSurfaceOnly = true;
        public string regionSurfaceId;
        public int regionTriangle = -1;

        public static Vector3[] DefaultPoints(int size = 3)
        {
            var result = new Vector3[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                result[y * size + x] = new Vector3((float)x / (size - 1) - 0.5f, (float)y / (size - 1) - 0.5f, 0f);
            return result;
        }

        public TexturePaintProjectionSettings Clone()
        {
            var copy = (TexturePaintProjectionSettings)MemberwiseClone();
            copy.garment = garment?.Clone();
            copy.channelSources = new List<TexturePaintProjectionChannelSource>();
            if (channelSources != null)
                foreach (var item in channelSources) if (item != null) copy.channelSources.Add(item.Clone());
            copy.points = points != null ? (Vector3[])points.Clone() : DefaultPoints(gridSize);
            copy.pinned = pinned != null ? (bool[])pinned.Clone() : new bool[gridSize * gridSize];
            copy.falloff = falloff != null ? new AnimationCurve(falloff.keys)
                { preWrapMode = falloff.preWrapMode, postWrapMode = falloff.postWrapMode } : AnimationCurve.Linear(0, 0, 1, 1);
            return copy;
        }

        public void Normalize()
        {
            channelSources ??= new List<TexturePaintProjectionChannelSource>();
            var seen = new HashSet<TexturePaintChannel>();
            channelSources.RemoveAll(item => item == null || !Enum.IsDefined(typeof(TexturePaintChannel), item.channel) || !seen.Add(item.channel));
            width = Mathf.Max(0.0001f, Finite(width,.1f)); height = Mathf.Max(0.0001f, Finite(height,.1f)); depth = Mathf.Max(0.0001f, Finite(depth,.04f));
            position=new Vector3(Finite(position.x,0),Finite(position.y,0),Finite(position.z,0));
            float magnitude=Quaternion.Dot(rotation,rotation);
            rotation = float.IsFinite(magnitude) && magnitude>1e-12f ? Quaternion.Normalize(rotation) : Quaternion.identity;
            gridSize = gridSize == 5 || gridSize == 9 ? gridSize : 3;
            if (points == null || points.Length != gridSize * gridSize) points = DefaultPoints(gridSize);
            if (pinned == null || pinned.Length != points.Length) pinned = new bool[points.Length];
            cylinderAngle = Mathf.Clamp(Finite(cylinderAngle, 360f), 10f, 360f);
            var defaults=DefaultPoints(gridSize);
            for(int i=0;i<points.Length;i++)points[i]=new Vector3(Finite(points[i].x,defaults[i].x),Finite(points[i].y,defaults[i].y),Finite(points[i].z,0));
            falloff ??= AnimationCurve.Linear(0, 0, 1, 1);
            edgeWidth = Mathf.Clamp(Finite(edgeWidth,.1f), 0f, 0.5f); depthFade = Mathf.Clamp01(Finite(depthFade,.15f));
            angleStart = Mathf.Clamp(Finite(angleStart,60f), 0f, 180f); angleEnd = Mathf.Clamp(Finite(angleEnd,85f), angleStart, 180f);
            visibilityTolerance = Mathf.Max(0.000001f, Finite(visibilityTolerance,.0002f));
            visibilityResolution = Mathf.Clamp(visibilityResolution, 64, 512);
        }

        private static float Finite(float value,float fallback) => float.IsFinite(value)?value:fallback;

        public void PlaceOnSurface(Vector3 point, Vector3 normal, string surfaceId, int triangle)
        {
            Normalize();
            normal = normal.normalized;
            if (!float.IsFinite(normal.sqrMagnitude) || normal.sqrMagnitude < 0.5f) throw new ArgumentException("The surface has no valid normal.", nameof(normal));
            // Transport the existing tangent frame so an artist's rotation survives repositioning.
            if (placed) rotation = Quaternion.FromToRotation(rotation * Vector3.forward, normal) * rotation;
            else
            {
                Vector3 up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
                rotation = Quaternion.LookRotation(normal, up);
            }
            position = point; placed = true; depthFromSurface = true;
            regionSurfaceId = surfaceId; regionTriangle = triangle;
            points = DefaultPoints(gridSize);
            pinned = new bool[points.Length];
        }

        public Vector3 PointToWorld(Vector3 point) => position + rotation *
            new Vector3(point.x * width, point.y * height, point.z);
        public Vector3 WorldToPoint(Vector3 world)
        {
            Vector3 p = Quaternion.Inverse(rotation) * (world - position);
            return new Vector3(p.x / width, p.y / height, p.z);
        }

        // Interpolating biquadratic patch: all nine handles lie on the patch.
        public Vector3 Evaluate(float u, float v)
        {
            if (mode == TexturePaintProjectionMode.Planar)
                return PointToWorld(new Vector3(u - 0.5f, v - 0.5f, 0f));
            if (mode == TexturePaintProjectionMode.Cylindrical)
            {
                float arc = cylinderAngle * Mathf.Deg2Rad, radius = width / arc, angle = (u - .5f) * arc;
                return position + rotation * new Vector3(Mathf.Sin(angle) * radius, (v - .5f) * height,
                    (Mathf.Cos(angle) - 1f) * radius);
            }
            int segments = (gridSize - 1) / 2;
            int sx = Mathf.Clamp(Mathf.FloorToInt(u * segments), 0, segments - 1);
            int sy = Mathf.Clamp(Mathf.FloorToInt(v * segments), 0, segments - 1);
            Vector3 weightsU = Weights(u * segments - sx), weightsV = Weights(v * segments - sy), p = Vector3.zero;
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                p += points[(sy * 2 + y) * gridSize + sx * 2 + x] * weightsU[x] * weightsV[y];
            return PointToWorld(p);
        }
        // Resampling retains the current shape and pins at matching grid coordinates.
        public void ResizeGrid(int size)
        {
            Normalize(); size = size == 5 || size == 9 ? size : 3;
            if (size == gridSize) return;
            var next = new Vector3[size * size]; var pins = new bool[next.Length];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float u = (float)x / (size - 1), v = (float)y / (size - 1);
                next[y * size + x] = WorldToPoint(Evaluate(u, v));
                float oldX = u * (gridSize - 1), oldY = v * (gridSize - 1);
                if (Mathf.Abs(oldX - Mathf.Round(oldX)) < .001f && Mathf.Abs(oldY - Mathf.Round(oldY)) < .001f)
                    pins[y * size + x] = pinned[Mathf.RoundToInt(oldY) * gridSize + Mathf.RoundToInt(oldX)];
            }
            points = next; pinned = pins; gridSize = size;
        }

        private static Vector3 Weights(float t) => new Vector3(2f * (t - 0.5f) * (t - 1f),
            4f * t * (1f - t), 2f * t * (t - 0.5f));
        public Vector3 Normal(float u, float v)
        {
            Vector3 du = Evaluate(u + 0.001f, v) - Evaluate(u - 0.001f, v);
            Vector3 dv = Evaluate(u, v + 0.001f) - Evaluate(u, v - 0.001f);
            Vector3 normal = Vector3.Cross(du, dv).normalized;
            return normal.sqrMagnitude > 0.5f ? normal : rotation * Vector3.forward;
        }
    }

    /// <summary>World-space triangle BVH shared by fitting and first-surface visibility.</summary>
    public sealed class TexturePaintProjectionGeometry
    {
        private struct Triangle
        {
            public Vector3 a, b, c;
            public Bounds bounds;
            public int set, index, component;
        }
        private struct Node { public Bounds bounds; public int start, count, left, right; }
        private readonly List<Triangle> triangles = new List<Triangle>();
        private readonly List<Node> nodes = new List<Node>();
        private readonly IReadOnlyList<TextureSet> sets;
        private int[] order;
        public int TriangleCount => triangles.Count;

        public TexturePaintProjectionGeometry(IReadOnlyList<TextureSet> sets)
        {
            this.sets = sets;
            var parents = new List<int>();
            var vertices = new Dictionary<Vector3Int, int>();
            for (int s = 0; s < sets.Count; s++)
            {
                Mesh mesh = sets[s]?.surface?.mesh;
                if (mesh == null) continue;
                Vector3[] positions = mesh.vertices;
                Matrix4x4 transform = LocalToWorld(sets[s]);
                for (int i = 0; i < positions.Length; i++) positions[i] = transform.MultiplyPoint3x4(positions[i]);
                int[] indices = mesh.triangles;
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    Vector3 a = positions[indices[t]], b = positions[indices[t + 1]], c = positions[indices[t + 2]];
                    int id = triangles.Count; parents.Add(id);
                    Bounds bounds = new Bounds(a, Vector3.zero); bounds.Encapsulate(b); bounds.Encapsulate(c);
                    triangles.Add(new Triangle { a = a, b = b, c = c, bounds = bounds, set = s, index = t / 3 });
                    Connect(a, id, parents, vertices); Connect(b, id, parents, vertices); Connect(c, id, parents, vertices);
                }
            }
            order = new int[triangles.Count];
            for (int i = 0; i < order.Length; i++)
            { Triangle t = triangles[i]; t.component = Root(parents, i); triangles[i] = t; order[i] = i; }
        }
        private void EnsureHierarchy()
        {
            // GPU visibility and polygon selection only need the triangle/component list.
            // Build the ray hierarchy lazily for placement, fitting, or the CPU fallback.
            if (nodes.Count == 0 && order.Length > 0) Build(0, order.Length);
        }
        public static Matrix4x4 LocalToWorld(TextureSet set) => set.surface.gameObject != null
            ? set.surface.gameObject.transform.localToWorldMatrix : Matrix4x4.identity;
        private static int Root(List<int> parents, int i)
        { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        private static void Connect(Vector3 p, int id, List<int> parents, Dictionary<Vector3Int, int> vertices)
        {
            var key = new Vector3Int(Mathf.RoundToInt(p.x * 100000f), Mathf.RoundToInt(p.y * 100000f), Mathf.RoundToInt(p.z * 100000f));
            if (vertices.TryGetValue(key, out int previous)) parents[Root(parents, id)] = Root(parents, previous);
            else vertices.Add(key, id);
        }
        private int Build(int start, int count)
        {
            Bounds bounds = triangles[order[start]].bounds;
            for (int i = start + 1; i < start + count; i++) bounds.Encapsulate(triangles[order[i]].bounds);
            int id = nodes.Count; nodes.Add(default);
            if (count <= 8) { nodes[id] = new Node { bounds = bounds, start = start, count = count }; return id; }
            Vector3 size = bounds.size; int axis = size.x > size.y ? 0 : 1; if (size.z > size[axis]) axis = 2;
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) =>
                triangles[a].bounds.center[axis].CompareTo(triangles[b].bounds.center[axis])));
            int half = count / 2;
            int left = Build(start, half), right = Build(start + half, count - half);
            nodes[id] = new Node { bounds = bounds, left = left, right = right };
            return id;
        }
        public int ResolveComponent(TexturePaintProjectionSettings settings)
        {
            if (!settings.connectedSurfaceOnly || string.IsNullOrEmpty(settings.regionSurfaceId)) return -1;
            foreach (Triangle t in triangles)
                if (sets[t.set].persistentId == settings.regionSurfaceId && t.index == settings.regionTriangle) return t.component;
            return -2; // A lost binding must never silently paint unrelated geometry.
        }
        public bool Raycast(Ray ray, float maxDistance, int component, out Vector3 hit, out Vector3 normal,
            bool frontFacesOnly = false)
        {
            EnsureHierarchy();
            float distance = maxDistance; int best = -1;
            if (nodes.Count > 0 && component != -2) Trace(0, ray, component, ref distance, ref best, frontFacesOnly);
            hit = ray.GetPoint(distance);
            normal = best >= 0 ? Vector3.Cross(triangles[best].b - triangles[best].a,
                triangles[best].c - triangles[best].a).normalized : Vector3.zero;
            return best >= 0;
        }
        public bool PickSurface(Ray ray, out TextureSet set, out int triangle, out Vector3 point, out Vector3 normal)
        {
            EnsureHierarchy();
            float distance = float.MaxValue; int best = -1;
            if (nodes.Count > 0) Trace(0, ray, -1, ref distance, ref best);
            set = null; triangle = -1; point = normal = Vector3.zero;
            if (best < 0) return false;
            Triangle hit = triangles[best];
            set = sets[hit.set]; triangle = hit.index; point = ray.GetPoint(distance);
            normal = Vector3.Cross(hit.b - hit.a, hit.c - hit.a).normalized;
            return normal.sqrMagnitude > 0.5f;
        }
        public bool InitialPlacement(Camera camera, out TextureSet set, out int triangle, out Vector3 point, out Vector3 normal)
        {
            set = null; triangle = -1; point = normal = Vector3.zero;
            EnsureHierarchy();
            if (nodes.Count == 0) return false;
            Bounds bounds = nodes[0].bounds;
            if (camera != null)
            {
                if (PickSurface(camera.ViewportPointToRay(new Vector3(.5f,.5f,0)), out set, out triangle, out point, out normal)) return true;
                Vector3 direction = camera.orthographic ? camera.transform.forward : (bounds.center - camera.transform.position).normalized;
                Ray centered = new Ray(bounds.center - direction * (bounds.extents.magnitude + 1f), direction);
                if (PickSurface(centered, out set, out triangle, out point, out normal)) return true;
            }
            // An offscreen target or a window without a camera still gets an actual surface placement.
            float closest = float.MaxValue;
            foreach (Triangle candidate in triangles)
            {
                Vector3 n = Vector3.Cross(candidate.b - candidate.a, candidate.c - candidate.a).normalized;
                if (n.sqrMagnitude < .5f) continue;
                Vector3 center = (candidate.a + candidate.b + candidate.c) / 3f;
                float score = (center - bounds.center).sqrMagnitude;
                if (score >= closest) continue;
                closest = score; set = sets[candidate.set]; triangle = candidate.index; point = center; normal = n;
            }
            return set != null;
        }
        private static bool IntersectsBounds(Bounds bounds, Ray ray, float maximum)
        {
            // Parallel rays on a split plane must visit both children. Native slab tests can
            // reject zero-thickness mesh bounds (0 * infinity), precisely where fit handles lie.
            Vector3 min = bounds.min, max = bounds.max;
            float near = 0f, far = maximum;
            for (int axis = 0; axis < 3; axis++)
            {
                float direction = ray.direction[axis], origin = ray.origin[axis];
                if (Mathf.Abs(direction) < 1e-10f)
                {
                    if (origin < min[axis] - 1e-6f || origin > max[axis] + 1e-6f) return false;
                    continue;
                }
                float a = (min[axis] - 1e-6f - origin) / direction;
                float b = (max[axis] + 1e-6f - origin) / direction;
                near = Mathf.Max(near, Mathf.Min(a, b)); far = Mathf.Min(far, Mathf.Max(a, b));
                if (near > far) return false;
            }
            return true;
        }
        private void Trace(int index, Ray ray, int component, ref float closest, ref int best,
            bool frontFacesOnly = false)
        {
            Node node = nodes[index];
            if (!IntersectsBounds(node.bounds, ray, closest)) return;
            if (node.count == 0)
            { Trace(node.left, ray, component, ref closest, ref best, frontFacesOnly); Trace(node.right, ray, component, ref closest, ref best, frontFacesOnly); return; }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                int id = order[i]; Triangle t = triangles[id];
                if (component >= 0 && t.component != component) continue;
                Vector3 e1 = t.b - t.a, e2 = t.c - t.a, p = Vector3.Cross(ray.direction, e2);
                float determinant = Vector3.Dot(e1, p);
                if (frontFacesOnly ? determinant <= 1e-12f : Mathf.Abs(determinant) < 1e-12f) continue;
                float inv = 1f / determinant;
                Vector3 origin = ray.origin - t.a;
                float u = Vector3.Dot(origin, p) * inv;
                if (u < -1e-5f || u > 1.00001f) continue;
                Vector3 q = Vector3.Cross(origin, e1);
                float v = Vector3.Dot(ray.direction, q) * inv;
                if (v < -1e-5f || u + v > 1.00001f) continue;
                float d = Vector3.Dot(e2, q) * inv;
                if (d < 0f || d > closest) continue;
                closest = d; best = id;
            }
        }
        public int Fit(TexturePaintProjectionSettings settings)
        {
            settings.Normalize(); int component = ResolveComponent(settings), hits = 0;
            var fitted = (Vector3[])settings.points.Clone();
            Vector3 forward = settings.rotation * Vector3.forward;
            for (int i = 0; i < settings.points.Length; i++)
            {
                if (settings.pinned[i]) { hits++; continue; }
                Vector3 point = settings.PointToWorld(settings.points[i]);
                if (Raycast(new Ray(point + forward * settings.FrontDepth, -forward), settings.FrontDepth - settings.BackDepth,
                        component, out Vector3 hit, out _, settings.frontFacesOnly))
                { fitted[i] = settings.WorldToPoint(hit); hits++; }
            }
            if (hits > 0) { settings.points = fitted; settings.mode = TexturePaintProjectionMode.Wrapped; }
            return hits;
        }
        public Mesh RestrictedMesh(TextureSet set, int component, out bool ownsMesh,
            TexturePaintProjectionSettings settings = null, Bounds? projectionBounds = null)
        {
            ownsMesh = false; Mesh mesh = set.surface.mesh;
            if (component == -1 && settings == null) return mesh;
            int setIndex = -1; for (int i = 0; i < sets.Count; i++) if (ReferenceEquals(sets[i], set)) { setIndex = i; break; }
            var indices = new List<int>(); int[] source = mesh.triangles;
            foreach (Triangle t in triangles)
            {
                if (t.set != setIndex || component == -2 || (component >= 0 && t.component != component)) continue;
                if (projectionBounds.HasValue && !projectionBounds.Value.Intersects(t.bounds)) continue;
                if (settings != null && settings.mode == TexturePaintProjectionMode.Planar)
                {
                    Vector3 forward = settings.rotation * Vector3.forward;
                    if (settings.frontFacesOnly && Vector3.Dot(Vector3.Cross(t.b - t.a, t.c - t.a), forward) <= 0f) continue;
                    Vector3 a = settings.WorldToPoint(t.a), b = settings.WorldToPoint(t.b), c = settings.WorldToPoint(t.c);
                    Vector3 min = Vector3.Min(a, Vector3.Min(b, c)), max = Vector3.Max(a, Vector3.Max(b, c));
                    // Keep every intersecting polygon, including those crossing the placement
                    // plane. The shader clips its pixels to the exact footprint and depth limits.
                    if (max.x < -.5f || min.x > .5f || max.y < -.5f || min.y > .5f ||
                        max.z < settings.BackDepth || min.z > settings.FrontDepth) continue;
                }
                indices.Add(source[t.index * 3]); indices.Add(source[t.index * 3 + 1]); indices.Add(source[t.index * 3 + 2]);
            }
            Mesh result = UnityEngine.Object.Instantiate(mesh); result.hideFlags = HideFlags.HideAndDontSave;
            result.subMeshCount = 1; result.SetTriangles(indices, 0); ownsMesh = true; return result;
        }
    }
}
