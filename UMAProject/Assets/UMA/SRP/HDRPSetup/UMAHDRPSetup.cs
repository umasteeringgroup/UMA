using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace UMA
{
    /// <summary>
    /// Applies the project-level settings required by UMA's HDRP content.
    /// The editor applies this component when its prefab is imported. It is safe to run repeatedly.
    /// </summary>
    [AddComponentMenu("UMA/HDRP Project Setup")]
    [DisallowMultipleComponent]
    public class UMAHDRPSetup : MonoBehaviour
    {
        public const string SkinShaderName = "Shader Graphs/UMA3_SkinShader_HDRP";
        public const string StudioShaderName = "Shader Graphs/UMA3_Lit(Metal)";

        [Header("HDRP Materials")]
        [Tooltip("The UMA HDRP skin material whose Shader Graph reference will be repaired during setup.")]
        [SerializeField]
        private Material skinMaterial;

        [Tooltip("StudioMat, whose HDRP Shader Graph reference will be repaired during setup.")]
        [SerializeField]
        private Material studioMaterial;

        [Header("Diffusion Profiles")]
        [Tooltip("The UMA skin diffusion profile to register in HDRP's Global Settings Default Volume Profile.")]
        [SerializeField]
        private DiffusionProfileSettings diffusionProfile;

        [Tooltip("Optional additional diffusion profiles to register. This leaves room for future UMA HDRP content without requiring another setup component.")]
        [SerializeField]
        private DiffusionProfileSettings[] additionalDiffusionProfiles = Array.Empty<DiffusionProfileSettings>();

        [Header("HDRP Features")]
        [Tooltip("Enable Subsurface Scattering support on HDRP assets assigned in Graphics and Quality settings. UMA's HDRP skin shader requires it.")]
        [SerializeField]
        private bool ensureSubsurfaceScattering = true;

        [Header("Automation")]
        [Tooltip("Apply this setup automatically when the prefab is imported or reimported.")]
        [SerializeField]
        private bool applyOnImport = true;

        [Tooltip("Log a confirmation when the automatic setup changes project assets.")]
        [SerializeField]
        private bool logChanges = true;

        public Material SkinMaterial => skinMaterial;
        public Material StudioMaterial => studioMaterial;
        public DiffusionProfileSettings DiffusionProfile => diffusionProfile;
        public IReadOnlyList<DiffusionProfileSettings> AdditionalDiffusionProfiles => additionalDiffusionProfiles ?? Array.Empty<DiffusionProfileSettings>();
        public bool EnsureSubsurfaceScattering => ensureSubsurfaceScattering;
        public bool ApplyOnImport => applyOnImport;
        public bool LogChanges => logChanges;

#if UNITY_EDITOR
        private void OnValidate()
        {
            additionalDiffusionProfiles ??= Array.Empty<DiffusionProfileSettings>();

            if (applyOnImport)
            {
                UMAHDRPSetupEditorUtility.Queue(this);
            }
        }

        [ContextMenu("Apply UMA HDRP Setup")]
        public void ApplySetup()
        {
            UMAHDRPSetupEditorUtility.Apply(this, true);
        }
#endif
    }
}

#if UNITY_EDITOR
#pragma warning disable UDR0001 // Editor-only queue state; it is reset after each delayed apply.
namespace UMA
{
    using UnityEditor;
    using UnityEditor.Rendering;

    internal static class UMAHDRPSetupEditorUtility
    {
        private const int MaxCustomDiffusionProfiles = 15;
        private static readonly HashSet<string> QueuedPrefabPaths = new HashSet<string>();
        private static bool applyQueued;

        internal enum SetupResult
        {
            Failed,
            AlreadyConfigured,
            Applied
        }

        [InitializeOnLoadMethod]
        private static void InitializeAfterImport()
        {
            // The prefab can be imported before this script finishes compiling. Looking up the
            // named setup prefab after the domain reload makes package import order irrelevant.
            EditorApplication.delayCall += QueueKnownSetupPrefabs;
        }

