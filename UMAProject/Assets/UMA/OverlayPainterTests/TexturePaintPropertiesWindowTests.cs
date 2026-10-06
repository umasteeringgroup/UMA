#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPropertiesWindowTests
    {
        [Serializable] private sealed class Layout { public Root view; }
        [Serializable] private sealed class Root { public Group[] children; }
        [Serializable] private sealed class Group
        {
            public bool vertical;
            public Panel[] children;
        }
        [Serializable] private sealed class Panel
        {
            public bool tabs;
            public string class_name;
            public Pane[] children;
        }
        [Serializable] private sealed class Pane { public string class_name; }

        [TearDown]
        public void TearDown()
        {
            TexturePaintDockWindow.LayoutTransitionInProgress = true;
            try
            {
                TexturePaintPropertiesWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintBrushWindow.CloseOpenWindowsForLayoutChange();
                TexturePaintDockWindow.CloseOpenWindowsForLayoutChange();
            }
            finally { TexturePaintDockWindow.LayoutTransitionInProgress = false; }
        }

        [Test]
        public void CompactLayoutStacksPropertiesBelowLayersAndBrush()
        {
            var layout = JsonUtility.FromJson<Layout>(TexturePaintWorkspaceLayout.CompactLayoutDefinition);
            Group group = layout.view.children[0];
            Assert.That(group.vertical, Is.True);
            Assert.That(group.children[0].tabs, Is.True);
            CollectionAssert.AreEqual(new[] { "TexturePaintDockWindow", "TexturePaintBrushWindow" },
                group.children[0].children.Select(child => child.class_name).ToArray());
            Assert.That(group.children[1].tabs, Is.True);
            Assert.That(group.children[1].children.Single().class_name, Is.EqualTo("TexturePaintPropertiesWindow"));
            Assert.That(layout.view.children[1].children.Any(child => child.class_name == "TexturePaintPropertiesWindow"),
                Is.False, "Properties belongs below Layers, not in the canvas group.");
        }

        [Test]
        public void NewPropertiesWindowDocksWithLayersAndBrushAndReopensWithoutDuplicates()
        {
            TexturePaintDockWindow.ShowDockable();
            TexturePaintBrushWindow.ShowDockable();
            TexturePaintPropertiesWindow.ShowDockable();
            var layers = Resources.FindObjectsOfTypeAll<TexturePaintDockWindow>().Single();
            var brush = Resources.FindObjectsOfTypeAll<TexturePaintBrushWindow>().Single();
            var properties = Resources.FindObjectsOfTypeAll<TexturePaintPropertiesWindow>().Single();
            Assert.That(properties.titleContent.text, Is.EqualTo("Overlay Painter Properties"));
            Assert.That(Parent(properties), Is.SameAs(Parent(layers)));
            Assert.That(Parent(brush), Is.SameAs(Parent(layers)));
            TexturePaintPropertiesWindow.ShowDockable();
            Assert.That(Resources.FindObjectsOfTypeAll<TexturePaintPropertiesWindow>().Single(), Is.SameAs(properties));
            properties.Close();
            Assert.That(layers != null && brush != null, Is.True,
                "Closing Properties must leave the other painter windows open.");
        }

        [Test]
        public void OpeningAnExistingFloatingPropertiesWindowPreservesItsPlacement()
        {
            var properties = ScriptableObject.CreateInstance<TexturePaintPropertiesWindow>();
            properties.Show();
            object originalParent = Parent(properties);
            TexturePaintDockWindow.ShowDockable();
            var layers = Resources.FindObjectsOfTypeAll<TexturePaintDockWindow>().Single();
            Assert.That(originalParent, Is.Not.SameAs(Parent(layers)));
            TexturePaintPropertiesWindow.ShowDockable();
            Assert.That(Parent(properties), Is.SameAs(originalParent),
                "Default docking must not move an existing user-arranged window.");
        }

        private static object Parent(EditorWindow window)
        {
            object parent = typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(window);
            Assert.That(parent, Is.Not.Null);
            return parent;
        }
    }
}
#endif
