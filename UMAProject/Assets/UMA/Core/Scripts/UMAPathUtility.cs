using System;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Compilation;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;
#endif

namespace UMA
{
    /// <summary>
    /// Resolves UMA's read-only installation and project-owned writable locations.
    /// UMA may be imported below Assets or installed as a UPM package.
    /// </summary>
    public static class UMAPathUtility
    {
        public const string PackageName = "com.umasteeringgroup.uma";
        public const string LegacyInstallRoot = "Assets/UMA";
        public const string ShaderPackagesRelativePath = "SRP/ShaderPackages";
        public const string ProjectDataRoot = "Assets/UMAProjectData";
        public const string ProjectResourcesRoot = ProjectDataRoot + "/Resources";
        public const string ProjectEditorResourcesRoot = ProjectDataRoot + "/Editor/Resources";
        public const string WelcomeScenesPath = ProjectEditorResourcesRoot + "/UMAWelcomeScenesProject.asset";
        public const string WelcomeCaptureRoot = ProjectDataRoot + "/Editor/WelcomeScenes";
        public const string GeneratedRoot = ProjectDataRoot + "/Generated";
        public const string GeneratedSlotsRoot = GeneratedRoot + "/Slots";
        public const string GeneratedCharactersRoot = GeneratedRoot + "/Characters";
        public const string GeneratedDecalsRoot = GeneratedRoot + "/DecalStamps";
        public const string GeneratedTPosesRoot = GeneratedRoot + "/TPoses";
        public const string GeneratedExpressionsRoot = GeneratedRoot + "/Expressions";
        public const string ConvertedSlotsRoot = GeneratedRoot + "/ConvertedSlots";
        public const string SlotBackupRoot = ProjectDataRoot + "/SlotBackup";
        public const string ClothingConformerRoot = ProjectDataRoot + "/ClothingConformer";
        public const string TaskRoot = ProjectDataRoot + "/Tasks";
        public const string ExampleAssetsRoot = ProjectDataRoot + "/Examples/ExampleAssets";
        public const string ProjectSettingsPath = ProjectResourcesRoot + "/UMAProjectSettings.asset";
        public const string ProjectIndexerPath = ProjectResourcesRoot + "/AssetIndexerProject.asset";
        public const string OverlayPainterRoot = ProjectDataRoot + "/OverlayPainter";
        public const string OverlayPainterGeneratedRoot = OverlayPainterRoot + "/Generated";
        public const string OverlayPainterRecoveryRoot = OverlayPainterRoot + "/Recovery";

#if UNITY_EDITOR
        private static string installAssetRoot;

        public static string InstallAssetRoot
        {
            get
            {
                if (string.IsNullOrEmpty(installAssetRoot))
                    installAssetRoot = FindInstallAssetRoot();
                return installAssetRoot;
            }
        }

        public static bool IsPackageInstallation =>
            InstallAssetRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

        public static void InvalidateInstallPathCache()
        {
            installAssetRoot = null;
        }

        public static string ResolveInstallAssetPath(string relativePath)
        {
            string relative = Normalize(relativePath).TrimStart('/');
            return string.IsNullOrEmpty(relative) ? InstallAssetRoot : InstallAssetRoot + "/" + relative;
        }

        public static string ResolveLegacyInstallAssetPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            if (normalized.Equals(LegacyInstallRoot, StringComparison.OrdinalIgnoreCase))
                return InstallAssetRoot;
            string prefix = LegacyInstallRoot + "/";
            return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? InstallAssetRoot + normalized.Substring(LegacyInstallRoot.Length)
                : normalized;
        }

        public static string ResolveAbsolutePath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            if (normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                PackageManagerInfo package = PackageManagerInfo.FindForAssetPath(normalized);
                if (package != null)
                {
                    string suffix = normalized.Length > package.assetPath.Length
                        ? normalized.Substring(package.assetPath.Length).TrimStart('/')
                        : string.Empty;
                    return Normalize(Path.Combine(package.resolvedPath, suffix));
                }
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            return Normalize(Path.GetFullPath(Path.Combine(projectRoot, normalized)));
        }

        /// <summary>
        /// Loads a shipped UMA asset from the active installation root. This works
        /// for both an Assets/UMA checkout and a UPM package installation.
        /// </summary>
        public static T LoadInstallAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(
                ResolveInstallAssetPath(relativePath));
        }

        public static bool IsWritableProjectAssetPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            return normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeWritableFolder(string candidate, string fallback)
        {
            string normalized = Normalize(candidate).TrimEnd('/');
            if (!IsWritableProjectAssetPath(normalized) || normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                normalized = Normalize(fallback).TrimEnd('/');
            return normalized;
        }

        public static void EnsureAssetFolder(string folder)
        {
            string normalized = NormalizeWritableFolder(folder, ProjectDataRoot);
            if (AssetDatabase.IsValidFolder(normalized)) return;

            string[] parts = normalized.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string FindInstallAssetRoot()
        {
            PackageManagerInfo package = PackageManagerInfo.FindForAssembly(typeof(UMAPathUtility).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.assetPath))
                return Normalize(package.assetPath).TrimEnd('/');

            string asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName("UMA_Core");
            if (!string.IsNullOrEmpty(asmdefPath))
            {
                string coreFolder = Normalize(Path.GetDirectoryName(asmdefPath));
                string root = Normalize(Path.GetDirectoryName(coreFolder));
                if (!string.IsNullOrEmpty(root)) return root.TrimEnd('/');
            }

            string[] guids = AssetDatabase.FindAssets("UMA_Core t:AssemblyDefinitionAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = Normalize(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (!path.EndsWith("/Core/UMA_Core.asmdef", StringComparison.OrdinalIgnoreCase)) continue;
                return path.Substring(0, path.Length - "/Core/UMA_Core.asmdef".Length);
            }

            return LegacyInstallRoot;
        }
#endif

        public static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
