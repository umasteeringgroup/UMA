#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintLayerMergeTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int Size = 64;
        private readonly List<TextureSet> sets = new();
        private readonly List<Object> objects = new();
        private readonly List<TexturePaintStageWindow> stages = new();
        private readonly List<TextureStore> stores = new();
        private TextureLayerCompositor compositor;

        [SetUp]
        public void SetUp()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var stage in stages)
            {
                Invoke(stage, "ClearLightweightHistory");
                typeof(TexturePaintStageWindow).GetField("controller", Private).SetValue(stage, null);
                Object.DestroyImmediate(stage);
            }
            stages.Clear();
            foreach (var store in stores)
            {
                ((List<TextureSet>)typeof(TextureStore).GetField("sets", Private).GetValue(store)).Clear();
                store.Dispose();
            }
            stores.Clear();
            foreach (var set in sets) { set.compositor = null; set.Dispose(); }
            sets.Clear();
            compositor?.Dispose();
            foreach (var obj in objects) if (obj != null) Object.DestroyImmediate(obj);
            objects.Clear();
        }

        private TextureSet Set(params TexturePaintChannel[] channels)
        {
            var set = new TextureSet
            {
                persistentId = Guid.NewGuid().ToString("N"), compositor = compositor,
                surface = new ReconstructedSurface { index = sets.Count, slotName = "Body",
                    slotNames = new List<string> { "Body" } }
            };
            if (channels.Length == 0) channels = new[] { TexturePaintChannel.Albedo };
            foreach (var channel in channels)
                set.channels.Add(channel, new TextureChannelTarget
                {
                    channel = channel, materialProperty = "_" + channel, format = RenderTextureFormat.ARGBHalf,
                    editable = Target("Merge backdrop", new Color(.18f, .32f, .46f, .8f)),
                    composite = EditableTextureTarget.Create("Merge composite", Size, Size, RenderTextureFormat.ARGBHalf)
                });
            sets.Add(set);
            return set;
        }

        private static EditableTextureTarget Target(string name, Color color) =>
            new(name, Size, Size, RenderTextureFormat.ARGBHalf, null, color);

        private Texture2D Image(Func<int, int, Color> sample)
        {
            var image = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            objects.Add(image);
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++) pixels[y * Size + x] = sample(x, y);
            image.SetPixels(pixels); image.Apply();
            return image;
        }

        private static TexturePaintLayer Group(TextureSet set, string name, TexturePaintLayer parent = null)
        {
            set.activeLayerIndex = -1;
            var group = set.AddGroup(name); group.parentId = parent?.id;
            set.NormalizeLayerHierarchy();
            return group;
        }

        private static TexturePaintLayer Paint(TextureSet set, string name, Color color,
            TexturePaintLayer parent = null, TexturePaintLayerKind kind = TexturePaintLayerKind.Paint)
        {
            set.activeLayerIndex = -1;
            var layer = kind == TexturePaintLayerKind.Plugin ? set.AddPluginLayer(name) : set.AddLayer(name);
            layer.parentId = parent?.id;
            foreach (var channel in set.channels.Keys)
            {
                var target = set.GetChannel(channel);
                layer.channels.Add(channel, new EditableTextureTarget(name + " " + channel,
                    target.editable.Width, target.editable.Height, target.format, null, color));
                layer.GetChannelSettings(channel);
            }
            set.NormalizeLayerHierarchy();
            return layer;
        }

        private static Color[] Composite(TextureSet set, TexturePaintChannel channel = TexturePaintChannel.Albedo)
        {
            set.RecomposeAll();
            return TexturePaintGpuTestFixture.ReadPixels(set.GetChannel(channel).composite);
        }

        private static object Invoke(TexturePaintStageWindow stage, string method, params object[] args)
        {
            var member = typeof(TexturePaintStageWindow).GetMethod(method, Private);
            Assert.That(member, Is.Not.Null, "Missing editor operation " + method);
            return member.Invoke(stage, args);
        }

        private TexturePaintStageWindow Stage(TextureSet set = null)
        {
            var stage = ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            stages.Add(stage);
            // The production launch path creates the editable brush before syncing a Paint row.
            // These tests construct the window directly, so provide that same prerequisite.
            var transientBrush = ScriptableObject.CreateInstance<BrushPreset>(); objects.Add(transientBrush);
            typeof(TexturePaintStageWindow).GetField("transientBrush", Private).SetValue(stage, transientBrush);
            if (set != null)
            {
                var store = new TextureStore(); stores.Add(store);
                ((List<TextureSet>)typeof(TextureStore).GetField("sets", Private).GetValue(store)).Add(set);
                var controller = new TexturePaintStageController();
                typeof(TexturePaintStageController).GetProperty("Textures").SetValue(controller, store);
                typeof(TexturePaintStageWindow).GetField("controller", Private).SetValue(stage, controller);
            }
            return stage;
        }

        private static void ReplaceSelection(TextureSet set, IReadOnlyList<TexturePaintLayer> selected,
            TexturePaintLayer merged)
        {
            Assert.That(set.TryGetMergeSelection(selected, out _, out var sources, out int insertion, out string reason),
                Is.True, reason);
            foreach (var source in sources) set.layers.Remove(source);
            set.layers.Insert(insertion, merged);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MergeDownBakesCachedPluginOnEitherSideAndPreservesTransparency(bool pluginAbove)
        {
            var set = Set();
            var lower = Paint(set, "Lower", new Color(.85f, .13f, .27f, .45f), kind:
                pluginAbove ? TexturePaintLayerKind.Paint : TexturePaintLayerKind.Plugin);
            var upper = Paint(set, "Upper", new Color(.12f, .81f, .36f, .6f), kind:
                pluginAbove ? TexturePaintLayerKind.Plugin : TexturePaintLayerKind.Paint);
            var plugin = pluginAbove ? upper : lower;
            plugin.pluginId = "tests.cached-filter"; plugin.pluginStale = false;
            upper.opacity = .7f;
            set.AddLayerMask(plugin, .5f);
            var before = Composite(set);
            Assert.That(set.CanMergeLayerDown(set.layers.IndexOf(upper), out string reason), Is.True, reason);
            Assert.That(set.MergeLayerDown(set.layers.IndexOf(upper)), Is.True);
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Assert.That(set.layers[0].kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            Assert.That(set.layers[0].pluginId, Is.Null.Or.Empty);
            TexturePaintGpuTestFixture.AssertImage("Plugin merge down " + pluginAbove, before, Composite(set));
            Assert.That(TexturePaintGpuTestFixture.ReadPixels(set.layers[0].channels[TexturePaintChannel.Albedo].Front)[0].a,
                Is.InRange(.1f, .9f), "Baked paint keeps coverage instead of baking the base texture.");
        }

        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 0)]
        [TestCase(2, 1)]
        public void MergedPaintKeepsIndependentSymmetryFrameFromRetainedRoot(int selectionKind, int version)
        {
            var set = Set();
            var lower = selectionKind == 0
                ? Paint(set, "Lower", Color.red) : Group(set, "Lower group");
            if (selectionKind != 0) Paint(set, "Lower child", Color.red, lower);
            var selected = new List<TexturePaintLayer> { lower };
            if (selectionKind != 1)
            {
                var upper = selectionKind == 0
                    ? Paint(set, "Upper", Color.blue) : Group(set, "Upper group");
                if (selectionKind == 2) Paint(set, "Upper child", Color.blue, upper);
                upper.layerSymmetry = new TexturePaintSymmetry { enabled = false, origin = Vector3.one };
                selected.Insert(0, upper); // Selection order must not choose the retained frame.
            }
            lower.layerSymmetry = new TexturePaintSymmetry
            {
                enabled = true, mirrorX = false, mirrorY = true, mirrorZ = true,
                origin = new Vector3(.2f, .7f, -.4f), euler = new Vector3(12f, 34f, 56f),
                radialCopies = 3, radialAxis = new Vector3(1f, 2f, 3f), legacyWorldMirrorOrder = true
            };
            lower.layerSymmetryVersion = version;
            string expectedFrame = JsonUtility.ToJson(lower.layerSymmetry);

            using var merged = set.CreateMergedLayers(selected, out string reason);
            Assert.That(merged, Is.Not.Null, reason);
            Assert.That(merged.id, Is.EqualTo(lower.id));
            Assert.That(merged.layerSymmetryVersion, Is.EqualTo(version));
            Assert.That(JsonUtility.ToJson(merged.layerSymmetry), Is.EqualTo(expectedFrame));
            Assert.That(merged.layerSymmetry, Is.Not.SameAs(lower.layerSymmetry));
            merged.layerSymmetry.origin = Vector3.zero;
            Assert.That(JsonUtility.ToJson(lower.layerSymmetry), Is.EqualTo(expectedFrame),
                "Editing the merged frame must not change the original retained for Undo.");
        }

        private static IEnumerable<TexturePaintChannel> Channels => Enum.GetValues(typeof(TexturePaintChannel)).Cast<TexturePaintChannel>();

        [TestCaseSource(nameof(Channels))]
        public void GroupToPaintPreservesNestedMasksOpacityAndAuthoredChannels(TexturePaintChannel channel)
        {
            var set = Set(channel);
            var outer = Group(set, "Outer"); outer.opacity = .7f;
            set.AddLayerMask(outer, .65f);
            var lower = Paint(set, "Lower", new Color(.8f, .2f, .4f, .8f), outer);
            var inner = Group(set, "Inner", outer); inner.opacity = .55f;
            var upper = Paint(set, "Plugin detail", new Color(.3f, .7f, .6f, .65f), inner, TexturePaintLayerKind.Plugin);
            upper.pluginId = "tests.cached-generator"; upper.pluginStale = false;
            upper.GetChannelSettings(channel).opacity = .6f;
            var mask = set.AddLayerMask(upper, 1f);
            mask.target.Reset(Image((x, y) => new Color(x / 63f, x / 63f, x / 63f, 1)), Color.white);
            var invert = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Invert);
            mask.effects.stack.Add(invert);
            upper.effects.colorOverlay.enabled = true;
            upper.effects.colorOverlay.channel = channel;
            upper.effects.colorOverlay.color = new Color(.4f, .3f, .9f, .25f);
            lower.blendMode = lower.GetChannelSettings(channel).blendMode = TexturePaintBlendMode.Multiply;
            var original = set.layers.ToArray();
            var before = Composite(set, channel);
            var merged = set.CreateMergedLayers(new[] { outer }, out string reason);
            Assert.That(merged, Is.Not.Null, reason);
            try
            {
                Assert.That(merged.kind, Is.EqualTo(TexturePaintLayerKind.Paint));
                Assert.That(merged.channels.ContainsKey(channel), Is.True);
                ReplaceSelection(set, new[] { outer }, merged);
                TexturePaintGpuTestFixture.AssertImage("Nested group merge " + channel, before, Composite(set, channel));
                // A second backdrop detects premultiplied-alpha mistakes and accidental inclusion of the base.
                set.GetChannel(channel).editable.Reset(null, new Color(.65f, .09f, .23f, .35f));
                var alternateAfter = Composite(set, channel);
                set.layers.Clear(); set.layers.AddRange(original);
                TexturePaintGpuTestFixture.AssertImage("Nested group merge alternate backdrop " + channel,
                    Composite(set, channel), alternateAfter);
            }
            finally { set.layers.Clear(); set.layers.AddRange(original); merged.Dispose(); }
        }

        [Test]
        public void MergeSelectedGroupsKeepsOuterParentAndDoesNotApplyAncestorOpacityTwice()
        {
            var set = Set();
            var parent = Group(set, "Parent"); parent.opacity = .37f; set.AddLayerMask(parent, .6f);
            var first = Group(set, "First", parent); first.opacity = .7f;
            Paint(set, "First detail", new Color(.9f, .2f, .1f, .6f), first);
            var second = Group(set, "Second", parent); second.opacity = .4f;
            Paint(set, "Second detail", new Color(.1f, .4f, .9f, .8f), second);
            var sibling = Paint(set, "Unaffected sibling", new Color(.6f, .7f, .2f, .15f), parent);
            var originals = set.layers.ToArray();
            var selected = new[] { second, first };
            var before = Composite(set);
            var merged = set.CreateMergedLayers(selected, out string reason);
            Assert.That(merged, Is.Not.Null, reason);
            try
            {
                Assert.That(merged.parentId, Is.EqualTo(parent.id));
                ReplaceSelection(set, selected, merged);
                Assert.That(set.layers.Contains(parent), Is.True);
                Assert.That(set.layers.Contains(sibling), Is.True);
                TexturePaintGpuTestFixture.AssertImage("Multiple sibling groups", before, Composite(set));
            }
            finally { set.layers.Clear(); set.layers.AddRange(originals); merged.Dispose(); }
        }

        [Test]
        public void SelectionIncludesWholeSubtreesAndDeduplicatesSelectedDescendants()
        {
            var set = Set();
            var first = Group(set, "First");
            var nested = Group(set, "Nested", first);
            var leaf = Paint(set, "Leaf", Color.red, nested);
            var second = Group(set, "Second");
            var other = Paint(set, "Other", Color.blue, second);
            Assert.That(set.TryGetMergeSelection(new[] { second, leaf, nested, first, first },
                out var roots, out var sources, out int insertion, out string reason), Is.True, reason);
            Assert.That(roots, Is.EqualTo(new[] { first, second }));
            Assert.That(sources, Is.EquivalentTo(new[] { first, nested, leaf, second, other }));
            Assert.That(sources.Count, Is.EqualTo(5));
            Assert.That(insertion, Is.Zero);
        }

        [Test]
        public void NoncontiguousOrDifferentParentSelectionsAreRejectedWithoutMutatingStack()
        {
            var set = Set();
            var first = Group(set, "First"); var child = Paint(set, "Child", Color.red, first);
            var middle = Paint(set, "Unselected middle", Color.green);
            var last = Group(set, "Last"); Paint(set, "Last child", Color.blue, last);
            var original = set.layers.ToArray();
            Assert.That(set.CreateMergedLayers(new[] { first, last }, out string gapReason), Is.Null);
            Assert.That(gapReason, Is.Not.Empty);
            Assert.That(set.CreateMergedLayers(new[] { child, middle }, out string parentReason), Is.Null);
            Assert.That(parentReason, Is.Not.Empty);
            Assert.That(set.layers, Is.EqualTo(original));
        }

        [Test]
        public void VisiblePluginWithoutGeneratedOutputIsRejected()
        {
            var set = Set(); Paint(set, "Below", Color.red);
            set.activeLayerIndex = -1;
            var plugin = set.AddPluginLayer("Not generated");
            Assert.That(set.CanMergeLayerDown(set.layers.IndexOf(plugin), out string reason), Is.False);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(set.layers, Has.Count.EqualTo(2));
        }

        [Test]
        public void GroupMergeUndoRestoresWholeTreeAndRedoReusesTheBakedPaint()
        {
            var set = Set(); var stage = Stage();
            var outside = Paint(set, "Outside", Color.black);
            var group = Group(set, "Construction");
            var nested = Group(set, "Nested", group);
            var plugin = Paint(set, "Plugin", new Color(.2f, .8f, .4f, .6f), nested, TexturePaintLayerKind.Plugin);
            plugin.pluginId = "tests.generator";
            var original = set.layers.ToArray();
            set.activeLayerIndex = set.layers.IndexOf(outside);
            var before = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.True);
            Assert.That(set.layers, Has.Count.EqualTo(2));
            var merged = set.layers.Single(layer => layer != outside);
            Assert.That(set.layers[set.activeLayerIndex], Is.SameAs(merged));
            TexturePaintGpuTestFixture.AssertImage("Undo merge pixels", before, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
            Assert.That(set.layers, Is.EqualTo(original));
            Assert.That(plugin.pluginId, Is.EqualTo("tests.generator"));
            Assert.That(plugin.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null);
            Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
            Assert.That(set.layers.Contains(merged), Is.True);
            TexturePaintGpuTestFixture.AssertImage("Redo merge pixels", before, Composite(set));
        }

        [Test]
        public void CtrlSelectionIsIndependentOfActiveLayerAndClearsAfterMerge()
        {
            var set = Set(); var stage = Stage();
            var active = Paint(set, "Currently painting", Color.black);
            var first = Group(set, "First"); Paint(set, "First child", Color.red, first);
            var second = Group(set, "Second"); Paint(set, "Second child", Color.green, second);
            set.activeLayerIndex = set.layers.IndexOf(active);
            Invoke(stage, "ToggleLayerMergeSelection", set, first);
            Invoke(stage, "ToggleLayerMergeSelection", set, second);
            Assert.That(set.layers[set.activeLayerIndex], Is.SameAs(active));
            Assert.That(Invoke(stage, "GetLayerMergeSelection", set), Is.EquivalentTo(new[] { first, second }));
            Assert.That(Invoke(stage, "IsLayerSelectedForMerge", set, active), Is.False);
            Invoke(stage, "ToggleLayerMergeSelection", set, first);
            Assert.That(Invoke(stage, "GetLayerMergeSelection", set), Is.EquivalentTo(new[] { second }));
            Invoke(stage, "ToggleLayerMergeSelection", set, first);
            Assert.That(Invoke(stage, "MergeSelectedLayers", set), Is.True);
            Assert.That(Invoke(stage, "GetLayerMergeSelection", set), Is.Empty);
            Assert.That(set.layers[set.activeLayerIndex].kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            Assert.That(set.layers[set.activeLayerIndex], Is.Not.SameAs(active));
        }

        [Test]
        public void MergeSelectionPrunesRemovedLayersAndCannotLeakToAnotherTarget()
        {
            var first = Set(); var second = Set(); var stage = Stage();
            var a = Paint(first, "A", Color.red); var b = Paint(first, "B", Color.green);
            var elsewhere = Paint(second, "Other target", Color.blue);
            Invoke(stage, "ToggleLayerMergeSelection", first, a);
            Invoke(stage, "ToggleLayerMergeSelection", first, b);
            first.layers.Remove(a);
            try { Assert.That(Invoke(stage, "GetLayerMergeSelection", first), Is.EquivalentTo(new[] { b })); }
            finally { first.layers.Insert(0, a); }
            Assert.That(Invoke(stage, "GetLayerMergeSelection", second), Is.Empty);
            Assert.That(Invoke(stage, "IsLayerSelectedForMerge", second, elsewhere), Is.False);
            Assert.That(Invoke(stage, "GetLayerMergeSelection", first), Is.Empty);
        }

        private TexturePaintStageWindow LogicalStage(TextureSet first, TextureSet second, out string targetId)
        {
            var reconstruction = new MeshReconstructionResult();
            reconstruction.logicalTargets.Rebuild(new[] { first.surface });
            var target = reconstruction.logicalTargets.Targets[0];
            target.isUdim = true;
            target.members[0].udimTileNumber = 1001;
            target.members[0].textureSets.Add(first);
            var member = new TexturePaintLogicalTargetMember { slotName = "Legs", udimTileNumber = 1002 };
            member.textureSets.Add(second); target.members.Add(member);
            var controller = new TexturePaintStageController();
            typeof(TexturePaintStageController).GetProperty("Reconstruction").SetValue(controller, reconstruction);
            typeof(TexturePaintStageController).GetProperty("LogicalLayers").SetValue(controller,
                new TexturePaintLogicalLayerController(reconstruction.logicalTargets));
            var stage = Stage();
            typeof(TexturePaintStageWindow).GetField("controller", Private).SetValue(stage, controller);
            targetId = target.id;
            return stage;
        }

        private static void Link(string targetId, string logicalId, params TexturePaintLayer[] peers)
        {
            foreach (var peer in peers) { peer.paintTargetId = targetId; peer.logicalLayerId = logicalId; }
        }

        [Test]
        public void MultipleGroupMergeAndUndoAreAtomicAcrossUdimTilesWithDifferentPixels()
        {
            var first = Set(); var second = Set();
            var stage = LogicalStage(first, second, out string targetId);
            var a = Group(first, "Lower group"); var aPeer = Group(second, "Lower group");
            Link(targetId, "lower-group", a, aPeer);
            var aChild = Paint(first, "Lower detail", new Color(.8f, .1f, .2f, .6f), a);
            var aChildPeer = Paint(second, "Lower detail", new Color(.2f, .8f, .1f, .4f), aPeer);
            Link(targetId, "lower-child", aChild, aChildPeer);
            var b = Group(first, "Upper group"); var bPeer = Group(second, "Upper group");
            Link(targetId, "upper-group", b, bPeer);
            var bChild = Paint(first, "Upper detail", new Color(.1f, .3f, .9f, .3f), b, TexturePaintLayerKind.Plugin);
            var bChildPeer = Paint(second, "Upper detail", new Color(.7f, .6f, .2f, .7f), bPeer, TexturePaintLayerKind.Plugin);
            Link(targetId, "upper-child", bChild, bChildPeer);
            first.activeLayerIndex = first.layers.IndexOf(aChild);
            second.activeLayerIndex = second.layers.IndexOf(bChildPeer);
            var originalFirst = first.layers.ToArray(); var originalSecond = second.layers.ToArray();
            var beforeFirst = Composite(first); var beforeSecond = Composite(second);

            Assert.That(Invoke(stage, "MergeLayersWithHistory", first, new[] { b, a }), Is.True);
            Assert.That(first.layers, Has.Count.EqualTo(1)); Assert.That(second.layers, Has.Count.EqualTo(1));
            var mergedFirst = first.layers[0]; var mergedSecond = second.layers[0];
            Assert.That(mergedFirst.logicalLayerId, Is.Not.Empty);
            Assert.That(mergedSecond.logicalLayerId, Is.EqualTo(mergedFirst.logicalLayerId));
            Assert.That(mergedSecond.paintTargetId, Is.EqualTo(targetId));
            TexturePaintGpuTestFixture.AssertImage("UDIM 1001 merged", beforeFirst, Composite(first));
            var afterSecond = Composite(second);
            // Baking inserts unassociation/reassociation between half-float render-target writes.
            // Two ULPs near 0.5 are 1/1024; bound both images against the analytic source-over value.
            var ideal = new Color(.5464f, .5736f, .2348f, .964f);
            foreach (var pixel in new[] { beforeSecond[0], afterSecond[0] })
                for (int component = 0; component < 4; component++)
                    Assert.That(pixel[component], Is.EqualTo(ideal[component]).Within(.002f));
            TexturePaintGpuTestFixture.AssertImage("UDIM 1002 merged", beforeSecond, afterSecond, .002f, .0015f);
            Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
            Assert.That(first.layers, Is.EqualTo(originalFirst)); Assert.That(second.layers, Is.EqualTo(originalSecond));
            Assert.That(first.layers[first.activeLayerIndex], Is.SameAs(aChild));
            Assert.That(second.layers[second.activeLayerIndex], Is.SameAs(bChildPeer));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "All tiles merge in a single history entry.");
            Assert.That(Invoke(stage, "RedoLightweight"), Is.True);
            Assert.That(first.layers[0], Is.SameAs(mergedFirst)); Assert.That(second.layers[0], Is.SameAs(mergedSecond));
            TexturePaintGpuTestFixture.AssertImage("UDIM 1002 redo", beforeSecond, Composite(second), .002f, .0015f);
        }

        [Test]
        public void InvalidPeerSelectionRejectsEveryTileBeforeChangingAnyLayer()
        {
            var first = Set(); var second = Set();
            var stage = LogicalStage(first, second, out string targetId);
            var a = Paint(first, "A", Color.red); var aPeer = Paint(second, "A", Color.blue);
            var b = Paint(first, "B", Color.green);
            Paint(second, "Unselected tile-local layer", Color.white);
            var bPeer = Paint(second, "B", Color.yellow);
            Link(targetId, "a", a, aPeer); Link(targetId, "b", b, bPeer);
            var originalFirst = first.layers.ToArray(); var originalSecond = second.layers.ToArray();
            Assert.That(Invoke(stage, "MergeLayersWithHistory", first, new[] { a, b }), Is.False);
            Assert.That(first.layers, Is.EqualTo(originalFirst)); Assert.That(second.layers, Is.EqualTo(originalSecond));
            Assert.That(a.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null);
            Assert.That(aPeer.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null);
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
        }

        [Test]
        public void SingleGroupRetainsBackdropBlendAndDisabledChildrenRemainAbsent()
        {
            var set = Set(); var stage = Stage();
            set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(null, new Color(.18f, .32f, .46f, 1));
            var group = Group(set, "Multiply group"); group.blendMode = TexturePaintBlendMode.Multiply;
            group.opacity = .6f; set.AddLayerMask(group, .5f);
            Paint(set, "Visible", new Color(.6f, .25f, .8f, .7f), group);
            var hidden = Paint(set, "Hidden", Color.white, group); hidden.visible = false;
            var disabled = Paint(set, "Disabled channel", Color.green, group);
            disabled.GetChannelSettings(TexturePaintChannel.Albedo).enabled = false;
            var before = Composite(set);
            Assert.That(Invoke(stage, "MergeGroupToPaintLayer", set, group), Is.True);
            Assert.That(set.layers, Has.Count.EqualTo(1));
            Assert.That(set.layers[0].blendMode, Is.EqualTo(TexturePaintBlendMode.Multiply));
            TexturePaintGpuTestFixture.AssertImage("Group blend and disabled content", before, Composite(set));
        }

        [Test]
        public void NonNormalGroupOverTransparentBackdropRejectsWithoutChangingPixels()
        {
            var set = Set(); var stage = Stage();
            set.GetChannel(TexturePaintChannel.Albedo).editable.Reset(null, Color.clear);
            var group = Group(set, "Multiply group"); group.blendMode = TexturePaintBlendMode.Multiply;
            Paint(set, "Visible", new Color(.6f, .25f, .8f, .7f), group);
            var original = set.layers.ToArray(); var before = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.False);
            Assert.That(set.layers, Is.EqualTo(original));
            TexturePaintGpuTestFixture.AssertImage("Rejected transparent backdrop", before, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GroupMergeBakesLayerHeightSamplingOnce(bool invert)
        {
            var channel = TexturePaintChannel.NormalControl;
            var set = Set(channel);
            set.GetChannel(channel).editable.Reset(null, new Color(.5f, .5f, .5f, 1));
            var stage = Stage();
            var group = Group(set, "Sampled height group");
            group.opacity = .7f;
            var layer = Paint(set, "Masked height", Color.clear, group);
            layer.channels[channel].Reset(Image((x, y) =>
                x >= 20 && x < 40 && y >= 12 && y < 48 ? new Color(.8f, .8f, .8f, .6f) : Color.clear), Color.clear);
            var mask = set.AddLayerMask(layer, 1);
            mask.target.Reset(Image((x, y) => y < 32 ? Color.white : Color.black), Color.black);
            var settings = layer.GetChannelSettings(channel);
            settings.hasNormalControlStrength = true;
            settings.normalControlStrength = 8;
            settings.normalControlInvert = invert;
            settings.normalControlRadius = 5;
            var before = Composite(set, channel);
            Assert.That(Invoke(stage, "MergeGroupToPaintLayer", set, group), Is.True);
            Assert.That(set.layers.Single().GetChannelSettings(channel).normalControlRadius, Is.EqualTo(1),
                "Merged pixels already contain the sampling footprint.");
            TexturePaintGpuTestFixture.AssertImage("Merged sampled height", before, Composite(set, channel));
        }

        [Test]
        public void MergingZeroStrengthHeightLayersCannotEnableNormalExport()
        {
            var set = Set(TexturePaintChannel.NormalControl);
            set.GetChannel(TexturePaintChannel.NormalControl).editable.Reset(null, new Color(.5f, .5f, .5f, 1));
            var group = Group(set, "Disabled height");
            var first = Paint(set, "First height", new Color(.8f, .8f, .8f, .5f), group);
            var second = Paint(set, "Second height", new Color(.2f, .2f, .2f, .5f), group);
            foreach (var layer in new[] { first, second })
            {
                var settings = layer.GetChannelSettings(TexturePaintChannel.NormalControl);
                settings.hasNormalControlStrength = true; settings.normalControlStrength = 0;
            }
            Assert.That(set.HasEnabledNormalControlStrength(), Is.False);
            var originals = set.layers.ToArray();
            var before = Composite(set, TexturePaintChannel.NormalControl);
            Assert.That(set.CreateMergedLayers(new[] { group }, out string reason), Is.Null);
            Assert.That(reason, Does.Contain("Normal Control"));
            Assert.That(set.layers, Is.EqualTo(originals));
            Assert.That(set.HasEnabledNormalControlStrength(), Is.False);
            TexturePaintGpuTestFixture.AssertImage("Zero height merge rejection", before,
                Composite(set, TexturePaintChannel.NormalControl));
        }

        [Test]
        public void GroupMaskPixelEffectsPreserveEachChannelsOwnResolution()
        {
            var set = Set(TexturePaintChannel.Albedo, TexturePaintChannel.Roughness);
            set.channels[TexturePaintChannel.Roughness].Dispose();
            set.channels[TexturePaintChannel.Roughness] = new TextureChannelTarget
            {
                channel = TexturePaintChannel.Roughness, materialProperty = "_Roughness", format = RenderTextureFormat.ARGBHalf,
                editable = new EditableTextureTarget("Half resolution backdrop", 32, 32, RenderTextureFormat.ARGBHalf, null, Color.white),
                composite = EditableTextureTarget.Create("Half resolution composite", 32, 32, RenderTextureFormat.ARGBHalf)
            };
            var stage = Stage(); var group = Group(set, "Mixed resolution group");
            Paint(set, "Detail", new Color(.2f, .4f, .7f, .8f), group);
            var mask = set.AddLayerMask(group, 1);
            mask.target.Reset(Image((x, y) => x >= 30 && x < 40 && y >= 18 && y < 46 ? Color.white : Color.black), Color.black);
            var dilate = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Dilate); dilate.radius = 4;
            var blur = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Blur); blur.radius = 2;
            mask.effects.stack.Add(dilate); mask.effects.stack.Add(blur);
            var colorBefore = Composite(set, TexturePaintChannel.Albedo);
            var roughnessBefore = Composite(set, TexturePaintChannel.Roughness);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.True);
            TexturePaintGpuTestFixture.AssertImage("Full resolution mask merge", colorBefore, Composite(set));
            var roughnessAfter = Composite(set, TexturePaintChannel.Roughness);
            Assert.That(roughnessAfter, Has.Length.EqualTo(roughnessBefore.Length));
            float maximumError = 0;
            for (int i = 0; i < roughnessBefore.Length; i++)
            {
                var delta = roughnessAfter[i] - roughnessBefore[i];
                maximumError = Mathf.Max(maximumError,
                    Mathf.Max(Mathf.Abs(delta.r), Mathf.Abs(delta.g), Mathf.Abs(delta.b), Mathf.Abs(delta.a)));
            }
            Assert.That(maximumError, Is.LessThan(.004f),
                "Dilation and blur must still evaluate at the 32px channel's pixel scale after merging.");
            Assert.That(set.layers[0].channels[TexturePaintChannel.Roughness].Width, Is.EqualTo(32));
        }

        private static TextureStore StageStore(TexturePaintStageWindow stage) =>
            ((TexturePaintStageController)typeof(TexturePaintStageWindow).GetField("controller", Private).GetValue(stage)).Textures;

        private static TexturePaintLayer AddDependent(TextureSet set, TexturePaintLayer source, int referenceKind)
        {
            var dependent = Paint(set, "Dependent", new Color(.7f, .2f, .8f, .6f));
            var reference = TexturePaintLayerReference.To(set, source);
            dependent.links = new TexturePaintLayerLinks();
            if (referenceKind == 0)
            {
                dependent.kind = TexturePaintLayerKind.Reference;
                reference.component = TexturePaintReferenceComponent.Color;
                dependent.links.content = reference;
            }
            else
            {
                reference.component = TexturePaintReferenceComponent.Mask;
                if (referenceKind == 1) dependent.links.mask = reference;
                else
                {
                    var mask = set.AddLayerMask(dependent, 1f);
                    var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.LayerReference);
                    effect.reference = reference; mask.effects.stack.Add(effect);
                }
            }
            return dependent;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ExternalGroupContentAndMaskReferencesStillResolveAfterMerge(int referenceKind)
        {
            var set = Set(); var stage = Stage(set);
            var group = Group(set, "Referenced group"); group.opacity = .65f;
            Paint(set, "First", new Color(.2f, .7f, .4f, .6f), group);
            var nested = Group(set, "Nested", group); nested.opacity = .7f;
            Paint(set, "Second", new Color(.8f, .2f, .5f, .4f), nested);
            var groupMask = set.AddLayerMask(group, 1);
            groupMask.target.Reset(Image((x, y) => new Color(x / 63f, x / 63f, x / 63f, 1)), Color.white);
            groupMask.effects.stack.Add(TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.Invert));
            var dependent = AddDependent(set, group, referenceKind);
            var store = StageStore(stage); store.RefreshLayerLinks();
            Assert.That(dependent.linkError, Is.Null.Or.Empty);
            var before = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.True);
            store.RefreshLayerLinks();
            Assert.That(dependent.linkError, Is.Null.Or.Empty);
            Assert.That(set.layers.Single(layer => layer.id == group.id).kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            TexturePaintGpuTestFixture.AssertImage("Retained group reference " + referenceKind, before, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
            store.RefreshLayerLinks();
            Assert.That(dependent.linkError, Is.Null.Or.Empty);
            TexturePaintGpuTestFixture.AssertImage("Undone group reference " + referenceKind, before, Composite(set));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ExternalChildReferencesPreventDeletingTheirSourceDuringGroupMerge(int referenceKind)
        {
            var set = Set(); var stage = Stage(set);
            var group = Group(set, "Construction");
            var child = Paint(set, "Referenced child", new Color(.2f, .6f, .3f, .7f), group);
            set.AddLayerMask(child, .4f);
            var dependent = AddDependent(set, child, referenceKind);
            var store = StageStore(stage); store.RefreshLayerLinks();
            Assert.That(dependent.linkError, Is.Null.Or.Empty);
            var original = set.layers.ToArray(); var before = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.False);
            Assert.That(set.layers, Is.EqualTo(original));
            Assert.That(child.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null);
            TexturePaintGpuTestFixture.AssertImage("Rejected child reference " + referenceKind, before, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
        }

        private static int HistoryCapacity => (int)typeof(TexturePaintStageWindow)
            .GetField("LightweightHistoryCapacity", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();

        private static void PushNoOpEdits(TexturePaintStageWindow stage, int count)
        {
            for (int i = 0; i < count; i++)
                Invoke(stage, "PushLightweightCommand", "Later edit " + i, (Action)(() => { }), (Action)(() => { }), null, null);
        }

        private static void UndoLaterEdits(TexturePaintStageWindow stage, int count)
        {
            for (int i = 0; i < count; i++) Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
        }

        [Test]
        public void ExpiringCreationHistoryKeepsSourcePixelsNeededByMergeUndo()
        {
            var set = Set(); var stage = Stage(set);
            var lower = Paint(set, "Created layer", new Color(.9f, .2f, .1f, .6f));
            Invoke(stage, "RegisterCreatedLayer", lower, "Create layer");
            var upper = Paint(set, "Upper", new Color(.1f, .3f, .8f, .4f));
            var originalPixels = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { lower, upper }), Is.True);
            PushNoOpEdits(stage, HistoryCapacity - 1);
            Assert.That(lower.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null,
                "Expiring the creation entry must not destroy a source retained by merge Undo.");
            UndoLaterEdits(stage, HistoryCapacity - 1);
            Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
            Assert.That(set.layers, Is.EqualTo(new[] { lower, upper }));
            TexturePaintGpuTestFixture.AssertImage("Creation expiry merge undo", originalPixels, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False, "The creation entry has left the history capacity.");
        }

        [Test]
        public void ExpiringEarlierMergeKeepsIntermediatePaintNeededByLaterMergeUndo()
        {
            var set = Set(); var stage = Stage();
            var lower = Paint(set, "Lower", new Color(.9f, .2f, .1f, .6f));
            var upper = Paint(set, "Upper", new Color(.1f, .3f, .8f, .4f));
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { lower, upper }), Is.True);
            var intermediate = set.layers[0];
            var last = Paint(set, "Last", new Color(.3f, .8f, .2f, .35f));
            var beforeLastMerge = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { intermediate, last }), Is.True);
            PushNoOpEdits(stage, HistoryCapacity - 1);
            Assert.That(intermediate.channels[TexturePaintChannel.Albedo].Front, Is.Not.Null,
                "A newer merge still owns the earlier merge's paint snapshot.");
            UndoLaterEdits(stage, HistoryCapacity - 1);
            Assert.That(Invoke(stage, "UndoLightweight"), Is.True);
            Assert.That(set.layers, Is.EqualTo(new[] { intermediate, last }));
            TexturePaintGpuTestFixture.AssertImage("Earlier merge expiry undo", beforeLastMerge, Composite(set));
            Assert.That(Invoke(stage, "UndoLightweight"), Is.False);
        }

        private static void UseByteAlbedo(TextureSet set)
        {
            set.channels[TexturePaintChannel.Albedo].Dispose();
            set.channels[TexturePaintChannel.Albedo] = new TextureChannelTarget
            {
                channel = TexturePaintChannel.Albedo, materialProperty = "_Albedo", format = RenderTextureFormat.ARGB32,
                editable = new EditableTextureTarget("Byte base", Size, Size, RenderTextureFormat.ARGB32, null, Color.black),
                composite = EditableTextureTarget.Create("Byte composite", Size, Size, RenderTextureFormat.ARGB32)
            };
        }

        [Test]
        public void MergedLowAlphaEffectsKeepHdrStraightPixelsThroughDuplicateAndDocumentRestore()
        {
            var set = Set(); UseByteAlbedo(set); var stage = Stage(set);
            var group = Group(set, "Low coverage effect");
            var child = Paint(set, "Translucent detail", new Color(1f, .1f, .2f, .1f), group);
            child.effects.colorOverlay.enabled = true;
            child.effects.colorOverlay.channel = TexturePaintChannel.Albedo;
            child.effects.colorOverlay.color = Color.red;
            var before = Composite(set);
            Assert.That(Invoke(stage, "MergeLayersWithHistory", set, new[] { group }), Is.True);
            var merged = set.layers[0];
            var mergedPixels = TexturePaintGpuTestFixture.ReadPixels(merged.channels[TexturePaintChannel.Albedo].Front);
            Assert.That(mergedPixels[0].r, Is.GreaterThan(1.1f),
                "Low-alpha clipped effects need straight RGB above one to preserve their visible contribution.");
            TexturePaintGpuTestFixture.AssertImage("Byte group floating merge", before, Composite(set), .005f, .005f);
            using (var duplicate = set.CloneLayer(merged, "Duplicate"))
                TexturePaintGpuTestFixture.AssertImage("HDR merge duplicate", mergedPixels,
                    TexturePaintGpuTestFixture.ReadPixels(duplicate.channels[TexturePaintChannel.Albedo].Front));

            var document = TexturePaintDocumentStorage.CreateTransient(null); objects.Add(document);
            TexturePaintDocumentStorage.Save(document, StageStore(stage));
            var reopened = TexturePaintDocumentStorage.CreateTransient(null); objects.Add(reopened);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(document), reopened);
            var restored = Set(); UseByteAlbedo(restored);
            var restoredStage = Stage(restored);
            TexturePaintDocumentStorage.Restore(reopened, StageStore(restoredStage));
            Assert.That(restored.layers, Has.Count.EqualTo(1));
            var restoredPixels = TexturePaintGpuTestFixture.ReadPixels(restored.layers[0].channels[TexturePaintChannel.Albedo].Front);
            Assert.That(restoredPixels[0].r, Is.GreaterThan(1.1f));
            TexturePaintGpuTestFixture.AssertImage("HDR merge document pixels", mergedPixels, restoredPixels);
            TexturePaintGpuTestFixture.AssertImage("HDR merge document composite", before, Composite(restored), .005f, .005f);
        }

        [Test]
        public async Task ExecutedFilterOutputMergesWithoutRunningTheFilterAgain()
        {
            var set = Set();
            Paint(set, "Input", new Color(.85f, .12f, .25f, 1));
            set.activeLayerIndex = -1;
            var pluginLayer = set.AddPluginLayer("Filter output");
            using var store = new TextureStore();
            var members = (List<TextureSet>)typeof(TextureStore).GetField("sets", Private).GetValue(store);
            members.Add(set);
            using var host = new PluginHost();
            var filter = new FilterProbe();
            try
            {
                await host.ExecutePluginLayerAsync(filter, store, new TexturePaintPluginParameterSet(),
                    new Dictionary<TextureSet, TexturePaintLayer> { { set, pluginLayer } }, null, CancellationToken.None);
                var generated = set.layers[set.layers.Count - 1];
                Assert.That(generated.kind, Is.EqualTo(TexturePaintLayerKind.Plugin));
                Assert.That(generated.channels.ContainsKey(TexturePaintChannel.Albedo), Is.True);
                var before = Composite(set);
                Assert.That(before[0].g, Is.GreaterThan(.1f), "The test filter must have produced visible pixels.");
                Assert.That(set.MergeLayerDown(set.layers.Count - 1), Is.True);
                TexturePaintGpuTestFixture.AssertImage("Executed filter merged", before, Composite(set));
                Assert.That(filter.calls, Is.EqualTo(1));
                Assert.That(set.layers[0].kind, Is.EqualTo(TexturePaintLayerKind.Paint));
            }
            finally { members.Clear(); }
        }

        private sealed class FilterProbe : ITexturePaintFilterV2
        {
            public int calls;
            public TexturePaintPluginDescriptor Descriptor { get; } = new()
            {
                id = "org.uma.tests.merge-filter", displayName = "Merge filter probe",
                capabilities = TexturePaintPluginCapability.Filter,
                declaredChannels = TexturePaintChannelMask.Albedo, readChannels = TexturePaintChannelMask.Albedo
            };

            public Task ExecuteAsync(TexturePaintCommandContextV2 context)
            {
                calls++;
                foreach (string id in context.source.surfaceIds)
                {
                    var source = context.source.Get(id, TexturePaintChannel.Albedo);
                    if (source == null) continue;
                    var pixels = source.CopyPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color(pixels[i].b, 1f - pixels[i].g, pixels[i].r, .65f);
                    context.WriteTile(id, TexturePaintChannel.Albedo,
                        new RectInt(0, 0, source.width, source.height), pixels,
                        TexturePaintPluginColorSpace.Linear, TexturePaintPluginBlend.Replace);
                }
                return Task.CompletedTask;
            }
        }
    }
}
#endif
