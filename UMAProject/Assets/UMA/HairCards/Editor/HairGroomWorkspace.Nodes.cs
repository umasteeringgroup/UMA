using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed partial class HairGroomWorkspace
    {
        [SerializeField] private bool initialPlacementDone;
        private string lastInspectorNode;
        internal static float NodeContentWidth(float windowWidth) => Mathf.Max(260f, windowWidth - 28f);

        internal static void RevealNodeProperties()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<HairGroomWorkspace>()) window.Repaint();
        }

        private void DrawNodeInspector(HairCardStage stage)
        {
            HairGroomNode node = stage.SelectedNode;
            if (lastInspectorNode != node?.Key)
            { lastInspectorNode = node?.Key; detailsScroll = Vector2.zero; atlasEditor?.CancelInteraction(); }
            using (var scroll = new EditorGUILayout.ScrollViewScope(detailsScroll))
            {
                detailsScroll = scroll.scrollPosition;
                if (node == null) { EditorGUILayout.HelpBox("Select a node in Hair Nodes to edit it.", MessageType.Info); return; }
                EditorGUILayout.Space(5f);
                if (node.Group?.generation?.enabled == true && node.Group.generation.cards.source == HairPopulationSource.PaintedScalp &&
                    (node.Kind == HairGroomNodeKind.Guides || node.Kind == HairGroomNodeKind.Groom || node.Kind == HairGroomNodeKind.Layer))
                    EditorGUILayout.HelpBox("Painted Scalp creates procedural strands directly from the map. Edit the generated population's Gather/modifiers and its shared helpers. Guide-only sculpt passes do not drive this source; choose Scalp generation if you want to interpolate combed authored guides.", MessageType.Info);
                EditorGUILayout.LabelField(node.Group == null ? stage.Groom.name : stage.Groom.name + " / " + node.Group.name, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(node.Label, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(node.Description, EditorStyles.wordWrappedLabel);
                if (node.Modifier != null)
                    EditorGUILayout.LabelField(node.Population != null ? "Population: " + node.Population.name : "Owner sculpt pass: " + node.Layer.name, EditorStyles.wordWrappedMiniLabel);
                if (node.Locked) EditorGUILayout.HelpBox("This item is locked. Unlock its group or sculpt pass to edit it.", MessageType.Info);
                if (node.Group != null && (!node.Group.enabled || !node.Group.visible))
                    EditorGUILayout.HelpBox("This group is disabled or hidden. Enable it and show it on the group node to see changes in the preview.", MessageType.Warning);
                EditorGUILayout.Space(5f);
                if (node.Group?.generation?.enabled == true && node.Group.generation.cards.source != HairPopulationSource.Scalp && node.Group.generation.cards.source != HairPopulationSource.PaintedScalp &&
                    (node.Kind == HairGroomNodeKind.Guides || node.Kind == HairGroomNodeKind.Groom || node.Kind == HairGroomNodeKind.Layer || node.Kind == HairGroomNodeKind.GrowthMap))
                    EditorGUILayout.HelpBox("This group generates hair from forms. Edit its Shared Helpers for shape and facing; add modifiers to its generated population. Optional painted root density only removes existing form roots. To create new roots when painting, use Painted Scalp generation.", MessageType.Info);
                switch (node.Kind)
                {
                    case HairGroomNodeKind.Source: DrawSetup(stage); break;
                    case HairGroomNodeKind.Group: DrawGroupNode(stage, node.Group); break;
                    case HairGroomNodeKind.GrowthMap:
                        using (new EditorGUI.DisabledScope(node.Group.locked)) DrawGrowth(stage); break;
                    case HairGroomNodeKind.OptionalMaps:
                        EditorGUILayout.LabelField("Maps", node.Group.maps.Count.ToString());
                        EditorGUILayout.HelpBox("Expand Optional Maps in Hair Nodes and select the map to edit. Use + Map in the tree to add an optional map.", MessageType.Info);
                        break;
                    case HairGroomNodeKind.Guides: DrawGuides(stage); break;
                    case HairGroomNodeKind.Groom:
                        EditorGUILayout.LabelField("Sculpt passes", node.Group.sculptLayers.Count.ToString());
                        EditorGUILayout.HelpBox("Select a sculpt pass beneath Grooming in Hair Nodes to comb or sculpt. Use + Sculpt Pass in the tree to create one. Passes evaluate top to bottom.", MessageType.Info);
                        break;
                    case HairGroomNodeKind.Layer: DrawPassNode(stage, node); DrawGroom(stage); break;
                    case HairGroomNodeKind.Modifier:
                        if (node.Layer != null && (!node.Layer.visible || node.Layer.opacity <= 0f))
                            EditorGUILayout.HelpBox("The owning sculpt pass is hidden or has zero opacity. This modifier will not affect the normal output.", MessageType.Warning);
                        using (new EditorGUI.DisabledScope(node.Locked)) DrawModifier(stage, node.Modifier);
                        break;
                    case HairGroomNodeKind.Children:
                        using (new EditorGUI.DisabledScope(node.Locked))
                        {
                            HairGenerationEditor.DrawOverview(stage, node.Group);
                            if (!node.Group.generation.enabled) DrawChildren(stage);
                        }
                        break;
                    case HairGroomNodeKind.Population:
                        using (new EditorGUI.DisabledScope(node.Locked)) HairGenerationEditor.DrawPopulation(stage, node); break;
                    case HairGroomNodeKind.ScalpShading:
                        using (new EditorGUI.DisabledScope(node.Locked)) HairGenerationEditor.DrawScalp(stage, node.Group); break;
                    case HairGroomNodeKind.Cards:
                    case HairGroomNodeKind.Geometry:
                    case HairGroomNodeKind.Atlas:
                        using (new EditorGUI.DisabledScope(node.Locked)) DrawCards(stage, node.Kind); break;
                    case HairGroomNodeKind.Helpers:
                        EditorGUILayout.LabelField("Shared helpers", stage.Groom.SharedHelpers.Count.ToString());
                        EditorGUILayout.HelpBox("Select a helper beneath Shared Helpers in Hair Nodes to edit it. Create or bind helpers using the tree's helper controls.", MessageType.Info);
                        break;
                    case HairGroomNodeKind.Helper: DrawHelperNode(stage, node.Helper); break;
                    case HairGroomNodeKind.Constraints:
                        EditorGUILayout.LabelField("Constraints", node.Group.constraints.Count.ToString());
                        EditorGUILayout.HelpBox("Select a constraint beneath this branch in Hair Nodes to edit it. Use + Constraint in the tree to choose a shared helper.", MessageType.Info);
                        break;
                    case HairGroomNodeKind.Constraint: DrawConstraintNode(stage, node); break;
                    case HairGroomNodeKind.Optimize: DrawOptimize(stage); break;
                    case HairGroomNodeKind.Output: DrawValidateAndBake(stage); break;
                    case HairGroomNodeKind.LegacyModifiers:
                        EditorGUILayout.HelpBox("Use Organize into sculpt pass in Hair Nodes to move these modifiers into an editable pass without changing their result.", MessageType.Info);
                        break;
                }
            }
        }

        private static void DrawGroupNode(HairCardStage stage, HairGroup group)
        {
            using (new EditorGUI.DisabledScope(group.locked))
            {
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField("Name", group.name);
                var role = (HairGroupRole)EditorGUILayout.EnumPopup("Role", group.role);
                Color color = EditorGUILayout.ColorField("Display color", group.color);
                bool enabled = EditorGUILayout.Toggle("Active (preview & output)", group.enabled);
                bool visible = EditorGUILayout.Toggle("Show in preview", group.visible);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(stage.Groom, "Edit Hair Group"); group.name = name; group.role = role; group.color = color; group.enabled = enabled; group.visible = visible; HairGroomCommands.Commit(stage.Groom); }
            }
            bool locked = EditorGUILayout.Toggle("Lock group", group.locked);
            if (locked != group.locked) { Undo.RecordObject(stage.Groom, "Lock Hair Group"); group.locked = locked; HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); }
            EditorGUILayout.LabelField($"{group.guides.Count:N0} guides · {group.sculptLayers.Count:N0} sculpt passes", EditorStyles.miniLabel);
        }

        private static void DrawPassNode(HairCardStage stage, HairGroomNode node)
        {
            HairSculptLayer layer = node.Layer;
            using (new EditorGUI.DisabledScope(node.Group.locked))
            {
                bool locked = EditorGUILayout.Toggle("Lock sculpt pass", layer.locked);
                if (locked != layer.locked) { Undo.RecordObject(stage.Groom, "Lock Hair Sculpt Pass"); layer.locked = locked; HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); }
                using (new EditorGUI.DisabledScope(layer.locked))
                {
                    EditorGUI.BeginChangeCheck();
                    bool active = EditorGUILayout.Toggle("Active", layer.visible);
                    string name = EditorGUILayout.DelayedTextField("Name", layer.name);
                    float opacity = EditorGUILayout.Slider("Opacity", layer.opacity, 0f, 1f);
                    var blend = (HairSculptBlendMode)EditorGUILayout.EnumPopup("Sculpt blend", layer.blendMode);
                    if (EditorGUI.EndChangeCheck())
                    { Undo.RecordObject(stage.Groom, "Edit Hair Sculpt Pass"); layer.visible = active; layer.name = name; layer.opacity = opacity; layer.blendMode = blend; HairGroomCommands.Commit(stage.Groom); }
                }
            }
            bool solo = stage.SoloLayerId == layer.Id;
            if (EditorGUILayout.Toggle("Solo (preview only)", solo) != solo) stage.SoloLayer(layer.Id);
        }

        private static void DrawHelperNode(HairCardStage stage, HairHelper helper)
        {
            bool locked = EditorGUILayout.Toggle("Lock helper", helper.locked);
            if (locked != helper.locked) { Undo.RecordObject(stage.Groom, "Lock Hair Helper"); helper.locked = locked; HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Display); }
            using (new EditorGUI.DisabledScope(helper.locked))
            {
                if (helper.type == HairHelperType.Gather || helper.type == HairHelperType.Bun)
                {
                    HairGatherEditor.Helper(stage, helper); return;
                }
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField("Name", helper.name);
                bool visible = EditorGUILayout.Toggle("Visible", helper.visible);
                float radius = HairFormEditor.IsForm(helper) ? helper.radius : EditorGUILayout.FloatField("Radius", helper.radius);
                Vector3 size = HairFormEditor.IsForm(helper) ? helper.size : EditorGUILayout.Vector3Field("Size", helper.size);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(stage.Groom, "Edit Hair Helper"); helper.name = name; helper.visible = visible; helper.radius = radius; helper.size = size; HairGroomCommands.Commit(stage.Groom); }
            }
            HairFormEditor.DrawHelperProperties(stage, helper);
            EditorGUILayout.HelpBox(helper.embedded ? "Move this helper with its Scene gizmo. Embedded helpers are saved with the groom." :
                "Move/rotate/scale the linked source scene object to edit this helper. Its source-relative transform is saved for bake/runtime, including scaled or mirrored parents. The stage gizmo is display-only for linked helpers.", MessageType.Info);
        }

        private static void DrawConstraintNode(HairCardStage stage, HairGroomNode node)
        {
            HairConstraintSettings constraint = node.Constraint;
            using (new EditorGUI.DisabledScope(node.Locked))
            {
                EditorGUI.BeginChangeCheck();
                bool enabled = EditorGUILayout.Toggle("Active", constraint.enabled);
                string name = EditorGUILayout.DelayedTextField("Name", constraint.name);
                float weight = EditorGUILayout.Slider("Weight", constraint.weight, 0f, 1f);
                var type = (HairConstraintType)EditorGUILayout.EnumPopup("Type", constraint.type);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(stage.Groom, "Edit Hair Constraint"); constraint.name = name; constraint.enabled = enabled; constraint.weight = weight; constraint.type = type; HairGroomCommands.Commit(stage.Groom); }
                string helperId = DrawHelperBinding(stage, constraint.helperId);
                if (helperId != constraint.helperId)
                { Undo.RecordObject(stage.Groom, "Bind Hair Constraint"); constraint.helperId = helperId; HairGroomCommands.Commit(stage.Groom); }
            }
        }

        private static string DrawHelperBinding(HairCardStage stage, string helperId)
        {
            var ids = new System.Collections.Generic.List<string> { string.Empty };
            var labels = new System.Collections.Generic.List<string> { "None" };
            foreach (var helper in stage.Groom.SharedHelpers)
                if (helper != null) { ids.Add(helper.Id); labels.Add(helper.name); }
            if (!string.IsNullOrEmpty(helperId) && !ids.Contains(helperId))
            { ids.Add(helperId); labels.Add("Missing helper (choose a replacement)"); }
            int current = Mathf.Max(0, ids.IndexOf(helperId ?? string.Empty));
            int selected = EditorGUILayout.Popup("Helper", current, labels.ToArray());
            return selected == current ? helperId : ids[selected];
        }
    }
}
