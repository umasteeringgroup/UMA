#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>
    /// Fits a mesh to a generated UMA character as a slot: captures the character's skin (bones, bind
    /// poses, weights and the posed surface), gives each vertex of the mesh the weights of the closest
    /// point of that surface, and builds the mesh in bind space. The Scene Mesh Slot Builder uses it,
    /// and so can any other tool that turns geometry into a slot.
    /// </summary>
    /// <remarks>
    /// Capture once and work from the captured data: <see cref="CaptureTargetData"/> only regenerates
    /// the character when asked to, so a tool that posed the character itself keeps that pose between
    /// capturing, fitting and sampling.
    /// </remarks>
    public static class UMASlotSkinTransfer
    {
        private const float Epsilon = 0.000001f;
        private const int MaxTransferredInfluences = 12;

        /// <summary>
        /// Captures the generated character's skin: its renderers, bones, bind poses and bone weights,
        /// as they are now.
        /// </summary>
        /// <param name="avatar">A character that has been generated.</param>
        /// <param name="rendererIndex">Which of its renderers receives the slot.</param>
        /// <param name="regenerate">
        /// True generates the character again first, which also puts it back in the pose its generator
        /// gives it. Pass false when the caller built and posed the character itself: nothing about the
        /// character is changed, so the surface sampled afterwards is the one the caller is showing.
        /// </param>
        public static TargetData CaptureTargetData(DynamicCharacterAvatar avatar, int rendererIndex, bool regenerate = true)
        {
            if (avatar == null)
            {
                throw new InvalidOperationException("Choose a target DynamicCharacterAvatar.");
            }

            if (regenerate)
            {
                avatar.GenerateNow();
            }

            UMAData umaData = avatar.umaData;
            if (umaData == null)
            {
                throw new InvalidOperationException("The target DynamicCharacterAvatar has no UMAData after generation.");
            }

            SkinnedMeshRenderer[] renderers = GetLiveTargetRenderers(avatar);
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException("The target DynamicCharacterAvatar has no generated SkinnedMeshRenderer after generation.");
            }

            rendererIndex = Mathf.Clamp(rendererIndex, 0, renderers.Length - 1);
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Mesh sourceMesh = renderer.sharedMesh;
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
            {
                throw new InvalidOperationException("The selected target renderer has no valid shared mesh.");
            }

            Transform[] bones = renderer.bones;
            if (bones == null || bones.Length == 0)
            {
                throw new InvalidOperationException("The selected target renderer has no bones.");
            }

            Matrix4x4[] bindPoses = sourceMesh.bindposes;
            if (bindPoses == null || bindPoses.Length != bones.Length)
            {
                throw new InvalidOperationException("The target renderer bindpose count does not match its bone count.");
            }

            byte[] bonesPerVertex = sourceMesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] boneWeights = sourceMesh.GetAllBoneWeights().ToArray();
            if (bonesPerVertex == null || bonesPerVertex.Length != sourceMesh.vertexCount || boneWeights == null || boneWeights.Length == 0)
            {
                throw new InvalidOperationException("The target renderer mesh does not expose valid bone weights.");
            }

            UMASkeleton skeleton = umaData.skeleton;
            Transform rootBone = renderer.rootBone;
            if (rootBone == null && skeleton != null)
            {
                rootBone = skeleton.GetGlobalTransform();
            }
            if (rootBone == null)
            {
                rootBone = umaData.GetGlobalTransform();
            }
            if (rootBone == null)
            {
                throw new InvalidOperationException("The target renderer does not expose a usable root bone.");
            }

            int[] boneNameHashes = new int[bones.Length];
            for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
            {
                if (bones[boneIndex] == null)
                {
                    throw new InvalidOperationException($"Target renderer bone {boneIndex} is null.");
                }

                boneNameHashes[boneIndex] = UMAUtils.StringToHash(bones[boneIndex].name);
            }

            RaceData raceData = null;
            if (avatar.activeRace != null)
            {
                raceData = avatar.activeRace.data != null ? avatar.activeRace.data : avatar.activeRace.racedata;
            }

            return new TargetData
            {
                Avatar = avatar,
                UmaData = umaData,
                RaceData = raceData,
                Renderer = renderer,
                SurfaceRenderers = renderers,
                SourceMesh = sourceMesh,
                Bones = bones,
                BindPoses = bindPoses,
                BoneNameHashes = boneNameHashes,
                BonesPerVertex = bonesPerVertex,
                BoneWeights = boneWeights,
                RootBone = rootBone,
                RootBoneName = rootBone.name,
                RootBoneHash = UMAUtils.StringToHash(rootBone.name)
            };
        }

        public static TargetSurface BuildTargetSurface(TargetData target)
        {
            List<TriangleInfo> triangles = new List<TriangleInfo>();
            List<List<BoneWeight1>> vertexWeights = new List<List<BoneWeight1>>();
            Matrix4x4 targetWorldToLocal = target.Renderer.transform.worldToLocalMatrix;
            SkinnedMeshRenderer[] renderers = target.SurfaceRenderers != null && target.SurfaceRenderers.Length > 0
                ? target.SurfaceRenderers
                : new[] { target.Renderer };
            int sampledRendererCount = 0;

            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer renderer = renderers[rendererIndex];
                Mesh sourceMesh = renderer != null ? renderer.sharedMesh : null;
                if (renderer == null || sourceMesh == null || sourceMesh.vertexCount == 0)
                {
                    continue;
                }

                int[] boneIndexRemap = BuildTargetBoneIndexRemap(target, renderer);
                Mesh bakedMesh = null;
                try
                {
                    bakedMesh = new Mesh { name = sourceMesh.name + "_BakedForSceneSlot" };
                    renderer.BakeMesh(bakedMesh);
                    if (bakedMesh.vertexCount != sourceMesh.vertexCount)
                    {
                        throw new InvalidOperationException($"BakeMesh changed the target renderer '{renderer.name}' vertex count. The slot builder requires stable target vertex order.");
                    }

                    int vertexBase = vertexWeights.Count;
                    AppendTargetVertexWeights(vertexWeights, sourceMesh, boneIndexRemap);

                    Vector3[] bakedVertices = bakedMesh.vertices;
                    Matrix4x4 rendererLocalToTargetLocal = targetWorldToLocal * renderer.transform.localToWorldMatrix;
                    bool rendererContributedTriangles = AppendUnityMeshTriangles(target, rendererIndex, renderer.name, sourceMesh, bakedVertices, rendererLocalToTargetLocal, vertexBase, triangles);

                    if (rendererContributedTriangles)
                    {
                        sampledRendererCount++;
                    }
                }
                finally
                {
                    if (bakedMesh != null)
                    {
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                    }
                }
            }

            if (triangles.Count == 0)
            {
                throw new InvalidOperationException("The target UMA renderers have no triangle topology to sample for closest-face weighting.");
            }

            Matrix4x4[] skinMatrices = new Matrix4x4[target.Bones.Length];
            Matrix4x4 rendererWorldToLocal = target.Renderer.transform.worldToLocalMatrix;
            for (int boneIndex = 0; boneIndex < target.Bones.Length; boneIndex++)
            {
                skinMatrices[boneIndex] = rendererWorldToLocal * target.Bones[boneIndex].localToWorldMatrix * target.BindPoses[boneIndex];
            }

            target.CurrentSkinMatrices = skinMatrices;

            return new TargetSurface
            {
                Triangles = triangles.ToArray(),
                VertexWeights = vertexWeights.ToArray(),
                CurrentSkinMatrices = skinMatrices,
                SampledRendererCount = sampledRendererCount
            };
        }

        public static bool AppendUnityMeshTriangles(TargetData target, int rendererIndex, string rendererName, Mesh sourceMesh, Vector3[] bakedVertices, Matrix4x4 rendererLocalToTargetLocal, int vertexBase, List<TriangleInfo> triangles)
        {
            bool contributed = false;
            for (int submeshIndex = 0; submeshIndex < sourceMesh.subMeshCount; submeshIndex++)
            {
                if (sourceMesh.GetTopology(submeshIndex) != MeshTopology.Triangles)
                {
                    continue;
                }

                int[] sourceTriangles = sourceMesh.GetTriangles(submeshIndex);
                for (int triangleOffset = 0; triangleOffset + 2 < sourceTriangles.Length; triangleOffset += 3)
                {
                    int index0 = sourceTriangles[triangleOffset];
                    int index1 = sourceTriangles[triangleOffset + 1];
                    int index2 = sourceTriangles[triangleOffset + 2];

                    Vector3 a = rendererLocalToTargetLocal.MultiplyPoint3x4(bakedVertices[index0]);
                    Vector3 b = rendererLocalToTargetLocal.MultiplyPoint3x4(bakedVertices[index1]);
                    Vector3 c = rendererLocalToTargetLocal.MultiplyPoint3x4(bakedVertices[index2]);

                    ResolveBakedTriangleSlot(target, rendererIndex, index0, index1, index2, out int slotIndex, out string slotName);
                    triangles.Add(new TriangleInfo(rendererIndex, rendererName, slotIndex, slotName, submeshIndex, triangleOffset / 3, vertexBase + index0, vertexBase + index1, vertexBase + index2, a, b, c));
                    contributed = true;
                }
            }

            return contributed;
        }

        public static void ResolveBakedTriangleSlot(TargetData target, int rendererIndex, int index0, int index1, int index2, out int slotIndex, out string slotName)
        {
            int slot0 = ResolveBakedVertexSlot(target, rendererIndex, index0, out string name0);
            int slot1 = ResolveBakedVertexSlot(target, rendererIndex, index1, out string name1);
            int slot2 = ResolveBakedVertexSlot(target, rendererIndex, index2, out string name2);

            if (slot0 >= 0 && slot0 == slot1 && slot0 == slot2)
            {
                slotIndex = slot0;
                slotName = name0;
                return;
            }

            if (slot0 >= 0 && (slot0 == slot1 || slot0 == slot2))
            {
                slotIndex = slot0;
                slotName = name0 + " <mixed>";
                return;
            }

            if (slot1 >= 0 && slot1 == slot2)
            {
                slotIndex = slot1;
                slotName = name1 + " <mixed>";
                return;
            }

            if (slot0 >= 0)
            {
                slotIndex = slot0;
                slotName = name0 + " <mixed>";
                return;
            }

            if (slot1 >= 0)
            {
                slotIndex = slot1;
                slotName = name1 + " <mixed>";
                return;
            }

            if (slot2 >= 0)
            {
                slotIndex = slot2;
                slotName = name2 + " <mixed>";
                return;
            }

            slotIndex = -1;
            slotName = "<unity mesh>";
        }

        public static int ResolveBakedVertexSlot(TargetData target, int rendererIndex, int bakedVertexIndex, out string slotName)
        {
            slotName = null;
            SlotData[] slots = target.UmaData != null && target.UmaData.umaRecipe != null ? target.UmaData.umaRecipe.slotDataList : null;
            if (slots == null)
            {
                return -1;
            }

            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                SlotData slot = slots[slotIndex];
                SlotDataAsset slotAsset = slot != null ? slot.asset : null;
                UMAMeshData meshData = slotAsset != null ? slotAsset.meshData : null;
                if (slot == null || slotAsset == null || UMAMeshData.IsNullOrEmptyMeshData(meshData))
                {
                    continue;
                }

                if (slot.skinnedMeshRenderer != rendererIndex)
                {
                    continue;
                }

                int slotStart = slot.vertexOffset;
                int slotEnd = slotStart + meshData.vertexCount;
                if (bakedVertexIndex >= slotStart && bakedVertexIndex < slotEnd)
                {
                    slotName = slotAsset.slotName;
                    return slotIndex;
                }
            }

            return -1;
        }

        public static WeightTransferResult TransferWeights(Vector3[] sourceVertices, TargetSurface targetSurface, int fallbackBoneIndex)
        {
            byte[] bonesPerVertex = new byte[sourceVertices.Length];
            List<BoneWeight1> allWeights = new List<BoneWeight1>(sourceVertices.Length * 4);
            int fallbackVertices = 0;
            // A grid over the surface's triangles finds the same closest triangle without testing them all.
            ClosestTriangleGrid grid = targetSurface != null && targetSurface.Triangles != null && targetSurface.Triangles.Length > 256 && sourceVertices.Length > 16
                ? new ClosestTriangleGrid(targetSurface.Triangles)
                : null;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                List<BoneWeight1> vertexWeights;
                bool found = grid != null
                    ? grid.TryFind(sourceVertices[vertexIndex], out TriangleHit hit)
                    : TryFindClosestTriangle(sourceVertices[vertexIndex], targetSurface, out hit);
                if (found)
                {
                    vertexWeights = InterpolateTriangleWeights(hit, targetSurface.VertexWeights, fallbackBoneIndex);
                }
                else
                {
                    vertexWeights = CreateSingleWeight(fallbackBoneIndex);
                    fallbackVertices++;
                }

                bonesPerVertex[vertexIndex] = (byte)vertexWeights.Count;
                allWeights.AddRange(vertexWeights);
            }

            return new WeightTransferResult
            {
                BonesPerVertex = bonesPerVertex,
                BoneWeights = allWeights.ToArray(),
                FallbackVertices = fallbackVertices
            };
        }

        /// <summary>
        /// Weights a mesh from the captured character by one of the shared weighting methods
        /// (<see cref="Weighting.UMAWeightTransfer"/>: Robust Inpaint, Tight Surface, Volumetric, Exact Match, Auto).
        /// The source is in the target renderer's space, as for <see cref="BuildSlotMesh"/>. Auto puts a humanoid
        /// character in the standard test poses (and back) to compare the methods.
        /// </summary>
        /// <param name="fallbackBoneIndex">The bone for vertices no body is found for.</param>
        /// <param name="report">What each vertex got, and for Auto how each method scored.</param>
        public static WeightTransferResult TransferWeights(SourceGeometry source, TargetData target, TargetSurface targetSurface, Weighting.UMAWeightSettings settings, int fallbackBoneIndex, out Weighting.UMAWeightReport report)
        {
            settings = (settings ?? new Weighting.UMAWeightSettings()).Clone();
            Weighting.UMAWeightDonor donor = Weighting.UMAWeightDonor.FromSurface(targetSurface, target, fallbackBoneIndex);
            Vector2[] uv = source.ChannelSourceMesh != null ? source.ChannelSourceMesh.uv : null;
            if (uv == null || uv.Length != source.VertexCount) uv = null;
            bool tryExact = settings.Method == Weighting.UMAWeightMethod.ExactMatch || settings.Method == Weighting.UMAWeightMethod.Auto;
            if (tryExact && settings.ExactUseUV && uv != null) donor.SetSlotUVs(target, targetSurface);
            var mesh = new Weighting.UMAWeightTarget
            {
                Positions = source.TargetLocalVertices,
                Triangles = source.Triangles,
                Normals = source.TargetLocalNormals != null && source.TargetLocalNormals.Length == source.VertexCount ? source.TargetLocalNormals : null,
                UVs = uv
            };
            if (settings.MirrorLeftRight && target.Avatar != null && target.Renderer != null) settings.SetMirrorFrom(target.Renderer.transform, target.Avatar.transform);
            if (settings.Poses == null && settings.Method == Weighting.UMAWeightMethod.Auto && Weighting.UMAWeightPoses.CanPose(target))
            {
                settings.Poses = Weighting.UMAWeightPoses.Capture(target, donor, null, out _);
            }
            Weighting.UMAWeightResult result = Weighting.UMAWeightTransfer.Transfer(donor, mesh, settings);
            report = result.Report;
            return result.ToTransferResult();
        }

        /// <summary>Where a renderer's vertices start in a captured surface (as <see cref="BuildTargetSurface"/> numbers them), or -1.</summary>
        public static int RendererVertexBase(TargetData target, SkinnedMeshRenderer renderer)
        {
            SkinnedMeshRenderer[] renderers = target.SurfaceRenderers != null && target.SurfaceRenderers.Length > 0 ? target.SurfaceRenderers : new[] { target.Renderer };
            int vertexBase = 0;
            foreach (SkinnedMeshRenderer r in renderers)
            {
                Mesh mesh = r != null ? r.sharedMesh : null;
                if (r == null || mesh == null || mesh.vertexCount == 0) continue;
                if (r == renderer) return vertexBase;
                vertexBase += mesh.vertexCount;
            }
            return -1;
        }

        public static WeightTransferResult BuildManualWeights(int vertexCount, int boneIndex)
        {
            byte[] bonesPerVertex = new byte[vertexCount];
            BoneWeight1[] weights = new BoneWeight1[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                bonesPerVertex[i] = 1;
                weights[i] = new BoneWeight1 { boneIndex = boneIndex, weight = 1f };
            }

            return new WeightTransferResult
            {
                BonesPerVertex = bonesPerVertex,
                BoneWeights = weights,
                FallbackVertices = 0
            };
        }

        public static bool TryFindClosestTriangle(Vector3 point, TargetSurface targetSurface, out TriangleHit hit)
        {
            hit = new TriangleHit { SqrDistance = float.MaxValue };
            if (targetSurface == null || targetSurface.Triangles == null || targetSurface.Triangles.Length == 0)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            TriangleInfo[] triangles = targetSurface.Triangles;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
            {
                TriangleInfo triangle = triangles[triangleIndex];
                Vector3 closestPoint = ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
                float sqrDistance = (closestPoint - point).sqrMagnitude;
                if (sqrDistance >= bestDistance)
                {
                    continue;
                }

                bestDistance = sqrDistance;
                hit = MakeHit(triangle, closestPoint, sqrDistance);
            }

            return bestDistance < float.MaxValue;
        }

        private static TriangleHit MakeHit(in TriangleInfo triangle, Vector3 closestPoint, float sqrDistance) => new TriangleHit
        {
            RendererIndex = triangle.RendererIndex,
            RendererName = triangle.RendererName,
            SlotIndex = triangle.SlotIndex,
            SlotName = triangle.SlotName,
            SubmeshIndex = triangle.SubmeshIndex,
            SubmeshTriangleIndex = triangle.SubmeshTriangleIndex,
            Index0 = triangle.Index0,
            Index1 = triangle.Index1,
            Index2 = triangle.Index2,
            A = triangle.A,
            B = triangle.B,
            C = triangle.C,
            VertexBlend = CalculateHitVertexBlend(closestPoint, triangle.A, triangle.B, triangle.C),
            ClosestPoint = closestPoint,
            SqrDistance = sqrDistance
        };

        /// <summary>
        /// The surface's triangles in a uniform grid, each in every cell its bounds touch. A search looks
        /// at the point's cell, then shells of cells around it, and stops once every triangle not yet
        /// tested must be farther than the best found: the answer is the same as testing them all (on a
        /// tie, the lower-numbered triangle, as the full search gives).
        /// </summary>
        public sealed class ClosestTriangleGrid
        {
            private readonly TriangleInfo[] triangles;
            private readonly Vector3 origin;
            private readonly float cell;
            private readonly int nx, ny, nz;
            private readonly int[] cellStart;
            private readonly int[] cellTriangles;
            private readonly int[] seen;
            private int stamp;

            public ClosestTriangleGrid(TriangleInfo[] triangles)
            {
                this.triangles = triangles;
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                double edges = 0;
                foreach (TriangleInfo t in triangles)
                {
                    min = Vector3.Min(min, Vector3.Min(t.A, Vector3.Min(t.B, t.C)));
                    max = Vector3.Max(max, Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
                    edges += (t.B - t.A).magnitude + (t.C - t.B).magnitude + (t.A - t.C).magnitude;
                }
                Vector3 size = max - min;
                // About two triangles across a cell, and not more than 96 cells along the longest side.
                float typical = (float)(edges / Mathf.Max(1, triangles.Length * 3));
                cell = Mathf.Max(typical * 2f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) / 96f, 1e-4f);
                origin = min;
                nx = Mathf.Max(1, Mathf.CeilToInt(size.x / cell) + 1);
                ny = Mathf.Max(1, Mathf.CeilToInt(size.y / cell) + 1);
                nz = Mathf.Max(1, Mathf.CeilToInt(size.z / cell) + 1);
                var counts = new int[nx * ny * nz + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    int[] write = pass == 1 ? (int[])counts.Clone() : null;
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        TriangleInfo t = triangles[i];
                        Vector3Int a = Cell(Vector3.Min(t.A, Vector3.Min(t.B, t.C))), b = Cell(Vector3.Max(t.A, Vector3.Max(t.B, t.C)));
                        for (int z = a.z; z <= b.z; z++)
                            for (int y = a.y; y <= b.y; y++)
                                for (int x = a.x; x <= b.x; x++)
                                {
                                    int c = x + nx * (y + ny * z);
                                    if (pass == 0) counts[c + 1]++;
                                    else cellTriangles[write[c]++] = i;
                                }
                    }
                    if (pass == 0)
                    {
                        for (int c = 0; c < counts.Length - 1; c++) counts[c + 1] += counts[c];
                        cellStart = counts;
                        cellTriangles = new int[counts[counts.Length - 1]];
                    }
                }
                seen = new int[triangles.Length];
            }

            private Vector3Int Cell(Vector3 p)
            {
                Vector3 q = (p - origin) / cell;
                return new Vector3Int(Mathf.Clamp(Mathf.FloorToInt(q.x), 0, nx - 1), Mathf.Clamp(Mathf.FloorToInt(q.y), 0, ny - 1), Mathf.Clamp(Mathf.FloorToInt(q.z), 0, nz - 1));
            }

            public bool TryFind(Vector3 point, out TriangleHit hit)
            {
                hit = new TriangleHit { SqrDistance = float.MaxValue };
                if (triangles.Length == 0) return false;
                if (++stamp == int.MaxValue) { System.Array.Clear(seen, 0, seen.Length); stamp = 1; }
                Vector3Int c = Cell(point);
                float best = float.MaxValue;
                int bestIndex = -1;
                Vector3 bestPoint = point;
                int maxRing = Mathf.Max(nx, Mathf.Max(ny, nz));
                for (int ring = 0; ring <= maxRing; ring++)
                {
                    for (int z = c.z - ring; z <= c.z + ring; z++)
                    {
                        if (z < 0 || z >= nz) continue;
                        for (int y = c.y - ring; y <= c.y + ring; y++)
                        {
                            if (y < 0 || y >= ny) continue;
                            for (int x = c.x - ring; x <= c.x + ring; x++)
                            {
                                if (x < 0 || x >= nx) continue;
                                // Only the shell: inner cells were searched in earlier rings.
                                if (Mathf.Abs(x - c.x) != ring && Mathf.Abs(y - c.y) != ring && Mathf.Abs(z - c.z) != ring) continue;
                                int cellIndex = x + nx * (y + ny * z);
                                for (int k = cellStart[cellIndex]; k < cellStart[cellIndex + 1]; k++)
                                {
                                    int i = cellTriangles[k];
                                    if (seen[i] == stamp) continue;
                                    seen[i] = stamp;
                                    Vector3 q = ClosestPointOnTriangle(point, triangles[i].A, triangles[i].B, triangles[i].C);
                                    float d = (q - point).sqrMagnitude;
                                    if (d < best || (d == best && i < bestIndex)) { best = d; bestIndex = i; bestPoint = q; }
                                }
                            }
                        }
                    }
                    // Any triangle not seen lies wholly outside these rings, at least ring cells away.
                    float reach = ring * cell;
                    if (bestIndex >= 0 && best <= reach * reach) break;
                }
                if (bestIndex < 0) return false;
                hit = MakeHit(triangles[bestIndex], bestPoint, best);
                return true;
            }
        }

        /// <summary>How <see cref="BuildSlotMesh"/> names the mesh and treats its normals and tangents.</summary>
        public sealed class SlotMeshOptions
        {
            /// <summary>The slot's name; the mesh is named after it.</summary>
            public string SlotName = "SceneMeshSlot";
            /// <summary>Leave the source's normals off the mesh.</summary>
            public bool ClearNormals;
            /// <summary>Leave the source's tangents off the mesh.</summary>
            public bool ClearTangents;
            /// <summary>Work normals out from the shape when the mesh has none. Battle buddies get one shared normal.</summary>
            public bool RecalculateMissingNormals = true;
            /// <summary>Work tangents out from the normals and UVs.</summary>
            public bool RecalculateTangents;
            /// <summary>
            /// Optional side id per vertex for edges marked sharp: buddies with different ids get a normal
            /// each (see <see cref="UMABuddyNormals.Sides"/>). Null for none.
            /// </summary>
            public int[] NormalSides;
        }

        /// <summary>
        /// Builds the slot's mesh in bind space from geometry given in the target renderer's space and
        /// the weights transferred to it. The caller owns the mesh and destroys it.
        /// </summary>
        public static Mesh BuildSlotMesh(SourceGeometry source, TargetData target, WeightTransferResult weights, SlotMeshOptions options = null)
        {
            options = options ?? new SlotMeshOptions();
            bool clearNormals = options.ClearNormals;
            bool clearTangents = options.ClearTangents;
            bool recalculateMissingNormals = options.RecalculateMissingNormals;
            bool recalculateTangents = options.RecalculateTangents;
            Vector3[] bindVertices = new Vector3[source.VertexCount];
            Vector3[] bindNormals = source.TargetLocalNormals != null ? new Vector3[source.VertexCount] : null;
            Vector4[] bindTangents = source.TargetLocalTangents != null ? new Vector4[source.VertexCount] : null;

            int weightOffset = 0;
            for (int vertexIndex = 0; vertexIndex < source.VertexCount; vertexIndex++)
            {
                int influenceCount = weights.BonesPerVertex[vertexIndex];
                Matrix4x4 blendedSkinMatrix = new Matrix4x4();
                for (int influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++)
                {
                    BoneWeight1 weight = weights.BoneWeights[weightOffset + influenceIndex];
                    if ((uint)weight.boneIndex >= target.Bones.Length)
                    {
                        throw new InvalidOperationException($"Vertex {vertexIndex} references invalid bone index {weight.boneIndex}.");
                    }

                    blendedSkinMatrix = AddWeightedMatrix(blendedSkinMatrix, target.CurrentSkinMatrices[weight.boneIndex], weight.weight);
                }

                if (Mathf.Abs(blendedSkinMatrix.determinant) <= Epsilon)
                {
                    throw new InvalidOperationException($"Vertex {vertexIndex} produced a singular skinning matrix. Try Skip reweight with a manual bone.");
                }

                Matrix4x4 unskinMatrix = blendedSkinMatrix.inverse;
                bindVertices[vertexIndex] = unskinMatrix.MultiplyPoint3x4(source.TargetLocalVertices[vertexIndex]);

                if (bindNormals != null)
                {
                    Matrix4x4 normalMatrix = unskinMatrix.inverse.transpose;
                    Vector3 normal = normalMatrix.MultiplyVector(source.TargetLocalNormals[vertexIndex]);
                    bindNormals[vertexIndex] = normal.sqrMagnitude > Epsilon ? normal.normalized : source.TargetLocalNormals[vertexIndex];
                }

                if (bindTangents != null)
                {
                    Vector4 sourceTangent = source.TargetLocalTangents[vertexIndex];
                    Vector3 tangent = new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z);
                    Vector3 bindTangent = unskinMatrix.MultiplyVector(tangent);
                    bindTangent = bindTangent.sqrMagnitude > Epsilon ? bindTangent.normalized : tangent;
                    bindTangents[vertexIndex] = new Vector4(bindTangent.x, bindTangent.y, bindTangent.z, sourceTangent.w);
                }

                weightOffset += influenceCount;
            }

            Mesh mesh = new Mesh
            {
                name = MakeSafeAssetName((options.SlotName ?? string.Empty).Trim()) + "_SceneSlotMesh",
                indexFormat = source.VertexCount > 65535 || source.ChannelSourceMesh == null ? (source.VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16) : source.ChannelSourceMesh.indexFormat
            };

            mesh.SetVertices(new List<Vector3>(bindVertices));
            if (bindNormals != null && !clearNormals)
            {
                mesh.SetNormals(new List<Vector3>(bindNormals));
            }
            if (bindTangents != null && !clearTangents)
            {
                mesh.SetTangents(new List<Vector4>(bindTangents));
            }

            // Geometry that was not read from a mesh has no other channels to copy; its maker sets them.
            if (source.ChannelSourceMesh != null)
            {
                CopyStaticVertexChannels(source.ChannelSourceMesh, mesh, source.VertexCount);
            }
            mesh.subMeshCount = 1;
            mesh.SetTriangles(source.Triangles, 0, true);

            if ((bindNormals == null || clearNormals) && recalculateMissingNormals)
            {
                mesh.SetNormals(CalculateMissingNormals(source.TargetLocalVertices, bindVertices, source.Triangles, options.NormalSides));
            }
            if (recalculateTangents && !clearTangents)
            {
                mesh.RecalculateTangents();
            }

            var nativeBonesPerVertex = new NativeArray<byte>(weights.BonesPerVertex, Allocator.Temp);
            var nativeBoneWeights = new NativeArray<BoneWeight1>(weights.BoneWeights, Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights);
            }
            finally
            {
                nativeBonesPerVertex.Dispose();
                nativeBoneWeights.Dispose();
            }

            mesh.bindposes = (Matrix4x4[])target.BindPoses.Clone();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Normals for a slot mesh whose source has none, worked out from its shape. Battle buddies
        /// (the copies of a vertex along a UV island edge) get one shared normal, so the light does
        /// not break along the seam as it would with <c>Mesh.RecalculateNormals</c>.
        /// </summary>
        /// <param name="sourceVertices">The source positions, which decide which vertices are buddies.</param>
        /// <param name="bindVertices">The shape the normals are for: the same vertices in bind space.</param>
        /// <param name="triangles">Three indices for each triangle.</param>
        /// <param name="sides">Optional side id per vertex: buddies on different sides of a sharp edge get a normal each.</param>
        public static Vector3[] CalculateMissingNormals(Vector3[] sourceVertices, Vector3[] bindVertices, int[] triangles, int[] sides = null)
        {
            var normals = new Vector3[bindVertices.Length];
            Vector3[] buddyPositions = sourceVertices != null && sourceVertices.Length == bindVertices.Length ? sourceVertices : bindVertices;
            new UMABuddyNormals(buddyPositions, triangles) { Sides = sides }.Recalculate(bindVertices, normals);
            return normals;
        }

        public static int[] BuildTargetBoneIndexRemap(TargetData target, SkinnedMeshRenderer renderer)
        {
            Transform[] sourceBones = renderer.bones;
            int[] remap = new int[sourceBones.Length];
            for (int sourceBoneIndex = 0; sourceBoneIndex < sourceBones.Length; sourceBoneIndex++)
            {
                remap[sourceBoneIndex] = -1;
                Transform sourceBone = sourceBones[sourceBoneIndex];
                if (sourceBone == null)
                {
                    continue;
                }

                for (int targetBoneIndex = 0; targetBoneIndex < target.Bones.Length; targetBoneIndex++)
                {
                    Transform targetBone = target.Bones[targetBoneIndex];
                    if (targetBone == sourceBone || (targetBone != null && targetBone.name == sourceBone.name))
                    {
                        remap[sourceBoneIndex] = targetBoneIndex;
                        break;
                    }
                }
            }

            return remap;
        }

        public static void AppendTargetVertexWeights(List<List<BoneWeight1>> vertexWeights, Mesh sourceMesh, int[] boneIndexRemap)
        {
            byte[] bonesPerVertex = sourceMesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] boneWeights = sourceMesh.GetAllBoneWeights().ToArray();
            if (bonesPerVertex == null || bonesPerVertex.Length != sourceMesh.vertexCount || boneWeights == null || boneWeights.Length == 0)
            {
                throw new InvalidOperationException($"Target renderer mesh '{sourceMesh.name}' does not expose valid bone weights.");
            }

            int weightOffset = 0;
            for (int vertexIndex = 0; vertexIndex < sourceMesh.vertexCount; vertexIndex++)
            {
                int influenceCount = bonesPerVertex[vertexIndex];
                List<BoneWeight1> weights = new List<BoneWeight1>(influenceCount);
                for (int influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++, weightOffset++)
                {
                    BoneWeight1 weight = boneWeights[weightOffset];
                    if (weight.weight <= Epsilon || (uint)weight.boneIndex >= boneIndexRemap.Length)
                    {
                        continue;
                    }

                    int remappedBoneIndex = boneIndexRemap[weight.boneIndex];
                    if (remappedBoneIndex < 0)
                    {
                        continue;
                    }

                    weights.Add(new BoneWeight1 { boneIndex = remappedBoneIndex, weight = weight.weight });
                }
                NormalizeWeights(weights);
                vertexWeights.Add(weights);
            }

            if (weightOffset != boneWeights.Length)
            {
                throw new InvalidOperationException($"Target renderer mesh '{sourceMesh.name}' bone weight streams are malformed.");
            }
        }

        public static List<BoneWeight1> InterpolateTriangleWeights(TriangleHit hit, List<BoneWeight1>[] vertexWeights, int fallbackBoneIndex)
        {
            Vector3 vertexBlend = CalculateHitVertexBlend(hit.ClosestPoint, hit.A, hit.B, hit.C);
            Dictionary<int, float> merged = new Dictionary<int, float>();
            AddWeightedInfluences(merged, vertexWeights[hit.Index0], vertexBlend.x);
            AddWeightedInfluences(merged, vertexWeights[hit.Index1], vertexBlend.y);
            AddWeightedInfluences(merged, vertexWeights[hit.Index2], vertexBlend.z);

            List<BoneWeight1> result = new List<BoneWeight1>(merged.Count);
            float sum = 0f;
            foreach (var item in merged)
            {
                if (item.Value > Epsilon)
                {
                    result.Add(new BoneWeight1 { boneIndex = item.Key, weight = item.Value });
                    sum += item.Value;
                }
            }

            if (result.Count == 0 || sum <= Epsilon)
            {
                return CreateSingleWeight(fallbackBoneIndex);
            }

            result.Sort((left, right) => right.weight.CompareTo(left.weight));
            if (result.Count > MaxTransferredInfluences)
            {
                result.RemoveRange(MaxTransferredInfluences, result.Count - MaxTransferredInfluences);
            }

            NormalizeWeights(result);
            return result;
        }

        public static void AddWeightedInfluences(Dictionary<int, float> merged, List<BoneWeight1> weights, float multiplier)
        {
            if (weights == null || multiplier <= Epsilon)
            {
                return;
            }

            for (int i = 0; i < weights.Count; i++)
            {
                BoneWeight1 weight = weights[i];
                float value = weight.weight * multiplier;
                if (value <= Epsilon)
                {
                    continue;
                }

                if (merged.TryGetValue(weight.boneIndex, out float current))
                {
                    merged[weight.boneIndex] = current + value;
                }
                else
                {
                    merged.Add(weight.boneIndex, value);
                }
            }
        }

        public static void NormalizeWeights(List<BoneWeight1> weights)
        {
            float sum = 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                sum += weights[i].weight;
            }

            if (sum <= Epsilon)
            {
                return;
            }

            for (int i = 0; i < weights.Count; i++)
            {
                BoneWeight1 weight = weights[i];
                weight.weight /= sum;
                weights[i] = weight;
            }
        }

        public static List<BoneWeight1> CreateSingleWeight(int boneIndex)
        {
            return new List<BoneWeight1>
            {
                new BoneWeight1 { boneIndex = boneIndex, weight = 1f }
            };
        }

        public static GameObject CreateTemporaryRenderer(SkinnedMeshRenderer targetRenderer)
        {
            GameObject tempObject = new GameObject(targetRenderer.name + "_SceneSlotCapture");
            tempObject.hideFlags = HideFlags.HideAndDontSave;

            Transform tempTransform = tempObject.transform;
            Transform targetTransform = targetRenderer.transform;
            if (targetTransform.parent != null)
            {
                tempTransform.SetParent(targetTransform.parent, false);
            }
            tempTransform.localPosition = targetTransform.localPosition;
            tempTransform.localRotation = targetTransform.localRotation;
            tempTransform.localScale = targetTransform.localScale;

            SkinnedMeshRenderer tempRenderer = tempObject.AddComponent<SkinnedMeshRenderer>();
            tempRenderer.hideFlags = HideFlags.HideAndDontSave;
            return tempObject;
        }

        public static void CopyStaticVertexChannels(Mesh sourceMesh, Mesh destinationMesh, int vertexCount)
        {
            List<Color32> colors = new List<Color32>(vertexCount);
            sourceMesh.GetColors(colors);
            if (colors.Count == vertexCount)
            {
                destinationMesh.SetColors(colors);
            }

            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
            {
                List<Vector4> uvs = new List<Vector4>(vertexCount);
                sourceMesh.GetUVs(uvChannel, uvs);
                if (uvs.Count == vertexCount)
                {
                    destinationMesh.SetUVs(uvChannel, uvs);
                }
            }
        }

        public static Matrix4x4 AddWeightedMatrix(Matrix4x4 accumulator, Matrix4x4 matrix, float weight)
        {
            accumulator.m00 += matrix.m00 * weight;
            accumulator.m01 += matrix.m01 * weight;
            accumulator.m02 += matrix.m02 * weight;
            accumulator.m03 += matrix.m03 * weight;
            accumulator.m10 += matrix.m10 * weight;
            accumulator.m11 += matrix.m11 * weight;
            accumulator.m12 += matrix.m12 * weight;
            accumulator.m13 += matrix.m13 * weight;
            accumulator.m20 += matrix.m20 * weight;
            accumulator.m21 += matrix.m21 * weight;
            accumulator.m22 += matrix.m22 * weight;
            accumulator.m23 += matrix.m23 * weight;
            accumulator.m30 += matrix.m30 * weight;
            accumulator.m31 += matrix.m31 * weight;
            accumulator.m32 += matrix.m32 * weight;
            accumulator.m33 += matrix.m33 * weight;
            return accumulator;
        }

        public static SkinnedMeshRenderer[] GetLiveTargetRenderers(DynamicCharacterAvatar avatar)
        {
            if (avatar == null || avatar.umaData == null)
            {
                return Array.Empty<SkinnedMeshRenderer>();
            }

            SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                return Array.Empty<SkinnedMeshRenderer>();
            }

            List<SkinnedMeshRenderer> liveRenderers = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMesh != null)
                {
                    liveRenderers.Add(renderers[i]);
                }
            }

            return liveRenderers.ToArray();
        }

        public static string MakeSafeAssetName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "SceneMeshSlot";
            }

            string fileName = value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalidChars.Length; i++)
            {
                fileName = fileName.Replace(invalidChars[i], '_');
            }

            return fileName.Replace(' ', '_');
        }

        public static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).sqrMagnitude <= Epsilon)
            {
                return ClosestPointOnDegenerateTriangle(point, a, b, c);
            }

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                return a;
            }

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + v * ab;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + w * (c - b);
            }

            float denominator = 1f / (va + vb + vc);
            float vInside = vb * denominator;
            float wInside = vc * denominator;
            return a + ab * vInside + ac * wInside;
        }

        public static Vector3 ClosestPointOnDegenerateTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 bestPoint = a;
            float bestDistance = (point - a).sqrMagnitude;

            Vector3 candidate = ClosestPointOnSegment(point, a, b);
            float candidateDistance = (point - candidate).sqrMagnitude;
            if (candidateDistance < bestDistance)
            {
                bestPoint = candidate;
                bestDistance = candidateDistance;
            }

            candidate = ClosestPointOnSegment(point, a, c);
            candidateDistance = (point - candidate).sqrMagnitude;
            if (candidateDistance < bestDistance)
            {
                bestPoint = candidate;
                bestDistance = candidateDistance;
            }

            candidate = ClosestPointOnSegment(point, b, c);
            candidateDistance = (point - candidate).sqrMagnitude;
            if (candidateDistance < bestDistance)
            {
                bestPoint = candidate;
            }

            return bestPoint;
        }

        public static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector3 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= Epsilon)
            {
                return start;
            }

            float t = Vector3.Dot(point - start, segment) / lengthSqr;
            t = Mathf.Clamp01(t);
            return start + segment * t;
        }

        public static Vector3 CalculateHitVertexBlend(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            float distanceA = Vector3.Distance(point, a);
            float distanceB = Vector3.Distance(point, b);
            float distanceC = Vector3.Distance(point, c);

            bool onA = distanceA <= Epsilon;
            bool onB = distanceB <= Epsilon;
            bool onC = distanceC <= Epsilon;
            if (onA || onB || onC)
            {
                float aWeight = onA ? 1f : 0f;
                float bWeight = onB ? 1f : 0f;
                float cWeight = onC ? 1f : 0f;
                float sum = aWeight + bWeight + cWeight;
                if (sum <= Epsilon)
                {
                    return new Vector3(1f, 0f, 0f);
                }

                return new Vector3(aWeight / sum, bWeight / sum, cWeight / sum);
            }

            float inverseA = 1f / distanceA;
            float inverseB = 1f / distanceB;
            float inverseC = 1f / distanceC;
            float sumInverse = inverseA + inverseB + inverseC;
            if (sumInverse <= Epsilon)
            {
                return new Vector3(1f, 0f, 0f);
            }

            return new Vector3(inverseA / sumInverse, inverseB / sumInverse, inverseC / sumInverse);
        }

        public struct SourceGeometry
        {
            public Mesh ChannelSourceMesh;
            public Vector3[] TargetLocalVertices;
            public Vector3[] TargetLocalNormals;
            public Vector4[] TargetLocalTangents;
            public int[] Triangles;
            public int SourceSubmeshIndex;
            public int VertexCount => TargetLocalVertices != null ? TargetLocalVertices.Length : 0;
        }

        public sealed class TargetData
        {
            public DynamicCharacterAvatar Avatar;
            public UMAData UmaData;
            public RaceData RaceData;
            public SkinnedMeshRenderer Renderer;
            public SkinnedMeshRenderer[] SurfaceRenderers;
            public Mesh SourceMesh;
            public Transform[] Bones;
            public Matrix4x4[] BindPoses;
            public int[] BoneNameHashes;
            public byte[] BonesPerVertex;
            public BoneWeight1[] BoneWeights;
            public Transform RootBone;
            public string RootBoneName;
            public int RootBoneHash;
            public Matrix4x4[] CurrentSkinMatrices;
        }

        public sealed class TargetSurface
        {
            public TriangleInfo[] Triangles;
            public List<BoneWeight1>[] VertexWeights;
            public Matrix4x4[] CurrentSkinMatrices;
            public int SampledRendererCount;
        }

        public sealed class WeightTransferResult
        {
            public byte[] BonesPerVertex;
            public BoneWeight1[] BoneWeights;
            public int FallbackVertices;
        }

        public struct TriangleInfo
        {
            public readonly int RendererIndex;
            public readonly string RendererName;
            public readonly int SlotIndex;
            public readonly string SlotName;
            public readonly int SubmeshIndex;
            public readonly int SubmeshTriangleIndex;
            public readonly int Index0;
            public readonly int Index1;
            public readonly int Index2;
            public readonly Vector3 A;
            public readonly Vector3 B;
            public readonly Vector3 C;

            public TriangleInfo(int rendererIndex, string rendererName, int slotIndex, string slotName, int submeshIndex, int submeshTriangleIndex, int index0, int index1, int index2, Vector3 a, Vector3 b, Vector3 c)
            {
                RendererIndex = rendererIndex;
                RendererName = rendererName;
                SlotIndex = slotIndex;
                SlotName = slotName;
                SubmeshIndex = submeshIndex;
                SubmeshTriangleIndex = submeshTriangleIndex;
                Index0 = index0;
                Index1 = index1;
                Index2 = index2;
                A = a;
                B = b;
                C = c;
            }
        }

        public struct TriangleHit
        {
            public int RendererIndex;
            public string RendererName;
            public int SlotIndex;
            public string SlotName;
            public int SubmeshIndex;
            public int SubmeshTriangleIndex;
            public int Index0;
            public int Index1;
            public int Index2;
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public Vector3 VertexBlend;
            public Vector3 ClosestPoint;
            public float SqrDistance;
        }
    }
}

#endif
