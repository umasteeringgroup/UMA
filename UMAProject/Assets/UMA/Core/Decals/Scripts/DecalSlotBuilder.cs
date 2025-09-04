using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// DecalSlotBuilder with bindpose mismatch correction:
    ///  - Per-slot rest data used (slot.asset.meshData.*) instead of combined sharedMesh arrays.
    ///  - If any influencing bone for selected vertices has a bindpose differing from the renderer's canonical bindpose
    ///    (first mismatch found), a correction matrix C = RestCanonical * inverse(RestSlot) is computed (from that bone)
    ///    and applied to positions, normals, tangents of ONLY vertices influenced by any mismatched bone.
    ///  - Bone duplication not performed; geometry is conformed to canonical bindposes instead.
    /// </summary>
    public sealed class DecalSlotBuilder : ScriptableObject
    {
        private DecalSlotBuilder() { }

        public class DecalBuildOptions
        {
            public LayerMask layerMask = ~0;
            public float maxDistance = 100f;
            public float facingThreshold = 0.15f;
            public bool enableDebug = false;
        }

        public static SlotDataAsset CreateDecalSlot(
            DynamicCharacterAvatar avatar,
            Ray ray,
            float radius,
            float angleDegrees,
            UMAMaterial umaMaterial,
            DecalBuildOptions options = null)
        {
            if (avatar == null || avatar.umaData == null || umaMaterial == null) return null;
            if (radius <= 0.00001f) return null;

            options ??= new DecalBuildOptions();

            if (!Physics.Raycast(ray, out var hit, options.maxDistance, options.layerMask, QueryTriggerInteraction.Ignore))
                return null;

            var smr = hit.collider ? hit.collider.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (smr == null)
                return null;

            Mesh baked = new Mesh();
            try
            {
                smr.BakeMesh(baked);
                var shared = smr.sharedMesh;
                if (shared == null) return null;

                var bakedVertsLocal = baked.vertices;
                var triIndices = shared.triangles;
                if (bakedVertsLocal == null || bakedVertsLocal.Length == 0 || triIndices == null || triIndices.Length == 0)
                    return null;

                // Build vertex -> slot/localIndex mapping from current recipe
                var recipe = avatar.umaData.umaRecipe;
                if (recipe == null || recipe.slotDataList == null) return null;

                int combinedVertexCount = shared.vertexCount;
                var vertexSlot = new SlotData[combinedVertexCount];
                var vertexLocalIndex = new int[combinedVertexCount];
                for (int si = 0; si < recipe.slotDataList.Length; si++)
                {
                    var slot = recipe.slotDataList[si];
                    if (slot?.asset?.meshData == null) continue;
                    int start = slot.vertexOffset;
                    int count = slot.asset.meshData.vertexCount;
                    int end = start + count;
                    if (start < 0 || end > combinedVertexCount) continue;
                    for (int v = start; v < end; v++)
                    {
                        vertexSlot[v] = slot;
                        vertexLocalIndex[v] = v - start;
                    }
                }

                Vector3 rayDirWorld = ray.direction.normalized;
                Vector3 hitPointWorld = hit.point;
                float radiusSqr = radius * radius;
                Transform t = smr.transform;

                var includedVertex = new bool[combinedVertexCount];
                var includedTriangles = new List<int>(2048);

                SelectTriangles(triIndices, bakedVertsLocal, t, rayDirWorld, hitPointWorld, radiusSqr,
                                options.facingThreshold, includedTriangles, includedVertex, options.enableDebug);

                if (includedTriangles.Count == 0)
                {
                    if (options.enableDebug) Debug.Log("DecalSlotBuilder: No triangles within radius/facing constraints.");
                    return null;
                }

                // Remap
                var remap = new int[combinedVertexCount];
                Array.Fill(remap, -1);
                int newVertexCount = 0;
                for (int i = 0; i < combinedVertexCount; i++)
                    if (includedVertex[i])
                        remap[i] = newVertexCount++;
                if (newVertexCount == 0) return null;

                // Allocate output arrays
                var outVerts = new Vector3[newVertexCount];
                var outNormals = new Vector3[newVertexCount];
                var outTangents = new Vector4[newVertexCount];
                var outColors32 = new Color32[newVertexCount];
                var outUV = new Vector2[newVertexCount];
                Vector2[][] slotExtraUVs = { null, null, null }; // uv2, uv3, uv4 assembled per-slot if needed (not projected here)

                // UV projection basis (local)
                Vector3 localHitPoint = t.InverseTransformPoint(hitPointWorld);
                Vector3 localRayDir = t.InverseTransformDirection(rayDirWorld).normalized;
                BuildProjectionAxesAroundRay(localRayDir, angleDegrees, out var axisX, out var axisY);

                // Copy per-slot rest attributes
                for (int ov = 0; ov < combinedVertexCount; ov++)
                {
                    int nv = remap[ov];
                    if (nv < 0) continue;

                    var slot = vertexSlot[ov];
                    int localIdx = vertexLocalIndex[ov];
                    // Fallback to shared if slot missing
                    Vector3 restPos, restNormal;
                    Vector4 restTangent;
                    Color32 restColor;
                    Vector2 uv2 = Vector2.zero, uv3 = Vector2.zero, uv4 = Vector2.zero;

                    if (slot?.asset?.meshData != null && localIdx >= 0 && localIdx < slot.asset.meshData.vertexCount)
                    {
                        var mdSrc = slot.asset.meshData;
                        restPos = SafeGet(mdSrc.vertices, localIdx, Vector3.zero);
                        restNormal = SafeGet(mdSrc.normals, localIdx, Vector3.up);
                        restTangent = SafeGet(mdSrc.tangents, localIdx, new Vector4(1, 0, 0, 1));
                        restColor = SafeGet(mdSrc.colors32, localIdx, new Color32(255, 255, 255, 255));
                        uv2 = SafeGet(mdSrc.uv2, localIdx, Vector2.zero);
                        uv3 = SafeGet(mdSrc.uv3, localIdx, Vector2.zero);
                        uv4 = SafeGet(mdSrc.uv4, localIdx, Vector2.zero);
                    }
                    else
                    {
                        // fallback shared
                        restPos = SafeGet(shared.vertices, ov, Vector3.zero);
                        restNormal = SafeGet(shared.normals, ov, Vector3.up);
                        restTangent = SafeGet(shared.tangents, ov, new Vector4(1, 0, 0, 1));
                        restColor = SafeGet(shared.colors32, ov, new Color32(255, 255, 255, 255));
                    }

                    outVerts[nv] = restPos;
                    outNormals[nv] = restNormal;
                    outTangents[nv] = restTangent;
                    outColors32[nv] = restColor;

                    // Projection uses baked posed vertex
                    Vector3 posedLocal = bakedVertsLocal[ov];
                    Vector3 offset = posedLocal - localHitPoint;
                    float along = Vector3.Dot(offset, localRayDir);
                    Vector3 planar = offset - along * localRayDir;
                    float u = (Vector3.Dot(planar, axisX) / radius) * 0.5f + 0.5f;
                    float v = (Vector3.Dot(planar, axisY) / radius) * 0.5f + 0.5f;
                    outUV[nv] = new Vector2(u, v);

                    // Store secondary UVs after we know arrays needed
                    if (uv2 != Vector2.zero || uv3 != Vector2.zero || uv4 != Vector2.zero)
                    {
                        if (slotExtraUVs[0] == null) slotExtraUVs[0] = new Vector2[newVertexCount];
                        if (slotExtraUVs[1] == null) slotExtraUVs[1] = new Vector2[newVertexCount];
                        if (slotExtraUVs[2] == null) slotExtraUVs[2] = new Vector2[newVertexCount];
                        slotExtraUVs[0][nv] = uv2;
                        slotExtraUVs[1][nv] = uv3;
                        slotExtraUVs[2][nv] = uv4;
                    }
                }

                // Remap triangles
                var outTriangles = new int[includedTriangles.Count];
                for (int i = 0; i < includedTriangles.Count; i++)
                    outTriangles[i] = remap[includedTriangles[i]];

                // Detect bindpose mismatch + build correction
                ApplyBindposeCorrection(shared, smr, vertexSlot, vertexLocalIndex,
                                        includedVertex, remap,
                                        outVerts, outNormals, outTangents,
                                        options.enableDebug);

                // Bone weights (post correction)
                BuildBoneWeightsFullSkeleton(avatar, smr, shared, includedVertex, remap, newVertexCount,
                    out var outBonesPerVertex, out var outBoneWeights);

                // Skeleton + bind poses (canonical)
                var skeleton = avatar.umaData.GetSkeleton();
                var skeletonHashes = new List<int>(skeleton.boneHashData.Keys);
                skeletonHashes.Sort();
                var skeletonTransforms = skeleton.HashesToTransforms(skeletonHashes);
                var umaBones = new UMATransform[skeletonHashes.Count];
                for (int i = 0; i < skeletonHashes.Count; i++)
                {
                    var bt = skeletonTransforms[i];
                    if (bt == null)
                    {
                        umaBones[i] = new UMATransform
                        {
                            hash = skeletonHashes[i],
                            name = "MissingBone_" + skeletonHashes[i],
                            parent = 0,
                            position = Vector3.zero,
                            rotation = Quaternion.identity,
                            scale = Vector3.one
                        };
                    }
                    else
                    {
                        int parentHash = bt.parent ? UMAUtils.StringToHash(bt.parent.name) : 0;
                        umaBones[i] = new UMATransform(bt, skeletonHashes[i], parentHash);
                    }
                }

                // Canonical bind poses (renderer)
                var rendererBones = smr.bones;
                var sharedBindPoses = shared.bindposes;
                var hashToBindPose = new Dictionary<int, Matrix4x4>(rendererBones.Length);
                for (int i = 0; i < rendererBones.Length && i < sharedBindPoses.Length; i++)
                {
                    var rb = rendererBones[i];
                    if (rb == null) continue;
                    int h = UMAUtils.StringToHash(rb.name);
                    if (!hashToBindPose.ContainsKey(h))
                        hashToBindPose.Add(h, sharedBindPoses[i]);
                }
                var finalBindPoses = new Matrix4x4[umaBones.Length];
                for (int i = 0; i < umaBones.Length; i++)
                    finalBindPoses[i] = hashToBindPose.TryGetValue(umaBones[i].hash, out var bp) ? bp : Matrix4x4.identity;

                var md = new UMAMeshData
                {
                    SlotName = $"Decal_{umaMaterial.name}",
                    vertices = outVerts,
                    normals = outNormals,
                    tangents = outTangents,
                    colors32 = outColors32,
                    uv = outUV,
                    uv2 = slotExtraUVs[0],
                    uv3 = slotExtraUVs[1],
                    uv4 = slotExtraUVs[2],
                    vertexCount = newVertexCount,
                    umaBones = umaBones,
                    umaBoneCount = umaBones.Length,
                    bindPoses = finalBindPoses,
                    boneNameHashes = skeletonHashes.ToArray(),
                    ManagedBonesPerVertex = outBonesPerVertex,
                    ManagedBoneWeights = outBoneWeights,
                    subMeshCount = 1,
                    submeshes = new SubMeshTriangles[1]
                };

                var sub = new SubMeshTriangles();
                sub.SetTriangles(outTriangles);
                sub.nativeTriangles = new NativeArray<int>(outTriangles, Allocator.Persistent);
                md.submeshes[0] = sub;

                Stub_BlendshapeSupport(md);
                Stub_ClothSupport(md);

                var slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
                slotAsset.slotName = md.SlotName;
                slotAsset.material = umaMaterial;
                slotAsset.meshData = md;
                slotAsset.subMeshIndex = 0;
                slotAsset.sourceSubmeshIndex = 0;
                slotAsset.tags = new[] { "Decal" };

                if (options.enableDebug)
                    Debug.Log($"DecalSlotBuilder: Created decal '{slotAsset.slotName}' Vertices={md.vertexCount} Tris={outTriangles.Length / 3}");

                return slotAsset;
            }
            finally
            {
                UMAUtils.DestroySceneObject(baked);
            }
        }

        private static void ApplyBindposeCorrection(
            Mesh shared,
            SkinnedMeshRenderer smr,
            SlotData[] vertexSlot,
            int[] vertexLocalIndex,
            bool[] includedVertex,
            int[] remap,
            Vector3[] outVerts,
            Vector3[] outNormals,
            Vector4[] outTangents,
            bool debug)
        {
            var bindposes = shared.bindposes;
            var bonesPerVertex = shared.GetBonesPerVertex();
            var allWeights = shared.GetAllBoneWeights();

            // Build prefix sum for bone weights
            int vertCount = includedVertex.Length;
            int[] weightStart = new int[vertCount];
            int acc = 0;
            for (int i = 0; i < vertCount; i++)
            {
                weightStart[i] = acc;
                acc += bonesPerVertex[i];
            }

            // Map bone index -> hash
            var rendererBones = smr.bones;
            var boneHashes = new int[rendererBones.Length];
            for (int i = 0; i < rendererBones.Length; i++)
                boneHashes[i] = rendererBones[i] ? UMAUtils.StringToHash(rendererBones[i].name) : 0;

            bool correctionComputed = false;
            Matrix4x4 correction = Matrix4x4.identity;
            var needsCorrection = new bool[outVerts.Length];

            // Cache per-slot bone hash -> slot bind pose
            var slotBindPoseCache = new Dictionary<SlotData, Dictionary<int, Matrix4x4>>();

            for (int ov = 0; ov < vertCount; ov++)
            {
                if (!includedVertex[ov]) continue;
                int nv = remap[ov];
                if (nv < 0) continue;

                var slot = vertexSlot[ov];
                int localIdx = vertexLocalIndex[ov];
                if (slot?.asset?.meshData == null) continue;

                // Build cache
                if (!slotBindPoseCache.TryGetValue(slot, out var perSlot))
                {
                    perSlot = new Dictionary<int, Matrix4x4>();
                    var md = slot.asset.meshData;
                    var slotBones = md.boneNameHashes;
                    var slotBindPoses = md.bindPoses;
                    if (slotBones != null && slotBindPoses != null)
                    {
                        int len = Math.Min(slotBones.Length, slotBindPoses.Length);
                        for (int i = 0; i < len; i++)
                            if (!perSlot.ContainsKey(slotBones[i]))
                                perSlot.Add(slotBones[i], slotBindPoses[i]);
                    }
                    slotBindPoseCache.Add(slot, perSlot);
                }

                int weightCount = bonesPerVertex[ov];
                int start = weightStart[ov];
                for (int w = 0; w < weightCount; w++)
                {
                    var bw = allWeights[start + w];
                    int boneIndex = bw.boneIndex;
                    if (boneIndex < 0 || boneIndex >= boneHashes.Length) continue;
                    int hash = boneHashes[boneIndex];
                    if (!perSlot.TryGetValue(hash, out var slotBindPose))
                        continue; // Slot didn't define this bone (possible if not weighted in original)

                    var canonicalBindPose = bindposes[boneIndex];

                    if (!CompareSkinningMatrices(canonicalBindPose, slotBindPose))
                    {
                        if (!correctionComputed)
                        {
                            // rest matrices
                            Matrix4x4 restCanon = Matrix4x4.Inverse(canonicalBindPose);
                            Matrix4x4 restSlot = Matrix4x4.Inverse(slotBindPose);
                            correction = restCanon * Matrix4x4.Inverse(restSlot);
                            correctionComputed = true;
                        }
                        needsCorrection[nv] = true;
                        // Pick first mismatch only (per user spec)
                        break;
                    }
                }
            }

            if (!correctionComputed) return;

            // Extract rotation (upper-left 3x3) for normals/tangents
            Quaternion rot = Quaternion.LookRotation(
                correction.GetColumn(2),
                correction.GetColumn(1));
            // Fallback if degenerate
            if (rot == Quaternion.identity)
            {
                // Build from matrix basis properly
                Vector3 c0 = correction.GetColumn(0);
                Vector3 c1 = correction.GetColumn(1);
                Vector3 c2 = correction.GetColumn(2);
                Matrix4x4 m = correction;
                rot = QuaternionFromMatrix(ref m);
            }

            for (int i = 0; i < outVerts.Length; i++)
            {
                if (!needsCorrection[i]) continue;

                Vector3 p = outVerts[i];
                Vector4 hp = new Vector4(p.x, p.y, p.z, 1f);
                hp = correction * hp;
                outVerts[i] = new Vector3(hp.x, hp.y, hp.z);

                Vector3 n = outNormals[i];
                n = rot * n;
                outNormals[i] = n.normalized;

                if (outTangents != null && i < outTangents.Length)
                {
                    Vector4 tan = outTangents[i];
                    Vector3 tv = new Vector3(tan.x, tan.y, tan.z);
                    tv = rot * tv;
                    tv.Normalize();
                    outTangents[i] = new Vector4(tv.x, tv.y, tv.z, tan.w);
                }
            }
        }

        private static Vector3 SafeGet(Vector3[] arr, int i, Vector3 def) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : def;
        private static Vector4 SafeGet(Vector4[] arr, int i, Vector4 def) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : def;
        private static Color32 SafeGet(Color32[] arr, int i, Color32 def) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : def;
        private static Vector2 SafeGet(Vector2[] arr, int i, Vector2 def) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : def;

        private static bool CompareSkinningMatrices(Matrix4x4 a, Matrix4x4 b)
        {
            const float eps = 0.0001f;
            return
                Math.Abs(a.m00 - b.m00) <= eps &&
                Math.Abs(a.m01 - b.m01) <= eps &&
                Math.Abs(a.m02 - b.m02) <= eps &&
                Math.Abs(a.m03 - b.m03) <= eps &&
                Math.Abs(a.m10 - b.m10) <= eps &&
                Math.Abs(a.m11 - b.m11) <= eps &&
                Math.Abs(a.m12 - b.m12) <= eps &&
                Math.Abs(a.m13 - b.m13) <= eps &&
                Math.Abs(a.m20 - b.m20) <= eps &&
                Math.Abs(a.m21 - b.m21) <= eps &&
                Math.Abs(a.m22 - b.m22) <= eps &&
                Math.Abs(a.m23 - b.m23) <= eps;
        }

        private static Quaternion QuaternionFromMatrix(ref Matrix4x4 m)
        {
            return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
        }

        private static void SelectTriangles(
            int[] triIndices,
            Vector3[] bakedVertsLocal,
            Transform rendererTransform,
            Vector3 rayDirWorld,
            Vector3 hitPointWorld,
            float radiusSqr,
            float facingThreshold,
            List<int> includedTriangles,
            bool[] includedVertex,
            bool debug)
        {
            int triCount = triIndices.Length / 3;
            for (int tri = 0; tri < triCount; tri++)
            {
                int i0 = triIndices[tri * 3 + 0];
                int i1 = triIndices[tri * 3 + 1];
                int i2 = triIndices[tri * 3 + 2];
                if ((uint)i0 >= bakedVertsLocal.Length || (uint)i1 >= bakedVertsLocal.Length || (uint)i2 >= bakedVertsLocal.Length)
                    continue;

                Vector3 w0 = rendererTransform.TransformPoint(bakedVertsLocal[i0]);
                Vector3 w1 = rendererTransform.TransformPoint(bakedVertsLocal[i1]);
                Vector3 w2 = rendererTransform.TransformPoint(bakedVertsLocal[i2]);

                Vector3 n = Vector3.Cross(w1 - w0, w2 - w0);
                float nm = n.magnitude;
                if (nm < 1e-7f) continue;
                n /= nm;

                if (Vector3.Dot(n, rayDirWorld) > -facingThreshold)
                    continue;

                bool anyInside =
                    (w0 - hitPointWorld).sqrMagnitude <= radiusSqr ||
                    (w1 - hitPointWorld).sqrMagnitude <= radiusSqr ||
                    (w2 - hitPointWorld).sqrMagnitude <= radiusSqr;

                bool edgeIntersects = false;
                if (!anyInside)
                {
                    if (SegmentSphereIntersect(w0, w1, hitPointWorld, radiusSqr) ||
                        SegmentSphereIntersect(w1, w2, hitPointWorld, radiusSqr) ||
                        SegmentSphereIntersect(w2, w0, hitPointWorld, radiusSqr))
                        edgeIntersects = true;
                }

                if (!anyInside && !edgeIntersects)
                    continue;

                includedTriangles.Add(i0); includedTriangles.Add(i1); includedTriangles.Add(i2);
                includedVertex[i0] = includedVertex[i1] = includedVertex[i2] = true;
            }

            if (debug)
                Debug.Log($"DecalSlotBuilder.SelectTriangles: {includedTriangles.Count / 3} tris selected.");
        }

        #region Bone Weights
        private static void BuildBoneWeightsFullSkeleton(
            DynamicCharacterAvatar avatar,
            SkinnedMeshRenderer renderer,
            Mesh sharedMesh,
            bool[] includedVertex,
            int[] remap,
            int newVertexCount,
            out byte[] outBonesPerVertex,
            out BoneWeight1[] outBoneWeights)
        {
            outBonesPerVertex = new byte[newVertexCount];
            var boneWeightList = new List<BoneWeight1>(newVertexCount * 4);

            var bonesPerVertex = sharedMesh.GetBonesPerVertex();
            var allBoneWeights = sharedMesh.GetAllBoneWeights();

            int origCount = includedVertex.Length;
            var weightStart = new int[origCount];
            int acc = 0;
            for (int i = 0; i < origCount; i++)
            {
                weightStart[i] = acc;
                acc += bonesPerVertex[i];
            }

            var skeleton = avatar.umaData.GetSkeleton();
            var skeletonHashes = new List<int>(skeleton.boneHashData.Keys);
            skeletonHashes.Sort();
            var hashToFinal = new Dictionary<int, int>(skeletonHashes.Count);
            for (int i = 0; i < skeletonHashes.Count; i++)
                hashToFinal[skeletonHashes[i]] = i;

            var rendererBones = renderer.bones;
            var rendererBoneHashes = new int[rendererBones.Length];
            for (int i = 0; i < rendererBones.Length; i++)
                rendererBoneHashes[i] = rendererBones[i] ? UMAUtils.StringToHash(rendererBones[i].name) : 0;

            for (int ov = 0; ov < origCount; ov++)
            {
                int nv = remap[ov];
                if (nv < 0) continue;

                int count = bonesPerVertex[ov];
                int start = weightStart[ov];
                byte stored = 0;

                for (int j = 0; j < count; j++)
                {
                    BoneWeight1 bw = allBoneWeights[start + j];
                    int rbIndex = bw.boneIndex;
                    if (rbIndex < 0 || rbIndex >= rendererBoneHashes.Length) continue;
                    int hash = rendererBoneHashes[rbIndex];
                    if (!hashToFinal.TryGetValue(hash, out int finalIndex)) continue;

                    boneWeightList.Add(new BoneWeight1 { boneIndex = finalIndex, weight = bw.weight });
                    stored++;
                }
                outBonesPerVertex[nv] = stored;
            }

            outBoneWeights = boneWeightList.ToArray();
        }
        #endregion

        #region Geometry Helpers
        private static bool SegmentSphereIntersect(Vector3 a, Vector3 b, Vector3 center, float radiusSqr)
        {
            Vector3 ab = b - a;
            float lenSqr = ab.sqrMagnitude;
            if (lenSqr < 1e-12f)
                return (a - center).sqrMagnitude <= radiusSqr;

            float t = Vector3.Dot(center - a, ab) / lenSqr;
            t = Mathf.Clamp01(t);
            Vector3 closest = a + t * ab;
            return (closest - center).sqrMagnitude <= radiusSqr;
        }
        #endregion

        #region Projection Axes
        private static void BuildProjectionAxesAroundRay(Vector3 rayDirLocal, float angleDeg, out Vector3 axisX, out Vector3 axisY)
        {
            Vector3 up = (Mathf.Abs(Vector3.Dot(rayDirLocal, Vector3.up)) > 0.95f) ? Vector3.right : Vector3.up;
            axisX = Vector3.Cross(up, rayDirLocal).normalized;
            axisY = Vector3.Cross(rayDirLocal, axisX).normalized;

            float rad = angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector3 rx = axisX * c + axisY * s;
            Vector3 ry = -axisX * s + axisY * c;
            axisX = rx.normalized;
            axisY = ry.normalized;
        }
        #endregion

        #region Future Stubs
        private static void Stub_BlendshapeSupport(UMAMeshData md) { }
        private static void Stub_ClothSupport(UMAMeshData md) { }
        #endregion
    }
}