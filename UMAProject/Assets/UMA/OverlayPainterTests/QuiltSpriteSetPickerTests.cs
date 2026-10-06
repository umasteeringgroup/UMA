#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class QuiltSpriteSetPickerTests
    {
        [Test]
        public void AlbedoTileClickTogglesSelectionAndCancelLeavesSourceUnchanged()
        {
            var source = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(
                TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Textures/LeatherSpriteSet.asset"));
            Assert.That(source, Is.Not.Null);
            var sprites = TexturePaintSpriteSetSource.Resolve(source)[TexturePaintChannel.Albedo];
            var selected = new HashSet<int> { 0, 2 };
            string before = EditorJsonUtility.ToJson(source);
            var window = ScriptableObject.CreateInstance<QuiltSpriteSetTileWindow>();
            var previous = EditorWindow.focusedWindow;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(QuiltSpriteSetTileWindow).GetField("source", flags).SetValue(window, source);
            typeof(QuiltSpriteSetTileWindow).GetField("sprites", flags).SetValue(window, sprites);
            typeof(QuiltSpriteSetTileWindow).GetField("enabled", flags).SetValue(window, selected);
            try
            {
                // Exercise the same OnGUI body without blocking the test runner in ShowModal.
                window.ShowUtility(); window.position = new Rect(100, 100, 640, 540); window.Focus();
                window.SendEvent(new Event { type = EventType.Layout });
                window.SendEvent(new Event { type = EventType.Repaint });
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(50, 120) });
                Assert.That(selected.Contains(0), Is.False, "Clicking the first albedo tile disables it.");
                Assert.That(selected.Contains(2), Is.True);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = new Vector2(50, 120) });
                Assert.That(selected.Contains(0), Is.True, "Clicking the tile again enables it.");
                Assert.That(typeof(QuiltSpriteSetTileWindow).GetField("result", flags).GetValue(window), Is.Null,
                    "Toggling alone must not apply the selection.");
                Assert.That(EditorJsonUtility.ToJson(source), Is.EqualTo(before));
            }
            finally { window.Close(); previous?.Focus(); }
        }
    }
}
#endif
