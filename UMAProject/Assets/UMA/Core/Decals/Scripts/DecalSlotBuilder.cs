using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// Builds a runtime SlotDataAsset representing a decal extracted from existing combined UMA skinned meshes.
    /// Includes (optionally) copying all frames of all contributing blendshapes, restricted to included vertices.
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

            // Gather baked meshes
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

            int vCount = output.Vertices.Count;
            md.vertices = output.LocalVertices.ToArray();
            md.normals = output.LocalNormals.ToArray();

            // Tangents (cheap orthogonal)
            md.tangents = new Vector4[vCount];
            for (int i = 0; i < vCount; i++)
            {
                var n = md.normals[i];
                Vector3 t = Vector3.Cross(Vector3.up, n);
                if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(Vector3.right, n);
                t.Normalize();
                md.tangents[i] = new Vector4(t.x, t.y, t.z, 1f);
            }

            md.uv = output.UVs.ToArray();
            md.subMeshCount = 1;
            md.submeshes = new SubMeshTriangles[1];
            var sub = new SubMeshTriangles();
            sub.SetTriangles(output.Triangles.ToArray());
            md.submeshes[0] = sub;

            // Bones
            md.umaBones = output.BoneList.ToArray();
            md.umaBoneCount = md.umaBones.Length;
            md.bindPoses = output.BindPoses.ToArray();
            md.boneNameHashes = new int[md.umaBoneCount];
            for (int i = 0; i < md.umaBoneCount; i++)
            {
                md.boneNameHashes[i] = UMAUtils.StringToHash(md.umaBones[i].name);
            }

            // Managed bone weights
            int vertexCount = vCount;
            var managedBonesPerVertex = new byte[vertexCount];
            List<BoneWeight1> managedWeights = new List<BoneWeight1>();
            for (int i = 0; i < vertexCount; i++)
            {
                var list = output.PerVertexWeights[i];
                int count = list.Count;
                if (count > 255) count = 255;
                managedBonesPerVertex[i] = (byte)count;
                for (int j = 0; j < count; j++)
                    managedWeights.Add(list[j]);
            }
            md.ManagedBonesPerVertex = managedBonesPerVertex;
            md.ManagedBoneWeights = managedWeights.ToArray();
            md.vertexCount = vertexCount;

            // Blendshapes (optional)
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
            public List<Vector3> Vertices = new List<Vector3>();          // world positions
            public List<Vector3> LocalVertices = new List<Vector3>();     // stored
            public List<Vector3> LocalNormals = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>();
            public List<int> Triangles = new List<int>();

            // Bones
            public List<UMATransform> BoneList = new List<UMATransform>();
            public List<Matrix4x4> BindPoses = new List<Matrix4x4>();
            public Dictionary<UMATransform, int> BoneIndexMap = new Dictionary<UMATransform, int>();
            public List<List<BoneWeight1>> PerVertexWeights = new List<List<BoneWeight1>>();

            // Blendshape source mapping
            public List<SlotData> VertexSourceSlots = new List<SlotData>();
            public List<int> VertexSourceLocalIndex = new List<int>();
        }

        #endregion

        #region Processing

        private static WorkerOutput Process(WorkerInput input)
        {
            var output = new WorkerOutput();
            BuildProjectionAxes(input.HitNormal, input.AngleDeg, out var axisX, out var axisY);
            float radiusSqr = input.Radius * input.Radius;

            var vertexMap = new Dictionary<long, int>(); // (renderer<<32)|bakedIndex
            var slotBoneMapCache = new Dictionary<SlotData, int[]>();

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

                    int nv0 = AddVertex(rend, i0, r, w0, input, axisX, axisY, output, vertexMap, slotRanges, slotBoneMapCache);
                    int nv1 = AddVertex(rend, i1, r, w1, input, axisX, axisY, output, vertexMap, slotRanges, slotBoneMapCache);
                    int nv2 = AddVertex(rend, i2, r, w2, input, axisX, axisY, output, vertexMap, slotRanges, slotBoneMapCache);

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
            int rendererIndex,
            Vector3 worldPos,
            WorkerInput input,
            Vector3 axisX,
            Vector3 axisY,
            WorkerOutput output,
            Dictionary<long, int> vertexMap,
            List<(SlotData slot, int start, int end)> slotRanges,
            Dictionary<SlotData, int[]> slotBoneMapCache)
        {
            long key = (((long)rendererIndex) << 32) | (uint)bakedIndex;
            if (vertexMap.TryGetValue(key, out int existing))
                return existing;

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

            Vector3 offset = worldPos - input.HitPoint;
            float u = (Vector3.Dot(offset, axisX) / input.Radius) * 0.5f + 0.5f;
            float v = (Vector3.Dot(offset, axisY) / input.Radius) * 0.5f + 0.5f;

            Vector3 localPos = worldPos;
            Vector3 localNormal = rend.Renderer.transform.TransformDirection(rend.Normals[bakedIndex]).normalized;

            int newIndex = output.Vertices.Count;
            vertexMap[key] = newIndex;

            output.Vertices.Add(worldPos);
            output.LocalVertices.Add(localPos);
            output.LocalNormals.Add(localNormal);
            output.UVs.Add(new Vector2(u, v));
            output.PerVertexWeights.Add(new List<BoneWeight1>());

            if (input.CaptureBlendshapeSourceInfo)
            {
                output.VertexSourceSlots.Add(owner);
                output.VertexSourceLocalIndex.Add(localVertexIndex);
            }

            if (owner == null || owner.asset?.meshData == null)
                return newIndex;

            var srcMD = owner.asset.meshData;
            if (srcMD.ManagedBonesPerVertex == null ||
                localVertexIndex < 0 ||
                localVertexIndex >= srcMD.ManagedBonesPerVertex.Length)
                return newIndex;

            if (!slotBoneMapCache.TryGetValue(owner, out var mapping))
            {
                mapping = BuildSlotBoneMap(srcMD, output);
                slotBoneMapCache[owner] = mapping;
            }

            int count = srcMD.ManagedBonesPerVertex[localVertexIndex];
            int bwStart = srcMD.BoneWeightOffset(localVertexIndex);
            for (int i = 0; i < count; i++)
            {
                var bw1 = srcMD.ManagedBoneWeights[bwStart + i];
                int srcBoneIndex = bw1.boneIndex;
                if (srcBoneIndex < 0 || srcBoneIndex >= mapping.Length) continue;
                int newBoneIndex = mapping[srcBoneIndex];
                if (newBoneIndex < 0) continue;

                output.PerVertexWeights[newIndex].Add(new BoneWeight1
                {
                    boneIndex = newBoneIndex,
                    weight = bw1.weight
                });
            }

            return newIndex;
        }

        private static int[] BuildSlotBoneMap(UMAMeshData srcMD, WorkerOutput output)
        {
            int boneCount = srcMD.umaBones.Length;
            int[] map = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
            {
                var umaBone = srcMD.umaBones[i];
                if (!output.BoneIndexMap.TryGetValue(umaBone, out int idx))
                {
                    idx = output.BoneList.Count;
                    output.BoneList.Add(umaBone);
                    Matrix4x4 bindPose = (srcMD.bindPoses != null && i < srcMD.bindPoses.Length)
                        ? srcMD.bindPoses[i]
                        : Matrix4x4.identity;
                    output.BindPoses.Add(bindPose);
                    output.BoneIndexMap.Add(umaBone, idx);
                }
                map[i] = idx;
            }
            return map;
        }

        private static void BuildProjectionAxes(Vector3 normal, float angleDeg, out Vector3 axisX, out Vector3 axisY)
        {
            var up = Math.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up;
            axisX = Vector3.Cross(up, normal).normalized;
            axisY = Vector3.Cross(normal, axisX);
            float rad = -angleDeg * Mathf.Deg2Rad; // clockwise
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector3 rx = axisX * c + axisY * s;
            Vector3 ry = -axisX * s + axisY * c;
            axisX = rx.normalized;
            axisY = ry.normalized;
        }

        #endregion

        #region Blendshape Construction (Multi-frame)

        private static void BuildBlendshapes(UMAMeshData md, WorkerOutput output)
        {
            if (output.VertexSourceSlots.Count != md.vertexCount ||
                output.VertexSourceLocalIndex.Count != md.vertexCount)
            {
                return;
            }

            // Accumulator per blendshape name
            // We preserve the frame structure (count & frameWeights) from the FIRST contributing slot that has that shape name.
            // Subsequent slots with the same shape name:
            //   - If frame count & frame weights match: merge (overwrite per-vertex deltas where provided).
            //   - Else: ignore (shape incompatibility).
            var accumulators = new Dictionary<string, BlendshapeAccum>();

            int vCount = md.vertexCount;

            for (int v = 0; v < vCount; v++)
            {
                var slot = output.VertexSourceSlots[v];
                int localIndex = output.VertexSourceLocalIndex[v];
                if (slot == null || localIndex < 0) continue;

                var srcMD = slot.asset.meshData;
                var srcShapes = srcMD.blendShapes;
                if (srcShapes == null || srcShapes.Length == 0) continue;

                for (int s = 0; s < srcShapes.Length; s++)
                {
                    var srcShape = srcShapes[s];
                    if (srcShape == null || string.IsNullOrEmpty(srcShape.shapeName) || srcShape.frames == null) continue;

                    if (!accumulators.TryGetValue(srcShape.shapeName, out var acc))
                    {
                        // Initialize accumulator
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
                                deltaVertices = new Vector3[vCount],
                                deltaNormals = hasNormals ? new Vector3[vCount] : null,
                                deltaTangents = hasTangents ? new Vector3[vCount] : null,
                                hasNormals = hasNormals,
                                hasTangents = hasTangents
                            });
                        }
                        accumulators.Add(srcShape.shapeName, acc);
                    }
                    else
                    {
                        // Validate frame structure matches
                        if (acc.frames.Count != srcShape.frames.Length)
                        {
                            // Incompatible frame count; skip this slot's contribution for this shape
                            continue;
                        }
                        bool frameMismatch = false;
                        for (int f = 0; f < acc.frames.Count; f++)
                        {
                            if (Math.Abs(acc.frameWeights[f] - srcShape.frames[f].frameWeight) > 0.0001f)
                            {
                                frameMismatch = true;
                                break;
                            }
                        }
                        if (frameMismatch) continue;
                    }

                    // Merge deltas for this vertex
                    for (int f = 0; f < srcShape.frames.Length; f++)
                    {
                        var srcFrame = srcShape.frames[f];
                        var dstFrame = acc.frames[f];

                        // Safety length checks
                        if (srcFrame.deltaVertices != null && localIndex < srcFrame.deltaVertices.Length)
                        {
                            dstFrame.deltaVertices[v] = srcFrame.deltaVertices[localIndex];
                        }
                        if (dstFrame.hasNormals && srcFrame.deltaNormals != null && localIndex < srcFrame.deltaNormals.Length)
                        {
                            dstFrame.deltaNormals[v] = srcFrame.deltaNormals[localIndex];
                        }
                        if (dstFrame.hasTangents && srcFrame.deltaTangents != null && localIndex < srcFrame.deltaTangents.Length)
                        {
                            dstFrame.deltaTangents[v] = srcFrame.deltaTangents[localIndex];
                        }
                    }
                }
            }

            if (accumulators.Count == 0)
                return;

            // Convert accumulators to UMABlendShape[]
            var newShapes = new UMABlendShape[accumulators.Count];
            int shapeIdx = 0;
            foreach (var kv in accumulators)
            {
                var acc = kv.Value;
                var newShape = new UMABlendShape();
                newShape.shapeName = acc.name;
                newShape.frames = new UMABlendFrame[acc.frames.Count];
                for (int f = 0; f < acc.frames.Count; f++)
                {
                    var fAcc = acc.frames[f];
                    var frame = new UMABlendFrame();
                    frame.frameWeight = fAcc.frameWeight;
                    frame.deltaVertices = fAcc.deltaVertices;
                    if (fAcc.hasNormals) frame.deltaNormals = fAcc.deltaNormals;
                    if (fAcc.hasTangents) frame.deltaTangents = fAcc.deltaTangents;
                    newShape.frames[f] = frame;
                }
                newShapes[shapeIdx++] = newShape;
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
    }
}