#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintLayerPreviewTests
    {
        [TestCase(TexturePaintLayerKind.Paint)]
        [TestCase(TexturePaintLayerKind.Fill)]
        [TestCase(TexturePaintLayerKind.Spline)]
        [TestCase(TexturePaintLayerKind.Projection)]
        public void OrdinaryLayersShowTheirOwnOutput(TexturePaintLayerKind kind)
        {
            var layer = new TexturePaintLayer { kind = kind };
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, false, false), Is.EqualTo(TexturePaintLayerPreviewKind.CachedLayer));
        }

        [Test]
        public void DraftSelectionFollowsLayerAndMaskContext()
        {
            var layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Plugin, pluginId = "generator" };
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, false, false), Is.EqualTo(TexturePaintLayerPreviewKind.Plugin));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, true, false), Is.EqualTo(TexturePaintLayerPreviewKind.CachedLayer));
            layer.layerMask = new TexturePaintLayerMask { pluginId = "mask-generator" };
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, true, false), Is.EqualTo(TexturePaintLayerPreviewKind.MaskPlugin));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, false, true), Is.EqualTo(TexturePaintLayerPreviewKind.Brush));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, true, true), Is.EqualTo(TexturePaintLayerPreviewKind.MaskPlugin));
        }

        [TestCase(false)] [TestCase(true)]
        public void GarmentPathsUseDraftSettingsWithoutChangingTheLayer(bool hem)
        {
            var layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Spline, splineSettings = new TexturePaintSplineSettings() };
            if (hem) layer.splineSettings.hemSeam = new TexturePaintHemSeamSettings { enabled = true };
            else layer.splineSettings.garment = new TexturePaintGarmentSettings { enabled = true };
            string before = JsonUtility.ToJson(layer.splineSettings);
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, false, false), Is.EqualTo(TexturePaintLayerPreviewKind.GarmentPath));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, true, false), Is.EqualTo(TexturePaintLayerPreviewKind.CachedLayer));
            Assert.That(JsonUtility.ToJson(layer.splineSettings), Is.EqualTo(before));
        }

        [Test]
        public void GarmentProjectionUsesItsDraftAndMissingSelectionHasAnEmptyState()
        {
            var layer = new TexturePaintLayer { kind = TexturePaintLayerKind.Projection,
                projectionSettings = new TexturePaintProjectionSettings { garment = new TexturePaintGarmentSettings { enabled = true } } };
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(layer, false, false), Is.EqualTo(TexturePaintLayerPreviewKind.GarmentProjection));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(null, false, false), Is.EqualTo(TexturePaintLayerPreviewKind.None));
            Assert.That(TexturePaintStageWindow.LayerPreviewKind(null, false, true), Is.EqualTo(TexturePaintLayerPreviewKind.Brush));
        }

        [TestCase(360, 300)] [TestCase(640, 480)] [TestCase(1920, 1080)]
        public void FloatingPreviewRemainsReachableAfterResizing(int width, int height)
        {
            var bounds = TexturePaintUVWindow.ClampLayerPreview(new Rect(1800, 1000, 380, 200), new Vector2(width, height));
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(8));
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(8));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(width - 8));
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(height - 8));
            Assert.That(bounds.width, Is.GreaterThanOrEqualTo(200));
        }

        [Test]
        public void CollapsedPreviewRetainsItsHeightWhenMoved()
        {
            var bounds = TexturePaintUVWindow.ClampLayerPreview(new Rect(-500, -500, 380, 24), new Vector2(640, 480));
            Assert.That(bounds.position, Is.EqualTo(new Vector2(8, 8)));
            Assert.That(bounds.height, Is.EqualTo(24));
        }

        [Test]
        public void FloatingTitleControlsReopenTheCollapsedPreviewAndMoveIt()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var host = ScriptableObject.CreateInstance<TexturePaintRegionInputTestWindow>();
            var uv = ScriptableObject.CreateInstance<TexturePaintUVWindow>();
            var stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var rect = typeof(TexturePaintUVWindow).GetField("layerPreviewRect", flags);
            var collapsed = typeof(TexturePaintUVWindow).GetField("layerPreviewCollapsed", flags);
            var draw = typeof(TexturePaintUVWindow).GetMethod("DrawFloatingLayerPreview", flags);
            int hot = GUIUtility.hotControl;
            try
            {
                uv.position = new Rect(100, 100, 720, 540);
                rect.SetValue(uv, new Rect(60, 60, 380, 200));
                host.position = new Rect(100, 100, 720, 540);
                var input = new List<string>();
                host.draw = () =>
                {
                    bool mouse = Event.current.isMouse;
                    if (mouse) input.Add(Event.current.type + " " + Event.current.mousePosition + " enabled=" + GUI.enabled + " hot=" + GUIUtility.hotControl);
                    draw.Invoke(uv, new object[] { stage });
                    if (mouse) input.Add("after " + Event.current.type + " collapsed=" + collapsed.GetValue(uv) + " hot=" + GUIUtility.hotControl);
                };
                host.ShowUtility();
                // ShowUtility restores the host type's saved size; establish input bounds afterwards.
                host.position = new Rect(100, 100, 720, 540);
                host.Focus(); GUIUtility.hotControl = 0;
                void Frame() { host.SendEvent(new Event { type = EventType.Layout }); host.SendEvent(new Event { type = EventType.Repaint }); }
                void Send(EventType type, Vector2 point, Vector2 delta = default)
                    => host.SendEvent(new Event { type = type, button = 0, mousePosition = point, delta = delta });
                void Toggle()
                {
                    Rect bounds = (Rect)rect.GetValue(uv);
                    var point = new Vector2(bounds.xMax - 15, bounds.y + 10);
                    Send(EventType.MouseDown, point); Send(EventType.MouseUp, point); Frame();
                }
                Frame(); Toggle();
                Assert.That(collapsed.GetValue(uv), Is.True, string.Join("; ", input));
                Assert.That(((Rect)rect.GetValue(uv)).height, Is.EqualTo(24));
                Toggle();
                Assert.That(collapsed.GetValue(uv), Is.False, "The title button must remain clickable when collapsed.");
                Assert.That(((Rect)rect.GetValue(uv)).height, Is.EqualTo(200));
                var start = new Vector2(90, 70);
                Send(EventType.MouseDown, start);
                Send(EventType.MouseDrag, start + new Vector2(40, 30), new Vector2(40, 30));
                Send(EventType.MouseUp, start + new Vector2(40, 30));
                Assert.That(((Rect)rect.GetValue(uv)).position, Is.EqualTo(new Vector2(100, 90)));
                Assert.That(GUIUtility.hotControl, Is.Zero);
            }
            finally
            {
                host.draw = null; host.Close();
                UnityEngine.Object.DestroyImmediate(uv); UnityEngine.Object.DestroyImmediate(stage);
                GUIUtility.hotControl = hot;
            }
        }

        [TestCase(TexturePaintChannel.Albedo)] [TestCase(TexturePaintChannel.Roughness)]
        public void CachedLayerReadbackPreservesWorkingPixelsAndActiveTarget(TexturePaintChannel channel)
        {
            var color = new Color(.21404114f, .07323896f, .01002283f, .65f);
            using var source = new EditableTextureTarget("Preview readback test", 16, 32, RenderTextureFormat.ARGBFloat, null, color);
            var active = RenderTexture.active;
            var method = typeof(TexturePaintStageWindow).GetMethod("ReadLayerPreview", BindingFlags.Static | BindingFlags.NonPublic);
            var output = (Dictionary<TexturePaintChannel, Color[]>)method.Invoke(null,
                new object[] { new Dictionary<TexturePaintChannel, RenderTexture> { [channel] = source.Front } });
            Assert.That(output[channel], Has.Length.EqualTo(128 * 128));
            Assert.That(output[channel][128 * 64 + 64].r, Is.EqualTo(color.r).Within(.00001f));
            Assert.That(output[channel][128 * 64 + 64].a, Is.EqualTo(color.a).Within(.00001f));
            Assert.That(RenderTexture.active, Is.SameAs(active));
        }
    }
}
#endif
