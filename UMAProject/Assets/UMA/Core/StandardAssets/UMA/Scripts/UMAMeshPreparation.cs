using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UMA
{
    public partial class UMAMeshData
    {
        [SerializeField, HideInInspector] private UMAMeshPreparation preparedData;
        public UMAMeshPreparation PreparedData => preparedData;
        internal void SetPreparedBuildData(UMAMeshPreparation value) => preparedData = value;

        /// <summary>Editor/build-time operation. Does not change geometry or shape names.</summary>
        public void PrepareBuildData(bool stripZeroShapeChannels = true)
        {
            // Validate before making any changes, so failed upgrades leave source data intact.
            UMAMeshPreparation.Validate(this);
            if (stripZeroShapeChannels && blendShapes != null)
                foreach (var shape in blendShapes)
                    foreach (var frame in shape.frames)
                    {
                        if (UMABlendFrame.isAllZero(frame.deltaNormals)) frame.deltaNormals = Array.Empty<Vector3>();
                        if (UMABlendFrame.isAllZero(frame.deltaTangents)) frame.deltaTangents = Array.Empty<Vector3>();
                    }
            preparedData = UMAMeshPreparation.Create(this);
            UMAResourceReuse.InvalidateMeshSource(this);
        }

        /// <summary>Call after modifying public source arrays, including aliased buffers.</summary>
        public void InvalidateBuildData() => UMAResourceReuse.InvalidateMeshSource(this);
    }

    /// <summary>
    /// Optional derived data, never the authority for mesh equality. Serialized data is
    /// verified once per source revision before use. Missing/stale metadata is rebuilt
    /// in memory automatically; invalid source data keeps the existing diagnostic path.
    /// Contains no native allocations, objects, or avatar state.
    /// </summary>
    [Serializable]
    public sealed class UMAMeshPreparation
    {
        public const int CurrentVersion = 2;
        [SerializeField] private int version;
        [SerializeField] private Hash128 geometry, topology, skinning, shapes, source, metadata;
        [SerializeField] private int vertexCount, weightCount;
        [SerializeField] private Bounds sourceBounds;
        [SerializeField] private Rect[] sourceUVBounds;
        [SerializeField] private Shape[] blendShapes;

        [Serializable]
        public sealed class Shape
        {
            public string Name;
            public Hash128 Fingerprint;
            public Frame[] Frames;
        }

        [Serializable]
        public sealed class Frame
        {
            public float Weight;
            public bool HasNormals, HasTangents, Sparse;
            // Union of nonzero position, normal and tangent deltas when Sparse is true.
            // Store sparse indices only when they cost less than half a dense int map.
            public int[] AffectedVertices;
        }

        public int Version => version;
        public Hash128 SourceFingerprint => source;
        public Hash128 GeometryFingerprint => geometry;
        public Hash128 TopologyFingerprint => topology;
        public Hash128 SkinningFingerprint => skinning;
        public Hash128 BlendShapeFingerprint => shapes;
        public int VertexCount => vertexCount;
        public int WeightCount => weightCount;
        public Bounds SourceBounds => sourceBounds;
        public bool TryGetSourceUVBounds(int submesh, out Rect bounds)
        {
            bounds = default;
            if (sourceUVBounds == null || submesh < 0 || submesh >= sourceUVBounds.Length) return false;
            bounds = sourceUVBounds[submesh];
            return bounds.width > 0 && bounds.height > 0;
        }
        public IReadOnlyList<Shape> BlendShapes => blendShapes;

        private sealed class Verification
        {
            internal int Epoch;
            internal UMAMeshPreparation Data, SerializedData;
            internal bool Valid, Initialized;
        }
        private sealed class VerifiedShape
        {
            internal int Epoch, VertexCount;
            internal Frame[] Frames;
        }
        private static readonly ConditionalWeakTable<UMAMeshData, Verification> verified = new();
        private static readonly ConditionalWeakTable<UMABlendShape, VerifiedShape> verifiedShapes = new();
        private sealed class BufferHash { internal int Epoch; internal Hash128 Hash; }
        private static readonly ConditionalWeakTable<object, BufferHash> bufferHashes = new();
        private sealed class TrackedSource { }
        private static readonly ConditionalWeakTable<object, TrackedSource> trackedSources = new();
        private static int epoch;
#if UNITY_EDITOR
        private sealed class Owner { internal WeakReference<SlotDataAsset> Asset; }
        private static readonly ConditionalWeakTable<UMAMeshData, Owner> owners = new();
        public static void RegisterOwner(SlotDataAsset asset)
        {
            if (asset?.meshData == null) return;
            owners.GetOrCreateValue(asset.meshData).Asset = new WeakReference<SlotDataAsset>(asset);
        }
#endif
        internal static void InvalidateAll() => System.Threading.Interlocked.Increment(ref epoch);
        internal static void TrackSource(object source)
        { if (source != null) trackedSources.GetOrCreateValue(source); }

        // New output buffers do not invalidate every other slot while a crowd builds.
        // Shared source buffers are tracked weakly so editing an alias also invalidates.
        internal static void InvalidateIfTracked(object source)
        {
            if (source != null && (trackedSources.TryGetValue(source, out _) ||
                bufferHashes.TryGetValue(source, out _) || source is UMAMeshData m && m.PreparedData != null))
                UMAResourceReuse.InvalidateMeshInputs();
        }
        internal static bool TryGetBufferHash(object buffer, out Hash128 hash)
        {
            hash = default;
            if (!bufferHashes.TryGetValue(buffer, out var entry) || entry.Epoch != epoch) return false;
            hash = entry.Hash;
            return true;
        }

        public static bool TryGet(UMAMeshData mesh, out UMAMeshPreparation result)
        {
            result = null;
            if (mesh == null || mesh.vertices == null || mesh.vertexCount <= 0) return false;
            TrackSource(mesh);
#if UNITY_EDITOR
            // Covers direct authoring tools using SetDirty, including SaveAssetIfDirty.
            if (owners.TryGetValue(mesh, out var owner) && owner.Asset.TryGetTarget(out var asset) && asset != null)
                UMAResourceReuse.CheckEditorSourceRevision(asset);
#endif
            var state = verified.GetOrCreateValue(mesh);
            int revision = epoch;
            if (!state.Initialized || state.Epoch != revision || !ReferenceEquals(state.SerializedData, mesh.PreparedData))
            {
                state.Initialized = true;
                state.Valid = false;
                state.SerializedData = mesh.PreparedData;
                state.Data = null;
                state.Epoch = revision;
                try
                {
                    var data = mesh.PreparedData;
                    // Check live content, not an asset GUID or a persisted 'valid' flag.
                    state.Valid = data != null && data.Matches(mesh);
                    if (!state.Valid)
                    {
                        // Automatic, non-destructive upgrade. Do not force a disk write
                        // or strip shared source arrays during builds. Keep the result on
                        // the slot so any later save persists the converted format.
                        Validate(mesh);
#if UNITY_EDITOR
                        if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
                            owners.TryGetValue(mesh, out var convertedOwner) &&
                            convertedOwner.Asset.TryGetTarget(out var convertedAsset) && convertedAsset != null &&
                            UnityEditor.EditorUtility.IsPersistent(convertedAsset))
                        {
                            // Saving a converted, otherwise unedited slot must work too.
                            // Marking dirty is not a save; there are no startup disk writes.
                            string path = UnityEditor.AssetDatabase.GetAssetPath(convertedAsset);
                            if (path.StartsWith("Assets/", StringComparison.Ordinal) && UnityEditor.AssetDatabase.IsOpenForEdit(path))
                            {
                                UnityEditor.EditorUtility.SetDirty(convertedAsset);
                                UMAResourceReuse.CheckEditorSourceRevision(convertedAsset);
                                state.Epoch = revision = epoch;
                            }
                        }
#endif
                        data = Create(mesh);
                        mesh.SetPreparedBuildData(data);
                        state.SerializedData = data;
                        state.Valid = true;
                    }
                    state.Data = data;
                    if (mesh.blendShapes != null)
                        for (int i = 0; i < mesh.blendShapes.Length; i++)
                        {
                            var shape = verifiedShapes.GetOrCreateValue(mesh.blendShapes[i]);
                            shape.Epoch = revision;
                            shape.VertexCount = mesh.vertexCount;
                            shape.Frames = data.blendShapes[i].Frames;
                        }
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException ||
                    e is NullReferenceException || e is IndexOutOfRangeException)
                {
                    // Invalid source data retains the old diagnostic/fallback path.
                    state.Valid = false;
                }
            }
            if (state.Valid) result = state.Data;
            return state.Valid;
        }

        private bool Matches(UMAMeshData mesh)
        {
            if (version != CurrentVersion) return false;
            try
            {
                Fingerprints(mesh, out var g, out var t, out var s, out var b, out var full);
                return full == source && metadata == MetadataHash() &&
                    g == geometry && t == topology && s == skinning && b == shapes;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException ||
                e is NullReferenceException || e is IndexOutOfRangeException)
            { return false; } // Damaged optional metadata must not prevent automatic conversion.
        }

        internal static bool TryGetShape(UMABlendShape shape, int count, out Frame[] frames)
        {
            frames = null;
            if (shape == null || !verifiedShapes.TryGetValue(shape, out var cached) ||
                cached.Epoch != epoch || cached.VertexCount != count) return false;
            frames = cached.Frames;
            return true;
        }

        internal static UMAMeshPreparation Create(UMAMeshData mesh)
        {
            var data = new UMAMeshPreparation { version = CurrentVersion, vertexCount = mesh.vertexCount,
                weightCount = mesh.ManagedBoneWeights.Length };
            Fingerprints(mesh, out data.geometry, out data.topology, out data.skinning, out data.shapes, out data.source);
            data.sourceBounds = new Bounds(mesh.vertices[0], Vector3.zero);
            foreach (var vertex in mesh.vertices) data.sourceBounds.Encapsulate(vertex);
            data.sourceUVBounds = new Rect[mesh.submeshes.Length];
            if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount)
                for (int s = 0; s < mesh.submeshes.Length; s++)
                {
                    var sub = mesh.submeshes[s];
                    var indices = sub.getManagedTriangles(-1);
                    int count = indices?.Length ?? (sub.nativeTriangles.IsCreated ? sub.nativeTriangles.Length : 0);
                    var min = Vector2.one;
                    var max = Vector2.zero;
                    bool valid = count > 0;
                    for (int i = 0; i < count; i++)
                    {
                        var uv = mesh.uv[indices != null ? indices[i] : sub.nativeTriangles[i]];
                        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) { valid = false; break; }
                        min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                    }
                    if (valid) data.sourceUVBounds[s] = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                }
            data.blendShapes = new Shape[mesh.blendShapes?.Length ?? 0];
            for (int i = 0; i < data.blendShapes.Length; i++)
            {
                var shape = mesh.blendShapes[i];
                var prepared = new Shape { Name = shape.shapeName, Fingerprint = ShapeHash(shape), Frames = new Frame[shape.frames.Length] };
                for (int f = 0; f < shape.frames.Length; f++)
                {
                    var frame = shape.frames[f];
                    var indices = new List<int>();
                    bool hasNormals = false, hasTangents = false;
                    for (int v = 0; v < mesh.vertexCount; v++)
                    {
                        bool normal = Nonzero(frame.deltaNormals, v), tangent = Nonzero(frame.deltaTangents, v);
                        hasNormals |= normal; hasTangents |= tangent;
                        if (indices != null && (Nonzero(frame.deltaVertices, v) || normal || tangent))
                        {
                            indices.Add(v);
                            // Dense frames do not need a temporary full vertex index map.
                            if (indices.Count >= mesh.vertexCount / 2) indices = null;
                        }
                    }
                    prepared.Frames[f] = new Frame { Weight = frame.frameWeight,
                        HasNormals = hasNormals, HasTangents = hasTangents,
                        Sparse = indices != null,
                        AffectedVertices = indices != null ? indices.ToArray() : Array.Empty<int>() };
                }
                data.blendShapes[i] = prepared;
            }
            data.metadata = data.MetadataHash();
            return data;
        }

        private static bool Nonzero(Vector3[] values, int index) => values != null && index < values.Length &&
            (values[index].x != 0 || values[index].y != 0 || values[index].z != 0);

        // Field boundaries and lengths are included; every source stream consumed by the
        // combiners/reuse cache is represented, including all LOD indices and shape frames.
        private struct Fingerprint
        {
            internal Hash128 Hash;
            internal void Add<T>(T value) where T : unmanaged => Hash.Append(ref value);
            internal void Text(string value)
            {
                // Inline Unity serialization also normalizes null strings to empty.
                value ??= string.Empty;
                Add(value.Length); if (value.Length != 0) Hash.Append(value);
            }
            internal void Array<T>(T[] values) where T : unmanaged
            {
                // Unity normalizes null serialized arrays to empty on load.
                Add(values?.Length ?? 0);
                if (values == null || values.Length == 0) return;
                var rawHash = Hash128.Compute(values);
                Add(rawHash);
                var cached = bufferHashes.GetOrCreateValue(values);
                cached.Epoch = epoch; cached.Hash = rawHash;
            }
        }

        private static Hash128 ShapeHash(UMABlendShape shape)
        {
            var h = new Fingerprint(); h.Text(shape.shapeName); h.Add(shape.frames.Length);
            foreach (var f in shape.frames)
            { h.Add(f.frameWeight); h.Array(f.deltaVertices); h.Array(f.deltaNormals); h.Array(f.deltaTangents); }
            return h.Hash;
        }

        private static void Fingerprints(UMAMeshData m, out Hash128 geometry, out Hash128 topology,
            out Hash128 skinning, out Hash128 shapes, out Hash128 source)
        {
            var g = new Fingerprint(); g.Add(m.vertexCount);
            g.Array(m.vertices); g.Array(m.normals); g.Array(m.tangents); g.Array(m.colors32);
            g.Array(m.uv); g.Array(m.uv2); g.Array(m.uv3); g.Array(m.uv4);
            geometry = g.Hash;
            var t = new Fingerprint(); t.Add(m.subMeshCount); t.Add(m.submeshes?.Length ?? 0);
            if (m.submeshes != null) foreach (var sub in m.submeshes)
            {
                t.Add(sub != null); if (sub == null) continue;
                TrackSource(sub);
                var triangles = sub.getManagedTriangles(-1);
                if (triangles != null) t.Array(triangles);
                else if (sub.nativeTriangles.IsCreated) { t.Add(sub.nativeTriangles.Length); t.Add(Hash128.Compute(sub.nativeTriangles)); }
                else t.Add(0);
                t.Add(sub.lodRanges?.Count ?? 0);
                if (sub.lodRanges != null) foreach (var r in sub.lodRanges) { t.Add(r.offset); t.Add(r.count); }
            }
            topology = t.Hash;
            var s = new Fingerprint(); s.Array(m.bindPoses); s.Array(m.boneNameHashes);
            s.Array(m.boneWeights); s.Array(m.ManagedBoneWeights); s.Array(m.ManagedBonesPerVertex);
            s.Array(m.clothSkinning); s.Array(m.clothSkinningSerialized);
            s.Text(m.RootBoneName); s.Add(m.rootBoneHash); s.Add(m.umaBoneCount); s.Add(m.umaBones?.Length ?? 0);
            if (m.umaBones != null) foreach (var bone in m.umaBones)
            {
                s.Add(bone != null); if (bone == null) continue;
                TrackSource(bone);
                s.Text(bone.name); s.Add(bone.hash); s.Add(bone.parent);
                s.Add(bone.position); s.Add(bone.rotation); s.Add(bone.scale);
            }
            skinning = s.Hash;
            var b = new Fingerprint(); b.Add(m.blendShapes?.Length ?? 0);
            if (m.blendShapes != null) foreach (var shape in m.blendShapes) b.Add(ShapeHash(shape));
            shapes = b.Hash;
            var all = new Fingerprint(); all.Add(CurrentVersion); all.Add(geometry); all.Add(topology); all.Add(skinning); all.Add(shapes);
            source = all.Hash;
        }

        private Hash128 MetadataHash()
        {
            var h = new Fingerprint(); h.Add(version); h.Add(vertexCount); h.Add(weightCount);
            h.Add(sourceBounds.center); h.Add(sourceBounds.size); h.Add(blendShapes.Length);
            h.Array(sourceUVBounds);
            foreach (var shape in blendShapes)
            {
                h.Text(shape.Name); h.Add(shape.Fingerprint); h.Add(shape.Frames.Length);
                foreach (var f in shape.Frames)
                { h.Add(f.Weight); h.Add(f.HasNormals); h.Add(f.HasTangents); h.Add(f.Sparse); h.Array(f.AffectedVertices); }
            }
            return h.Hash;
        }

        internal static void Validate(UMAMeshData m)
        {
            SkinnedMeshCombinerMeshAPI.ValidateSourceForPreparation(m);
            CheckVectors(m.vertices); CheckVectors(m.normals);
            if (m.tangents != null) foreach (var v in m.tangents) { Finite(v.x); Finite(v.y); Finite(v.z); Finite(v.w); }
            CheckUV(m.uv); CheckUV(m.uv2); CheckUV(m.uv3); CheckUV(m.uv4);
            foreach (var sub in m.submeshes)
            {
                if (sub == null) throw new InvalidOperationException("Mesh contains a null submesh.");
                var indices = sub.getManagedTriangles(-1);
                int length = indices?.Length ?? (sub.nativeTriangles.IsCreated ? sub.nativeTriangles.Length : 0);
                if (length % 3 != 0) throw new InvalidOperationException("Triangle buffer is not divisible by three.");
                for (int i = 0; i < length; i++)
                    if ((uint)(indices != null ? indices[i] : sub.nativeTriangles[i]) >= (uint)m.vertexCount)
                        throw new InvalidOperationException("Triangle references an invalid vertex.");
                if (sub.lodRanges != null) foreach (var r in sub.lodRanges)
                    if (r == null || (ulong)r.offset + r.count > (ulong)length || r.count % 3 != 0 || r.offset % 3 != 0)
                        throw new InvalidOperationException("Invalid LOD index range.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (m.blendShapes != null) foreach (var shape in m.blendShapes)
            {
                SkinnedMeshCombinerMeshAPI.ValidateShapeForPreparation(shape, m.vertexCount);
                if (!names.Add(shape.shapeName)) throw new InvalidOperationException("Duplicate blendshape: " + shape.shapeName);
                foreach (var frame in shape.frames)
                { CheckVectors(frame.deltaVertices); CheckVectors(frame.deltaNormals); CheckVectors(frame.deltaTangents); }
            }
        }
        private static void CheckVectors(Vector3[] values)
        { if (values != null) foreach (var v in values) { Finite(v.x); Finite(v.y); Finite(v.z); } }
        private static void CheckUV(Vector2[] values)
        { if (values != null) foreach (var v in values) { Finite(v.x); Finite(v.y); } }
        private static void Finite(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Mesh contains non-finite values."); }
    }
}
