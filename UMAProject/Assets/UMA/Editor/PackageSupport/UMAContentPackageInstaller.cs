using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace UMA.Editors.PackageSupport
{
    public enum UMAContentInstallationState
    {
        Missing,
        Unmanaged,
        Installed,
        Installing
    }

    public enum UMAContentConflictPolicy
    {
        Abort,
        AdoptIfUnmanaged,
        BackupAndReplace
    }

    [InitializeOnLoad]
    public static class UMAContentPackageInstaller
    {
        private const string PendingFileName = "pending.json";
        private const string InstalledFileName = "installed.json";

        [Serializable]
        private sealed class PendingImport
        {
            public string contentId;
            public string archivePath;
            public string expectedPackageName;
            public string destinationRoot;
            public string backupRoot;
            public string archiveSha256;
            public string expectedContentVersion;
            public string expectedManifestSha256;
            public string startedUtc;
            public bool hadPreviousContent;
        }

        [Serializable]
        private sealed class InstalledRecord
        {
            public string contentId;
            public string contentVersion;
            public string archiveSha256;
            public string installedUtc;
        }

        private sealed class ChangeAnalysis
        {
            public readonly List<string> conflicts = new List<string>();
            public readonly List<string> reportLines = new List<string>();
            public bool canAdopt;
            public bool hasManagedConflicts;
        }

        static UMAContentPackageInstaller()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageCancelled += OnImportCancelled;
            AssetDatabase.importPackageFailed += OnImportFailed;
            EditorApplication.delayCall += ResumePendingImport;
        }

        [MenuItem("UMA/Content/Install UMA 3 Content...")]
        public static void InstallUma3FromFile() =>
            InstallFromFile(UMAContentKind.Uma3);

        [MenuItem("UMA/Content/Install UMA 2 Legacy Content...")]
        public static void InstallUma2FromFile() =>
            InstallFromFile(UMAContentKind.Uma2);

        public static UMAContentInstallationState GetState(UMAContentKind kind)
        {
            PendingImport pending = LoadPending();
            if (pending != null && string.Equals(pending.contentId,
                    UMAContentCatalog.Id(kind), StringComparison.OrdinalIgnoreCase))
                return UMAContentInstallationState.Installing;
            if (File.Exists(PendingPath))
                return UMAContentInstallationState.Installing;
            if (!AssetDatabase.IsValidFolder(UMAContentCatalog.Root(kind)))
                return UMAContentInstallationState.Missing;
            if (!TryValidateInstalledRequiredPaths(kind,
                    out UMAContentManifest manifest, out _) ||
                !IsCoreVersionCompatible(manifest, out _) ||
                !AreContentDependenciesSatisfied(kind, manifest, out _))
                return UMAContentInstallationState.Unmanaged;
            return UMAContentInstallationState.Installed;
        }

        public static string GetInstalledVersion(UMAContentKind kind)
        {
            return UMAContentPackageArchiveValidator.TryReadInstalledManifest(
                kind, out UMAContentManifest manifest, out _)
                ? manifest.contentVersion
                : string.Empty;
        }

        public static void InstallFromFile(UMAContentKind kind)
        {
            if (LoadPending() != null || File.Exists(PendingPath))
            {
                EditorUtility.DisplayDialog("UMA Content Installation",
                    "Another UMA content installation is still in progress, or its " +
                    "transaction record is unreadable. The saved transaction under " +
                    "Library/UMA/ContentInstaller was left untouched for recovery.", "OK");
                return;
            }
            string archivePath = EditorUtility.OpenFilePanel(
                "Select " + UMAContentCatalog.DisplayName(kind) + " Package",
                string.Empty, "unitypackage");
            if (string.IsNullOrEmpty(archivePath))
                return;

            if (!UMAContentPackageArchiveValidator.TryValidate(archivePath, kind,
                    out UMAContentPackageArchiveInfo archive, out string error))
            {
                EditorUtility.DisplayDialog("Invalid UMA Content Package", error, "OK");
                return;
            }
            if (!IsCoreVersionCompatible(archive.Manifest, out error))
            {
                EditorUtility.DisplayDialog("Incompatible UMA Content Package", error, "OK");
                return;
            }
            if (!AreContentDependenciesSatisfied(kind, archive.Manifest, out error))
            {
                EditorUtility.DisplayDialog("UMA Content Dependency Required", error, "OK");
                return;
            }
            if (kind == UMAContentKind.Uma2 &&
                !MoveLegacyUma2TreeIfNeeded(true, out _))
                return;

            ChangeAnalysis analysis = AnalyzeLocalChanges(kind, archive.Manifest,
                archive.Archive);
            if (analysis.canAdopt)
            {
                if (!EditorUtility.DisplayDialog("Adopt Existing UMA Content?",
                        "Every archive-owned path in the existing " +
                        UMAContentCatalog.DisplayName(kind) +
                        " tree has the expected GUID and file hash. UMA can adopt it " +
                        "without replacing content. Any additional project files remain " +
                        "untouched and will be reported on future updates.",
                        "Adopt", "Cancel"))
                    return;
                if (analysis.conflicts.Count > 0)
                    WriteChangeReport(kind, archive.Manifest, analysis);
                AdoptExisting(kind, archive);
                return;
            }

            string action = AssetDatabase.IsValidFolder(UMAContentCatalog.Root(kind))
                ? "replace the existing content"
                : "install project-owned content";
            if (analysis.conflicts.Count > 0)
            {
                string reportPath = WriteChangeReport(kind, archive.Manifest, analysis);
                int choice = EditorUtility.DisplayDialogComplex(
                    "Local UMA Content Changes Detected",
                    analysis.conflicts.Count +
                    " locally changed, added, or deleted path(s) were found. " +
                    "The default is to cancel and leave the project unchanged. " +
                    "Every affected path is listed in:\n\n" + reportPath +
                    "\n\nBackup and Replace retains the current tree under " +
                    "Library/UMA/ContentInstaller before importing.",
                    "Cancel", "Review Report", "Back Up and Replace");
                if (choice == 1)
                    EditorUtility.RevealInFinder(reportPath);
                if (choice != 2)
                    return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Install " + UMAContentCatalog.DisplayName(kind) + "?",
                    "This will " + action + " at " +
                    UMAContentCatalog.Root(kind) + ".",
                    analysis.conflicts.Count == 0 ? "Install" : "Continue",
                    "Cancel"))
                return;

            BeginImport(kind, archivePath, archive, out _);
        }

        public static bool InstallFromFileForAutomation(UMAContentKind kind,
            string archivePath, UMAContentConflictPolicy conflictPolicy,
            out string error)
        {
            error = string.Empty;
            if (LoadPending() != null || File.Exists(PendingPath))
            {
                error = "Another UMA content installation is still in progress, or " +
                        "its saved transaction record is unreadable. No files were changed.";
                return false;
            }
            if (!UMAContentPackageArchiveValidator.TryValidate(archivePath, kind,
                    out UMAContentPackageArchiveInfo archive, out error) ||
                !IsCoreVersionCompatible(archive.Manifest, out error) ||
                !AreContentDependenciesSatisfied(kind, archive.Manifest, out error))
                return false;
            if (kind == UMAContentKind.Uma2 &&
                AssetDatabase.IsValidFolder("Assets/UMA2") &&
                !AssetDatabase.IsValidFolder(UMAContentCatalog.Root(kind)) &&
                conflictPolicy != UMAContentConflictPolicy.BackupAndReplace)
            {
                error = "Legacy Assets/UMA2 content requires an explicit " +
                        "BackupAndReplace migration policy. No files were changed.";
                return false;
            }
            if (kind == UMAContentKind.Uma2 &&
                !MoveLegacyUma2TreeIfNeeded(false, out error))
                return false;

            ChangeAnalysis analysis = AnalyzeLocalChanges(kind, archive.Manifest,
                archive.Archive);
            if (analysis.canAdopt &&
                conflictPolicy == UMAContentConflictPolicy.AdoptIfUnmanaged)
            {
                if (analysis.conflicts.Count > 0)
                    WriteChangeReport(kind, archive.Manifest, analysis);
                AdoptExisting(kind, archive);
                return true;
            }
            if (analysis.canAdopt &&
                conflictPolicy == UMAContentConflictPolicy.Abort)
            {
                string reportPath = WriteChangeReport(kind, archive.Manifest, analysis);
                error = "Matching unmanaged content was found. No files were changed. " +
                        "Use AdoptIfUnmanaged to record archive ownership. Report: " +
                        reportPath;
                return false;
            }
            if (analysis.conflicts.Count > 0 &&
                conflictPolicy != UMAContentConflictPolicy.BackupAndReplace)
            {
                string reportPath = WriteChangeReport(kind, archive.Manifest, analysis);
                error = analysis.conflicts.Count +
                        " local content conflict(s) were detected. No files were changed. " +
                        "Report: " + reportPath;
                return false;
            }
            return BeginImport(kind, archivePath, archive, out error);
        }

        private static bool MoveLegacyUma2TreeIfNeeded(bool interactive,
            out string error)
        {
            error = string.Empty;
            const string legacyRoot = "Assets/UMA2";
            string targetRoot = UMAContentCatalog.Root(UMAContentKind.Uma2);
            if (!AssetDatabase.IsValidFolder(legacyRoot))
                return true;
            if (AssetDatabase.IsValidFolder(targetRoot))
            {
                error = "Both " + legacyRoot + " and " + targetRoot +
                        " exist. Consolidate them before installing UMA2 Content; " +
                        "UMA will not guess which tree owns your edits.";
                if (interactive)
                    EditorUtility.DisplayDialog("Two UMA2 Content Trees Found", error, "OK");
                return false;
            }
            if (interactive && !EditorUtility.DisplayDialog("Move Legacy UMA2 Content?",
                    "UMA found the old editable content tree at " + legacyRoot +
                    ". It must move to " + targetRoot +
                    " before it can be adopted or updated. All assets and .meta " +
                    "files will move together, preserving GUIDs and local edits.",
                    "Move Content", "Cancel"))
                return false;

            if (!AssetDatabase.IsValidFolder("Assets/UMA"))
            {
                string folderGuid = AssetDatabase.CreateFolder("Assets", "UMA");
                if (string.IsNullOrEmpty(folderGuid))
                {
                    error = "Could not create Assets/UMA.";
                    if (interactive)
                        EditorUtility.DisplayDialog("UMA2 Move Failed", error, "OK");
                    return false;
                }
            }
            string moveError = AssetDatabase.MoveAsset(legacyRoot, targetRoot);
            if (!string.IsNullOrEmpty(moveError))
            {
                error = moveError;
                if (interactive)
                    EditorUtility.DisplayDialog("UMA2 Move Failed", error, "OK");
                return false;
            }
            AssetDatabase.Refresh();
            return true;
        }

        private static bool IsCoreVersionCompatible(UMAContentManifest manifest,
            out string error)
        {
            error = string.Empty;
            string installedVersion = GetInstalledCoreVersion();
            if (string.IsNullOrEmpty(installedVersion))
            {
                error = "This archive requires a known UMA Core version, but the " +
                        "installed version could not be read.";
                return false;
            }

            if (manifest.formatVersion == 1)
            {
                if (string.Equals(installedVersion, manifest.requiredCoreVersion,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
                error = "This legacy archive requires UMA Core " +
                        manifest.requiredCoreVersion + ", but the project has " +
                        installedVersion + ".";
                return false;
            }

            if (!TryParseSemanticVersion(installedVersion, out Version installed) ||
                !TryParseSemanticVersion(manifest.minimumCoreVersion, out Version minimum) ||
                !TryParseSemanticVersion(manifest.maximumCoreVersionExclusive,
                    out Version maximum))
            {
                error = "The installed Core version or archive compatibility range is invalid.";
                return false;
            }
            if (installed.CompareTo(minimum) >= 0 && installed.CompareTo(maximum) < 0)
                return true;
            error = "This archive supports UMA Core " + manifest.minimumCoreVersion +
                    " up to (but not including) " +
                    manifest.maximumCoreVersionExclusive + ", but the project has " +
                    installedVersion + ".";
            return false;
        }

        private static string GetInstalledCoreVersion()
        {
            string installedVersion = string.Empty;
            PackageManagerInfo package = PackageManagerInfo.FindForAssembly(
                typeof(UMAPathUtility).Assembly);
            if (package != null)
                installedVersion = package.version;
            if (string.IsNullOrEmpty(installedVersion))
            {
                string manifestPath = UMAPathUtility.ResolveAbsolutePath(
                    UMAPathUtility.ResolveInstallAssetPath("package.json"));
                if (File.Exists(manifestPath))
                {
                    string json = File.ReadAllText(manifestPath);
                    var match = System.Text.RegularExpressions.Regex.Match(json,
                        "\\\"version\\\"\\s*:\\s*\\\"(?<value>[^\\\"]+)\\\"");
                    if (match.Success)
                        installedVersion = match.Groups["value"].Value;
                }
            }
            return installedVersion;
        }

        private static bool TryParseSemanticVersion(string value, out Version version)
        {
            version = null;
            var match = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty,
                @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-+].*)?$");
            return match.Success && Version.TryParse(
                match.Groups["major"].Value + "." + match.Groups["minor"].Value +
                "." + match.Groups["patch"].Value, out version);
        }

        private static bool AreContentDependenciesSatisfied(UMAContentKind kind,
            UMAContentManifest incoming, out string error)
        {
            if (kind == UMAContentKind.Uma3)
            {
                if (TryGetInstalledSrpSupport(out _, out error))
                    return true;
                error = "Install exactly one valid UMA URP or UMA HDRP support package " +
                        "before installing UMA 3 Content. " + error;
                return false;
            }

            if (!TryValidateInstalledRequiredPaths(UMAContentKind.Uma3,
                    out UMAContentManifest uma3, out error))
            {
                error = "Install and validate UMA 3 Content before installing UMA 2 " +
                        "Legacy Content. " + error;
                return false;
            }
            if (!IsCoreVersionCompatible(uma3, out error))
            {
                error = "The installed UMA 3 Content is not compatible with the " +
                        "current Core version. " + error;
                return false;
            }
            if (!TryGetInstalledSrpSupport(out _, out error))
            {
                error = "The installed UMA 3 Content dependency is incomplete because " +
                        "no single valid UMA render-pipeline support package is selected. " +
                        error;
                return false;
            }
            if (!string.Equals(uma3.contentVersion, incoming.contentVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "UMA 2 Legacy Content " + incoming.contentVersion +
                        " requires the matching UMA 3 Content version, but " +
                        uma3.contentVersion + " is installed.";
                return false;
            }
            return true;
        }

        public static bool TryGetInstalledSrpSupport(out string pipeline,
            out string error)
        {
            bool urp = UMASrpPackageArchiveValidator.TryValidateInstalledSupport(
                "URP", out _);
            bool hdrp = UMASrpPackageArchiveValidator.TryValidateInstalledSupport(
                "HDRP", out _);
            pipeline = urp == hdrp ? string.Empty : urp ? "URP" : "HDRP";
            if (!string.IsNullOrEmpty(pipeline))
            {
                error = string.Empty;
                return true;
            }
            error = urp && hdrp
                ? "Both pipeline manifests are present; select one pipeline."
                : "No valid installed pipeline manifest was found at Assets/UMA/SRP.";
            return false;
        }

        public static bool TryValidateInstalledRequiredPaths(UMAContentKind kind,
            out UMAContentManifest manifest, out string error)
        {
            if (!UMAContentPackageArchiveValidator.TryReadInstalledManifest(kind,
                    out manifest, out error))
                return false;
            foreach (string required in manifest.requiredPaths ?? Array.Empty<string>())
            {
                string absolute = UMAPathUtility.ResolveAbsolutePath(required);
                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                {
                    error = "Installed content is missing required path " + required + ".";
                    manifest = null;
                    return false;
                }
            }
            return true;
        }

        private static ChangeAnalysis AnalyzeLocalChanges(UMAContentKind kind,
            UMAContentManifest incomingManifest, UMASrpPackageArchiveInfo incoming)
        {
            var analysis = new ChangeAnalysis();
            string root = UMAContentCatalog.Root(kind);
            if (!AssetDatabase.IsValidFolder(root))
                return analysis;

            bool hasInstalledManifest =
                UMAContentPackageArchiveValidator.TryReadInstalledManifest(kind,
                    out UMAContentManifest installed, out _);
            UMAContentManifest baseline = hasInstalledManifest
                ? installed
                : incomingManifest;
            var baselineByPath = (baseline.assets ?? Array.Empty<UMAContentManifestAsset>())
                .ToDictionary(asset => asset.path, StringComparer.OrdinalIgnoreCase);
            var incomingByPath = (incomingManifest.assets ??
                    Array.Empty<UMAContentManifestAsset>())
                .ToDictionary(asset => asset.path, StringComparer.OrdinalIgnoreCase);
            var expectedDiskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UMAContentManifestAsset asset in baseline.assets ??
                     Array.Empty<UMAContentManifestAsset>())
            {
                expectedDiskPaths.Add(asset.path);
                expectedDiskPaths.Add(asset.path + ".meta");
                string absolute = UMAPathUtility.ResolveAbsolutePath(asset.path);
                string localStatus = "unchanged";
                if (asset.bytes == 0)
                {
                    if (!Directory.Exists(absolute))
                    {
                        localStatus = "locally deleted";
                        analysis.conflicts.Add(asset.path + " (deleted)");
                        analysis.hasManagedConflicts = true;
                    }
                }
                else if (!File.Exists(absolute))
                {
                    localStatus = "locally deleted";
                    analysis.conflicts.Add(asset.path + " (deleted)");
                    analysis.hasManagedConflicts = true;
                }
                else if (new FileInfo(absolute).Length != asset.bytes ||
                    !string.Equals(ComputeFileHash(absolute), asset.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    localStatus = "locally modified";
                    analysis.conflicts.Add(asset.path + " (modified)");
                    analysis.hasManagedConflicts = true;
                }

                string metaPath = absolute + ".meta";
                if (!File.Exists(metaPath) ||
                    new FileInfo(metaPath).Length != asset.metaBytes ||
                    !string.Equals(ComputeFileHash(metaPath), asset.metaSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    localStatus = localStatus == "unchanged"
                        ? "locally modified importer"
                        : localStatus + "; locally modified importer";
                    analysis.conflicts.Add(asset.path + ".meta (modified or deleted)");
                    analysis.hasManagedConflicts = true;
                }

                string upstreamStatus = "not managed";
                if (hasInstalledManifest)
                {
                    if (!incomingByPath.TryGetValue(asset.path,
                            out UMAContentManifestAsset incomingAsset))
                        upstreamStatus = "upstream removed";
                    else
                        upstreamStatus = ManifestAssetMatches(asset, incomingAsset)
                            ? "upstream unchanged"
                            : "upstream changed";
                }
                analysis.reportLines.Add(asset.path + "\t" + localStatus + "\t" +
                                         upstreamStatus);
            }

            if (hasInstalledManifest)
                foreach (UMAContentManifestAsset asset in incomingManifest.assets ??
                         Array.Empty<UMAContentManifestAsset>())
                    if (!baselineByPath.ContainsKey(asset.path))
                        analysis.reportLines.Add(asset.path +
                                                 "\tlocally absent\tupstream added");

            string absoluteRoot = UMAPathUtility.ResolveAbsolutePath(root);
            if (Directory.Exists(absoluteRoot))
            {
                var currentPaths = new List<string>();
                currentPaths.AddRange(Directory.GetDirectories(absoluteRoot, "*",
                    SearchOption.AllDirectories).Select(ToAssetPath));
                currentPaths.AddRange(Directory.GetFiles(absoluteRoot, "*",
                    SearchOption.AllDirectories).Select(ToAssetPath));
                foreach (string assetPath in currentPaths)
                {
                    if (string.Equals(assetPath,
                            UMAContentCatalog.ManifestPath(kind),
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(assetPath,
                            UMAContentCatalog.ManifestPath(kind) + ".meta",
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!expectedDiskPaths.Contains(assetPath))
                    {
                        analysis.conflicts.Add(assetPath + " (added/untracked)");
                        analysis.reportLines.Add(assetPath +
                                                 "\tlocally added\tupstream unmanaged");
                    }
                }
            }

            bool guidsMatch = true;
            foreach (KeyValuePair<string, string> pair in incoming.GuidByPath)
            {
                if (string.Equals(pair.Key, UMAContentCatalog.ManifestPath(kind),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(AssetDatabase.AssetPathToGUID(pair.Key), pair.Value,
                        StringComparison.OrdinalIgnoreCase))
                {
                    guidsMatch = false;
                    break;
                }
            }
            analysis.conflicts.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = analysis.conflicts.Count - 1; i > 0; i--)
                if (string.Equals(analysis.conflicts[i], analysis.conflicts[i - 1],
                        StringComparison.OrdinalIgnoreCase))
                    analysis.conflicts.RemoveAt(i);
            analysis.reportLines.Sort(StringComparer.OrdinalIgnoreCase);
            analysis.canAdopt = !hasInstalledManifest &&
                                !analysis.hasManagedConflicts && guidsMatch;
            return analysis;
        }

        private static bool ManifestAssetMatches(UMAContentManifestAsset left,
            UMAContentManifestAsset right)
        {
            return left != null && right != null &&
                   string.Equals(left.guid, right.guid,
                       StringComparison.OrdinalIgnoreCase) &&
                   left.bytes == right.bytes && left.metaBytes == right.metaBytes &&
                   string.Equals(left.sha256, right.sha256,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.metaSha256, right.metaSha256,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string WriteChangeReport(UMAContentKind kind,
            UMAContentManifest incoming, ChangeAnalysis analysis)
        {
            string directory = Path.Combine(InstallerRoot, UMAContentCatalog.Id(kind));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "LastChangeReport.txt");
            var lines = new List<string>
            {
                "UMA content change report",
                "Content: " + UMAContentCatalog.DisplayName(kind),
                "Incoming version: " + incoming.contentVersion,
                "Generated UTC: " + DateTime.UtcNow.ToString("O"),
                "Conflicts: " + analysis.conflicts.Count,
                string.Empty,
                "CONFLICTS"
            };
            lines.AddRange(analysis.conflicts);
            lines.Add(string.Empty);
            lines.Add("PATH CLASSIFICATION (path, local state, upstream state)");
            lines.AddRange(analysis.reportLines);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return path;
        }

        private static void AdoptExisting(UMAContentKind kind,
            UMAContentPackageArchiveInfo archive)
        {
            string manifestPath = UMAContentCatalog.ManifestPath(kind);
            string absolutePath = UMAPathUtility.ResolveAbsolutePath(manifestPath);
            string json = archive.Archive.TextByPath[manifestPath];
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
            File.WriteAllText(absolutePath, json, new UTF8Encoding(false));
            File.WriteAllText(absolutePath + ".meta",
                "fileFormatVersion: 2\n" +
                "guid: " + archive.Archive.GuidByPath[manifestPath] + "\n" +
                "DefaultImporter:\n  externalObjects: {}\n  userData: \n" +
                "  assetBundleName: \n  assetBundleVariant: \n",
                new UTF8Encoding(false));
            AssetDatabase.Refresh();
            WriteInstalledRecord(kind, archive.Manifest, string.Empty);
            ClearLastError(kind);
            RebuildGlobalLibrary();
            Debug.Log("[UMA] Adopted existing " +
                UMAContentCatalog.DisplayName(kind) + " at " +
                UMAContentCatalog.Root(kind) + ".");
        }

        private static bool BeginImport(UMAContentKind kind, string archivePath,
            UMAContentPackageArchiveInfo archive, out string error)
        {
            error = string.Empty;
            if (archive == null || !archive.Archive.AssetSha256ByPath.TryGetValue(
                    UMAContentCatalog.ManifestPath(kind),
                    out string expectedManifestHash))
            {
                error = "The validated content archive has no manifest hash.";
                return false;
            }
            string transactionRoot = TransactionRoot(kind);
            string backupRoot = Path.Combine(transactionRoot, "CurrentBackup");
            string archiveCopy = Path.Combine(transactionRoot,
                Path.GetFileName(archivePath));
            DeleteDirectory(transactionRoot);
            Directory.CreateDirectory(transactionRoot);
            ClearLastError(kind);

            string destinationRoot = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.Root(kind));
            bool hadPrevious = Directory.Exists(destinationRoot);
            try
            {
                ThrowIfReparsePoint(destinationRoot,
                    "UMA content destination");
                if (hadPrevious)
                {
                    if (!File.Exists(destinationRoot + ".meta"))
                        throw new InvalidDataException(
                            "The existing content root has no recoverable folder metadata: " +
                            destinationRoot + ".meta");
                    CopyDirectory(destinationRoot, backupRoot);
                    File.Copy(destinationRoot + ".meta",
                        backupRoot + ".root.meta", true);
                }
                File.Copy(archivePath, archiveCopy, true);
                var pending = new PendingImport
                {
                    contentId = UMAContentCatalog.Id(kind),
                    archivePath = archiveCopy,
                    expectedPackageName = Path.GetFileNameWithoutExtension(archiveCopy),
                    destinationRoot = destinationRoot,
                    backupRoot = backupRoot,
                    archiveSha256 = ComputeFileHash(archiveCopy),
                    expectedContentVersion = archive.Manifest.contentVersion,
                    expectedManifestSha256 = expectedManifestHash,
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    hadPreviousContent = hadPrevious
                };
                SavePending(pending);
                DeleteContentRoot(kind);
                RestoreRootIdentity(pending);
                AssetDatabase.ImportPackage(archiveCopy, false);
                SchedulePendingCompletion();
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                PendingImport pending = LoadPending();
                if (pending != null)
                    Rollback(pending, exception.Message, true);
                else
                {
                    WriteLastError(kind, exception.Message);
                    Debug.LogError("[UMA] Content installation failed: " +
                        exception.Message);
                }
                return false;
            }
        }

        private static void OnImportCompleted(string packageName)
        {
            PendingImport pending = LoadPending();
            if (Matches(pending, packageName))
                SchedulePendingCompletion();
        }

        private static void OnImportCancelled(string packageName)
        {
            PendingImport pending = LoadPending();
            if (Matches(pending, packageName))
                Rollback(pending, "The content import was cancelled.", true);
        }

        private static void OnImportFailed(string packageName, string errorMessage)
        {
            PendingImport pending = LoadPending();
            if (Matches(pending, packageName))
                Rollback(pending, "The content import failed: " + errorMessage, true);
        }

        private static void ResumePendingImport()
        {
            if (LoadPending() != null)
                SchedulePendingCompletion();
        }

        private static void SchedulePendingCompletion()
        {
            EditorApplication.update -= CompletePendingImport;
            EditorApplication.update += CompletePendingImport;
        }

        private static void CompletePendingImport()
        {
            PendingImport pending = LoadPending();
            if (pending == null)
            {
                EditorApplication.update -= CompletePendingImport;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            UMAContentKind kind = ParseKind(pending.contentId);
            if (!UMAContentPackageArchiveValidator.TryReadInstalledManifest(kind,
                    out UMAContentManifest manifest, out string error) ||
                !ValidatePendingArchive(pending, out error) ||
                !ValidateExpectedManifest(kind, pending, manifest, out error) ||
                !ValidateInstalledFiles(manifest, out error) ||
                !ValidatePreservedRootIdentity(pending, out error))
            {
                if (DateTime.TryParse(pending.startedUtc, out DateTime started) &&
                    (DateTime.UtcNow - started.ToUniversalTime()).TotalMinutes < 5)
                    return;
                EditorApplication.update -= CompletePendingImport;
                Rollback(pending, error, true);
                return;
            }

            EditorApplication.update -= CompletePendingImport;
            WriteInstalledRecord(kind, manifest, pending.archiveSha256);
            ClearLastError(kind);
            PreservePreviousBackup(kind, pending);
            DeletePending();
            AssetDatabase.Refresh();
            RebuildGlobalLibrary();
            Debug.Log("[UMA] Installed " + UMAContentCatalog.DisplayName(kind) +
                " " + manifest.contentVersion + " at " + manifest.installRoot + ".");
        }

        private static bool ValidateExpectedManifest(UMAContentKind kind,
            PendingImport pending, UMAContentManifest manifest, out string error)
        {
            string manifestPath = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.ManifestPath(kind));
            if (manifest == null ||
                !string.Equals(manifest.contentVersion,
                    pending.expectedContentVersion, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(pending.expectedManifestSha256) ||
                !File.Exists(manifestPath) ||
                !string.Equals(ComputeFileHash(manifestPath),
                    pending.expectedManifestSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "The installed content manifest does not match the selected archive.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidatePendingArchive(PendingImport pending,
            out string error)
        {
            if (pending == null || !File.Exists(pending.archivePath) ||
                !string.Equals(ComputeFileHash(pending.archivePath),
                    pending.archiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "The saved content archive no longer matches the selected file.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static bool ValidateInstalledFiles(UMAContentManifest manifest,
            out string error)
        {
            foreach (string path in manifest.requiredPaths ?? Array.Empty<string>())
            {
                string absolute = UMAPathUtility.ResolveAbsolutePath(path);
                if (!File.Exists(absolute) && !Directory.Exists(absolute))
                {
                    error = "Installed content is missing required path " + path + ".";
                    return false;
                }
            }
            foreach (UMAContentManifestAsset asset in manifest.assets ??
                     Array.Empty<UMAContentManifestAsset>())
            {
                string absolute = UMAPathUtility.ResolveAbsolutePath(asset.path);
                if (asset.bytes == 0)
                {
                    if (!Directory.Exists(absolute))
                    {
                        error = "Installed content is missing folder " + asset.path;
                        return false;
                    }
                }
                else if (!File.Exists(absolute) ||
                    new FileInfo(absolute).Length != asset.bytes ||
                    !string.Equals(ComputeFileHash(absolute), asset.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Installed content does not match the archive: " + asset.path;
                    return false;
                }
                string metaPath = absolute + ".meta";
                if (!File.Exists(metaPath) ||
                    new FileInfo(metaPath).Length != asset.metaBytes ||
                    !string.Equals(ComputeFileHash(metaPath), asset.metaSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Installed importer settings do not match the archive: " +
                            asset.path + ".meta";
                    return false;
                }
            }
            error = string.Empty;
            return true;
        }

        private static void RestoreRootIdentity(PendingImport pending)
        {
            if (!pending.hadPreviousContent)
                return;
            string rootMeta = pending.backupRoot + ".root.meta";
            if (!File.Exists(rootMeta))
                throw new InvalidDataException(
                    "The existing content root has no recoverable folder metadata.");
            string destinationRoot = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.Root(ParseKind(pending.contentId)));
            Directory.CreateDirectory(destinationRoot);
            File.Copy(rootMeta, destinationRoot + ".meta", true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static bool ValidatePreservedRootIdentity(PendingImport pending,
            out string error)
        {
            error = string.Empty;
            if (!pending.hadPreviousContent)
                return true;
            string backupMeta = pending.backupRoot + ".root.meta";
            string installedMeta = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.Root(ParseKind(pending.contentId))) + ".meta";
            if (!File.Exists(backupMeta) || !File.Exists(installedMeta) ||
                !string.Equals(ComputeFileHash(backupMeta),
                    ComputeFileHash(installedMeta),
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The content-root folder GUID was not preserved during the update.";
                return false;
            }
            return true;
        }

        private static void Rollback(PendingImport pending, string reason,
            bool logError)
        {
            if (!TryValidatePending(pending, out string pendingError))
            {
                WritePendingValidationError(reason + " " + pendingError, logError);
                return;
            }
            UMAContentKind kind = ParseKind(pending.contentId);
            string destinationRoot = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.Root(kind));
            bool restored = false;
            try
            {
                if (pending.hadPreviousContent &&
                    (!Directory.Exists(pending.backupRoot) ||
                     !File.Exists(pending.backupRoot + ".root.meta")))
                    throw new InvalidDataException(
                        "The saved UMA content backup or its root metadata is missing.");
                DeleteContentRoot(kind);
                if (pending.hadPreviousContent)
                {
                    CopyDirectory(pending.backupRoot, destinationRoot);
                    string rootMeta = pending.backupRoot + ".root.meta";
                    File.Copy(rootMeta, destinationRoot + ".meta", true);
                }
                restored = true;
            }
            catch (Exception exception)
            {
                reason += " Rollback also failed: " + exception.Message;
            }
            finally
            {
                try { WriteLastError(kind, reason); }
                catch { /* Preserve the original rollback result. */ }
                if (restored) DeletePending();
                AssetDatabase.Refresh();
            }
            if (logError) Debug.LogError("[UMA] " + reason);
        }

        private static void DeleteContentRoot(UMAContentKind kind)
        {
            string assetRoot = UMAContentCatalog.Root(kind);
            string absolute = UMAPathUtility.ResolveAbsolutePath(assetRoot);
            string expected = Path.GetFullPath(Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
                assetRoot));
            if (!string.Equals(Path.GetFullPath(absolute), expected,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unsafe UMA content delete target: " + absolute);
            ThrowIfReparsePoint(absolute, "UMA content delete target");
            ThrowIfTreeContainsReparsePoint(absolute,
                "UMA content delete target");
            if (AssetDatabase.IsValidFolder(assetRoot))
                AssetDatabase.DeleteAsset(assetRoot);
            if (Directory.Exists(absolute))
                Directory.Delete(absolute, true);
            if (File.Exists(absolute + ".meta")) File.Delete(absolute + ".meta");
        }

        private static void RebuildGlobalLibrary()
        {
            try
            {
                UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
                if (indexer != null) indexer.RebuildLibrary();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UMA] Content installed, but the Global Library " +
                    "could not be rebuilt automatically: " + exception.Message);
            }
        }

        private static bool Matches(PendingImport pending, string packageName)
        {
            return pending != null && string.Equals(
                Path.GetFileNameWithoutExtension((packageName ?? string.Empty)
                    .Replace('\\', '/')), pending.expectedPackageName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static UMAContentKind ParseKind(string id) =>
            string.Equals(id, "uma2", StringComparison.OrdinalIgnoreCase)
                ? UMAContentKind.Uma2
                : string.Equals(id, "uma3", StringComparison.OrdinalIgnoreCase)
                    ? UMAContentKind.Uma3
                    : throw new InvalidDataException(
                        "Unknown UMA content transaction identity: " + id);

        private static string ProjectRoot =>
            Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;

        private static string InstallerRoot =>
            Path.Combine(ProjectRoot, "Library", "UMA", "ContentInstaller");

        private static string TransactionRoot(UMAContentKind kind) =>
            Path.Combine(InstallerRoot, UMAContentCatalog.Id(kind), "Transaction");

        private static string LastErrorPath(UMAContentKind kind) =>
            Path.Combine(InstallerRoot, UMAContentCatalog.Id(kind), "LastError.txt");

        private static void WriteLastError(UMAContentKind kind, string error)
        {
            string path = LastErrorPath(kind);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? InstallerRoot);
            File.WriteAllText(path, error ?? "Unknown content installation error.",
                new UTF8Encoding(false));
        }

        private static void ClearLastError(UMAContentKind kind)
        {
            string path = LastErrorPath(kind);
            if (File.Exists(path)) File.Delete(path);
        }

        private static string PendingPath =>
            Path.Combine(InstallerRoot, PendingFileName);

        private static void SavePending(PendingImport pending)
        {
            Directory.CreateDirectory(InstallerRoot);
            if (!TryValidatePending(pending, out string error))
                throw new InvalidDataException(error);
            if (File.Exists(PendingPath))
                throw new IOException("A UMA content transaction record already exists.");
            string temporary = PendingPath + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(pending, true),
                    new UTF8Encoding(false));
                File.Move(temporary, PendingPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static PendingImport LoadPending()
        {
            try
            {
                if (!File.Exists(PendingPath)) return null;
                PendingImport pending = JsonUtility.FromJson<PendingImport>(
                    File.ReadAllText(PendingPath));
                return TryValidatePending(pending, out _) ? pending : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryValidatePending(PendingImport pending,
            out string error)
        {
            error = string.Empty;
            if (pending == null ||
                (!string.Equals(pending.contentId, "uma3",
                     StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(pending.contentId, "uma2",
                     StringComparison.OrdinalIgnoreCase)))
            {
                error = "The UMA content transaction has an invalid identity.";
                return false;
            }

            UMAContentKind kind = ParseKind(pending.contentId);
            string expectedTransaction = TransactionRoot(kind);
            string expectedBackup = Path.Combine(expectedTransaction, "CurrentBackup");
            string expectedDestination = UMAPathUtility.ResolveAbsolutePath(
                UMAContentCatalog.Root(kind));
            string archivePath;
            try
            {
                archivePath = Path.GetFullPath(pending.archivePath ?? string.Empty);
            }
            catch (Exception)
            {
                error = "The UMA content transaction has an invalid archive path.";
                return false;
            }
            string archiveParent = Path.GetDirectoryName(archivePath) ?? string.Empty;
            string archiveName = Path.GetFileNameWithoutExtension(archivePath);
            if (!SameFullPath(pending.backupRoot, expectedBackup) ||
                !SameFullPath(pending.destinationRoot, expectedDestination) ||
                !SameFullPath(archiveParent, expectedTransaction) ||
                !string.Equals(Path.GetExtension(archivePath), ".unitypackage",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(archiveName, pending.expectedPackageName,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsHex(pending.archiveSha256, 64) ||
                !IsHex(pending.expectedManifestSha256, 64) ||
                !TryParseSemanticVersion(pending.expectedContentVersion, out _) ||
                !DateTime.TryParse(pending.startedUtc, out _))
            {
                error = "The UMA content transaction contains unsafe or incomplete paths " +
                        "or integrity metadata. Its backup was preserved.";
                return false;
            }
            return true;
        }

        private static bool SameFullPath(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(right ?? string.Empty)
                        .TrimEnd(Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsHex(string value, int length) =>
            !string.IsNullOrEmpty(value) && value.Length == length &&
            value.All(Uri.IsHexDigit);

        private static void WritePendingValidationError(string reason,
            bool logError)
        {
            string message = "UMA left an unreadable content transaction and its backup " +
                             "untouched. " + reason;
            if (logError) Debug.LogError("[UMA] " + message);
            else Debug.LogWarning("[UMA] " + message);
        }

        private static void DeletePending()
        {
            if (File.Exists(PendingPath)) File.Delete(PendingPath);
        }

        private static void WriteInstalledRecord(UMAContentKind kind,
            UMAContentManifest manifest, string archiveHash)
        {
            string root = Path.Combine(InstallerRoot, UMAContentCatalog.Id(kind));
            Directory.CreateDirectory(root);
            var record = new InstalledRecord
            {
                contentId = manifest.contentId,
                contentVersion = manifest.contentVersion,
                archiveSha256 = archiveHash,
                installedUtc = DateTime.UtcNow.ToString("O")
            };
            File.WriteAllText(Path.Combine(root, InstalledFileName),
                JsonUtility.ToJson(record, true), new UTF8Encoding(false));
        }

        private static void PreservePreviousBackup(UMAContentKind kind,
            PendingImport pending)
        {
            string root = Path.Combine(InstallerRoot, UMAContentCatalog.Id(kind));
            string previous = Path.Combine(root, "PreviousBackup");
            string previousRootMeta = previous + ".root.meta";
            if (pending.hadPreviousContent)
            {
                string currentRootMeta = pending.backupRoot + ".root.meta";
                if (!Directory.Exists(pending.backupRoot) ||
                    !File.Exists(currentRootMeta))
                    throw new InvalidDataException(
                        "The current content backup is incomplete; the transaction " +
                        "record was retained.");

                string suffix = ".retired-" + Guid.NewGuid().ToString("N");
                string retired = previous + suffix;
                string retiredRootMeta = previousRootMeta + suffix;
                bool retiredDirectory = false;
                bool retiredMeta = false;
                bool promotedDirectory = false;
                bool promotedMeta = false;
                try
                {
                    if (Directory.Exists(previous))
                    {
                        ThrowIfTreeContainsReparsePoint(previous,
                            "Previous UMA content backup");
                        Directory.Move(previous, retired);
                        retiredDirectory = true;
                    }
                    if (File.Exists(previousRootMeta))
                    {
                        ThrowIfReparsePoint(previousRootMeta,
                            "Previous UMA content root metadata");
                        File.Move(previousRootMeta, retiredRootMeta);
                        retiredMeta = true;
                    }

                    Directory.Move(pending.backupRoot, previous);
                    promotedDirectory = true;
                    File.Move(currentRootMeta, previousRootMeta);
                    promotedMeta = true;
                }
                catch
                {
                    if (promotedMeta && File.Exists(previousRootMeta) &&
                        !File.Exists(currentRootMeta))
                        File.Move(previousRootMeta, currentRootMeta);
                    if (promotedDirectory && Directory.Exists(previous) &&
                        !Directory.Exists(pending.backupRoot))
                        Directory.Move(previous, pending.backupRoot);
                    if (retiredMeta && File.Exists(retiredRootMeta) &&
                        !File.Exists(previousRootMeta))
                        File.Move(retiredRootMeta, previousRootMeta);
                    if (retiredDirectory && Directory.Exists(retired) &&
                        !Directory.Exists(previous))
                        Directory.Move(retired, previous);
                    throw;
                }

                try
                {
                    DeleteDirectory(retired);
                    if (File.Exists(retiredRootMeta)) File.Delete(retiredRootMeta);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[UMA] The new content backup was retained, but " +
                        "an older retired backup could not be removed: " +
                        exception.Message);
                }
            }

            string transaction = TransactionRoot(kind);
            try
            {
                if (Directory.Exists(transaction))
                {
                    ThrowIfTreeContainsReparsePoint(transaction,
                        "UMA content transaction cleanup");
                    foreach (string file in Directory.GetFiles(transaction))
                        File.Delete(file);
                    if (!Directory.EnumerateFileSystemEntries(transaction).Any())
                        Directory.Delete(transaction);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[UMA] Content installed and its backup was retained, " +
                    "but transaction cleanup was incomplete: " + exception.Message);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            ThrowIfReparsePoint(source, "UMA content backup source");
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                ThrowIfReparsePoint(file, "UMA content backup source");
                File.Copy(file, Path.Combine(destination,
                    Path.GetFileName(file)), true);
            }
            foreach (string directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination,
                    Path.GetFileName(directory)));
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            ThrowIfTreeContainsReparsePoint(path,
                "UMA installer working directory");
            Directory.Delete(path, true);
        }

        private static void ThrowIfTreeContainsReparsePoint(string path,
            string description)
        {
            if (!Directory.Exists(path)) return;
            ThrowIfReparsePoint(path, description);
            foreach (string file in Directory.GetFiles(path))
                ThrowIfReparsePoint(file, description);
            foreach (string directory in Directory.GetDirectories(path))
            {
                ThrowIfReparsePoint(directory, description);
                ThrowIfTreeContainsReparsePoint(directory, description);
            }
        }

        private static void ThrowIfReparsePoint(string path, string description)
        {
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(description +
                    " cannot be a symbolic link or junction: " + path);
        }

        private static string ComputeFileHash(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ToAssetPath(string absolutePath)
        {
            string normalizedRoot = ProjectRoot.Replace('\\', '/').TrimEnd('/');
            string normalized = Path.GetFullPath(absolutePath).Replace('\\', '/');
            return normalized.StartsWith(normalizedRoot + "/",
                    StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(normalizedRoot.Length + 1)
                : normalized;
        }
    }
}
