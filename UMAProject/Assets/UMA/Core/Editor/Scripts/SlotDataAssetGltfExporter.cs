#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    internal static class SlotDataAssetGltfExporter
    {
        private const uint GltfMagic = 0x46546C67; // glTF
        private const uint GltfVersion = 2;
        private const uint ChunkTypeJson = 0x4E4F534A; // JSON
        private const uint ChunkTypeBin = 0x004E4942; // BIN
        private static readonly Matrix4x4 HandednessFlip = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        private class BufferView
        {
            public int buffer;
            public int byteOffset;
            public int byteLength;
            public int? target;
        }

        private class Accessor
        {
            public int bufferView;
            public int byteOffset;
            public int componentType;
            public int count;
            public string type;
            public float[] min;
            public float[] max;
        }

        public static void ExportSlotToGlb(SlotDataAsset slot, string outputPath, bool includeRig)
        {
            if (slot == null)
            {
                EditorUtility.DisplayDialog("Export glTF", "SlotDataAsset is null.", "OK");
                return;
            }

            var meshData = slot.meshData;
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData))
            {
                EditorUtility.DisplayDialog("Export glTF", "SlotDataAsset has no meshData.", "OK");
                return;
            }

            var vertices = meshData.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                EditorUtility.DisplayDialog("Export glTF", "Mesh has no vertices.", "OK");
                return;
            }

            var normals = meshData.normals;
            var uvs = meshData.uv;

            int vertexCount = vertices.Length;
            int subMeshIndex = 0;
            if (slot.subMeshIndex > 0 && meshData.subMeshCount > slot.subMeshIndex)
            {
                subMeshIndex = slot.subMeshIndex;
            }

            var submesh = meshData.submeshes;
            if (submesh == null || submesh.Length == 0 || subMeshIndex >= submesh.Length)
            {
                EditorUtility.DisplayDialog("Export glTF", "Mesh has no submesh data.", "OK");
                return;
            }

            int[] triangles = submesh[subMeshIndex].GetBaseTriangles();
            if (triangles == null || triangles.Length == 0)
            {
                EditorUtility.DisplayDialog("Export glTF", "Mesh has no triangles.", "OK");
                return;
            }
            ReverseTriangleWinding(triangles);

            var boneNames = meshData.boneNameHashes;
            var bindPoses = meshData.bindPoses;
            var umaBones = meshData.umaBones;
            bool hasBones = includeRig && boneNames != null && bindPoses != null && boneNames.Length == bindPoses.Length && boneNames.Length > 0;

            Vector4[] jointData = null;
            Vector4[] weightData = null;
            if (hasBones)
            {
                BuildSkinningArrays(meshData, vertexCount, out jointData, out weightData);
            }

            var buffer = new List<byte>(1024 * 1024);
            var bufferViews = new List<BufferView>();
            var accessors = new List<Accessor>();

            int positionAccessor = AddVector3Accessor(vertices, buffer, bufferViews, accessors, 34962, true);
            int normalAccessor = -1;
            int uvAccessor = -1;
            int jointsAccessor = -1;
            int weightsAccessor = -1;

            if (normals != null && normals.Length == vertexCount)
            {
                normalAccessor = AddVector3Accessor(normals, buffer, bufferViews, accessors, 34962, true);
            }

            if (uvs != null && uvs.Length == vertexCount)
            {
                uvAccessor = AddVector2Accessor(uvs, buffer, bufferViews, accessors, 34962, true);
            }

            if (hasBones && jointData != null && weightData != null)
            {
                jointsAccessor = AddVector4UShortAccessor(jointData, buffer, bufferViews, accessors, 34962);
                weightsAccessor = AddVector4Accessor(weightData, buffer, bufferViews, accessors, 34962);
            }

            int indicesAccessor = AddIndicesAccessor(triangles, buffer, bufferViews, accessors);

            int inverseBindAccessor = -1;
            if (hasBones)
            {
                inverseBindAccessor = AddMatrix4x4Accessor(bindPoses, buffer, bufferViews, accessors, true);
            }

            var json = BuildGltfJson(
                slot,
                vertexCount,
                positionAccessor,
                normalAccessor,
                uvAccessor,
                jointsAccessor,
                weightsAccessor,
                indicesAccessor,
                buffer.Count,
                bufferViews,
                accessors,
                hasBones,
                boneNames,
                umaBones,
                inverseBindAccessor);

            WriteGlb(outputPath, json, buffer.ToArray());
        }

        private static void BuildSkinningArrays(UMAMeshData meshData, int vertexCount, out Vector4[] joints, out Vector4[] weights)
        {
            joints = new Vector4[vertexCount];
            weights = new Vector4[vertexCount];

            if (meshData.ManagedBonesPerVertex != null && meshData.ManagedBoneWeights != null && meshData.ManagedBonesPerVertex.Length == vertexCount)
            {
                int offset = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    int count = meshData.ManagedBonesPerVertex[i];
                    var entries = new List<BoneWeight1>(count);
                    for (int j = 0; j < count; j++)
                    {
                        entries.Add(meshData.ManagedBoneWeights[offset + j]);
                    }
                    offset += count;
                    FillWeightVectors(entries, out joints[i], out weights[i]);
                }
                return;
            }

            if (meshData.boneWeights != null && meshData.boneWeights.Length == vertexCount)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    var bw = meshData.boneWeights[i];
                    var entries = new List<BoneWeight1>(4)
                    {
                        new BoneWeight1 { boneIndex = bw.boneIndex0, weight = bw.weight0 },
                        new BoneWeight1 { boneIndex = bw.boneIndex1, weight = bw.weight1 },
                        new BoneWeight1 { boneIndex = bw.boneIndex2, weight = bw.weight2 },
                        new BoneWeight1 { boneIndex = bw.boneIndex3, weight = bw.weight3 }
                    };
                    FillWeightVectors(entries, out joints[i], out weights[i]);
                }
                return;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                joints[i] = Vector4.zero;
                weights[i] = new Vector4(1f, 0f, 0f, 0f);
            }
        }

        private static void FillWeightVectors(List<BoneWeight1> entries, out Vector4 joints, out Vector4 weights)
        {
            entries.Sort((a, b) => b.weight.CompareTo(a.weight));

            float w0 = entries.Count > 0 ? entries[0].weight : 0f;
            float w1 = entries.Count > 1 ? entries[1].weight : 0f;
            float w2 = entries.Count > 2 ? entries[2].weight : 0f;
            float w3 = entries.Count > 3 ? entries[3].weight : 0f;
            float total = w0 + w1 + w2 + w3;
            if (total <= 0f)
            {
                w0 = 1f;
                w1 = 0f;
                w2 = 0f;
                w3 = 0f;
                total = 1f;
            }

            w0 /= total;
            w1 /= total;
            w2 /= total;
            w3 /= total;

            int j0 = entries.Count > 0 ? entries[0].boneIndex : 0;
            int j1 = entries.Count > 1 ? entries[1].boneIndex : 0;
            int j2 = entries.Count > 2 ? entries[2].boneIndex : 0;
            int j3 = entries.Count > 3 ? entries[3].boneIndex : 0;

            if (j0 < 0) j0 = 0;
            if (j1 < 0) j1 = 0;
            if (j2 < 0) j2 = 0;
            if (j3 < 0) j3 = 0;

            joints = new Vector4(j0, j1, j2, j3);
            weights = new Vector4(w0, w1, w2, w3);
        }

        private static int AddVector3Accessor(Vector3[] values, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors, int target, bool convertHandedness)
        {
            int offset = AlignBuffer(buffer, 4);
            var min = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var max = new float[] { float.MinValue, float.MinValue, float.MinValue };

            for (int i = 0; i < values.Length; i++)
            {
                Vector3 v = convertHandedness ? ConvertPosition(values[i]) : values[i];
                WriteFloat(buffer, v.x);
                WriteFloat(buffer, v.y);
                WriteFloat(buffer, v.z);

                if (v.x < min[0]) min[0] = v.x;
                if (v.y < min[1]) min[1] = v.y;
                if (v.z < min[2]) min[2] = v.z;

                if (v.x > max[0]) max[0] = v.x;
                if (v.y > max[1]) max[1] = v.y;
                if (v.z > max[2]) max[2] = v.z;
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length, target = target });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor { bufferView = viewIndex, byteOffset = 0, componentType = 5126, count = values.Length, type = "VEC3", min = min, max = max });
            return accessorIndex;
        }

        private static int AddVector2Accessor(Vector2[] values, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors, int target, bool flipV)
        {
            int offset = AlignBuffer(buffer, 4);
            for (int i = 0; i < values.Length; i++)
            {
                Vector2 v = values[i];
                if (flipV)
                {
                    v.y = 1f - v.y;
                }
                WriteFloat(buffer, v.x);
                WriteFloat(buffer, v.y);
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length, target = target });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor { bufferView = viewIndex, byteOffset = 0, componentType = 5126, count = values.Length, type = "VEC2" });
            return accessorIndex;
        }

        private static int AddVector4Accessor(Vector4[] values, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors, int target)
        {
            int offset = AlignBuffer(buffer, 4);
            for (int i = 0; i < values.Length; i++)
            {
                Vector4 v = values[i];
                WriteFloat(buffer, v.x);
                WriteFloat(buffer, v.y);
                WriteFloat(buffer, v.z);
                WriteFloat(buffer, v.w);
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length, target = target });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor { bufferView = viewIndex, byteOffset = 0, componentType = 5126, count = values.Length, type = "VEC4" });
            return accessorIndex;
        }

        private static int AddVector4UShortAccessor(Vector4[] values, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors, int target)
        {
            int offset = AlignBuffer(buffer, 4);
            for (int i = 0; i < values.Length; i++)
            {
                Vector4 v = values[i];
                WriteUShort(buffer, (ushort)v.x);
                WriteUShort(buffer, (ushort)v.y);
                WriteUShort(buffer, (ushort)v.z);
                WriteUShort(buffer, (ushort)v.w);
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length, target = target });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor { bufferView = viewIndex, byteOffset = 0, componentType = 5123, count = values.Length, type = "VEC4" });
            return accessorIndex;
        }

        private static int AddMatrix4x4Accessor(Matrix4x4[] values, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors, bool convertHandedness)
        {
            int offset = AlignBuffer(buffer, 4);
            for (int i = 0; i < values.Length; i++)
            {
                Matrix4x4 m = convertHandedness ? ConvertMatrix(values[i]) : values[i];
                WriteFloat(buffer, m.m00); WriteFloat(buffer, m.m10); WriteFloat(buffer, m.m20); WriteFloat(buffer, m.m30);
                WriteFloat(buffer, m.m01); WriteFloat(buffer, m.m11); WriteFloat(buffer, m.m21); WriteFloat(buffer, m.m31);
                WriteFloat(buffer, m.m02); WriteFloat(buffer, m.m12); WriteFloat(buffer, m.m22); WriteFloat(buffer, m.m32);
                WriteFloat(buffer, m.m03); WriteFloat(buffer, m.m13); WriteFloat(buffer, m.m23); WriteFloat(buffer, m.m33);
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor { bufferView = viewIndex, byteOffset = 0, componentType = 5126, count = values.Length, type = "MAT4" });
            return accessorIndex;
        }

        private static int AddIndicesAccessor(int[] indices, List<byte> buffer, List<BufferView> bufferViews, List<Accessor> accessors)
        {
            int offset = AlignBuffer(buffer, 4);
            int max = 0;
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] > max) max = indices[i];
                WriteUInt(buffer, (uint)indices[i]);
            }

            int length = buffer.Count - offset;
            int viewIndex = bufferViews.Count;
            bufferViews.Add(new BufferView { buffer = 0, byteOffset = offset, byteLength = length, target = 34963 });

            int accessorIndex = accessors.Count;
            accessors.Add(new Accessor
            {
                bufferView = viewIndex,
                byteOffset = 0,
                componentType = 5125,
                count = indices.Length,
                type = "SCALAR",
                min = new float[] { 0 },
                max = new float[] { max }
            });
            return accessorIndex;
        }

        private static string BuildGltfJson(
            SlotDataAsset slot,
            int vertexCount,
            int positionAccessor,
            int normalAccessor,
            int uvAccessor,
            int jointsAccessor,
            int weightsAccessor,
            int indicesAccessor,
            int bufferLength,
            List<BufferView> bufferViews,
            List<Accessor> accessors,
            bool hasSkin,
            int[] boneNameHashes,
            UMATransform[] umaBones,
            int inverseBindAccessor)
        {
            var nodes = new List<string>();
            var nodeChildren = new List<List<int>>();
            var jointNodeIndices = new List<int>();
            var boneHashToNode = new Dictionary<int, int>();
            var parentHashes = new List<int>();

            if (hasSkin && boneNameHashes != null)
            {
                for (int i = 0; i < boneNameHashes.Length; i++)
                {
                    int hash = boneNameHashes[i];
                    string name = "bone_" + hash;
                    Vector3 pos = Vector3.zero;
                    Quaternion rot = Quaternion.identity;
                    Vector3 scale = Vector3.one;
                    int parentHash = 0;

                    if (umaBones != null)
                    {
                        for (int b = 0; b < umaBones.Length; b++)
                        {
                            var bone = umaBones[b];
                            if (bone != null && bone.hash == hash)
                            {
                                name = bone.name;
                                pos = bone.position;
                                rot = bone.rotation;
                                scale = bone.scale;
                                parentHash = bone.parent;
                                break;
                            }
                        }
                    }

                    int nodeIndex = nodes.Count;
                    boneHashToNode[hash] = nodeIndex;
                    jointNodeIndices.Add(nodeIndex);
                    nodeChildren.Add(new List<int>());
                    parentHashes.Add(parentHash);

                    nodes.Add(BuildNodeJson(name, ConvertPosition(pos), ConvertRotation(rot), scale, null, null));
                }

                for (int i = 0; i < parentHashes.Count; i++)
                {
                    int parentHash = parentHashes[i];
                    if (parentHash == 0)
                    {
                        continue;
                    }
                    if (boneHashToNode.ContainsKey(parentHash))
                    {
                        int parentIndex = boneHashToNode[parentHash];
                        nodeChildren[parentIndex].Add(i);
                    }
                }

                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodeChildren[i].Count > 0)
                    {
                        nodes[i] = InjectChildren(nodes[i], nodeChildren[i]);
                    }
                }
            }

            int meshNodeIndex = nodes.Count;
            int meshIndex = 0;
            int skinIndex = hasSkin ? 0 : -1;
            nodes.Add(BuildNodeJson(slot != null ? slot.slotName : "Slot", Vector3.zero, Quaternion.identity, Vector3.one, meshIndex, skinIndex));

            int skeletonRoot = -1;
            if (hasSkin)
            {
                var roots = new List<int>();
                for (int i = 0; i < jointNodeIndices.Count; i++)
                {
                    int nodeIndex = jointNodeIndices[i];
                    bool hasParent = false;
                    for (int p = 0; p < nodeChildren.Count; p++)
                    {
                        if (nodeChildren[p].Contains(nodeIndex))
                        {
                            hasParent = true;
                            break;
                        }
                    }
                    if (!hasParent)
                    {
                        roots.Add(nodeIndex);
                    }
                }

                if (roots.Count == 1)
                {
                    skeletonRoot = roots[0];
                }
                else if (roots.Count > 1)
                {
                    skeletonRoot = nodes.Count;
                    nodes.Add(BuildNodeJson("ArmatureRoot", Vector3.zero, Quaternion.identity, Vector3.one, null, null));
                    nodes[skeletonRoot] = InjectChildren(nodes[skeletonRoot], roots);
                }
            }

            var sb = new StringBuilder(4096);
            sb.Append("{");
            sb.Append("\"asset\":{\"version\":\"2.0\",\"generator\":\"UMA Slot glTF Exporter\"},");
            sb.Append("\"buffers\":[{\"byteLength\":").Append(bufferLength).Append("}],");

            sb.Append("\"bufferViews\":[");
            for (int i = 0; i < bufferViews.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var view = bufferViews[i];
                sb.Append("{\"buffer\":").Append(view.buffer)
                  .Append(",\"byteOffset\":").Append(view.byteOffset)
                  .Append(",\"byteLength\":").Append(view.byteLength);
                if (view.target.HasValue)
                {
                    sb.Append(",\"target\":").Append(view.target.Value);
                }
                sb.Append("}");
            }
            sb.Append("],");

            sb.Append("\"accessors\":[");
            for (int i = 0; i < accessors.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var acc = accessors[i];
                sb.Append("{\"bufferView\":").Append(acc.bufferView)
                  .Append(",\"byteOffset\":").Append(acc.byteOffset)
                  .Append(",\"componentType\":").Append(acc.componentType)
                  .Append(",\"count\":").Append(acc.count)
                  .Append(",\"type\":\"").Append(acc.type).Append("\"");
                if (acc.min != null)
                {
                    sb.Append(",\"min\":[");
                    for (int m = 0; m < acc.min.Length; m++)
                    {
                        if (m > 0) sb.Append(',');
                        sb.Append(acc.min[m].ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    sb.Append(']');
                }
                if (acc.max != null)
                {
                    sb.Append(",\"max\":[");
                    for (int m = 0; m < acc.max.Length; m++)
                    {
                        if (m > 0) sb.Append(',');
                        sb.Append(acc.max[m].ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    sb.Append(']');
                }
                sb.Append("}");
            }
            sb.Append("],");

            sb.Append("\"meshes\":[{");
            sb.Append("\"primitives\":[{");
            sb.Append("\"attributes\":{");
            sb.Append("\"POSITION\":").Append(positionAccessor);
            if (normalAccessor >= 0)
            {
                sb.Append(",\"NORMAL\":").Append(normalAccessor);
            }
            if (uvAccessor >= 0)
            {
                sb.Append(",\"TEXCOORD_0\":").Append(uvAccessor);
            }
            if (jointsAccessor >= 0 && weightsAccessor >= 0)
            {
                sb.Append(",\"JOINTS_0\":").Append(jointsAccessor);
                sb.Append(",\"WEIGHTS_0\":").Append(weightsAccessor);
            }
            sb.Append("},");
            sb.Append("\"indices\":").Append(indicesAccessor);
            sb.Append("}]}],");

            sb.Append("\"nodes\":[");
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(nodes[i]);
            }
            sb.Append("],");

            if (hasSkin)
            {
                sb.Append("\"skins\":[{");
                sb.Append("\"joints\":[");
                for (int i = 0; i < jointNodeIndices.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(jointNodeIndices[i]);
                }
                sb.Append(']');
                if (inverseBindAccessor >= 0)
                {
                    sb.Append(",\"inverseBindMatrices\":").Append(inverseBindAccessor);
                }
                if (skeletonRoot >= 0)
                {
                    sb.Append(",\"skeleton\":").Append(skeletonRoot);
                }
                sb.Append("}],");
            }

            sb.Append("\"scenes\":[{\"nodes\":[");
            sb.Append(meshNodeIndex);
            sb.Append("]}],\"scene\":0");
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildNodeJson(string name, Vector3 position, Quaternion rotation, Vector3 scale, int? meshIndex, int? skinIndex)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"name\":\"").Append(Escape(name)).Append("\"");
            if (meshIndex.HasValue)
            {
                sb.Append(",\"mesh\":").Append(meshIndex.Value);
            }
            if (skinIndex.HasValue && skinIndex.Value >= 0)
            {
                sb.Append(",\"skin\":").Append(skinIndex.Value);
            }
            sb.Append(",\"translation\":[")
              .Append(position.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(position.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(position.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            sb.Append(",\"rotation\":[")
              .Append(rotation.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(rotation.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(rotation.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(rotation.w.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            sb.Append(",\"scale\":[")
              .Append(scale.x.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(scale.y.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(scale.z.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)).Append(']');
            sb.Append("}");
            return sb.ToString();
        }

        private static string InjectChildren(string nodeJson, List<int> children)
        {
            int insertIndex = nodeJson.LastIndexOf('}');
            if (insertIndex < 0)
            {
                return nodeJson;
            }
            var sb = new StringBuilder();
            sb.Append(nodeJson.Substring(0, insertIndex));
            sb.Append(",\"children\":[");
            for (int i = 0; i < children.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(children[i]);
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void ReverseTriangleWinding(int[] triangles)
        {
            if (triangles == null) return;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }
        }

        private static Vector3 ConvertPosition(Vector3 v)
        {
            return new Vector3(-v.x, v.y, v.z);
        }

        private static Quaternion ConvertRotation(Quaternion q)
        {
            return new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        private static Matrix4x4 ConvertMatrix(Matrix4x4 unityMatrix)
        {
            return HandednessFlip * unityMatrix * HandednessFlip;
        }

        private static void WriteGlb(string outputPath, string json, byte[] bin)
        {
            if (string.IsNullOrEmpty(outputPath))
            {
                return;
            }

            string jsonPadded = PadTo4(json);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPadded);

            byte[] binPadded = PadTo4(bin);

            int length = 12 + 8 + jsonBytes.Length + 8 + binPadded.Length;

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(GltfMagic);
                bw.Write(GltfVersion);
                bw.Write(length);

                bw.Write(jsonBytes.Length);
                bw.Write(ChunkTypeJson);
                bw.Write(jsonBytes);

                bw.Write(binPadded.Length);
                bw.Write(ChunkTypeBin);
                bw.Write(binPadded);
            }
        }

        private static string PadTo4(string json)
        {
            int mod = json.Length % 4;
            if (mod == 0)
            {
                return json;
            }
            int pad = 4 - mod;
            return json + new string(' ', pad);
        }

        private static byte[] PadTo4(byte[] data)
        {
            int mod = data.Length % 4;
            if (mod == 0)
            {
                return data;
            }
            int pad = 4 - mod;
            byte[] padded = new byte[data.Length + pad];
            Buffer.BlockCopy(data, 0, padded, 0, data.Length);
            return padded;
        }

        private static int AlignBuffer(List<byte> buffer, int alignment)
        {
            int mod = buffer.Count % alignment;
            if (mod == 0)
            {
                return buffer.Count;
            }
            int pad = alignment - mod;
            for (int i = 0; i < pad; i++)
            {
                buffer.Add(0);
            }
            return buffer.Count;
        }

        private static void WriteFloat(List<byte> buffer, float value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static void WriteUShort(List<byte> buffer, ushort value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static void WriteUInt(List<byte> buffer, uint value)
        {
            buffer.AddRange(BitConverter.GetBytes(value));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
#endif
