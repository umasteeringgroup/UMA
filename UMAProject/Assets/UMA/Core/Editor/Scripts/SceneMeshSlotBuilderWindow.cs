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
using static UMA.Editors.UMASlotSkinTransfer;

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
            // Older saved states have none of these: they start at the field defaults.
            public int weightMethod = (int)Weighting.UMAWeightMethod.RobustInpaint;
            public float weightMatchCm = 4f;
            public float weightMatchAngle = 35f;
            public int weightMaxBones = 4;
            public bool weightMirror;
        }

        /// <summary>The weighting methods the window offers (Simulated Fit needs the Clothes Designer's simulation).</summary>
        private static readonly Weighting.UMAWeightMethod[] WeightMethods =
        {
            Weighting.UMAWeightMethod.RobustInpaint,
            Weighting.UMAWeightMethod.TightSurface,
            Weighting.UMAWeightMethod.Volumetric,
            Weighting.UMAWeightMethod.ExactMatch,
            Weighting.UMAWeightMethod.Auto
        };

        [SerializeField] private DynamicCharacterAvatar targetAvatar;
        [SerializeField] private UnityEngine.Object sourceObject;
        [SerializeField] private DefaultAsset outputFolder;
        [SerializeField] private string slotName = "SceneMeshSlot";
        [SerializeField] private bool combineSourceSubmeshes = true;
        [SerializeField] private int sourceSubmeshIndex;
        [SerializeField] private int targetRendererIndex;
        [SerializeField] private bool skipReweight;
        [SerializeField] private string manualBoneName = "HeadAdjust";
        [SerializeField] private Weighting.UMAWeightMethod weightMethod = Weighting.UMAWeightMethod.RobustInpaint;
        [SerializeField] private float weightMatchCm = 4f;
        [SerializeField] private float weightMatchAngle = 35f;
        [SerializeField] private int weightMaxBones = 4;
        [SerializeField] private bool weightMirror;
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
                DrawWeightingOptions();

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

        /// <summary>
        /// How the slot gets its bone weights: one of the shared weighting methods (Docs/WeightTransfer.md), or a
        /// single bone. The single bone is also the fallback for vertices no body is near.
        /// </summary>
        private void DrawWeightingOptions()
        {
            var labels = new GUIContent[WeightMethods.Length + 1];
            int index = WeightMethods.Length;
            for (int i = 0; i < WeightMethods.Length; i++)
            {
                labels[i] = new GUIContent(Weighting.UMAWeightSettings.DisplayName(WeightMethods[i]), Weighting.UMAWeightSettings.Describe(WeightMethods[i]));
                if (!skipReweight && WeightMethods[i] == weightMethod) index = i;
            }
            labels[WeightMethods.Length] = new GUIContent("Single Bone (no reweight)", "Every vertex is weighted fully to the bone chosen below: for rigid things such as a helmet or a sword.");
            if (!skipReweight && index == WeightMethods.Length) index = 0;
            index = EditorGUILayout.Popup(new GUIContent("Weighting", "How the slot gets its bone weights from the target character's other slots. Hover over a method for when to use it."), index, labels);
            skipReweight = index == WeightMethods.Length;
            if (!skipReweight) weightMethod = WeightMethods[index];
            EditorGUILayout.LabelField(skipReweight ? labels[WeightMethods.Length].tooltip : Weighting.UMAWeightSettings.Describe(weightMethod), EditorStyles.wordWrappedMiniLabel);
            if (!skipReweight)
            {
                if (weightMethod == Weighting.UMAWeightMethod.RobustInpaint || weightMethod == Weighting.UMAWeightMethod.Volumetric || weightMethod == Weighting.UMAWeightMethod.Auto)
                {
                    weightMatchCm = EditorGUILayout.Slider(new GUIContent("Match Distance (cm)", "Mesh this close to the body (and facing the same way) copies it; mesh further out is filled in, or takes the field's weights."), weightMatchCm, 0.5f, 15f);
                    weightMatchAngle = EditorGUILayout.Slider(new GUIContent("Match Angle", "How far the mesh's normal may turn from the body's and still copy it (degrees)."), weightMatchAngle, 5f, 90f);
                }
                weightMaxBones = EditorGUILayout.IntSlider(new GUIContent("Max Bones", "The most bones a vertex keeps."), weightMaxBones, 1, 8);
                weightMirror = EditorGUILayout.Toggle(new GUIContent("Mirror Left/Right", "Make the two sides match: each vertex averages with its mirror image, Left and Right bones swapped by name."), weightMirror);
            }
            DrawManualBoneSelector(skipReweight ? "Bone" : "Fallback Bone");
        }

        private Weighting.UMAWeightSettings WeightSettings() => new Weighting.UMAWeightSettings
        {
            Method = weightMethod,
            MatchDistance = Mathf.Max(0.005f, weightMatchCm * 0.01f),
            MatchAngleDegrees = weightMatchAngle,
            MaxBones = Mathf.Clamp(weightMaxBones, 1, 8),
            MirrorLeftRight = weightMirror
        };

        private void DrawManualBoneSelector(string label)
        {
            SkinnedMeshRenderer renderer = GetSelectedTargetRenderer(false);
            Transform[] bones = renderer != null ? renderer.bones : null;
            var content = new GUIContent(label, skipReweight ? "The bone every vertex is weighted to." : "The bone for vertices no body is found near.");
            if (bones == null || bones.Length == 0)
            {
                manualBoneName = EditorGUILayout.TextField(content, manualBoneName);
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

            int selected = EditorGUILayout.Popup(content, currentIndex, Array.ConvertAll(boneNames, n => new GUIContent(n)));
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

                EditorUtility.DisplayProgressBar(WindowTitle, skipReweight ? "Weighting to one bone..." : "Weighting (" + Weighting.UMAWeightSettings.DisplayName(weightMethod) + ")...", 0.55f);
                Weighting.UMAWeightReport weightReport = null;
                WeightTransferResult weights = skipReweight
                    ? BuildManualWeights(sourceGeometry.VertexCount, manualBoneIndex)
                    : TransferWeights(sourceGeometry, target, targetSurface, WeightSettings(), manualBoneIndex, out weightReport);
                LogWeightTransferVertexDiagnostic(sourceGeometry, target, targetSurface, weights, "HeadAdjust", WeightDiagnosticVertexIndex);

                EditorUtility.DisplayProgressBar(WindowTitle, "Building UMA slot mesh...", 0.72f);
                slotMesh = BuildSlotMesh(sourceGeometry, target, weights, new SlotMeshOptions
                {
                    SlotName = slotName,
                    ClearNormals = clearNormals,
                    ClearTangents = clearTangents,
                    RecalculateMissingNormals = recalculateMissingNormals,
                    RecalculateTangents = recalculateTangents
                });
                temporaryRendererObject = CreateTemporaryRenderer(target.Renderer);
                var tempRenderer = temporaryRendererObject.GetComponent<SkinnedMeshRenderer>();
                tempRenderer.sharedMesh = slotMesh;
                tempRenderer.sharedMaterials = source.Materials;
                tempRenderer.bones = target.Bones;
                tempRenderer.rootBone = target.RootBone;
                tempRenderer.updateWhenOffscreen = true;

                EditorUtility.DisplayProgressBar(WindowTitle, "Saving SlotDataAsset...", 0.90f);
                UMASlotPublisher.Result published = UMASlotPublisher.Publish(
                    BuildPublishRequest(source, sourceGeometry, target), tempRenderer, target);
                string assetPath = published.SlotPath;
                SlotDataAsset createdSlot = published.Slot;
                UMAWardrobeRecipe createdRecipe = published.Recipe;
                if (weights.FallbackVertices > 0)
                {
                    Debug.LogWarning($"[UMA] Scene mesh slot builder used the manual bone fallback for {weights.FallbackVertices} vertex/vertices.");
                }

                // Each created asset was saved where it was made; nothing else in the project is saved.
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
                string weighting = skipReweight ? "single bone " + target.Bones[manualBoneIndex].name : weightReport != null ? weightReport.Summary() : Weighting.UMAWeightSettings.DisplayName(weightMethod);
                Debug.Log($"[UMA] Created SlotDataAsset '{assetPath}' from '{source.DisplayName}' for race '{raceName}'{recipeLog}. Vertices: {sourceGeometry.VertexCount}, triangles: {sourceGeometry.Triangles.Length / 3}, target renderers sampled: {targetSurface.SampledRendererCount}. Weights: {weighting}");
                if (weightReport != null)
                {
                    foreach (Weighting.UMAWeightScore score in weightReport.Scores) Debug.Log("[UMA] Scene mesh slot weights " + (score.Method == weightReport.Used ? "chosen: " : "tried: ") + score);
                    foreach (string message in weightReport.Messages) Debug.Log("[UMA] Scene mesh slot weights: " + message);
                }
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

        /// <summary>The window's choices as a publish request.</summary>
        private UMASlotPublisher.Request BuildPublishRequest(SourceSelection source, SourceGeometry sourceGeometry, TargetData target)
        {
            return new UMASlotPublisher.Request
            {
                SlotName = slotName,
                FolderPath = GetOutputFolderPath(),
                SlotGroup = slotGroup,
                Tags = ParseTags(tagsCsv),
                MaxLOD = maxLOD,
                SourceSubmeshIndex = combineSourceSubmeshes ? 0 : sourceGeometry.SourceSubmeshIndex,
                ClearNormals = clearNormals,
                ClearTangents = clearTangents,
                AddToGlobalLibrary = addToGlobalLibrary,
                OverwriteExisting = overwriteExistingAsset,
                CreateWardrobeRecipe = createWardrobeRecipe,
                Race = target.RaceData,
                WardrobeSlot = createWardrobeRecipe ? GetSelectedWardrobeSlot(target.RaceData) : null,
                ExistingOverlay = existingOverlayAsset,
                OverlayMaterial = generatedOverlayMaterial,
                SourceMaterial = GetSourceMaterialForOverlay(source, sourceGeometry)
            };
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
                generatedOverlayMaterialRef = SerializeObjectReference(generatedOverlayMaterial),
                weightMethod = (int)weightMethod,
                weightMatchCm = weightMatchCm,
                weightMatchAngle = weightMatchAngle,
                weightMaxBones = weightMaxBones,
                weightMirror = weightMirror
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
                weightMethod = Array.IndexOf(WeightMethods, (Weighting.UMAWeightMethod)state.weightMethod) >= 0 ? (Weighting.UMAWeightMethod)state.weightMethod : Weighting.UMAWeightMethod.RobustInpaint;
                weightMatchCm = Mathf.Clamp(state.weightMatchCm, 0.5f, 15f);
                weightMatchAngle = Mathf.Clamp(state.weightMatchAngle, 5f, 90f);
                weightMaxBones = Mathf.Clamp(state.weightMaxBones, 1, 8);
                weightMirror = state.weightMirror;

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

    }
}

#endif
