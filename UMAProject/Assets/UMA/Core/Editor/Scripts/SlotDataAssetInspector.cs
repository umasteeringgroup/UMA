#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;

namespace UMA.Editors
{
    [CustomEditor(typeof(SlotDataAsset))]
    [CanEditMultipleObjects]
    public class SlotDataAssetInspector : Editor
    {
        enum SlotPreviewMode { ThisSlot, WeldSlot, BothSlots };

        [Serializable]
        private class PersistedSectionState
        {
            public bool smooshFoldout;
            public bool utilitiesFoldout;
            public string weldToSlotGuid;
            public string bindposeSourceSlotGuid;
            public string uma3DonorSlotGuid;
            public bool overrideUma3AxisConversion;
            public Vector3 uma3AxisConversion = new Vector3(0f, 0f, 90f);
            public float weldDistance = 0.0001f;
            public string selectedRaceName;
            public int uvChannel;
            public int uvChannelToMirror;
            public int triplanarUvChannel = 1;
            public float triplanarTileU = 1f;
            public float triplanarTileV = 1f;
            public int normalCopyMode;
            public int blendshapeCopyMode;
            public int previewLodLevel;
            public bool weightRecalculatedNormalsByTriangleSize;
        }

        static string[] RegularSlotFields = new string[] { "slotName", "slotGroup", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "_oldSlotName", "tags", "isWildCardSlot", "Races", "smooshOffset", "smooshExpand", "Welds" };
        static string[] WildcardSlotFields = new string[] { "slotName", "slotGroup", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "_oldSlotName", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "_slotDNA", "meshData", "subMeshIndex", "Welds" };
        private static readonly string[] TriplanarUvChannelLabels = new string[] { "0 (uv)", "1 (uv2)", "2 (uv3)", "3 (uv4)" };
        SerializedProperty CharacterBegun;
        SerializedProperty SlotAtlassed;
        SerializedProperty SlotProcessed;
        SerializedProperty SlotBeginProcessing;
        SerializedProperty DNAApplied;
        SerializedProperty CharacterCompleted;
        SerializedProperty MaxLOD;
        SerializedProperty isClippingPlane;
        SerializedProperty clippingPlaneOffset;
        SerializedProperty isSmooshable;
        SerializedProperty smooshOffset;
        SerializedProperty smooshExpand;
        SerializedProperty oldSlotName;
        SlotDataAsset slot;
        SlotDataAsset WeldToSlot = null;

        bool lodFoldout;

        // Source slot for bindpose conformity
        SlotDataAsset bindposeSourceSlot = null;
        string lastBindposeInfo = "";
        SlotDataAsset uma3DonorSlot = null;
        bool overrideUma3AxisConversion;
        Vector3 uma3AxisConversion = new Vector3(0f, 0f, 90f);

        bool CopyNormals;
        bool CopyBoneWeights;
        bool clearNormals;
        bool clearTangents;
        UMA.SlotDataAsset.BlendshapeCopyMode blendshapeCopyMode;
        UMA.SlotDataAsset.NormalCopyMode normalCopyMode;
        bool AverageNormals;
        float weldDistance = 0.0001f;
        bool reConfigurePreview = false;
        private static string lastInfo = "";
        private int selectedRaceIndex = -1;
        private List<RaceData> foundRaces = new List<RaceData>();
        private List<string> foundRaceNames = new List<string>();
        private int uvChannel;
        private int uvChannelToMirror;
        private int triplanarUvChannel = 1;
        private float triplanarTileU = 1f;
        private float triplanarTileV = 1f;
        private bool weightRecalculatedNormalsByTriangleSize;
        private string recalculateNormalsInfo = string.Empty;
        private bool exportIncludeRig = true;
        private string persistedSectionStateKey;
        private string persistedSectionStateCache;
        private int extractBlendshapeIndex;
        private bool udimInfoFoldout;
        private bool udimSeamMapFoldout;
        private Vector2 udimSeamMapScrollPosition;
        private readonly HashSet<int> expandedUdimWeldPointLists = new HashSet<int>();
        private readonly Dictionary<int, Vector2> udimWeldPointScrollPositions = new Dictionary<int, Vector2>();

        // Animated bones selection
        private bool animatedBonesFoldout;
        private List<bool> boneSelection = new List<bool>();
        private Vector2 boneSelectionScrollPos;
        private string boneFilter = string.Empty;

        public override bool HasPreviewGUI() => true;
        MeshPreview MeshPreview;
        Mesh meshToPreview;
        // Make rotation per-inspector (not static) so multi-inspector drags don't conflict
        Vector3 previewRotation = Vector3.zero;
        int previewLodLevel;
        // Track last built rotation to know when to rebuild
        Vector3 lastBuiltRotation = new Vector3(9999, 9999, 9999);
        int lastBuiltLodLevel = -1;
        SlotPreviewMode previewMode = SlotPreviewMode.ThisSlot;
        int previewVertex = -1;

        // Track which target the current preview was built for
        private SlotDataAsset previewForTarget = null;

        private static bool IsEditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

        [MenuItem("Assets/Create/UMA/Core/Custom Slot Asset")]
        public static void CreateCustomSlotAssetMenuItem()
        {
            CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Custom");
        }

        [MenuItem("Assets/Create/UMA/Core/Wildcard Slot Asset")]
        public static void CreateWildcardSlotAssetMenuItem()
        {
            SlotDataAsset wildcard = CustomAssetUtility.CreateAsset<SlotDataAsset>("", true, "Wildcard", true);
            wildcard.isWildCardSlot = true;
            wildcard.name = "WildCard";
            EditorUtility.SetDirty(wildcard);
            string path = AssetDatabase.GetAssetPath(wildcard.GetEntityId());
            AssetDatabase.ImportAsset(path);
            EditorUtility.DisplayDialog("UMA", "Wildcard slot created. You should first change the SlotName in the inspector, and then add it to the global library or to a scene library", "OK");
        }

        private void OnDestroy()
        {
            DisposePreview();
        }

        void OnEnable()
        {
            previewRotation = Vector3.zero;
            // Defer initialization until editor is stable
            if (IsEditorBusy || target == null)
            {
                EditorApplication.delayCall += () =>
                {
                    if (this != null) OnEnable();
                };
                return;
            }

            // Dispose on domain reload proactively
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;

            if (serializedObject == null || serializedObject.targetObject == null)
                return;

            CharacterBegun = serializedObject.FindProperty("CharacterBegun");
            SlotAtlassed = serializedObject.FindProperty("SlotAtlassed");
            DNAApplied = serializedObject.FindProperty("DNAApplied");
            SlotProcessed = serializedObject.FindProperty("SlotProcessed");
            SlotBeginProcessing = serializedObject.FindProperty("SlotBeginProcessing");
            CharacterCompleted = serializedObject.FindProperty("CharacterCompleted");
            MaxLOD = serializedObject.FindProperty("maxLOD");
            isClippingPlane = serializedObject.FindProperty("isClippingPlane");
            clippingPlaneOffset = serializedObject.FindProperty("clippingPlaneOffset");
            isSmooshable = serializedObject.FindProperty("isSmooshable");
            smooshExpand = serializedObject.FindProperty("smooshExpand");
            smooshOffset = serializedObject.FindProperty("smooshOffset");
            oldSlotName = serializedObject.FindProperty("_oldSlotName");

            slot = target as SlotDataAsset;
            persistedSectionStateKey = GetPersistedSectionStateKey(slot);

            SetRaceListsSafe();
            RestorePersistedSectionState();

            if (slot != null)
            {
                if (slot.tags == null)
                {
                    slot.backingTags = new List<string>();
                }
                else
                {
                    slot.backingTags = new List<string>(slot.tags);
                }
                slot.tagList = GUIHelper.InitGenericTagsList(slot.backingTags);
            }
        }

        private void HandleBeforeAssemblyReload()
        {
            DisposePreview();
        }

        private void DisposePreview()
        {
            if (meshToPreview != null)
            {
                DestroyImmediate(meshToPreview);
                meshToPreview = null;
            }
            if (MeshPreview != null)
            {
                try { MeshPreview.Dispose(); } catch { }
                MeshPreview = null;
            }
            previewForTarget = null;
            lastBuiltLodLevel = -1;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
            PersistSectionStateIfNeeded(true);
            DisposePreview();
        }

        private void SetRaceListsSafe()
        {
            foundRaces.Clear();
            foundRaceNames.Clear();
            foundRaces.Add(null);
            foundRaceNames.Add("None Set");

            try
            {
                var indexer = UMAAssetIndexer.Instance;
                if (indexer == null) return;
                RaceData[] raceDataArray = indexer.GetAllRaces();
                if (raceDataArray == null) return;

                foreach (RaceData race in raceDataArray)
                {
                    if (race != null && race.raceName != "RaceDataPlaceholder")
                    {
                        foundRaces.Add(race);
                        foundRaceNames.Add(race.raceName);
                    }
                }
            }
            catch
            {
                // Indexer may be reloading. We'll just show the "None Set" option.
            }
        }

        public override void OnInspectorGUI()
        {
            // Busy or invalid state protections
            if (IsEditorBusy)
            {
                EditorGUILayout.HelpBox("Unity is compiling/reloading. Please wait�", MessageType.Info);
                return;
            }
            if (target == null || serializedObject == null || serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox("Inspector target is not available (asset reloading).", MessageType.Info);
                return;
            }

            // Rehydrate slot if needed
            if (slot == null)
            {
                slot = target as SlotDataAsset;
                if (slot == null)
                {
                    EditorGUILayout.HelpBox("Slot asset is not available.", MessageType.Info);
                    return;
                }
            }

            bool forceUpdate = false;
            SlotDataAsset targetAsset = target as SlotDataAsset;
            serializedObject.Update();

            bool hasAssetNameMismatch = false;
            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                SlotDataAsset candidate = targets[targetIndex] as SlotDataAsset;
                string candidatePath = AssetDatabase.GetAssetPath(candidate);
                string fileName = string.IsNullOrEmpty(candidatePath)
                    ? string.Empty : Path.GetFileNameWithoutExtension(candidatePath);
                if (candidate != null && !string.IsNullOrEmpty(fileName) && candidate.name != fileName)
                {
                    hasAssetNameMismatch = true;
                    break;
                }
            }
            if (hasAssetNameMismatch)
            {
                EditorGUILayout.HelpBox(
                    "The Unity object name does not match the slot asset filename. Use this repair " +
                    "to preserve UMA's logical Slot Name while correcting the file-facing name.",
                    MessageType.Warning);
                if (GUILayout.Button("Repair Asset Name Safely"))
                {
                    foreach (UnityEngine.Object selected in targets)
                    {
                        if (!(selected is SlotDataAsset candidate)) continue;
                        string candidatePath = AssetDatabase.GetAssetPath(candidate);
                        if (string.IsNullOrEmpty(candidatePath)) continue;
                        string logicalName = candidate.slotName;
                        Undo.RecordObject(candidate, "Repair Slot Asset Name");
                        candidate.PrepareForAssetPath(candidatePath, logicalName);
                        EditorUtility.SetDirty(candidate);
                        AssetDatabase.SaveAssetIfDirty(candidate);
                    }
                    serializedObject.Update();
                }
            }

