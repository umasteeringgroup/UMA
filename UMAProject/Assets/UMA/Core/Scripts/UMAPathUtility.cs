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
        public const string Uma3ContentRoot = LegacyInstallRoot + "/UMA3";
        public const string Uma2ContentRoot = LegacyInstallRoot + "/UMA2";
        public const string ProjectSrpRoot = LegacyInstallRoot + "/SRP";
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

        /// <summary>
        /// Resolves editable UMA 3 content. Content packages always install
        /// below Assets, even when UMA Core is installed through UPM.
        /// </summary>
        public static string ResolveUma3ContentPath(string relativePath = "")
        {
            return ResolveContentAssetPath(Uma3ContentRoot, relativePath);
        }

        /// <summary>
        /// Resolves optional editable UMA 2 legacy content.
        /// </summary>
        public static string ResolveUma2ContentPath(string relativePath = "")
        {
            return ResolveContentAssetPath(Uma2ContentRoot, relativePath);
        }

        public static bool IsUma3ContentInstalled =>
            AssetDatabase.IsValidFolder(Uma3ContentRoot);

        public static bool IsUma2ContentInstalled =>
            AssetDatabase.IsValidFolder(Uma2ContentRoot);

        /// <summary>
        /// Returns true for the project-owned UMA trees that remain writable
        /// when Core itself is installed as a read-only UPM package.
        /// </summary>
        public static bool IsProjectOwnedUmaAssetPath(string assetPath)
        {
            string normalized = Normalize(assetPath).TrimEnd('/');
            return IsAtOrBelow(normalized, Uma3ContentRoot) ||
                   IsAtOrBelow(normalized, Uma2ContentRoot) ||
                   IsAtOrBelow(normalized, ProjectSrpRoot);
        }

        private static string ResolveContentAssetPath(string root,
            string relativePath)
        {
            string relative = Normalize(relativePath).Trim('/');
            return string.IsNullOrEmpty(relative) ? root : root + "/" + relative;
        }

        private static bool IsAtOrBelow(string assetPath, string root)
        {
            return assetPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   assetPath.StartsWith(root + "/",
                       StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves installed UMA render-pipeline content. UPM installations keep
        /// the core package read-only and import the selected SRP support package
        /// into Assets/UMA/SRP as a writable project override.
        /// </summary>
        public static string ResolveSrpAssetPath(string relativePath = "")
        {
            string root = AssetDatabase.IsValidFolder(ProjectSrpRoot)
                ? ProjectSrpRoot
                : ResolveInstallAssetPath("SRP");
            string relative = Normalize(relativePath).Trim('/');
            return string.IsNullOrEmpty(relative) ? root : root + "/" + relative;
        }

        public static string ResolveLegacyInstallAssetPath(string assetPath)
        {
            string normalized = Normalize(assetPath);
            const string oldUma2Root = "Assets/UMA2";
            if (normalized.Equals(oldUma2Root, StringComparison.OrdinalIgnoreCase))
                return Uma2ContentRoot;
            if (normalized.StartsWith(oldUma2Root + "/",
                    StringComparison.OrdinalIgnoreCase))
                return Uma2ContentRoot + normalized.Substring(oldUma2Root.Length);
            if (normalized.Equals(Uma3ContentRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(Uma3ContentRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
                return normalized;
            if (normalized.Equals(Uma2ContentRoot,
                    StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(Uma2ContentRoot + "/",
                    StringComparison.OrdinalIgnoreCase))
                return normalized;
            if (normalized.Equals(ProjectSrpRoot,
                    StringComparison.OrdinalIgnoreCase))
                return ResolveSrpAssetPath();
            string srpPrefix = ProjectSrpRoot + "/";
            if (normalized.StartsWith(srpPrefix,
                    StringComparison.OrdinalIgnoreCase))
                return ResolveSrpAssetPath(normalized.Substring(
                    srpPrefix.Length));
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
