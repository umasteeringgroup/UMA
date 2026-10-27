#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginPackageFilesTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t")]
        [TestCase("HairCards.unitypackage")]
        [TestCase("invalid|path.unitypackage")]
        public void DefaultDiscoveryContinuesWithoutAUsableRememberedDirectory(string remembered)
        {
            var folders = CollectSearchFolders(remembered);
            Assert.That(folders, Does.Contain(UMAPluginPackageFiles.DefaultDirectory));
            Assert.That(folders, Does.Contain(UMAPluginPackageDownload.CurrentCacheDirectory));
            Assert.That(folders, Has.None.Null.Or.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultDiscoveryIncludesValidRememberedDirectoryEvenWhenArchiveIsMissing(bool absolute)
        {
            string folder = "Library/UMA/PluginDiscoveryTests/remembered folder";
            if (absolute) folder = Path.GetFullPath(folder);
            string path = Path.Combine(folder, "HairCards.unitypackage");
            Assert.That(CollectSearchFolders(path), Does.Contain(Path.GetDirectoryName(path)));
        }

        private sealed class StopBeforeArchiveValidation : Exception { }

        private static List<string> CollectSearchFolders(string remembered)
        {
            const UMAContentKind kind = UMAContentKind.HairCards;
            string key = "UMA.PluginPackage." + Application.dataPath + "." + UMAContentCatalog.Id(kind);
            bool existed = EditorPrefs.HasKey(key);
            string original = EditorPrefs.GetString(key);
            var folders = new List<string>();
            try
            {
                if (remembered == null) EditorPrefs.DeleteKey(key);
                else EditorPrefs.SetString(key, remembered);
                UMAPluginPackageFiles.FindPackage(kind, out _, (message, fraction) =>
                {
                    const string prefix = "Looking for local packages in ";
                    // Cover the production discovery entry point without unpacking the user's archives.
                    if (!message.StartsWith(prefix, StringComparison.Ordinal)) throw new StopBeforeArchiveValidation();
                    folders.Add(message.Substring(prefix.Length));
                });
            }
            catch (StopBeforeArchiveValidation) { }
            finally
            {
                if (existed) EditorPrefs.SetString(key, original);
                else EditorPrefs.DeleteKey(key);
            }
            return folders;
        }
    }
}
#endif
