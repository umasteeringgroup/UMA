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

        private static bool _internalSlotLodFoldout;
        private static bool _optionsFoldout = true;
        private static Vector2 _slotScroll;
        private static Dictionary<string, bool> _slotSelection = new Dictionary<string, bool>(64);

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
            return slot.asset.GetEntityId().ToString();
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

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var lod = (UMASimpleLOD)target;

            DrawInternalSlotLodSection(lod);

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

            if (Application.isPlaying)
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
