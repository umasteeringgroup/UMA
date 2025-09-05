using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// DecalSlotBuilder with:
    ///  - Pure mesh raycast (no collider dependency). Ray tests every SkinnedMeshRenderer under the avatar.
    ///  - First hit triangle (closest along ray, facing camera) determines hit point.
    ///  - (Current implementation builds decal from the hit SkinnedMeshRenderer only; triangle selection radius works on that renderer.
    ///    NOTE: To extend to “all SMRs in radius” (requirement #4) you would replicate the selection pass for each SMR and merge results.
    ///    A TODO marker is left where that aggregation would occur.)
    ///  - Bindpose mismatch correction retained.
    ///  - Debug visualization (triangle edges + normal) when enableDebug = true.
    ///  - Future perf hook (BVH / early culling) marked with TODO (#16).
    /// </summary>
    public sealed class DecalSlotBuilder
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
            float fudgeRadius,
            float angleDegrees,
            UMAMaterial umaMaterial,
            DecalBuildOptions options = null)
        {
            if (avatar == null || avatar.umaData == null || umaMaterial == null) return null;
            if (radius <= 0.00001f) return null;

            options ??= new DecalBuildOptions();

            // Mesh-based raycast (replaces collider based raycast)
            if (!MeshRaycastAvatar(avatar, ray, options, out var smr, out var hitPointWorld, out var hitNormalWorld))
                return null;

            // Bake only the hit SMR (already baked once in raycast; reuse bakedMeshCache if desired)
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
                float expandedRadius = radius + fudgeRadius;
                float radiusSqr = expandedRadius * expandedRadius;
                Transform t = smr.transform;

                // Triangle selection (CURRENTLY ONLY THE HIT SMR)
                // TODO (Multi-SMR aggregation): Iterate all SMRs again, selecting triangles within radius; merge arrays.
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
                Vector2[][] slotExtraUVs = { null, null, null }; // uv2, uv3, uv4

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

                // Bindpose correction
                ApplyBindposeCorrection(shared, smr, vertexSlot, vertexLocalIndex,
                                        includedVertex, remap,
                                        outVerts, outNormals, outTangents,
                                        options.enableDebug);

                // Bone weights
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

                // Canonical bind poses (aggregate first match per hash)
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

                // Build & assign blendshapes and cloth coefficients from contributing slots (selected vertices only)
                md.blendShapes = BuildBlendshapesFromSources(vertexSlot, vertexLocalIndex, includedVertex, remap, newVertexCount);
                md.clothSkinningSerialized = BuildClothCoefficients(vertexSlot, vertexLocalIndex, includedVertex, remap, newVertexCount);

                var slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
                slotAsset.slotName = md.SlotName;
                slotAsset.material = umaMaterial;
                slotAsset.meshData = md;
                slotAsset.subMeshIndex = 0;
                slotAsset.sourceSubmeshIndex = 0;
                slotAsset.tags = new[] { "Decal" };

                if (options.enableDebug)
                    Debug.Log($"DecalSlotBuilder: Created decal '{slotAsset.slotName}' Vertices={md.vertexCount} Tris={outTriangles.Length / 3} BlendShapes={(md.blendShapes != null ? md.blendShapes.Length : 0)} Cloth={(md.clothSkinningSerialized != null)}");

                return slotAsset;
            }
            finally
            {
                UMAUtils.DestroySceneObject(baked);
            }
        }

        #region Mesh Raycast
        private struct MeshHit
        {
            public SkinnedMeshRenderer smr;
            public float distance;
            public Vector3 point;
            public Vector3 normal;
            public int triangleIndex;
        }

        private static bool MeshRaycastAvatar(DynamicCharacterAvatar avatar,
                                              Ray ray,
                                              DecalBuildOptions options,
                                              out SkinnedMeshRenderer hitSmr,
                                              out Vector3 hitPoint,
                                              out Vector3 hitNormal)
        {
            hitSmr = null;
            hitPoint = default;
            hitNormal = default;

            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs == null || smrs.Length == 0) return false;

            Mesh bakeMesh = new Mesh(); // reused
            MeshHit best = new MeshHit { distance = float.MaxValue, triangleIndex = -1 };

            foreach (var smr in smrs)
            {
                if (smr == null || !smr.enabled) continue;
                int layerBit = 1 << smr.gameObject.layer;
                if ((options.layerMask.value & layerBit) == 0) continue;

                var shared = smr.sharedMesh;
                if (shared == null || shared.vertexCount == 0) continue;

                smr.BakeMesh(bakeMesh);
                var verts = bakeMesh.vertices;
                var tris = shared.triangles; // idx order maps shared->baked

                if (verts == null || tris == null || tris.Length == 0) continue;

                Transform tr = smr.transform;
                Vector3 ro = ray.origin;
                Vector3 rd = ray.direction;

                int triCount = tris.Length / 3;

                // TODO (Perf #16): Add bounds test here if needed
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = tris[t * 3 + 0];
                    int i1 = tris[t * 3 + 1];
                    int i2 = tris[t * 3 + 2];
                    if ((uint)i0 >= verts.Length || (uint)i1 >= verts.Length || (uint)i2 >= verts.Length) continue;

                    Vector3 w0 = tr.TransformPoint(verts[i0]);
                    Vector3 w1 = tr.TransformPoint(verts[i1]);
                    Vector3 w2 = tr.TransformPoint(verts[i2]);

                    // Normal & facing
                    Vector3 e1 = w1 - w0;
                    Vector3 e2 = w2 - w0;
                    Vector3 n = Vector3.Cross(e1, e2);
                    float nm = n.magnitude;
                    if (nm < 1e-6f) continue;
                    n /= nm;
                    if (Vector3.Dot(n, rd) > -options.facingThreshold) continue;

                    if (RayTriangle(ro, rd, w0, w1, w2, out float dist, out Vector3 bary))
                    {
                        if (dist < 0 || dist > options.maxDistance) continue;
                        if (dist < best.distance)
                        {
                            best.distance = dist;
                            best.point = w0 * (1 - bary.x - bary.y) + w1 * bary.x + w2 * bary.y;
                            best.normal = n;
                            best.smr = smr;
                            best.triangleIndex = t;
                            if (dist <= 1e-5f) break;
                        }
                    }
                }
            }

            UMAUtils.DestroySceneObject(bakeMesh);

            if (best.smr == null) return false;

            hitSmr = best.smr;
            hitPoint = best.point;
            hitNormal = best.normal;

            if (options.enableDebug)
            {
                Debug.DrawLine(hitPoint, hitPoint + hitNormal * 0.05f, Color.green, 2f);
                // Edge visualization
                var shared = hitSmr.sharedMesh;
                if (shared != null && best.triangleIndex >= 0)
                {
                    var tris = shared.triangles;
                    int i0 = tris[best.triangleIndex * 3 + 0];
                    int i1 = tris[best.triangleIndex * 3 + 1];
                    int i2 = tris[best.triangleIndex * 3 + 2];

                    hitSmr.BakeMesh(bakeMesh);
                    var v = bakeMesh.vertices;
                    if (i0 < v.Length && i1 < v.Length && i2 < v.Length)
                    {
                        Transform tr = hitSmr.transform;
                        Vector3 w0 = tr.TransformPoint(v[i0]);
                        Vector3 w1 = tr.TransformPoint(v[i1]);
                        Vector3 w2 = tr.TransformPoint(v[i2]);
                        Debug.DrawLine(w0, w1, Color.yellow, 2f);
                        Debug.DrawLine(w1, w2, Color.yellow, 2f);
                        Debug.DrawLine(w2, w0, Color.yellow, 2f);
                    }
                }
            }

            return true;
        }

        // Möller–Trumbore
        private static bool RayTriangle(Vector3 ro, Vector3 rd,
                                        Vector3 v0, Vector3 v1, Vector3 v2,
                                        out float distance,
                                        out Vector3 bary)
        {
            bary = default;
            distance = 0f;
            const float EPS = 1e-7f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(rd, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -EPS && det < EPS) return false;
            float invDet = 1.0f / det;
            Vector3 tvec = ro - v0;
            float u = Vector3.Dot(tvec, p) * invDet;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(tvec, e1);
            float v = Vector3.Dot(rd, q) * invDet;
            if (v < 0 || (u + v) > 1) return false;
            float t = Vector3.Dot(e2, q) * invDet;
            if (t < 0) return false;
            distance = t;
            bary = new Vector3(u, v, 1 - u - v);
            return true;
        }
        #endregion

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

            int vertCount = includedVertex.Length;
            int[] weightStart = new int[vertCount];
            int acc = 0;
            for (int i = 0; i < vertCount; i++)
            {
                weightStart[i] = acc;
                acc += bonesPerVertex[i];
            }

            var rendererBones = smr.bones;
            var boneHashes = new int[rendererBones.Length];
            for (int i = 0; i < rendererBones.Length; i++)
                boneHashes[i] = rendererBones[i] ? UMAUtils.StringToHash(rendererBones[i].name) : 0;

            bool correctionComputed = false;
            Matrix4x4 correction = Matrix4x4.identity;
            var needsCorrection = new bool[outVerts.Length];

            var slotBindPoseCache = new Dictionary<SlotData, Dictionary<int, Matrix4x4>>();

            for (int ov = 0; ov < vertCount; ov++)
            {
                if (!includedVertex[ov]) continue;
                int nv = remap[ov];
                if (nv < 0) continue;

                var slot = vertexSlot[ov];
                if (slot?.asset?.meshData == null) continue;

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
                        continue;

                    var canonicalBindPose = bindposes[boneIndex];
                    if (!CompareSkinningMatrices(canonicalBindPose, slotBindPose))
                    {
                        if (!correctionComputed)
                        {
                            Matrix4x4 restCanon = Matrix4x4.Inverse(canonicalBindPose);
                            Matrix4x4 restSlot = Matrix4x4.Inverse(slotBindPose);
                            correction = restCanon * Matrix4x4.Inverse(restSlot);
                            correctionComputed = true;
                        }
                        needsCorrection[nv] = true;
                        break;
                    }
                }
            }

            if (!correctionComputed) return;

            Quaternion rot = Quaternion.LookRotation(
                correction.GetColumn(2),
                correction.GetColumn(1));
            if (rot == Quaternion.identity)
            {
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

        #region Blendshapes & Cloth support

        private struct LocalRemap
        {
            public int localIndex;   // index within the source slot mesh
            public int newIndex;     // index within the new decal mesh
        }

        private static UMABlendShape[] BuildBlendshapesFromSources(
            SlotData[] vertexSlot,
            int[] vertexLocalIndex,
            bool[] includedVertex,
            int[] remap,
            int newVertexCount)
        {
            // Gather per-slot selected vertices mapping
            var perSlot = BuildPerSlotSelection(vertexSlot, vertexLocalIndex, includedVertex, remap);
            if (perSlot.Count == 0) return null;

            // Collect shape metadata (by name): max frame count, OR of normals/tangents, frame weights from first/longest
            var shapeMeta = new Dictionary<string, (int frameCount, bool hasNormals, bool hasTangents, float[] frameWeights)>(64);

            foreach (var kv in perSlot)
            {
                var slot = kv.Key;
                var md = slot?.asset?.meshData;
                var shapes = md?.blendShapes;
                if (shapes == null || shapes.Length == 0) continue;

                for (int s = 0; s < shapes.Length; s++)
                {
                    var ubs = shapes[s];
                    string name = ubs.shapeName ?? $"Blend_{s}";
                    int framesHere = ubs.frames.Length;
                    bool hasN = framesHere > 0 && ubs.frames[0].HasNormals();
                    bool hasT = framesHere > 0 && ubs.frames[0].HasTangents();

                    if (!shapeMeta.TryGetValue(name, out var meta))
                    {
                        meta.frameCount = framesHere;
                        meta.hasNormals = hasN;
                        meta.hasTangents = hasT;
                        meta.frameWeights = new float[framesHere];
                        for (int f = 0; f < framesHere; f++)
                            meta.frameWeights[f] = ubs.frames[f].frameWeight;
                        shapeMeta[name] = meta;
                    }
                    else
                    {
                        // Merge max frames and union flags
                        if (framesHere > meta.frameCount)
                        {
                            var newWeights = new float[framesHere];
                            Array.Copy(meta.frameWeights, newWeights, meta.frameCount);
                            // fill any remaining with this source's weights
                            for (int f = meta.frameCount; f < framesHere; f++)
                                newWeights[f] = ubs.frames[Mathf.Clamp(f, 0, ubs.frames.Length - 1)].frameWeight;
                            meta.frameWeights = newWeights;
                            meta.frameCount = framesHere;
                        }
                        meta.hasNormals |= hasN;
                        meta.hasTangents |= hasT;
                        shapeMeta[name] = meta;
                    }
                }
            }

            if (shapeMeta.Count == 0) return null;

            // Allocate destination shapes
            var dest = new UMABlendShape[shapeMeta.Count];
            var names = new string[shapeMeta.Count];
            int idx = 0;
            foreach (var kv in shapeMeta)
            {
                string name = kv.Key;
                var meta = kv.Value;
                var ubs = new UMABlendShape();
                ubs.shapeName = name;
                ubs.frames = new UMABlendFrame[meta.frameCount];
                for (int f = 0; f < meta.frameCount; f++)
                {
                    ubs.frames[f] = new UMABlendFrame(newVertexCount, meta.hasNormals, meta.hasTangents);
                    ubs.frames[f].frameWeight = meta.frameWeights[f];
                }
                dest[idx] = ubs;
                names[idx] = name;
                idx++;
            }

            // Name -> dest index
            var nameToIndex = new Dictionary<string, int>(shapeMeta.Count);
            for (int i = 0; i < names.Length; i++)
                nameToIndex[names[i]] = i;

            // Copy deltas for selected vertices only
            foreach (var kv in perSlot)
            {
                var slot = kv.Key;
                var md = slot?.asset?.meshData;
                var shapes = md?.blendShapes;
                if (shapes == null || shapes.Length == 0) continue;

                var mapping = kv.Value; // list of (localIndex -> newIndex)

                for (int s = 0; s < shapes.Length; s++)
                {
                    var srcShape = shapes[s];
                    string name = srcShape.shapeName ?? $"Blend_{s}";
                    if (!nameToIndex.TryGetValue(name, out int di)) continue;

                    var dstShape = dest[di];
                    int framesToCopy = Math.Min(dstShape.frames.Length, srcShape.frames.Length);
                    for (int f = 0; f < framesToCopy; f++)
                    {
                        var sf = srcShape.frames[f];
                        var df = dstShape.frames[f];
                        CopyBlendShapeDeltas(mapping, sf, df);
                    }
                }
            }

            return dest;
        }

        private static void CopyBlendShapeDeltas(List<LocalRemap> mapping, UMABlendFrame src, UMABlendFrame dst)
        {
            var sV = src.deltaVertices;
            var dV = dst.deltaVertices;
            Vector3[] sN = src.HasNormals() ? src.deltaNormals : null;
            Vector3[] dN = dst.HasNormals() ? dst.deltaNormals : null;
            Vector3[] sT = src.HasTangents() ? src.deltaTangents : null;
            Vector3[] dT = dst.HasTangents() ? dst.deltaTangents : null;

            for (int i = 0; i < mapping.Count; i++)
            {
                int li = mapping[i].localIndex;
                int ni = mapping[i].newIndex;
                if (li < 0 || ni < 0) continue;

                if (sV != null && li < sV.Length && ni < dV.Length)
                    dV[ni] = sV[li];
                if (sN != null && dN != null && li < sN.Length && ni < dN.Length)
                    dN[ni] = sN[li];
                if (sT != null && dT != null && li < sT.Length && ni < dT.Length)
                    dT[ni] = sT[li];
            }
        }

        private static Vector2[] BuildClothCoefficients(
            SlotData[] vertexSlot,
            int[] vertexLocalIndex,
            bool[] includedVertex,
            int[] remap,
            int newVertexCount)
        {
            var perSlot = BuildPerSlotSelection(vertexSlot, vertexLocalIndex, includedVertex, remap);
            if (perSlot.Count == 0) return null;

            bool anyCloth = false;
            Vector2 defaultCoeff = new Vector2(float.MaxValue, 0f); // (collisionSphereDistance, maxDistance)
            var dest = new Vector2[newVertexCount];
            for (int i = 0; i < newVertexCount; i++) dest[i] = defaultCoeff;

            foreach (var kv in perSlot)
            {
                var slot = kv.Key;
                var md = slot?.asset?.meshData;
                if (md == null) continue;

                Vector2[] srcSerialized = md.clothSkinningSerialized;
                ClothSkinningCoefficient[] srcCloth = md.clothSkinning;

                if ((srcSerialized == null || srcSerialized.Length == 0) &&
                    (srcCloth == null || srcCloth.Length == 0))
                    continue;

                anyCloth = true;

                var mapping = kv.Value;
                for (int i = 0; i < mapping.Count; i++)
                {
                    int li = mapping[i].localIndex;
                    int ni = mapping[i].newIndex;
                    if (li < 0 || ni < 0) continue;

                    if (srcSerialized != null && li < srcSerialized.Length)
                    {
                        dest[ni] = srcSerialized[li];
                    }
                    else if (srcCloth != null && li < srcCloth.Length)
                    {
                        var c = srcCloth[li];
                        dest[ni] = new Vector2(c.collisionSphereDistance, c.maxDistance);
                    }
                }
            }

            return anyCloth ? dest : null;
        }

        private static Dictionary<SlotData, List<LocalRemap>> BuildPerSlotSelection(
            SlotData[] vertexSlot,
            int[] vertexLocalIndex,
            bool[] includedVertex,
            int[] remap)
        {
            var perSlot = new Dictionary<SlotData, List<LocalRemap>>(16);
            int count = includedVertex.Length;
            for (int ov = 0; ov < count; ov++)
            {
                if (!includedVertex[ov]) continue;
                int nv = remap[ov];
                if (nv < 0) continue;
                var slot = vertexSlot[ov];
                int li = vertexLocalIndex[ov];
                if (slot == null || li < 0) continue;

                if (!perSlot.TryGetValue(slot, out var list))
                {
                    list = new List<LocalRemap>(64);
                    perSlot.Add(slot, list);
                }
                list.Add(new LocalRemap { localIndex = li, newIndex = nv });
            }
            return perSlot;
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