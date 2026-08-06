using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    [Serializable]
    public sealed class TexturePaintMask
    {
        public string id = Guid.NewGuid().ToString("N");
        public string ownerLayerId;
        public string ownerSurfaceId;
        public string name = "Mask";
        public bool enabled = true;
        public TexturePaintMaskKind kind;
        public TexturePaintMaskOperation operation = TexturePaintMaskOperation.Add;
        public Texture2D grayscaleTexture;
        public int surfaceIndex = -1;
        public List<int> triangleIndices = new List<int>();
        public List<int> uvIslandIndices = new List<int>();
        public string proceduralPluginId;
        public bool invert;
        public float threshold = 0.001f;
        [Range(0f, 1f)] public float inputMin;
        [Range(0f, 1f)] public float inputMax = 1f;
        [Min(0.01f)] public float gamma = 1f;
        [Min(0f)] public float feather;
        [Range(0, 16)] public int blurRadius;
        public int idValue;
        public int contentRevision;

        public bool Allows(int candidateSurface, int triangleIndex, int uvIsland,
            ReconstructedSurface surface = null, Vector2 uv = default, Vector3 worldPosition = default)
        {
            if (!enabled || kind == TexturePaintMaskKind.None || kind == TexturePaintMaskKind.White ||
                kind == TexturePaintMaskKind.Bitmap || kind == TexturePaintMaskKind.Painted)
                return true;
            bool value;
            switch (kind)
            {
                case TexturePaintMaskKind.Black: value = false; break;
                case TexturePaintMaskKind.Slot: value = surfaceIndex < 0 || surfaceIndex == candidateSurface; break;
                case TexturePaintMaskKind.Polygon: value = triangleIndices.Contains(triangleIndex); break;
                case TexturePaintMaskKind.UVIsland: value = uvIslandIndices.Contains(uvIsland); break;
                case TexturePaintMaskKind.ID: value = idValue < 0 || idValue == candidateSurface || idValue == triangleIndex; break;
                case TexturePaintMaskKind.Procedural:
                    value = TexturePaintProceduralMaskRegistry.TryEvaluate(proceduralPluginId,
                        new TexturePaintProceduralMaskSampleV2(
                            candidateSurface.ToString(), candidateSurface, triangleIndex, uvIsland, uv, worldPosition),
                        out float proceduralValue) && proceduralValue >= threshold;
                    break;
                default: value = true; break;
            }
            return invert ? !value : value;
        }
    }

    public static class TexturePaintProceduralMaskRegistry
    {
        private sealed class Entry
        {
            public ITexturePaintProceduralMaskV2 plugin;
            public TexturePaintPluginParameterSet parameters;
        }

        private static readonly Dictionary<string, Entry> plugins = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static void Register(ITexturePaintProceduralMaskV2 plugin, TexturePaintPluginParameterSet parameters)
        {
            string id = plugin?.Descriptor?.id;
            if (!string.IsNullOrEmpty(id)) plugins[id] = new Entry { plugin = plugin, parameters = parameters };
        }

        public static void Unregister(ITexturePaintProceduralMaskV2 plugin)
        {
            if (plugin == null) return;
            string remove = null;
            foreach (KeyValuePair<string, Entry> pair in plugins)
                if (ReferenceEquals(pair.Value.plugin, plugin)) { remove = pair.Key; break; }
            if (remove != null) plugins.Remove(remove);
        }

        public static bool TryEvaluate(string id, TexturePaintProceduralMaskSampleV2 sample, out float value)
        {
            value = 0f;
            if (!plugins.TryGetValue(id ?? string.Empty, out Entry entry) || entry.plugin == null) return false;
            try
            {
                value = entry.plugin.Evaluate(sample, entry.parameters);
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Overlay Painter procedural mask plugin '{id}' failed.\n{exception}");
                return false;
            }
        }
    }

    [Serializable]
    public sealed class TexturePaintMaskStack
    {
        [SerializeField] private List<TexturePaintMask> masks = new List<TexturePaintMask>();
        [NonSerialized] private int revision;
        public IReadOnlyList<TexturePaintMask> Masks => masks;
        public int Revision => revision;
        public bool HasActiveTextureMasks
        {
            get
            {
                for (int i = 0; i < masks.Count; i++)
                {
                    TexturePaintMask mask = masks[i];
                    if (mask == null || !mask.enabled) continue;
                    if (mask.kind == TexturePaintMaskKind.White || mask.kind == TexturePaintMaskKind.Black ||
                        mask.kind == TexturePaintMaskKind.Bitmap || mask.kind == TexturePaintMaskKind.Painted)
                        return true;
                }
                return false;
            }
        }
        public int Signature
        {
            get
            {
                unchecked
                {
                    int hash = revision;
                    for (int i = 0; i < masks.Count; i++)
                    {
                        TexturePaintMask mask = masks[i];
                        if (mask == null) continue;
                        hash = hash * 31 + (mask.id?.GetHashCode() ?? 0);
                        hash = hash * 31 + (int)mask.kind; hash = hash * 31 + (int)mask.operation;
                        hash = hash * 31 + (mask.enabled ? 1 : 0); hash = hash * 31 + (mask.invert ? 1 : 0);
                        hash = hash * 31 + mask.inputMin.GetHashCode(); hash = hash * 31 + mask.inputMax.GetHashCode();
                        hash = hash * 31 + mask.gamma.GetHashCode(); hash = hash * 31 + mask.feather.GetHashCode();
                        hash = hash * 31 + mask.blurRadius;
                        hash = hash * 31 + (mask.grayscaleTexture != null ? mask.grayscaleTexture.GetInstanceID() : 0);
                        hash = hash * 31 + mask.contentRevision;
                        for (int p = 0; p < mask.triangleIndices.Count; p++) hash = hash * 31 + mask.triangleIndices[p];
                        for (int u = 0; u < mask.uvIslandIndices.Count; u++) hash = hash * 31 + mask.uvIslandIndices[u];
                    }
                    return hash;
                }
            }
        }

        public TexturePaintMaskStack() { }
        public TexturePaintMaskStack(List<TexturePaintMask> backing) { masks = backing ?? new List<TexturePaintMask>(); }

        public void Add(TexturePaintMask mask) { if (mask != null) { masks.Add(mask); revision++; } }
        public void RemoveAt(int index) { if ((uint)index < (uint)masks.Count) { masks.RemoveAt(index); revision++; } }
        public void Clear() { masks.Clear(); revision++; }
        public void Touch() => revision++;
        public void ReplaceWith(IReadOnlyList<TexturePaintMask> replacements)
        {
            masks.Clear();
            revision++;
            if (replacements == null) return;
            for (int i = 0; i < replacements.Count; i++) if (replacements[i] != null) masks.Add(replacements[i]);
        }

        public bool Allows(int surface, int triangle, int uvIsland, ReconstructedSurface reconstructed = null,
            Vector2 uv = default, Vector3 worldPosition = default)
            => AllowsInternal(surface, triangle, uvIsland, reconstructed, uv, worldPosition, true);

        public bool AllowsStructural(int surface, int triangle, int uvIsland, ReconstructedSurface reconstructed = null,
            Vector2 uv = default, Vector3 worldPosition = default)
            => AllowsInternal(surface, triangle, uvIsland, reconstructed, uv, worldPosition, false);

        private bool AllowsInternal(int surface, int triangle, int uvIsland, ReconstructedSurface reconstructed,
            Vector2 uv, Vector3 worldPosition, bool includePainted)
        {
            bool result = true;
            bool initialized = false;
            for (int i = 0; i < masks.Count; i++)
            {
                TexturePaintMask mask = masks[i];
                bool textureKind = mask != null && (mask.kind == TexturePaintMaskKind.White ||
                    mask.kind == TexturePaintMaskKind.Black || mask.kind == TexturePaintMaskKind.Bitmap ||
                    mask.kind == TexturePaintMaskKind.Painted);
                if (mask == null || !mask.enabled ||
                    (!string.IsNullOrEmpty(mask.ownerSurfaceId) && mask.ownerSurfaceId != surface.ToString()) ||
                    (!includePainted && textureKind)) continue;
                bool value = mask.Allows(surface, triangle, uvIsland, reconstructed, uv, worldPosition);
                switch (mask.operation)
                {
                    case TexturePaintMaskOperation.Add: result = initialized ? result || value : value; break;
                    case TexturePaintMaskOperation.Subtract: result = result && !value; break;
                    case TexturePaintMaskOperation.Intersect: result = result && value; break;
                    case TexturePaintMaskOperation.Invert: result = !result; break;
                }
                initialized = true;
            }
            return result;
        }

        public Texture GetPaintedMaskTexture()
        {
            for (int i = masks.Count - 1; i >= 0; i--)
            {
                if (masks[i] != null && masks[i].enabled && masks[i].kind == TexturePaintMaskKind.Painted)
                    return masks[i].grayscaleTexture;
            }
            return Texture2D.whiteTexture;
        }

        public float EvaluateTextureMasks(int surface, string surfaceId, Vector2 uv)
        {
            float result = 1f;
            bool initialized = false;
            for (int i = 0; i < masks.Count; i++)
            {
                TexturePaintMask mask = masks[i];
                if (mask == null || !mask.enabled ||
                    (!string.IsNullOrEmpty(mask.ownerSurfaceId) && mask.ownerSurfaceId != surfaceId)) continue;
                bool textureKind = mask.kind == TexturePaintMaskKind.White || mask.kind == TexturePaintMaskKind.Black ||
                    mask.kind == TexturePaintMaskKind.Bitmap || mask.kind == TexturePaintMaskKind.Painted;
                if (!textureKind) continue;
                float value = mask.kind == TexturePaintMaskKind.Black ? 0f : 1f;
                if ((mask.kind == TexturePaintMaskKind.Bitmap || mask.kind == TexturePaintMaskKind.Painted) && mask.grayscaleTexture != null)
                {
                    try { value = SampleTexture(mask, uv); }
                    catch (UnityException) { value = 1f; }
                }
                float range = Mathf.Max(0.00001f, mask.inputMax - mask.inputMin);
                value = Mathf.Pow(Mathf.Clamp01((value - mask.inputMin) / range), 1f / Mathf.Max(0.01f, mask.gamma));
                if (mask.feather > 0f)
                    value = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f - mask.feather, 0.5f + mask.feather, value));
                if (mask.invert) value = 1f - value;
                switch (mask.operation)
                {
                    case TexturePaintMaskOperation.Add: result = initialized ? Mathf.Max(result, value) : value; break;
                    case TexturePaintMaskOperation.Subtract: result *= 1f - value; break;
                    case TexturePaintMaskOperation.Intersect: result *= value; break;
                    case TexturePaintMaskOperation.Invert: result = 1f - result; break;
                }
                initialized = true;
            }
            return Mathf.Clamp01(result);
        }

        private static float SampleTexture(TexturePaintMask mask, Vector2 uv)
        {
            Texture2D texture = mask.grayscaleTexture;
            int radius = Mathf.Clamp(mask.blurRadius, 0, 16);
            if (radius == 0) return texture.GetPixelBilinear(uv.x, uv.y).grayscale;
            float sum = 0f;
            int samples = 0;
            float du = 1f / Mathf.Max(1, texture.width);
            float dv = 1f / Mathf.Max(1, texture.height);
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius) continue;
                sum += texture.GetPixelBilinear(uv.x + x * du, uv.y + y * dv).grayscale;
                samples++;
            }
            return samples > 0 ? sum / samples : 1f;
        }
    }

    public static class UVIslandUtility
    {
        public static int[] BuildTriangleIslands(Mesh mesh)
        {
            if (mesh == null) return Array.Empty<int>();
            int[] triangles = mesh.triangles;
            Vector2[] uv = mesh.uv;
            int triCount = triangles.Length / 3;
            int[] islands = new int[triCount];
            for (int i = 0; i < islands.Length; i++) islands[i] = -1;
            Dictionary<UVEdge, List<int>> edgeOwners = new Dictionary<UVEdge, List<int>>();
            for (int tri = 0; tri < triCount; tri++)
            {
                int a = triangles[tri * 3], b = triangles[tri * 3 + 1], c = triangles[tri * 3 + 2];
                AddEdge(edgeOwners, new UVEdge(uv[a], uv[b]), tri);
                AddEdge(edgeOwners, new UVEdge(uv[b], uv[c]), tri);
                AddEdge(edgeOwners, new UVEdge(uv[c], uv[a]), tri);
            }
            List<int>[] adjacency = new List<int>[triCount];
            foreach (List<int> owners in edgeOwners.Values)
            {
                if (owners.Count < 2) continue;
                for (int i = 0; i < owners.Count; i++)
                for (int j = i + 1; j < owners.Count; j++)
                {
                    (adjacency[owners[i]] ??= new List<int>()).Add(owners[j]);
                    (adjacency[owners[j]] ??= new List<int>()).Add(owners[i]);
                }
            }
            Queue<int> queue = new Queue<int>();
            int island = 0;
            for (int start = 0; start < triCount; start++)
            {
                if (islands[start] >= 0) continue;
                islands[start] = island;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<int> neighbors = adjacency[current];
                    if (neighbors == null) continue;
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int next = neighbors[i];
                        if (islands[next] >= 0) continue;
                        islands[next] = island;
                        queue.Enqueue(next);
                    }
                }
                island++;
            }
            return islands;
        }

        private static void AddEdge(Dictionary<UVEdge, List<int>> edges, UVEdge edge, int triangle)
        {
            if (!edges.TryGetValue(edge, out List<int> owners)) edges.Add(edge, owners = new List<int>());
            owners.Add(triangle);
        }

        private readonly struct UVEdge : IEquatable<UVEdge>
        {
            private readonly Vector2Int a;
            private readonly Vector2Int b;
            public UVEdge(Vector2 one, Vector2 two)
            {
                Vector2Int q1 = Quantize(one), q2 = Quantize(two);
                if (q1.x < q2.x || (q1.x == q2.x && q1.y <= q2.y)) { a = q1; b = q2; }
                else { a = q2; b = q1; }
            }
            private static Vector2Int Quantize(Vector2 value) => new Vector2Int(Mathf.RoundToInt(value.x * 100000f), Mathf.RoundToInt(value.y * 100000f));
            public bool Equals(UVEdge other) => a == other.a && b == other.b;
            public override bool Equals(object obj) => obj is UVEdge other && Equals(other);
            public override int GetHashCode() { unchecked { return (a.GetHashCode() * 397) ^ b.GetHashCode(); } }
        }
    }
}
