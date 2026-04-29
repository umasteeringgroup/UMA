using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public partial class SlotDataAsset
    {
        private const float Uma3ConversionMatrixTolerance = 0.001f;
        private const float Uma3WeightEpsilon = 0.000001f;

        private struct Uma3MeshTransformCandidate
        {
            public int boneIndex;
            public float weight;
            public Matrix4x4 meshFromRoot;
        }

        private struct Uma3WeightRemapStats
        {
            public int mappedWeights;
            public int unmappedWeights;
            public int fallbackVertices;
        }

        /// <summary>
        /// Converts this slot's mesh data into the donor slot's UMA3 bindpose space and donor bone ordering.
        /// The resulting slot keeps its own geometry, but is rebound to donor-authoritative bindposes and weight indices.
        /// </summary>
        public string ConvertSlotDataToUMA3(SlotDataAsset donorSlot)
        {
            return ConvertSlotDataToUMA3(donorSlot, false, 0f, 0f, 90f);
        }

        public string ConvertSlotDataToUMA3(SlotDataAsset donorSlot, bool overrideAxisConversion, float axisX, float axisY, float axisZ)
        {
            if (donorSlot == null)
            {
                return "Donor slot is null.";
            }

            if (meshData == null)
            {
                return $"Slot '{slotName}' has no meshData.";
            }

            if (donorSlot.meshData == null)
            {
                return $"Donor slot '{donorSlot.slotName}' has no meshData.";
            }

            if (!EnsureManagedWeightsForUma3(meshData, slotName, out string sourceWeightMessage))
            {
                return sourceWeightMessage;
            }

            if (!EnsureManagedWeightsForUma3(donorSlot.meshData, donorSlot.slotName, out string donorWeightMessage))
            {
                return donorWeightMessage;
            }

            if (!TryGetCanonicalMeshFromRootMatrix(meshData, slotName, out Matrix4x4 sourceMeshFromRoot))
            {
                return $"Could not reconstruct source mesh transform for slot '{slotName}'.";
            }

            if (!TryGetCanonicalMeshFromRootMatrix(donorSlot.meshData, donorSlot.slotName, out Matrix4x4 donorMeshFromRoot))
            {
                return $"Could not reconstruct donor mesh transform for slot '{donorSlot.slotName}'.";
            }

            int donorBoneCount = Mathf.Min(donorSlot.meshData.bindPoses != null ? donorSlot.meshData.bindPoses.Length : 0,
                                           donorSlot.meshData.boneNameHashes != null ? donorSlot.meshData.boneNameHashes.Length : 0);
            if (donorBoneCount <= 0)
            {
                return $"Donor slot '{donorSlot.slotName}' is missing donor-authoritative bindposes or bone hashes.";
            }

            UMAMeshData converted = meshData.DeepCopy();
            converted.SlotName = slotName;

            Quaternion legacyRotation = Quaternion.identity;
            if (isLegacySlot)
            {
                legacyRotation = overrideAxisConversion
                    ? Quaternion.Euler(axisX, axisY, axisZ)
                    : Quaternion.Euler(0f, 0f, 90f);
            }

            Matrix4x4 legacyVertexRotation = Matrix4x4.TRS(Vector3.zero, legacyRotation, Vector3.one);

            Matrix4x4 sourceToDonor = donorMeshFromRoot.inverse * sourceMeshFromRoot;
            Matrix4x4 vertexTransform = sourceToDonor * legacyVertexRotation;
            Matrix4x4 normalTransform = sourceToDonor.inverse.transpose;

            TransformMeshIntoDonorSpace(converted, vertexTransform, normalTransform, sourceToDonor);

            Uma3WeightRemapStats remapStats = RemapManagedWeightsToDonor(converted, donorSlot.meshData);
            ApplyDonorBindingMetadata(converted, donorSlot.meshData);
            converted.boneWeights = null;

            converted.vertexCount = converted.vertices != null ? converted.vertices.Length : 0;
            converted.verticesModified = converted.vertices != null && converted.vertices.Length > 0;
            converted.normalsModified = converted.normals != null && converted.normals.Length > 0;
            converted.tangentsModified = converted.tangents != null && converted.tangents.Length > 0;
            converted.SlotName = slotName;

            meshData = converted;
            isLegacySlot = false;
            ValidateMeshData();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif

            return $"Converted '{slotName}' to donor '{donorSlot.slotName}'. Mapped weights: {remapStats.mappedWeights}, unmapped weights: {remapStats.unmappedWeights}, fallback vertices: {remapStats.fallbackVertices}.";
        }

        private static bool EnsureManagedWeightsForUma3(UMAMeshData data, string debugName, out string message)
        {
            message = null;
            if (data == null)
            {
                message = $"Slot '{debugName}' has no meshData.";
                return false;
            }

            bool haveManaged = data.ManagedBoneWeights != null && data.ManagedBoneWeights.Length > 0 &&
                               data.ManagedBonesPerVertex != null && data.ManagedBonesPerVertex.Length == data.vertexCount;
            if (haveManaged)
            {
                return true;
            }

            if (data.boneWeights != null && data.boneWeights.Length > 0)
            {
                data.LoadBoneWeights();
                haveManaged = data.ManagedBoneWeights != null && data.ManagedBoneWeights.Length > 0 &&
                              data.ManagedBonesPerVertex != null && data.ManagedBonesPerVertex.Length == data.vertexCount;
                if (haveManaged)
                {
                    return true;
                }
            }

            message = $"Slot '{debugName}' does not have usable ManagedBoneWeights/ManagedBonesPerVertex for UMA3 conversion.";
            return false;
        }

        private static void TransformMeshIntoDonorSpace(UMAMeshData data, Matrix4x4 vertexTransform, Matrix4x4 normalTransform, Matrix4x4 tangentTransform)
        {
            if (data.vertices != null && data.vertices.Length > 0)
            {
                data.vertices = TransformPositions(data.vertices, vertexTransform);
            }

            if (data.normals != null && data.normals.Length == data.vertexCount)
            {
                data.normals = TransformDirections(data.normals, normalTransform, true);
            }

            if (data.tangents != null && data.tangents.Length == data.vertexCount)
            {
                data.tangents = TransformTangents(data.tangents, tangentTransform);
            }

            data.blendShapes = CloneAndTransformBlendShapes(data.blendShapes, vertexTransform, normalTransform, tangentTransform);
        }

        private static Uma3WeightRemapStats RemapManagedWeightsToDonor(UMAMeshData source, UMAMeshData donor)
        {
            var stats = new Uma3WeightRemapStats();
            var donorHashToIndex = BuildDonorHashLookup(donor);
            int fallbackBoneIndex = GetFallbackDonorBoneIndex(donor, donorHashToIndex);

            if (fallbackBoneIndex < 0)
            {
                throw new InvalidOperationException("Donor slot does not contain a valid fallback bone index.");
            }

            int[] sourceBoneHashes = source.boneNameHashes ?? Array.Empty<int>();
            byte[] newBonesPerVertex = new byte[source.ManagedBonesPerVertex.Length];
            var newWeights = new List<BoneWeight1>(Math.Max(source.ManagedBoneWeights.Length, source.ManagedBonesPerVertex.Length));

            int weightOffset = 0;
            for (int vertexIndex = 0; vertexIndex < source.ManagedBonesPerVertex.Length; vertexIndex++)
            {
                int weightCount = source.ManagedBonesPerVertex[vertexIndex];
                var accumulatedWeights = new Dictionary<int, float>(weightCount);

                for (int weightIndex = 0; weightIndex < weightCount && weightOffset < source.ManagedBoneWeights.Length; weightIndex++)
                {
                    BoneWeight1 sourceWeight = source.ManagedBoneWeights[weightOffset++];
                    if (sourceWeight.weight <= Uma3WeightEpsilon)
                    {
                        continue;
                    }

                    if (sourceWeight.boneIndex < 0 || sourceWeight.boneIndex >= sourceBoneHashes.Length)
                    {
                        stats.unmappedWeights++;
                        continue;
                    }

                    int boneHash = sourceBoneHashes[sourceWeight.boneIndex];
                    if (!donorHashToIndex.TryGetValue(boneHash, out int donorBoneIndex))
                    {
                        stats.unmappedWeights++;
                        continue;
                    }

                    stats.mappedWeights++;
                    if (accumulatedWeights.ContainsKey(donorBoneIndex))
                    {
                        accumulatedWeights[donorBoneIndex] += sourceWeight.weight;
                    }
                    else
                    {
                        accumulatedWeights.Add(donorBoneIndex, sourceWeight.weight);
                    }
                }

                if (accumulatedWeights.Count == 0)
                {
                    accumulatedWeights.Add(fallbackBoneIndex, 1f);
                    stats.fallbackVertices++;
                }

                float totalWeight = 0f;
                foreach (var kvp in accumulatedWeights)
                {
                    totalWeight += kvp.Value;
                }

                if (totalWeight <= Uma3WeightEpsilon)
                {
                    accumulatedWeights.Clear();
                    accumulatedWeights.Add(fallbackBoneIndex, 1f);
                    totalWeight = 1f;
                    stats.fallbackVertices++;
                }

                var orderedWeights = new List<KeyValuePair<int, float>>(accumulatedWeights);
                orderedWeights.Sort((left, right) =>
                {
                    int byWeight = right.Value.CompareTo(left.Value);
                    if (byWeight != 0)
                    {
                        return byWeight;
                    }
                    return left.Key.CompareTo(right.Key);
                });

                newBonesPerVertex[vertexIndex] = (byte)orderedWeights.Count;
                for (int i = 0; i < orderedWeights.Count; i++)
                {
                    var ordered = orderedWeights[i];
                    newWeights.Add(new BoneWeight1
                    {
                        boneIndex = ordered.Key,
                        weight = ordered.Value / totalWeight
                    });
                }
            }

            source.ManagedBonesPerVertex = newBonesPerVertex;
            source.ManagedBoneWeights = newWeights.ToArray();
            return stats;
        }

        private static Dictionary<int, int> BuildDonorHashLookup(UMAMeshData donor)
        {
            int donorBoneCount = Mathf.Min(donor.bindPoses != null ? donor.bindPoses.Length : 0,
                                           donor.boneNameHashes != null ? donor.boneNameHashes.Length : 0);
            var lookup = new Dictionary<int, int>(donorBoneCount);
            for (int i = 0; i < donorBoneCount; i++)
            {
                int hash = donor.boneNameHashes[i];
                if (!lookup.ContainsKey(hash))
                {
                    lookup.Add(hash, i);
                }
            }
            return lookup;
        }

        private static int GetFallbackDonorBoneIndex(UMAMeshData donor, Dictionary<int, int> donorHashToIndex)
        {
            if (donor == null)
            {
                return -1;
            }

            if (donor.rootBoneHash != 0 && donorHashToIndex.TryGetValue(donor.rootBoneHash, out int rootBoneIndex))
            {
                return rootBoneIndex;
            }

            int donorBoneCount = Mathf.Min(donor.bindPoses != null ? donor.bindPoses.Length : 0,
                                           donor.boneNameHashes != null ? donor.boneNameHashes.Length : 0);
            return donorBoneCount > 0 ? 0 : -1;
        }

        private static void ApplyDonorBindingMetadata(UMAMeshData target, UMAMeshData donor)
        {
            target.bindPoses = donor.bindPoses != null ? (Matrix4x4[])donor.bindPoses.Clone() : null;
            target.boneNameHashes = donor.boneNameHashes != null ? (int[])donor.boneNameHashes.Clone() : null;
            target.rootBoneHash = donor.rootBoneHash;
            target.RootBoneName = donor.RootBoneName;
            target.umaBoneCount = donor.umaBoneCount;
            target.umaBones = DuplicateUmaBones(donor.umaBones);
            target.bones = donor.bones != null ? (Transform[])donor.bones.Clone() : null;
            target.rootBone = donor.rootBone;
        }

        private static UMATransform[] DuplicateUmaBones(UMATransform[] bones)
        {
            if (bones == null)
            {
                return null;
            }

            var duplicated = new UMATransform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                duplicated[i] = bones[i] != null ? bones[i].Duplicate() : null;
            }
            return duplicated;
        }

        private static UMABlendShape[] CloneAndTransformBlendShapes(UMABlendShape[] source, Matrix4x4 deltaVertexTransform, Matrix4x4 deltaNormalTransform, Matrix4x4 deltaTangentTransform)
        {
            if (source == null)
            {
                return null;
            }

            var cloned = new UMABlendShape[source.Length];
            for (int shapeIndex = 0; shapeIndex < source.Length; shapeIndex++)
            {
                UMABlendShape shape = source[shapeIndex];
                if (shape == null)
                {
                    continue;
                }

                var clonedShape = new UMABlendShape();
                clonedShape.shapeName = shape.shapeName;
                if (shape.frames != null)
                {
                    clonedShape.frames = new UMABlendFrame[shape.frames.Length];
                    for (int frameIndex = 0; frameIndex < shape.frames.Length; frameIndex++)
                    {
                        UMABlendFrame frame = shape.frames[frameIndex];
                        if (frame == null)
                        {
                            continue;
                        }

                        var clonedFrame = new UMABlendFrame();
                        clonedFrame.frameWeight = frame.frameWeight;
                        clonedFrame.deltaVertices = TransformDirections(frame.deltaVertices, deltaVertexTransform, false);
                        clonedFrame.deltaNormals = TransformDirections(frame.deltaNormals, deltaNormalTransform, false);
                        clonedFrame.deltaTangents = TransformDirections(frame.deltaTangents, deltaTangentTransform, false);
                        clonedShape.frames[frameIndex] = clonedFrame;
                    }
                }

                cloned[shapeIndex] = clonedShape;
            }

            return cloned;
        }

        private static Vector3[] TransformPositions(Vector3[] source, Matrix4x4 transform)
        {
            if (source == null)
            {
                return null;
            }

            var transformed = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                transformed[i] = transform.MultiplyPoint3x4(source[i]);
            }
            return transformed;
        }

        private static Vector3[] TransformDirections(Vector3[] source, Matrix4x4 transform, bool normalize)
        {
            if (source == null)
            {
                return null;
            }

            var transformed = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 direction = transform.MultiplyVector(source[i]);
                if (normalize && direction.sqrMagnitude > 1e-8f)
                {
                    direction.Normalize();
                }
                transformed[i] = direction;
            }
            return transformed;
        }

        private static Vector4[] TransformTangents(Vector4[] source, Matrix4x4 transform)
        {
            if (source == null)
            {
                return null;
            }

            bool flipHandedness = IsMirrored(transform);
            var transformed = new Vector4[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 tangent = transform.MultiplyVector(new Vector3(source[i].x, source[i].y, source[i].z));
                if (tangent.sqrMagnitude > 1e-8f)
                {
                    tangent.Normalize();
                }

                float tangentW = flipHandedness ? -source[i].w : source[i].w;
                transformed[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangentW);
            }
            return transformed;
        }

        private static bool IsMirrored(Matrix4x4 transform)
        {
            Vector3 xAxis = transform.MultiplyVector(Vector3.right);
            Vector3 yAxis = transform.MultiplyVector(Vector3.up);
            Vector3 zAxis = transform.MultiplyVector(Vector3.forward);
            return Vector3.Dot(Vector3.Cross(xAxis, yAxis), zAxis) < 0f;
        }

        private static bool TryGetCanonicalMeshFromRootMatrix(UMAMeshData meshData, string debugName, out Matrix4x4 meshFromRoot)
        {
            meshFromRoot = Matrix4x4.identity;
            if (meshData == null || meshData.bindPoses == null || meshData.bindPoses.Length == 0 ||
                meshData.boneNameHashes == null || meshData.boneNameHashes.Length == 0)
            {
                return false;
            }

            int rootHash = meshData.rootBoneHash;
            if (rootHash == 0 && !string.IsNullOrEmpty(meshData.RootBoneName))
            {
                rootHash = UMAUtils.StringToHash(meshData.RootBoneName);
            }

            var localByHash = new Dictionary<int, UMATransform>(meshData.umaBones != null ? meshData.umaBones.Length : 0);
            if (meshData.umaBones != null)
            {
                for (int i = 0; i < meshData.umaBones.Length; i++)
                {
                    UMATransform bone = meshData.umaBones[i];
                    if (bone != null)
                    {
                        localByHash[bone.hash] = bone;
                    }
                }
            }

            int candidateCount = Mathf.Min(meshData.bindPoses.Length, meshData.boneNameHashes.Length);
            if (candidateCount <= 0)
            {
                return false;
            }

            float[] boneWeightSums = BuildBoneWeightSums(meshData, candidateCount);
            var cachedBoneMatrices = new Dictionary<int, Matrix4x4>(localByHash.Count + 1);

            Uma3MeshTransformCandidate bestCandidate = default;
            bool foundCandidate = false;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!TryGetBoneFromRootMatrix(meshData.boneNameHashes[i], rootHash, localByHash, cachedBoneMatrices, out Matrix4x4 boneFromRoot))
                {
                    continue;
                }

                float candidateWeight = boneWeightSums != null && i < boneWeightSums.Length ? boneWeightSums[i] : 0f;
                if (!foundCandidate || candidateWeight > bestCandidate.weight)
                {
                    bestCandidate = new Uma3MeshTransformCandidate
                    {
                        boneIndex = i,
                        weight = candidateWeight,
                        meshFromRoot = boneFromRoot * meshData.bindPoses[i]
                    };
                    foundCandidate = true;
                }
            }

            if (!foundCandidate)
            {
                return false;
            }

            int mismatchCount = 0;
            float maxMismatch = 0f;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!TryGetBoneFromRootMatrix(meshData.boneNameHashes[i], rootHash, localByHash, cachedBoneMatrices, out Matrix4x4 boneFromRoot))
                {
                    continue;
                }

                Matrix4x4 candidate = boneFromRoot * meshData.bindPoses[i];
                if (!MatricesApproximatelyEqual(candidate, bestCandidate.meshFromRoot, Uma3ConversionMatrixTolerance, out float diff))
                {
                    mismatchCount++;
                    if (diff > maxMismatch)
                    {
                        maxMismatch = diff;
                    }
                }
            }

            if (mismatchCount > 0)
            {
                Debug.LogWarning($"[ConvertSlotDataToUMA3] Slot '{debugName}' produced {mismatchCount} canonical matrix mismatches. Using bone index {bestCandidate.boneIndex} with max diff {maxMismatch:0.######}.");
            }

            meshFromRoot = bestCandidate.meshFromRoot;
            return true;
        }

        private static float[] BuildBoneWeightSums(UMAMeshData meshData, int boneCount)
        {
            float[] sums = new float[boneCount];
            if (meshData == null || boneCount <= 0)
            {
                return sums;
            }

            if (meshData.ManagedBoneWeights != null && meshData.ManagedBoneWeights.Length > 0)
            {
                for (int i = 0; i < meshData.ManagedBoneWeights.Length; i++)
                {
                    BoneWeight1 weight = meshData.ManagedBoneWeights[i];
                    if (weight.boneIndex >= 0 && weight.boneIndex < boneCount)
                    {
                        sums[weight.boneIndex] += weight.weight;
                    }
                }
                return sums;
            }

            if (meshData.boneWeights != null && meshData.boneWeights.Length > 0)
            {
                for (int i = 0; i < meshData.boneWeights.Length; i++)
                {
                    UMABoneWeight weight = meshData.boneWeights[i];
                    AccumulateLegacyBoneWeight(sums, weight.boneIndex0, weight.weight0, boneCount);
                    AccumulateLegacyBoneWeight(sums, weight.boneIndex1, weight.weight1, boneCount);
                    AccumulateLegacyBoneWeight(sums, weight.boneIndex2, weight.weight2, boneCount);
                    AccumulateLegacyBoneWeight(sums, weight.boneIndex3, weight.weight3, boneCount);
                }
            }

            return sums;
        }

        private static void AccumulateLegacyBoneWeight(float[] sums, int boneIndex, float weight, int boneCount)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= boneCount)
            {
                return;
            }

            sums[boneIndex] += weight;
        }

        private static bool TryGetBoneFromRootMatrix(int boneHash, int rootHash, Dictionary<int, UMATransform> localByHash, Dictionary<int, Matrix4x4> cache, out Matrix4x4 boneFromRoot)
        {
            boneFromRoot = Matrix4x4.identity;
            if (boneHash == 0)
            {
                return false;
            }

            if (rootHash != 0 && boneHash == rootHash)
            {
                return true;
            }

            if (cache.TryGetValue(boneHash, out boneFromRoot))
            {
                return true;
            }

            if (!localByHash.TryGetValue(boneHash, out UMATransform bone) || bone == null)
            {
                return false;
            }

            Matrix4x4 localMatrix = Matrix4x4.TRS(bone.position, NormalizeSafe(bone.rotation), SanitizeScale(bone.scale));
            if (bone.parent == 0 || bone.parent == boneHash || (rootHash != 0 && bone.parent == rootHash) || !localByHash.ContainsKey(bone.parent))
            {
                boneFromRoot = localMatrix;
            }
            else if (TryGetBoneFromRootMatrix(bone.parent, rootHash, localByHash, cache, out Matrix4x4 parentFromRoot))
            {
                boneFromRoot = parentFromRoot * localMatrix;
            }
            else
            {
                boneFromRoot = localMatrix;
            }

            cache[boneHash] = boneFromRoot;
            return true;
        }

        private static bool MatricesApproximatelyEqual(Matrix4x4 lhs, Matrix4x4 rhs, float tolerance, out float maxDifference)
        {
            maxDifference = 0f;
            bool equal = true;
            for (int i = 0; i < 16; i++)
            {
                float diff = Mathf.Abs(lhs[i] - rhs[i]);
                if (diff > maxDifference)
                {
                    maxDifference = diff;
                }
                if (diff > tolerance)
                {
                    equal = false;
                }
            }
            return equal;
        }

        private static Quaternion NormalizeSafe(Quaternion rotation)
        {
            if (float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) || float.IsNaN(rotation.w) ||
                float.IsInfinity(rotation.x) || float.IsInfinity(rotation.y) || float.IsInfinity(rotation.z) || float.IsInfinity(rotation.w))
            {
                return Quaternion.identity;
            }

            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            if (magnitude < 1e-8f)
            {
                return Quaternion.identity;
            }

            float invMagnitude = 1f / magnitude;
            return new Quaternion(rotation.x * invMagnitude, rotation.y * invMagnitude, rotation.z * invMagnitude, rotation.w * invMagnitude);
        }

        private static Vector3 SanitizeScale(Vector3 scale)
        {
            if (float.IsNaN(scale.x) || float.IsInfinity(scale.x)) scale.x = 1f;
            if (float.IsNaN(scale.y) || float.IsInfinity(scale.y)) scale.y = 1f;
            if (float.IsNaN(scale.z) || float.IsInfinity(scale.z)) scale.z = 1f;
            return scale;
        }
    }
}