using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// DecalSlotBuilder:
    ///  - Selects triangles on the hit SkinnedMeshRenderer baked pose.
    ///  - Triangle included if any vertex is within radius OR any edge intersects the radius sphere.
    ///  - Facing test uses facingThreshold (normal must face the ray origin). If nothing selected, a fallback pass
    ///    re-runs WITHOUT the facing test (so you still get a decal instead of silent failure).
    ///  - angleDegrees only rotates UV axes around ray direction.
    ///  - All geometry (positions/normals/tangents/etc.) copied from sharedMesh (rest pose).
    ///  - UV0 planar projected (straight) onto plane perpendicular to ray; UV in [0..1] circle inscribed.
    ///  - Full UMA skeleton included; weights remapped by bone hash.
    ///  - Blendshapes & cloth skipped (stubs left).
    ///  - Returns null if still no triangles after fallback.
    /// </summary>
    public static class DecalSlotBuilder
    {
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

                // Rest pose data
                var restVerts = shared.vertices;
                var restNormals = shared.normals;
                var restTangents = shared.tangents;
                var restColors32 = shared.colors32;
                var restUV2 = shared.uv2;
                var restUV3 = shared.uv3;
                var restUV4 = shared.uv4;

                int sharedVertexCount = restVerts.Length;
                if (sharedVertexCount == 0) return null;

                Vector3 rayDirWorld = ray.direction.normalized;
                Vector3 hitPointWorld = hit.point;
                float radiusSqr = radius * radius;
                Transform t = smr.transform;

                // First pass with facing test
                var includedTriangles = new List<int>(2048);
                var includedVertex = new bool[sharedVertexCount];
                SelectTriangles(triIndices, bakedVertsLocal, t, rayDirWorld, hitPointWorld, radiusSqr,
                                options.facingThreshold, applyFacingTest: true,
                                includedTriangles, includedVertex, options.enableDebug);

                // Fallback (no facing test) if no triangles
                if (includedTriangles.Count == 0)
                {
                    if (options.enableDebug)
                        Debug.Log("DecalSlotBuilder: First pass found 0 triangles, retrying without facing test.");
                    Array.Clear(includedVertex, 0, includedVertex.Length);
                    includedTriangles.Clear();
                    SelectTriangles(triIndices, bakedVertsLocal, t, rayDirWorld, hitPointWorld, radiusSqr,
                                    options.facingThreshold, applyFacingTest: false,
                                    includedTriangles, includedVertex, options.enableDebug);
                }

                if (includedTriangles.Count == 0)
                {
                    if (options.enableDebug)
                        Debug.LogWarning("DecalSlotBuilder: No triangles after fallback; decal aborted.");
                    return null;
                }

                // Remap
                var remap = new int[sharedVertexCount];
                Array.Fill(remap, -1);
                int newVertexCount = 0;
                for (int i = 0; i < sharedVertexCount; i++)
                    if (includedVertex[i]) remap[i] = newVertexCount++;
                if (newVertexCount == 0) return null;

                // Output arrays
                var outVerts = new Vector3[newVertexCount];
                var outNormals = new Vector3[newVertexCount];
                var outTangents = new Vector4[newVertexCount];
                var outColors32 = new Color32[newVertexCount];
                var outUV = new Vector2[newVertexCount];
                var outUV2 = (restUV2 != null && restUV2.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;
                var outUV3 = (restUV3 != null && restUV3.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;
                var outUV4 = (restUV4 != null && restUV4.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;

                // UV projection (local)
                Vector3 localHitPoint = t.InverseTransformPoint(hitPointWorld);
                Vector3 localRayDir = t.InverseTransformDirection(rayDirWorld).normalized;
                BuildProjectionAxesAroundRay(localRayDir, angleDegrees, out var axisX, out var axisY);

                for (int ov = 0; ov < sharedVertexCount; ov++)
                {
                    int nv = remap[ov];
                    if (nv < 0) continue;

                    Vector3 lp = restVerts[ov];
                    outVerts[nv] = lp;
                    outNormals[nv] = (restNormals != null && ov < restNormals.Length) ? restNormals[ov] : Vector3.up;
                    outTangents[nv] = (restTangents != null && ov < restTangents.Length) ? restTangents[ov] : new Vector4(1, 0, 0, 1);
                    outColors32[nv] = (restColors32 != null && ov < restColors32.Length) ? restColors32[ov] : new Color32(255, 255, 255, 255);
                    if (outUV2 != null) outUV2[nv] = restUV2[ov];
                    if (outUV3 != null) outUV3[nv] = restUV3[ov];
                    if (outUV4 != null) outUV4[nv] = restUV4[ov];

                    Vector3 offset = lp - localHitPoint;
                    float along = Vector3.Dot(offset, localRayDir);
                    Vector3 planar = offset - along * localRayDir;

                    float u = (Vector3.Dot(planar, axisX) / radius) * 0.5f + 0.5f;
                    float v = (Vector3.Dot(planar, axisY) / radius) * 0.5f + 0.5f;
                    outUV[nv] = new Vector2(u, v);
                }

                var outTriangles = new int[includedTriangles.Count];
                for (int i = 0; i < includedTriangles.Count; i++)
                    outTriangles[i] = remap[includedTriangles[i]];

                // Bones
                BuildBoneWeightsFullSkeleton(avatar, smr, shared, includedVertex, remap, newVertexCount,
                    out var outBonesPerVertex, out var outBoneWeights);

                // Full skeleton
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

                // Bind poses
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

                // Mesh data
                var md = new UMAMeshData
                {
                    SlotName = $"Decal_{umaMaterial.name}",
                    vertices = outVerts,
                    normals = outNormals,
                    tangents = outTangents,
                    colors32 = outColors32,
                    uv = outUV,
                    uv2 = outUV2,
                    uv3 = outUV3,
                    uv4 = outUV4,
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

                // Stubs
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
                {
                    Debug.Log($"DecalSlotBuilder: Created decal '{slotAsset.slotName}' Vertices={md.vertexCount} Tris={outTriangles.Length / 3}");
                }

                return slotAsset;
            }
            finally
            {
                UMAUtils.DestroySceneObject(baked);
            }
        }

        private static void SelectTriangles(
            int[] triIndices,
            Vector3[] bakedVertsLocal,
            Transform rendererTransform,
            Vector3 rayDirWorld,
            Vector3 hitPointWorld,
            float radiusSqr,
            float facingThreshold,
            bool applyFacingTest,
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

                if (applyFacingTest)
                {
                    // Normal should oppose ray direction (face camera)
                    if (Vector3.Dot(n, rayDirWorld) > -facingThreshold)
                        continue;
                }

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
                    {
                        edgeIntersects = true;
                    }
                }

                if (!anyInside && !edgeIntersects)
                    continue;

                includedTriangles.Add(i0);
                includedTriangles.Add(i1);
                includedTriangles.Add(i2);
                includedVertex[i0] = true;
                includedVertex[i1] = true;
                includedVertex[i2] = true;
            }

            if (debug)
            {
                Debug.Log($"DecalSlotBuilder.SelectTriangles: Found {includedTriangles.Count / 3} triangles (applyFacingTest={applyFacingTest})");
            }
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