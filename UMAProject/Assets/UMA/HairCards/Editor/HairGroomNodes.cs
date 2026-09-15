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
        internal bool CanReorder => Kind == HairGroomNodeKind.Layer || Kind == HairGroomNodeKind.Modifier ||
            (Kind == HairGroomNodeKind.Population && Population != Group.generation.cards);
        internal bool Locked => Group?.locked == true || Layer?.locked == true || Helper?.locked == true;
        internal bool CanToggle => Kind == HairGroomNodeKind.Group || Kind == HairGroomNodeKind.Layer ||
            Kind == HairGroomNodeKind.Modifier || Kind == HairGroomNodeKind.Constraint || Kind == HairGroomNodeKind.Population;
        internal bool Enabled => Kind switch
        {
            HairGroomNodeKind.Group => Group.enabled,
            HairGroomNodeKind.Layer => Layer.visible,
            HairGroomNodeKind.Modifier => Modifier.enabled,
            HairGroomNodeKind.Constraint => Constraint.enabled,
            HairGroomNodeKind.Population => Population.enabled,
            _ => true
        };
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
                if (primary != null) Add(HairGroomNodeKind.GrowthMap, "1 · Growth / Density", "Paint 0 for no growth, 1 for full density. Painting does not overwrite authored guides.", HairWorkflowStep.Growth, primary.Id, root, group).Map = primary;
                var maps = Add(HairGroomNodeKind.OptionalMaps, "Optional Maps", "Optional density multiplier, length and styling masks. The multiplier defaults to 1.", HairWorkflowStep.Growth, group.Id, root, group);
                foreach (HairGrowthMap map in group.maps)
                    if (map != null && map != primary) Add(HairGroomNodeKind.GrowthMap, map.DisplayName, "Paint this map; its values remain independent of the primary Growth / Density map.", HairWorkflowStep.Growth, map.Id, maps, group).Map = map;
                Add(HairGroomNodeKind.Guides, $"2 · Guides ({group.guides.Count:N0})", "Place, generate, select and remove guides. Preview a generated layout before accepting it.", HairWorkflowStep.Guides, group.Id, root, group);
                var grooming = Add(HairGroomNodeKind.Groom, "3 · Grooming", "Sculpt passes evaluate top to bottom, each followed by its modifiers. Select a pass to sculpt; select a modifier to edit it.", HairWorkflowStep.Groom, group.Id, root, group);
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
                var generation = Add(HairGroomNodeKind.Children, "4 · Generate Hair", "Scalp-bound roots -> clump guides -> final cards. Inspect each population independently.", HairWorkflowStep.Cards, group.Id, root, group);
                if (group.generation?.enabled == true)
                {
                    void AddPopulation(HairGenerationStage population, bool final)
                    {
                        if (population == null) return;
                        var item = Add(HairGroomNodeKind.Population, population.name, final ? "Final scalp-bound cards, following the last enabled clump population." :
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
                var cards = Add(HairGroomNodeKind.Cards, "5 · Hair Cards", "Assign shared card/atlas resources and preview the result.", HairWorkflowStep.Cards, group.Id, root, group);
                Add(HairGroomNodeKind.Geometry, "Geometry & Vertex Colors", "Ribbon shape, taper, sampling, root embedding and root-to-tip RGBA.", HairWorkflowStep.Cards, group.Id, cards, group);
                Add(HairGroomNodeKind.Atlas, "Materials & UVs", "Two material passes, shared colors, textures and inline UV-set editing.", HairWorkflowStep.Cards, group.Id, cards, group);
                Add(HairGroomNodeKind.ScalpShading, "Scalp Vertex Shading", "Painted growth drives a vertex-color Mesh Modifier on the existing scalp/body. No extra scalp cap.", HairWorkflowStep.Cards, group.Id, root, group);
            }
            var helpers = Add(HairGroomNodeKind.Helpers, "Shared Helpers", "Create or bind scene helpers. These resources can be referenced by multiple groups.", HairWorkflowStep.Groom);
            foreach (HairHelper helper in groom.SharedHelpers)
                if (helper != null) Add(HairGroomNodeKind.Helper, helper.name, "Shared helper; changes can affect every referencing group.", HairWorkflowStep.Groom, helper.Id, helpers).Helper = helper;
            Add(HairGroomNodeKind.Optimize, "6 · Optimize & LODs", "Preview quality, LOD budgets and release sampling.", HairWorkflowStep.Optimize);
            Add(HairGroomNodeKind.Output, "7 · Validate & Bake", "Validate all output, resolve issues and bake reusable assets.", HairWorkflowStep.ValidateAndBake);
            return nodes;
        }

        internal static bool Toggle(HairCardStage stage, HairGroomNode node, bool enabled)
        {
            if (stage?.Groom == null || node == null || node.Locked || !node.CanToggle || node.Enabled == enabled) return false;
            if (!stage.Groom.Groups.Contains(node.Group) ||
                (node.Layer != null && !node.Group.sculptLayers.Contains(node.Layer)) ||
                (node.Constraint != null && !node.Group.constraints.Contains(node.Constraint))) return false;
            if (node.Modifier != null && node.Population == null) return HairGroomCommands.SetModifierEnabled(stage.Groom, node.Group, node.Layer, node.Modifier, enabled);
            Undo.RecordObject(stage.Groom, "Toggle Hair Node");
            if (node.Kind == HairGroomNodeKind.Group) node.Group.enabled = enabled;
            else if (node.Kind == HairGroomNodeKind.Layer) node.Layer.visible = enabled;
            else if (node.Modifier != null) node.Modifier.enabled = enabled;
            else if (node.Population != null) node.Population.enabled = enabled;
            else node.Constraint.enabled = enabled;
            HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation); return true;
        }

        internal static bool Move(HairCardStage stage, HairGroomNode source, HairGroomNode target, bool after)
        {
            if (stage?.Groom == null || source == null || target == null || !source.CanReorder || source.Locked ||
                target.Locked || source.Kind != target.Kind || source.ParentKey != target.ParentKey || source.Key == target.Key ||
                source.Group != target.Group || !stage.Groom.Groups.Contains(source.Group)) return false;
            bool pass = source.Kind == HairGroomNodeKind.Layer;
            if (source.Population != null) return HairGenerationEditor.MoveRelative(stage, source, target, after);
            int from = pass ? source.Group.sculptLayers.IndexOf(source.Layer) : source.Layer.modifiers.IndexOf(source.Modifier);
            int to = pass ? source.Group.sculptLayers.IndexOf(target.Layer) : source.Layer.modifiers.IndexOf(target.Modifier);
            if (from < 0 || to < 0) return false;
            if (after) to++;
            if (from < to) to--;
            return from != to && HairGroomCommands.EditStack(stage.Groom, source.Group,
                pass ? source.Layer.Id : source.Modifier.Id, pass, to - from);
        }
    }
}
