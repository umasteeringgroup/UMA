using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    /// <summary>
    /// ID references between authored items (masks, helpers, shared growth maps) shown as
    /// outliner badges and as "Used by" lists, so links are visible without a node graph.
    /// </summary>
    internal static class HairGroomNodeReferences
    {
        internal readonly struct Usage
        {
            internal readonly string NodeKey, Label, Relation;
            internal Usage(string nodeKey, string label, string relation) { NodeKey = nodeKey; Label = label; Relation = relation; }
        }

        /// <summary>Short labels for the references this node makes.</summary>
        internal static void AppendBadges(HairGroomAsset groom, HairGroomNode node, List<string> badges)
        {
            if (groom == null || node == null) return;
            switch (node.Kind)
            {
                case HairGroomNodeKind.Layer:
                    if (node.Layer.afterGroupOperations) badges.Add("finishing");
                    AddMask(node.Group, node.Layer.maskMapId, badges);
                    break;
                case HairGroomNodeKind.Modifier:
                    AddMask(node.Group, node.Modifier.maskMapId, badges);
                    AddHelper(groom, node.Modifier.helperId, badges);
                    if (node.Modifier.gather != null && !string.IsNullOrEmpty(node.Modifier.gather.continuationHelperId))
                        AddHelper(groom, node.Modifier.gather.continuationHelperId, badges, "continues");
                    break;
                case HairGroomNodeKind.Constraint:
                    AddHelper(groom, node.Constraint.helperId, badges);
                    AddMask(node.Group, node.Constraint.maskMapId, badges);
                    break;
                case HairGroomNodeKind.Population:
                    var population = node.Population;
                    if (population.source == HairPopulationSource.PaintedScalp && !string.IsNullOrEmpty(population.rootMapGroupId) &&
                        population.rootMapGroupId != node.Group.Id)
                        badges.Add("growth: " + (groom.Groups.Find(g => g?.Id == population.rootMapGroupId)?.name ?? "missing group"));
                    if (population.form != null && population.form.helperIds.Count > 0)
                        badges.Add(population.form.helperIds.Count == 1 ? "1 form" : population.form.helperIds.Count + " forms");
                    break;
                case HairGroomNodeKind.Helper:
                    int uses = CountHelperUsages(groom, node.Helper.Id);
                    badges.Add(uses == 0 ? "unused" : uses == 1 ? "used by 1" : "used by " + uses);
                    break;
            }
        }

        private static void AddMask(HairGroup group, string mapId, List<string> badges)
        {
            if (string.IsNullOrEmpty(mapId) || group == null) return;
            var map = group.maps.Find(m => m != null && m.Id == mapId);
            badges.Add("mask: " + (map != null ? map.DisplayName : "missing map"));
        }

        private static void AddHelper(HairGroomAsset groom, string helperId, List<string> badges, string relation = "helper")
        {
            if (string.IsNullOrEmpty(helperId)) return;
            badges.Add(relation + ": " + (groom.FindHelper(helperId)?.name ?? "missing"));
        }

        internal static int CountHelperUsages(HairGroomAsset groom, string helperId)
        {
            var usages = new List<Usage>();
            CollectHelperUsages(groom, helperId, usages);
            return usages.Count;
        }

        internal static void CollectHelperUsages(HairGroomAsset groom, string helperId, List<Usage> usages)
        {
            if (groom == null || string.IsNullOrEmpty(helperId)) return;
            foreach (var group in groom.Groups)
            {
                if (group == null) continue;
                foreach (var constraint in group.constraints)
                    if (constraint != null && constraint.helperId == helperId)
                        usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Constraint, constraint.Id), group.name + " / " + constraint.name, "constraint"));
                foreach (var layer in group.sculptLayers)
                    if (layer != null) CollectModifiers(group, layer.modifiers, helperId, usages, group.name + " / " + layer.name);
                if (group.modifiers.Count > 0) CollectModifiers(group, group.modifiers, helperId, usages, group.name + " / Unorganized", false);
                var generation = group.generation;
                if (generation == null) continue;
                foreach (var population in generation.clumps) CollectPopulation(group, population, helperId, usages);
                CollectPopulation(group, generation.cards, helperId, usages);
            }
            foreach (var helper in groom.SharedHelpers)
                if (helper?.bun != null && helper.bun.gatherHelperId == helperId)
                    usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id), helper.name, "bun gather ring"));
        }

        private static void CollectPopulation(HairGroup group, HairGenerationStage population, string helperId, List<Usage> usages)
        {
            if (population == null) return;
            if (population.form != null && population.form.helperIds.Contains(helperId))
                usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Population, population.Id), group.name + " / " + population.name, "form"));
            CollectModifiers(group, population.modifiers, helperId, usages, group.name + " / " + population.name);
        }

        private static void CollectModifiers(HairGroup group, List<HairModifierSettings> modifiers, string helperId,
            List<Usage> usages, string owner, bool hasNode = true)
        {
            foreach (var modifier in modifiers)
            {
                if (modifier == null) continue;
                bool direct = modifier.helperId == helperId;
                bool continuation = modifier.gather != null && modifier.gather.continuationHelperId == helperId;
                if (!direct && !continuation) continue;
                string key = hasNode ? HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id) : HairGroomNodes.Key(HairGroomNodeKind.LegacyModifiers, group.Id);
                usages.Add(new Usage(key, owner + " / " + modifier.name, direct ? "modifier" : "gather continuation"));
            }
        }

        internal static void CollectMapUsages(HairGroomAsset groom, HairGroup owner, HairGrowthMap map, List<Usage> usages)
        {
            if (groom == null || owner == null || map == null) return;
            foreach (var layer in owner.sculptLayers)
            {
                if (layer == null) continue;
                if (layer.maskMapId == map.Id)
                    usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id), layer.name, "pass mask"));
                foreach (var modifier in layer.modifiers)
                    if (modifier != null && modifier.maskMapId == map.Id)
                        usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id), layer.name + " / " + modifier.name, "mask"));
            }
            foreach (var constraint in owner.constraints)
                if (constraint != null && constraint.maskMapId == map.Id)
                    usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Constraint, constraint.Id), constraint.name, "mask"));
            if (owner.generation != null)
            {
                var populations = new List<HairGenerationStage>(owner.generation.clumps) { owner.generation.cards };
                foreach (var population in populations)
                    if (population != null)
                        foreach (var modifier in population.modifiers)
                            if (modifier != null && modifier.maskMapId == map.Id)
                                usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id), population.name + " / " + modifier.name, "mask"));
            }
            if (map.kind != HairMapKind.GrowthArea) return;
            foreach (var group in groom.Groups)
            {
                var cards = group?.generation?.cards;
                if (group == null || group == owner || group.generation?.enabled != true || cards == null) continue;
                if (cards.source == HairPopulationSource.PaintedScalp && cards.rootMapGroupId == owner.Id)
                    usages.Add(new Usage(HairGroomNodes.Key(HairGroomNodeKind.Population, cards.Id), group.name + " / " + cards.name, "shared growth"));
            }
        }

        /// <summary>Properties section listing what references a helper or map, with jump buttons.</summary>
        internal static void DrawUsedBy(HairCardStage stage, HairGroomNode node)
        {
            if (stage?.Groom == null || node == null) return;
            var usages = new List<Usage>();
            if (node.Helper != null) CollectHelperUsages(stage.Groom, node.Helper.Id, usages);
            else if (node.Map != null) CollectMapUsages(stage.Groom, node.Group, node.Map, usages);
            else return;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Used By", EditorStyles.boldLabel);
            if (usages.Count == 0)
            {
                EditorGUILayout.LabelField(node.Helper != null ? "Nothing references this helper yet." : "No masks or groups reference this map.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            foreach (var usage in usages)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(usage.Label, usage.Relation), EditorStyles.wordWrappedLabel);
                    GUILayout.Label(usage.Relation, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    using (new EditorGUI.DisabledScope(stage.FindNode(usage.NodeKey) == null))
                        if (GUILayout.Button(new GUIContent("Select", "Select this item in the Groom Outliner."), EditorStyles.miniButton, GUILayout.Width(52f)))
                        { GUI.FocusControl(null); stage.SelectNode(usage.NodeKey); GUIUtility.ExitGUI(); }
                }
        }
    }
}
