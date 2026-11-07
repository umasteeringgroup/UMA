#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;
using UMA.Editors.PackageSupport;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAContentInstallerFileTests
    {
        private string root;
        private string backup;
        private string destination;
        private string assetRoot;

        [SetUp]
        public void SetUp()
        {
            string id = Guid.NewGuid().ToString("N");
            root = Path.GetFullPath("Temp/UMAContentInstallerFileTests-" + id);
            backup = Path.Combine(root, "Backup");
            destination = Path.Combine(root, "Destination");
            assetRoot = "Assets/UMAContentInstallerFileTests-" + id;
            Directory.CreateDirectory(backup);
            Directory.CreateDirectory(destination);
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.ReleaseCachedFileHandles();
            if (AssetDatabase.IsValidFolder(assetRoot)) AssetDatabase.DeleteAsset(assetRoot);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void RollbackRestoresPartialDeletionWithoutTouchingUnchangedLockedFiles()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows file-sharing regression.");
            byte[] bytes = { 0, 255, 128, 17, 0, 42 };
            File.WriteAllBytes(Path.Combine(backup, "sprite.asset"), bytes);
            string locked = Path.Combine(destination, "sprite.asset");
            File.WriteAllBytes(locked, bytes);
            File.WriteAllText(Path.Combine(backup, "sprite.asset.meta"), "original metadata");
            Directory.CreateDirectory(Path.Combine(backup, "Empty"));
            Directory.CreateDirectory(Path.Combine(destination, "Incoming"));
            File.WriteAllText(Path.Combine(destination, "Incoming", "extra"), "new content");
            File.WriteAllText(Path.Combine(destination, "extra.meta"), "new metadata");
            using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(() => File.Delete(locked), "Reproduce the original delete failure.");
                Restore();
                Assert.That(File.ReadAllBytes(locked), Is.EqualTo(bytes));
                Assert.That(File.ReadAllText(Path.Combine(destination, "sprite.asset.meta")), Is.EqualTo("original metadata"));
                Assert.That(Directory.Exists(Path.Combine(destination, "Empty")), Is.True);
                Assert.That(Directory.Exists(Path.Combine(destination, "Incoming")), Is.False);
                Assert.That(File.Exists(Path.Combine(destination, "extra.meta")), Is.False);
            }
        }

        [Test]
        public void RollbackReplacesDifferentBytesEvenWhenLengthMatches()
        {
            File.WriteAllBytes(Path.Combine(backup, "native.asset"), new byte[] { 0, 255, 1 });
            File.WriteAllBytes(Path.Combine(destination, "native.asset"), new byte[] { 0, 254, 1 });
            Restore();
            Assert.That(File.ReadAllBytes(Path.Combine(destination, "native.asset")), Is.EqualTo(new byte[] { 0, 255, 1 }));
        }

        [Test]
        public void FailedRestoreKeepsBackupAndCanBeRetriedAfterExternalLockCloses()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("Windows file-sharing regression.");
            string saved = Path.Combine(backup, "native.asset");
            string installed = Path.Combine(destination, "native.asset");
            File.WriteAllText(saved, "original");
            File.WriteAllText(installed, "incoming");
            using (new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.Throws<IOException>(Restore);
                Assert.That(File.ReadAllText(saved), Is.EqualTo("original"));
                Assert.That(File.ReadAllText(installed), Is.EqualTo("incoming"));
            }
            Restore();
            Assert.That(File.ReadAllText(installed), Is.EqualTo("original"));
        }

        [Test]
        public void MissingBackupDoesNotDeleteInstalledFiles()
        {
            File.WriteAllText(Path.Combine(destination, "keep"), "installed");
            Directory.Delete(backup);
            Assert.Throws<DirectoryNotFoundException>(Restore);
            Assert.That(File.ReadAllText(Path.Combine(destination, "keep")), Is.EqualTo("installed"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RollbackRestoresPathsWhoseFileOrDirectoryTypeChanged(bool originalWasDirectory)
        {
            string saved = Path.Combine(backup, "changed");
            string installed = Path.Combine(destination, "changed");
            Directory.CreateDirectory(originalWasDirectory ? saved : installed);
            File.WriteAllText(originalWasDirectory ? installed : saved, "content");
            Restore();
            Assert.That(Directory.Exists(installed), Is.EqualTo(originalWasDirectory));
            Assert.That(File.Exists(installed), Is.EqualTo(!originalWasDirectory));
            if (!originalWasDirectory) Assert.That(File.ReadAllText(installed), Is.EqualTo("content"));
        }

        [Test]
        public void AssetEditingResumesAfterFileOperationThrows()
        {
            Assert.Throws<IOException>(() => Change(() => throw new IOException("Simulated lock")));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(assetRoot));
            string path = assetRoot + "/after.txt";
            File.WriteAllText(path, "import still works");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(path).text, Is.EqualTo("import still works"));
        }

        [Test]
        public void LoadedNativeAssetCanBeRemovedAndRestoredWithItsOriginalGuid()
        {
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(assetRoot));
            string path = assetRoot + "/mesh.asset";
            var mesh = new Mesh { name = "Installer native asset", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssetIfDirty(mesh);
            string guid = AssetDatabase.AssetPathToGUID(path);
            File.Copy(path, Path.Combine(backup, "mesh.asset"));
            File.Copy(path + ".meta", Path.Combine(backup, "mesh.asset.meta"));
            byte[] folderMeta = File.ReadAllBytes(assetRoot + ".meta");
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(path).vertexCount, Is.EqualTo(3));
            Change(() =>
            {
                Directory.Delete(assetRoot, true);
                File.Delete(assetRoot + ".meta");
                Invoke("RestoreDirectorySnapshot", backup, Path.GetFullPath(assetRoot));
                File.WriteAllBytes(assetRoot + ".meta", folderMeta);
            });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(path).vertexCount, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(File.ReadAllBytes(Path.Combine(backup, "mesh.asset"))));
        }

        private void Restore() => Invoke("RestoreDirectorySnapshot", backup, destination);
        private static void Change(Action change) => Invoke("ChangeAssetFiles", change);

        private static void Invoke(string name, params object[] arguments)
        {
            try
            {
                typeof(UMAContentPackageInstaller).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
            }
        }
    }
}
#endif
