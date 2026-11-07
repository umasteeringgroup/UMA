using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairCardStage
    {
        [SerializeField] private string activeNodeKey;
        private List<HairGroomNode> nodeCache;
        private HairGroomAsset nodeCacheGroom;
        private int nodeCacheRevision = -1;

        internal IReadOnlyList<HairGroomNode> Nodes
        {
            get
            {
                int revision = groom == null ? 0 : EditorUtility.GetDirtyCount(groom);
                if (nodeCache == null || nodeCacheGroom != groom || nodeCacheRevision != revision)
                { nodeCache = HairGroomNodes.Build(groom); nodeCacheGroom = groom; nodeCacheRevision = revision; }
                return nodeCache;
            }
        }

        internal HairGroomNode SelectedNode
        {
            get
            {
                HairGroomNode node = FindNode(activeNodeKey);
                if (node == null || node.Step != WorkflowStep || (node.Group != null && node.Group != ActiveGroup))
                { activeNodeKey = DefaultNodeKey(); node = FindNode(activeNodeKey); }
                return node;
            }
        }

        internal HairGroomNode FindNode(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (HairGroomNode node in Nodes) if (node.Key == key) return node;
            return null;
        }

        private string DefaultNodeKey() => WorkflowStep switch
        {
            HairWorkflowStep.Growth => HairGroomNodes.Key(HairGroomNodeKind.GrowthMap, ActiveMap?.Id),
            HairWorkflowStep.Guides => HairGroomNodes.Key(HairGroomNodeKind.Guides, ActiveGroup?.Id),
            HairWorkflowStep.Groom => ActiveModifier != null ? HairGroomNodes.Key(HairGroomNodeKind.Modifier, ActiveModifier.Id) :
                HairGroomNodes.Key(HairGroomNodeKind.Groom, ActiveGroup?.Id),
            HairWorkflowStep.Cards => HairGroomNodes.Key(HairGroomNodeKind.Cards, ActiveGroup?.Id),
            HairWorkflowStep.Optimize => HairGroomNodes.Key(HairGroomNodeKind.Optimize),
            HairWorkflowStep.ValidateAndBake => HairGroomNodes.Key(HairGroomNodeKind.Output),
            _ => HairGroomNodes.Key(HairGroomNodeKind.Source)
        };

        internal void SelectNode(string key)
        {
            HairGroomNode node = FindNode(key);
            if (node == null) return;
            // A linked growth node is an explicit shortcut to the real painted map, not a
            // second editable map that silently has no effect on the generated population.
            if (node.Map?.kind == HairMapKind.GrowthArea && node.Group?.generation?.enabled == true &&
                node.Group.generation.cards.source == HairPopulationSource.PaintedScalp &&
                !string.IsNullOrEmpty(node.Group.generation.cards.rootMapGroupId))
            {
                var owner = groom.Groups.Find(g => g.Id == node.Group.generation.cards.rootMapGroupId);
                var map = owner?.FindMap(HairMapKind.GrowthArea);
                if (map != null && owner != node.Group)
                { key = HairGroomNodes.Key(HairGroomNodeKind.GrowthMap, map.Id); node = FindNode(key); if (node == null) return; }
            }
            EndGravitySimulation(); ReleaseSceneInputCapture(true);
            if (populationInspection && node.Population == null && node.Modifier == null) InspectPopulation(null);
            if (node.Kind != HairGroomNodeKind.Layer) ClearLayerEditing();
            if (node.Group != null && node.Group.Id != activeGroupId) SetActiveGroup(node.Group.Id);
            if (WorkflowStep != node.Step) WorkflowStep = node.Step;
            if (node.Map != null) { SetActiveMap(node.Map.Id); SceneTool = HairSceneTool.PaintGrowth; }
            if (node.Kind == HairGroomNodeKind.OptionalMaps || node.Kind == HairGroomNodeKind.Constraints ||
                node.Kind == HairGroomNodeKind.Constraint || node.Kind == HairGroomNodeKind.Helpers ||
                node.Kind == HairGroomNodeKind.Groom || node.Kind == HairGroomNodeKind.LegacyModifiers)
                SceneTool = HairSceneTool.Select;
            if (node.Kind == HairGroomNodeKind.Layer &&
                (SceneTool == HairSceneTool.Select || SceneTool == HairSceneTool.Helper)) SceneTool = HairSceneTool.Comb;
            if (node.Modifier != null && node.Layer != null) SetActiveModifier(node.Layer.Id, node.Modifier.Id);
            else if (node.Kind == HairGroomNodeKind.Layer) SetActiveLayer(node.Layer.Id);
            if (node.Population != null || node.Kind == HairGroomNodeKind.ScalpShading)
            { activeModifierId = node.Modifier?.Id; SceneTool = HairSceneTool.Select; }
            if (node.Helper != null) { SetActiveHelper(node.Helper.Id); SceneTool = HairSceneTool.Helper; if (node.Helper.type == HairHelperType.BraidRail) FormEditAll = true; }
            activeNodeKey = key;
            HairGroomWorkspace.RevealNodeProperties();
            HairGroomWorkspace.RepaintOpenWindows();
        }
    }
}
