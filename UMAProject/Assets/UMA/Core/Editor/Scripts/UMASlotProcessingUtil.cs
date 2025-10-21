#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Collections;
using UMA.CharacterSystem;
using System;

namespace UMA.Editors
{
    public static class UMASlotProcessingUtil
    {
        // Result object returned to the caller with all created assets
        public class SlotBuildResult
        {
            public List<SlotDataAsset> Slots = new List<SlotDataAsset>();
            public Dictionary<SlotDataAsset, OverlayDataAsset> SlotToOverlay = new Dictionary<SlotDataAsset, OverlayDataAsset>();
            public bool IsUDIM;
        }

        /// <summary>
        ///  Updates an Existing SlotDataAsset.
        /// </summary>
        public static void UpdateSlotData( SlotDataAsset slot, SkinnedMeshRenderer mesh, UMAMaterial material, SkinnedMeshRenderer prefabMesh, string rootBone, bool calcTangents, bool clearNormals, bool clearTangents)
        {
            int subMesh = slot.subMeshIndex;
            if (slot.sourceSubmeshIndex > 0)
            {
                subMesh = slot.sourceSubmeshIndex;
            }
            string path = UMAUtils.GetAssetFolder(AssetDatabase.GetAssetPath(slot));
            string assetName = slot.slotName;

            if (path.Length <= 0)
            {
                Debug.LogWarning("CreateSlotData: Path to existing asset is empty!");
                return;
            }

            GameObject tempGameObject = UnityEngine.Object.Instantiate(mesh.transform.parent.gameObject) as GameObject;
            var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer resultingSkinnedMesh = null;
            foreach (var skinnedMesh in resultingSkinnedMeshes)
            {
                if (skinnedMesh.name == mesh.name)
                {
                    resultingSkinnedMesh = skinnedMesh;
                }
            }

            Mesh resultingMesh;
            if (prefabMesh != null)
            {
                resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f,calcTangents);
                resultingSkinnedMesh.sharedMesh = resultingMesh;
                SkinnedMeshAligner.AlignBindPose(prefabMesh, resultingSkinnedMesh);
            }
            else
            {
                resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
                if (calcTangents)
                {
                    resultingMesh.RecalculateTangents();
                }
            }

            var usedBonesDictionaryUpdate = CompileUsedBonesDictionary(resultingMesh, new List<int>());
            if (usedBonesDictionaryUpdate.Count != resultingSkinnedMesh.bones.Length)
            {
                resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionaryUpdate);
            }

            string meshAssetName = path + '/' + mesh.name + "_TempMesh.asset";

            AssetDatabase.CreateAsset(resultingMesh, meshAssetName );

            tempGameObject.name = mesh.transform.parent.gameObject.name;
            Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();

            GameObject newObject = new GameObject();

            for (int i = 0; i < transformList.Length; i++)
            {
                if (transformList[i].name == rootBone)
                {
                    transformList[i].parent = newObject.transform;
                }
                else if (transformList[i].name == mesh.name)
                {
                    transformList[i].parent = newObject.transform;
                }
            }

            GameObject.DestroyImmediate(tempGameObject);
            resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (resultingSkinnedMesh)
            {
                if (usedBonesDictionaryUpdate.Count != resultingSkinnedMesh.bones.Length)
                {

                    resultingSkinnedMesh.bones = BuildNewReducedBonesList(resultingSkinnedMesh.bones, usedBonesDictionaryUpdate);
                }
                resultingSkinnedMesh.sharedMesh = resultingMesh;
            }

            string SkinnedName = path + '/' + assetName + "_TempSkinned.prefab";

