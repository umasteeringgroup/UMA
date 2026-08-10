using System;
using System.Collections.Generic;
using System.IO;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    public class UMABakeUnskinRebindSlotWindow : EditorWindow
    {
        [Serializable]
        private sealed class SourceRigData
        {
            public DynamicCharacterAvatar Avatar;
            public UMAData UmaData;
            public SkinnedMeshRenderer Renderer;
            public Mesh SourceMesh;
            public Transform[] SourceBones;
            public Matrix4x4[] SourceBindPoses;
            public int[] BoneNameHashes;
            public byte[] BonesPerVertex;
            public BoneWeight1[] BoneWeights;
            public string RootBoneName;
            public int RootBoneHash;
            public Transform RootBone;
        }

        [SerializeField] private DynamicCharacterAvatar avatar;
        [SerializeField] private Transform newRigRoot;

        [MenuItem("UMA/Tools/Slot Tools/Bake Unskin Rebind/Create SlotDataAsset")]
        private static void OpenWindow()
        {
            GetWindow<UMABakeUnskinRebindSlotWindow>("Bake Rebind Slot");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Bake -> Unskin -> Rebind -> Create SlotDataAsset", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            avatar = (DynamicCharacterAvatar)EditorGUILayout.ObjectField("DynamicCharacterAvatar", avatar, typeof(DynamicCharacterAvatar), true);
            newRigRoot = (Transform)EditorGUILayout.ObjectField("New Rig Root", newRigRoot, typeof(Transform), true);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This tool expects a generated UMA avatar with exactly one active SkinnedMeshRenderer. " +
                "It bakes the current deformation, unskins it back into bind-pose space, rebinds the mesh to the supplied rig by bone name, and saves one SlotDataAsset per submesh.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(avatar == null || newRigRoot == null))
            {
                if (GUILayout.Button("Bake -> Unskin -> Rebind -> Create SlotDataAsset", GUILayout.Height(32f)))
                {
                    CreateSlotDataAssets();
                }
            }
        }

        private void CreateSlotDataAssets()
        {
            Mesh bakedMesh = null;
            Mesh reboundMesh = null;
            GameObject temporaryRendererObject = null;

            try
            {
                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Collecting source renderer data...", 0.05f);
                var sourceData = CaptureSourceRigData(avatar);
                string baseAssetPath = PromptForBaseSlotPath(sourceData);
                if (string.IsNullOrEmpty(baseAssetPath))
                {
                    return;
                }

                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Resolving new rig bones...", 0.15f);
                Dictionary<int, Transform> targetBoneLookup = BuildTargetBoneLookup(newRigRoot);
                Transform[] targetBonesInSourceOrder = BuildTargetBoneArray(sourceData, targetBoneLookup);
                Transform targetRootBone = ResolveTargetRootBone(sourceData, targetBoneLookup);

                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Baking the current deformed mesh...", 0.30f);
                bakedMesh = new Mesh
                {
                    name = sourceData.SourceMesh.name + "_Baked"
                };
                sourceData.Renderer.BakeMesh(bakedMesh);

                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Unskinning and rebinding the mesh...", 0.55f);
                Matrix4x4 targetMeshLocalToWorld = newRigRoot.localToWorldMatrix;
                reboundMesh = BuildReboundMesh(sourceData, bakedMesh, targetBonesInSourceOrder, targetMeshLocalToWorld);

                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Creating temporary renderer for UMA capture...", 0.75f);
                temporaryRendererObject = CreateTemporaryRendererObject(sourceData.Renderer, newRigRoot);
                var tempRenderer = temporaryRendererObject.GetComponent<SkinnedMeshRenderer>();
                tempRenderer.sharedMesh = reboundMesh;
                tempRenderer.sharedMaterials = sourceData.Renderer.sharedMaterials;
                tempRenderer.bones = targetBonesInSourceOrder;
                tempRenderer.rootBone = targetRootBone;
                tempRenderer.updateWhenOffscreen = true;

                EditorUtility.DisplayProgressBar("UMA Bake Rebind", "Saving SlotDataAsset assets...", 0.90f);
                List<string> createdAssetPaths = CreateSlotAssets(tempRenderer, baseAssetPath, sourceData);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (createdAssetPaths.Count > 0)
                {
                    var firstSlot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(createdAssetPaths[0]);
                    if (firstSlot != null)
                    {
                        Selection.activeObject = firstSlot;
                        EditorGUIUtility.PingObject(firstSlot);
                    }
                }

                Debug.Log($"[UMA] Created {createdAssetPaths.Count} rebound SlotDataAsset(s):\n - {string.Join("\n - ", createdAssetPaths)}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("UMA Bake Rebind", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (temporaryRendererObject != null)
                {
                    DestroyImmediate(temporaryRendererObject);
                }

                if (reboundMesh != null)
                {
                    DestroyImmediate(reboundMesh);
                }

                if (bakedMesh != null)
                {
                    DestroyImmediate(bakedMesh);
                }
            }
        }

        private static SourceRigData CaptureSourceRigData(DynamicCharacterAvatar avatar)
        {
            if (avatar == null)
            {
                throw new InvalidOperationException("Assign a DynamicCharacterAvatar before running the bake pipeline.");
            }

            UMAData umaData = avatar.umaData;
            if (umaData == null)
            {
                throw new InvalidOperationException("The selected DynamicCharacterAvatar has no UMAData. Generate the avatar before running the tool.");
            }

            SkinnedMeshRenderer[] renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                throw new InvalidOperationException("The avatar has no generated SkinnedMeshRenderer. Generate the avatar before running the tool.");
            }

            List<SkinnedMeshRenderer> liveRenderers = new List<SkinnedMeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].sharedMesh != null)
                {
                    liveRenderers.Add(renderers[i]);
                }
            }

            if (liveRenderers.Count != 1)
            {
                throw new InvalidOperationException($"Expected exactly one generated SkinnedMeshRenderer, but found {liveRenderers.Count}. This tool only supports a single generated slot/renderer at a time.");
            }

            SkinnedMeshRenderer sourceRenderer = liveRenderers[0];
            Mesh sourceMesh = sourceRenderer.sharedMesh;
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
            {
                throw new InvalidOperationException("The generated renderer does not have a valid shared mesh.");
            }

            Transform[] sourceRendererBones = sourceRenderer.bones;
            if (sourceRendererBones == null || sourceRendererBones.Length == 0)
            {
                throw new InvalidOperationException("The generated renderer has no bones to rebind.");
            }

            Matrix4x4[] sourceBindPoses = sourceMesh.bindposes;
            if (sourceBindPoses == null || sourceBindPoses.Length != sourceRendererBones.Length)
            {
                throw new InvalidOperationException("The generated mesh bindpose count does not match the renderer bone count.");
            }

            byte[] bonesPerVertex = sourceMesh.GetBonesPerVertex().ToArray();
            BoneWeight1[] boneWeights = sourceMesh.GetAllBoneWeights().ToArray();
            if (bonesPerVertex == null || bonesPerVertex.Length != sourceMesh.vertexCount)
            {
                throw new InvalidOperationException("The generated mesh does not expose a valid BonesPerVertex stream.");
            }

            UMASkeleton skeleton = umaData.skeleton;
            if (skeleton == null)
            {
                throw new InvalidOperationException("The generated UMAData has no UMASkeleton. Generate the avatar before running the tool.");
            }

            Transform[] sourceBones = new Transform[sourceRendererBones.Length];
            int[] boneNameHashes = new int[sourceRendererBones.Length];
            for (int boneIndex = 0; boneIndex < sourceRendererBones.Length; boneIndex++)
            {
                Transform rendererBone = sourceRendererBones[boneIndex];
                if (rendererBone == null)
                {
                    throw new InvalidOperationException($"Renderer bone {boneIndex} is null. The source rig must be complete before baking.");
                }

                int boneHash = UMAUtils.StringToHash(rendererBone.name);
                Transform skeletonBone = skeleton.GetBoneTransform(boneHash);
                sourceBones[boneIndex] = skeletonBone != null ? skeletonBone : rendererBone;
                boneNameHashes[boneIndex] = boneHash;
            }

            Transform rootBone = sourceRenderer.rootBone != null ? sourceRenderer.rootBone : (skeleton.GetGlobalTransform() ?? umaData.GetGlobalTransform());
            if (rootBone == null)
            {
                throw new InvalidOperationException("The source renderer does not expose a usable root bone.");
            }

            return new SourceRigData
            {
                Avatar = avatar,
                UmaData = umaData,
                Renderer = sourceRenderer,
                SourceMesh = sourceMesh,
                SourceBones = sourceBones,
                SourceBindPoses = sourceBindPoses,
                BoneNameHashes = boneNameHashes,
                BonesPerVertex = bonesPerVertex,
                BoneWeights = boneWeights,
                RootBone = rootBone,
                RootBoneName = rootBone.name,
                RootBoneHash = UMAUtils.StringToHash(rootBone.name)
            };
        }

        private static string PromptForBaseSlotPath(SourceRigData sourceData)
        {
            string defaultName = sourceData.SourceMesh != null && !string.IsNullOrEmpty(sourceData.SourceMesh.name)
                ? sourceData.SourceMesh.name + "_Rebound_slot"
                : sourceData.Avatar.name + "_Rebound_slot";

            string requestedPath = EditorUtility.SaveFilePanelInProject(
                "Save Rebound SlotDataAsset",
                defaultName,
                "asset",
                "Choose a location for the new SlotDataAsset asset.");

            if (string.IsNullOrEmpty(requestedPath))
            {
                return null;
            }

            return AssetDatabase.GenerateUniqueAssetPath(requestedPath);
        }

        private static Dictionary<int, Transform> BuildTargetBoneLookup(Transform newRigRoot)
        {
            if (newRigRoot == null)
            {
                throw new InvalidOperationException("Assign the new rig root before running the bake pipeline.");
            }

            Dictionary<int, Transform> boneLookup = new Dictionary<int, Transform>();
            Dictionary<int, Transform> duplicates = new Dictionary<int, Transform>();
            Stack<Transform> pending = new Stack<Transform>();
            pending.Push(newRigRoot);

            while (pending.Count > 0)
            {
                Transform current = pending.Pop();
                int hash = UMAUtils.StringToHash(current.name);
                if (boneLookup.ContainsKey(hash))
                {
                    duplicates[hash] = current;
                }
                else
                {
                    boneLookup.Add(hash, current);
                }

                for (int childIndex = 0; childIndex < current.childCount; childIndex++)
                {
                    pending.Push(current.GetChild(childIndex));
                }
            }

            if (duplicates.Count > 0)
            {
                foreach (var duplicate in duplicates)
                {
                    Debug.LogError($"[UMA] Duplicate target bone name detected: {duplicate.Value.name}", duplicate.Value);
                }
                throw new InvalidOperationException("The target rig contains duplicate bone names. Rebinding requires unique names so the source bone hashes can be mapped reliably.");
            }

            return boneLookup;
        }

        private static Transform[] BuildTargetBoneArray(SourceRigData sourceData, Dictionary<int, Transform> targetBoneLookup)
        {
            Transform[] targetBones = new Transform[sourceData.BoneNameHashes.Length];
            for (int boneIndex = 0; boneIndex < sourceData.BoneNameHashes.Length; boneIndex++)
            {
                int boneHash = sourceData.BoneNameHashes[boneIndex];
                if (!targetBoneLookup.TryGetValue(boneHash, out targetBones[boneIndex]) || targetBones[boneIndex] == null)
                {
                    string missingBoneName = sourceData.SourceBones[boneIndex] != null ? sourceData.SourceBones[boneIndex].name : boneHash.ToString();
                    throw new InvalidOperationException($"The target rig is missing bone '{missingBoneName}'. The new rig must contain every source bone name.");
                }
            }

            return targetBones;
        }

        private static Transform ResolveTargetRootBone(SourceRigData sourceData, Dictionary<int, Transform> targetBoneLookup)
        {
            if (!targetBoneLookup.TryGetValue(sourceData.RootBoneHash, out Transform targetRootBone) || targetRootBone == null)
            {
                throw new InvalidOperationException($"The target rig is missing the source root bone '{sourceData.RootBoneName}'. Use a rig with the same bone names, including the root bone.");
            }

            return targetRootBone;
        }

        private static Mesh BuildReboundMesh(SourceRigData sourceData, Mesh bakedMesh, Transform[] targetBones, Matrix4x4 targetMeshLocalToWorld)
        {
            if (bakedMesh == null)
            {
                throw new InvalidOperationException("BakeMesh did not produce a mesh to process.");
            }

            if (bakedMesh.vertexCount != sourceData.SourceMesh.vertexCount)
            {
                throw new InvalidOperationException("BakeMesh changed the vertex count. The pipeline requires identical vertex ordering and count.");
            }

            Vector3[] bakedVertices = bakedMesh.vertices;
            Vector3[] bakedNormals = bakedMesh.normals;
            Vector4[] bakedTangents = bakedMesh.tangents;

            bool hasNormals = bakedNormals != null && bakedNormals.Length == bakedMesh.vertexCount;
            bool hasTangents = bakedTangents != null && bakedTangents.Length == bakedMesh.vertexCount;

            Vector3[] reboundVertices = new Vector3[bakedMesh.vertexCount];
            Vector3[] reboundNormals = hasNormals ? new Vector3[bakedMesh.vertexCount] : null;
            Vector4[] reboundTangents = hasTangents ? new Vector4[bakedMesh.vertexCount] : null;

            Matrix4x4 sourceMeshLocalToWorld = sourceData.Renderer.localToWorldMatrix;
            Matrix4x4 sourceToTargetMeshSpace = targetMeshLocalToWorld.inverse * sourceMeshLocalToWorld;
            Matrix4x4[] positionMatrices = new Matrix4x4[sourceData.SourceBindPoses.Length];
            Matrix4x4[] directionMatrices = new Matrix4x4[sourceData.SourceBindPoses.Length];

            for (int boneIndex = 0; boneIndex < sourceData.SourceBindPoses.Length; boneIndex++)
            {
                // Convert each baked point from the current skinned pose back into the mesh bind space,
                // then move that bind-space point into the target mesh reference space.
                Matrix4x4 positionMatrix = sourceToTargetMeshSpace
                    * sourceData.SourceBindPoses[boneIndex].inverse
                    * sourceData.SourceBones[boneIndex].worldToLocalMatrix
                    * sourceMeshLocalToWorld;

                positionMatrices[boneIndex] = positionMatrix;
                directionMatrices[boneIndex] = positionMatrix.inverse.transpose;
            }

            int weightIndex = 0;
            for (int vertexIndex = 0; vertexIndex < bakedMesh.vertexCount; vertexIndex++)
            {
                Vector3 position = Vector3.zero;
                Vector3 normal = Vector3.zero;
                Vector3 tangent = Vector3.zero;

                int influenceCount = sourceData.BonesPerVertex[vertexIndex];
                if (influenceCount <= 0)
                {
                    throw new InvalidOperationException($"Vertex {vertexIndex} has no bone influences. A skinned UMA mesh must have at least one influence per vertex.");
                }

                for (int influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++, weightIndex++)
                {
                    BoneWeight1 weight = sourceData.BoneWeights[weightIndex];
                    if (weight.weight <= 0f)
                    {
                        continue;
                    }

                    int boneIndex = weight.boneIndex;
                    if (boneIndex < 0 || boneIndex >= positionMatrices.Length)
                    {
                        throw new InvalidOperationException($"Vertex {vertexIndex} references bone index {boneIndex}, but the rebound mesh only has {positionMatrices.Length} bindposes.");
                    }

                    position += positionMatrices[boneIndex].MultiplyPoint3x4(bakedVertices[vertexIndex]) * weight.weight;

                    if (hasNormals)
                    {
                        normal += directionMatrices[boneIndex].MultiplyVector(bakedNormals[vertexIndex]) * weight.weight;
                    }

                    if (hasTangents)
                    {
                        tangent += directionMatrices[boneIndex].MultiplyVector(new Vector3(bakedTangents[vertexIndex].x, bakedTangents[vertexIndex].y, bakedTangents[vertexIndex].z)) * weight.weight;
                    }
                }

                reboundVertices[vertexIndex] = position;

                if (hasNormals)
                {
                    reboundNormals[vertexIndex] = normal.sqrMagnitude > 0f ? normal.normalized : bakedNormals[vertexIndex];
                }

                if (hasTangents)
                {
                    Vector3 tangentDirection = tangent.sqrMagnitude > 0f
                        ? tangent.normalized
                        : new Vector3(bakedTangents[vertexIndex].x, bakedTangents[vertexIndex].y, bakedTangents[vertexIndex].z);
                    reboundTangents[vertexIndex] = new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, bakedTangents[vertexIndex].w);
                }
            }

            if (weightIndex != sourceData.BoneWeights.Length)
            {
                throw new InvalidOperationException($"Processed {weightIndex} bone weights, but the mesh exposes {sourceData.BoneWeights.Length}. The source weight streams are malformed.");
            }

            Mesh reboundMesh = new Mesh
            {
                name = sourceData.SourceMesh.name + "_Rebound",
                indexFormat = sourceData.SourceMesh.indexFormat
            };

            reboundMesh.SetVertices(new List<Vector3>(reboundVertices));
            if (hasNormals)
            {
                reboundMesh.SetNormals(new List<Vector3>(reboundNormals));
            }
            if (hasTangents)
            {
                reboundMesh.SetTangents(new List<Vector4>(reboundTangents));
            }

            CopyStaticVertexChannels(sourceData.SourceMesh, reboundMesh);
            CopySubmeshes(sourceData.SourceMesh, reboundMesh);

            // SetBoneWeights requires NativeArray; wrap the managed arrays with Temp allocation and
            // dispose immediately after the call — data is copied into the mesh before we return.
            var nativeBonesPerVertex = new NativeArray<byte>(sourceData.BonesPerVertex, Allocator.Temp);
            var nativeBoneWeights = new NativeArray<BoneWeight1>(sourceData.BoneWeights, Allocator.Temp);
            try
            {
                reboundMesh.SetBoneWeights(nativeBonesPerVertex, nativeBoneWeights);
            }
            finally
            {
                nativeBonesPerVertex.Dispose();
                nativeBoneWeights.Dispose();
            }

            Matrix4x4[] newBindPoses = new Matrix4x4[targetBones.Length];
            for (int boneIndex = 0; boneIndex < targetBones.Length; boneIndex++)
            {
                newBindPoses[boneIndex] = targetBones[boneIndex].worldToLocalMatrix * targetMeshLocalToWorld;
            }
            reboundMesh.bindposes = newBindPoses;
            reboundMesh.RecalculateBounds();

            return reboundMesh;
        }

        private static void CopyStaticVertexChannels(Mesh sourceMesh, Mesh destinationMesh)
        {
            List<Color32> colors = new List<Color32>(sourceMesh.vertexCount);
            sourceMesh.GetColors(colors);
            if (colors.Count > 0)
            {
                destinationMesh.SetColors(colors);
            }

            for (int uvChannel = 0; uvChannel < 8; uvChannel++)
            {
                List<Vector4> uvs = new List<Vector4>(sourceMesh.vertexCount);
                sourceMesh.GetUVs(uvChannel, uvs);
                if (uvs.Count > 0)
                {
                    destinationMesh.SetUVs(uvChannel, uvs);
                }
            }
        }

        private static void CopySubmeshes(Mesh sourceMesh, Mesh destinationMesh)
        {
            destinationMesh.subMeshCount = sourceMesh.subMeshCount;
            for (int submeshIndex = 0; submeshIndex < sourceMesh.subMeshCount; submeshIndex++)
            {
                MeshTopology topology = sourceMesh.GetTopology(submeshIndex);
                if (topology != MeshTopology.Triangles)
                {
                    throw new InvalidOperationException($"Submesh {submeshIndex} uses topology '{topology}'. SlotDataAsset capture expects triangle submeshes.");
                }

                destinationMesh.SetTriangles(sourceMesh.GetTriangles(submeshIndex), submeshIndex, false);
            }
        }

        private static GameObject CreateTemporaryRendererObject(SkinnedMeshRenderer sourceRenderer, Transform meshReference)
        {
            var tempObject = new GameObject(sourceRenderer.name + "_RebindCapture");
            tempObject.hideFlags = HideFlags.HideAndDontSave;

            Transform tempTransform = tempObject.transform;
            Transform meshReferenceParent = meshReference.parent;
            if (meshReferenceParent != null)
            {
                tempTransform.SetParent(meshReferenceParent, false);
            }

            tempTransform.localPosition = meshReference.localPosition;
            tempTransform.localRotation = meshReference.localRotation;
            tempTransform.localScale = meshReference.localScale;

            var tempRenderer = tempObject.AddComponent<SkinnedMeshRenderer>();
            tempRenderer.hideFlags = HideFlags.HideAndDontSave;
            return tempObject;
        }

        private static List<string> CreateSlotAssets(SkinnedMeshRenderer reboundRenderer, string baseAssetPath, SourceRigData sourceData)
        {
            List<string> createdAssetPaths = new List<string>();
            string directory = Path.GetDirectoryName(baseAssetPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Could not resolve the target asset directory for the rebound slot.");
            }
            directory = directory.Replace('\\', '/');

            string baseSlotName = Path.GetFileNameWithoutExtension(baseAssetPath);
            Mesh reboundMesh = reboundRenderer.sharedMesh;
            for (int submeshIndex = 0; submeshIndex < reboundMesh.subMeshCount; submeshIndex++)
            {
                string slotAssetPath = submeshIndex == 0
                    ? baseAssetPath
                    : AssetDatabase.GenerateUniqueAssetPath($"{directory}/{baseSlotName}_{submeshIndex}.asset");
                string slotName = Path.GetFileNameWithoutExtension(slotAssetPath);

                // Route slot serialization through UMA's normal UpdateMeshData flow so the created asset
                // matches importer-created SlotDataAsset structure instead of a custom, partial meshData.
                SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
                slot.name = slotName;
                slot.sourceSubmeshIndex = submeshIndex;
                slot.UpdateMeshData(reboundRenderer, sourceData.RootBoneName, false, submeshIndex, false, false);

                if (!UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                {
                    slot.meshData.RootBoneName = sourceData.RootBoneName;
                    slot.meshData.rootBoneHash = sourceData.RootBoneHash;
                    slot.meshData.boneNameHashes = (int[])sourceData.BoneNameHashes.Clone();
                    slot.meshData.SlotName = slot.slotName;
                }

                slot.PrepareForAssetPath(slotAssetPath, slotName);
                AssetDatabase.CreateAsset(slot, slotAssetPath);
                EditorUtility.SetDirty(slot);
                createdAssetPaths.Add(slotAssetPath);
            }

            return createdAssetPaths;
        }
    }
}
