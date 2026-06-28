#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    internal class MeshDataViewerWindow : EditorWindow
    {
        private SlotDataAsset slotDataAsset;
        private UMAMeshData meshData;
        private Vector2 scrollPosition;

        private bool statisticsFoldout = true;
        private bool summaryFoldout = true;
        private bool geometryFoldout = true;
        private bool boneWeightsFoldout;
        private bool bindPosesFoldout;
        private bool bonesFoldout;
        private bool submeshesFoldout;
        private bool blendShapesFoldout;
        private bool clothFoldout;
        private bool stateFoldout;
        private bool lodTotalsFoldout;

        private bool verticesFoldout;
        private bool normalsFoldout;
        private bool tangentsFoldout;
        private bool colorsFoldout;
        private bool uvFoldout;
        private bool uv2Foldout;
        private bool uv3Foldout;
        private bool uv4Foldout;
        private bool boneNameHashesFoldout;
        private bool managedBonesPerVertexFoldout;
        private bool managedBoneWeightsFoldout;
        private bool legacyBoneWeightsFoldout;
        private bool umaBonesFoldout;
        private bool clothSkinningFoldout;
        private bool clothSkinningSerializedFoldout;

        private readonly List<bool> submeshElementFoldouts = new List<bool>();
        private readonly List<bool> blendShapeElementFoldouts = new List<bool>();
        private readonly List<bool> blendShapeFrameFoldouts = new List<bool>();

        internal static void Open(SlotDataAsset slot)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                EditorUtility.DisplayDialog("View MeshData", "This SlotDataAsset has no MeshData.", "OK");
                return;
            }

            MeshDataViewerWindow window = CreateInstance<MeshDataViewerWindow>();
            window.titleContent = new GUIContent("MeshData Viewer");
            window.minSize = new Vector2(760f, 480f);
            window.slotDataAsset = slot;
            window.meshData = slot.meshData;
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            if (slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(meshData))
            {
                EditorGUILayout.HelpBox("MeshData is not available.", MessageType.Info);
                DrawCloseButton();
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Slot", slotDataAsset.slotName);
            EditorGUILayout.LabelField("Asset", slotDataAsset.name);
            EditorGUILayout.Space(4f);

            DrawStatisticsSection();
            DrawSummarySection();
            DrawGeometrySection();
            DrawBoneWeightsSection();
            DrawBindPosesSection();
            DrawBonesSection();
            DrawSubmeshesSection();
            DrawBlendShapesSection();
            DrawClothSection();
            DrawStateSection();
            DrawLodTotalsSection();

            EditorGUILayout.EndScrollView();

            DrawCloseButton();
        }

        private void DrawStatisticsSection()
        {
            statisticsFoldout = EditorGUILayout.Foldout(statisticsFoldout, "Statistics", true);
            if (!statisticsFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Asset Created", GetAssetCreatedDate());
                EditorGUILayout.LabelField("Asset Modified", GetAssetModifiedDate());
                EditorGUILayout.LabelField("Maximum Bones Per Vertex", GetMaximumBonesPerVertex().ToString());
                EditorGUILayout.LabelField("Average Bones Per Vertex", GetAverageBonesPerVertex().ToString("0.00"));
                EditorGUILayout.LabelField("UV Set Count", GetUvSetCount().ToString());
                EditorGUILayout.LabelField("Total LOD0 Triangle Indices", GetTotalLod0TriangleIndices().ToString());
                EditorGUILayout.LabelField("Total LOD0 Triangles", GetTotalLod0Triangles().ToString());
                EditorGUILayout.LabelField("Submeshes With LOD Ranges", GetSubmeshesWithLodsCount().ToString());
                EditorGUILayout.LabelField("BlendShape Count", GetBlendShapeCount().ToString());
                EditorGUILayout.LabelField("Total BlendShape Frames", GetBlendShapeFrameCount().ToString());
                EditorGUILayout.LabelField("Animated Bones Count", GetAnimatedBonesCount().ToString());
                EditorGUILayout.LabelField("Cloth Coefficients Count", GetClothCoefficientCount().ToString());
            }
        }

        private string GetAssetCreatedDate()
        {
            string assetPath = AssetDatabase.GetAssetPath(slotDataAsset);
            string fullPath = GetAssetFullPath(assetPath);
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
            {
                return "Unknown";
            }

            return System.IO.File.GetCreationTime(fullPath).ToString("yyyy-MM-dd HH:mm:ss");
        }

        private string GetAssetModifiedDate()
        {
            string assetPath = AssetDatabase.GetAssetPath(slotDataAsset);
            string fullPath = GetAssetFullPath(assetPath);
            if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath))
            {
                return "Unknown";
            }

            return System.IO.File.GetLastWriteTime(fullPath).ToString("yyyy-MM-dd HH:mm:ss");
        }

        private string GetAssetFullPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            return System.IO.Path.Combine(projectRoot, assetPath);
        }

        private void DrawSummarySection()
        {
            summaryFoldout = EditorGUILayout.Foldout(summaryFoldout, "Summary", true);
            if (!summaryFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("SlotName", meshData.SlotName ?? string.Empty);
                EditorGUILayout.LabelField("RootBoneName", meshData.RootBoneName ?? string.Empty);
                EditorGUILayout.LabelField("Vertex Count", meshData.vertexCount.ToString());
                EditorGUILayout.LabelField("SubMesh Count", meshData.subMeshCount.ToString());
                EditorGUILayout.LabelField("UMA Bone Count", meshData.umaBoneCount.ToString());
                EditorGUILayout.LabelField("Root Bone Hash", meshData.rootBoneHash.ToString());
                EditorGUILayout.LabelField("Loaded Boneweights", meshData.LoadedBoneweights.ToString());
                EditorGUILayout.LabelField("Has Root Bone", (meshData.rootBone != null).ToString());
                EditorGUILayout.LabelField("Bones Array Count", meshData.bones != null ? meshData.bones.Length.ToString() : "0");
            }
        }

        private void DrawGeometrySection()
        {
            geometryFoldout = EditorGUILayout.Foldout(geometryFoldout, "Geometry", true);
            if (!geometryFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawVector3ArrayFoldout("Vertices", meshData.vertices, ref verticesFoldout);
                DrawVector3ArrayFoldout("Normals", meshData.normals, ref normalsFoldout);
                DrawVector4ArrayFoldout("Tangents", meshData.tangents, ref tangentsFoldout);
                DrawColor32ArrayFoldout("Colors32", meshData.colors32, ref colorsFoldout);
                DrawVector2ArrayFoldout("UV", meshData.uv, ref uvFoldout);
                DrawVector2ArrayFoldout("UV2", meshData.uv2, ref uv2Foldout);
                DrawVector2ArrayFoldout("UV3", meshData.uv3, ref uv3Foldout);
                DrawVector2ArrayFoldout("UV4", meshData.uv4, ref uv4Foldout);
            }
        }

        private void DrawBoneWeightsSection()
        {
            boneWeightsFoldout = EditorGUILayout.Foldout(boneWeightsFoldout, "Bone Weights", true);
            if (!boneWeightsFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawIntArrayFoldout("Bone Name Hashes", meshData.boneNameHashes, ref boneNameHashesFoldout);
                DrawByteArrayFoldout("Managed Bones Per Vertex", meshData.ManagedBonesPerVertex, ref managedBonesPerVertexFoldout);
                DrawManagedBoneWeightsFoldout("Managed Bone Weights", meshData.ManagedBoneWeights, ref managedBoneWeightsFoldout);
                DrawLegacyBoneWeightsFoldout("Legacy Bone Weights", meshData.boneWeights, ref legacyBoneWeightsFoldout);
            }
        }

        private void DrawBindPosesSection()
        {
            bindPosesFoldout = EditorGUILayout.Foldout(bindPosesFoldout, "BindPoses", true);
            if (!bindPosesFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Matrix4x4[] bindPoses = meshData.bindPoses;
                int count = bindPoses != null ? bindPoses.Length : 0;
                EditorGUILayout.LabelField("Count", count.ToString());
                if (count == 0)
                {
                    return;
                }

                for (int i = 0; i < bindPoses.Length; i++)
                {
                    EditorGUILayout.LabelField("BindPose " + i, bindPoses[i].ToString());
                }
            }
        }



        private List<bool> BoneOpen = new List<bool>(65535);

        private void DrawBonesSection()
        {
            bonesFoldout = EditorGUILayout.Foldout(bonesFoldout, "Bones", true);
            if (!bonesFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // DrawUmaBonesFoldout("UMA Bones", meshData.umaBones, ref umaBonesFoldout);

                if (meshData.umaBones != null)
                {
                    for (int boneindex = 0; boneindex < meshData.umaBones.Length; boneindex++)
                    {
                        UMATransform b = meshData.umaBones[boneindex];
                        if (BoneOpen.Count <= boneindex)
                        {
                            BoneOpen.Add(false);

                        }
                        BoneOpen[boneindex] = EditorGUILayout.Foldout(BoneOpen[boneindex], $"{boneindex} {b.name}");
                        if (BoneOpen[boneindex])
                        {
                            GUILayout.Label($"Position: {b.position}");
                            GUILayout.Label($"Rotation: {b.rotation}");
                            GUILayout.Label($"Scale:    {b.scale}");
                        }
                    }
                }

                /*
                Transform[] bones = meshData.bones;
                int count = bones != null ? bones.Length : 0;
                EditorGUILayout.LabelField("Transform Bones Count", count.ToString());
                if (bones != null)
                {
                    for (int i = 0; i < bones.Length; i++)
                    {
                        Transform bone = bones[i];
                        EditorGUILayout.LabelField("Bone " + i, bone != null ? bone.name : "<null>");
                    }
                }*/

                EditorGUILayout.LabelField("Root Bone", meshData.rootBone != null ? meshData.rootBone.name : "<null>");
            }
        }

        private void DrawSubmeshesSection()
        {
            submeshesFoldout = EditorGUILayout.Foldout(submeshesFoldout, "Submeshes", true);
            if (!submeshesFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SubMeshTriangles[] submeshes = meshData.submeshes;
                int count = submeshes != null ? submeshes.Length : 0;
                EditorGUILayout.LabelField("Count", count.ToString());
                EnsureFoldoutCount(submeshElementFoldouts, count);
                if (submeshes == null)
                {
                    return;
                }

                for (int i = 0; i < submeshes.Length; i++)
                {
                    SubMeshTriangles submesh = submeshes[i];
                    submeshElementFoldouts[i] = EditorGUILayout.Foldout(submeshElementFoldouts[i], "Submesh " + i, true);
                    if (!submeshElementFoldouts[i])
                    {
                        continue;
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (submesh == null)
                        {
                            EditorGUILayout.LabelField("<null>");
                            continue;
                        }

                        EditorGUILayout.LabelField("smtID", submesh.smtID.ToString());
                        EditorGUILayout.LabelField("LOD Count", submesh.LODCount().ToString());
                        EditorGUILayout.LabelField("Native Triangles Created", submesh.nativeTriangles.IsCreated.ToString());
                        EditorGUILayout.LabelField("Base Triangle Count", submesh.GetTriangleCount(0).ToString());

                        if (submesh.lodRanges != null)
                        {
                            for (int j = 0; j < submesh.lodRanges.Count; j++)
                            {
                                UMALodRange lodRange = submesh.lodRanges[j];
                                EditorGUILayout.LabelField("LOD " + j, "offset=" + lodRange.offset + ", count=" + lodRange.count);
                            }
                        }

                        int[] triangles = submesh.getManagedTriangles(0);
                        EditorGUILayout.LabelField("LOD0 Triangle Buffer Length", triangles != null ? triangles.Length.ToString() : "0");
                        if (triangles != null)
                        {
                            for (int j = 0; j < triangles.Length; j++)
                            {
                                EditorGUILayout.LabelField("Triangle Index " + j, triangles[j].ToString());
                            }
                        }
                    }
                }
            }
        }

        private void DrawBlendShapesSection()
        {
            blendShapesFoldout = EditorGUILayout.Foldout(blendShapesFoldout, "BlendShapes", true);
            if (!blendShapesFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                UMABlendShape[] blendShapes = meshData.blendShapes;
                int count = blendShapes != null ? blendShapes.Length : 0;
                EditorGUILayout.LabelField("Count", count.ToString());
                EnsureFoldoutCount(blendShapeElementFoldouts, count);
                if (blendShapes == null)
                {
                    return;
                }

                for (int i = 0; i < blendShapes.Length; i++)
                {
                    UMABlendShape blendShape = blendShapes[i];
                    string label = blendShape != null ? blendShape.shapeName : "<null>";
                    blendShapeElementFoldouts[i] = EditorGUILayout.Foldout(blendShapeElementFoldouts[i], "BlendShape " + i + ": " + label, true);
                    if (!blendShapeElementFoldouts[i])
                    {
                        continue;
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        if (blendShape == null)
                        {
                            EditorGUILayout.LabelField("<null>");
                            continue;
                        }

                        EditorGUILayout.LabelField("Shape Name", blendShape.shapeName ?? string.Empty);
                        UMABlendFrame[] frames = blendShape.frames;
                        int frameCount = frames != null ? frames.Length : 0;
                        EditorGUILayout.LabelField("Frame Count", frameCount.ToString());

                        EnsureFoldoutCount(blendShapeFrameFoldouts, frameCount);
                        if (frames == null)
                        {
                            continue;
                        }

                        for (int j = 0; j < frames.Length; j++)
                        {
                            UMABlendFrame frame = frames[j];
                            blendShapeFrameFoldouts[j] = EditorGUILayout.Foldout(blendShapeFrameFoldouts[j], "Frame " + j, true);
                            if (!blendShapeFrameFoldouts[j])
                            {
                                continue;
                            }

                            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                            {
                                if (frame == null)
                                {
                                    EditorGUILayout.LabelField("<null>");
                                    continue;
                                }

                                EditorGUILayout.LabelField("Frame Weight", frame.frameWeight.ToString());
                                EditorGUILayout.LabelField("Delta Vertices", frame.deltaVertices != null ? frame.deltaVertices.Length.ToString() : "0");
                                EditorGUILayout.LabelField("Delta Normals", frame.deltaNormals != null ? frame.deltaNormals.Length.ToString() : "0");
                                EditorGUILayout.LabelField("Delta Tangents", frame.deltaTangents != null ? frame.deltaTangents.Length.ToString() : "0");
                            }
                        }
                    }
                }
            }
        }

        private void DrawClothSection()
        {
            clothFoldout = EditorGUILayout.Foldout(clothFoldout, "Cloth", true);
            if (!clothFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawClothSkinningFoldout("Cloth Skinning", meshData.clothSkinning, ref clothSkinningFoldout);
                DrawVector2ArrayFoldout("Cloth Skinning Serialized", meshData.clothSkinningSerialized, ref clothSkinningSerializedFoldout);
            }
        }

        private void DrawStateSection()
        {
            stateFoldout = EditorGUILayout.Foldout(stateFoldout, "State", true);
            if (!stateFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("verticesModified", meshData.verticesModified.ToString());
                EditorGUILayout.LabelField("normalsModified", meshData.normalsModified.ToString());
                EditorGUILayout.LabelField("tangentsModified", meshData.tangentsModified.ToString());
                EditorGUILayout.LabelField("colors32Modified", meshData.colors32Modified.ToString());
                EditorGUILayout.LabelField("uvModified", meshData.uvModified.ToString());
                EditorGUILayout.LabelField("uv2Modified", meshData.uv2Modified.ToString());
                EditorGUILayout.LabelField("uv3Modified", meshData.uv3Modified.ToString());
                EditorGUILayout.LabelField("uv4Modified", meshData.uv4Modified.ToString());
            }
        }

        private void DrawLodTotalsSection()
        {
            lodTotalsFoldout = EditorGUILayout.Foldout(lodTotalsFoldout, "LOD Totals", true);
            if (!lodTotalsFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                int lodCount = GetEffectiveLodCount();
                EditorGUILayout.LabelField("LOD Levels", lodCount.ToString());

                if (lodCount == 0)
                {
                    EditorGUILayout.LabelField("No triangle data.");
                    return;
                }

                for (int lodIndex = 0; lodIndex < lodCount; lodIndex++)
                {
                    int indexCount = GetTotalTriangleIndicesForLod(lodIndex);
                    int triangleCount = indexCount / 3;
                    EditorGUILayout.LabelField("LOD " + lodIndex, "Triangles=" + triangleCount + ", Indices=" + indexCount);
                }
            }
        }

        private void DrawCloseButton()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(120f), GUILayout.Height(24f)))
                {
                    Close();
                }
            }
        }

        private int GetMaximumBonesPerVertex()
        {
            byte[] managedBonesPerVertex = meshData.ManagedBonesPerVertex;
            if (managedBonesPerVertex != null && managedBonesPerVertex.Length > 0)
            {
                int maximum = 0;
                for (int i = 0; i < managedBonesPerVertex.Length; i++)
                {
                    if (managedBonesPerVertex[i] > maximum)
                    {
                        maximum = managedBonesPerVertex[i];
                    }
                }
                return maximum;
            }

            UMABoneWeight[] legacyWeights = meshData.boneWeights;
            if (legacyWeights == null || legacyWeights.Length == 0)
            {
                return 0;
            }

            int legacyMaximum = 0;
            for (int i = 0; i < legacyWeights.Length; i++)
            {
                int currentCount = GetLegacyBoneWeightCount(legacyWeights[i]);
                if (currentCount > legacyMaximum)
                {
                    legacyMaximum = currentCount;
                }
            }
            return legacyMaximum;
        }

        private float GetAverageBonesPerVertex()
        {
            byte[] managedBonesPerVertex = meshData.ManagedBonesPerVertex;
            if (managedBonesPerVertex != null && managedBonesPerVertex.Length > 0)
            {
                int total = 0;
                for (int i = 0; i < managedBonesPerVertex.Length; i++)
                {
                    total += managedBonesPerVertex[i];
                }
                return (float)total / managedBonesPerVertex.Length;
            }

            UMABoneWeight[] legacyWeights = meshData.boneWeights;
            if (legacyWeights == null || legacyWeights.Length == 0)
            {
                return 0f;
            }

            int legacyTotal = 0;
            for (int i = 0; i < legacyWeights.Length; i++)
            {
                legacyTotal += GetLegacyBoneWeightCount(legacyWeights[i]);
            }
            return (float)legacyTotal / legacyWeights.Length;
        }

        private int GetUvSetCount()
        {
            int count = 0;
            if (meshData.uv != null && meshData.uv.Length > 0)
            {
                count++;
            }
            if (meshData.uv2 != null && meshData.uv2.Length > 0)
            {
                count++;
            }
            if (meshData.uv3 != null && meshData.uv3.Length > 0)
            {
                count++;
            }
            if (meshData.uv4 != null && meshData.uv4.Length > 0)
            {
                count++;
            }
            return count;
        }

        private int GetTotalLod0TriangleIndices()
        {
            SubMeshTriangles[] submeshes = meshData.submeshes;
            if (submeshes == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < submeshes.Length; i++)
            {
                SubMeshTriangles submesh = submeshes[i];
                if (submesh == null)
                {
                    continue;
                }
                total += submesh.GetTriangleCount(0);
            }
            return total;
        }

        private int GetTotalLod0Triangles()
        {
            return GetTotalLod0TriangleIndices() / 3;
        }

        private int GetEffectiveLodCount()
        {
            SubMeshTriangles[] submeshes = meshData.submeshes;
            if (submeshes == null)
            {
                return 0;
            }

            int max = 0;
            for (int i = 0; i < submeshes.Length; i++)
            {
                int lodCount = GetDisplayedLodCount(submeshes[i]);
                if (lodCount > max)
                {
                    max = lodCount;
                }
            }

            return max;
        }

        private int GetTotalTriangleIndicesForLod(int lodIndex)
        {
            SubMeshTriangles[] submeshes = meshData.submeshes;
            if (submeshes == null || lodIndex < 0)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < submeshes.Length; i++)
            {
                SubMeshTriangles submesh = submeshes[i];
                if (!HasDisplayedLodLevel(submesh, lodIndex))
                {
                    continue;
                }

                total += submesh.GetTriangleCount(lodIndex);
            }

            return total;
        }

        private static int GetDisplayedLodCount(SubMeshTriangles submesh)
        {
            if (submesh == null)
            {
                return 0;
            }

            int lodCount = submesh.LODCount();
            if (lodCount > 0)
            {
                return lodCount;
            }

            return submesh.GetTriangleCount(0) > 0 ? 1 : 0;
        }

        private static bool HasDisplayedLodLevel(SubMeshTriangles submesh, int lodIndex)
        {
            if (submesh == null || lodIndex < 0)
            {
                return false;
            }

            int lodCount = submesh.LODCount();
            if (lodCount > 0)
            {
                return lodIndex < lodCount;
            }

            return lodIndex == 0 && submesh.GetTriangleCount(0) > 0;
        }

        private int GetSubmeshesWithLodsCount()
        {
            SubMeshTriangles[] submeshes = meshData.submeshes;
            if (submeshes == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < submeshes.Length; i++)
            {
                SubMeshTriangles submesh = submeshes[i];
                if (submesh != null && submesh.lodRanges != null && submesh.lodRanges.Count > 0)
                {
                    count++;
                }
            }
            return count;
        }

        private int GetBlendShapeCount()
        {
            if (meshData.blendShapes == null)
            {
                return 0;
            }
            return meshData.blendShapes.Length;
        }

        private int GetBlendShapeFrameCount()
        {
            UMABlendShape[] blendShapes = meshData.blendShapes;
            if (blendShapes == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < blendShapes.Length; i++)
            {
                UMABlendShape blendShape = blendShapes[i];
                if (blendShape == null || blendShape.frames == null)
                {
                    continue;
                }
                total += blendShape.frames.Length;
            }
            return total;
        }

        private int GetAnimatedBonesCount()
        {
            BaseUpdatedObject[] animatedBones = slotDataAsset.animatedBones;
            if (animatedBones == null)
            {
                return 0;
            }
            return animatedBones.Length;
        }

        private int GetClothCoefficientCount()
        {
            ClothSkinningCoefficient[] clothSkinning = meshData.clothSkinning;
            if (clothSkinning == null)
            {
                return 0;
            }
            return clothSkinning.Length;
        }

        private int GetLegacyBoneWeightCount(UMABoneWeight value)
        {
            int count = 0;
            if (value.weight0 > 0f)
            {
                count++;
            }
            if (value.weight1 > 0f)
            {
                count++;
            }
            if (value.weight2 > 0f)
            {
                count++;
            }
            if (value.weight3 > 0f)
            {
                count++;
            }
            return count;
        }

        private static void EnsureFoldoutCount(List<bool> foldouts, int count)
        {
            while (foldouts.Count < count)
            {
                foldouts.Add(false);
            }

            while (foldouts.Count > count)
            {
                foldouts.RemoveAt(foldouts.Count - 1);
            }
        }

        private void DrawVector2ArrayFoldout(string label, Vector2[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.Vector2Field(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawVector3ArrayFoldout(string label, Vector3[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.Vector3Field(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawVector4ArrayFoldout(string label, Vector4[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.Vector4Field(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawColor32ArrayFoldout(string label, Color32[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.ColorField(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawIntArrayFoldout(string label, int[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.IntField(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawByteArrayFoldout(string label, byte[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    EditorGUILayout.IntField(label + " [" + i + "]", values[i]);
                }
            }
        }

        private void DrawManagedBoneWeightsFoldout(string label, BoneWeight1[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    BoneWeight1 value = values[i];
                    EditorGUILayout.LabelField(label + " [" + i + "]", "boneIndex=" + value.boneIndex + ", weight=" + value.weight);
                }
            }
        }

        private void DrawLegacyBoneWeightsFoldout(string label, UMABoneWeight[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    UMABoneWeight value = values[i];
                    EditorGUILayout.LabelField(label + " [" + i + "]",
                        "indices=(" + value.boneIndex0 + ", " + value.boneIndex1 + ", " + value.boneIndex2 + ", " + value.boneIndex3 + ")"
                        + ", weights=(" + value.weight0 + ", " + value.weight1 + ", " + value.weight2 + ", " + value.weight3 + ")");
                }
            }
        }

        private void DrawUmaBonesFoldout(string label, UMATransform[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    UMATransform value = values[i];
                    if (value == null)
                    {
                        EditorGUILayout.LabelField(label + " [" + i + "]", "<null>");
                        continue;
                    }

                    EditorGUILayout.LabelField(label + " [" + i + "]", value.name ?? string.Empty);
                    EditorGUILayout.IntField("Hash", value.hash);
                    EditorGUILayout.IntField("Parent", value.parent);
                    EditorGUILayout.Vector3Field("Position", value.position);
                    Vector4 rotation = new Vector4(value.rotation.x, value.rotation.y, value.rotation.z, value.rotation.w);
                    EditorGUILayout.Vector4Field("Rotation", rotation);
                    EditorGUILayout.Vector3Field("Scale", value.scale);
                    EditorGUILayout.Space(2f);
                }
            }
        }

        private void DrawClothSkinningFoldout(string label, ClothSkinningCoefficient[] values, ref bool foldout)
        {
            foldout = EditorGUILayout.Foldout(foldout, label + " (" + (values != null ? values.Length : 0) + ")", true);
            if (!foldout || values == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                for (int i = 0; i < values.Length; i++)
                {
                    ClothSkinningCoefficient value = values[i];
                    EditorGUILayout.LabelField(label + " [" + i + "]",
                        "maxDistance=" + value.maxDistance + ", collisionSphereDistance=" + value.collisionSphereDistance);
                }
            }
        }
    }

}
#endif
