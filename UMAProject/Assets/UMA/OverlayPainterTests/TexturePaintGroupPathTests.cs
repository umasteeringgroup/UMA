#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    /// <summary>A group's paths edited together (Edit Paths Together): which paths show, picking one, and keeping the option.</summary>
    public sealed class TexturePaintGroupPathTests
    {
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly List<TextureSet> sets = new List<TextureSet>();

        [TearDown]
        public void TearDown()
        {
            foreach (TextureSet set in sets) set.Dispose();
            sets.Clear();
        }

        private TextureSet Set()
        {
            var set = new TextureSet
            {
                persistentId = Guid.NewGuid().ToString("N"),
                surface = new ReconstructedSurface { index = sets.Count, slotName = "Body", slotNames = new List<string> { "Body" } }
            };
            sets.Add(set);
            return set;
        }

        private static TexturePaintLayer Group(TextureSet set, string name, TexturePaintLayer parent = null)
        {
            set.activeLayerIndex = -1;
            TexturePaintLayer group = set.AddGroup(name);
            group.parentId = parent?.id;
            set.NormalizeLayerHierarchy();
            return group;
        }

        /// <summary>A UV path from one point to another, as a trim comes in.</summary>
        private static TexturePaintLayer Path(TextureSet set, string name, TexturePaintLayer parent, Vector2 from, Vector2 to)
        {
            set.activeLayerIndex = -1;
            TexturePaintLayer layer = set.AddSplineLayer(name);
            layer.parentId = parent?.id;
            layer.spline.worldSpace = false;
            layer.spline.useBezier = false;
            layer.spline.AddPoint(new Vector3(from.x, from.y, 0f), from, 0, -1, Vector3.forward);
            layer.spline.AddPoint(new Vector3(to.x, to.y, 0f), to, 0, -1, Vector3.forward);
            set.NormalizeLayerHierarchy();
            return layer;
        }

        private static void Select(TextureSet set, TexturePaintLayer layer) => set.activeLayerIndex = set.layers.IndexOf(layer);

        [Test]
        public void GroupPaths_AreTheGroupsVisiblePaths_WhenItOrOneOfThemIsSelected()
        {
            TextureSet set = Set();
            TexturePaintLayer trim = Group(set, "Trim");
            Assert.That(trim.editPathsTogether, Is.True, "On by default.");
            TexturePaintLayer zipper = Path(set, "Zipper", trim, new Vector2(0.1f, 0.1f), new Vector2(0.1f, 0.9f));
            TexturePaintLayer hem = Path(set, "Hem", trim, new Vector2(0.2f, 0.1f), new Vector2(0.8f, 0.1f));
            TexturePaintLayer hidden = Path(set, "Hidden", trim, new Vector2(0.5f, 0.5f), new Vector2(0.6f, 0.6f));
            hidden.visible = false;
            TexturePaintLayer pockets = Group(set, "Pockets", trim);
            TexturePaintLayer pocket = Path(set, "Pocket Stitch", pockets, new Vector2(0.3f, 0.3f), new Vector2(0.4f, 0.3f));
            TexturePaintLayer outside = Path(set, "Outside", null, new Vector2(0.9f, 0.9f), new Vector2(0.9f, 0.5f));
            set.activeLayerIndex = -1;
            TexturePaintLayer paint = set.AddLayer("Paint");
            paint.parentId = trim.id;
            set.NormalizeLayerHierarchy();

            Select(set, trim);
            Assert.That(TexturePaintStageWindow.PathGroupFor(set), Is.SameAs(trim));
            var expected = new[] { set.layers.IndexOf(zipper), set.layers.IndexOf(hem), set.layers.IndexOf(pocket) };
            Assert.That(TexturePaintStageWindow.PathLayersIn(set, trim), Is.EquivalentTo(expected),
                "Its visible paths at any depth; not a hidden one, a paint layer or a path outside it.");
            Select(set, hem);
            Assert.That(TexturePaintStageWindow.PathGroupFor(set), Is.SameAs(trim), "A path of the group selected: the same group.");
            Select(set, pocket);
            Assert.That(TexturePaintStageWindow.PathGroupFor(set), Is.SameAs(pockets), "The nearest group of the selected path.");
            Select(set, outside);
            Assert.That(TexturePaintStageWindow.PathGroupFor(set), Is.Null);

            pockets.visible = false;
            Assert.That(TexturePaintStageWindow.PathLayersIn(set, trim), Has.No.Member(set.layers.IndexOf(pocket)), "Inside a hidden group: not shown.");
            trim.editPathsTogether = false;
            Select(set, zipper);
            Assert.That(TexturePaintStageWindow.PathGroupFor(set), Is.Null, "Turned off.");
        }

        [Test]
        public void UVPaths_ArePickedNearTheirCurve_TheNearestFirst()
        {
            TextureSet set = Set();
            TexturePaintLayer group = Group(set, "Trim");
            TexturePaintLayer across = Path(set, "Across", group, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            var rect = new Rect(10f, 20f, 200f, 100f);
            List<Vector2> points = TexturePaintStageWindow.UVPathCanvasPoints(across.spline, rect);
            Assert.That(points.Count, Is.GreaterThan(2));
            Assert.That(Vector2.Distance(points[0], new Vector2(10f, 70f)), Is.LessThan(1e-3f), "u right, v up: the left end, half way down.");
            Assert.That(Vector2.Distance(points[points.Count - 1], new Vector2(210f, 70f)), Is.LessThan(1e-3f));
            var curves = new List<(int, List<Vector2>)>
            {
                (1, new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 0f) }),
                (2, new List<Vector2> { new Vector2(0f, 6f), new Vector2(100f, 6f) })
            };
            Assert.That(TexturePaintStageWindow.NearestCurve(curves, new Vector2(50f, 2f), 8f), Is.EqualTo(1));
            Assert.That(TexturePaintStageWindow.NearestCurve(curves, new Vector2(50f, 5f), 8f), Is.EqualTo(2));
            Assert.That(TexturePaintStageWindow.NearestCurve(curves, new Vector2(50f, 30f), 8f), Is.EqualTo(-1), "Out of reach.");
            Assert.That(TexturePaintStageWindow.NearestCurve(curves, new Vector2(108f, 0f), 8f), Is.EqualTo(-1), "Past the end, out of reach.");
            across.spline.worldSpace = true;
            Assert.That(TexturePaintStageWindow.UVPathCanvasPoints(across.spline, rect), Is.Empty, "A path laid on the model in 3D is not drawn in UV.");
        }

        [Test]
        public void EditPathsTogether_IsKeptBySavingRestoringAndCopying()
        {
            TextureSet set = Set();
            TexturePaintLayer group = Group(set, "Trim");
            group.editPathsTogether = false;
            var capture = typeof(TexturePaintDocumentStorage).GetMethod("CaptureLayerMetadata", Statics);
            var clone = typeof(TexturePaintDocumentStorage).GetMethod("CloneDocumentLayer", Statics);
            var restore = typeof(TexturePaintDocumentStorage).GetMethod("RestoreLayer", Statics, null, new[] { typeof(TexturePaintDocumentLayer), typeof(TextureSet) }, null);
            Assert.That(capture, Is.Not.Null); Assert.That(clone, Is.Not.Null); Assert.That(restore, Is.Not.Null);
            var saved = (TexturePaintDocumentLayer)capture.Invoke(null, new object[] { group });
            Assert.That(saved.editPathsTogether, Is.False);
            var copied = (TexturePaintDocumentLayer)clone.Invoke(null, new object[] { saved });
            Assert.That(copied.editPathsTogether, Is.False);
            TextureSet other = Set();
            restore.Invoke(null, new object[] { copied, other });
            Assert.That(other.layers[other.layers.Count - 1].editPathsTogether, Is.False, "Restored from the document.");
            Assert.That(set.CloneLayer(group, null, true, false).editPathsTogether, Is.False, "Copied for Undo and Duplicate.");
            Assert.That(new TexturePaintDocumentLayer().editPathsTogether, Is.True, "A document saved before the option has it on.");
            Assert.That(JsonUtility.FromJson<TexturePaintDocumentLayer>("{\"name\":\"Old Group\"}").editPathsTogether, Is.True);
        }
    }
}
#endif
