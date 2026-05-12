#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
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
        }

        static string[] RegularSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "smooshOffset", "smooshExpand", "Welds" };
        static string[] WildcardSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "_slotDNA", "meshData", "subMeshIndex", "Welds" };
        private static readonly string[] TriplanarUvChannelLabels = new string[] { "0 (uv)", "1 (uv2)", "2 (uv3)", "3 (uv4)" };
        SerializedProperty slotName;
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
        private bool exportIncludeRig = true;
        private string persistedSectionStateKey;
        private string persistedSectionStateCache;

        public override bool HasPreviewGUI() => true;
        MeshPreview MeshPreview;
        Mesh meshToPreview;
        // Make rotation per-inspector (not static) so multi-inspector drags don't conflict
        Vector3 previewRotation = Vector3.zero;
        // Track last built rotation to know when to rebuild
        Vector3 lastBuiltRotation = new Vector3(9999, 9999, 9999);
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

            slotName = serializedObject.FindProperty("slotName");
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

            // Top-level change check (closed at bottom)
            EditorGUI.BeginChangeCheck();

            // Name + tools
            GUILayout.BeginHorizontal();
            if (slotName != null)
                EditorGUILayout.DelayedTextField(slotName);
            if (GUILayout.Button("Clear Legacy Name", GUILayout.Width(90)))
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

            // Draw base properties
            if (slot.isWildCardSlot)
            {
                Editor.DrawPropertiesExcluding(serializedObject, WildcardSlotFields);
            }
            else
            {
                Editor.DrawPropertiesExcluding(serializedObject, RegularSlotFields);
            }
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

                int selectedGroupIndex = 0;
                for (int i = 0; i < groupNames.Length; i++)
                {
                    if (string.Equals(groupNames[i], slotGroupProp.stringValue, StringComparison.Ordinal))
                    {
                        selectedGroupIndex = i;
                        break;
                    }
                }

                GUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(slotGroupProp, new GUIContent("Slot Group"), GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(groupNames.Length == 0))
                {
                    selectedGroupIndex = EditorGUILayout.Popup(selectedGroupIndex, groupNames, GUILayout.Width(110));
                }
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    string value;
                    if (groupNames.Length > 0)
                    {
                        value = groupNames[Mathf.Clamp(selectedGroupIndex, 0, groupNames.Length - 1)];
                    }
                    else
                    {
                        value = slotGroupProp.stringValue;
                    }

                    foreach (var t in targets)
                    {
                        var sda = t as SlotDataAsset;
                        if (sda == null)
                        {
                            continue;
                        }
                        sda.slotGroup = value;
                        EditorUtility.SetDirty(sda);
                    }
                    forceUpdate = true;
                    GUI.changed = true;
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
                if (newPreviewMode != previewMode)
                {
                    reConfigurePreview = true;
                    previewMode = newPreviewMode;
                }
                if (reConfigurePreview)
                {
                    reConfigurePreview = false;
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
                    meshToPreview = GetPreviewMeshFor(target as SlotDataAsset);
                    previewForTarget = target as SlotDataAsset;
                    lastBuiltRotation = previewRotation;
                    if (meshToPreview != null)
                    {
                        MeshPreview = new MeshPreview(meshToPreview);
                    }
                    else
                    {
                        if (MeshPreview != null)
                        {
                            MeshPreview.Dispose();
                            MeshPreview = null;
                        }
                    }

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
            DrawTransformDebugInfo();



            // Bottom quick-rotate controls for the preview
            GUILayout.Space(12);
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

        private static float Wrap360(float angle)
        {
            angle %= 360f;
            if (angle < 0) angle += 360f;
            return angle;
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
            normalCopyMode = Enum.IsDefined(typeof(UMA.SlotDataAsset.NormalCopyMode), state.normalCopyMode)
                ? (UMA.SlotDataAsset.NormalCopyMode)state.normalCopyMode
                : default;
            blendshapeCopyMode = Enum.IsDefined(typeof(UMA.SlotDataAsset.BlendshapeCopyMode), state.blendshapeCopyMode)
                ? (UMA.SlotDataAsset.BlendshapeCopyMode)state.blendshapeCopyMode
                : default;

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
                normalCopyMode = (int)normalCopyMode,
                blendshapeCopyMode = (int)blendshapeCopyMode
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

        private Mesh GetPreviewMeshFor(SlotDataAsset which)
        {
            try
            {
                Quaternion pRot = Quaternion.Euler(previewRotation);
                if (previewMode == SlotPreviewMode.ThisSlot)
                {
                    if (which == null) return null;
                    return SlotToMesh.ConvertSlotToMesh(which, pRot, previewVertex);
                }
                if (previewMode == SlotPreviewMode.WeldSlot)
                {
                    if (WeldToSlot != null)
                    {
                        return SlotToMesh.ConvertSlotToMesh(WeldToSlot, pRot, previewVertex);
                    }
                }
                if (previewMode == SlotPreviewMode.BothSlots)
                {
                    if (which == null) return null;
                    Mesh mesh = SlotToMesh.ConvertSlotToMeshLTOW(which, pRot, previewVertex);
                    if (WeldToSlot != null)
                    {
                        Mesh weldMesh = SlotToMesh.ConvertSlotToMeshLTOW(WeldToSlot, pRot, previewVertex);
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
            if (currentTarget.isUtilitySlot)
            {
                EditorGUI.LabelField(r, "Utility slots cannot be previewed.");
                return;
            }

            const float controlYOffset = 32f;
            const float controlButtonHeight = 30f;

            // Rebuild preview if first time, settings changed, or the target changed
            if (meshToPreview == null || previewForTarget != currentTarget)
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
                meshToPreview = GetPreviewMeshFor(currentTarget);
                previewForTarget = currentTarget;
                lastBuiltRotation = previewRotation;
                if (meshToPreview != null)
                {
                    MeshPreview = new MeshPreview(meshToPreview);
                }
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

            // If rotation changed since last mesh build, rebuild the mesh for this target
            if (meshToPreview != null && (lastBuiltRotation != previewRotation))
            {
                if (MeshPreview != null)
                {
                    MeshPreview.Dispose();
                    MeshPreview = null;
                }
                DestroyImmediate(meshToPreview);
                meshToPreview = GetPreviewMeshFor(currentTarget);
                lastBuiltRotation = previewRotation;
                if (meshToPreview != null)
                {
                    MeshPreview = new MeshPreview(meshToPreview);
                }
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
    }

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