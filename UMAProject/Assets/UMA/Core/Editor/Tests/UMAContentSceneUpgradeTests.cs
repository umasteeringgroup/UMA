#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors.PackageSupport;

namespace UMA.Editors.Tests
{
    // Unity's package import may open and re-save an imported scene (lighting settings upgrade), so the
    // installer accepts a changed scene only when it was written during the import. Everything else keeps
    // the strict byte check.
    public sealed class UMAContentSceneUpgradeTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            root = "Library/UMA/ReinstallTests/" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            string parent = Path.GetFullPath("Library/UMA/ReinstallTests") + Path.DirectorySeparatorChar;
            if (root != null && Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, true);
        }

        private static bool Accepts(string assetPath, string absolute, DateTime? started) =>
            (bool)typeof(UMAContentPackageInstaller).GetMethod("IsSceneUpgradedDuringImport",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, new object[] { assetPath, absolute, started });

        private string Write(string name, DateTime writtenUtc)
        {
            string path = Path.GetFullPath(root + "/" + name);
            File.WriteAllText(path, "upgraded");
            File.SetLastWriteTimeUtc(path, writtenUtc);
            return path;
        }

        [Test]
        public void SceneRewrittenDuringImportIsAccepted()
        {
            DateTime started = DateTime.UtcNow.AddMinutes(-1);
            string scene = Write("Example.unity", DateTime.UtcNow);
            Assert.That(Accepts("Assets/Example/Example.unity", scene, started), Is.True);
        }

        [Test]
        public void SceneOlderThanTheImportIsRejected()
        {
            DateTime started = DateTime.UtcNow;
            string scene = Write("Example.unity", started.AddHours(-1));
            Assert.That(Accepts("Assets/Example/Example.unity", scene, started), Is.False,
                "A stale scene left from before the import is not an upgrade.");
        }

        [Test]
        public void OtherAssetsAndUnknownImportTimesKeepTheStrictCheck()
        {
            DateTime started = DateTime.UtcNow.AddMinutes(-1);
            string asset = Write("Example.asset", DateTime.UtcNow);
            string scene = Write("Example.unity", DateTime.UtcNow);
            Assert.That(Accepts("Assets/Example/Example.asset", asset, started), Is.False);
            Assert.That(Accepts("Assets/Example/Example.unity", scene, null), Is.False);
            Assert.That(Accepts("Assets/Example/Missing.unity", Path.GetFullPath(root + "/Missing.unity"), started), Is.False);
        }

        [Test]
        public void EmptySceneIsRejected()
        {
            DateTime started = DateTime.UtcNow.AddMinutes(-1);
            string path = Path.GetFullPath(root + "/Empty.unity");
            File.WriteAllBytes(path, Array.Empty<byte>());
            Assert.That(Accepts("Assets/Example/Empty.unity", path, started), Is.False);
        }
    }
}
#endif
