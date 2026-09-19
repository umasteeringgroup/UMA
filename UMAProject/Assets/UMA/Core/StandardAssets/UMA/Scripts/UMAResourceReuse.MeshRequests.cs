using System;
using UnityEngine;

namespace UMA
{
    public static partial class UMAResourceReuse
    {
        internal static long MeshDetailedKeyCount;
        internal static long MeshAdmissionCount;
        internal static long MeshFirstStageRejectCount;

        // Geometry references only: never retain an avatar, renderer, recipe, material
        // or build operation in a lazy cache entry. Input edits must invalidate sources.
        internal sealed class MeshRequestSource
        {
            private readonly UMAMeshData mesh, basis;
            private readonly VertexAdjustmentCollection adjustments;
            internal MeshRequestSource(UMAMeshData mesh, UMAMeshData basis) { this.mesh = mesh; this.basis = basis; }
            internal MeshRequestSource(VertexAdjustmentCollection adjustments) { this.adjustments = adjustments; }
            internal UMAGeneratedResourceKey Resolve() => adjustments != null
                ? ModifierFingerprint(adjustments) : Description.MeshFingerprint(mesh, basis);
            internal bool SameReferences(MeshRequestSource other) => ReferenceEquals(mesh, other.mesh) &&
                ReferenceEquals(basis, other.basis) && ReferenceEquals(adjustments, other.adjustments);
        }

        internal sealed class MeshRequest
        {
            internal readonly UMAGeneratedResourceKey Key;
            private readonly UMAGeneratedResourceKey parameters;
            private readonly byte[] parameterBytes;
            private readonly UMAMeshInputSnapshot[] buffers;
            private readonly MeshRequestSource[] sources;
            private readonly int revision;
            internal bool DetailedKeyCreated { get; private set; }

            internal MeshRequest(byte[] parameterBytes, UMAMeshInputSnapshot[] buffers, MeshRequestSource[] sources, int revision)
            {
                this.parameterBytes = parameterBytes; this.buffers = buffers; this.sources = sources; this.revision = revision;
                parameters = new UMAGeneratedResourceKey("UMA.MeshParameters.v1", parameterBytes, buffers);
                var firstStage = new UMAGeneratedResourceKey("UMA.MeshFirstStage.v1", BitConverter.GetBytes(revision),
                    null, new[] { parameters });
                Key = new UMAGeneratedResourceKey(firstStage, ResolveDetails, () => this.revision == meshInputRevision);
            }

            private UMAGeneratedResourceKey ResolveDetails()
            {
                MeshDetailedKeyCount++;
                DetailedKeyCreated = true;
                var fingerprints = new UMAGeneratedResourceKey[sources.Length];
                for (int i = 0; i < sources.Length; i++) fingerprints[i] = sources[i].Resolve();
                return new UMAGeneratedResourceKey("UMA.MeshData.inputs.v3", parameterBytes, buffers, fingerprints);
            }

            internal bool Matches(MeshRequest other)
            {
                // This is the publication guard, NOT permission to share outputs.
                // Full equality is still required by the cache for potential matches.
                if (revision != meshInputRevision || revision != other.revision || sources.Length != other.sources.Length) return false;
                for (int i = 0; i < sources.Length; i++)
                    if (!sources[i].SameReferences(other.sources[i])) return false;
                return parameters.Equals(other.parameters);
            }
        }

        private static MeshRequest DescribeMeshRequest(UMAData data, SkinnedMeshCombiner.CombineInstance[] sources,
            UMAData.GeneratedMaterial[] materials, int atlasResolution, Quaternion boundsRotation, string backend)
        {
            PrepareMeshRequest(sources);
            using (var description = new Description(compactMeshInputs: true, deferMeshSources: true))
            {
                DescribeMeshParameters(description, data, sources, materials, atlasResolution, boundsRotation, backend);
                return description.MeshRequest(meshInputRevision);
            }
        }

        internal static UMAGeneratedResourceCache.Lease<Mesh> AcquireMesh(UMAData data,
            SkinnedMeshCombiner.CombineInstance[] sources, UMAData.GeneratedMaterial[] materials,
            int atlasResolution, Quaternion boundsRotation, out MeshRequest request, string backend = "MeshData")
        {
            // Includes signature creation AND the dictionary/full-comparison work.
            using var measurement = new LookupMeasurement(System.Diagnostics.Stopwatch.GetTimestamp());
            using var marker = MeshLookupMarker.Auto();
            request = DescribeMeshRequest(data, sources, materials, atlasResolution, boundsRotation, backend);
            var lease = UMAGeneratedResourceCache.Shared.Acquire<Mesh>(request.Key);
            MeshAdmissionCount++;
            if (lease.IsBuilder && !request.DetailedKeyCreated) MeshFirstStageRejectCount++;
            return lease;
        }

        internal static bool MeshInputsUnchanged(MeshRequest original, UMAData data,
            SkinnedMeshCombiner.CombineInstance[] sources, UMAData.GeneratedMaterial[] materials,
            int atlasResolution, Quaternion boundsRotation, string backend = "MeshData")
        {
            using var measurement = new LookupMeasurement(System.Diagnostics.Stopwatch.GetTimestamp());
            if (original == null) return false;
            return original.Matches(DescribeMeshRequest(data, sources, materials, atlasResolution, boundsRotation, backend));
        }
    }
}
