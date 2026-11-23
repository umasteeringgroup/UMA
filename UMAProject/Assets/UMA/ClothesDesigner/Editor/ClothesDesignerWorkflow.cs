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
            EditorGUILayout.LabelField(new GUIContent("Body", "Measured round the chest, waist and hips, arms left out."),
                new GUIContent("chest " + m.chestCm.ToString("0") + ", waist " + m.waistCm.ToString("0") + ", hip " + m.hipCm.ToString("0") + " cm"));
        }

        private void DrawPatternSection(DesignerSession session)
        {
            if (!Section("Pattern", "Pattern")) return;
            EditorGUI.BeginChangeCheck();
            var picked = (ClothesPattern)EditorGUILayout.ObjectField(new GUIContent("Pattern" + (session.PatternDirty ? " *" : ""), "The sewing pattern being fitted. * means it has changes that are not saved."),
                session.PatternAsset, typeof(ClothesPattern), false);
            if (EditorGUI.EndChangeCheck() && ConfirmLosingWork(session)) session.OpenPattern(picked);
            if (session.Pattern != null && session.PatternAsset == null)
            {
                EditorGUILayout.LabelField(" ", "'" + session.Pattern.name + "' (not saved yet)", EditorStyles.miniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("New", "Start a blank pattern.")) && ConfirmLosingWork(session)) session.NewPattern(null);
            if (EditorGUILayout.DropdownButton(new GUIContent("Samples", "Start from a sample pattern: the sleeveless vest, tops, trousers, skirts, dresses, socks or a beanie."), FocusType.Passive))
            {
                var menu = new GenericMenu();
                void Add(string name)
                {
                    menu.AddItem(new GUIContent(name), false, () =>
                    {
                        if (!ConfirmLosingWork(session)) return;
                        session.NewPattern(SamplePatterns.Create(name));
                    });
                }
                Add("Sleeveless Vest");
                menu.AddSeparator("");
                foreach (var sample in SamplePatterns.All) Add(sample.name);
                menu.ShowAsContext();
            }
            using (new EditorGUI.DisabledScope(!session.HasPattern))
            {
                if (GUILayout.Button(new GUIContent("Pattern Canvas", "Draw and sew the pieces in a 2D window."))) PatternCanvasWindow.Open(session);
            }
            EditorGUILayout.EndHorizontal();
            if (!session.HasPattern)
            {
                EditorGUILayout.HelpBox("Start from one of the Samples, a new pattern, or pick a saved one above.", MessageType.Info);
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

        private static bool ConfirmLosingWork(DesignerSession session)
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
                ? (!fit.Sewn ? "Sewing " + Mathf.RoundToInt((1f - fit.Closure) * 100f) + "%" : "Settling")
                : (session.Settled ? "Settled" : (fit.SimTime <= 0f ? "Laid out round the body" : "Paused"));
            EditorGUILayout.LabelField("Cloth", status, EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(session.Running ? "Pause" : "Simulate", session.Running ? ClothFitButtons.PauseTip : ClothFitButtons.SimulateTip)))
            {
                session.SetRunning(!session.Running);
            }
            if (GUILayout.Button(new GUIContent("Step", ClothFitButtons.StepTip))) session.StepOnce();
            if (GUILayout.Button(new GUIContent("Reset", ClothFitButtons.ResetTip))) session.ResetToArrangement();
            if (GUILayout.Button(new GUIContent("Keep Drape", ClothFitButtons.KeepDrapeTip))) session.KeepDrape();
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
            int pickedTool = GUILayout.Toolbar(toolIndex, toolContents);
            if (pickedTool != toolIndex) { session.Tool = toolOrder[pickedTool]; SceneView.RepaintAll(); }
            if (session.Tool == ClothTool.Place)
            {
                DrawPlaceTool(session);
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
            session.BrushRadius = EditorGUILayout.Slider(new GUIContent("Radius (cm)", "How much cloth around the point is moved or pinned."), session.BrushRadius * 100f, 1f, 30f) / 100f;
            session.BrushStrength = EditorGUILayout.Slider(new GUIContent("Strength"), session.BrushStrength, 0.05f, 1f);
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
            GUILayout.Label(session.Tool == ClothTool.Grab
                ? "Drag the cloth in the Scene view. Release to let it settle; Escape puts it back. Alt-drag still orbits."
                : session.Tool == ClothTool.Pin ? "Click or drag on the cloth to pin it. Pins are stored on the pattern, so they survive a new mesh level."
                : "Click or drag on pinned cloth to release it.", wrapStyle);
            int broken = session.Fit.CountBrokenBodyPins();
            if (broken > 0) EditorGUILayout.HelpBox(broken + " body pins point at a part of the body that no longer exists. Erase and pin them again.", MessageType.Warning);
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
            if (EditorGUI.EndChangeCheck()) session.MarkFitChanged();

            List<string> problems = GarmentBaker.Validate(session.Fit);
            using (new EditorGUI.DisabledScope(problems.Count > 0))
            {
                if (GUILayout.Button(new GUIContent(session.FitData.output.slot != null ? "Bake Again and Try On" : "Bake and Try On",
                    problems.Count > 0 ? string.Join(" ", problems) : "Make the slot, overlay and wardrobe recipe and show them on a character of this race."), GUILayout.Height(26f)))
                {
                    session.BakeAndTryOn();
                    GUIUtility.ExitGUI();
                }
            }
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

        private static readonly string[] slotLevelNames = { "Same as the Drape", "Draft (3 cm)", "Medium (2 cm)", "Fine (1.5 cm)", "Very Fine (1 cm)", "Custom" };
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
                if (GUILayout.Button(new GUIContent("Save Pattern" + (session.PatternDirty ? " *" : ""), "Save the pattern. The first time, choose where."))) session.SavePattern(false);
                if (GUILayout.Button(new GUIContent("Save Pattern As", "Save an independent copy with a new asset. Fits keep pointing at the original."))) session.SavePattern(true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!session.HasPattern))
            {
                if (GUILayout.Button(new GUIContent("Save Fit" + (session.FitDirty ? " *" : ""), "Save the fit: race, placement, pins, kept drape and publish settings."))) session.SaveFit(false);
                if (GUILayout.Button("Save Fit As")) session.SaveFit(true);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.BeginChangeCheck();
            var openFit = (ClothesFit)EditorGUILayout.ObjectField(new GUIContent("Open Fit", "Open a saved fit with its pattern, pins and kept drape."), session.FitAsset, typeof(ClothesFit), false);
            if (EditorGUI.EndChangeCheck() && openFit != null && openFit != session.FitAsset && ConfirmLosingWork(session)) session.OpenFit(openFit);
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
            bool torsoOnly = EditorGUILayout.Toggle(new GUIContent("Weights from Torso Only", "The clothes take their bone weights from the body without the arms. Clothes with a piece laid round an arm (sleeves) always take them from the arms too."), session.FitData.donorsExcludeArms);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(session.FitData, "Simulation Settings");
                session.FitData.damping = damping;
                session.FitData.clearanceCm = clearance / 10f;
                session.FitData.clothThicknessMm = thickness;
                session.FitData.donorsExcludeArms = torsoOnly;
                session.MarkFitChanged();
            }
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