        internal static void Queue(UMAHDRPSetup setup)
        {
            if (setup == null || !setup.ApplyOnImport)
            {
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(setup);
            if (!string.IsNullOrEmpty(assetPath))
            {
                Queue(assetPath);
            }
        }

        internal static void Queue(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            QueuedPrefabPaths.Add(prefabPath);
            if (applyQueued)
            {
                return;
            }

            applyQueued = true;
            EditorApplication.delayCall += ApplyQueuedSetups;
        }

        internal static void QueueKnownSetupPrefabs()
        {
            string[] prefabGuids = AssetDatabase.FindAssets($"{nameof(UMAHDRPSetup)} t:Prefab");
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                Queue(AssetDatabase.GUIDToAssetPath(prefabGuids[i]));
            }
        }

        private static void ApplyQueuedSetups()
        {
            applyQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ApplyQueuedSetups;
                applyQueued = true;
                return;
            }

            string[] paths = new string[QueuedPrefabPaths.Count];
            QueuedPrefabPaths.CopyTo(paths);
            QueuedPrefabPaths.Clear();

            for (int i = 0; i < paths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                {
                    continue;
                }

                UMAHDRPSetup[] setups = prefab.GetComponentsInChildren<UMAHDRPSetup>(true);
                for (int setupIndex = 0; setupIndex < setups.Length; setupIndex++)
                {
                    UMAHDRPSetup setup = setups[setupIndex];
                    if (setup.ApplyOnImport)
                    {
                        Apply(setup, setup.LogChanges);
                    }
                }
            }
        }

        internal static SetupResult Apply(UMAHDRPSetup setup, bool logResult)
        {
            if (setup == null)
            {
                return SetupResult.Failed;
            }

            bool changed = EnsureMaterialShader(setup, setup.SkinMaterial, UMAHDRPSetup.SkinShaderName, "UMA3_SkinShader_HDRP");
            changed |= EnsureMaterialShader(setup, setup.StudioMaterial, UMAHDRPSetup.StudioShaderName, "UMA3_Lit(Metal)");

            if (!EditorGraphicsSettings.TryGetRenderPipelineSettingsForPipeline<HDRPDefaultVolumeProfileSettings, HDRenderPipeline>(out var volumeSettings) || volumeSettings == null)
            {
                Debug.LogWarning("UMA HDRP setup could not find HDRP Global Settings. Assign an HDRP Global Settings asset in Project Settings > Graphics, then run 'Apply UMA HDRP Setup' on the setup prefab.", setup);
                return changed ? SetupResult.Applied : SetupResult.Failed;
            }

            VolumeProfile defaultVolumeProfile = volumeSettings.volumeProfile;
            if (defaultVolumeProfile == null)
            {
                Debug.LogWarning("UMA HDRP setup could not find the Default Volume Profile in HDRP Global Settings. Assign one in Project Settings > Graphics > HDRP, then run the setup again.", setup);
                return changed ? SetupResult.Applied : SetupResult.Failed;
            }

            changed |= RegisterDiffusionProfiles(setup, defaultVolumeProfile);
            if (setup.EnsureSubsurfaceScattering)
            {
                changed |= EnableSubsurfaceScattering();
            }

            if (changed)
            {
                VolumeManager.instance.OnVolumeProfileChanged(defaultVolumeProfile);
                AssetDatabase.SaveAssetIfDirty(defaultVolumeProfile);

                if (logResult)
                {
                    Debug.Log("UMA HDRP project setup completed. The diffusion profile is registered and required HDRP features are enabled.", setup);
                }

                return SetupResult.Applied;
            }

            return SetupResult.AlreadyConfigured;
        }

        private static bool EnsureMaterialShader(UMAHDRPSetup setup, Material material, string shaderName, string shaderAssetName)
        {
            if (material == null)
            {
                Debug.LogWarning($"UMA HDRP setup has no material assigned for shader '{shaderName}'; that material was not validated.", setup);
                return false;
            }

            Shader shader = FindShader(shaderName, shaderAssetName);
            if (shader == null)
            {
                Debug.LogWarning($"UMA HDRP setup could not find shader '{shaderName}'. Reimport its UMA HDRP Shader Graph, then run the setup again.", setup);
                return false;
            }

            if (material.shader == shader)
            {
                return false;
            }

            material.shader = shader;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return true;
        }

