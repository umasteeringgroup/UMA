using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal enum HairGroomNodeKind
    {
        Source, Group, GrowthMap, OptionalMaps, Guides, Groom, Layer, Modifier,
        Children, Cards, Geometry, Atlas, Helpers, Helper, Constraints, Constraint, Optimize, Output, LegacyModifiers,
        Population, ScalpShading
    }

    // A typed view over authored data, not a second copy of it. Keys survive rename, reorder and Undo.
    internal sealed class HairGroomNode
    {
        internal string Key, ParentKey, Label, Description;
        internal HairGroomNodeKind Kind;
        internal HairWorkflowStep Step;
        internal HairGroup Group;
        internal HairSculptLayer Layer;
        internal HairModifierSettings Modifier;
        internal HairGrowthMap Map;
        internal HairHelper Helper;
        internal HairConstraintSettings Constraint;
        internal HairGenerationStage Population;
        internal int Depth;
        internal bool HasChildren;
        internal HairGroomNodeHandler Handler => HairGroomNodeHandler.For(Kind);
        internal bool CanReorder => Handler.CanReorder(this);
        // Locked by itself or by an owner (group, sculpt pass or helper).
        internal bool Locked => Group?.locked == true || Layer?.locked == true || Helper?.locked == true;
        internal bool CanToggle => Handler.CanToggle(this);
        internal bool Enabled => Handler.IsEnabled(this);
    }

    internal static class HairGroomNodes
    {
        internal static string Key(HairGroomNodeKind kind, string id = null) => kind + ":" + (id ?? "root");

        internal static List<HairGroomNode> Build(HairGroomAsset groom)
        {
            var nodes = new List<HairGroomNode>();
            if (groom == null) return nodes;
            HairGroomNode Add(HairGroomNodeKind kind, string label, string description, HairWorkflowStep step,
                string id = null, HairGroomNode parent = null, HairGroup group = null)
            {
                var node = new HairGroomNode { Kind = kind, Key = Key(kind, id), Label = label, Description = description,
                    Step = step, ParentKey = parent?.Key, Depth = parent == null ? 0 : parent.Depth + 1, Group = group };
                if (parent != null) parent.HasChildren = true;
                nodes.Add(node); return node;
            }
            Add(HairGroomNodeKind.Source, "Source & Setup", "Source binding, topology, root reprojection and symmetry.", HairWorkflowStep.Setup);
            foreach (HairGroup group in groom.Groups)
            {
                if (group == null) continue;
                var root = Add(HairGroomNodeKind.Group, group.name, "Group identity, preview visibility, locking and inclusion in output.", HairWorkflowStep.Setup, group.Id, group: group);
                HairGrowthMap primary = group.FindMap(HairMapKind.GrowthArea);
                var rootOwner = group.generation.enabled && group.generation.cards.source == HairPopulationSource.PaintedScalp
                    ? groom.Groups.Find(g => g.Id == group.generation.cards.rootMapGroupId) : null;
                if (primary != null) Add(HairGroomNodeKind.GrowthMap, rootOwner != null ? "Growth / Density → " + rootOwner.name : "Growth / Density",
                    rootOwner != null ? "Shared growth map: selecting this opens the source group's painting." : "Paint 0 for no growth, 1 for full density. Painting does not overwrite authored guides.", HairWorkflowStep.Growth, primary.Id, root, group).Map = primary;
                var maps = Add(HairGroomNodeKind.OptionalMaps, "Optional Maps", "Optional density multiplier, length and styling masks. The multiplier defaults to 1.", HairWorkflowStep.Growth, group.Id, root, group);
                foreach (HairGrowthMap map in group.maps)
                    if (map != null && map != primary) Add(HairGroomNodeKind.GrowthMap, map.DisplayName, "Paint this map; its values remain independent of the primary Growth / Density map.", HairWorkflowStep.Growth, map.Id, maps, group).Map = map;
                Add(HairGroomNodeKind.Guides, $"Guides ({group.guides.Count:N0})", "Place, generate, select and remove guides. Preview a generated layout before accepting it.", HairWorkflowStep.Guides, group.Id, root, group);
                var grooming = Add(HairGroomNodeKind.Groom, "Grooming", "Sculpt passes evaluate top to bottom, each followed by its modifiers. Select a pass to sculpt; select a modifier to edit it.", HairWorkflowStep.Groom, group.Id, root, group);
                void AddPass(HairSculptLayer layer)
                {
                    var pass = Add(HairGroomNodeKind.Layer, layer.name, (layer.afterGroupOperations ? "Finishing sculpt after group modifiers and constraints. " : "") +
                        "Use Edit This Layer to sculpt before its modifiers; use a finishing layer to refine the final result. Opacity scales sculpting and modifiers.", HairWorkflowStep.Groom, layer.Id, grooming, group);
                    pass.Layer = layer;
                    foreach (HairModifierSettings modifier in layer.modifiers)
                    {
                        if (modifier == null) continue;
                        var operation = Add(HairGroomNodeKind.Modifier, modifier.name, ObjectNames.NicifyVariableName(modifier.type.ToString()) + " · processes the accumulated hair at this point in the tree.", HairWorkflowStep.Groom, modifier.Id, pass, group);
                        operation.Layer = layer; operation.Modifier = modifier;
                    }
                }
                foreach (HairSculptLayer layer in group.sculptLayers) if (layer != null && !layer.afterGroupOperations) AddPass(layer);
                if (group.modifiers.Count > 0) Add(HairGroomNodeKind.LegacyModifiers, "Unorganized modifiers", "Move these existing modifiers into a sculpt pass without changing their result.", HairWorkflowStep.Groom, group.Id, grooming, group);
                var constraints = Add(HairGroomNodeKind.Constraints, "Constraints", "Bind this group's hair to shared helpers. Runs before finishing sculpt layers.", HairWorkflowStep.Groom, group.Id, grooming, group);
                foreach (HairConstraintSettings constraint in group.constraints)
                    if (constraint != null) Add(HairGroomNodeKind.Constraint, constraint.name, "Helper constraint applied after grooming.", HairWorkflowStep.Groom, constraint.Id, constraints, group).Constraint = constraint;
                foreach (HairSculptLayer layer in group.sculptLayers) if (layer != null && layer.afterGroupOperations) AddPass(layer);
                var generation = Add(HairGroomNodeKind.Children, "Generate Hair", "Scalp-bound roots -> clump guides -> final cards. Inspect each population independently.", HairWorkflowStep.Cards, group.Id, root, group);
                if (group.generation?.enabled == true)
                {
                    void AddPopulation(HairGenerationStage population, bool final)
                    {
                        if (population == null) return;
                        var item = Add(HairGroomNodeKind.Population, population.name, population.source != HairPopulationSource.Scalp ?
                            "Form-driven hair: edit its grid/rail under Shared Helpers, tune this population, then add modifiers beneath it." : final ? "Final scalp-bound cards, following the last enabled clump population." :
                            "Intermediate clump guides: shape this population before generating finer hair from it.", HairWorkflowStep.Cards, population.Id, generation, group);
                        item.Population = population;
                        foreach (var modifier in population.modifiers)
                        {
                            if (modifier == null) continue;
                            var operation = Add(HairGroomNodeKind.Modifier, modifier.name, "Shapes only this population, once. Later populations inherit the result.", HairWorkflowStep.Cards, modifier.Id, item, group);
                            operation.Population = population; operation.Modifier = modifier;
                        }
                    }
                    foreach (var population in group.generation.clumps) AddPopulation(population, false);
                    AddPopulation(group.generation.cards, true);
                }
                var cards = Add(HairGroomNodeKind.Cards, "Hair Cards", "Assign shared card/atlas resources and preview the result.", HairWorkflowStep.Cards, group.Id, root, group);
                Add(HairGroomNodeKind.Geometry, "Geometry & Vertex Colors", "Ribbon shape, taper, sampling, root embedding and root-to-tip RGBA.", HairWorkflowStep.Cards, group.Id, cards, group);
                Add(HairGroomNodeKind.Atlas, "Materials & UVs", "Two material passes, shared colors, textures and inline UV-set editing.", HairWorkflowStep.Cards, group.Id, cards, group);
                Add(HairGroomNodeKind.ScalpShading, "Scalp Vertex Shading", "Painted growth drives a vertex-color Mesh Modifier on the existing scalp/body. No extra scalp cap.", HairWorkflowStep.Cards, group.Id, root, group);
            }
            var helpers = Add(HairGroomNodeKind.Helpers, "Shared Helpers", "Create or bind scene helpers. These resources can be referenced by multiple groups.", HairWorkflowStep.Groom);
            foreach (HairHelper helper in groom.SharedHelpers)
                if (helper != null) Add(HairGroomNodeKind.Helper, helper.name, "Shared helper; changes can affect every referencing group.", HairWorkflowStep.Groom, helper.Id, helpers).Helper = helper;
            Add(HairGroomNodeKind.Optimize, "Optimize & LODs", "Preview quality, LOD budgets and release sampling.", HairWorkflowStep.Optimize);
            Add(HairGroomNodeKind.Output, "Validate & Bake", "Validate all output, resolve issues and bake reusable assets.", HairWorkflowStep.ValidateAndBake);
            return nodes;
        }

        internal static bool Toggle(HairCardStage stage, HairGroomNode node, bool enabled)
        {
            if (stage?.Groom == null || node == null || node.Locked || !node.CanToggle || node.Enabled == enabled) return false;
            if (!StillOwned(stage.Groom, node)) return false;
            if (node.Modifier != null && node.Population == null) return HairGroomCommands.SetModifierEnabled(stage.Groom, node.Group, node.Layer, node.Modifier, enabled);
            Undo.RecordObject(stage.Groom, "Toggle Hair Node");
            node.Handler.WriteEnabled(node, enabled);
            HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation); return true;
        }

        /// <summary>Lock or unlock a group, sculpt pass or helper (its own flag, not its owners).</summary>
        internal static bool SetLocked(HairCardStage stage, HairGroomNode node, bool locked)
        {
            if (stage?.Groom == null || node == null || !node.Handler.CanLock(node) || node.Handler.IsLockedSelf(node) == locked) return false;
            if (node.Helper != null ? !stage.Groom.SharedHelpers.Contains(node.Helper) : !StillOwned(stage.Groom, node)) return false;
            Undo.RecordObject(stage.Groom, locked ? "Lock Hair Node" : "Unlock Hair Node");
            node.Handler.WriteLocked(node, locked);
            HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); return true;
        }

        internal static bool CanDrop(HairGroomNode source, HairGroomNode target) =>
            source != null && target != null && source.Handler.CanDropOn(source, target);

        internal static bool Move(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after)
        {
            if (stage?.Groom == null || !CanDrop(source, target) || !stage.Groom.Groups.Contains(source.Group)) return false;
            return source.Handler.MoveRelative(stage, source, target, after);
        }

        private static bool StillOwned(HairGroomAsset groom, HairGroomNode node) =>
            groom.Groups.Contains(node.Group) &&
            (node.Layer == null || node.Group.sculptLayers.Contains(node.Layer)) &&
            (node.Constraint == null || node.Group.constraints.Contains(node.Constraint));

        /// <summary>Changes only when rows appear, move, rename or change state, so the outliner
        /// does not rebuild for paint strokes and other edits that leave the tree as it was.</summary>
        internal static int StructureSignature(IReadOnlyList<HairGroomNode> nodes)
        {
            unchecked
            {
                int hash = 17;
                foreach (var node in nodes)
                {
                    hash = hash * 31 + StableHash(node.Key);
                    hash = hash * 31 + StableHash(node.ParentKey);
                    hash = hash * 31 + StableHash(node.Label);
                    hash = hash * 31 + (node.CanToggle && node.Enabled ? 1 : 0) + (node.Locked ? 2 : 0) + (node.Handler.IsLockedSelf(node) ? 4 : 0);
                }
                return hash * 31 + nodes.Count;
            }
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                if (text != null) foreach (char c in text) hash = hash * 31 + c;
                return hash;
            }
        }
    }
}
