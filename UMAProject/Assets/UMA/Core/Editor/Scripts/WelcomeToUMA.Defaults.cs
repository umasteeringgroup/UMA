using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public partial class WelcomeToUMA
    {
        private const int DefaultsPage = 12;
        private Vector2 defaultsScroll;
        private readonly List<UMAMaterial> defaultsMaterials = new();
        private readonly HashSet<UMAMaterial> selectedDefaultsMaterials = new();
        private string defaultsResult;
        private bool defaultsFailed;

        private void ShowDefaultsPage()
        {
            ClearLog(); currentButton = DefaultsPage; defaultsScroll = Vector2.zero;
            RefreshDefaultsMaterials();
        }

        private void RefreshDefaultsMaterials()
        {
            defaultsMaterials.Clear(); selectedDefaultsMaterials.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:UMAMaterial"))
            {
                var material = AssetDatabase.LoadAssetAtPath<UMAMaterial>(AssetDatabase.GUIDToAssetPath(guid));
                if (!UMAProjectDefaults.IsSkinMaterial(material)) continue;
                defaultsMaterials.Add(material);
                if (UMAProjectDefaults.CanSwitchMaterial(material)) selectedDefaultsMaterials.Add(material);
            }
            defaultsMaterials.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void DrawDefaultsPage()
        {
            using var scroll = new EditorGUILayout.ScrollViewScope(defaultsScroll);
            defaultsScroll = scroll.scrollPosition;
            EditorGUILayout.LabelField("UMA Defaults", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Choose project defaults for rendering speed or separate, editable skin materials. Changes are saved to the assets listed below.", EditorStyles.wordWrappedLabel);
            var settings = UMASettings.GetOrCreateSettings();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Generator Prefab", settings.generatorPrefab, typeof(GameObject), false);
            if (GUILayout.Button("Open UMA Project Settings")) SettingsService.OpenProjectSettings("Project/UMA");

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Maximum speed", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Atlas skin textures to reduce compatible material groups and draw calls. Enable supported source-UV cropping and remove unused, non-T-pose bones. Mark external attachments with UMAIgnore / UMAKeepChain as appropriate. Atlas generation itself can take longer than No Atlas.", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsPluginOperationBusy()))
            {
                if (GUILayout.Button("Setup UMA for maximum speed", GUILayout.Height(30))) ApplyDefaults(true);
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Maximum flexibility", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Use No Atlas for separate skin textures and material parameters. Preserve unused bones and full source UV regions for customization. This can increase material count and memory usage.", EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("Setup UMA for maximum flexibility", GUILayout.Height(30))) ApplyDefaults(false);
            }
            EditorGUILayout.HelpBox("Both: Jobified mesh combiner; one queued avatar per frame; 2 ms soft incremental-work budget; retain RenderTextures; reuse renderers; automatic 16/32-bit index sizing; preserve ignored attachments; garbage collection rate 128. Async conversion and mip regeneration are prepared if conversion is enabled later.", MessageType.Info);
            EditorGUILayout.LabelField("Texture resolution, scaling, material mipmaps, race blendshape behavior, custom references and quality profiles are preserved. Existing scene generators and quality overrides may override these defaults. Rebuild characters or re-enter Play mode to use the changed assets.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Skin UMAMaterials", EditorStyles.boldLabel);
                if (GUILayout.Button("Rescan", GUILayout.Width(70))) RefreshDefaultsMaterials();
            }
            EditorGUILayout.LabelField("Detected by material or shader name containing 'skin'. Review the selection; add custom skin materials below. Other material types are preserved.", EditorStyles.wordWrappedMiniLabel);
            foreach (var material in defaultsMaterials)
            {
                if (material == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!UMAProjectDefaults.CanSwitchMaterial(material)))
                    {
                        bool selected = EditorGUILayout.Toggle(selectedDefaultsMaterials.Contains(material), GUILayout.Width(18));
                        if (selected) selectedDefaultsMaterials.Add(material); else selectedDefaultsMaterials.Remove(material);
                    }
                    EditorGUILayout.ObjectField(material, typeof(UMAMaterial), false);
                    GUILayout.Label(material.materialType.ToString(), GUILayout.Width(110));
                }
            }
            var added = (UMAMaterial)EditorGUILayout.ObjectField("Add Skin Material", null, typeof(UMAMaterial), false);
            if (added != null && !defaultsMaterials.Contains(added))
            {
                defaultsMaterials.Add(added);
                if (UMAProjectDefaults.CanSwitchMaterial(added)) selectedDefaultsMaterials.Add(added);
            }
            if (defaultsMaterials.Count == 0) EditorGUILayout.HelpBox("No skin UMAMaterials found. Install skin content or add a custom material. The generator can still be configured.", MessageType.Info);
            if (!string.IsNullOrEmpty(defaultsResult))
                EditorGUILayout.HelpBox(defaultsResult, defaultsFailed ? MessageType.Error : MessageType.Info);
        }

        private void ApplyDefaults(bool speed)
        {
            try
            {
                defaultsResult = UMAProjectDefaults.Apply(UMASettings.GetOrCreateSettings().generatorPrefab,
                    selectedDefaultsMaterials.ToArray(), speed);
                defaultsFailed = false;
            }
            catch (Exception exception) { defaultsFailed = true; defaultsResult = exception.Message; }
            EditorUtility.DisplayDialog("UMA Defaults", defaultsResult, "OK");
        }
    }

    /// <summary>Project-level defaults; intentionally independent of optional plugins.</summary>
    public static class UMAProjectDefaults
    {
        public static bool IsSkinMaterial(UMAMaterial material)
            => material != null && (ContainsSkin(material.name) || ContainsSkin(material.material?.shader?.name));
        private static bool ContainsSkin(string value)
            => value?.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool CanSwitchMaterial(UMAMaterial material)
            => material != null && (material.materialType == UMAMaterial.MaterialType.Atlas || material.materialType == UMAMaterial.MaterialType.NoAtlas);

        public static string Apply(GameObject prefab, IReadOnlyList<UMAMaterial> materials, bool speed)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply UMA Defaults outside Play mode.");
            string path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Assign a Generator Prefab in Project Settings > UMA first.");
            var generators = prefab.GetComponentsInChildren<UMAGeneratorBuiltin>(true);
            if (generators.Length != 1)
                throw new InvalidOperationException("Expected exactly one UMAGeneratorBuiltin-compatible generator in " + path);
            RequireWritable(path);
            var selected = (materials ?? Array.Empty<UMAMaterial>()).Where(CanSwitchMaterial).Distinct().ToArray();
            foreach (var material in selected) RequireWritable(AssetDatabase.GetAssetPath(material));
            var type = speed ? UMAMaterial.MaterialType.Atlas : UMAMaterial.MaterialType.NoAtlas;
            var report = new List<string>();
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                var generator = contents.GetComponentInChildren<UMAGeneratorBuiltin>(true);
                string before = EditorJsonUtility.ToJson(generator);
                var combiner = generator.GetComponent<UMAJobifiedMeshCombiner>();
                bool combinerChanged = combiner == null || !combiner.enabled;
                if (combiner == null) combiner = generator.gameObject.AddComponent<UMAJobifiedMeshCombiner>();
                combiner.enabled = true;
                generator.meshCombiner = combiner;
                generator.processAllPending = false;
                generator.IterationCount = 1;
                generator.InterFrameDelay = 0;
                generator.MaxMultiStepWorkMilliseconds = 2f;
                generator.garbageCollectionRate = 128;
                generator.convertRenderTexture = false;
                generator.useAsyncConversion = true;
                generator.asyncMipRegen = true;
                generator.MultiThreadTextureConversion = true;
                generator.convertMipMaps = true;
                generator.applyInline = false;
                generator.MaxQueuedConversionsPerFrame = 4;
                generator.alwaysRegenerateRenderers = false;
                generator.Use32BitBuffers = false; // Automatically promoted for large meshes.
                generator.fitAtlas = true;
                generator.SaveAndRestoreIgnoredItems = true;
                generator.CleanupUnusedBones = speed;
                generator.MeasureBoneCleanup = false;
                generator.enableSourceUVCropping = speed;
                // Preserve filtering safety if an older prefab has an invalid padding value.
                generator.sourceUVCropPadding = Mathf.Max(4, generator.sourceUVCropPadding);
                if (before != EditorJsonUtility.ToJson(generator) || combinerChanged)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                    if (!saved) throw new InvalidOperationException("Could not save generator prefab: " + path);
                }
                report.Add("Generator configured: " + path);
                foreach (var material in selected)
                {
                    string materialPath = AssetDatabase.GetAssetPath(material);
                    if (material.materialType == type) { report.Add("Already " + type + ": " + materialPath); continue; }
                    material.materialType = type; EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    report.Add("Set " + type + ": " + materialPath);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("UMA Defaults could not finish: " + exception.Message +
                    (report.Count > 0 ? "\nCompleted before the failure:\n" + string.Join("\n", report) : "\nNo changes were saved."), exception);
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
            return (speed ? "Maximum speed" : "Maximum flexibility") + " defaults applied.\n" +
                string.Join("\n", report) + "\nRebuild characters or re-enter Play mode. Scene and quality overrides remain unchanged.";
        }

        private static void RequireWritable(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !AssetDatabase.IsOpenForEdit(path, StatusQueryOptions.ForceUpdate) ||
                (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReadOnly) != 0)
                throw new InvalidOperationException("Asset is not writable: " + path + ". Use an editable project copy or check it out first.");
        }
    }
}
