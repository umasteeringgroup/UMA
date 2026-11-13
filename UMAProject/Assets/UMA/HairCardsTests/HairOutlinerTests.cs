#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        // Structure fingerprints (kind, key, parent, workflow step, depth; not labels) of the
        // packaged examples, captured before the outliner rewrite. Labels and presentation
        // may change; the node structure that selection, preferences and tests rely on may not.
        [TestCase("BraidedBun/BraidedBun_Compact_HairGroom", 113, 14248306298942287071UL)]
        [TestCase("BraidedBun/BraidedBun_HairGroom", 121, 6031617313213036232UL)]
        [TestCase("BraidedBun/BraidedBun_Loose_HairGroom", 133, 16725271850628548742UL)]
        [TestCase("CurlyVolume/Curly_HairGroom", 22, 11676097205791300213UL)]
        [TestCase("PointySwept/Pointy_HairGroom", 22, 7829614558635569929UL)]
        [TestCase("ShortHairPart/ShortHairPart_CloseCut_HairGroom", 44, 16360915937679685245UL)]
        [TestCase("ShortHairPart/ShortHairPart_FrontFlipNatural_HairGroom", 50, 9234313481342135867UL)]
        [TestCase("ShortHairPart/ShortHairPart_HairGroom", 42, 13350817328813530028UL)]
        [TestCase("ShortHairPart/ShortHairPart_Natural_HairGroom", 47, 14446673546268209898UL)]
        [TestCase("ShortHairPart/ShortHairPart_Relaxed_HairGroom", 42, 6116753288102214303UL)]
        public void OutlinerNodeStructureOfPackagedExamplesIsUnchanged(string name, int count, ulong fingerprint)
        {
            string path = "Assets/UMAProjectData/HairCards/Examples/" + name + ".asset";
            if (!File.Exists(path)) return; // Optional sample, but mandatory checks when installed.
            var example = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(path);
            Assert.That(example, Is.Not.Null);
            var nodes = HairGroomNodes.Build(example);
            string listing = StructureListing(nodes);
            Assert.That(nodes.Count, Is.EqualTo(count), listing);
            Assert.That(Fingerprint(listing), Is.EqualTo(fingerprint), listing);
        }

        [Test]
        public void OutlinerNodesHaveExistingEarlierParentsAndUniqueKeys()
        {
            BuildEveryNodeKind();
            var nodes = HairGroomNodes.Build(groom);
            var seen = new HashSet<string>();
            foreach (var node in nodes)
            {
                Assert.That(seen.Add(node.Key), Is.True, "Duplicate key " + node.Key);
                if (node.ParentKey == null) { Assert.That(node.Depth, Is.Zero); continue; }
                Assert.That(seen.Contains(node.ParentKey), Is.True, node.Key + " must follow its parent " + node.ParentKey);
            }
            foreach (HairGroomNodeKind kind in Enum.GetValues(typeof(HairGroomNodeKind)))
                Assert.That(nodes.Any(node => node.Kind == kind), Is.True, "Fixture should exercise " + kind);
            Undo.ClearUndo(groom);
        }

        [Test]
        public void SelectingANodeSelectsItsWorkflowStepAndEachStepHasADefaultNode()
        {
            BuildEveryNodeKind();
            HairCardStage stage = CreateEditingStage();
            try
            {
                foreach (var node in stage.Nodes.ToArray())
                {
                    stage.SelectNode(node.Key);
                    var selected = stage.SelectedNode;
                    Assert.That(selected, Is.Not.Null, node.Key);
                    Assert.That(stage.WorkflowStep, Is.EqualTo(selected.Step), node.Key);
                    // Linked growth nodes deliberately redirect to the owning group's map.
                    if (node.Kind != HairGroomNodeKind.GrowthMap) Assert.That(selected.Key, Is.EqualTo(node.Key));
                }
                foreach (HairWorkflowStep step in Enum.GetValues(typeof(HairWorkflowStep)))
                {
                    stage.WorkflowStep = step;
                    Assert.That(stage.SelectedNode, Is.Not.Null, step.ToString());
                    Assert.That(stage.SelectedNode.Step, Is.EqualTo(step));
                }
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        [Test]
        public void OutlinerMenusBuildForEveryNodeKindWithoutChangingTheGroom()
        {
            BuildEveryNodeKind();
            HairCardStage stage = CreateEditingStage();
            try
            {
                string before = EditorJsonUtility.ToJson(groom);
                foreach (var node in stage.Nodes.ToArray())
                {
                    stage.SelectNode(node.Key);
                    var selected = stage.FindNode(node.Key) ?? stage.SelectedNode;
                    var menu = HairGroomNodeCommands.BuildNodeMenu(stage, selected);
                    var add = HairGroomNodeCommands.BuildAddMenu(stage, selected);
                    Assert.That(add.GetItemCount(), Is.GreaterThan(0), "Add menu for " + node.Kind);
                    if (selected.CanReorder || selected.CanToggle || HairGroomNodeHandler.For(selected.Kind).CanLock(selected))
                        Assert.That(menu.GetItemCount(), Is.GreaterThan(0), "Context menu for " + node.Kind);
                }
                Assert.That(HairGroomNodeCommands.BuildNodeMenu(stage, null).GetItemCount(), Is.GreaterThan(0), "Empty-space menu offers groom-level creation.");
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before), "Building menus must not edit the groom.");
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        [Test]
        public void OutlinerStructureSignatureIgnoresPaintButTracksRowsNamesAndState()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            int signature = HairGroomNodes.StructureSignature(HairGroomNodes.Build(groom));
            var map = group.FindMap(HairMapKind.GrowthArea);
            if (map.values.Length > 0) map.values[0] = map.values[0] > 0.5f ? 0f : 1f;
            Assert.That(HairGroomNodes.StructureSignature(HairGroomNodes.Build(groom)), Is.EqualTo(signature), "Painting must not rebuild the outliner.");
            layer.name = "Renamed";
            int renamed = HairGroomNodes.StructureSignature(HairGroomNodes.Build(groom));
            Assert.That(renamed, Is.Not.EqualTo(signature));
            layer.visible = false;
            Assert.That(HairGroomNodes.StructureSignature(HairGroomNodes.Build(groom)), Is.Not.EqualTo(renamed));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void OutlinerLockColumnLocksOnlyGroupsPassesAndHelpersWithUndo()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairHelper helper = HairGroomCommands.AddHelper(groom, HairHelperType.Sphere, Vector3.zero);
            HairCardStage stage = CreateEditingStage();
            try
            {
                var pass = stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id));
                Undo.IncrementCurrentGroup();
                Assert.That(HairGroomNodes.SetLocked(stage, pass, true), Is.True);
                Assert.That(layer.locked, Is.True);
                Assert.That(stage.FindNode(pass.Key).Locked, Is.True);
                Undo.PerformUndo();
                Assert.That(layer.locked, Is.False, "Locking is undoable.");
                var helperNode = stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
                Assert.That(HairGroomNodes.SetLocked(stage, helperNode, true), Is.True);
                Assert.That(helper.locked, Is.True);
                var guides = stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Guides, group.Id));
                Assert.That(HairGroomNodes.SetLocked(stage, guides, true), Is.False, "Only groups, passes and helpers own a lock.");
                var groupNode = stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Group, group.Id));
                Assert.That(HairGroomNodes.SetLocked(stage, groupNode, true), Is.True);
                pass = stage.FindNode(pass.Key);
                Assert.That(HairGroomNodeHandler.For(pass.Kind).CanLock(pass), Is.False, "A locked group's passes cannot be unlocked individually.");
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        [Test]
        public void OutlinerReferencesShowHelperAndMaskUsage()
        {
            HairGroup group = groom.Groups[0];
            HairHelper helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.zero);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Smooth, layer);
            HairGrowthMap length = HairGroomCommands.EnsureMap(groom, group, HairMapKind.Length);
            modifier.maskMapId = length.Id;
            var nodes = HairGroomNodes.Build(groom);
            var badges = new List<string>();
            HairGroomNodeReferences.AppendBadges(groom, nodes.Single(n => n.Kind == HairGroomNodeKind.Helper), badges);
            Assert.That(badges, Does.Contain("used by 1"));
            badges.Clear();
            HairGroomNodeReferences.AppendBadges(groom, nodes.Single(n => n.Kind == HairGroomNodeKind.Constraint), badges);
            Assert.That(badges.Any(b => b.StartsWith("helper: ")), Is.True);
            badges.Clear();
            HairGroomNodeReferences.AppendBadges(groom, nodes.Single(n => n.Kind == HairGroomNodeKind.Modifier), badges);
            Assert.That(badges.Any(b => b.StartsWith("mask: ")), Is.True);
            var usages = new List<HairGroomNodeReferences.Usage>();
            HairGroomNodeReferences.CollectMapUsages(groom, group, length, usages);
            Assert.That(usages.Select(u => u.NodeKey), Does.Contain(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id)));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void OutlinerDropsOnlyReorderCompatibleSiblings()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer first = HairGroomCommands.AddSculptLayer(groom, group);
            HairSculptLayer second = HairGroomCommands.AddSculptLayer(groom, group);
            HairSculptLayer finishing = HairGroomCommands.AddSculptLayer(groom, group, "Finishing", true);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Smooth, first);
            var nodes = HairGroomNodes.Build(groom);
            HairGroomNode Find(HairGroomNodeKind kind, string id) => nodes.Single(n => n.Key == HairGroomNodes.Key(kind, id));
            Assert.That(HairGroomNodes.CanDrop(Find(HairGroomNodeKind.Layer, second.Id), Find(HairGroomNodeKind.Layer, first.Id)), Is.True);
            Assert.That(HairGroomNodes.CanDrop(Find(HairGroomNodeKind.Layer, second.Id), Find(HairGroomNodeKind.Layer, finishing.Id)), Is.False,
                "Passes cannot cross the constraint boundary.");
            Assert.That(HairGroomNodes.CanDrop(Find(HairGroomNodeKind.Modifier, modifier.Id), Find(HairGroomNodeKind.Layer, first.Id)), Is.False);
            Assert.That(HairGroomNodes.CanDrop(Find(HairGroomNodeKind.Guides, group.Id), Find(HairGroomNodeKind.Groom, group.Id)), Is.False);
            Undo.ClearUndo(groom);
        }

        [UnityTest]
        public System.Collections.IEnumerator OutlinerWindowShowsTheGroomAndFollowsStageSelection()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairCardStage stage = CreateEditingStage();
            var window = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage);
                window.Show();
                window.position = new Rect(20, 40, 240, 600);
                for (int frame = 0; frame < 4; frame++) yield return null;
                var tree = window.rootVisualElement.Q<UnityEngine.UIElements.MultiColumnTreeView>();
                Assert.That(tree, Is.Not.Null);
                var visible = HairGroomNodeWindow.Filter(stage.Nodes, "", new List<string>(), new List<string>());
                Assert.That(tree.viewController.GetItemsCount(), Is.EqualTo(visible.Count));
                string key = HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id);
                stage.SelectNode(key);
                for (int frame = 0; frame < 6 && (tree.selectedIndex < 0 || tree.GetItemDataForIndex<string>(tree.selectedIndex) != key); frame++)
                { window.Repaint(); yield return new WaitForSecondsRealtimeOrFrames(0.12f); }
                Assert.That(tree.selectedIndex, Is.GreaterThanOrEqualTo(0));
                Assert.That(tree.GetItemDataForIndex<string>(tree.selectedIndex), Is.EqualTo(key), "The outliner follows the stage's selection.");
                LogAssert.NoUnexpectedReceived();
            }
            finally { window.Close(); active.SetValue(null, previous); Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        // Editor tests run without a player loop guarantee; wait on real time, bounded by frames.
        private sealed class WaitForSecondsRealtimeOrFrames : CustomYieldInstruction
        {
            private readonly double until; private int frames;
            internal WaitForSecondsRealtimeOrFrames(float seconds) { until = EditorApplication.timeSinceStartup + seconds; }
            public override bool keepWaiting => EditorApplication.timeSinceStartup < until && ++frames < 120;
        }

        private void BuildEveryNodeKind()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Smooth, layer);
            HairGroomCommands.AddSculptLayer(groom, group, "Finishing", true);
            HairHelper helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.zero);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            var legacy = new HairModifierSettings { type = HairModifierType.Length };
            legacy.EnsureIntegrity(); group.modifiers.Add(legacy);
            HairGroomCommands.EnsureMap(groom, group, HairMapKind.Density);
            var population = HairGenerationEditor.AddPopulation(groom, group);
            HairGenerationEditor.AddModifier(groom, group, population, HairModifierType.Noise);
        }

        private static string StructureListing(IEnumerable<HairGroomNode> nodes)
        {
            var text = new System.Text.StringBuilder();
            foreach (var node in nodes)
                text.Append(node.Kind).Append('|').Append(node.Key).Append('|').Append(node.ParentKey).Append('|')
                    .Append(node.Step).Append('|').Append(node.Depth).Append('\n');
            return text.ToString();
        }

        private static ulong Fingerprint(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text) unchecked { hash = (hash ^ c) * 1099511628211UL; }
            return hash;
        }
    }
}
#endif
