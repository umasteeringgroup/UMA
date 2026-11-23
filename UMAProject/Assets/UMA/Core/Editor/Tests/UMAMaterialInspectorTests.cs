using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAMaterialInspectorTests
    {
        private UMAMaterial material;
        private UMAMaterialInspector inspector;
        private List<Object> pending;
        private Object[] previousSelection;
        private HashSet<EditorWindow> previousWindows;
        private readonly List<EditorWindow> openedWindows = new();
        private static readonly System.Type InspectorWindowType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");

        private static EditorWindow[] InspectorWindows() => Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(w => w.GetType() == InspectorWindowType).ToArray();

        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.objects;
            previousWindows = new HashSet<EditorWindow>(InspectorWindows());
            material = ScriptableObject.CreateInstance<UMAMaterial>();
            inspector = (UMAMaterialInspector)Editor.CreateEditor(material);
            pending = (List<Object>)typeof(UMAMaterialInspector).GetField("_inspectedObjects", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inspector);
        }

        [TearDown]
        public void TearDown()
        {
            CaptureOpenedWindows();
            foreach (var window in openedWindows) if (window != null) window.Close();
            openedWindows.Clear();
            Object.DestroyImmediate(inspector);
            Object.DestroyImmediate(material);
            Selection.objects = previousSelection;
        }

        private EditorWindow[] CaptureOpenedWindows()
        {
            var windows = InspectorWindows().Where(w => !previousWindows.Contains(w)).ToArray();
            foreach (var window in windows) if (!openedWindows.Contains(window)) openedWindows.Add(window);
            return windows;
        }

        [Test]
        public void MissingOrDestroyedTargetsDoNotOpenWindowsOrChangeSelection()
        {
            Assert.That(InspectorUtlity.InspectTarget(null), Is.Null);
            var destroyed = ScriptableObject.CreateInstance<UMAMaterial>();
            Object.DestroyImmediate(destroyed);
            Assert.That(InspectorUtlity.InspectTarget(destroyed), Is.Null);
            Assert.That(CaptureOpenedWindows(), Is.Empty);
            Assert.That(Selection.objects, Is.EqualTo(previousSelection));
        }

        [Test]
        public void InvalidQueuedTargetsAreConsumedWithoutOpeningWindowsOnLaterUpdates()
        {
            var destroyed = ScriptableObject.CreateInstance<UMAMaterial>();
            Object.DestroyImmediate(destroyed);
            pending.Add(null);
            pending.Add(destroyed);
            for (int i = 0; i < 5; i++) inspector.DoInspectors();
            Assert.That(pending, Is.Empty);
            Assert.That(CaptureOpenedWindows(), Is.Empty);
        }

        [Test]
        public void ClosingInspectorCancelsPendingRequests()
        {
            pending.Add(material);
            inspector.OnDisable();
            inspector.DoInspectors();
            Assert.That(pending, Is.Empty);
            Assert.That(CaptureOpenedWindows(), Is.Empty);
        }

        [Test]
        public void ChannelsAddedToAnEmptyUseExistingTexturesMaterialBecomeTintedTexture()
        {
            material.materialType = UMAMaterial.MaterialType.UseExistingTextures;
            using var serialized = new SerializedObject(material);
            serialized.FindProperty("channels").arraySize = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // The first channels of an empty list are created zeroed, which is ChannelType.Texture.
            Assert.That(material.channels.Select(c => c.channelType), Is.All.EqualTo(UMAMaterial.ChannelType.Texture));

            Assert.That(UMAMaterialInspector.ApplyUseExistingTexturesChannelRule(serialized), Is.True);
            Assert.That(material.channels.Select(c => c.channelType), Is.All.EqualTo(UMAMaterial.ChannelType.TintedTexture));
            Assert.That(UMAMaterialInspector.ApplyUseExistingTexturesChannelRule(serialized), Is.False);
        }

        [TestCase(UMAMaterial.MaterialType.Atlas)]
        [TestCase(UMAMaterial.MaterialType.NoAtlas)]
        public void ChannelTypesOfGeneratedTextureMaterialsAreLeftAlone(UMAMaterial.MaterialType type)
        {
            material.materialType = type;
            material.channels = new[]
            {
                new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.DiffuseTexture },
                new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.NormalMap }
            };
            using var serialized = new SerializedObject(material);

            Assert.That(UMAMaterialInspector.ApplyUseExistingTexturesChannelRule(serialized), Is.False);
            Assert.That(UMAMaterial.EnsureChannelTypesForMaterialType(type, material.channels), Is.False);
            Assert.That(material.channels.Select(c => c.channelType), Is.EqualTo(new[]
                { UMAMaterial.ChannelType.DiffuseTexture, UMAMaterial.ChannelType.NormalMap }));
        }

        [Test]
        public void UseExistingTexturesChannelsBuiltInCodeBecomeTintedTexture()
        {
            var channels = new[]
            {
                new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.Texture },
                new UMAMaterial.MaterialChannel { channelType = UMAMaterial.ChannelType.NormalMap }
            };

            Assert.That(UMAMaterial.EnsureChannelTypesForMaterialType(UMAMaterial.MaterialType.UseExistingTextures, channels), Is.True);
            Assert.That(channels.Select(c => c.channelType), Is.All.EqualTo(UMAMaterial.ChannelType.TintedTexture));
        }

        [UnityTest]
        public IEnumerator ValidRequestOpensOneLockedInspectorWithoutRepeatingOnLaterUpdates()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            var target = new Material(shader) { name = "UMA Inspector Regression Material" };
            try
            {
                pending.Add(target);
                try { inspector.DoInspectors(); }
                finally { CaptureOpenedWindows(); }
                Assert.That(pending, Is.Empty);
                for (int i = 0; i < 5; i++)
                {
                    inspector.DoInspectors();
                    yield return null;
                }
                var windows = CaptureOpenedWindows();
                Assert.That(windows, Has.Length.EqualTo(1));
                Assert.That(InspectorWindowType.GetProperty("isLocked", BindingFlags.Public | BindingFlags.Instance).GetValue(windows[0]), Is.EqualTo(true));
                Assert.That(InspectorUtlity.GetInspectorsEditors(windows[0]).Any(editor => editor != null && editor.target == target), Is.True);
            }
            finally
            {
                foreach (var window in openedWindows) if (window != null) window.Close();
                openedWindows.Clear();
                Object.DestroyImmediate(target);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
