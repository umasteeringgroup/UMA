using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static class HairBraidSplineEditor
    {
        internal static void Properties(HairCardStage stage, HairHelper helper)
        {
            EditorGUILayout.HelpBox("Click the spline to move the whole braid. Click a dot to reshape it. Green = root/start; orange = tip/end. Surface offsets locate the braid's spine, not its outer edge—allow room for braid thickness.", MessageType.Info);
            using (new EditorGUI.DisabledScope(helper.locked || !helper.embedded))
            {
                stage.FormEditAll = GUILayout.Toolbar(stage.FormEditAll ? 0 : 1, new[] { "Whole spline", "Control point" }) == 0;
                stage.FormPointIndex = Mathf.Clamp(EditorGUILayout.IntField("Selected point", stage.FormPointIndex), 0, Mathf.Max(0, helper.points.Count - 1));
                if (!stage.FormEditAll) stage.FormSoftRadius = EditorGUILayout.Slider("Soft radius (m)", stage.FormSoftRadius, 0, .3f);
                if (helper.points.Count > 0)
                {
                    var query = stage.RailEditorWorkspace; query.Prepare(stage.Groom, helper);
                    Vector3 before = query.Sample(stage.FormPointIndex / Mathf.Max(1f, helper.points.Count - 1), true);
                    var after = EditorGUILayout.Vector3Field("Selected point (source)", before);
                    if (before != after) Move(stage, helper, stage.FormPointIndex, before, after);
                }
                string parent = TargetPopup(stage.Groom, helper.rail.parentHelperId, "Follow helper", "None (source space)");
                if (parent != (helper.rail.parentHelperId ?? string.Empty))
                {
                    Undo.RecordObject(stage.Groom, "Reparent Braid Spline");
                    HairRailUtility.BindParent(stage.Groom, helper, parent); HairGroomCommands.Commit(stage.Groom);
                }
                using var so = new SerializedObject(stage.Groom);
                var h = so.FindProperty("sharedHelpers").GetArrayElementAtIndex(stage.Groom.SharedHelpers.IndexOf(helper));
                var rail = h.FindPropertyRelative("rail");
                void Field(SerializedProperty p, string name, string label, string tip = null) => EditorGUILayout.PropertyField(p.FindPropertyRelative(name), new GUIContent(label, tip));
                EditorGUILayout.Space(); EditorGUILayout.LabelField("Surface placement", EditorStyles.boldLabel);
                Field(rail, "followSurface", "Snap entire spine to surface");
                if (rail.FindPropertyRelative("followSurface").boolValue) Field(rail, "surfaceOffset", "Spine offset (m)");
                End("root", "Root / start attachment"); End("tip", "Tip / end attachment");
                if (rail.FindPropertyRelative("followSurface").boolValue ||
                    (HairRailAttachment)rail.FindPropertyRelative("root").FindPropertyRelative("attachment").enumValueIndex == HairRailAttachment.Surface ||
                    (HairRailAttachment)rail.FindPropertyRelative("tip").FindPropertyRelative("attachment").enumValueIndex == HairRailAttachment.Surface)
                {
                    Field(rail, "surfaceMesh", "Surface mesh override", "Empty uses the groom's scalp. Assign another readable mesh with its source-local transform below. The groom source mesh is not replaced.");
                    if (rail.FindPropertyRelative("surfaceMesh").objectReferenceValue != null)
                    {
                        Field(rail, "surfacePosition", "Surface position (source)");
                        var rotation = rail.FindPropertyRelative("surfaceRotation");
                        EditorGUI.BeginChangeCheck(); var euler = EditorGUILayout.Vector3Field("Surface rotation", rotation.quaternionValue.eulerAngles);
                        if (EditorGUI.EndChangeCheck()) rotation.quaternionValue = Quaternion.Euler(euler);
                        Field(rail, "surfaceScale", "Surface scale");
                    }
                }
                Field(h, "braidScale", "Braid thickness scale");
                EditorGUI.BeginChangeCheck(); var angles = EditorGUILayout.Vector3Field("Initial braid frame", helper.rotation.eulerAngles);
                if (EditorGUI.EndChangeCheck()) h.FindPropertyRelative("rotation").quaternionValue = Quaternion.Euler(angles);
                var coil = h.FindPropertyRelative("coilRadius"); coil.isExpanded = EditorGUILayout.Foldout(coil.isExpanded, "Coil preset settings", true);
                if (coil.isExpanded)
                {
                    Field(h, "coilRadius", "Coil radius (m)"); Field(h, "coilTurns", "Coil turns"); Field(h, "coilRise", "Coil rise (m)");
                    EditorGUILayout.LabelField("Applied only by Form Rail into Bun in the tree. Your spline is not replaced automatically.", EditorStyles.wordWrappedMiniLabel);
                }
                if (so.ApplyModifiedProperties()) HairGroomCommands.Commit(stage.Groom);
                EditorGUILayout.LabelField("Whole-spline movement preserves endpoint attachments. Move an attached endpoint to adjust its attachment offset. Helper targets can be embedded controls or scene objects bound under Shared Helpers.", EditorStyles.wordWrappedMiniLabel);

                void End(string propertyName, string label)
                {
                    var end = rail.FindPropertyRelative(propertyName);
                    EditorGUILayout.Space(); EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    Field(end, "attachment", "Attach to");
                    var mode = (HairRailAttachment)end.FindPropertyRelative("attachment").enumValueIndex;
                    if (mode == HairRailAttachment.Helper)
                    {
                        var id = end.FindPropertyRelative("helperId"); id.stringValue = TargetPopup(stage.Groom, id.stringValue, "Target helper", "None (choose target)");
                        Field(end, "helperOffset", "Offset in target space");
                    }
                    if (mode == HairRailAttachment.Surface) Field(end, "surfaceOffset", "Endpoint offset (m)");
                    if (mode != HairRailAttachment.Free) Field(end, "influence", "Attachment blend reach", "Fraction of the spine over which an endpoint move blends into the freeform controls.");
                }
            }
        }

        private static string TargetPopup(HairGroomAsset groom, string current, string label, string none)
        {
            var ids = new List<string> { string.Empty }; var names = new List<string> { none };
            foreach (var target in groom.SharedHelpers) if (HairRailUtility.IsTarget(target)) { ids.Add(target.Id); names.Add(target.name); }
            current ??= string.Empty;
            if (!ids.Contains(current)) { ids.Add(current); names.Add("Missing/invalid target (replace)"); }
            return ids[EditorGUILayout.Popup(label, ids.IndexOf(current), names.ToArray())];
        }

        internal static void Move(HairCardStage stage, HairHelper helper, int index, Vector3 from, Vector3 to)
        {
            if (helper.locked || !helper.embedded || index < 0 || index >= helper.points.Count || !float.IsFinite(to.sqrMagnitude)) return;
            var matrix = HairRailUtility.PointMatrix(stage.Groom, helper);
            if (!HairCoordinateUtility.IsInvertibleAffine(matrix)) return;
            Undo.RecordObject(stage.Groom, "Move Braid Spline");
            var end = index == 0 ? helper.rail.root : index == helper.points.Count - 1 ? helper.rail.tip : null;
            if (!stage.FormEditAll && end?.attachment == HairRailAttachment.Helper && HairRailUtility.IsTarget(stage.Groom.FindHelper(end.helperId)))
                end.helperOffset = HairRailUtility.TargetMatrix(stage.Groom, stage.Groom.FindHelper(end.helperId)).inverse.MultiplyPoint3x4(to);
            else
            {
                Vector3 delta = matrix.inverse.MultiplyVector(to - from), origin = matrix.MultiplyPoint3x4(helper.points[index]);
                for (int i = 0; i < helper.points.Count; i++)
                {
                    float weight = stage.FormEditAll || i == index ? 1 : stage.FormSoftRadius > 0 ?
                        Mathf.SmoothStep(1, 0, Vector3.Distance(origin, matrix.MultiplyPoint3x4(helper.points[i])) / stage.FormSoftRadius) : 0;
                    helper.points[i] += delta * weight;
                }
                if (stage.FormEditAll) helper.position += delta;
            }
            HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation);
        }

        internal static HairHelper ExtractBunBraid(HairGroomAsset groom, HairHelper bun)
        {
            if (bun?.type != HairHelperType.Bun || bun.locked) return null;
            Undo.RecordObject(groom, "Make Bun Braid Spline Editable");
            HairHelper requested = GetRail(bun);
            foreach (var group in groom.Groups)
            {
                Convert(group.generation.cards);
                foreach (var population in group.generation.clumps) Convert(population);
            }
            HairGroomCommands.Commit(groom); return requested;
            HairHelper GetRail(HairHelper source)
            {
                var candidate = HairRailUtility.CreateBunBraid(groom, source);
                var existing = groom.FindHelper(candidate.Id);
                if (existing != null) return existing;
                groom.SharedHelpers.Add(candidate); return candidate;
            }
            void Convert(HairGenerationStage population)
            {
                if (population.source != HairPopulationSource.Bun || population.form.bunPart != HairBunPart.SurroundingBraid || !population.form.helperIds.Contains(bun.Id)) return;
                for (int i = 0; i < population.form.helperIds.Count; i++)
                {
                    var target = groom.FindHelper(population.form.helperIds[i]);
                    if (target?.type == HairHelperType.Bun) population.form.helperIds[i] = GetRail(target).Id;
                }
                population.source = HairPopulationSource.Braid;
            }
        }
    }

    public partial class HairCardStage
    {
        internal readonly HairRailWorkspace RailEditorWorkspace = new HairRailWorkspace();
        private readonly Vector3[] braidSplineDraw = new Vector3[129];
        private Plane braidDragPlane;
        private Vector3 braidDragSource;
        private int braidDragUndo;
        private int braidDragControl;
        private string braidDragHelper;
        private void EndBraidSplineDrag(bool cancel = false)
        {
            if (braidDragHelper == null) return;
            if (GUIUtility.hotControl == braidDragControl) GUIUtility.hotControl = 0;
            braidDragHelper = null;
            if (cancel) Undo.RevertAllDownToGroup(braidDragUndo); else Undo.CollapseUndoOperations(braidDragUndo);
            RepaintAll();
        }
        private void DrawBraidSpline(HairHelper helper, bool selected)
        {
            var query = RailEditorWorkspace; query.Prepare(groom, helper);
            if (query.Controls.Count < 2) return;
            for (int i = 0; i < braidSplineDraw.Length; i++) braidSplineDraw[i] = query.Sample(i / 128f, true);
            Handles.color = selected ? Color.cyan : new Color(.3f, .8f, 1, .75f);
            Handles.DrawAAPolyLine(selected ? 4 : 2, braidSplineDraw);
            int control = GUIUtility.GetControlID(helper.Id.GetHashCode(), FocusType.Passive);
            var evt = Event.current;
            bool canPick = SceneTool == HairSceneTool.Select || SceneTool == HairSceneTool.Helper;
            if (canPick && evt.type == EventType.Layout)
                for (int i = 1; i < braidSplineDraw.Length; i++) HandleUtility.AddControl(control, HandleUtility.DistanceToLine(braidSplineDraw[i - 1], braidSplineDraw[i]));
            if (canPick && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && HandleUtility.nearestControl == control)
            {
                FormEditAll = true; SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
                if (!helper.locked && helper.embedded)
                {
                    Vector3 nearest = braidSplineDraw[0]; float best = float.PositiveInfinity;
                    foreach (var p in braidSplineDraw)
                    {
                        float d = (HandleUtility.WorldToGUIPoint(p) - evt.mousePosition).sqrMagnitude;
                        if (d < best) { best = d; nearest = p; }
                    }
                    var view = SceneView.currentDrawingSceneView;
                    braidDragPlane = new Plane(view != null ? view.camera.transform.forward : Vector3.forward, Handles.matrix.MultiplyPoint3x4(nearest));
                    var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                    if (braidDragPlane.Raycast(ray, out float distance))
                    {
                        braidDragSource = Handles.matrix.inverse.MultiplyPoint3x4(ray.GetPoint(distance));
                        Undo.IncrementCurrentGroup(); braidDragUndo = Undo.GetCurrentGroup();
                        braidDragControl = control; braidDragHelper = helper.Id; GUIUtility.hotControl = control;
                    }
                }
                evt.Use();
            }
            bool dragging = braidDragHelper == helper.Id && GUIUtility.hotControl == braidDragControl;
            if (dragging && evt.type == EventType.MouseDrag)
            {
                var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (braidDragPlane.Raycast(ray, out float distance))
                {
                    var next = Handles.matrix.inverse.MultiplyPoint3x4(ray.GetPoint(distance));
                    HairBraidSplineEditor.Move(this, helper, FormPointIndex, braidDragSource, next); braidDragSource = next;
                }
                evt.Use();
            }
            if (dragging && (evt.type == EventType.MouseUp || evt.type == EventType.MouseLeaveWindow || evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape))
            {
                EndBraidSplineDrag(evt.type == EventType.KeyDown);
                evt.Use();
            }
            if (!selected) return;
            Handles.Label(braidSplineDraw[0], "ROOT / START"); Handles.Label(braidSplineDraw[^1], "TIP / END");
            if (helper.locked || !helper.embedded) return;
            FormPointIndex = Mathf.Clamp(FormPointIndex, 0, helper.points.Count - 1);
            for (int i = 0; i < helper.points.Count; i++)
            {
                Vector3 p = query.Sample(i / (helper.points.Count - 1f), true);
                Handles.color = i == 0 ? Color.green : i == helper.points.Count - 1 ? new Color(1, .55f, .1f) : i == FormPointIndex ? Color.yellow : Color.cyan;
                float size = LocalHandleSize(p) * .025f;
                if (Handles.Button(p, Quaternion.identity, size, size, Handles.DotHandleCap)) { FormPointIndex = i; FormEditAll = false; RepaintAll(); }
            }
            Vector3 point = query.Sample(FormPointIndex / (helper.points.Count - 1f), true);
            if (FormEditAll)
            {
                point = Vector3.zero; foreach (var p in braidSplineDraw) point += p; point /= braidSplineDraw.Length;
            }
            else if (FormSoftRadius > 0) { Handles.color = Color.yellow; Handles.DrawWireDisc(point, Vector3.up, FormSoftRadius); }
            Handles.Label(point, FormEditAll ? "MOVE BRAID SPLINE" : "MOVE SPLINE POINT");
            EditorGUI.BeginChangeCheck(); var moved = Handles.PositionHandle(point, FormEditAll ? query.Frame : Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) HairBraidSplineEditor.Move(this, helper, FormPointIndex, point, moved);
        }
    }
}
