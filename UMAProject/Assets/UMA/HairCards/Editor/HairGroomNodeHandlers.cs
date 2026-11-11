using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>
    /// Behavior of one <see cref="HairGroomNodeKind"/>: state toggles, structural edits,
    /// Properties drawing and context-menu items. Nodes remain a read-only view over the
    /// groom; every edit goes through Undo and <see cref="HairGroomCommands"/>.
    /// </summary>
    internal abstract class HairGroomNodeHandler
    {
        private static readonly Dictionary<HairGroomNodeKind, HairGroomNodeHandler> Handlers = CreateHandlers();
        private static readonly HairGroomNodeHandler Fallback = new InfoHandler(null);

        internal static HairGroomNodeHandler For(HairGroomNodeKind kind) =>
            Handlers.TryGetValue(kind, out var handler) ? handler : Fallback;

        private static Dictionary<HairGroomNodeKind, HairGroomNodeHandler> CreateHandlers()
        {
            var cards = new CardsHandler();
            return new Dictionary<HairGroomNodeKind, HairGroomNodeHandler>
            {
                [HairGroomNodeKind.Source] = new SourceHandler(),
                [HairGroomNodeKind.Group] = new GroupHandler(),
                [HairGroomNodeKind.GrowthMap] = new GrowthMapHandler(),
                [HairGroomNodeKind.OptionalMaps] = new OptionalMapsHandler(),
                [HairGroomNodeKind.Guides] = new GuidesHandler(),
                [HairGroomNodeKind.Groom] = new GroomHandler(),
                [HairGroomNodeKind.Layer] = new LayerHandler(),
                [HairGroomNodeKind.Modifier] = new ModifierHandler(),
                [HairGroomNodeKind.LegacyModifiers] = new LegacyModifiersHandler(),
                [HairGroomNodeKind.Constraints] = new ConstraintsHandler(),
                [HairGroomNodeKind.Constraint] = new ConstraintHandler(),
                [HairGroomNodeKind.Children] = new ChildrenHandler(),
                [HairGroomNodeKind.Population] = new PopulationHandler(),
                [HairGroomNodeKind.Cards] = cards,
                [HairGroomNodeKind.Geometry] = cards,
                [HairGroomNodeKind.Atlas] = cards,
                [HairGroomNodeKind.ScalpShading] = new ScalpShadingHandler(),
                [HairGroomNodeKind.Helpers] = new HelpersHandler(),
                [HairGroomNodeKind.Helper] = new HelperHandler(),
                [HairGroomNodeKind.Optimize] = new OptimizeHandler(),
                [HairGroomNodeKind.Output] = new OutputHandler()
            };
        }

        // ---- Active / lock state -------------------------------------------------------

        internal virtual bool CanToggle(HairGroomNode node) => false;
        internal virtual bool IsEnabled(HairGroomNode node) => true;
        /// <summary>Raw write; callers record Undo first (see <see cref="HairGroomNodes.Toggle"/>).</summary>
        internal virtual void WriteEnabled(HairGroomNode node, bool enabled) { }

        internal virtual bool CanLock(HairGroomNode node) => false;
        internal virtual bool IsLockedSelf(HairGroomNode node) => false;
        internal virtual void WriteLocked(HairGroomNode node, bool locked) { }

        // ---- Structure -----------------------------------------------------------------

        internal virtual bool CanReorder(HairGroomNode node) => false;
        internal virtual bool CanMoveBy(HairGroomNode node, int offset) => false;
        internal virtual bool MoveBy(HairCardStage stage, HairGroomNode node, int offset) => false;
        internal virtual bool CanDropOn(HairGroomNode source, HairGroomNode target) =>
            source != null && target != null && CanReorder(source) && !source.Locked && !target.Locked &&
            source.Kind == target.Kind && source.ParentKey == target.ParentKey && source.Key != target.Key && source.Group == target.Group;
        internal virtual bool MoveRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after) => false;
        internal virtual bool Duplicate(HairCardStage stage, HairGroomNode node) => false;
        internal virtual bool CanRemove(HairCardStage stage, HairGroomNode node) => false;
        internal virtual bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm) => false;

        // ---- Presentation --------------------------------------------------------------

        /// <summary>Kind-specific Properties body; the workspace draws the shared header.</summary>
        internal abstract void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node);

        /// <summary>Items that create, organize or remove authored data from this node.</summary>
        internal virtual void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) { }

        // ---- Shared helpers ------------------------------------------------------------

        protected static void Item(GenericMenu menu, string path, bool enabled, GenericMenu.MenuFunction action, bool on = false)
        {
            if (enabled) menu.AddItem(new GUIContent(path), on, action);
            else menu.AddDisabledItem(new GUIContent(path), on);
        }

        protected static bool Editable(HairCardStage stage, HairGroomNode node) =>
            stage?.Groom != null && node != null && !node.Locked && stage.FindNode(node.Key) == node;

        protected static void Hint(string text) => EditorGUILayout.HelpBox(text, MessageType.Info);

        private sealed class InfoHandler : HairGroomNodeHandler
        {
            private readonly string text;
            internal InfoHandler(string text) { this.text = text; }
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            { if (!string.IsNullOrEmpty(text)) Hint(text); }
        }

        // ---- Kinds ---------------------------------------------------------------------

        private sealed class SourceHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                workspace.DrawSetup(stage);
                EditorGUILayout.Space(4f);
                if (GUILayout.Button(new GUIContent("Import Authored Guide File…", "Import guides authored in another tool into this groom.")))
                { HairGuideImport.ImportDialog(); GUIUtility.ExitGUI(); }
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                HairGroomNodeCommands.AddGroupItems(stage, menu, "Add/");
                Item(menu, "Import Authored Guide File…", true, HairGuideImport.ImportDialog);
            }
        }

        private sealed class GroupHandler : HairGroomNodeHandler
        {
            internal override bool CanToggle(HairGroomNode node) => true;
            internal override bool IsEnabled(HairGroomNode node) => node.Group.enabled;
            internal override void WriteEnabled(HairGroomNode node, bool enabled) => node.Group.enabled = enabled;
            internal override bool CanLock(HairGroomNode node) => true;
            internal override bool IsLockedSelf(HairGroomNode node) => node.Group.locked;
            internal override void WriteLocked(HairGroomNode node, bool locked) => node.Group.locked = locked;
            internal override bool CanRemove(HairCardStage stage, HairGroomNode node) =>
                Editable(stage, node) && stage.Groom.Groups.Count > 1;
            internal override bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm)
            {
                if (!HairGroomCommands.RemoveGroup(stage.Groom, node.Group.Id)) return false;
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Group, stage.Groom.Groups[0].Id));
                return true;
            }
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node) =>
                HairGroomWorkspace.DrawGroupNode(stage, node.Group);
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                HairGroomNodeCommands.AddGroupItems(stage, menu, "Add/");
                Item(menu, "Add/Sculpt Pass", !node.Locked, () => HairGroomNodeCommands.AddSculptPass(stage, node.Group));
                HairGroomNodeCommands.AddMapItems(stage, node.Group, menu, "Add/Map/", node.Locked);
                HairGroomNodeCommands.AddGenerationItems(stage, node, menu);
            }
        }

        private sealed class GrowthMapHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                using (new EditorGUI.DisabledScope(node.Group.locked)) workspace.DrawGrowth(stage);
                HairGroomNodeReferences.DrawUsedBy(stage, node);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddMapItems(stage, node.Group, menu, "Add/Map/", node.Locked);
        }

        private sealed class OptionalMapsHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                EditorGUILayout.LabelField("Maps", node.Group.maps.Count.ToString());
                Hint("Expand Optional Maps in the Groom Outliner and select a map to paint it. Right-click to add an optional map.");
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddMapItems(stage, node.Group, menu, "Add/Map/", node.Locked);
        }

        private sealed class GuidesHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node) => workspace.DrawGuides(stage);
        }

        private sealed class GroomHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                EditorGUILayout.LabelField("Sculpt passes", node.Group.sculptLayers.Count.ToString());
                Hint("Select a sculpt pass beneath Grooming in the Groom Outliner to comb or sculpt. Right-click to add a pass. Passes evaluate top to bottom.");
                HairGroomWorkspace.DrawLayerEditingControls(stage, node);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                Item(menu, "Add/Sculpt Pass", !node.Locked, () => HairGroomNodeCommands.AddSculptPass(stage, node.Group));
                HairGroomNodeCommands.AddFinishingItem(stage, node.Group, menu);
            }
        }

        private sealed class LayerHandler : HairGroomNodeHandler
        {
            internal override bool CanToggle(HairGroomNode node) => true;
            internal override bool IsEnabled(HairGroomNode node) => node.Layer.visible;
            internal override void WriteEnabled(HairGroomNode node, bool enabled) => node.Layer.visible = enabled;
            internal override bool CanLock(HairGroomNode node) => !node.Group.locked;
            internal override bool IsLockedSelf(HairGroomNode node) => node.Layer.locked;
            internal override void WriteLocked(HairGroomNode node, bool locked) => node.Layer.locked = locked;
            internal override bool CanReorder(HairGroomNode node) => true;
            internal override bool CanMoveBy(HairGroomNode node, int offset)
            {
                var layers = node.Group.sculptLayers;
                int index = layers.IndexOf(node.Layer), next = index + offset;
                return index >= 0 && next >= 0 && next < layers.Count && layers[next].afterGroupOperations == node.Layer.afterGroupOperations;
            }
            internal override bool MoveBy(HairCardStage stage, HairGroomNode node, int offset) => MoveStack(stage, node, offset);
            internal override bool CanDropOn(HairGroomNode source, HairGroomNode target) =>
                base.CanDropOn(source, target) && source.Layer.afterGroupOperations == target.Layer.afterGroupOperations;
            internal override bool MoveRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after) =>
                MoveStackRelative(stage, source, target, after, source.Group.sculptLayers.IndexOf(source.Layer), source.Group.sculptLayers.IndexOf(target.Layer));
            internal override bool Duplicate(HairCardStage stage, HairGroomNode node)
            {
                string id = HairGroomCommands.DuplicateLayer(stage.Groom, node.Group, node.Layer)?.Id;
                if (id == null) return false;
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, id)); return true;
            }
            internal override bool CanRemove(HairCardStage stage, HairGroomNode node) => Editable(stage, node);
            internal override bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm) =>
                RemoveFromStack(stage, node, confirm, " and all its sculpting and modifiers?");
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                HairGroomWorkspace.DrawLayerEditingControls(stage, node);
                HairGroomWorkspace.DrawPassNode(stage, node);
                workspace.DrawGroom(stage);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                HairGroomNodeCommands.AddLayerModifierItems(stage, node.Group, node.Layer, menu, "Add/Modifier/");
                Item(menu, "Add/Gather Helper and Modifier", !node.Locked, () => HairGatherEditor.AddGather(stage, node));
                Item(menu, "Add/Sculpt Pass", !node.Group.locked, () => HairGroomNodeCommands.AddSculptPass(stage, node.Group));
                HairGroomNodeCommands.AddFinishingItem(stage, node.Group, menu);
            }
        }

        private sealed class ModifierHandler : HairGroomNodeHandler
        {
            internal override bool CanToggle(HairGroomNode node) => true;
            internal override bool IsEnabled(HairGroomNode node) => node.Modifier.enabled;
            internal override void WriteEnabled(HairGroomNode node, bool enabled) => node.Modifier.enabled = enabled;
            internal override bool CanReorder(HairGroomNode node) => true;
            internal override bool CanMoveBy(HairGroomNode node, int offset)
            {
                var list = node.Population != null ? node.Population.modifiers : node.Layer.modifiers;
                int index = list.IndexOf(node.Modifier), next = index + offset;
                return index >= 0 && next >= 0 && next < list.Count;
            }
            internal override bool MoveBy(HairCardStage stage, HairGroomNode node, int offset) =>
                node.Population != null ? HairGenerationEditor.Move(stage, node, offset) : MoveStack(stage, node, offset);
            internal override bool MoveRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after) =>
                source.Population != null ? HairGenerationEditor.MoveRelative(stage, source, target, after) :
                MoveStackRelative(stage, source, target, after, source.Layer.modifiers.IndexOf(source.Modifier), source.Layer.modifiers.IndexOf(target.Modifier));
            internal override bool Duplicate(HairCardStage stage, HairGroomNode node)
            {
                if (node.Population != null) return HairGenerationEditor.Duplicate(stage, node);
                string id = HairGroomCommands.DuplicateModifier(stage.Groom, node.Group, node.Layer, node.Modifier)?.Id;
                if (id == null) return false;
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, id)); return true;
            }
            internal override bool CanRemove(HairCardStage stage, HairGroomNode node) => Editable(stage, node);
            internal override bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm) =>
                node.Population != null ? HairGenerationEditor.Remove(stage, node, confirm) : RemoveFromStack(stage, node, confirm, "?");
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                if (node.Layer != null && (!node.Layer.visible || node.Layer.opacity <= 0f))
                    EditorGUILayout.HelpBox("The owning sculpt pass is hidden or has zero opacity. This modifier will not affect the normal output.", MessageType.Warning);
                HairGroomWorkspace.DrawInspectionControls(stage, node);
                using (new EditorGUI.DisabledScope(node.Locked)) HairGroomWorkspace.DrawModifier(stage, node.Modifier);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                if (node.Population != null) HairGroomNodeCommands.AddPopulationModifierItems(stage, node.Group, node.Population, menu, "Add/Modifier/");
                else HairGroomNodeCommands.AddLayerModifierItems(stage, node.Group, node.Layer, menu, "Add/Modifier/");
            }
        }

        private sealed class LegacyModifiersHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                Hint("These modifiers predate sculpt passes. Organize them into a pass to edit them; the result is unchanged.");
                using (new EditorGUI.DisabledScope(node.Locked))
                    if (GUILayout.Button("Organize into Sculpt Pass")) HairGroomNodeCommands.OrganizeLegacyModifiers(stage, node.Group);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                Item(menu, "Organize into Sculpt Pass", !node.Locked, () => HairGroomNodeCommands.OrganizeLegacyModifiers(stage, node.Group));
        }

        private sealed class ConstraintsHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                EditorGUILayout.LabelField("Constraints", node.Group.constraints.Count.ToString());
                Hint(stage.Groom.SharedHelpers.Count == 0 ? "Create a Shared Helper first, then right-click here to constrain this group to it." :
                    "Select a constraint beneath this branch to edit it. Right-click to constrain this group to a shared helper.");
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddConstraintItems(stage, node.Group, menu, "Add/Constraint to Helper/");
        }

        private sealed class ConstraintHandler : HairGroomNodeHandler
        {
            internal override bool CanToggle(HairGroomNode node) => true;
            internal override bool IsEnabled(HairGroomNode node) => node.Constraint.enabled;
            internal override void WriteEnabled(HairGroomNode node, bool enabled) => node.Constraint.enabled = enabled;
            internal override bool CanRemove(HairCardStage stage, HairGroomNode node) => Editable(stage, node);
            internal override bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm)
            {
                confirm ??= EditorUtility.DisplayDialog;
                if (!confirm("Remove Hair Node", "Remove '" + node.Label + "'? Undo restores the removed data.", "Remove", "Cancel")) return false;
                if (!CanRemove(stage, node)) return false;
                bool removed = HairGroomCommands.RemoveConstraint(stage.Groom, node.Group, node.Constraint.Id);
                if (removed) stage.SelectNode(node.ParentKey);
                return removed;
            }
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node) =>
                HairGroomWorkspace.DrawConstraintNode(stage, node);
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddConstraintItems(stage, node.Group, menu, "Add/Constraint to Helper/");
        }

        private sealed class ChildrenHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                HairGroomWorkspace.DrawGenerationPresets(stage, node);
                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    HairGenerationEditor.DrawOverview(stage, node.Group);
                    if (!node.Group.generation.enabled) workspace.DrawChildren(stage);
                }
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddGenerationItems(stage, node, menu);
        }

        private sealed class PopulationHandler : HairGroomNodeHandler
        {
            internal override bool CanToggle(HairGroomNode node) => true;
            internal override bool IsEnabled(HairGroomNode node) => node.Population.enabled;
            internal override void WriteEnabled(HairGroomNode node, bool enabled) => node.Population.enabled = enabled;
            // The final card population is fixed at the end of the chain.
            internal override bool CanReorder(HairGroomNode node) => node.Population != node.Group.generation.cards;
            internal override bool CanMoveBy(HairGroomNode node, int offset)
            {
                int index = node.Group.generation.clumps.IndexOf(node.Population), next = index + offset;
                return CanReorder(node) && index >= 0 && next >= 0 && next < node.Group.generation.clumps.Count;
            }
            internal override bool MoveBy(HairCardStage stage, HairGroomNode node, int offset) => HairGenerationEditor.Move(stage, node, offset);
            internal override bool MoveRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after) =>
                HairGenerationEditor.MoveRelative(stage, source, target, after);
            internal override bool Duplicate(HairCardStage stage, HairGroomNode node) => HairGenerationEditor.Duplicate(stage, node);
            internal override bool CanRemove(HairCardStage stage, HairGroomNode node) => CanReorder(node) && Editable(stage, node);
            internal override bool Remove(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm) =>
                HairGenerationEditor.Remove(stage, node, confirm);
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                HairGroomWorkspace.DrawInspectionControls(stage, node);
                using (new EditorGUI.DisabledScope(node.Locked)) HairGenerationEditor.DrawPopulation(stage, node);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                HairGroomNodeCommands.AddPopulationModifierItems(stage, node.Group, node.Population, menu, "Add/Modifier/");
                Item(menu, "Add/Gather Helper and Modifier", !node.Locked, () => HairGatherEditor.AddGather(stage, node));
                HairGroomNodeCommands.AddGenerationItems(stage, node, menu);
                HairFormEditor.AddFormMenuItems(stage, node, menu);
            }
        }

        private sealed class CardsHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                if (node.Kind == HairGroomNodeKind.Atlas) HairSweptAtlasSetup.DrawTreeAction(stage, node);
                using (new EditorGUI.DisabledScope(node.Locked)) workspace.DrawCards(stage, node.Kind);
            }
        }

        private sealed class ScalpShadingHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            { using (new EditorGUI.DisabledScope(node.Locked)) HairGenerationEditor.DrawScalp(stage, node.Group); }
        }

        private sealed class HelpersHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                EditorGUILayout.LabelField("Shared helpers", stage.Groom.SharedHelpers.Count.ToString());
                Hint("Select a helper beneath Shared Helpers to edit it. Right-click to create a helper or bind the selected scene object.");
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu) =>
                HairGroomNodeCommands.AddHelperItems(stage, menu, "Add/Helper/");
        }

        private sealed class HelperHandler : HairGroomNodeHandler
        {
            internal override bool CanLock(HairGroomNode node) => true;
            internal override bool IsLockedSelf(HairGroomNode node) => node.Helper.locked;
            internal override void WriteLocked(HairGroomNode node, bool locked) => node.Helper.locked = locked;
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node)
            {
                HairGroomWorkspace.DrawHelperNode(stage, node.Helper);
                HairGroomNodeReferences.DrawUsedBy(stage, node);
            }
            internal override void AddMenuItems(HairCardStage stage, HairGroomNode node, GenericMenu menu)
            {
                HairGroomNodeCommands.AddHelperItems(stage, menu, "Add/Helper/");
                HairGroomNodeCommands.AddHelperActionItems(stage, node, menu);
            }
        }

        private sealed class OptimizeHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node) => HairGroomWorkspace.DrawOptimize(stage);
        }

        private sealed class OutputHandler : HairGroomNodeHandler
        {
            internal override void DrawProperties(HairGroomWorkspace workspace, HairCardStage stage, HairGroomNode node) => workspace.DrawValidateAndBake(stage);
        }

        // ---- Shared stack edits (sculpt passes and their modifiers) --------------------

        private static bool MoveStack(HairCardStage stage, HairGroomNode node, int offset)
        {
            bool changed = HairGroomCommands.EditStack(stage.Groom, node.Group, node.Modifier?.Id ?? node.Layer.Id, node.Modifier == null, offset);
            if (changed) stage.SelectNode(node.Key);
            return changed;
        }

        private static bool MoveStackRelative(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after, int from, int to)
        {
            if (from < 0 || to < 0) return false;
            if (after) to++;
            if (from < to) to--;
            return from != to && HairGroomCommands.EditStack(stage.Groom, source.Group,
                source.Modifier == null ? source.Layer.Id : source.Modifier.Id, source.Modifier == null, to - from);
        }

        private static bool RemoveFromStack(HairCardStage stage, HairGroomNode node, Func<string, string, string, string, bool> confirm, string suffix)
        {
            confirm ??= EditorUtility.DisplayDialog;
            if (!confirm("Remove Hair Node", "Remove '" + node.Label + "'" + suffix + " Undo restores the removed data.", "Remove", "Cancel")) return false;
            if (!Editable(stage, node)) return false;
            bool removed = HairGroomCommands.EditStack(stage.Groom, node.Group, node.Modifier?.Id ?? node.Layer.Id, node.Modifier == null, 0, true);
            if (removed) stage.SelectNode(node.ParentKey);
            return removed;
        }
    }
}
