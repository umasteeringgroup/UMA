using UnityEngine;
using System;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using Unity.Collections;

namespace UMA
{
    public class RuntimeSlotData
    {
        public string slotName;
        public string material;
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Color32[] colors32;
        public Vector2[] uv;
        public Vector2[] uv2;
        public Vector2[] uv3;
        public Vector2[] uv4;
        // Serializable forms
        public SubmeshDTO[] submeshes;
        public Matrix4x4[] bindPoses;
        public int vertexCount;
        public int[] boneNameHashes;
        public BoneDTO[] bones;
        public byte[] bonesPerVertex;
        public BoneWeightDTO[] boneWeights;
        public BlendShapeDTO[] blendShapes;
        public Vector2[] clothCoeffs;
        public string overlayAssetName;
        public string[] tags;
        public static RuntimeSlotData FromSlot(SlotDataAsset slot, string overlayName)
        {
            if (slot == null)
            {
                return null;
            }
            UMAMeshData md = slot.meshData;
            var dto = new RuntimeSlotData
            {
                slotName = slot.slotName,
                material = slot.material ? slot.material.name : "",
                tags = slot.tags,
                vertices = md.vertices,
                normals = md.normals,
                tangents = md.tangents,
                colors32 = md.colors32,
                uv = md.uv,
                uv2 = md.uv2,
                uv3 = md.uv3,
                uv4 = md.uv4,
                submeshes = md.submeshes != null ? md.submeshes.Select(sm => new SubmeshDTO { triangles = sm.getBaseTriangles() }).ToArray() : null,
                vertexCount = md.vertexCount,
                boneNameHashes = md.boneNameHashes,
                bonesPerVertex = md.ManagedBonesPerVertex,
                boneWeights = md.ManagedBoneWeights != null ? md.ManagedBoneWeights.Select(bw => new BoneWeightDTO { boneIndex = bw.boneIndex, weight = bw.weight }).ToArray() : null,
                bones = md.umaBones != null ? md.umaBones.Select(b => new BoneDTO { hash = b.hash, name = b.name, parent = b.parent, position = b.position, rotation = b.rotation, scale = b.scale }).ToArray() : null,
                bindPoses = md.bindPoses,
                blendShapes = md.blendShapes != null ? md.blendShapes.Select(bs => new BlendShapeDTO
                {
                    name = bs.shapeName,
                    frames = bs.frames.Select(fr => new BlendShapeFrameDTO
                    {
                        frameWeight = fr.frameWeight,
                        deltaVertices = fr.deltaVertices,
                        deltaNormals = fr.HasNormals() ? fr.deltaNormals : null,
                        deltaTangents = fr.HasTangents() ? fr.deltaTangents : null
                    }).ToArray()
                }).ToArray() : null,
                clothCoeffs = md.clothSkinningSerialized,
                overlayAssetName = overlayName
            };
            return dto;
        }

        public SlotReturnValue ToSlot()
        {
            SlotReturnValue result = new SlotReturnValue();
            RuntimeSlotData dto = this;

            var md = new UMAMeshData
            {
                SlotName = dto.slotName ?? "RuntimeSlot",
                vertices = dto.vertices,
                normals = dto.normals,
                tangents = dto.tangents,
                colors32 = dto.colors32,
                uv = dto.uv,
                uv2 = dto.uv2,
                uv3 = dto.uv3,
                uv4 = dto.uv4,
                vertexCount = dto.vertexCount,
                boneNameHashes = dto.boneNameHashes,
                ManagedBonesPerVertex = dto.bonesPerVertex,
                ManagedBoneWeights = dto.boneWeights != null ? dto.boneWeights.Select(b => new BoneWeight1 { boneIndex = b.boneIndex, weight = b.weight }).ToArray() : null,
                clothSkinningSerialized = dto.clothCoeffs
            };

            if (dto.bones != null)
            {
                md.umaBones = new UMATransform[dto.bones.Length];
                for (int i = 0; i < dto.bones.Length; i++)
                {
                    var b = dto.bones[i];
                    md.umaBones[i] = new UMATransform
                    {
                        hash = b.hash,
                        name = b.name,
                        parent = b.parent,
                        position = b.position,
                        rotation = b.rotation,
                        scale = b.scale
                    };
                }
                md.umaBoneCount = md.umaBones.Length;
            }

            if (dto.bindPoses != null && dto.bindPoses.Length > 0)
            {
                md.bindPoses = dto.bindPoses;
            }

            if (dto.submeshes != null)
            {
                md.subMeshCount = dto.submeshes.Length;
                md.submeshes = new SubMeshTriangles[md.subMeshCount];
                for (int i = 0; i < dto.submeshes.Length; i++)
                {
                    var sm = new SubMeshTriangles();
                    var tris = dto.submeshes[i].triangles ?? Array.Empty<int>();
                    sm.SetTriangles(tris);
                    sm.nativeTriangles = new NativeArray<int>(tris, Allocator.Persistent);
                    md.submeshes[i] = sm;
                }
            }

            if (dto.blendShapes != null)
            {
                var shapes = new UMABlendShape[dto.blendShapes.Length];
                for (int s = 0; s < dto.blendShapes.Length; s++)
                {
                    var sd = dto.blendShapes[s];
                    var shape = new UMABlendShape();
                    shape.shapeName = sd.name;
                    shape.frames = new UMABlendFrame[sd.frames.Length];
                    for (int f = 0; f < sd.frames.Length; f++)
                    {
                        var fd = sd.frames[f];
                        var frame = new UMABlendFrame(md.vertexCount, fd.deltaNormals != null, fd.deltaTangents != null);
                        frame.frameWeight = fd.frameWeight;
                        if (fd.deltaVertices != null && fd.deltaVertices.Length == frame.deltaVertices.Length)
                            Array.Copy(fd.deltaVertices, frame.deltaVertices, frame.deltaVertices.Length);
                        if (frame.deltaNormals != null && fd.deltaNormals != null && fd.deltaNormals.Length == frame.deltaNormals.Length)
                            Array.Copy(fd.deltaNormals, frame.deltaNormals, frame.deltaNormals.Length);
                        if (frame.deltaTangents != null && fd.deltaTangents != null && fd.deltaTangents.Length == frame.deltaTangents.Length)
                            Array.Copy(fd.deltaTangents, frame.deltaTangents, frame.deltaTangents.Length);
                        shape.frames[f] = frame;
                    }
                    shapes[s] = shape;
                }
                md.blendShapes = shapes;
            }

            UMAMaterial umaMaterial = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(dto.material);
            result.material = umaMaterial;

            var slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            slotAsset.slotName = md.SlotName;
            slotAsset.material = umaMaterial;
            slotAsset.meshData = md;
            slotAsset.subMeshIndex = 0;
            slotAsset.sourceSubmeshIndex = 0;
            slotAsset.tags = new[] { "Decal" };

            if (!string.IsNullOrEmpty(dto.overlayAssetName))
            {
                string overlayTag = "DecalOverlay:" + dto.overlayAssetName;
                var list = slotAsset.tags.ToList();
                if (!list.Contains(overlayTag)) list.Add(overlayTag);
                slotAsset.tags = list.ToArray();
                try
                {
                    var indexer = UMAAssetIndexer.Instance;
                    if (indexer != null)
                    {
                        var overlayAsset = indexer.GetAsset<OverlayDataAsset>(dto.overlayAssetName);
                        result.overlay = overlayAsset;
                    }
                }
                catch { }
            }
            result.slot = slotAsset;
            return result;
        }

