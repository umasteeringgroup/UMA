#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(UMAClothingConformer))]
    public class UMAClothingConformerEditor : Editor
    {
        private const string DefaultFolder = "Assets/UMA/ClothingConformer";
        private string saveFolder = DefaultFolder;
        private string blendshapeName = "Conformed";
        private Vector2 slotsScroll;

        public override void OnInspectorGUI()
        {
            UMAClothingConformer conformer = (UMAClothingConformer)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("umaAvatar"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("umaData"));
            if (GUILayout.Button("Auto Detect UMA"))
            {
                Undo.RecordObject(conformer, "Auto detect UMA clothing conformer target");
                conformer.AutoDetectUMA();
                EditorUtility.SetDirty(conformer);
            }

            DrawSlotSelection(conformer);
            DrawConformerSettings(serializedObject.FindProperty("settings"));
            // Make newly edited settings available to a button pressed in this same inspector event.
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginChangeCheck();
            bool preview = EditorGUILayout.Toggle(new GUIContent("Preview", "Apply the current conform result through UMA vertex overrides."), conformer.preview);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(conformer, "Toggle UMA clothing conformer preview");
                conformer.SetPreview(preview);
                EditorUtility.SetDirty(conformer);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(conformer.selectedSlotNames == null || conformer.selectedSlotNames.Count == 0))
            {
                if (GUILayout.Button("Bind Selected Slots"))
                {
                    Undo.RegisterCompleteObjectUndo(conformer, "Bind UMA clothing slots");
                    RunWithProgress("Bind UMA Clothing", conformer.BindSelectedSlots);
                    if (conformer.bindDataAssets != null && conformer.bindDataAssets.Count > 0)
                        conformer.SaveBindDataAssets(saveFolder);
                    EditorUtility.SetDirty(conformer);
                }
            }

            using (new EditorGUI.DisabledScope(conformer.bindDataAssets == null || conformer.bindDataAssets.Count == 0))
            {
                if (GUILayout.Button("Rebind Selected Slots"))
                {
                    Undo.RegisterCompleteObjectUndo(conformer, "Rebind UMA clothing slots");
                    RunWithProgress("Rebind UMA Clothing", conformer.BindSelectedSlots);
                    if (conformer.bindDataAssets != null && conformer.bindDataAssets.Count > 0)
                        conformer.SaveBindDataAssets(saveFolder);
                    EditorUtility.SetDirty(conformer);
                }
                if (GUILayout.Button("Conform Selected Slots"))
                {
                    Undo.RecordObject(conformer, "Conform UMA clothing slots");
                    RunWithProgress("Conform UMA Clothing", conformer.ConformSelectedSlots);
                    EditorUtility.SetDirty(conformer);
                }
            }

            if (GUILayout.Button("Revert Preview to Original Mesh"))
            {
                Undo.RecordObject(conformer, "Revert UMA clothing conformer");
                conformer.RevertChanges();
                EditorUtility.SetDirty(conformer);
            }

            DrawSaveSection(conformer);
            DrawStatus(conformer);
        }

        private static void DrawConformerSettings(SerializedProperty settings)
        {
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Unable to serialize Clothing Conformer settings.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Conformer Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Binding (takes effect on Bind or Rebind)", EditorStyles.miniBoldLabel);
            DrawSetting(settings, "maxSearchRadius", "Search Radius", "Candidate triangle search radius around each clothing vertex.");
            DrawSetting(settings, "maxTriangleDistance", "Max Triangle Distance", "Vertices farther than this from the body surface are left unbound.");
            DrawSetting(settings, "useUnselectedSlotsAsBase", "Use Unselected Slots as Base", "When no body surface is checked, use all active non-clothing slots.");

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Conform", EditorStyles.miniBoldLabel);
            DrawSetting(settings, "additionalNormalOffset", "Additional Normal Offset", "Positive moves clothing outward on its original bound side; negative moves it inward.");

            SerializedProperty collision = settings.FindPropertyRelative("enableCollisionCorrection");
            if (collision != null)
            {
                EditorGUILayout.PropertyField(collision, new GUIContent("Collision Correction", "Pushes conforming vertices back outside the mapped body surface."));
                if (collision.boolValue)
                {
                    EditorGUI.indentLevel++;
                    DrawSetting(settings, "normalOffsetEpsilon", "Surface Epsilon", "Minimum allowed distance outside the body surface.");
                    DrawSetting(settings, "collisionPushDistance", "Push Distance", "Extra outward push applied when a vertex is inside the surface.");
                    DrawSetting(settings, "maxCollisionDisplacement", "Max Push Distance", "Caps a single collision-correction move.");
                    EditorGUI.indentLevel--;
                }
            }

            SerializedProperty smoothing = settings.FindPropertyRelative("enableSmoothing");
            if (smoothing != null)
            {
                EditorGUILayout.PropertyField(smoothing, new GUIContent("Smoothing", "Smooths the resulting clothing positions without changing UV topology."));
                if (smoothing.boolValue)
                {
                    EditorGUI.indentLevel++;
                    DrawSetting(settings, "smoothingAlgorithm", "Algorithm", "Laplacian is simple, Taubin reduces shrinkage, and HC preserves volume best.");
                    DrawSetting(settings, "smoothingIterations", "Iterations", "Number of smoothing passes.");
                    DrawSetting(settings, "smoothingStrength", "Strength", "Per-pass smoothing amount.");
                    DrawSetting(settings, "smoothOnlyMovedVertices", "Only Moved Vertices", "Leaves unmoved clothing vertices untouched.");
                    SerializedProperty algorithm = settings.FindPropertyRelative("smoothingAlgorithm");
                    if (algorithm != null && algorithm.enumValueIndex == (int)SmoothingAlgorithm.HC)
                    {
                        DrawSetting(settings, "hcAlpha", "HC Alpha", "HC correction weight toward the original shape.");
                        DrawSetting(settings, "hcBeta", "HC Beta", "HC neighbor-correction weight.");
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.LabelField("Seams and Preview", EditorStyles.miniBoldLabel);
            DrawSetting(settings, "preserveWeldedSeams", "Preserve Welded Seams", "Keeps UV-split vertices from drifting apart during conforming.");
            SerializedProperty weldSeams = settings.FindPropertyRelative("preserveWeldedSeams");
            if (weldSeams != null && weldSeams.boolValue)
            {
                EditorGUI.indentLevel++;
                DrawSetting(settings, "weldedSeamTolerance", "Welded Seam Tolerance", "Maximum original-space seam separation to stitch.");
                EditorGUI.indentLevel--;
            }
            DrawSetting(settings, "livePreview", "Live Preview", "Conform automatically when body blendshape or skeleton state changes.");

            EditorGUILayout.EndVertical();
        }

        private static void DrawSetting(SerializedProperty settings, string name, string label, string tooltip)
        {
            SerializedProperty property = settings.FindPropertyRelative(name);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        }

        private void DrawSlotSelection(UMAClothingConformer conformer)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active UMA Slots", EditorStyles.boldLabel);
            List<SlotData> slots = conformer.GetActiveSlots();
            if (slots.Count == 0)
            {
                EditorGUILayout.HelpBox("Build the UMA first. The conformer lists slots from the generated avatar.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Conform", "Body Surface");
            slotsScroll = EditorGUILayout.BeginScrollView(slotsScroll, GUILayout.MaxHeight(180));
            for (int i = 0; i < slots.Count; i++)
            {
                SlotData slot = slots[i];
                string slotName = slot.slotName;
                EditorGUILayout.BeginHorizontal();
                bool selected = conformer.selectedSlotNames.Contains(slotName);
                bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(50));
                EditorGUILayout.LabelField(slotName);
                using (new EditorGUI.DisabledScope(newSelected))
                {
                    bool baseSelected = conformer.baseSlotNames.Contains(slotName);
                    bool newBaseSelected = EditorGUILayout.Toggle(baseSelected, GUILayout.Width(82));
                    if (newBaseSelected != baseSelected)
                        SetListMembership(conformer.baseSlotNames, slotName, newBaseSelected);
                }
                EditorGUILayout.EndHorizontal();
                if (newSelected != selected)
                    SetListMembership(conformer.selectedSlotNames, slotName, newSelected);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox("Leave Body Surface unchecked to bind against every active slot not selected for conforming. Explicit body slots are useful when hair, accessories, or other clothing should not affect the mapping.", MessageType.None);
        }

        private void DrawSaveSection(UMAClothingConformer conformer)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Save", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            saveFolder = EditorGUILayout.TextField("Assets Folder", saveFolder);
            if (GUILayout.Button("Choose", GUILayout.Width(64)))
            {
                string selected = EditorUtility.OpenFolderPanel("Choose UMA Clothing Conformer Asset Folder", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    string relative = FileUtil.GetProjectRelativePath(selected);
                    if (!string.IsNullOrEmpty(relative)) saveFolder = relative;
                    else EditorUtility.DisplayDialog("UMA Clothing Conformer", "Choose a folder under this project's Assets directory.", "OK");
                }
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(!conformer.HasConformedResults))
            {
                if (GUILayout.Button("Save as New SlotDataAsset"))
                    conformer.SaveConformedSlotsAsNewAssets(saveFolder);

                blendshapeName = EditorGUILayout.TextField("Blendshape Name", blendshapeName);
                if (GUILayout.Button("Save to Blendshape on Current SlotDataAsset"))
                {
                    Undo.RegisterCompleteObjectUndo(conformer, "Save UMA conformer blendshape");
                    conformer.SaveConformedSlotsToBlendshape(blendshapeName, saveFolder);
                }
            }
        }

        private static void DrawStatus(UMAClothingConformer conformer)
        {
            if (string.IsNullOrEmpty(conformer.LastStatus)) return;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(conformer.LastStatus, MessageType.None);
            if (conformer.UnboundVertexPositions.Count > 0)
                EditorGUILayout.HelpBox(conformer.UnboundVertexPositions.Count + " vertices could not be bound. Select the component to inspect them in magenta in the Scene view, then increase radius or choose a different body surface.", MessageType.Warning);
        }

        private static void RunWithProgress(string title, Func<Func<float, string, bool>, bool> operation)
        {
            try
            {
                operation((progress, message) => EditorUtility.DisplayCancelableProgressBar(title, message, progress));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void SetListMembership(List<string> list, string value, bool contains)
        {
            if (contains)
            {
                if (!list.Contains(value)) list.Add(value);
            }
            else
            {
                list.Remove(value);
            }
        }
    }

    internal static class UMAClothingConformerMenu
    {
        [MenuItem("UMA/Avatar/Clothing Conformer/Add To Selected UMA", priority = 116)]
        private static void AddToSelectedUMA()
        {
            GameObject selected = Selection.activeGameObject;
            UMAData data = selected != null ? selected.GetComponentInParent<UMAData>() : null;
            if (data == null)
            {
                EditorUtility.DisplayDialog("UMA Clothing Conformer", "Select a fully built UMA GameObject first.", "OK");
                return;
            }
            UMAClothingConformer conformer = data.GetComponent<UMAClothingConformer>();
            if (conformer == null) conformer = Undo.AddComponent<UMAClothingConformer>(data.gameObject);
            conformer.AutoDetectUMA();
            Selection.activeGameObject = data.gameObject;
            EditorUtility.SetDirty(conformer);
        }

        [MenuItem("UMA/Avatar/Clothing Conformer/Demo Bind First Clothing Slot On Selected UMA", priority = 117)]
        private static void BindFirstClothingSlotDemo()
        {
            GameObject selected = Selection.activeGameObject;
            UMAData data = selected != null ? selected.GetComponentInParent<UMAData>() : null;
            if (data == null)
            {
                EditorUtility.DisplayDialog("UMA Clothing Conformer Demo", "Select a fully built UMA with a body and at least one clothing slot.", "OK");
                return;
            }
            UMAClothingConformer conformer = data.GetComponent<UMAClothingConformer>();
            if (conformer == null) conformer = Undo.AddComponent<UMAClothingConformer>(data.gameObject);
            conformer.AutoDetectUMA();
            List<SlotData> slots = conformer.GetActiveSlots();
            if (slots.Count < 2)
            {
                EditorUtility.DisplayDialog("UMA Clothing Conformer Demo", "The demo needs at least two active slots: a base body and clothing.", "OK");
                return;
            }
            Undo.RecordObject(conformer, "Configure UMA clothing conformer demo");
            conformer.baseSlotNames.Clear();
            conformer.selectedSlotNames.Clear();
            conformer.baseSlotNames.Add(slots[0].slotName);
            conformer.selectedSlotNames.Add(slots[1].slotName);
            try
            {
                conformer.BindSelectedSlots((progress, message) => EditorUtility.DisplayCancelableProgressBar("UMA Clothing Conformer Demo", message, progress));
                conformer.ConformSelectedSlots((progress, message) => EditorUtility.DisplayCancelableProgressBar("UMA Clothing Conformer Demo", message, progress));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Debug.Log("[UMAClothingConformer] Demo completed. Change a body blendshape, then press Conform Selected Slots and Save as New SlotDataAsset in the component inspector.", conformer);
            Selection.activeGameObject = data.gameObject;
        }
    }
}
#endif
