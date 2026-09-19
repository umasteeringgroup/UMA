using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;

namespace UMA
{
    /// <summary>Deterministic descriptions of the inputs consumed by the existing builders.</summary>
    public static partial class UMAResourceReuse
    {
        private static readonly ProfilerMarker MeshLookupMarker = new ProfilerMarker("UMA.Reuse.MeshInputLookup");
        internal static long MeshLookupTicks;
        internal static long MeshLookupCount;
        private readonly struct LookupMeasurement : IDisposable
        {
            private readonly long start;
            internal LookupMeasurement(long start) { this.start = start; }
            public void Dispose()
            {
                MeshLookupTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
                MeshLookupCount++;
            }
        }
        private sealed class SourceFingerprint
        {
            internal UMAGeneratedResourceKey Key;
            internal UMAGeneratedResourceKey BasisKey;
        }
        private static ConditionalWeakTable<UMAMeshData, SourceFingerprint> meshFingerprints = new ConditionalWeakTable<UMAMeshData, SourceFingerprint>();
        private static ConditionalWeakTable<VertexAdjustmentCollection, SourceFingerprint> modifierFingerprints = new ConditionalWeakTable<VertexAdjustmentCollection, SourceFingerprint>();
        private sealed class ModifierOrigin
        {
            internal WeakReference<VertexAdjustmentCollection> Source;
            internal int Revision;
        }
        private static ConditionalWeakTable<VertexAdjustmentCollection, ModifierOrigin> modifierOrigins = new ConditionalWeakTable<VertexAdjustmentCollection, ModifierOrigin>();
        private sealed class BakedMeshOrigin
        {
            internal WeakReference<UMAMeshData> Source;
            internal int Revision;
            internal string[] Names, IncludedShapes;
            internal float[] Values;
            internal bool CopyUnbaked;
            internal float SmoothingAngle;
#if UNITY_EDITOR
            internal WeakReference<SlotDataAsset> SourceAsset;
            internal int DirtyCount;
#endif
        }
        private static ConditionalWeakTable<UMAMeshData, BakedMeshOrigin> bakedMeshOrigins = new ConditionalWeakTable<UMAMeshData, BakedMeshOrigin>();
        private static int meshInputsInvalidated;
        private static int meshInputRevision;

        /// <summary>
        /// Call after modifying a source mesh's public arrays/fields in place, before
        /// rebuilding. Generated outputs already in use remain valid until rebuilt.
        /// Aliased buffers are common, so this invalidates all source fingerprint lookups.
        /// Replacing an entire UMAMeshData object is detected automatically.
        /// </summary>
        public static void InvalidateMeshSource(UMAMeshData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            InvalidateMeshInputs();
        }

