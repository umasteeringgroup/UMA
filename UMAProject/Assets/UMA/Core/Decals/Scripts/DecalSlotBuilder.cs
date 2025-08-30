using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// Builds a runtime SlotDataAsset representing a decal extracted from existing combined UMA skinned meshes.
    /// Copies source rest-pose mesh data (vertices/normals/tangents etc) directly (no transforms), projects ONLY the primary UV set.
    /// Only bones actually referenced by the kept vertices are copied, with their bind poses and remapped weights.
    /// No re-sorting or re-mapping of bones beyond what is required for the reduced set.
    /// </summary>
    public static class DecalSlotBuilder
    {
        public class DecalBuildOptions
        {
            public LayerMask layerMask = ~0;
            public float maxDistance = 100f;
            public float facingThreshold = 0.15f;
            public bool multithread = true;
            public bool copyBlendshapes = true;
        }

        #region Public API

        public static SlotDataAsset CreateDecalSlot(
            DynamicCharacterAvatar avatar,
            Ray ray,
            float radius,
            float angleDegrees,
            UMAMaterial umaMaterial,
            DecalBuildOptions options = null)
        {
            if (avatar == null || avatar.umaData == null || umaMaterial == null)
                return null;

            options ??= new DecalBuildOptions();

            if (!Physics.Raycast(ray, out var hit, options.maxDistance, options.layerMask, QueryTriggerInteraction.Ignore))
                return null;

            var hitRenderer = hit.collider ? hit.collider.GetComponentInParent<SkinnedMeshRenderer>() : null;
            if (hitRenderer != null && !hitRenderer.transform.IsChildOf(avatar.transform))
                return null;

            Vector3 hitPoint = hit.point;
            Vector3 hitNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : (-ray.direction).normalized;

            var umaData = avatar.umaData;
            var renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0) return null;

            // Collect active slots
            List<SlotData> activeSlots = new List<SlotData>();
            for (int i = 0; ; i++)
            {
                var slot = umaData.GetSlot(i);
                if (slot == null) break;
                if (slot.asset != null && slot.asset.meshData != null)
                    activeSlots.Add(slot);
            }
            if (activeSlots.Count == 0) return null;

            // Build slot vertex ranges per renderer
            var rendererSlotRanges = new Dictionary<int, List<(SlotData slot, int start, int end)>>();
            foreach (var sd in activeSlots)
            {
                int rIdx = sd.skinnedMeshRenderer;
                if (!rendererSlotRanges.TryGetValue(rIdx, out var list))
                {
                    list = new List<(SlotData, int, int)>();
                    rendererSlotRanges[rIdx] = list;
                }
                int start = sd.vertexOffset;
                int end = start + (sd.asset.meshData.vertices?.Length ?? 0);
                list.Add((sd, start, end));
            }

            // Gather baked meshes ONLY for selection & projection tests
            var perRendererData = new List<RendererTemp>();
            for (int r = 0; r < renderers.Length; r++)
            {
                var smr = renderers[r];
                if (smr == null) continue;
                if (!smr.transform.IsChildOf(avatar.transform)) continue;

                Mesh baked = new Mesh();
                smr.BakeMesh(baked);
                var shared = smr.sharedMesh;
                if (shared == null)
                {
                    UnityEngine.Object.Destroy(baked);
                    continue;
                }

                perRendererData.Add(new RendererTemp
                {
                    RendererIndex = r,
                    Renderer = smr,
                    BakedMesh = baked,
                    SharedMesh = shared,
                    Vertices = baked.vertices,
                    Normals = baked.normals,
                    Triangles = shared.triangles
                });
            }

            if (perRendererData.Count == 0) return null;

            var workerInput = new WorkerInput
            {
                HitPoint = hitPoint,
                HitNormal = hitNormal,
                Radius = radius,
                RayDirection = ray.direction.normalized,
                AngleDeg = angleDegrees,
                FacingThreshold = options.facingThreshold,
                Renderers = perRendererData.ToArray(),
                RendererSlotRanges = rendererSlotRanges,
                CaptureBlendshapeSourceInfo = options.copyBlendshapes
            };

            WorkerOutput output = options.multithread
                ? Task.Run(() => Process(workerInput)).Result
                : Process(workerInput);

            foreach (var rd in perRendererData)
                UnityEngine.Object.Destroy(rd.BakedMesh);

            if (output == null || output.Vertices.Count == 0 || output.Triangles.Count == 0)
                return null;

            // Assemble SlotDataAsset & UMAMeshData
            var sda = ScriptableObject.CreateInstance<SlotDataAsset>();
            sda.slotName = $"Decal_{umaMaterial.name}_{Guid.NewGuid():N}";
            sda.material = umaMaterial;
            var md = new UMAMeshData();
            sda.meshData = md;
            md.SlotName = sda.slotName;
            sda.tags = new string[] { "Decal" };

            int vCount = output.LocalVertices.Count;
            md.vertices = output.LocalVertices.ToArray(); // exact rest-space vertices copied, no transform
            md.normals = output.LocalNormals.ToArray();   // exact normals copied
            md.tangents = output.LocalTangents.ToArray(); // exact tangents copied
            md.uv = output.UVs.ToArray();                 // projected UVs (only thing we intentionally generate)

            // Copy optional color & other UV sets if present (rest unchanged)
            if (output.LocalColors32.Count == vCount) md.colors32 = output.LocalColors32.ToArray();
            if (output.LocalUV2.Count == vCount) md.uv2 = output.LocalUV2.ToArray();
            if (output.LocalUV3.Count == vCount) md.uv3 = output.LocalUV3.ToArray();
            if (output.LocalUV4.Count == vCount) md.uv4 = output.LocalUV4.ToArray();

            md.subMeshCount = 1;
            md.submeshes = new SubMeshTriangles[1];
            var sub = new SubMeshTriangles();
            sub.SetTriangles(output.Triangles.ToArray());
            md.submeshes[0] = sub;

            // Bones (already trimmed to only those referenced)
            md.umaBones = output.BoneList.ToArray();
            md.umaBoneCount = md.umaBones.Length;
            md.bindPoses = output.BindPoses.ToArray();
            md.boneNameHashes = new int[md.umaBoneCount];
            for (int i = 0; i < md.umaBoneCount; i++)
            {
                md.boneNameHashes[i] = UMAUtils.StringToHash(md.umaBones[i].name);
            }

            // Managed bone weights
            md.ManagedBonesPerVertex = output.BonesPerVertex.ToArray();
            md.ManagedBoneWeights = output.FlattenedWeights.ToArray();
            md.vertexCount = vCount;

            // Blendshapes (optional) - uses indices mapped earlier; copies raw deltas (rest-space)
            if (options.copyBlendshapes)
            {
                BuildBlendshapes(md, output);
            }

            sda.subMeshIndex = 0;
            sda.sourceSubmeshIndex = 0;
            return sda;
        }

        #endregion

        #region Worker Structures

        private class RendererTemp
        {
            public int RendererIndex;
            public SkinnedMeshRenderer Renderer;
            public Mesh BakedMesh;
            public Mesh SharedMesh;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public int[] Triangles;
        }

        private class WorkerInput
        {
            public Vector3 HitPoint;
            public Vector3 HitNormal;
            public float Radius;
            public Vector3 RayDirection;
            public float AngleDeg;
            public float FacingThreshold;
            public RendererTemp[] Renderers;
            public Dictionary<int, List<(SlotData slot, int start, int end)>> RendererSlotRanges;
            public bool CaptureBlendshapeSourceInfo;
        }

        private class WorkerOutput
        {
            public List<Vector3> Vertices = new List<Vector3>();          // baked world (selection only)
            public List<Vector3> LocalVertices = new List<Vector3>();     // copied rest-space
            public List<Vector3> LocalNormals = new List<Vector3>();
            public List<Vector4> LocalTangents = new List<Vector4>();
            public List<Color32> LocalColors32 = new List<Color32>();
            public List<Vector2> LocalUV2 = new List<Vector2>();
            public List<Vector2> LocalUV3 = new List<Vector2>();
            public List<Vector2> LocalUV4 = new List<Vector2>();
            public List<Vector2> UVs = new List<Vector2>();               // projected decal UV
            public List<int> Triangles = new List<int>();

            // Bone data
            public List<UMATransform> BoneList = new List<UMATransform>();
            public List<Matrix4x4> BindPoses = new List<Matrix4x4>();
            public Dictionary<(SlotData slot, int oldBone), int> BoneRemap = new Dictionary<(SlotData, int), int>();
            public List<byte> BonesPerVertex = new List<byte>();
            public List<BoneWeight1> FlattenedWeights = new List<BoneWeight1>();

            // Blendshape source mapping
            public List<SlotData> VertexSourceSlots = new List<SlotData>();
            public List<int> VertexSourceLocalIndex = new List<int>();

            // For accumulating blendshapes (name -> accumulator)
            public Dictionary<string, BlendshapeAccum> BlendshapeAccums = new Dictionary<string, BlendshapeAccum>();
        }

        #endregion

        #region Processing

        private static WorkerOutput Process(WorkerInput input)
        {
            var output = new WorkerOutput();
            BuildProjectionAxes(input.HitNormal, input.AngleDeg, out var axisX, out var axisY);
            float radiusSqr = input.Radius * input.Radius;

            // Map to avoid duplicating the *same logical source vertex* (slot + local index).
            var vertexMap = new Dictionary<(SlotData slot, int localIndex), int>();

            for (int r = 0; r < input.Renderers.Length; r++)
            {
                var rend = input.Renderers[r];
                if (!input.RendererSlotRanges.TryGetValue(rend.RendererIndex, out var slotRanges) || slotRanges.Count == 0)
                    continue;

                int triCount = rend.Triangles.Length / 3;
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = rend.Triangles[t * 3];
                    int i1 = rend.Triangles[t * 3 + 1];
                    int i2 = rend.Triangles[t * 3 + 2];

                    Vector3 w0 = rend.Renderer.transform.TransformPoint(rend.Vertices[i0]);
                    Vector3 w1 = rend.Renderer.transform.TransformPoint(rend.Vertices[i1]);
                    Vector3 w2 = rend.Renderer.transform.TransformPoint(rend.Vertices[i2]);

                    // Facing test (use baked tri normal)
                    Vector3 triNormal = Vector3.Cross(w1 - w0, w2 - w0);
                    float mag = triNormal.magnitude;
                    if (mag < 1e-6f) continue;
                    triNormal /= mag;
                    if (Vector3.Dot(triNormal, input.RayDirection) > -input.FacingThreshold)
                        continue;

                    bool inside =
                        (w0 - input.HitPoint).sqrMagnitude <= radiusSqr ||
                        (w1 - input.HitPoint).sqrMagnitude <= radiusSqr ||
                        (w2 - input.HitPoint).sqrMagnitude <= radiusSqr;
                    if (!inside) continue;

                    int nv0 = AddVertex(rend, i0, input, axisX, axisY, output, vertexMap, slotRanges);
                    int nv1 = AddVertex(rend, i1, input, axisX, axisY, output, vertexMap, slotRanges);
                    int nv2 = AddVertex(rend, i2, input, axisX, axisY, output, vertexMap, slotRanges);

                    output.Triangles.Add(nv0);
                    output.Triangles.Add(nv1);
                    output.Triangles.Add(nv2);
                }
            }

            if (output.Vertices.Count == 0) return null;
            return output;
        }

        private static int AddVertex(
            RendererTemp rend,
            int bakedIndex,
            WorkerInput input,
            Vector3 axisX,
            Vector3 axisY,
            WorkerOutput output,
            Dictionary<(SlotData slot, int localIndex), int> vertexMap,
            List<(SlotData slot, int start, int end)> slotRanges)
        {
            // Determine source slot & local vertex index
            SlotData owner = null;
            int localVertexIndex = -1;
            int combinedIndex = bakedIndex;
            foreach (var (slot, start, end) in slotRanges)
            {
                if (combinedIndex >= start && combinedIndex < end)
                {
                    owner = slot;
                    localVertexIndex = combinedIndex - start;
                    break;
                }
            }

            if (owner == null || owner.asset?.meshData == null)
            {
                return -1; // Shouldn't happen for valid triangles; skip
            }

            var key = (owner, localVertexIndex);
            if (vertexMap.TryGetValue(key, out int existing))
                return existing;

            var srcMD = owner.asset.meshData;

            // Copy rest-space attributes directly
            Vector3 restPos = (localVertexIndex < srcMD.vertices.Length) ? srcMD.vertices[localVertexIndex] : Vector3.zero;
            Vector3 restNormal = (srcMD.normals != null && localVertexIndex < srcMD.normals.Length)
                ? srcMD.normals[localVertexIndex]
                : Vector3.up;
            Vector4 restTangent = (srcMD.tangents != null && localVertexIndex < srcMD.tangents.Length)
                ? srcMD.tangents[localVertexIndex]
                : new Vector4(1, 0, 0, 1);

            Color32 restColor = (srcMD.colors32 != null && localVertexIndex < srcMD.colors32.Length)
                ? srcMD.colors32[localVertexIndex]
                : new Color32(255, 255, 255, 255);
            Vector2 uv2 = (srcMD.uv2 != null && localVertexIndex < srcMD.uv2.Length) ? srcMD.uv2[localVertexIndex] : Vector2.zero;
            Vector2 uv3 = (srcMD.uv3 != null && localVertexIndex < srcMD.uv3.Length) ? srcMD.uv3[localVertexIndex] : Vector2.zero;
            Vector2 uv4 = (srcMD.uv4 != null && localVertexIndex < srcMD.uv4.Length) ? srcMD.uv4[localVertexIndex] : Vector2.zero;

            // For projection & selection we used baked world space; get worldPos again
            Vector3 worldPos = rend.Renderer.transform.TransformPoint(rend.Vertices[bakedIndex]);
            Vector3 offset = worldPos - input.HitPoint;
            float u = (Vector3.Dot(offset, axisX) / input.Radius) * 0.5f + 0.5f;
            float v = (Vector3.Dot(offset, axisY) / input.Radius) * 0.5f + 0.5f;

            int newIndex = output.LocalVertices.Count;
            vertexMap.Add(key, newIndex);

            output.Vertices.Add(worldPos);
            output.LocalVertices.Add(restPos);
            output.LocalNormals.Add(restNormal);
            output.LocalTangents.Add(restTangent);
            output.LocalColors32.Add(restColor);
            output.LocalUV2.Add(uv2);
            output.LocalUV3.Add(uv3);
            output.LocalUV4.Add(uv4);
            output.UVs.Add(new Vector2(u, v));

            if (input.CaptureBlendshapeSourceInfo)
            {
                output.VertexSourceSlots.Add(owner);
                output.VertexSourceLocalIndex.Add(localVertexIndex);
            }

            // Bone weights: copy only weights actually used
            if (srcMD.ManagedBonesPerVertex != null &&
                localVertexIndex >= 0 &&
                localVertexIndex < srcMD.ManagedBonesPerVertex.Length)
            {
                int count = srcMD.ManagedBonesPerVertex[localVertexIndex];
                int bwStart = srcMD.BoneWeightOffset(localVertexIndex);
                byte storedCount = 0;
                for (int w = 0; w < count; w++)
                {
                    var bw1 = srcMD.ManagedBoneWeights[bwStart + w];
                    if (bw1.weight <= 0f) continue;

                    int oldBoneIndex = bw1.boneIndex;
                    if (oldBoneIndex < 0 || oldBoneIndex >= srcMD.umaBones.Length) continue;

                    var umaBone = srcMD.umaBones[oldBoneIndex];
                    var mapKey = (owner, oldBoneIndex);
                    if (!output.BoneRemap.TryGetValue(mapKey, out int newBoneIndex))
                    {
                        newBoneIndex = output.BoneList.Count;
                        output.BoneList.Add(umaBone);
                        Matrix4x4 bindPose = (srcMD.bindPoses != null && oldBoneIndex < srcMD.bindPoses.Length)
                            ? srcMD.bindPoses[oldBoneIndex]
                            : Matrix4x4.identity;
                        output.BindPoses.Add(bindPose);
                        output.BoneRemap.Add(mapKey, newBoneIndex);
                    }

                    output.FlattenedWeights.Add(new BoneWeight1
                    {
                        boneIndex = newBoneIndex,
                        weight = bw1.weight
                    });
                    storedCount++;
                }
                output.BonesPerVertex.Add(storedCount);
            }
            else
            {
                output.BonesPerVertex.Add(0);
            }

            return newIndex;
        }

        private static void BuildBlendshapes(UMAMeshData md, WorkerOutput output)
        {
            // If no mapping info, skip
            if (output.VertexSourceSlots.Count != md.vertexCount ||
                output.VertexSourceLocalIndex.Count != md.vertexCount)
                return;

            // Accumulate shapes
            for (int v = 0; v < md.vertexCount; v++)
            {
                var slot = output.VertexSourceSlots[v];
                int localIndex = output.VertexSourceLocalIndex[v];
                if (slot == null || localIndex < 0) continue;
                var srcMD = slot.asset.meshData;
                var srcShapes = srcMD.blendShapes;
                if (srcShapes == null) continue;
                for (int s = 0; s < srcShapes.Length; s++)
                {
                    var srcShape = srcShapes[s];
                    if (srcShape == null || string.IsNullOrEmpty(srcShape.shapeName) || srcShape.frames == null) continue;
                    if (!output.BlendshapeAccums.TryGetValue(srcShape.shapeName, out var acc))
                    {
                        acc = new BlendshapeAccum
                        {
                            name = srcShape.shapeName,
                            frameWeights = new List<float>(srcShape.frames.Length),
                            frames = new List<BlendshapeFrameAccum>(srcShape.frames.Length)
                        };
                        for (int f = 0; f < srcShape.frames.Length; f++)
                        {
                            var sf = srcShape.frames[f];
                            bool hasNormals = sf.deltaNormals != null && sf.deltaNormals.Length == srcMD.vertices.Length && !UMABlendFrame.isAllZero(sf.deltaNormals);
                            bool hasTangents = sf.deltaTangents != null && sf.deltaTangents.Length == srcMD.vertices.Length && !UMABlendFrame.isAllZero(sf.deltaTangents);
                            acc.frameWeights.Add(sf.frameWeight);
                            acc.frames.Add(new BlendshapeFrameAccum
                            {
                                frameWeight = sf.frameWeight,
                                deltaVertices = new Vector3[md.vertexCount],
                                deltaNormals = hasNormals ? new Vector3[md.vertexCount] : null,
                                deltaTangents = hasTangents ? new Vector3[md.vertexCount] : null,
                                hasNormals = hasNormals,
                                hasTangents = hasTangents
                            });
                        }
                        output.BlendshapeAccums.Add(srcShape.shapeName, acc);
                    }
                    else
                    {
                        if (acc.frames.Count != srcShape.frames.Length) continue;
                        bool mismatch = false;
                        for (int f = 0; f < acc.frames.Count; f++)
                        {
                            if (Math.Abs(acc.frameWeights[f] - srcShape.frames[f].frameWeight) > 0.0001f)
                            {
                                mismatch = true; break;
                            }
                        }
                        if (mismatch) continue;
                    }

                    for (int f = 0; f < srcShape.frames.Length; f++)
                    {
                        var srcFrame = srcShape.frames[f];
                        var dst = output.BlendshapeAccums[srcShape.shapeName].frames[f];
                        if (srcFrame.deltaVertices != null && localIndex < srcFrame.deltaVertices.Length)
                            dst.deltaVertices[v] = srcFrame.deltaVertices[localIndex];
                        if (dst.hasNormals && srcFrame.deltaNormals != null && localIndex < srcFrame.deltaNormals.Length)
                            dst.deltaNormals[v] = srcFrame.deltaNormals[localIndex];
                        if (dst.hasTangents && srcFrame.deltaTangents != null && localIndex < srcFrame.deltaTangents.Length)
                            dst.deltaTangents[v] = srcFrame.deltaTangents[localIndex];
                    }
                }
            }

            if (output.BlendshapeAccums.Count == 0) return;

            var newShapes = new UMABlendShape[output.BlendshapeAccums.Count];
            int idx = 0;
            foreach (var kv in output.BlendshapeAccums)
            {
                var acc = kv.Value;
                var shape = new UMABlendShape
                {
                    shapeName = acc.name,
                    frames = new UMABlendFrame[acc.frames.Count]
                };
                for (int f = 0; f < acc.frames.Count; f++)
                {
                    var fAcc = acc.frames[f];
                    var frame = new UMABlendFrame
                    {
                        frameWeight = fAcc.frameWeight,
                        deltaVertices = fAcc.deltaVertices
                    };
                    if (fAcc.hasNormals) frame.deltaNormals = fAcc.deltaNormals;
                    if (fAcc.hasTangents) frame.deltaTangents = fAcc.deltaTangents;
                    shape.frames[f] = frame;
                }
                newShapes[idx++] = shape;
            }
            md.blendShapes = newShapes;
        }

        private struct BlendshapeAccum
        {
            public string name;
            public List<float> frameWeights;
            public List<BlendshapeFrameAccum> frames;
        }

        private struct BlendshapeFrameAccum
        {
            public float frameWeight;
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
            public bool hasNormals;
            public bool hasTangents;
        }
        #endregion
        #region Helpers

        private static void BuildProjectionAxes(Vector3 normal, float angleDeg, out Vector3 axisX, out Vector3 axisY)
        {
            var up = Math.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            axisX = Vector3.Cross(up, normal).normalized;
            axisY = Vector3.Cross(normal, axisX);
            float rad = -angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector3 rx = axisX * c + axisY * s;
            Vector3 ry = -axisX * s + axisY * c;
            axisX = rx.normalized;
            axisY = ry.normalized;
        }

        #endregion
    }
}