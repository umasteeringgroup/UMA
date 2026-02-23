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
        public static SkinnedMeshRenderer finalMeshRenderer;

        // Helper: choose a triangle's UDIM tile by majority vertex membership.
        // If the triangle spans multiple tiles, it will be assigned to the tile that contains the most vertices.
        // Ties are resolved deterministically by choosing the lowest UDIM number (lowest v, then u).
        private static bool TryGetTriangleUdimTileByMajority(Vector2[] uv, int a, int b, int c, int tilesU, int tilesV, out (int u, int v) tileKey)
        {
            tileKey = default;
            if (uv == null)
            {
                return false;
            }

            (int u, int v) ta = (Mathf.FloorToInt(uv[a].x), Mathf.FloorToInt(uv[a].y));
            (int u, int v) tb = (Mathf.FloorToInt(uv[b].x), Mathf.FloorToInt(uv[b].y));
            (int u, int v) tc = (Mathf.FloorToInt(uv[c].x), Mathf.FloorToInt(uv[c].y));

            int ca = 1;
            int cb = 0;
            int cc = 0;

            if (tb.u == ta.u && tb.v == ta.v)
            {
                ca++;
            }
            else
            {
                cb = 1;
            }

            if (tc.u == ta.u && tc.v == ta.v)
            {
                ca++;
            }
            else if (cb > 0 && tc.u == tb.u && tc.v == tb.v)
            {
                cb++;
            }
            else
            {
                cc = 1;
            }

            (int u, int v) best = ta;
            int bestCount = ca;

            if (cb > bestCount)
            {
                best = tb;
                bestCount = cb;
            }
            else if (cb == bestCount && cb > 0)
            {
                if (tb.v < best.v || (tb.v == best.v && tb.u < best.u))
                {
                    best = tb;
                }
            }

            if (cc > bestCount)
            {
                best = tc;
                bestCount = cc;
            }
            else if (cc == bestCount && cc > 0)
            {
                if (tc.v < best.v || (tc.v == best.v && tc.u < best.u))
                {
                    best = tc;
                }
            }

            // Ignore outside configured UDIM grid
            if (best.u < 0 || best.v < 0 || best.u >= tilesU || best.v >= tilesV)
            {
                return false;
            }

            tileKey = best;
            return true;
        }

        // Result object returned to the caller with all created assets
        public class SlotBuildResult
        {
            public List<SlotDataAsset> Slots = new List<SlotDataAsset>();
            public Dictionary<SlotDataAsset, OverlayDataAsset> SlotToOverlay = new Dictionary<SlotDataAsset, OverlayDataAsset>();
            public bool IsUDIM;
            // New: paths to temporary assets to delete later (when in batch mode)
            public List<string> TempAssetsToDelete = new List<string>();
        }

        private class SlotPreserveData
        {
            public string[] races;
            public string[] tags;
            public bool forceKeep;
            public bool noAutoAdd;
            public bool isClippingPlane;
            public bool isSmooshable;
            public Vector3 smooshOffset;
            public Vector3 smooshExpand;
            public bool isWildCardSlot;
            public BaseUpdatedObject[] animatedBones;
            public UMAMaterial material;
            public string materialName;
            public int maxLOD;
            public bool useAtlasOverlay;
            public float overlayScale;

            public static SlotPreserveData FromSlot(SlotDataAsset slot)
            {
                if (slot == null)
                {
                    return null;
                }
                var data = new SlotPreserveData();
                if (slot.Races != null)
                {
                    data.races = (string[])slot.Races.Clone();
                }
                if (slot.tags != null)
                {
                    data.tags = (string[])slot.tags.Clone();
                }
                data.forceKeep = slot.forceKeep;
                data.noAutoAdd = slot.noAutoAdd;
                data.isClippingPlane = slot.isClippingPlane;
                data.isSmooshable = slot.isSmooshable;
                data.smooshOffset = slot.smooshOffset;
                data.smooshExpand = slot.smooshExpand;
                data.isWildCardSlot = slot.isWildCardSlot;
                if (slot.animatedBones != null)
                {
                    data.animatedBones = (BaseUpdatedObject[])slot.animatedBones.Clone();
                }
                data.material = slot.material;
                data.materialName = slot.materialName;
                data.maxLOD = slot.maxLOD;
                data.useAtlasOverlay = slot.useAtlasOverlay;
                data.overlayScale = slot.overlayScale;
                return data;
            }

            public void ApplyTo(SlotDataAsset slot)
            {
                if (slot == null)
                {
                    return;
                }
                if (races != null)
                {
                    slot.Races = (string[])races.Clone();
                }
                if (tags != null)
                {
                    slot.tags = (string[])tags.Clone();
                }
                slot.forceKeep = forceKeep;
                slot.noAutoAdd = noAutoAdd;
                slot.isClippingPlane = isClippingPlane;
                slot.isSmooshable = isSmooshable;
                slot.smooshOffset = smooshOffset;
                slot.smooshExpand = smooshExpand;
                slot.isWildCardSlot = isWildCardSlot;
                if (animatedBones != null)
                {
                    slot.animatedBones = (BaseUpdatedObject[])animatedBones.Clone();
                }
                slot.material = material;
                slot.materialName = materialName;
                slot.maxLOD = maxLOD;
                slot.useAtlasOverlay = useAtlasOverlay;
                slot.overlayScale = overlayScale;
            }
        }

        // Helper: copy LOD ranges from a source mesh submesh into a SlotDataAsset's meshData submesh
        private static void CopyLodRangesFromSourceMesh(SlotDataAsset sda, int targetSubmeshIndex, Mesh sourceMesh, int sourceSubmeshIndex)
        {
#if UNITY_6000_2_OR_NEWER
            if (sda == null || sda.meshData == null || sda.meshData.submeshes == null) return;
            if (targetSubmeshIndex < 0 || targetSubmeshIndex >= sda.meshData.submeshes.Length) return;
            if (sourceMesh == null) return;
            int lodCount = sourceMesh.lodCount;
            if (lodCount <= 0) return;

            var ranges = new List<UMA.UMALodRange>(lodCount);
            for (int l = 0; l < lodCount; l++)
            {
                Debug.Log("Processing LOD " + l + " for slot " + sda.slotName);
                var lor = sourceMesh.GetLod(sourceSubmeshIndex, l);
                ranges.Add(new UMA.UMALodRange(lor));
            }
            sda.meshData.submeshes[targetSubmeshIndex].SetLodRanges(ranges);
#endif
        }

        private static void ClearInternalLods(SlotDataAsset slot, int targetSubmeshIndex)
        {
#if UNITY_6000_2_OR_NEWER
            if (slot == null || slot.meshData == null || slot.meshData.submeshes == null)
            {
                return;
            }
            if (targetSubmeshIndex < 0 || targetSubmeshIndex >= slot.meshData.submeshes.Length)
            {
                return;
            }
            var smt = slot.meshData.submeshes[targetSubmeshIndex];
            if (smt == null)
            {
                return;
            }
            if (smt.lodRanges != null && smt.lodRanges.Count > 0)
            {
                smt.lodRanges = null;
                EditorUtility.SetDirty(slot);
            }
#endif
        }

        private static void GenerateSlotLodsIfEnabled(SlotBuilderParameters sbp, SlotDataAsset slot)
        {
            if (!sbp.generateSlotLods)
            {
                return;
            }
            if (slot == null)
            {
                return;
            }

            try
            {
                var md = slot.meshData;
                int lodsBefore = (md != null && md.submeshes != null && md.submeshes.Length > 0 && md.submeshes[0] != null) ? md.submeshes[0].LODCount() : -1;
                Debug.Log($"[SlotLOD] BEFORE slot='{slot.slotName}' (new slots use submesh0) useUnity={sbp.useUnityLodGenerator} lodCount={lodsBefore}");
            }
            catch { }

            Debug.Log(string.Format("[SlotLOD] Generating internal LODs for slot='{0}' maxLevels={1} minTris={2} reduction={3} preserveBorders={4} borderWeight={5}",
                slot.slotName,
                sbp.slotLodMaxLevels,
                sbp.slotLodMinTriangles,
                sbp.slotLodTargetReductionPerLevel,
                sbp.slotLodPreserveBoundaryEdges,
                sbp.slotLodBoundaryWeight));

            // Persist the parameters used for LOD generation onto the slot (editor-only) so Updates can reuse them.
            try
            {
                var snap = new SlotDataAsset.SlotBuilderParametersSnapshot();
                snap.generateSlotLods = sbp.generateSlotLods;
                snap.slotLodMaxLevels = sbp.slotLodMaxLevels;
                snap.slotLodMinTriangles = sbp.slotLodMinTriangles;
                snap.slotLodTargetReductionPerLevel = sbp.slotLodTargetReductionPerLevel;
                snap.slotLodPreserveBoundaryEdges = sbp.slotLodPreserveBoundaryEdges;
                snap.slotLodBoundaryWeight = sbp.slotLodBoundaryWeight;
                slot.SetSlotBuilderParamsSnapshot(snap);
                EditorUtility.SetDirty(slot);
            }
            catch { }

          var options = new SlotLodGenerator.LodGenOptions();
            options.MaxLodLevels = Mathf.Clamp(sbp.slotLodMaxLevels > 0 ? sbp.slotLodMaxLevels : 8, 1, 8);
            options.MinTriangles = Mathf.Max(0, sbp.slotLodMinTriangles > 0 ? sbp.slotLodMinTriangles : 256);
            options.TargetReductionPerLevel = Mathf.Clamp01(sbp.slotLodTargetReductionPerLevel > 0f ? sbp.slotLodTargetReductionPerLevel : 0.5f);
            options.PreserveBoundaryEdges = sbp.slotLodPreserveBoundaryEdges;
            options.BoundaryWeight = Mathf.Max(0f, sbp.slotLodBoundaryWeight);
#if UNITY_6000_2_OR_NEWER
            options.useUnityLodGenerator = sbp.useUnityLodGenerator;
#endif

            try
            {
                SlotLodGenerator.GenerateAndApplyLods(slot, options);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[SlotLOD] Failed generating internal slot LODs: " + ex.Message);
            }

            try
            {
                var md = slot.meshData;
                int lodsAfter = (md != null && md.submeshes != null && md.submeshes.Length > 0 && md.submeshes[0] != null) ? md.submeshes[0].LODCount() : -1;
                Debug.Log($"[SlotLOD] AFTER slot='{slot.slotName}' (new slots use submesh0) useUnity={sbp.useUnityLodGenerator} lodCount={lodsAfter}");
            }
            catch { }
        }

        /// <summary>
        /// Updates an Existing SlotDataAsset.
        /// </summary>
        public static void UpdateSlotData(SlotDataAsset slot, SkinnedMeshRenderer mesh, UMAMaterial material, SkinnedMeshRenderer prefabMesh, string rootBone, bool calcTangents, bool clearNormals, bool clearTangents)
        {
#if UNITY_EDITOR
            // If this slot was built with Slot Builder LOD settings, reuse those defaults on update.
            // This keeps updates consistent even when called from other tools.
            try
            {
                if (slot != null && slot.HasSlotBuilderParamsSnapshot)
                {
                    var snap = slot.GetSlotBuilderParamsSnapshot();
                    if (snap.hasData)
                    {
                        // We only override options that are commonly driven by the builder.
                        // Tangent recomputation is expensive: only force it when LOD generation was enabled.
                        if (snap.generateSlotLods)
                        {
                            calcTangents = true;
                        }
                    }
                }
            }
            catch { }
#endif

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
                resultingMesh = SeamRemoval.PerformSeamRemoval(resultingSkinnedMesh, prefabMesh, 0.0001f, calcTangents);
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

            AssetDatabase.CreateAsset(resultingMesh, meshAssetName);

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
            finalMeshRenderer = meshgo.GetComponent<SkinnedMeshRenderer>();

            slot.UpdateMeshData(finalMeshRenderer, rootBone, false, subMesh, clearNormals, clearTangents);
            slot.meshData.SlotName = slot.slotName;
#if UNITY_6000_2_OR_NEWER
            // Try to carry LOD ranges from the source mesh (if available)
            var srcMesh = mesh != null ? mesh.sharedMesh : null;
            if (srcMesh != null)
            {
                CopyLodRangesFromSourceMesh(slot, 0, srcMesh, subMesh);
            }
#endif
            var cloth = mesh.GetComponent<Cloth>();
            if (cloth != null)
            {
                slot.meshData.RetrieveDataFromUnityCloth(cloth);
            }

#if UNITY_EDITOR
            // If the slot was previously built with internal LODs, regenerate them after updating mesh data.
            try
            {
                if (slot != null && slot.HasSlotBuilderParamsSnapshot)
                {
                    var snap = slot.GetSlotBuilderParamsSnapshot();
                    if (snap.hasData && snap.generateSlotLods)
                    {
                        var sbp = new SlotBuilderParameters();
                        sbp.generateSlotLods = true;
                        sbp.slotLodMaxLevels = snap.slotLodMaxLevels;
                        sbp.slotLodMinTriangles = snap.slotLodMinTriangles;
                        sbp.slotLodTargetReductionPerLevel = snap.slotLodTargetReductionPerLevel;
                        sbp.slotLodPreserveBoundaryEdges = snap.slotLodPreserveBoundaryEdges;
                        sbp.slotLodBoundaryWeight = snap.slotLodBoundaryWeight;
                        GenerateSlotLodsIfEnabled(sbp, slot);
                    }
                }
            }
            catch { }
#endif
            AssetDatabase.SaveAssets();
            // Always clean up here; batch deferral is handled only in CreateSlotData
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

            string overlayName = slot.slotName;
            if (sbp.nameByMaterial)
            {
                string matName = (srcMat != null && !string.IsNullOrEmpty(srcMat.name)) ? srcMat.name : "Material";
                matName = matName.Replace(" (Instance)", "").Replace(" ", "_");
                overlayName += "_" + matName;
            }
            if (sbp.appendTypeToName)
            {
                overlayName += "_overlay";
            }


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
            string fileName = overlayName+".asset";
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

            var skinnedResult = PrefabUtility.SaveAsPrefabAsset(newObject, SkinnedName, out bool success);
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
                if (sbp.batchMode)
                {
                    // Defer deletion to caller (batch window)
                    if (result != null)
                    {
                        result.TempAssetsToDelete.Add(SkinnedName);
                        result.TempAssetsToDelete.Add(theMesh);
                    }
                }
                else
                {
                    AssetDatabase.DeleteAsset(SkinnedName);
                    AssetDatabase.DeleteAsset(theMesh);
                }
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
                slot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, false, 0, sbp.clearNormals, sbp.clearTangents);
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
            if (sbp.batchMode)
            {
                if (OldAsset != null)
                {
                    AssetDatabase.DeleteAsset(slotPath);
                    OldAsset = null;
                }
            }

            if (sbp.alwaysRecreateSlots)
            {
                if (OldAsset != null)
                {
                    AssetDatabase.DeleteAsset(slotPath);
                    OldAsset = null;
                }
            }

            if (OldAsset != null)
            {
                // Overwrite existing slot in place
                string existingRootBone = slot.meshData.RootBoneName;
                UpdateSlotData(OldAsset, finalMeshRenderer, OldAsset.material, OldAsset.normalReferenceMesh, existingRootBone, true, sbp.clearNormals, sbp.clearTangents);
                EditorUtility.SetDirty(OldAsset);
#if UNITY_6000_2_OR_NEWER
                if (!sbp.generateSlotLods)
                {
                    ClearInternalLods(OldAsset, 0);
                }
#endif
                // Carry LOD ranges from the source mesh
#if UNITY_6000_2_OR_NEWER
                if (sbp.generateSlotLods)
                {
                    CopyLodRangesFromSourceMesh(OldAsset, 0, sbp.slotMesh.sharedMesh, 0);
                }
#endif
                GenerateSlotLodsIfEnabled(sbp, OldAsset);
                createdSlots.Add(OldAsset);
                // Replace working reference with existing for overlay mapping
                UnityEngine.Object.DestroyImmediate(slot);
                slot = OldAsset;
            }
            else
            {
                AssetDatabase.CreateAsset(slot, slotPath);
#if UNITY_6000_2_OR_NEWER
                // Carry LOD ranges from the source mesh
                if (sbp.generateSlotLods)
                {
                    CopyLodRangesFromSourceMesh(slot, 0, sbp.slotMesh.sharedMesh, 0);
                }
                else
                {
                    ClearInternalLods(slot, 0);
                }
#endif
                GenerateSlotLodsIfEnabled(sbp, slot);
                if (sbp.addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), slot);
                }
                createdSlots.Add(slot);
            }

            // Create/overwrite overlay for submesh0 if requested (non-UDIM reuse rule applies)
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

            var frenderer = finalMeshRenderer;
            var shmesh = finalMeshRenderer.sharedMesh;
            int meshCount = finalMeshRenderer.sharedMesh.subMeshCount;

            // Additional submeshes
            for (int i = 1; i < meshCount; i++)
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
                if (sbp.alwaysRecreateSlots)
                {
                    if (existingAdditional != null)
                    {
                        AssetDatabase.DeleteAsset(theSlotPath);
                        existingAdditional = null;
                    }
                }
                if (existingAdditional != null)
                {
                    // Update existing submesh slot
                    string existingRootBone = slot.meshData.RootBoneName;
                    UpdateSlotData(existingAdditional, finalMeshRenderer, existingAdditional.material, existingAdditional.normalReferenceMesh, existingRootBone, true, sbp.clearNormals, sbp.clearTangents);
                    existingAdditional.sourceSubmeshIndex = i;
#if UNITY_6000_2_OR_NEWER
                    if (!sbp.generateSlotLods)
                    {
                        ClearInternalLods(existingAdditional, 0);
                    }
                    if (sbp.generateSlotLods)
                    {
                        CopyLodRangesFromSourceMesh(existingAdditional, 0, sbp.slotMesh.sharedMesh, i);
                    }
#endif
                    GenerateSlotLodsIfEnabled(sbp, existingAdditional);
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
                additionalSlot.UpdateMeshData(finalMeshRenderer, sbp.rootBone, false, i, sbp.clearNormals, sbp.clearTangents);
                TransformMeshData(additionalSlot, sbp);

                additionalSlot.sourceSubmeshIndex = i;

                AssetDatabase.CreateAsset(additionalSlot, theSlotPath);
#if UNITY_6000_2_OR_NEWER
                if (sbp.generateSlotLods)
                {
                    CopyLodRangesFromSourceMesh(additionalSlot, 0, sbp.slotMesh.sharedMesh, i);
                }
                else
                {
                    ClearInternalLods(additionalSlot, 0);
                }
#endif
                GenerateSlotLodsIfEnabled(sbp, additionalSlot);
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

            if (sbp.batchMode)
            {
                // Defer deletion to caller
                var resultBatch = new SlotBuildResult();
                resultBatch.Slots = createdSlots;
                resultBatch.SlotToOverlay = slotToOverlay;
                resultBatch.IsUDIM = false;
                resultBatch.TempAssetsToDelete.Add(SkinnedName);
                resultBatch.TempAssetsToDelete.Add(theMesh);
                return resultBatch;
            }

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
            for (int i = 0; i < Vertices.Length; i++)
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

            var usedBonesDictionary = CompileUsedBonesDictionary(mesh, KeepBonesList);
            var smrOldBones = smr.bones.Length;
            if (usedBonesDictionary.Count != smrOldBones)
            {
                mesh.SetBoneWeights(mesh.GetBonesPerVertex(), BuildNewBoneWeights(mesh.GetAllBoneWeights(), usedBonesDictionary));
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
            newMesh.SetBoneWeights(sourceMesh.GetBonesPerVertex(), BuildNewBoneWeights(sourceMesh.GetAllBoneWeights(), usedBonesDictionary));
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

            foreach (int boneIndex in keepBones)
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

        // Build a compact mesh from a subset of triangles, remapping all vertex attributes and bone weights
        private static Mesh BuildCompactTriangleMesh(Mesh source, IList<int> triangleIndices)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (triangleIndices == null) throw new ArgumentNullException("triangleIndices");

            int srcVertexCount = source.vertexCount;

            // Build remap from old vertex index -> new compact index, preserving discovery order
            var oldToNew = new Dictionary<int, int>(1024);
            var newToOld = new List<int>(1024);
            int triCount = triangleIndices.Count;
            for (int i = 0; i < triCount; i++)
            {
                int oldIndex = triangleIndices[i];
                if (!oldToNew.ContainsKey(oldIndex))
                {
                    int newIndex = newToOld.Count;
                    oldToNew.Add(oldIndex, newIndex);
                    newToOld.Add(oldIndex);
                }
            }

            int newVertexCount = newToOld.Count;

            // Prepare source attributes
            var srcVertices = source.vertices;
            var srcNormals = source.normals;
            var srcTangents = source.tangents;
            var srcColors32 = source.colors32;
            var srcUV0 = source.uv;
            var srcUV1 = source.uv2;
            var srcUV2 = source.uv3;
            var srcUV3 = source.uv4;

            // Allocate new attributes
            var newVertices = new Vector3[newVertexCount];
            Vector3[] newNormals = (srcNormals != null && srcNormals.Length == srcVertexCount) ? new Vector3[newVertexCount] : null;
            Vector4[] newTangents = (srcTangents != null && srcTangents.Length == srcVertexCount) ? new Vector4[newVertexCount] : null;
            Color32[] newColors32 = (srcColors32 != null && srcColors32.Length == srcVertexCount) ? new Color32[newVertexCount] : null;
            Vector2[] newUV0 = (srcUV0 != null && srcUV0.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV1 = (srcUV1 != null && srcUV1.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV2 = (srcUV2 != null && srcUV2.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV3 = (srcUV3 != null && srcUV3.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;

            for (int i = 0; i < newVertexCount; i++)
            {
                int old = newToOld[i];
                newVertices[i] = srcVertices[old];
                if (newNormals != null) newNormals[i] = srcNormals[old];
                if (newTangents != null) newTangents[i] = srcTangents[old];
                if (newColors32 != null) newColors32[i] = srcColors32[old];
                if (newUV0 != null) newUV0[i] = srcUV0[old];
                if (newUV1 != null) newUV1[i] = srcUV1[old];
                if (newUV2 != null) newUV2[i] = srcUV2[old];
                if (newUV3 != null) newUV3[i] = srcUV3[old];
            }

            // Remap triangles
            var newTris = new int[triCount];
            for (int i = 0; i < triCount; i++)
            {
                newTris[i] = oldToNew[triangleIndices[i]];
            }

            // Bone weights
            NativeArray<byte> srcBPV = default;
            NativeArray<BoneWeight1> srcAllBW = default;
            bool hasSkinning = false;
            try
            {
                srcBPV = source.GetBonesPerVertex();
                srcAllBW = source.GetAllBoneWeights();
                hasSkinning = srcBPV.IsCreated && srcBPV.Length == srcVertexCount && srcAllBW.IsCreated;
            }
            catch { hasSkinning = false; }

            NativeArray<byte> newBPV = default;
            NativeArray<BoneWeight1> newAllBW = default;
            if (hasSkinning)
            {
                var offsets = new int[srcVertexCount];
                int acc = 0;
                for (int v = 0; v < srcVertexCount; v++)
                {
                    offsets[v] = acc;
                    acc += (v < srcBPV.Length ? srcBPV[v] : (byte)0);
                }
                var bpvManaged = new byte[newVertexCount];
                var bwManaged = new List<BoneWeight1>(acc);
                for (int i = 0; i < newVertexCount; i++)
                {
                    int old = newToOld[i];
                    byte count = (old < srcBPV.Length) ? srcBPV[old] : (byte)0;
                    bpvManaged[i] = count;
                    int srcOffset = offsets[old];
                    for (int j = 0; j < count; j++)
                    {
                        bwManaged.Add(srcAllBW[srcOffset + j]);
                    }
                }
                newBPV = new NativeArray<byte>(bpvManaged, Allocator.Temp);
                var bwArray = bwManaged.Count > 0 ? bwManaged.ToArray() : Array.Empty<BoneWeight1>();
                newAllBW = new NativeArray<BoneWeight1>(bwArray, Allocator.Temp);
            }

            // Build mesh
            var compact = new Mesh();
            compact.name = source.name + "_Compact";
            compact.indexFormat = (newVertexCount > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            compact.vertices = newVertices;
            if (newNormals != null) compact.normals = newNormals;
            if (newTangents != null) compact.tangents = newTangents;
            if (newColors32 != null) compact.colors32 = newColors32;
            if (newUV0 != null) compact.uv = newUV0;
            if (newUV1 != null) compact.uv2 = newUV1;
            if (newUV2 != null) compact.uv3 = newUV2;
            if (newUV3 != null) compact.uv4 = newUV3;
            compact.bindposes = source.bindposes;
            compact.subMeshCount = 1;
            compact.SetTriangles(newTris, 0, true);
            if (hasSkinning && newBPV.IsCreated && newAllBW.IsCreated)
            {
                compact.SetBoneWeights(newBPV, newAllBW);
            }

            // Copy blendshapes with remapped vertices
            int shapeCount = source.blendShapeCount;
            if (shapeCount > 0)
            {
                var dv = new Vector3[srcVertexCount];
                var dn = new Vector3[srcVertexCount];
                var dt = new Vector3[srcVertexCount];

                for (int si = 0; si < shapeCount; si++)
                {
                    string shapeName = source.GetBlendShapeName(si);
                    int frameCount = source.GetBlendShapeFrameCount(si);
                    for (int fi = 0; fi < frameCount; fi++)
                    {
                        float w = source.GetBlendShapeFrameWeight(si, fi);
                        source.GetBlendShapeFrameVertices(si, fi, dv, dn, dt);
                        var ndv = new Vector3[newVertexCount];
                        var ndn = new Vector3[newVertexCount];
                        var ndt = new Vector3[newVertexCount];
                        for (int vi = 0; vi < newVertexCount; vi++)
                        {
                            int old = newToOld[vi];
                            ndv[vi] = dv[old];
                            ndn[vi] = dn[old];
                            ndt[vi] = dt[old];
                        }
                        compact.AddBlendShapeFrame(shapeName, w, ndv, ndn, ndt);
                    }
                }
            }

            compact.RecalculateBounds();

            // Cleanup NativeArrays
            if (srcBPV.IsCreated) srcBPV.Dispose();
            if (srcAllBW.IsCreated) srcAllBW.Dispose();
            if (newBPV.IsCreated) newBPV.Dispose();
            if (newAllBW.IsCreated) newAllBW.Dispose();

            return compact;
        }

        // Build a compact mesh and also return the mapping between original indices and new indices
        private struct CompactMeshResult
        {
            public Mesh Mesh;
            public Dictionary<int, int> OldToNew; // original -> local
            public List<int> NewToOld; // local -> original
        }
        private static CompactMeshResult BuildCompactTriangleMeshWithMap(Mesh source, IList<int> triangleIndices)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (triangleIndices == null) throw new ArgumentNullException("triangleIndices");

            int srcVertexCount = source.vertexCount;

            // Build remap from old vertex index -> new compact index, preserving discovery order
            var oldToNew = new Dictionary<int, int>(1024);
            var newToOld = new List<int>(1024);
            int triCount = triangleIndices.Count;
            for (int i = 0; i < triCount; i++)
            {
                int oldIndex = triangleIndices[i];
                if (!oldToNew.ContainsKey(oldIndex))
                {
                    int newIndex = newToOld.Count;
                    oldToNew.Add(oldIndex, newIndex);
                    newToOld.Add(oldIndex);
                }
            }

            int newVertexCount = newToOld.Count;

            // Prepare attributes
            var srcVertices = source.vertices;
            var srcNormals = source.normals;
            var srcTangents = source.tangents;
            var srcColors32 = source.colors32;
            var srcUV0 = source.uv;
            var srcUV1 = source.uv2;
            var srcUV2 = source.uv3;
            var srcUV3 = source.uv4;

            var newVertices = new Vector3[newVertexCount];
            Vector3[] newNormals = (srcNormals != null && srcNormals.Length == srcVertexCount) ? new Vector3[newVertexCount] : null;
            Vector4[] newTangents = (srcTangents != null && srcTangents.Length == srcVertexCount) ? new Vector4[newVertexCount] : null;
            Color32[] newColors32 = (srcColors32 != null && srcColors32.Length == srcVertexCount) ? new Color32[newVertexCount] : null;
            Vector2[] newUV0 = (srcUV0 != null && srcUV0.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV1 = (srcUV1 != null && srcUV1.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV2 = (srcUV2 != null && srcUV2.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;
            Vector2[] newUV3 = (srcUV3 != null && srcUV3.Length == srcVertexCount) ? new Vector2[newVertexCount] : null;

            for (int i = 0; i < newVertexCount; i++)
            {
                int old = newToOld[i];
                newVertices[i] = srcVertices[old];
                if (newNormals != null) newNormals[i] = srcNormals[old];
                if (newTangents != null) newTangents[i] = srcTangents[old];
                if (newColors32 != null) newColors32[i] = srcColors32[old];
                if (newUV0 != null) newUV0[i] = srcUV0[old];
                if (newUV1 != null) newUV1[i] = srcUV1[old];
                if (newUV2 != null) newUV2[i] = srcUV2[old];
                if (newUV3 != null) newUV3[i] = srcUV3[old];
            }

            // Remap triangles
            var newTris = new int[triCount];
            for (int i = 0; i < triCount; i++)
            {
                newTris[i] = oldToNew[triangleIndices[i]];
            }

            // Bone weights
            NativeArray<byte> srcBPV = default;
            NativeArray<BoneWeight1> srcAllBW = default;
            bool hasSkinning = false;
            try
            {
                srcBPV = source.GetBonesPerVertex();
                srcAllBW = source.GetAllBoneWeights();
                hasSkinning = srcBPV.IsCreated && srcBPV.Length == srcVertexCount && srcAllBW.IsCreated;
            }
            catch { hasSkinning = false; }

            NativeArray<byte> newBPV = default;
            NativeArray<BoneWeight1> newAllBW = default;
            if (hasSkinning)
            {
                var offsets = new int[srcVertexCount];
                int acc = 0;
                for (int v = 0; v < srcVertexCount; v++)
                {
                    offsets[v] = acc;
                    acc += (v < srcBPV.Length ? srcBPV[v] : (byte)0);
                }
                var bpvManaged = new byte[newVertexCount];
                var bwManaged = new List<BoneWeight1>(acc);
                for (int i = 0; i < newVertexCount; i++)
                {
                    int old = newToOld[i];
                    byte count = (old < srcBPV.Length) ? srcBPV[old] : (byte)0;
                    bpvManaged[i] = count;
                    int srcOffset = offsets[old];
                    for (int j = 0; j < count; j++)
                    {
                        bwManaged.Add(srcAllBW[srcOffset + j]);
                    }
                }
                newBPV = new NativeArray<byte>(bpvManaged, Allocator.Temp);
                var bwArray = bwManaged.Count > 0 ? bwManaged.ToArray() : Array.Empty<BoneWeight1>();
                newAllBW = new NativeArray<BoneWeight1>(bwArray, Allocator.Temp);
            }

            // Build mesh
            var compact = new Mesh();
            compact.name = source.name + "_Compact";
            compact.indexFormat = (newVertexCount > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            compact.vertices = newVertices;
            if (newNormals != null) compact.normals = newNormals;
            if (newTangents != null) compact.tangents = newTangents;
            if (newColors32 != null) compact.colors32 = newColors32;
            if (newUV0 != null) compact.uv = newUV0;
            if (newUV1 != null) compact.uv2 = newUV1;
            if (newUV2 != null) compact.uv3 = newUV2;
            if (newUV3 != null) compact.uv4 = newUV3;
            compact.bindposes = source.bindposes;
            compact.subMeshCount = 1;
            compact.SetTriangles(newTris, 0, true);
            if (hasSkinning && newBPV.IsCreated && newAllBW.IsCreated)
            {
                compact.SetBoneWeights(newBPV, newAllBW);
            }

            // Blendshapes remap
            int shapeCount = source.blendShapeCount;
            if (shapeCount > 0)
            {
                var dv = new Vector3[srcVertexCount];
                var dn = new Vector3[srcVertexCount];
                var dt = new Vector3[srcVertexCount];
                for (int si = 0; si < shapeCount; si++)
                {
                    string shapeName = source.GetBlendShapeName(si);
                    int frameCount = source.GetBlendShapeFrameCount(si);
                    for (int fi = 0; fi < frameCount; fi++)
                    {
                        float w = source.GetBlendShapeFrameWeight(si, fi);
                        source.GetBlendShapeFrameVertices(si, fi, dv, dn, dt);
                        var ndv = new Vector3[newVertexCount];
                        var ndn = new Vector3[newVertexCount];
                        var ndt = new Vector3[newVertexCount];
                        for (int vi = 0; vi < newVertexCount; vi++)
                        {
                            int old = newToOld[vi];
                            ndv[vi] = dv[old];
                            ndn[vi] = dn[old];
                            ndt[vi] = dt[old];
                        }
                        compact.AddBlendShapeFrame(shapeName, w, ndv, ndn, ndt);
                    }
                }
            }

            compact.RecalculateBounds();

            if (srcBPV.IsCreated) srcBPV.Dispose();
            if (srcAllBW.IsCreated) srcAllBW.Dispose();
            if (newBPV.IsCreated) newBPV.Dispose();
            if (newAllBW.IsCreated) newAllBW.Dispose();

            return new CompactMeshResult { Mesh = compact, OldToNew = oldToNew, NewToOld = newToOld };
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

            // First pass: gather triangles per (submesh, tile) and record which original vertices belong to multiple tiles.
            var perSubTileToTris = new Dictionary<int, Dictionary<(int u, int v), List<int>>>();
            var oldIndexToTiles = new Dictionary<int, HashSet<(int u, int v)>>();
#if UNITY_6000_2_OR_NEWER
            // Also track per-tile LOD counts in original order so we can rebuild ranges on the compact mesh
            var perSubTileToLodCounts = new Dictionary<int, Dictionary<(int u, int v), int[]>>();
#endif

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] tris = mesh.GetTriangles(sub);
                // Classify triangles by tile
                var tileToTris = new Dictionary<(int u, int v), List<int>>();
#if UNITY_6000_2_OR_NEWER
                int lodCount = mesh.lodCount;
                // Pre-fetch source submesh LOD ranges
                var lodRanges = new List<(int start, int count)>(lodCount);
                for (int l = 0; l < lodCount; l++)
                {
                    var lor = mesh.GetLod(sub, l);
                    lodRanges.Add(((int)lor.indexStart, (int)lor.indexCount));
                }
                var tileToLodCounts = new Dictionary<(int u, int v), int[]>();
#endif

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t];
                    int b = tris[t + 1];
                    int c = tris[t + 2];

                    // Choose the owning tile by majority vertex membership (supports triangles spanning tiles)
                    if (!TryGetTriangleUdimTileByMajority(uv, a, b, c, tilesU, tilesV, out var key))
                    {
                        continue;
                    }
                    if (!tileToTris.TryGetValue(key, out var list))
                    {
                        list = new List<int>(6);
                        tileToTris.Add(key, list);
                    }
                    list.Add(a);
                    list.Add(b);
                    list.Add(c);

#if UNITY_6000_2_OR_NEWER
                    // Determine which LOD this triangle belongs to based on its starting index 't'
                    int lodLevel = 0;
                    if (lodCount > 0 && lodRanges.Count == lodCount)
                    {
                        for (int l = 0; l < lodCount; l++)
                        {
                            var lr = lodRanges[l];
                            if (t >= lr.start && t < (lr.start + lr.count))
                            {
                                lodLevel = l;
                                break;
                            }
                        }
                    }
                    if (!tileToLodCounts.TryGetValue(key, out var counts))
                    {
                        counts = (lodCount > 0) ? new int[lodCount] : new int[0];
                        tileToLodCounts.Add(key, counts);
                    }
                    if (counts.Length > 0)
                    {
                        counts[lodLevel] += 3; // three indices for this triangle
                    }
#endif

                    // Record tile membership for each original vertex index
                    if (!oldIndexToTiles.TryGetValue(a, out var setA)) { setA = new HashSet<(int, int)>(); oldIndexToTiles.Add(a, setA); }
                    setA.Add(key);
                    if (!oldIndexToTiles.TryGetValue(b, out var setB)) { setB = new HashSet<(int, int)>(); oldIndexToTiles.Add(b, setB); }
                    setB.Add(key);
                    if (!oldIndexToTiles.TryGetValue(c, out var setC)) { setC = new HashSet<(int, int)>(); oldIndexToTiles.Add(c, setC); }
                    setC.Add(key);
                }

                // Store per submesh tile map
                perSubTileToTris[sub] = tileToTris;
#if UNITY_6000_2_OR_NEWER
                perSubTileToLodCounts[sub] = tileToLodCounts;
#endif
            }

            // Determine which original vertices are shared across multiple tiles.
            var sharedOldIndices = new HashSet<int>();
            foreach (var kv in oldIndexToTiles)
            {
                if (kv.Value != null && kv.Value.Count > 1)
                    sharedOldIndices.Add(kv.Key);
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (!perSubTileToTris.TryGetValue(sub, out var tileToTris) || tileToTris.Count == 0)
                {
                    continue;
                }

                int i = 0;
                // Create a slot per used tile
                foreach (var kvp in tileToTris)
                {
                    ++i;
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
                    string theSlotName = baseName;

                    if (sbp.addUDIMTileNumbers)
                    {    
                        theSlotName = string.Format("{0}_UDIM{1}", baseName, udimNumber);
                    }
                    else
                    {
                        // get the last 3 digits 
                        theSlotName = string.Format("{0}_{1}", baseName, i);
                    }


                    // Build a compact temporary mesh limited to this tile for submesh ->0, with index map
                    Mesh tileMeshSrc = UnityEngine.Object.Instantiate(mesh);
                    var cmr = BuildCompactTriangleMeshWithMap(tileMeshSrc, kvp.Value);

                    // Create temp renderer to feed UpdateMeshData
                    var go = new GameObject("UDIM_Tile_TempSMR");
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = cmr.Mesh;
                    smr.bones = sourceRenderer.bones;
                    smr.rootBone = sourceRenderer.rootBone;

                    try
                    {
                        string append = ".asset";
                        if (sbp.appendTypeToName)
                        {
                            append = "_slot.asset";
                        }
                        // Determine target path
                        string theSlotPath = sbp.slotFolder + '/' + sbp.assetName + '/' + theSlotName + append;
                        if (sbp.useRootFolder)
                        {
                            theSlotPath = sbp.slotFolder + '/' + theSlotName + append;
                        }

                        var existing = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(theSlotPath);
						SlotPreserveData preserved = null;
                        if (sbp.alwaysRecreateSlots)
                        {
                            if (existing != null)
                            {
								preserved = SlotPreserveData.FromSlot(existing);
                                AssetDatabase.DeleteAsset(theSlotPath);
                                existing = null;
                            }
                        }
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

#if UNITY_6000_2_OR_NEWER
                            if (!sbp.generateSlotLods)
                            {
                                ClearInternalLods(sda, 0);
                            }
#endif

                            // Populate UDIM seam map (original vertex index -> this slot's local vertex index)
                            if (sharedOldIndices != null && sharedOldIndices.Count > 0 && cmr.NewToOld != null && cmr.NewToOld.Count > 0)
                            {
                                var orig = new List<int>(sharedOldIndices.Count);
                                var loc = new List<int>(sharedOldIndices.Count);
                                for (int localIndex = 0; localIndex < cmr.NewToOld.Count; localIndex++)
                                {
                                    int oldIndex = cmr.NewToOld[localIndex];
                                    if (sharedOldIndices.Contains(oldIndex))
                                    {
                                        orig.Add(oldIndex);
                                        loc.Add(localIndex);
                                    }
                                }
                                if (orig.Count > 0)
                                {
                                    sda.UdimSharedVertexMap = new SlotDataAsset.UdimSeamMap
                                    {
                                        originalIndices = orig.ToArray(),
                                        localIndices = loc.ToArray()
                                    };
                                }
                                else
                                {
                                    sda.UdimSharedVertexMap = null;
                                }
                            }
                            else
                            {
                                sda.UdimSharedVertexMap = null;
                            }
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
							if (preserved != null)
							{
								preserved.ApplyTo(sda);
							}

                            // Populate UDIM seam map (original vertex index -> this slot's local vertex index)
                            if (sharedOldIndices != null && sharedOldIndices.Count > 0 && cmr.NewToOld != null && cmr.NewToOld.Count > 0)
                            {
                                var orig = new List<int>(sharedOldIndices.Count);
                                var loc = new List<int>(sharedOldIndices.Count);
                                for (int localIndex = 0; localIndex < cmr.NewToOld.Count; localIndex++)
                                {
                                    int oldIndex = cmr.NewToOld[localIndex];
                                    if (sharedOldIndices.Contains(oldIndex))
                                    {
                                        orig.Add(oldIndex);
                                        loc.Add(localIndex);
                                    }
                                }
                                if (orig.Count > 0)
                                {
                                    sda.UdimSharedVertexMap = new SlotDataAsset.UdimSeamMap
                                    {
                                        originalIndices = orig.ToArray(),
                                        localIndices = loc.ToArray()
                                    };
                                }
                            }

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

#if UNITY_6000_2_OR_NEWER
                            if (!sbp.generateSlotLods)
                            {
                                ClearInternalLods(sda, 0);
                            }
#endif
                        }

                        // If requested, generate internal per-slot LOD buffers/ranges for this UDIM slot.
                        // This must run after UpdateMeshData (meshData exists) and after the asset is created/updated.
                        GenerateSlotLodsIfEnabled(sbp, sda);

#if UNITY_6000_2_OR_NEWER
                        // If we are NOT generating UMA internal LODs, preserve Unity-authored mesh LODs by
                        // rebuilding compact-mesh LOD ranges from per-tile counts.
                        //
                        // When UMA internal LODs are generated above, they already append triangle buffers and
                        // assign correct `lodRanges`. Overwriting here would destroy those generated ranges.
                        if (!sbp.generateSlotLods && perSubTileToLodCounts.TryGetValue(sub, out var tileLodCounts) && tileLodCounts.TryGetValue((tu, tv), out var countsArr) && countsArr != null && countsArr.Length > 0)
                        {
                            var ranges = new List<UMA.UMALodRange>(countsArr.Length);
                            uint offset = 0;
                            for (int l = 0; l < countsArr.Length; l++)
                            {
                                uint cnt = (uint)Mathf.Max(0, countsArr[l]);
                                ranges.Add(new UMA.UMALodRange(offset, cnt));
                                offset += cnt;
                            }
                            if (sda.meshData != null && sda.meshData.submeshes != null && sda.meshData.submeshes.Length > 0)
                            {
                                sda.meshData.submeshes[0].SetLodRanges(ranges);
                            }
                        }
#endif

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
                        UnityEngine.Object.DestroyImmediate(cmr.Mesh);
                        UnityEngine.Object.DestroyImmediate(tileMeshSrc);
                        return new SlotBuildResult { Slots = createdSlots, SlotToOverlay = slotToOverlay, IsUDIM = true };
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                        UnityEngine.Object.DestroyImmediate(cmr.Mesh);
                        UnityEngine.Object.DestroyImmediate(tileMeshSrc);
                    }
                }
            }

            // Optional: weld normals/tangents across UDIM seams by averaging
            if (sbp.weldUdimNormals)
            {
                WeldUdimSeamNormalsAverage(createdSlots);
                foreach (var s in createdSlots) { EditorUtility.SetDirty(s); }
            }

            // Build result for UDIM; recipe creation happens in caller
            return new SlotBuildResult { Slots = createdSlots, SlotToOverlay = slotToOverlay, IsUDIM = true };
        }

        private static void WeldUdimSeamNormalsAverage(List<SlotDataAsset> slots)
        {
            if (slots == null || slots.Count == 0) return;
            // Build map: original index -> list of (slot, local index)
            var map = new Dictionary<int, List<(SlotDataAsset s, int idx)>>();
            foreach (var s in slots)
            {
                if (s == null || s.meshData == null) continue;
                var m = s.UdimSharedVertexMap;
                if (m == null || m.originalIndices == null || m.localIndices == null) continue;
                int count = Math.Min(m.originalIndices.Length, m.localIndices.Length);
                for (int i = 0; i < count; i++)
                {
                    int orig = m.originalIndices[i];
                    int loc = m.localIndices[i];
                    if (loc < 0 || s.meshData.vertices == null || loc >= s.meshData.vertices.Length) continue;
                    if (!map.TryGetValue(orig, out var list)) { list = new List<(SlotDataAsset, int)>(); map.Add(orig, list); }
                    list.Add((s, loc));
                }
            }

            // Average normals/tangents per original vertex across participating slots
            foreach (var kv in map)
            {
                var list = kv.Value;
                if (list == null || list.Count <= 1) continue; // nothing to weld

                // Compute average normal
                Vector3 sumNormal = Vector3.zero;
                bool haveNormals = true;
                foreach (var r in list)
                {
                    var md = r.s.meshData;
                    if (md.normals == null || md.normals.Length != md.vertices.Length) { haveNormals = false; break; }
                    sumNormal += md.normals[r.idx];
                }
                if (haveNormals && sumNormal != Vector3.zero)
                {
                    Vector3 avgN = sumNormal.normalized;
                    foreach (var r in list)
                    {
                        r.s.meshData.normals[r.idx] = avgN;
                    }
                }

                // Average tangents if present
                bool haveTangents = true;
                Vector3 sumTan = Vector3.zero;
                float wSign = 1f;
                foreach (var r in list)
                {
                    var md = r.s.meshData;
                    if (md.tangents == null || md.tangents.Length != md.vertices.Length) { haveTangents = false; break; }
                    Vector4 t = md.tangents[r.idx];
                    sumTan += new Vector3(t.x, t.y, t.z);
                    wSign = t.w; // keep any
                }
                if (haveTangents && sumTan != Vector3.zero)
                {
                    Vector3 avgT = sumTan.normalized;
                    Vector4 t4 = new Vector4(avgT.x, avgT.y, avgT.z, wSign);
                    foreach (var r in list)
                    {
                        r.s.meshData.tangents[r.idx] = t4;
                    }
                }
            }
        }
    }
}
#endif
