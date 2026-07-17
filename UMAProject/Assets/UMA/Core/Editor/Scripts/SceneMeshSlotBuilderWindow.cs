#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    public class SceneMeshSlotBuilderWindow : EditorWindow
    {
        private const string WindowTitle = "Scene Mesh Slot";
        private const string PersistedStateKey = "UMA_SceneMeshSlotBuilderWindow_State_v1";
        private const float Epsilon = 0.000001f;
        private const int MaxTransferredInfluences = 12;
        private const int WeightDiagnosticVertexIndex = 1003;

        [Serializable]
        private sealed class PersistedState
        {
            public string targetAvatarRef;
            public string sourceObjectRef;
            public string outputFolderRef;
            public string slotName;
            public bool combineSourceSubmeshes;
            public int sourceSubmeshIndex;
            public int targetRendererIndex;
            public bool skipReweight;
            public string manualBoneName;
            public bool addToGlobalLibrary;
            public bool overwriteExistingAsset;
            public bool clearNormals;
            public bool clearTangents;
            public bool recalculateMissingNormals;
            public bool recalculateTangents;
            public string slotGroup;
            public string tagsCsv;
            public int maxLOD;
            public bool createWardrobeRecipe;
            public string wardrobeSlotName;
            public string existingOverlayAssetRef;
            public string generatedOverlayMaterialRef;
        }

        [SerializeField] private DynamicCharacterAvatar targetAvatar;
        [SerializeField] private UnityEngine.Object sourceObject;
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private string slotName = "SceneMeshSlot";
        [SerializeField] private bool combineSourceSubmeshes = true;
        [SerializeField] private int sourceSubmeshIndex;
        [SerializeField] private int targetRendererIndex;
        [SerializeField] private bool skipReweight;
        [SerializeField] private string manualBoneName = "HeadAdjust";
        [SerializeField] private bool addToGlobalLibrary = true;
        [SerializeField] private bool overwriteExistingAsset;
        [SerializeField] private bool clearNormals;
        [SerializeField] private bool clearTangents;
        [SerializeField] private bool recalculateMissingNormals = true;
        [SerializeField] private bool recalculateTangents;
        [SerializeField] private string slotGroup = string.Empty;
        [SerializeField] private string tagsCsv = string.Empty;
        [SerializeField] private int maxLOD = -1;
        [SerializeField] private bool createWardrobeRecipe = true;
        [SerializeField] private int wardrobeSlotIndex;
        [SerializeField] private OverlayDataAsset existingOverlayAsset;
        [SerializeField] private UMAMaterial generatedOverlayMaterial;

        private UnityEngine.Object lastSourceObject;
        private RaceData lastWardrobeRace;
        private Vector2 scrollPosition;

        [MenuItem("UMA/Content Creation/Slots/Scene Mesh Slot Builder", priority = 121)]
        public static void OpenWindow()
        {
            ShowWindow(null);
        }

        public static void ShowWindow(DynamicCharacterAvatar avatar)
        {
            var window = GetWindow<SceneMeshSlotBuilderWindow>(WindowTitle);
            window.minSize = new Vector2(520f, 430f);
            if (avatar != null)
            {
                window.targetAvatar = avatar;
            }
            window.EnsureDefaultFolder();
            window.RefreshManualBoneName();
            window.Show();
        }

        private void OnEnable()
        {
            LoadPersistedState();
            EnsureDefaultFolder();
            RefreshManualBoneName();
        }

        private void OnDisable()
        {
            SavePersistedState();
        }

        private void OnGUI()
        {
            EnsureDefaultFolder();

            using (var changeScope = new EditorGUI.ChangeCheckScope())
            {
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                EditorGUILayout.LabelField("Create SlotDataAsset From Scene Mesh", EditorStyles.boldLabel);
                EditorGUILayout.Space();

                EditorGUI.BeginChangeCheck();
                targetAvatar = (DynamicCharacterAvatar)EditorGUILayout.ObjectField("Target DCA", targetAvatar, typeof(DynamicCharacterAvatar), true);
                if (EditorGUI.EndChangeCheck())
                {
                    targetRendererIndex = 0;
                    RefreshManualBoneName();
                }

                DrawTargetRendererSelector();

                EditorGUI.BeginChangeCheck();
                sourceObject = EditorGUILayout.ObjectField("Source Mesh", sourceObject, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                {
                    UpdateSlotNameFromSource();
                }

                DrawSourceOptions();

                EditorGUILayout.Space();
                slotName = EditorGUILayout.TextField("Slot Name", slotName);
                slotGroup = EditorGUILayout.TextField("Slot Group", slotGroup);
                tagsCsv = EditorGUILayout.TextField("Tags", tagsCsv);
                maxLOD = EditorGUILayout.IntField("Max LOD", maxLOD);

                EditorGUILayout.BeginHorizontal();
                outputFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolder, typeof(DefaultAsset), false);
                if (GUILayout.Button("Assets", GUILayout.Width(70f)))
                {
                    outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
                }
                EditorGUILayout.EndHorizontal();

                DrawWardrobeRecipeOptions();

                EditorGUILayout.Space();
                skipReweight = EditorGUILayout.ToggleLeft("Skip reweight", skipReweight);
                DrawManualBoneSelector();

                EditorGUILayout.Space();
                addToGlobalLibrary = EditorGUILayout.ToggleLeft("Add to UMA Global Library", addToGlobalLibrary);
                overwriteExistingAsset = EditorGUILayout.ToggleLeft("Overwrite existing asset", overwriteExistingAsset);
                clearNormals = EditorGUILayout.ToggleLeft("Clear normals in SlotDataAsset", clearNormals);
                clearTangents = EditorGUILayout.ToggleLeft("Clear tangents in SlotDataAsset", clearTangents);
                recalculateMissingNormals = EditorGUILayout.ToggleLeft("Recalculate missing normals", recalculateMissingNormals);
                recalculateTangents = EditorGUILayout.ToggleLeft("Recalculate tangents", recalculateTangents);

                EditorGUILayout.Space();
                string validationMessage = GetValidationMessage();
                if (!string.IsNullOrEmpty(validationMessage))
                {
                    EditorGUILayout.HelpBox(validationMessage, MessageType.Info);
                }

                using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(validationMessage)))
                {
                    string createButtonText = createWardrobeRecipe ? "Create SlotDataAsset and WardrobeRecipe" : "Create SlotDataAsset";
                    if (GUILayout.Button(createButtonText, GUILayout.Height(32f)))
                    {
                        CreateSlotDataAsset();
                    }
                }

                EditorGUILayout.EndScrollView();

                if (changeScope.changed)
                {
                    SavePersistedState();
                }
            }
        }

        private void DrawTargetRendererSelector()
        {
            SkinnedMeshRenderer[] renderers = GetLiveTargetRenderers(targetAvatar);
            if (renderers.Length == 0)
            {
                EditorGUILayout.HelpBox("Generate the target DCA before creating the slot. The tool will also try to generate it when you create the asset.", MessageType.None);
                return;
            }

            targetRendererIndex = Mathf.Clamp(targetRendererIndex, 0, renderers.Length - 1);
            if (renderers.Length == 1)
            {
                EditorGUILayout.ObjectField("Target Renderer", renderers[0], typeof(SkinnedMeshRenderer), true);
                return;
            }

            string[] names = new string[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                names[i] = renderers[i] != null ? renderers[i].name : "<missing>";
            }

            targetRendererIndex = EditorGUILayout.Popup("Target Renderer", targetRendererIndex, names);
        }

        private void DrawSourceOptions()
        {
            if (!TryResolveSource(sourceObject, out SourceSelection source, out string message))
            {
                if (!string.IsNullOrEmpty(message))
                {
                    EditorGUILayout.HelpBox(message, MessageType.None);
                }
                return;
            }

            Mesh sourceMesh = source.SharedMesh;
            EditorGUILayout.ObjectField("Resolved Mesh", sourceMesh, typeof(Mesh), false);
            EditorGUILayout.LabelField("Source Type", source.IsSkinned ? "SkinnedMeshRenderer" : "MeshFilter/MeshRenderer");

            if (sourceMesh != null && sourceMesh.subMeshCount > 1)
            {
                combineSourceSubmeshes = EditorGUILayout.ToggleLeft("Combine source submeshes into submesh 0", combineSourceSubmeshes);
                if (!combineSourceSubmeshes)
                {
                    string[] submeshNames = new string[sourceMesh.subMeshCount];
                    for (int i = 0; i < submeshNames.Length; i++)
                    {
                        submeshNames[i] = "Submesh " + i;
                    }
                    sourceSubmeshIndex = EditorGUILayout.Popup("Source Submesh", Mathf.Clamp(sourceSubmeshIndex, 0, sourceMesh.subMeshCount - 1), submeshNames);
                }
            }
            else
            {
                combineSourceSubmeshes = true;
                sourceSubmeshIndex = 0;
            }
        }

        private void DrawManualBoneSelector()
        {
            SkinnedMeshRenderer renderer = GetSelectedTargetRenderer(false);
            Transform[] bones = renderer != null ? renderer.bones : null;
            if (bones == null || bones.Length == 0)
            {
                manualBoneName = EditorGUILayout.TextField("Manual Bone", manualBoneName);
                return;
            }

            int currentIndex = FindBoneIndexByName(bones, manualBoneName);
            if (currentIndex < 0)
            {
                currentIndex = ChooseDefaultBoneIndex(bones, renderer.rootBone);
                manualBoneName = bones[currentIndex] != null ? bones[currentIndex].name : manualBoneName;
            }

            string[] boneNames = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                boneNames[i] = bones[i] != null ? bones[i].name : "<missing>";
            }

            int selected = EditorGUILayout.Popup("Manual Bone", currentIndex, boneNames);
            if (selected >= 0 && selected < bones.Length && bones[selected] != null)
            {
                manualBoneName = bones[selected].name;
            }
        }

        private string GetValidationMessage()
        {
            if (targetAvatar == null)
            {
                return "Choose the DynamicCharacterAvatar whose race and generated renderer should receive this slot.";
            }

            if (!TryResolveSource(sourceObject, out _, out string sourceMessage))
            {
                return sourceMessage;
            }

            if (string.IsNullOrWhiteSpace(slotName))
            {
                return "Enter a Slot Name.";
            }

            string folderPath = GetOutputFolderPath();
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
            {
                return "Choose a valid project output folder.";
            }

            if (createWardrobeRecipe)
            {
                RaceData raceData = GetAvatarRaceData(targetAvatar);
                if (raceData == null)
                {
                    return "Choose a target DynamicCharacterAvatar with an active race before creating a wardrobe recipe.";
                }

                string wardrobeSlot = GetSelectedWardrobeSlot(raceData);
                if (string.IsNullOrWhiteSpace(wardrobeSlot) || string.Equals(wardrobeSlot, "None", StringComparison.OrdinalIgnoreCase))
                {
                    return "Choose a Wardrobe Slot (Location) other than None.";
                }

                if (existingOverlayAsset == null && generatedOverlayMaterial == null)
                {
                    return "Select an existing OverlayDataAsset or choose an UMAMaterial for the generated overlay.";
                }
            }

            return null;
        }

        private void CreateSlotDataAsset()
        {
            Mesh bakedSourceMesh = null;
            Mesh slotMesh = null;
            GameObject temporaryRendererObject = null;

            try
            {
                EditorUtility.DisplayProgressBar(WindowTitle, "Resolving source and target...", 0.05f);
                SourceSelection source = ResolveSourceOrThrow(sourceObject);
                TargetData target = CaptureTargetData(targetAvatar, targetRendererIndex);
                int manualBoneIndex = ResolveManualBoneIndex(target, manualBoneName);

                EditorUtility.DisplayProgressBar(WindowTitle, "Baking target UMA meshes...", 0.20f);
                TargetSurface targetSurface = BuildTargetSurface(target);

                EditorUtility.DisplayProgressBar(WindowTitle, "Baking source mesh...", 0.40f);
                SourceGeometry sourceGeometry = BuildSourceGeometry(source, target.Renderer, targetSurface, out bakedSourceMesh);

                EditorUtility.DisplayProgressBar(WindowTitle, "Transferring weights...", 0.55f);
                WeightTransferResult weights = skipReweight
                    ? BuildManualWeights(sourceGeometry.VertexCount, manualBoneIndex)
                    : TransferWeights(sourceGeometry.TargetLocalVertices, targetSurface, manualBoneIndex);
                LogWeightTransferVertexDiagnostic(sourceGeometry, target, targetSurface, weights, "HeadAdjust", WeightDiagnosticVertexIndex);

                EditorUtility.DisplayProgressBar(WindowTitle, "Building UMA slot mesh...", 0.72f);
                slotMesh = BuildSlotMesh(sourceGeometry, target, weights);
                temporaryRendererObject = CreateTemporaryRenderer(target.Renderer);
                var tempRenderer = temporaryRendererObject.GetComponent<SkinnedMeshRenderer>();
                tempRenderer.sharedMesh = slotMesh;
                tempRenderer.sharedMaterials = source.Materials;
                tempRenderer.bones = target.Bones;
                tempRenderer.rootBone = target.RootBone;
                tempRenderer.updateWhenOffscreen = true;

                EditorUtility.DisplayProgressBar(WindowTitle, "Saving SlotDataAsset...", 0.90f);
                string assetPath = SaveSlotAsset(tempRenderer, target, sourceGeometry, weights.FallbackVertices);
                var createdSlot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(assetPath);
                UMAWardrobeRecipe createdRecipe = null;

                if (createWardrobeRecipe && createdSlot != null)
                {
                    EditorUtility.DisplayProgressBar(WindowTitle, "Saving wardrobe recipe...", 0.96f);
                    createdRecipe = CreateWardrobeRecipeAsset(createdSlot, source, sourceGeometry, target);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (createdRecipe != null)
                {
                    Selection.activeObject = createdRecipe;
                    EditorGUIUtility.PingObject(createdRecipe);
                }
                else if (createdSlot != null)
                {
                    Selection.activeObject = createdSlot;
                    EditorGUIUtility.PingObject(createdSlot);
                }

                string raceName = target.RaceData != null ? target.RaceData.raceName : "<unknown race>";
                string recipeLog = createdRecipe != null ? $", wardrobe recipe: '{AssetDatabase.GetAssetPath(createdRecipe)}'" : string.Empty;
                Debug.Log($"[UMA] Created SlotDataAsset '{assetPath}' from '{source.DisplayName}' for race '{raceName}'{recipeLog}. Vertices: {sourceGeometry.VertexCount}, triangles: {sourceGeometry.Triangles.Length / 3}, reweight: {(skipReweight ? "manual " + target.Bones[manualBoneIndex].name : "closest face")}, target renderers sampled: {targetSurface.SampledRendererCount}, fallback vertices: {weights.FallbackVertices}.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(WindowTitle, ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (temporaryRendererObject != null)
                {
                    DestroyImmediate(temporaryRendererObject);
                }

                if (slotMesh != null)
                {
                    DestroyImmediate(slotMesh);
                }

                if (bakedSourceMesh != null)
                {
                    DestroyImmediate(bakedSourceMesh);
                }

            }
        }

        private string SaveSlotAsset(SkinnedMeshRenderer tempRenderer, TargetData target, SourceGeometry sourceGeometry, int fallbackVertices)
        {
            string safeSlotName = MakeSafeAssetName(slotName.Trim());
            string folderPath = GetOutputFolderPath();
            string assetPath = folderPath.TrimEnd('/') + "/" + safeSlotName + "_slot.asset";
            if (AssetDatabase.LoadAssetAtPath<SlotDataAsset>(assetPath) != null)
            {
                if (overwriteExistingAsset)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                else
                {
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                }
            }

            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.name = safeSlotName;
            slot.subMeshIndex = 0;
            slot.sourceSubmeshIndex = combineSourceSubmeshes ? 0 : sourceGeometry.SourceSubmeshIndex;
            slot.slotGroup = string.IsNullOrWhiteSpace(slotGroup) ? null : slotGroup.Trim();
            slot.tags = ParseTags(tagsCsv);
            slot.maxLOD = maxLOD;
            slot.UpdateMeshData(tempRenderer, target.RootBoneName, false, 0, clearNormals, clearTangents);

            if (!UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                slot.meshData.RootBoneName = target.RootBoneName;
                slot.meshData.rootBoneHash = target.RootBoneHash;
                slot.meshData.boneNameHashes = (int[])target.BoneNameHashes.Clone();
                slot.meshData.SlotName = slot.slotName;
            }

            List<string> reasons = new List<string>();
            if (!slot.ValidateMeshData(reasons))
            {
                DestroyImmediate(slot);
                throw new InvalidOperationException("Created SlotDataAsset failed validation: " + string.Join("; ", reasons));
            }

            AssetDatabase.CreateAsset(slot, assetPath);
            EditorUtility.SetDirty(slot);

            if (addToGlobalLibrary)
            {
                UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    indexer.EvilAddAsset(typeof(SlotDataAsset), slot);
                    indexer.ForceSave();
                }
            }

            if (fallbackVertices > 0)
            {
                Debug.LogWarning($"[UMA] Scene mesh slot builder used the manual bone fallback for {fallbackVertices} vertex/vertices.");
            }

            return assetPath;
        }

        private UMAWardrobeRecipe CreateWardrobeRecipeAsset(SlotDataAsset slotAsset, SourceSelection source, SourceGeometry sourceGeometry, TargetData target)
        {
            if (slotAsset == null)
            {
                throw new InvalidOperationException("The SlotDataAsset was not created, so the wardrobe recipe cannot be generated.");
            }

            string folderPath = GetOutputFolderPath();
            string wardrobeSlot = GetSelectedWardrobeSlot(target.RaceData);
            OverlayDataAsset overlayAsset = ResolveRecipeOverlayAsset(slotAsset, source, sourceGeometry, folderPath);
            if (overlayAsset == null)
            {
                throw new InvalidOperationException("The wardrobe recipe needs an OverlayDataAsset.");
            }

            string safeSlotName = MakeSafeAssetName(slotAsset.slotName);
            string recipeName = safeSlotName + "_Wardrobe";
            string recipePath = GetAvailableAssetPath(folderPath, recipeName + ".asset");

            UMAData.UMARecipe recipe = new UMAData.UMARecipe();
            recipe.ClearDna();
            recipe.SetRace(target.RaceData);

            SlotData slotData = new SlotData(slotAsset);
            if (target.RaceData != null && !string.IsNullOrWhiteSpace(target.RaceData.raceName))
            {
                slotData.Races = new[] { target.RaceData.raceName };
            }
            slotData.AddOverlay(new OverlayData(overlayAsset));
            recipe.SetSlot(0, slotData);

            var wardrobeRecipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            wardrobeRecipe.name = recipeName;
            wardrobeRecipe.recipeType = "Wardrobe";
            wardrobeRecipe.DisplayValue = slotAsset.slotName;
            wardrobeRecipe.wardrobeSlot = wardrobeSlot;
            wardrobeRecipe.compatibleRaces = new List<string>();
            if (target.RaceData != null && !string.IsNullOrWhiteSpace(target.RaceData.raceName))
            {
                wardrobeRecipe.compatibleRaces.Add(target.RaceData.raceName);
            }
            wardrobeRecipe.Save(recipe);

            AssetDatabase.CreateAsset(wardrobeRecipe, recipePath);
            EditorUtility.SetDirty(wardrobeRecipe);

            if (addToGlobalLibrary)
            {
                UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    indexer.EvilAddAsset(typeof(UMAWardrobeRecipe), wardrobeRecipe);
                    indexer.ForceSave();
                }
            }

            return wardrobeRecipe;
        }

        private OverlayDataAsset ResolveRecipeOverlayAsset(SlotDataAsset slotAsset, SourceSelection source, SourceGeometry sourceGeometry, string folderPath)
        {
            if (existingOverlayAsset != null)
            {
                AddOverlayToIndex(existingOverlayAsset);
                return existingOverlayAsset;
            }

            return CreateGeneratedOverlayAsset(slotAsset, source, sourceGeometry, folderPath);
        }

        private OverlayDataAsset CreateGeneratedOverlayAsset(SlotDataAsset slotAsset, SourceSelection source, SourceGeometry sourceGeometry, string folderPath)
        {
            if (generatedOverlayMaterial == null)
            {
                throw new InvalidOperationException("Choose an UMAMaterial for the generated overlay.");
            }

            string safeSlotName = MakeSafeAssetName(slotAsset.slotName);
            string overlayName = safeSlotName + "_overlay";
            string overlayPath = GetAvailableAssetPath(folderPath, overlayName + ".asset");
            Material sourceMaterial = GetSourceMaterialForOverlay(source, sourceGeometry);

            var overlayAsset = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlayAsset.name = overlayName;
            overlayAsset.material = generatedOverlayMaterial;
            overlayAsset.materialName = generatedOverlayMaterial.name;

            int channelCount = generatedOverlayMaterial.channels != null ? generatedOverlayMaterial.channels.Length : 0;
            overlayAsset.textureList = new Texture[channelCount];
            overlayAsset.textureNames = new string[channelCount];
            overlayAsset.overlayBlend = new OverlayDataAsset.OverlayBlend[channelCount];

            for (int channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                Texture texture = GetTextureForChannel(sourceMaterial, generatedOverlayMaterial.channels[channelIndex]);
                overlayAsset.textureList[channelIndex] = texture;
                overlayAsset.textureNames[channelIndex] = texture != null ? texture.name : string.Empty;
                overlayAsset.overlayBlend[channelIndex] = OverlayDataAsset.OverlayBlend.Normal;
            }

            overlayAsset.ValidateBlendList();
            AssetDatabase.CreateAsset(overlayAsset, overlayPath);
            EditorUtility.SetDirty(overlayAsset);
            UMAUpdateProcessor.UpdateOverlay(overlayAsset);
            AddOverlayToIndex(overlayAsset);
            return overlayAsset;
        }

        private void AddOverlayToIndex(OverlayDataAsset overlayAsset)
        {
            if (!addToGlobalLibrary || overlayAsset == null)
            {
                return;
            }

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer != null)
            {
                indexer.EvilAddAsset(typeof(OverlayDataAsset), overlayAsset);
                indexer.ForceSave();
            }
        }

        private string GetAvailableAssetPath(string folderPath, string fileName)
        {
            string assetPath = folderPath.TrimEnd('/') + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) == null)
            {
                return assetPath;
            }

            if (overwriteExistingAsset)
            {
                AssetDatabase.DeleteAsset(assetPath);
                return assetPath;
            }

            return AssetDatabase.GenerateUniqueAssetPath(assetPath);
        }

        private Material GetSourceMaterialForOverlay(SourceSelection source, SourceGeometry sourceGeometry)
        {
            if (source.Materials == null || source.Materials.Length == 0)
            {
                return null;
            }

            if (!combineSourceSubmeshes)
            {
                int selectedMaterialIndex = Mathf.Clamp(sourceGeometry.SourceSubmeshIndex, 0, source.Materials.Length - 1);
                return source.Materials[selectedMaterialIndex];
            }

            for (int materialIndex = 0; materialIndex < source.Materials.Length; materialIndex++)
            {
                if (source.Materials[materialIndex] != null)
                {
                    return source.Materials[materialIndex];
                }
            }

            return null;
        }

        private static Texture GetTextureForChannel(Material sourceMaterial, UMAMaterial.MaterialChannel channel)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            string materialPropertyName = !string.IsNullOrEmpty(channel.materialPropertyName)
                ? channel.materialPropertyName
                : channel.sourceTextureName;
            if (string.IsNullOrEmpty(materialPropertyName) || !sourceMaterial.HasProperty(materialPropertyName))
            {
                return null;
            }

            return sourceMaterial.GetTexture(materialPropertyName);
        }

        private TargetData CaptureTargetData(DynamicCharacterAvatar avatar, int rendererIndex)
        {
            if (avatar == null)
            {
                throw new InvalidOperationException("Choose a target DynamicCharacterAvatar.");
            }

            avatar.GenerateNow();

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

        private SourceGeometry BuildSourceGeometry(SourceSelection source, SkinnedMeshRenderer targetRenderer, TargetSurface targetSurface, out Mesh bakedSourceMesh)
        {
            bakedSourceMesh = null;
            Mesh sharedMesh = source.SharedMesh;
            if (sharedMesh == null || sharedMesh.vertexCount == 0)
            {
                throw new InvalidOperationException("The source mesh is empty.");
            }

            Mesh positionMesh = sharedMesh;
            if (source.IsSkinned)
            {
                bakedSourceMesh = new Mesh { name = sharedMesh.name + "_SceneSlotBake" };
                source.SkinnedRenderer.BakeMesh(bakedSourceMesh);
                if (bakedSourceMesh.vertexCount != sharedMesh.vertexCount)
                {
                    throw new InvalidOperationException("BakeMesh changed the source vertex count. The slot builder requires stable source vertex order.");
                }
                positionMesh = bakedSourceMesh;
            }

            Vector3[] sourceVertices = positionMesh.vertices;
            Vector3[] sourceNormals = positionMesh.normals;
            Vector4[] sourceTangents = positionMesh.tangents;
            bool hasNormals = sourceNormals != null && sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents != null && sourceTangents.Length == sourceVertices.Length;

            Matrix4x4 targetWorldToLocal = targetRenderer.transform.worldToLocalMatrix;
            Matrix4x4 sourceToTargetLocal = source.IsSkinned
                ? ChooseSkinnedSourceToTargetLocalMatrix(source, sourceVertices, targetWorldToLocal, targetSurface)
                : targetWorldToLocal * source.Transform.localToWorldMatrix;
            Vector3[] targetLocalVertices = new Vector3[sourceVertices.Length];
            Vector3[] targetLocalNormals = hasNormals ? new Vector3[sourceVertices.Length] : null;
            Vector4[] targetLocalTangents = hasTangents ? new Vector4[sourceVertices.Length] : null;

            for (int i = 0; i < sourceVertices.Length; i++)
            {
                targetLocalVertices[i] = sourceToTargetLocal.MultiplyPoint3x4(sourceVertices[i]);

                if (hasNormals)
                {
                    Vector3 targetNormal = sourceToTargetLocal.MultiplyVector(sourceNormals[i]);
                    targetLocalNormals[i] = targetNormal.sqrMagnitude > Epsilon ? targetNormal.normalized : Vector3.up;
                }

                if (hasTangents)
                {
                    Vector3 tangent = new Vector3(sourceTangents[i].x, sourceTangents[i].y, sourceTangents[i].z);
                    Vector3 targetTangent = sourceToTargetLocal.MultiplyVector(tangent);
                    targetTangent = targetTangent.sqrMagnitude > Epsilon ? targetTangent.normalized : Vector3.right;
                    targetLocalTangents[i] = new Vector4(targetTangent.x, targetTangent.y, targetTangent.z, sourceTangents[i].w);
                }
            }

            int[] triangles = BuildSourceTriangles(sharedMesh);
            return new SourceGeometry
            {
                ChannelSourceMesh = sharedMesh,
                TargetLocalVertices = targetLocalVertices,
                TargetLocalNormals = targetLocalNormals,
                TargetLocalTangents = targetLocalTangents,
                Triangles = triangles,
                SourceSubmeshIndex = combineSourceSubmeshes ? 0 : sourceSubmeshIndex
            };
        }

        private static Matrix4x4 ChooseSkinnedSourceToTargetLocalMatrix(SourceSelection source, Vector3[] sourceVertices, Matrix4x4 targetWorldToLocal, TargetSurface targetSurface)
        {
            Matrix4x4 rendererLocalToTargetLocal = targetWorldToLocal * source.Transform.localToWorldMatrix;
            Matrix4x4 bakedWorldToTargetLocal = targetWorldToLocal;

            float rendererLocalScore = ScoreSourceTargetSurfaceDistance(sourceVertices, rendererLocalToTargetLocal, targetSurface);
            float bakedWorldScore = ScoreSourceTargetSurfaceDistance(sourceVertices, bakedWorldToTargetLocal, targetSurface);

            if (bakedWorldScore * 1.25f < rendererLocalScore)
            {
                LogVerboseMessage($"[UMA] Scene mesh slot builder treated baked SkinnedMeshRenderer '{source.DisplayName}' vertices as world-space because that placement is closer to the target UMA surface. Renderer-local score: {rendererLocalScore:0.#####}, world-space score: {bakedWorldScore:0.#####}.");
                return bakedWorldToTargetLocal;
            }

            return rendererLocalToTargetLocal;
        }

        private static float ScoreSourceTargetSurfaceDistance(Vector3[] sourceVertices, Matrix4x4 sourceToTargetLocal, TargetSurface targetSurface)
        {
            if (sourceVertices == null || sourceVertices.Length == 0 || targetSurface == null || targetSurface.Triangles == null || targetSurface.Triangles.Length == 0)
            {
                return float.MaxValue;
            }

            int sampleCount = Mathf.Min(sourceVertices.Length, 512);
            int step = Mathf.Max(1, sourceVertices.Length / sampleCount);
            float totalDistance = 0f;
            int testedCount = 0;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length && testedCount < sampleCount; vertexIndex += step)
            {
                Vector3 targetLocalVertex = sourceToTargetLocal.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                if (TryFindClosestTriangle(targetLocalVertex, targetSurface, out TriangleHit hit))
                {
                    totalDistance += hit.SqrDistance;
                    testedCount++;
                }
            }

            return testedCount > 0 ? totalDistance / testedCount : float.MaxValue;
        }

        private TargetSurface BuildTargetSurface(TargetData target)
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
                        DestroyImmediate(bakedMesh);
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

        private static bool AppendUnityMeshTriangles(TargetData target, int rendererIndex, string rendererName, Mesh sourceMesh, Vector3[] bakedVertices, Matrix4x4 rendererLocalToTargetLocal, int vertexBase, List<TriangleInfo> triangles)
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

        private static void ResolveBakedTriangleSlot(TargetData target, int rendererIndex, int index0, int index1, int index2, out int slotIndex, out string slotName)
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

        private static int ResolveBakedVertexSlot(TargetData target, int rendererIndex, int bakedVertexIndex, out string slotName)
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

        private WeightTransferResult TransferWeights(Vector3[] sourceVertices, TargetSurface targetSurface, int fallbackBoneIndex)
        {
            byte[] bonesPerVertex = new byte[sourceVertices.Length];
            List<BoneWeight1> allWeights = new List<BoneWeight1>(sourceVertices.Length * 4);
            int fallbackVertices = 0;

            for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
            {
                List<BoneWeight1> vertexWeights;
                if (TryFindClosestTriangle(sourceVertices[vertexIndex], targetSurface, out TriangleHit hit))
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

        private WeightTransferResult BuildManualWeights(int vertexCount, int boneIndex)
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

        [System.Diagnostics.Conditional("UMA_SCENE_SLOT_BUILDER_VERBOSE_DIAGNOSTICS")]
        private static void LogWeightTransferVertexDiagnostic(SourceGeometry source, TargetData target, TargetSurface targetSurface, WeightTransferResult weights, string expectedBoneName, int vertexIndex)
        {
            if (source.TargetLocalVertices == null || source.TargetLocalVertices.Length == 0 || weights.BonesPerVertex == null || weights.BoneWeights == null)
            {
                return;
            }

            if (weights.BonesPerVertex.Length != source.TargetLocalVertices.Length)
            {
                Debug.LogWarning("[UMA] Scene mesh slot builder could not log weight diagnostics because the vertex and weight counts do not match.");
                return;
            }

            if ((uint)vertexIndex >= source.TargetLocalVertices.Length)
            {
                Debug.LogWarning($"[UMA] Scene mesh slot builder could not log weight diagnostics because vertex {vertexIndex} is out of range for {source.TargetLocalVertices.Length} vertices.");
                return;
            }

            int[] weightOffsets = BuildWeightOffsets(weights);
            Matrix4x4 targetLocalToWorld = target.Renderer.transform.localToWorldMatrix;
            Vector3 sourceTargetLocal = source.TargetLocalVertices[vertexIndex];
            Vector3 sourceWorld = targetLocalToWorld.MultiplyPoint3x4(sourceTargetLocal);
            int weightOffset = weightOffsets[vertexIndex];
            int influenceCount = weights.BonesPerVertex[vertexIndex];
            int expectedBoneIndex = FindBoneIndexByName(target.Bones, expectedBoneName);
            float expectedWeight = 0f;
            for (int influenceIndex = 0; influenceIndex < influenceCount; influenceIndex++)
            {
                BoneWeight1 weight = weights.BoneWeights[weightOffset + influenceIndex];
                if (weight.boneIndex == expectedBoneIndex)
                {
                    expectedWeight += weight.weight;
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("[UMA] Scene mesh slot builder weight diagnostic for vertex ")
                .Append(vertexIndex)
                .AppendLine(".");
            builder.Append("Source target-local=");
            AppendVector3(builder, sourceTargetLocal);
            builder.Append(" world=");
            AppendVector3(builder, sourceWorld);
            builder.AppendLine();
            if (expectedBoneIndex < 0)
            {
                builder.AppendLine($"Expected bone '{expectedBoneName}' was not found on the target renderer bone list.");
            }

            builder.Append(expectedBoneName)
                .Append('=')
                .Append((expectedWeight * 100f).ToString("0.##"))
                .Append("% transferred weights: ");
            AppendWeightList(builder, weights.BoneWeights, weightOffset, influenceCount, target.Bones);
            builder.AppendLine();

            if (targetSurface == null || targetSurface.Triangles == null || targetSurface.Triangles.Length == 0)
            {
                builder.AppendLine("Closest-face search data is unavailable because the target surface has no triangles.");
                Debug.Log(builder.ToString());
                return;
            }

            if (!TryFindClosestTriangle(sourceTargetLocal, targetSurface, out TriangleHit hit))
            {
                builder.AppendLine("Closest-face search did not find a target triangle.");
                Debug.Log(builder.ToString());
                return;
            }

            Vector3 hitWorld = targetLocalToWorld.MultiplyPoint3x4(hit.ClosestPoint);
            Vector3 directionTargetLocal = hit.ClosestPoint - sourceTargetLocal;
            Vector3 directionWorld = hitWorld - sourceWorld;
            float distanceTargetLocal = Mathf.Sqrt(hit.SqrDistance);
            float distanceWorld = directionWorld.magnitude;
            Vector3 directionWorldNormalized = distanceWorld > Epsilon ? directionWorld / distanceWorld : Vector3.zero;

            builder.Append("Closest face renderer=")
                .Append(hit.RendererIndex)
                .Append(" '")
                .Append(hit.RendererName)
                .Append("' slot=")
                .Append(hit.SlotIndex)
                .Append(" '")
                .Append(hit.SlotName)
                .Append("' sourceSubmesh=")
                .Append(hit.SubmeshIndex)
                .Append(" triangle=")
                .Append(hit.SubmeshTriangleIndex)
                .Append(" indices=(")
                .Append(hit.Index0)
                .Append(", ")
                .Append(hit.Index1)
                .Append(", ")
                .Append(hit.Index2)
                .Append(") vertexBlend=");
            AppendVector3(builder, hit.VertexBlend);
            builder.Append(" distanceTargetLocal=")
                .Append(distanceTargetLocal.ToString("0.#####"))
                .Append(" distanceWorld=")
                .Append(distanceWorld.ToString("0.#####"))
                .AppendLine();

            builder.Append("Hit point target-local=");
            AppendVector3(builder, hit.ClosestPoint);
            builder.Append(" world=");
            AppendVector3(builder, hitWorld);
            builder.AppendLine();

            builder.Append("Direction source->hit target-local=");
            AppendVector3(builder, directionTargetLocal);
            builder.Append(" world=");
            AppendVector3(builder, directionWorld);
            builder.Append(" normalizedWorld=");
            AppendVector3(builder, directionWorldNormalized);
            builder.AppendLine();

            AppendFaceVertexDiagnostic(builder, "Face v0", hit.Index0, hit.A, targetLocalToWorld.MultiplyPoint3x4(hit.A), target, targetSurface);
            AppendFaceVertexDiagnostic(builder, "Face v1", hit.Index1, hit.B, targetLocalToWorld.MultiplyPoint3x4(hit.B), target, targetSurface);
            AppendFaceVertexDiagnostic(builder, "Face v2", hit.Index2, hit.C, targetLocalToWorld.MultiplyPoint3x4(hit.C), target, targetSurface);
            AppendClosestSurfaceCandidates(builder, sourceTargetLocal, targetSurface, targetLocalToWorld, 12);

            Debug.Log(builder.ToString());
        }

        [System.Diagnostics.Conditional("UMA_SCENE_SLOT_BUILDER_VERBOSE_DIAGNOSTICS")]
        private static void LogVerboseMessage(string message)
        {
            Debug.Log(message);
        }

        private static void AppendClosestSurfaceCandidates(StringBuilder builder, Vector3 point, TargetSurface targetSurface, Matrix4x4 targetLocalToWorld, int maxCandidates)
        {
            Dictionary<string, TriangleHit> bestBySurface = new Dictionary<string, TriangleHit>();
            TriangleInfo[] triangles = targetSurface.Triangles;
            for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
            {
                TriangleInfo triangle = triangles[triangleIndex];
                Vector3 closestPoint = ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
                float sqrDistance = (closestPoint - point).sqrMagnitude;
                string key = triangle.RendererIndex + ":" + triangle.SlotIndex + ":" + triangle.SlotName + ":" + triangle.SubmeshIndex;
                if (bestBySurface.TryGetValue(key, out TriangleHit existing) && existing.SqrDistance <= sqrDistance)
                {
                    continue;
                }

                bestBySurface[key] = new TriangleHit
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
            }

            List<TriangleHit> candidates = new List<TriangleHit>(bestBySurface.Values);
            candidates.Sort((left, right) => left.SqrDistance.CompareTo(right.SqrDistance));
            builder.AppendLine("Closest candidates by slot/sourceSubmesh:");
            int count = Mathf.Min(maxCandidates, candidates.Count);
            for (int candidateIndex = 0; candidateIndex < count; candidateIndex++)
            {
                TriangleHit candidate = candidates[candidateIndex];
                Vector3 candidateWorld = targetLocalToWorld.MultiplyPoint3x4(candidate.ClosestPoint);
                builder.Append("  ")
                    .Append(candidateIndex + 1)
                    .Append(" renderer=")
                    .Append(candidate.RendererIndex)
                    .Append(" '")
                    .Append(candidate.RendererName)
                    .Append("' slot=")
                    .Append(candidate.SlotIndex)
                    .Append(" '")
                    .Append(candidate.SlotName)
                    .Append("' sourceSubmesh=")
                    .Append(candidate.SubmeshIndex)
                    .Append(" triangle=")
                    .Append(candidate.SubmeshTriangleIndex)
                    .Append(" distance=")
                    .Append(Mathf.Sqrt(candidate.SqrDistance).ToString("0.#####"))
                    .Append(" hitWorld=");
                AppendVector3(builder, candidateWorld);
                builder.AppendLine();
            }
        }

        private static bool TryFindClosestTriangle(Vector3 point, TargetSurface targetSurface, out TriangleHit hit)
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
                hit = new TriangleHit
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
            }

            return bestDistance < float.MaxValue;
        }

        private static int[] BuildWeightOffsets(WeightTransferResult weights)
        {
            int[] offsets = new int[weights.BonesPerVertex.Length];
            int offset = 0;
            for (int vertexIndex = 0; vertexIndex < weights.BonesPerVertex.Length; vertexIndex++)
            {
                offsets[vertexIndex] = offset;
                offset += weights.BonesPerVertex[vertexIndex];
            }

            return offsets;
        }

        private static void AppendFaceVertexDiagnostic(StringBuilder builder, string label, int vertexIndex, Vector3 targetLocalPosition, Vector3 worldPosition, TargetData target, TargetSurface targetSurface)
        {
            builder.Append(label)
                .Append(" vertex ")
                .Append(vertexIndex)
                .Append(" target-local=");
            AppendVector3(builder, targetLocalPosition);
            builder.Append(" world=");
            AppendVector3(builder, worldPosition);
            builder.Append(" weights: ");

            if ((uint)vertexIndex >= targetSurface.VertexWeights.Length || targetSurface.VertexWeights[vertexIndex] == null)
            {
                builder.Append("<none>");
            }
            else
            {
                List<BoneWeight1> weights = targetSurface.VertexWeights[vertexIndex];
                AppendWeightList(builder, weights, 0, weights.Count, target.Bones);
            }

            builder.AppendLine();
        }

        private static void AppendWeightList(StringBuilder builder, IList<BoneWeight1> weights, int startIndex, int count, Transform[] bones)
        {
            if (count <= 0)
            {
                builder.Append("<none>");
                return;
            }

            for (int influenceIndex = 0; influenceIndex < count; influenceIndex++)
            {
                if (influenceIndex > 0)
                {
                    builder.Append(", ");
                }

                BoneWeight1 weight = weights[startIndex + influenceIndex];
                builder.Append(GetBoneDisplayName(bones, weight.boneIndex))
                    .Append(' ')
                    .Append((weight.weight * 100f).ToString("0.##"))
                    .Append('%');
            }
        }

        private static void AppendVector3(StringBuilder builder, Vector3 value)
        {
            builder.Append('(')
                .Append(value.x.ToString("0.#####"))
                .Append(", ")
                .Append(value.y.ToString("0.#####"))
                .Append(", ")
                .Append(value.z.ToString("0.#####"))
                .Append(')');
        }

        private static string GetBoneDisplayName(Transform[] bones, int boneIndex)
        {
            if ((uint)boneIndex >= bones.Length || bones[boneIndex] == null)
            {
                return "<invalid bone " + boneIndex + ">";
            }

            return bones[boneIndex].name;
        }

        private Mesh BuildSlotMesh(SourceGeometry source, TargetData target, WeightTransferResult weights)
        {
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
                name = MakeSafeAssetName(slotName.Trim()) + "_SceneSlotMesh",
                indexFormat = source.VertexCount > 65535 ? IndexFormat.UInt32 : source.ChannelSourceMesh.indexFormat
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

            CopyStaticVertexChannels(source.ChannelSourceMesh, mesh, source.VertexCount);
            mesh.subMeshCount = 1;
            mesh.SetTriangles(source.Triangles, 0, true);

            if ((bindNormals == null || clearNormals) && recalculateMissingNormals)
            {
                mesh.RecalculateNormals();
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

        private int[] BuildSourceTriangles(Mesh sourceMesh)
        {
            if (combineSourceSubmeshes || sourceMesh.subMeshCount == 1)
            {
                List<int> triangles = new List<int>();
                for (int submeshIndex = 0; submeshIndex < sourceMesh.subMeshCount; submeshIndex++)
                {
                    if (sourceMesh.GetTopology(submeshIndex) != MeshTopology.Triangles)
                    {
                        throw new InvalidOperationException($"Source submesh {submeshIndex} is not triangle topology.");
                    }
                    triangles.AddRange(sourceMesh.GetTriangles(submeshIndex));
                }
                return triangles.ToArray();
            }

            int selectedSubmesh = Mathf.Clamp(sourceSubmeshIndex, 0, sourceMesh.subMeshCount - 1);
            if (sourceMesh.GetTopology(selectedSubmesh) != MeshTopology.Triangles)
            {
                throw new InvalidOperationException($"Source submesh {selectedSubmesh} is not triangle topology.");
            }

            return sourceMesh.GetTriangles(selectedSubmesh);
        }

        private void DrawWardrobeRecipeOptions()
        {
            EditorGUILayout.Space();
            createWardrobeRecipe = EditorGUILayout.ToggleLeft("Create UMAWardrobeRecipe", createWardrobeRecipe);
            if (!createWardrobeRecipe)
            {
                return;
            }

            RaceData raceData = GetAvatarRaceData(targetAvatar);
            SyncWardrobeSlotSelection(raceData);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Recipe Race", raceData, typeof(RaceData), false);
            }

            string[] wardrobeSlotOptions = GetWardrobeSlotOptions(raceData);
            if (wardrobeSlotOptions.Length > 0)
            {
                wardrobeSlotIndex = Mathf.Clamp(wardrobeSlotIndex, 0, wardrobeSlotOptions.Length - 1);
                wardrobeSlotIndex = EditorGUILayout.Popup("Wardrobe Slot (Location)", wardrobeSlotIndex, wardrobeSlotOptions);
            }
            else
            {
                EditorGUILayout.HelpBox("The selected race has no wardrobe slots.", MessageType.Warning);
            }

            existingOverlayAsset = (OverlayDataAsset)EditorGUILayout.ObjectField("OverlayDataAsset (Optional)", existingOverlayAsset, typeof(OverlayDataAsset), false);
            if (existingOverlayAsset == null)
            {
                generatedOverlayMaterial = (UMAMaterial)EditorGUILayout.ObjectField("Generated Overlay UMAMaterial", generatedOverlayMaterial, typeof(UMAMaterial), false);
                EditorGUILayout.HelpBox("Leave OverlayDataAsset empty to generate a new overlay from the source material textures.", MessageType.None);
            }
        }

        private void SyncWardrobeSlotSelection(RaceData raceData)
        {
            if (lastWardrobeRace != raceData)
            {
                lastWardrobeRace = raceData;
                wardrobeSlotIndex = GetDefaultWardrobeSlotIndex(raceData);
            }

            string[] options = GetWardrobeSlotOptions(raceData);
            if (options.Length > 0)
            {
                wardrobeSlotIndex = Mathf.Clamp(wardrobeSlotIndex, 0, options.Length - 1);
            }
            else
            {
                wardrobeSlotIndex = 0;
            }
        }

        private static int GetDefaultWardrobeSlotIndex(RaceData raceData)
        {
            string[] options = GetWardrobeSlotOptions(raceData);
            for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                if (!string.IsNullOrWhiteSpace(options[optionIndex]) && !string.Equals(options[optionIndex], "None", StringComparison.OrdinalIgnoreCase))
                {
                    return optionIndex;
                }
            }

            return 0;
        }

        private static int GetWardrobeSlotIndex(RaceData raceData, string wardrobeSlotName)
        {
            if (string.IsNullOrWhiteSpace(wardrobeSlotName))
            {
                return -1;
            }

            string[] options = GetWardrobeSlotOptions(raceData);
            for (int optionIndex = 0; optionIndex < options.Length; optionIndex++)
            {
                if (string.Equals(options[optionIndex], wardrobeSlotName, StringComparison.OrdinalIgnoreCase))
                {
                    return optionIndex;
                }
            }

            return -1;
        }

        private static string[] GetWardrobeSlotOptions(RaceData raceData)
        {
            if (raceData == null || raceData.wardrobeSlots == null || raceData.wardrobeSlots.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> options = new List<string>();
            for (int optionIndex = 0; optionIndex < raceData.wardrobeSlots.Count; optionIndex++)
            {
                string option = raceData.wardrobeSlots[optionIndex];
                if (!string.IsNullOrWhiteSpace(option))
                {
                    options.Add(option);
                }
            }

            return options.ToArray();
        }

        private string GetSelectedWardrobeSlot(RaceData raceData)
        {
            string[] options = GetWardrobeSlotOptions(raceData);
            if (options.Length == 0)
            {
                return string.Empty;
            }

            wardrobeSlotIndex = Mathf.Clamp(wardrobeSlotIndex, 0, options.Length - 1);
            return options[wardrobeSlotIndex];
        }

        private static RaceData GetAvatarRaceData(DynamicCharacterAvatar avatar)
        {
            if (avatar == null)
            {
                return null;
            }

            RaceData raceData = null;
            if (avatar.activeRace != null)
            {
                raceData = avatar.activeRace.racedata;
                if (raceData == null)
                {
                    avatar.activeRace.SetRaceData();
                    raceData = avatar.activeRace.racedata;
                }
            }

            if (raceData == null && avatar.umaData != null && avatar.umaData.umaRecipe != null)
            {
                raceData = avatar.umaData.umaRecipe.raceData;
            }

            return raceData;
        }

        private static int[] BuildTargetBoneIndexRemap(TargetData target, SkinnedMeshRenderer renderer)
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

        private static void AppendTargetVertexWeights(List<List<BoneWeight1>> vertexWeights, Mesh sourceMesh, int[] boneIndexRemap)
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

        private static List<BoneWeight1> InterpolateTriangleWeights(TriangleHit hit, List<BoneWeight1>[] vertexWeights, int fallbackBoneIndex)
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

        private static void AddWeightedInfluences(Dictionary<int, float> merged, List<BoneWeight1> weights, float multiplier)
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

        private static void NormalizeWeights(List<BoneWeight1> weights)
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

        private static List<BoneWeight1> CreateSingleWeight(int boneIndex)
        {
            return new List<BoneWeight1>
            {
                new BoneWeight1 { boneIndex = boneIndex, weight = 1f }
            };
        }

        private static GameObject CreateTemporaryRenderer(SkinnedMeshRenderer targetRenderer)
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

        private static void CopyStaticVertexChannels(Mesh sourceMesh, Mesh destinationMesh, int vertexCount)
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

        private static Matrix4x4 AddWeightedMatrix(Matrix4x4 accumulator, Matrix4x4 matrix, float weight)
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

        private int ResolveManualBoneIndex(TargetData target, string requestedBoneName)
        {
            int boneIndex = FindBoneIndexByName(target.Bones, requestedBoneName);
            if (boneIndex >= 0)
            {
                return boneIndex;
            }

            boneIndex = ChooseDefaultBoneIndex(target.Bones, target.RootBone);
            manualBoneName = target.Bones[boneIndex].name;
            Debug.LogWarning($"[UMA] Manual bone '{requestedBoneName}' was not found on the target renderer. Using '{manualBoneName}' instead.");
            return boneIndex;
        }

        private static int ChooseDefaultBoneIndex(Transform[] bones, Transform rootBone)
        {
            int headAdjust = FindBoneIndexByName(bones, "HeadAdjust");
            if (headAdjust >= 0)
            {
                return headAdjust;
            }

            if (rootBone != null)
            {
                int rootIndex = FindBoneIndexByName(bones, rootBone.name);
                if (rootIndex >= 0)
                {
                    return rootIndex;
                }
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    return i;
                }
            }

            return 0;
        }

        private static int FindBoneIndexByName(Transform[] bones, string boneName)
        {
            if (bones == null || string.IsNullOrEmpty(boneName))
            {
                return -1;
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && string.Equals(bones[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void RefreshManualBoneName()
        {
            SkinnedMeshRenderer renderer = GetSelectedTargetRenderer(false);
            Transform[] bones = renderer != null ? renderer.bones : null;
            if (bones == null || bones.Length == 0)
            {
                return;
            }

            if (FindBoneIndexByName(bones, manualBoneName) >= 0)
            {
                return;
            }

            int defaultIndex = ChooseDefaultBoneIndex(bones, renderer.rootBone);
            if (defaultIndex >= 0 && defaultIndex < bones.Length && bones[defaultIndex] != null)
            {
                manualBoneName = bones[defaultIndex].name;
            }
        }

        private SkinnedMeshRenderer GetSelectedTargetRenderer(bool generateIfMissing)
        {
            if (generateIfMissing && targetAvatar != null)
            {
                targetAvatar.GenerateNow();
            }

            SkinnedMeshRenderer[] renderers = GetLiveTargetRenderers(targetAvatar);
            if (renderers.Length == 0)
            {
                return null;
            }

            targetRendererIndex = Mathf.Clamp(targetRendererIndex, 0, renderers.Length - 1);
            return renderers[targetRendererIndex];
        }

        private static SkinnedMeshRenderer[] GetLiveTargetRenderers(DynamicCharacterAvatar avatar)
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

        private static bool TryResolveSource(UnityEngine.Object selectedObject, out SourceSelection source, out string message)
        {
            source = default;
            message = null;
            if (selectedObject == null)
            {
                message = "Choose a scene MeshFilter/MeshRenderer or SkinnedMeshRenderer to convert.";
                return false;
            }

            SkinnedMeshRenderer skinnedRenderer = selectedObject as SkinnedMeshRenderer;
            MeshFilter meshFilter = selectedObject as MeshFilter;
            MeshRenderer meshRenderer = selectedObject as MeshRenderer;
            GameObject gameObject = selectedObject as GameObject;
            Component component = selectedObject as Component;

            if (gameObject == null && component != null)
            {
                gameObject = component.gameObject;
            }

            if (skinnedRenderer == null && gameObject != null)
            {
                skinnedRenderer = gameObject.GetComponent<SkinnedMeshRenderer>();
            }
            if (skinnedRenderer != null)
            {
                if (EditorUtility.IsPersistent(skinnedRenderer))
                {
                    message = "Choose a SkinnedMeshRenderer instance from the scene, not a project asset.";
                    return false;
                }
                if (skinnedRenderer.sharedMesh == null)
                {
                    message = "The selected SkinnedMeshRenderer has no shared mesh.";
                    return false;
                }

                source = new SourceSelection
                {
                    IsSkinned = true,
                    SkinnedRenderer = skinnedRenderer,
                    SharedMesh = skinnedRenderer.sharedMesh,
                    Transform = skinnedRenderer.transform,
                    Materials = skinnedRenderer.sharedMaterials,
                    DisplayName = skinnedRenderer.name
                };
                return true;
            }

            if (meshFilter == null && gameObject != null)
            {
                meshFilter = gameObject.GetComponent<MeshFilter>();
            }
            if (meshRenderer == null && gameObject != null)
            {
                meshRenderer = gameObject.GetComponent<MeshRenderer>();
            }

            if (meshFilter != null)
            {
                if (EditorUtility.IsPersistent(meshFilter))
                {
                    message = "Choose a MeshFilter/MeshRenderer instance from the scene, not a project asset.";
                    return false;
                }
                if (meshFilter.sharedMesh == null)
                {
                    message = "The selected MeshFilter has no shared mesh.";
                    return false;
                }
                if (meshRenderer == null)
                {
                    message = "The selected static mesh needs a MeshRenderer on the same GameObject.";
                    return false;
                }

                source = new SourceSelection
                {
                    IsSkinned = false,
                    MeshFilter = meshFilter,
                    MeshRenderer = meshRenderer,
                    SharedMesh = meshFilter.sharedMesh,
                    Transform = meshFilter.transform,
                    Materials = meshRenderer.sharedMaterials,
                    DisplayName = meshFilter.name
                };
                return true;
            }

            message = "Choose a scene object with a MeshFilter/MeshRenderer or SkinnedMeshRenderer.";
            return false;
        }

        private static SourceSelection ResolveSourceOrThrow(UnityEngine.Object selectedObject)
        {
            if (TryResolveSource(selectedObject, out SourceSelection source, out string message))
            {
                return source;
            }

            throw new InvalidOperationException(message);
        }

        private void UpdateSlotNameFromSource()
        {
            if (sourceObject == lastSourceObject)
            {
                return;
            }

            lastSourceObject = sourceObject;
            if (!TryResolveSource(sourceObject, out SourceSelection source, out _))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(slotName) || slotName == "SceneMeshSlot")
            {
                slotName = MakeSafeAssetName(source.DisplayName);
            }
        }

        private void EnsureDefaultFolder()
        {
            if (outputFolder == null)
            {
                outputFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets");
            }
        }

        private void SavePersistedState()
        {
            var state = new PersistedState
            {
                targetAvatarRef = SerializeObjectReference(targetAvatar),
                sourceObjectRef = SerializeObjectReference(sourceObject),
                outputFolderRef = SerializeObjectReference(outputFolder),
                slotName = slotName,
                combineSourceSubmeshes = combineSourceSubmeshes,
                sourceSubmeshIndex = sourceSubmeshIndex,
                targetRendererIndex = targetRendererIndex,
                skipReweight = skipReweight,
                manualBoneName = manualBoneName,
                addToGlobalLibrary = addToGlobalLibrary,
                overwriteExistingAsset = overwriteExistingAsset,
                clearNormals = clearNormals,
                clearTangents = clearTangents,
                recalculateMissingNormals = recalculateMissingNormals,
                recalculateTangents = recalculateTangents,
                slotGroup = slotGroup,
                tagsCsv = tagsCsv,
                maxLOD = maxLOD,
                createWardrobeRecipe = createWardrobeRecipe,
                wardrobeSlotName = GetSelectedWardrobeSlot(GetAvatarRaceData(targetAvatar)),
                existingOverlayAssetRef = SerializeObjectReference(existingOverlayAsset),
                generatedOverlayMaterialRef = SerializeObjectReference(generatedOverlayMaterial)
            };

            EditorPrefs.SetString(PersistedStateKey, JsonUtility.ToJson(state));
        }

        private void LoadPersistedState()
        {
            if (!EditorPrefs.HasKey(PersistedStateKey))
            {
                return;
            }

            string json = EditorPrefs.GetString(PersistedStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                PersistedState state = JsonUtility.FromJson<PersistedState>(json);
                if (state == null)
                {
                    return;
                }

                targetAvatar = DeserializeObjectReference<DynamicCharacterAvatar>(state.targetAvatarRef) ?? targetAvatar;
                sourceObject = DeserializeObjectReference<UnityEngine.Object>(state.sourceObjectRef) ?? sourceObject;
                outputFolder = DeserializeObjectReference<DefaultAsset>(state.outputFolderRef) ?? outputFolder;
                existingOverlayAsset = DeserializeObjectReference<OverlayDataAsset>(state.existingOverlayAssetRef) ?? existingOverlayAsset;
                generatedOverlayMaterial = DeserializeObjectReference<UMAMaterial>(state.generatedOverlayMaterialRef) ?? generatedOverlayMaterial;

                slotName = string.IsNullOrWhiteSpace(state.slotName) ? slotName : state.slotName;
                combineSourceSubmeshes = state.combineSourceSubmeshes;
                sourceSubmeshIndex = state.sourceSubmeshIndex;
                targetRendererIndex = state.targetRendererIndex;
                skipReweight = state.skipReweight;
                manualBoneName = string.IsNullOrWhiteSpace(state.manualBoneName) ? manualBoneName : state.manualBoneName;
                addToGlobalLibrary = state.addToGlobalLibrary;
                overwriteExistingAsset = state.overwriteExistingAsset;
                clearNormals = state.clearNormals;
                clearTangents = state.clearTangents;
                recalculateMissingNormals = state.recalculateMissingNormals;
                recalculateTangents = state.recalculateTangents;
                slotGroup = state.slotGroup ?? string.Empty;
                tagsCsv = state.tagsCsv ?? string.Empty;
                maxLOD = state.maxLOD;
                createWardrobeRecipe = state.createWardrobeRecipe;

                RaceData raceData = GetAvatarRaceData(targetAvatar);
                if (!string.IsNullOrWhiteSpace(state.wardrobeSlotName))
                {
                    int restoredWardrobeSlotIndex = GetWardrobeSlotIndex(raceData, state.wardrobeSlotName);
                    if (restoredWardrobeSlotIndex >= 0)
                    {
                        wardrobeSlotIndex = restoredWardrobeSlotIndex;
                    }
                }

                lastWardrobeRace = raceData;
            }
            catch
            {
            }
        }

        private static string SerializeObjectReference(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath))
            {
                return "asset:" + assetPath;
            }

            GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(obj);
            return globalId.identifierType != 0 ? "gid:" + globalId.ToString() : string.Empty;
        }

        private static T DeserializeObjectReference<T>(string serializedReference) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(serializedReference))
            {
                return null;
            }

            if (serializedReference.StartsWith("asset:", StringComparison.Ordinal))
            {
                return AssetDatabase.LoadAssetAtPath<T>(serializedReference.Substring("asset:".Length));
            }

            if (serializedReference.StartsWith("gid:", StringComparison.Ordinal)
                && GlobalObjectId.TryParse(serializedReference.Substring("gid:".Length), out GlobalObjectId globalId))
            {
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as T;
            }

            return null;
        }

        private string GetOutputFolderPath()
        {
            if (outputFolder == null)
            {
                return "Assets";
            }

            string path = AssetDatabase.GetAssetPath(outputFolder);
            return AssetDatabase.IsValidFolder(path) ? path : null;
        }

        private static string MakeSafeAssetName(string value)
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

        private static string[] ParseTags(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<string>();
            }

            string[] parts = value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> tags = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string tag = parts[i].Trim();
                if (!string.IsNullOrEmpty(tag))
                {
                    tags.Add(tag);
                }
            }

            return tags.ToArray();
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
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

        private static Vector3 ClosestPointOnDegenerateTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
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

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
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

        private static Vector3 CalculateHitVertexBlend(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
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

        private struct SourceSelection
        {
            public bool IsSkinned;
            public SkinnedMeshRenderer SkinnedRenderer;
            public MeshFilter MeshFilter;
            public MeshRenderer MeshRenderer;
            public Mesh SharedMesh;
            public Transform Transform;
            public Material[] Materials;
            public string DisplayName;
        }

        private struct SourceGeometry
        {
            public Mesh ChannelSourceMesh;
            public Vector3[] TargetLocalVertices;
            public Vector3[] TargetLocalNormals;
            public Vector4[] TargetLocalTangents;
            public int[] Triangles;
            public int SourceSubmeshIndex;
            public int VertexCount => TargetLocalVertices != null ? TargetLocalVertices.Length : 0;
        }

        private sealed class TargetData
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

        private sealed class TargetSurface
        {
            public TriangleInfo[] Triangles;
            public List<BoneWeight1>[] VertexWeights;
            public Matrix4x4[] CurrentSkinMatrices;
            public int SampledRendererCount;
        }

        private sealed class WeightTransferResult
        {
            public byte[] BonesPerVertex;
            public BoneWeight1[] BoneWeights;
            public int FallbackVertices;
        }

        private struct TriangleInfo
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

        private struct TriangleHit
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
