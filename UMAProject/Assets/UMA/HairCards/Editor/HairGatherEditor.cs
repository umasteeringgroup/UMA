using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static class HairGatherEditor
    {
        private static void Field(SerializedProperty p, string name, string label, string tip = null)
            => EditorGUILayout.PropertyField(p.FindPropertyRelative(name), new GUIContent(label, tip), true);
        internal static void Modifier(HairCardStage stage, SerializedProperty property)
        {
            var p = property.FindPropertyRelative("gather");
            EditorGUILayout.HelpBox("Anchored roots → scalp-following paths → gather ring. The ring's local Y arrow is the arrival/exit direction. Preserve never silently lengthens hair; choose Extend To Reach deliberately for procedural updos.", MessageType.Info);
            HelperPopup(stage, property.FindPropertyRelative("helperId"), "Gather target", HairHelperType.Gather);
            Field(p, "mode", "Gather mode"); Field(p, "length", "Length behavior");
            Field(p, "rootSide", "Roots on symmetry side", "Both, or the positive/negative side of the groom's source-local symmetry plane. Use two Gather modifiers with opposite sides for pigtails.");
            Field(p, "tension", "Tension", "Taut scalp-following hair versus relaxed lift above the scalp.");
            Field(p, "liftOff", "Lift-off along scalp path", "Fraction of the surface route before hair leaves the scalp for the ring. Larger = closer to the scalp for longer.");
            Field(p, "surfaceOffset", "Surface offset (m)");
            var bend = p.FindPropertyRelative("rootBend"); bend.isExpanded = EditorGUILayout.Foldout(bend.isExpanded, "Root & arrival bends (S-curve)", true);
            if (bend.isExpanded)
            {
                Field(p, "startInMeters", "Measure bend start in meters"); Field(p, "bendStart", "Protected root reach");
                Field(p, "flexibility", "Flexibility", "How strongly the gather can bend stiff controls. Fully frozen controls always remain anchored.");
                Field(p, "rootLift", "Root bend height (m)"); Field(p, "rootBend", "Root bend profile");
                Field(p, "approachLength", "Arrival tangent reach (m)"); Field(p, "arrivalLift", "Arrival bend height (m)");
                Field(p, "arrivalBend", "Arrival bend profile");
                EditorGUILayout.LabelField("The two bend profiles are independent. Their endpoint envelopes keep the root and tie fixed; clearance limits inward bends.", EditorStyles.wordWrappedMiniLabel);
            }
            if ((HairGatherMode)p.FindPropertyRelative("mode").enumValueIndex == HairGatherMode.PassThrough)
            {
                Field(p, "passFraction", "Tie position along strand");
                HelperPopup(stage, p.FindPropertyRelative("continuationHelperId"), "Tail continuation", HairHelperType.CurveRail);
                EditorGUILayout.LabelField("The continuation is translated to start at each packed tie position. Without a rail, the tail continues along the ring's Y arrow.", EditorStyles.wordWrappedMiniLabel);
            }
            var advanced = p.FindPropertyRelative("followScalp"); advanced.isExpanded = EditorGUILayout.Foldout(advanced.isExpanded, "Surface, facing & variation", true);
            if (advanced.isExpanded)
            {
                Field(p, "followScalp", "Follow connected scalp"); Field(p, "outwardFacing", "Face cards outward");
                Field(p, "offsetAlongStrand", "Offset along strand");
                Field(p, "fullCardClearance", "Prevent card-edge penetration");
                Field(p, "clearance", "Minimum card clearance (m)");
                Field(p, "twist", "Root → tie twist (degrees)"); Field(p, "variation", "Natural bend variation");
            }
        }

        internal static void PaintedPopulation(HairCardStage stage, SerializedProperty property)
        {
            EditorGUILayout.HelpBox("Roots come directly from the painted scalp—no authored guides or grids are required. Add Gather to shape them. Expanding Growth / Density creates candidates in the new area.", MessageType.Info);
            var ids = new List<string> { string.Empty }; var labels = new List<string> { "This group's Growth / Density" };
            foreach (var group in stage.Groom.Groups) if (group != stage.ActiveGroup) { ids.Add(group.Id); labels.Add(group.name); }
            var reference = property.FindPropertyRelative("rootMapGroupId"); string current = reference.stringValue ?? string.Empty;
            if (!ids.Contains(current)) { ids.Add(current); labels.Add("Missing group (choose replacement)"); }
            reference.stringValue = ids[EditorGUILayout.Popup("Growth map group", ids.IndexOf(current), labels.ToArray())];
            if (!string.IsNullOrEmpty(reference.stringValue)) EditorGUILayout.HelpBox("Paint the referenced group's map. This population uses it directly; this group's own Growth map is not a second mask.", MessageType.Info);
            Field(property, "minimumSpacing", "Root spacing (m)"); Field(property, "uniformity", "Root uniformity");
            Field(property, "rootLength", "Initial strand length (m)"); Field(property, "shapeSamples", "Shape points per strand");
            Field(property, "lengthVariation", "Initial length variation"); Field(property, "widthVariation", "Width variation");
            Field(property.FindPropertyRelative("form"), "preserveLodCoverage", "Preserve coverage at LODs", "Widen surviving cards by up to 2x as density decreases. Roots, centerlines and gather slots stay fixed. Useful for close-fitting coverage layers.");
            Field(property, "seed", "Seed");
            EditorGUILayout.LabelField("Card tessellation is controlled by Geometry & Vertex Colors / LOD. Shape points control bend accuracy, independently of rendered LOD.", EditorStyles.wordWrappedMiniLabel);
        }

        internal static void HelperPopup(HairCardStage stage, SerializedProperty property, string label, HairHelperType type)
        {
            var ids = new List<string> { string.Empty }; var labels = new List<string> { "None" };
            foreach (var helper in stage.Groom.SharedHelpers) if (helper.type == type) { ids.Add(helper.Id); labels.Add(helper.name); }
            string current = property.stringValue ?? string.Empty;
            if (!ids.Contains(current)) { ids.Add(current); labels.Add("Missing/wrong helper (choose replacement)"); }
            property.stringValue = ids[EditorGUILayout.Popup(label, ids.IndexOf(current), labels.ToArray())];
        }

        internal static void Helper(HairCardStage stage, HairHelper helper)
        {
            using var serialized = new SerializedObject(stage.Groom);
            var property = serialized.FindProperty("sharedHelpers").GetArrayElementAtIndex(stage.Groom.SharedHelpers.IndexOf(helper));
            Field(property, "name", "Name"); Field(property, "visible", "Visible");
            bool linkedBun = helper.type == HairHelperType.Bun && stage.Groom.FindHelper(helper.bun.gatherHelperId)?.type == HairHelperType.Gather;
            using (new EditorGUI.DisabledScope(linkedBun)) Field(property, "position", "Source-local position", "Linked buns use Offset from gather instead.");
            EditorGUI.BeginChangeCheck(); Vector3 angles = EditorGUILayout.Vector3Field(linkedBun ? "Relative rotation (degrees)" : "Rotation (degrees)", helper.rotation.eulerAngles);
            if (EditorGUI.EndChangeCheck()) property.FindPropertyRelative("rotation").quaternionValue = Quaternion.Euler(angles);
            if (helper.type == HairHelperType.Gather)
            {
                var p = property.FindPropertyRelative("gather");
                Field(p, "radii", "Bundle radii X / Z (m)"); Field(p, "spacing", "Minimum endpoint spacing (m)"); Field(p, "scatter", "Endpoint variation");
                EditorGUILayout.HelpBox("Move/rotate this ring in Scene. The Y arrow sets the incoming/outgoing direction. Orange warnings mean the ring cannot accommodate the requested spacing or some strands cannot reach it.", MessageType.Info);
            }
            else
            {
                var p = property.FindPropertyRelative("bun");
                HelperPopup(stage, p.FindPropertyRelative("gatherHelperId"), "Follow gather target", HairHelperType.Gather);
                if (!string.IsNullOrEmpty(p.FindPropertyRelative("gatherHelperId").stringValue)) Field(p, "gatherOffset", "Offset from gather (m)");
                Field(p, "radius", "Bun ring radius (m)"); Field(p, "tubeRadius", "Wrapped volume thickness (m)"); Field(p, "height", "Bun height (m)");
                Field(p, "tuck", "Center tuck"); Field(p, "sweep", "Wrap sweep (degrees)"); Field(p, "irregularity", "Surface irregularity (m)");
                bool separateBraid = stage.Groom.SharedHelpers.Exists(h => HairRailUtility.IsRail(h) && h.rail.parentHelperId == helper.Id);
                if (separateBraid) EditorGUILayout.HelpBox("A separate braid spline follows this bun. Select 'Braid spline' under Shared Helpers to move or reshape the braid independently. The procedural braid preset below does not reshape that spline.", MessageType.Info);
                var braidPreset = p.FindPropertyRelative("braidRadius");
                if (!separateBraid || (braidPreset.isExpanded = EditorGUILayout.Foldout(braidPreset.isExpanded, "Procedural braid preset (not the editable spline)", true)))
                { Field(p, "braidRadius", "Surrounding braid radius (m)"); Field(p, "braidHeight", "Braid height (m)"); Field(p, "braidOverlap", "Braid overlap (turn fraction)"); }
                Field(p, "sectors", "Wrap sectors");
                EditorGUILayout.HelpBox("One Bun helper can drive separate Wrapped Volume, Center Tuck, and Surrounding Braid populations. Count, material, width and segment budgets remain independent on those populations. When linked, position comes from the gather plus offset; rotation is relative to the gather.", MessageType.Info);
            }
            if (serialized.ApplyModifiedProperties()) HairGroomCommands.Commit(stage.Groom);
        }

        internal static HairHelper Mirror(HairGroomAsset groom, HairHelper helper)
        {
            Undo.RecordObject(groom, "Mirror Gather / Bun helper");
            var matrix = helper.type == HairHelperType.Bun ? HairBunUtility.Matrix(groom, helper) : helper.LocalToSource;
            Vector3 normal = groom.SymmetryPlaneNormal.normalized;
            Vector3 Reflect(Vector3 v) => v - normal * (2 * Vector3.Dot(v, normal));
            Vector3 position = groom.SymmetryPlanePoint + Reflect(matrix.MultiplyPoint3x4(Vector3.zero) - groom.SymmetryPlanePoint);
            var copy = HairGroomCommands.AddHelper(groom, helper.type, position);
            copy.name = helper.name + " Mirrored";
            copy.rotation = Quaternion.LookRotation(Reflect(matrix.MultiplyVector(Vector3.forward)), Reflect(matrix.MultiplyVector(Vector3.up)));
            copy.scale = helper.scale;
            copy.gather = JsonUtility.FromJson<HairGatherTarget>(JsonUtility.ToJson(helper.gather));
            copy.bun = JsonUtility.FromJson<HairBunSettings>(JsonUtility.ToJson(helper.bun));
            copy.bun.gatherHelperId = null; copy.EnsureIntegrity(); HairGroomCommands.Commit(groom);
            return copy;
        }

        internal static HairModifierSettings AddGather(HairCardStage stage, HairGroomNode node)
        {
            if (node?.Group == null || node.Locked || (node.Layer == null && node.Population == null)) return null;
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add Gather setup");
            Vector3 center = stage.Groom.SourceMesh != null ? stage.Groom.SourceMesh.bounds.center + Vector3.up * stage.Groom.SourceMesh.bounds.extents.y : Vector3.up;
            var helper = HairGroomCommands.AddHelper(stage.Groom, HairHelperType.Gather, center + Vector3.up * .025f);
            Undo.RecordObject(stage.Groom, "Add Gather modifier");
            var modifier = new HairModifierSettings { name = "Scalp Gather", type = HairModifierType.Gather, helperId = helper.Id, rootInfluence = 1 };
            modifier.EnsureIntegrity();
            (node.Population != null ? node.Population.modifiers : node.Layer.modifiers).Add(modifier);
            HairGroomCommands.Commit(stage.Groom); Undo.CollapseUndoOperations(undo);
            stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
            return modifier;
        }
    }

    public sealed partial class HairCardStage
    {
        private readonly Vector3[] gatherRing = new Vector3[65];
        private void DrawGatherBunHelper(HairHelper helper, bool edit)
        {
            Matrix4x4 local = helper.type == HairHelperType.Bun ? HairBunUtility.Matrix(groom, helper) : helper.LocalToSource;
            Vector3 center = local.MultiplyPoint3x4(Vector3.zero);
            var color = Handles.color;
            if (helper.type == HairHelperType.Gather)
            {
                for (int i = 0; i < gatherRing.Length; i++)
                {
                    float a = i / 64f * Mathf.PI * 2;
                    gatherRing[i] = local.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * helper.gather.radii.x, 0, Mathf.Sin(a) * helper.gather.radii.y));
                }
                Handles.DrawAAPolyLine(3, gatherRing);
                Vector3 end = local.MultiplyPoint3x4(Vector3.up * .04f);
                Handles.DrawLine(center, end, 2);
                Handles.ConeHandleCap(0, end, Quaternion.LookRotation(end - center), LocalHandleSize(center) * .025f, EventType.Repaint);
                if (edit && Evaluation != null)
                {
                    int shown = 0;
                    foreach (var curve in Evaluation.curves)
                    {
                        var owner = groom.Groups.Find(g => g.Id == curve.groupId);
                        if (owner == null) continue;
                        HairModifierSettings modifier = owner.generation.cards.modifiers.Find(m => m.enabled && m.type == HairModifierType.Gather && m.helperId == helper.Id);
                        if (modifier == null) foreach (var layer in owner.sculptLayers)
                            if (layer.visible && modifier == null) modifier = layer.modifiers.Find(m => m.enabled && m.type == HairModifierType.Gather && m.helperId == helper.Id);
                        if (modifier == null || curve.points.Count < 2 || shown++ > 180) continue;
                        int tie = modifier.gather.mode == HairGatherMode.PassThrough ? Mathf.RoundToInt(modifier.gather.passFraction * (curve.points.Count - 1)) : curve.points.Count - 1;
                        Vector3 tip = curve.points[tie].position;
                        Vector3 slot = local.inverse.MultiplyPoint3x4(tip);
                        bool missed = Mathf.Abs(slot.y) > .004f || Mathf.Abs(slot.x) > helper.gather.radii.x + .004f || Mathf.Abs(slot.z) > helper.gather.radii.y + .004f;
                        Handles.color = missed ? new Color(1, .55f, .1f, .8f) : new Color(.1f, .9f, .65f, .35f);
                        float reach = modifier.gather.startInMeters ? modifier.gather.bendStart : modifier.gather.bendStart * curve.Length;
                        int protectedEnd = 0; float traveled = 0;
                        while (protectedEnd < curve.points.Count - 2 && traveled + Vector3.Distance(curve.points[protectedEnd].position, curve.points[protectedEnd + 1].position) <= reach)
                        { traveled += Vector3.Distance(curve.points[protectedEnd].position, curve.points[protectedEnd + 1].position); protectedEnd++; }
                        for (int i = 1; i < curve.points.Count; i++)
                            Handles.DrawLine(curve.points[i - 1].position, curve.points[i].position, i <= protectedEnd ? 3 : 1);
                    }
                }
            }
            else
            {
                for (int sector = 0; sector < 8; sector++)
                {
                    for (int i = 0; i < gatherRing.Length; i++)
                        gatherRing[i] = local.MultiplyPoint3x4(HairBunUtility.Point(helper.bun, sector * Mathf.PI * .25f, i / 64f, false));
                    Handles.DrawAAPolyLine(2, gatherRing);
                }
            }
            Handles.color = color;
            Handles.Label(center, helper.name + (helper.type == HairHelperType.Gather ? " • Gather / Y exit" : " • Bun"));
            float size = LocalHandleSize(center) * .04f;
            if (Handles.Button(center, Quaternion.identity, size, size, Handles.RectangleHandleCap)) SetActiveHelper(helper.Id);
            if (!edit || helper.locked || !helper.embedded) return;
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(center, local.rotation);
            Quaternion rotation = Handles.RotationHandle(local.rotation, center);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(groom, "Move Hair Gather / Bun");
                var parent = helper.type == HairHelperType.Bun ? groom.FindHelper(helper.bun.gatherHelperId) : null;
                if (parent?.type == HairHelperType.Gather)
                {
                    helper.bun.gatherOffset = parent.LocalToSource.inverse.MultiplyPoint3x4(position);
                    helper.rotation = Quaternion.Inverse(parent.LocalToSource.rotation) * rotation;
                }
                else { helper.position = position; helper.rotation = rotation; }
                HairGroomCommands.Commit(groom);
            }
        }
    }
}
