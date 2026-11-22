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
        private readonly List<UMAMaterial> defaultsHairMaterials = new();
        private readonly HashSet<UMAMaterial> selectedDefaultsHairMaterials = new();
        private string defaultsResult;
        private bool defaultsFailed;
        private bool defaultsRemoveWrinkles;
        private bool defaultsEnableWrinkles;

        private void ShowDefaultsPage()
        {
            ClearLog(); currentButton = DefaultsPage; defaultsScroll = Vector2.zero;
            RefreshDefaultsMaterials();
        }

        private void RefreshDefaultsMaterials()
        {
            defaultsMaterials.Clear(); selectedDefaultsMaterials.Clear();
            defaultsHairMaterials.Clear(); selectedDefaultsHairMaterials.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:UMAMaterial"))
            {
                var material = AssetDatabase.LoadAssetAtPath<UMAMaterial>(AssetDatabase.GUIDToAssetPath(guid));
                if (UMAProjectDefaults.IsHairMaterial(material))
                {
                    defaultsHairMaterials.Add(material);
                    selectedDefaultsHairMaterials.Add(material);
                }
                if (!UMAProjectDefaults.IsSkinMaterial(material)) continue;
                defaultsMaterials.Add(material);
                if (UMAProjectDefaults.CanSwitchMaterial(material)) selectedDefaultsMaterials.Add(material);
            }
            defaultsMaterials.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            defaultsHairMaterials.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        }

        private void DrawDefaultsPage()
        {
            using var scroll = new EditorGUILayout.ScrollViewScope(defaultsScroll);
            defaultsScroll = scroll.scrollPosition;
            EditorGUILayout.LabelField("Configure UMA", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Choose project defaults for rendering speed or visual fidelity. Changes are saved to the assets listed below.", EditorStyles.wordWrappedLabel);
            var settings = UMASettings.GetOrCreateSettings();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Generator Prefab", settings.generatorPrefab, typeof(GameObject), false);
            if (GUILayout.Button("Open UMA Project Settings")) SettingsService.OpenProjectSettings("Project/UMA");

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Maximum speed", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Atlas skin textures to reduce compatible material groups and draw calls. Use only the opaque Alpha Clip pass of hair materials, backing up their original material assignments. Enable supported source-UV cropping and remove unused, non-T-pose bones. Mark external attachments with UMAIgnore / UMAKeepChain as appropriate. Switch Realistic Skin SSS materials to a Realistic Skin copy (the SSS originals are backed up) and use Aniso Level " + UMAProjectDefaults.SpeedAnisoLevel + " for generated skin textures. Compress generated textures on the GPU (BC3 on desktop, ETC2 on mobile; experimental, verified on Direct3D 11/12 only; channels marked Never Compress stay uncompressed). Atlas generation itself can take longer than No Atlas.", EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsPluginOperationBusy()))
            {
                defaultsRemoveWrinkles = EditorGUILayout.ToggleLeft(new GUIContent("Remove wrinkle maps",
                    "Turn expression wrinkles off for every avatar, including avatars with an UMA Expression Wrinkles component. Saves the head's two extra atlas textures (about 43 MB per avatar) and the wrinkle texture reads."), defaultsRemoveWrinkles);
                if (GUILayout.Button("Setup UMA for maximum speed", GUILayout.Height(30))) ApplyDefaults(true);
                GUILayout.Space(8);
                EditorGUILayout.LabelField("Maximum Fidelity", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Restore the original hair materials and their additional passes. Use No Atlas for separate skin textures and material parameters. Preserve unused bones and full source UV regions for customization. Keep generated textures uncompressed. Restore Realistic Skin SSS materials that Maximum speed switched, and use Aniso Level " + UMAProjectDefaults.FidelityAnisoLevel + " for generated skin textures so pores stay sharp at grazing angles. This can increase material count and memory usage.", EditorStyles.wordWrappedLabel);
                defaultsEnableWrinkles = EditorGUILayout.ToggleLeft(new GUIContent("Enable wrinkle maps",
                    "Give every UMA avatar expression wrinkles that follow its expressions (UMA 3 heads). Adds two uncompressed 2048 atlas textures to the head, about 43 MB per avatar."), defaultsEnableWrinkles);
                if (GUILayout.Button("Setup UMA for maximum fidelity", GUILayout.Height(30))) ApplyDefaults(false);
            }
            EditorGUILayout.HelpBox("Both: Jobified mesh combiner; one queued avatar per frame; 2 ms soft incremental-work budget; retain RenderTextures; reuse renderers; automatic 16/32-bit index sizing; preserve ignored attachments; garbage collection rate 128. Async conversion and mip regeneration are prepared if conversion is enabled later.", MessageType.Info);
            EditorGUILayout.LabelField("Expression wrinkles now: " + ObjectNames.NicifyVariableName(UMASettings.GetOrCreateSettings().expressionWrinkles.ToString()) +
                ". Leave the wrinkle option unticked to keep this setting.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Texture resolution, scaling, material mipmaps, race blendshape behavior, custom references and quality profiles are preserved. Existing scene generators and quality overrides may override these defaults. Both presets automatically rebuild avatars in the active scene that have editor-time generation enabled.", EditorStyles.wordWrappedLabel);

            GUILayout.Space(8);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Skin UMAMaterials", EditorStyles.boldLabel);
                if (GUILayout.Button("Rescan", GUILayout.Width(70))) RefreshDefaultsMaterials();
            }
            EditorGUILayout.LabelField("Detected by material or shader name containing 'skin'. Review the selection; add custom skin materials below. Only selected skin materials change between Atlas and No Atlas.", EditorStyles.wordWrappedMiniLabel);
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
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Hair UMAMaterials", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Detected by material or shader name containing 'hair', or an existing preset backup. Speed keeps an existing opaque Alpha Clip pass; hair without a compatible pass is preserved and reported. Fidelity restores the saved default and HDRP assignments. Texture and shader settings are preserved.", EditorStyles.wordWrappedMiniLabel);
            foreach (var material in defaultsHairMaterials)
            {
                if (material == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool selected = EditorGUILayout.Toggle(selectedDefaultsHairMaterials.Contains(material), GUILayout.Width(18));
                    if (selected) selectedDefaultsHairMaterials.Add(material); else selectedDefaultsHairMaterials.Remove(material);
                    EditorGUILayout.ObjectField(material, typeof(UMAMaterial), false);
                    GUILayout.Label(UMAProjectDefaults.HasHairMaterialBackup(material) ? "Backup saved" :
                        UMAProjectDefaults.CanUseAlphaClipHair(material) ? "Alpha Clip available" : "No compatible Alpha Clip pass", GUILayout.Width(180));
                }
            }
            var addedHair = (UMAMaterial)EditorGUILayout.ObjectField("Add Hair Material", null, typeof(UMAMaterial), false);
            if (addedHair != null && !defaultsHairMaterials.Contains(addedHair))
            {
                defaultsHairMaterials.Add(addedHair);
                selectedDefaultsHairMaterials.Add(addedHair);
            }
            if (!string.IsNullOrEmpty(defaultsResult))
                EditorGUILayout.HelpBox(defaultsResult, defaultsFailed ? MessageType.Error : MessageType.Info);
        }

        private void ApplyDefaults(bool speed)
        {
            try
            {
                defaultsResult = UMAProjectDefaults.Apply(UMASettings.GetOrCreateSettings().generatorPrefab,
                    selectedDefaultsMaterials.ToArray(), speed, selectedDefaultsHairMaterials.ToArray(),
                    speed ? defaultsRemoveWrinkles : defaultsEnableWrinkles);
                Editors.UMAGeneratorBuiltinEditor.RebuildAllEditorUMA();
                defaultsResult += "\nEditor UMA rebuilds requested for the active scene.";
                defaultsFailed = false;
            }
            catch (Exception exception) { defaultsFailed = true; defaultsResult = exception.Message; }
            EditorUtility.DisplayDialog("Configure UMA", defaultsResult, "OK");
        }
    }

    /// <summary>Project-level defaults; intentionally independent of optional plugins.</summary>
    public static class UMAProjectDefaults
    {
        private static readonly string[] HairMaterialFields = { "_material", "_secondPass", "_HDRPMaterial", "_HDRPSecondPass" };
        private static readonly string[] HairBackupFields = { "_speedBackupMaterial", "_speedBackupSecondPass", "_speedBackupHDRPMaterial", "_speedBackupHDRPSecondPass" };
        private const string HasHairBackupField = "_hasSpeedMaterialBackup";
        private const string HasSkinBackupField = "_hasSpeedSkinBackup";
        private const string SkinBackupField = "_speedBackupSkinMaterial";
        /// <summary>Suffix of the Realistic Skin copy that Maximum speed makes next to a Realistic Skin SSS material.</summary>
        public const string SpeedSkinSuffix = "_Speed";
        /// <summary>Generated skin texture filtering: Maximum speed uses plain trilinear, Maximum Fidelity keeps pores sharp at grazing angles.</summary>
        public const int SpeedAnisoLevel = 1, FidelityAnisoLevel = 8;

        public static bool IsHairMaterial(UMAMaterial material)
        {
            if (material == null) return false;
            using var serialized = new SerializedObject(material);
            if (serialized.FindProperty(HasHairBackupField).boolValue || ContainsHair(material.name)) return true;
            foreach (string field in HairMaterialFields)
            {
                var pass = serialized.FindProperty(field).objectReferenceValue as Material;
                if (pass != null && (ContainsHair(pass.name) || ContainsHair(pass.shader?.name))) return true;
            }
            return false;
        }

        private static bool ContainsHair(string value)
            => value?.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool HasHairMaterialBackup(UMAMaterial material)
        {
            if (material == null) return false;
            using var serialized = new SerializedObject(material);
            return serialized.FindProperty(HasHairBackupField).boolValue;
        }

        public static bool CanUseAlphaClipHair(UMAMaterial material)
        {
            if (material == null) return false;
            using var serialized = new SerializedObject(material);
            return TryGetAlphaClipHairPasses(serialized, out _);
        }

        private static bool IsOpaqueAlphaClip(Material material)
        {
            if (material == null || material.shader == null) return false;
            if ((material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0) ||
                (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0) ||
                (material.HasProperty("_Mode") && material.GetFloat("_Mode") >= 2) ||
                material.GetTag("RenderType", false) == "Transparent") return false;
            return (material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0) ||
                (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0) ||
                material.IsKeywordEnabled("_ALPHATEST_ON") || material.GetTag("RenderType", false) == "TransparentCutout";
        }

        private static bool TryGetAlphaClipHairPasses(SerializedObject serialized, out Material[] passes)
        {
            passes = new Material[4];
            bool found = false;
            for (int i = 0; i < HairMaterialFields.Length; i += 2)
            {
                var first = serialized.FindProperty(HairMaterialFields[i]).objectReferenceValue as Material;
                var second = serialized.FindProperty(HairMaterialFields[i + 1]).objectReferenceValue as Material;
                if (first == null && second == null) continue;
                var alphaClip = IsOpaqueAlphaClip(first) ? first : IsOpaqueAlphaClip(second) ? second : null;
                // A null HDRP second pass falls back to the default second pass at runtime.
                // Only switch when every configured pair can safely become a single pass.
                if (alphaClip == null) return false;
                passes[i] = alphaClip;
                found = true;
            }
            return found;
        }

        private static string ConfigureHairMaterial(UMAMaterial material, bool speed)
        {
            string path = AssetDatabase.GetAssetPath(material);
            using var serialized = new SerializedObject(material);
            var hasBackup = serialized.FindProperty(HasHairBackupField);
            if (!speed)
            {
                if (!hasBackup.boolValue) return "Hair unchanged (no preset backup): " + path;
                for (int i = 0; i < HairMaterialFields.Length; i++)
                {
                    var backup = serialized.FindProperty(HairBackupFields[i]);
                    serialized.FindProperty(HairMaterialFields[i]).objectReferenceValue = backup.objectReferenceValue;
                    backup.objectReferenceValue = null;
                }
                hasBackup.boolValue = false;
            }
            else
            {
                if (!TryGetAlphaClipHairPasses(serialized, out var passes))
                    return "Hair unchanged (no compatible opaque Alpha Clip pass): " + path;
                bool changed = false;
                for (int i = 0; i < HairMaterialFields.Length; i++)
                    changed |= serialized.FindProperty(HairMaterialFields[i]).objectReferenceValue != passes[i];
                if (!changed) return "Hair already uses Alpha Clip only: " + path;
                // Never replace the original assignments when Speed is applied repeatedly.
                if (!hasBackup.boolValue)
                {
                    for (int i = 0; i < HairMaterialFields.Length; i++)
                        serialized.FindProperty(HairBackupFields[i]).objectReferenceValue = serialized.FindProperty(HairMaterialFields[i]).objectReferenceValue;
                    hasBackup.boolValue = true;
                }
                for (int i = 0; i < HairMaterialFields.Length; i++)
                    serialized.FindProperty(HairMaterialFields[i]).objectReferenceValue = passes[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(material);
            return (speed ? "Hair set to Alpha Clip only (originals backed up): " : "Original hair materials restored: ") + path;
        }

        public static bool HasSkinShaderBackup(UMAMaterial material)
        {
            if (material == null) return false;
            using var serialized = new SerializedObject(material);
            return serialized.FindProperty(HasSkinBackupField).boolValue;
        }

        /// <summary>
        /// Speed: a Realistic Skin SSS material is swapped for a Realistic Skin copy saved next to it (no diffusion
        /// blur, curvature or thickness shadow reads), and the original is kept as an editor-only backup.
        /// Fidelity: restores that backup. Other skin shaders are left alone, including the expression wrinkle shader,
        /// which needs SSS. Returns null when there is nothing to report.
        /// </summary>
        private static string ConfigureSkinShader(UMAMaterial material, bool speed)
        {
            string path = AssetDatabase.GetAssetPath(material);
            using var serialized = new SerializedObject(material);
            var hasBackup = serialized.FindProperty(HasSkinBackupField);
            var backup = serialized.FindProperty(SkinBackupField);
            var current = serialized.FindProperty("_material");
            var source = current.objectReferenceValue as Material;
            if (!speed)
            {
                if (!hasBackup.boolValue) return null;
                var original = backup.objectReferenceValue as Material;
                if (original == null) return "Skin shader unchanged (backup material missing): " + path;
                current.objectReferenceValue = original;
                backup.objectReferenceValue = null;
                hasBackup.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(material);
                return "Realistic Skin SSS restored (" + original.name + "): " + path;
            }
            string shaderName = source != null && source.shader != null ? source.shader.name : null;
            if (shaderName == Editors.UMARealisticSkinUtility.WrinkleShaderName)
                return "Skin shader kept (expression wrinkles need Realistic Skin SSS): " + path;
            if (shaderName != Editors.UMARealisticSkinUtility.SSSShaderName) return null;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath) || !sourcePath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                return "Skin shader unchanged (material is not a .mat asset): " + path;
            var copy = SaveSpeedSkinCopy(source, sourcePath);
            if (copy == null) return "Skin shader unchanged (cannot write a Realistic Skin copy next to " + sourcePath + "): " + path;
            // A Realistic Skin SSS material assigned after an earlier Speed run becomes the one Fidelity restores.
            backup.objectReferenceValue = source;
            hasBackup.boolValue = true;
            current.objectReferenceValue = copy;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(material);
            return "Skin set to Realistic Skin (" + AssetDatabase.GetAssetPath(copy) + ", SSS original backed up): " + path;
        }

        /// <summary>
        /// Makes or refreshes the Realistic Skin copy of an SSS material, so later SSS edits carry over.
        /// Null when the copy cannot be written (read-only package or file).
        /// </summary>
        private static Material SaveSpeedSkinCopy(Material source, string sourcePath)
        {
            string copyPath = sourcePath.Substring(0, sourcePath.Length - 4) + SpeedSkinSuffix + ".mat";
            if (!sourcePath.StartsWith("Assets/", StringComparison.Ordinal)) return null;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
            if (existing != null && (!AssetDatabase.IsOpenForEdit(copyPath, StatusQueryOptions.ForceUpdate) ||
                (System.IO.File.GetAttributes(copyPath) & System.IO.FileAttributes.ReadOnly) != 0)) return null;
            var fresh = Editors.UMARealisticSkinUtility.CreateMaterialCopy(source);
            fresh.name = System.IO.Path.GetFileNameWithoutExtension(copyPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, copyPath);
                return fresh;
            }
            try
            {
                string before = EditorJsonUtility.ToJson(existing);
                EditorUtility.CopySerialized(fresh, existing);
                if (before != EditorJsonUtility.ToJson(existing))
                {
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                }
                return existing;
            }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
        }

        public static bool IsSkinMaterial(UMAMaterial material)
            => material != null && (ContainsSkin(material.name) || ContainsSkin(material.material?.shader?.name));
        private static bool ContainsSkin(string value)
            => value?.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool CanSwitchMaterial(UMAMaterial material)
            => material != null && (material.materialType == UMAMaterial.MaterialType.Atlas || material.materialType == UMAMaterial.MaterialType.NoAtlas);

        /// <param name="wrinkles">Speed: turn expression wrinkles off for every avatar. Fidelity: turn them on for every
        /// avatar. False leaves the project's expression wrinkle setting as it is.</param>
        public static string Apply(GameObject prefab, IReadOnlyList<UMAMaterial> materials, bool speed,
            IReadOnlyList<UMAMaterial> hairMaterials = null, bool wrinkles = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Use Configure UMA outside Play mode.");
            string path = AssetDatabase.GetAssetPath(prefab);
            if (prefab == null || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Assign a Generator Prefab in Project Settings > UMA first.");
            var generators = prefab.GetComponentsInChildren<UMAGeneratorBuiltin>(true);
            if (generators.Length != 1)
                throw new InvalidOperationException("Expected exactly one UMAGeneratorBuiltin-compatible generator in " + path);
            RequireWritable(path);
            var selected = (materials ?? Array.Empty<UMAMaterial>()).Where(CanSwitchMaterial).Distinct().ToArray();
            var selectedHair = (hairMaterials ?? Array.Empty<UMAMaterial>()).Where(material => material != null).Distinct().ToArray();
            foreach (var material in selected) RequireWritable(AssetDatabase.GetAssetPath(material));
            foreach (var material in selectedHair) RequireWritable(AssetDatabase.GetAssetPath(material));
            var settings = wrinkles ? UMASettings.GetOrCreateSettings() : null;
            if (wrinkles) RequireWritable(AssetDatabase.GetAssetPath(settings));
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
                // Speed: BC3 / ETC2 atlases, about a quarter of the memory of RGBA32. The compressor falls back to
                // uncompressed textures on devices it cannot handle, and Never Compress channels are always skipped.
                generator.forceGPUTextureCompression = speed;
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
                int aniso = speed ? SpeedAnisoLevel : FidelityAnisoLevel;
                foreach (var material in selected)
                {
                    string materialPath = AssetDatabase.GetAssetPath(material);
                    var changes = new List<string>();
                    if (material.materialType != type) { material.materialType = type; changes.Add(type.ToString()); }
                    if (material.AnisoLevel != aniso) { material.AnisoLevel = aniso; changes.Add("Aniso Level " + aniso); }
                    if (changes.Count > 0)
                    {
                        EditorUtility.SetDirty(material);
                        AssetDatabase.SaveAssetIfDirty(material);
                        report.Add("Set " + string.Join(", ", changes) + ": " + materialPath);
                    }
                    else report.Add("Already " + type + ", Aniso Level " + aniso + ": " + materialPath);
                    string shaderReport = ConfigureSkinShader(material, speed);
                    if (shaderReport != null) report.Add(shaderReport);
                }
                foreach (var material in selectedHair) report.Add(ConfigureHairMaterial(material, speed));
                if (wrinkles) report.Add(ConfigureExpressionWrinkles(settings, speed));
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Configure UMA could not finish: " + exception.Message +
                    (report.Count > 0 ? "\nCompleted before the failure:\n" + string.Join("\n", report) : "\nNo changes were saved."), exception);
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
            return (speed ? "Maximum speed" : "Maximum Fidelity") + " defaults applied.\n" +
                string.Join("\n", report) + "\nScene and quality overrides remain unchanged.";
        }

        /// <summary>
        /// Speed: expression wrinkles off for every avatar, and the settings no longer reference the wrinkle overlay and
        /// profile (so they stop pulling the wrinkle textures into builds). Fidelity: every avatar gets them, with the UMA 3
        /// overlay and profile, made first if missing.
        /// </summary>
        public static string ConfigureExpressionWrinkles(UMASettings settings, bool speed)
        {
            OverlayDataAsset overlay = null;
            UMAWrinkleProfile profile = null;
            if (!speed)
            {
                overlay = Editors.UMAWrinkleAssets.EnsureUMA3Assets(out string message);
                if (overlay == null) return "Expression wrinkles unchanged: " + message;
                profile = Editors.UMAWrinkleAssets.EnsureUMA3Profile();
            }
            var mode = speed ? UMASettings.ExpressionWrinkleMode.Off : UMASettings.ExpressionWrinkleMode.AllAvatars;
            using (var serialized = new SerializedObject(settings))
            {
                serialized.FindProperty("expressionWrinkles").intValue = (int)mode;
                serialized.FindProperty("wrinkleOverlay").objectReferenceValue = overlay;
                serialized.FindProperty("wrinkleProfile").objectReferenceValue = profile;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssetIfDirty(settings);
            return speed ? "Expression wrinkles off for every avatar: " + AssetDatabase.GetAssetPath(settings)
                : "Expression wrinkles on every avatar (" + overlay.name + ", " + profile.name + "): " + AssetDatabase.GetAssetPath(settings);
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
