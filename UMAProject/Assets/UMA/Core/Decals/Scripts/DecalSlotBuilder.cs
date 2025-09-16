using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UMA.CharacterSystem;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Compression;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public sealed class DecalSlotBuilder
    {
        private DecalSlotBuilder() { }

        public static SlotDataAsset LastCreatedDecalSlot { get; private set; }
        public static OverlayDataAsset LastCreatedDecalOverlayAsset { get; private set; }
        public static OverlayDataAsset LastDecalOverlaySent { get; private set; }

        /// <summary>
        /// Record the overlay assigned to the last created decal (call after you apply your overlay).
        /// </summary>
        public static void SetLastDecalOverlay(OverlayData overlay)
        {
            LastCreatedDecalOverlayAsset = overlay ? overlay.asset : null;
        }

        private static void EnsureOverlayTag(SlotDataAsset slot)
        {
            if (slot == null) return;
            if (slot == LastCreatedDecalSlot && LastCreatedDecalOverlayAsset != null)
            {
                string overlayTag = "DecalOverlay:" + LastCreatedDecalOverlayAsset.name;
                if (slot.tags == null)
                {
                    slot.tags = new[] { "Decal", overlayTag };
                }
                else if (!slot.tags.Contains(overlayTag))
                {
                    var list = slot.tags.ToList();
                    if (!list.Contains("Decal")) list.Add("Decal");
                    list.Add(overlayTag);
                    slot.tags = list.ToArray();
                }
            }
        }

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
            return CreateDecalSlot(avatar, ray, radius, fudgeRadius, angleDegrees, umaMaterial, null, options);
        }

        public static SlotDataAsset CreateDecalSlot(
            DynamicCharacterAvatar avatar,
            Ray ray,
            float radius,
            float fudgeRadius,
            float angleDegrees,
            UMAMaterial umaMaterial,
            OverlayDataAsset overlayAsset,
            DecalBuildOptions options = null)
        {
            if (overlayAsset != null)
            {
                LastCreatedDecalOverlayAsset = overlayAsset; // existing tracking
                LastDecalOverlaySent = overlayAsset; // new tracking for serialization
            }
            // Original body below (start after initial param validation)
            if (avatar == null || avatar.umaData == null || umaMaterial == null) return null;
            if (radius <= 0.00001f) return null;

            options ??= new DecalBuildOptions();

            if (!MeshRaycastAvatar(avatar, ray, options, out var smr, out var hitPointWorld, out var hitNormalWorld))
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

                var includedVertex = new bool[combinedVertexCount];
                var includedTriangles = new List<int>(2048);

                SelectTriangles(triIndices, bakedVertsLocal, t, rayDirWorld, hitPointWorld, radiusSqr,
                                options.facingThreshold, includedTriangles, includedVertex, options.enableDebug);

                if (includedTriangles.Count == 0)
                {
                    if (options.enableDebug) Debug.Log("DecalSlotBuilder: No triangles within radius/facing constraints.");
                    return null;
                }

                var remap = new int[combinedVertexCount];
                Array.Fill(remap, -1);
                int newVertexCount = 0;
                for (int i = 0; i < combinedVertexCount; i++)
                    if (includedVertex[i])
                        remap[i] = newVertexCount++;
                if (newVertexCount == 0) return null;

                var outVerts = new Vector3[newVertexCount];
                var outNormals = new Vector3[newVertexCount];
                var outTangents = new Vector4[newVertexCount];
                var outColors32 = new Color32[newVertexCount];
                var outUV = new Vector2[newVertexCount];
                Vector2[][] slotExtraUVs = { null, null, null };

                Vector3 localHitPoint = t.InverseTransformPoint(hitPointWorld);
                Vector3 localRayDir = t.InverseTransformDirection(rayDirWorld).normalized;
                BuildProjectionAxesAroundRay(localRayDir, angleDegrees, out var axisX, out var axisY);

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
                        restPos = SafeGet(shared.vertices, ov, Vector3.zero);
                        restNormal = SafeGet(shared.normals, ov, Vector3.up);
                        restTangent = SafeGet(shared.tangents, ov, new Vector4(1, 0, 0, 1));
                        restColor = SafeGet(shared.colors32, ov, new Color32(255, 255, 255, 255));
                    }

                    outVerts[nv] = restPos;
                    outNormals[nv] = restNormal;
                    outTangents[nv] = restTangent;
                    outColors32[nv] = restColor;

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

                var outTriangles = new int[includedTriangles.Count];
                for (int i = 0; i < includedTriangles.Count; i++)
                    outTriangles[i] = remap[includedTriangles[i]];

                ApplyBindposeCorrection(shared, smr, vertexSlot, vertexLocalIndex,
                                        includedVertex, remap,
                                        outVerts, outNormals, outTangents,
                                        options.enableDebug);

                BuildBoneWeightsFullSkeleton(avatar, smr, shared, includedVertex, remap, newVertexCount,
                    out var outBonesPerVertex, out var outBoneWeights);

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

                md.blendShapes = BuildBlendshapesFromSources(vertexSlot, vertexLocalIndex, includedVertex, remap, newVertexCount);
                md.clothSkinningSerialized = BuildClothCoefficients(vertexSlot, vertexLocalIndex, includedVertex, remap, newVertexCount);

                var slotAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
                slotAsset.slotName = md.SlotName;
                slotAsset.material = umaMaterial;
                slotAsset.meshData = md;
                slotAsset.subMeshIndex = 0;
                slotAsset.sourceSubmeshIndex = 0;
                slotAsset.tags = new[] { "Decal" };

                EnsureOverlayTag(slotAsset); // overlay tag based on tracked asset

                if (options.enableDebug)
                    Debug.Log($"DecalSlotBuilder: Created decal '{slotAsset.slotName}' Vertices={md.vertexCount} Tris={outTriangles.Length / 3} BlendShapes={(md.blendShapes != null ? md.blendShapes.Length : 0)} Cloth={(md.clothSkinningSerialized != null)} Overlay={(overlayAsset!=null ? overlayAsset.name : "None")}");

                LastCreatedDecalSlot = slotAsset;
                return slotAsset;
            }
            finally
            {
                UMAUtils.DestroySceneObject(baked);
            }
        }

        #region Save & Persistence
        public static SlotDataAsset SaveLastDecalSlotAsset(string folderPath, string baseName)
        {
            return SaveDecalSlotAsset(LastCreatedDecalSlot, folderPath, baseName);
        }

        public static SlotDataAsset SaveDecalSlotAsset(SlotDataAsset slot, string folderPath, string baseName)
        {
            if (slot == null || slot.meshData == null || string.IsNullOrEmpty(baseName)) return slot;
            if (string.IsNullOrEmpty(folderPath)) folderPath = "Assets";

#if UNITY_EDITOR
            if (!folderPath.StartsWith("Assets"))
            {
                folderPath = Path.Combine("Assets", folderPath.TrimStart('/', '\\'));
            }
            folderPath = folderPath.Replace('\\', '/');
            if (!folderPath.EndsWith("/")) folderPath += "/";

            // Ensure directory exists
            var dirSegments = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string cumulative = "";
            for (int i = 0; i < dirSegments.Length; i++)
            {
                cumulative = (i == 0) ? dirSegments[0] : cumulative + "/" + dirSegments[i];
                if (!UnityEditor.AssetDatabase.IsValidFolder(cumulative))
                {
                    string parent = Path.GetDirectoryName(cumulative).Replace('\\', '/');
                    if (string.IsNullOrEmpty(parent)) parent = "Assets";
                    string newFolderName = Path.GetFileName(cumulative);
                    UnityEditor.AssetDatabase.CreateFolder(parent, newFolderName);
                }
            }

            // Collect existing slot names to ensure uniqueness
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:SlotDataAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<SlotDataAsset>(p);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(existing.slotName)) existingNames.Add(existing.slotName);
                    else existingNames.Add(Path.GetFileNameWithoutExtension(p));
                }
            }

            string finalName = baseName;
            if (existingNames.Contains(finalName))
            {
                int suffix = 1;
                while (existingNames.Contains(finalName + "_" + suffix)) suffix++;
                finalName = finalName + "_" + suffix;
            }

            // Update slot & mesh names
            slot.slotName = finalName;
            if (slot.meshData != null) slot.meshData.SlotName = finalName;
            slot.name = finalName;

            // Ensure Decal tag present
            if (slot.tags == null)
            {
                slot.tags = new[] { "Decal" };
            }
            else if (!slot.tags.Contains("Decal"))
            {
                var list = slot.tags.ToList();
                list.Add("Decal");
                slot.tags = list.ToArray();
            }

            EnsureOverlayTag(slot); // add overlay tag if needed

            string assetPath = folderPath + finalName + ".asset";
            string existingPath = UnityEditor.AssetDatabase.GetAssetPath(slot);
            if (string.IsNullOrEmpty(existingPath))
            {
                UnityEditor.AssetDatabase.CreateAsset(slot, assetPath);
            }
            else if (existingPath != assetPath)
            {
                UnityEditor.AssetDatabase.MoveAsset(existingPath, assetPath);
            }

            UnityEditor.EditorUtility.SetDirty(slot);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(slot);
            UnityEditor.AssetDatabase.Refresh();

            return slot;
