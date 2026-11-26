using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Clothes Designer's workflow in the Vertex Editor: the panel in the Scene view, the Vertex
    /// Editor window's part, and the cloth tools in the Scene view. The work itself is in
    /// <see cref="DesignerSession"/>, one per open Vertex Editor.
    /// </summary>
    [InitializeOnLoad]
    public sealed class ClothesDesignerWorkflow : VertexEditorWorkflow
    {
        /// <summary>The workflow's name in code. Keep it stable: other tools start the designer by it.</summary>
        public const string WorkflowId = "uma.clothes-designer";

        /// <summary>What the designer was opened to do.</summary>
        public enum Mode
        {
            /// <summary>Make new clothes from a pattern.</summary>
            NewClothes,
            /// <summary>Refit a clothing slot that already exists.</summary>
            FitExistingSlot
        }

        private static readonly ClothesDesignerWorkflow instance = new ClothesDesignerWorkflow();
        private static readonly GUIContent toolbarContent = new GUIContent(
            "Clothes Designer", "Design clothes from sewing patterns and fit them to this race.");
        private static readonly GUIContent[] toolContents = ClothToolInfo.PanelContents();
        private static readonly ClothTool[] toolOrder = ClothToolInfo.Order;
        private static readonly string[] levelNames = { "Draft (3 cm)", "Medium (2 cm)", "Fine (1.5 cm)", "Very Fine (1 cm)", "Custom" };

        private readonly Dictionary<VertexEditorStage, DesignerSession> sessions = new Dictionary<VertexEditorStage, DesignerSession>();
        private GUIStyle titleStyle;
        private GUIStyle wrapStyle;
        private Vector2 windowScroll;
        private string dnaFilter = string.Empty;

        static ClothesDesignerWorkflow()
        {
            VertexEditorWorkflows.Register(instance);
        }

        public static ClothesDesignerWorkflow Instance => instance;

        public override string Id => WorkflowId;
        public override GUIContent ToolbarContent => toolbarContent;
        public override int Order => 100;
        // The designer opens from its own commands and needs none of the other workflows; it is left by
        // closing the Vertex Editor (or by Publish and Sculpt, which opens a Vertex Editor of its own).
        public override bool HidesWorkflowBars => true;
        public override string SaveLabel => "Save Pattern and Fit";
        public override string SaveHelp => "Save the pattern and the fit as assets (you choose where the first time).";

        /// <summary>The designer's session on a stage, made when first needed. Null for no stage.</summary>
        public DesignerSession GetSession(VertexEditorStage stage)
        {
            if (stage == null) return null;
            if (!sessions.TryGetValue(stage, out DesignerSession session))
            {
                session = new DesignerSession(stage);
                sessions.Add(stage, session);
            }
            return session;
        }

        /// <summary>The designer's session on a stage if it has one; never makes one.</summary>
        public bool TryGetSession(VertexEditorStage stage, out DesignerSession session)
        {
            session = null;
            return stage != null && sessions.TryGetValue(stage, out session);
        }

        /// <summary>Sets what the designer does on a stage before it is activated there.</summary>
        public void SetMode(VertexEditorStage stage, Mode mode, IEnumerable<string> fitSlotNames = null)
        {
            DesignerSession session = GetSession(stage);
            session.Mode = mode;
            session.FitSlotNames.Clear();
            if (fitSlotNames != null) session.FitSlotNames.AddRange(fitSlotNames);
        }

        /// <summary>The body captured for a stage, or null when the designer has not been active there.</summary>
        public BodyReference GetBody(VertexEditorStage stage)
        {
            return stage != null && sessions.TryGetValue(stage, out DesignerSession session) ? session.Body : null;
        }

        public override void OnActivate(VertexEditorStage stage)
        {
            DesignerSession session = GetSession(stage);
            if (session.Body == null) session.CaptureBody();
            session.EnsureFit();
            session.SetPreviewVisible(true);
        }

        public override void OnDeactivate(VertexEditorStage stage)
        {
            if (sessions.TryGetValue(stage, out DesignerSession session))
            {
                session.Running = false;
                session.SetPreviewVisible(false);
            }
        }

        public override void OnCharacterRebuilt(VertexEditorStage stage)
        {
            if (!sessions.TryGetValue(stage, out DesignerSession session)) return;
            // The body changed shape or pose: collide with and measure the new one.
            session.CaptureBody();
        }

        public override void OnStageClosing(VertexEditorStage stage)
        {
            if (sessions.TryGetValue(stage, out DesignerSession session))
            {
                session.Dispose();
                sessions.Remove(stage);
            }
        }

        public override bool HasUnsavedChanges(VertexEditorStage stage) =>
            stage != null && sessions.TryGetValue(stage, out DesignerSession session) && session.HasUnsavedChanges;

        public override string DescribeUnsavedChanges(VertexEditorStage stage) =>
            stage != null && sessions.TryGetValue(stage, out DesignerSession session) ? session.DescribeUnsavedChanges() : null;

        public override bool SaveChanges(VertexEditorStage stage) =>
            stage == null || !sessions.TryGetValue(stage, out DesignerSession session) || session.SaveAll();

        public override void DiscardChanges(VertexEditorStage stage)
        {
            if (stage != null && sessions.TryGetValue(stage, out DesignerSession session)) session.DiscardAll();
        }

        public override void OnSceneGUI(VertexEditorStage stage, SceneView sceneView, bool pointerOverPanel)
        {
            DesignerSession session = GetSession(stage);
            session.OnSceneGUI(sceneView, pointerOverPanel);
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            wrapStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }

        private static bool Section(string key, string title, bool defaultOpen = true)
        {
            string prefKey = "UMA.ClothesDesigner.Section." + key;
            bool open = SessionState.GetBool(prefKey, defaultOpen);
            bool now = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            if (now != open) SessionState.SetBool(prefKey, now);
            return now;
        }

        // ---- Scene view panel -------------------------------------------------------------------

        public override void OnToolsGUI(VertexEditorStage stage)
        {
            EnsureStyles();
            // The width the panel's rows must fit: the stage's content width less where this content starts in it.
            // A row wider than that (the tool grid, the Pattern buttons) widened everything in the panel and cut it off.
            // Where it starts is read on a repaint and used from the next event, so a layout pass and its repaint
            // lay out the same rows.
            Rect start = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            panelWidth = Mathf.Max(120f, stage.ToolsContentWidth - contentLeft - 2f);
            if (Event.current.type == EventType.Repaint) contentLeft = Mathf.Max(0f, start.x);
            DesignerSession session = GetSession(stage);
            GUILayout.Label(session.Mode == Mode.FitExistingSlot ? "Clothes Designer: Fit Existing Slot" : "Clothes Designer", titleStyle);
            DrawBodyLine(stage, session);
            if (session.Mode == Mode.FitExistingSlot)
            {
                DrawRefitPanel(stage, session);
                return;
            }
            DrawPatternSection(session);
            if (!session.HasPattern) return;
            DrawFitSection(session);
            if (session.ClothReady)
            {
                DrawToolsSection(session);
                DrawReportSection(session);
                DrawPublishSection(session);
            }
            DrawFilesSection(session);
        }

        private void DrawBodyLine(VertexEditorStage stage, DesignerSession session)
        {
            RaceData race = stage.CharacterRace;
            EditorGUILayout.LabelField("Race", race != null ? race.raceName : "(none)");
            EditorGUILayout.LabelField(
                new GUIContent("Pose", "Clothes are fitted and weighted in the pose the race's meshes were modelled in."),
                new GUIContent(stage.OwnsTemporaryCharacter ? "Race Default" : "Scene character's pose"));
            if (!stage.OwnsTemporaryCharacter)
            {
                EditorGUILayout.HelpBox("This is a character from your scene, in its own pose and shape. To fit clothes on the race's default body, use UMA > Clothes Designer > Create Clothes on Race.", MessageType.Warning);
            }
            if (session.Body == null)
            {
                EditorGUILayout.HelpBox("The body could not be read" + (string.IsNullOrEmpty(session.BodyProblem) ? "." : ": " + session.BodyProblem), MessageType.Error);
                if (GUILayout.Button("Capture Body Again")) session.CaptureBody();
                return;
            }
            BodyLandmarks m = session.Body.Landmarks;
            var bodyLabel = new GUIContent("Body", "Measured round the chest, waist and hips, arms left out.");
            var measured = new GUIContent("chest " + m.chestCm.ToString("0") + ", waist " + m.waistCm.ToString("0") + ", hip " + m.hipCm.ToString("0") + " cm");
            // On a narrow panel the measurements go on a line of their own rather than being cut off.
            if (EditorGUIUtility.labelWidth + EditorStyles.label.CalcSize(measured).x <= panelWidth) EditorGUILayout.LabelField(bodyLabel, measured);
            else
            {
                EditorGUILayout.LabelField(bodyLabel, GUIContent.none);
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(measured, wrapStyle);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawPatternSection(DesignerSession session)
        {
            if (!Section("Pattern", "Pattern")) return;
            EditorGUI.BeginChangeCheck();
            var picked = (ClothesPattern)EditorGUILayout.ObjectField(new GUIContent("Pattern" + (session.PatternDirty ? " *" : ""), "The sewing pattern being fitted. * means it has changes that are not saved."),
                session.PatternAsset, typeof(ClothesPattern), false);
            if (EditorGUI.EndChangeCheck() && ConfirmLosingWork(session)) { session.OpenPattern(picked); session.FrameCloth(); }
            if (session.Pattern != null && session.PatternAsset == null)
            {
                EditorGUILayout.LabelField(" ", "'" + session.Pattern.name + "' (not saved yet)", EditorStyles.miniLabel);
            }
            // Pattern Canvas goes on a row of its own when the four buttons do not fit the panel.
            bool oneRow = RowWidth("New", "Samples", "Generator", "Pattern Canvas") <= panelWidth;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("New", "Start a blank pattern."), Shrinkable) && ConfirmLosingWork(session)) { session.NewPattern(null); session.FrameCloth(); }
            if (GUILayout.Button(new GUIContent("Samples", "Browse the sample patterns, with pictures of each pattern and its drape: tops, bottoms, dresses and skirts, underwear and swimwear, accessories and medieval clothes."), Shrinkable))
            {
                SampleBrowserWindow.Open(session);
            }
            if (GUILayout.Button(new GUIContent("Generator", "Draft a garment to this body from a type and its style: tops, dresses, kimonos, trousers, skirts and jumpsuits, with sliders for length, fit, neck, sleeves, collars and bands. Changes drape as you make them."), Shrinkable))
            {
                GarmentGeneratorWindow.Open(session);
            }
            if (!oneRow)
            {
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }
            using (new EditorGUI.DisabledScope(!session.HasPattern))
            {
                if (GUILayout.Button(new GUIContent("Pattern Canvas", "Draw and sew the pieces in a 2D window."), Shrinkable)) PatternCanvasWindow.Open(session);
            }
            EditorGUILayout.EndHorizontal();
            if (!session.HasPattern)
            {
                EditorGUILayout.HelpBox("Start from the Generator (pick a garment and its style), one of the Samples, a new pattern, or pick a saved one above.", MessageType.Info);
            }
            DrawConvertRow(session);
        }

        private static UMA.CharacterSystem.UMAWardrobeRecipe convertRecipe;
        private static readonly List<string> convertMessages = new List<string>();

        /// <summary>Converts modelled clothes (a wardrobe recipe this race can wear) into a pattern to start from.</summary>
        private void DrawConvertRow(DesignerSession session)
        {
            EditorGUILayout.BeginHorizontal();
            convertRecipe = (UMA.CharacterSystem.UMAWardrobeRecipe)EditorGUILayout.ObjectField(new GUIContent("From Clothes",
                "Make a pattern from modelled clothes: each UV island becomes a piece, sized from the mesh with its baked-in folds taken out, and sewn where the islands meet. The clothes keep their UVs, so their textures still fit. Expect to tidy the result on the Pattern Canvas."),
                convertRecipe, typeof(UMA.CharacterSystem.UMAWardrobeRecipe), false);
            using (new EditorGUI.DisabledScope(convertRecipe == null || session.Body == null))
            {
                if (GUILayout.Button(new GUIContent("Convert", "Convert the chosen wardrobe recipe's clothes into a new pattern."), GUILayout.Width(70f)) && ConfirmLosingWork(session))
                {
                    convertMessages.Clear();
                    PatternFromMesh.Result result = null;
                    try
                    {
                        EditorUtility.DisplayProgressBar("Clothes Designer", "Converting '" + convertRecipe.name + "'...", 0.5f);
                        result = PatternFromMesh.FromWardrobeRecipe(session.Stage.CharacterRace, convertRecipe, session.Body.Landmarks);
                    }
                    finally
                    {
                        EditorUtility.ClearProgressBar();
                    }
                    if (result == null) convertMessages.Add("'" + convertRecipe.name + "' has no clothes this race can wear.");
                    else
                    {
                        convertMessages.Add(result.Pieces + " pieces and " + result.Seams + " seams from " + result.Islands + " UV islands.");
                        convertMessages.AddRange(result.Messages);
                        session.NewPattern(result.Pattern);
                        session.FrameCloth();
                    }
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (convertMessages.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", convertMessages), MessageType.Info);
                if (GUILayout.Button("Dismiss", EditorStyles.miniButton)) convertMessages.Clear();
            }
        }

        internal static bool ConfirmLosingWork(DesignerSession session)
        {
            if (!session.HasUnsavedChanges) return true;
            int choice = EditorUtility.DisplayDialogComplex("Clothes Designer", session.DescribeUnsavedChanges(), "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            if (choice == 0) return session.SaveAll();
            session.DiscardAll();
            return true;
        }

        private void DrawFitSection(DesignerSession session)
        {
            if (!Section("Fit", "Fit")) return;
            FitSession fit = session.Fit;
            foreach (string message in fit.Messages) EditorGUILayout.HelpBox(message, MessageType.Info);
            if (!session.ClothReady)
            {
                if (GUILayout.Button("Build Cloth")) session.RebuildCloth(false);
                return;
            }
            string status = session.Running
                ? (!fit.Sewn ? "Sewing " + Mathf.RoundToInt((1f - fit.Closure) * 100f) + "%"
                    : !fit.AllSewn ? "Tightening free pieces " + Mathf.RoundToInt((1f - fit.LateClosure) * 100f) + "%" : "Settling")
                : (session.Settled ? "Settled" : (fit.SimTime <= 0f ? "Laid out round the body" : "Paused"));
            EditorGUILayout.LabelField("Cloth", status, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(session.Running ? "Pause" : "Simulate", session.Running ? ClothFitButtons.PauseTip : ClothFitButtons.SimulateTip), Shrinkable))
            {
                session.SetRunning(!session.Running);
            }
            if (GUILayout.Button(new GUIContent("Step", ClothFitButtons.StepTip), Shrinkable)) session.StepOnce();
            if (GUILayout.Button(new GUIContent("Reset", ClothFitButtons.ResetTip), Shrinkable)) session.ResetToArrangement();
            if (GUILayout.Button(new GUIContent("Keep Drape", ClothFitButtons.KeepDrapeTip), Shrinkable)) session.KeepDrape();
            EditorGUILayout.EndHorizontal();

            bool gpuOk = ClothSolverGpu.Supported;
            EditorGUILayout.LabelField(new GUIContent("Solver", "The GPU runs the drape; the CPU solver does the same where compute shaders are not available."),
                new GUIContent(fit.Solver.IsGpu ? "GPU (" + SystemInfo.graphicsDeviceName + ")" : "CPU" + (gpuOk ? "" : " - this GPU cannot run compute shaders")));
            using (new EditorGUI.DisabledScope(!gpuOk))
            {
                bool cpu = EditorGUILayout.Toggle(new GUIContent("Use CPU Solver", "Run the drape on the CPU, for example to compare or when a graphics driver misbehaves."), fit.PreferCpu || !gpuOk);
                if (gpuOk && cpu != fit.PreferCpu) session.SetPreferCpu(cpu);
            }
            fit.FinalQuality = EditorGUILayout.Toggle(new GUIContent("Final Quality", "More substeps: stiffer cloth and fewer creases from the solver, slower."), fit.FinalQuality);
            DesignerSession.ShowSeams = EditorGUILayout.Toggle(new GUIContent("Show Seams", "Draw each seam in the Scene view: its two edges, and a thread from each point to the point it is sewn to. Colours match the Pattern Canvas."), DesignerSession.ShowSeams);
            EditorGUI.BeginChangeCheck();
            var view = (ClothView)EditorGUILayout.EnumPopup(new GUIContent("Show", "Texture: each piece's outline, grain and name. Stretch: how much longer the cloth is than the flat pattern. Body Distance: how far it is from the body."), DesignerSession.View);
            if (EditorGUI.EndChangeCheck()) { DesignerSession.View = view; session.RefreshView(); }
            if (view == ClothView.Stretch) GUILayout.Label("Blue no stretch, green 5%, yellow 10%, red 20% or more.", EditorStyles.wordWrappedMiniLabel);
            else if (view == ClothView.BodyDistance) GUILayout.Label("Red in the body, orange closer than the clearance, green at the clearance, blue 3 cm and more.", EditorStyles.wordWrappedMiniLabel);
            bool within = EditorGUILayout.Toggle(new GUIContent("Fold onto Itself", "Keep cloth of one piece apart from itself too, where it folds onto itself (deep folds, gathers, a long skirt), not only from other pieces. Needs a Cloth Thickness above 0."), session.FitData.selfCollideWithinPieces);
            if (within != session.FitData.selfCollideWithinPieces) { Undo.RecordObject(session.FitData, "Fold onto Itself"); session.FitData.selfCollideWithinPieces = within; session.ApplyFabric(); }
            DrawWornUnder(session);
            bool backFaces = EditorGUILayout.Toggle(new GUIContent("Show Back Faces", "Draw the cloth from both sides, so the inside of the clothes shows through openings. Only the Scene view's preview: the baked slot is one-sided unless its material draws both faces (see Inside Facing in Publish)."), DesignerSession.ShowBackFaces);
            if (backFaces != DesignerSession.ShowBackFaces) { DesignerSession.ShowBackFaces = backFaces; session.RefreshPreview(); }

            ClothesFit data = session.FitData;
            MeshLevel level = data.overrideMeshLevel ? data.meshLevel : session.Pattern.meshLevel;
            float custom = data.overrideMeshLevel ? data.customEdgeLengthCm : session.Pattern.customEdgeLengthCm;
            EditorGUI.BeginChangeCheck();
            var newLevel = (MeshLevel)EditorGUILayout.Popup(new GUIContent("Mesh Level", "How finely the cloth is meshed. Fit at a low level, bake at a higher one: the drape is carried over."), (int)level, levelNames);
            if (newLevel == MeshLevel.Custom) custom = EditorGUILayout.Slider(new GUIContent("Edge Length (cm)"), custom, 0.3f, 6f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, "Mesh Level");
                data.overrideMeshLevel = true;
                data.meshLevel = newLevel;
                data.customEdgeLengthCm = custom;
                session.RebuildCloth(true);
                GUIUtility.ExitGUI();
            }
            var topology = (MeshTopology)EditorGUILayout.EnumPopup(new GUIContent("Mesh Topology", "Even: nearly equilateral triangles, the most even cloth. Grain Grid: squares along each piece's grain and across it, split in two, so the mesh's rows and columns follow the warp and the weft: cleaner to sculpt, weight and read, and pairs of triangles make quads. The UVs come from the pattern either way. The drape is carried over."), session.Pattern.meshTopology);
            if (topology != session.Pattern.meshTopology)
            {
                Undo.RecordObject(session.Pattern, "Mesh Topology");
                session.Pattern.meshTopology = topology;
                session.RebuildCloth(true);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.LabelField(" ", fit.Mesh.VertexCount.ToString("N0") + " vertices, " + fit.Mesh.TriangleCount.ToString("N0") + " triangles", EditorStyles.miniLabel);
            if (!fit.Solver.IsGpu && fit.Mesh.VertexCount > 6000)
            {
                EditorGUILayout.HelpBox("The CPU solver is slow at this many vertices. Fit at a lower Mesh Level and raise it before baking.", MessageType.Warning);
            }
        }

        /// <summary>Clothes the body wears under these: the cloth drapes over them.</summary>
        private static UMAWardrobeRecipe wornToAdd;

        private void DrawWornUnder(DesignerSession session)
        {
            List<UMAWardrobeRecipe> worn = session.FitData.wornUnder;
            EditorGUILayout.LabelField(new GUIContent("Worn Underneath", "Clothes the body wears under the clothes being made. The cloth drapes over them; the sizes still follow the body itself."), new GUIContent(worn.Count == 0 ? "nothing" : worn.Count + " recipes"));
            for (int i = 0; i < worn.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16f);
                GUILayout.Label(worn[i] != null ? worn[i].name : "(missing)", EditorStyles.miniLabel);
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    var list = new List<UMAWardrobeRecipe>(worn);
                    list.RemoveAt(i);
                    session.SetWornUnder(list);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16f);
            wornToAdd = (UMAWardrobeRecipe)EditorGUILayout.ObjectField(wornToAdd, typeof(UMAWardrobeRecipe), false);
            using (new EditorGUI.DisabledScope(wornToAdd == null))
            {
                if (GUILayout.Button("Wear", EditorStyles.miniButton, GUILayout.Width(60f)))
                {
                    var list = new List<UMAWardrobeRecipe>(worn) { wornToAdd };
                    wornToAdd = null;
                    session.SetWornUnder(list);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (worn.Count > 0)
            {
                bool over = EditorGUILayout.Toggle(new GUIContent("  Size over Them", "Measure the chest, waist and hips round the clothes worn underneath, so the sizes fit over them (a jacket over a shirt). Off: the sizes follow the body itself."), session.FitData.sizeOverWorn);
                if (over != session.FitData.sizeOverWorn)
                {
                    Undo.RecordObject(session.FitData, "Size over Worn Clothes");
                    session.FitData.sizeOverWorn = over;
                    session.CaptureBody();
                    session.RebuildCloth(false);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawToolsSection(DesignerSession session)
        {
            if (!Section("Tools", "Cloth Tools")) return;
            int toolIndex = Mathf.Max(0, Array.IndexOf(toolOrder, session.Tool));
            // As many tools a row as fit the panel (it is a third of the Scene view, 320 to 460 px): a fixed five a
            // row was wider than a narrow panel and pushed everything in it off the right edge.
            int pickedTool = GUILayout.SelectionGrid(toolIndex, toolContents, ToolColumns(panelWidth), GUILayout.MaxWidth(panelWidth));
            if (pickedTool != toolIndex) { session.Tool = toolOrder[pickedTool]; SceneView.RepaintAll(); }
            if (session.Tool == ClothTool.Place)
            {
                DrawPlaceTool(session);
                return;
            }
            if (session.Tool == ClothTool.Size)
            {
                DrawSizeTool(session);
                return;
            }
            if (session.Tool == ClothTool.Measure)
            {
                GUILayout.Label("Click two points on the cloth or the body. A third click starts again.", wrapStyle);
                if (session.MeasureHasA && session.MeasureHasB) EditorGUILayout.LabelField("Distance", session.MeasureDistanceCm.ToString("0.0") + " cm");
                if (session.MeasureHasA)
                {
                    EditorGUILayout.LabelField(new GUIContent("Round the Cloth", "How far round the cloth is at the first point's height: the tape measure round the outside."), session.MeasureClothGirthCm > 0f ? session.MeasureClothGirthCm.ToString("0.0") + " cm" : "no cloth there");
                    EditorGUILayout.LabelField(new GUIContent("Round the Body", "The body at the same height, arms left out."), session.MeasureBodyGirthCm > 0f ? session.MeasureBodyGirthCm.ToString("0.0") + " cm" : "-");
                    if (session.MeasureClothGirthCm > 0f && session.MeasureBodyGirthCm > 0f) EditorGUILayout.LabelField("Ease", (session.MeasureClothGirthCm - session.MeasureBodyGirthCm).ToString("+0.0;-0.0") + " cm");
                }
                return;
            }
            if (session.Tool == ClothTool.BodyRegion)
            {
                DrawBodyRegion(session);
                return;
            }
            if (session.Tool == ClothTool.Dart || session.Tool == ClothTool.Pleat || session.Tool == ClothTool.DrawPiece)
            {
                GUILayout.Label(ClothToolInfo.Tooltip(session.Tool), wrapStyle);
                if (session.Tool == ClothTool.DrawPiece)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(session.BodyOutline.Count + " points", EditorStyles.miniLabel);
                    using (new EditorGUI.DisabledScope(session.BodyOutline.Count < 3))
                    {
                        if (GUILayout.Button("Finish Piece", GUILayout.Width(100f))) session.FinishBodyPiece();
                    }
                    using (new EditorGUI.DisabledScope(session.BodyOutline.Count == 0))
                    {
                        if (GUILayout.Button("Clear", GUILayout.Width(60f))) { session.BodyOutline.Clear(); SceneView.RepaintAll(); }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (!string.IsNullOrEmpty(session.PatternToolMessage)) EditorGUILayout.HelpBox(session.PatternToolMessage, MessageType.Info);
                return;
            }
            session.BrushRadius = EditorGUILayout.Slider(new GUIContent("Radius (cm)", "How much cloth around the point is moved, pinned, painted or rolled."), session.BrushRadius * 100f, 1f, 30f) / 100f;
            session.BrushStrength = EditorGUILayout.Slider(new GUIContent("Strength"), session.BrushStrength, 0.05f, 1f);
            if (session.Tool == ClothTool.FitBrush)
            {
                DrawFitBrush(session);
                return;
            }
            if (session.Tool == ClothTool.Roll)
            {
                GUILayout.Label("Press on the cloth that belongs in front and drag over where it sinks into, or lies flush with, the cloth behind it. It is lifted out until it is " + (session.RollGapM * 1000f).ToString("0.#") + " mm clear (the Cloth Thickness, at least 1 mm). The stroke keeps to the piece and side you pressed on, even where other cloth pokes through it, and stops at folds. Each stroke is one Undo step; Escape puts it back. Rolling does not start the simulation: roll as a last step, then Keep Drape. A folded seam's two pieces (a collar's leaf and stand) are not kept apart by the simulation, so simulating again may undo the rolling there.", wrapStyle);
                if (session.LastRollLifted > 0) GUILayout.Label("Last stroke lifted " + session.LastRollLifted + " points.", EditorStyles.miniLabel);
                return;
            }
            if (session.Tool == ClothTool.Pin)
            {
                session.NewPinKind = (PinKind)EditorGUILayout.EnumPopup(new GUIContent("Pins Follow", "Body: the pin moves with the body when its shape changes. Placement: it holds a point in space."), session.NewPinKind);
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(session.FitData.pins.Count + " pins", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(session.FitData.pins.Count == 0))
            {
                if (GUILayout.Button("Clear Pins", GUILayout.Width(90f))) session.ClearPins();
            }
            EditorGUILayout.EndHorizontal();
            if (session.Tool == ClothTool.Grab)
            {
                session.GrabOneLayer = EditorGUILayout.ToggleLeft(new GUIContent("Grab One Layer", "Move only the cloth on the layer of the piece you press on (its Layer on the Pattern Canvas), and hold the cloth on the other layers where it is while you drag: pull a bodice up to blouse over a belt on a higher layer, and the belt stays put."), session.GrabOneLayer);
            }
            GUILayout.Label(session.Tool == ClothTool.Grab
                ? "Drag the cloth in the Scene view. Release to let it settle; Escape puts it back. Alt-drag still orbits."
                : session.Tool == ClothTool.Pin ? "Click or drag on the cloth to pin it. Pins are stored on the pattern, so they survive a new mesh level."
                : "Click or drag on pinned cloth to release it.", wrapStyle);
            int broken = session.Fit.CountBrokenBodyPins();
            if (broken > 0) EditorGUILayout.HelpBox(broken + " body pins point at a part of the body that no longer exists. Erase and pin them again.", MessageType.Warning);
        }

        /// <summary>Where this panel's content starts in the stage's tools area, and the width it has (GUI points), measured on a repaint.</summary>
        private float contentLeft, panelWidth = 300f;

        /// <summary>A button that may be narrower than its text, so a row of them never widens the panel.</summary>
        private static readonly GUILayoutOption Shrinkable = GUILayout.MinWidth(24f);

        /// <summary>How wide a row of buttons would like to be (GUI points).</summary>
        private static float RowWidth(params string[] labels)
        {
            GUIStyle style = GUI.skin.button;
            float width = 0f;
            foreach (string label in labels) width += style.CalcSize(new GUIContent(label)).x + Mathf.Max(style.margin.left, style.margin.right);
            return width;
        }

        /// <summary>How many tool buttons fit a row of a panel this wide, each as wide as the widest name needs.</summary>
        internal static int ToolColumns(float width)
        {
            GUIStyle style = GUI.skin.button;
            float widest = 0f;
            foreach (GUIContent c in toolContents) widest = Mathf.Max(widest, style.CalcSize(new GUIContent(c.text)).x);
            float spacing = Mathf.Max(style.margin.left, style.margin.right);
            return Mathf.Clamp(Mathf.FloorToInt((width + spacing) / (widest + spacing)), 2, toolContents.Length);
        }

        /// <summary>The paint modes as buttons, shared with the Fit Tools toolbar (<see cref="FitPaintInfo"/>).</summary>
        private static readonly GUIContent[] paintModes = FitPaintInfo.PanelContents();

        /// <summary>The paint modes in the order their buttons are shown (Erase last).</summary>
        private static readonly FitPaintMode[] paintOrder = FitPaintInfo.Order;

        private void DrawFitBrush(DesignerSession session)
        {
            int shown = Mathf.Max(0, Array.IndexOf(paintOrder, session.PaintMode));
            // Seven buttons: on one row when the panel is wide enough, otherwise four and three.
            var names = new string[paintModes.Length];
            for (int i = 0; i < names.Length; i++) names[i] = paintModes[i].text;
            int columns = RowWidth(names) <= panelWidth ? paintModes.Length : 4;
            int picked = GUILayout.SelectionGrid(shown, paintModes, columns, GUILayout.MaxWidth(panelWidth));
            session.PaintMode = paintOrder[picked];
            GUILayout.Label(paintModes[picked].tooltip + (session.PaintMode != FitPaintMode.Erase ? " Ctrl-drag takes this paint off." : ""), wrapStyle);
            GUILayout.Label("Green hugs, blue smooths, orange puffs, purple slims, teal stiffens, brown weights. Each stroke is one Undo step; the cloth simulates when you let go. The paint is kept on the pattern, so it survives a new mesh level, and it bakes into the drape.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.BeginHorizontal();
            int painted = 0;
            foreach (FitPaintPoint p in session.FitData.paint) if (p.hug > 0f || p.smooth > 0f || p.inflate != 0f || p.stiff > 0f || p.weight > 0f) painted++;
            GUILayout.Label(painted + " painted points", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(session.FitData.paint.Count == 0))
            {
                if (GUILayout.Button("Clear Paint", GUILayout.Width(90f))) session.ClearPaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBodyRegion(DesignerSession session)
        {
            GUILayout.Label(ClothToolInfo.Tooltip(ClothTool.BodyRegion), wrapStyle);
            session.RegionRadius = EditorGUILayout.Slider(new GUIContent("Radius (cm)", "How much of the body each dab of the brush paints."), session.RegionRadius * 100f, 1f, 20f) / 100f;
            session.RegionMirror = EditorGUILayout.Toggle(new GUIContent("Mirror", "Paint the other side of the body too."), session.RegionMirror);
            session.RegionEase = EditorGUILayout.Slider(new GUIContent("Ease (%)", "How much bigger (or, negative, smaller) than the body the pieces are cut: below 0 for stretch fabrics that grip, above 0 for room."), session.RegionEase * 100f, -15f, 20f) / 100f;
            string[] fabrics = FabricPresets.Names();
            int fabric = Mathf.Max(0, Array.IndexOf(fabrics, session.RegionFabric));
            fabric = EditorGUILayout.Popup(new GUIContent("Fabric", "What the new pieces are made of."), fabric, fabrics);
            session.RegionFabric = fabrics[fabric];
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(session.BodyRegion.Count + " body triangles painted", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(session.BodyRegion.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Make Pieces", "Make pattern pieces from the painted region and add them to the pattern (a new pattern if none is open)."), GUILayout.Width(100f))) session.MakeRegionPieces();
                if (GUILayout.Button("Clear", GUILayout.Width(60f))) session.ClearRegion();
            }
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(session.RegionMessage)) EditorGUILayout.HelpBox(session.RegionMessage, MessageType.Info);
        }

        /// <summary>Lists the hair slots UMA would not hold down and offers to make them smooshable (only those slots are saved).</summary>
        private static void CheckHair()
        {
            List<SlotDataAsset> hair = HairSmoosh.HairNotSmooshable();
            if (hair.Count == 0) { EditorUtility.DisplayDialog("Holds Hair Down", "Every hair slot in the Global Library is smooshable and tagged '" + HairSmoosh.SmooshableTag + "'.", "OK"); return; }
            var names = new List<string>();
            foreach (SlotDataAsset s in hair) names.Add(s.slotName);
            string list = string.Join("\n", names.Count > 25 ? names.GetRange(0, 25) : names) + (names.Count > 25 ? "\n... and " + (names.Count - 25) + " more" : "");
            if (EditorUtility.DisplayDialog("Holds Hair Down", hair.Count + " hair slots will not be held down by a hat:\n\n" + list + "\n\nMake them smooshable and tag them '" + HairSmoosh.SmooshableTag + "'? Only these slot assets are changed and saved.", "Make Smooshable", "Cancel"))
            {
                int changed = HairSmoosh.MakeSmooshable(hair);
                EditorUtility.DisplayDialog("Holds Hair Down", changed + " hair slots are now smooshable.", "OK");
            }
        }

        private void DrawPlaceTool(DesignerSession session)
        {
            GUILayout.Label("Click a piece to select it, then drag it round the body or up and down. Shift-drag up or down moves it out from or in towards the body. On an arm or leg, Ctrl-drag tilts its tube and Ctrl+Shift-drag moves its far end in or out. Escape during a drag puts it back.", wrapStyle);
            bool guide = EditorGUILayout.Toggle(new GUIContent("Show Layout Guide", "Draw the selected piece's layout shape (the oval, cone or tube it is laid on before sewing), with handles: the yellow dot turns it round the body or limb, Top moves it up or down (along an arm), Distance moves it out or in, and on arms and legs End Distance moves its far end and Tilt tilts it. Selecting a piece here also selects it on the Pattern Canvas, and the other way round."), DesignerSession.ShowLayoutGuide);
            if (guide != DesignerSession.ShowLayoutGuide) { DesignerSession.ShowLayoutGuide = guide; SceneView.RepaintAll(); }
            if (session.Fit.SimTime > 0f)
            {
                EditorGUILayout.HelpBox("Moving a piece lays all the pieces out again. Simulate afterwards.", MessageType.None);
            }
            int index = session.PlacePiece;
            if (index < 0 || index >= session.Pattern.pieces.Count)
            {
                GUILayout.Label(session.HoverPiece >= 0 ? "Click to select '" + session.Pattern.pieces[session.HoverPiece].name + "'." : "No piece selected.", EditorStyles.miniLabel);
                return;
            }
            DrawPlacementFields(session, index, true);
        }

        private void DrawSizeTool(DesignerSession session)
        {
            GUILayout.Label("Click a piece for its handles: drag a side handle (red) out or in to make it wider or narrower, both sides moving, or the hem handle (green) down or up to make it longer or shorter. Dragging anywhere on the piece works too, across or down it. The cloth shows the new size as you drag; release to keep it and it settles into it, or press Escape to put it back. Shift keeps its proportions. Or type the size here.", wrapStyle);
            int index = session.PlacePiece;
            if (index < 0 || index >= session.Pattern.pieces.Count)
            {
                GUILayout.Label(session.HoverPiece >= 0 ? "Click to select '" + session.Pattern.pieces[session.HoverPiece].name + "'." : "No piece selected.", EditorStyles.miniLabel);
                if (!string.IsNullOrEmpty(session.SizeMessage)) EditorGUILayout.HelpBox(session.SizeMessage, MessageType.Info);
                return;
            }
            PatternPiece piece = session.Pattern.pieces[index];
            EditorGUILayout.LabelField("Piece", piece.name, EditorStyles.boldLabel);
            Vector2 size = session.Sizing ? session.SizeDragTo : session.CutSizeOf(index);
            float shownWidth = Mathf.Round(size.x * 10f) / 10f, shownLength = Mathf.Round(size.y * 10f) / 10f;
            float width = EditorGUILayout.DelayedFloatField(new GUIContent("Width (cm)", "Across the piece as it is cut on this body: the widest point of its outline, sized by the pattern's sizes."), shownWidth);
            SizeNote(session.SizeNote(index, DimensionKind.Width));
            float length = EditorGUILayout.DelayedFloatField(new GUIContent("Length (cm)", "Down the piece as it is cut on this body, from its top to its hem. The top stays where it is laid."), shownLength);
            SizeNote(session.SizeNote(index, DimensionKind.Length));
            bool matching = EditorGUILayout.ToggleLeft(new GUIContent("Resize Matching Pieces", "Also resize the pieces that share a size with this one (the other sleeve, a belt's other half) the same way, so the seams between them still match. A width or length that one of the pattern's sizes sets changes that size, and so every piece it sizes, either way."), DesignerSession.SizeMatchingPieces);
            if (matching != DesignerSession.SizeMatchingPieces) DesignerSession.SizeMatchingPieces = matching;
            if (!session.Sizing && (!Mathf.Approximately(width, shownWidth) || !Mathf.Approximately(length, shownLength)))
            {
                // The field left alone keeps its exact value, so rounding for display changes nothing.
                session.ResizePiece(index, Mathf.Approximately(width, shownWidth) ? size.x : width, Mathf.Approximately(length, shownLength) ? size.y : length);
                GUIUtility.ExitGUI();
            }
            if (!string.IsNullOrEmpty(session.SizeMessage)) EditorGUILayout.HelpBox(session.SizeMessage, MessageType.Info);
        }

        private static void SizeNote(string note)
        {
            if (!string.IsNullOrEmpty(note)) GUILayout.Label("    " + note + ".", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Faces, Above Shoulders and Distance for one piece, editing the placement in effect.</summary>
        public static void DrawPlacementFields(DesignerSession session, int index, bool showName)
        {
            PatternPiece piece = session.Pattern.pieces[index];
            PiecePlacement current = session.EditablePlacement(index, out _);
            if (showName) EditorGUILayout.LabelField("Piece", piece.name, EditorStyles.boldLabel);
            var part = (BodyPart)EditorGUILayout.EnumPopup(new GUIContent("Body Part", "What the piece is laid round before sewing: the torso, the head, an arm (sleeves) or a leg (trousers, socks)."), current.part);
            bool arm = PiecePlacement.IsArm(part);
            float angle = EditorGUILayout.DelayedFloatField(new GUIContent("Faces (deg)", arm
                ? "Where the piece is laid round the arm: 0 the top of the arm, 90 its front, 180 underneath."
                : "Where the piece is laid round the " + (part == BodyPart.Torso ? "body" : PiecePlacement.IsLeg(part) ? "leg" : "head") + ": 0 front, 180 back, 90 the body's left."), current.angle);
            string topLabel = PiecePlacement.TopLabel(part);
            float top = EditorGUILayout.DelayedFloatField(new GUIContent(topLabel + " (cm)", arm
                ? "How far the piece's top starts above the shoulder joint, along the arm. Negative starts it down the arm."
                : "How far the piece's top starts " + topLabel.ToLowerInvariant() + ". Negative starts it lower."), current.topAboveShoulderCm);
            float distance = EditorGUILayout.DelayedFloatField(new GUIContent("Distance (cm)", "How much farther from the body than the other pieces it starts. Use it for a piece that goes over another, like a pocket or a band."), current.distanceCm);
            float across = EditorGUILayout.DelayedFloatField(new GUIContent("Across (cm)", "Moves the piece sideways round its part (across it, when laid flat), towards its right as drawn. A piece is otherwise centred by its outline's middle."), current.acrossCm);
            bool flat = EditorGUILayout.Toggle(new GUIContent("Lay Flat", "Lay the piece flat beside its part, facing out, instead of wrapped round it. For pieces that wrap poorly, like the two halves of a sock."), current.flat);
            float tilt = current.tiltDeg, endDistance = current.endDistanceCm;
            if (PiecePlacement.IsLimb(part))
            {
                tilt = Mathf.Clamp(EditorGUILayout.DelayedFloatField(new GUIContent("Tilt (deg)", arm
                    ? "Turns the tube the piece is wrapped on about the shoulder joint: positive lowers the cuff end below the arm, negative raises it. It moves only as far as the arm still fits inside, so a close sleeve barely moves; give its end more room with End Distance first. Ctrl-drag the piece up or down to tilt it."
                    : "Turns the tube the piece is wrapped on about its top: positive moves the hem end out from the other leg, negative in. It moves only as far as the leg still fits inside. Ctrl-drag the piece up or down to tilt it."), current.tiltDeg),
                    -Arrangement.MaxTiltDeg, Arrangement.MaxTiltDeg);
                endDistance = EditorGUILayout.DelayedFloatField(new GUIContent("End Distance (cm)", (arm ? "How much farther out the cuff end starts than the top; negative brings it in towards the wrist. " : "How much farther out the hem end starts than the top; negative brings it in towards the ankle. ")
                    + "It changes evenly along the piece. A piece is never laid " + (arm ? "inside the clearance from the arm, or narrower than it is cut." : "closer than half a centimetre to the leg.") + " Ctrl+Shift-drag the piece up or down to change it."), current.endDistanceCm);
            }
            if (session.ClothReady)
            {
                bool frozen = EditorGUILayout.Toggle(new GUIContent("Freeze", "Hold this piece where it is while the rest is simulated: drape one layer, freeze it, then drape the next over it."), session.IsFrozen(index));
                if (frozen != session.IsFrozen(index)) { session.SetFrozen(index, frozen); GUIUtility.ExitGUI(); }
            }
            var next = current.Clone();
            next.part = part; next.angle = angle; next.topAboveShoulderCm = top; next.distanceCm = distance; next.acrossCm = across; next.flat = flat; next.tiltDeg = tilt; next.endDistanceCm = endDistance;
            if (!next.SameAs(current))
            {
                session.SetPlacement(index, next);
                GUIUtility.ExitGUI();
            }
            if (session.HasFitPlacement(index))
            {
                EditorGUILayout.HelpBox("This fit has its own placement for this piece, so the pattern's is not used here.", MessageType.Info);
                if (GUILayout.Button("Use the Pattern's Placement"))
                {
                    session.UsePatternPlacement(index);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawReportSection(DesignerSession session)
        {
            if (!Section("Report", "Fit Report", false)) return;
            if (!session.HasMetrics)
            {
                GUILayout.Label("Simulate to measure the fit.", EditorStyles.miniLabel);
                return;
            }
            FitMetrics m = session.Metrics;
            EditorGUILayout.LabelField("Open seams", m.maxStitchGapMm.ToString("0.0") + " mm at most");
            EditorGUILayout.LabelField("In the body", m.particlesInBody == 0 ? "none" : m.particlesInBody + " points, " + m.deepestMm.ToString("0.0") + " mm deep");
            EditorGUILayout.LabelField("Stretch", m.meanStretchPercent.ToString("0.0") + "% average, " + m.maxStretchPercent.ToString("0") + "% at most");
            EditorGUILayout.LabelField("Frame", session.Fit.LastStepMilliseconds.ToString("0.0") + " ms" + (session.Fit.Solver.IsGpu ? ", GPU " + (session.Fit.Solver.GpuBytes / 1048576f).ToString("0.0") + " MB" : ""));
        }

        private void DrawPublishSection(DesignerSession session)
        {
            if (!Section("Publish", "Publish")) return;
            if (GUILayout.Button(new GUIContent("Publish...", PublishClothesWindow.Tip), GUILayout.Height(26f)))
            {
                PublishClothesWindow.AskAndPublish(session);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.Space(2f);
            PublishSettings p = session.FitData.publish;
            EditorGUI.BeginChangeCheck();
            p.slotName = EditorGUILayout.TextField(new GUIContent("Name", "The slot, overlay and recipe are named after it."), p.slotName);
            p.material = (UMAMaterial)EditorGUILayout.ObjectField(new GUIContent("Material", "The UMA material the clothes are drawn with."), p.material, typeof(UMAMaterial), false);
            if (GarmentBaker.MaterialCullsBackFaces(p.material) && !p.addFacing) EditorGUILayout.HelpBox("This material hides back faces: the inside of the clothes will not show at the openings. Turn on Inside Facing below.", MessageType.Warning);
            RaceData race = session.FitData.race;
            List<string> slots = race != null && race.wardrobeSlots != null ? race.wardrobeSlots : new List<string>();
            if (slots.Count > 0)
            {
                int index = Mathf.Max(0, slots.IndexOf(p.wardrobeSlot));
                p.wardrobeSlot = slots[EditorGUILayout.Popup(new GUIContent("Wardrobe Slot"), index, slots.ToArray())];
            }
            else p.wardrobeSlot = EditorGUILayout.TextField("Wardrobe Slot", p.wardrobeSlot);
            EditorGUILayout.BeginHorizontal();
            p.folder = EditorGUILayout.TextField(new GUIContent("Folder"), p.folder);
            if (GUILayout.Button("...", GUILayout.Width(26f)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Folder for the clothes", "Assets", "");
                string data = Application.dataPath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(chosen) && chosen.Replace('\\', '/').StartsWith(data, StringComparison.Ordinal))
                {
                    p.folder = "Assets" + chosen.Replace('\\', '/').Substring(data.Length);
                }
            }
            EditorGUILayout.EndHorizontal();
            p.addToGlobalLibrary = EditorGUILayout.Toggle(new GUIContent("Add to Global Library", "Register the slot, overlay and recipe so characters and wardrobe tools find them by name."), p.addToGlobalLibrary);
            p.generateMeshHide = EditorGUILayout.Toggle(new GUIContent("Hide Body Underneath", "Also make a mesh hide for the body triangles the clothes fully cover. Off by default: loose clothes show skin at the openings."), p.generateMeshHide);
            p.makeUvCheckTexture = EditorGUILayout.Toggle(new GUIContent("UV Check Texture", "Draw the pieces' outlines, grain and names on the clothes, to check the UVs."), p.makeUvCheckTexture);
            if (HairSmoosh.HasHeadPieces(session.Pattern))
            {
                p.holdsHairDown = EditorGUILayout.Toggle(new GUIContent("Holds Hair Down", "Publish a plane across the openings of the pieces round the head, and tag the slot, so UMA squashes smooshable hair inside the hat or hood when a character is built."), p.holdsHairDown);
                if (p.holdsHairDown)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Check Hair...", "List the hair slots in the Global Library that UMA will not hold down (not smooshable, or not tagged), and offer to fix them."), GUILayout.Width(110f))) CheckHair();
                    EditorGUILayout.EndHorizontal();
                }
            }
            p.addFacing = EditorGUILayout.Toggle(new GUIContent("Inside Facing", "A turned-in copy of the cloth inside the neck, armholes and hem, facing inwards, so a material that hides back faces still shows cloth when you look into an opening."), p.addFacing);
            if (p.addFacing) p.facingDepthCm = EditorGUILayout.Slider(new GUIContent("  Facing Depth (cm)", "How far into the clothes the facing reaches from each opening, measured along the cloth."), p.facingDepthCm, 0.5f, 30f);
            p.addRim = EditorGUILayout.Toggle(new GUIContent("Rim", "A narrow band round each opening joining the outside to the facing, so the edge shows the cloth's thickness."), p.addRim);
            if (p.addRim) p.rimThicknessMm = EditorGUILayout.Slider(new GUIContent("  Rim Thickness (mm)", "How thick the edge looks: the facing lies this far inside the cloth."), p.rimThicknessMm, 0.5f, 15f);
            DrawSlotLevel(session, p);
            p.fullThickness = EditorGUILayout.Toggle(new GUIContent("Full Thickness", "Give the whole garment a thickness: a turned-in copy of all of it, joined at every opening. About twice the triangles; use it for thick cloth seen from both sides (coats, capes)."), p.fullThickness);
            if (p.fullThickness) p.thicknessMm = EditorGUILayout.Slider(new GUIContent("  Thickness (mm)"), p.thicknessMm, 0.5f, 15f);
            p.lodLevels = EditorGUILayout.IntSlider(new GUIContent("LOD Slots", "Coarser copies of the slot for distant characters (Name_LOD1, _LOD2 ...), each meshed at half again the edge length of the one before. UMA's Simple LOD component swaps them in by name."), p.lodLevels, 0, 3);
            p.addParts = EditorGUILayout.Toggle(new GUIContent("Buttons and Parts", "Add the pattern's buttons (tacks marked Button) and parts to the slot, moving with the cloth."), p.addParts);
            p.addTopstitch = EditorGUILayout.Toggle(new GUIContent("Topstitching", "Draw the seams' topstitching (set on each seam on the Pattern Canvas) into a texture overlay on the clothes."), p.addTopstitch);
            if (p.addTopstitch) p.topstitchColor = EditorGUILayout.ColorField(new GUIContent("  Thread Colour"), p.topstitchColor);
            p.bakeLooks = EditorGUILayout.Toggle(new GUIContent("Piece Looks", "Bake each look the pieces use (set per piece on the Pattern Canvas) into its own overlay, masked to those pieces, over the clothes' own: several fabrics on one slot and material, each recoloured on its own."), p.bakeLooks);
            DrawBodyShapes(session, p);
            p.clothMotion = EditorGUILayout.Toggle(new GUIContent("Cloth Motion", "Let the clothes move at run time with Unity's Cloth: a skirt or coat swings while cloth that is held by the body (a waistband, shoulders) stays. The slot gets a renderer of its own, so only the clothes are simulated, not the body. Add UMA's Physics Avatar to the character to give the cloth colliders."), p.clothMotion);
            if (p.clothMotion)
            {
                p.motionHeldCm = EditorGUILayout.Slider(new GUIContent("  Held Within (cm)", "Cloth this close to the body moves only with the bones."), p.motionHeldCm, 0f, 5f);
                p.motionGain = EditorGUILayout.Slider(new GUIContent("  Swing per cm", "How far cloth may swing for each centimetre of cloth between it and where it is held: 0.3 lets a hem 30 cm below the waist swing 9 cm."), p.motionGain, 0f, 1f);
                p.motionMaxCm = EditorGUILayout.Slider(new GUIContent("  Most Swing (cm)", "The most any cloth may move from where the bones put it."), p.motionMaxCm, 0f, 40f);
            }
            if (EditorGUI.EndChangeCheck()) session.MarkFitChanged();

            List<string> problems = PublishProblems(session);
            using (new EditorGUI.DisabledScope(problems.Count > 0))
            {
                if (GUILayout.Button(new GUIContent(session.FitData.output.slot != null ? "Bake Again and Try On" : "Bake and Try On",
                    problems.Count > 0 ? string.Join(" ", problems) : "Make the slot, overlay and wardrobe recipe and show them on a character of this race."), GUILayout.Height(26f)))
                {
                    session.BakeAndTryOn();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Publish and Sculpt",
                    problems.Count > 0 ? string.Join(" ", problems) : "Bake, save the pattern and fit, close the Clothes Designer and open Sculpt on a character wearing the clothes, to touch up small problems by hand. Sculpting edits the baked slot; baking again from the Clothes Designer replaces it, so sculpted changes would be lost.")))
                {
                    session.PublishAndSculpt();
                    GUIUtility.ExitGUI();
                }
            }
            // Painting is in the Publish window: Publish and Open in Overlay Painter, shown when it is installed.
            foreach (string problem in problems) GUILayout.Label("• " + problem, EditorStyles.wordWrappedMiniLabel);
            if (!string.IsNullOrEmpty(session.LastBakeError)) EditorGUILayout.HelpBox(session.LastBakeError, MessageType.Error);
            if (session.LastBake != null)
            {
                EditorGUILayout.HelpBox("Baked " + session.LastBake.Vertices.ToString("N0") + " vertices, " + session.LastBake.Triangles.ToString("N0") + " triangles.", MessageType.Info);
                foreach (string message in session.LastBake.Messages) EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
            if (session.FitData.output.recipe != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(session.TryOnVisible ? "Hide Try-On" : "Show Try-On"))
                {
                    if (session.TryOnVisible) session.HideTryOn(); else session.ShowTryOn();
                }
                using (new EditorGUI.DisabledScope(!session.TryOnVisible))
                {
                    bool raised = GUILayout.Toggle(session.TryOnArmsRaised, "Arms Raised", GUI.skin.button);
                    if (raised != session.TryOnArmsRaised) { session.TryOnArmsRaised = raised; session.RefreshTryOnView(); }
                }
                if (GUILayout.Button("Select Recipe")) { Selection.activeObject = session.FitData.output.recipe; EditorGUIUtility.PingObject(session.FitData.output.recipe); }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("View Fitting Body", "Point the Scene view at the body the clothes are fitted on."))) session.FrameFittingBody();
                using (new EditorGUI.DisabledScope(!session.TryOnVisible))
                {
                    if (GUILayout.Button(new GUIContent("View Try-On", "Point the Scene view at the character wearing the baked clothes."))) session.FrameTryOn();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.BeginChangeCheck();
                var clip = (AnimationClip)EditorGUILayout.ObjectField(new GUIContent("Try-On Animation", "An animation played on the try-on character, to see the clothes move. Scrub it with Time."), session.FitData.tryOnClip, typeof(AnimationClip), false);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(session.FitData, "Try-On Animation"); session.FitData.tryOnClip = clip; session.TryOnTime = 0f; session.RefreshTryOnView(); }
                if (session.FitData.tryOnClip != null && session.TryOnVisible)
                {
                    float t = EditorGUILayout.Slider(new GUIContent("  Time (s)"), session.TryOnTime, 0f, session.FitData.tryOnClip.length);
                    if (!Mathf.Approximately(t, session.TryOnTime)) { session.TryOnTime = t; session.RefreshTryOnView(); }
                    bool play = GUILayout.Toggle(session.TryOnPlaying, session.TryOnPlaying ? "Stop" : "Play", GUI.skin.button);
                    if (play != session.TryOnPlaying) session.TryOnPlaying = play;
                }
                if (session.TryOnVisible && session.TryOnArmsRaised)
                {
                    float angle = EditorGUILayout.Slider(new GUIContent("  Arm Angle (deg)", "Where the arms are, from hanging at the sides: 90 level, 180 straight up. Above about 30 degrees the shoulder blades and collarbones turn too, a third of the movement, as a body does."), session.TryOnArmAngle, 0f, 180f);
                    if (!Mathf.Approximately(angle, session.TryOnArmAngle)) { session.TryOnArmAngle = angle; session.RefreshTryOnView(); }
                }
            }
        }

        private List<string> publishProblems;

        /// <summary>
        /// What stops a bake, worked out at each Layout and reused for the events that follow it, so a Layout and
        /// its Repaint draw the same rows and the checks run once a frame rather than for every GUI event.
        /// </summary>
        private List<string> PublishProblems(DesignerSession session)
        {
            if (publishProblems == null || Event.current.type == EventType.Layout) publishProblems = GarmentBaker.Validate(session.Fit);
            return publishProblems;
        }

        private static readonly string[] slotLevelNames ={ "Same as the Drape", "Draft (3 cm)", "Medium (2 cm)", "Fine (1.5 cm)", "Very Fine (1 cm)", "Custom" };
        private static readonly int[] normalMapSizes = { 0, 512, 1024, 2048, 4096 };

        /// <summary>
        /// The slot's own mesh level, and the normal map that keeps a finer drape's folds. Drape fine, bake
        /// coarse: the slot stays light and the small folds still show.
        /// </summary>
        private void DrawSlotLevel(DesignerSession session, PublishSettings p)
        {
            int index = p.bakeAtOwnLevel ? 1 + (int)p.bakeMeshLevel : 0;
            int picked = EditorGUILayout.Popup(new GUIContent("Slot Mesh Level", "How finely the baked slot is meshed. Simulate at a fine Mesh Level for small folds, then bake a lighter slot here: it takes its shape from the drape."), index, slotLevelNames);
            if (picked != index)
            {
                p.bakeAtOwnLevel = picked > 0;
                if (picked > 0) p.bakeMeshLevel = (MeshLevel)(picked - 1);
            }
            if (p.bakeAtOwnLevel && p.bakeMeshLevel == MeshLevel.Custom)
            {
                p.bakeCustomEdgeLengthCm = EditorGUILayout.Slider(new GUIContent("  Slot Edge Length (cm)"), p.bakeCustomEdgeLengthCm, 0.3f, 6f);
            }
            p.slotTopology = (SlotTopology)EditorGUILayout.Popup(new GUIContent("Slot Topology",
                "Triangles: the slot is meshed as the cloth was simulated. Quads: remeshed from the pattern (as Retopo Planes does from a garment's flat layout) - a grid along each piece's grain and an edge loop along every outline, a few triangles where they meet. All Quads: only quads (made at twice the spacing, then each face split into quads). Seams, darts and lines stay matched, the UVs come from the pattern, and the drape is carried onto the new mesh. In the slot each quad is two triangles, split along its shorter diagonal."),
                (int)p.slotTopology, new[] { "Triangles", "Quads", "All Quads" });
            if (p.slotTopology != SlotTopology.Triangles)
            {
                p.writeQuadObj = EditorGUILayout.Toggle(new GUIContent("  Quad OBJ for Other Tools", "Also write the quads, as draped, to <Name>_Quads.obj beside the slot: one object per piece, sewn points joined, UVs from the pattern, X negated for right-handed tools such as Blender."), p.writeQuadObj);
            }
            float drapeEdge = session.Fit.Mesh != null ? session.Fit.Mesh.edgeLengthCm : 0f;
            float slotEdge = p.BakeEdgeLengthCm(drapeEdge);
            bool coarser = p.bakeAtOwnLevel && slotEdge > drapeEdge + 1e-3f;
            using (new EditorGUI.DisabledScope(!coarser))
            {
                p.bakeNormalMap = EditorGUILayout.Toggle(new GUIContent("Normal Map from Drape", "Keep the drape's finer folds in a normal map on the overlay, so the coarser slot still shows them."), p.bakeNormalMap);
                if (p.bakeNormalMap)
                {
                    int size = Mathf.Max(0, Array.IndexOf(normalMapSizes, p.normalMapSize));
                    string[] names = { "Texture Size (" + session.Pattern.uvLayout.bakeResolution + ")", "512", "1024", "2048", "4096" };
                    p.normalMapSize = normalMapSizes[EditorGUILayout.Popup(new GUIContent("  Normal Map Size", "Texture Size uses the size set on the Pattern Canvas's UVs tab."), size, names)];
                }
            }
            if (p.bakeAtOwnLevel)
            {
                string note = coarser
                    ? "Slot at " + slotEdge.ToString("0.##") + " cm from a " + drapeEdge.ToString("0.##") + " cm drape" + (p.bakeNormalMap ? "; finer folds go in the normal map." : ".")
                    : "The slot is not coarser than the drape (" + drapeEdge.ToString("0.##") + " cm). For a normal map, simulate at a finer Mesh Level.";
                GUILayout.Label(note, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawFilesSection(DesignerSession session)
        {
            if (!Section("Files", "Save and Open")) return;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!session.HasPattern))
            {
                if (GUILayout.Button(new GUIContent("Save Pattern" + (session.PatternDirty ? " *" : ""), "Save the pattern. The first time, choose where."), Shrinkable)) session.SavePattern(false);
                if (GUILayout.Button(new GUIContent("Save Pattern As", "Save an independent copy with a new asset. Fits keep pointing at the original."), Shrinkable)) session.SavePattern(true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!session.HasPattern))
            {
                if (GUILayout.Button(new GUIContent("Save Fit" + (session.FitDirty ? " *" : ""), "Save the fit: race, placement, pins, kept drape and publish settings."), Shrinkable)) session.SaveFit(false);
                if (GUILayout.Button("Save Fit As", Shrinkable)) session.SaveFit(true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            var openFit = (ClothesFit)EditorGUILayout.ObjectField(new GUIContent("Open Fit", "Open a saved fit with its pattern, pins and kept drape."), session.FitAsset, typeof(ClothesFit), false);
            if (EditorGUI.EndChangeCheck() && openFit != null && openFit != session.FitAsset && ConfirmLosingWork(session)) { session.OpenFit(openFit); session.FrameCloth(); }
        }

        // ---- Fit Existing Slot --------------------------------------------------------------------

        private void DrawRefitPanel(VertexEditorStage stage, DesignerSession session)
        {
            SlotRefit refit = session.Refit;
            SlotDataAsset slot = FindFitSlot(stage, session);
            EditorGUILayout.LabelField("Slot", slot != null ? slot.slotName : "(none)");
            if (slot == null) { EditorGUILayout.HelpBox("The slot is not on the character.", MessageType.Error); return; }
            GUILayout.Label("1. Bind the slot to the body as it is now.\n2. Change the body's shape under Body Shape in the Vertex Editor window.\n3. Refit, look, then save.", wrapStyle);
            if (GUILayout.Button(new GUIContent(refit.IsBound ? "Bind Again" : "Bind to Body", "Remember where every vertex sits on the body.")))
            {
                refit.Bind(stage, session.Body, slot);
                session.ShowRefitPreview();
            }
            using (new EditorGUI.DisabledScope(!refit.IsBound))
            {
                if (GUILayout.Button(new GUIContent("Refit to Current Body", "Move the slot onto the body's current shape and relax it. Changing the body shape refits it too.")))
                {
                    refit.Conform(session.Body, 20, 0.004f);
                    session.ShowRefitPreview();
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Save as New Slot", "A copy of the slot with the refitted shape. Its UVs, vertex order, weights and blendshapes are kept.")))
                {
                    string path = refit.SaveAsNewSlot(stage, "Refit");
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path));
                }
                if (GUILayout.Button(new GUIContent("Save as Blendshape", "A copy of the slot with the refit as a blendshape, to dial in with matching DNA.")))
                {
                    string path = refit.SaveAsBlendshape(stage, "Refit");
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path));
                }
                EditorGUILayout.EndHorizontal();
            }
            foreach (string m in refit.Messages) EditorGUILayout.HelpBox(m, MessageType.Info);
        }

        private static SlotDataAsset FindFitSlot(VertexEditorStage stage, DesignerSession session)
        {
            if (session.FitSlotNames.Count == 0 || stage.Character == null || stage.Character.umaData == null) return null;
            foreach (SlotData s in stage.Character.umaData.umaRecipe.slotDataList)
            {
                if (s != null && s.asset != null && session.FitSlotNames.Contains(s.asset.slotName)) return s.asset;
            }
            return null;
        }

        // ---- Vertex Editor window -----------------------------------------------------------------

        public override void OnWindowGUI(VertexEditorStage stage)
        {
            EnsureStyles();
            DesignerSession session = GetSession(stage);
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            GUILayout.Label(session.Mode == Mode.FitExistingSlot ? "Fit Existing Slot" : "Clothes Designer", titleStyle);
            DrawBodyDetails(stage, session);
            DrawDna(stage, session);
            if (session.Mode == Mode.NewClothes && session.HasPattern)
            {
                DrawDimensions(session);
                DrawFabric(session);
                DrawPlacement(session);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBodyDetails(VertexEditorStage stage, DesignerSession session)
        {
            if (!Section("BodyDetails", "Body")) return;
            if (session.Body == null) { EditorGUILayout.HelpBox("The body could not be read.", MessageType.Error); return; }
            BodyLandmarks m = session.Body.Landmarks;
            EditorGUILayout.LabelField("Height", m.heightCm.ToString("0.0") + " cm");
            EditorGUILayout.LabelField("Chest", m.chestCm.ToString("0.0") + " cm at " + (m.chestY * 100f).ToString("0") + " cm");
            EditorGUILayout.LabelField("Waist", m.waistCm.ToString("0.0") + " cm at " + (m.waistY * 100f).ToString("0") + " cm");
            EditorGUILayout.LabelField("Hip", m.hipCm.ToString("0.0") + " cm at " + (m.hipY * 100f).ToString("0") + " cm");
            EditorGUILayout.LabelField("Top of the shoulders", (m.shoulderTopY * 100f).ToString("0") + " cm");
            EditorGUILayout.LabelField("Body", session.Body.SlotCount + " slots, " + session.Body.Surface.Triangles.Length.ToString("N0") + " triangles, read in " + session.Body.CaptureMilliseconds.ToString("0") + " ms");
            RaceData race = stage.CharacterRace;
            if (race != null && Mathf.Abs(m.heightCm / 100f - race.raceHeight) > 0.25f)
            {
                EditorGUILayout.HelpBox("The body's height differs from the race's Race Height by more than 25 cm. If the race is built at an unusual scale, pattern sizes will be off by the same amount.", MessageType.Warning);
            }
            if (GUILayout.Button("Capture Body Again")) session.CaptureBody();
        }

        private void DrawDna(VertexEditorStage stage, DesignerSession session)
        {
            if (!Section("Dna", "Body Shape (DNA)", false)) return;
            DynamicCharacterAvatar avatar = stage.Character;
            if (avatar == null || avatar.umaData == null) return;
            GUILayout.Label("Fit the same pattern on another body shape. Clothes are weighted on the body shown; save a fit for each shape.", wrapStyle);
            Dictionary<string, DnaSetter> dna = avatar.GetDNA();
            dnaFilter = EditorGUILayout.TextField("Search", dnaFilter);
            bool changed = false;
            foreach (KeyValuePair<string, DnaSetter> kv in dna)
            {
                if (!string.IsNullOrEmpty(dnaFilter) && kv.Key.IndexOf(dnaFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                float value = kv.Value.Get();
                float now = EditorGUILayout.Slider(ObjectNames.NicifyVariableName(kv.Key), value, 0f, 1f);
                if (!Mathf.Approximately(now, value)) { kv.Value.Set(now); changed = true; }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Body Shape"))
            {
                foreach (KeyValuePair<string, DnaSetter> kv in dna) kv.Value.Set(0.5f);
                changed = true;
            }
            EditorGUILayout.EndHorizontal();
            if (changed && session.FitData != null)
            {
                session.FitData.dna.Clear();
                foreach (KeyValuePair<string, DnaSetter> kv in dna)
                {
                    if (!Mathf.Approximately(kv.Value.Get(), 0.5f)) session.FitData.dna.Add(new DnaValue { name = kv.Key, value = kv.Value.Get() });
                }
                stage.RebuildCharacter(true);
                session.MarkFitChanged();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawDimensions(DesignerSession session)
        {
            if (!Section("Dimensions", "Sizes")) return;
            GUILayout.Label("What each size works out to on this body. Change the allowance (ease) to make the clothes looser or tighter.", wrapStyle);
            foreach (EvaluatedDimension d in session.Fit.Dimensions)
            {
                EditorGUILayout.LabelField(d.dimension.name, d.fromBody
                    ? d.measuredCm.ToString("0.0") + " + allowance = " + d.targetCm.ToString("0.0") + " cm" + (d.dimension.kind == DimensionKind.Width ? " per piece" : "")
                    : d.targetCm.ToString("0.0") + " cm (fixed)");
                float? current = session.FitData.AllowanceFor(d.dimension.id);
                float allowance = current ?? d.dimension.allowanceCm;
                float now = EditorGUILayout.DelayedFloatField("  Allowance (cm)", allowance);
                if (!Mathf.Approximately(now, allowance))
                {
                    Undo.RecordObject(session.FitData, "Allowance");
                    session.FitData.allowances.RemoveAll(a => a.dimensionId == d.dimension.id);
                    session.FitData.allowances.Add(new AllowanceOverride { dimensionId = d.dimension.id, allowanceCm = now });
                    session.RebuildCloth(false);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawFabric(DesignerSession session)
        {
            if (!Section("Fabric", "Fabric")) return;
            GUILayout.Label("Each piece has its own fabric, saved with the pattern: a cotton body can have an elastic band sewn on. Changes take effect at once and keep the drape.", wrapStyle);
            for (int i = 0; i < session.Pattern.pieces.Count; i++)
            {
                FabricField(session, session.Pattern.pieces[i], session.Pattern.pieces[i].name, $"Fabric.{i}");
            }
            if (session.Pattern.pieces.Count > 1 && GUILayout.Button(new GUIContent("Use the First Piece's Fabric for All", "Copy the fabric of '" + session.Pattern.pieces[0].name + "' to every piece.")))
            {
                Undo.RecordObject(session.Pattern, "Fabric for All Pieces");
                FabricSettings first = session.Pattern.pieces[0].fabric;
                foreach (PatternPiece piece in session.Pattern.pieces) piece.fabric.CopyValuesFrom(first);
                session.SyncToData();
                GUIUtility.ExitGUI();
            }

            GUILayout.Space(4f);
            GUILayout.Label("Simulation (this fit)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float damping = EditorGUILayout.Slider(new GUIContent("Damping", "How quickly movement dies down. Changes how fast the cloth settles, not where."), session.FitData.damping, 0f, 10f);
            float clearance = EditorGUILayout.Slider(new GUIContent("Clearance (mm)", "The gap kept between the cloth and the skin."), session.FitData.clearanceCm * 10f, 0f, 20f);
            float thickness = EditorGUILayout.Slider(new GUIContent("Cloth Thickness (mm)", "The least distance kept between pieces once they are sewn, so a collar lies on the shirt instead of through it. 0 lets pieces pass through each other. A piece does not collide with itself."), session.FitData.clothThicknessMm, 0f, 10f);
            ClothesFit fitData = session.FitData;
            GUILayout.Space(4f);
            GUILayout.Label(new GUIContent("Bone Weights (this fit)", "How the baked clothes get their bone weights from the body."), EditorStyles.boldLabel);
            var methods = (UMA.Editors.Weighting.UMAWeightMethod[])Enum.GetValues(typeof(UMA.Editors.Weighting.UMAWeightMethod));
            var methodLabels = new GUIContent[methods.Length];
            int methodIndex = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                methodLabels[i] = new GUIContent(UMA.Editors.Weighting.UMAWeightSettings.DisplayName(methods[i]), UMA.Editors.Weighting.UMAWeightSettings.Describe(methods[i]));
                if (methods[i] == fitData.weightMethod) methodIndex = i;
            }
            methodIndex = EditorGUILayout.Popup(new GUIContent("Weighting", "How the clothes get their bone weights from the body. Hover over a method for when to use it."), methodIndex, methodLabels);
            UMA.Editors.Weighting.UMAWeightMethod method = methods[methodIndex];
            GUILayout.Label(UMA.Editors.Weighting.UMAWeightSettings.Describe(method), EditorStyles.wordWrappedMiniLabel);
            bool torsoOnly = EditorGUILayout.Toggle(new GUIContent("Weights from Torso Only", "Pieces not laid round an arm take their bone weights from the body without the arms, so a vest's side does not follow the arm next to it. Pieces laid round an arm (sleeves) always take them from the arms."), fitData.donorsExcludeArms);
            float matchCm = fitData.weightMatchCm, matchAngle = fitData.weightMatchAngle;
            if (method == UMA.Editors.Weighting.UMAWeightMethod.RobustInpaint || method == UMA.Editors.Weighting.UMAWeightMethod.Auto || method == UMA.Editors.Weighting.UMAWeightMethod.SimulatedFit)
            {
                matchCm = EditorGUILayout.Slider(new GUIContent("  Match Distance (cm)", "Cloth this close to the body, facing the same way and not deep inside it, copies the body's weights. Cloth further out is filled in smoothly over the cloth from there."), matchCm, 0.5f, 15f);
                matchAngle = EditorGUILayout.Slider(new GUIContent("  Match Angle", "How far the cloth's normal may turn from the body's and still copy it (degrees)."), matchAngle, 5f, 90f);
            }
            int maxBones = EditorGUILayout.IntSlider(new GUIContent("  Max Bones", "The most bones a vertex keeps (Unity's default skin quality is 4)."), fitData.weightMaxBones, 1, 8);
            bool mirror = EditorGUILayout.Toggle(new GUIContent("  Mirror Left/Right", "Make the two sides' weights match: each vertex averages with its mirror image, Left and Right bones swapped by name. For clothes cut the same on both sides."), fitData.weightMirror);
            if (method == UMA.Editors.Weighting.UMAWeightMethod.SimulatedFit)
            {
                GUILayout.Label("The bake drapes the clothes in " + UMA.Editors.Weighting.UMAWeightPoses.StandardPoseCount + " poses (" + GarmentWeights.SettleFrames + " frames each) before weighting, so it takes longer.", EditorStyles.wordWrappedMiniLabel);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(fitData, "Simulation Settings");
                fitData.damping = damping;
                fitData.clearanceCm = clearance / 10f;
                fitData.clothThicknessMm = thickness;
                fitData.donorsExcludeArms = torsoOnly;
                fitData.weightMethod = method;
                fitData.weightMatchCm = matchCm;
                fitData.weightMatchAngle = matchAngle;
                fitData.weightMaxBones = maxBones;
                fitData.weightMirror = mirror;
                session.MarkFitChanged();
            }
        }

        // The race's DNA names, for the body shape popups: read once per race, not on every GUI event.
        private RaceData bodyShapeRace;
        private string[] bodyShapeDna = new string[0];

        /// <summary>
        /// Body Shapes: other DNA the clothes are fitted to as well as the fit's own, each baked as a blendshape on the
        /// slot that its DNA drives once the DNA asset has the effect for it.
        /// </summary>
        private void DrawBodyShapes(DesignerSession session, PublishSettings p)
        {
            ClothesFit fit = session.FitData;
            if (fit.bodyShapes == null) fit.bodyShapes = new List<BodyShapeTarget>();
            p.bakeBodyShapes = EditorGUILayout.Toggle(new GUIContent("Body Shapes", "Fit the clothes to other body shapes as well: for each, one DNA at the top or bottom of its range. The bake moves the slot with the body under it there and keeps what the bones do not carry (a bigger belly or chest under the cloth) as a blendshape on the slot named after the DNA (DNA_belly_Up). Every garment uses the same names, so one effect on the DNA drives them all."), p.bakeBodyShapes);
            if (!p.bakeBodyShapes) return;
            RaceData race = fit.race != null ? fit.race : session.Stage != null ? session.Stage.CharacterRace : null;
            if (race != bodyShapeRace) { bodyShapeRace = race; bodyShapeDna = BodyShapes.DnaNames(race).ToArray(); }
            EditorGUI.indentLevel++;
            if (bodyShapeDna.Length == 0)
            {
                EditorGUILayout.LabelField(" ", "The race has no DNA in UMA's DNA assets to drive shapes.", EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
                return;
            }
            string[] ends = { "Top (1)", "Bottom (0)" };
            for (int i = 0; i < fit.bodyShapes.Count; i++)
            {
                BodyShapeTarget t = fit.bodyShapes[i];
                EditorGUILayout.BeginHorizontal();
                int at = Mathf.Max(0, Array.IndexOf(bodyShapeDna, t.dna));
                t.dna = bodyShapeDna[EditorGUILayout.Popup(at, bodyShapeDna)];
                t.value = EditorGUILayout.Popup(t.value >= 0.5f ? 0 : 1, ends, GUILayout.Width(110f)) == 0 ? 1f : 0f;
                if (GUILayout.Button(new GUIContent("x", "Remove this body shape."), GUILayout.Width(22f))) { fit.bodyShapes.RemoveAt(i); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button(new GUIContent("Add Body Shape", "Fit the clothes to one more DNA at one end of its range.")))
            {
                string first = Array.IndexOf(bodyShapeDna, "belly") >= 0 ? "belly" : bodyShapeDna[0];
                fit.bodyShapes.Add(new BodyShapeTarget { dna = first, value = 1f });
            }
            var missing = new List<BodyShapeTarget>();
            foreach (BodyShapeTarget t in fit.bodyShapes) if (t != null && !BodyShapes.HasEffect(BodyShapes.FindDna(race, t.dna), BodyShapes.ShapeName(t.dna, t.value))) missing.Add(t);
            using (new EditorGUI.DisabledScope(missing.Count == 0))
            {
                if (GUILayout.Button(new GUIContent(missing.Count > 0 ? "Drive Shapes from DNA (" + missing.Count + ")" : "Shapes Driven by DNA",
                    "Add a blendshape effect for each body shape to its DNA asset, so a character's DNA sets the clothes' shape. This changes the shared DNA assets, for every character of the race and every garment with these shapes.")))
                {
                    var names = new List<string>();
                    foreach (BodyShapeTarget t in missing) names.Add(BodyShapes.ShapeName(t.dna, t.value));
                    if (EditorUtility.DisplayDialog("Drive Shapes from DNA", "Add blendshape effects to the DNA assets of '" + race.raceName + "' for:\n\n" + string.Join("\n", names) +
                        "\n\nThe DNA assets are shared: every character of the race, and every garment with these shapes, follows them.", "Add Effects", "Cancel"))
                    {
                        var skipped = new List<string>();
                        int added = BodyShapes.AddEffects(race, missing, skipped);
                        EditorUtility.DisplayDialog("Drive Shapes from DNA", "Added " + added + " blendshape effects to DNA assets." +
                            (skipped.Count > 0 ? "\n\nNot changed, because they have unsaved changes (save or revert them first): " + string.Join(", ", skipped) + "." : ""), "OK");
                    }
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// A piece's fabric: a preset popup, and the four values under a foldout. Changing a value makes it
        /// Custom. Used by the designer's window and the Pattern Canvas.
        /// </summary>
        public static void FabricField(DesignerSession session, PatternPiece piece, string label, string foldKey)
        {
            FabricSettings fabric = piece.fabric;
            string[] names = FabricPresets.Names();
            int index = FabricPresets.IndexOf(fabric);
            EditorGUILayout.BeginHorizontal();
            string prefKey = "UMA.ClothesDesigner." + foldKey;
            bool open = SessionState.GetBool(prefKey, false);
            bool nowOpen = EditorGUILayout.Foldout(open, label, true);
            if (nowOpen != open) SessionState.SetBool(prefKey, nowOpen);
            int picked = EditorGUILayout.Popup(index, names, GUILayout.MinWidth(90f));
            EditorGUILayout.EndHorizontal();
            if (picked != index && picked < FabricPresets.All.Count)
            {
                Undo.RecordObject(session.Pattern, "Fabric");
                fabric.CopyValuesFrom(FabricPresets.All[picked].Settings);
                session.SyncToData();
                GUIUtility.ExitGUI();
            }
            if (index < FabricPresets.All.Count) GUILayout.Label(FabricPresets.All[index].Description, EditorStyles.wordWrappedMiniLabel);
            if (!nowOpen) return;
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            float stretch = EditorGUILayout.Slider(new GUIContent("Stretch", "0 firm, like cotton or denim; 1 very elastic, like spandex."), fabric.Stretch, 0f, 1f);
            float softness = EditorGUILayout.Slider(new GUIContent("Softness", "0 stiff, like leather; 1 limp, like silk."), fabric.Softness, 0f, 1f);
            float weight = EditorGUILayout.Slider(new GUIContent("Weight (g/m²)", "As fabric is sold: shirting about 120, denim 450, leather 800."), fabric.GramsPerSquareMetre, 30f, 900f);
            float friction = EditorGUILayout.Slider(new GUIContent("Friction", "How much the cloth grips the body: 0 slides, 1 grips."), fabric.friction, 0f, 1f);
            // Woven cloth gives more across the grain than along it, and most on the bias; knits stretch most across.
            bool grain = EditorGUILayout.Toggle(new GUIContent("Stretch by Grain", "Stretch differently across the grain (the weft) and on the bias than along it (Stretch above). The piece's Grain sets the direction."), fabric.HasGrain);
            float cross = fabric.crossStretchCompliance, bias = fabric.biasStretchCompliance;
            if (grain)
            {
                float crossSlider = EditorGUILayout.Slider(new GUIContent("  Across the Grain", "0 firm to 1 very elastic, across the grain."), FabricSettings.StretchSlider(Mathf.Max(0f, cross >= 0f ? cross : fabric.stretchCompliance)), 0f, 1f);
                float biasSlider = EditorGUILayout.Slider(new GUIContent("  On the Bias", "0 firm to 1 very elastic, diagonally to the grain. Woven cloth gives most here."), FabricSettings.StretchSlider(Mathf.Max(0f, bias >= 0f ? bias : Mathf.Max(fabric.stretchCompliance, cross))), 0f, 1f);
                cross = FabricSettings.StretchFromSlider(crossSlider);
                bias = FabricSettings.StretchFromSlider(biasSlider);
            }
            else { cross = -1f; bias = -1f; }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(session.Pattern, "Fabric");
                if (!Mathf.Approximately(stretch, fabric.Stretch)) fabric.Stretch = stretch;
                if (!Mathf.Approximately(softness, fabric.Softness)) fabric.Softness = softness;
                if (!Mathf.Approximately(weight, fabric.GramsPerSquareMetre)) fabric.GramsPerSquareMetre = weight;
                fabric.friction = friction;
                fabric.crossStretchCompliance = cross;
                fabric.biasStretchCompliance = bias;
                FabricPreset preset = FabricPresets.Find(fabric.preset);
                if (preset == null || !preset.Settings.SameValues(fabric)) fabric.preset = FabricPresets.Custom;
                session.SyncToData();
            }
            EditorGUI.indentLevel--;
        }

        private void DrawPlacement(DesignerSession session)
        {
            if (!Section("Placement", "Piece Placement", false)) return;
            GUILayout.Label("Where each piece is laid round the body before sewing. The Place tool in the Scene view does the same by dragging.", wrapStyle);
            for (int i = 0; i < session.Pattern.pieces.Count; i++)
            {
                EditorGUILayout.LabelField(session.Pattern.pieces[i].name, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawPlacementFields(session, i, false);
                EditorGUI.indentLevel--;
            }
        }
    }
}
