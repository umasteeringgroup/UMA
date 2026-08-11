using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Compilation;

namespace UMA.Editors.Tests
{
    [TestFixture]
    [Category("UMA")]
    [Category("Package Readiness")]
    public sealed class UMAPackageReadinessTests
    {
        [Serializable]
        private sealed class AssemblyDefinitionData
        {
            public string name;
            public string[] references;
            public string[] defineConstraints;
            public VersionDefineData[] versionDefines;
        }

        [Serializable]
        private sealed class VersionDefineData
        {
            public string name;
            public string expression;
            public string define;
        }

        [Test]
        public void InstallationRootResolvesCoreAssets()
        {
            string root = UMAPathUtility.InstallAssetRoot;
            Assert.That(root.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                        root.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase), Is.True, root);
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                UMAPathUtility.ResolveInstallAssetPath("Core/UMA_Core.asmdef")), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                UMAPathUtility.ResolveInstallAssetPath("OverlayPainter/Shaders/StrokeRasterize.compute")),
                Is.Not.Null);
        }

        [Test]
        public void MutableUmaDataAlwaysLivesBelowAssets()
        {
            string[] mutablePaths =
            {
                UMAPathUtility.ProjectSettingsPath,
                UMAPathUtility.ProjectIndexerPath,
                UMAPathUtility.OverlayPainterGeneratedRoot,
                UMAPathUtility.OverlayPainterRecoveryRoot,
                UMAPathUtility.GeneratedSlotsRoot,
                UMAPathUtility.TaskRoot
            };
            for (int i = 0; i < mutablePaths.Length; i++)
            {
                Assert.That(mutablePaths[i], Does.StartWith("Assets/"), mutablePaths[i]);
                Assert.That(mutablePaths[i], Does.Not.StartWith("Packages/"), mutablePaths[i]);
            }
        }

        [Test]
        public void EveryUmaScriptBelongsToAnExplicitAssembly()
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                new[] { UMAPathUtility.InstallAssetRoot });
            var assemblyDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < asmdefGuids.Length; i++)
            {
                string asmdefPath = AssetDatabase.GUIDToAssetPath(asmdefGuids[i]);
                assemblyDirectories.Add(NormalizeAssetPath(Path.GetDirectoryName(asmdefPath)));
            }

            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript",
                new[] { UMAPathUtility.InstallAssetRoot });
            var failures = new List<string>();
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(scriptGuids[i]);
                string assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(path);
                bool belongsToDisabledOptionalAssembly = string.IsNullOrEmpty(assembly) &&
                    HasAssemblyDefinitionAncestor(path, assemblyDirectories);
                if ((!belongsToDisabledOptionalAssembly && string.IsNullOrEmpty(assembly)) ||
                    (!string.IsNullOrEmpty(assembly) &&
                     assembly.StartsWith("Assembly-CSharp", StringComparison.Ordinal)))
                    failures.Add(path + " -> " + (assembly ?? "(none)"));
            }
            Assert.That(failures, Is.Empty,
                "UPM scripts must not rely on predefined project assemblies:\n" +
                string.Join("\n", failures));
        }

        [Test]
        public void AssemblyDefinitionsHaveUniqueNamesAndFolders()
        {
            string[] asmdefGuids = AssetDatabase.FindAssets(
                "t:AssemblyDefinitionAsset",
                new[] { UMAPathUtility.InstallAssetRoot });
            var pathsByName = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var pathsByDirectory = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();

            for (int i = 0; i < asmdefGuids.Length; i++)
            {
                string path = NormalizeAssetPath(
                    AssetDatabase.GUIDToAssetPath(asmdefGuids[i]));
                AssemblyDefinitionData definition =
                    UnityEngine.JsonUtility.FromJson<AssemblyDefinitionData>(
                        File.ReadAllText(
                            UMAPathUtility.ResolveAbsolutePath(path)));
                string name = definition?.name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    failures.Add(path + " -> missing assembly name");
                    continue;
                }

                AddPath(pathsByName, name, path);
                AddPath(pathsByDirectory,
                    NormalizeAssetPath(Path.GetDirectoryName(path)), path);
            }

            AddDuplicateFailures(pathsByName,
                "duplicate assembly name", failures);
            AddDuplicateFailures(pathsByDirectory,
                "multiple assembly definitions in one folder", failures);

            Assert.That(failures, Is.Empty,
                "UMA package assembly definitions must have unique names " +
                "and folders:\n" + string.Join("\n", failures));
        }

        [Test]
        public void PackageMetadataGuidsAreUnique()
        {
            string installRoot = UMAPathUtility.ResolveAbsolutePath(
                UMAPathUtility.InstallAssetRoot);
            var pathsByGuid = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();

            foreach (string metaPath in Directory.EnumerateFiles(
                         installRoot, "*.meta", SearchOption.AllDirectories))
            {
                string guid = ReadMetaGuid(metaPath);
                string displayPath = NormalizeAssetPath(metaPath)
                    .Substring(NormalizeAssetPath(installRoot).Length)
                    .TrimStart('/');
                if (string.IsNullOrWhiteSpace(guid))
                {
                    failures.Add(displayPath + " -> missing GUID");
                    continue;
                }

                AddPath(pathsByGuid, guid, displayPath);
            }

            AddDuplicateFailures(pathsByGuid, "duplicate asset GUID",
                failures);
            Assert.That(failures, Is.Empty,
                "UMA package metadata must contain unique GUIDs:\n" +
                string.Join("\n", failures));
        }

        private static string ReadMetaGuid(string metaPath)
        {
            foreach (string line in File.ReadLines(metaPath))
            {
                if (!line.StartsWith("guid:",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return line.Substring("guid:".Length).Trim();
            }
            return null;
        }

        private static void AddPath(
            Dictionary<string, List<string>> pathsByKey,
            string key,
            string path)
        {
            if (!pathsByKey.TryGetValue(key, out List<string> paths))
            {
                paths = new List<string>();
                pathsByKey.Add(key, paths);
            }
            paths.Add(path);
        }

        private static void AddDuplicateFailures(
            Dictionary<string, List<string>> pathsByKey,
            string description,
            List<string> failures)
        {
            foreach (KeyValuePair<string, List<string>> item in pathsByKey)
            {
                if (item.Value.Count > 1)
                {
                    failures.Add(description + " '" + item.Key + "': " +
                        string.Join(", ", item.Value));
                }
            }
        }

        private static bool HasAssemblyDefinitionAncestor(
            string assetPath,
            HashSet<string> assemblyDirectories)
        {
            string directory = NormalizeAssetPath(Path.GetDirectoryName(assetPath));
            string installRoot = NormalizeAssetPath(UMAPathUtility.InstallAssetRoot);
            while (!string.IsNullOrEmpty(directory) &&
                   directory.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (assemblyDirectories.Contains(directory))
                    return true;

                string parent = NormalizeAssetPath(Path.GetDirectoryName(directory));
                if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                    break;
                directory = parent;
            }
            return false;
        }

        private static string NormalizeAssetPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        [Test]
        public void UmaAssembliesDoNotReferenceConsumerAssemblyCSharp()
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                new[] { UMAPathUtility.InstallAssetRoot });
            var failures = new List<string>();
            for (int i = 0; i < asmdefGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(asmdefGuids[i]);
                string json = File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(path));
                if (json.IndexOf("\"Assembly-CSharp\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add(path);
            }
            Assert.That(failures, Is.Empty,
                "Package assemblies cannot depend on a consumer project's Assembly-CSharp:\n" +
                string.Join("\n", failures));
        }

        [Test]
        public void PackageManifestIsAtTheResolvedRoot()
        {
            string manifestPath = UMAPathUtility.ResolveInstallAssetPath("package.json");
            Assert.That(File.Exists(UMAPathUtility.ResolveAbsolutePath(manifestPath)), Is.True,
                manifestPath);
        }

        [Test]
        public void OptionalPackageReferencesAreConfinedToConstrainedAssemblies()
        {
            string[] asmdefGuids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset",
                new[] { UMAPathUtility.InstallAssetRoot });
            var failures = new List<string>();
            for (int i = 0; i < asmdefGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(asmdefGuids[i]);
                AssemblyDefinitionData definition = UnityEngine.JsonUtility.FromJson<AssemblyDefinitionData>(
                    File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(path)));
                string[] references = definition?.references ?? Array.Empty<string>();
                string[] constraints = definition?.defineConstraints ?? Array.Empty<string>();
                for (int referenceIndex = 0; referenceIndex < references.Length; referenceIndex++)
                {
                    string reference = references[referenceIndex] ?? string.Empty;
                    if (reference.IndexOf("Addressable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reference.IndexOf("ResourceManager", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (Array.IndexOf(constraints, "UMA_ADDRESSABLES") < 0)
                            failures.Add(path + " -> " + reference + " lacks UMA_ADDRESSABLES");
                    }
                    if (reference.IndexOf("Formats.Fbx", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        Array.IndexOf(constraints, "UMA_FBX_EXPORT") < 0)
                        failures.Add(path + " -> " + reference + " lacks UMA_FBX_EXPORT");
                    if (reference.IndexOf("Unity.2D.Sprite.Editor", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool constrained = Array.IndexOf(constraints, "UMA_2D_SPRITE") >= 0;
                        bool packageDetected = definition.versionDefines != null &&
                            Array.Exists(definition.versionDefines, item =>
                                item != null &&
                                string.Equals(item.name, "com.unity.2d.sprite",
                                    StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(item.define, "UMA_2D_SPRITE",
                                    StringComparison.Ordinal));
                        if (!constrained || !packageDetected)
                            failures.Add(path + " -> " + reference +
                                " must be isolated by a com.unity.2d.sprite version define");
                    }
                    if (reference.IndexOf("TestRunner", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        bool constrained = Array.IndexOf(constraints, "UMA_TEST_FRAMEWORK") >= 0;
                        bool packageDetected = definition.versionDefines != null &&
                            Array.Exists(definition.versionDefines, item =>
                                item != null &&
                                string.Equals(item.name, "com.unity.test-framework",
                                    StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(item.define, "UMA_TEST_FRAMEWORK",
                                    StringComparison.Ordinal));
                        if (!constrained || !packageDetected)
                            failures.Add(path + " -> " + reference +
                                " must be isolated by a com.unity.test-framework version define");
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "Optional package references leaked into an unconditional assembly:\n" +
                string.Join("\n", failures));
        }

        [Test]
        public void PackageManifestDoesNotInstallOptionalIntegrations()
        {
            string manifestPath = UMAPathUtility.ResolveInstallAssetPath("package.json");
            string json = File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(manifestPath));
            Assert.That(json, Does.Not.Contain("com.unity.addressables"));
            Assert.That(json, Does.Not.Contain("com.unity.formats.fbx"));
            Assert.That(json, Does.Not.Contain("com.unity.2d.sprite"));
            Assert.That(json, Does.Not.Contain("com.unity.test-framework"));
        }

        [Test]
        public void PackageManifestContainsDirectCompileDependencies()
        {
            string manifestPath = UMAPathUtility.ResolveInstallAssetPath("package.json");
            string json = File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(manifestPath));
            string[] requiredPackages =
            {
                "com.unity.burst",
                "com.unity.collections",
                "com.unity.inputsystem",
                "com.unity.jobs",
                "com.unity.mathematics",
                "com.unity.timeline",
                "com.unity.ugui",
                "com.unity.modules.animation",
                "com.unity.modules.assetbundle",
                "com.unity.modules.audio",
                "com.unity.modules.cloth",
                "com.unity.modules.director",
                "com.unity.modules.imageconversion",
                "com.unity.modules.imgui",
                "com.unity.modules.jsonserialize",
                "com.unity.modules.physics",
                "com.unity.modules.ui",
                "com.unity.modules.uielements",
                "com.unity.modules.unitywebrequest"
            };

            for (int i = 0; i < requiredPackages.Length; i++)
                Assert.That(json, Does.Contain("\"" + requiredPackages[i] + "\""),
                    requiredPackages[i]);
        }

        [Test]
        public void OptionalPackageNamespacesStayInsideTheirIntegrationBoundaries()
        {
            string root = UMAPathUtility.InstallAssetRoot;
            string[] scriptGuids = AssetDatabase.FindAssets("t:MonoScript", new[] { root });
            var failures = new List<string>();
            for (int i = 0; i < scriptGuids.Length; i++)
            {
                string path = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(scriptGuids[i]));
                if (path.EndsWith("/UMAPackageReadinessTests.cs",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                string source = File.ReadAllText(UMAPathUtility.ResolveAbsolutePath(path));
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEditor.U2D.Sprites", "/SpriteEditorIntegration/", failures);
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEditor.TestTools.TestRunner", "/TestRunnerIntegration/", failures,
                    "/Tests/");
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEditor.Formats.Fbx", "/FBX/", failures);
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEditor.AddressableAssets", "/Addressables/", failures);
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEngine.AddressableAssets", "/Addressables/", failures);
                AssertOptionalNamespaceBoundary(source, path,
                    "using UnityEngine.ResourceManagement", "/Addressables/", failures);

                if (source.IndexOf("JetBrains.Annotations", StringComparison.Ordinal) >= 0)
                    failures.Add(path + " -> unused JetBrains.Annotations dependency");
                if (source.IndexOf("using System.Drawing;", StringComparison.Ordinal) >= 0)
                    failures.Add(path + " -> unexpected System.Drawing dependency");
            }

            Assert.That(failures, Is.Empty,
                "Optional package APIs leaked outside their constrained integration assemblies:\n" +
                string.Join("\n", failures));
        }

        private static void AssertOptionalNamespaceBoundary(
            string source,
            string path,
            string namespaceName,
            string requiredPathFragment,
            List<string> failures,
            string alternatePathFragment = null)
        {
            if (source.IndexOf(namespaceName, StringComparison.Ordinal) < 0)
                return;
            if (path.IndexOf(requiredPathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (!string.IsNullOrEmpty(alternatePathFragment) &&
                path.IndexOf(alternatePathFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            failures.Add(path + " -> " + namespaceName);
        }
    }
}