        public static RuntimeSlotData FromGzip(byte[] gz)
        {
            if (gz == null || gz.Length == 0)
            {
                return null;
            }
            try
            {
                string json = DecompressGzipToString(gz);
                return JsonUtility.FromJson<RuntimeSlotData>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError("RuntimeSlotData: runtime binary gzip load failed: " + ex.Message);
                return null;
            }
        }

        public static RuntimeSlotData FromJSON(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            try
            {
                var wrapper = JsonUtility.FromJson<CompressedWrapper>(json);
                if (wrapper != null && wrapper.compressed && !string.IsNullOrEmpty(wrapper.payload))
                {
                    // decompress
                    byte[] gz = Convert.FromBase64String(wrapper.payload);
                    using (var ms = new MemoryStream(gz))
                    {
                        using (var gzs = new GZipStream(ms, CompressionMode.Decompress))
                        {
                            using (var reader = new StreamReader(gzs))
                            {
                                string inner = reader.ReadToEnd();
                                return JsonUtility.FromJson<RuntimeSlotData>(inner);
                            }
                        }
                    }
                }
                else
                {
                    return JsonUtility.FromJson<RuntimeSlotData>(json);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("RuntimeSlotData: runtime binary gzip load failed: " + ex.Message);
                return null;
            }
        }

        public string ToJSON(bool compress)
        {
            string inner = JsonUtility.ToJson(this, false);
            if (!compress)
            {
                return inner;
            }
            // compress into base64 wrapper
            byte[] raw = Encoding.UTF8.GetBytes(inner);
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    gz.Write(raw, 0, raw.Length);
                }
                string b64 = Convert.ToBase64String(ms.ToArray());
                var wrapper = new CompressedWrapper { compressed = true, payload = b64 };
                return JsonUtility.ToJson(wrapper, false);
            }
        }

        public byte[] ToGzip()
        {
            try
            {
                string json = ToJSON(false);
                byte[] data = CompressStringToGzip(json);
                return data;
            }
            catch (Exception ex)
            {
                Debug.LogError("RuntimeSlotData: runtime binary gzip creation failed: " + ex.Message);
                return null;
            }
        }
        #region Helpers
        private static byte[] CompressStringToGzip(string s)
        {
            if (string.IsNullOrEmpty(s)) return Array.Empty<byte>();
            byte[] raw = Encoding.UTF8.GetBytes(s);
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    gz.Write(raw, 0, raw.Length);
                }
                return ms.ToArray();
            }
        }

        private static string DecompressGzipToString(byte[] gz)
        {
            using (var ms = new MemoryStream(gz))
            using (var gzS = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                gzS.CopyTo(outMs);
                return Encoding.UTF8.GetString(outMs.ToArray());
            }
        }
        #endregion
    }

    public struct SlotReturnValue
    {
        public SlotDataAsset slot;
        public OverlayDataAsset overlay;
        public UMAMaterial material;
        public string errorMessage;
    }


    [Serializable]
    public struct SubmeshDTO { public int[] triangles; }

    [Serializable]
    public struct BoneDTO
    {
        public int hash;
        public string name;
        public int parent;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
    }

    [Serializable]
    public struct BoneWeightDTO { public int boneIndex; public float weight; }

    [Serializable]
    public struct BlendShapeFrameDTO
    {
        public float frameWeight;
        public Vector3[] deltaVertices;
        public Vector3[] deltaNormals;
        public Vector3[] deltaTangents;
    }

    [Serializable]
    public struct BlendShapeDTO
    {
        public string name;
        public BlendShapeFrameDTO[] frames;
    }

    [Serializable]
    public class CompressedWrapper
    {
        public bool compressed;
        public string payload; // base64 gzip of inner JSON
    }
}