            Debug.Log($"Saving prefab to {SkinnedName}");
            var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName);

            var meshgo = skinnedResult.transform.Find(mesh.name);
            var finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

            slot.UpdateMeshData(finalMeshRenderer,rootBone, false, subMesh, clearNormals,clearTangents);
            slot.meshData.SlotName = slot.slotName;
            var cloth = mesh.GetComponent<Cloth>();
            if (cloth != null)
            {
                slot.meshData.RetrieveDataFromUnityCloth(cloth);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(SkinnedName);
            AssetDatabase.DeleteAsset(meshAssetName);
        }

        // Helper: create an OverlayDataAsset for a slot using the source Unity material and UMAMaterial channels
        private static OverlayDataAsset CreateOverlayFromMaterial(SlotBuilderParameters sbp, SlotDataAsset slot, Material srcMat, int? udimNumber, string assetDir)
        {
            if (sbp.material == null)
            {
                return null;
            }

            string matName = (srcMat != null && !string.IsNullOrEmpty(srcMat.name)) ? srcMat.name : "Material";
            string overlayName = udimNumber.HasValue
                ? string.Format("{0}_{1}_UDIM{2}", slot.slotName, matName, udimNumber.Value)
                : string.Format("{0}_{1}", slot.slotName, matName);

            // Build texture list based on UMAMaterial channels from the source material
            int channelCount = (sbp.material.channels != null) ? sbp.material.channels.Length : 0;
            if (channelCount < 0) channelCount = 0;
            Texture[] newTextureList = new Texture[channelCount];
            OverlayDataAsset.OverlayBlend[] newBlend = new OverlayDataAsset.OverlayBlend[channelCount];
            for (int i = 0; i < channelCount; i++)
            {
                newBlend[i] = OverlayDataAsset.OverlayBlend.Normal;
                try
                {
                    if (srcMat != null)
                    {
                        string prop = sbp.material.channels[i].materialPropertyName;
                        if (!string.IsNullOrEmpty(prop) && srcMat.HasProperty(prop))
                        {
                            var tex = srcMat.GetTexture(prop);
                            if (tex != null)
                            {
                                newTextureList[i] = tex;
                            }
                        }
                    }
                }
                catch { }
            }

            // Compute target asset path (no unique name generation)
            string fileName = overlayName + "_overlay.asset";
            string overlayPath = sbp.useRootFolder ? (sbp.slotFolder + '/' + fileName) : (assetDir + '/' + fileName);

            // If an overlay already exists at this path, update it in place
            var existing = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath);
            if (existing != null)
            {
                Undo.RecordObject(existing, "Update OverlayDataAsset");
                existing.overlayName = overlayName;
                existing.material = sbp.material;
                // Resize arrays if needed
                if (existing.textureList == null || existing.textureList.Length != channelCount)
                {
                    existing.textureList = new Texture[channelCount];
                }
                if (existing.overlayBlend == null || existing.overlayBlend.Length != channelCount)
                {
                    existing.overlayBlend = new OverlayDataAsset.OverlayBlend[channelCount];
                }
                // Assign values
                for (int i = 0; i < channelCount; i++)
                {
                    existing.textureList[i] = newTextureList[i];
                    existing.overlayBlend[i] = newBlend[i];
                }
                EditorUtility.SetDirty(existing);
                if (sbp.addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.AddIfIndexed(existing);
                }
                return existing;
            }

            // Create a new overlay asset
            var oda = ScriptableObject.CreateInstance<OverlayDataAsset>();
            oda.overlayName = overlayName;
            oda.material = sbp.material;
            oda.textureList = newTextureList;
            oda.overlayBlend = newBlend;

            AssetDatabase.CreateAsset(oda, overlayPath);
            // Add to index if requested
            if (sbp.addToGlobalLibrary)
            {
                UMAAssetIndexer.Instance.EvilAddAsset(typeof(OverlayDataAsset), oda);
            }
            return oda;
        }

        public static SlotBuildResult CreateSlotData(SlotBuilderParameters sbp)
        {
            if (sbp.useRootFolder)
            {
                if (!System.IO.Directory.Exists(sbp.slotFolder))
                {
                    System.IO.Directory.CreateDirectory(sbp.slotFolder);
                }
            }
            else
            {
                if (!System.IO.Directory.Exists(sbp.slotFolder + '/' + sbp.assetFolder))
                {
                    System.IO.Directory.CreateDirectory(sbp.slotFolder + '/' + sbp.assetFolder);
                }

                if (!System.IO.Directory.Exists(sbp.slotFolder + '/' + sbp.assetName))
                {
                    System.IO.Directory.CreateDirectory(sbp.slotFolder + '/' + sbp.assetName);
                }
            }

            GameObject tempGameObject = UnityEngine.Object.Instantiate(sbp.slotMesh.transform.parent.gameObject) as GameObject;

            var resultingSkinnedMeshes = tempGameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            SkinnedMeshRenderer resultingSkinnedMesh = null;
            foreach (var skinnedMesh in resultingSkinnedMeshes)
            {
                if (skinnedMesh.name == sbp.slotMesh.name)
                {
                    resultingSkinnedMesh = skinnedMesh;
                }
            }

            Transform[] bones = resultingSkinnedMesh.bones;
            List<int> KeepBoneIndexes = new List<int>();

            int startBone = sbp.keepAllBones ? 1 : 0;
            for (int i = startBone; i < bones.Length; i++)
            {
                Transform t = bones[i];
                if (sbp.keepList.Contains(t.name) || sbp.keepAllBones)
                {
                    if (!string.IsNullOrEmpty(t.name))
                    {
                        KeepBoneIndexes.Add(i);
                    }
                }
            }


            Mesh resultingMesh;
            if (sbp.seamsMesh != null)
            {
                resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, sbp.seamsMesh, 0.0001f, sbp.calculateTangents);
                resultingSkinnedMesh.sharedMesh = resultingMesh;
                SkinnedMeshAligner.AlignBindPose(sbp.seamsMesh, resultingSkinnedMesh);
            }
            else
            {
                resultingMesh = (Mesh)GameObject.Instantiate(resultingSkinnedMesh.sharedMesh);
            }
            if (sbp.calculateTangents)
            {
                resultingMesh.RecalculateTangents();
            }

            // Preserve all bones in UDIM mode; otherwise optionally reduce
            if (!sbp.udimAdjustment)
            {
                var usedBonesDictionary = CompileUsedBonesDictionary(resultingMesh, KeepBoneIndexes);
                if (usedBonesDictionary.Count != resultingSkinnedMesh.bones.Length)
                {
                    resultingMesh = BuildNewReduceBonesMesh(resultingMesh, usedBonesDictionary);
                }
            }

            string theMesh = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.slotMesh.name + "_TempMesh.asset";
            if (sbp.useRootFolder)
            {
                theMesh = sbp.slotFolder + '/' + sbp.slotMesh.name + "_TempMesh.asset";
            }
            if (sbp.binarySerialization)
            {
                //Work around for mesh being serialized as project format settings (text) when binary is much faster.
                BinaryAssetWrapper binaryAsset = ScriptableObject.CreateInstance<BinaryAssetWrapper>();
                AssetDatabase.CreateAsset(binaryAsset, theMesh);
                AssetDatabase.AddObjectToAsset(resultingMesh, binaryAsset);
            }
            else
            {
                AssetDatabase.CreateAsset(resultingMesh, theMesh);
            }

            tempGameObject.name = sbp.slotMesh.transform.parent.gameObject.name;
            Transform[] transformList = tempGameObject.GetComponentsInChildren<Transform>();

            GameObject newObject = new GameObject();

            for (int i = 0; i < transformList.Length; i++)
            {
                if (!string.IsNullOrEmpty(sbp.stripBones))
                {
                    string bname = transformList[i].name;
                    if (bname.Contains(sbp.stripBones))
                    {
                        bname = bname.Replace(sbp.stripBones, "");
                      }
                      transformList[i].name = bname;
                }
                if (transformList[i].name == sbp.rootBone)
                {
                    transformList[i].parent = newObject.transform;
                }
                else if (transformList[i].name == sbp.slotMesh.name)
                {
                    transformList[i].parent = newObject.transform;
                }
            }

            resultingSkinnedMesh = newObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (resultingSkinnedMesh == null)
            {
                Debug.Log("Skinned mesh is null!!!");
                return null;
            }

            if (!sbp.udimAdjustment)
            {
                var usedBonesDictionary2 = CompileUsedBonesDictionary(resultingSkinnedMesh.sharedMesh, KeepBoneIndexes);
                if (usedBonesDictionary2.Count != resultingSkinnedMesh.bones.Length)
                {

                    resultingSkinnedMesh.bones = BuildNewReducedBonesList(resultingSkinnedMesh.bones, usedBonesDictionary2);
                }
            }
            resultingSkinnedMesh.sharedMesh = resultingMesh;

            string SkinnedName = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.assetName + "_TempSkinned.prefab";

            if (sbp.useRootFolder)
            {
                SkinnedName = sbp.slotFolder + '/' + sbp.assetName + "_TempSkinned.prefab";
            }

            var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName,out bool success);
            if (!success)
            {
                Debug.Log($"failed saving {SkinnedName} prefab"); 
            }

            SkinnedMeshRenderer finalMeshRenderer = null;

            int childCount = skinnedResult.transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = skinnedResult.transform.GetChild(i);
                if (child.name == sbp.slotMesh.name)
                {
                    if (child.GetComponent<SkinnedMeshRenderer>() != null)
                    {
                        finalMeshRenderer = child.GetComponent<SkinnedMeshRenderer>();
                        break;
                    }
                }
            }

            if (finalMeshRenderer == null)
            {
                Debug.LogWarning($"Final Mesh Renderer is null on temp object {sbp.slotMesh.name} of skinned prefab {SkinnedName}");
                return null;
            }
            if (finalMeshRenderer.sharedMesh == null)
            {
                Debug.Log("Final Mesh Renderer shareMesh is null!!!");
                finalMeshRenderer.sharedMesh = resultingMesh;
            }

            // Decide if this mesh actually uses UDIM tiles beyond (0,0)
            bool isUdimMesh = false;
            if (sbp.udimAdjustment)
            {
                var mesh = finalMeshRenderer.sharedMesh;
                var uv = mesh.uv;
                if (uv != null && uv.Length == mesh.vertexCount)
                {
                    for (int i = 0; i < uv.Length; i++)
                    {
                        int u = Mathf.FloorToInt(uv[i].x);
                        int v = Mathf.FloorToInt(uv[i].y);
                        if (u != 0 || v != 0)
                        {
                            isUdimMesh = true;
                            break;
                        }
                    }
                }
            }

            if (isUdimMesh)
            {
                var result = GenerateUDIMSlotsResult(sbp, finalMeshRenderer);
                AssetDatabase.SaveAssets();
                GameObject.DestroyImmediate(tempGameObject);
                GameObject.DestroyImmediate(newObject);
                AssetDatabase.DeleteAsset(SkinnedName);
                AssetDatabase.DeleteAsset(theMesh);
                return result;
            }

            // Track created slots and overlays
            var createdSlots = new List<SlotDataAsset>();
            var slotToOverlay = new Dictionary<SlotDataAsset, OverlayDataAsset>();
            var materialToOverlay = new Dictionary<Material, OverlayDataAsset>();
            string assetDir = sbp.useRootFolder ? sbp.slotFolder : (sbp.slotFolder + '/' + sbp.assetName);

            // Base slot
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.slotName = sbp.slotName;
            //Make sure slots get created with a name hash
            slot.nameHash = UMAUtils.StringToHash(slot.slotName);
            slot.material = sbp.material;
            slot.sourceSubmeshIndex = 0;
            try
            {
                // Non-UDIM path: ensure udimAdjustment=false in UpdateMeshData
                slot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, false, 0, sbp.clearNormals, sbp.clearTangents );
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
            TransformMeshData(slot, sbp);

            var cloth = sbp.slotMesh.GetComponent<Cloth>();
            if (cloth != null)
            {
                slot.meshData.RetrieveDataFromUnityCloth(cloth);
            }
            string slotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + sbp.slotName + "_slot.asset";
            if (sbp.useRootFolder)
            {
                slotPath = sbp.slotFolder + '/' + sbp.slotName + "_slot.asset";
            }

            SlotDataAsset OldAsset = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(slotPath);

            if (OldAsset != null)
            {
                // Overwrite existing slot in place
                string existingRootBone = slot.meshData.RootBoneName;
                UpdateSlotData(OldAsset, finalMeshRenderer, OldAsset.material, OldAsset.normalReferenceMesh, existingRootBone, true, sbp.clearNormals, sbp.clearTangents);
                EditorUtility.SetDirty(OldAsset);
                createdSlots.Add(OldAsset);
                // Replace working reference with existing for overlay mapping
                UnityEngine.Object.DestroyImmediate(slot);
                slot = OldAsset;
            }
            else
            {
                AssetDatabase.CreateAsset(slot, slotPath);
                if (sbp.addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), slot);
                }
                createdSlots.Add(slot);
            }

            // Create/overwrite overlay for submesh 0 if requested (non-UDIM reuse rule applies)
            if (sbp.createOverlays)
            {
                var srcMat0 = (sbp.slotMesh.sharedMaterials != null && sbp.slotMesh.sharedMaterials.Length > 0) ? sbp.slotMesh.sharedMaterials[0] : null;
                if (srcMat0 != null)
                {
                    var oda = CreateOverlayFromMaterial(sbp, slot, srcMat0, null, assetDir);
                    slotToOverlay[slot] = oda;
                }
                else
                {
                    // Create overlay shell with no textures if we can't determine a material
                    var oda = CreateOverlayFromMaterial(sbp, slot, null, null, assetDir);
                    slotToOverlay[slot] = oda;
                }
            }

            // Additional submeshes
            for (int i = 1; i < finalMeshRenderer.sharedMesh.subMeshCount; i++)
            {
                string theSlotName = string.Format("{0}_{1}", sbp.slotName, i);

                if (i < sbp.slotMesh.sharedMaterials.Length && sbp.nameByMaterial)
                {
                    if (!string.IsNullOrEmpty(sbp.slotMesh.sharedMaterials[i].name))
                    {
                        string titlecase = sbp.slotMesh.sharedMaterials[i].name.ToTitleCase();
                        if (!string.IsNullOrWhiteSpace(titlecase))
                        {
                            theSlotName = titlecase;
                        }
                    }
                }

                string theSlotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + theSlotName + "_slot.asset";
                if (sbp.useRootFolder)
                {
                    theSlotPath = sbp.slotFolder + '/' + theSlotName + "_slot.asset";
                }

                var existingAdditional = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(theSlotPath);
                if (existingAdditional != null)
                {
                    // Update existing submesh slot
                    string existingRootBone = slot.meshData.RootBoneName;
                    UpdateSlotData(existingAdditional, finalMeshRenderer, existingAdditional.material, existingAdditional.normalReferenceMesh, existingRootBone, true, sbp.clearNormals, sbp.clearTangents);
                    existingAdditional.sourceSubmeshIndex = i;
                    EditorUtility.SetDirty(existingAdditional);
                    createdSlots.Add(existingAdditional);

                    // Overlay for this submesh
                    if (sbp.createOverlays)
                    {
                        Material srcMat = (sbp.slotMesh.sharedMaterials != null && i < sbp.slotMesh.sharedMaterials.Length) ? sbp.slotMesh.sharedMaterials[i] : null;
                        var oda = CreateOverlayFromMaterial(sbp, existingAdditional, srcMat, null, assetDir);
                        slotToOverlay[existingAdditional] = oda;
                    }
                    continue;
                }

                // Create new additional slot
                var additionalSlot = ScriptableObject.CreateInstance<SlotDataAsset>();
                additionalSlot.slotName = theSlotName;
                additionalSlot.material = sbp.material;
                // Non-UDIM path: ensure udimAdjustment=false
                additionalSlot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, false, i, sbp.clearNormals,sbp.clearTangents);
                TransformMeshData(additionalSlot, sbp);

                additionalSlot.sourceSubmeshIndex = i;

                AssetDatabase.CreateAsset(additionalSlot, theSlotPath);
                if (sbp.addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), additionalSlot);
                }
                createdSlots.Add(additionalSlot);

                // Overlay creation for additional submeshes (non-UDIM reuse rule)
                if (sbp.createOverlays)
                {
                    Material srcMat = (sbp.slotMesh.sharedMaterials != null && i < sbp.slotMesh.sharedMaterials.Length) ? sbp.slotMesh.sharedMaterials[i] : null;
                    var oda = CreateOverlayFromMaterial(sbp, additionalSlot, srcMat, null, assetDir);
                    slotToOverlay[additionalSlot] = oda;
                }
            }
            AssetDatabase.SaveAssets();
            GameObject.DestroyImmediate(tempGameObject);
            GameObject.DestroyImmediate(newObject);

            AssetDatabase.DeleteAsset(SkinnedName);
            AssetDatabase.DeleteAsset(theMesh);

            // Build and return result. Recipe creation is handled by the caller (window)
            var resultNonUdim = new SlotBuildResult();
            resultNonUdim.Slots = createdSlots;
            resultNonUdim.SlotToOverlay = slotToOverlay;
            resultNonUdim.IsUDIM = false;
            return resultNonUdim;
        }

        private static void TransformMeshData(SlotDataAsset slot, SlotBuilderParameters sbp)
        {
            var meshData = slot.meshData;
            var Vertices = meshData.vertices;
            Vector3[] newVerts = new Vector3[meshData.vertices.Length];
            for (int i=0; i < Vertices.Length; i++)
            {
                if (sbp.rotationEnabled)
                {
                    newVerts[i] = sbp.rotation * Vertices[i];
                }
                else
                {
                    newVerts[i] = DoInversions(sbp, Vertices[i]);
                }
            }
            slot.meshData.vertices = newVerts;
        }

        public static void OptimizeSlotDataMesh(SkinnedMeshRenderer smr, List<int> KeepBonesList)
        {
            if (smr == null) return;
            var mesh = smr.sharedMesh;

            var usedBonesDictionary = CompileUsedBonesDictionary(mesh,KeepBonesList);
            var smrOldBones = smr.bones.Length;
            if (usedBonesDictionary.Count != smrOldBones)
            {
                mesh.SetBoneWeights(mesh.GetBonesPerVertex(),BuildNewBoneWeights(mesh.GetAllBoneWeights(), usedBonesDictionary));
                mesh.bindposes = BuildNewBindPoses(mesh.bindposes, usedBonesDictionary);
                EditorUtility.SetDirty(mesh);
                smr.bones = BuildNewReducedBonesList(smr.bones, usedBonesDictionary);
                EditorUtility.SetDirty(smr);
                Debug.Log(string.Format("Optimized Mesh {0} from {1} bones to {2} bones.", smr.name, smrOldBones, usedBonesDictionary.Count), smr);
            }
        }

        private static Mesh BuildNewReduceBonesMesh(Mesh sourceMesh, Dictionary<int, int> usedBonesDictionary)
        {
            Mesh newMesh = GameObject.Instantiate<Mesh>(sourceMesh);
            newMesh.SetBoneWeights(sourceMesh.GetBonesPerVertex(),BuildNewBoneWeights(sourceMesh.GetAllBoneWeights(), usedBonesDictionary));
            newMesh.bindposes = BuildNewBindPoses(sourceMesh.bindposes, usedBonesDictionary);

            return newMesh;
        }

        private static Matrix4x4[] BuildNewBindPoses(Matrix4x4[] bindPoses, Dictionary<int, int> usedBonesDictionary)
        {
            var res = new Matrix4x4[usedBonesDictionary.Count];
            foreach (var entry in usedBonesDictionary)
            {
                res[entry.Value] = bindPoses[entry.Key];
            }
            return res;
        }

        private static NativeArray<BoneWeight1> BuildNewBoneWeights(NativeArray<BoneWeight1> boneWeight, Dictionary<int, int> usedBonesDictionary)
        {
            var newBoneWeights = new BoneWeight1[boneWeight.Length];
            for (int i = 0; i < boneWeight.Length; i++)
            {
                BoneWeight1 bone = boneWeight[i];

                if (usedBonesDictionary.ContainsKey(boneWeight[i].boneIndex))
                {
                    bone.boneIndex = usedBonesDictionary[boneWeight[i].boneIndex]; 
                }
                newBoneWeights[i] = bone;
            }
            var weightsArray = new NativeArray<BoneWeight1>(newBoneWeights, Allocator.Temp);
            return weightsArray;
        }

        private static Transform[] BuildNewReducedBonesList(Transform[] bones, Dictionary<int, int> usedBonesDictionary)
        {
            var res = new Transform[usedBonesDictionary.Count];
            foreach (var entry in usedBonesDictionary)
            {
                res[entry.Value] = bones[entry.Key];
            }
            return res;
        }

        private static Dictionary<int, int> CompileUsedBonesDictionary(Mesh resultingMesh, List<int> keepBones)
        {
            var usedBones = new Dictionary<int, int>();
            var boneWeights = resultingMesh.GetAllBoneWeights();

            foreach(int boneIndex in keepBones)
            {
                usedBones.Add(boneIndex, usedBones.Count);
            }
            for (int i = 0; i < boneWeights.Length; i++)
            {
                
                BoneWeight1 boneWeight = boneWeights[i];
                if (boneWeight.weight > 0 && !usedBones.ContainsKey(boneWeight.boneIndex))
                {
                    usedBones.Add(boneWeight.boneIndex, usedBones.Count);
                }
            }
            return usedBones;
        }

        private static Vector3 DoInversions(SlotBuilderParameters sbp, Vector3 inVector)
        {
            float x = sbp.invertX ? -inVector.x : inVector.x;
            float y = sbp.invertY ? -inVector.y : inVector.y;
            float z = sbp.invertZ ? -inVector.z : inVector.z;
            return new Vector3(x, y, z);
        }

        // Helper: Generate one slot per UDIM tile per submesh, return result set
        private static SlotBuildResult GenerateUDIMSlotsResult(SlotBuilderParameters sbp, SkinnedMeshRenderer sourceRenderer)
        {
            Mesh mesh = sourceRenderer.sharedMesh;
            if (mesh == null)
            {
                Debug.LogError("[UDIM] Source mesh is null");
                return null;
            }
            if (mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
            {
                Debug.LogError("[UDIM] Mesh has no primary UVs to classify UDIM tiles");
                return null;
            }

            int tilesU = sbp.udimTilesU > 0 ? sbp.udimTilesU : 10;
            int tilesV = sbp.udimTilesV > 0 ? sbp.udimTilesV : 10;

            Vector2[] uv = mesh.uv;
            string assetDir = sbp.useRootFolder ? sbp.slotFolder : (sbp.slotFolder + '/' + sbp.assetName);

            // Track for result
            var createdSlots = new List<SlotDataAsset>();
            var slotToOverlay = new Dictionary<SlotDataAsset, OverlayDataAsset>();

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tris = mesh.GetTriangles(sub);
                // Classify triangles by tile
                var tileToTris = new Dictionary<(int u,int v), List<int>>();

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t];
                    int b = tris[t + 1];
                    int c = tris[t + 2];

                    Vector2 uva = uv[a];
                    Vector2 uvb = uv[b];
                    Vector2 uvc = uv[c];

                    int ua = Mathf.FloorToInt(uva.x);
                    int va = Mathf.FloorToInt(uva.y);
                    int ub = Mathf.FloorToInt(uvb.x);
                    int vb = Mathf.FloorToInt(uvb.y);
                    int uc = Mathf.FloorToInt(uvc.x);
                    int vc = Mathf.FloorToInt(uvc.y);

                    // Spanning across tiles? Error out
                    if (ua != ub || ua != uc || va != vb || va != vc)
                    {
                        Debug.LogError($"[UDIM] Triangle spans UDIM tiles in submesh {sub} at indices ({a},{b},{c}). Aborting.");
                        return null;
                    }

                    // Ignore outside configured UDIM grid
                    if (ua < 0 || va < 0 || ua >= tilesU || va >= tilesV)
                    {
                        continue;
                    }

                    var key = (ua, va);
                    if (!tileToTris.TryGetValue(key, out var list))
                    {
                        list = new List<int>(6);
                        tileToTris.Add(key, list);
                    }
                    list.Add(a);
                    list.Add(b);
                    list.Add(c);
                }

                // Create a slot per used tile
                foreach (var kvp in tileToTris)
                {
                    int tu = kvp.Key.u;
                    int tv = kvp.Key.v;
                    int udimNumber = 1001 + tu + (tv * 10);

                    // Base name logic like existing code
                    string baseName = (sub == 0) ? sbp.slotName : string.Format("{0}_{1}", sbp.slotName, sub);
                    if (sub < sbp.slotMesh.sharedMaterials.Length && sbp.nameByMaterial)
                    {
                        var mat = sbp.slotMesh.sharedMaterials[sub];
                        if (mat != null && !string.IsNullOrEmpty(mat.name))
                        {
                            string titlecase = mat.name.ToTitleCase();
                            if (!string.IsNullOrWhiteSpace(titlecase))
                            {
                                baseName = titlecase;
                            }
                        }
                    }
                    string theSlotName = string.Format("{0}_UDIM{1}", baseName, udimNumber);

                    // Build a temporary mesh limited to this tile for submesh -> 0
                    Mesh tileMesh = UnityEngine.Object.Instantiate(mesh);
                    tileMesh.subMeshCount = 1;
                    tileMesh.SetTriangles(kvp.Value, 0);

                    // Create temp renderer to feed UpdateMeshData
                    var go = new GameObject("UDIM_Tile_TempSMR");
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = tileMesh;
                    smr.bones = sourceRenderer.bones;
                    smr.rootBone = sourceRenderer.rootBone;

                    try
                    {
                        // Determine target path
                        string theSlotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + theSlotName + "_slot.asset";
                        if (sbp.useRootFolder)
                        {
                            theSlotPath = sbp.slotFolder + '/' + theSlotName + "_slot.asset";
                        }

                        var existing = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(theSlotPath);
                        SlotDataAsset sda;
                        if (existing != null)
                        {
                            // Update existing asset in place
                            sda = existing;
                            sda.slotName = theSlotName;
                            sda.nameHash = UMAUtils.StringToHash(sda.slotName);
                            sda.material = sbp.material;
                            sda.sourceSubmeshIndex = sub;
                            sda.UpdateMeshData(smr, sbp.rootBone, true, 0, sbp.clearNormals, sbp.clearTangents);
                            TransformMeshData(sda, sbp);
                            var cloth = sbp.slotMesh.GetComponent<Cloth>();
                            if (cloth != null)
                            {
                                sda.meshData.RetrieveDataFromUnityCloth(cloth);
                            }
                            EditorUtility.SetDirty(sda);
                        }
                        else
                        {
                            // Create a new slot asset
                            sda = ScriptableObject.CreateInstance<SlotDataAsset>();
                            sda.slotName = theSlotName;
                            sda.nameHash = UMAUtils.StringToHash(sda.slotName);
                            sda.material = sbp.material;
                            sda.sourceSubmeshIndex = sub;

                            // Normalize UVs via udimAdjustment flag
                            sda.UpdateMeshData(smr, sbp.rootBone, true, 0, sbp.clearNormals, sbp.clearTangents);
                            TransformMeshData(sda, sbp);

                            var cloth = sbp.slotMesh.GetComponent<Cloth>();
                            if (cloth != null)
                            {
                                sda.meshData.RetrieveDataFromUnityCloth(cloth);
                            }

                            AssetDatabase.CreateAsset(sda, theSlotPath);
                            if (sbp.addToGlobalLibrary)
                            {
                                UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), sda);
                            }
                        }

                        createdSlots.Add(sda);

                        // UDIM rule: always create/overwrite overlay per tile
                        if (sbp.createOverlays)
                        {
                            Material srcMat = (sbp.slotMesh.sharedMaterials != null && sub < sbp.slotMesh.sharedMaterials.Length) ? sbp.slotMesh.sharedMaterials[sub] : null;
                            var oda = CreateOverlayFromMaterial(sbp, sda, srcMat, udimNumber, assetDir);
                            slotToOverlay[sda] = oda;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        UnityEngine.Object.DestroyImmediate(go);
                        UnityEngine.Object.DestroyImmediate(tileMesh);
                        return new SlotBuildResult { Slots = createdSlots, SlotToOverlay = slotToOverlay, IsUDIM = true };
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                        UnityEngine.Object.DestroyImmediate(tileMesh);
                    }
                }
            }

            // Build result for UDIM; recipe creation happens in caller
            return new SlotBuildResult { Slots = createdSlots, SlotToOverlay = slotToOverlay, IsUDIM = true };
        }
    }
}
#endif
