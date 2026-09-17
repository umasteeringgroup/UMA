using System;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairGroomNodeWindow
    {
        private GameObject sceneHelperCandidate;

        private void DrawTreeActions(HairCardStage stage)
        {
            var node = stage.SelectedNode;
            var group = node?.Group;
            if (node != null && (node.Layer != null || node.Population != null) && !node.Locked && node.Modifier == null)
                if (GUILayout.Button("+ Gather Helper & Modifier")) HairGatherEditor.AddGather(stage, node);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("+ Group", EditorStyles.toolbarButton)) ShowAddGroupMenu(stage);
                using (new EditorGUI.DisabledScope(group == null || group.locked))
                    if (GUILayout.Button("+ Sculpt Pass", EditorStyles.toolbarButton)) AddSculptPass(stage, group);
                using (new EditorGUI.DisabledScope(node == null || (node.Layer == null && node.Population == null) || node.Locked))
                    if (GUILayout.Button("+ Modifier", EditorStyles.toolbarButton)) ShowModifierMenu(stage);
            }
            if (node == null) return;
            HairGenerationEditor.DrawTreeActions(stage, node);
            if (node.Kind == HairGroomNodeKind.Layer || (node.Kind == HairGroomNodeKind.Modifier && node.Layer != null) || node.Kind == HairGroomNodeKind.Groom)
            {
                EditorGUILayout.HelpBox(stage.SculptEditingStatus, MessageType.Info);
                if (stage.IsLayerEditing)
                {
                    if (GUILayout.Button("Return to Final Preview")) stage.ReturnToFinalPreview();
                }
                else
                    using (new EditorGUI.DisabledScope(!stage.CanEditLayer))
                        if (GUILayout.Button(new GUIContent("Edit This Layer", "Edit before this layer's modifiers, temporarily bypassing all later operations."))) stage.BeginLayerEditing();
                using (new EditorGUI.DisabledScope(group == null || group.locked || !group.enabled || !group.visible))
                    if (GUILayout.Button(new GUIContent("Add Finishing Sculpt Layer", "Add an additive layer after the complete guide stack, including helper constraints. Nothing is baked or overwritten.")))
                        AddFinishingSculptLayer(stage, group);
            }
            if (node.Kind == HairGroomNodeKind.OptionalMaps || node.Kind == HairGroomNodeKind.GrowthMap)
                using (new EditorGUI.DisabledScope(node.Locked))
                    if (GUILayout.Button("+ Map...")) ShowAddMapMenu(stage);
            if (node.Kind == HairGroomNodeKind.Helpers || node.Kind == HairGroomNodeKind.Helper)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Curve Rail")) AddHelper(stage, HairHelperType.CurveRail);
                    if (GUILayout.Button("+ Braid Spline")) AddHelper(stage, HairHelperType.BraidRail);
                    if (GUILayout.Button("+ Collider")) AddHelper(stage, HairHelperType.Sphere);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("+ Gather Ring")) AddHelper(stage, HairHelperType.Gather);
                    if (GUILayout.Button("+ Bun Helper")) AddHelper(stage, HairHelperType.Bun);
                    if (GUILayout.Button("+ Attachment")) AddHelper(stage, HairHelperType.BindingRing);
                }
                if (node.Helper?.type == HairHelperType.Bun && !node.Locked && GUILayout.Button("Make Surrounding Braid Spline Editable"))
                {
                    var rail = HairBraidSplineEditor.ExtractBunBraid(stage.Groom, node.Helper);
                    if (rail != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, rail.Id));
                }
                if (node.Helper != null && (node.Helper.type == HairHelperType.Gather || node.Helper.type == HairHelperType.Bun))
                    using (new EditorGUI.DisabledScope(node.Helper.locked))
                        if (GUILayout.Button("Duplicate Mirrored Helper"))
                        {
                            var copy = HairGatherEditor.Mirror(stage.Groom, node.Helper);
                            stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, copy.Id));
                        }
                sceneHelperCandidate = (GameObject)EditorGUILayout.ObjectField(new GUIContent("New helper source",
                    "Scene object to bind; if empty, uses the selected object in the Hierarchy."), sceneHelperCandidate, typeof(GameObject), true);
                using (new EditorGUI.DisabledScope(sceneHelperCandidate == null && Selection.activeGameObject == null))
                    if (GUILayout.Button("Bind Scene Object as Attachment"))
                    {
                        var attachment = HairGroomCommands.BindSceneHelper(stage.Groom, sceneHelperCandidate != null ? sceneHelperCandidate : Selection.activeGameObject, HairHelperType.BindingRing);
                        if (attachment != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, attachment.Id));
                    }
                using (new EditorGUI.DisabledScope(sceneHelperCandidate == null && Selection.activeGameObject == null))
                    if (GUILayout.Button("Bind Scene Object as Curve Rail"))
                    {
                        var helper = HairGroomCommands.BindSceneHelper(stage.Groom,
                            sceneHelperCandidate != null ? sceneHelperCandidate : Selection.activeGameObject, HairHelperType.CurveRail);
                        if (helper != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
                    }
                if (node.Helper != null)
                    using (new EditorGUI.DisabledScope(stage.ActiveGroup == null || stage.ActiveGroup.locked))
                        if (GUILayout.Button("Constrain group: " + (stage.ActiveGroup?.name ?? "None")))
                            AddConstraint(stage, stage.ActiveGroup, node.Helper);
            }
            if (node.Kind == HairGroomNodeKind.Constraints || node.Kind == HairGroomNodeKind.Constraint)
                using (new EditorGUI.DisabledScope(group == null || group.locked || stage.Groom.SharedHelpers.Count == 0))
                    if (GUILayout.Button("+ Constraint to Helper...")) ShowConstraintMenu(stage, group);
            if ((node.Kind == HairGroomNodeKind.Constraints || node.Kind == HairGroomNodeKind.Constraint) && stage.Groom.SharedHelpers.Count == 0)
                EditorGUILayout.LabelField("Create a Shared Helper first.", EditorStyles.wordWrappedMiniLabel);
            if (node.Kind == HairGroomNodeKind.LegacyModifiers)
                using (new EditorGUI.DisabledScope(node.Locked))
                    if (GUILayout.Button("Organize into sculpt pass"))
                    {
                        var pass = HairGroomCommands.ImportLegacyModifiers(stage.Groom, group);
                        if (pass != null) stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id));
                    }
            if (node.Population == null && (node.CanReorder || node.Kind == HairGroomNodeKind.Group || node.Kind == HairGroomNodeKind.Constraint))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (node.CanReorder)
                    {
                        int index = node.Modifier == null ? node.Group.sculptLayers.IndexOf(node.Layer) : node.Layer.modifiers.IndexOf(node.Modifier);
                        int count = node.Modifier == null ? node.Group.sculptLayers.Count : node.Layer.modifiers.Count;
                        bool canMoveEarlier = index > 0 && (node.Modifier != null || node.Group.sculptLayers[index - 1].afterGroupOperations == node.Layer.afterGroupOperations);
                        bool canMoveLater = index < count - 1 && (node.Modifier != null || node.Group.sculptLayers[index + 1].afterGroupOperations == node.Layer.afterGroupOperations);
                        using (new EditorGUI.DisabledScope(node.Locked || !canMoveEarlier))
                            if (GUILayout.Button(new GUIContent("↑", "Earlier in evaluation order"), GUILayout.Width(26f))) MoveSelected(stage, node, -1);
                        using (new EditorGUI.DisabledScope(node.Locked || !canMoveLater))
                            if (GUILayout.Button(new GUIContent("↓", "Later in evaluation order"), GUILayout.Width(26f))) MoveSelected(stage, node, 1);
                        using (new EditorGUI.DisabledScope(node.Locked))
                            if (GUILayout.Button("Duplicate")) DuplicateNode(stage, node);
                    }
                    using (new EditorGUI.DisabledScope(!CanRemoveNode(stage, node)))
                        if (GUILayout.Button("Remove...")) RemoveNode(stage, node);
                }
        }

        internal static HairSculptLayer AddSculptPass(HairCardStage stage, HairGroup group)
        {
            if (stage?.Groom == null || group == null || group.locked || !stage.Groom.Groups.Contains(group)) return null;
            var pass = HairGroomCommands.AddSculptLayer(stage.Groom, group);
            if (pass != null) { stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id)); stage.BeginLayerEditing(); }
            return pass;
        }

        internal static HairSculptLayer AddFinishingSculptLayer(HairCardStage stage, HairGroup group)
        {
            if (stage?.Groom == null || group == null || group.locked || !group.enabled || !group.visible || !stage.Groom.Groups.Contains(group)) return null;
            var pass = HairGroomCommands.AddSculptLayer(stage.Groom, group, $"Finishing Sculpt {group.sculptLayers.Count + 1}", true);
            if (pass != null) { stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, pass.Id)); stage.BeginLayerEditing(); }
            return pass;
        }

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

        private static void ShowConstraintMenu(HairCardStage stage, HairGroup group)
        {
            var menu = new GenericMenu();
            foreach (var helper in stage.Groom.SharedHelpers)
            {
                if (helper == null) continue;
                var captured = helper;
                menu.AddItem(new GUIContent(helper.name), false, () => AddConstraint(stage, group, captured));
            }
            menu.ShowAsContext();
        }

        internal static bool MoveSelected(HairCardStage stage, HairGroomNode node, int offset)
        {
            if (stage?.Groom == null || node == null || !node.CanReorder || node.Locked || stage.FindNode(node.Key) != node) return false;
            if (node.Population != null) return HairGenerationEditor.Move(stage, node, offset);
            bool changed = HairGroomCommands.EditStack(stage.Groom, node.Group, node.Modifier?.Id ?? node.Layer.Id, node.Modifier == null, offset);
            if (changed) stage.SelectNode(node.Key);
            return changed;
        }

        internal static bool DuplicateNode(HairCardStage stage, HairGroomNode node)
        {
            if (stage?.Groom == null || node == null || !node.CanReorder || node.Locked || stage.FindNode(node.Key) != node) return false;
            if (node.Population != null) return HairGenerationEditor.Duplicate(stage, node);
            string id = node.Modifier == null ? HairGroomCommands.DuplicateLayer(stage.Groom, node.Group, node.Layer)?.Id :
                HairGroomCommands.DuplicateModifier(stage.Groom, node.Group, node.Layer, node.Modifier)?.Id;
            if (id == null) return false;
            stage.SelectNode(HairGroomNodes.Key(node.Modifier == null ? HairGroomNodeKind.Layer : HairGroomNodeKind.Modifier, id));
            return true;
        }

        internal static bool CanRemoveNode(HairCardStage stage, HairGroomNode node) =>
            stage?.Groom != null && node != null && !node.Locked && stage.FindNode(node.Key) == node &&
            (node.CanReorder || node.Kind == HairGroomNodeKind.Constraint ||
                (node.Kind == HairGroomNodeKind.Group && stage.Groom.Groups.Count > 1));

        internal static bool RemoveNode(HairCardStage stage, HairGroomNode node,
            Func<string, string, string, string, bool> confirm = null)
        {
            if (!CanRemoveNode(stage, node)) return false;
            if (node.Population != null) return HairGenerationEditor.Remove(stage, node, confirm);
            if (node.Kind == HairGroomNodeKind.Group)
            {
                if (!HairGroomCommands.RemoveGroup(stage.Groom, node.Group.Id)) return false;
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Group, stage.Groom.Groups[0].Id));
                return true;
            }
            confirm ??= EditorUtility.DisplayDialog;
            if (!confirm("Remove Hair Node", "Remove '" + node.Label + "'" +
                (node.Kind == HairGroomNodeKind.Layer ? " and all its sculpting and modifiers?" : "?") +
                " Undo restores the removed data.", "Remove", "Cancel")) return false;
            if (!CanRemoveNode(stage, node)) return false;
            bool removed = node.Kind == HairGroomNodeKind.Constraint ?
                HairGroomCommands.RemoveConstraint(stage.Groom, node.Group, node.Constraint.Id) :
                HairGroomCommands.EditStack(stage.Groom, node.Group, node.Modifier?.Id ?? node.Layer.Id, node.Modifier == null, 0, true);
            if (removed) stage.SelectNode(node.ParentKey);
            return removed;
        }

        private static void ShowIssuesMenu(HairCardStage stage)
        {
            var menu = new GenericMenu();
            var report = stage.ReleaseValidation;
            if (report == null || report.issues.Count == 0) return;
            for (int i = 0; i < report.issues.Count; i++)
            {
                var issue = report.issues[i];
                string path = (i + 1) + ". " + issue.severity + ": " + issue.message.Replace("/", " - ").Replace("\n", " ");
                menu.AddItem(new GUIContent(path + "/Go to setting"), false, () => NavigateToIssue(stage, issue, false));
                if (!string.IsNullOrEmpty(issue.guideId))
                    menu.AddItem(new GUIContent(path + "/Select guide"), false, () => NavigateToIssue(stage, issue, true));
            }
            menu.ShowAsContext();
        }

        internal static void NavigateToIssue(HairCardStage stage, HairValidationIssue issue, bool selectGuide)
        {
            if (stage?.Groom == null || issue == null) return;
            stage.NavigateToIssue(issue, selectGuide);
            if (Resources.FindObjectsOfTypeAll<HairGroomWorkspace>().Length == 0 && !HairEditorPreferences.Suspended)
                HairGroomWorkspace.OpenProperties();
        }

        internal static void ShowAddGroupMenu(HairCardStage stage)
        {
            GenericMenu menu = new GenericMenu();
            foreach (HairGroupRole role in Enum.GetValues(typeof(HairGroupRole)))
            {
                HairGroupRole captured = role;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(role.ToString())), false, () =>
                {
                    HairGroup group = HairGroomCommands.AddGroup(stage.Groom, captured);
                    stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Group, group.Id));
                });
            }
            menu.ShowAsContext();
        }

        internal static void ShowAddMapMenu(HairCardStage stage)
        {
            GenericMenu menu = new GenericMenu();
            foreach (HairMapKind kind in Enum.GetValues(typeof(HairMapKind)))
            {
                HairMapKind captured = kind;
                string label = captured == HairMapKind.GrowthArea ? "Growth / Density" :
                    captured == HairMapKind.Density ? "Density Multiplier (optional)" : ObjectNames.NicifyVariableName(captured.ToString());
                bool exists = stage.ActiveGroup.FindMap(captured) != null && captured != HairMapKind.Custom;
                if (exists) menu.AddDisabledItem(new GUIContent(label));
                else menu.AddItem(new GUIContent(label), false, () =>
                {
                    HairGrowthMap map = HairGroomCommands.EnsureMap(stage.Groom, stage.ActiveGroup, captured);
                    stage.SetActiveMap(map.Id);
                });
            }
            menu.ShowAsContext();
        }

        internal static void ShowModifierMenu(HairCardStage stage)
        {
            if (stage.SelectedNode?.Population != null) { HairGenerationEditor.ShowModifierMenu(stage); return; }
            HairGroup group = stage.ActiveGroup;
            HairSculptLayer layer = stage.ActiveLayer;
            if (layer == null || layer.locked || group.locked) return;
            GenericMenu menu = new GenericMenu();
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
            {
                if (type == HairModifierType.LodReduction) continue;
                HairModifierType captured = type;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.ToString())), false,
                    () =>
                    {
                        if (stage == null || stage.ActiveGroup != group) return;
                        HairModifierSettings modifier = HairGroomCommands.AddModifier(stage.Groom, group, captured, layer);
                        if (modifier == null) return;
                        modifier.rootInfluence = stage.BrushRootInfluence;
                        if (captured == HairModifierType.Gravity)
                        {
                            modifier.gravityStrength = stage.GravityStrength;
                            modifier.gravitySeparation = stage.GravitySeparation;
                            modifier.gravityCollision = stage.GravityCollision;
                            modifier.gravityClearance = stage.GravityClearance;
                        }
                        HairGroomCommands.Commit(stage.Groom);
                        stage.SetActiveModifier(layer.Id, modifier.Id);
                    });
            }
            menu.ShowAsContext();
        }

    }
}
