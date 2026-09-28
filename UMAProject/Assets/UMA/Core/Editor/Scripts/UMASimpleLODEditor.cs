using System;
using UnityEngine;
using UnityEditor;
using UMA.Examples;
using UMA.CharacterSystem;
using System.Collections.Generic;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMASimpleLOD))]
    public class UMASimpleLODEditor : Editor
    {

        private const string PrefKeyPrefix = "UMA.UMASimpleLODEditor.InternalSlotLOD.";
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(UMASimpleLOD));

        private static bool _internalSlotLodFoldout;
        private static bool _optionsFoldout = true;
        private static Vector2 _slotScroll;
        private static Dictionary<string, bool> _slotSelection = new Dictionary<string, bool>(64);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RuntimeInitializeOnLoad()
        {
            _internalSlotLodFoldout = false;
            _optionsFoldout = true;
            _slotScroll = Vector2.zero;
            _slotSelection = new Dictionary<string, bool>(64);
        }

        private static int LoadInt(string key, int defaultValue)
        {
            return EditorPrefs.GetInt(PrefKeyPrefix + key, defaultValue);
        }

        private static float LoadFloat(string key, float defaultValue)
        {
            return EditorPrefs.GetFloat(PrefKeyPrefix + key, defaultValue);
        }

        private static bool LoadBool(string key, bool defaultValue)
        {
            return EditorPrefs.GetBool(PrefKeyPrefix + key, defaultValue);
        }

        private static void SaveInt(string key, int value)
        {
            EditorPrefs.SetInt(PrefKeyPrefix + key, value);
        }

        private static void SaveFloat(string key, float value)
        {
            EditorPrefs.SetFloat(PrefKeyPrefix + key, value);
        }

        private static void SaveBool(string key, bool value)
        {
            EditorPrefs.SetBool(PrefKeyPrefix + key, value);
        }

        private static void ForceEditTimeRebuild(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var dca = go.GetComponent<DynamicCharacterAvatar>();
            if (dca != null)
            {
                if (!Application.isPlaying && dca.editorTimeGeneration)
                {
                    dca.ForceUpdate(true, true, true);
                }
                return;
            }

            var ud = go.GetComponent<UMAData>();
            if (ud != null)
            {
                ud.Dirty(true, true, true);
            }
        }

        private static int GetCurrentTriangleCount(UMASimpleLOD lod)
        {
            if (lod == null)
            {
                return -1;
            }

            var umaData = lod.GetComponent<UMAData>();
            if (umaData == null)
            {
                return -1;
            }

            var renderers = umaData.GetRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                return -1;
            }

            int totalTriangles = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null || smr.sharedMesh == null)
                {
                    continue;
                }

                var mesh = smr.sharedMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    totalTriangles += (int)mesh.GetIndexCount(sub) / 3;
                }
            }

            return totalTriangles;
        }

        private static void DrawCurrentLodStatusGrid(UMASimpleLOD lod)
        {
            if (lod == null)
                return;

            var statuses = lod.SlotLodStatuses;
            if (statuses == null || statuses.Count == 0)
            {
                EditorGUILayout.HelpBox("No LOD status data available. LOD has not been updated yet.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Current LOD Status", EditorStyles.boldLabel);

            // Header row
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Slot Name", EditorStyles.boldLabel, GUILayout.MinWidth(100));
            EditorGUILayout.LabelField("LODs", EditorStyles.boldLabel, GUILayout.Width(40));
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField("Calls", EditorStyles.boldLabel, GUILayout.Width(45));
            EditorGUILayout.LabelField("MS", EditorStyles.boldLabel, GUILayout.Width(55));
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel, GUILayout.Width(85));
            EditorGUILayout.EndHorizontal();

            foreach (var kvp in statuses)
            {
                var entry = kvp.Value;
                if (string.IsNullOrEmpty(entry.slotName))
                    continue;

                string status;
                if (entry.wasSuppressed)
                    status = "Suppressed";
                else if (entry.wasDroppedByMaxLod)
                    status = "Dropped";
                else if (!entry.hadAnyLOD)
                    status = "No LODs";
                else
                    status = "OK";

                string lodCountStr = entry.slotLodCount > 0 ? entry.slotLodCount.ToString() : "-";
                string levelStr = entry.hadAnyLOD ? entry.actualChosenLod.ToString() : "-";

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.slotName, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField(lodCountStr, GUILayout.Width(40));
                EditorGUILayout.LabelField(levelStr, GUILayout.Width(45));
                EditorGUILayout.LabelField(entry.count.ToString(), GUILayout.Width(45));
                EditorGUILayout.LabelField(entry.totalMS.ToString("F1"), GUILayout.Width(55));
                EditorGUILayout.LabelField(status, GUILayout.Width(85));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("Total Update MS: " + lod.TotalLodUpdateMS.ToString("F1"), EditorStyles.boldLabel);
        }

        private static string GetSlotKey(SlotData slot)
        {
            if (slot == null)
            {
                return string.Empty;
            }
            if (slot.asset == null)
            {
                return slot.slotName;
            }

            string path = AssetDatabase.GetAssetPath(slot.asset);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
            return slot.asset.GetUmaObjectId().ToString();
        }

        private static bool GetSlotSelected(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (_slotSelection.TryGetValue(key, out bool v))
            {
                return v;
            }
            _slotSelection[key] = false;
            return false;
        }

        private static void SetSlotSelected(string key, bool value)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }
            _slotSelection[key] = value;
        }

        private static void DrawInternalSlotLodSection(UMASimpleLOD lod)
        {
            _internalSlotLodFoldout = EditorGUILayout.Foldout(_internalSlotLodFoldout, "Generate LOD", true);
            if (!_internalSlotLodFoldout)
            {
                return;
            }
            //using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.HelpBox("Generate simplified LOD meshes for individual UMA slots. This modifies the slot assets directly, so it's recommended to back up your project or use version control before proceeding.", MessageType.Info);
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
                var umaData = lod != null ? lod.GetComponent<UMAData>() : null;
                var recipe = (umaData != null) ? umaData.umaRecipe : null;
                var slots = (recipe != null) ? recipe.slotDataList : null;

                if (slots == null || slots.Length == 0)
                {
                    EditorGUILayout.HelpBox("No UMA slots found. Generate the character once so UMAData.umaRecipe.slotDataList is available.", MessageType.Info);
                    GUIHelper.EndVerticalPadded();
                    return;
                }

                // Load persisted options
                int maxLodLevels = LoadInt("MaxLodLevels", 8);
                int minTriangles = LoadInt("MinTriangles", 256);
                float reduction = LoadFloat("TargetReductionPerLevel", 0.5f);
                bool preserveBorders = LoadBool("PreserveBoundaryEdges", true);
                float boundaryWeight = LoadFloat("BoundaryWeight", 10f);
                bool preserveVolume = LoadBool("PreserveVolume", true);
                float volumeWeight = LoadFloat("VolumeWeight", 1.0f);
                bool useUnityLodGenerator = LoadBool("UseUnityLodGenerator", false);

                _optionsFoldout = EditorGUILayout.Foldout(_optionsFoldout, "LOD Gen Options", true);
                if (_optionsFoldout)
                {
                    EditorGUI.BeginChangeCheck();
                    maxLodLevels = EditorGUILayout.IntSlider(new GUIContent("Max LOD Levels"), maxLodLevels, 1, 8);
                    useUnityLodGenerator = EditorGUILayout.Toggle(new GUIContent(
                        "Use Unity LOD Generator",
                        "When enabled, uses Unity's MeshLodUtility.GenerateMeshLods instead of UMA's internal reducer."),
                        useUnityLodGenerator);

                    using (new EditorGUI.DisabledScope(useUnityLodGenerator))
                    {
                        minTriangles = EditorGUILayout.IntField(new GUIContent("Min Triangles"), Mathf.Max(0, minTriangles));
                        reduction = EditorGUILayout.Slider(new GUIContent("Reduction Per Level"), reduction, 0.01f, 0.99f);
                        preserveBorders = EditorGUILayout.Toggle(new GUIContent("Preserve Boundary Edges"), preserveBorders);
                        boundaryWeight = EditorGUILayout.FloatField(new GUIContent("Boundary Weight"), Mathf.Max(0f, boundaryWeight));
                        preserveVolume = EditorGUILayout.Toggle(new GUIContent(
                            "Preserve Volume",
                            "When enabled, penalizes edge collapses that would flatten thin features like arms and fingers."),
                            preserveVolume);

                        using (new EditorGUI.DisabledScope(!preserveVolume))
                        {
                            volumeWeight = EditorGUILayout.Slider(new GUIContent(
                                "Volume Weight",
                                "How strongly to preserve volume. Higher values prevent more flattening but may reduce simplification quality."),
                                volumeWeight, 0.1f, 5.0f);
                        }
                    }
                    if (EditorGUI.EndChangeCheck())
                    {
                        SaveInt("MaxLodLevels", maxLodLevels);
                        SaveBool("UseUnityLodGenerator", useUnityLodGenerator);
                        SaveInt("MinTriangles", minTriangles);
                        SaveFloat("TargetReductionPerLevel", reduction);
                        SaveBool("PreserveBoundaryEdges", preserveBorders);
                        SaveFloat("BoundaryWeight", boundaryWeight);
                        SaveBool("PreserveVolume", preserveVolume);
                        SaveFloat("VolumeWeight", volumeWeight);
                    }
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Slots", EditorStyles.boldLabel);

                using (var scroll = new EditorGUILayout.ScrollViewScope(_slotScroll, GUILayout.MinHeight(140)))
                {
                    _slotScroll = scroll.scrollPosition;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var sd = slots[i];
                        if (sd == null)
                        {
                            continue;
                        }

                        string key = GetSlotKey(sd);
                        bool isChecked = GetSlotSelected(key);

                        string label = sd.slotName;
                        if (sd.asset != null)
                        {
                            label = label + "  [" + sd.asset.name + "]";
                        }

                        bool newChecked = EditorGUILayout.ToggleLeft(label, isChecked);
                        if (newChecked != isChecked)
                        {
                            SetSlotSelected(key, newChecked);
                        }
                    }
                }

                EditorGUILayout.Space(5);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button("Generate LOD for selected slots"))
                    {
                        var opts = new SlotLodGenerator.LodGenOptions();
                        opts.MaxLodLevels = maxLodLevels;
                        opts.MinTriangles = minTriangles;
                        opts.TargetReductionPerLevel = reduction;
                        opts.PreserveBoundaryEdges = preserveBorders;
                        opts.BoundaryWeight = boundaryWeight;
                        opts.PreserveVolume = preserveVolume;
                        opts.VolumeWeight = volumeWeight;
                        opts.useUnityLodGenerator = useUnityLodGenerator;

                        int changed = 0;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            var sd = slots[i];
                            if (sd == null || sd.asset == null)
                            {
                                continue;
                            }

                            string key = GetSlotKey(sd);
                            if (!GetSlotSelected(key))
                            {
                                continue;
                            }

                            bool did = SlotLodGenerator.GenerateAndApplyLods(sd.asset, opts);
                            if (did)
                            {
                                changed++;
                            }
                        }

                        if (changed > 0)
                        {
                            AssetDatabase.SaveAssets();
                            lod.UpdateInternalLOD();
                            ForceEditTimeRebuild(lod.gameObject);
                            EditorUtility.DisplayDialog("Internal Slot LOD", "Regenerated internal LODs for " + changed + " slot(s).", "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Internal Slot LOD", "No slots were updated. (Either none selected, or slots were already below Min Triangles.)", "OK");
                        }
                    }
                }
                GUIHelper.EndVerticalPadded();
            }
        }

        private static void DrawSlotBasedLodSection(UMASimpleLOD lod)
        {
            if (lod == null)
            {
                return;
            }

            bool hasSlotBasedLod = lod.swapSlots || lod.useSlotDropping;
            if (!hasSlotBasedLod)
            {
                return;
            }

            EditorGUILayout.Space(5);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Slot-Based LOD", EditorStyles.boldLabel);

                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Mode", lod.swapSlots ? "Swap Slots" : "Slot Dropping");
                EditorGUILayout.LabelField("Current LOD Level", lod.CurrentLOD.ToString());
                if (lod.lodOffset != 0)
                {
                    EditorGUILayout.LabelField("LOD Offset", lod.lodOffset.ToString());
                }
                EditorGUI.indentLevel--;

                var umaData = lod.GetComponent<UMAData>();
                var recipe = (umaData != null) ? umaData.umaRecipe : null;
                var slots = (recipe != null) ? recipe.slotDataList : null;

                if (slots == null || slots.Length == 0)
                {
                    EditorGUILayout.HelpBox("No slot data available yet. Build the character to see slot LOD info.", MessageType.Info);
                    return;
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Equipped Slots", EditorStyles.boldLabel);

                int lodVariantCount = 0;
                int baseSlotCount = 0;
                for (int i = 0; i < slots.Length; i++)
                {
                    var sd = slots[i];
                    if (sd == null)
                    {
                        continue;
                    }

                    string slotName = sd.slotName;
                    bool isLodVariant = slotName.IndexOf("_LOD", StringComparison.Ordinal) >= 0;

                    EditorGUILayout.BeginHorizontal();
                    if (sd.Suppressed)
                    {
                        GUI.color = Color.gray;
                    }
                    EditorGUILayout.LabelField(slotName, EditorStyles.wordWrappedLabel);
                    if (isLodVariant)
                    {
                        EditorGUILayout.LabelField("(LOD variant)", EditorStyles.miniLabel, GUILayout.Width(80));
                        lodVariantCount++;
                    }
                    else
                    {
                        EditorGUILayout.LabelField("(base)", EditorStyles.miniLabel, GUILayout.Width(50));
                        baseSlotCount++;
                    }
                    if (sd.Suppressed)
                    {
                        EditorGUILayout.LabelField("[dropped]", EditorStyles.miniLabel, GUILayout.Width(60));
                        GUI.color = Color.white;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(
                    string.Format("LOD variants: {0}  |  Base slots: {1}  |  Total: {2}", lodVariantCount, baseSlotCount, slots.Length),
                    EditorStyles.miniLabel);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var lod = (UMASimpleLOD)target;

            if (!inspectorView.DrawSelector())
            {
                DrawStandardInspector(lod);
                return;
            }

            DrawAdvancedInspector(lod);
        }

        private void DrawStandardInspector(UMASimpleLOD lod)
        {
            using (inspectorView.Section("Distance & levels",
                "First LOD Distance is the camera distance where LOD 0 changes to LOD 1. Distance Multiplier expands each following threshold cumulatively. Maximum LOD Levels is the number of usable levels, starting at 0. LOD Offset shifts slot-variant lookup when a character should begin at a coarser or finer prepared level."))
            {
                inspectorView.Field(serializedObject, "lodDistance", "First LOD Distance");
                inspectorView.Field(serializedObject, "distanceMultiplier", "Distance Multiplier");
                inspectorView.Field(serializedObject, "maxLOD", "Maximum LOD Levels");
                inspectorView.Field(serializedObject, "lodOffset", "LOD Offset");
                EditorGUILayout.LabelField(
                    "Thresholds grow cumulatively: first distance, then distance × multiplier for each following level.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            using (inspectorView.Section("Geometry & textures",
                "Use Prepared Mesh LODs swaps precomputed triangle data without replacing slots. Swap LOD Slots looks for slot names ending in _LOD# instead. Drop Slots by Maximum LOD removes slots after their Last Visible LOD. Reduce Atlas Resolution lowers generated texture resolution at distant levels, while Maximum Atlas Reduction Divisor sets the smallest permitted scale. Normally choose either prepared mesh LODs or slot swapping as the geometry strategy."))
            {
                inspectorView.Field(serializedObject, "useInternalMeshLOD", "Use Prepared Mesh LODs");
                inspectorView.Field(serializedObject, "swapSlots", "Swap LOD Slots");
                inspectorView.Field(serializedObject, "useSlotDropping", "Drop Slots by Maximum LOD");
                inspectorView.Field(serializedObject, "useTextureResize", "Reduce Atlas Resolution");
                using (new EditorGUI.DisabledScope(!inspectorView.Property(serializedObject, "useTextureResize").boolValue))
                    inspectorView.Field(serializedObject, "maxReduction", "Maximum Atlas Reduction Divisor");
                if (inspectorView.Property(serializedObject, "useTextureResize").boolValue)
                    EditorGUILayout.LabelField(
                        "For example, 8 prevents the generated atlas from shrinking below one eighth of its original size.",
                        EditorStyles.wordWrappedMiniLabel);

                if (inspectorView.Property(serializedObject, "useInternalMeshLOD").boolValue &&
                    inspectorView.Property(serializedObject, "swapSlots").boolValue)
                    EditorGUILayout.HelpBox(
                        "Prepared mesh LODs and slot swapping are both enabled. Swapping a slot can leave its prepared mesh LOD out of sync; normally choose one geometry strategy.",
                        MessageType.Warning);
            }

            using (inspectorView.Section("Stability & update timing",
                "Percentage Hysteresis uses a percentage of the threshold as a buffer; when disabled, World-Space Buffer uses a fixed distance. The buffer prevents rapid level switching near a boundary. Minimum Check Interval limits how often this component evaluates distance, and Random Check Stagger spreads many characters across frames. Disable Automatic Checks requires your code to request LOD checks manually."))
            {
                inspectorView.Field(serializedObject, "UsePercentageBuffer", "Percentage Hysteresis");
                if (inspectorView.Property(serializedObject, "UsePercentageBuffer").boolValue)
                    inspectorView.Field(serializedObject, "BufferPercent", "Threshold Buffer");
                else
                    inspectorView.Field(serializedObject, "BufferZone", "World-Space Buffer");
                inspectorView.Field(serializedObject, "MinCheck", "Minimum Check Interval");
                inspectorView.Field(serializedObject, "CheckRange", "Random Check Stagger");
                inspectorView.Field(serializedObject, "disableAutomatedProcessing", "Disable Automatic Checks");
            }

            using (inspectorView.Section("Runtime feature reduction",
                "The three Disable At LOD values stop bone animators, UMA expressions, or dynamic expressions at and beyond the selected level; -1 leaves that feature unmanaged. Additional LOD-Tuned Components receive level changes so other systems can reduce their own cost with the character."))
            {
                inspectorView.Field(serializedObject, "disableBoneAnimatorsAtLOD", "Disable Bone Animators at LOD");
                inspectorView.Field(serializedObject, "disableUMAExpressionPlayerAtLOD", "Disable UMA Expressions at LOD");
                inspectorView.Field(serializedObject, "disableDynamicExpressionPlayerAtLOD", "Disable Dynamic Expressions at LOD");
                inspectorView.Field(serializedObject, "lodTunings", "Additional LOD-Tuned Components");
                EditorGUILayout.LabelField("Use -1 to leave a feature unmanaged.", EditorStyles.wordWrappedMiniLabel);
            }

            if (!Application.isPlaying)
                DrawStandardEditTimePreview(lod);
            else
                DrawStandardRuntimeStatus(lod);

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.LabelField(
                "Slot LOD generation, equipped-slot details, and per-slot timing diagnostics are in Advanced View.",
                EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawStandardEditTimePreview(UMASimpleLOD lod)
        {
            using (inspectorView.Section("Edit-time preview",
                "Override LOD in Editor enables a forced scene-view preview. Preview LOD Level rebuilds or swaps the character to that exact level so you can inspect silhouettes, slots, and triangle counts without entering Play Mode. Disable the override to return control to normal distance-based behavior."))
            {
                var overrideProperty = inspectorView.Property(serializedObject, "editorOverrideLOD");
                EditorGUILayout.PropertyField(overrideProperty, UMAInspectorView.Label("Override LOD in Editor"));
                using (new EditorGUI.DisabledScope(!overrideProperty.boolValue))
                {
                    int max = Mathf.Max(1, inspectorView.Property(serializedObject, "maxLOD").intValue);
                    var forcedProperty = inspectorView.Property(serializedObject, "editorForcedLOD");
                    int current = Mathf.Clamp(forcedProperty.intValue, 0, max - 1);
                    EditorGUI.BeginChangeCheck();
                    int desired = EditorGUILayout.IntSlider(UMAInspectorView.Label("Preview LOD Level"), current, 0, max - 1);
                    if (EditorGUI.EndChangeCheck())
                    {
                        forcedProperty.intValue = desired;
                        serializedObject.ApplyModifiedProperties();
                        lod.DoManualLODCheck(desired);
                        if (lod.useInternalMeshLOD) lod.UpdateInternalLOD();
                        else ForceEditTimeRebuild(lod.gameObject);
                        EditorUtility.SetDirty(lod);
                        serializedObject.Update();
                    }
                    int triangleCount = GetCurrentTriangleCount(lod);
                    if (triangleCount >= 0) EditorGUILayout.LabelField("Current Triangles", triangleCount.ToString("N0"));
                }
            }
        }

        private void DrawStandardRuntimeStatus(UMASimpleLOD lod)
        {
            using (inspectorView.Section("Current runtime status",
                "Current LOD is the level selected at runtime. Current Triangles is the combined visible skinned-mesh triangle count when it can be measured. Accumulated LOD Update Time is the total time this component has spent applying LOD changes, useful for profiling rather than a per-frame cost."))
            {
                EditorGUILayout.LabelField("Current LOD", lod.CurrentLOD.ToString());
                int triangleCount = GetCurrentTriangleCount(lod);
                if (triangleCount >= 0) EditorGUILayout.LabelField("Current Triangles", triangleCount.ToString("N0"));
                EditorGUILayout.LabelField("Accumulated LOD Update Time", lod.TotalLodUpdateMS.ToString("F1") + " ms");
            }
        }

        private void DrawAdvancedInspector(UMASimpleLOD lod)
        {

            DrawInternalSlotLodSection(lod);
            DrawSlotBasedLodSection(lod);

            if (!Application.isPlaying)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Edit-time LOD", EditorStyles.boldLabel);

                    lod.editorOverrideLOD = EditorGUILayout.Toggle(new GUIContent(
                        "Editor Override LOD",
                        "When enabled, you can force a specific LOD level at edit time."),
                        lod.editorOverrideLOD);

                            using (new EditorGUI.DisabledScope(!lod.editorOverrideLOD))
                            {
                                int max = lod.maxLOD;
                                if (max < 1)
                                {
                                    max = 1;
                                }

                                int currentForced = Mathf.Clamp(lod.editorForcedLOD, 0, max - 1);
                                int desired = EditorGUILayout.IntSlider(new GUIContent(
                                    "Current LOD Level",
                                    "Forces the UMA to rebuild at the selected LOD level (edit time)."),
                                    currentForced,
                                    0,
                                    max - 1);

                                // Display current triangle count
                                int triangleCount = GetCurrentTriangleCount(lod);
                                if (triangleCount >= 0)
                                {
                                    EditorGUILayout.LabelField("Triangle Count", triangleCount.ToString("N0"));
                                }

                                if (desired != currentForced)
                                {

                                    lod.editorForcedLOD = desired;
                                    lod.DoManualLODCheck(desired);
                                    if (lod.useInternalMeshLOD)
                                    {
                                        lod.UpdateInternalLOD();
                                        return;
                                    }
                                    EditorUtility.SetDirty(lod);
                                    ForceEditTimeRebuild(lod.gameObject);
                                }
                            }
                        }
                    }

                    if(Application.isPlaying)
                    {
                        EditorGUILayout.LabelField("Current LOD", lod.CurrentLOD.ToString());

                        // Display current triangle count in play mode too
                        int playModeTriCount = GetCurrentTriangleCount(lod);
                        if (playModeTriCount >= 0)
                        {
                            EditorGUILayout.LabelField("Triangle Count", playModeTriCount.ToString("N0"));
                        }

                        EditorGUILayout.Space(5);
                        DrawCurrentLodStatusGrid(lod);
                    }

            DrawDefaultInspector();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
