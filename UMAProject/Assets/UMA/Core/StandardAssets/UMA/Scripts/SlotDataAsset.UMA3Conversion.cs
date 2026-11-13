using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    public partial class SlotDataAsset
    {
        private const float Uma3ConversionMatrixTolerance = 0.001f;

        private struct Uma3MeshTransformCandidate
        {
            public int boneIndex;
            public float weight;
            public Matrix4x4 meshFromRoot;
        }

        /// <summary>
        /// Reconstructs the mesh-to-root transform encoded by a slot's bone hierarchy and bind
        /// poses. Editor previews and standalone tools use this to display raw UMAMeshData in
        /// canonical character space without generating an avatar.
        /// </summary>
        /// <remarks>
        /// This assumes one mesh transform for the whole slot and picks the heaviest bone when the
        /// bind poses disagree. Rig-to-rig conversion must not rely on it; use
        /// <see cref="UMASlotRigConverter"/>, which bakes each influence through its own bind pose.
        /// </remarks>
        public static bool TryGetCanonicalMeshFromRootMatrix(UMAMeshData meshData, string debugName,
            out Matrix4x4 meshFromRoot)
        {
            meshFromRoot = Matrix4x4.identity;
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || meshData.bindPoses == null || meshData.bindPoses.Length == 0 ||
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

            // Only bones that move vertices can disagree in a way that matters. Weightless bones, such as the "_end"
            // leaf bones exporters add with blank bind poses, are left out, so the warning only names real problems.
            bool hasWeights = false;
            if (boneWeightSums != null)
            {
                for (int i = 0; i < boneWeightSums.Length && !hasWeights; i++)
                {
                    hasWeights = boneWeightSums[i] > 0f;
                }
            }

            int mismatchCount = 0;
            float maxMismatch = 0f;
            for (int i = 0; i < candidateCount; i++)
            {
                if (hasWeights && (i >= boneWeightSums.Length || boneWeightSums[i] <= 0f))
                {
                    continue;
                }
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
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData) || boneCount <= 0)
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
