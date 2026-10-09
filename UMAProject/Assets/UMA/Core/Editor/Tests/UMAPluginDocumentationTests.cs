using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UMA.Editors.Tests
{
    public sealed class UMAPluginDocumentationTests
    {
        private string root, id, descriptor;
        [SetUp]
        public void SetUp()
        {
            id = "documentation-test-" + Guid.NewGuid().ToString("N");
            root = "Assets/" + id;
            AssetDatabase.CreateFolder("Assets", id);
            AssetDatabase.CreateFolder(root, "Documentation");
            File.WriteAllText(root + "/Documentation/Guide.md", "# Plugin Guide");
            descriptor = root + "/" + UMAPluginDocumentationRegistry.DescriptorFileName;
            File.WriteAllText(descriptor, "{\"pluginId\":\"" + id + "\",\"displayName\":\"Test Plugin\",\"documentationFolder\":\"Documentation\"}");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void TearDown()
        {
            UMAPluginDocumentationRegistry.Unregister(id);
            AssetDatabase.DeleteAsset(root);
        }

        [UnityTest]
        public IEnumerator ImportRegistersDocumentationAutomatically()
        {
            for (int i = 0; i < 10 && !UMAPluginDocumentationRegistry.Registrations.Any(r => r.pluginId == id); i++) yield return null;
            Assert.That(UMAPluginDocumentationRegistry.Registrations.Single(r => r.pluginId == id).DocumentationPath,
                Is.EqualTo(root + "/Documentation"));
        }

        [Test]
        public void RegistrationIsIdempotentAndUnregisterPreservesDocuments()
        {
            UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor);
            Assert.That(UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor), Is.False);
            UMAPluginDocumentationRegistry.Unregister(id);
            UMAPluginDocumentationRegistry.PruneMissing();
            Assert.That(UMAPluginDocumentationRegistry.Registrations.Any(r => r.pluginId == id), Is.False);
            Assert.That(File.Exists(root + "/Documentation/Guide.md"), Is.True);
        }

        [Test]
        public void DeletedDescriptorRemovesMetadata()
        {
            UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor);
            AssetDatabase.DeleteAsset(descriptor);
            UMAPluginDocumentationRegistry.PruneMissing();
            Assert.That(UMAPluginDocumentationRegistry.Registrations.Any(r => r.pluginId == id), Is.False);
        }

        [Test]
        public void MovedPluginRetainsDocumentationByGuid()
        {
            UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor);
            string moved = root + "-moved";
            Assert.That(AssetDatabase.MoveAsset(root, moved), Is.Empty);
            root = moved;
            UMAPluginDocumentationRegistry.PruneMissing();
            Assert.That(UMAPluginDocumentationRegistry.Registrations.Single(r => r.pluginId == id).DocumentationPath,
                Is.EqualTo(root + "/Documentation"));
        }

        [TestCase("../Docs")]
        [TestCase("/Assets/UMA/Docs")]
        public void InvalidDocumentationRootIsRejected(string folder)
        {
            File.WriteAllText(descriptor, "{\"pluginId\":\"" + id + "\",\"displayName\":\"Test Plugin\",\"documentationFolder\":\"" + folder + "\"}");
            AssetDatabase.ImportAsset(descriptor, ImportAssetOptions.ForceSynchronousImport);
            Assert.Throws<InvalidDataException>(() => UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BrowserDefaultsToUmaSeparatesSourcesAndFallsBackAfterRemoval(bool nestedInUmaDocs)
        {
            if (nestedInUmaDocs)
            {
                string nested = UMAEditorUtilities.FindUMAFullPath() + "/Docs/" + id;
                Assert.That(AssetDatabase.MoveAsset(root, nested), Is.Empty);
                root = nested; descriptor = root + "/" + UMAPluginDocumentationRegistry.DescriptorFileName;
            }
            UMAPluginDocumentationRegistry.RegisterDescriptor(descriptor);
            var window = ScriptableObject.CreateInstance<UMADocumentationWindow>();
            var configuration = ScriptableObject.CreateInstance<UMADocumentConfiguration>();
            configuration.hideFlags = HideFlags.HideAndDontSave;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                typeof(UMADocumentationWindow).GetField("configuration", flags).SetValue(window, configuration);
                window.Show();
                typeof(UMADocumentationWindow).GetMethod("CreateGUI", flags).Invoke(window, null);
                var selector = window.rootVisualElement.Q<PopupField<string>>("documentation-source");
                var paths = (System.Collections.Generic.List<string>)typeof(UMADocumentationWindow).GetField("documentationPaths", flags).GetValue(window);
                Assert.That(selector.value, Is.EqualTo("UMA"));
                Assert.That(paths.Any(p => p.StartsWith(root + "/")), Is.False);
                selector.value = "Test Plugin";
                Assert.That(paths, Is.EqualTo(new[] { root + "/Documentation/Guide.md" }));
                UMAPluginDocumentationRegistry.Unregister(id);
                Assert.That(selector.value, Is.EqualTo("UMA"));
                Assert.That(selector.choices, Does.Not.Contain("Test Plugin"));
            }
            finally
            {
                window.Close();
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }
    }
}
