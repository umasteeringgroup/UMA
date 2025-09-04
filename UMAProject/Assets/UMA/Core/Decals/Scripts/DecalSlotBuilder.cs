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
    ///  - A triangle is included if ANY vertex lies within radius OR any edge intersects the radius sphere.
    ///  - Normal facing test uses facingThreshold only (dot(triNormal, rayDir) <= -facingThreshold).
    ///  - angleDegrees ONLY rotates the UV projection axes around the ray direction (no filtering).
    ///  - Rest (bind-pose) vertex data copied from sharedMesh.
    ///  - UV0 planar projected using the POSED (baked) vertex positions (fixes 90° rotation on bones like hair),
    ///    while geometry stored remains in rest space. (Previously used rest vertices -> UV rotation mismatch.)
    ///  - Full UMA skeleton included; weights remapped by bone hash.
    ///  - Blendshapes & cloth skipped (stubs).
    ///  - Returns null if no triangles selected.
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
            if (smr == null)//  || !smr.transform.IsChildOf(avatar.transform))
                return null;

            Mesh baked = new Mesh();
            try
            {
                smr.BakeMesh(baked);
                var shared = smr.sharedMesh;
                if (shared == null) return null;

                var bakedVertsLocal = baked.vertices;   // posed local vertices (used for selection & UV projection)
                var triIndices = shared.triangles;
                if (bakedVertsLocal == null || bakedVertsLocal.Length == 0 || triIndices == null || triIndices.Length == 0)
                    return null;

                // Rest pose (shared mesh) attribute arrays
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

                var includedVertex = new bool[sharedVertexCount];
                var includedTriangles = new List<int>(2048);

                SelectTriangles(triIndices, bakedVertsLocal, t, rayDirWorld, hitPointWorld, radiusSqr,
                                options.facingThreshold, includedTriangles, includedVertex, options.enableDebug);

                if (includedTriangles.Count == 0)
                {
                    if (options.enableDebug)
                        Debug.Log("DecalSlotBuilder: No triangles within radius/facing constraints.");
                    return null;
                }

                // Build vertex remap
                var remap = new int[sharedVertexCount];
                Array.Fill(remap, -1);
                int newVertexCount = 0;
                for (int i = 0; i < sharedVertexCount; i++)
                    if (includedVertex[i])
                        remap[i] = newVertexCount++;
                if (newVertexCount == 0) return null;

                // Allocate output arrays
                var outVerts = new Vector3[newVertexCount];
                var outNormals = new Vector3[newVertexCount];
                var outTangents = new Vector4[newVertexCount];
                var outColors32 = new Color32[newVertexCount];
                var outUV = new Vector2[newVertexCount];
                var outUV2 = (restUV2 != null && restUV2.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;
                var outUV3 = (restUV3 != null && restUV3.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;
                var outUV4 = (restUV4 != null && restUV4.Length == sharedVertexCount) ? new Vector2[newVertexCount] : null;

                // UV projection setup (local space)
                Vector3 localHitPoint = t.InverseTransformPoint(hitPointWorld);
                Vector3 localRayDir = t.InverseTransformDirection(rayDirWorld).normalized;
                BuildProjectionAxesAroundRay(localRayDir, angleDegrees, out var axisX, out var axisY);

                // Fill geometry (rest pose) & compute UV using POSED baked local vertex
                for (int ov = 0; ov < sharedVertexCount; ov++)
                {
                    int nv = remap[ov];
                    if (nv < 0) continue;

                    // Rest geometry
                    outVerts[nv] = restVerts[ov];
                    outNormals[nv] = (restNormals != null && ov < restNormals.Length) ? restNormals[ov] : Vector3.up;
                    outTangents[nv] = (restTangents != null && ov < restTangents.Length) ? restTangents[ov] : new Vector4(1, 0, 0, 1);
                    outColors32[nv] = (restColors32 != null && ov < restColors32.Length) ? restColors32[ov] : new Color32(255, 255, 255, 255);
                    if (outUV2 != null) outUV2[nv] = restUV2[ov];
                    if (outUV3 != null) outUV3[nv] = restUV3[ov];
                    if (outUV4 != null) outUV4[nv] = restUV4[ov];

                    // Projection uses baked (posed) vertex (fixes 90° rotation issues on bones like hair)
                    Vector3 posedLocal = bakedVertsLocal[ov];
                    Vector3 offset = posedLocal - localHitPoint;
                    float along = Vector3.Dot(offset, localRayDir);
                    Vector3 planar = offset - along * localRayDir;

                    float u = (Vector3.Dot(planar, axisX) / radius) * 0.5f + 0.5f;
                    float v = (Vector3.Dot(planar, axisY) / radius) * 0.5f + 0.5f;
                    outUV[nv] = new Vector2(u, v);
                }

                // Remap triangles
                var outTriangles = new int[includedTriangles.Count];
                for (int i = 0; i < includedTriangles.Count; i++)
                    outTriangles[i] = remap[includedTriangles[i]];

                // Bone weights
                BuildBoneWeightsFullSkeleton(avatar, smr, shared, includedVertex, remap, newVertexCount,
                    out var outBonesPerVertex, out var outBoneWeights);

                // Full UMA skeleton (REST-POSE DATA, not current animated pose)
                // Using live transforms previously caused rotated / offset hair (double transform) because
                // umaBones must contain the REST local values that match the bind poses.
                var skeleton          = avatar.umaData.GetSkeleton();
                var skeletonHashes    = new List<int>(skeleton.boneHashData.Keys);
                skeletonHashes.Sort();

                // Build rest pose map from TPose (preferred) or from an existing slot asset if available.
                var restBoneMap = BuildRestBoneMap(avatar.umaData, skeletonHashes);

                var umaBones = new UMATransform[skeletonHashes.Count];
                for (int i = 0; i < skeletonHashes.Count; i++)
                {
                    if (restBoneMap.TryGetValue(skeletonHashes[i], out var restUT))
                    {
                        umaBones[i] = restUT;
                    }
                    else
                    {
                        // Fallback – identity (should be rare)
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
                }

                // Bind poses: use the renderer's bindposes keyed by bone hash (these already match REST pose)
                var rendererBones    = smr.bones;
                var sharedBindPoses  = shared.bindposes;
                var hashToBindPose   = new Dictionary<int, Matrix4x4>(rendererBones.Length);
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

                // Assemble UMAMeshData
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

                // Future stubs
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

                includedTriangles.Add(i0);
                includedTriangles.Add(i1);
                includedTriangles.Add(i2);
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

        #region Rest Bone Map
        // Build a dictionary of hash -> UMATransform representing REST pose (NOT current animation pose).
        // Priority: TPose (race / override) > any existing slot meshData.umaBones (first hit) > identity.
        private static Dictionary<int, UMATransform> BuildRestBoneMap(UMAData umaData, List<int> skeletonHashes)
        {
            var map = new Dictionary<int, UMATransform>(skeletonHashes.Count);

            // 1. TPose data
            var tp = umaData.GetTPose();
            if (tp != null)
            {
                tp.DeSerialize(); // ensure boneInfo loaded
                var bones = tp.boneInfo;
                if (bones != null)
                {
                    for (int i = 0; i < bones.Length; i++)
                    {
                        var sb = bones[i];
                        int h = UMAUtils.StringToHash(sb.name);
                        if (!map.ContainsKey(h))
                        {
                            int parentHash = 0;
                            // parent name is not stored in SkeletonBone; rely on existing skeleton for parent link
                            var parentTf = umaData.skeleton.GetBoneTransform(h)?.parent;
                            if (parentTf != null)
                                parentHash = UMAUtils.StringToHash(parentTf.name);
                            map.Add(h, new UMATransform
                            {
                                hash = h,
                                name = sb.name,
                                parent = parentHash,
                                position = sb.position,
                                rotation = sb.rotation,
                                scale = sb.scale
                            });
                        }
                    }
                }
            }

            // 2. Existing slot assets (fill gaps)
            var slots = umaData.umaRecipe?.slotDataList;
            if (slots != null)
            {
                for (int s = 0; s < slots.Length; s++)
                {
                    var slot = slots[s];
                    var md = slot?.asset?.meshData;
                    if (md?.umaBones == null) continue;
                    for (int b = 0; b < md.umaBones.Length; b++)
                    {
                        var ub = md.umaBones[b];
                        if (!map.ContainsKey(ub.hash))
                        {
                            // Clone so we don't share instance
                            map.Add(ub.hash, new UMATransform
                            {
                                hash = ub.hash,
                                name = ub.name,
                                parent = ub.parent,
                                position = ub.position,
                                rotation = ub.rotation,
                                scale = ub.scale
                            });
                        }
                    }
                }
            }

            return map;
        }
        #endregion
     }
 }