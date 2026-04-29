#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UMA;
using UMA.CharacterSystem;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public static class UMAGltfExporter
    {
        private const int ComponentTypeUnsignedInt = 5125;
        private const int ComponentTypeUnsignedShort = 5123;
        private const int ComponentTypeFloat = 5126;

        private const int TargetArrayBuffer = 34962;
        private const int TargetElementArrayBuffer = 34963;

        private const int PrimitiveModeTriangles = 4;

        private static readonly Matrix4x4 HandednessFlip = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

        private sealed class ExportOptions
        {
            public bool EmbedImages;
        }

        public static void ExportAvatar(GameObject sourceObject, string assetFolder, string charName)
        {
            if (sourceObject == null)
            {
                throw new ArgumentNullException(nameof(sourceObject));
            }
            if (string.IsNullOrEmpty(assetFolder))
            {
                throw new ArgumentNullException(nameof(assetFolder));
            }
            if (string.IsNullOrEmpty(charName))
            {
                throw new ArgumentNullException(nameof(charName));
            }

            Directory.CreateDirectory(GetAbsolutePathFromAssetPath(assetFolder));

            GameObject exportRoot = CreateExportClone(sourceObject);
            try
            {
                ResetCloneToDefaultPose(exportRoot);

                string gltfAssetPath = Path.Combine(assetFolder, charName + ".gltf").Replace('\\', '/');
                string binAssetPath = Path.Combine(assetFolder, charName + ".bin").Replace('\\', '/');

                DocumentBuilder doc = BuildDocument(exportRoot, gltfAssetPath, binAssetPath, charName, null);
                File.WriteAllBytes(GetAbsolutePathFromAssetPath(binAssetPath), doc.Buffer.ToArray());
                File.WriteAllText(GetAbsolutePathFromAssetPath(gltfAssetPath), doc.ToJson(), new UTF8Encoding(false));

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(exportRoot);
            }
        }

        public static void ExportAvatarSlots(UMAAvatarBase avatar, string assetFolder, string charName, bool includeRig)
        {
            if (avatar == null)
            {
                throw new ArgumentNullException(nameof(avatar));
            }
            if (avatar.umaData == null || avatar.umaData.umaRecipe == null)
            {
                throw new InvalidOperationException("Avatar has no UMA recipe data.");
            }
            if (string.IsNullOrEmpty(assetFolder))
            {
                throw new ArgumentNullException(nameof(assetFolder));
            }
            if (string.IsNullOrEmpty(charName))
            {
                throw new ArgumentNullException(nameof(charName));
            }

            Directory.CreateDirectory(GetAbsolutePathFromAssetPath(assetFolder));

            ExportOptions options = new ExportOptions { EmbedImages = true };
            GameObject exportRoot = BuildAvatarSlotsExportObject(avatar, charName, includeRig);
            try
            {
                string gltfAssetPath = Path.Combine(assetFolder, charName + ".gltf").Replace('\\', '/');
                string binAssetPath = Path.Combine(assetFolder, charName + ".bin").Replace('\\', '/');

                DocumentBuilder doc = BuildDocument(exportRoot, gltfAssetPath, binAssetPath, charName, options);
                File.WriteAllBytes(GetAbsolutePathFromAssetPath(binAssetPath), doc.Buffer.ToArray());
                File.WriteAllText(GetAbsolutePathFromAssetPath(gltfAssetPath), doc.ToJson(), new UTF8Encoding(false));

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(exportRoot);
            }
        }

        private static GameObject BuildAvatarSlotsExportObject(UMAAvatarBase avatar, string charName, bool includeRig)
        {
            GameObject root = new GameObject(string.IsNullOrEmpty(charName) ? "AvatarSlots_glTFExport" : charName + "_Slots_glTFExport");
            SlotData[] slotDataList = avatar.umaData.umaRecipe.slotDataList;
            if (slotDataList == null)
            {
                return root;
            }

            int slotCounter = 0;
            for (int i = 0; i < slotDataList.Length; i++)
            {
                SlotData slot = slotDataList[i];
                if (slot == null || slot.asset == null || slot.asset.meshData == null)
                {
                    continue;
                }

                Mesh mesh = SlotToMesh.ConvertSlotToMesh(slot.asset);
                if (mesh == null)
                {
                    continue;
                }

                string slotName = !string.IsNullOrEmpty(slot.slotName) ? slot.slotName : ("Slot_" + slotCounter.ToString(CultureInfo.InvariantCulture));
                mesh.name = slotName + "_Mesh";
                Material slotMaterial = BuildMaterialFromSlotOverlays(slot, slotName);

                GameObject slotGo = new GameObject(slotName);
                slotGo.transform.SetParent(root.transform, false);

                if (includeRig && slot.asset.meshData.bindPoses != null && slot.asset.meshData.bindPoses.Length > 0)
                {
                    BuildSkinnedSlotRenderer(slotGo, slot.asset, mesh, slotMaterial);
                }
                else
                {
                    MeshFilter mf = slotGo.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;

                    MeshRenderer mr = slotGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = slotMaterial;
                }

                slotCounter++;
            }

            return root;
        }

        private static Material BuildMaterialFromSlotOverlays(SlotData slot, string slotName)
        {
            Shader opaqueShader = Shader.Find("UMA/Diffuse");
            Shader alphaShader = Shader.Find("UMA/Diffuse_Alpha");
            Shader fallbackShader = Shader.Find("Standard");

            bool hasAlpha = false;
            List<OverlayData> overlays = slot != null ? slot.GetOverlayList() : null;
            if (overlays != null)
            {
                for (int i = 0; i < overlays.Count; i++)
                {
                    OverlayData overlay = overlays[i];
                    if (overlay != null && overlay.asset != null && (overlay.asset.overlayType == OverlayDataAsset.OverlayType.Cutout || overlay.asset.alphaMask != null))
                    {
                        hasAlpha = true;
                        break;
                    }
                }
            }

            Shader shader = hasAlpha ? alphaShader : opaqueShader;
            if (shader == null)
            {
                shader = fallbackShader;
            }

            Material material = new Material(shader);
            material.name = slotName + "_OverlayMat";
            material.SetColor("_Color", Color.white);

            List<string> fallbackTextureSlots = new List<string>
            {
                "_MainTex", "_BaseMap", "_BumpMap", "_EmissionMap", "_OcclusionMap", "_MetallicGlossMap", "_DetailAlbedoMap", "_DetailNormalMap"
            };

            int fallbackTextureSlotIndex = 0;
            if (overlays == null)
            {
                return material;
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                OverlayData overlay = overlays[i];
                if (overlay == null || overlay.asset == null)
                {
                    continue;
                }

                if (i == 0)
                {
                    material.SetColor("_Color", overlay.colorData != null ? (Color)overlay.colorData.color : Color.white);
                }

                UMAMaterial umaMat = overlay.asset.GetMaterial();
                Texture[] texList = overlay.asset.textureList;
                if (texList == null)
                {
                    continue;
                }

                for (int t = 0; t < texList.Length; t++)
                {
                    Texture tex = texList[t];
                    if (tex == null)
                    {
                        continue;
                    }

                    string propertyName = string.Empty;
                    if (umaMat != null && umaMat.channels != null && t < umaMat.channels.Length)
                    {
                        propertyName = umaMat.channels[t].materialPropertyName;
                        if (!string.IsNullOrEmpty(propertyName) && propertyName.StartsWith("unity", StringComparison.OrdinalIgnoreCase))
                        {
                            propertyName = string.Empty;
                        }
                    }

                    if (!string.IsNullOrEmpty(propertyName) && material.HasProperty(propertyName))
                    {
                        material.SetTexture(propertyName, tex);
                        continue;
                    }

                    while (fallbackTextureSlotIndex < fallbackTextureSlots.Count)
                    {
                        string fallbackProp = fallbackTextureSlots[fallbackTextureSlotIndex];
                        fallbackTextureSlotIndex++;
                        if (!material.HasProperty(fallbackProp))
                        {
                            continue;
                        }
                        if (material.GetTexture(fallbackProp) != null)
                        {
                            continue;
                        }

                        material.SetTexture(fallbackProp, tex);
                        break;
                    }
                }
            }

            return material;
        }

        public static void ExportSlotDataAsset(SlotDataAsset slot, string assetFolder, string slotName, bool includeRig)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }

            if (string.IsNullOrEmpty(assetFolder))
            {
                throw new ArgumentNullException(nameof(assetFolder));
            }
            if (string.IsNullOrEmpty(slotName))
            {
                throw new ArgumentNullException(nameof(slotName));
            }
            if (slot.meshData == null)
            {
                throw new InvalidOperationException("SlotDataAsset has no meshData.");
            }

            Directory.CreateDirectory(GetAbsolutePathFromAssetPath(assetFolder));

            GameObject exportRoot = BuildSlotExportObject(slot, slotName, includeRig);
            try
            {
                string gltfAssetPath = Path.Combine(assetFolder, slotName + ".gltf").Replace('\\', '/');
                string binAssetPath = Path.Combine(assetFolder, slotName + ".bin").Replace('\\', '/');

                DocumentBuilder doc = BuildDocument(exportRoot, gltfAssetPath, binAssetPath, slotName, null);
                File.WriteAllBytes(GetAbsolutePathFromAssetPath(binAssetPath), doc.Buffer.ToArray());
                File.WriteAllText(GetAbsolutePathFromAssetPath(gltfAssetPath), doc.ToJson(), new UTF8Encoding(false));

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            finally
            {
                Object.DestroyImmediate(exportRoot);
            }
        }

        private static GameObject BuildSlotExportObject(SlotDataAsset slot, string slotName, bool includeRig)
        {
            GameObject root = new GameObject(string.IsNullOrEmpty(slotName) ? "Slot_glTFExport" : slotName + "_glTFExport");

            Mesh mesh = CreateExportMeshFromSlot(slot, slot != null ? slot.subMeshIndex : 0);
            mesh.name = !string.IsNullOrEmpty(slotName) ? slotName : slot.slotName;

            Material material = new Material(Shader.Find("Standard"));
            material.name = mesh.name + "_Mat";

            if (includeRig && slot.meshData != null && slot.meshData.bindPoses != null && slot.meshData.bindPoses.Length > 0)
            {
                BuildSkinnedSlotRenderer(root, slot, mesh, material);
            }
            else
            {
                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }

            return root;
        }

        private static Mesh CreateExportMeshFromSlot(SlotDataAsset slot, int requestedSubmesh)
        {
            if (slot == null || slot.meshData == null || slot.meshData.vertices == null || slot.meshData.vertices.Length == 0)
            {
                return null;
            }

            UMAMeshData meshData = slot.meshData;
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.name = !string.IsNullOrEmpty(slot.slotName) ? slot.slotName : slot.name;
            mesh.bindposes = meshData.bindPoses;

            mesh.vertices = meshData.vertices;

            if (meshData.uv != null && meshData.uv.Length == meshData.vertices.Length)
            {
                mesh.uv = meshData.uv;
            }
            if (meshData.uv2 != null && meshData.uv2.Length == meshData.vertices.Length)
            {
                mesh.uv2 = meshData.uv2;
            }
            if (meshData.uv3 != null && meshData.uv3.Length == meshData.vertices.Length)
            {
                mesh.uv3 = meshData.uv3;
            }
            if (meshData.uv4 != null && meshData.uv4.Length == meshData.vertices.Length)
            {
                mesh.uv4 = meshData.uv4;
            }

            if (meshData.normals != null && meshData.normals.Length == meshData.vertices.Length)
            {
                mesh.normals = meshData.normals;
            }
            if (meshData.tangents != null && meshData.tangents.Length == meshData.vertices.Length)
            {
                mesh.tangents = meshData.tangents;
            }

            int submeshCount = meshData.subMeshCount;
            bool exportSingleLegacySubmesh = submeshCount > 1 && requestedSubmesh >= 0 && requestedSubmesh < submeshCount;

            if (exportSingleLegacySubmesh)
            {
                mesh.subMeshCount = 1;
                int[] tris = meshData.submeshes[requestedSubmesh].getManagedTriangles(0);
                if (tris != null && tris.Length > 0)
                {
                    mesh.SetIndices(tris, MeshTopology.Triangles, 0, false);
                }
            }
            else
            {
                mesh.subMeshCount = submeshCount;
                for (int i = 0; i < submeshCount; i++)
                {
                    int[] tris = meshData.submeshes[i].getManagedTriangles(0);
                    if (tris == null)
                    {
                        tris = Array.Empty<int>();
                    }
                    mesh.SetIndices(tris, MeshTopology.Triangles, i, false);
                }
            }

            return mesh;
        }

        private static void BuildSkinnedSlotRenderer(GameObject root, SlotDataAsset slot, Mesh mesh, Material material)
        {
            int[] boneHashes = slot.meshData.boneNameHashes;
            Matrix4x4[] bindPoses = slot.meshData.bindPoses;
            UMATransform[] umaBones = slot.meshData.umaBones;

            Dictionary<int, UMATransform> boneLookup = new Dictionary<int, UMATransform>();
            if (umaBones != null)
            {
                for (int i = 0; i < umaBones.Length; i++)
                {
                    UMATransform bone = umaBones[i];
                    if (bone != null && !boneLookup.ContainsKey(bone.hash))
                    {
                        boneLookup.Add(bone.hash, bone);
                    }
                }
            }

            Transform[] bones = new Transform[boneHashes.Length];
            Dictionary<int, Transform> created = new Dictionary<int, Transform>();

            for (int i = 0; i < boneHashes.Length; i++)
            {
                int hash = boneHashes[i];
                bones[i] = CreateBoneRecursive(hash, created, boneLookup, root.transform);
            }

            BoneWeight[] boneWeights = new BoneWeight[mesh.vertexCount];
            if (slot.meshData.boneWeights != null && slot.meshData.boneWeights.Length == mesh.vertexCount)
            {
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    UMABoneWeight src = slot.meshData.boneWeights[i];
                    BoneWeight bw = new BoneWeight();
                    bw.boneIndex0 = src.boneIndex0;
                    bw.boneIndex1 = src.boneIndex1;
                    bw.boneIndex2 = src.boneIndex2;
                    bw.boneIndex3 = src.boneIndex3;
                    bw.weight0 = src.weight0;
                    bw.weight1 = src.weight1;
                    bw.weight2 = src.weight2;
                    bw.weight3 = src.weight3;
                    boneWeights[i] = bw;
                }
            }
            else if (slot.meshData.ManagedBonesPerVertex != null && slot.meshData.ManagedBoneWeights != null)
            {
                ConvertManagedBoneWeights(slot.meshData, boneWeights);
            }
            mesh.boneWeights = boneWeights;
            mesh.bindposes = bindPoses;

            SkinnedMeshRenderer renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.bones = bones;
            renderer.rootBone = root.transform;
            renderer.updateWhenOffscreen = true;
        }

        private static Transform CreateBoneRecursive(int hash, Dictionary<int, Transform> created, Dictionary<int, UMATransform> boneLookup, Transform root)
        {
            Transform existing;
            if (created.TryGetValue(hash, out existing))
            {
                return existing;
            }

            UMATransform boneData;
            Transform parent = root;
            string boneName = "bone_" + hash.ToString(CultureInfo.InvariantCulture);
            Vector3 localPos = Vector3.zero;
            Quaternion localRot = Quaternion.identity;
            Vector3 localScale = Vector3.one;

            if (boneLookup.TryGetValue(hash, out boneData) && boneData != null)
            {
                boneName = string.IsNullOrEmpty(boneData.name) ? boneName : boneData.name;
                localPos = boneData.position;
                localRot = boneData.rotation;
                localScale = boneData.scale;

                if (boneData.parent != 0)
                {
                    parent = CreateBoneRecursive(boneData.parent, created, boneLookup, root);
                }
            }

            GameObject boneGo = new GameObject(boneName);
            Transform t = boneGo.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = localRot;
            t.localScale = localScale;

            created[hash] = t;
            return t;
        }

        private static void ConvertManagedBoneWeights(UMAMeshData meshData, BoneWeight[] output)
        {
            int offset = 0;
            for (int i = 0; i < output.Length; i++)
            {
                int count = meshData.ManagedBonesPerVertex[i];
                BoneWeight bw = new BoneWeight();

                for (int j = 0; j < count && j < 4; j++)
                {
                    BoneWeight1 src = meshData.ManagedBoneWeights[offset + j];
                    if (j == 0) { bw.boneIndex0 = src.boneIndex; bw.weight0 = src.weight; }
                    else if (j == 1) { bw.boneIndex1 = src.boneIndex; bw.weight1 = src.weight; }
                    else if (j == 2) { bw.boneIndex2 = src.boneIndex; bw.weight2 = src.weight; }
                    else if (j == 3) { bw.boneIndex3 = src.boneIndex; bw.weight3 = src.weight; }
                }

                output[i] = bw;
                offset += count;
            }
        }
        private static GameObject CreateExportClone(GameObject sourceObject)
        {
            DynamicCharacterAvatar sourceDca = sourceObject.GetComponent<DynamicCharacterAvatar>();
            bool previousEditorTimeGeneration = false;
            if (sourceDca != null)
            {
                previousEditorTimeGeneration = sourceDca.editorTimeGeneration;
                sourceDca.editorTimeGeneration = false;
            }

            GameObject clone = Object.Instantiate(sourceObject);

            if (sourceDca != null)
            {
                sourceDca.editorTimeGeneration = previousEditorTimeGeneration;
            }

            clone.name = sourceObject.name + "_glTFExport";
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.transform.localScale = Vector3.one;

            Animator animator = clone.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            DynamicCharacterAvatar cloneDca = clone.GetComponent<DynamicCharacterAvatar>();
            if (cloneDca != null)
            {
                UMAGeneratorBase generator = null;
                bool previousConvertRenderTexture = false;
                bool previousUseAsyncConversion = false;
                bool restoreGeneratorSettings = false;

                if (UMAAssetIndexer.Instance != null)
                {
                    generator = UMAAssetIndexer.Instance.generator;
                }

                if (generator != null)
                {
                    previousConvertRenderTexture = generator.convertRenderTexture;
                    previousUseAsyncConversion = generator.useAsyncConversion;
                    generator.convertRenderTexture = true;
                    generator.useAsyncConversion = false;
                    restoreGeneratorSettings = true;
                }

                try
                {
                    cloneDca.BuildNow();
                }
                finally
                {
                    if (restoreGeneratorSettings)
                    {
                        generator.convertRenderTexture = previousConvertRenderTexture;
                        generator.useAsyncConversion = previousUseAsyncConversion;
                    }
                }
            }

            if (cloneDca != null)
            {
                cloneDca.enabled = false;
            }

            UMAExpressionPlayer expressionPlayer = clone.GetComponent<UMAExpressionPlayer>();
            if (expressionPlayer != null)
            {
                expressionPlayer.enabled = false;
            }

            SkinnedMeshRenderer[] renderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }

            return clone;
        }

        private static void ResetCloneToDefaultPose(GameObject exportRoot)
        {
            if (exportRoot == null)
            {
                return;
            }

            UMAData[] umaData = exportRoot.GetComponentsInChildren<UMAData>(true);
            for (int i = 0; i < umaData.Length; i++)
            {
                if (umaData[i] != null && umaData[i].skeleton != null)
                {
                    umaData[i].skeleton.ResetAll();
                }
            }
        }

        private static DocumentBuilder BuildDocument(GameObject exportRoot, string gltfAssetPath, string binAssetPath, string charName, ExportOptions options)
        {
            DocumentBuilder doc = new DocumentBuilder(gltfAssetPath, binAssetPath, charName, options);

            List<Transform> transforms = new List<Transform>();
            CollectTransforms(exportRoot.transform, transforms);

            for (int i = 0; i < transforms.Count; i++)
            {
                doc.TransformToNodeIndex[transforms[i]] = i;
            }

            for (int i = 0; i < transforms.Count; i++)
            {
                Transform t = transforms[i];
                NodeData node = new NodeData();
                node.name = t.name;
                node.matrix = ToGltfMatrixArray(ConvertMatrix(t.localPosition, t.localRotation, t.localScale));

                if (t.childCount > 0)
                {
                    node.children = new int[t.childCount];
                    for (int c = 0; c < t.childCount; c++)
                    {
                        node.children[c] = doc.TransformToNodeIndex[t.GetChild(c)];
                    }
                }

                doc.nodes.Add(node);
            }

            SkinnedMeshRenderer[] renderers = exportRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = renderers[i];
                if (smr == null || smr.sharedMesh == null)
                {
                    continue;
                }

                // Pre-validate skin before committing any buffer data
                bool canBuildSkin = CanBuildSkin(doc, smr);
                int skinIndex = canBuildSkin ? BuildSkin(doc, smr) : -1;
                int meshIndex = BuildMesh(doc, smr, skinIndex >= 0);

                int nodeIndex;
                if (!doc.TransformToNodeIndex.TryGetValue(smr.transform, out nodeIndex))
                {
                    continue;
                }

                if (meshIndex >= 0)
                {
                    doc.nodes[nodeIndex].mesh = meshIndex;
                    if (skinIndex >= 0)
                    {
                        doc.nodes[nodeIndex].skin = skinIndex;
                    }
                }
            }

            SceneData scene = new SceneData();
            scene.name = charName;
            scene.nodes = new[] { 0 };
            doc.scenes.Add(scene);
            doc.scene = 0;

            BufferData buffer = new BufferData();
            buffer.uri = Path.GetFileName(binAssetPath);
            buffer.byteLength = doc.Buffer.Length;
            doc.buffers.Add(buffer);

            return doc;
        }

        private static int BuildMesh(DocumentBuilder doc, SkinnedMeshRenderer smr, bool includeSkinningAttributes)
        {
            Mesh mesh = smr.sharedMesh;
            Vector3[] srcVertices = mesh.vertices;
            int vertexCount = srcVertices != null ? srcVertices.Length : 0;

            if (vertexCount == 0)
            {
                return -1;
            }

            Vector3[] srcNormals = mesh.normals;
            Vector4[] srcTangents = mesh.tangents;
            Vector2[] srcUv = mesh.uv;
            BoneWeight[] srcWeights = mesh.boneWeights;

            DedupedSkin skin = null;
            if (includeSkinningAttributes)
            {
                skin = DeduplicateBones(mesh, smr.bones, srcWeights, vertexCount);
            }

            float[] positions = new float[vertexCount * 3];
            float[] positionMin = new float[] { float.MaxValue, float.MaxValue, float.MaxValue };
            float[] positionMax = new float[] { float.MinValue, float.MinValue, float.MinValue };

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 v = ConvertPosition(srcVertices[i]);
                int offset = i * 3;
                positions[offset + 0] = v.x;
                positions[offset + 1] = v.y;
                positions[offset + 2] = v.z;

                positionMin[0] = Mathf.Min(positionMin[0], v.x);
                positionMin[1] = Mathf.Min(positionMin[1], v.y);
                positionMin[2] = Mathf.Min(positionMin[2], v.z);

                positionMax[0] = Mathf.Max(positionMax[0], v.x);
                positionMax[1] = Mathf.Max(positionMax[1], v.y);
                positionMax[2] = Mathf.Max(positionMax[2], v.z);
            }

            int positionAccessor = doc.AddFloatAccessor(positions, ComponentTypeFloat, vertexCount, "VEC3", TargetArrayBuffer, positionMin, positionMax);

            int normalAccessor = -1;
            if (srcNormals != null && srcNormals.Length == vertexCount)
            {
                float[] normals = new float[vertexCount * 3];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector3 n = ConvertDirection(srcNormals[i]).normalized;
                    int offset = i * 3;
                    normals[offset + 0] = n.x;
                    normals[offset + 1] = n.y;
                    normals[offset + 2] = n.z;
                }
                normalAccessor = doc.AddFloatAccessor(normals, ComponentTypeFloat, vertexCount, "VEC3", TargetArrayBuffer, null, null);
            }

            int tangentAccessor = -1;
            if (srcTangents != null && srcTangents.Length == vertexCount)
            {
                float[] tangents = new float[vertexCount * 4];
                for (int i = 0; i < vertexCount; i++)
                {
                    Vector4 t = ConvertTangent(srcTangents[i]);
                    int offset = i * 4;
                    tangents[offset + 0] = t.x;
                    tangents[offset + 1] = t.y;
                    tangents[offset + 2] = t.z;
                    tangents[offset + 3] = t.w;
                }
                tangentAccessor = doc.AddFloatAccessor(tangents, ComponentTypeFloat, vertexCount, "VEC4", TargetArrayBuffer, null, null);
            }

            int uvAccessor = -1;
            if (srcUv != null && srcUv.Length == vertexCount)
            {
                float[] uv = new float[vertexCount * 2];
                for (int i = 0; i < vertexCount; i++)
                {
                    int offset = i * 2;
                    uv[offset + 0] = srcUv[i].x;
                    uv[offset + 1] = srcUv[i].y;
                }
                uvAccessor = doc.AddFloatAccessor(uv, ComponentTypeFloat, vertexCount, "VEC2", TargetArrayBuffer, null, null);
            }

            int jointsAccessor = -1;
            int weightsAccessor = -1;
            if (skin != null && skin.HasSkinning)
            {
                jointsAccessor = doc.AddUShortAccessor(skin.joints0, vertexCount, "VEC4", TargetArrayBuffer);
                weightsAccessor = doc.AddFloatAccessor(skin.weights0, ComponentTypeFloat, vertexCount, "VEC4", TargetArrayBuffer, null, null);
            }

            MeshData meshData = new MeshData();
            meshData.name = mesh.name;

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    Debug.LogWarning("glTF export skipped non-triangle submesh on " + smr.name + " / " + mesh.name);
                    continue;
                }

                int[] triangles = mesh.GetTriangles(subMesh);
                if (triangles == null || triangles.Length == 0)
                {
                    continue;
                }

                ReverseTriangleWinding(triangles);

                int indexAccessor;
                if (vertexCount > ushort.MaxValue)
                {
                    uint[] indices = new uint[triangles.Length];
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        indices[i] = (uint)triangles[i];
                    }
                    indexAccessor = doc.AddUIntAccessor(indices, triangles.Length, "SCALAR", TargetElementArrayBuffer);
                }
                else
                {
                    ushort[] indices = new ushort[triangles.Length];
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        indices[i] = (ushort)triangles[i];
                    }
                    indexAccessor = doc.AddUShortAccessor(indices, triangles.Length, "SCALAR", TargetElementArrayBuffer);
                }

                PrimitiveData primitive = new PrimitiveData();
                primitive.mode = PrimitiveModeTriangles;
                primitive.indices = indexAccessor;
                primitive.position = positionAccessor;
                primitive.normal = normalAccessor;
                primitive.tangent = tangentAccessor;
                primitive.texcoord0 = uvAccessor;
                primitive.joints0 = jointsAccessor;
                primitive.weights0 = weightsAccessor;

                Material[] sharedMaterials = smr.sharedMaterials;
                Material material = null;
                if (sharedMaterials != null && subMesh < sharedMaterials.Length)
                {
                    material = sharedMaterials[subMesh];
                }
                primitive.material = doc.RegisterMaterial(material);

                meshData.primitives.Add(primitive);
            }

            if (meshData.primitives.Count == 0)
            {
                return -1;
            }

            doc.meshes.Add(meshData);
            return doc.meshes.Count - 1;
        }

        private static bool CanBuildSkin(DocumentBuilder doc, SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
            {
                return false;
            }

            Transform[] bones = smr.bones;
            if (bones == null || bones.Length == 0)
            {
                return false;
            }

            // Check that all non-null bones can be resolved to nodes
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                {
                    continue; // Null bones are remapped to root node, which is valid
                }
                if (!doc.TransformToNodeIndex.ContainsKey(bone))
                {
                    return false;
                }
            }

            return true;
        }

        private static int BuildSkin(DocumentBuilder doc, SkinnedMeshRenderer smr)
        {
            Mesh mesh = smr.sharedMesh;
            if (mesh == null)
            {
                return -1;
            }

            DedupedSkin deduped = DeduplicateBones(mesh, smr.bones, mesh.boneWeights, mesh.vertexCount);
            if (!deduped.HasSkinning || deduped.uniqueBones.Count == 0)
            {
                return -1;
            }

            float[] inverseBindMatrices = new float[deduped.uniqueBindPoses.Count * 16];
            for (int i = 0; i < deduped.uniqueBindPoses.Count; i++)
            {
                float[] matrix = ToGltfMatrixArray(ConvertMatrix(deduped.uniqueBindPoses[i]));
                Array.Copy(matrix, 0, inverseBindMatrices, i * 16, 16);
            }

            int accessor = doc.AddFloatAccessor(inverseBindMatrices, ComponentTypeFloat, deduped.uniqueBindPoses.Count, "MAT4", 0, null, null);

            SkinData skin = new SkinData();
            skin.name = smr.name + "_Skin";
            skin.inverseBindMatrices = accessor;
            skin.joints = new int[deduped.uniqueBones.Count];

            for (int i = 0; i < deduped.uniqueBones.Count; i++)
            {
                Transform bone = deduped.uniqueBones[i];
                if (bone == null)
                {
                    // Null bone - use root node (index 0) as fallback
                    skin.joints[i] = 0;
                    continue;
                }
                int nodeIndex;
                if (!doc.TransformToNodeIndex.TryGetValue(bone, out nodeIndex))
                {
                    // Should not happen if CanBuildSkin passed, but fallback to root
                    skin.joints[i] = 0;
                    continue;
                }
                skin.joints[i] = nodeIndex;
            }

            if (smr.rootBone != null)
            {
                int skeletonNode;
                if (doc.TransformToNodeIndex.TryGetValue(smr.rootBone, out skeletonNode))
                {
                    skin.skeleton = skeletonNode;
                }
            }

            doc.skins.Add(skin);
            return doc.skins.Count - 1;
        }

        private static DedupedSkin DeduplicateBones(Mesh mesh, Transform[] bones, BoneWeight[] weights, int vertexCount)
        {
            DedupedSkin result = new DedupedSkin();
            result.uniqueBones = new List<Transform>();
            result.uniqueBindPoses = new List<Matrix4x4>();
            result.joints0 = new ushort[vertexCount * 4];
            result.weights0 = new float[vertexCount * 4];

            if (bones == null || bones.Length == 0)
            {
                return result;
            }

            Matrix4x4[] bindposes = mesh.bindposes != null ? mesh.bindposes : new Matrix4x4[0];
            Dictionary<int, int> remap = new Dictionary<int, int>();
            Dictionary<int, int> seen = new Dictionary<int, int>();

            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                int key = bone != null ? bone.GetInstanceID() : int.MinValue + i;

                int dedupedIndex;
                if (!seen.TryGetValue(key, out dedupedIndex))
                {
                    dedupedIndex = result.uniqueBones.Count;
                    seen.Add(key, dedupedIndex);
                    result.uniqueBones.Add(bone);
                    result.uniqueBindPoses.Add(i < bindposes.Length ? bindposes[i] : Matrix4x4.identity);
                }

                remap[i] = dedupedIndex;
            }

            if (result.uniqueBones.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < vertexCount; i++)
            {
                BoneWeight bw = (weights != null && i < weights.Length) ? weights[i] : default;
                ushort[] joints = new ushort[4];
                float[] normalizedWeights = new float[4];
                RemapBoneWeight(bw, remap, result.uniqueBones.Count, joints, normalizedWeights);

                int offset = i * 4;
                result.joints0[offset + 0] = joints[0];
                result.joints0[offset + 1] = joints[1];
                result.joints0[offset + 2] = joints[2];
                result.joints0[offset + 3] = joints[3];

                result.weights0[offset + 0] = normalizedWeights[0];
                result.weights0[offset + 1] = normalizedWeights[1];
                result.weights0[offset + 2] = normalizedWeights[2];
                result.weights0[offset + 3] = normalizedWeights[3];
            }

            result.HasSkinning = true;
            return result;
        }

        private static void RemapBoneWeight(BoneWeight bw, Dictionary<int, int> remap, int jointCount, ushort[] outJoints, float[] outWeights)
        {
            Dictionary<int, float> merged = new Dictionary<int, float>();

            AddWeight(merged, RemapIndex(bw.boneIndex0, remap), bw.weight0);
            AddWeight(merged, RemapIndex(bw.boneIndex1, remap), bw.weight1);
            AddWeight(merged, RemapIndex(bw.boneIndex2, remap), bw.weight2);
            AddWeight(merged, RemapIndex(bw.boneIndex3, remap), bw.weight3);

            List<KeyValuePair<int, float>> ordered = new List<KeyValuePair<int, float>>(merged);
            ordered.Sort((a, b) => b.Value.CompareTo(a.Value));

            float total = 0f;
            int count = Mathf.Min(4, ordered.Count);
            for (int i = 0; i < count; i++)
            {
                total += ordered[i].Value;
            }

            if (count == 0 || total <= 0.000001f)
            {
                outJoints[0] = 0;
                outWeights[0] = jointCount > 0 ? 1f : 0f;
                for (int i = 1; i < 4; i++)
                {
                    outJoints[i] = 0;
                    outWeights[i] = 0f;
                }
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                if (i < count)
                {
                    outJoints[i] = (ushort)Mathf.Clamp(ordered[i].Key, 0, ushort.MaxValue);
                    outWeights[i] = ordered[i].Value / total;
                }
                else
                {
                    outJoints[i] = 0;
                    outWeights[i] = 0f;
                }
            }
        }

        private static int RemapIndex(int originalIndex, Dictionary<int, int> remap)
        {
            int remapped;
            if (remap.TryGetValue(originalIndex, out remapped))
            {
                return remapped;
            }
            return 0;
        }

        private static void AddWeight(Dictionary<int, float> merged, int jointIndex, float weight)
        {
            if (weight <= 0f)
            {
                return;
            }

            float existing;
            if (merged.TryGetValue(jointIndex, out existing))
            {
                merged[jointIndex] = existing + weight;
            }
            else
            {
                merged.Add(jointIndex, weight);
            }
        }

        private static void ReverseTriangleWinding(int[] triangles)
        {
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }
        }

        private static void CollectTransforms(Transform root, List<Transform> results)
        {
            results.Add(root);
            for (int i = 0; i < root.childCount; i++)
            {
                CollectTransforms(root.GetChild(i), results);
            }
        }

        private static Vector3 ConvertPosition(Vector3 v)
        {
            return new Vector3(-v.x, v.y, v.z);
        }

        private static Vector3 ConvertDirection(Vector3 v)
        {
            return new Vector3(-v.x, v.y, v.z);
        }

        private static Vector4 ConvertTangent(Vector4 t)
        {
            return new Vector4(-t.x, t.y, t.z, -t.w);
        }

        private static Matrix4x4 ConvertMatrix(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
        {
            return ConvertMatrix(Matrix4x4.TRS(localPosition, localRotation, localScale));
        }

        private static Matrix4x4 ConvertMatrix(Matrix4x4 unityMatrix)
        {
            return HandednessFlip * unityMatrix * HandednessFlip;
        }

        private static float[] ToGltfMatrixArray(Matrix4x4 m)
        {
            return new[]
            {
                m.m00, m.m10, m.m20, m.m30,
                m.m01, m.m11, m.m21, m.m31,
                m.m02, m.m12, m.m22, m.m32,
                m.m03, m.m13, m.m23, m.m33
            };
        }

        private static string GetAbsolutePathFromAssetPath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrEmpty(projectRoot))
            {
                return path;
            }

            return Path.Combine(projectRoot, path.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetRelativeUri(string fromAssetFilePath, string toAssetPath)
        {
            Uri from = new Uri(GetAbsolutePathFromAssetPath(fromAssetFilePath));
            Uri to = new Uri(GetAbsolutePathFromAssetPath(toAssetPath));
            return Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString());
        }

        private sealed class DocumentBuilder
        {
            private readonly string _gltfAssetPath;
            private readonly string _outputFolder;
            private readonly string _charName;
            private readonly bool _embedImages;
            private readonly Dictionary<Material, int> _materialMap = new Dictionary<Material, int>();
            private readonly Dictionary<string, int> _imageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<int, int> _runtimeTextureMap = new Dictionary<int, int>();
            private readonly Dictionary<string, int> _samplerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, int> _textureMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            private int _runtimeTextureCounter;

            public readonly Dictionary<Transform, int> TransformToNodeIndex = new Dictionary<Transform, int>();
            public readonly BinaryBuffer Buffer = new BinaryBuffer();
            public readonly List<SceneData> scenes = new List<SceneData>();
            public readonly List<NodeData> nodes = new List<NodeData>();
            public readonly List<MeshData> meshes = new List<MeshData>();
            public readonly List<SkinData> skins = new List<SkinData>();
            public readonly List<MaterialData> materials = new List<MaterialData>();
            public readonly List<TextureData> textures = new List<TextureData>();
            public readonly List<ImageData> images = new List<ImageData>();
            public readonly List<SamplerData> samplers = new List<SamplerData>();
            public readonly List<AccessorData> accessors = new List<AccessorData>();
            public readonly List<BufferViewData> bufferViews = new List<BufferViewData>();
            public readonly List<BufferData> buffers = new List<BufferData>();
            public int scene;

            public DocumentBuilder(string gltfAssetPath, string binAssetPath, string charName, ExportOptions options)
            {
                _gltfAssetPath = gltfAssetPath;
                _outputFolder = Path.GetDirectoryName(gltfAssetPath);
                _charName = charName;
                _embedImages = options != null && options.EmbedImages;
            }

            private int AddImageBufferView(byte[] bytes)
            {
                int offset = Buffer.Align(4);
                Buffer.Write(bytes);

                BufferViewData view = new BufferViewData();
                view.buffer = 0;
                view.byteOffset = offset;
                view.byteLength = bytes.Length;
                view.byteStride = 0;
                view.target = 0;
                bufferViews.Add(view);
                return bufferViews.Count - 1;
            }

            private static string GetMimeTypeFromTextureAssetPath(string assetPath)
            {
                string ext = Path.GetExtension(assetPath);
                if (string.IsNullOrEmpty(ext))
                {
                    return "image/png";
                }

                ext = ext.ToLowerInvariant();
                if (ext == ".jpg" || ext == ".jpeg")
                {
                    return "image/jpeg";
                }
                return "image/png";
            }

            private byte[] EncodeTextureToImageBytes(Texture texture, out string mimeType)
            {
                mimeType = "image/png";
                if (texture == null)
                {
                    return null;
                }

                Texture2D readable = null;
                try
                {
                    if (texture is RenderTexture rt)
                    {
                        readable = GetReadableTexture(rt, false);
                    }
                    else if (texture is Texture2D tex2d)
                    {
                        readable = GetReadableTexture(tex2d, false);
                    }
                    else
                    {
                        return null;
                    }

                    if (readable == null)
                    {
                        return null;
                    }

                    return readable.EncodeToPNG();
                }
                finally
                {
                    if (readable != null)
                    {
                        Object.DestroyImmediate(readable);
                    }
                }
            }

            public int AddFloatAccessor(float[] values, int componentType, int count, string type, int target, float[] min, float[] max)
            {
                byte[] bytes = new byte[values.Length * sizeof(float)];
                System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                int bufferView = AddBufferView(bytes, target, 0, 4);

                AccessorData accessor = new AccessorData();
                accessor.bufferView = bufferView;
                accessor.componentType = componentType;
                accessor.count = count;
                accessor.type = type;
                accessor.min = min;
                accessor.max = max;
                accessors.Add(accessor);
                return accessors.Count - 1;
            }

            public int AddUShortAccessor(ushort[] values, int count, string type, int target)
            {
                byte[] bytes = new byte[values.Length * sizeof(ushort)];
                System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                int bufferView = AddBufferView(bytes, target, 0, 4);

                AccessorData accessor = new AccessorData();
                accessor.bufferView = bufferView;
                accessor.componentType = ComponentTypeUnsignedShort;
                accessor.count = count;
                accessor.type = type;
                accessors.Add(accessor);
                return accessors.Count - 1;
            }

            public int AddUIntAccessor(uint[] values, int count, string type, int target)
            {
                byte[] bytes = new byte[values.Length * sizeof(uint)];
                System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
                int bufferView = AddBufferView(bytes, target, 0, 4);

                AccessorData accessor = new AccessorData();
                accessor.bufferView = bufferView;
                accessor.componentType = ComponentTypeUnsignedInt;
                accessor.count = count;
                accessor.type = type;
                accessors.Add(accessor);
                return accessors.Count - 1;
            }

            private int AddBufferView(byte[] bytes, int target, int byteStride, int alignment)
            {
                int byteOffset = Buffer.Align(alignment);
                Buffer.Write(bytes);

                BufferViewData view = new BufferViewData();
                view.buffer = 0;
                view.byteOffset = byteOffset;
                view.byteLength = bytes.Length;
                view.byteStride = byteStride;
                view.target = target;
                bufferViews.Add(view);
                return bufferViews.Count - 1;
            }

            public int RegisterMaterial(Material material)
            {
                if (material == null)
                {
                    return -1;
                }

                int existing;
                if (_materialMap.TryGetValue(material, out existing))
                {
                    return existing;
                }

                MaterialData gltfMaterial = new MaterialData();
                gltfMaterial.name = material.name;
                gltfMaterial.pbrMetallicRoughness = new PbrMetallicRoughnessData();

                Color baseColor = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                gltfMaterial.pbrMetallicRoughness.baseColorFactor = new[]
                {
                    baseColor.r,
                    baseColor.g,
                    baseColor.b,
                    baseColor.a
                };

                Texture baseColorTexture = GetBaseColorTexture(material);
                int baseColorTextureIndex = RegisterTexture(baseColorTexture);
                if (baseColorTextureIndex >= 0)
                {
                    gltfMaterial.pbrMetallicRoughness.baseColorTexture = new TextureInfoData { index = baseColorTextureIndex };
                }
                else if (baseColorTexture != null)
                {
                    Debug.LogWarning("glTF export failed to register base color texture for material: " + material.name);
                }

                float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
                float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") :
                    (material.HasProperty("_Glossiness") ? material.GetFloat("_Glossiness") : 0f);

                gltfMaterial.pbrMetallicRoughness.metallicFactor = Mathf.Clamp01(metallic);
                gltfMaterial.pbrMetallicRoughness.roughnessFactor = Mathf.Clamp01(1f - smoothness);

                Texture normalTexture = GetNormalTexture(material);
                int normalTextureIndex = RegisterTexture(normalTexture);
                if (normalTextureIndex >= 0)
                {
                    gltfMaterial.normalTexture = new NormalTextureInfoData
                    {
                        index = normalTextureIndex,
                        scale = material.HasProperty("_BumpScale") ? material.GetFloat("_BumpScale") : 1f
                    };
                }
                else if (normalTexture != null)
                {
                    Debug.LogWarning("glTF export failed to register normal texture for material: " + material.name);
                }

                Texture occlusionTexture = GetTextureByExactName(material, "_OcclusionMap");
                int occlusionTextureIndex = RegisterTexture(occlusionTexture);
                if (occlusionTextureIndex >= 0)
                {
                    gltfMaterial.occlusionTexture = new OcclusionTextureInfoData
                    {
                        index = occlusionTextureIndex,
                        strength = material.HasProperty("_OcclusionStrength") ? material.GetFloat("_OcclusionStrength") : 1f
                    };
                }
                else if (occlusionTexture != null)
                {
                    Debug.LogWarning("glTF export failed to register occlusion texture for material: " + material.name);
                }

                Texture emissionTexture = GetTextureByExactName(material, "_EmissionMap");
                Color emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
                if (emissionTexture != null || emissionColor.maxColorComponent > 0f)
                {
                    gltfMaterial.emissiveFactor = new[]
                    {
                        emissionColor.r,
                        emissionColor.g,
                        emissionColor.b
                    };

                    int emissiveTextureIndex = RegisterTexture(emissionTexture);
                    if (emissiveTextureIndex >= 0)
                    {
                        gltfMaterial.emissiveTexture = new TextureInfoData { index = emissiveTextureIndex };
                    }
                    else if (emissionTexture != null)
                    {
                        Debug.LogWarning("glTF export failed to register emissive texture for material: " + material.name);
                    }
                }

                if (IsAlphaCutout(material))
                {
                    gltfMaterial.alphaMode = "MASK";
                    gltfMaterial.alphaCutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
                }
                else if (IsAlphaBlend(material))
                {
                    gltfMaterial.alphaMode = "BLEND";
                }

                if (material.HasProperty("_Cull") && Mathf.Approximately(material.GetFloat("_Cull"), 0f))
                {
                    gltfMaterial.doubleSided = true;
                }

                materials.Add(gltfMaterial);
                int index = materials.Count - 1;
                _materialMap.Add(material, index);
                return index;
            }

            private int RegisterTexture(Texture texture)
            {
                if (texture == null)
                {
                    return -1;
                }

                string assetPath = AssetDatabase.GetAssetPath(texture);
                bool isRuntimeTexture = string.IsNullOrEmpty(assetPath);

                // Check if we already processed this runtime texture
                if (isRuntimeTexture)
                {
                    int instanceId = texture.GetInstanceID();
                    int existingIndex;
                    if (_runtimeTextureMap.TryGetValue(instanceId, out existingIndex))
                    {
                        return existingIndex;
                    }
                }

                int imageIndex;
                string imageKey = isRuntimeTexture ? texture.GetInstanceID().ToString(CultureInfo.InvariantCulture) : assetPath;

                if (!_imageMap.TryGetValue(imageKey, out imageIndex))
                {
                    ImageData image = new ImageData();

                    if (_embedImages)
                    {
                        byte[] imageBytes = EncodeTextureToImageBytes(texture, out string mimeType);
                        if (imageBytes == null || imageBytes.Length == 0)
                        {
                            return -1;
                        }

                        image.name = texture.name;
                        image.mimeType = mimeType;
                        image.bufferView = AddImageBufferView(imageBytes);
                    }
                    else if (isRuntimeTexture)
                    {
                        string exportedFileName = ExportRuntimeTexture(texture);
                        if (string.IsNullOrEmpty(exportedFileName))
                        {
                            return -1;
                        }
                        image.name = Path.GetFileNameWithoutExtension(exportedFileName);
                        image.uri = exportedFileName;
                    }
                    else
                    {
                        image.name = Path.GetFileNameWithoutExtension(assetPath);
                        image.uri = GetRelativeUri(_gltfAssetPath, assetPath).Replace('\\', '/');
                        image.mimeType = GetMimeTypeFromTextureAssetPath(assetPath);
                    }

                    images.Add(image);
                    imageIndex = images.Count - 1;
                    _imageMap.Add(imageKey, imageIndex);
                }

                int samplerIndex = RegisterSampler(texture, assetPath);
                string textureKey = imageKey + "|" + samplerIndex.ToString(CultureInfo.InvariantCulture);

                int textureIndex;
                if (_textureMap.TryGetValue(textureKey, out textureIndex))
                {
                    if (isRuntimeTexture)
                    {
                        _runtimeTextureMap[texture.GetInstanceID()] = textureIndex;
                    }
                    return textureIndex;
                }

                TextureData gltfTexture = new TextureData();
                gltfTexture.name = texture.name;
                gltfTexture.sampler = samplerIndex;
                gltfTexture.source = imageIndex;
                textures.Add(gltfTexture);

                textureIndex = textures.Count - 1;
                _textureMap.Add(textureKey, textureIndex);

                if (isRuntimeTexture)
                {
                    _runtimeTextureMap[texture.GetInstanceID()] = textureIndex;
                }

                return textureIndex;
            }

            private int RegisterSampler(Texture texture, string assetPath)
            {
                TextureImporter importer = string.IsNullOrEmpty(assetPath) ? null : AssetImporter.GetAtPath(assetPath) as TextureImporter;

                TextureWrapMode wrapU = importer != null ? importer.wrapModeU : texture.wrapMode;
                TextureWrapMode wrapV = importer != null ? importer.wrapModeV : texture.wrapMode;
                FilterMode filterMode = texture.filterMode;
                bool hasMipmaps = importer != null ? importer.mipmapEnabled : (texture is Texture2D tex2d && tex2d.mipmapCount > 1);

                int magFilter = filterMode == FilterMode.Point ? 9728 : 9729;
                int minFilter;
                if (!hasMipmaps)
                {
                    minFilter = filterMode == FilterMode.Point ? 9728 : 9729;
                }
                else
                {
                    minFilter = filterMode == FilterMode.Point ? 9984 : 9987;
                }

                int wrapS = ToGltfWrap(wrapU);
                int wrapT = ToGltfWrap(wrapV);

                string key = magFilter + "|" + minFilter + "|" + wrapS + "|" + wrapT;
                int samplerIndex;
                if (_samplerMap.TryGetValue(key, out samplerIndex))
                {
                    return samplerIndex;
                }

                SamplerData sampler = new SamplerData();
                sampler.magFilter = magFilter;
                sampler.minFilter = minFilter;
                sampler.wrapS = wrapS;
                sampler.wrapT = wrapT;
                samplers.Add(sampler);

                samplerIndex = samplers.Count - 1;
                _samplerMap.Add(key, samplerIndex);
                return samplerIndex;
            }

            private string ExportRuntimeTexture(Texture texture)
            {
                int width = texture.width;
                int height = texture.height;
                if (width <= 0 || height <= 0)
                {
                    return null;
                }

                Texture2D readableTex;
                if (texture is RenderTexture renderTexture)
                {
                    readableTex = GetReadableTexture(renderTexture, false);
                }
                else if (texture is Texture2D texture2D)
                {
                    readableTex = GetReadableTexture(texture2D, false);
                }
                else
                {
                    return null;
                }

                if (readableTex == null)
                {
                    return null;
                }

                byte[] pngData = readableTex.EncodeToPNG();
                Object.DestroyImmediate(readableTex);

                if (pngData == null || pngData.Length == 0)
                {
                    return null;
                }

                string textureName = !string.IsNullOrEmpty(texture.name) ? texture.name : "texture";
                textureName = SanitizeFileName(textureName);
                string fileName = _charName + "_" + textureName + "_" + _runtimeTextureCounter.ToString(CultureInfo.InvariantCulture) + ".png";
                _runtimeTextureCounter++;

                string outputPath = Path.Combine(_outputFolder, fileName).Replace('\\', '/');
                string absolutePath = GetAbsolutePathFromAssetPath(outputPath);
                File.WriteAllBytes(absolutePath, pngData);

                return fileName;
            }

            private static Texture2D GetReadableTexture(RenderTexture texture, bool isNormal)
            {
                RenderTexture tmp;

                if (isNormal)
                {
                    tmp = RenderTexture.GetTemporary(
                        texture.width,
                        texture.height,
                        0,
                        RenderTextureFormat.Default,
                        RenderTextureReadWrite.Linear);
                }
                else
                {
                    tmp = RenderTexture.GetTemporary(
                        texture.width,
                        texture.height,
                        0,
                        RenderTextureFormat.Default,
                        RenderTextureReadWrite.sRGB);
                }

                Graphics.Blit(texture, tmp);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;

                Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, isNormal);
                readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
                readableTexture.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);

                return readableTexture;
            }

            private static Texture2D GetReadableTexture(Texture2D texture, bool isNormal)
            {
                RenderTexture tmp;

                if (isNormal)
                {
                    tmp = RenderTexture.GetTemporary(
                        texture.width,
                        texture.height,
                        0,
                        RenderTextureFormat.Default,
                        RenderTextureReadWrite.Linear);
                }
                else
                {
                    tmp = RenderTexture.GetTemporary(
                        texture.width,
                        texture.height,
                        0,
                        RenderTextureFormat.Default,
                        RenderTextureReadWrite.sRGB);
                }

                Graphics.Blit(texture, tmp);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;

                Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, isNormal);
                readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
                readableTexture.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);

                return readableTexture;
            }

            private static string SanitizeFileName(string name)
            {
                char[] invalid = Path.GetInvalidFileNameChars();
                StringBuilder sb = new StringBuilder(name.Length);
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if (Array.IndexOf(invalid, c) < 0)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append('_');
                    }
                }
                return sb.ToString();
            }

            private static int ToGltfWrap(TextureWrapMode wrap)
            {
                switch (wrap)
                {
                    case TextureWrapMode.Clamp:
                        return 33071;
                    case TextureWrapMode.Mirror:
                        return 33648;
                    default:
                        return 10497;
                }
            }

            public string ToJson()
            {
                StringBuilder sb = new StringBuilder(131072);
                sb.Append("{");

                bool firstRoot = true;
                WriteName(sb, ref firstRoot, "asset");
                sb.Append("{\"version\":\"2.0\",\"generator\":\"UMA glTF Exporter\"}");

                WriteProperty(sb, ref firstRoot, "scene", scene);
                WriteScenes(sb, ref firstRoot);
                WriteNodes(sb, ref firstRoot);
                WriteMeshes(sb, ref firstRoot);
                WriteSkins(sb, ref firstRoot);
                WriteMaterials(sb, ref firstRoot);
                WriteTextures(sb, ref firstRoot);
                WriteImages(sb, ref firstRoot);
                WriteSamplers(sb, ref firstRoot);
                WriteAccessors(sb, ref firstRoot);
                WriteBufferViews(sb, ref firstRoot);
                WriteBuffers(sb, ref firstRoot);

                sb.Append("}");
                return sb.ToString();
            }

            private void WriteScenes(StringBuilder sb, ref bool firstRoot)
            {
                if (scenes.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "scenes");
                sb.Append("[");
                for (int i = 0; i < scenes.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    SceneData s = scenes[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", s.name);
                    WriteIntArray(sb, ref first, "nodes", s.nodes);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteNodes(StringBuilder sb, ref bool firstRoot)
            {
                if (nodes.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "nodes");
                sb.Append("[");
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    NodeData n = nodes[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", n.name);
                    WriteIntArray(sb, ref first, "children", n.children);
                    WriteFloatArray(sb, ref first, "matrix", n.matrix);
                    if (n.mesh >= 0) WriteProperty(sb, ref first, "mesh", n.mesh);
                    if (n.skin >= 0) WriteProperty(sb, ref first, "skin", n.skin);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteMeshes(StringBuilder sb, ref bool firstRoot)
            {
                if (meshes.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "meshes");
                sb.Append("[");
                for (int i = 0; i < meshes.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    MeshData m = meshes[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", m.name);
                    WriteName(sb, ref first, "primitives");
                    sb.Append("[");
                    for (int p = 0; p < m.primitives.Count; p++)
                    {
                        if (p > 0) sb.Append(",");
                        PrimitiveData primitive = m.primitives[p];
                        sb.Append("{");
                        bool primitiveFirst = true;

                        WriteName(sb, ref primitiveFirst, "attributes");
                        sb.Append("{");
                        bool attrFirst = true;
                        WriteProperty(sb, ref attrFirst, "POSITION", primitive.position);
                        if (primitive.normal >= 0) WriteProperty(sb, ref attrFirst, "NORMAL", primitive.normal);
                        if (primitive.tangent >= 0) WriteProperty(sb, ref attrFirst, "TANGENT", primitive.tangent);
                        if (primitive.texcoord0 >= 0) WriteProperty(sb, ref attrFirst, "TEXCOORD_0", primitive.texcoord0);
                        if (primitive.joints0 >= 0) WriteProperty(sb, ref attrFirst, "JOINTS_0", primitive.joints0);
                        if (primitive.weights0 >= 0) WriteProperty(sb, ref attrFirst, "WEIGHTS_0", primitive.weights0);
                        sb.Append("}");

                        WriteProperty(sb, ref primitiveFirst, "indices", primitive.indices);
                        if (primitive.material >= 0) WriteProperty(sb, ref primitiveFirst, "material", primitive.material);
                        WriteProperty(sb, ref primitiveFirst, "mode", primitive.mode);
                        sb.Append("}");
                    }
                    sb.Append("]");
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteSkins(StringBuilder sb, ref bool firstRoot)
            {
                if (skins.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "skins");
                sb.Append("[");
                for (int i = 0; i < skins.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    SkinData skin = skins[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", skin.name);
                    WriteProperty(sb, ref first, "inverseBindMatrices", skin.inverseBindMatrices);
                    WriteIntArray(sb, ref first, "joints", skin.joints);
                    if (skin.skeleton >= 0) WriteProperty(sb, ref first, "skeleton", skin.skeleton);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteMaterials(StringBuilder sb, ref bool firstRoot)
            {
                if (materials.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "materials");
                sb.Append("[");
                for (int i = 0; i < materials.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    MaterialData m = materials[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", m.name);

                    if (m.pbrMetallicRoughness != null)
                    {
                        WriteName(sb, ref first, "pbrMetallicRoughness");
                        sb.Append("{");
                        bool pbrFirst = true;
                        if (m.pbrMetallicRoughness.baseColorFactor != null)
                        {
                            WriteFloatArray(sb, ref pbrFirst, "baseColorFactor", m.pbrMetallicRoughness.baseColorFactor);
                        }
                        if (m.pbrMetallicRoughness.baseColorTexture != null)
                        {
                            WriteTextureInfo(sb, ref pbrFirst, "baseColorTexture", m.pbrMetallicRoughness.baseColorTexture);
                        }
                        WriteFloatProperty(sb, ref pbrFirst, "metallicFactor", m.pbrMetallicRoughness.metallicFactor);
                        WriteFloatProperty(sb, ref pbrFirst, "roughnessFactor", m.pbrMetallicRoughness.roughnessFactor);
                        sb.Append("}");
                    }

                    if (m.normalTexture != null)
                    {
                        WriteNormalTextureInfo(sb, ref first, "normalTexture", m.normalTexture);
                    }
                    if (m.occlusionTexture != null)
                    {
                        WriteOcclusionTextureInfo(sb, ref first, "occlusionTexture", m.occlusionTexture);
                    }
                    if (m.emissiveTexture != null)
                    {
                        WriteTextureInfo(sb, ref first, "emissiveTexture", m.emissiveTexture);
                    }
                    if (m.emissiveFactor != null)
                    {
                        WriteFloatArray(sb, ref first, "emissiveFactor", m.emissiveFactor);
                    }
                    if (!string.IsNullOrEmpty(m.alphaMode))
                    {
                        WriteString(sb, ref first, "alphaMode", m.alphaMode);
                    }
                    if (m.alphaMode == "MASK")
                    {
                        WriteFloatProperty(sb, ref first, "alphaCutoff", m.alphaCutoff);
                    }
                    if (m.doubleSided)
                    {
                        WriteBoolProperty(sb, ref first, "doubleSided", true);
                    }
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteTextures(StringBuilder sb, ref bool firstRoot)
            {
                if (textures.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "textures");
                sb.Append("[");
                for (int i = 0; i < textures.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    TextureData t = textures[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", t.name);
                    WriteProperty(sb, ref first, "sampler", t.sampler);
                    WriteProperty(sb, ref first, "source", t.source);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteImages(StringBuilder sb, ref bool firstRoot)
            {
                if (images.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "images");
                sb.Append("[");
                for (int i = 0; i < images.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    ImageData image = images[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "name", image.name);
                    if (image.bufferView >= 0)
                    {
                        WriteProperty(sb, ref first, "bufferView", image.bufferView);
                    }
                    WriteString(sb, ref first, "uri", image.uri);
                    WriteString(sb, ref first, "mimeType", image.mimeType);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteSamplers(StringBuilder sb, ref bool firstRoot)
            {
                if (samplers.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "samplers");
                sb.Append("[");
                for (int i = 0; i < samplers.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    SamplerData sampler = samplers[i];
                    sb.Append("{");
                    bool first = true;
                    WriteProperty(sb, ref first, "magFilter", sampler.magFilter);
                    WriteProperty(sb, ref first, "minFilter", sampler.minFilter);
                    WriteProperty(sb, ref first, "wrapS", sampler.wrapS);
                    WriteProperty(sb, ref first, "wrapT", sampler.wrapT);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteAccessors(StringBuilder sb, ref bool firstRoot)
            {
                if (accessors.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "accessors");
                sb.Append("[");
                for (int i = 0; i < accessors.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    AccessorData accessor = accessors[i];
                    sb.Append("{");
                    bool first = true;
                    WriteProperty(sb, ref first, "bufferView", accessor.bufferView);
                    WriteProperty(sb, ref first, "componentType", accessor.componentType);
                    WriteProperty(sb, ref first, "count", accessor.count);
                    WriteString(sb, ref first, "type", accessor.type);
                    if (accessor.min != null) WriteFloatArray(sb, ref first, "min", accessor.min);
                    if (accessor.max != null) WriteFloatArray(sb, ref first, "max", accessor.max);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteBufferViews(StringBuilder sb, ref bool firstRoot)
            {
                if (bufferViews.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "bufferViews");
                sb.Append("[");
                for (int i = 0; i < bufferViews.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    BufferViewData view = bufferViews[i];
                    sb.Append("{");
                    bool first = true;
                    WriteProperty(sb, ref first, "buffer", view.buffer);
                    WriteProperty(sb, ref first, "byteOffset", view.byteOffset);
                    WriteProperty(sb, ref first, "byteLength", view.byteLength);
                    if (view.byteStride > 0) WriteProperty(sb, ref first, "byteStride", view.byteStride);
                    if (view.target > 0) WriteProperty(sb, ref first, "target", view.target);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private void WriteBuffers(StringBuilder sb, ref bool firstRoot)
            {
                if (buffers.Count == 0)
                {
                    return;
                }

                WriteName(sb, ref firstRoot, "buffers");
                sb.Append("[");
                for (int i = 0; i < buffers.Count; i++)
                {
                    if (i > 0) sb.Append(",");
                    BufferData buffer = buffers[i];
                    sb.Append("{");
                    bool first = true;
                    WriteString(sb, ref first, "uri", buffer.uri);
                    WriteProperty(sb, ref first, "byteLength", buffer.byteLength);
                    sb.Append("}");
                }
                sb.Append("]");
            }

            private static void WriteTextureInfo(StringBuilder sb, ref bool first, string name, TextureInfoData info)
            {
                WriteName(sb, ref first, name);
                sb.Append("{");
                bool childFirst = true;
                WriteProperty(sb, ref childFirst, "index", info.index);
                if (info.texCoord != 0) WriteProperty(sb, ref childFirst, "texCoord", info.texCoord);
                sb.Append("}");
            }

            private static void WriteNormalTextureInfo(StringBuilder sb, ref bool first, string name, NormalTextureInfoData info)
            {
                WriteName(sb, ref first, name);
                sb.Append("{");
                bool childFirst = true;
                WriteProperty(sb, ref childFirst, "index", info.index);
                if (!Mathf.Approximately(info.scale, 1f)) WriteFloatProperty(sb, ref childFirst, "scale", info.scale);
                sb.Append("}");
            }

            private static void WriteOcclusionTextureInfo(StringBuilder sb, ref bool first, string name, OcclusionTextureInfoData info)
            {
                WriteName(sb, ref first, name);
                sb.Append("{");
                bool childFirst = true;
                WriteProperty(sb, ref childFirst, "index", info.index);
                if (!Mathf.Approximately(info.strength, 1f)) WriteFloatProperty(sb, ref childFirst, "strength", info.strength);
                sb.Append("}");
            }

            private static void WriteName(StringBuilder sb, ref bool first, string name)
            {
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                sb.Append("\"");
                sb.Append(name);
                sb.Append("\":");
            }

            private static void WriteProperty(StringBuilder sb, ref bool first, string name, int value)
            {
                WriteName(sb, ref first, name);
                sb.Append(value.ToString(CultureInfo.InvariantCulture));
            }

            private static void WriteFloatProperty(StringBuilder sb, ref bool first, string name, float value)
            {
                WriteName(sb, ref first, name);
                sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
            }

            private static void WriteBoolProperty(StringBuilder sb, ref bool first, string name, bool value)
            {
                WriteName(sb, ref first, name);
                sb.Append(value ? "true" : "false");
            }

            private static void WriteString(StringBuilder sb, ref bool first, string name, string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                WriteName(sb, ref first, name);
                sb.Append("\"");
                AppendEscapedString(sb, value);
                sb.Append("\"");
            }

            private static void WriteIntArray(StringBuilder sb, ref bool first, string name, int[] values)
            {
                if (values == null || values.Length == 0)
                {
                    return;
                }

                WriteName(sb, ref first, name);
                sb.Append("[");
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append("]");
            }

            private static void WriteFloatArray(StringBuilder sb, ref bool first, string name, float[] values)
            {
                if (values == null || values.Length == 0)
                {
                    return;
                }

                WriteName(sb, ref first, name);
                sb.Append("[");
                for (int i = 0; i < values.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    sb.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append("]");
            }

            private static void AppendEscapedString(StringBuilder sb, string value)
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\\': sb.Append("\\\\"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default: sb.Append(c); break;
                    }
                }
            }
        }

        private static Texture GetBaseColorTexture(Material material)
        {
            Texture tex = GetTextureByExactName(material, "_BaseMap");
            if (tex != null) return tex;

            tex = GetTextureByExactName(material, "_MainTex");
            if (tex != null) return tex;

            string[] props = material.GetTexturePropertyNames();
            for (int i = 0; i < props.Length; i++)
            {
                string prop = props[i];
                if (string.IsNullOrEmpty(prop))
                {
                    continue;
                }

                string lower = prop.ToLowerInvariant();
                if (lower.Contains("base") || lower.Contains("albedo") || lower.Contains("diffuse"))
                {
                    tex = material.GetTexture(prop);
                    if (tex != null)
                    {
                        return tex;
                    }
                }
            }

            return null;
        }

        private static Texture GetNormalTexture(Material material)
        {
            Texture tex = GetTextureByExactName(material, "_BumpMap");
            if (tex != null) return tex;

            tex = GetTextureByExactName(material, "_NormalMap");
            if (tex != null) return tex;

            string[] props = material.GetTexturePropertyNames();
            for (int i = 0; i < props.Length; i++)
            {
                string prop = props[i];
                if (string.IsNullOrEmpty(prop))
                {
                    continue;
                }

                string lower = prop.ToLowerInvariant();
                if (lower.Contains("normal") || lower.Contains("bump"))
                {
                    tex = material.GetTexture(prop);
                    if (tex != null)
                    {
                        return tex;
                    }
                }
            }

            return null;
        }

        private static Texture GetTextureByExactName(Material material, string propertyName)
        {
            if (material == null || string.IsNullOrEmpty(propertyName) || !material.HasProperty(propertyName))
            {
                return null;
            }
            return material.GetTexture(propertyName);
        }

        private static bool IsAlphaCutout(Material material)
        {
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (!string.IsNullOrEmpty(shaderName) && shaderName.IndexOf("cutout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f)
            {
                return true;
            }

            return material.HasProperty("_Cutoff") && material.GetFloat("_Cutoff") > 0f && material.renderQueue == (int)RenderQueue.AlphaTest;
        }

        private static bool IsAlphaBlend(Material material)
        {
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (!string.IsNullOrEmpty(shaderName) && shaderName.IndexOf("alpha", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (material.renderQueue >= (int)RenderQueue.Transparent)
            {
                return true;
            }

            return material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f;
        }

        private sealed class BinaryBuffer
        {
            private readonly MemoryStream _stream = new MemoryStream();

            public int Length
            {
                get { return (int)_stream.Length; }
            }

            public int Align(int alignment)
            {
                while ((_stream.Length % alignment) != 0)
                {
                    _stream.WriteByte(0);
                }
                return (int)_stream.Position;
            }

            public void Write(byte[] bytes)
            {
                _stream.Write(bytes, 0, bytes.Length);
            }

            public byte[] ToArray()
            {
                return _stream.ToArray();
            }
        }

        private sealed class DedupedSkin
        {
            public bool HasSkinning;
            public List<Transform> uniqueBones;
            public List<Matrix4x4> uniqueBindPoses;
            public ushort[] joints0;
            public float[] weights0;
        }

        private sealed class SceneData
        {
            public string name;
            public int[] nodes;
        }

        private sealed class NodeData
        {
            public string name;
            public int[] children;
            public float[] matrix;
            public int mesh = -1;
            public int skin = -1;
        }

        private sealed class MeshData
        {
            public string name;
            public readonly List<PrimitiveData> primitives = new List<PrimitiveData>();
        }

        private sealed class PrimitiveData
        {
            public int position = -1;
            public int normal = -1;
            public int tangent = -1;
            public int texcoord0 = -1;
            public int joints0 = -1;
            public int weights0 = -1;
            public int indices = -1;
            public int material = -1;
            public int mode = PrimitiveModeTriangles;
        }

        private sealed class SkinData
        {
            public string name;
            public int inverseBindMatrices = -1;
            public int[] joints;
            public int skeleton = -1;
        }

        private sealed class MaterialData
        {
            public string name;
            public PbrMetallicRoughnessData pbrMetallicRoughness;
            public NormalTextureInfoData normalTexture;
            public OcclusionTextureInfoData occlusionTexture;
            public TextureInfoData emissiveTexture;
            public float[] emissiveFactor;
            public string alphaMode;
            public float alphaCutoff = 0.5f;
            public bool doubleSided;
        }

        private sealed class PbrMetallicRoughnessData
        {
            public float[] baseColorFactor;
            public TextureInfoData baseColorTexture;
            public float metallicFactor;
            public float roughnessFactor = 1f;
        }

        private class TextureInfoData
        {
            public int index;
            public int texCoord;
        }

        private sealed class NormalTextureInfoData : TextureInfoData
        {
            public float scale = 1f;
        }

        private sealed class OcclusionTextureInfoData : TextureInfoData
        {
            public float strength = 1f;
        }

        private sealed class TextureData
        {
            public string name;
            public int sampler = -1;
            public int source = -1;
        }

        private sealed class ImageData
        {
            public string name;
            public string uri;
            public string mimeType;
            public int bufferView = -1;
        }

        private sealed class SamplerData
        {
            public int magFilter;
            public int minFilter;
            public int wrapS;
            public int wrapT;
        }

        private sealed class AccessorData
        {
            public int bufferView = -1;
            public int componentType;
            public int count;
            public string type;
            public float[] min;
            public float[] max;
        }

        private sealed class BufferViewData
        {
            public int buffer;
            public int byteOffset;
            public int byteLength;
            public int byteStride;
            public int target;
        }

        private sealed class BufferData
        {
            public string uri;
            public int byteLength;
        }
    }
}
#endif