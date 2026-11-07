#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test, Category("SourceImport")]
        public void HairCardScriptsAreImportedFromSourceIntoTheirExpectedAssemblies()
        {
            const string sourceRoot = "Assets/UMA/HairCards";
            Assert.That(System.IO.Directory.Exists(sourceRoot + "/Core"), Is.True,
                "Source-import validation requires actual Hair Cards sources. A prebuilt-DLL test project cannot validate Unity imports.");
            var assemblies = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor);
            foreach (string folder in new[] { "Core", "Runtime", "Editor" })
            {
                foreach (string file in System.IO.Directory.GetFiles(sourceRoot + "/" + folder, "*.cs", System.IO.SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    string expected = path.Contains("/Editor/Tests/") ? "UMA.HairCards.Editor.Tests" : "UMA.HairCards." + folder;
                    var assembly = assemblies.SingleOrDefault(item => item.name == expected);
                    Assert.That(assembly, Is.Not.Null, "Missing source assembly: " + expected);
                    Assert.That(assembly.sourceFiles.Any(item => string.Equals(System.IO.Path.GetFullPath(item),
                        System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)), Is.True,
                        path + " was omitted from Unity's compilation inputs (check its .meta GUID and assembly definition).");
                    Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(path), Is.Not.Null, "Unity did not import " + path);
                }
            }
            var windowScript = AssetDatabase.LoadAssetAtPath<MonoScript>(sourceRoot + "/Editor/HairGroomNodeWindow.cs");
            Assert.That(windowScript.GetClass(), Is.EqualTo(typeof(HairGroomNodeWindow)));
        }

        [Test]
        public void NodeModelIsReadOnlyAndRepresentsEveryAuthoredOperationWithStableKeys()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType))) HairGroomCommands.AddModifier(groom, group, type, layer);
            HairHelper helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.zero);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            string before = EditorJsonUtility.ToJson(groom);
            var nodes = HairGroomNodes.Build(groom);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            Assert.That(nodes.Select(item => item.Key).Distinct().Count(), Is.EqualTo(nodes.Count));
            Assert.That(nodes.Count(item => item.Kind == HairGroomNodeKind.Modifier), Is.EqualTo(layer.modifiers.Count));
            Assert.That(nodes.Count(item => item.Kind == HairGroomNodeKind.GrowthMap), Is.EqualTo(group.maps.Count));
            Assert.That(nodes.Single(item => item.Kind == HairGroomNodeKind.Constraint).Constraint, Is.SameAs(group.constraints[0]));
            Assert.That(nodes.Single(item => item.Kind == HairGroomNodeKind.Helper).Helper, Is.SameAs(helper));
            var keys = nodes.Select(item => item.Key).ToArray();
            layer.name = "Renamed pass"; group.name = "Renamed group"; layer.modifiers.Reverse();
            CollectionAssert.AreEquivalent(keys, HairGroomNodes.Build(groom).Select(item => item.Key));
            foreach (var node in nodes.Where(item => item.ParentKey != null))
                Assert.That(nodes.Single(item => item.Key == node.ParentKey).Depth, Is.EqualTo(node.Depth - 1));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void PropertiesWindowHasNoSecondNavigatorOrNodeManagementUi()
        {
            var methods = typeof(HairGroomWorkspace).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Select(method => method.Name).ToArray();
            foreach (string removed in new[] { "DrawNodeLink", "DrawStep", "DrawHeader", "DrawGrowthMapPicker", "DrawUnifiedLayerStack",
                "DrawNodeActions", "DrawAddPass", "ShowAddGroupMenu", "ShowAddMapMenu", "ShowModifierMenu", "DrawHelpers" })
                Assert.That(methods, Does.Not.Contain(removed), "Navigation and node management belong in Hair Nodes: " + removed);
            Assert.That(typeof(HairGroomNodeWindow).GetMethod("ShowModifierMenu", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
            Assert.That(typeof(HairGroomNodeWindow).GetMethod("ShowAddMapMenu", BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
        }

        [Test]
        public void TreeOwnsSculptCreationDuplicationOrderingAndConfirmedRemoval()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                var group = groom.Groups[0];
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Groom, group.Id));
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Select), "A collection must not sculpt an invisible previous pass.");
                var layer = HairGroomNodeWindow.AddSculptPass(stage, group);
                Assert.That(stage.SelectedNode.Layer, Is.SameAs(layer));
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Comb));
                int initialCount = group.sculptLayers.Count;
                Assert.That(HairGroomNodeWindow.DuplicateNode(stage, stage.SelectedNode), Is.True);
                string duplicateId = stage.SelectedNode.Layer.Id;
                Assert.That(duplicateId, Is.Not.EqualTo(layer.Id));
                Assert.That(group.sculptLayers.Count, Is.EqualTo(initialCount + 1));
                Assert.That(HairGroomNodeWindow.MoveSelected(stage, stage.SelectedNode, -1), Is.True);
                Assert.That(stage.SelectedNode.Layer.Id, Is.EqualTo(duplicateId));
                bool prompted = false;
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (title, message, ok, cancel) =>
                { prompted = true; Assert.That(message, Does.Contain("modifiers")); return false; }), Is.False);
                Assert.That(prompted, Is.True);
                Assert.That(group.sculptLayers.Count, Is.EqualTo(initialCount + 1));
                // Separate GUI gestures get separate Undo groups in the editor; model that here.
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (title, message, ok, cancel) => true), Is.True);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Groom));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(groom.Groups[0].sculptLayers.Exists(item => item.Id == duplicateId), Is.True);
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, duplicateId));
                stage.SelectedNode.Layer.locked = true;
                Assert.That(HairGroomNodeWindow.DuplicateNode(stage, stage.SelectedNode), Is.False);
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (title, message, ok, cancel) =>
                { Assert.Fail("Locked removal must not prompt."); return true; }), Is.False);
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        [Test]
        public void TreeCreatesAndSelectsHelpersAndConstraintsWithoutAPropertiesNavigator()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                var group = groom.Groups[0];
                var helper = HairGroomNodeWindow.AddHelper(stage, HairHelperType.CurveRail);
                Assert.That(stage.SelectedNode.Helper, Is.SameAs(helper));
                var constraint = HairGroomNodeWindow.AddConstraint(stage, group, helper);
                Assert.That(stage.SelectedNode.Constraint, Is.SameAs(constraint));
                Assert.That(constraint.helperId, Is.EqualTo(helper.Id));
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (title, message, ok, cancel) => true), Is.True);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Constraints));
                Assert.That(group.constraints.Contains(constraint), Is.False);
                group.locked = true;
                Assert.That(HairGroomNodeWindow.AddConstraint(stage, group, helper), Is.Null);
                Assert.That(HairGroomNodeWindow.AddSculptPass(stage, group), Is.Null);
            }
            finally { Undo.ClearUndo(groom); DestroyEditingStage(stage); }
        }

        [Test]
        public void NodeSelectionNavigatesWithoutMutatingAuthoredDataAndPersistsPerGroom()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.FlowAlign, layer);
            HairCardStage stage = CreateEditingStage();
            try
            {
                string before = EditorJsonUtility.ToJson(groom);
                foreach (HairGroomNode node in stage.Nodes.ToArray())
                {
                    stage.SelectNode(node.Key);
                    Assert.That(stage.SelectedNode.Key, Is.EqualTo(node.Key));
                    Assert.That(stage.WorkflowStep, Is.EqualTo(node.Step));
                    if (node.Map != null) Assert.That(stage.ActiveMap, Is.SameAs(node.Map));
                    if (node.Modifier != null) Assert.That(stage.ActiveModifier, Is.SameAs(node.Modifier));
                }
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
                string preferences = HairPreferenceCodec.Capture(stage);
                Assert.That(preferences, Does.Contain("activeNodeKey"));
                Assert.That(HairPreferenceCodec.Capture(stage, true), Does.Not.Contain("activeNodeKey"));
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Source));
                HairPreferenceCodec.Restore(stage, preferences);
                Assert.That(stage.SelectedNode.Modifier, Is.SameAs(modifier));
                stage.SetActiveLayer(layer.Id);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Layer));
                stage.WorkflowStep = HairWorkflowStep.Guides;
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Guides));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void NodeDragOrderMatchesEvaluationAndRejectsCrossParentDropsWithUndo()
        {
            HairGroup group = groom.Groups[0];
            var first = HairGroomCommands.AddSculptLayer(groom, group, "First");
            var second = HairGroomCommands.AddSculptLayer(groom, group, "Second");
            var a = HairGroomCommands.AddModifier(groom, group, HairModifierType.Width, first);
            var b = HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, first);
            var c = HairGroomCommands.AddModifier(groom, group, HairModifierType.Curl, second);
            string firstId = first.Id, secondId = second.Id, aId = a.Id, bId = b.Id, cId = c.Id;
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGroomNode N(HairGroomNodeKind kind, string id) => stage.FindNode(HairGroomNodes.Key(kind, id));
                Assert.That(stage.Nodes.Where(item => item.Kind == HairGroomNodeKind.Layer).Select(item => item.Layer.Id), Is.EqualTo(group.sculptLayers.Select(item => item.Id)));
                Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
                Assert.That(HairGroomNodes.Move(stage, N(HairGroomNodeKind.Layer, firstId), N(HairGroomNodeKind.Layer, secondId), true), Is.True);
                Assert.That(group.sculptLayers[1], Is.SameAs(first));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(groom.Groups[0].sculptLayers[0].Id, Is.EqualTo(firstId));
                InvokeStageMethod(stage, "OnUndoRedo");
                Assert.That(HairGroomNodes.Move(stage, N(HairGroomNodeKind.Modifier, aId), N(HairGroomNodeKind.Modifier, cId), true), Is.False);
                Assert.That(HairGroomNodes.Move(stage, N(HairGroomNodeKind.Modifier, aId), N(HairGroomNodeKind.Modifier, bId), true), Is.True);
                Assert.That(groom.Groups[0].sculptLayers[0].modifiers[0].Id, Is.EqualTo(bId));
                Assert.That(groom.Groups[0].sculptLayers[0].modifiers[1].Id, Is.EqualTo(aId));
                groom.Groups[0].sculptLayers[0].locked = true; EditorUtility.SetDirty(groom);
                Assert.That(HairGroomNodes.Move(stage, N(HairGroomNodeKind.Modifier, aId), N(HairGroomNodeKind.Modifier, bId), false), Is.False);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void NodeSearchIncludesAncestorsAndOptionalMapsRemainCollapsedByDefault()
        {
            var nodes = HairGroomNodes.Build(groom);
            var density = nodes.Single(node => node.Map?.kind == HairMapKind.Density);
            var noCollapse = new List<string>();
            Assert.That(HairGroomNodeWindow.Filter(nodes, "", noCollapse, noCollapse).Contains(density), Is.False);
            var filtered = HairGroomNodeWindow.Filter(nodes, "multiplier", nodes.Select(node => node.Key).ToList(), noCollapse);
            Assert.That(filtered.Contains(density), Is.True);
            Assert.That(filtered.Exists(node => node.Key == density.ParentKey), Is.True);
            Assert.That(filtered.Exists(node => node.Kind == HairGroomNodeKind.Group), Is.True);
            Assert.That(HairGroomNodeWindow.Filter(nodes, "no-such-node-123", noCollapse, noCollapse), Is.Empty);
        }

        [Test]
        public void NodeToggleIsIndependentOfSelectionAndRemovalFallsBackToAValidNode()
        {
            var group = groom.Groups[0];
            var layer = HairGroomCommands.AddSculptLayer(groom, group);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, layer);
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id));
                var node = stage.FindNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
                Assert.That(HairGroomNodes.Toggle(stage, node, false), Is.True);
                Assert.That(stage.SelectedNode.Layer, Is.SameAs(layer));
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Layer));
                stage.SelectNode(node.Key);
                HairGroomCommands.EditStack(groom, group, modifier.Id, false, 0, true);
                Assert.That(stage.SelectedNode, Is.Not.Null);
                Assert.That(stage.SelectedNode.Kind, Is.Not.EqualTo(HairGroomNodeKind.Modifier));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void NodeKeyboardNavigationAndCollectionSelectionKeepEditingTargetsSafe()
        {
            HairCardStage stage = CreateEditingStage();
            var tree = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            var preview = ScriptableObject.CreateInstance<HairGroomPreviewWindow>();
            try
            {
                var group = groom.Groups[0];
                string optionalKey = HairGroomNodes.Key(HairGroomNodeKind.OptionalMaps, group.Id);
                var collapsed = new List<string>(); var expanded = new List<string>();
                var visible = HairGroomNodeWindow.Filter(stage.Nodes, "", collapsed, expanded);
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Source));
                Assert.That(tree.HandleNodeNavigation(stage, visible, KeyCode.DownArrow), Is.True);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Group));
                tree.HandleNodeNavigation(stage, visible, KeyCode.RightArrow);
                Assert.That(stage.SelectedNode.Map.kind, Is.EqualTo(HairMapKind.GrowthArea));
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.PaintGrowth));
                stage.SelectNode(optionalKey);
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.Select));
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.GrowthMap, group.FindMap(HairMapKind.Density).Id));
                Assert.That(stage.SceneTool, Is.EqualTo(HairSceneTool.PaintGrowth));
                preview.Page = 1;
                stage.SelectNode(stage.SelectedNode.Key);
                Assert.That(preview.Page, Is.EqualTo(1), "Selecting a node must not switch the independent Preview & Settings window.");
                Assert.That(typeof(HairGroomWorkspace).GetField("inspectorPage", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null,
                    "Hair Properties must remain a properties-only window.");
                tree.HandleNodeNavigation(stage, visible, KeyCode.End);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Output));
                Assert.That(tree.HandleNodeNavigation(stage, visible, KeyCode.A), Is.False);
            }
            finally { Object.DestroyImmediate(tree); Object.DestroyImmediate(properties); Object.DestroyImmediate(preview); DestroyEditingStage(stage); }
        }

        [Serializable]
        private sealed class FormerWorkspaceVisibility
        {
            [SerializeField] public string visibilitySearch = "Head";
            [SerializeField] public bool recipeVisibilityExpanded;
            [SerializeField] public bool udimVisibilityExpanded;
            [SerializeField] public bool slotVisibilityExpanded;
            [SerializeField] public bool initialPlacementDone = true;
        }

        [Test]
        public void PreviewWindowPreferencesMoveWithoutCopyingPlacementOrChangingNodeProperties()
        {
            var first = ScriptableObject.CreateInstance<HairGroomPreviewWindow>();
            var second = ScriptableObject.CreateInstance<HairGroomPreviewWindow>();
            var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            try
            {
                string groomBefore = EditorJsonUtility.ToJson(groom);
                string propertiesBefore = HairPreferenceCodec.Capture(properties);
                HairPreferenceCodec.RestoreVisibilityFields(first, HairPreferenceCodec.Capture(new FormerWorkspaceVisibility()));
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Assert.That(typeof(HairGroomPreviewWindow).GetField("visibilitySearch", flags).GetValue(first), Is.EqualTo("Head"));
                Assert.That(typeof(HairGroomPreviewWindow).GetField("recipeVisibilityExpanded", flags).GetValue(first), Is.False);
                Assert.That(typeof(HairGroomPreviewWindow).GetField("initialPlacementDone", flags).GetValue(first), Is.False,
                    "Moving old preferences must not skip first-use placement for the new window.");
                first.Page = 1;
                typeof(HairGroomPreviewWindow).GetField("previewScroll", flags).SetValue(first, new Vector2(0f, 157f));
                typeof(HairGroomPreviewWindow).GetField("settingsScroll", flags).SetValue(first, new Vector2(0f, 33f));
                HairPreferenceCodec.Restore(second, HairPreferenceCodec.Capture(first));
                Assert.That(HairPreferenceCodec.Capture(second), Is.EqualTo(HairPreferenceCodec.Capture(first)));
                Assert.That(HairPreferenceCodec.Capture(first, true), Does.Not.Contain("previewScroll").And.Not.Contain("settingsScroll"));
                HairPreferenceCodec.Reset(second);
                Assert.That(second.Page, Is.Zero);
                Assert.That(typeof(HairGroomPreviewWindow).GetField("visibilitySearch", flags).GetValue(second), Is.EqualTo(string.Empty));
                Assert.That(HairPreferenceCodec.Capture(properties), Is.EqualTo(propertiesBefore));
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(groomBefore));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); Object.DestroyImmediate(properties); }
        }

        [Test]
        public void NodeTreePreferencesAreGroomLocalAndResetIndependentlyOfAuthoredData()
        {
            var first = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var second = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            try
            {
                var search = typeof(HairGroomNodeWindow).GetField("nodeSearch", BindingFlags.Instance | BindingFlags.NonPublic);
                search.SetValue(first, "Gravity");
                string before = EditorJsonUtility.ToJson(groom);
                HairPreferenceCodec.Restore(second, HairPreferenceCodec.Capture(first));
                Assert.That(search.GetValue(second), Is.EqualTo("Gravity"));
                Assert.That(HairPreferenceCodec.Capture(first, true), Does.Not.Contain("nodeSearch").And.Not.Contain("collapsedNodeKeys"));
                HairPreferenceCodec.Reset(second);
                Assert.That(search.GetValue(second), Is.EqualTo(string.Empty));
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void LockedPassRejectsImplicitSculptAndExplicitFinishingActionSelectsItsDestination()
        {
            var group = groom.Groups[0];
            var layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id));
                layer.locked = true;
                Assert.That(stage.BeginLayerEditing(), Is.False);
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.right);
                Assert.That(stage.ActiveLayer, Is.SameAs(layer));
                Assert.That(group.sculptLayers.Count, Is.EqualTo(1));
                HairGroomNodeWindow.AddFinishingSculptLayer(stage, group);
                Assert.That(stage.ActiveLayer, Is.Not.SameAs(layer));
                Assert.That(stage.SelectedNode.Layer, Is.SameAs(stage.ActiveLayer));
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase("assign-profile", (int)HairGroomNodeKind.Geometry)]
        [TestCase("assign-atlas", (int)HairGroomNodeKind.Atlas)]
        [TestCase("assign-atlas-region", (int)HairGroomNodeKind.Atlas)]
        public void NodeValidationLinksOpenTheRelevantResourceProperties(string fix, int expected)
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGroomNodeWindow.NavigateToIssue(stage, new HairValidationIssue { groupId = groom.Groups[0].Id, fixId = fix }, false);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo((HairGroomNodeKind)expected));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void NodeAtlasCanvasUsesTheWholePropertiesPanelWithoutAnExplorerDeduction()
        {
            Assert.That(HairGroomWorkspace.NodeContentWidth(480f), Is.EqualTo(452f));
            Assert.That(HairGroomWorkspace.NodeContentWidth(1000f), Is.EqualTo(972f));
        }

        [Test]
        public void NodeCardPickingRoutesToMaterialsAndUvsInsteadOfHidingTheSelectedSet()
        {
            HairCardStage stage = CreateEditingStage();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage);
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Cards, groom.Groups[0].Id));
                HairGroomWorkspace.SelectUvSet(groom.Groups[0].atlas, null);
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Atlas));
            }
            finally { active.SetValue(null, previous); DestroyEditingStage(stage); }
        }

        [UnityTest]
        public IEnumerator NodeWindowsRenderEveryNodeAtDockedAndWideWidthsWithoutChangingData()
        {
            HairGroup group = groom.Groups[0];
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            atlas.EnsureIntegrity();
            Material material = new Material(Shader.Find("Standard"));
            atlas.albedo = Texture2D.whiteTexture;
            atlas.material = material;
            group.atlas = atlas;
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                var modifier = HairGroomCommands.AddModifier(groom, group, type, layer);
                if (type == HairModifierType.SplineFlow) modifier.flowSplines.Add(TestFlowPath(Vector3.zero, Vector3.right * 0.2f));
            }
            var helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.zero);
            HairGroomCommands.AddConstraint(groom, group, HairConstraintType.FollowCurve, helper);
            HairCardStage stage = CreateEditingStage();
            HairGroomWorkspace properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            HairGroomNodeWindow tree = ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            HairGroomPreviewWindow preview = ScriptableObject.CreateInstance<HairGroomPreviewWindow>();
            FieldInfo active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage);
                properties.Show(); tree.Show(); preview.Show();
                string before = EditorJsonUtility.ToJson(groom);
                string atlasBefore = EditorJsonUtility.ToJson(atlas), profileBefore = EditorJsonUtility.ToJson(profile);
                foreach (float width in new[] { 480f, 1000f })
                {
                    properties.position = new Rect(400f, 40f, width, 900f);
                    tree.position = new Rect(20f, 40f, width == 480f ? 320f : 500f, 900f);
                    preview.position = new Rect(1040f, 40f, width == 480f ? 360f : 600f, 900f);
                    foreach (var node in stage.Nodes.ToArray())
                    {
                        stage.SelectNode(node.Key);
                        properties.Repaint(); tree.Repaint(); yield return null; yield return null;
                        LogAssert.NoUnexpectedReceived();
                    }
                    foreach (int page in new[] { 0, 1 })
                    {
                        preview.Page = page;
                        preview.Repaint(); yield return null; yield return null;
                        LogAssert.NoUnexpectedReceived();
                    }
                }
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before), "Browsing nodes must not mutate authored data.");
                Assert.That(EditorJsonUtility.ToJson(atlas), Is.EqualTo(atlasBefore));
                Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(profileBefore));
                tree.Close();
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Geometry, group.Id));
                properties.Repaint(); yield return null; yield return null;
                LogAssert.NoUnexpectedReceived();
                properties.Close();
                preview.Page = 0;
                preview.Repaint(); yield return null; yield return null;
                LogAssert.NoUnexpectedReceived();
                Assert.That(stage.SelectedNode.Kind, Is.EqualTo(HairGroomNodeKind.Geometry));
                active.SetValue(null, null);
                preview.Page = 1;
                preview.Repaint(); yield return null; yield return null;
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (properties != null) properties.Close(); if (tree != null) tree.Close(); if (preview != null) preview.Close();
                active.SetValue(null, previous); DestroyEditingStage(stage);
                Object.DestroyImmediate(atlas); Object.DestroyImmediate(material);
            }
        }
    }
}
#endif
