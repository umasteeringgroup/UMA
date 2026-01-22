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

        static string[] RegularSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "smooshOffset", "smooshExpand", "Welds" };
        static string[] WildcardSlotFields = new string[] { "slotName", "CharacterBegun", "SlotAtlassed", "SlotProcessed", "SlotBeginProcessing", "DNAApplied", "CharacterCompleted", "_slotDNALegacy", "tags", "isWildCardSlot", "Races", "_rendererAsset", "maxLOD", "useAtlasOverlay", "overlayScale", "_slotDNA", "meshData", "subMeshIndex", "Welds" };
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
            wildcard.slotName = "WildCard";
            EditorUtility.SetDirty(wildcard);
            string path = AssetDatabase.GetAssetPath(wildcard.GetInstanceID());
            AssetDatabase.ImportAsset(path);
            EditorUtility.DisplayDialog("UMA", "Wildcard slot created. You should first change the SlotName in the inspector, and then add it to the global library or to a scene library", "OK");
        }

        private void OnDestroy()
        {
            DisposePreview();
        }

        void OnEnable()
        {
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

            SetRaceListsSafe();

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
                EditorGUILayout.HelpBox("Unity is compiling/reloading. Please wait…", MessageType.Info);
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
            if (GUILayout.Button("Use Obj Name", GUILayout.Width(90)))
            {
                foreach (var t in targets)
                {
                    var slotDataAsset = t as SlotDataAsset;
                    if (slotDataAsset == null) continue;
                    slotDataAsset.slotName = slotDataAsset.name;
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

            // LOD Info
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            lodFoldout = EditorGUILayout.Foldout(lodFoldout, "LOD");
            GUILayout.EndHorizontal();
            if (lodFoldout)
            {
                GUILayout.Space(10);
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                if (slot.meshData == null || slot.meshData.submeshes == null || slot.meshData.subMeshCount <= 0)
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
                    string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
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

                GUILayout.BeginHorizontal();
                GUILayout.Label("Mirror UV Channel ", GUILayout.Width(150));
                uvChannelToMirror = EditorGUILayout.Popup(uvChannelToMirror, new string[] { "1", "2", "3", "4" }, GUILayout.Width(50));

                if (GUILayout.Button("Mirror U"))
                {
                    var slotDataAsset = target as SlotDataAsset;
                    if (slotDataAsset?.meshData == null)
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
                    if (slotDataAsset?.meshData == null)
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

                bool canConform = bindposeSourceSlot != null && bindposeSourceSlot.meshData != null && slot.meshData != null;
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

                bool haveWeldSource = WeldToSlot != null && WeldToSlot.meshData != null && slot.meshData != null;

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

                if (slot.meshData != null)
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

                if (WeldToSlot != null && WeldToSlot.meshData != null)
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

            // Bottom quick-rotate controls for the preview
            GUILayout.Space(12);
            GUIHelper.BeginVerticalPadded(8, new Color(0.90f, 0.95f, 1f));
            GUILayout.Label("Quick Rotate (90°)", EditorStyles.boldLabel);
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

            if (EditorGUI.EndChangeCheck() || forceUpdate)
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
                string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
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

        private void ShowDebugVertInfo(SlotDataAsset current, int previewVertex)
        {
            if (current == null || WeldToSlot == null || current.meshData == null || WeldToSlot.meshData == null)
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
                    Mesh mesh = SlotToMesh.ConvertSlotToMesh(which, pRot, previewVertex);
                    if (WeldToSlot != null)
                    {
                        Mesh weldMesh = SlotToMesh.ConvertSlotToMesh(WeldToSlot, pRot, previewVertex);
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

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background)
        {
            var currentTarget = target as SlotDataAsset;
            if (currentTarget.isUtilitySlot)
            {
                EditorGUI.LabelField(r, "Utility slots cannot be previewed.");
                return;
            }
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

            // Handle mouse drag to rotate (per inspector), independent of MeshPreview
            HandlePreviewDrag(r);

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
                        evt.Use();
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
                        evt.Use();
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

            string existingRootBone = s.meshData != null ? s.meshData.RootBoneName : string.Empty;

            UMASlotProcessingUtil.UpdateSlotData(s, skinnedMesh, s.material, seamsMesh, existingRootBone, true, clearNormals, clearTangents);
            string path = AssetDatabase.GetAssetPath(target.GetInstanceID());
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
            if (targetSlot == null || sourceSlot == null || targetSlot.meshData == null || sourceSlot.meshData == null)
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
}
#endif