#else
            // Runtime JSON fallback (writes to persistent data path)
            try
            {
                string root = folderPath;
                if (!Path.IsPathRooted(root))
                {
                    root = Path.Combine(Application.persistentDataPath, folderPath.TrimStart('/', '\\'));
                }
                Directory.CreateDirectory(root);

                // Unique name across existing JSON files in folder
                string finalName = baseName;
                int suffix = 1;
                while (File.Exists(Path.Combine(root, finalName + ".json")))
                {
                    finalName = baseName + "_" + suffix++;
                }

                slot.slotName = finalName;
                if (slot.meshData != null) slot.meshData.SlotName = finalName;

                var json = SerializeDecalSlotToJson(slot, false);
                File.WriteAllText(Path.Combine(root, finalName + ".json"), json);
            }
            catch (Exception ex)
            {
                Debug.LogError("DecalSlotBuilder runtime save failed: " + ex.Message);
            }
            return slot;
#endif
        }

        // Public helper for runtime JSON save with optional compression.
        public static bool SaveRuntimeJson(SlotDataAsset slot, string folderPath, string baseName, bool compress)
        {
            if (slot == null || slot.meshData == null) return false;
            try
            {
                string root = folderPath;
                if (!Path.IsPathRooted(root))
                {
                    root = Path.Combine(Application.persistentDataPath, folderPath.TrimStart('/', '\\'));
                }
                Directory.CreateDirectory(root);
                string finalName = baseName;
                int suffix = 1;
                while (File.Exists(Path.Combine(root, finalName + (compress ? ".cjson" : ".json"))))
                {
                    finalName = baseName + "_" + suffix++;
                }
                string json = SerializeDecalSlotToJson(slot, compress);
                string path = Path.Combine(root, finalName + (compress ? ".cjson" : ".json"));
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("DecalSlotBuilder runtime json save failed: " + ex.Message);
                return false;
            }
        }

        private class RuntimeSlotData
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
        }

        [Serializable]
        private struct SubmeshDTO { public int[] triangles; }

        [Serializable]
        private struct BoneDTO
        {
            public int hash;
            public string name;
            public int parent;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [Serializable]
        private struct BoneWeightDTO { public int boneIndex; public float weight; }

        [Serializable]
        private struct BlendShapeFrameDTO
        {
            public float frameWeight;
            public Vector3[] deltaVertices;
            public Vector3[] deltaNormals;
            public Vector3[] deltaTangents;
        }

        [Serializable]
        private struct BlendShapeDTO
        {
            public string name;
            public BlendShapeFrameDTO[] frames;
        }

        [Serializable]
        private class CompressedWrapper
        {
            public bool compressed;
            public string payload; // base64 gzip of inner JSON
        }

        private static string SerializeDecalSlotToJson(SlotDataAsset slot, bool compress)
        {
            if (slot == null || slot.meshData == null) return "{}";
            var md = slot.meshData;
            string overlayName = null;
            if (slot == LastCreatedDecalSlot)
            {
                if (LastDecalOverlaySent != null) overlayName = LastDecalOverlaySent.name;
                else if (LastCreatedDecalOverlayAsset != null) overlayName = LastCreatedDecalOverlayAsset.name;
            }
            var dto = new RuntimeSlotData
            {
                slotName = slot.slotName,
                material = slot.material ? slot.material.name : "",
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
            string inner = JsonUtility.ToJson(dto, false);
            if (!compress) return inner;
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

        private static float[] MatrixToArray(Matrix4x4 m)
        {
            return new float[] { m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23, m.m30, m.m31, m.m32, m.m33 };
        }

        private static Matrix4x4 ArrayToMatrix(float[] a)
        {
            if (a == null || a.Length != 16) return Matrix4x4.identity;
            Matrix4x4 m = new Matrix4x4();
            m.m00 = a[0]; m.m01 = a[1]; m.m02 = a[2]; m.m03 = a[3];
            m.m10 = a[4]; m.m11 = a[5]; m.m12 = a[6]; m.m13 = a[7];
            m.m20 = a[8]; m.m21 = a[9]; m.m22 = a[10]; m.m23 = a[11];
            m.m30 = a[12]; m.m31 = a[13]; m.m32 = a[14]; m.m33 = a[15];
            return m;
        }

        public static SlotDataAsset LoadDecalSlotFromJson(string json, UMAMaterial umaMaterial)
        {
            return LoadDecalSlotFromJson(json, (UMAMaterial)umaMaterial, false);
        }

        public static SlotDataAsset LoadDecalSlotFromJson(string json, UMAMaterial umaMaterial = null, bool silent = false)
        {
            if (string.IsNullOrEmpty(json)) return null;
            RuntimeSlotData dto = null;
            try
            {
                if (json.Contains("\"compressed\""))
                {
                    var wrapper = JsonUtility.FromJson<CompressedWrapper>(json);
                    if (wrapper != null && wrapper.compressed && !string.IsNullOrEmpty(wrapper.payload))
                    {
                        byte[] cmp = Convert.FromBase64String(wrapper.payload);
                        using (var ms = new MemoryStream(cmp))
                        using (var gz = new GZipStream(ms, CompressionMode.Decompress))
                        using (var outMs = new MemoryStream())
                        {
                            gz.CopyTo(outMs);
                            string inner = Encoding.UTF8.GetString(outMs.ToArray());
                            dto = JsonUtility.FromJson<RuntimeSlotData>(inner);
                        }
                    }
                }
                if (dto == null)
                {
                    dto = JsonUtility.FromJson<RuntimeSlotData>(json);
                }
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogError("DecalSlotBuilder: Failed to parse decal JSON: " + ex.Message);
                return null;
            }
            if (dto == null || dto.vertices == null || dto.submeshes == null) return null;

            // Resolve material by name if not supplied
            if (umaMaterial == null && !string.IsNullOrEmpty(dto.material))
            {
                try
                {
                    var indexer = UMAAssetIndexer.Instance;
                    if (indexer != null)
                    {
                        umaMaterial = indexer.GetAsset<UMAMaterial>(dto.material);
                        if (umaMaterial == null)
                        {
                            umaMaterial = Resources.FindObjectsOfTypeAll<UMAMaterial>().FirstOrDefault(m => string.Equals(m.name, dto.material, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (!silent) Debug.LogWarning("DecalSlotBuilder: Material lookup via UMAAssetIndexer failed: " + ex.Message);
                }
                if (umaMaterial == null && !silent)
                {
                    Debug.LogWarning("DecalSlotBuilder: Could not resolve UMAMaterial '" + dto.material + "'. Decal slot will be created without material.");
                }
            }

            var md = new UMAMeshData
            {
                SlotName = dto.slotName ?? "Decal_Runtime",
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

            LastCreatedDecalOverlayAsset = null; // reset before attempting to set

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
                        if (overlayAsset != null) LastCreatedDecalOverlayAsset = overlayAsset;
                    }
                }
                catch { }
            }

            LastCreatedDecalSlot = slotAsset;

#if UNITY_EDITOR
            // If a DynamicCharacterAvatar is currently selected in the editor, auto-apply the loaded decal
            var selectedGO = UnityEditor.Selection.activeGameObject;
            if (selectedGO != null)
            {
                var avatar = selectedGO.GetComponent<DynamicCharacterAvatar>();
                if (avatar != null && avatar.umaData != null)
                {
                    try
                    {
                        UMAAssetIndexer.Instance.ProcessNewItem(slotAsset, false, false);
                        var slotData = new SlotData(slotAsset);
                        if (LastCreatedDecalOverlayAsset != null)
                        {
                            var overlayInstance = new OverlayData(LastCreatedDecalOverlayAsset);
                            slotData.AddOverlay(overlayInstance);
                        }
                        slotData.expandAlongNormal = 3000; // avoid z-fighting
                        avatar.umaData.umaRecipe.MergeSlot(slotData, true);
                        avatar.ForceUpdate(true, true, true);
                    }
                    catch (Exception ex)
                    {
                        if (!silent) Debug.LogWarning("DecalSlotBuilder: Failed to auto-apply loaded decal to selected avatar: " + ex.Message);
                    }
                }
            }
#endif
            return slotAsset;
        }

        /// <summary>
        /// Save a binary gzip of the runtime decal slot (gzip of JSON DTO, not base64).
        /// </summary>
        public static bool SaveRuntimeBinaryGZip(SlotDataAsset slot, string folderPath, string baseName)
        {
            if (slot == null || slot.meshData == null) return false;
            try
            {
                string root = folderPath;
                if (!Path.IsPathRooted(root))
                {
                    root = Path.Combine(Application.persistentDataPath, folderPath.TrimStart('/', '\\'));
                }
                Directory.CreateDirectory(root);
                string finalName = baseName;
                int suffix = 1;
                while (File.Exists(Path.Combine(root, finalName + ".dgz")))
                {
                    finalName = baseName + "_" + suffix++;
                }
                string json = SerializeDecalSlotToJson(slot, false);
                byte[] data = CompressStringToGzip(json);
                File.WriteAllBytes(Path.Combine(root, finalName + ".dgz"), data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("DecalSlotBuilder runtime binary gzip save failed: " + ex.Message);
                return false;
            }
        }

        public static SlotDataAsset LoadDecalSlotFromBinaryGZipFile(string filePath, UMAMaterial umaMaterial = null, bool silent = false)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;
                byte[] gz = File.ReadAllBytes(filePath);
                return LoadDecalSlotFromBinaryGZip(gz, umaMaterial, silent);
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogError("DecalSlotBuilder: Failed to load gzip file: " + ex.Message);
                return null;
            }
        }

        public static SlotDataAsset LoadDecalSlotFromBinaryGZip(byte[] gzipData, UMAMaterial umaMaterial = null, bool silent = false)
        {
            if (gzipData == null || gzipData.Length == 0) return null;
            try
            {
                string json = DecompressGzipToString(gzipData);
                return LoadDecalSlotFromJson(json, umaMaterial, silent);
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogError("DecalSlotBuilder: Failed to parse binary gzip decal: " + ex.Message);
                return null;
            }
        }

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
                Debug.DrawLine(hitPoint, hitPoint + hitNormal * 0.05f, Color.green, 2f);
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

        #region Helpers
        private struct LocalRemap { public int localIndex; public int newIndex; }

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

        private static UMABlendShape[] BuildBlendshapesFromSources(
            SlotData[] vertexSlot,
            int[] vertexLocalIndex,
            bool[] includedVertex,
            int[] remap,
            int newVertexCount)
        {
            var perSlot = BuildPerSlotSelection(vertexSlot, vertexLocalIndex, includedVertex, remap);
            if (perSlot.Count == 0) return null;
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
                        for (int f = 0; f < framesHere; f++) meta.frameWeights[f] = ubs.frames[f].frameWeight;
                        shapeMeta[name] = meta;
                    }
                    else
                    {
                        if (framesHere > meta.frameCount)
                        {
                            var newWeights = new float[framesHere];
                            Array.Copy(meta.frameWeights, newWeights, meta.frameCount);
                            for (int f = meta.frameCount; f < framesHere; f++) newWeights[f] = ubs.frames[Mathf.Clamp(f, 0, ubs.frames.Length - 1)].frameWeight;
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
            var dest = new UMABlendShape[shapeMeta.Count];
            var names = new string[shapeMeta.Count];
            int idx = 0;
            foreach (var kv in shapeMeta)
            {
                var meta = kv.Value; string name = kv.Key;
                var ubs = new UMABlendShape();
                ubs.shapeName = name;
                ubs.frames = new UMABlendFrame[meta.frameCount];
                for (int f = 0; f < meta.frameCount; f++)
                {
                    ubs.frames[f] = new UMABlendFrame(newVertexCount, meta.hasNormals, meta.hasTangents);
                    ubs.frames[f].frameWeight = meta.frameWeights[f];
                }
                dest[idx] = ubs; names[idx] = name; idx++;
            }
            var nameToIndex = new Dictionary<string, int>(shapeMeta.Count);
            for (int i = 0; i < names.Length; i++) nameToIndex[names[i]] = i;
            foreach (var kv in perSlot)
            {
                var slot = kv.Key; var md = slot?.asset?.meshData; var shapes = md?.blendShapes; if (shapes == null || shapes.Length == 0) continue; var mapping = kv.Value;
                for (int s = 0; s < shapes.Length; s++)
                {
                    var srcShape = shapes[s]; string name = srcShape.shapeName ?? $"Blend_{s}"; if (!nameToIndex.TryGetValue(name, out int di)) continue; var dstShape = dest[di]; int framesToCopy = Math.Min(dstShape.frames.Length, srcShape.frames.Length);
                    for (int f = 0; f < framesToCopy; f++)
                    {
                        var sf = srcShape.frames[f]; var df = dstShape.frames[f]; CopyBlendShapeDeltas(mapping, sf, df);
                    }
                }
            }
            return dest;
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
            bool anyCloth = false; Vector2 defaultCoeff = new Vector2(float.MaxValue, 0f); var dest = new Vector2[newVertexCount]; for (int i = 0; i < newVertexCount; i++) dest[i] = defaultCoeff;
            foreach (var kv in perSlot)
            {
                var slot = kv.Key; var md = slot?.asset?.meshData; if (md == null) continue; Vector2[] srcSerialized = md.clothSkinningSerialized; ClothSkinningCoefficient[] srcCloth = md.clothSkinning;
                if ((srcSerialized == null || srcSerialized.Length == 0) && (srcCloth == null || srcCloth.Length == 0)) continue; anyCloth = true; var mapping = kv.Value;
                for (int i = 0; i < mapping.Count; i++)
                {
                    int li = mapping[i].localIndex; int ni = mapping[i].newIndex; if (li < 0 || ni < 0) continue;
                    if (srcSerialized != null && li < srcSerialized.Length) dest[ni] = srcSerialized[li];
                    else if (srcCloth != null && li < srcCloth.Length) { var c = srcCloth[li]; dest[ni] = new Vector2(c.collisionSphereDistance, c.maxDistance); }
                }
            }
            return anyCloth ? dest : null;
        }

        private static bool SegmentSphereIntersect(Vector3 a, Vector3 b, Vector3 center, float radiusSqr)
        {
            Vector3 ab = b - a; float lenSqr = ab.sqrMagnitude; if (lenSqr < 1e-12f) return (a - center).sqrMagnitude <= radiusSqr; float t = Vector3.Dot(center - a, ab) / lenSqr; t = Mathf.Clamp01(t); Vector3 closest = a + t * ab; return (closest - center).sqrMagnitude <= radiusSqr;
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

        private static void CopyBlendShapeDeltas(List<LocalRemap> mapping, UMABlendFrame src, UMABlendFrame dst)
        {
            var sV = src.deltaVertices; var dV = dst.deltaVertices;
            Vector3[] sN = src.HasNormals() ? src.deltaNormals : null; Vector3[] dN = dst.HasNormals() ? dst.deltaNormals : null;
            Vector3[] sT = src.HasTangents() ? src.deltaTangents : null; Vector3[] dT = dst.HasTangents() ? dst.deltaTangents : null;
            for (int i = 0; i < mapping.Count; i++)
            {
                int li = mapping[i].localIndex; int ni = mapping[i].newIndex; if (li < 0 || ni < 0) continue;
                if (sV != null && li < sV.Length && ni < dV.Length) dV[ni] = sV[li];
                if (sN != null && dN != null && li < sN.Length && ni < dN.Length) dN[ni] = sN[li];
                if (sT != null && dT != null && li < sT.Length && ni < dT.Length) dT[ni] = sT[li];
            }
        }
        #endregion

#if UNITY_EDITOR
        // Editor menu helpers (JSON/Asset)
        [UnityEditor.MenuItem("UMA/Decals/Save Last Decal Slot Asset...")]
        private static void MenuSaveAsset()
        {
            if (LastCreatedDecalSlot == null)
            {
                UnityEditor.EditorUtility.DisplayDialog("Save Decal Slot", "No decal slot has been created yet.", "OK");
                return;
            }
            string path = UnityEditor.EditorUtility.SaveFilePanel("Save Decal Slot Asset", "Assets", (LastCreatedDecalSlot.slotName ?? "DecalSlot") + ".asset", "asset");
            if (string.IsNullOrEmpty(path)) return;
            string norm = path.Replace('\\', '/');
            int idx = norm.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                UnityEditor.EditorUtility.DisplayDialog("Invalid Path", "Path must be inside the project's Assets folder.", "OK");
                return;
            }
            string rel = norm.Substring(idx + 1); // remove leading '/'
            string folder = Path.GetDirectoryName(rel).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(rel);
            SaveDecalSlotAsset(LastCreatedDecalSlot, folder, name);
        }

        [UnityEditor.MenuItem("UMA/Decals/Save Last Decal Slot JSON (Uncompressed)...")] 
        private static void MenuSaveJson()
        {
            if (LastCreatedDecalSlot == null)
            {
                UnityEditor.EditorUtility.DisplayDialog("Save Decal JSON", "No decal slot has been created yet.", "OK");
                return;
            }
            string path = UnityEditor.EditorUtility.SaveFilePanel("Save Decal JSON", Application.dataPath, (LastCreatedDecalSlot.slotName ?? "DecalSlot") + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            string json = SerializeDecalSlotToJson(LastCreatedDecalSlot, false);
            File.WriteAllText(path, json);
            UnityEditor.EditorUtility.RevealInFinder(path);
        }

        [UnityEditor.MenuItem("UMA/Decals/Save Last Decal Slot JSON (Compressed)...")] 
        private static void MenuSaveJsonCompressed()
        {
            if (LastCreatedDecalSlot == null)
            {
                UnityEditor.EditorUtility.DisplayDialog("Save Decal JSON (Compressed)", "No decal slot has been created yet.", "OK");
                return;
            }
            string path = UnityEditor.EditorUtility.SaveFilePanel("Save Decal JSON (Compressed)", Application.dataPath, (LastCreatedDecalSlot.slotName ?? "DecalSlot") + ".cjson", "cjson");
            if (string.IsNullOrEmpty(path)) return;
            string json = SerializeDecalSlotToJson(LastCreatedDecalSlot, true);
            File.WriteAllText(path, json);
            UnityEditor.EditorUtility.RevealInFinder(path);
        }

        [UnityEditor.MenuItem("UMA/Decals/Load Decal Slot JSON...")]
        private static void MenuLoadJson()
        {
            string path;
#if UNITY_2020_1_OR_NEWER
            path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(
                "Load Decal JSON",
                Application.dataPath,
                new[] { "Decal JSON", "json", "Compressed JSON", "cjson" });
#else
            path = UnityEditor.EditorUtility.OpenFilePanel("Load Decal JSON", Application.dataPath, "json");
#endif
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string json = File.ReadAllText(path);
                UMAMaterial mat = null; // optional, try resolve by name in JSON
                var slot = LoadDecalSlotFromJson(json, mat);
                if (slot != null)
                {
                    if (Application.isPlaying)
                    {
                        UnityEditor.EditorUtility.DisplayDialog("Decal Loaded", $"Loaded decal slot: {slot.slotName}", "OK");
                    }
                    else
                    {
                        UnityEditor.EditorUtility.DisplayDialog("Decal Loaded", $"Loaded decal slot: {slot.slotName}. Decals are not applied at edit time.", "OK");
                    }
                }
                else
                {
                    UnityEditor.EditorUtility.DisplayDialog("Load Failed", "Unable to load decal JSON.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load decal JSON: " + ex.Message);
            }
        }

        [UnityEditor.MenuItem("UMA/Decals/Save Last Decal Slot Binary GZip...")]
        private static void MenuSaveBinaryGZip()
        {
            if (LastCreatedDecalSlot == null)
            {
                UnityEditor.EditorUtility.DisplayDialog("Save Decal Binary GZip", "No decal slot has been created yet.", "OK");
                return;
            }
            string defaultName = string.IsNullOrEmpty(LastCreatedDecalSlot.slotName) ? "DecalSlot" : LastCreatedDecalSlot.slotName;
            string path = UnityEditor.EditorUtility.SaveFilePanel("Save Decal Binary GZip", Application.dataPath, defaultName + ".dgz", "dgz");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string json = SerializeDecalSlotToJson(LastCreatedDecalSlot, false);
                byte[] data = CompressStringToGzip(json);
                File.WriteAllBytes(path, data);
                UnityEditor.EditorUtility.RevealInFinder(path);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save binary gzip decal: " + ex.Message);
            }
        }

        [UnityEditor.MenuItem("UMA/Decals/Load Decal Slot Binary GZip...")]
        private static void MenuLoadBinaryGZip()
        {
            string path = UnityEditor.EditorUtility.OpenFilePanel("Load Decal Binary GZip", Application.dataPath, "dgz");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var slot = LoadDecalSlotFromBinaryGZipFile(path, null, false);
                if (slot != null)
                {
                    UnityEditor.EditorUtility.DisplayDialog("Decal Loaded", $"Loaded decal slot: {slot.slotName}", "OK");
                }
                else
                {
                    UnityEditor.EditorUtility.DisplayDialog("Load Failed", "Unable to load binary gzip decal.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load binary gzip decal: " + ex.Message);
            }
        }
#endif
    }
}