using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using UnityEngine;
using UMA.CharacterSystem;

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
            UMASettings settings =
                AssetDatabase.LoadAssetAtPath<UMASettings>(
                    UMAPathUtility.ResolveInstallAssetPath(
                        "InternalDataStore/InGame/Resources/UMASettings.asset"));
            GameObject generatorPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    UMAPathUtility.ResolveInstallAssetPath(
                        "Core/Defaults/UMA_GLIB.prefab"));
            Assert.That(settings, Is.Not.Null);
            Assert.That(generatorPrefab, Is.Not.Null);
            Assert.That(settings.generatorPrefab, Is.EqualTo(generatorPrefab));
            UMAGenerator generator = generatorPrefab.GetComponent<UMAGenerator>();
            Assert.That(generator, Is.Not.Null);
            Assert.That(generator.meshCombiner, Is.Not.Null);
            Assert.That(generator.textureMerge, Is.Not.Null);
            Assert.That(generator.defaultRendererAsset, Is.Not.Null);
            Assert.That(settings.ShaderFolder,
                Is.EqualTo(UMAPathUtility.ShaderPackagesRelativePath));
            string shaderPackagesPath = UMAPathUtility.ResolveInstallAssetPath(
                UMAPathUtility.ShaderPackagesRelativePath);
            Assert.That(AssetDatabase.IsValidFolder(shaderPackagesPath), Is.True,
                shaderPackagesPath);
            string whatsNewPath = UMAPathUtility.ResolveInstallAssetPath(
                "Docs/!WhatsNewInUMA3.md");
            Assert.That(AssetDatabase.LoadAssetAtPath<TextAsset>(whatsNewPath),
                Is.Not.Null, whatsNewPath);

            string[] shippedResourcePaths =
            {
                "InternalDataStore/InGame/Resources/AssetIndexer.asset",
                "InternalDataStore/InGame/Resources/UmaBanner.png",
                "InternalDataStore/Editor/Resources/UMAWelcomeScenes.asset",
                "InternalDataStore/InGame/Resources/Shader/Combiner.compute",
                "InternalDataStore/InGame/Resources/Shader/NormalShader.compute",
                "InternalDataStore/InGame/Resources/Shader/DQSkin.compute",
                "InternalDataStore/InGame/Resources/PlaceholderAssets/bonemesh.fbx"
            };
            for (int i = 0; i < shippedResourcePaths.Length; i++)
            {
                string path = UMAPathUtility.ResolveInstallAssetPath(
                    shippedResourcePaths[i]);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(path),
                    Is.Not.Null, path);
            }
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
        public void ShippedWelcomeScenesResolveFromActiveInstallation()
        {
            UMAWelcomeScenes scenes =
                UMAPathUtility.LoadInstallAsset<UMAWelcomeScenes>(
                    "InternalDataStore/Editor/Resources/UMAWelcomeScenes.asset");
            Assert.That(scenes, Is.Not.Null);
            Assert.That(scenes.umaScenes, Is.Not.Empty);
            for (int i = 0; i < scenes.umaScenes.Count; i++)
            {
                UMAWelcomeScenes.UMAScene scene = scenes.umaScenes[i];
                string path = UMAPathUtility.ResolveLegacyInstallAssetPath(
                    scene.scenePath);
                Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(path),
                    Is.Not.Null, scene.sceneName + " -> " + path);
            }
        }

        [Test]
        public void InternalLodPrefabKeepsGeneratedMeshReadable()
        {
            string prefabPath = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Getting Started/UMADynamicCharacterAvatar-LOD.prefab");
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);

            UMA.Examples.UMASimpleLOD lod =
                prefab.GetComponent<UMA.Examples.UMASimpleLOD>();
            UMAData data = prefab.GetComponent<UMAData>();
            Assert.That(lod, Is.Not.Null, prefabPath);
            Assert.That(data, Is.Not.Null, prefabPath);
            Assert.That(lod.useInternalMeshLOD, Is.True,
                "The LOD sample must exercise internal mesh LOD.");
            Assert.That(data.markNotReadable, Is.False,
                "Internal mesh LOD rewrites index buffers at runtime and " +
                "therefore requires a readable generated mesh.");
        }

        [Test]
        public void SliderSampleRandomAvatarCreatesItsAnimatorController()
        {
            string generatorPath = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Getting Started/UMARandomGeneratedCharacter.prefab");
            GameObject generatorPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(generatorPath);
            Assert.That(generatorPrefab, Is.Not.Null, generatorPath);

            UMARandomAvatar generator =
                generatorPrefab.GetComponent<UMARandomAvatar>();
            Assert.That(generator, Is.Not.Null, generatorPath);
            Assert.That(generator.prefab, Is.Not.Null,
                "The slider sample's random-avatar generator needs a " +
                "package-owned character prefab.");

            DynamicCharacterAvatar prefabAvatar =
                generator.prefab.GetComponent<DynamicCharacterAvatar>();
            Assert.That(prefabAvatar, Is.Not.Null, generator.prefab.name);
            RuntimeAnimatorController expectedController =
                prefabAvatar.raceAnimationControllers.defaultAnimationController;
            Assert.That(expectedController, Is.Not.Null,
                "The generated character prefab needs a default animator " +
                "controller.");

            GameObject instance = UnityEngine.Object.Instantiate(
                generator.prefab);
            try
            {
                DynamicCharacterAvatar avatar =
                    instance.GetComponent<DynamicCharacterAvatar>();
                avatar.SetAnimatorController(true);

                Animator animator = instance.GetComponent<Animator>();
                Assert.That(animator, Is.Not.Null,
                    "Random-avatar setup must create an Animator.");
                Assert.That(animator.runtimeAnimatorController,
                    Is.EqualTo(expectedController));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void RandomCharacterSampleUsesDedicatedWalkerPrefab()
        {
            string walkerPath = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Getting Started/" +
                "UMADynamicCharacterAvatar-LOD-walker.prefab");
            GameObject walkerPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(walkerPath);
            Assert.That(walkerPrefab, Is.Not.Null, walkerPath);

            MonoBehaviour walker = null;
            MonoBehaviour[] behaviours =
                walkerPrefab.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().FullName ==
                    "UMA.Examples.RandomCharacterWalker")
                {
                    walker = behaviour;
                    break;
                }
            }
            Assert.That(walker, Is.Not.Null,
                "The dedicated crowd prefab must include the sample walker.");

            Assert.That(walker.enabled, Is.True,
                "The sample walker must be enabled on the dedicated prefab.");

            DynamicCharacterAvatar avatar =
                walkerPrefab.GetComponent<DynamicCharacterAvatar>();
            Assert.That(avatar, Is.Not.Null,
                "The dedicated walker prefab must contain a DCA.");
            Assert.That(avatar.applyRootMotion, Is.True,
                "The DCA must configure its runtime Animator for root motion. " +
                "The walker also preserves this setting when it refreshes " +
                "the Animator during Play Mode.");

            string scenePath = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Scenes/U3-Generating Random Characters.unity");
            string[] dependencies = AssetDatabase.GetDependencies(
                scenePath, true);
            Assert.That(Array.Exists(dependencies, dependency =>
                    string.Equals(NormalizeAssetPath(dependency),
                        NormalizeAssetPath(walkerPath),
                        StringComparison.OrdinalIgnoreCase)),
                Is.True,
                "The random-character scene must use the dedicated walker " +
                "prefab without changing the shared stationary avatar.");
        }

        [Test]
        public void ChallengerLocomotionUsesChallengerAnimations()
        {
            string animationRoot = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Animation");
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(
                    animationRoot + "/Locomotion_Challenger.controller");
            AnimationClip expectedIdle =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    animationRoot + "/Chal_Idle.anim");
            AnimationClip expectedWalk =
                AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    animationRoot + "/Chal_Walk.anim");

            Assert.That(controller, Is.Not.Null);
            Assert.That(expectedIdle, Is.Not.Null);
            Assert.That(expectedWalk, Is.Not.Null);

            AnimatorState idleState = null;
            AnimatorState walkState = null;
            ChildAnimatorState[] states =
                controller.layers[0].stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state.name == "Idle")
                    idleState = states[i].state;
                else if (states[i].state.name == "Walk")
                    walkState = states[i].state;
            }

            Assert.That(idleState, Is.Not.Null);
            Assert.That(walkState, Is.Not.Null);
            Assert.That(idleState.motion, Is.EqualTo(expectedIdle));
            Assert.That(walkState.motion, Is.EqualTo(expectedWalk));
        }

        [Test]
        public void Uma3SampleScenesUseOnlyPackagedAssets()
        {
            string installRoot = NormalizeAssetPath(
                UMAPathUtility.InstallAssetRoot);
            string sceneRoot = UMAPathUtility.ResolveInstallAssetPath(
                "UMA3/Scenes");
            string[] sceneGuids = AssetDatabase.FindAssets(
                "t:Scene", new[] { sceneRoot });
            Assert.That(sceneGuids, Is.Not.Empty, sceneRoot);

            var failures = new List<string>();
            for (int sceneIndex = 0; sceneIndex < sceneGuids.Length;
                 sceneIndex++)
            {
                string scenePath = NormalizeAssetPath(
                    AssetDatabase.GUIDToAssetPath(sceneGuids[sceneIndex]));
                string[] dependencies = AssetDatabase.GetDependencies(
                    scenePath, true);
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Length;
                     dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(
                        dependencies[dependencyIndex]);
                    if (dependency.StartsWith("Assets/",
                            StringComparison.OrdinalIgnoreCase) &&
                        !IsPathInside(dependency, installRoot))
                    {
                        failures.Add(scenePath + " -> " + dependency);
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                "UMA 3 sample scenes must not depend on assets outside the " +
                "active UMA installation:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Uma2SampleScenesUseOnlyLegacyAndSharedUmaAssetsWhenInstalled()
        {
            const string legacyRoot = "Assets/UMA2";
            if (!AssetDatabase.IsValidFolder(legacyRoot))
                return;

            string installRoot = NormalizeAssetPath(
                UMAPathUtility.InstallAssetRoot);
            string uma3Root = installRoot + "/UMA3";
            string[] sceneGuids = AssetDatabase.FindAssets(
                "t:Scene", new[] { legacyRoot });
            var failures = new List<string>();

            for (int sceneIndex = 0; sceneIndex < sceneGuids.Length;
                 sceneIndex++)
            {
                string scenePath = NormalizeAssetPath(
                    AssetDatabase.GUIDToAssetPath(sceneGuids[sceneIndex]));
                string[] dependencies = AssetDatabase.GetDependencies(
                    scenePath, true);
                for (int dependencyIndex = 0;
                     dependencyIndex < dependencies.Length;
                     dependencyIndex++)
                {
                    string dependency = NormalizeAssetPath(
                        dependencies[dependencyIndex]);
                    bool usesUma3Content = IsPathInside(
                        dependency, uma3Root);
                    bool usesUnownedProjectAsset =
                        dependency.StartsWith("Assets/",
                            StringComparison.OrdinalIgnoreCase) &&
                        !IsPathInside(dependency, legacyRoot) &&
                        !IsPathInside(dependency, installRoot);
                    if (usesUma3Content || usesUnownedProjectAsset)
                        failures.Add(scenePath + " -> " + dependency);
                }
            }

            Assert.That(failures, Is.Empty,
                "UMA 2 sample scenes may use Assets/UMA2 and shared UMA " +
                "package assets, but not UMA 3 or consumer-project content:\n" +
                string.Join("\n", failures));
        }

        [Test]
        public void SettingsResolutionUsesInMemoryCache()
        {
            UMASettings previous = UMASettings.instance;
            UMASettings cached =
                ScriptableObject.CreateInstance<UMASettings>();
            try
            {
                UMASettings.instance = cached;
                Assert.That(UMASettings.GetOrCreateSettings(),
                    Is.SameAs(cached));
                Assert.That(UMASettings.GetSettings(), Is.SameAs(cached));
            }
            finally
            {
                UMASettings.instance = previous;
                UnityEngine.Object.DestroyImmediate(cached);
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

        [Test]
        public void InputActionsDoNotGenerateWrappersIntoConsumerProjects()
        {
            string installRoot = UMAPathUtility.ResolveAbsolutePath(
                UMAPathUtility.InstallAssetRoot);
            var failures = new List<string>();

            foreach (string metaPath in Directory.EnumerateFiles(
                         installRoot, "*.inputactions.meta",
                         SearchOption.AllDirectories))
            {
                string metadata = File.ReadAllText(metaPath);
                if (metadata.IndexOf("generateWrapperCode: 1",
                        StringComparison.Ordinal) < 0)
                    continue;

                failures.Add(NormalizeAssetPath(metaPath)
                    .Substring(NormalizeAssetPath(installRoot).Length)
                    .TrimStart('/'));
            }

            Assert.That(failures, Is.Empty,
                "Package input actions must ship generated wrappers instead of " +
                "writing source files into the consumer project:\n" +
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

        private static bool IsPathInside(string path, string root)
        {
            path = NormalizeAssetPath(path);
            root = NormalizeAssetPath(root);
            return string.Equals(path, root,
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root + "/",
                       StringComparison.OrdinalIgnoreCase);
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
        public void Uma2ManifestDependsOnlyOnMatchingBaseUmaVersionWhenInstalled()
        {
            string[] candidates =
            {
                "Assets/UMA2/package.json",
                "Packages/com.umasteeringgroup.uma2/package.json"
            };
            string uma2ManifestPath = Array.Find(candidates, candidate =>
                File.Exists(UMAPathUtility.ResolveAbsolutePath(candidate)));
            if (string.IsNullOrEmpty(uma2ManifestPath))
                Assert.Ignore("The optional UMA2 package is not installed.");

            string baseManifestPath =
                UMAPathUtility.ResolveInstallAssetPath("package.json");
            string baseJson = File.ReadAllText(
                UMAPathUtility.ResolveAbsolutePath(baseManifestPath));
            string uma2Json = File.ReadAllText(
                UMAPathUtility.ResolveAbsolutePath(uma2ManifestPath));

            string baseVersion = ReadJsonString(baseJson, "version");
            Assert.That(ReadJsonString(uma2Json, "name"),
                Is.EqualTo("com.umasteeringgroup.uma2"));
            Assert.That(ReadJsonString(uma2Json, "version"),
                Is.EqualTo(baseVersion));

            Match dependencies = Regex.Match(uma2Json,
                "\\\"dependencies\\\"\\s*:\\s*\\{(?<body>[^}]*)\\}",
                RegexOptions.Singleline);
            Assert.That(dependencies.Success, Is.True,
                uma2ManifestPath + " has no dependencies object.");
            MatchCollection dependencyNames = Regex.Matches(
                dependencies.Groups["body"].Value,
                "\\\"(?<name>[^\\\"]+)\\\"\\s*:");
            Assert.That(dependencyNames.Count, Is.EqualTo(1),
                "UMA2 must depend only on the base UMA package.");
            Assert.That(dependencyNames[0].Groups["name"].Value,
                Is.EqualTo("com.umasteeringgroup.uma"));
            Assert.That(ReadJsonString(dependencies.Groups["body"].Value,
                    "com.umasteeringgroup.uma"),
                Is.EqualTo(baseVersion));
        }

        private static string ReadJsonString(string json, string propertyName)
        {
            Match match = Regex.Match(json,
                "\\\"" + Regex.Escape(propertyName) +
                "\\\"\\s*:\\s*\\\"(?<value>[^\\\"]*)\\\"");
            Assert.That(match.Success, Is.True,
                "Missing JSON string property: " + propertyName);
            return match.Groups["value"].Value;
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
            Assert.That(json, Does.Not.Contain(
                "com.unity.render-pipelines.high-definition"));
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
