using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    /// <summary>Builds manifest-validated plugins without importing temporary assets or bundling dependencies.</summary>
    public static class UMAPluginPackageBuilder
    {
        [Serializable] private sealed class PackageVersion { public string version; }
        private static readonly byte[] Padding = new byte[512];

        [MenuItem("UMA/Plugins/Build Plugin Packages", priority = 100)]
        private static void BuildMenu()
        {
            try
            {
                string[] packages = BuildAll(UMAPluginPackageFiles.DefaultDirectory);
                if (packages.Length == 0)
                    EditorUtility.DisplayDialog("Build Plugin Packages", "No installed plugin sources were found.", "OK");
                else
                {
                    Debug.Log("[UMA Plugins] Built and validated:\n" + string.Join("\n", packages));
                    EditorUtility.RevealInFinder(UMAPluginPackageFiles.DefaultDirectory);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Plugin build failed", exception.Message, "OK");
            }
        }

        [MenuItem("UMA/Plugins/Build Plugin Packages", true)]
        private static bool CanBuild() => !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !File.Exists("Library/UMA/ContentInstaller/pending.json") &&
            !File.Exists("Library/UMA/PluginRemoval/pending.json");

        public static string[] BuildAll(string destination)
        {
            if (!CanBuild()) throw new InvalidOperationException("Wait for the current import or package operation to finish.");
            var kinds = UMAContentCatalog.Plugins.Where(k => Directory.Exists(UMAContentCatalog.Root(k))).ToArray();
            var results = new List<string>();
            try
            {
                for (int i = 0; i < kinds.Length; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Build UMA plugins", UMAContentCatalog.DisplayName(kinds[i]), (float)i / kinds.Length))
                        throw new OperationCanceledException("Plugin build cancelled. Completed packages have been kept.");
                    string package = Build(kinds[i], destination);
                    results.Add(package);
                    UMAPluginPackageFiles.Remember(kinds[i], package);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            return results.ToArray();
        }

        public static string Build(UMAContentKind kind, string destination)
        {
            if (!UMAContentCatalog.IsPlugin(kind)) throw new ArgumentException("Only plugins can be built here.");
            string output = Path.GetFullPath(destination);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar);
            if (output.Equals(assets, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Build plugin archives outside Assets to avoid importing nested packages.");
            string root = UMAContentCatalog.Root(kind);
            string manifestPath = UMAContentCatalog.ManifestPath(kind);
            string jsonPath = UMAPathUtility.ResolveAbsolutePath(UMAPathUtility.ResolveInstallAssetPath("package.json"));
            string version = JsonUtility.FromJson<PackageVersion>(File.ReadAllText(jsonPath)).version;
            if (!Version.TryParse(version.Split('-', '+')[0], out var numeric)) throw new InvalidDataException("Invalid UMA package version.");
            string[] files = EnumerateFiles(root).Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                p != manifestPath && !p.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) &&
                !p.EndsWith("~", StringComparison.Ordinal) && !Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var paths = new HashSet<string>(files, StringComparer.Ordinal);
            foreach (string file in files)
            {
                string parent = Path.GetDirectoryName(file).Replace('\\', '/');
                while (parent != root)
                {
                    paths.Add(parent);
                    parent = Path.GetDirectoryName(parent).Replace('\\', '/');
                }
            }
            var records = paths.OrderBy(p => p, StringComparer.Ordinal).Select(CreateRecord).ToArray();
            if (records.Select(r => r.guid).Distinct(StringComparer.OrdinalIgnoreCase).Count() != records.Length ||
                records.Any(r => r.guid == UMAContentCatalog.ManifestGuid(kind)))
                throw new InvalidDataException("Duplicate GUID in plugin source.");
            foreach (string required in UMAContentCatalog.PluginRequiredPaths(kind))
                if (!files.Contains(required)) throw new FileNotFoundException("Required plugin source is missing: " + required);
            var manifest = new UMAContentManifest
            {
                formatVersion = UMAContentCatalog.CurrentManifestFormatVersion,
                requiredPluginApiVersion = UMAPluginApi.Version,
                contentId = UMAContentCatalog.Id(kind), contentVersion = version,
                requiredCoreVersion = version, minimumCoreVersion = version,
                maximumCoreVersionExclusive = (numeric.Major + 1) + ".0.0",
                installRoot = root, dependencies = UMAContentCatalog.Dependencies(kind),
                requiredPaths = UMAContentCatalog.PluginRequiredPaths(kind),
                ownedPaths = paths.Append(manifestPath).OrderBy(p => p, StringComparer.Ordinal).ToArray(), assets = records
            };
            if (!UMAContentPackageArchiveValidator.TryValidateManifestStructure(manifest, kind, out string error))
                throw new InvalidDataException(error);
            Directory.CreateDirectory(output);
            string target = Path.Combine(output, UMAContentCatalog.PackageStem(kind) + "-" + version + ".unitypackage");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = File.Create(temporary))
                using (var gzip = new GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
                {
                    foreach (var record in records)
                    {
                        if (File.Exists(record.path)) WriteFile(gzip, record.guid + "/asset", record.path);
                        WriteFile(gzip, record.guid + "/asset.meta", record.path + ".meta");
                        WriteText(gzip, record.guid + "/pathname", record.path);
                    }
                    string guid = UMAContentCatalog.ManifestGuid(kind);
                    WriteText(gzip, guid + "/asset", JsonUtility.ToJson(manifest, true) + "\n");
                    WriteText(gzip, guid + "/asset.meta", "fileFormatVersion: 2\nguid: " + guid + "\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
                    WriteText(gzip, guid + "/pathname", manifestPath);
                    gzip.Write(Padding, 0, 512); gzip.Write(Padding, 0, 512);
                }
                if (!UMAContentPackageArchiveValidator.TryValidate(temporary, kind, out _, out error))
                    throw new InvalidDataException(error);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                return target;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static IEnumerable<string> EnumerateFiles(string root)
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Plugin builds do not follow symbolic links: " + root);
            foreach (string path in Directory.EnumerateFileSystemEntries(root).OrderBy(p => p, StringComparer.Ordinal))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Plugin builds do not follow symbolic links: " + path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string child in EnumerateFiles(path)) yield return child;
                }
                else yield return path.Replace('\\', '/');
            }
        }

        private static UMAContentManifestAsset CreateRecord(string path)
        {
            string meta = path + ".meta";
            if (!File.Exists(meta)) throw new FileNotFoundException("Import plugin source in Unity before building: " + meta);
            var match = Regex.Match(File.ReadAllText(meta), @"(?m)^guid:\s*([a-fA-F0-9]{32})\s*$");
            if (!match.Success) throw new InvalidDataException("Missing asset GUID: " + meta);
            bool file = File.Exists(path);
            return new UMAContentManifestAsset
            {
                path = path, guid = match.Groups[1].Value,
                bytes = file ? new FileInfo(path).Length : 0, sha256 = file ? Hash(path) : "",
                metaBytes = new FileInfo(meta).Length, metaSha256 = Hash(meta)
            };
        }

        private static string Hash(string path)
        {
            using var sha = SHA256.Create(); using var file = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteText(Stream target, string name, string value)
        {
            using var source = new MemoryStream(Encoding.UTF8.GetBytes(value));
            WriteEntry(target, name, source);
        }

        private static void WriteFile(Stream target, string name, string path)
        {
            using var source = File.OpenRead(path);
            WriteEntry(target, name, source);
        }

        // Unity packages use short GUID/record names, so basic USTAR needs no extended path records.
        private static void WriteEntry(Stream target, string name, Stream source)
        {
            byte[] header = new byte[512];
            Put(header, 0, name); Put(header, 100, "0000644\0"); Put(header, 108, "0000000\0");
            Put(header, 116, "0000000\0"); Put(header, 124, Convert.ToString(source.Length, 8).PadLeft(11, '0') + "\0");
            Put(header, 136, "07033241600\0"); Put(header, 148, "        "); header[156] = (byte)'0';
            Put(header, 257, "ustar\0"); Put(header, 263, "00");
            Put(header, 148, Convert.ToString(header.Sum(b => (int)b), 8).PadLeft(6, '0') + "\0 ");
            target.Write(header, 0, header.Length); source.CopyTo(target);
            int padding = (int)((512 - source.Length % 512) % 512);
            if (padding > 0) target.Write(Padding, 0, padding);
        }

        private static void Put(byte[] target, int offset, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, target, offset, bytes.Length);
        }
    }
}
