#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintLayerMergeUITests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object Invoke(TexturePaintStageWindow stage, string method, params object[] args)
            => typeof(TexturePaintStageWindow).GetMethod(method, Private).Invoke(stage, args);

        [TestCase(false)]
        [TestCase(true)]
        public void ModifierClicksOnGroupFolderMaskAndNameMarkWithoutChangingActiveLayer(bool command)
        {
            var stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var window = ScriptableObject.CreateInstance<TexturePaintMergeInputTestWindow>();
            var set = new TextureSet();
            var active = new TexturePaintLayer { name = "Active paint" };
            var group = new TexturePaintLayer { name = "Merge group", kind = TexturePaintLayerKind.Group };
            set.layers.Add(active);
            set.layers.Add(group);
            set.activeLayerIndex = 0;
            group.layerMask = new TexturePaintLayerMask
            {
                target = new EditableTextureTarget("Merge UI mask", 8, 8,
                    RenderTextureFormat.ARGB32, null, Color.white)
            };
            var row = new Rect(0, 0, 500, 46);
            window.draw = () => Invoke(stage, "DrawLayerRow", set, group, 1, row, -1);
            try
            {
                window.position = new Rect(100, 100, 510, 80);
                window.ShowUtility();
                window.Focus();
                var maskMode = typeof(TexturePaintStageWindow).GetField("layerMaskMode", Private);
                maskMode.SetValue(stage, true);
                bool expanded = (bool)Invoke(stage, "IsGroupExpanded", group);
                // These hit the folder, mask thumbnail, and name, respectively.
                foreach (float x in new[] { 65f, 100f, 150f })
                {
                    void Click()
                    {
                        foreach (EventType type in new[] { EventType.MouseDown, EventType.MouseUp })
                            window.SendEvent(new Event
                            {
                                type = type, button = 0, mousePosition = new Vector2(x, 20),
                                control = !command, command = command
                            });
                    }
                    Click();
                    Assert.That((List<TexturePaintLayer>)Invoke(stage, "GetLayerMergeSelection", set),
                        Is.EqualTo(new[] { group }));
                    Assert.That(set.activeLayerIndex, Is.Zero);
                    Assert.That(maskMode.GetValue(stage), Is.True);
                    Assert.That(Invoke(stage, "IsGroupExpanded", group), Is.EqualTo(expanded));
                    Click();
                    Assert.That((List<TexturePaintLayer>)Invoke(stage, "GetLayerMergeSelection", set), Is.Empty);
                    Assert.That(set.activeLayerIndex, Is.Zero);
                }
                Invoke(stage, "ToggleLayerMergeSelection", set, group);
                window.SendEvent(new Event { type = EventType.MouseDown, button = 0,
                    mousePosition = new Vector2(150, 20) });
                Assert.That(set.activeLayerIndex, Is.EqualTo(1), "An ordinary click still activates the layer.");
                Assert.That((List<TexturePaintLayer>)Invoke(stage, "GetLayerMergeSelection", set), Is.Empty);
                Assert.That(maskMode.GetValue(stage), Is.False);
            }
            finally
            {
                window.draw = null;
                window.Close();
                set.Dispose();
                Object.DestroyImmediate(stage);
            }
        }
    }

    internal sealed class TexturePaintMergeInputTestWindow : EditorWindow
    {
        internal Action draw;
        private void OnGUI() => draw?.Invoke();
    }
}
#endif