        private static Shader FindShader(string shaderName, string shaderAssetName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
            {
                return shader;
            }

            string[] shaderGuids = AssetDatabase.FindAssets($"{shaderAssetName} t:Shader");
            for (int i = 0; i < shaderGuids.Length; i++)
            {
                string shaderPath = AssetDatabase.GUIDToAssetPath(shaderGuids[i]);
                Shader candidate = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                if (candidate != null && candidate.name == shaderName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool RegisterDiffusionProfiles(UMAHDRPSetup setup, VolumeProfile defaultVolumeProfile)
        {
            List<DiffusionProfileSettings> requestedProfiles = new List<DiffusionProfileSettings>();
            AddUnique(requestedProfiles, setup.DiffusionProfile);

            IReadOnlyList<DiffusionProfileSettings> additionalProfiles = setup.AdditionalDiffusionProfiles;
            for (int i = 0; i < additionalProfiles.Count; i++)
            {
                AddUnique(requestedProfiles, additionalProfiles[i]);
            }

            if (requestedProfiles.Count == 0)
            {
                Debug.LogWarning("UMA HDRP setup has no diffusion profile assigned; diffusion profile registration was skipped.", setup);
                return false;
            }

            bool changed = false;
            if (!defaultVolumeProfile.TryGet(out DiffusionProfileList profileList))
            {
                profileList = defaultVolumeProfile.Add<DiffusionProfileList>(true);
                if (EditorUtility.IsPersistent(defaultVolumeProfile))
                {
                    AssetDatabase.AddObjectToAsset(profileList, defaultVolumeProfile);
                }

                changed = true;
            }

            List<DiffusionProfileSettings> profiles = new List<DiffusionProfileSettings>();
            DiffusionProfileSettings[] existingProfiles = profileList.diffusionProfiles.value;
            if (existingProfiles != null)
            {
                for (int i = 0; i < existingProfiles.Length; i++)
                {
                    if (existingProfiles[i] == null)
                    {
                        changed = true;
                        continue;
                    }

                    if (!profiles.Contains(existingProfiles[i]))
                    {
                        profiles.Add(existingProfiles[i]);
                    }
                    else
                    {
                        changed = true;
                    }
                }
            }

            for (int i = 0; i < requestedProfiles.Count; i++)
            {
                DiffusionProfileSettings profile = requestedProfiles[i];
                if (profiles.Contains(profile))
                {
                    continue;
                }

                if (profiles.Count >= MaxCustomDiffusionProfiles)
                {
                    Debug.LogError($"UMA HDRP setup could not register '{profile.name}'. HDRP's Default Volume Profile already contains the maximum of {MaxCustomDiffusionProfiles} custom diffusion profiles.", setup);
                    continue;
                }

                profiles.Add(profile);
                changed = true;
            }

            if (changed)
            {
                profileList.active = true;
                profileList.diffusionProfiles.overrideState = true;
                profileList.diffusionProfiles.value = profiles.ToArray();
                EditorUtility.SetDirty(profileList);
                EditorUtility.SetDirty(defaultVolumeProfile);
            }

            return changed;
        }

        private static bool EnableSubsurfaceScattering()
        {
            HashSet<HDRenderPipelineAsset> pipelineAssets = new HashSet<HDRenderPipelineAsset>();
            AddHDRPAsset(pipelineAssets, GraphicsSettings.defaultRenderPipeline);

            string[] qualityNames = QualitySettings.names;
            for (int i = 0; i < qualityNames.Length; i++)
            {
                AddHDRPAsset(pipelineAssets, QualitySettings.GetRenderPipelineAssetAt(i));
            }

            bool changed = false;
            foreach (HDRenderPipelineAsset pipelineAsset in pipelineAssets)
            {
                RenderPipelineSettings settings = pipelineAsset.currentPlatformRenderPipelineSettings;
                if (settings.supportSubsurfaceScattering)
                {
                    continue;
                }

                settings.supportSubsurfaceScattering = true;
                pipelineAsset.currentPlatformRenderPipelineSettings = settings;
                EditorUtility.SetDirty(pipelineAsset);
                AssetDatabase.SaveAssetIfDirty(pipelineAsset);
                changed = true;
            }

            return changed;
        }

        private static void AddHDRPAsset(HashSet<HDRenderPipelineAsset> assets, RenderPipelineAsset asset)
        {
            if (asset is HDRenderPipelineAsset hdrpAsset)
            {
                assets.Add(hdrpAsset);
            }
        }

        private static void AddUnique(List<DiffusionProfileSettings> profiles, DiffusionProfileSettings profile)
        {
            if (profile != null && !profiles.Contains(profile))
            {
                profiles.Add(profile);
            }
        }

        internal static bool IsConfigured(UMAHDRPSetup setup, out string status)
        {
            if (setup == null)
            {
                status = "UMA HDRP setup is unavailable.";
                return false;
            }

            Shader skinShader = FindShader(UMAHDRPSetup.SkinShaderName, "UMA3_SkinShader_HDRP");
            if (setup.SkinMaterial == null)
            {
                status = "Assign the UMA HDRP skin material.";
                return false;
            }

            if (skinShader == null || setup.SkinMaterial.shader != skinShader)
            {
                status = $"The UMA HDRP skin material is not using '{UMAHDRPSetup.SkinShaderName}'.";
                return false;
            }

            Shader studioShader = FindShader(UMAHDRPSetup.StudioShaderName, "UMA3_Lit(Metal)");
            if (setup.StudioMaterial == null)
            {
                status = "Assign StudioMat.";
                return false;
            }

            if (studioShader == null || setup.StudioMaterial.shader != studioShader)
            {
                status = $"StudioMat is not using '{UMAHDRPSetup.StudioShaderName}'.";
                return false;
            }

            if (setup.DiffusionProfile == null)
            {
                status = "Assign the UMA skin diffusion profile.";
                return false;
            }

            if (!EditorGraphicsSettings.TryGetRenderPipelineSettingsForPipeline<HDRPDefaultVolumeProfileSettings, HDRenderPipeline>(out var volumeSettings) || volumeSettings?.volumeProfile == null)
            {
                status = "HDRP Global Settings does not have a Default Volume Profile.";
                return false;
            }

            if (!volumeSettings.volumeProfile.TryGet(out DiffusionProfileList profileList))
            {
                status = "The Default Volume Profile does not contain a Diffusion Profile List.";
                return false;
            }

            DiffusionProfileSettings[] profiles = profileList.diffusionProfiles.value;
            if (profiles == null || Array.IndexOf(profiles, setup.DiffusionProfile) < 0)
            {
                status = "The UMA skin diffusion profile is not registered.";
                return false;
            }

            status = "UMA's HDRP diffusion profile is registered.";
            return true;
        }
    }

    [CustomEditor(typeof(UMAHDRPSetup))]
    internal class UMAHDRPSetupEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            UMAHDRPSetup setup = (UMAHDRPSetup)target;
            bool configured = UMAHDRPSetupEditorUtility.IsConfigured(setup, out string status);
            EditorGUILayout.HelpBox(status, configured ? MessageType.Info : MessageType.Warning);

            if (GUILayout.Button("Apply UMA HDRP Setup Now"))
            {
                UMAHDRPSetupEditorUtility.Apply(setup, true);
            }
        }
    }

    internal class UMAHDRPSetupAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                string importedAsset = importedAssets[i];
                if (importedAsset.EndsWith($"/{nameof(UMAHDRPSetup)}.prefab", StringComparison.OrdinalIgnoreCase))
                {
                    UMAHDRPSetupEditorUtility.Queue(importedAsset);
                }
                else if (importedAsset.EndsWith("/UMA3_SkinShader_HDRP.mat", StringComparison.OrdinalIgnoreCase) ||
                         importedAsset.EndsWith("/UMA3_SkinShader_HDRP.shadergraph", StringComparison.OrdinalIgnoreCase) ||
                         importedAsset.EndsWith("/StudioMat.mat", StringComparison.OrdinalIgnoreCase) ||
                         importedAsset.EndsWith("/UMA3_Lit(Metal).shadergraph", StringComparison.OrdinalIgnoreCase))
                {
                    EditorApplication.delayCall += UMAHDRPSetupEditorUtility.QueueKnownSetupPrefabs;
                }
            }
        }
    }
}
#pragma warning restore UDR0001
#endif