            // Top-level change check (closed at bottom)
            EditorGUI.BeginChangeCheck();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate"))
            {
                foreach (var t in targets)
                {
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset != null)
                    {
                        slotDataAsset.ValidateMeshData();
                    }
                }
            }
            using (new EditorGUI.DisabledScope((target as SlotDataAsset) == null || UMAMeshData.IsNullOrEmptyMeshData((target as SlotDataAsset).meshData)))
            {
                if (GUILayout.Button("View MeshData"))
                {
                    MeshDataViewerWindow.Open(target as SlotDataAsset);
                }
            }
            if (GUILayout.Button("Clear Errors"))
            {
                foreach (var t in targets)
                {
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset != null)
                    {
                        slotDataAsset.Errors = "";
                        EditorUtility.SetDirty(slotDataAsset);
                    }
                }
            }
            GUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(targets.Length != 1 || slot == null ||
                UMAMeshData.IsNullOrEmptyMeshData(slot.meshData)))
            {
                if (GUILayout.Button(new GUIContent("Open in Overlay Painter",
                    "Open this slot, or its complete UDIM group, without generating an avatar."),
                    GUILayout.Height(28f)))
                {
                    UMA.TexturePaint.Editor.TexturePaintStandaloneSetupWindow.ShowForSlot(slot);
                }
            }
            if (targets.Length != 1)
                EditorGUILayout.HelpBox("Select one SlotDataAsset to open Overlay Painter.", MessageType.Info);

            if (targetAsset != null && !string.IsNullOrEmpty(targetAsset.Errors))
            {
                EditorGUILayout.HelpBox($"Errors: {targetAsset.Errors}", MessageType.Error);
            }
            if (targetAsset != null && targetAsset.isWildCardSlot)
            {
                EditorGUILayout.HelpBox("This is a wildcard slot", MessageType.Info);
            }

            if (targetAsset != null)
            {
                EditorGUILayout.LabelField($"UtilitySlot: " + targetAsset.isUtilitySlot);
            }

            GUILayout.BeginHorizontal();
            if (oldSlotName != null)
                EditorGUILayout.PropertyField(oldSlotName, new GUIContent("Old Slot Name"), GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Clear", GUILayout.Width(45)))
            {
                foreach (var t in targets)
                {
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset == null) continue;
                    slotDataAsset._oldSlotName = "";
                    EditorUtility.SetDirty(slotDataAsset);
                    GUI.changed = true;
                }
            }
            GUILayout.EndHorizontal();

            // Draw base properties
            if (slot.isWildCardSlot)
            {
                Editor.DrawPropertiesExcluding(serializedObject, WildcardSlotFields);
            }
            else
            {
                Editor.DrawPropertiesExcluding(serializedObject, RegularSlotFields);
            }

            // Animated Bones Selection — appears below the animatedBones array field
            GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.90f, 1f));
            animatedBonesFoldout = EditorGUILayout.Foldout(animatedBonesFoldout, "Animated Bones Selection");
            if (animatedBonesFoldout)
            {
                if (slot != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) && slot.meshData.umaBones != null)
                {
                    int boneCount = slot.meshData.umaBones.Length;
                    while (boneSelection.Count < boneCount)
                        boneSelection.Add(false);
                    if (boneSelection.Count > boneCount)
                        boneSelection.RemoveRange(boneCount, boneSelection.Count - boneCount);

                    // Filter
                    boneFilter = EditorGUILayout.TextField("Filter", boneFilter);
                    string filterLower = boneFilter?.Trim().ToLowerInvariant() ?? string.Empty;

                    // Select by type buttons
                    EditorGUILayout.LabelField("Select by type", EditorStyles.boldLabel);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Eyes"))     SelectBonesByKeyword("eye");
                    if (GUILayout.Button("Cheeks"))   SelectBonesByKeyword("cheek");
                    if (GUILayout.Button("Lips"))     SelectBonesByKeyword("lip");
                    if (GUILayout.Button("Nose"))     SelectBonesByKeyword("nose");
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Eyelids"))  SelectBonesByKeyword("lid");
                    if (GUILayout.Button("Face"))     SelectBonesByKeywords("eye", "cheek", "lip", "nose", "lid", "maxilar");
                    GUILayout.EndHorizontal();

                    // Clear Selection row
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Clear Selection"))
                    {
                        for (int i = 0; i < boneSelection.Count; i++)
                            boneSelection[i] = false;
                    }
                    GUILayout.EndHorizontal();

                    boneSelectionScrollPos = EditorGUILayout.BeginScrollView(boneSelectionScrollPos, GUILayout.Height(200));
                    for (int i = 0; i < boneCount; i++)
                    {
                        string boneName = slot.meshData.umaBones[i]?.name ?? $"Bone {i}";
                        if (!string.IsNullOrEmpty(filterLower) && boneName.ToLowerInvariant().IndexOf(filterLower, StringComparison.Ordinal) < 0)
                            continue;
                        boneSelection[i] = EditorGUILayout.ToggleLeft(boneName, boneSelection[i]);
                    }
                    EditorGUILayout.EndScrollView();

                    EditorGUILayout.Space(4);
                    if (GUILayout.Button("Add Checked to Animated Bones"))
                    {
                        AddCheckedBonesToAnimated();
                    }
                    if (GUILayout.Button("Add to Unbaked Animated Bones"))
                    {
                        AddCheckedBonesToUnbaked();
                    }

                    EditorGUILayout.Space(8);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Clear All Animated Bones"))
                    {
                        ClearAnimatedBones();
                    }
                    if (GUILayout.Button("Clear All Unbaked Animated Bones"))
                    {
                        ClearUnbakedAnimatedBones();
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Mesh data or bone list is not available.", MessageType.Info);
                }
            }
            GUIHelper.EndVerticalPadded(10);

            EditorGUILayout.HelpBox("Exports this SlotDataAsset to glTF 2.0 (.glb) with mesh, UVs, and skinning data. This is a minimal export (no materials or textures).", MessageType.Info);
            exportIncludeRig = EditorGUILayout.ToggleLeft("Include Rig", exportIncludeRig);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Export glTF (.glb)", GUILayout.Width(140)))
            {
                var slotDataAsset = target as SlotDataAsset;
                if (slotDataAsset != null)
                {
                    string defaultName = slotDataAsset.slotName;
                    if (string.IsNullOrEmpty(defaultName))
                    {
                        defaultName = slotDataAsset.name;
                    }
                    string path = EditorUtility.SaveFilePanelInProject("Export Slot glTF", defaultName + ".glb", "glb", "Export SlotDataAsset mesh to glTF 2.0 (.glb)");
                    if (!string.IsNullOrEmpty(path))
                    {
                        SlotDataAssetGltfExporter.ExportSlotToGlb(slotDataAsset, path, exportIncludeRig);
                        AssetDatabase.Refresh();
                    }
                }
            }

            if (GUILayout.Button("Export via UMA glTF", GUILayout.Width(140)))
            {
                var slotDataAsset = target as SlotDataAsset;
                if (slotDataAsset != null)
                {
                    string defaultName = slotDataAsset.slotName;
                    if (string.IsNullOrEmpty(defaultName))
                    {
                        defaultName = slotDataAsset.name;
                    }
                    string path = EditorUtility.SaveFilePanelInProject("Export Slot via UMA glTF", defaultName + ".gltf", "gltf", "Export SlotDataAsset using UMAGltfExporter");
                    if (!string.IsNullOrEmpty(path))
                    {
                        string assetFolder = System.IO.Path.GetDirectoryName(path);
                        string exportName = System.IO.Path.GetFileNameWithoutExtension(path);
                        UMAGltfExporter.ExportSlotDataAsset(slotDataAsset, assetFolder, exportName, exportIncludeRig);
                        AssetDatabase.Refresh();
                    }
                }
            }
            GUILayout.EndHorizontal();

            // Slot Group
            var slotGroupProp = serializedObject.FindProperty("slotGroup");
            if (slotGroupProp != null)
            {
                UMASettings settings = UMASettings.GetOrCreateSettings();
                string[] groupNames = (settings != null) ? settings.groupNames : null;
                if (groupNames == null)
                {
                    groupNames = Array.Empty<string>();
                }

                int selectedGroupIndex = -1;
                if (!slotGroupProp.hasMultipleDifferentValues)
                {
                    for (int i = 0; i < groupNames.Length; i++)
                    {
                        if (string.Equals(groupNames[i], slotGroupProp.stringValue, StringComparison.Ordinal))
                        {
                            selectedGroupIndex = i;
                            break;
                        }
                    }
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(slotGroupProp, new GUIContent("Slot Group"), GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(groupNames.Length == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUI.showMixedValue = slotGroupProp.hasMultipleDifferentValues;
                    int newSelectedGroupIndex = EditorGUILayout.Popup(selectedGroupIndex, groupNames, GUILayout.Width(110));
                    EditorGUI.showMixedValue = false;
                    if (EditorGUI.EndChangeCheck() && newSelectedGroupIndex >= 0)
                    {
                        slotGroupProp.stringValue = groupNames[newSelectedGroupIndex];
                        forceUpdate = true;
                    }
                }

                string enteredGroup = slotGroupProp.hasMultipleDifferentValues
                    ? string.Empty
                    : (slotGroupProp.stringValue ?? string.Empty).Trim();
                bool groupAlreadyRegistered = false;
                for (int groupIndex = 0; groupIndex < groupNames.Length; groupIndex++)
                {
                    if (string.Equals(groupNames[groupIndex], enteredGroup, StringComparison.Ordinal))
                    {
                        groupAlreadyRegistered = true;
                        break;
                    }
                }

                using (new EditorGUI.DisabledScope(settings == null || string.IsNullOrEmpty(enteredGroup) || groupAlreadyRegistered))
                {
                    if (GUILayout.Button(new GUIContent("Add", "Add the typed Slot Group to the UMA Settings pick list."), GUILayout.Width(45)))
                    {
                        Undo.RecordObject(settings, "Add UMA Slot Group");
                        var updatedGroups = new List<string>(groupNames) { enteredGroup };
                        settings.groupNames = updatedGroups.ToArray();
                        EditorUtility.SetDirty(settings);
                        AssetDatabase.SaveAssetIfDirty(settings);
                        forceUpdate = true;
                    }
                }

                using (new EditorGUI.DisabledScope(slotGroupProp.hasMultipleDifferentValues || string.IsNullOrEmpty(slotGroupProp.stringValue)))
                {
                    if (GUILayout.Button("Clear", GUILayout.Width(45)))
                    {
                        slotGroupProp.stringValue = string.Empty;
                        forceUpdate = true;
                    }
                }
                GUILayout.EndHorizontal();
            }

            // LOD Info
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            lodFoldout = EditorGUILayout.Foldout(lodFoldout, "LOD");
            GUILayout.EndHorizontal();
            if (lodFoldout)
            {
                GUILayout.Space(10);
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                if (UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.submeshes == null || slot.meshData.subMeshCount <= 0)
                {
                    EditorGUILayout.HelpBox("MeshData is missing.", MessageType.Info);
                }
                else
                {
                    int sm = slot.subMeshIndex;
                    if (sm < 0 || sm >= slot.meshData.subMeshCount)
                    {
                        sm = 0;
                    }

                    var smt = slot.meshData.submeshes[sm];
                    if (smt == null)
                    {
                        EditorGUILayout.HelpBox("Submesh data is missing.", MessageType.Info);
                    }
                    else
                    {
                        int baseCount = 0;
                        try { baseCount = smt.GetTriangleCount(0) / 3; } catch { }
                        EditorGUILayout.LabelField("LOD0 Triangles", baseCount.ToString());

                        if (smt.lodRanges != null && smt.lodRanges.Count > 0)
                        {
                            EditorGUILayout.Space(5);
                            EditorGUILayout.LabelField("Internal LOD Ranges", EditorStyles.boldLabel);
                            for (int i = 0; i < smt.lodRanges.Count; i++)
                            {
                                var r = smt.lodRanges[i];
                                int triCount = (int)r.count / 3;
                                EditorGUILayout.LabelField(
                                    string.Format("LOD{0}", i),
                                    string.Format("triangles={0}, offset={1}, count={2}", triCount, r.offset, r.count));
                            }

                            EditorGUILayout.Space(5);
                            if (GUILayout.Button("Remove all LOD"))
                            {
                                int[] lod0 = smt.GetBaseTriangles();
                                smt.SetTriangles(lod0);
                                smt.SetLodRanges(null);
                                EditorUtility.SetDirty(slot);
                                AssetDatabase.SaveAssetIfDirty(slot);
                            }
                        }
                        else
                        {
                            EditorGUILayout.HelpBox("No internal LOD ranges found on this slot.", MessageType.Info);
                        }
                    }
                }
                GUIHelper.EndVerticalPadded(10);
            }

            // Smooshing
            EditorGUI.BeginChangeCheck();
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            slot.smooshFoldout = EditorGUILayout.Foldout(slot.smooshFoldout, "Smooshing");
            GUILayout.EndHorizontal();
            if (slot.smooshFoldout)
            {
                GUILayout.Space(10);
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                EditorGUILayout.HelpBox("Smooshing and clipping plane controls for this slot.", MessageType.Info);

                if (isSmooshable != null) EditorGUILayout.PropertyField(isSmooshable, new GUIContent("Is Smooshable"));
                if (isClippingPlane != null) EditorGUILayout.PropertyField(isClippingPlane, new GUIContent("Is Clipping Plane"));
                if (clippingPlaneOffset != null) EditorGUILayout.PropertyField(clippingPlaneOffset, new GUIContent("Clipping Plane Offset"), true);

                EditorGUILayout.Space(5);
                if (smooshOffset != null) EditorGUILayout.PropertyField(smooshOffset);
                if (smooshExpand != null) EditorGUILayout.PropertyField(smooshExpand);

                if (GUILayout.Button("Save and Test Smoosh"))
                {
                    UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                    string path = AssetDatabase.GetAssetPath(target.GetEntityId());
                    AssetDatabase.ImportAsset(path);
                    forceUpdate = true;
                }
                GUIHelper.EndVerticalPadded(10);
            }
            forceUpdate |= EditorGUI.EndChangeCheck();

            // Tags
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            slot.tagsFoldout = EditorGUILayout.Foldout(slot.tagsFoldout, "Tags");
            GUILayout.EndHorizontal();

            if (slot.tagsFoldout)
            {
                GUILayout.Space(10);
                if (slot.tagList != null)
                {
                    slot.tagList.DoLayoutList();
                    if (GUI.changed)
                    {
                        slot.tags = slot.backingTags.ToArray();
                        EditorUtility.SetDirty(slot);
                        forceUpdate = true;
                    }
                }
            }

            // Events
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            (target as SlotDataAsset).eventsFoldout = EditorGUILayout.Foldout((target as SlotDataAsset).eventsFoldout, "Slot Events");
            GUILayout.EndHorizontal();
            if ((target as SlotDataAsset).eventsFoldout)
            {
                if (CharacterBegun != null) EditorGUILayout.PropertyField(CharacterBegun);
                if (!slot.isWildCardSlot)
                {
                    if (SlotAtlassed != null) EditorGUILayout.PropertyField(SlotAtlassed);
                    if (DNAApplied != null) EditorGUILayout.PropertyField(DNAApplied);
                    if (SlotBeginProcessing != null) EditorGUILayout.PropertyField(SlotBeginProcessing);
                    if (SlotProcessed != null) EditorGUILayout.PropertyField(SlotProcessed);
                }
                if (CharacterCompleted != null) EditorGUILayout.PropertyField(CharacterCompleted);
            }

            // Utilities
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            slot.utilitiesFoldout = EditorGUILayout.Foldout(slot.utilitiesFoldout, "Slot Utilities");
            GUILayout.EndHorizontal();

            if (slot.utilitiesFoldout)
            {
                // Fixup UMA 2 slots: upgrade legacy slot data to UMA 3 parity
                if (GUILayout.Button("Fixup UMA 2 -> UMA 3"))
                {
                    FixupUMA2Slots();
                }
                GUILayout.Space(8f);

                #region UV_Utilities
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("UV Utilities", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Copy UV0 to UV Channel", GUILayout.Width(150));
                uvChannel = EditorGUILayout.Popup(uvChannel, new string[] { "2", "3", "4" }, GUILayout.Width(50));
                if (GUILayout.Button("Copy"))
                {
                    var slotDataAsset = target as SlotDataAsset;
                    if (slotDataAsset?.meshData?.uv == null)
                    {
                        EditorUtility.DisplayDialog("Error", "MeshData or UV0 is missing.", "OK");
                    }
                    else
                    {
                        switch (uvChannel)
                        {
                            case 0:
                                slotDataAsset.meshData.uv2 = slotDataAsset.meshData.uv.Clone() as Vector2[];
                                break;
                            case 1:
                                slotDataAsset.meshData.uv3 = slotDataAsset.meshData.uv.Clone() as Vector2[];
                                break;
                            case 2:
                                slotDataAsset.meshData.uv4 = slotDataAsset.meshData.uv.Clone() as Vector2[];
                                break;
                        }
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(target);
                        UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                        EditorUtility.DisplayDialog("Complete", "UV0 copied to UV" + (uvChannel + 2), "OK");
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("Tri-Planar Detail UVs", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Target UV Channel", GUILayout.Width(150));
                triplanarUvChannel = EditorGUILayout.Popup(Mathf.Clamp(triplanarUvChannel, 0, 3), TriplanarUvChannelLabels, GUILayout.Width(90));
                GUILayout.Label("Tile U", GUILayout.Width(45));
                triplanarTileU = EditorGUILayout.FloatField(triplanarTileU, GUILayout.Width(70));
                GUILayout.Label("Tile V", GUILayout.Width(45));
                triplanarTileV = EditorGUILayout.FloatField(triplanarTileV, GUILayout.Width(70));
                if (GUILayout.Button("Generate"))
                {
                    GenerateTriplanarDetailUvsForSelection();
                }
                GUILayout.EndHorizontal();

                if (triplanarUvChannel == 0)
                {
                    EditorGUILayout.HelpBox("Generating into channel 0 overwrites the slot's primary overlay/atlas UVs.", MessageType.Warning);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mirror UV Channel ", GUILayout.Width(150));
                uvChannelToMirror = EditorGUILayout.Popup(uvChannelToMirror, new string[] { "1", "2", "3", "4" }, GUILayout.Width(50));

                if (GUILayout.Button("Mirror U"))
                {
                    var slotDataAsset = target as SlotDataAsset;
                    if (UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset?.meshData))
                    {
                        EditorUtility.DisplayDialog("Error", "MeshData missing.", "OK");
                    }
                    else
                    {
                        switch (uvChannelToMirror)
                        {
                            case 0: slotDataAsset.meshData.MirrorU(0); break;
                            case 1: slotDataAsset.meshData.MirrorU(1); break;
                            case 2: slotDataAsset.meshData.MirrorU(2); break;
                            case 3: slotDataAsset.meshData.MirrorU(3); break;
                        }
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(target);
                        UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                        EditorUtility.DisplayDialog("Complete", "UV U" + (uvChannelToMirror + 1) + " mirrored", "OK");
                    }
                }
                if (GUILayout.Button("Mirror V"))
                {
                    var slotDataAsset = target as SlotDataAsset;
                    if (UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset?.meshData))
                    {
                        EditorUtility.DisplayDialog("Error", "MeshData missing.", "OK");
                    }
                    else
                    {
                        switch (uvChannelToMirror)
                        {
                            case 0: slotDataAsset.meshData.MirrorV(0); break;
                            case 1: slotDataAsset.meshData.MirrorV(1); break;
                            case 2: slotDataAsset.meshData.MirrorV(2); break;
                            case 3: slotDataAsset.meshData.MirrorV(3); break;
                        }
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssetIfDirty(target);
                        UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
                        EditorUtility.DisplayDialog("Complete", "UV V" + (uvChannelToMirror + 1) + " mirrored", "OK");
                    }
                }
                GUILayout.EndHorizontal();
                GUIHelper.EndVerticalPadded(10);
                #endregion

                #region Recalculate Normals
                GUIHelper.BeginVerticalPadded(10, new Color(0.90f, 0.92f, 1f));
                GUILayout.Label("Recalculate Normals", EditorStyles.boldLabel);
                weightRecalculatedNormalsByTriangleSize = EditorGUILayout.ToggleLeft("Weight normals by triangle size", weightRecalculatedNormalsByTriangleSize);
                using (new EditorGUI.DisabledScope(UMAMeshData.IsNullOrEmptyMeshData(slot?.meshData)))
                {
                    if (GUILayout.Button("Recalculate Normals and Tangents"))
                    {
                        SlotDataAsset slotDataAsset = target as SlotDataAsset;
                        if (slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData))
                        {
                            recalculateNormalsInfo = "MeshData missing.";
                        }
                        else
                        {
                            Undo.RecordObject(slotDataAsset, "Recalculate Slot Normals");
                            recalculateNormalsInfo = RecalculateSlotNormalsAndTangents(slotDataAsset, weightRecalculatedNormalsByTriangleSize);
                            slotDataAsset.ValidateMeshData();
                            EditorUtility.SetDirty(slotDataAsset);
                            AssetDatabase.SaveAssetIfDirty(slotDataAsset);
                            string path = AssetDatabase.GetAssetPath(slotDataAsset.GetEntityId());
                            if (!string.IsNullOrEmpty(path))
                            {
                                AssetDatabase.ImportAsset(path);
                            }
                            UMAUpdateProcessor.UpdateSlot(slotDataAsset, false);
                            RebuildPreviewMesh(slotDataAsset);
                            Repaint();
                        }
                    }
                }
                if (!string.IsNullOrEmpty(recalculateNormalsInfo))
                {
                    EditorGUILayout.HelpBox(recalculateNormalsInfo, MessageType.Info);
                }
                GUIHelper.EndVerticalPadded(10);
                #endregion

                #region Bindpose Conform
                GUIHelper.BeginVerticalPadded(10, new Color(0.80f, 0.95f, 0.80f));
                GUILayout.Label("Bindpose Conform", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Conform this slot's bindposes and vertex positions to those in the source slot. Vertices are adjusted using the dominant bone so skin output stays consistent. Bones not present in the source keep their original bindpose.", MessageType.Info);
                bindposeSourceSlot = EditorGUILayout.ObjectField("Source Slot", bindposeSourceSlot, typeof(SlotDataAsset), false) as SlotDataAsset;

                bool canConform = bindposeSourceSlot != null && !UMAMeshData.IsNullOrEmptyMeshData(bindposeSourceSlot.meshData) && !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData);
                EditorGUI.BeginDisabledGroup(!canConform);
                if (GUILayout.Button("Conform Bindposes && Vertices"))
                {
                    lastBindposeInfo = ConformBindposesAndVertices(slot, bindposeSourceSlot);
                    EditorUtility.SetDirty(slot);
                    AssetDatabase.SaveAssetIfDirty(slot);
                    UMAUpdateProcessor.UpdateSlot(slot, false);
                }
                EditorGUI.EndDisabledGroup();

                if (!string.IsNullOrEmpty(lastBindposeInfo))
                {
                    EditorGUILayout.HelpBox(lastBindposeInfo, MessageType.None);
                }
                GUIHelper.EndVerticalPadded(10);
                #endregion

                #region WELDS
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                selectedRaceIndex = EditorGUILayout.Popup("Select Base Slot by Race", selectedRaceIndex, foundRaceNames.ToArray());
                if (selectedRaceIndex <= 0)
                {
                    EditorGUILayout.HelpBox("Select a slot by race quickly, or use manual selection below", MessageType.Info);
                }
                else
                {
                    try
                    {
                        UMAData.UMARecipe baseRecipe = new UMAData.UMARecipe();
                        foundRaces[selectedRaceIndex].baseRaceRecipe.Load(baseRecipe);

                        foreach (SlotData sd in baseRecipe.slotDataList)
                        {
                            if (sd != null && sd.asset != null)
                            {
                                if (GUILayout.Button(string.Format("{0} ({1})", sd.asset.name, sd.slotName)))
                                {
                                    WeldToSlot = sd.asset;
                                }
                            }
                        }
                    }
                    catch
                    {
                        EditorGUILayout.HelpBox("Race data unavailable (reloading).", MessageType.Info);
                    }
                }

                GUILayout.Space(12);

                WeldToSlot = EditorGUILayout.ObjectField("Source Slot", WeldToSlot, typeof(SlotDataAsset), false) as SlotDataAsset;

                weldDistance = EditorGUILayout.FloatField("Max Vertex Distance", weldDistance);

                bool haveWeldSource = WeldToSlot != null && !UMAMeshData.IsNullOrEmptyMeshData(WeldToSlot.meshData) && !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData);

                if (!haveWeldSource) { EditorGUI.BeginDisabledGroup(true); }
                GUILayout.Box("Warning! averaging normals will update both slots!", GUILayout.ExpandWidth(true));

                if (GUILayout.Button($"Copy boneweights"))
                {
                    lastInfo = slot.CopyBoneweightsFrom(WeldToSlot);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Normal Copy Mode", GUILayout.Width(150));

                normalCopyMode = (UMA.SlotDataAsset.NormalCopyMode)EditorGUILayout.EnumPopup(normalCopyMode, GUILayout.Width(130));
                if (GUILayout.Button($"Copy Normals"))
                {
                    lastInfo = slot.CopyNormalsFrom(WeldToSlot, weldDistance, normalCopyMode);
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Blendshape Copy Mode", GUILayout.Width(150));
                blendshapeCopyMode = (UMA.SlotDataAsset.BlendshapeCopyMode)EditorGUILayout.EnumPopup(blendshapeCopyMode, GUILayout.Width(130));

                if (GUILayout.Button($"Copy Blendshapes"))
                {
                    lastInfo = slot.CopyBlendshapesFrom(WeldToSlot, blendshapeCopyMode);
                }
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(lastInfo))
                {
                    EditorGUILayout.HelpBox(lastInfo, MessageType.Info);
                }

                if (!haveWeldSource) { EditorGUI.EndDisabledGroup(); }
                GUIHelper.EndVerticalPadded(10);
                #endregion 

                #region Blendshape_To_MeshModifier
                GUIHelper.BeginVerticalPadded(10, new Color(0.92f, 0.86f, 0.98f));
                GUILayout.Label("Blendshape To MeshModifier", EditorStyles.boldLabel);

                string[] slotBlendshapeNames = GetSlotBlendshapeNames(slot);
                if (slotBlendshapeNames.Length == 0)
                {
                    EditorGUILayout.HelpBox("No blendshapes found on this slot.", MessageType.Info);
                }
                else
                {
                    extractBlendshapeIndex = Mathf.Clamp(extractBlendshapeIndex, 0, slotBlendshapeNames.Length - 1);
                    extractBlendshapeIndex = EditorGUILayout.Popup("Blendshape", extractBlendshapeIndex, slotBlendshapeNames);

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Extract to MeshModifier"))
                    {
                        ExtractSingleBlendshapeMeshModifier(slot, slotBlendshapeNames[extractBlendshapeIndex]);
                    }
                    if (GUILayout.Button("Extract all"))
                    {
                        ExtractAllBlendshapesToMeshModifiers(slot, slotBlendshapeNames);
                    }
                    GUILayout.EndHorizontal();
                }
                GUIHelper.EndVerticalPadded(10);
                #endregion

                #region info
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("This mesh");

                if (!UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  Vertices: ", GUILayout.Width(160));
                    GUILayout.Label($"{slot.meshData.vertices?.Length ?? 0}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  BoneWeights: ", GUILayout.Width(160));
                    GUILayout.Label($"{slot.meshData.ManagedBoneWeights?.Length ?? 0}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("MeshData is missing.", MessageType.Info);
                }

                if (WeldToSlot != null && !UMAMeshData.IsNullOrEmptyMeshData(WeldToSlot.meshData))
                {
                    GUILayout.Space(10);
                    GUILayout.Label("Source Mesh");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  Vertices: ", GUILayout.Width(160));
                    GUILayout.Label($"{WeldToSlot.meshData.vertices?.Length ?? 0}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("  BoneWeights: ", GUILayout.Width(160));
                    GUILayout.Label($"{WeldToSlot.meshData.ManagedBoneWeights?.Length ?? 0}", GUILayout.Width(160));
                    GUILayout.Label("", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                }

                GUIHelper.EndVerticalPadded(10);
                #endregion

                #region Preview

                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                SlotPreviewMode newPreviewMode = (SlotPreviewMode)EditorGUILayout.EnumPopup("Preview Mode", previewMode);
                if (newPreviewMode != previewMode)
                {
                    reConfigurePreview = true;
                    previewMode = newPreviewMode;
                    ClampPreviewLodLevel(target as SlotDataAsset);
                }

                DrawPreviewLodSelector(target as SlotDataAsset, false);

                if (meshToPreview != null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Preview Vert", GUILayout.Width(100));
                    int newpreviewVertex = EditorGUILayout.IntSlider(previewVertex, -1, meshToPreview.vertexCount - 1);
                    if (newpreviewVertex != previewVertex)
                    {
                        previewVertex = newpreviewVertex;
                        reConfigurePreview = true;
                    }
                    if (GUILayout.Button("Dump Vert", GUILayout.Width(80)))
                    {
                        ShowDebugVertInfo(target as SlotDataAsset, previewVertex);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                Vector3 savedPreviewRotation = previewRotation;
                previewRotation = EditorGUILayout.Vector3Field("Preview Rotation", previewRotation);
                if (savedPreviewRotation != previewRotation)
                {
                    reConfigurePreview = true;
                }
                if (reConfigurePreview)
                {
                    RebuildPreviewMesh(target as SlotDataAsset);
                }
                GUIHelper.EndVerticalPadded(10);
                #endregion
            }

            // Drag-n-drop seam update (only for non-wildcards)
            if (!slot.isWildCardSlot)
            {
                GUILayout.Space(20);
                Rect updateDropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
                GUI.Box(updateDropArea, "Drag SkinnedMeshRenderers here to update the slot meshData.");
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                clearNormals = EditorGUILayout.ToggleLeft("Force Clear Normals", clearNormals);
                clearTangents = EditorGUILayout.ToggleLeft("Force Clear Tangents", clearTangents);
                GUILayout.EndHorizontal();
                UpdateSlotDropAreaGUI(updateDropArea);

                GUILayout.Space(10);
            }

            // Display information on rotations here. 
            // commented out for now, but can be useful for debugging.
            // DrawTransformDebugInfo();



            // Bottom quick-rotate controls for the preview
            GUILayout.Space(12);
            DrawUdimInfo(slot);

            GUIHelper.BeginVerticalPadded(8, new Color(0.90f, 0.95f, 1f));
            GUILayout.Label("Quick Rotate (90�)", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("X +90")) { previewRotation.x = Wrap360(previewRotation.x + 90f); reConfigurePreview = true; Repaint(); }
            if (GUILayout.Button("X -90")) { previewRotation.x = Wrap360(previewRotation.x - 90f); reConfigurePreview = true; Repaint(); }
            if (GUILayout.Button("Y +90")) { previewRotation.y = Wrap360(previewRotation.y + 90f); reConfigurePreview = true; Repaint(); }
            if (GUILayout.Button("Y -90")) { previewRotation.y = Wrap360(previewRotation.y - 90f); reConfigurePreview = true; Repaint(); }
            if (GUILayout.Button("Z +90")) { previewRotation.z = Wrap360(previewRotation.z + 90f); reConfigurePreview = true; Repaint(); }
            if (GUILayout.Button("Z -90")) { previewRotation.z = Wrap360(previewRotation.z - 90f); reConfigurePreview = true; Repaint(); }
            GUILayout.EndHorizontal();
            GUIHelper.EndVerticalPadded(8);

            serializedObject.ApplyModifiedProperties();

            PersistSectionStateIfNeeded(false);

            if (EditorGUI.EndChangeCheck() || forceUpdate)
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                string path = AssetDatabase.GetAssetPath(target.GetEntityId());
                AssetDatabase.ImportAsset(path);
                UMAUpdateProcessor.UpdateSlot(target as SlotDataAsset, false);
            }
        }

        private void DrawUdimInfo(SlotDataAsset slotDataAsset)
        {
            GUIHelper.BeginVerticalPadded(8, new Color(0.82f, 0.9f, 0.96f));
            udimInfoFoldout = EditorGUILayout.Foldout(udimInfoFoldout, "UDIM info", true);
            if (!udimInfoFoldout || slotDataAsset == null)
            {
                GUIHelper.EndVerticalPadded(8);
                return;
            }

            EditorGUILayout.LabelField("UDIM Member", slotDataAsset.IsUdimMember ? "Yes" : "No");
            EditorGUILayout.LabelField("Group Name", string.IsNullOrEmpty(slotDataAsset.udimGroupName) ? "-" : slotDataAsset.udimGroupName);
            EditorGUILayout.LabelField("Group ID", string.IsNullOrEmpty(slotDataAsset.udimGroupId) ? "-" : slotDataAsset.udimGroupId);
            EditorGUILayout.LabelField("Tile Number", slotDataAsset.udimTileNumber > 0 ? slotDataAsset.udimTileNumber.ToString() : "-");
            EditorGUILayout.LabelField("Source Submesh", slotDataAsset.udimSourceSubmeshIndex >= 0 ? slotDataAsset.udimSourceSubmeshIndex.ToString() : "-");

            if (!slotDataAsset.IsUdimMember)
            {
                EditorGUILayout.HelpBox("This slot has no complete UDIM membership metadata.", MessageType.Info);
            }

            DrawUdimSeamMap(slotDataAsset.UdimSharedVertexMap);
            DrawUdimWelds(slotDataAsset.Welds);
            GUIHelper.EndVerticalPadded(8);
        }

        private void DrawUdimSeamMap(SlotDataAsset.UdimSeamMap seamMap)
        {
            int originalCount = seamMap != null && seamMap.originalIndices != null ? seamMap.originalIndices.Length : 0;
            int localCount = seamMap != null && seamMap.localIndices != null ? seamMap.localIndices.Length : 0;
            int pairCount = Mathf.Min(originalCount, localCount);

            udimSeamMapFoldout = EditorGUILayout.Foldout(udimSeamMapFoldout, "Shared Seam Vertices (" + pairCount + ")", true);
            if (!udimSeamMapFoldout)
            {
                return;
            }

            EditorGUILayout.LabelField("Seam Key Count", originalCount.ToString());
            EditorGUILayout.LabelField("Local Index Count", localCount.ToString());
            if (originalCount != localCount)
            {
                EditorGUILayout.HelpBox("The seam map has unmatched original and local index counts.", MessageType.Warning);
            }
            if (pairCount == 0)
            {
                EditorGUILayout.HelpBox("No shared seam vertices are recorded.", MessageType.Info);
                return;
            }

            udimSeamMapScrollPosition = EditorGUILayout.BeginScrollView(udimSeamMapScrollPosition, GUILayout.Height(180f));
            for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                EditorGUILayout.LabelField("Pair " + pairIndex, "Seam Key " + seamMap.originalIndices[pairIndex] + " -> Local " + seamMap.localIndices[pairIndex]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawUdimWelds(List<SlotDataAsset.Welding> welds)
        {
            int weldCount = welds != null ? welds.Count : 0;
            EditorGUILayout.LabelField("Manual Weld Records", weldCount.ToString());
            if (weldCount == 0)
            {
                return;
            }

            for (int weldIndex = 0; weldIndex < weldCount; weldIndex++)
            {
                SlotDataAsset.Welding weld = welds[weldIndex];
                if (weld == null)
                {
                    EditorGUILayout.LabelField("Weld " + weldIndex, "Missing record");
                    continue;
                }

                int pointCount = weld.WeldPoints != null ? weld.WeldPoints.Count : 0;
                string targetName = string.IsNullOrEmpty(weld.WeldedToSlot) ? "Unnamed Slot" : weld.WeldedToSlot;
                bool expanded = expandedUdimWeldPointLists.Contains(weldIndex);
                bool newExpanded = EditorGUILayout.Foldout(
                    expanded,
                    targetName + " (" + pointCount + " points, " + weld.MisMatchCount + " mismatches)",
                    true);
                if (newExpanded)
                {
                    expandedUdimWeldPointLists.Add(weldIndex);
                }
                else
                {
                    expandedUdimWeldPointLists.Remove(weldIndex);
                }

                if (!newExpanded)
                {
                    continue;
                }

                if (pointCount == 0)
                {
                    EditorGUILayout.HelpBox("No weld points are recorded.", MessageType.Info);
                    continue;
                }

                if (!udimWeldPointScrollPositions.TryGetValue(weldIndex, out Vector2 scrollPosition))
                {
                    scrollPosition = Vector2.zero;
                }

                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(160f));
                for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                {
                    SlotDataAsset.WeldPoint point = weld.WeldPoints[pointIndex];
                    if (point == null)
                    {
                        EditorGUILayout.LabelField("Point " + pointIndex, "Missing record");
                        continue;
                    }

                    string pointInfo = "Vertex " + point.ourVertex + " -> " + point.theirVertex +
                        ", Normal " + point.newNormal + ", Mismatch " + point.misMatch;
                    EditorGUILayout.LabelField("Point " + pointIndex, pointInfo);
                }
                EditorGUILayout.EndScrollView();
                udimWeldPointScrollPositions[weldIndex] = scrollPosition;
            }
        }

        private static float Wrap360(float angle)
        {
            angle %= 360f;
            if (angle < 0) angle += 360f;
            return angle;
        }

        private static string[] GetSlotBlendshapeNames(SlotDataAsset slotDataAsset)
        {
            if (slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData) || slotDataAsset.meshData.blendShapes == null)
            {
                return Array.Empty<string>();
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var blendshape in slotDataAsset.meshData.blendShapes)
            {
                if (blendshape == null || string.IsNullOrEmpty(blendshape.shapeName))
                {
                    continue;
                }

                names.Add(blendshape.shapeName);
            }

            List<string> sortedNames = new List<string>(names);
            sortedNames.Sort(StringComparer.Ordinal);
            return sortedNames.ToArray();
        }

        private static string GetMeshModifierSlotKey(SlotDataAsset slotDataAsset)
        {
            if (slotDataAsset == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(slotDataAsset.sourceSlot))
            {
                return slotDataAsset.sourceSlot;
            }

            if (!string.IsNullOrEmpty(slotDataAsset.slotName))
            {
                return slotDataAsset.slotName;
            }

            return slotDataAsset.name;
        }

        private static UMA.MeshModifier CreateBlendshapeMeshModifier(SlotDataAsset slotDataAsset, string blendshapeName)
        {
            if (slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData) || slotDataAsset.meshData.blendShapes == null)
            {
                return null;
            }

            UMABlendShape foundShape = null;
            foreach (var shape in slotDataAsset.meshData.blendShapes)
            {
                if (shape != null && string.Equals(shape.shapeName, blendshapeName, StringComparison.Ordinal))
                {
                    foundShape = shape;
                    break;
                }
            }

            if (foundShape == null || foundShape.frames == null || foundShape.frames.Length == 0)
            {
                return null;
            }

            UMABlendFrame frame = foundShape.frames[foundShape.frames.Length - 1];
            if (frame == null || frame.deltaVertices == null || frame.deltaVertices.Length == 0)
            {
                return null;
            }

            var meshModifier = ScriptableObject.CreateInstance<UMA.MeshModifier>();
            meshModifier.EditorModifiers = new List<UMA.MeshModifier.Modifier>();

            string slotKey = GetMeshModifierSlotKey(slotDataAsset);
            var newMod = new UMA.MeshModifier.Modifier
            {
                ModifierName = blendshapeName,
                DNAName = string.Empty,
                Scale = 1.0f,
                SlotName = slotKey,
                keepAsIs = true,
                adjustments = new VertexBlendshapeAdjustmentCollection(),
                TemplateAdjustment = new VertexBlendshapeAdjustment()
            };

            for (int i = 0; i < frame.deltaVertices.Length; i++)
            {
                if (frame.deltaVertices[i] == Vector3.zero)
                {
                    continue;
                }

                var vba = new VertexBlendshapeAdjustment
                {
                    vertexIndex = i,
                    slotName = slotKey,
                    delta = frame.deltaVertices[i],
                    tangent = Vector3.zero,
                    normal = Vector3.zero
                };

                if (frame.HasTangents() && frame.deltaTangents != null && i < frame.deltaTangents.Length)
                {
                    vba.tangent = frame.deltaTangents[i];
                }

                if (frame.HasNormals() && frame.deltaNormals != null && i < frame.deltaNormals.Length)
                {
                    vba.normal = frame.deltaNormals[i];
                }

                newMod.adjustments.Add(vba);
            }

            if (newMod.adjustments.Count() == 0)
            {
                DestroyImmediate(meshModifier);
                return null;
            }

            meshModifier.EditorModifiers.Add(newMod);
            meshModifier.SyncRuntimeModifiersFromEditorModifiers();
            return meshModifier;
        }

        private static void ExtractSingleBlendshapeMeshModifier(SlotDataAsset slotDataAsset, string blendshapeName)
        {
            var meshModifier = CreateBlendshapeMeshModifier(slotDataAsset, blendshapeName);
            if (meshModifier == null)
            {
                EditorUtility.DisplayDialog("Extract Blendshape", "Could not extract this blendshape.", "OK");
                return;
            }

            string slotBaseName = !string.IsNullOrEmpty(slotDataAsset.slotName) ? slotDataAsset.slotName : slotDataAsset.name;
            string defaultAssetName = (slotBaseName + "_" + blendshapeName).Replace(' ', '_') + ".asset";
            string path = EditorUtility.SaveFilePanelInProject("Save MeshModifier", defaultAssetName, "asset", "Save MeshModifier asset");
            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(meshModifier);
                return;
            }

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            AssetDatabase.CreateAsset(meshModifier, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = meshModifier;
            EditorUtility.FocusProjectWindow();
        }

        private static void ExtractAllBlendshapesToMeshModifiers(SlotDataAsset slotDataAsset, string[] blendshapeNames)
        {
            if (slotDataAsset == null || blendshapeNames == null || blendshapeNames.Length == 0)
            {
                EditorUtility.DisplayDialog("Extract Blendshapes", "No blendshapes available to extract.", "OK");
                return;
            }

            string slotAssetPath = AssetDatabase.GetAssetPath(slotDataAsset);
            string startFolder = Application.dataPath;
            if (!string.IsNullOrEmpty(slotAssetPath))
            {
                string slotFolder = Path.GetDirectoryName(slotAssetPath);
                if (!string.IsNullOrEmpty(slotFolder))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                    startFolder = Path.GetFullPath(Path.Combine(projectRoot, slotFolder));
                }
            }

            string selectedFolder = EditorUtility.SaveFolderPanel("Select folder for extracted MeshModifiers", startFolder, "");
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            string relativeFolder = FileUtil.GetProjectRelativePath(selectedFolder).Replace("\\", "/");
            if (string.IsNullOrEmpty(relativeFolder) || !relativeFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside this Unity project.", "OK");
                return;
            }

            string slotBaseName = !string.IsNullOrEmpty(slotDataAsset.slotName) ? slotDataAsset.slotName : slotDataAsset.name;
            int createdCount = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string blendshapeName in blendshapeNames)
                {
                    var meshModifier = CreateBlendshapeMeshModifier(slotDataAsset, blendshapeName);
                    if (meshModifier == null)
                    {
                        continue;
                    }

                    string fileName = (slotBaseName + "_" + blendshapeName).Replace(' ', '_') + ".asset";
                    string fullAssetPath = (relativeFolder + "/" + fileName).Replace("\\", "/");
                    string uniquePath = AssetDatabase.GenerateUniqueAssetPath(fullAssetPath);
                    AssetDatabase.CreateAsset(meshModifier, uniquePath);
                    createdCount++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            EditorUtility.DisplayDialog("Extract Blendshapes", $"Created {createdCount} MeshModifier assets.", "OK");
        }

        private void RestorePersistedSectionState()
        {
            if (string.IsNullOrEmpty(persistedSectionStateKey) || slot == null)
            {
                return;
            }

            if (!EditorPrefs.HasKey(persistedSectionStateKey))
            {
                persistedSectionStateCache = JsonUtility.ToJson(BuildPersistedSectionState());
                return;
            }

            string json = EditorPrefs.GetString(persistedSectionStateKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                persistedSectionStateCache = JsonUtility.ToJson(BuildPersistedSectionState());
                return;
            }

            PersistedSectionState state = JsonUtility.FromJson<PersistedSectionState>(json);
            if (state == null)
            {
                persistedSectionStateCache = JsonUtility.ToJson(BuildPersistedSectionState());
                return;
            }

            slot.smooshFoldout = state.smooshFoldout;
            slot.utilitiesFoldout = state.utilitiesFoldout;
            WeldToSlot = LoadAssetFromGuid<SlotDataAsset>(state.weldToSlotGuid);
            bindposeSourceSlot = LoadAssetFromGuid<SlotDataAsset>(state.bindposeSourceSlotGuid);
            uma3DonorSlot = LoadAssetFromGuid<SlotDataAsset>(state.uma3DonorSlotGuid);
            overrideUma3AxisConversion = state.overrideUma3AxisConversion;
            uma3AxisConversion = state.uma3AxisConversion;
            weldDistance = state.weldDistance > 0f ? state.weldDistance : 0.0001f;
            selectedRaceIndex = FindRaceIndex(state.selectedRaceName);
            uvChannel = Mathf.Clamp(state.uvChannel, 0, 2);
            uvChannelToMirror = Mathf.Clamp(state.uvChannelToMirror, 0, 3);
            bool hasTriplanarUvState = json.Contains("\"triplanarUvChannel\"");
            triplanarUvChannel = hasTriplanarUvState ? Mathf.Clamp(state.triplanarUvChannel, 0, 3) : 1;
            triplanarTileU = hasTriplanarUvState ? state.triplanarTileU : 1f;
            triplanarTileV = hasTriplanarUvState ? state.triplanarTileV : 1f;
            weightRecalculatedNormalsByTriangleSize = json.Contains("\"weightRecalculatedNormalsByTriangleSize\"") && state.weightRecalculatedNormalsByTriangleSize;
            normalCopyMode = Enum.IsDefined(typeof(UMA.SlotDataAsset.NormalCopyMode), state.normalCopyMode)
                ? (UMA.SlotDataAsset.NormalCopyMode)state.normalCopyMode
                : default;
            blendshapeCopyMode = Enum.IsDefined(typeof(UMA.SlotDataAsset.BlendshapeCopyMode), state.blendshapeCopyMode)
                ? (UMA.SlotDataAsset.BlendshapeCopyMode)state.blendshapeCopyMode
                : default;
            previewLodLevel = Mathf.Max(0, state.previewLodLevel);

            persistedSectionStateCache = json;
        }

        private void PersistSectionStateIfNeeded(bool force)
        {
            if (string.IsNullOrEmpty(persistedSectionStateKey) || slot == null)
            {
                return;
            }

            string json = JsonUtility.ToJson(BuildPersistedSectionState());
            if (!force && string.Equals(json, persistedSectionStateCache, StringComparison.Ordinal))
            {
                return;
            }

            EditorPrefs.SetString(persistedSectionStateKey, json);
            persistedSectionStateCache = json;
        }

        private PersistedSectionState BuildPersistedSectionState()
        {
            return new PersistedSectionState
            {
                smooshFoldout = slot != null && slot.smooshFoldout,
                utilitiesFoldout = slot != null && slot.utilitiesFoldout,
                weldToSlotGuid = GetAssetGuid(WeldToSlot),
                bindposeSourceSlotGuid = GetAssetGuid(bindposeSourceSlot),
                uma3DonorSlotGuid = GetAssetGuid(uma3DonorSlot),
                overrideUma3AxisConversion = overrideUma3AxisConversion,
                uma3AxisConversion = uma3AxisConversion,
                weldDistance = weldDistance,
                selectedRaceName = GetSelectedRaceName(),
                uvChannel = uvChannel,
                uvChannelToMirror = uvChannelToMirror,
                triplanarUvChannel = triplanarUvChannel,
                triplanarTileU = triplanarTileU,
                triplanarTileV = triplanarTileV,
                weightRecalculatedNormalsByTriangleSize = weightRecalculatedNormalsByTriangleSize,
                normalCopyMode = (int)normalCopyMode,
                blendshapeCopyMode = (int)blendshapeCopyMode,
                previewLodLevel = previewLodLevel
            };
        }

        private string GetSelectedRaceName()
        {
            if (selectedRaceIndex > 0 && selectedRaceIndex < foundRaceNames.Count)
            {
                return foundRaceNames[selectedRaceIndex];
            }

            return string.Empty;
        }

        private int FindRaceIndex(string raceName)
        {
            if (string.IsNullOrEmpty(raceName))
            {
                return -1;
            }

            for (int i = 0; i < foundRaceNames.Count; i++)
            {
                if (string.Equals(foundRaceNames[i], raceName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetPersistedSectionStateKey(SlotDataAsset targetSlot)
        {
            if (targetSlot == null)
            {
                return null;
            }

            string assetPath = AssetDatabase.GetAssetPath(targetSlot);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? null : $"UMA.SlotDataAssetInspector.SectionState.{guid}";
        }

        private static string GetAssetGuid(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrEmpty(assetPath) ? string.Empty : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static T LoadAssetFromGuid<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private void GenerateTriplanarDetailUvsForSelection()
        {
            int channel = Mathf.Clamp(triplanarUvChannel, 0, 3);
            if (channel == 0 && !EditorUtility.DisplayDialog(
                "Overwrite UV Channel 0?",
                "This will replace the slot's primary UV channel. Channel 0 is usually used for overlay and atlas mapping.",
                "Overwrite UV0",
                "Cancel"))
            {
                return;
            }

            int updatedCount = 0;
            StringBuilder skipped = new StringBuilder();
            foreach (UnityEngine.Object selectedTarget in targets)
            {
                SlotDataAsset slotDataAsset = selectedTarget as SlotDataAsset;
                if (slotDataAsset == null)
                {
                    continue;
                }

                if (UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData) || slotDataAsset.meshData.vertices == null || slotDataAsset.meshData.vertices.Length == 0)
                {
                    skipped.AppendLine(slotDataAsset.name + ": meshData or vertices missing.");
                    continue;
                }

                Undo.RecordObject(slotDataAsset, "Generate Tri-Planar Detail UVs");
                Vector2[] generatedUvs = GenerateTriplanarDetailUvs(slotDataAsset.meshData, triplanarTileU, triplanarTileV);
                SetMeshDataUvChannel(slotDataAsset.meshData, channel, generatedUvs);
                slotDataAsset.ValidateMeshData();
                EditorUtility.SetDirty(slotDataAsset);
                AssetDatabase.SaveAssetIfDirty(slotDataAsset);
                UMAUpdateProcessor.UpdateSlot(slotDataAsset, false);
                updatedCount++;
            }

            if (updatedCount > 0)
            {
                GUI.changed = true;
                Repaint();
            }

            if (skipped.Length > 0)
            {
                EditorUtility.DisplayDialog(
                    "Tri-Planar Detail UVs",
                    "Generated UVs for " + updatedCount + " slot(s).\n\nSkipped:\n" + skipped,
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Tri-Planar Detail UVs",
                    "Generated UVs for " + updatedCount + " slot(s) into channel " + channel + ".",
                    "OK");
            }
        }

        private static Vector2[] GenerateTriplanarDetailUvs(UMAMeshData meshData, float tileU, float tileV)
        {
            Vector3[] vertices = meshData.vertices;
            Vector3[] projectionNormals = GetTriplanarProjectionNormals(meshData);
            Vector2[] generatedUvs = new Vector2[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 normal = i < projectionNormals.Length ? projectionNormals[i] : Vector3.up;
                generatedUvs[i] = ProjectTriplanarUv(vertices[i], normal, tileU, tileV);
            }

            return generatedUvs;
        }

        private static Vector3[] GetTriplanarProjectionNormals(UMAMeshData meshData)
        {
            int vertexCount = meshData.vertices != null ? meshData.vertices.Length : 0;
            Vector3[] projectionNormals = new Vector3[vertexCount];
            bool haveNormals = meshData.normals != null && meshData.normals.Length == vertexCount;

            if (haveNormals)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    projectionNormals[i] = SafeProjectionNormal(meshData.normals[i], meshData.vertices[i]);
                }
                return projectionNormals;
            }

            AccumulateTriangleNormals(meshData, projectionNormals);
            for (int i = 0; i < vertexCount; i++)
            {
                projectionNormals[i] = SafeProjectionNormal(projectionNormals[i], meshData.vertices[i]);
            }

            return projectionNormals;
        }

        private static void AccumulateTriangleNormals(UMAMeshData meshData, Vector3[] projectionNormals)
        {
            if (meshData.submeshes == null || projectionNormals == null || projectionNormals.Length == 0)
            {
                return;
            }

            for (int submeshIndex = 0; submeshIndex < meshData.submeshes.Length; submeshIndex++)
            {
                SubMeshTriangles submesh = meshData.submeshes[submeshIndex];
                if (submesh == null)
                {
                    continue;
                }

                int[] triangles = submesh.GetBaseTriangles();
                if (triangles == null || triangles.Length < 3)
                {
                    triangles = submesh.getManagedTriangles(0);
                }

                if (triangles == null)
                {
                    continue;
                }

                for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                {
                    int a = triangles[triangleIndex];
                    int b = triangles[triangleIndex + 1];
                    int c = triangles[triangleIndex + 2];
                    if (!IsValidVertexIndex(a, projectionNormals.Length) || !IsValidVertexIndex(b, projectionNormals.Length) || !IsValidVertexIndex(c, projectionNormals.Length))
                    {
                        continue;
                    }

                    Vector3 normal = Vector3.Cross(meshData.vertices[b] - meshData.vertices[a], meshData.vertices[c] - meshData.vertices[a]);
                    if (normal.sqrMagnitude <= 0.0000001f)
                    {
                        continue;
                    }

                    projectionNormals[a] += normal;
                    projectionNormals[b] += normal;
                    projectionNormals[c] += normal;
                }
            }
        }

        private static bool IsValidVertexIndex(int index, int vertexCount)
        {
            return index >= 0 && index < vertexCount;
        }

        private static Vector3 SafeProjectionNormal(Vector3 normal, Vector3 vertex)
        {
            if (normal.sqrMagnitude > 0.0000001f)
            {
                return normal.normalized;
            }

            if (vertex.sqrMagnitude > 0.0000001f)
            {
                return vertex.normalized;
            }

            return Vector3.up;
        }

        private static Vector2 ProjectTriplanarUv(Vector3 vertex, Vector3 normal, float tileU, float tileV)
        {
            Vector3 axisWeights = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
            if (axisWeights.x >= axisWeights.y && axisWeights.x >= axisWeights.z)
            {
                return new Vector2(vertex.z * tileU, vertex.y * tileV);
            }

            if (axisWeights.y >= axisWeights.z)
            {
                return new Vector2(vertex.x * tileU, vertex.z * tileV);
            }

            return new Vector2(vertex.x * tileU, vertex.y * tileV);
        }

        private static void SetMeshDataUvChannel(UMAMeshData meshData, int channel, Vector2[] uvs)
        {
            switch (channel)
            {
                case 0:
                    meshData.uv = uvs;
                    meshData.uvModified = true;
                    break;
                case 1:
                    meshData.uv2 = uvs;
                    meshData.uv2Modified = true;
                    break;
                case 2:
                    meshData.uv3 = uvs;
                    meshData.uv3Modified = true;
                    break;
                case 3:
                    meshData.uv4 = uvs;
                    meshData.uv4Modified = true;
                    break;
            }
        }

        private static string RecalculateSlotNormalsAndTangents(SlotDataAsset slotDataAsset, bool weightNormalsByTriangleSize)
        {
            if (slotDataAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotDataAsset.meshData))
            {
                return "MeshData missing.";
            }

            UMAMeshData meshData = slotDataAsset.meshData;
            Vector3[] vertices = meshData.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return "MeshData has no vertices.";
            }

            int vertexCount = vertices.Length;
            Vector3[] normalSums = new Vector3[vertexCount];
            int processedTriangleCount = AccumulateSlotTriangleNormals(meshData, normalSums, weightNormalsByTriangleSize);
            if (processedTriangleCount == 0)
            {
                return "No valid triangles found.";
            }

            Vector3[] previousNormals = meshData.normals;
            Vector3[] recalculatedNormals = new Vector3[vertexCount];
            int fallbackNormalCount = 0;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                Vector3 normal = normalSums[vertexIndex];
                if (normal.sqrMagnitude > 0.0000001f)
                {
                    recalculatedNormals[vertexIndex] = normal.normalized;
                    continue;
                }

                fallbackNormalCount++;
                if (previousNormals != null && vertexIndex < previousNormals.Length && previousNormals[vertexIndex].sqrMagnitude > 0.0000001f)
                {
                    recalculatedNormals[vertexIndex] = previousNormals[vertexIndex].normalized;
                }
                else
                {
                    recalculatedNormals[vertexIndex] = Vector3.up;
                }
            }

            meshData.normals = recalculatedNormals;
            meshData.tangents = RecalculateSlotTangents(meshData, recalculatedNormals);
            meshData.normalsModified = true;
            meshData.tangentsModified = true;

            return "Recalculated normals and tangents from " + processedTriangleCount + " triangle(s)." +
                (fallbackNormalCount > 0 ? " " + fallbackNormalCount + " vertex normal(s) had no triangle contribution and used fallback normals." : string.Empty);
        }

        private static int AccumulateSlotTriangleNormals(UMAMeshData meshData, Vector3[] normalSums, bool weightNormalsByTriangleSize)
        {
            if (meshData == null || meshData.submeshes == null || normalSums == null || normalSums.Length == 0)
            {
                return 0;
            }

            int processedTriangleCount = 0;
            Vector3[] vertices = meshData.vertices;
            for (int submeshIndex = 0; submeshIndex < meshData.submeshes.Length; submeshIndex++)
            {
                SubMeshTriangles submesh = meshData.submeshes[submeshIndex];
                int[] triangles = GetBaseManagedTriangles(submesh);
                if (triangles == null || triangles.Length < 3)
                {
                    continue;
                }

                for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                {
                    int vertexIndex0 = triangles[triangleIndex];
                    int vertexIndex1 = triangles[triangleIndex + 1];
                    int vertexIndex2 = triangles[triangleIndex + 2];
                    if (!IsValidVertexIndex(vertexIndex0, normalSums.Length) || !IsValidVertexIndex(vertexIndex1, normalSums.Length) || !IsValidVertexIndex(vertexIndex2, normalSums.Length))
                    {
                        continue;
                    }

                    Vector3 edge0 = vertices[vertexIndex1] - vertices[vertexIndex0];
                    Vector3 edge1 = vertices[vertexIndex2] - vertices[vertexIndex0];
                    Vector3 faceNormal = Vector3.Cross(edge0, edge1);
                    if (faceNormal.sqrMagnitude <= 0.0000001f)
                    {
                        continue;
                    }

                    Vector3 contribution = weightNormalsByTriangleSize ? faceNormal : faceNormal.normalized;
                    normalSums[vertexIndex0] += contribution;
                    normalSums[vertexIndex1] += contribution;
                    normalSums[vertexIndex2] += contribution;
                    processedTriangleCount++;
                }
            }

            return processedTriangleCount;
        }

        private static Vector4[] RecalculateSlotTangents(UMAMeshData meshData, Vector3[] normals)
        {
            Vector3[] vertices = meshData.vertices;
            Vector2[] uv = meshData.uv;
            int vertexCount = vertices.Length;
            Vector3[] tangentSums = new Vector3[vertexCount];
            Vector3[] bitangentSums = new Vector3[vertexCount];
            bool haveUsableUvs = uv != null && uv.Length == vertexCount;

            if (haveUsableUvs && meshData.submeshes != null)
            {
                for (int submeshIndex = 0; submeshIndex < meshData.submeshes.Length; submeshIndex++)
                {
                    SubMeshTriangles submesh = meshData.submeshes[submeshIndex];
                    int[] triangles = GetBaseManagedTriangles(submesh);
                    if (triangles == null || triangles.Length < 3)
                    {
                        continue;
                    }

                    for (int triangleIndex = 0; triangleIndex + 2 < triangles.Length; triangleIndex += 3)
                    {
                        int vertexIndex0 = triangles[triangleIndex];
                        int vertexIndex1 = triangles[triangleIndex + 1];
                        int vertexIndex2 = triangles[triangleIndex + 2];
                        if (!IsValidVertexIndex(vertexIndex0, vertexCount) || !IsValidVertexIndex(vertexIndex1, vertexCount) || !IsValidVertexIndex(vertexIndex2, vertexCount))
                        {
                            continue;
                        }

                        Vector3 edge0 = vertices[vertexIndex1] - vertices[vertexIndex0];
                        Vector3 edge1 = vertices[vertexIndex2] - vertices[vertexIndex0];
                        Vector2 uvDelta0 = uv[vertexIndex1] - uv[vertexIndex0];
                        Vector2 uvDelta1 = uv[vertexIndex2] - uv[vertexIndex0];
                        float determinant = uvDelta0.x * uvDelta1.y - uvDelta1.x * uvDelta0.y;
                        if (Mathf.Abs(determinant) <= 0.0000001f)
                        {
                            continue;
                        }

                        float reciprocal = 1f / determinant;
                        Vector3 tangent = (edge0 * uvDelta1.y - edge1 * uvDelta0.y) * reciprocal;
                        Vector3 bitangent = (edge1 * uvDelta0.x - edge0 * uvDelta1.x) * reciprocal;
                        tangentSums[vertexIndex0] += tangent;
                        tangentSums[vertexIndex1] += tangent;
                        tangentSums[vertexIndex2] += tangent;
                        bitangentSums[vertexIndex0] += bitangent;
                        bitangentSums[vertexIndex1] += bitangent;
                        bitangentSums[vertexIndex2] += bitangent;
                    }
                }
            }

            Vector4[] tangents = new Vector4[vertexCount];
            Vector4[] previousTangents = meshData.tangents;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                Vector3 normal = normals[vertexIndex].sqrMagnitude > 0.0000001f ? normals[vertexIndex].normalized : Vector3.up;
                Vector3 tangent = tangentSums[vertexIndex];
                if (tangent.sqrMagnitude <= 0.0000001f && previousTangents != null && vertexIndex < previousTangents.Length)
                {
                    tangent = new Vector3(previousTangents[vertexIndex].x, previousTangents[vertexIndex].y, previousTangents[vertexIndex].z);
                }
                tangent = tangent - normal * Vector3.Dot(normal, tangent);
                if (tangent.sqrMagnitude <= 0.0000001f)
                {
                    tangent = BuildFallbackTangent(normal);
                }
                else
                {
                    tangent.Normalize();
                }

                Vector3 bitangent = bitangentSums[vertexIndex];
                float handedness = bitangent.sqrMagnitude > 0.0000001f && Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0f ? -1f : 1f;
                tangents[vertexIndex] = new Vector4(tangent.x, tangent.y, tangent.z, handedness);
            }

            return tangents;
        }

        private static Vector3 BuildFallbackTangent(Vector3 normal)
        {
            Vector3 reference = Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, normal);
            if (tangent.sqrMagnitude <= 0.0000001f)
            {
                tangent = Vector3.right;
            }
            else
            {
                tangent.Normalize();
            }
            return tangent;
        }

        private static int[] GetBaseManagedTriangles(SubMeshTriangles submesh)
        {
            if (submesh == null)
            {
                return null;
            }

            int[] triangles = submesh.GetBaseTriangles();
            if (triangles == null || triangles.Length < 3)
            {
                triangles = submesh.getManagedTriangles(0);
            }
            return triangles;
        }

        private void DrawTransformDebugInfo()
        {
            GUILayout.Space(8);
            GUIHelper.BeginVerticalPadded(10, new Color(0.92f, 0.95f, 1f));
            GUILayout.Label("Transform Rotation / Bindpose", EditorStyles.boldLabel);

            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
            {
                EditorGUILayout.HelpBox("MeshData is missing.", MessageType.Info);
                GUIHelper.EndVerticalPadded(10);
                return;
            }

            string rootTransformName = string.IsNullOrEmpty(slot.meshData.RootBoneName) ? "rootTransform" : slot.meshData.RootBoneName;
            DrawTransformDebugGroup("rootTransform", rootTransformName);
            DrawTransformDebugGroup("Global", "Global");
            DrawTransformDebugGroup("Position", "Position");

            GUIHelper.EndVerticalPadded(10);
        }

        private void DrawTransformDebugGroup(string label, string transformName)
        {
            GUIHelper.BeginVerticalPadded(8, new Color(0.98f, 0.98f, 1f));
            GUILayout.Label(string.Equals(label, transformName, StringComparison.Ordinal) ? label : label + " (" + transformName + ")", EditorStyles.boldLabel);

            if (TryGetMeshTransformInfo(transformName, out UMATransform meshTransform, out Matrix4x4? bindPose))
            {
                Vector3 euler = meshTransform.rotation.eulerAngles;
                EditorGUILayout.LabelField("Rotation", $"Euler {FormatVector3(euler)}");
                EditorGUILayout.LabelField("Bindpose", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(FormatMatrix(bindPose.GetValueOrDefault()), EditorStyles.textArea, GUILayout.MinHeight(76f));
            }
            else
            {
                EditorGUILayout.LabelField("Rotation", "Not found");
                EditorGUILayout.LabelField("Bindpose", "Not found");
            }

            GUIHelper.EndVerticalPadded(8);
        }

        private bool TryGetMeshTransformInfo(string transformName, out UMATransform meshTransform, out Matrix4x4? bindPose)
        {
            meshTransform = null;
            bindPose = null;

            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || slot.meshData.umaBones == null || string.IsNullOrEmpty(transformName))
            {
                return false;
            }

            for (int i = 0; i < slot.meshData.umaBones.Length; i++)
            {
                var bone = slot.meshData.umaBones[i];
                if (bone == null || !string.Equals(bone.name, transformName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                meshTransform = bone;
                if (slot.meshData.boneNameHashes != null && slot.meshData.bindPoses != null)
                {
                    for (int bindPoseIndex = 0; bindPoseIndex < slot.meshData.boneNameHashes.Length; bindPoseIndex++)
                    {
                        if (slot.meshData.boneNameHashes[bindPoseIndex] == bone.hash)
                        {
                            if (bindPoseIndex < slot.meshData.bindPoses.Length)
                            {
                                bindPose = slot.meshData.bindPoses[bindPoseIndex];
                            }
                            break;
                        }
                    }
                }

                return bindPose.HasValue;
            }

            return false;
        }

        private static string FormatVector3(Vector3 value)
        {
            return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
        }

        private static string FormatMatrix(Matrix4x4 matrix)
        {
            return
                $"[{matrix.m00,8:F4} {matrix.m01,8:F4} {matrix.m02,8:F4} {matrix.m03,8:F4}]\n" +
                $"[{matrix.m10,8:F4} {matrix.m11,8:F4} {matrix.m12,8:F4} {matrix.m13,8:F4}]\n" +
                $"[{matrix.m20,8:F4} {matrix.m21,8:F4} {matrix.m22,8:F4} {matrix.m23,8:F4}]\n" +
                $"[{matrix.m30,8:F4} {matrix.m31,8:F4} {matrix.m32,8:F4} {matrix.m33,8:F4}]";
        }

        private void ShowDebugVertInfo(SlotDataAsset current, int previewVertex)
        {
            if (current == null || WeldToSlot == null || UMAMeshData.IsNullOrEmptyMeshData(current.meshData) || UMAMeshData.IsNullOrEmptyMeshData(WeldToSlot.meshData))
            {
                Debug.Log("Missing mesh data for debug info.");
                return;
            }

            StringBuilder sb = new StringBuilder();

            current.BuildVertexLookups(WeldToSlot);
            current.BuildOurAndTheirBoneWeights(WeldToSlot);
            current.BuildBoneLookups(WeldToSlot);

            if (previewVertex < 0 || previewVertex >= (current.meshData.vertices?.Length ?? 0))
            {
                Debug.Log("Preview vertex out of range.");
                return;
            }

            foreach (var bw in current.OurBoneWeights[previewVertex])
            {
                string boneName = current.meshData.umaBones[bw.boneIndex].name;
                sb.Append($"Bone {boneName}({bw.boneIndex}): Weight {bw.weight}");
                sb.Append(Environment.NewLine);
            }
            Debug.Log("Our vertex " + previewVertex + Environment.NewLine + sb.ToString());

            int theirVertex = current.OurVertextoTheirVertex[previewVertex];
            foreach (var bw in current.TheirBoneWeights[theirVertex])
            {
                string boneName = WeldToSlot.meshData.umaBones[bw.boneIndex].name;
                sb.Append($"Bone {boneName}({bw.boneIndex}): Weight {bw.weight}");
                sb.Append(Environment.NewLine);
            }
            Debug.Log("Their vertex " + theirVertex + Environment.NewLine + sb.ToString());

        }

        public override void OnPreviewSettings()
        {
            DrawPreviewLodSelector(target as SlotDataAsset, true);

            if (MeshPreview == null)
                return;
            try
            {
                MeshPreview.OnPreviewSettings();
            }
            catch (System.Exception)
            {
            }
        }

        private void RebuildPreviewMesh(SlotDataAsset currentTarget)
        {
            if (MeshPreview != null)
            {
                MeshPreview.Dispose();
                MeshPreview = null;
            }
            if (meshToPreview != null)
            {
                DestroyImmediate(meshToPreview);
                meshToPreview = null;
            }

            ClampPreviewLodLevel(currentTarget);
            meshToPreview = GetPreviewMeshFor(currentTarget);
            previewForTarget = currentTarget;
            lastBuiltRotation = previewRotation;
            lastBuiltLodLevel = previewLodLevel;
            reConfigurePreview = false;

            if (meshToPreview != null)
            {
                MeshPreview = new MeshPreview(meshToPreview);
            }
        }

        private void DrawPreviewLodSelector(SlotDataAsset currentTarget, bool compact)
        {
            int lodCount = Mathf.Max(1, GetPreviewLodCount(currentTarget));
            int clampedLodLevel = Mathf.Clamp(previewLodLevel, 0, lodCount - 1);
            if (clampedLodLevel != previewLodLevel)
            {
                previewLodLevel = clampedLodLevel;
                reConfigurePreview = true;
            }

            string[] lodOptions = BuildPreviewLodOptions(lodCount);
            EditorGUI.BeginChangeCheck();
            int selectedLodLevel;
            if (compact)
            {
                GUILayout.Label("LOD", EditorStyles.miniLabel, GUILayout.Width(28f));
                selectedLodLevel = EditorGUILayout.Popup(previewLodLevel, lodOptions, EditorStyles.toolbarPopup, GUILayout.Width(72f));
            }
            else
            {
                selectedLodLevel = EditorGUILayout.Popup("Preview LOD", previewLodLevel, lodOptions);
            }

            if (EditorGUI.EndChangeCheck())
            {
                previewLodLevel = selectedLodLevel;
                reConfigurePreview = true;
                Repaint();
            }
        }

        private bool ClampPreviewLodLevel(SlotDataAsset currentTarget)
        {
            int lodCount = Mathf.Max(1, GetPreviewLodCount(currentTarget));
            int clampedLodLevel = Mathf.Clamp(previewLodLevel, 0, lodCount - 1);
            if (clampedLodLevel == previewLodLevel)
            {
                return false;
            }

            previewLodLevel = clampedLodLevel;
            return true;
        }

        private int GetPreviewLodCount(SlotDataAsset currentTarget)
        {
            switch (previewMode)
            {
                case SlotPreviewMode.WeldSlot:
                    return GetSlotPreviewLodCount(WeldToSlot);
                case SlotPreviewMode.BothSlots:
                    return Mathf.Max(GetSlotPreviewLodCount(currentTarget), GetSlotPreviewLodCount(WeldToSlot));
                default:
                    return GetSlotPreviewLodCount(currentTarget);
            }
        }

        private static int GetSlotPreviewLodCount(SlotDataAsset previewSlot)
        {
            if (previewSlot == null || UMAMeshData.IsNullOrEmptyMeshData(previewSlot.meshData) || previewSlot.meshData.submeshes == null)
            {
                return 0;
            }

            int maxLodCount = 0;
            for (int submeshIndex = 0; submeshIndex < previewSlot.meshData.submeshes.Length; submeshIndex++)
            {
                SubMeshTriangles submesh = previewSlot.meshData.submeshes[submeshIndex];
                if (submesh == null)
                {
                    continue;
                }

                int lodCount = submesh.LODCount();
                if (lodCount <= 0 && submesh.GetTriangleCount(0) > 0)
                {
                    lodCount = 1;
                }

                if (lodCount > maxLodCount)
                {
                    maxLodCount = lodCount;
                }
            }

            return maxLodCount;
        }

        private static string[] BuildPreviewLodOptions(int lodCount)
        {
            string[] lodOptions = new string[lodCount];
            for (int lodIndex = 0; lodIndex < lodOptions.Length; lodIndex++)
            {
                lodOptions[lodIndex] = "LOD " + lodIndex;
            }

            return lodOptions;
        }

        private Mesh GetPreviewMeshFor(SlotDataAsset which)
        {
            try
            {
                ClampPreviewLodLevel(which);
                int lodLevel = previewLodLevel;
                Quaternion pRot = Quaternion.Euler(previewRotation);
                if (previewMode == SlotPreviewMode.ThisSlot)
                {
                    if (which == null) return null;
                    return SlotToMesh.ConvertSlotToMesh(which, pRot, previewVertex, lodLevel);
                }
                if (previewMode == SlotPreviewMode.WeldSlot)
                {
                    if (WeldToSlot != null)
                    {
                        return SlotToMesh.ConvertSlotToMesh(WeldToSlot, pRot, previewVertex, lodLevel);
                    }
                }
                if (previewMode == SlotPreviewMode.BothSlots)
                {
                    if (which == null) return null;
                    Mesh mesh = SlotToMesh.ConvertSlotToMeshLTOW(which, pRot, previewVertex, lodLevel);
                    if (WeldToSlot != null)
                    {
                        Mesh weldMesh = SlotToMesh.ConvertSlotToMeshLTOW(WeldToSlot, pRot, previewVertex, lodLevel);
                        if (weldMesh != null)
                        {
                            CombineInstance[] combine = new CombineInstance[2];
                            combine[0].mesh = mesh;
                            combine[1].mesh = weldMesh;
                            Mesh combinedMesh = new Mesh();
                            combinedMesh.CombineMeshes(combine, false, false, false);
                            DestroyImmediate(mesh);
                            DestroyImmediate(weldMesh);
                            return combinedMesh;
                        }
                    }
                    return mesh;
                }
            }
            catch
            {
                // Preview failed (likely due to reload). Return null and let GUI handle it gracefully.
            }
            return null;
        }

        public bool GuiPreviewButton(Rect buttonRect, string label )
        {
            GUI.Button(buttonRect, label);
            Event e = Event.current;
            // Handle click manually
            if (e.type == EventType.MouseUp && buttonRect.Contains(e.mousePosition))
            {
                return true;
            }
            return false;
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
        {
            var currentTarget = target as SlotDataAsset;
            if (currentTarget == null)
            {
                EditorGUI.LabelField(r, "Slot is not available.");
                return;
            }

            if (currentTarget.isUtilitySlot)
            {
                EditorGUI.LabelField(r, "Utility slots cannot be previewed.");
                return;
            }

            if (ClampPreviewLodLevel(currentTarget))
            {
                reConfigurePreview = true;
            }

            const float controlYOffset = 32f;
            const float controlButtonHeight = 30f;

            if (meshToPreview == null || previewForTarget != currentTarget || reConfigurePreview)
            {
                RebuildPreviewMesh(currentTarget);
            }

            Rect controlArea = new Rect(r.x, r.y + controlYOffset, r.width, r.height - controlYOffset);
            Rect dragArea = r;
            if (controlArea.height > 60f)
            {
                dragArea = new Rect(r.x, r.y, r.width, Mathf.Max(0f, (controlArea.y - r.y) + (controlArea.height - controlButtonHeight)));
            }

            // Handle mouse drag to rotate (per inspector), independent of MeshPreview.
            // Exclude the button strip so button clicks are not consumed by the drag handler.
            HandlePreviewDrag(dragArea);

            if (meshToPreview != null && (lastBuiltRotation != previewRotation || lastBuiltLodLevel != previewLodLevel || reConfigurePreview))
            {
                RebuildPreviewMesh(currentTarget);
            }

            if (meshToPreview != null && MeshPreview != null)
            {
                MeshPreview.OnPreviewGUI(r, background);

                if (controlArea.height > 60)
                {
                    Event e = Event.current;
                    float buttonSpace = controlArea.width / 4;
                    float buttonWidth = buttonSpace - 2;
                    Rect ButtonArea = new Rect(controlArea.x, controlArea.y, buttonWidth, 30);
                    if (GuiPreviewButton(ButtonArea, "Reset"))
                    {
                        previewRotation = Vector3.zero;
                        reConfigurePreview = true;
                        Repaint();
                    }
                    ButtonArea.x += buttonSpace;
                    if (GuiPreviewButton(ButtonArea, "X+90"))
                    {
                        previewRotation.x = Wrap360(previewRotation.x + 90f);
                        reConfigurePreview = true;
                        Repaint();
                    }
                    ButtonArea.x += buttonSpace;
                    if (GuiPreviewButton(ButtonArea, "Y+90"))
                    {
                        previewRotation.y = Wrap360(previewRotation.y + 90f);
                        reConfigurePreview = true;
                        Repaint();
                    }
                    ButtonArea.x += buttonSpace;
                    if (GuiPreviewButton(ButtonArea, "Z+90"))
                    {
                        previewRotation.z = Wrap360(previewRotation.z + 90f);
                        reConfigurePreview = true;
                        Repaint();
                    }


                    GUI.Label(new Rect(controlArea.x, controlArea.y + 32, controlArea.width, 20), $"{previewRotation}");
                }

                // Only draw overlay during repaint so we don't intercept mouse events (fixes rotate with multiple previews)
                if (Event.current.type == EventType.Repaint)
                {
                    string info = MeshPreview.GetInfoString(meshToPreview);
                    float pad = 6f;
                    float line = EditorGUIUtility.singleLineHeight;
                    Rect labelRect = new Rect(r.x + pad, r.yMax - line - pad, r.width - (pad * 2f), line);

                    var bgRect = new Rect(labelRect.x - 2, labelRect.y - 1, labelRect.width + 4, labelRect.height + 2);
                    EditorGUI.DrawRect(bgRect, new Color(0f, 0f, 0f, 0.4f));

                    var style = new GUIStyle(EditorStyles.whiteMiniLabel)
                    {
                        alignment = TextAnchor.LowerLeft,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                        richText = false
                    };

                    GUI.Label(labelRect, info, style);
                }
            }
        }

        // Mouse drag handler to update previewRotation per inspector
        private void HandlePreviewDrag(Rect r)
        {
            int controlID = GUIUtility.GetControlID("UMASlotPreviewDrag".GetHashCode(), FocusType.Passive);
            Event evt = Event.current;
            switch (evt.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (r.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        GUIUtility.hotControl = controlID;
                        evt.Use();
                        EditorGUIUtility.SetWantsMouseJumping(1);
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        //evt.Use();
                    }
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        // Scale drag by rect size similar to Unity's preview controls
                        float scale = 140f / Mathf.Min(Mathf.Max(1f, r.width), Mathf.Max(1f, r.height));
                        // Yaw around Y with horizontal drag; Pitch around X with vertical drag
                        previewRotation.y = Wrap360(previewRotation.y - evt.delta.x * scale);
                        previewRotation.x = Mathf.Clamp(previewRotation.x + evt.delta.y * scale, -90f, 90f);
                        //evt.Use();
                        Repaint();
                    }
                    break;
            }
        }

        private void UpdateSlotDropAreaGUI(Rect dropArea)
        {
            GameObject obj = DropAreaGUI(dropArea);
            if (obj != null)
            {
                SkinnedMeshRenderer skinnedMesh = obj.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMesh != null)
                {
                    // normalReferenceMesh may be null. It's OK to pass null seams mesh.
                    UpdateSlotData(slot != null ? slot.normalReferenceMesh : null, skinnedMesh);
                    GUI.changed = true;
                    EditorUtility.DisplayDialog("Complete", "Update completed", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "No SkinnedMeshRenderer found!", "Ok");
                }
            }

        }

        private GameObject DropAreaGUI(Rect dropArea)
        {
            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    Event.current.Use();
                }
            }

            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if (draggedObjects[i])
                        {
                            var go = draggedObjects[i] as GameObject;
                            if (go != null)
                            {
                                Event.current.Use();
                                return go;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private void UpdateSlotData(SkinnedMeshRenderer seamsMesh, SkinnedMeshRenderer skinnedMesh)
        {
            SlotDataAsset s = target as SlotDataAsset;
            if (s == null)
            {
                EditorUtility.DisplayDialog("Error", "Target slot is not available.", "OK");
                return;
            }

            string existingRootBone = !UMAMeshData.IsNullOrEmptyMeshData(s.meshData) ? s.meshData.RootBoneName : string.Empty;

            UMASlotProcessingUtil.UpdateSlotData(s, skinnedMesh, null, seamsMesh, existingRootBone, true, clearNormals, clearTangents);
            string path = AssetDatabase.GetAssetPath(target.GetEntityId());
            AssetDatabase.ImportAsset(path);
            UMAUpdateProcessor.UpdateSlot(s);
        }

        /// <summary>
        /// Conform this slot's bindposes & vertices to those of sourceSlot.
        /// Vertices are transformed using the dominant bone (highest weight).
        /// Bones absent in source retain original bindpose.
        /// </summary>
        public static string ConformBindposesAndVertices(SlotDataAsset targetSlot, SlotDataAsset sourceSlot)
        {
            if (targetSlot == null || sourceSlot == null || UMAMeshData.IsNullOrEmptyMeshData(targetSlot.meshData) || UMAMeshData.IsNullOrEmptyMeshData(sourceSlot.meshData))
                return "Missing mesh data.";

            var tMesh = targetSlot.meshData;
            var sMesh = sourceSlot.meshData;

            if (tMesh.bindPoses == null || sMesh.bindPoses == null ||
                tMesh.boneNameHashes == null || sMesh.boneNameHashes == null)
                return "Bindpose arrays missing.";

            int tBoneCount = tMesh.bindPoses.Length;
            int vCount = tMesh.vertexCount;
            if (vCount == 0 || tBoneCount == 0) return "No vertices or bones.";

            // Map source hash -> bindPose
            var srcMap = new Dictionary<int, Matrix4x4>(sMesh.boneNameHashes.Length);
            for (int i = 0; i < sMesh.boneNameHashes.Length && i < sMesh.bindPoses.Length; i++)
            {
                int h = sMesh.boneNameHashes[i];
                if (!srcMap.ContainsKey(h))
                    srcMap.Add(h, sMesh.bindPoses[i]);
            }

            // Prepare transformation per target bone (identity if no change)
            var boneTransforms = new Matrix4x4[tBoneCount];
            bool anyChange = false;
            for (int i = 0; i < tBoneCount; i++)
            {
                boneTransforms[i] = Matrix4x4.identity;
                int hash = tMesh.boneNameHashes[i];
                if (srcMap.TryGetValue(hash, out var srcBind))
                {
                    var oldBind = tMesh.bindPoses[i];
                    if (!CompareBindpose(oldBind, srcBind))
                    {
                        // Target must change positions by T so that: srcBind * p_new = oldBind * p_old
                        Matrix4x4 T = Matrix4x4.Inverse(srcBind) * oldBind;
                        boneTransforms[i] = T;
                        anyChange = true;
                    }
                }
            }
            if (!anyChange) return "No differing bindposes found.";

            // Bone weights
            byte[] bonesPerVertex = tMesh.ManagedBonesPerVertex;
            BoneWeight1[] weights = tMesh.ManagedBoneWeights;
            if (bonesPerVertex == null || weights == null || bonesPerVertex.Length == 0 || weights.Length == 0)
                return "Bone weights missing.";

            Vector3[] verts = tMesh.vertices;
            Vector3[] normals = tMesh.normals;
            Vector4[] tangents = tMesh.tangents;

            int wOffset = 0;
            for (int v = 0; v < vCount; v++)
            {
                byte count = bonesPerVertex[v];
                if (count == 0) { continue; }

                int dominantIndex = -1;
                float dominantWeight = -1f;
                for (int j = 0; j < count; j++)
                {
                    var bw = weights[wOffset + j];
                    if (bw.weight > dominantWeight)
                    {
                        dominantWeight = bw.weight;
                        dominantIndex = bw.boneIndex;
                    }
                }

                if (dominantIndex >= 0 && dominantIndex < boneTransforms.Length)
                {
                    Matrix4x4 T = boneTransforms[dominantIndex];
                    if (!IsIdentity(T))
                    {
                        // Position
                        Vector3 p = verts[v];
                        Vector4 hp = new Vector4(p.x, p.y, p.z, 1f);
                        hp = T * hp;
                        verts[v] = new Vector3(hp.x, hp.y, hp.z);

                        // Normal
                        if (normals != null && v < normals.Length)
                        {
                            Vector3 n = normals[v];
                            Vector3 tn = T.MultiplyVector(n);
                            if (tn.sqrMagnitude > 0f) tn.Normalize();
                            normals[v] = tn;
                        }
                        // Tangent
                        if (tangents != null && v < tangents.Length)
                        {
                            Vector4 tan = tangents[v];
                            Vector3 tv = new Vector3(tan.x, tan.y, tan.z);
                            tv = T.MultiplyVector(tv);
                            if (tv.sqrMagnitude > 0f) tv.Normalize();
                            tangents[v] = new Vector4(tv.x, tv.y, tv.z, tan.w);
                        }
                    }
                }
                wOffset += count;
            }

            // Replace bindposes (only those with matches)
            for (int i = 0; i < tBoneCount; i++)
            {
                int hash = tMesh.boneNameHashes[i];
                if (srcMap.TryGetValue(hash, out var srcBind))
                {
                    tMesh.bindPoses[i] = srcBind;
                }
            }

            // Mark modifications
            tMesh.verticesModified = true;
            tMesh.normalsModified = true;
            tMesh.tangentsModified = true;
            targetSlot.ValidateMeshData();
            EditorUtility.SetDirty(targetSlot);
            return "Bindpose/vertex conformity complete.";
        }

        private static bool CompareBindpose(Matrix4x4 a, Matrix4x4 b)
        {
            const float eps = 0.0001f;
            return
                Mathf.Abs(a.m00 - b.m00) < eps &&
                Mathf.Abs(a.m01 - b.m01) < eps &&
                Mathf.Abs(a.m02 - b.m02) < eps &&
                Mathf.Abs(a.m03 - b.m03) < eps &&
                Mathf.Abs(a.m10 - b.m10) < eps &&
                Mathf.Abs(a.m11 - b.m11) < eps &&
                Mathf.Abs(a.m12 - b.m12) < eps &&
                Mathf.Abs(a.m13 - b.m13) < eps &&
                Mathf.Abs(a.m20 - b.m20) < eps &&
                Mathf.Abs(a.m21 - b.m21) < eps &&
                Mathf.Abs(a.m22 - b.m22) < eps &&
                Mathf.Abs(a.m23 - b.m23) < eps;
        }

        private static bool IsIdentity(Matrix4x4 m)
        {
            return m == Matrix4x4.identity;
        }

        private void SelectBonesByKeyword(string keyword)
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData) || slotAsset.meshData.umaBones == null)
                return;

            string kw = keyword.ToLowerInvariant();
            var bones = slotAsset.meshData.umaBones;
            while (boneSelection.Count < bones.Length)
                boneSelection.Add(false);

            for (int i = 0; i < bones.Length; i++)
            {
                string name = (bones[i]?.name ?? string.Empty).ToLowerInvariant();
                if (name.IndexOf(kw, StringComparison.Ordinal) >= 0)
                    boneSelection[i] = true;
            }
        }

        private void SelectBonesByKeywords(params string[] keywords)
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData) || slotAsset.meshData.umaBones == null)
                return;

            var bones = slotAsset.meshData.umaBones;
            while (boneSelection.Count < bones.Length)
                boneSelection.Add(false);

            for (int i = 0; i < bones.Length; i++)
            {
                string name = (bones[i]?.name ?? string.Empty).ToLowerInvariant();
                for (int k = 0; k < keywords.Length; k++)
                {
                    if (name.IndexOf(keywords[k].ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                    {
                        boneSelection[i] = true;
                        break;
                    }
                }
            }
        }

        private void AddCheckedBonesToAnimated()
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData) || slotAsset.meshData.umaBones == null)
                return;

            var bones = slotAsset.meshData.umaBones;
            var selectedNames = new List<string>();
            for (int i = 0; i < boneSelection.Count && i < bones.Length; i++)
            {
                if (boneSelection[i])
                    selectedNames.Add(bones[i]?.name ?? $"Bone_{i}");
            }

            if (selectedNames.Count == 0)
            {
                EditorUtility.DisplayDialog("Animated Bones", "No bones selected.", "OK");
                return;
            }

            // Create BaseUpdatedObject entries for each selected bone
            var existingList = slotAsset.animatedBones != null
                ? new List<BaseUpdatedObject>(slotAsset.animatedBones)
                : new List<BaseUpdatedObject>();

            string assetPath = AssetDatabase.GetAssetPath(slotAsset);
            int added = 0;
            foreach (string boneName in selectedNames)
            {
                // Skip if already present
                bool alreadyExists = false;
                foreach (var existing in existingList)
                {
                    if (existing != null && existing.name == boneName)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (alreadyExists) continue;

                var anim = ScriptableObject.CreateInstance<BaseUpdatedObject>();
                anim.name = boneName;
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.AddObjectToAsset(anim, slotAsset);
                }
                existingList.Add(anim);
                added++;
            }

            slotAsset.animatedBones = existingList.ToArray();
            EditorUtility.SetDirty(slotAsset);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.SaveAssetIfDirty(slotAsset);

            EditorUtility.DisplayDialog("Animated Bones",
                $"Added {added} bone(s) to animated bones list. {selectedNames.Count - added} already existed.",
                "OK");
        }

        private void AddCheckedBonesToUnbaked()
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null || UMAMeshData.IsNullOrEmptyMeshData(slotAsset.meshData) || slotAsset.meshData.umaBones == null)
                return;

            var bones = slotAsset.meshData.umaBones;
            var selectedNames = new List<string>();
            for (int i = 0; i < boneSelection.Count && i < bones.Length; i++)
            {
                if (boneSelection[i])
                    selectedNames.Add(bones[i]?.name ?? $"Bone_{i}");
            }

            if (selectedNames.Count == 0)
            {
                EditorUtility.DisplayDialog("Unbaked Animated Bones", "No bones selected.", "OK");
                return;
            }

            var existingList = slotAsset.UnbakedAnimatedBones != null
                ? new List<string>(slotAsset.UnbakedAnimatedBones)
                : new List<string>();

            int added = 0;
            foreach (string boneName in selectedNames)
            {
                if (!existingList.Contains(boneName))
                {
                    existingList.Add(boneName);
                    added++;
                }
            }

            slotAsset.UnbakedAnimatedBones = existingList.ToArray();
            EditorUtility.SetDirty(slotAsset);
            string assetPath = AssetDatabase.GetAssetPath(slotAsset);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.SaveAssetIfDirty(slotAsset);

            EditorUtility.DisplayDialog("Unbaked Animated Bones",
                $"Added {added} bone(s) to unbaked animated bones list. {selectedNames.Count - added} already existed.",
                "OK");
        }

        private void ClearAnimatedBones()
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null) return;

            slotAsset.animatedBones = new BaseUpdatedObject[0];
            EditorUtility.SetDirty(slotAsset);
            string assetPath = AssetDatabase.GetAssetPath(slotAsset);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.SaveAssetIfDirty(slotAsset);

            EditorUtility.DisplayDialog("Animated Bones", "Cleared all animated bones.", "OK");
        }

        private void ClearUnbakedAnimatedBones()
        {
            var slotAsset = target as SlotDataAsset;
            if (slotAsset == null) return;

            slotAsset.UnbakedAnimatedBones = new string[0];
            EditorUtility.SetDirty(slotAsset);
            string assetPath = AssetDatabase.GetAssetPath(slotAsset);
            if (!string.IsNullOrEmpty(assetPath))
                AssetDatabase.SaveAssetIfDirty(slotAsset);

            EditorUtility.DisplayDialog("Unbaked Animated Bones", "Cleared all unbaked animated bones.", "OK");
        }

        private void FixupUMA2Slots()
        {
            int fixedCount = 0;
            int skippedCount = 0;
            var sb = new System.Text.StringBuilder();

            foreach (var t in targets)
            {
                var slotAsset = t as SlotDataAsset;
                if (slotAsset == null) continue;

                var md = slotAsset.meshData;
                if (UMAMeshData.IsNullOrEmptyMeshData(md))
                {
                    skippedCount++;
                    continue;
                }

                bool changed = false;
                int vertCount = md.vertices != null ? md.vertices.Length : md.vertexCount;
                if (vertCount <= 0)
                {
                    skippedCount++;
                    continue;
                }

                // 1. Convert legacy bone weights to managed format
                if (md.boneWeights != null && md.boneWeights.Length > 0)
                {
                    bool needConversion = md.ManagedBonesPerVertex == null
                        || md.ManagedBonesPerVertex.Length != vertCount
                        || md.ManagedBoneWeights == null
                        || md.ManagedBoneWeights.Length == 0;

                    if (needConversion)
                    {
                        md.LoadBoneWeights();
                        sb.AppendLine($"  - Converted legacy boneWeights -> ManagedBoneWeights ({md.ManagedBonesPerVertex.Length} vertices)");
                        changed = true;
                    }

                    if (md.ManagedBoneWeights != null && md.ManagedBoneWeights.Length > 0)
                    {
                        md.boneWeights = null;
                        sb.AppendLine("  - Cleared legacy boneWeights array");
                        changed = true;
                    }
                }

                // 2. Add vertex colors if missing
                if (md.colors32 == null || md.colors32.Length != vertCount)
                {
                    var colors = new Color32[vertCount];
                    for (int i = 0; i < vertCount; i++)
                        colors[i] = new Color32(255, 255, 255, 255);
                    md.colors32 = colors;
                    sb.AppendLine($"  - Added vertex colors (white) for {vertCount} vertices");
                    changed = true;
                }

                // 3. Ensure vertexCount matches vertices.Length
                if (md.vertexCount != vertCount)
                {
                    md.vertexCount = vertCount;
                    sb.AppendLine($"  - Fixed vertexCount: {md.vertexCount} -> {vertCount}");
                    changed = true;
                }

                // 4. Mark as non-legacy
                if (slotAsset.isLegacySlot)
                {
                    slotAsset.isLegacySlot = false;
                    sb.AppendLine("  - Cleared isLegacySlot flag");
                    changed = true;
                }

                // 5. Ensure subMeshIndex is valid
                if (slotAsset.subMeshIndex < 0 || (md.subMeshCount > 0 && slotAsset.subMeshIndex >= md.subMeshCount))
                {
                    slotAsset.subMeshIndex = 0;
                    sb.AppendLine("  - Reset subMeshIndex to 0");
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(slotAsset);
                    fixedCount++;
                    sb.Insert(0, $"Fixed '{slotAsset.slotName}':\n");
                }
                else
                {
                    skippedCount++;
                }
            }

            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
                EditorUtility.DisplayDialog("Fixup UMA 2 -> UMA 3",
                    $"Fixed {fixedCount} slot(s), skipped {skippedCount}.\n\nDetails:\n{sb}",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Fixup UMA 2 -> UMA 3",
                    $"No changes needed. All {skippedCount} slot(s) already up to date.",
                    "OK");
            }
        }
    }

}
#endif