        /// <summary>Invalidate cached mesh/modifier input fingerprints after source edits.</summary>
        public static void InvalidateMeshInputs()
        {
            System.Threading.Interlocked.Increment(ref meshInputRevision);
            System.Threading.Interlocked.Exchange(ref meshInputsInvalidated, 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMeshFingerprintSession()
        {
            MeshLookupTicks = MeshLookupCount = 0;
            MeshDetailedKeyCount = MeshAdmissionCount = MeshFirstStageRejectCount = 0;
            modifierOrigins = new ConditionalWeakTable<VertexAdjustmentCollection, ModifierOrigin>();
            bakedMeshOrigins = new ConditionalWeakTable<UMAMeshData, BakedMeshOrigin>();
            InvalidateMeshInputs();
        }

        private static void ResetMeshFingerprints()
        {
            meshFingerprints = new ConditionalWeakTable<UMAMeshData, SourceFingerprint>();
            modifierFingerprints = new ConditionalWeakTable<VertexAdjustmentCollection, SourceFingerprint>();
            UMAMeshInputSnapshot.StartSession();
        }

        // Scaling a modifier clones its adjustment collection, but does not change its
        // contents. Remember that provenance without doing any fingerprint work with
        // reuse OFF. Explicit source invalidation also revokes this equivalence.
        internal static void RegisterModifierClone(VertexAdjustmentCollection source, VertexAdjustmentCollection clone)
        {
            var origin = modifierOrigins.GetOrCreateValue(clone);
            origin.Source = new WeakReference<VertexAdjustmentCollection>(source);
            origin.Revision = meshInputRevision;
        }

        // Race slots can be baked into fresh deep copies for every avatar. Describe
        // that deterministic transformation, not all the copied vertex/shape buffers.
        // Capture parameters now: callers can subsequently edit/reuse their lists.
        internal static void RegisterBakedMesh(SlotDataAsset source, UMAMeshData baked, SlotDataAsset.BakeSlotParams settings)
        {
#if UNITY_EDITOR
            CheckEditorSourceRevision(source);
#endif
            var origin = bakedMeshOrigins.GetOrCreateValue(baked);
            origin.Source = new WeakReference<UMAMeshData>(source.meshData);
            origin.Revision = meshInputRevision;
            origin.Names = settings.burnOptions?.Select(option => option?.BlendShape).ToArray();
            origin.Values = settings.burnOptions?.Select(option => option?.value ?? 0f).ToArray();
            origin.IncludedShapes = settings.ShapesToInclude?.ToArray();
            origin.CopyUnbaked = settings.copyUnbakedBlendshapes;
            origin.SmoothingAngle = settings.smoothingAngleDegrees;
#if UNITY_EDITOR
            origin.SourceAsset = new WeakReference<SlotDataAsset>(source);
            origin.DirtyCount = UnityEditor.EditorUtility.GetDirtyCount(source);
#endif
        }

        private static UMAGeneratedResourceKey ModifierFingerprint(VertexAdjustmentCollection adjustments)
        {
            var fingerprint = modifierFingerprints.GetOrCreateValue(adjustments);
            if (fingerprint.Key == null)
            {
                if (modifierOrigins.TryGetValue(adjustments, out var origin) && origin.Revision == meshInputRevision &&
                    origin.Source.TryGetTarget(out var source))
                    fingerprint.Key = ModifierFingerprint(source);
                else using (var description = new Description(compactMeshInputs: true, immutableSource: true))
                {
                    description.Value(adjustments);
                    fingerprint.Key = description.Key("UMA.MeshModifier.v1");
                }
            }
            return fingerprint.Key;
        }

#if UNITY_EDITOR
        private sealed class EditorSourceRevision { internal int DirtyCount; internal bool Initialized; }
        private static readonly ConditionalWeakTable<UnityEngine.Object, EditorSourceRevision> editorSourceRevisions = new ConditionalWeakTable<UnityEngine.Object, EditorSourceRevision>();
        internal static void CheckEditorSourceRevision(UnityEngine.Object asset)
        {
            if (asset == null) return;
            var revision = editorSourceRevisions.GetOrCreateValue(asset);
            int dirtyCount = UnityEditor.EditorUtility.GetDirtyCount(asset);
            if (revision.Initialized && revision.DirtyCount != dirtyCount) InvalidateMeshInputs();
            revision.Initialized = true;
            revision.DirtyCount = dirtyCount;
        }

        [UnityEditor.InitializeOnLoadMethod]
        private static void WatchMeshSourceEdits()
        {
            UnityEditor.EditorApplication.projectChanged -= InvalidateMeshInputs;
            UnityEditor.EditorApplication.projectChanged += InvalidateMeshInputs;
            UnityEditor.Undo.undoRedoPerformed -= InvalidateMeshInputs;
            UnityEditor.Undo.undoRedoPerformed += InvalidateMeshInputs;
        }
#endif
        public static bool MeshesEnabled(UMAData data) => data != null && data.reuseGeneratedMeshes;
        public static bool TexturesEnabled(UMAData data) => data != null && data.reuseGeneratedTextures;

        internal sealed partial class Description : IDisposable
        {
            private readonly MemoryStream stream = new MemoryStream();
            internal readonly BinaryWriter Writer;
            private readonly List<UMAMeshInputSnapshot> meshInputs;
            private readonly List<UMAGeneratedResourceKey> sourceKeys = new List<UMAGeneratedResourceKey>();
            private readonly List<MeshRequestSource> deferredSources;
            private readonly bool immutableSource;
            private static readonly Dictionary<Type, FieldInfo[]> Fields = new Dictionary<Type, FieldInfo[]>();
            internal Description(bool compactMeshInputs = false, bool immutableSource = false, bool deferMeshSources = false)
            {
                Writer = new BinaryWriter(stream);
                if (compactMeshInputs) meshInputs = new List<UMAMeshInputSnapshot>();
                this.immutableSource = immutableSource;
                if (deferMeshSources) deferredSources = new List<MeshRequestSource>();
            }
            internal UMAGeneratedResourceKey Key(string kind)
            {
                Writer.Flush();
                return new UMAGeneratedResourceKey(kind, stream.ToArray(), meshInputs?.ToArray(), sourceKeys.ToArray());
            }
            internal MeshRequest MeshRequest(int revision)
            {
                Writer.Flush();
                return new MeshRequest(stream.ToArray(), meshInputs?.ToArray(), deferredSources.ToArray(), revision);
            }
            // Small changing parameters are the hot path. Keep their representation
            // identical to Value(object), without boxing or repeated type discovery.
            private Span<byte> ValueBlock(int size)
            {
                int start = checked((int)stream.Position);
                int end = checked(start + size + 1);
                stream.SetLength(end);
                stream.Position = end;
                var block = stream.GetBuffer().AsSpan(start, size + 1);
                block[0] = 1;
                return block.Slice(1);
            }
            internal void Value(bool value) { ValueBlock(1)[0] = value ? (byte)1 : (byte)0; }
            internal void Value(int value) { System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(ValueBlock(4), value); }
            internal void Value(float value) { System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(ValueBlock(4), BitConverter.SingleToInt32Bits(value)); }
            internal void Value(string value) { Writer.Write(value != null); if (value != null) Writer.Write(value); }
            internal void Value(Vector2 value) { Writer.Write(true); Writer.Write(value.x); Writer.Write(value.y); }
            internal void Value(Vector3 value) { Writer.Write(true); Writer.Write(value.x); Writer.Write(value.y); Writer.Write(value.z); }
            internal void Value(Vector4 value) { Writer.Write(true); Writer.Write(value.x); Writer.Write(value.y); Writer.Write(value.z); Writer.Write(value.w); }
            internal void Value(Quaternion value) { Writer.Write(true); Writer.Write(value.x); Writer.Write(value.y); Writer.Write(value.z); Writer.Write(value.w); }
            internal void Value(Rect value)
            {
                var block = ValueBlock(20);
                for (int i = 0; i < 4; i++) block[i * 5] = 1;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(block.Slice(1), BitConverter.SingleToInt32Bits(value.x));
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(block.Slice(6), BitConverter.SingleToInt32Bits(value.y));
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(block.Slice(11), BitConverter.SingleToInt32Bits(value.width));
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(block.Slice(16), BitConverter.SingleToInt32Bits(value.height));
            }
            internal void Value(object value)
            {
                Writer.Write(value != null);
                if (value == null) return;
                Type type = value.GetType();
                if (type == typeof(float)) { Writer.Write((float)value); return; }
                if (type == typeof(int)) { Writer.Write((int)value); return; }
                if (type == typeof(bool)) { Writer.Write((bool)value); return; }
                if (type == typeof(byte)) { Writer.Write((byte)value); return; }
                if (type == typeof(uint)) { Writer.Write((uint)value); return; }
                if (type == typeof(ulong)) { Writer.Write((ulong)value); return; }
                if (type == typeof(long)) { Writer.Write((long)value); return; }
                if (type == typeof(double)) { Writer.Write((double)value); return; }
                if (type == typeof(string)) { Writer.Write((string)value); return; }
                if (type.IsEnum) { Writer.Write(type.FullName); Writer.Write(Convert.ToInt64(value)); return; }
                if (value is Rect rect) { Value(rect.x); Value(rect.y); Value(rect.width); Value(rect.height); return; }
                if (value is Bounds bounds) { Value(bounds.center); Value(bounds.size); return; }
                if (value is Vector3[] v3) { Raw(v3); return; }
                if (value is Vector2[] v2) { Raw(v2); return; }
                if (value is Vector4[] v4) { Raw(v4); return; }
                if (value is Matrix4x4[] matrices) { Raw(matrices); return; }
                if (value is Color32[] colors) { Raw(colors); return; }
                if (value is BoneWeight1[] weights) { Raw(weights); return; }
                if (value is int[] integers) { Raw(integers); return; }
                if (value is byte[] bytes) { Raw(bytes); return; }
                if (value is float[] floats) { Raw(floats); return; }
                if (value is UMABoneWeight[] legacyWeights) { Raw(legacyWeights); return; }
                if (value is ClothSkinningCoefficient[] clothValues) { Raw(clothValues); return; }
                if (value is Vector3 vector3) { Writer.Write(vector3.x); Writer.Write(vector3.y); Writer.Write(vector3.z); return; }
                if (value is Vector2 vector2) { Writer.Write(vector2.x); Writer.Write(vector2.y); return; }
                if (value is Vector4 vector4) { Writer.Write(vector4.x); Writer.Write(vector4.y); Writer.Write(vector4.z); Writer.Write(vector4.w); return; }
                if (value is Quaternion rotation) { Writer.Write(rotation.x); Writer.Write(rotation.y); Writer.Write(rotation.z); Writer.Write(rotation.w); return; }
                if (value is UMATransform bone)
                {
                    Value(bone.name); Value(bone.hash); Value(bone.parent);
                    Value(bone.position); Value(bone.rotation); Value(bone.scale);
                    return;
                }
                if (value is UMABlendShape shape) { Value(shape.shapeName); Value(shape.frames); return; }
                if (value is UMABlendFrame frame)
                {
                    Value(frame.frameWeight); Value(frame.deltaVertices); Value(frame.deltaNormals); Value(frame.deltaTangents);
                    return;
                }
                if (value is BoneWeight1 weight) { Value(weight.boneIndex); Value(weight.weight); return; }
                if (value is ClothSkinningCoefficient cloth) { Value(cloth.maxDistance); Value(cloth.collisionSphereDistance); return; }
                if (value is BitArray bits)
                {
                    Writer.Write(bits.Length);
                    var packed = new int[(bits.Length + 31) / 32];
                    bits.CopyTo(packed, 0);
                    // Ignore unused bits in the last word: they are not part of the mask.
                    if ((bits.Length & 31) != 0) packed[packed.Length - 1] &= (int)((1u << (bits.Length & 31)) - 1);
                    Raw(packed);
                    return;
                }
                if (value is UnityEngine.Object) throw new NotSupportedException("Object dependencies must be described explicitly.");
                if (value is IDictionary dictionary)
                {
                    var keys = dictionary.Keys.Cast<object>().OrderBy(k => k.ToString(), StringComparer.Ordinal).ToArray();
                    Writer.Write(keys.Length);
                    foreach (var key in keys) { Value(key); Value(dictionary[key]); }
                    return;
                }
                if (value is HashSet<string> set) { Value(set.OrderBy(s => s, StringComparer.Ordinal).ToArray()); return; }
                if (value is IList list)
                {
                    Writer.Write(list.Count);
                    foreach (object item in list) Value(item);
                    return;
                }
                Writer.Write(type.FullName);
                if (!Fields.TryGetValue(type, out var fields))
                {
                    fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(f => !f.IsNotSerialized && (f.IsPublic || f.IsDefined(typeof(SerializeField), true) || f.IsDefined(typeof(SerializeReference), true)))
                        .OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
                    Fields.Add(type, fields);
                }
                if (fields.Length == 0) throw new NotSupportedException("No deterministic fields for " + type.FullName);
                foreach (var field in fields) Value(field.GetValue(value));
            }
            private void Raw<T>(T[] values) where T : unmanaged
            {
                Writer.Write(values.Length);
                Buffer(values, MemoryMarshal.AsBytes(values.AsSpan()));
            }
            private void Buffer(object sourceIdentity, ReadOnlySpan<byte> bytes)
            {
                // Small parameters belong inline. Large geometry belongs to a shared,
                // immutable input revision, never duplicated into every character key.
                if (meshInputs != null && bytes.Length >= 128)
                {
                    meshInputs.Add(UMAMeshInputSnapshot.Capture(sourceIdentity, bytes, immutableSource));
                    return;
                }
                // BinaryWriter's Span overload is a byte-at-a-time path on some Unity
                // runtimes. Copy directly into the stream's backing storage instead.
                int start = checked((int)stream.Position);
                int end = checked(start + bytes.Length);
                stream.SetLength(end);
                bytes.CopyTo(stream.GetBuffer().AsSpan(start, bytes.Length));
                stream.Position = end;
            }
            private unsafe void Triangles(SubMeshTriangles submesh)
            {
                // Describe the backing buffer + ranges without allocating an LOD slice.
                int[] managed = submesh.getManagedTriangles(-1);
                if (managed != null) Value(managed);
                else if (submesh.nativeTriangles.IsCreated)
                {
                    Writer.Write(true);
                    Writer.Write(submesh.nativeTriangles.Length);
                    Buffer(submesh, new ReadOnlySpan<byte>(submesh.nativeTriangles.GetUnsafeReadOnlyPtr(),
                        checked(submesh.nativeTriangles.Length * sizeof(int))));
                }
                else Value(null);
            }
            internal void Mesh(UMAMeshData mesh, int lod)
            {
                Value(mesh != null);
                if (mesh == null) return;
                Value(mesh.vertexCount); Value(mesh.subMeshCount);
                Value(mesh.vertices); Value(mesh.normals); Value(mesh.tangents); Value(mesh.colors32);
                Value(mesh.uv); Value(mesh.uv2); Value(mesh.uv3); Value(mesh.uv4);
                Value(mesh.bindPoses); Value(mesh.boneWeights);
                Value(mesh.ManagedBoneWeights); Value(mesh.ManagedBonesPerVertex);
                Value(mesh.boneNameHashes); Value(mesh.umaBones); Value(mesh.umaBoneCount); Value(mesh.rootBoneHash);
                Value(mesh.blendShapes); Value(mesh.clothSkinning); Value(mesh.clothSkinningSerialized);
                Value(mesh.submeshes?.Length ?? -1);
                if (mesh.submeshes == null) return;
                foreach (var submesh in mesh.submeshes)
                {
                    Value(submesh != null);
                    if (submesh == null) continue;
                    Triangles(submesh);
                    Value(submesh.lodRanges);
                }
            }
            private void Changed(object basis, object current)
            {
                bool changed = !ReferenceEquals(basis, current);
                Value(changed);
                if (changed) Value(current);
            }

            private void MeshDifferences(UMAMeshData mesh, UMAMeshData basis)
            {
                Value(mesh.vertexCount); Value(mesh.subMeshCount);
                Changed(basis.vertices, mesh.vertices); Changed(basis.normals, mesh.normals);
                Changed(basis.tangents, mesh.tangents); Changed(basis.colors32, mesh.colors32);
                Changed(basis.uv, mesh.uv); Changed(basis.uv2, mesh.uv2);
                Changed(basis.uv3, mesh.uv3); Changed(basis.uv4, mesh.uv4);
                Changed(basis.bindPoses, mesh.bindPoses); Changed(basis.boneWeights, mesh.boneWeights);
                Changed(basis.ManagedBoneWeights, mesh.ManagedBoneWeights); Changed(basis.ManagedBonesPerVertex, mesh.ManagedBonesPerVertex);
                Changed(basis.boneNameHashes, mesh.boneNameHashes); Changed(basis.umaBones, mesh.umaBones);
                Value(mesh.umaBoneCount); Value(mesh.rootBoneHash);
                Changed(basis.blendShapes, mesh.blendShapes);
                Changed(basis.clothSkinning, mesh.clothSkinning); Changed(basis.clothSkinningSerialized, mesh.clothSkinningSerialized);
                bool changedTriangles = !ReferenceEquals(basis.submeshes, mesh.submeshes);
                Value(changedTriangles);
                if (changedTriangles)
                {
                    Value(mesh.submeshes?.Length ?? -1);
                    if (mesh.submeshes != null)
                        foreach (var submesh in mesh.submeshes)
                        {
                            Value(submesh != null);
                            if (submesh != null) { Triangles(submesh); Value(submesh.lodRanges); }
                        }
                }
            }

            internal static UMAGeneratedResourceKey MeshFingerprint(UMAMeshData mesh, UMAMeshData basis)
            {
                if (ReferenceEquals(mesh, basis)) basis = null;
                BakedMeshOrigin bakedOrigin = null;
                if (basis == null && bakedMeshOrigins.TryGetValue(mesh, out var origin) &&
                    origin.Revision == meshInputRevision && origin.Source.TryGetTarget(out var original)
#if UNITY_EDITOR
                    && origin.SourceAsset.TryGetTarget(out var originalAsset) && originalAsset != null &&
                    ReferenceEquals(originalAsset.meshData, original) && UnityEditor.EditorUtility.GetDirtyCount(originalAsset) == origin.DirtyCount
#endif
                    )
                {
                    bakedOrigin = origin;
                    basis = original;
                }
                var basisKey = basis == null ? null : MeshFingerprint(basis, null);
                var fingerprint = meshFingerprints.GetOrCreateValue(mesh);
                if (fingerprint.Key == null || !ReferenceEquals(fingerprint.BasisKey, basisKey))
                {
                    using (var source = new Description(compactMeshInputs: true, immutableSource: true))
                    {
                        if (basis == null) source.Mesh(mesh, -1);
                        else
                        {
                            source.sourceKeys.Add(basisKey);
                            if (bakedOrigin != null)
                            {
                                source.Value(bakedOrigin.Names); source.Value(bakedOrigin.Values);
                                source.Value(bakedOrigin.IncludedShapes); source.Value(bakedOrigin.CopyUnbaked);
                                source.Value(bakedOrigin.SmoothingAngle);
                            }
                            else source.MeshDifferences(mesh, basis);
                        }
                        fingerprint.Key = source.Key(bakedOrigin != null ? "UMA.MeshSource.Baked.v1" :
                            basis == null ? "UMA.MeshSource.v1" : "UMA.MeshSource.Derived.v1");
                        fingerprint.BasisKey = basisKey;
                    }
                }
                return fingerprint.Key;
            }

            internal void MeshReference(UMAMeshData mesh, UMAMeshData basis = null)
            {
                Value(mesh != null);
                if (mesh == null) return;
                // SlotName is set by the builder, so it is a per-request parameter,
                // not part of the immutable source-geometry fingerprint.
                Value(mesh.SlotName);
                if (deferredSources != null) deferredSources.Add(new MeshRequestSource(mesh, basis));
                else sourceKeys.Add(MeshFingerprint(mesh, basis));
            }
            internal void Modifiers(List<MeshModifier.Modifier> modifiers)
            {
                Value(modifiers?.Count ?? -1);
                if (modifiers == null) return;
                foreach (var modifier in modifiers)
                {
                    Value(modifier != null);
                    if (modifier == null) continue;
                    Value(modifier.SlotName); Value(modifier.DNAName); Value(modifier.Scale);
                    Value(modifier.adjustments != null);
                    if (modifier.adjustments != null)
                    {
                        if (deferredSources != null) deferredSources.Add(new MeshRequestSource(modifier.adjustments));
                        else sourceKeys.Add(ModifierFingerprint(modifier.adjustments));
                    }
                }
            }
            public void Dispose() { Writer.Dispose(); stream.Dispose(); }
        }

        public static UMAGeneratedResourceKey DescribeMesh(UMAData data,
            SkinnedMeshCombiner.CombineInstance[] sources, UMAData.GeneratedMaterial[] materials,
            int atlasResolution, Quaternion boundsRotation, string backend = "MeshData")
        {
            using var measurement = new LookupMeasurement(System.Diagnostics.Stopwatch.GetTimestamp());
            PrepareMeshRequest(sources);
            using (MeshLookupMarker.Auto())
            using (var d = new Description(compactMeshInputs: true))
            {
                DescribeMeshParameters(d, data, sources, materials, atlasResolution, boundsRotation, backend);
                return d.Key("UMA.MeshData.inputs.v3");
            }
        }

        private static void PrepareMeshRequest(SkinnedMeshCombiner.CombineInstance[] sources)
        {
#if UNITY_EDITOR
            foreach (var source in sources) CheckEditorSourceRevision(source.slotData?.asset);
#endif
            if (System.Threading.Interlocked.Exchange(ref meshInputsInvalidated, 0) != 0) ResetMeshFingerprints();
        }

        private static void DescribeMeshParameters(Description d, UMAData data,
            SkinnedMeshCombiner.CombineInstance[] sources, UMAData.GeneratedMaterial[] materials,
            int atlasResolution, Quaternion boundsRotation, string backend)
        {
            d.Value(backend);
            d.Value(data.currentLODLevel); d.Value(data.force32bit); d.Value(data.markDynamic); d.Value(data.markNotReadable);
            d.Value(atlasResolution); d.Value(boundsRotation); d.Value(SkinnedMeshCombinerMeshAPI.BoundsInflationFraction);
            var settings = data.blendShapeSettings;
            // Explicit inclusion is identified by name, not count. Live expression
            // weights stay renderer-local; only baked values change mesh identity.
            d.Value(settings?.blendShapes?.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
            d.Value(settings?.ignoreBlendShapes ?? false); d.Value(settings?.loadAllFrames ?? true);
            d.Value(settings?.loadNormals ?? true); d.Value(settings?.loadTangents ?? true);
            d.Value(settings?.forceBakedBlendShapeValue ?? false); d.Value(settings?.forcedBakedBlendShapeValue ?? .5f);
            d.Value(settings?.filteredBlendshapes);
            var baked = new SortedDictionary<string, float>(StringComparer.Ordinal);
            if (settings?.blendShapes != null)
                foreach (var pair in settings.blendShapes)
                    if (pair.Value != null && pair.Value.isBaked) baked.Add(pair.Key, pair.Value.value);
            d.Value(baked);
            d.Value(sources.Length);
            foreach (var source in sources)
            {
                d.MeshReference(source.meshData, source.slotData?.asset?.meshData);
                d.Value(source.targetSubmeshIndices);
                d.Value(source.triangleMask);
                d.Value(source.applyMeshModifiersInJobs);
                if (source.applyMeshModifiersInJobs) d.Modifiers(source.slotData?.meshModifiers);
                var slot = source.slotData;
                d.Value(slot?.slotName); d.Value(slot?.UVSet ?? 0); d.Value(slot?.UVRemapped ?? false);
                d.Value(slot?.useAtlasOverlay ?? false);
            }
            var shapes = data.umaRecipe?.BlendshapeSlots;
            d.Value(shapes?.Count ?? 0);
            if (shapes != null)
                foreach (var pair in shapes.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    d.Value(pair.Key); d.Value(pair.Value.Count);
                    foreach (var mesh in pair.Value) d.MeshReference(mesh);
                }
            d.Value(materials.Length);
            foreach (var material in materials)
            {
                d.Value(material.umaMaterial.IsGeneratedTextures);
                d.Value(material.umaMaterial.secondPass != null);
                d.Value(material.cropResolution); d.Value(material.resolutionScale);
                d.Value(material.materialFragments.Count);
                foreach (var fragment in material.materialFragments)
                {
                    d.Value(fragment.atlasRegion); d.Value(fragment.isRectShared);
                    d.Value(fragment.slotData?.useAtlasOverlay ?? false);
                    // Only shared-rect UV remapping reads overlay names/rects.
                    // Ordinary texture/color overlays do not change mesh identity.
                    if (fragment.isRectShared && (fragment.slotData?.useAtlasOverlay ?? false))
                    {
                        d.Value(fragment.overlayList?.Count ?? 0);
                        if (fragment.overlayList != null)
                            foreach (var overlay in fragment.overlayList)
                            { d.Value(overlay?.overlayName); d.Value(overlay?.rect ?? Rect.zero); }
                    }
                }
            }
        }

        internal sealed class MeshBinding
        {
            internal int[] BoneHashes;
            internal Bounds LocalBounds;
            internal ClothSkinningCoefficient[] Cloth;
            internal int[] VertexOffsets;
        }

        internal static MeshBinding CaptureMeshBinding(SkinnedMeshRenderer renderer,
            SkinnedMeshCombiner.CombineInstance[] sources, ClothSkinningCoefficient[] cloth)
        {
            var bones = renderer.bones;
            var metadata = new MeshBinding { BoneHashes = new int[bones.Length], LocalBounds = renderer.localBounds,
                Cloth = cloth == null ? null : (ClothSkinningCoefficient[])cloth.Clone(), VertexOffsets = new int[sources.Length] };
            for (int i = 0; i < bones.Length; i++) metadata.BoneHashes[i] = bones[i] == null ? 0 : UMAUtils.StringToHash(bones[i].name);
            int offset = 0;
            for (int i = 0; i < sources.Length; i++) { metadata.VertexOffsets[i] = offset; offset += sources[i].meshData.vertexCount; }
            return metadata;
        }

        internal static void BindMesh(UMAData data, SkinnedMeshRenderer renderer, UMAGeneratedResourceCache.Lease<Mesh> lease)
        {
            var binding = (MeshBinding)lease.Metadata;
            var old = renderer.sharedMesh;
            bool released = UMAResourceLeaseOwner.ReleaseMesh(renderer);
            renderer.sharedMesh = lease.Resource;
            renderer.bones = data.skeleton.HashesToTransforms(binding.BoneHashes);
            renderer.rootBone = data.GetGlobalTransform();
            renderer.localBounds = binding.LocalBounds;
            UMAResourceLeaseOwner.Get(renderer).SetMesh(lease);
            if (!released && old != null && old != renderer.sharedMesh) UMAUtils.DestroySceneObject(old);
        }
    }
}
