using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class DynamicCharacterAvatarRendererCleanupTests
    {
        public enum CleanupPath { Regeneration, PlayModePreparation, Inspector, HideAndCleanup }

        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> resources = new List<Object>();
        private Scene scene;
        private DynamicCharacterAvatar avatar;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Renderer cleanup test");
            SceneManager.MoveGameObjectToScene(root, scene);
            avatar = root.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;
            avatar.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var resource in resources)
                if (resource != null) Object.DestroyImmediate(resource);
            resources.Clear();
        }

        [TestCase(CleanupPath.Regeneration)]
        [TestCase(CleanupPath.PlayModePreparation)]
        [TestCase(CleanupPath.Inspector)]
        [TestCase(CleanupPath.HideAndCleanup)]
        public void CleanupPreservesAuthoredAndIgnoredRenderers(CleanupPath path)
        {
            var authored = Renderer("Custom hair", "Authored mesh");
            var reservedName = Renderer("UMARenderer 3", "Imported mesh");
            var ignored = Renderer("UMARenderer 4", "UMAMesh 4");
            ignored.gameObject.tag = UMASettings.GetIgnoreTag();
            ignored.gameObject.AddComponent<UMAGeneratedRenderer>();
            var ignoredLegacy = Renderer("UMARenderer 5", "UMAMesh 5");
            ignoredLegacy.gameObject.tag = UMASettings.GetIgnoreTag();
            var nested = Renderer("Nested attachment", "UMAMesh");
            nested.transform.SetParent(authored.transform, false);
            nested.gameObject.AddComponent<UMAGeneratedRenderer>();
            var renderers = new[] { authored, reservedName, ignored, ignoredLegacy, nested };
            var meshes = new List<Mesh>();
            foreach (var renderer in renderers) meshes.Add(renderer.sharedMesh);

            Cleanup(path);

            for (int i = 0; i < renderers.Length; i++)
            {
                Assert.That(renderers[i] != null, Is.True, $"Preserve authored/ignored renderer {i}.");
                Assert.That(meshes[i] != null, Is.True, $"Preserve its mesh {i}.");
                Assert.That(renderers[i].sharedMesh, Is.SameAs(meshes[i]));
            }
        }

        [TestCase(CleanupPath.Regeneration)]
        [TestCase(CleanupPath.PlayModePreparation)]
        [TestCase(CleanupPath.Inspector)]
        [TestCase(CleanupPath.HideAndCleanup)]
        public void CleanupReleasesTrackedAndOrphanedMeshes(CleanupPath path)
        {
            var tracked = Renderer("Tracked output", "Tracked mesh");
            avatar.SetRenderers(new[] { tracked });
            var orphan = Renderer("Marked orphan", "Orphan mesh");
            orphan.gameObject.AddComponent<UMAGeneratedRenderer>();
            var inactive = Renderer("Inactive orphan", "Inactive mesh");
            inactive.gameObject.AddComponent<UMAGeneratedRenderer>();
            inactive.gameObject.SetActive(false);
            var legacy = Renderer("UMARenderer 1", "UMAMesh 1");
            var customLegacy = Renderer("Custom renderer asset name", "UMAMesh 2");
            var outputs = new[] { tracked, orphan, inactive, legacy, customLegacy };
            var objects = new List<GameObject>();
            var meshes = new List<Mesh>();
            foreach (var renderer in outputs)
            {
                objects.Add(renderer.gameObject);
                meshes.Add(renderer.sharedMesh);
            }
            var empty = new GameObject("UMARenderer 6");
            empty.transform.SetParent(avatar.transform, false);
            objects.Add(empty);

            Cleanup(path);
            Cleanup(path); // Repeating cleanup must be harmless.

            foreach (var mesh in meshes)
                Assert.That(mesh == null, Is.True, "Untracked private meshes must also be destroyed.");
            foreach (var output in objects)
                Assert.That(output == null, Is.True, "Generated renderer objects must be removed.");
            Assert.That(avatar.RendererCount, Is.Zero);
        }

        [TestCase(CleanupPath.Regeneration)]
        [TestCase(CleanupPath.PlayModePreparation)]
        [TestCase(CleanupPath.Inspector)]
        [TestCase(CleanupPath.HideAndCleanup)]
        public void CleanupPreservesFbxRouteEvenWithGeneratedNamesAndMarkers(CleanupPath path)
        {
            var renderer = Renderer("UMARenderer", "UMAMesh");
            renderer.gameObject.AddComponent<UMAGeneratedRenderer>();
            var mesh = renderer.sharedMesh;
            var route = avatar.gameObject.AddComponent<UMAFbxRouteRuntime>();
            typeof(UMAFbxRouteRuntime).GetField("baseInstanceRoot", PrivateInstance)
                .SetValue(route, renderer.gameObject);

            Cleanup(path);

            Assert.That(renderer != null, Is.True);
            Assert.That(mesh != null, Is.True);
            Assert.That(renderer.sharedMesh, Is.SameAs(mesh));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CleanupReleasesSharedMeshLeaseWithoutDestroyingPeer(bool tracked, bool inactive)
        {
            var renderer = Renderer("Shared output", "UMAMesh");
            renderer.gameObject.AddComponent<UMAGeneratedRenderer>();
            var rendererObject = renderer.gameObject;
            var mesh = renderer.sharedMesh;
            var cache = new UMAGeneratedResourceCache();
            var key = new UMAGeneratedResourceKey("Renderer cleanup", new byte[] { 1 });
            using (var peer = cache.Acquire<Mesh>(key))
            {
                peer.Publish(mesh);
                rendererObject.AddComponent<UMAResourceLeaseOwner>();
                Assert.That(cache.ReferenceCount, Is.EqualTo(2));
                if (tracked) avatar.SetRenderers(new[] { renderer });
                if (inactive) rendererObject.SetActive(false);

                avatar.HideAndCleanup(false);

                Assert.That(rendererObject == null, Is.True);
                Assert.That(cache.ReferenceCount, Is.EqualTo(1));
                Assert.That(mesh != null, Is.True, "The peer still owns this shared mesh.");
                Assert.That(peer.Resource, Is.SameAs(mesh));
            }
            Assert.That(mesh == null, Is.True, "The last lease releases the shared mesh.");
        }

        private SkinnedMeshRenderer Renderer(string objectName, string meshName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(avatar.transform, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { name = meshName };
            resources.Add(mesh);
            renderer.sharedMesh = mesh;
            return renderer;
        }

        private void Cleanup(CleanupPath path)
        {
            switch (path)
            {
                case CleanupPath.Regeneration:
                    avatar.CleanupGeneratedData();
                    break;
                case CleanupPath.PlayModePreparation:
                    typeof(DynamicCharacterAvatar).GetMethod("CleanupEditorGeneratedDataForPlayMode", PrivateInstance)
                        .Invoke(avatar, null);
                    break;
                case CleanupPath.Inspector:
                    var editor = Editor.CreateEditor(avatar);
                    try
                    {
                        editor.GetType().GetMethod("CleanupGeneratedData", PrivateInstance)
                            .Invoke(editor, new object[] { false, false });
                    }
                    finally { Object.DestroyImmediate(editor); }
                    break;
                case CleanupPath.HideAndCleanup:
                    avatar.HideAndCleanup(false);
                    break;
            }
        }
    }
}
