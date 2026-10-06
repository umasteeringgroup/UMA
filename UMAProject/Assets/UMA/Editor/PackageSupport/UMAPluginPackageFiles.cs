using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    public static class UMAPluginPackageFiles
    {
        public static string DefaultDirectory => Path.GetFullPath("Build/Plugins");
        private static string PreferenceKey(UMAContentKind kind) =>
            "UMA.PluginPackage." + Application.dataPath + "." + UMAContentCatalog.Id(kind);

        public static void Remember(UMAContentKind kind, string path) =>
            EditorPrefs.SetString(PreferenceKey(kind), Path.GetFullPath(path));

        public static string FindPackage(UMAContentKind kind)
        {
            string remembered = EditorPrefs.GetString(PreferenceKey(kind), "");
            return FindPackage(kind, new[]
            {
                DefaultDirectory,
                Path.GetDirectoryName(remembered),
                Path.GetFullPath("Plugins"),
                UMAPathUtility.ResolveAbsolutePath(UMAPathUtility.ResolveInstallAssetPath("Plugins"))
            }, remembered);
        }

        // Only search known package locations, never recurse through user content or the whole disk.
        public static string FindPackage(UMAContentKind kind, IEnumerable<string> directories, string remembered = null)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(remembered) && File.Exists(remembered)) candidates.Add(remembered);
            string stem = UMAContentCatalog.PackageStem(kind);
            foreach (string folder in directories.Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    foreach (string path in Directory.EnumerateFiles(folder, "*.unitypackage"))
                    {
                        string name = Path.GetFileNameWithoutExtension(path);
                        if (name.Equals(stem, StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith(stem + "-", StringComparison.OrdinalIgnoreCase)) candidates.Add(path);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            string best = null;
            Version bestVersion = null;
            foreach (string path in candidates.OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!UMAContentPackageArchiveValidator.TryValidate(path, kind, out var archive, out _) ||
                    archive.Manifest.requiredPluginApiVersion != UMAPluginApi.Version ||
                    !UMAContentPackageInstaller.IsCoreVersionCompatible(archive.Manifest, out _)) continue;
                string numeric = archive.Manifest.contentVersion.Split('-', '+')[0];
                if (!Version.TryParse(numeric, out var version)) continue;
                if (bestVersion != null && version.CompareTo(bestVersion) <= 0) continue;
                best = Path.GetFullPath(path);
                bestVersion = version;
            }
            return best;
        }
    }
}
