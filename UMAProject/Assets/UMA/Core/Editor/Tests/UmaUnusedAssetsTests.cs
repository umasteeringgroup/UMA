using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UmaUnusedAssetsTests
    {
        private string root;
        private string candidates;
        private UMASettings previousSettings;
        private UMASettings settings;

        [SetUp]
        public void SetUp()
        {
            previousSettings = UMASettings.instance;
            settings = ScriptableObject.CreateInstance<UMASettings>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.postProcessAllAssets = false;
            UMASettings.instance = settings;
            root = "Assets/__UMAUnusedAssetsTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", root.Substring("Assets/".Length));
            AssetDatabase.CreateFolder(root, "Candidates");
            candidates = root + "/Candidates";
        }

        [TearDown]
        public void TearDown()
        {
            // Only this test's uniquely created fixture folder is removed.
            Assert.That(root, Does.StartWith("Assets/__UMAUnusedAssetsTests_"));
            if (AssetDatabase.IsValidFolder(root)) AssetDatabase.DeleteAsset(root);
            UMASettings.instance = previousSettings;
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void UnreferencedStandaloneAssetsOfAllThreeKindsAreFound()
        {
            var uma = CreateUma(candidates + "/Unused.asset");
            var material = CreateMaterial(candidates + "/Unused.mat");
            var shader = CreateShader(candidates + "/Unused.shader");
            var scan = Scan();
            Assert.That(scan.Complete, Is.True);
            foreach (Object asset in new Object[] { uma, material, shader })
            {
                var result = Find(scan, asset);
                Assert.That(result.IsUnused, Is.True, result.Protection + string.Join(";", result.References));
                Assert.That(UmaUnusedAssetUtility.CanTrash(result, out _), Is.True);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(result.Path), Is.Not.Null, "Scanning must not delete assets.");
            }
        }

        [Test]
        public void ReferencesOutsideSearchFolderKeepBothMaterialPassesAndTheirShader()
        {
            var shader = CreateShader(candidates + "/Used.shader");
            var first = CreateMaterial(candidates + "/First.mat", shader);
            var second = CreateMaterial(candidates + "/Second.mat", shader);
            var owner = CreateUma(root + "/OutsideScope.asset");
            owner.material = first;
            using (var serialized = new SerializedObject(owner))
            {
                serialized.FindProperty("_secondPass").objectReferenceValue = second;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            var scan = Scan();
            Assert.That(Find(scan, first).References, Does.Contain(root + "/OutsideScope.asset"));
            Assert.That(Find(scan, second).IsUnused, Is.False);
            Assert.That(Find(scan, shader).IsUnused, Is.False);
        }

        [Test]
        public void UnusedDependencyChainsAreKeptUntilOwnersAreRemoved()
        {
            var material = CreateMaterial(candidates + "/Dependency.mat");
            var owner = CreateUma(candidates + "/Owner.asset");
            owner.material = material;
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            var scan = Scan();
            Assert.That(Find(scan, owner).IsUnused, Is.True);
            Assert.That(Find(scan, material).IsUnused, Is.False);
            AssetDatabase.DeleteAsset(candidates + "/Owner.asset"); // Test fixture only.
            Assert.That(Find(Scan(), material).IsUnused, Is.True);
        }

        [TestCase("Resources")]
        [TestCase("Editor Default Resources")]
        [TestCase("StreamingAssets")]
        public void DynamicLoadingFoldersAreProtected(string folder)
        {
            AssetDatabase.CreateFolder(candidates, folder);
            var material = CreateMaterial(candidates + "/" + folder + "/Runtime.mat");
            var result = Find(Scan(), material);
            Assert.That(result.IsUnused, Is.False);
            Assert.That(result.Protection, Does.Contain("loading folder"));
        }

        [Test]
        public void BundleEntryIsProtectedWithoutAStaticReference()
        {
            string path = candidates + "/Bundled.mat";
            var material = CreateMaterial(path);
            AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant("uma-unused-test", "");
            var result = Find(Scan(), material);
            Assert.That(result.Protection, Does.Contain("AssetBundle"));
        }

        [TestCase("Guid")]
        [TestCase("Path")]
        [TestCase("Name")]
        [TestCase("Object")]
        public void UmaRegistryEntriesAreInformationalNotUsage(string identity)
        {
            var material = CreateUma(candidates + "/Indexed.asset");
            var indexer = CreateRegistry(Entry(material, identity));
            string indexPath = AssetDatabase.GetAssetPath(indexer);
            Hash128 before = AssetDatabase.GetAssetDependencyHash(indexPath);
            var result = Find(Scan(), material);
            Assert.That(result.IsUnused, Is.True, result.Protection + string.Join(";", result.References));
            Assert.That(result.RegisteredIn, Does.Contain(indexPath));
            Assert.That(UmaUnusedAssetUtility.CanTrash(result, out _), Is.True);
            Assert.That(AssetDatabase.GetAssetDependencyHash(indexPath), Is.EqualTo(before), "Scanning must not rewrite the index.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ActualOverlayUsageStillKeepsRegisteredMaterial(bool stripped)
        {
            var material = CreateUma(candidates + "/OverlayMaterial.asset");
            CreateRegistry(Entry(material, "Object"));
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            overlay.material = stripped ? null : material;
            overlay.materialName = stripped ? material.name : "";
            AssetDatabase.CreateAsset(overlay, root + "/Consumer.asset");
            var result = Find(Scan(), material);
            Assert.That(result.IsUnused, Is.False);
            Assert.That(result.References.Any(reference => reference.Contains("Consumer.asset")), Is.True);
        }

        [Test]
        public void DirtyAndPreloadedIndexDoNotTurnRegistrationIntoUsage()
        {
            var material = CreateUma(candidates + "/OnlyIndexed.asset");
            var registry = CreateRegistry(Entry(material, "Object"));
            Object[] preloaded = PlayerSettings.GetPreloadedAssets();
            try
            {
                PlayerSettings.SetPreloadedAssets(preloaded.Concat(new Object[] { registry }).ToArray());
                EditorUtility.SetDirty(registry);
                var result = Find(Scan(), material);
                Assert.That(result.IsUnused, Is.True, result.Protection);
                Assert.That(UmaUnusedAssetUtility.CanTrash(result, out string reason), Is.False);
                Assert.That(reason, Does.Contain("unsaved"), "Dirty index must be saved explicitly before cleanup.");
            }
            finally { PlayerSettings.SetPreloadedAssets(preloaded); }
        }

        [TestCase("Guid")]
        [TestCase("Path")]
        [TestCase("Name")]
        [TestCase("Object")]
        public void DeletionRemovesMatchingEntriesFromAllIndexesAndLiveCaches(string identity)
        {
            var material = CreateUma(candidates + "/DeleteMe.asset");
            string path = AssetDatabase.GetAssetPath(material);
            var unrelated = CreateUma(root + "/KeepMe.asset");
            var target = Entry(material, identity);
            var survivor = Entry(unrelated, "Object");
            var first = CreateRegistry(target, survivor);
            var second = CreateRegistry(Entry(material, identity));
            first.GetAssetDictionary(typeof(UMAMaterial))[target._Name] = target;
            first.GetAssetDictionary(typeof(UMAMaterial))[survivor._Name] = survivor;
            if (!string.IsNullOrEmpty(target._Guid)) first.GuidTypes[target._Guid] = target;
            var result = Find(Scan(), material);
            string firstPath = AssetDatabase.GetAssetPath(first);
            Assert.That(DeleteFixture(result, candidatePath =>
            {
                Assert.That(first.SerializedItems, Has.Count.EqualTo(1), "Index cleanup is saved before removing the asset.");
                return AssetDatabase.DeleteAsset(candidatePath); // Only this test's unique fixture.
            }, out string reason), Is.True, reason);
            Assert.That(File.Exists(path), Is.False);
            Assert.That(first.SerializedItems.Single(), Is.SameAs(survivor));
            Assert.That(survivor.Index, Is.EqualTo(0));
            Assert.That(first.GetAssetDictionary(typeof(UMAMaterial)).ContainsKey(target._Name), Is.False);
            Assert.That(first.GetAssetDictionary(typeof(UMAMaterial))[survivor._Name], Is.SameAs(survivor));
            Assert.That(first.GuidTypes.Values.Contains(target), Is.False);
            Assert.That(second.SerializedItems, Is.Empty);
            AssetDatabase.ImportAsset(firstPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(firstPath);
            Assert.That(saved.SerializedItems.Select(item => item._Guid), Is.EquivalentTo(new[] { survivor._Guid }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedDeletionRestoresRegistrationAndCaches(bool throws)
        {
            var material = CreateUma(candidates + "/CannotDelete.asset");
            var target = Entry(material, "Object");
            var registry = CreateRegistry(target);
            registry.GetAssetDictionary(typeof(UMAMaterial))[target._Name] = target;
            registry.GuidTypes[target._Guid] = target;
            var result = Find(Scan(), material);
            Assert.That(DeleteFixture(result, candidatePath =>
            {
                Assert.That(registry.SerializedItems, Is.Empty);
                if (throws) throw new IOException("Simulated trash failure");
                return false;
            }, out string reason), Is.False);
            Assert.That(reason, Does.Contain("restored"));
            Assert.That(File.Exists(result.Path), Is.True);
            Assert.That(registry.SerializedItems.Single(), Is.SameAs(target));
            Assert.That(registry.GetAssetDictionary(typeof(UMAMaterial))[target._Name], Is.SameAs(target));
            Assert.That(registry.GuidTypes[target._Guid], Is.SameAs(target));
            Assert.That(EditorUtility.IsDirty(registry), Is.False);
        }

        [Test]
        public void StrongIdentityDoesNotRemoveAnotherSameNamedAsset()
        {
            var deleted = CreateUma(candidates + "/One.asset");
            var kept = CreateUma(root + "/Two.asset");
            kept.name = deleted.name;
            EditorUtility.SetDirty(kept);
            AssetDatabase.SaveAssetIfDirty(kept);
            var keeper = Entry(kept, "Guid");
            var registry = CreateRegistry(Entry(deleted, "Guid"), keeper);
            var result = Find(Scan(), deleted);
            Assert.That(DeleteFixture(result, AssetDatabase.DeleteAsset, out string reason), Is.True, reason);
            Assert.That(registry.SerializedItems.Single(), Is.SameAs(keeper));
            Assert.That(File.Exists(AssetDatabase.GetAssetPath(kept)), Is.True);
        }

        [Test]
        public void AmbiguousNameOnlyRegistrationPreventsAccidentalRemoval()
        {
            var first = CreateUma(candidates + "/One.asset");
            var second = CreateUma(root + "/Two.asset");
            second.name = first.name;
            EditorUtility.SetDirty(second);
            AssetDatabase.SaveAssetIfDirty(second);
            var registry = CreateRegistry(Entry(first, "Name"));
            Assert.That(DeleteFixture(Find(Scan(), first), path => { Assert.Fail("Must not attempt deletion"); return false; }, out string reason), Is.False);
            Assert.That(reason, Does.Contain("Ambiguous"));
            Assert.That(registry.SerializedItems, Has.Count.EqualTo(1));
        }

        [Test]
        public void ReadOnlyIndexPreventsDeletionWithoutChangingEitherAsset()
        {
            var material = CreateUma(candidates + "/Registered.asset");
            var registry = CreateRegistry(Entry(material, "Guid"));
            string path = AssetDatabase.GetAssetPath(registry);
            var attributes = File.GetAttributes(path);
            try
            {
                File.SetAttributes(path, attributes | FileAttributes.ReadOnly);
                var result = Find(Scan(), material);
                Assert.That(result.IsUnused, Is.True);
                Assert.That(DeleteFixture(result, candidatePath => { Assert.Fail("Must not attempt deletion"); return false; }, out string reason), Is.False);
                Assert.That(reason, Does.Contain("Read-only"));
                Assert.That(File.Exists(result.Path), Is.True);
                Assert.That(registry.SerializedItems, Has.Count.EqualTo(1));
            }
            finally { File.SetAttributes(path, attributes); }
        }

        [Test]
        public void RegisteredUnityMaterialsAndShadersAreAlsoEligibleForCleanup()
        {
            var material = CreateMaterial(candidates + "/Registered.mat");
            var shader = CreateShader(candidates + "/Registered.shader");
            var registry = CreateRegistry(Entry(material, "Object"), Entry(shader, "Guid"));
            var scan = Scan();
            foreach (Object asset in new Object[] { material, shader })
            {
                var result = Find(scan, asset);
                Assert.That(result.IsUnused, Is.True);
                Assert.That(result.RegisteredIn, Does.Contain(AssetDatabase.GetAssetPath(registry)));
                Assert.That(DeleteFixture(result, AssetDatabase.DeleteAsset, out string reason), Is.True, reason);
            }
            Assert.That(registry.SerializedItems, Is.Empty);
        }

        [Test]
        public void SystemTrashDeletionAlsoSavesCleanupOfANativeBinaryIndex()
        {
            var material = CreateUma(candidates + "/TrashFixture.asset");
            string materialPath = AssetDatabase.GetAssetPath(material);
            var entry = Entry(material, "Guid");
            string registryPath;
            SerializationMode previousMode = EditorSettings.serializationMode;
            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceBinary;
                var registry = CreateRegistry(entry);
                registryPath = AssetDatabase.GetAssetPath(registry);
                // Validate the binary fixture as bytes; never decode native assets as text.
                using (var stream = File.OpenRead(registryPath))
                    Assert.That(stream.ReadByte(), Is.Not.EqualTo((int)'%'));
            }
            finally { EditorSettings.serializationMode = previousMode; }
            material = AssetDatabase.LoadAssetAtPath<UMAMaterial>(materialPath);
            Assert.That(material, Is.Not.Null);
            var result = Find(Scan(), material);
            Assert.That(result.Path, Does.StartWith(root + "/"));
            Assert.That(UmaUnusedAssetUtility.TrashAndUnregister(result, out string reason), Is.True, reason);
            Assert.That(File.Exists(result.Path), Is.False);
            AssetDatabase.ImportAsset(registryPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Assert.That(AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(registryPath).SerializedItems, Is.Empty);
        }

        [Test]
        public void OpenUnsavedSceneReferencesAreProtected()
        {
            var material = CreateMaterial(candidates + "/UnsavedScene.mat");
            var go = new GameObject("Unsaved material owner");
            try
            {
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                var result = Find(Scan(), material);
                Assert.That(result.Protection, Does.Contain("Open scene"));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void UnsavedMaterialReferencesAreProtected()
        {
            var shader = CreateShader(candidates + "/UnsavedReference.shader");
            var owner = CreateMaterial(root + "/DirtyOwner.mat");
            owner.shader = shader;
            EditorUtility.SetDirty(owner);
            Assert.That(Find(Scan(), shader).IsUnused, Is.False);
        }

        [Test]
        public void LiteralShaderFindInCodeProtectsShader()
        {
            Shader shader = CreateShader(candidates + "/Literal.shader", "Hidden/UMAUnusedAssetsTests/Literal");
            var result = Find(Scan(), shader);
            Assert.That(result.References.Any(reference => reference.Contains("Shader.Find literal")), Is.True);
        }

        // The scanner should find this literal in C# source without executing it.
        private static Shader ExampleRuntimeLookup() => Shader.Find("Hidden/UMAUnusedAssetsTests/Literal");

        [Test]
        public void ContainerAssetsAndUnsupportedPathsCannotBeDeleted()
        {
            var owner = CreateUma(candidates + "/Container.asset");
            var child = new Material(Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.AddObjectToAsset(child, owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            Assert.That(Find(Scan(), owner).Protection, Does.Contain("sub-assets"));
            Assert.That(UmaUnusedAssetUtility.TryGetKind("Packages/test/Material.mat", out _), Is.False);
            Assert.That(UmaUnusedAssetUtility.TryGetKind("Assets/../Material.mat", out _), Is.False);
            Assert.That(UmaUnusedAssetUtility.TryGetKind(candidates, out _), Is.False);
            Assert.Throws<ArgumentException>(() => new UmaUnusedAssetScan("Packages"));
        }

        [Test]
        public void CanceledScanNeverReportsComplete()
        {
            var material = CreateMaterial(candidates + "/Canceled.mat");
            var scan = new UmaUnusedAssetScan(candidates);
            using (var iterator = scan.Run().GetEnumerator()) Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(scan.Complete, Is.False);
            Assert.That(material, Is.Not.Null);
        }

        [Test]
        public void FreshScanRejectsNewReferenceAndIdentityGuardRejectsReplacement()
        {
            string path = candidates + "/Changed.mat";
            var material = CreateMaterial(path);
            var original = Find(Scan(), material);
            var owner = CreateUma(root + "/NewOwner.asset");
            owner.material = material;
            EditorUtility.SetDirty(owner);
            AssetDatabase.SaveAssetIfDirty(owner);
            Assert.That(UmaUnusedAssetUtility.CanTrash(Find(Scan(), material), out _), Is.False);
            // Unity can reuse the GUID of an immediately deleted/recreated path.
            // Keep the old asset elsewhere so this is genuinely a different identity.
            Assert.That(AssetDatabase.MoveAsset(path, root + "/Moved.mat"), Is.Empty);
            CreateMaterial(path);
            Assert.That(UmaUnusedAssetUtility.CanTrash(original, out string reason), Is.False);
            Assert.That(reason, Does.Contain("replaced"));
        }

        [Test]
        public void ReadOnlyMetadataProtectsTheWholeAsset()
        {
            string path = candidates + "/ReadOnly.mat";
            var material = CreateMaterial(path);
            FileAttributes original = File.GetAttributes(path + ".meta");
            try
            {
                File.SetAttributes(path + ".meta", original | FileAttributes.ReadOnly);
                Assert.That(Find(Scan(), material).IsUnused, Is.False);
            }
            finally { File.SetAttributes(path + ".meta", original); }
        }

        [Test]
        public void SavedContentChangeInvalidatesPreviouslyScannedIdentity()
        {
            var material = CreateMaterial(candidates + "/ChangedContent.mat");
            var original = Find(Scan(), material);
            material.renderQueue = 3123;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            Assert.That(UmaUnusedAssetUtility.CanTrash(original, out _), Is.False);
            Assert.That(UmaUnusedAssetUtility.CanTrash(Find(Scan(), material), out _), Is.True);
        }

        [Test]
        public void AlwaysIncludedShaderInGraphicsSettingsIsProtected()
        {
            var shader = CreateShader(candidates + "/AlwaysIncluded.shader");
            Object graphics = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").First();
            using (var serialized = new SerializedObject(graphics))
            {
                var included = serialized.FindProperty("m_AlwaysIncludedShaders");
                int originalSize = included.arraySize;
                try
                {
                    included.arraySize++;
                    included.GetArrayElementAtIndex(originalSize).objectReferenceValue = shader;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    Assert.That(Find(Scan(), shader).IsUnused, Is.False);
                }
                finally
                {
                    serialized.Update();
                    included.arraySize = originalSize;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        [UnityTest]
        public IEnumerator WindowOpensWithoutSelectingAssetsOrEnablingUnscannedDeletion()
        {
            Object selection = Selection.activeObject;
            var window = ScriptableObject.CreateInstance<UmaUnusedAssetsWindow>();
            try
            {
                window.Show();
                window.Repaint();
                yield return null;
                yield return null;
                Assert.That(Selection.activeObject, Is.SameAs(selection));
                var ready = typeof(UmaUnusedAssetsWindow).GetProperty("Ready", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(ready.GetValue(window), Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.Close(); }
        }

        [UnityTest]
        public IEnumerator WindowFinishesIncrementalScanAndInvalidatesResultsAfterChanges()
        {
            CreateMaterial(candidates + "/WindowCandidate.mat");
            var window = ScriptableObject.CreateInstance<UmaUnusedAssetsWindow>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(UmaUnusedAssetsWindow);
            try
            {
                window.Show();
                type.GetField("folder", flags).SetValue(window, AssetDatabase.LoadAssetAtPath<DefaultAsset>(candidates));
                // Allow fixture imports and opening the window to publish their change events.
                for (int i = 0; i < 5; i++) yield return null;
                type.GetMethod("StartScan", flags).Invoke(window, new object[] { false });
                double timeout = EditorApplication.timeSinceStartup + 30;
                while (type.GetField("work", flags).GetValue(window) != null && EditorApplication.timeSinceStartup < timeout)
                    yield return null;
                Assert.That(type.GetProperty("Ready", flags).GetValue(window), Is.True,
                    (string)type.GetField("status", flags).GetValue(window));
                type.GetMethod("UpdateVisibleRows", flags).Invoke(window, null);
                var rows = (System.Collections.IList)type.GetField("visible", flags).GetValue(window);
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(((UmaUnusedAssetResult)rows[0]).IsUnused, Is.True);
                type.GetMethod("Changed", flags).Invoke(window, null);
                Assert.That(type.GetProperty("Ready", flags).GetValue(window), Is.False);
                window.Repaint();
                yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.Close(); }
        }

        private UmaUnusedAssetScan Scan()
        {
            var scan = new UmaUnusedAssetScan(candidates);
            foreach (string step in scan.Run()) { }
            Assert.That(scan.Complete, Is.True);
            return scan;
        }

        private static UmaUnusedAssetResult Find(UmaUnusedAssetScan scan, Object asset) =>
            scan.Results.Single(result => result.Path == AssetDatabase.GetAssetPath(asset));

        private static UMAMaterial CreateUma(string path)
        {
            var asset = ScriptableObject.CreateInstance<UMAMaterial>();
            asset.name = Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material CreateMaterial(string path, Shader shader = null)
        {
            var material = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private UMAAssetIndexer CreateRegistry(params AssetItem[] items)
        {
            var registry = ScriptableObject.CreateInstance<UMAAssetIndexer>();
            registry.SerializedItems.AddRange(items);
            for (int i = 0; i < items.Length; i++) items[i].Index = i;
            AssetDatabase.CreateAsset(registry, root + "/Index_" + Guid.NewGuid().ToString("N") + ".asset");
            AssetDatabase.SaveAssetIfDirty(registry);
            return registry;
        }

        private static AssetItem Entry(Object asset, string identity)
        {
            var item = new AssetItem(asset.GetType(), asset.name, AssetDatabase.GetAssetPath(asset), identity == "Object" ? asset : null);
            if (identity == "Name" || identity == "Path") item._Guid = "";
            if (identity == "Name") item._Path = "";
            return item;
        }

        private bool DeleteFixture(UmaUnusedAssetResult result, Func<string, bool> mover, out string reason)
        {
            Assert.That(result.Path, Does.StartWith(root + "/"), "Only test fixtures can be deleted.");
            object[] arguments = { result, mover, null };
            var method = typeof(UmaUnusedAssetUtility).GetMethod("TrashAndUnregisterCore", BindingFlags.Static | BindingFlags.NonPublic);
            bool success = (bool)method.Invoke(null, arguments);
            reason = (string)arguments[2];
            return success;
        }

        private Shader CreateShader(string path, string name = null)
        {
            // Shader source is a text fixture, not a native Unity asset.
            File.WriteAllText(path, "Shader \"" + (name ?? "Hidden/" + root.Substring(7) + "/" + Path.GetFileNameWithoutExtension(path)) +
                "\" { SubShader { Pass { } } }");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Shader>(path);
        }
    }
}
