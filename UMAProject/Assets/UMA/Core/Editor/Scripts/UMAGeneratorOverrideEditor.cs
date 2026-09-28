using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAGeneratorOverride)), CanEditMultipleObjects]
    public class UMAGeneratorOverrideEditor : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMAGeneratorOverride));

        private static readonly GUIContent[] AtlasLabels =
        {
            new GUIContent("512"), new GUIContent("1024"),
            new GUIContent("2048"), new GUIContent("4096"),
            new GUIContent("8192")
        };

        private static readonly int[] AtlasValues =
            { 512, 1024, 2048, 4096, 8192 };

        public override void OnInspectorGUI()
        {
            if (target == null || serializedObject == null ||
                serializedObject.targetObject == null)
            {
                EditorGUILayout.HelpBox(
                    "Inspector target is not available during the current domain reload.",
                    MessageType.Info);
                return;
            }

            serializedObject.Update();
            bool advanced = inspectorView.DrawSelector();
            DrawQualityProfile();

            SerializedProperty profile = Property("qualityProfile");
            bool profilesDiffer = profile != null &&
                profile.hasMultipleDifferentValues;
            bool usesProfile = profile != null && !profilesDiffer &&
                profile.objectReferenceValue != null;

            if (profilesDiffer)
            {
                EditorGUILayout.HelpBox(
                    "The selected objects do not use the same quality profile. " +
                    "Choose a common profile, or clear the profiles to edit local overrides together.",
                    MessageType.Warning);
            }
            else if (usesProfile)
            {
                using (inspectorView.Section("Effective settings",
                    "The assigned Quality Profile supplies this component's generator settings. " +
                    "Edit the profile to change its checked overrides; unchecked values inherit " +
                    "from a lower-priority controller or the generator. Clear Quality Profile to " +
                    "configure local settings on this component."))
                {
                    EditorGUILayout.HelpBox(
                        "Local settings are inactive while a Quality Profile is assigned.",
                        MessageType.Info);
                }
            }
            else if (advanced)
            {
                DrawAdvancedInspector();
            }
            else
            {
                DrawStandardInspector();
            }

            bool applyRequested = DrawRuntimeApplication();
            bool changed = serializedObject.ApplyModifiedProperties();
            if (Application.isPlaying && (changed || applyRequested))
                ApplyAllTargets();
        }

        private void DrawQualityProfile()
        {
            using (inspectorView.Section("Quality profile & ownership",
                "Quality Profile is an optional reusable collection of generator settings. " +
                "When assigned, its checked values replace this component's local controls and " +
                "its unchecked values inherit. Quality Priority resolves competing controllers: " +
                "higher values win and equal values use activation order. Quality Rebuild chooses " +
                "whether a change affects only new builds or also rebuilds existing avatars."))
            {
                Field("qualityProfile", "Quality Profile");
                Field("qualityPriority", "Quality Priority");
                Field("qualityRebuild", "Existing Avatar Rebuild");
            }
        }

        private void DrawStandardInspector()
        {
            using (inspectorView.Section("Atlas quality",
                "Atlas Resolution limits generated atlas dimensions. Fit Atlas reduces content " +
                "that would overflow; Prefer Sharper Fit chooses sharper reductions, while the " +
                "fit method and reduction step control how sizes are tested. Generate Mip Maps " +
                "adds filtered lower-resolution levels. Source UV Cropping can override the " +
                "generator and remove unused source texture area, with padding measured in texels."))
            {
                DrawAtlasResolution();
                Field("fitAtlas", "Fit Atlas");
                if (IsTrue("fitAtlas"))
                {
                    Field("SharperFitTextures", "Prefer Sharper Fit");
                    Field("AtlasOverflowFitMethod", "Overflow Fit Method");
                    Field("FitPercentageDecrease", "Fit Reduction Step");
                }
                Field("convertMipMaps", "Generate Mip Maps");
                DrawSourceUVCropping(false);
            }

            using (inspectorView.Section("Frame pacing",
                "Initial Scale Factor reduces texture work at the start of generation. Iterations " +
                "Per Frame and Inter-Frame Delay distribute avatar builds over time. Multi-Step " +
                "Budget is the desired main-thread mesh-work budget; zero is unlimited. Maximum " +
                "Queued Conversions limits outstanding texture conversions. Process All Pending " +
                "favors total throughput over frame consistency."))
            {
                Field("InitialScaleFactor", "Initial Scale Factor");
                Field("IterationCount", "Iterations Per Frame");
                Field("InterFrameDelay", "Inter-Frame Delay");
                Field("MaxMultiStepWorkMilliseconds",
                    "Multi-Step Budget (ms)");
                Field("MaxQueuedConversionsPerFrame",
                    "Maximum Queued Conversions");
                Field("processAllPending", "Process All Pending");
            }

            using (inspectorView.Section("Generated textures",
                "Convert Render Textures creates Texture2D output for CPU access, saving, or " +
                "compression. Asynchronous Conversion avoids a synchronous GPU readback stall; " +
                "Regenerate Async Mips rebuilds mip levels after that copy. Leave conversion off " +
                "when RenderTexture output is sufficient."))
            {
                DrawTextureConversion(false);
            }

            using (inspectorView.Section("Memory safeguards",
                "Automatic Scaling increases texture reduction on devices below the configured " +
                "GPU or system-memory cutoffs. Collect Garbage enables scheduled cleanup and " +
                "Garbage Collection Rate controls its frequency; zero disables periodic cleanup. " +
                "These controls trade peak quality or occasional cleanup work for lower memory use."))
            {
                Field("AutomaticScaling", "Automatic Scaling");
                if (IsTrue("AutomaticScaling"))
                {
                    Field("ScaleGPUMemoryCutoffMB", "GPU Memory Cutoff (MB)");
                    Field("ScaleSystemMemoryCutoffMB",
                        "System Memory Cutoff (MB)");
                }
                Field("collectGarbage", "Collect Garbage");
                if (IsTrue("collectGarbage"))
                    Field("garbageCollectionRate", "Garbage Collection Rate");
            }

            using (inspectorView.Section("Edit-time preview",
                "Editor Atlas Resolution and Editor Initial Scale Factor are used for scene " +
                "previews. A smaller atlas or larger scale factor reduces editor memory and " +
                "generation time without changing player-build quality."))
            {
                Field("editorAtlasResolution", "Editor Atlas Resolution");
                Field("editorInitialScaleFactor",
                    "Editor Initial Scale Factor");
                Field("showInHierarchy",
                    "Show Generated Objects In Hierarchy");
            }
        }

        private void DrawAdvancedInspector()
        {
            using (inspectorView.Section("Atlas packing",
                "Atlas Resolution is the output limit. Fit Atlas, Prefer Sharper Fit, Overflow " +
                "Fit Method, and Fit Reduction Step determine how overflowing content is scaled. " +
                "Generate Mip Maps includes lower-resolution levels. Source UV Cropping optionally " +
                "overrides the generator, using prepared slot bounds and source-texel padding."))
            {
                DrawAtlasResolution();
                Field("fitAtlas", "Fit Atlas");
                using (new EditorGUI.DisabledScope(!IsTrue("fitAtlas")))
                {
                    Field("SharperFitTextures", "Prefer Sharper Fit");
                    Field("AtlasOverflowFitMethod", "Overflow Fit Method");
                    Field("FitPercentageDecrease", "Fit Reduction Step");
                }
                Field("convertMipMaps", "Generate Mip Maps");
                DrawSourceUVCropping(true);
            }

            using (inspectorView.Section("Texture conversion",
                "Convert Render Textures creates Texture2D output. Asynchronous Conversion copies " +
                "without a blocking GPU readback; Regenerate Async Mips rebuilds mip levels after " +
                "that copy. Apply Inline applies supported converted textures immediately instead " +
                "of waiting for the normal generator application stage."))
            {
                DrawTextureConversion(true);
                Field("applyInline", "Apply Inline");
            }

            using (inspectorView.Section("Generation & scheduling",
                "Maximum Queued Conversions caps texture conversion starts. Initial Scale Factor " +
                "reduces initial texture work. Iterations Per Frame, Inter-Frame Delay, and the " +
                "Multi-Step Budget distribute work. Process All Pending favors throughput. Save " +
                "And Restore Ignored Items preserves tagged objects. Cleanup Unused Bones removes obsolete slot bones while retaining the effective T-pose, dependencies and UMAIgnore subtrees; external skeletons are excluded. Measure Bone Cleanup records CPU cost. Show Generated Objects " +
                "exposes generated children for inspection."))
            {
                Field("MaxQueuedConversionsPerFrame",
                    "Maximum Queued Conversions");
                Field("InitialScaleFactor", "Initial Scale Factor");
                Field("IterationCount", "Iterations Per Frame");
                Field("InterFrameDelay", "Inter-Frame Delay");
                Field("MaxMultiStepWorkMilliseconds",
                    "Multi-Step Budget (ms)");
                Field("processAllPending", "Process All Pending");
                Field("SaveAndRestoreIgnoredItems",
                    "Save And Restore Ignored Items");
                Field("CleanupUnusedBones", "Cleanup Unused Bones");
                Field("MeasureBoneCleanup", "Measure Bone Cleanup");
                Field("showInHierarchy",
                    "Show Generated Objects In Hierarchy");
            }

            using (inspectorView.Section("Memory management",
                "Automatic Scaling increases texture reduction below the GPU or system-memory " +
                "cutoffs. Collect Garbage and its rate schedule cleanup. Use 32-bit Mesh Buffers " +
                "supports meshes above the 16-bit index limit at a higher memory cost."))
            {
                Field("AutomaticScaling", "Automatic Scaling");
                using (new EditorGUI.DisabledScope(!IsTrue("AutomaticScaling")))
                {
                    Field("ScaleGPUMemoryCutoffMB", "GPU Memory Cutoff (MB)");
                    Field("ScaleSystemMemoryCutoffMB",
                        "System Memory Cutoff (MB)");
                }
                Field("collectGarbage", "Collect Garbage");
                using (new EditorGUI.DisabledScope(!IsTrue("collectGarbage")))
                    Field("garbageCollectionRate", "Garbage Collection Rate");
                Field("Use32BitBuffers", "Use 32-bit Mesh Buffers");
            }

            using (inspectorView.Section("Edit-time preview",
                "Editor Atlas Resolution and Editor Initial Scale Factor constrain scene previews. " +
                "They do not alter the runtime atlas settings used in a player build."))
            {
                Field("editorAtlasResolution", "Editor Atlas Resolution");
                Field("editorInitialScaleFactor",
                    "Editor Initial Scale Factor");
            }

            using (inspectorView.Section("Rendering & implementations",
                "Default Renderer and Overlay are fallbacks when an avatar or slot supplies none. " +
                "Always Regenerate Renderers recreates renderer objects instead of reusing them. " +
                "Texture Merge and Mesh Combiner replace the generator implementations; leave " +
                "them empty to preserve the generator's current references. Mesh Combiner choices " +
                "are limited to components attached to this GameObject."))
            {
                Field("defaultRendererAsset", "Default Renderer Asset");
                Field("defaultOverlayAsset", "Default Overlay Asset");
                Field("alwaysRegenerateRenderers",
                    "Always Regenerate Renderers");
                Field("textureMerge", "Texture Merge");
                DrawMeshCombinerPicker();
            }
        }

        private void DrawAtlasResolution()
        {
            SerializedProperty property = Property("atlasResolution");
            if (property == null) return;
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.IntPopup(rect, property, AtlasLabels, AtlasValues,
                new GUIContent("Atlas Resolution", property.tooltip));
        }

        private void DrawSourceUVCropping(bool showInactiveValues)
        {
            Field("overrideSourceUVCropping", "Override Source UV Cropping");
            bool enabled = IsTrue("overrideSourceUVCropping");
            if (!showInactiveValues && !enabled) return;

            using (new EditorGUI.DisabledScope(!enabled))
            {
                Field("enableSourceUVCropping", "Enable Source UV Cropping");
                bool cropEnabled = enabled && IsTrue("enableSourceUVCropping");
                if (showInactiveValues || cropEnabled)
                {
                    using (new EditorGUI.DisabledScope(!cropEnabled))
                        Field("sourceUVCropPadding", "Crop Padding (texels)");
                }
            }
        }

        private void DrawTextureConversion(bool showInactiveValues)
        {
            Field("convertRenderTexture", "Convert Render Textures");
            bool conversionEnabled = IsTrue("convertRenderTexture");
            if (!showInactiveValues && !conversionEnabled) return;

            using (new EditorGUI.DisabledScope(!conversionEnabled))
            {
                Field("useAsyncConversion", "Asynchronous Conversion");
                bool asyncEnabled = conversionEnabled &&
                    IsTrue("useAsyncConversion");
                if (showInactiveValues || asyncEnabled)
                {
                    using (new EditorGUI.DisabledScope(!asyncEnabled))
                        Field("asyncMipRegen", "Regenerate Async Mips");
                }
            }
        }

        private bool DrawRuntimeApplication()
        {
            using (inspectorView.Section("Runtime application",
                "Changes apply automatically while the game is running. Apply Settings Now is " +
                "useful after a script changes referenced profile data without changing this " +
                "component. The generator is restored when this component is disabled or destroyed."))
            {
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                    return GUILayout.Button("Apply Settings Now");
            }
        }

        private void DrawMeshCombinerPicker()
        {
            SerializedProperty property = Property("meshCombiner");
            if (property == null) return;

            if (serializedObject.isEditingMultipleObjects ||
                property.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(property,
                    new GUIContent("Mesh Combiner"));
                return;
            }

            UMAGeneratorOverride generatorOverride =
                target as UMAGeneratorOverride;
            UMAMeshCombiner[] attachedCombiners =
                GetAttachedMeshCombiners(generatorOverride);

            if (attachedCombiners.Length == 0)
            {
                EditorGUILayout.PropertyField(property,
                    new GUIContent("Mesh Combiner"));
                EditorGUILayout.HelpBox(
                    "Attach one or more UMAMeshCombiner components to this GameObject " +
                    "to select them here.", MessageType.Info);
                return;
            }

            var choices = new List<UMAMeshCombiner> { null };
            var labels = new List<GUIContent>
            {
                new GUIContent("None (Keep Generator's Current)",
                    "Do not override the generator's current Mesh Combiner.")
            };

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

            if (current != null && !currentIsAttached)
            {
                choices.Add(current);
                labels.Add(new GUIContent(
                    "External: " + GetCombinerLabel(current),
                    "This Mesh Combiner is not attached to the current GameObject."));
            }

            for (int i = 0; i < attachedCombiners.Length; i++)
            {
                UMAMeshCombiner combiner = attachedCombiners[i];
                choices.Add(combiner);
                labels.Add(new GUIContent(
                    GetCombinerLabel(combiner, attachedCombiners, i),
                    "Use this attached Mesh Combiner for the generator override."));
            }

            int selectedIndex = choices.IndexOf(current);
            if (selectedIndex < 0) selectedIndex = 0;

            EditorGUI.BeginChangeCheck();
            selectedIndex = EditorGUILayout.Popup(
                new GUIContent("Mesh Combiner",
                    "Select a Mesh Combiner attached to this GameObject."),
                selectedIndex, labels.ToArray());
            if (EditorGUI.EndChangeCheck())
                property.objectReferenceValue = choices[selectedIndex];
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

        private static string GetCombinerLabel(UMAMeshCombiner combiner,
            UMAMeshCombiner[] attachedCombiners, int combinerIndex)
        {
            string label = GetCombinerLabel(combiner);
            int duplicateNumber = 1;
            bool hasDuplicate = false;

            for (int i = 0; i < attachedCombiners.Length; i++)
            {
                if (attachedCombiners[i] == null ||
                    attachedCombiners[i].GetType() != combiner.GetType())
                    continue;

                if (i < combinerIndex) duplicateNumber++;
                else if (i > combinerIndex) hasDuplicate = true;
            }

            return hasDuplicate || duplicateNumber > 1
                ? label + " (" + duplicateNumber + ")"
                : label;
        }

        private void ApplyAllTargets()
        {
            foreach (Object inspectedTarget in targets)
            {
                if (inspectedTarget is UMAGeneratorOverride generatorOverride)
                    generatorOverride.ApplySettings();
            }
        }

        private SerializedProperty Property(string propertyName)
        {
            return inspectorView.Property(serializedObject, propertyName);
        }

        private bool IsTrue(string propertyName)
        {
            SerializedProperty property = Property(propertyName);
            return property != null &&
                (property.boolValue || property.hasMultipleDifferentValues);
        }

        private void Field(string propertyName, string label = null)
        {
            inspectorView.Field(serializedObject, propertyName, label);
        }
    }
}
