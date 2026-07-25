using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAGeneratorOverride))]
    public class UMAGeneratorOverrideEditor : Editor
    {
        private static bool showAtlasSettings = true;
        private static bool showConversionSettings = true;
        private static bool showGenerationSettings = true;
        private static bool showRuntimeTuningSettings;
        private static bool showEditTimeSettings;
        private static bool showAdvancedSettings;

        private readonly GUIContent[] atlasLabels =
        {
            new GUIContent("512"),
            new GUIContent("1024"),
            new GUIContent("2048"),
            new GUIContent("4096"),
            new GUIContent("8192")
        };

        private readonly int[] atlasValues = { 512, 1024, 2048, 4096, 8192 };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUIStyle centeredLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            showAtlasSettings = EditorGUILayout.Foldout(showAtlasSettings, "Atlas Settings");
            if (showAtlasSettings)
            {
                EditorGUILayout.LabelField("Basic Configuration", centeredLabel);
                GUIHelper.BeginVerticalPadded();
                Draw("fitAtlas");
                Draw("SharperFitTextures");
                Draw("AtlasOverflowFitMethod");
                Draw("FitPercentageDecrease");
                Draw("convertMipMaps");
                EditorGUILayout.IntPopup(serializedObject.FindProperty("atlasResolution"), atlasLabels, atlasValues);
                GUIHelper.EndVerticalPadded();
            }

            showConversionSettings = EditorGUILayout.Foldout(showConversionSettings, "Conversion Settings");
            if (showConversionSettings)
            {
                GUIHelper.BeginVerticalPadded();
                EditorGUILayout.HelpBox(
                    "Convert RenderTextures to Texture2D. This creates a Texture2D that can be modified or saved.\n" +
                    "Use Async Conversion to copy without a GPU stall.",
                    MessageType.None);
                Draw("convertRenderTexture");
                Draw("useAsyncConversion");
                Draw("asyncMipRegen");
                GUIHelper.EndVerticalPadded();
            }

            showGenerationSettings = EditorGUILayout.Foldout(showGenerationSettings, "Generation Settings");
            if (showGenerationSettings)
            {
                Draw("MaxQueuedConversionsPerFrame");
                Draw("InitialScaleFactor");
                Draw("IterationCount");
                Draw("InterFrameDelay");
                Draw("MaxMultiStepWorkMilliseconds", "Max Multi-Step Work (ms)");
                Draw("collectGarbage");
                Draw("garbageCollectionRate");
                Draw("processAllPending");
                Draw("SaveAndRestoreIgnoredItems");
                Draw("showInHierarchy");
            }

            showRuntimeTuningSettings = EditorGUILayout.Foldout(showRuntimeTuningSettings, "Runtime Tuning Settings");
            if (showRuntimeTuningSettings)
            {
                EditorGUILayout.HelpBox(
                    "Automatic scaling options to help manage memory usage on constrained devices.",
                    MessageType.None);
                Draw("AutomaticScaling");
                Draw("ScaleGPUMemoryCutoffMB", "GPU Memory Cutoff (MB)");
                Draw("ScaleSystemMemoryCutoffMB", "System Memory Cutoff (MB)");
            }

            showEditTimeSettings = EditorGUILayout.Foldout(showEditTimeSettings, "Edit Time Settings");
            if (showEditTimeSettings)
            {
                EditorGUILayout.HelpBox(
                    "Edit time generation options. Keep the atlas size down and the scale factor high to reduce scene-file memory usage.",
                    MessageType.None);
                Draw("editorAtlasResolution");
                Draw("editorInitialScaleFactor");
            }

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings");
            if (showAdvancedSettings)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.LabelField("Advanced Configuration", centeredLabel);
                EditorGUILayout.HelpBox(
                    "Use Apply Inline when converted RenderTextures should be applied immediately on the current platform.",
                    MessageType.None);
                Draw("applyInline");
                EditorGUILayout.HelpBox(
                    "The default renderer asset supplies rendering parameters when the character, slot, and renderer manager do not specify one.",
                    MessageType.None);
                Draw("defaultRendererAsset");
                EditorGUILayout.HelpBox(
                    "The default overlay asset is used when a slot has no overlay. This is intended primarily for testing.",
                    MessageType.None);
                Draw("defaultOverlayAsset");
                Draw("alwaysRegenerateRenderers", "Always Regenerate Renderers");
                Draw("Use32BitBuffers");
                Draw("showInHierarchy");
                Draw("textureMerge");
                DrawMeshCombinerPicker();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMeshCombinerPicker()
        {
            SerializedProperty property =
                serializedObject.FindProperty("meshCombiner");
            if (property == null)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects ||
                property.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(
                    property,
                    new GUIContent("Mesh Combiner"));
                return;
            }

            UMAGeneratorOverride generatorOverride =
                target as UMAGeneratorOverride;
            UMAMeshCombiner[] attachedCombiners =
                GetAttachedMeshCombiners(generatorOverride);

            if (attachedCombiners.Length == 0)
            {
                EditorGUILayout.PropertyField(
                    property,
                    new GUIContent("Mesh Combiner"));
                EditorGUILayout.HelpBox(
                    "Attach one or more UMAMeshCombiner components to this GameObject " +
                    "to select them here.",
                    MessageType.Info);
                return;
            }

            var choices = new List<UMAMeshCombiner>();
            var labels = new List<GUIContent>();

            choices.Add(null);
            labels.Add(
                new GUIContent(
                    "None (Keep Generator's Current)",
                    "Do not override the generator's current Mesh Combiner."));

            UMAMeshCombiner current =
                property.objectReferenceValue as UMAMeshCombiner;
            bool currentIsAttached = false;
            for (int i = 0; i < attachedCombiners.Length; i++)
            {
                if (attachedCombiners[i] == current)
                {
                    currentIsAttached = true;
                    break;
                }
            }

            // Preserve and display an existing external assignment until the user
            // deliberately chooses an attached component or None.
            if (current != null && !currentIsAttached)
            {
                choices.Add(current);
                labels.Add(
                    new GUIContent(
                        "External: " + GetCombinerLabel(current),
                        "This Mesh Combiner is not attached to the current GameObject."));
            }

            for (int i = 0; i < attachedCombiners.Length; i++)
            {
                UMAMeshCombiner combiner = attachedCombiners[i];
                choices.Add(combiner);
                labels.Add(
                    new GUIContent(
                        GetCombinerLabel(combiner, attachedCombiners, i),
                        "Use this attached Mesh Combiner for the generator override."));
            }

            int selectedIndex = choices.IndexOf(current);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            EditorGUI.BeginChangeCheck();
            selectedIndex = EditorGUILayout.Popup(
                new GUIContent(
                    "Mesh Combiner",
                    "Select a Mesh Combiner attached to this GameObject."),
                selectedIndex,
                labels.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                property.objectReferenceValue = choices[selectedIndex];
            }
        }

        internal static UMAMeshCombiner[] GetAttachedMeshCombiners(
            UMAGeneratorOverride generatorOverride)
        {
            return generatorOverride != null
                ? generatorOverride.GetComponents<UMAMeshCombiner>()
                : new UMAMeshCombiner[0];
        }

        private static string GetCombinerLabel(UMAMeshCombiner combiner)
        {
            return combiner == null
                ? "Missing Mesh Combiner"
                : ObjectNames.NicifyVariableName(combiner.GetType().Name);
        }

        private static string GetCombinerLabel(
            UMAMeshCombiner combiner,
            UMAMeshCombiner[] attachedCombiners,
            int combinerIndex)
        {
            string label = GetCombinerLabel(combiner);
            int duplicateNumber = 1;
            bool hasDuplicate = false;

            for (int i = 0; i < attachedCombiners.Length; i++)
            {
                if (attachedCombiners[i] == null ||
                    attachedCombiners[i].GetType() != combiner.GetType())
                {
                    continue;
                }

                if (i < combinerIndex)
                {
                    duplicateNumber++;
                }
                else if (i > combinerIndex)
                {
                    hasDuplicate = true;
                }
            }

            return hasDuplicate || duplicateNumber > 1
                ? label + " (" + duplicateNumber + ")"
                : label;
        }

        private void Draw(string propertyName, string explicitLabel = null)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(explicitLabel))
            {
                EditorGUILayout.PropertyField(property);
            }
            else
            {
                EditorGUILayout.PropertyField(property, new GUIContent(explicitLabel));
            }
        }
    }
}
