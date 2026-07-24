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
                Draw("meshCombiner");
            }

            serializedObject.ApplyModifiedProperties();
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
