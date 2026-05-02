using UnityEngine;
using System;
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

        private static SubmeshDTO[] CreateSubmeshDtos(SubMeshTriangles[] submeshes)
        {
            if (submeshes == null)
            {
                return null;
            }

            SubmeshDTO[] result = new SubmeshDTO[submeshes.Length];
            for (int submeshIndex = 0; submeshIndex < submeshes.Length; submeshIndex++)
            {
                result[submeshIndex] = new SubmeshDTO
                {
                    triangles = submeshes[submeshIndex].getManagedTriangles(0)
                };
            }

            return result;
        }

        private static BoneWeightDTO[] CreateBoneWeightDtos(BoneWeight1[] source)
        {
            if (source == null)
            {
                return null;
            }

            BoneWeightDTO[] result = new BoneWeightDTO[source.Length];
            for (int boneWeightIndex = 0; boneWeightIndex < source.Length; boneWeightIndex++)
            {
                BoneWeight1 boneWeight = source[boneWeightIndex];
                result[boneWeightIndex] = new BoneWeightDTO
                {
                    boneIndex = boneWeight.boneIndex,
                    weight = boneWeight.weight
                };
            }

            return result;
        }

        private static BoneDTO[] CreateBoneDtos(UMATransform[] source)
        {
            if (source == null)
            {
                return null;
            }

            BoneDTO[] result = new BoneDTO[source.Length];
            for (int boneIndex = 0; boneIndex < source.Length; boneIndex++)
            {
                UMATransform bone = source[boneIndex];
                result[boneIndex] = new BoneDTO
                {
                    hash = bone.hash,
                    name = bone.name,
                    parent = bone.parent,
                    position = bone.position,
                    rotation = bone.rotation,
                    scale = bone.scale
                };
            }

            return result;
        }

        private static BlendShapeFrameDTO[] CreateBlendShapeFrameDtos(UMABlendFrame[] source)
        {
            BlendShapeFrameDTO[] result = new BlendShapeFrameDTO[source.Length];
            for (int frameIndex = 0; frameIndex < source.Length; frameIndex++)
            {
                UMABlendFrame frame = source[frameIndex];
                result[frameIndex] = new BlendShapeFrameDTO
                {
                    frameWeight = frame.frameWeight,
                    deltaVertices = frame.deltaVertices,
                    deltaNormals = frame.HasNormals() ? frame.deltaNormals : null,
                    deltaTangents = frame.HasTangents() ? frame.deltaTangents : null
                };
            }

            return result;
        }

        private static BlendShapeDTO[] CreateBlendShapeDtos(UMABlendShape[] source)
        {
            if (source == null)
            {
                return null;
            }

            BlendShapeDTO[] result = new BlendShapeDTO[source.Length];
            for (int blendShapeIndex = 0; blendShapeIndex < source.Length; blendShapeIndex++)
            {
                UMABlendShape blendShape = source[blendShapeIndex];
                result[blendShapeIndex] = new BlendShapeDTO
                {
                    name = blendShape.shapeName,
                    frames = CreateBlendShapeFrameDtos(blendShape.frames)
                };
            }

            return result;
        }

        private static BoneWeight1[] CreateManagedBoneWeights(BoneWeightDTO[] source)
        {
            if (source == null)
            {
                return null;
            }

            BoneWeight1[] result = new BoneWeight1[source.Length];
            for (int boneWeightIndex = 0; boneWeightIndex < source.Length; boneWeightIndex++)
            {
                BoneWeightDTO boneWeight = source[boneWeightIndex];
                result[boneWeightIndex] = new BoneWeight1
                {
                    boneIndex = boneWeight.boneIndex,
                    weight = boneWeight.weight
                };
            }

            return result;
        }

        private static bool HasTag(string[] source, string tag)
        {
            if (source == null)
            {
                return false;
            }

            for (int tagIndex = 0; tagIndex < source.Length; tagIndex++)
            {
                if (source[tagIndex] == tag)
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] EnsureTagPresent(string[] source, string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return source;
            }

            if (source == null)
            {
                return new[] { tag };
            }

            if (HasTag(source, tag))
            {
                return source;
            }

            string[] result = new string[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = tag;
            return result;
        }

        // Backward-compatible convenience overload (no UDIM or clearing)
        public static RuntimeSlotData FromSkinnedMesh(SkinnedMeshRenderer smr, int SubMesh)
        {
            return FromSkinnedMesh(smr, SubMesh, udimAdjustment: false, clearNormals: false, clearTangents: false);
        }

        // New overload: control UDIM UV adjustment and clearing normals/tangents
        public static RuntimeSlotData FromSkinnedMesh(SkinnedMeshRenderer smr, int SubMesh, bool udimAdjustment, bool clearNormals, bool clearTangents)
        {
            if (smr == null || smr.sharedMesh == null)
            {
                return null;
            }

            // Build UMAMeshData from the SkinnedMeshRenderer (handles submesh remap)
            var md = new UMAMeshData();
            try
            {
                // SubMesh < 0 means "all"; otherwise export a single submesh remapped to its own vertex buffer
                md.RetrieveDataFromUnityMesh(smr, SubMesh, udimAdjustment, clearNormals, clearTangents);
                // Ensure bone tables and UMA transform hierarchy are captured
                md.UpdateBones(smr.rootBone, smr.bones);

                // Optional cloth
                var cloth = smr.GetComponent<Cloth>();
                if (cloth != null)
                {
                    md.RetrieveDataFromUnityCloth(cloth);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"RuntimeSlotData.FromSkinnedMesh: failed extracting mesh data: {ex.Message}");
                return null;
            }

            // Material name best-effort: prefer the submesh's material, else first material, else empty
            string matName = string.Empty;
            var mats = smr.sharedMaterials;
            if (mats != null && mats.Length > 0)
            {
                int mi = (SubMesh >= 0 && SubMesh < mats.Length) ? SubMesh : 0;
                var m = mats[mi];
                if (m != null) matName = m.name;
            }

            var dto = new RuntimeSlotData
            {
                slotName = (SubMesh >= 0) ? ($"{smr.name}_{SubMesh}") : smr.name,
                material = matName,
                tags = new string[0],

                // Mesh data
                vertices = md.vertices,
                normals = md.normals,
                tangents = md.tangents,
                colors32 = md.colors32,
                uv = md.uv,
                uv2 = md.uv2,
                uv3 = md.uv3,
                uv4 = md.uv4,
                submeshes = CreateSubmeshDtos(md.submeshes),
                vertexCount = md.vertexCount,
                bindPoses = md.bindPoses,

                // Skinning
                boneNameHashes = md.boneNameHashes,
                bonesPerVertex = md.ManagedBonesPerVertex,
                boneWeights = md.ManagedBoneWeights != null ? CreateBoneWeightDtos(md.ManagedBoneWeights) : Array.Empty<BoneWeightDTO>(),
                bones = md.umaBones != null ? CreateBoneDtos(md.umaBones) : Array.Empty<BoneDTO>(),

                // Blendshapes
                blendShapes = CreateBlendShapeDtos(md.blendShapes),

                // Cloth (serialized two-float coefficients)
                clothCoeffs = md.clothSkinningSerialized
            };

            return dto;
        }

        public static RuntimeSlotData FromSlot(SlotDataAsset slot, string overlayName)
        {
            if (slot == null)
            {
                return null;
            }
            UMAMeshData md = slot.meshData;
            UMAMaterial material = null;
            if (!string.IsNullOrEmpty(overlayName))
            {
                var overlayAsset = UMAAssetIndexer.Instance.GetAsset<OverlayDataAsset>(overlayName);
                if (overlayAsset != null)
                {
                    material = overlayAsset.GetMaterial();
                }
            }
            var dto = new RuntimeSlotData
            {
                slotName = slot.slotName,
                material = material ? material.name : "",
                tags = slot.tags,
                vertices = md.vertices,
                normals = md.normals,
                tangents = md.tangents,
                colors32 = md.colors32,
                uv = md.uv,
                uv2 = md.uv2,
                uv3 = md.uv3,
                uv4 = md.uv4,
                submeshes = CreateSubmeshDtos(md.submeshes),
                vertexCount = md.vertexCount,
                boneNameHashes = md.boneNameHashes,
                bonesPerVertex = md.ManagedBonesPerVertex,
                boneWeights = CreateBoneWeightDtos(md.ManagedBoneWeights),
                bones = CreateBoneDtos(md.umaBones),
                bindPoses = md.bindPoses,
                blendShapes = CreateBlendShapeDtos(md.blendShapes),
                clothCoeffs = md.clothSkinningSerialized,
                overlayAssetName = overlayName
            };
            return dto;
        }

        // Build a RuntimeSlotData from an existing UMAMeshData (used by DecalSlotBuilder)
        public static RuntimeSlotData FromMeshData(UMAMeshData md, string slotName, UMAMaterial material, string overlayName = null, string[] tags = null)
        {
            if (UMAMeshData.IsNullOrEmptyMeshData(md)) return null;
            var dto = new RuntimeSlotData
            {
                slotName = string.IsNullOrEmpty(slotName) ? (md.SlotName ?? "RuntimeSlot") : slotName,
                material = material ? material.name : string.Empty,
                tags = tags,
                vertices = md.vertices,
                normals = md.normals,
                tangents = md.tangents,
                colors32 = md.colors32,
                uv = md.uv,
                uv2 = md.uv2,
                uv3 = md.uv3,
                uv4 = md.uv4,
                submeshes = CreateSubmeshDtos(md.submeshes),
                vertexCount = md.vertexCount,
                boneNameHashes = md.boneNameHashes,
                bonesPerVertex = md.ManagedBonesPerVertex,
                boneWeights = CreateBoneWeightDtos(md.ManagedBoneWeights),
                bones = CreateBoneDtos(md.umaBones),
                bindPoses = md.bindPoses,
                blendShapes = CreateBlendShapeDtos(md.blendShapes),
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
                ManagedBoneWeights = CreateManagedBoneWeights(dto.boneWeights),
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
                    // Avoid allocating persistent NativeArray to prevent leaks
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
            slotAsset.name = md.SlotName;
            slotAsset.meshData = md;
            slotAsset.subMeshIndex = 0;
            slotAsset.sourceSubmeshIndex = 0;
            slotAsset.tags = new[] { "Decal" };

            if (!string.IsNullOrEmpty(dto.overlayAssetName))
            {
                string overlayTag = "DecalOverlay:" + dto.overlayAssetName;
                slotAsset.tags = EnsureTagPresent(slotAsset.tags, overlayTag);
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