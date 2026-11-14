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
                if (node == null) { EditorGUILayout.HelpBox("Select an item in the Groom Outliner to edit it.", MessageType.Info); return; }
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
                node.Handler.DrawProperties(this, stage, node);
            }
        }

        /// <summary>Edit This Layer / Return to Final Preview, and a finishing layer, for the
        /// grooming branch. Editing state is not node management, so it lives with the pass.</summary>
        internal static void DrawLayerEditingControls(HairCardStage stage, HairGroomNode node)
        {
            HairGroup group = node.Group;
            EditorGUILayout.HelpBox(stage.SculptEditingStatus, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (stage.IsLayerEditing)
                {
                    if (GUILayout.Button("Return to Final Preview")) stage.ReturnToFinalPreview();
                }
                else
                    using (new EditorGUI.DisabledScope(!stage.CanEditLayer || node.Kind != HairGroomNodeKind.Layer))
                        if (GUILayout.Button(new GUIContent("Edit This Layer", "Edit before this layer's modifiers, temporarily bypassing all later operations."))) stage.BeginLayerEditing();
                using (new EditorGUI.DisabledScope(!HairGroomNodeCommands.CanAddFinishingLayer(stage, group)))
                    if (GUILayout.Button(new GUIContent("Add Finishing Sculpt Layer", "Add an additive layer after the complete guide stack, including helper constraints. Nothing is baked or overwritten.")))
                    { HairGroomNodeCommands.AddFinishingSculptLayer(stage, group); GUIUtility.ExitGUI(); }
            }
            EditorGUILayout.Space(4f);
        }

        /// <summary>Population and mask inspection previews for generation items.</summary>
        internal static void DrawInspectionControls(HairCardStage stage, HairGroomNode node)
        {
            bool inspectionActive = !string.IsNullOrEmpty(stage.InspectedPopulationId) || !string.IsNullOrEmpty(stage.InspectedMaskId);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (node.Population != null && node.Modifier == null)
                {
                    bool inspecting = stage.InspectedPopulationId == node.Population.Id && string.IsNullOrEmpty(stage.InspectedMaskId);
                    if (GUILayout.Toggle(inspecting, new GUIContent("Inspect Population", "Isolate this population, colored by parent clump."), EditorStyles.miniButton) != inspecting)
                        stage.InspectPopulation(inspecting ? null : node.Population.Id);
                }
                if (node.Modifier != null)
                {
                    bool showing = stage.InspectedMaskId == node.Modifier.Id;
                    if (GUILayout.Toggle(showing, new GUIContent("Show Mask", "Preview where this modifier applies."), EditorStyles.miniButton) != showing)
                        stage.InspectPopulation(node.Population?.Id, showing ? null : node.Modifier.Id);
                }
                using (new EditorGUI.DisabledScope(!inspectionActive))
                    if (GUILayout.Button(new GUIContent("Final Hair", "Return to the complete output, all downstream stages included."), EditorStyles.miniButton))
                        stage.InspectPopulation(null);
            }
            EditorGUILayout.LabelField(inspectionActive ? "Inspection preview active · not the baked output" : "Final hair · all downstream stages included",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        /// <summary>One-click generation presets for a group's Generate Hair branch.</summary>
        internal static void DrawGenerationPresets(HairCardStage stage, HairGroomNode node)
        {
            using (new EditorGUI.DisabledScope(node.Locked))
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Presets", EditorStyles.miniBoldLabel, GUILayout.Width(52f));
                if (GUILayout.Button(new GUIContent("Short Hair Part…", "Short, softly clumped ribbons: up to 2,500 cards and four segments per card. Preserve a part with separate groups or Part Regions."), EditorStyles.miniButtonLeft))
                    HairGenerationEditor.ConfirmPreset(stage, node, HairGenerationEditor.Preset.ShortHairPart);
                if (GUILayout.Button(new GUIContent("Curly Volume…", "Set up surface-rooted ringlets with automatic curl resolution. Preserves guides, sculpt passes, assigned textures and materials."), EditorStyles.miniButtonMid))
                    HairGenerationEditor.ConfirmPreset(stage, node, HairGenerationEditor.Preset.CurlyVolume);
                if (GUILayout.Button(new GUIContent("Swept Clumps…", "Configure generated clumps, hairline, curved ribbons and LOD sampling. Existing guides/sculpting and assigned textures/materials are preserved."), EditorStyles.miniButtonRight))
                    HairGenerationEditor.ConfirmPreset(stage, node, HairGenerationEditor.Preset.SweptClumps);
            }
            EditorGUILayout.Space(4f);
        }

        internal static void DrawGroupNode(HairCardStage stage, HairGroup group)
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

        internal static void DrawPassNode(HairCardStage stage, HairGroomNode node)
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

        internal static void DrawHelperNode(HairCardStage stage, HairHelper helper)
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
            if (HairFormEditor.IsForm(helper)) HairFormEditor.DrawHelperActions(stage, helper);
            EditorGUILayout.HelpBox(helper.embedded ? "Move this helper with its Scene gizmo. Embedded helpers are saved with the groom." :
                "Move/rotate/scale the linked source scene object to edit this helper. Its source-relative transform is saved for bake/runtime, including scaled or mirrored parents. The stage gizmo is display-only for linked helpers.", MessageType.Info);
        }

        internal static void DrawConstraintNode(HairCardStage stage, HairGroomNode node)
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
