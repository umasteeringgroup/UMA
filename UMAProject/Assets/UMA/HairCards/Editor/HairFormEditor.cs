using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    internal static class HairFormEditor
    {
        internal static bool IsForm(HairHelper helper) => helper != null &&
            (helper.type == HairHelperType.GuideGrid || helper.type == HairHelperType.BraidRail || helper.type == HairHelperType.CurveRail);

        internal static void DrawPopulation(HairCardStage stage, HairGenerationStage population, SerializedProperty property)
        {
            var form = property.FindPropertyRelative("form");
            bool bun = population.source == HairPopulationSource.Bun;
            if (bun) EditorGUILayout.PropertyField(form.FindPropertyRelative("bunPart"), new GUIContent("Bun part"));
            EditorGUILayout.HelpBox(bun ? "A shared Bun helper controls the wrapped volume, center tuck and braid ring together. Select that helper to move, resize or link the bun to a Gather ring. Each population retains its own material and polygon budget." : population.source == HairPopulationSource.Braid ?
                "Three bundles weave over and under along each rail. The rail sets the bun/ponytail shape; repeats sets the plait frequency. Edit rail control points under Shared Helpers." :
                "Cards run from the green root row to the orange tip row. Edit the grid under Shared Helpers; its surface controls both flow and card facing. No painted roots or authored guides are required.", MessageType.Info);
            EditorGUILayout.LabelField("Form helpers", EditorStyles.boldLabel);
            var bindings = form.FindPropertyRelative("helperIds");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var entry = bindings.GetArrayElementAtIndex(i);
                var ids = new List<string> { string.Empty }; var names = new List<string> { "None" };
                foreach (var h in stage.Groom.SharedHelpers)
                    if (h != null && (bun ? h.type == HairHelperType.Bun : population.source == HairPopulationSource.GridPanels ? h.type == HairHelperType.GuideGrid : h.type == HairHelperType.BraidRail || h.type == HairHelperType.CurveRail))
                    { ids.Add(h.Id); names.Add(h.name); }
                string current = entry.stringValue ?? string.Empty;
                if (!ids.Contains(current)) { ids.Add(current); names.Add("Missing helper (replace)"); }
                entry.stringValue = ids[EditorGUILayout.Popup("Form " + (i + 1), ids.IndexOf(current), names.ToArray())];
            }
            EditorGUILayout.LabelField("Add/remove form bindings in the tree actions. One count is shared across all assigned forms; it is not multiplied per form.", EditorStyles.wordWrappedMiniLabel);
            void Field(string name, string label, string tip = null) => EditorGUILayout.PropertyField(form.FindPropertyRelative(name), new GUIContent(label, tip));
            Field("cardWidth", "Card width (m)");
            Field("segments", "Length segments / card", "This directly sets the segment ceiling. LOD sample overrides and interactive preview may lower it. Shape control points are independent of this budget.");
            Field("smooth", "Smooth form interpolation"); Field("flipFacing", "Flip card facing");
            Field("adaptiveSamples", "Concentrate segments at bends", "Retain detailed facing and spend the segment budget at curves and twists. Disable for uniformly spaced, deliberately angular cards.");
            if (form.FindPropertyRelative("adaptiveSamples").boolValue) Field("shapeError", "Shape tolerance (m)");
            if (population.source == HairPopulationSource.GridPanels || (bun && population.form.bunPart != HairBunPart.SurroundingBraid))
            {
                Field("jitter", "Placement variation"); Field("thickness", "Panel depth (m)");
                Field("rootStartVariation", "Root start variation", "Stagger starts along the first 0–10% of the grid flow. Softens a ruler-straight hairline without moving the controls. Painted density is sampled at the resulting root.");
                Field("lengthVariation", "Tip length variation");
                Field("preserveLodCoverage", "Preserve coverage at LODs", "Spread surviving cards across each grid instead of randomly leaving holes. Keeps at least one candidate per panel at nonzero LOD density and widens survivors by up to 2×. Painted density can still remove roots. Full-density geometry is unchanged.");
            }
            Field("usePaintedRootDensity", "Use painted root density", "Samples Growth / Density at the nearest source surface to each form root. Works with vertex or texture maps. It removes whole cards, not their tips. Leave off for a bun or braid away from the scalp.");
            if (form.FindPropertyRelative("usePaintedRootDensity").boolValue)
                EditorGUILayout.HelpBox("Growth / Density now controls which form roots survive. Paint that group's map to trim the root boundary; surviving cards keep their shape. Keep this off for free-standing forms.", MessageType.Info);
            Field("flyaways", "Flyaway lift (m)");
            if (population.source == HairPopulationSource.Braid || (bun && population.form.bunPart == HairBunPart.SurroundingBraid))
            {
                EditorGUILayout.Space(); EditorGUILayout.LabelField("Plait shape", EditorStyles.boldLabel);
                Field("braidRadius", "Braid half-width (m)"); Field("braidDepth", "Crossing depth");
                Field("repeats", "Weave repeats"); Field("bundleRadius", "Bundle thickness"); Field("tipScale", "Tip size");
                Field("phase", "Weave phase"); Field("reverseWeave", "Reverse weave");
                if (population.form.segments < population.form.repeats * 12)
                    EditorGUILayout.HelpBox("Low segment budget: the braid can look angular or miss crossings. Start around 12 segments per repeat, then reduce while viewing the silhouette.", MessageType.Warning);
                if (population.count < population.form.helperIds.Count * 9)
                    EditorGUILayout.HelpBox("Use at least nine cards per rail for coverage of all three bundles.", MessageType.Warning);
            }
            EditorGUILayout.PropertyField(property.FindPropertyRelative("seed"));
            int spans = stage.ActiveGroup?.profile?.RibbonSpans ?? 1;
            EditorGUILayout.LabelField($"Full-density ceiling: {(long)population.count * population.form.segments * 2 * spans:N0} triangles (single-sided ribbons). Active LOD overrides may lower it.", EditorStyles.wordWrappedMiniLabel);
            var info = stage.Evaluation?.populations.Find(p => p.stageId == population.Id);
            if (info != null) EditorGUILayout.LabelField($"Current output: {info.outputCount:N0} cards · {info.milliseconds:F1} ms", EditorStyles.wordWrappedMiniLabel);
        }

        internal static HairGroup CreateGroup(HairGroomAsset groom, HairPopulationSource source, Vector3 origin)
        {
            Undo.RecordObject(groom, "Add Hair Form Group");
            var group = new HairGroup { name = source == HairPopulationSource.Bun ? "Bun" : source == HairPopulationSource.Braid ? "Braid" : "Grid panels" };
            group.EnsureIntegrity(groom.SourceMesh != null ? groom.SourceMesh.vertexCount : 0); groom.Groups.Add(group);
            group.generation.enabled = true; group.generation.cards.source = source;
            group.generation.cards.name = group.name + " cards"; group.generation.cards.count = source == HairPopulationSource.Braid ? 60 : 100;
            group.generation.cards.form.segments = source == HairPopulationSource.Braid ? 80 : 20;
            group.children.childrenPerGuide = 0;
            var profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.name = groom.name + " " + group.name + " Ribbon";
            profile.Configure(HairCardShape.Ribbon, .012f, .008f, 32, generateBackfaces: false); profile.ConfigureRibbon(2, .1f, false);
            string path = AssetDatabase.GetAssetPath(groom);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(System.IO.Path.GetDirectoryName(path).Replace('\\','/') + "/" + profile.name + ".asset"));
            Undo.RegisterCreatedObjectUndo(profile, "Create Form Ribbon Profile"); group.profile = profile;
            foreach (var other in groom.Groups) if (other != group && other.atlas != null) { group.atlas = other.atlas; break; }
            AddHelper(groom, group.generation.cards, origin);
            HairGroomCommands.Commit(groom); return group;
        }
        internal static HairHelper AddHelper(HairGroomAsset groom, HairGenerationStage population, Vector3 position)
        {
            Undo.RecordObject(groom, "Add Hair Form");
            var helper = HairGroomCommands.AddHelper(groom, population.source == HairPopulationSource.Bun ? HairHelperType.Bun : population.source == HairPopulationSource.Braid ? HairHelperType.BraidRail : HairHelperType.GuideGrid, position);
            if (population.source == HairPopulationSource.Braid)
            {
                helper.points.Clear();
                for (int i = 0; i < 7; i++) helper.points.Add(position + Vector3.up * (.2f * i / 6));
            }
            population.form.helperIds.Add(helper.Id); HairGroomCommands.Commit(groom); return helper;
        }
        internal static void DrawActions(HairCardStage stage, HairGroomNode node)
        {
            if (node.Kind == HairGroomNodeKind.Group || node.Kind == HairGroomNodeKind.Children)
                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    if (GUILayout.Button("+ Grid Panel Group")) AddGroup(stage, HairPopulationSource.GridPanels);
                    if (GUILayout.Button("+ Braid Group")) AddGroup(stage, HairPopulationSource.Braid);
                    if (GUILayout.Button("+ Bun Group")) AddGroup(stage, HairPopulationSource.Bun);
                }
            if (node.Kind == HairGroomNodeKind.Population && node.Population.source != HairPopulationSource.Scalp && node.Population.source != HairPopulationSource.PaintedScalp)
                using (new EditorGUI.DisabledScope(node.Locked))
                {
                    if (GUILayout.Button(node.Population.source == HairPopulationSource.Bun ? "+ Create & Edit Bun Helper" : node.Population.source == HairPopulationSource.Braid ? "+ Create & Edit Braid Rail" : "+ Create & Edit Grid Panel"))
                    {
                        var helper = AddHelper(stage.Groom, node.Population, stage.Groom.SourceMesh.bounds.center + Vector3.up * stage.Groom.SourceMesh.bounds.extents.y);
                        stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, helper.Id));
                    }
                    if (GUILayout.Button("+ Bind Existing Form"))
                    { Undo.RecordObject(stage.Groom, "Add Form Binding"); node.Population.form.helperIds.Add(string.Empty); HairGroomCommands.Commit(stage.Groom); }
                    using (new EditorGUI.DisabledScope(node.Population.form.helperIds.Count == 0))
                        if (GUILayout.Button("Remove Form Binding…"))
                        {
                            var menu = new GenericMenu();
                            for (int index = 0; index < node.Population.form.helperIds.Count; index++)
                            {
                                int captured = index; string id = node.Population.form.helperIds[index];
                                string label = (index + 1) + ". " + (stage.Groom.FindHelper(id)?.name ?? "Missing / unassigned form");
                                menu.AddItem(new GUIContent(label), false, () =>
                                {
                                    if (captured >= node.Population.form.helperIds.Count || node.Population.form.helperIds[captured] != id || node.Locked) return;
                                    if (!EditorUtility.DisplayDialog("Remove Form Binding?", "Stop generating on '" + label + "'? The helper itself is kept. Undo restores the binding.", "Remove Binding", "Cancel")) return;
                                    Undo.RecordObject(stage.Groom, "Remove Form Binding"); node.Population.form.helperIds.RemoveAt(captured); HairGroomCommands.Commit(stage.Groom);
                                });
                            }
                            menu.ShowAsContext();
                        }
                }
            if (node.Kind == HairGroomNodeKind.Helper && IsForm(node.Helper)) DrawHelperActions(stage, node.Helper);
        }
        private static void AddGroup(HairCardStage stage, HairPopulationSource source)
        {
            Vector3 origin = stage.Groom.SourceMesh.bounds.center + Vector3.up * stage.Groom.SourceMesh.bounds.extents.y;
            var group = CreateGroup(stage.Groom, source, origin);
            stage.SelectNode(source == HairPopulationSource.Braid ? HairGroomNodes.Key(HairGroomNodeKind.Helper, group.generation.cards.form.helperIds[0]) :
                HairGroomNodes.Key(HairGroomNodeKind.Population, group.generation.cards.Id));
        }
        private static void DrawHelperActions(HairCardStage stage, HairHelper helper)
        {
            if (GUILayout.Button("Frame This Form")) stage.FrameForm(helper);
            using (new EditorGUI.DisabledScope(helper.locked || !helper.embedded))
            {
                if (helper.type == HairHelperType.GuideGrid && !HairFormUtility.ValidGrid(helper) && GUILayout.Button("Initialize Grid…") &&
                    EditorUtility.DisplayDialog("Initialize Grid?", "Replace this incomplete grid with a 3 × 5 panel? Undo restores its previous points.", "Initialize", "Cancel"))
                { Undo.RecordObject(stage.Groom, "Initialize Hair Grid"); HairFormUtility.CreateGrid(helper, 3, 5, helper.position, Vector3.right * .08f, Vector3.up * .2f); HairGroomCommands.Commit(stage.Groom); }
                if (HairFormUtility.ValidGrid(helper) && GUILayout.Button("Conform Root Row to Surface…") &&
                    EditorUtility.DisplayDialog("Conform Grid Roots?", "Move only the green root row onto the nearest authoring surface, with 1 mm clearance? Other rows keep their shape. Undo restores it.", "Conform Roots", "Cancel"))
                {
                    var query = new HairMeshRaycaster(stage.Groom.SourceMesh);
                    Undo.RecordObject(stage.Groom, "Conform Grid Roots");
                    for (int i = 0; i < helper.gridColumns; i++)
                        if (query.ClosestPoint(helper.points[i], out var hit)) helper.points[i] = hit.Point + hit.Normal * .001f;
                    HairGroomCommands.Commit(stage.Groom);
                }
                if (HairFormUtility.ValidGrid(helper) && GUILayout.Button("Conform Whole Grid to Surface…") &&
                    EditorUtility.DisplayDialog("Conform Entire Grid?", "Project every grid control point onto the nearest authoring surface with 2 mm clearance? This removes the grid's volume, useful for close-fitting foundation hair. Undo restores it.", "Conform Grid", "Cancel"))
                {
                    var query = new HairMeshRaycaster(stage.Groom.SourceMesh);
                    Undo.RecordObject(stage.Groom, "Conform Hair Grid");
                    for (int i = 0; i < helper.points.Count; i++)
                        if (query.ClosestPoint(helper.points[i], out var hit)) helper.points[i] = hit.Point + hit.Normal * .002f;
                    HairGroomCommands.Commit(stage.Groom);
                }
                if (GUILayout.Button("Reverse Root / Tip…") && EditorUtility.DisplayDialog("Reverse Form Direction?", "Swap the start and end of this form? Every population referencing it updates. Undo restores it.", "Reverse", "Cancel"))
                { Undo.RecordObject(stage.Groom, "Reverse Hair Form"); HairFormUtility.Reverse(helper); HairGroomCommands.Commit(stage.Groom); }
                if (helper.type != HairHelperType.GuideGrid)
                {
                    if (GUILayout.Button("Form Rail into Bun…") && EditorUtility.DisplayDialog("Coil Rail into a Bun?", "Replace this rail's points with a tapered coil around its helper origin? Tune Coil radius, turns and rise in Properties first. Referencing braids update immediately. Undo restores the previous shape.", "Coil Rail", "Cancel"))
                    { Undo.RecordObject(stage.Groom, "Coil Braid Rail"); HairFormUtility.CoilRail(helper); HairGroomCommands.Commit(stage.Groom); }
                    using (new EditorGUI.DisabledScope(helper.points.Count == 0))
                    if (GUILayout.Button("Insert Point After Selected"))
                    {
                        Undo.RecordObject(stage.Groom, "Insert Rail Point"); int p = Mathf.Clamp(stage.FormPointIndex, 0, helper.points.Count - 1);
                        Vector3 next = p + 1 < helper.points.Count ? helper.points[p + 1] : helper.points[p] + (helper.points[p] - helper.points[Mathf.Max(0, p - 1)]);
                        helper.points.Insert(p + 1, Vector3.Lerp(helper.points[p], next, .5f)); stage.FormPointIndex = p + 1; HairGroomCommands.Commit(stage.Groom);
                    }
                    using (new EditorGUI.DisabledScope(helper.points.Count <= 2))
                        if (GUILayout.Button("Remove Selected Point…") && EditorUtility.DisplayDialog("Remove Rail Point?", "Remove this control point? The rail will change shape. Undo restores it.", "Remove", "Cancel"))
                        { Undo.RecordObject(stage.Groom, "Remove Rail Point"); helper.points.RemoveAt(Mathf.Clamp(stage.FormPointIndex, 0, helper.points.Count - 1)); HairGroomCommands.Commit(stage.Groom); }
                }
                if (GUILayout.Button("Duplicate Mirrored Form"))
                {
                    Undo.RecordObject(stage.Groom, "Mirror Hair Form");
                    var copy = HairGroomCommands.AddHelper(stage.Groom, helper.type, helper.position);
                    copy.name = helper.name + " Mirrored"; copy.gridColumns = helper.gridColumns; copy.gridRows = helper.gridRows; copy.points.Clear();
                    copy.braidScale = helper.braidScale; copy.rotation = helper.rotation;
                    copy.coilRadius = helper.coilRadius; copy.coilTurns = helper.coilTurns; copy.coilRise = helper.coilRise;
                    if (helper.type == HairHelperType.BraidRail)
                    {
                        stage.RailEditorWorkspace.Prepare(stage.Groom, helper);
                        Vector3 Mirror(Vector3 p) => p - stage.Groom.SymmetryPlaneNormal * (2 * Vector3.Dot(p - stage.Groom.SymmetryPlanePoint, stage.Groom.SymmetryPlaneNormal));
                        for (int i = 0; i < helper.points.Count; i++) copy.points.Add(Mirror(stage.RailEditorWorkspace.Sample(i / Mathf.Max(1f, helper.points.Count - 1), true)));
                        copy.position = Mirror(HairRailUtility.PointMatrix(stage.Groom, helper).MultiplyPoint3x4(helper.position));
                    }
                    else foreach (var p in helper.points) copy.points.Add(new Vector3(-p.x, p.y, p.z));
                    if (HairFormUtility.ValidGrid(copy)) for (int r = 0; r < copy.gridRows; r++) copy.points.Reverse(r * copy.gridColumns, copy.gridColumns);
                    if (helper.type != HairHelperType.BraidRail) copy.position = new Vector3(-helper.position.x, helper.position.y, helper.position.z);
                    foreach (var group in stage.Groom.Groups)
                    {
                        foreach (var population in group.generation.clumps) if (population.form.helperIds.Contains(helper.Id)) population.form.helperIds.Add(copy.Id);
                        if (group.generation.cards.form.helperIds.Contains(helper.Id)) group.generation.cards.form.helperIds.Add(copy.Id);
                    }
                    HairGroomCommands.Commit(stage.Groom); stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper, copy.Id));
                }
            }
        }
        internal static void DrawHelperProperties(HairCardStage stage, HairHelper helper)
        {
            if (!IsForm(helper)) return;
            if (helper.type == HairHelperType.BraidRail) { HairBraidSplineEditor.Properties(stage, helper); return; }
            using (new EditorGUI.DisabledScope(helper.locked || !helper.embedded))
            {
                EditorGUILayout.HelpBox("Click a control point in Scene, then move its gizmo. Green = root/start; orange = tip/end. Soft radius moves neighboring controls. All points and settings are saved in this groom.", MessageType.Info);
                if (helper.type == HairHelperType.GuideGrid)
                {
                    int columns = Mathf.Clamp(EditorGUILayout.DelayedIntField("Columns across", helper.gridColumns), 2, 16);
                    int rows = Mathf.Clamp(EditorGUILayout.DelayedIntField("Rows along flow", helper.gridRows), 2, 32);
                    if (columns != helper.gridColumns || rows != helper.gridRows)
                    { Undo.RecordObject(stage.Groom, "Resample Hair Grid"); HairFormUtility.ResizeGrid(helper, columns, rows); HairGroomCommands.Commit(stage.Groom); }
                }
                stage.FormPointIndex = Mathf.Clamp(EditorGUILayout.IntField("Selected point", stage.FormPointIndex), 0, Mathf.Max(0, helper.points.Count - 1));
                stage.FormEditAll = EditorGUILayout.Toggle("Move whole form", stage.FormEditAll);
                stage.FormSoftRadius = EditorGUILayout.Slider("Soft radius (m)", stage.FormSoftRadius, 0, .3f);
                if (helper.points.Count > 0)
                {
                    var before = helper.points[stage.FormPointIndex];
                    var after = EditorGUILayout.Vector3Field("Source-local position", before);
                    if (after != before) MovePoint(stage, helper, stage.FormPointIndex, after);
                }
                if (helper.type != HairHelperType.GuideGrid)
                {
                    EditorGUI.BeginChangeCheck();
                    float braidScale = EditorGUILayout.Slider("Braid thickness scale", helper.braidScale, .1f, 3f);
                    float radius = EditorGUILayout.Slider("Coil radius (m)", helper.coilRadius, .005f, .2f);
                    float turns = EditorGUILayout.Slider("Coil turns", helper.coilTurns, .5f, 3f);
                    float rise = EditorGUILayout.Slider("Coil rise (m)", helper.coilRise, -.1f, .1f);
                    if (EditorGUI.EndChangeCheck())
                    { Undo.RecordObject(stage.Groom, "Edit Bun Coil Settings"); helper.braidScale = braidScale; helper.coilRadius = radius; helper.coilTurns = turns; helper.coilRise = rise; HairGroomCommands.Commit(stage.Groom); }
                    EditorGUILayout.LabelField("Coil settings apply when you choose Form Rail into Bun in the tree. They never replace your edited points automatically.", EditorStyles.wordWrappedMiniLabel);
                    var euler = EditorGUILayout.Vector3Field("Initial braid frame", helper.rotation.eulerAngles);
                    if (Quaternion.Angle(Quaternion.Euler(euler), helper.rotation) > .001f)
                    { Undo.RecordObject(stage.Groom, "Rotate Braid Frame"); helper.rotation = Quaternion.Euler(euler); HairGroomCommands.Commit(stage.Groom); }
                }
            }
        }
        internal static void MovePoint(HairCardStage stage, HairHelper helper, int selected, Vector3 target)
        {
            if (helper.locked || !helper.embedded || selected < 0 || selected >= helper.points.Count || !float.IsFinite(target.x) || !float.IsFinite(target.y) || !float.IsFinite(target.z)) return;
            Undo.RecordObject(stage.Groom, "Form Hair Controls");
            Vector3 origin = helper.points[selected], delta = target - origin;
            for (int i = 0; i < helper.points.Count; i++)
            {
                float weight = stage.FormEditAll || i == selected ? 1 : stage.FormSoftRadius > 0 ? Mathf.SmoothStep(1, 0, Vector3.Distance(origin, helper.points[i]) / stage.FormSoftRadius) : 0;
                helper.points[i] += delta * weight;
            }
            if (stage.FormEditAll) helper.position += delta;
            HairGroomCommands.Commit(stage.Groom, HairPreviewChange.Evaluation);
        }
    }

    public partial class HairCardStage
    {
        [SerializeField] internal int FormPointIndex;
        [SerializeField] internal bool FormEditAll;
        [SerializeField] internal float FormSoftRadius;
        internal void FrameForm(HairHelper helper)
        {
            if (helper?.points == null || helper.points.Count == 0 || SceneView.lastActiveSceneView == null) return;
            Matrix4x4 matrix = SourceToStageMatrix;
            if (helperPoseMatrices.TryGetValue(helper.Id, out var pose)) matrix *= pose;
            var bounds = new Bounds(matrix.MultiplyPoint3x4(helper.points[0]), Vector3.one * .04f);
            if (HairRailUtility.IsRail(helper))
            {
                RailEditorWorkspace.Prepare(groom, helper);
                bounds = new Bounds(matrix.MultiplyPoint3x4(RailEditorWorkspace.Sample(0, true)), Vector3.one * .04f);
                for (int i = 1; i <= 128; i++) bounds.Encapsulate(matrix.MultiplyPoint3x4(RailEditorWorkspace.Sample(i / 128f, true)));
            }
            else foreach (var point in helper.points) bounds.Encapsulate(matrix.MultiplyPoint3x4(point));
            bounds.Expand(.06f); SceneView.lastActiveSceneView.Frame(bounds, false);
        }
        private void DrawFormControls(HairHelper helper, bool selected)
        {
            if (helper.type == HairHelperType.BraidRail) { DrawBraidSpline(helper, selected); return; }
            var points = helper.points;
            if (HairFormUtility.ValidGrid(helper))
            {
                for (int r = 0; r < helper.gridRows; r++) for (int c = 0; c < helper.gridColumns; c++)
                {
                    int i = r * helper.gridColumns + c;
                    Handles.color = r == 0 ? Color.green : r == helper.gridRows - 1 ? new Color(1, .55f, .1f) : new Color(.3f, .8f, 1, .65f);
                    if (c > 0) Handles.DrawAAPolyLine(r == 0 || r == helper.gridRows - 1 ? 4 : 1, points[i - 1], points[i]);
                    if (r > 0) Handles.DrawLine(points[i - helper.gridColumns], points[i]);
                }
            }
            else if (points.Count > 1)
            {
                Handles.color = selected ? Color.cyan : new Color(.3f, .8f, 1, .6f);
                Vector3 previous = points[0];
                for (int i = 1; i <= 100; i++) { Vector3 next = HairFormUtility.RailPoint(points, i / 100f, true); Handles.DrawLine(previous, next); previous = next; }
            }
            if (!selected || points.Count == 0) return;
            Handles.Label(points[0], helper.type == HairHelperType.GuideGrid ? "ROOT EDGE" : "START");
            Handles.Label(points[points.Count - 1], "TIP / END");
            if (helper.locked || !helper.embedded) return;
            FormPointIndex = Mathf.Clamp(FormPointIndex, 0, points.Count - 1);
            for (int i = 0; i < points.Count; i++)
            {
                Handles.color = i == FormPointIndex ? Color.yellow : Color.cyan;
                float size = LocalHandleSize(points[i]) * .025f;
                if (Handles.Button(points[i], Quaternion.identity, size, size, Handles.DotHandleCap)) { FormPointIndex = i; RepaintAll(); }
            }
            var point = points[FormPointIndex];
            if (FormSoftRadius > 0) { Handles.color = new Color(1, 1, 0, .4f); Handles.DrawWireDisc(point, Vector3.up, FormSoftRadius); }
            EditorGUI.BeginChangeCheck();
            var moved = Handles.PositionHandle(point, Quaternion.identity);
            if (EditorGUI.EndChangeCheck()) HairFormEditor.MovePoint(this, helper, FormPointIndex, moved);
        }
    }
}
