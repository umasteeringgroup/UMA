using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public static class UMAShaderPackageArchive
    {
        public static string ArchivePath => UMAEditorUtilities.FindUMAFullPath() + "/ShaderPackages/Archive/OldShaderPackages.zip";

        public static string ValidateSource(string assetPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Exit Play Mode before archiving shader packages.";
            if (string.IsNullOrEmpty(assetPath) || !UMAShaderPackageUtility.IsPackage(assetPath)) return "Select a shader package asset.";
            if (!IsUnderAssets(assetPath)) return "Only shader packages under Assets can be archived.";
            if (!File.Exists(assetPath) || !File.Exists(assetPath + ".meta")) return "The shader package or its .meta file is missing. Refresh the package list.";
            if ((File.GetAttributes(assetPath) & FileAttributes.ReadOnly) != 0 ||
                (File.GetAttributes(assetPath + ".meta") & FileAttributes.ReadOnly) != 0 ||
                !AssetDatabase.IsOpenForEdit(assetPath, StatusQueryOptions.ForceUpdate))
                return "The shader package is read-only or not checked out.";
            return null;
        }

        private static bool IsUnderAssets(string path)
        {
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static string Archive(string assetPath) => Archive(assetPath, ArchivePath);

        public static string Archive(string assetPath, string archivePath)
        {
            string error = ValidateSource(assetPath);
            if (error != null) throw new InvalidOperationException(error);
            if (string.IsNullOrEmpty(archivePath) || !IsUnderAssets(archivePath) ||
                !string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The archive must be a ZIP file under Assets.");
            if (File.Exists(archivePath) && ((File.GetAttributes(archivePath) & FileAttributes.ReadOnly) != 0 ||
                !AssetDatabase.IsOpenForEdit(archivePath, StatusQueryOptions.ForceUpdate)))
                throw new InvalidOperationException("The archive is read-only or not checked out.");

            byte[] packageBytes = File.ReadAllBytes(assetPath);
            byte[] metaBytes = File.ReadAllBytes(assetPath + ".meta");
            string directory = Path.GetDirectoryName(archivePath);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, ".uma-archive-" + Guid.NewGuid().ToString("N") + ".tmp");
            string entryName = Path.GetFileName(assetPath);
            try
            {
                if (File.Exists(archivePath)) File.Copy(archivePath, temporary);
                using (var stream = new FileStream(temporary, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Update))
                {
                    if (zip.Entries.Any(entry => string.Equals(entry.FullName, entryName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.FullName, entryName + ".meta", StringComparison.OrdinalIgnoreCase)))
                        entryName = "Snapshots/" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "_" + Guid.NewGuid().ToString("N") + "/" + entryName;
                    WriteEntry(zip, entryName, packageBytes);
                    WriteEntry(zip, entryName + ".meta", metaBytes);
                }
                using (var stream = File.OpenRead(temporary))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    VerifyEntry(zip, entryName, packageBytes);
                    VerifyEntry(zip, entryName + ".meta", metaBytes);
                }
                if (File.Exists(archivePath)) File.Replace(temporary, archivePath, null);
                else File.Move(temporary, archivePath);

                if (!File.ReadAllBytes(assetPath).SequenceEqual(packageBytes) || !File.ReadAllBytes(assetPath + ".meta").SequenceEqual(metaBytes))
                    throw new IOException("The package changed while archiving. The archived snapshot was saved; the source has not been deleted.");
                if (!AssetDatabase.DeleteAsset(assetPath))
                    throw new IOException("The archive was saved, but Unity could not delete the source package. Refresh the list and check file permissions.");
                AssetDatabase.ImportAsset(archivePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                return entryName;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
        {
            using (var stream = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open())
                stream.Write(bytes, 0, bytes.Length);
        }

        private static void VerifyEntry(ZipArchive zip, string name, byte[] expected)
        {
            var entry = zip.GetEntry(name);
            if (entry == null) throw new IOException("The archive is missing " + name + ". The source has not been deleted.");
            using (var stream = entry.Open())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                if (!memory.ToArray().SequenceEqual(expected))
                    throw new IOException("Archive verification failed for " + name + ". The source has not been deleted.");
            }
        }
    }
}
