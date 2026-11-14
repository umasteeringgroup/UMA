using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>
    /// Node management for the Groom Outliner: commands that create, reorder, duplicate or
    /// remove authored data, and the menus that offer them (right-click and the toolbar's
    /// Add menu). Every edit goes through Undo via <see cref="HairGroomCommands"/>.
    /// </summary>
    internal static class HairGroomNodeCommands
    {
        // ---- Commands --------------------------------------------------------------------

        internal static HairSculptLayer AddSculptPass(HairCardStage stage, HairGroup group)
        {
            if (stage?.Groom == null || group == null || group.locked || !stage.Groom.Groups.Contains(group)) return null;
            var pass = HairGroomCommands.AddSculptLayer(stage.Groom, group);
            if (pass != null) { stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id)); stage.BeginLayerEditing(); }
            return pass;
        }

        internal static HairSculptLayer AddFinishingSculptLayer(HairCardStage stage, HairGroup group)
        {
            if (!CanAddFinishingLayer(stage, group)) return null;
            var pass = HairGroomCommands.AddSculptLayer(stage.Groom, group, $"Finishing Sculpt {group.sculptLayers.Count + 1}", true);
            if (pass != null) { stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id)); stage.BeginLayerEditing(); }
            return pass;
        }

        internal static bool CanAddFinishingLayer(HairCardStage stage, HairGroup group) =>
            stage?.Groom != null && group != null && !group.locked && group.enabled && group.visible && stage.Groom.Groups.Contains(group);

        internal static HairHelper AddHelper(HairCardStage stage, HairHelperType type)
        {
            if (stage?.Groom == null) return null;
            var helper = HairGroomCommands.AddHelper(stage.Groom, type, stage.Groom.SourceMesh != null ? stage.Groom.SourceMesh.bounds.center : Vector3.zero);
            if (helper != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
            return helper;
        }

        internal static HairConstraintSettings AddConstraint(HairCardStage stage, HairGroup group, HairHelper helper)
        {
            if (stage?.Groom == null || group == null || group.locked || helper == null ||
                !stage.Groom.Groups.Contains(group) || !stage.Groom.SharedHelpers.Contains(helper)) return null;
            var constraint = HairGroomCommands.AddConstraint(stage.Groom, group, HairConstraintType.FollowCurve, helper);
            if (constraint != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Constraint, constraint.Id));
            return constraint;
        }

        internal static HairHelper BindSceneObject(HairCardStage stage, GameObject source, HairHelperType type)
        {
            if (stage?.Groom == null || source == null) return null;
            var helper = HairGroomCommands.BindSceneHelper(stage.Groom, source, type);
            if (helper != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
            return helper;
        }

        internal static HairSculptLayer OrganizeLegacyModifiers(HairCardStage stage, HairGroup group)
        {
            if (stage?.Groom == null || group == null || group.locked) return null;
            var pass = HairGroomCommands.ImportLegacyModifiers(stage.Groom, group);
            if (pass != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id));
            return pass;
        }

        internal static bool CanMove(HairCardStage stage, HairGroomNode node, int offset) =>
            stage?.Groom != null && node != null && !node.Locked && node.CanReorder && stage.FindNode(node.Key) == node &&
            HairGroomNodeHandler.For(node.Kind).CanMoveBy(node, offset);

        internal static bool MoveSelected(HairCardStage stage, HairGroomNode node, int offset)
        {
            if (stage?.Groom == null || node == null || !node.CanReorder || node.Locked || stage.FindNode(node.Key) != node) return false;
            return HairGroomNodeHandler.For(node.Kind).MoveBy(stage, node, offset);
        }

        internal static bool DuplicateNode(HairCardStage stage, HairGroomNode node)
        {
            if (stage?.Groom == null || node == null || !node.CanReorder || node.Locked || stage.FindNode(node.Key) != node) return false;
            return HairGroomNodeHandler.For(node.Kind).Duplicate(stage, node);
        }

        internal static bool CanRemoveNode(HairCardStage stage, HairGroomNode node) =>
            stage?.Groom != null && node != null && !node.Locked && stage.FindNode(node.Key) == node &&
            HairGroomNodeHandler.For(node.Kind).CanRemove(stage, node);

        internal static bool RemoveNode(HairCardStage stage, HairGroomNode node,
            Func<string, string, string, string, bool> confirm = null)
        {
            if (!CanRemoveNode(stage, node)) return false;
            return HairGroomNodeHandler.For(node.Kind).Remove(stage, node, confirm);
        }

        internal static void NavigateToIssue(HairCardStage stage, HairValidationIssue issue, bool selectGuide)
        {
            if (stage?.Groom == null || issue == null) return;
            stage.NavigateToIssue(issue, selectGuide);
            if (Resources.FindObjectsOfTypeAll<HairGroomWorkspace>().Length == 0 && !HairEditorPreferences.Suspended)
                HairGroomWorkspace.OpenProperties();
        }

        // ---- Menus -----------------------------------------------------------------------

        /// <summary>The full right-click menu for a node: its own items, then shared order/lock/remove.</summary>
        internal static GenericMenu BuildNodeMenu(HairCardStage stage, HairGroomNode node)
        {
            var menu = new GenericMenu();
            if (stage?.Groom == null) return menu;
            if (node == null) { AddRootItems(stage, menu, "Add/"); return menu; }
            HairGroomNodeHandler handler = HairGroomNodeHandler.For(node.Kind);
            handler.AddMenuItems(stage, node, menu);
            if (node.CanToggle)
            {
                menu.AddSeparator(string.Empty);
                bool enabled = node.Enabled;
                Item(menu, "Active", !node.Locked, () => HairGroomNodes.Toggle(stage, node, !enabled), enabled);
            }
            if (handler.CanLock(node))
            {
                bool locked = handler.IsLockedSelf(node);
                Item(menu, "Locked", true, () => HairGroomNodes.SetLocked(stage, node, !locked), locked);
            }
            if (node.CanReorder)
            {
                menu.AddSeparator(string.Empty);
                Item(menu, "Move Earlier", CanMove(stage, node, -1), () => MoveSelected(stage, node, -1));
                Item(menu, "Move Later", CanMove(stage, node, 1), () => MoveSelected(stage, node, 1));
                Item(menu, "Duplicate", !node.Locked, () => DuplicateNode(stage, node));
            }
            if (handler.CanRemove(stage, node) || node.CanReorder)
            {
                menu.AddSeparator(string.Empty);
                Item(menu, "Remove…", CanRemoveNode(stage, node), () => RemoveNode(stage, node));
            }
            return menu;
        }

        /// <summary>Only the creation items for the selection (the toolbar's Add menu).</summary>
        internal static GenericMenu BuildAddMenu(HairCardStage stage, HairGroomNode node)
        {
            var menu = new GenericMenu();
            if (stage?.Groom == null) return menu;
            // Re-root the selection's "Add/…" items and always offer groom-level creation.
            CopyAddItems(stage, node, menu);
            if (menu.GetItemCount() > 0) menu.AddSeparator(string.Empty);
            AddRootItems(stage, menu, string.Empty);
            return menu;
        }

        private static void CopyAddItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
        {
            if (node == null) return;
            var group = node.Group;
            switch (node.Kind)
            {
                case HairGroomNodeKind.Group:
                    Item(menu, "Sculpt Pass", !node.Locked, () => AddSculptPass(stage, group));
                    AddMapItems(stage, group, menu, "Map/", node.Locked);
                    AddGenerationItems(stage, node, menu, string.Empty);
                    break;
                case HairGroomNodeKind.GrowthMap:
                case HairGroomNodeKind.OptionalMaps:
                    AddMapItems(stage, group, menu, "Map/", node.Locked);
                    break;
                case HairGroomNodeKind.Groom:
                    Item(menu, "Sculpt Pass", !node.Locked, () => AddSculptPass(stage, group));
                    AddFinishingItem(stage, group, menu, string.Empty);
                    break;
                case HairGroomNodeKind.Layer:
                    AddLayerModifierItems(stage, group, node.Layer, menu, "Modifier/");
                    Item(menu, "Gather Helper and Modifier", !node.Locked, () => HairGatherEditor.AddGather(stage, node));
                    Item(menu, "Sculpt Pass", !group.locked, () => AddSculptPass(stage, group));
                    AddFinishingItem(stage, group, menu, string.Empty);
                    break;
                case HairGroomNodeKind.Modifier:
                    if (node.Population != null) AddPopulationModifierItems(stage, group, node.Population, menu, "Modifier/");
                    else AddLayerModifierItems(stage, group, node.Layer, menu, "Modifier/");
                    break;
                case HairGroomNodeKind.Population:
                    AddPopulationModifierItems(stage, group, node.Population, menu, "Modifier/");
                    Item(menu, "Gather Helper and Modifier", !node.Locked, () => HairGatherEditor.AddGather(stage, node));
                    AddGenerationItems(stage, node, menu, string.Empty);
                    break;
                case HairGroomNodeKind.Children:
                    AddGenerationItems(stage, node, menu, string.Empty);
                    break;
                case HairGroomNodeKind.Constraints:
                case HairGroomNodeKind.Constraint:
                    AddConstraintItems(stage, group, menu, "Constraint to Helper/");
                    break;
            }
        }

        private static void AddRootItems(HairCardStage stage, GenericMenu menu, string prefix)
        {
            AddGroupItems(stage, menu, prefix);
            AddHelperItems(stage, menu, prefix + "Helper/");
        }

        internal static void AddGroupItems(HairCardStage stage, GenericMenu menu, string prefix)
        {
            foreach (HairGroupRole role in Enum.GetValues(typeof(HairGroupRole)))
            {
                HairGroupRole captured = role;
                Item(menu, prefix + "Group/" + ObjectNames.NicifyVariableName(role.ToString()), true, () =>
                {
                    HairGroup group = HairGroomCommands.AddGroup(stage.Groom, captured);
                    stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Group, group.Id));
                });
            }
            bool hasSource = stage.Groom.SourceMesh != null;
            menu.AddSeparator(prefix + "Group/");
            Item(menu, prefix + "Group/Grid Panel Group", hasSource, () => HairFormEditor.AddGroup(stage, HairPopulationSource.GridPanels));
            Item(menu, prefix + "Group/Braid Group", hasSource, () => HairFormEditor.AddGroup(stage, HairPopulationSource.Braid));
            Item(menu, prefix + "Group/Bun Group", hasSource, () => HairFormEditor.AddGroup(stage, HairPopulationSource.Bun));
        }

        internal static void AddHelperItems(HairCardStage stage, GenericMenu menu, string prefix)
        {
            Item(menu, prefix + "Curve Rail", true, () => AddHelper(stage, HairHelperType.CurveRail));
            Item(menu, prefix + "Braid Spline", true, () => AddHelper(stage, HairHelperType.BraidRail));
            Item(menu, prefix + "Collider", true, () => AddHelper(stage, HairHelperType.Sphere));
            Item(menu, prefix + "Gather Ring", true, () => AddHelper(stage, HairHelperType.Gather));
            Item(menu, prefix + "Bun Helper", true, () => AddHelper(stage, HairHelperType.Bun));
            Item(menu, prefix + "Attachment", true, () => AddHelper(stage, HairHelperType.BindingRing));
            GameObject selected = Selection.activeGameObject;
            string name = selected != null ? " '" + selected.name + "'" : string.Empty;
            menu.AddSeparator(prefix);
            Item(menu, prefix + "Bind Selected Scene Object" + name + " as Attachment", selected != null,
                () => BindSceneObject(stage, selected, HairHelperType.BindingRing));
            Item(menu, prefix + "Bind Selected Scene Object" + name + " as Curve Rail", selected != null,
                () => BindSceneObject(stage, selected, HairHelperType.CurveRail));
        }

        internal static void AddHelperActionItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
        {
            HairHelper helper = node.Helper;
            HairGroup group = stage.ActiveGroup;
            menu.AddSeparator(string.Empty);
            Item(menu, "Constrain Group '" + (group?.name ?? "None") + "' to This Helper", group != null && !group.locked,
                () => AddConstraint(stage, group, helper));
            if (helper.type == HairHelperType.Gather || helper.type == HairHelperType.Bun)
                Item(menu, "Duplicate Mirrored Helper", !helper.locked, () =>
                {
                    var copy = HairGatherEditor.Mirror(stage.Groom, helper);
                    if (copy != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, copy.Id));
                });
            if (helper.type == HairHelperType.Bun)
                Item(menu, "Make Surrounding Braid Spline Editable", !helper.locked, () =>
                {
                    var rail = HairBraidSplineEditor.ExtractBunBraid(stage.Groom, helper);
                    if (rail != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, rail.Id));
                });
        }

        internal static void AddConstraintItems(HairCardStage stage, HairGroup group, GenericMenu menu, string prefix)
        {
            bool any = false;
            foreach (var helper in stage.Groom.SharedHelpers)
            {
                if (helper == null) continue;
                var captured = helper; any = true;
                Item(menu, prefix + helper.name, group != null && !group.locked, () => AddConstraint(stage, group, captured));
            }
            if (!any) Item(menu, prefix + "No shared helpers yet", false, null);
        }

        internal static void AddMapItems(HairCardStage stage, HairGroup group, GenericMenu menu, string prefix, bool locked)
        {
            if (group == null) return;
            foreach (HairMapKind kind in Enum.GetValues(typeof(HairMapKind)))
            {
                HairMapKind captured = kind;
                string label = captured == HairMapKind.GrowthArea ? "Growth - Density" :
                    captured == HairMapKind.Density ? "Density Multiplier (optional)" : ObjectNames.NicifyVariableName(captured.ToString());
                bool exists = group.FindMap(captured) != null && captured != HairMapKind.Custom;
                Item(menu, prefix + label, !exists && !locked, () =>
                {
                    HairGrowthMap map = HairGroomCommands.EnsureMap(stage.Groom, group, captured);
                    if (stage.ActiveGroup != group) stage.SetActiveGroup(group.Id);
                    stage.SetActiveMap(map.Id);
                });
            }
        }

        internal static void AddLayerModifierItems(HairCardStage stage, HairGroup group, HairSculptLayer layer, GenericMenu menu, string prefix)
        {
            bool locked = group == null || layer == null || group.locked || layer.locked;
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                if (type == HairModifierType.LodReduction) continue;
                HairModifierType captured = type;
                Item(menu, prefix + ObjectNames.NicifyVariableName(type.ToString()), !locked, () => AddLayerModifier(stage, group, layer, captured));
            }
        }

        internal static HairModifierSettings AddLayerModifier(HairCardStage stage, HairGroup group, HairSculptLayer layer, HairModifierType type)
        {
            if (stage?.Groom == null || group == null || layer == null || group.locked || layer.locked ||
                !stage.Groom.Groups.Contains(group) || !group.sculptLayers.Contains(layer)) return null;
            HairModifierSettings modifier = HairGroomCommands.AddModifier(stage.Groom, group, type, layer);
            if (modifier == null) return null;
            modifier.rootInfluence = stage.BrushRootInfluence;
            if (type == HairModifierType.Gravity)
            {
                modifier.gravityStrength = stage.GravityStrength;
                modifier.gravitySeparation = stage.GravitySeparation;
                modifier.gravityCollision = stage.GravityCollision;
                modifier.gravityClearance = stage.GravityClearance;
            }
            HairGroomCommands.Commit(stage.Groom);
            if (stage.ActiveGroup != group) stage.SetActiveGroup(group.Id);
            stage.SetActiveModifier(layer.Id, modifier.Id);
            return modifier;
        }

        internal static void AddPopulationModifierItems(HairCardStage stage, HairGroup group, HairGenerationStage population, GenericMenu menu, string prefix)
        {
            bool locked = group == null || population == null || group.locked;
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                if (type == HairModifierType.LodReduction) continue;
                var captured = type;
                Item(menu, prefix + ObjectNames.NicifyVariableName(type.ToString()), !locked, () =>
                {
                    var modifier = HairGenerationEditor.AddModifier(stage.Groom, group, population, captured);
                    if (modifier != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
                });
            }
        }

        internal static void AddFinishingItem(HairCardStage stage, HairGroup group, GenericMenu menu, string prefix = "Add/") =>
            Item(menu, prefix + "Finishing Sculpt Layer", CanAddFinishingLayer(stage, group), () => AddFinishingSculptLayer(stage, group));

        internal static void AddGenerationItems(HairCardStage stage, HairGroomNode node, GenericMenu menu, string prefix = "Add/")
        {
            HairGroup group = node.Group;
            if (group?.generation?.enabled != true) return;
            Item(menu, prefix + "Clump Guide Stage", !group.locked, () =>
            {
                var population = HairGenerationEditor.AddPopulation(stage.Groom, group);
                if (population != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Population, population.Id));
            });
        }

        internal static GenericMenu BuildIssuesMenu(HairCardStage stage)
        {
            var menu = new GenericMenu();
            var report = stage?.ReleaseValidation;
            if (report == null || report.issues.Count == 0) return menu;
            for (int i = 0; i < report.issues.Count; i++)
            {
                var issue = report.issues[i];
                string path = (i + 1) + ". " + issue.severity + ": " + issue.message.Replace("/", " - ").Replace("\n", " ");
                menu.AddItem(new GUIContent(path + "/Go to setting"), false, () => NavigateToIssue(stage, issue, false));
                if (!string.IsNullOrEmpty(issue.guideId))
                    menu.AddItem(new GUIContent(path + "/Select guide"), false, () => NavigateToIssue(stage, issue, true));
            }
            return menu;
        }

        // Kept for callers that open these lists from an IMGUI context.
        internal static void ShowModifierMenu(HairCardStage stage)
        {
            var node = stage?.SelectedNode;
            if (node == null) return;
            var menu = new GenericMenu();
            if (node.Population != null) AddPopulationModifierItems(stage, node.Group, node.Population, menu, string.Empty);
            else AddLayerModifierItems(stage, stage.ActiveGroup, stage.ActiveLayer, menu, string.Empty);
            menu.ShowAsContext();
        }

        internal static void ShowAddMapMenu(HairCardStage stage)
        {
            var menu = new GenericMenu();
            AddMapItems(stage, stage?.ActiveGroup, menu, string.Empty, stage?.ActiveGroup?.locked != false);
            menu.ShowAsContext();
        }

        private static void Item(GenericMenu menu, string path, bool enabled, GenericMenu.MenuFunction action, bool on = false)
        {
            if (enabled && action != null) menu.AddItem(new GUIContent(path), on, action);
            else menu.AddDisabledItem(new GUIContent(path), on);
        }
    }
}
