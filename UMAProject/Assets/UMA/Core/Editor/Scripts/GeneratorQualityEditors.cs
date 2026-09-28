using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(GeneratorQualityProfile)), CanEditMultipleObjects]
    public sealed class GeneratorQualityProfileEditor : Editor
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(GeneratorQualityProfile));
        private static readonly string[][] Groups =
        {
            new[] { "Atlas", "fitAtlas", "SharperFitTextures", "AtlasOverflowFitMethod", "FitPercentageDecrease", "convertMipMaps", "atlasResolution", "enableSourceUVCropping", "sourceUVCropPadding" },
            new[] { "Conversion", "convertRenderTexture", "useAsyncConversion", "asyncMipRegen" },
            new[] { "Scheduling", "MaxQueuedConversionsPerFrame", "IterationCount", "InterFrameDelay", "MaxMultiStepWorkMilliseconds", "collectGarbage", "garbageCollectionRate", "processAllPending", "applyInline" },
            new[] { "Memory", "InitialScaleFactor", "AutomaticScaling", "ScaleGPUMemoryCutoffMB", "ScaleSystemMemoryCutoffMB" },
            new[] { "Editor preview", "editorAtlasResolution", "editorInitialScaleFactor" },
            new[] { "Advanced", "SaveAndRestoreIgnoredItems", "CleanupUnusedBones", "MeasureBoneCleanup", "showInHierarchy", "defaultRendererAsset", "defaultOverlayAsset", "alwaysRegenerateRenderers", "Use32BitBuffers", "textureMerge", "meshCombiner" },
            new[] { "Generated textures", "textures.maximumDimension", "textures.mipMaps", "textures.filterMode", "textures.anisotropy", "textures.mipBias", "textures.compression" },
            new[] { "Character detail, reuse & rendering", "characters.reuseMeshes", "characters.reuseTextures", "characters.atlasScale", "characters.lodDistance", "characters.lodDistanceMultiplier", "characters.lodOffset", "characters.maxLOD", "characters.slotDropping", "characters.resizeLODTextures", "characters.swapLODSlots", "characters.internalMeshLOD", "characters.disableBoneAnimatorsAtLOD", "characters.disableExpressionsAtLOD", "characters.disableDynamicExpressionsAtLOD", "characters.markMeshNotReadable", "characters.ignoreBlendShapes", "characters.loadBlendShapeNormals", "characters.loadBlendShapeTangents", "characters.loadAllBlendShapeFrames", "characters.castShadows", "characters.receiveShadows", "characters.skinnedMotionVectors", "characters.skinWeights", "characters.overrideExplicitRendererSettings" }
        };
        private static readonly string[] GroupHelp =
        {
            "Fit Atlas reduces output when content would overflow. Sharper Fit Textures favors sharper downsampling; Atlas Overflow Fit Method and Fit Percentage Decrease control the fitting strategy and step. Convert Mip Maps includes mip levels during conversion. Atlas Resolution is the target atlas size. Source UV Cropping removes unused source texture area using prepared slot bounds, with padding in source texels.",
            "Convert Render Texture creates Texture2D output when generated textures must be CPU-readable, editable, saved, or compressed. Use Async Conversion avoids a synchronous GPU readback stall. Async Mip Regen rebuilds mip levels after that copy when required. Leave conversion off when RenderTexture output is sufficient.",
            "Max Queued Conversions Per Frame limits texture readback starts. Iteration Count, Inter Frame Delay, and Max Multi-Step Work distribute generator work across frames. Collect Garbage and Garbage Collection Rate control explicit cleanup. Process All Pending drains queued work, while Apply Inline applies supported conversions immediately. Aggressive values can cause frame hitches.",
            "Initial Scale Factor begins generation at a reduced atlas scale. Automatic Scaling increases reduction when available GPU or system memory falls below the configured cutoffs. These settings trade texture detail for lower peak and resident memory.",
            "Editor Atlas Resolution and Editor Initial Scale Factor are used for edit-time character previews. Lower resolution and a larger scale factor make scene editing faster and reduce scene memory without changing player-build quality settings.",
            "Save And Restore Ignored Items preserves tagged objects across rig rebuilds. Cleanup Unused Bones removes obsolete slot bones after successful builds, retaining the effective T-pose, dependencies and UMAIgnore subtrees; external skeletons are excluded. Measure Bone Cleanup records CPU cost. Show In Hierarchy exposes generated objects for debugging. Default Renderer and Overlay Assets are fallbacks. Always Regenerate Renderers rebuilds renderer objects. 32-Bit Buffers supports large meshes. Texture Merge and Mesh Combiner choose the generation implementations; inherited references are usually safest.",
            "Maximum Dimension caps generated texture width and height. Mip Maps, Filter Mode, Anisotropy, and Mip Bias control sampling. Compression optionally compresses supported generated Texture2D outputs and may add runtime work. These policies affect generated outputs only and never modify source textures.",
            "Reuse Meshes and Reuse Textures share exact compatible results for avatars that inherit generator reuse policy. Atlas Scale controls per-character texture size. LOD settings configure distance, levels, slot dropping/swapping, prepared mesh LODs, texture resizing, and feature shutoff. Mesh readability and blendshape fields control retained runtime payload. The remaining fields set shadows, motion vectors, skin weights, and whether explicit renderer settings may be overridden."
        };
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Unchecked = inherit. Checked = override. For asset references, checked + None explicitly clears the inherited asset. Profiles never edit shared source assets.", MessageType.Info);
            for (int groupIndex = 0; groupIndex < Groups.Length; groupIndex++)
            {
                var group = Groups[groupIndex];
                using (inspectorView.Section(group[0], GroupHelp[groupIndex]))
                    for (int i = 1; i < group.Length; i++) DrawOverride(serializedObject.FindProperty(group[i]));
            }
            EditorGUILayout.HelpBox("Runtime compression is opt-in and can add main-thread work. It only compresses supported 8-bit RGBA Texture2D outputs with block-aligned sizes; RenderTextures, HDR and unsupported devices retain their uncompressed output. Reuse choices only apply to characters with Inherit Generator Reuse Policy enabled. LOD controls tune an existing UMASimpleLOD component. A mesh-combiner reference must be a component on a dedicated prefab, or left inherited.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
                if (GUILayout.Button("Refresh active quality controllers"))
                {
                    foreach (var controller in UnityEngine.Object.FindObjectsByType<GeneratorQualityController>(FindObjectsSortMode.None)) controller.Refresh();
                    foreach (var sceneOverride in UnityEngine.Object.FindObjectsByType<UMAGeneratorOverride>(FindObjectsSortMode.None)) sceneOverride.ApplySettings();
                }
        }
        private static void DrawOverride(SerializedProperty property)
        {
            if (property == null) return;
            var toggle = property.FindPropertyRelative("overrideValue");
            var value = property.FindPropertyRelative("value");
            var rect = EditorGUILayout.GetControlRect();
            var label = UMAInspectorView.Label(ObjectNames.NicifyVariableName(property.name));
            EditorGUI.BeginProperty(rect, label, property);
            var check = new Rect(rect.x, rect.y, 18, rect.height);
            EditorGUI.PropertyField(check, toggle, GUIContent.none);
            rect.xMin += 22;
            using (new EditorGUI.DisabledScope(!toggle.boolValue && !toggle.hasMultipleDifferentValues))
                EditorGUI.PropertyField(rect, value, label);
            EditorGUI.EndProperty();
        }
    }

    [CustomEditor(typeof(GeneratorQualitySettings))]
    public sealed class GeneratorQualitySettingsEditor : Editor
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(GeneratorQualitySettings));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (inspectorView.Section("Platform & quality profiles",
                "Default Profile is used when no mapping matches. Each mapping selects a profile for one runtime platform or Any Platform and one named Unity quality level or Any Quality Level. Resolution prefers exact platform plus quality, then the platform default, then the all-platform quality mapping, then Default Profile. The first duplicate row wins; quality names must match Project Settings."))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultProfile"));
                EditorGUILayout.HelpBox("Priority: exact platform + quality, platform default, all-platform quality, then default profile. First duplicate wins. Quality names must match Unity Project Settings; numeric indices are never stored.", MessageType.Info);
                var entries = serializedObject.FindProperty("entries");
                var seen = new HashSet<string>();
                for (int i = 0; i < entries.arraySize; i++)
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        var entry = entries.GetArrayElementAtIndex(i);
                        var any = entry.FindPropertyRelative("anyPlatform");
                        var platform = entry.FindPropertyRelative("platform");
                        var quality = entry.FindPropertyRelative("qualityLevel");
                        EditorGUILayout.PropertyField(any);
                        using (new EditorGUI.DisabledScope(any.boolValue)) EditorGUILayout.PropertyField(platform);
                        var names = new List<string> { "" }; names.AddRange(QualitySettings.names);
                        string current = quality.stringValue;
                        if (!names.Contains(current)) names.Add(current);
                        var labels = names.ToArray(); labels[0] = "Any quality level";
                        EditorGUI.BeginChangeCheck();
                        int choice = EditorGUILayout.Popup("Quality level", names.IndexOf(current), labels);
                        if (EditorGUI.EndChangeCheck()) quality.stringValue = names[choice];
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("profile"));
                        string key = (any.boolValue ? "*" : platform.intValue.ToString()) + ":" + quality.stringValue;
                        if (!seen.Add(key)) EditorGUILayout.HelpBox("Duplicate mapping: the first matching row wins.", MessageType.Warning);
                        if (GUILayout.Button("Remove mapping")) { entries.DeleteArrayElementAtIndex(i); break; }
                    }
                if (GUILayout.Button("Add mapping"))
                {
                    int index = entries.arraySize++;
                    var entry = entries.GetArrayElementAtIndex(index);
                    entry.FindPropertyRelative("anyPlatform").boolValue = true;
                    entry.FindPropertyRelative("qualityLevel").stringValue = "";
                    entry.FindPropertyRelative("profile").objectReferenceValue = null;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(GeneratorQualityController))]
    public sealed class GeneratorQualityControllerEditor : Editor
    {
        private readonly UMAInspectorView inspectorView = new UMAInspectorView(typeof(GeneratorQualityController));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (inspectorView.Section("Generator quality selection",
                "Generator selects the controlled generator, or uses UMA's indexed generator when empty. Settings resolves a profile from platform and Unity quality; Profile optionally forces one instead. Priority resolves multiple active controllers. Rebuild Existing chooses new builds only, gradual rebuilds, or an immediate safe-boundary rebuild; Rebuilds Per Frame limits gradual work. Preview Platform and Editor Platform test another target while in the Editor."))
                DrawPropertiesExcluding(serializedObject, "m_Script");
            var controller = (GeneratorQualityController)target;
            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed && Application.isPlaying && controller.isActiveAndEnabled) controller.Refresh();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !controller.isActiveAndEnabled))
                if (GUILayout.Button("Apply / refresh quality")) controller.Refresh();
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Selected: " + (controller.SelectedProfile != null ? controller.SelectedProfile.name : "Generator defaults") +
                    (controller.ChangePending ? "\nWaiting for the current build / GPU readbacks." : "\nSettings applied."), MessageType.Info);
            EditorGUILayout.HelpBox("New Builds Only leaves existing characters alone. Gradual queues a limited number per frame. Immediate rebuilds synchronously at the next safe boundary: use on loading screens. Mesh/texture changes retain old shared resources until their owners release them.", MessageType.None);
        }
    }
}
