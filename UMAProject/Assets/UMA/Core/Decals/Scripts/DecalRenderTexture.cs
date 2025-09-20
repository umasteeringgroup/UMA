using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// DecalRenderTexture:
    ///  - Parallel in spirit to DecalSlotBuilder but writes a stamped overlay texture into UMA's generated RenderTextures (UV space).
    ///  - Raycasts (mesh-based) against first SkinnedMeshRenderer hit (closest facing triangle).
    ///  - Selects triangles within (radius + fudgeRadius) sphere around hit point (world space).
    ///  - Uses existing UV0 to position fragments in the target RenderTexture(s).
    ///  - Generates overlay UV (planar projected + rotation) in UV1 for circular mask + falloff.
    ///  - Alpha blends (straight alpha) using overlay texture RGBA (overlay.textureList[channel]) per UMA material channel.
    ///  - Optional smooth edge falloff controlled by fudgeRadius (simple smoothstep).
    ///  - Optional dilation pass (bleedPixels > 0) to reduce mip seam artifacts (applied per stamped RenderTexture).
    ///  - Returns DecalLayerResult with UV bounding rect and stats.
    /// </summary>
    public sealed class DecalRenderTexture : ScriptableObject
    {
        private DecalRenderTexture() { }

        [Serializable]
        public struct DecalLayerResult
        {
            public bool success;
            public Rect uvBounds;
            public int vertexCount;
            public int triangleCount;
            public Vector3 hitPoint;
            public Vector3 hitNormal;
        }

        [Serializable]
        public class DecalRTOptions
        {
            public LayerMask layerMask = ~0;
            public float maxDistance = 100f;
            public float facingThreshold = 0.15f;
            public bool enableDebug = false;
            public bool forceLinearSampling = false;   // #16.2
            public int bleedPixels = 2;                // #15.2 edge dilation
        }

        /// <summary>
        /// CreateDecalLayer: stamps an overlay's textures into all UMA-generated RenderTextures for that overlay's UMAMaterial and channels.
        /// Each channel uses the same projected triangle set and UVs.
        /// </summary>
        /// <param name="avatar">Target avatar used for mesh raycast and skeleton.</param>
        /// <param name="ray">Ray to project decal from.</param>
        /// <param name="radius">World-space radius.</param>
        /// <param name="fudgeRadius">Extra radius to soften edges.</param>
        /// <param name="angleDegrees">Rotation around normal in degrees.</param>
        /// <param name="umaData">UMAData that holds generated RenderTextures.</param>
        /// <param name="overlay">OverlayDataAsset providing per-channel source textures and UMAMaterial mapping.</param>
        /// <param name="options">Stamping options.</param>
        public static DecalLayerResult? CreateDecalLayer(
            DynamicCharacterAvatar avatar,
            Ray ray,
            float radius,
            float fudgeRadius,
            float angleDegrees,
            UMAData umaData,
            OverlayDataAsset overlay,
            DecalRTOptions options = null)
        {
            var result = new DecalLayerResult { success = false };
            if (avatar == null || avatar.umaData == null)
            {
                Debug.LogError("DecalRenderTexture: Avatar or UMAData null.");
                return null;
            }
            if (umaData == null)
            {
                Debug.LogError("DecalRenderTexture: Provided UMAData is null.");
                return null;
            }
            if (overlay == null || overlay.textureList == null || overlay.textureList.Length == 0)
            {
                if (options?.enableDebug == true)
                    Debug.LogWarning("DecalRenderTexture: Overlay missing or has no textures. Aborting.");
                return null;
            }
            if (radius <= 0.00001f) return null;

            options ??= new DecalRTOptions();

            if (!MeshRaycastAvatar(avatar, ray, options, out var smr, out var hitPointWorld, out var hitNormalWorld))
            {
                if (options.enableDebug)
                    Debug.LogWarning("DecalRenderTexture: Mesh raycast failed / no facing triangle.");
                return null;
            }

            // Bake SMR (we only need vertex positions for selection & projection)
            Mesh baked = new Mesh();
            smr.BakeMesh(baked);
            try
            {
                var shared = smr.sharedMesh;
                if (shared == null) return null;

                var bakedVertsLocal = baked.vertices;
                var triIndices = shared.triangles;
                var meshUV = shared.uv; // UV0
                if (bakedVertsLocal == null || bakedVertsLocal.Length == 0 ||
                    triIndices == null || triIndices.Length == 0 ||
                    meshUV == null || meshUV.Length != bakedVertsLocal.Length)
                    return null;

                // Prepare selection
                float expandedRadius = radius + fudgeRadius;
                float radiusSqr = expandedRadius * expandedRadius;
                Transform t = smr.transform;

                var includedVertex = new bool[bakedVertsLocal.Length];
                var selectedTris = new List<int>(2048);

                SelectTriangles(
                    triIndices,
                    bakedVertsLocal,
                    t,
                    ray.direction.normalized,
                    hitPointWorld,
                    radiusSqr,
                    options.facingThreshold,
                    selectedTris,
                    includedVertex,
                    options.enableDebug);

                if (selectedTris.Count == 0)
                {
                    if (options.enableDebug)
                        Debug.LogWarning("DecalRenderTexture: No triangles selected inside radius.");
                    return null;
                }

                // Remap vertices for a compact dynamic mesh
                int[] remap = new int[bakedVertsLocal.Length];
                Array.Fill(remap, -1);
                int newVertexCount = 0;
                for (int i = 0; i < bakedVertsLocal.Length; i++)
                {
                    if (includedVertex[i])
                        remap[i] = newVertexCount++;
                }
                if (newVertexCount == 0) return null;

                var outPositions = new Vector3[newVertexCount]; // will store clip-space XY from UV0 later
                var outOverlayUV = new Vector2[newVertexCount]; // UV1: planar projected local circle
                var outMainUV = new Vector2[newVertexCount];    // UV0: original UV0
                var outColors = new Color32[newVertexCount];    // optional debug/neutral (white)

                // Build projection axes (planar) like DecalSlotBuilder
                Vector3 localHit = t.InverseTransformPoint(hitPointWorld);
                Vector3 localRayDir = t.InverseTransformDirection(ray.direction).normalized;
                BuildProjectionAxesAroundRay(localRayDir, angleDegrees, out var axisX, out var axisY);

                // Fill vertex data
                Vector2 uvMin = new Vector2(1f, 1f);
                Vector2 uvMax = new Vector2(0f, 0f);

                for (int v = 0; v < bakedVertsLocal.Length; v++)
                {
                    int nv = remap[v];
                    if (nv < 0) continue;

                    // Main UV
                    Vector2 uv = meshUV[v];
                    uv.x = Mathf.Clamp01(uv.x);
                    uv.y = Mathf.Clamp01(uv.y);
                    outMainUV[nv] = uv;

                    uvMin = Vector2.Min(uvMin, uv);
                    uvMax = Vector2.Max(uvMax, uv);

                    // Planar projection around hit for overlay space
                    Vector3 posedLocal = bakedVertsLocal[v];
                    Vector3 offset = posedLocal - localHit;
                    float along = Vector3.Dot(offset, localRayDir);
                    Vector3 planar = offset - along * localRayDir;

                    float px = Vector3.Dot(planar, axisX);
                    float py = Vector3.Dot(planar, axisY);
                    float u = (px / radius) * 0.5f + 0.5f;
                    float v2 = (py / radius) * 0.5f + 0.5f;
                    outOverlayUV[nv] = new Vector2(u, v2);

                    outColors[nv] = new Color32(255, 255, 255, 255);

                    // Vertex position for stamping mesh: map UV0 -> clip space (-1..1)
                    outPositions[nv] = new Vector3(uv.x * 2f - 1f, uv.y * 2f - 1f, 0f);
                }

                // Remap triangle indices
                int[] outIndices = new int[selectedTris.Count];
                for (int i = 0; i < selectedTris.Count; i++)
                {
                    outIndices[i] = remap[selectedTris[i]];
                }

                // Build dynamic mesh (positions already in clip space)
                var stampMesh = new Mesh { name = "DecalRT_StampMesh" };
                stampMesh.indexFormat = (newVertexCount > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                stampMesh.vertices = outPositions;
                stampMesh.triangles = outIndices;
                stampMesh.uv = outMainUV;
                stampMesh.uv2 = outOverlayUV;
                stampMesh.colors32 = outColors;
                stampMesh.RecalculateBounds();

                var recipe = umaData.umaRecipe;
                // Map combined vertices to their originating SlotData
                SlotData[] vertexSlot = null;
                if (recipe != null && shared != null)
                {
                    int combinedVertexCount = shared.vertexCount;
                    vertexSlot = new SlotData[combinedVertexCount];
                    var slots = recipe.slotDataList;
                    if (slots != null)
                    {
                        for (int si = 0; si < slots.Length; si++)
                        {
                            var slot = slots[si];
                            if (slot?.asset?.meshData == null) continue;
                            int start = slot.vertexOffset;
                            int count = slot.asset.meshData.vertexCount;
                            int end = start + count;
                            if (start < 0 || end > combinedVertexCount) continue;
                            for (int v = start; v < end; v++)
                            {
                                vertexSlot[v] = slot;
                            }
                        }
                    }
                }

                // Collect UMAMaterials from the slots that contributed to the selected region
                var selectedMaterials = new HashSet<UMAMaterial>();
                if (vertexSlot != null)
                {
                    for (int ov = 0; ov < includedVertex.Length; ov++)
                    {
                        if (!includedVertex[ov]) continue;
                        var slot = vertexSlot[ov];
                        if (slot == null) continue;
                        var mat = slot.material;
                        if (mat != null)
                        {
                            selectedMaterials.Add(mat);
                        }
                    }
                }

                // Build list of generated materials that belong to the hit SMR and selected UMAMaterials
                var targetGeneratedMaterials = new List<UMAData.GeneratedMaterial>();
                var gms = umaData.generatedMaterials.materials;
                for (int i = 0; i < gms.Count; i++)
                {
                    var gm = gms[i];
                    if (gm == null) continue;
                    if (gm.skinnedMeshRenderer != smr) continue;
                    if (gm.umaMaterial == null) continue;
                    if (selectedMaterials.Count > 0)
                    {
                        // Use UMA's Equals to handle cross-bundle equality
                        foreach (var sel in selectedMaterials)
                        {
                            if (gm.umaMaterial.Equals(sel))
                            {
                                targetGeneratedMaterials.Add(gm);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Fallback: if we couldn't map slots, still target this renderer's materials
                        targetGeneratedMaterials.Add(gm);
                    }
                }
                if (targetGeneratedMaterials.Count == 0)
                {
                    if (options.enableDebug) Debug.LogWarning("DecalRenderTexture: No matching generated materials found on hit renderer.");
                    UnityEngine.Object.DestroyImmediate(stampMesh);
                    return null;
                }

                // Build per-material UV clipping rect from SlotData.UVArea (generated atlas rects)
                var matToUVRect = new Dictionary<UMAMaterial, Rect>();
                if (vertexSlot != null)
                {
                    for (int ov = 0; ov < includedVertex.Length; ov++)
                    {
                        if (!includedVertex[ov]) continue;
                        var slot = vertexSlot[ov];
                        if (slot == null) continue;
                        var mat = slot.material;
                        if (mat == null) continue;
                        var r = slot.UVArea;
                        if (matToUVRect.TryGetValue(mat, out var existing))
                        {
                            // Union with existing
                            float minX = Mathf.Min(existing.xMin, r.xMin);
                            float minY = Mathf.Min(existing.yMin, r.yMin);
                            float maxX = Mathf.Max(existing.xMax, r.xMax);
                            float maxY = Mathf.Max(existing.yMax, r.yMax);
                            matToUVRect[mat] = Rect.MinMaxRect(minX, minY, maxX, maxY);
                        }
                        else
                        {
                            matToUVRect[mat] = r;
                        }
                    }
                }

                // Fallback UV bounds from selected vertices (if UVArea data not present)
                float minU = 1f, minV = 1f, maxU = 0f, maxV = 0f;
                for (int v = 0; v < outMainUV.Length; v++)
                {
                    float u = outMainUV[v].x;
                    float vv = outMainUV[v].y;
                    if (u < minU) minU = u;
                    if (vv < minV) minV = vv;
                    if (u > maxU) maxU = u;
                    if (vv > maxV) maxV = vv;
                }
                var fallbackRect = Rect.MinMaxRect(minU, minV, maxU, maxV);

                // Acquire material & shader
                Material stampMat = GetOrCreateStampMaterial(options.forceLinearSampling);
                if (stampMat == null)
                {
                    UnityEngine.Object.DestroyImmediate(stampMesh);
                    return null;
                }

                // Fudge factor for falloff: portion between radius and expanded radius
                float fudgeFactor = (fudgeRadius <= 0f) ? 0.0001f : (fudgeRadius / (radius + fudgeRadius));
                stampMat.SetFloat("_Fudge", fudgeFactor);
                stampMat.SetFloat("_UseUVRect", 1.0f);

                // Shared draw state for stamping
                var prevRTGlobal = RenderTexture.active;
                GL.PushMatrix();
                GL.LoadOrtho();

                int stampedCount = 0;

                // Iterate target generated materials and stamp per channel
                for (int mg = 0; mg < targetGeneratedMaterials.Count; mg++)
                {
                    var gm = targetGeneratedMaterials[mg];
                    if (gm.resultingAtlasList == null) continue;

                    // Choose UVRect based on this generated material's UMA material
                    Rect clipRect;
                    if (gm.umaMaterial != null && matToUVRect.TryGetValue(gm.umaMaterial, out var cr))
                    {
                        clipRect = cr;
                    }
                    else
                    {
                        clipRect = fallbackRect;
                    }
                    stampMat.SetVector("_UVRect", new Vector4(clipRect.xMin, clipRect.yMin, clipRect.xMax, clipRect.yMax));

                    int targetChannels = gm.resultingAtlasList.Length;
                    int sourceChannels = overlay.textureList.Length;
                    int channels = Mathf.Min(targetChannels, sourceChannels);

                    for (int ch = 0; ch < channels; ch++)
                    {
                        var src = overlay.textureList[ch];
                        if (src == null) continue;
                        var tgt = gm.resultingAtlasList[ch];
                        if (!(tgt is RenderTexture rt)) continue;

                        stampMat.SetTexture("_OverlayTex", src);

                        // Draw into RT (alpha blend)
                        RenderTexture.active = rt;
                        stampMat.SetPass(0);
                        Graphics.DrawMeshNow(stampMesh, Matrix4x4.identity);
                        stampedCount++;

                        // Optional dilation per RT
                        if (options.bleedPixels > 0)
                        {
                            RunDilation(rt, options.bleedPixels);
                        }
                    }
                }

                // Restore global state
                GL.PopMatrix();
                RenderTexture.active = prevRTGlobal;

                if (options.enableDebug)
                {
                    Debug.Log($"DecalRenderTexture: Stamped overlay '{overlay.name}' on {stampedCount} target(s). Verts={newVertexCount} Tris={outIndices.Length / 3}");
                }

                result.success = stampedCount > 0;
                result.vertexCount = newVertexCount;
                result.triangleCount = outIndices.Length / 3;
                result.uvBounds = Rect.MinMaxRect(uvMin.x, uvMin.y, uvMax.x, uvMax.y);
                result.hitPoint = hitPointWorld;
                result.hitNormal = hitNormalWorld;

                UnityEngine.Object.DestroyImmediate(stampMesh);

                return result.success ? result : (DecalLayerResult?)null;
            }
            finally
            {
                UMAUtils.DestroySceneObject(baked);
            }
        }

        #region Mesh Raycast (copied style from DecalSlotBuilder)
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
                                              DecalRTOptions options,
                                              out SkinnedMeshRenderer hitSmr,
                                              out Vector3 hitPoint,
                                              out Vector3 hitNormal)
        {
            hitSmr = null;
            hitPoint = default;
            hitNormal = default;

            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs == null || smrs.Length == 0) return false;

            Mesh bakeMesh = new Mesh();
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
                var tris = shared.triangles;
                if (verts == null || tris == null || tris.Length == 0) continue;

                Transform tr = smr.transform;
                Vector3 ro = ray.origin;
                Vector3 rd = ray.direction;
                int triCount = tris.Length / 3;

                for (int t = 0; t < triCount; t++)
                {
                    int i0 = tris[t * 3 + 0];
                    int i1 = tris[t * 3 + 1];
                    int i2 = tris[t * 3 + 2];
                    if ((uint)i0 >= verts.Length || (uint)i1 >= verts.Length || (uint)i2 >= verts.Length) continue;

                    Vector3 w0 = tr.TransformPoint(verts[i0]);
                    Vector3 w1 = tr.TransformPoint(verts[i1]);
                    Vector3 w2 = tr.TransformPoint(verts[i2]);

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
                Debug.DrawLine(hitPoint, hitPoint + hitNormal * 0.05f, Color.cyan, 2f);
            }

            return true;
        }

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
            float invDet = 1f / det;
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

        #region Triangle Selection (mirrors DecalSlotBuilder)
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
                Debug.Log($"DecalRenderTexture.SelectTriangles: {includedTriangles.Count / 3} tris selected.");
        }

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

        #region Projection Axis (reused)
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

        #region Materials & Shaders
        private static Material GetOrCreateStampMaterial(bool forceLinear)
        {
            Shader stampShader = Shader.Find("Hidden/UMA/DecalRTStamp");
            if (stampShader == null)
            {
                Debug.LogWarning("DecalRenderTexture: stamp shader 'Hidden/UMA/DecalRTStamp' not found.");
                return null;
            }
            var mat = new Material(stampShader) { name = "DecalRTStamp_Mat" };
            mat.SetFloat("_ForceLinear", forceLinear ? 1f : 0f);
            return mat;
        }

        private static void RunDilation(RenderTexture rt, int bleedPixels)
        {
            if (bleedPixels <= 0) return;
            Shader dilateShader = Shader.Find("Hidden/UMA/DecalRTDilate");
            if (dilateShader == null)
            {
                Debug.LogWarning("DecalRenderTexture: Dilation shader 'Hidden/UMA/DecalRTDilate' not found.");
                return;
            }
            var mat = new Material(dilateShader) { name = "DecalRT_DilateMat" };

            // Use the new radius parameter to reduce passes. Max radius per pass is 16.
            int remaining = Mathf.Max(0, bleedPixels);
            while (remaining > 0)
            {
                int step = Mathf.Min(remaining, 16);
                mat.SetFloat("_Radius", step);

                RenderTexture tmp = RenderTexture.GetTemporary(rt.descriptor);
                Graphics.Blit(rt, tmp);          // input copy
                Graphics.Blit(tmp, rt, mat);     // dilated output with radius=step
                RenderTexture.ReleaseTemporary(tmp);

                remaining -= step;
            }
            UnityEngine.Object.DestroyImmediate(mat);
        }
        #endregion
    }
}