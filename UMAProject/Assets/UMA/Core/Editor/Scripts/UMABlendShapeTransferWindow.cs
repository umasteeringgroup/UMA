using System;
using System.Collections.Generic;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>
    /// Dialog for <see cref="UMABlendShapeTransfer"/>: gives one or more slots the blend shapes of a
    /// race's body (or of other slots), with a live preview in the Scene view.
    /// </summary>
    public sealed class UMABlendShapeTransferWindow : EditorWindow
    {
        private enum ColorMode { Shaded, Movement, Influence, UnderTheSurface }

        private const string MenuPath = "UMA/Tools/Slot Tools/Transfer Blend Shapes";
        private const string SettingsPrefsKey = "UMA.BlendShapeTransfer.Settings";
        private const string RacePrefsKey = "UMA.BlendShapeTransfer.Race";
        private const double AutoUpdateDelay = 0.35;
        private static readonly string[] ColorModeLabels = { "Shaded", "Movement", "Influence", "Under the surface" };
        private static readonly string[] OutputLabels = { "The slots themselves", "Copies of the slots" };
        private static readonly Color BodyColor = new Color(0.74f, 0.64f, 0.58f);
        private static readonly Color[] SlotColors =
        {
            new Color(0.45f, 0.62f, 0.85f), new Color(0.55f, 0.78f, 0.52f), new Color(0.86f, 0.70f, 0.42f), new Color(0.74f, 0.55f, 0.82f),
            new Color(0.42f, 0.78f, 0.78f), new Color(0.86f, 0.55f, 0.55f), new Color(0.70f, 0.74f, 0.45f), new Color(0.60f, 0.60f, 0.72f),
        };

        private sealed class RaceSlotInfo
        {
            public string Name;
            public int ShapeCount;
            public bool IsBody;
        }

        // --- what the user chose (kept across reloads) ---------------------------------------------
        [SerializeField] private RaceData race;
        [SerializeField] private UMABlendShapeBodyShape bodyShape = UMABlendShapeBodyShape.AsTheRaceBuildsIt;
        [SerializeField] private List<string> sourceSlotNames = new List<string>();
        [SerializeField] private RaceData sourceSlotsFor;
        [SerializeField] private List<SlotDataAsset> extraSources = new List<SlotDataAsset>();
        [SerializeField] private List<string> selectedShapes = new List<string>();
        [SerializeField] private RaceData shapesChosenFor;
        [SerializeField] private string shapeFilter = string.Empty;
        [SerializeField] private List<SlotDataAsset> targets = new List<SlotDataAsset>();
        [SerializeField] private UMABlendShapeTransferSettings settings = new UMABlendShapeTransferSettings();
        [SerializeField] private bool writeCopies;
        [SerializeField] private string copySuffix = "_Shapes";
        [SerializeField] private DefaultAsset copyFolder;
        [SerializeField] private bool addCopiesToLibrary = true;
        [SerializeField] private bool showSourceSlots;
        [SerializeField] private bool showAdvanced;
        [SerializeField] private bool showReport = true;
        [SerializeField] private Vector2 scroll;
        [SerializeField] private Vector2 shapeScroll;
        [SerializeField] private Vector2 targetScroll;
        [SerializeField] private Vector2 reportScroll;
        [SerializeField] private string report = string.Empty;
        // preview
        [SerializeField] private bool previewOpen;
        [SerializeField] private string previewShape = string.Empty;
        [SerializeField] private float previewWeight = 1f;
        [SerializeField] private bool showBody = true;
        [SerializeField] private ColorMode colorMode = ColorMode.Shaded;
        [SerializeField] private bool autoUpdate = true;
        [SerializeField] private bool animate;
        [SerializeField] private Vector3 previewOffset;

        // --- derived (rebuilt on demand) -------------------------------------------------------------
        [NonSerialized] private UMABlendShapeRigContext context;
        [NonSerialized] private string contextProblem;
        [NonSerialized] private List<RaceSlotInfo> raceSlots = new List<RaceSlotInfo>();
        // Shapes the race's built body still carries, and every shape its base slots have.
        [NonSerialized] private readonly HashSet<string> liveShapes = new HashSet<string>();
        [NonSerialized] private readonly HashSet<string> raceShapes = new HashSet<string>();
        [NonSerialized] private UMABlendShapeSource source;
        [NonSerialized] private readonly Dictionary<SlotDataAsset, UMABlendShapeSlotSpace> spaces = new Dictionary<SlotDataAsset, UMABlendShapeSlotSpace>();
        [NonSerialized] private readonly Dictionary<SlotDataAsset, string> spaceProblems = new Dictionary<SlotDataAsset, string>();
        [NonSerialized] private List<UMABlendShapeTransferResult> results;
        [NonSerialized] private bool resultsStale = true;
        [NonSerialized] private double lastChange;
        [NonSerialized] private UMABlendShapeTransferPreview preview;
        [NonSerialized] private bool previewDirty = true;
        [NonSerialized] private double animateStart;

        // --- opening ---------------------------------------------------------------------------------

        [MenuItem(MenuPath)]
        public static UMABlendShapeTransferWindow Open()
        {
            var window = GetWindow<UMABlendShapeTransferWindow>("Blend Shape Transfer");
            window.minSize = new Vector2(430f, 480f);
            window.Show();
            return window;
        }

        [MenuItem("Assets/UMA/Transfer Blend Shapes To Selection", false, 2001)]
        private static void OpenForSelection()
        {
            UMABlendShapeTransferWindow window = Open();
            window.AddTargets(Selection.objects);
        }

        [MenuItem("Assets/UMA/Transfer Blend Shapes To Selection", true)]
        private static bool CanOpenForSelection()
        {
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (selected is SlotDataAsset || selected is UMATextRecipe)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnEnable()
        {
            string json = EditorPrefs.GetString(SettingsPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, settings); } catch (Exception) { settings = new UMABlendShapeTransferSettings(); }
            }
            if (race == null)
            {
                string path = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(RacePrefsKey, string.Empty));
                if (!string.IsNullOrEmpty(path))
                {
                    race = AssetDatabase.LoadAssetAtPath<RaceData>(path);
                }
            }
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorPrefs.SetString(SettingsPrefsKey, JsonUtility.ToJson(settings));
            ClosePreview();
        }

        private void OnUndoRedo()
        {
            // An undo may have put a slot's mesh data back.
            spaces.Clear();
            spaceProblems.Clear();
            source = null;
            MarkChanged();
            Repaint();
        }

        /// <summary>The race whose body the shapes come from. For tools and tests.</summary>
        public RaceData Race
        {
            get => race;
            set
            {
                if (race != value)
                {
                    race = value;
                    OnRaceChanged();
                }
            }
        }

        /// <summary>The slots that receive the shapes. For tools and tests.</summary>
        public IReadOnlyList<SlotDataAsset> Targets => targets;

        /// <summary>What the last preview or transfer worked out. For tools and tests.</summary>
        public IReadOnlyList<UMABlendShapeTransferResult> Results => results;

        public UMABlendShapeTransferSettings Settings => settings;

        public IList<string> SelectedShapes => selectedShapes;

        // --- state -----------------------------------------------------------------------------------

        private void OnRaceChanged()
        {
            context = null;
            source = null;
            spaces.Clear();
            spaceProblems.Clear();
            if (race != null)
            {
                EditorPrefs.SetString(RacePrefsKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(race)));
            }
            MarkChanged();
        }

        private void MarkChanged()
        {
            resultsStale = true;
            previewDirty = true;
            lastChange = EditorApplication.timeSinceStartup;
        }

        private bool EnsureContext()
        {
            if (race == null)
            {
                context = null;
                contextProblem = "Choose the race whose blend shapes the slots should get.";
                return false;
            }
            if (context != null && context.Race == race)
            {
                return true;
            }
            source = null;
            raceSlots.Clear();
            liveShapes.Clear();
            raceShapes.Clear();
            if (!UMABlendShapeRigContext.TryCreate(race, out context, out contextProblem))
            {
                context = null;
                return false;
            }
            foreach (UMAMeshHideAutoGenerator.BodySlot body in context.GetBodySlots())
            {
                if (body.BaseSlot == null)
                {
                    continue;
                }
                UMABlendShape[] shapes = body.BaseSlot.meshData.blendShapes;
                liveShapes.UnionWith(body.BuiltBlendShapeNames);
                if (shapes != null)
                {
                    foreach (UMABlendShape shape in shapes)
                    {
                        if (shape != null) raceShapes.Add(shape.shapeName);
                    }
                }
                raceSlots.Add(new RaceSlotInfo
                {
                    Name = body.BaseSlot.slotName,
                    ShapeCount = shapes != null ? shapes.Length : 0,
                    IsBody = UMALegacyContentConverter.IsBodySlot(body.BaseSlot),
                });
            }
            if (sourceSlotsFor != race)
            {
                // Default: the body's skin. Eyes, mouth, lashes and brows rarely carry anything clothing should follow.
                sourceSlotNames.Clear();
                foreach (RaceSlotInfo info in raceSlots)
                {
                    if (info.IsBody && info.ShapeCount > 0)
                    {
                        sourceSlotNames.Add(info.Name);
                    }
                }
                sourceSlotsFor = race;
            }
            return true;
        }

        private bool EnsureSource()
        {
            if (!EnsureContext())
            {
                return false;
            }
            if (source != null)
            {
                return true;
            }
            source = UMABlendShapeSource.FromRace(context, bodyShape, new HashSet<string>(sourceSlotNames));
            foreach (SlotDataAsset extra in extraSources)
            {
                if (extra == null)
                {
                    continue;
                }
                if (UMABlendShapeSlotSpace.TryCreate(extra, context, out UMABlendShapeSlotSpace space, out string reason))
                {
                    source.Add(space);
                }
                else
                {
                    source.Messages.Add($"Source slot '{extra.slotName}' was left out: {reason}");
                }
            }
            source.Build();
            if (shapesChosenFor != race)
            {
                // Default: the shapes characters of the race can dial in.
                selectedShapes.Clear();
                foreach (UMABlendShapeSource.Shape shape in source.Shapes)
                {
                    if (IsLive(shape.Name))
                    {
                        selectedShapes.Add(shape.Name);
                    }
                }
                shapesChosenFor = race;
            }
            return true;
        }

        /// <summary>
        /// True for a shape characters of the race can dial in: the race's built body still carries it,
        /// or it comes from a slot that is not part of the race's body.
        /// </summary>
        private bool IsLive(string shapeName)
        {
            return PrebakedValue(shapeName) == null && (liveShapes.Contains(shapeName) || !raceShapes.Contains(shapeName));
        }

        /// <summary>The weight the race bakes a blend shape into its body with, or null when it does not.</summary>
        private float? PrebakedValue(string shapeName)
        {
            if (race == null || race.PrebakedBlendshapes == null)
            {
                return null;
            }
            foreach (SlotBurnOptions option in race.PrebakedBlendshapes)
            {
                if (option != null && option.BlendShape == shapeName)
                {
                    return option.value;
                }
            }
            return null;
        }

        /// <summary>The selected shapes the source really has, in source order.</summary>
        private List<string> ShapesToTransfer()
        {
            var names = new List<string>();
            if (source == null)
            {
                return names;
            }
            foreach (UMABlendShapeSource.Shape shape in source.Shapes)
            {
                if (selectedShapes.Contains(shape.Name))
                {
                    names.Add(shape.Name);
                }
            }
            return names;
        }

        private List<SlotDataAsset> DistinctTargets()
        {
            var list = new List<SlotDataAsset>();
            foreach (SlotDataAsset slot in targets)
            {
                if (slot != null && !list.Contains(slot))
                {
                    list.Add(slot);
                }
            }
            return list;
        }

        private bool IsSourceSlot(SlotDataAsset slot)
        {
            if (slot == null)
            {
                return false;
            }
            if (extraSources.Contains(slot))
            {
                return true;
            }
            foreach (RaceSlotInfo info in raceSlots)
            {
                if (info.Name == slot.slotName && sourceSlotNames.Contains(info.Name))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Adds slots, the slots of recipes, and the slots in folders to the targets. Returns how many were added.</summary>
        public int AddTargets(IEnumerable<UnityEngine.Object> objects)
        {
            EnsureContext();
            int added = 0;
            if (objects == null)
            {
                return 0;
            }
            foreach (UnityEngine.Object selected in objects)
            {
                if (selected is SlotDataAsset slot)
                {
                    added += AddTarget(slot) ? 1 : 0;
                }
                else if (selected is UMATextRecipe recipe)
                {
                    UMAData.UMARecipe cached = recipe.GetCachedRecipe(true);
                    if (cached != null && cached.slotDataList != null)
                    {
                        foreach (SlotData slotData in cached.slotDataList)
                        {
                            added += slotData != null && AddTarget(slotData.asset) ? 1 : 0;
                        }
                    }
                }
                else if (selected is DefaultAsset folder)
                {
                    string path = AssetDatabase.GetAssetPath(folder);
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        foreach (string guid in AssetDatabase.FindAssets("t:SlotDataAsset", new[] { path }))
                        {
                            added += AddTarget(AssetDatabase.LoadAssetAtPath<SlotDataAsset>(AssetDatabase.GUIDToAssetPath(guid))) ? 1 : 0;
                        }
                    }
                }
            }
            if (added > 0)
            {
                MarkChanged();
                Repaint();
            }
            return added;
        }

        private bool AddTarget(SlotDataAsset slot)
        {
            if (slot == null || UMAMeshData.IsNullOrEmptyMeshData(slot.meshData) || targets.Contains(slot) || IsSourceSlot(slot) || IsRaceSlot(slot))
            {
                return false;
            }
            targets.Add(slot);
            return true;
        }

        private bool IsRaceSlot(SlotDataAsset slot)
        {
            foreach (RaceSlotInfo info in raceSlots)
            {
                if (info.Name == slot.slotName)
                {
                    return true;
                }
            }
            return false;
        }

        private UMABlendShapeSlotSpace GetSpace(SlotDataAsset slot, out string problem)
        {
            problem = null;
            if (spaces.TryGetValue(slot, out UMABlendShapeSlotSpace space))
            {
                return space;
            }
            if (spaceProblems.TryGetValue(slot, out problem))
            {
                return null;
            }
            if (UMABlendShapeSlotSpace.TryCreate(slot, context, out space, out problem))
            {
                spaces[slot] = space;
                return space;
            }
            spaceProblems[slot] = problem;
            return null;
        }

        /// <summary>Works the transfer out for every target, in memory. Nothing is written.</summary>
        public bool ComputeResults(bool showProgress)
        {
            results = new List<UMABlendShapeTransferResult>();
            resultsStale = false;
            previewDirty = true;
            if (!EnsureSource())
            {
                return false;
            }
            List<string> names = ShapesToTransfer();
            List<SlotDataAsset> slots = DistinctTargets();
            try
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    SlotDataAsset slot = slots[i];
                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar("Transferring blend shapes", slot.slotName, (float)i / slots.Count);
                    }
                    UMABlendShapeSlotSpace space = GetSpace(slot, out string problem);
                    if (space == null)
                    {
                        var failed = new UMABlendShapeTransferResult();
                        failed.Messages.Add($"'{slot.slotName}' was left out: {problem}");
                        results.Add(failed);
                        continue;
                    }
                    results.Add(UMABlendShapeTransfer.Transfer(source, space, names, settings));
                }
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            return true;
        }

        private UMABlendShapeTransferResult ResultFor(SlotDataAsset slot)
        {
            if (results == null)
            {
                return null;
            }
            foreach (UMABlendShapeTransferResult result in results)
            {
                if (result.Slot == slot)
                {
                    return result;
                }
            }
            return null;
        }

        // --- GUI -------------------------------------------------------------------------------------

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            // Wide enough for the longest labels at the deepest indent.
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.42f, 190f, 260f);
            EditorGUILayout.LabelField("Transfer blend shapes to slots", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Gives slots the blend shapes of a race's body, so clothing follows the body when a shape is dialled in. " +
                "Each vertex follows the nearest point of the body; the movement fades with distance and is smoothed over the slot. " +
                "Slots built for the race's legacy rig are matched as characters wear them.", MessageType.Info);

            DrawSource();
            EditorGUILayout.Space();
            DrawTargets();
            EditorGUILayout.Space();
            DrawFit();
            EditorGUILayout.Space();
            DrawOutput();
            EditorGUILayout.Space();
            DrawActions();
            DrawPreviewControls();
            DrawReport();
            GUILayout.Space(8f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSource()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var newRace = (RaceData)EditorGUILayout.ObjectField(new GUIContent("Race", "The race whose body carries the blend shapes. Its rig is also what the slots are placed on."), race, typeof(RaceData), false);
            if (EditorGUI.EndChangeCheck())
            {
                Race = newRace;
            }
            if (!EnsureSource())
            {
                EditorGUILayout.HelpBox(contextProblem ?? "The race could not be used.", race == null ? MessageType.Info : MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            bodyShape = (UMABlendShapeBodyShape)EditorGUILayout.Popup(new GUIContent("Match against",
                "The shape of the body the slots are matched to. As the race builds it: with the blend shapes the race bakes in, which is the body clothing sits on. Base slots: the stored slots with no shape applied."),
                (int)bodyShape, new[] { "The body as the race builds it", "The base slots, no shapes applied" });
            if (EditorGUI.EndChangeCheck())
            {
                source = null;
                MarkChanged();
                EnsureSource();
            }

            int on = 0;
            foreach (RaceSlotInfo info in raceSlots)
            {
                if (sourceSlotNames.Contains(info.Name)) on++;
            }
            showSourceSlots = EditorGUILayout.Foldout(showSourceSlots, $"Source slots ({on} of the race's {raceSlots.Count}" + (extraSources.Count > 0 ? $", plus {extraSources.Count} other" : string.Empty) + ")", true);
            if (showSourceSlots)
            {
                EditorGUI.indentLevel++;
                bool changed = false;
                foreach (RaceSlotInfo info in raceSlots)
                {
                    bool was = sourceSlotNames.Contains(info.Name);
                    bool now = EditorGUILayout.ToggleLeft($"{info.Name}  ({info.ShapeCount} blend shapes)", was);
                    if (now != was)
                    {
                        if (now) sourceSlotNames.Add(info.Name);
                        else sourceSlotNames.Remove(info.Name);
                        changed = true;
                    }
                }
                EditorGUILayout.LabelField(new GUIContent("Other source slots", "Slots that are not part of the race's body but carry blend shapes to take over, for example a shirt whose shapes a jacket should follow."), EditorStyles.miniBoldLabel);
                for (int i = 0; i < extraSources.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    var value = (SlotDataAsset)EditorGUILayout.ObjectField(extraSources[i], typeof(SlotDataAsset), false);
                    if (value != extraSources[i])
                    {
                        extraSources[i] = value;
                        changed = true;
                    }
                    if (GUILayout.Button("-", GUILayout.Width(22f)))
                    {
                        extraSources.RemoveAt(i);
                        changed = true;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                if (GUILayout.Button("+", GUILayout.Width(22f)))
                {
                    extraSources.Add(null);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
                if (changed)
                {
                    source = null;
                    MarkChanged();
                    if (!EnsureSource())
                    {
                        return;
                    }
                }
            }
            foreach (string message in source.Messages)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
            foreach (string message in context.Messages)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }

            // The shapes.
            int chosen = ShapesToTransfer().Count;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Blend shapes ({chosen} of {source.Shapes.Count})", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40f)))
            {
                SetShapes(shape => true);
            }
            if (GUILayout.Button(new GUIContent("Live", "The shapes characters of this race can dial in: the ones its built body still carries."), EditorStyles.miniButtonMid, GUILayout.Width(40f)))
            {
                SetShapes(shape => IsLive(shape.Name));
            }
            if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44f)))
            {
                SetShapes(shape => false);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            shapeFilter = EditorGUILayout.TextField(new GUIContent("Filter", "Show only blend shapes whose name contains this text. All, Live and None act on the shapes shown."), shapeFilter);
            if (GUILayout.Button("x", GUILayout.Width(22f)))
            {
                shapeFilter = string.Empty;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            if (source.Shapes.Count == 0)
            {
                EditorGUILayout.HelpBox("The source slots carry no blend shapes.", MessageType.Warning);
                return;
            }
            int shown = 0;
            foreach (UMABlendShapeSource.Shape shape in source.Shapes)
            {
                if (ShapeShown(shape)) shown++;
            }
            shapeScroll = EditorGUILayout.BeginScrollView(shapeScroll, GUILayout.Height(Mathf.Min(Mathf.Max(1, shown) * 20f + 4f, 204f)));
            foreach (UMABlendShapeSource.Shape shape in source.Shapes)
            {
                if (!ShapeShown(shape))
                {
                    continue;
                }
                bool was = selectedShapes.Contains(shape.Name);
                float? baked = PrebakedValue(shape.Name);
                bool dropped = baked == null && !IsLive(shape.Name);
                string label = $"{shape.Name}   {shape.MaxMovement * 1000f:0.#} mm" + (shape.Frames.Length > 1 ? $", {shape.Frames.Length} frames" : string.Empty) +
                               (baked != null ? $"   · baked into {race.raceName} at {baked.Value:0.##}" : dropped ? $"   · not kept by {race.raceName}" : string.Empty);
                string tip = $"Moves the source up to {shape.MaxMovement * 1000f:0.0} mm. On: {string.Join(", ", shape.Slots)}." +
                             (baked != null ? $"\n{race.raceName} bakes this shape into its body. Clothing fitted to that body already has it; transfer it only to slots fitted to the base slots."
                              : dropped ? $"\n{race.raceName} removes this shape when it builds its body (it is not in the race's Unbaked Shapes To Include), so characters of this race cannot dial it in. Transfer it for other races that share these slots, or add it to that list first."
                              : string.Empty);
                bool now = EditorGUILayout.ToggleLeft(new GUIContent(label, tip), was);
                if (now != was)
                {
                    if (now) selectedShapes.Add(shape.Name);
                    else selectedShapes.Remove(shape.Name);
                    MarkChanged();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ShapeShown(UMABlendShapeSource.Shape shape)
        {
            return string.IsNullOrEmpty(shapeFilter) || shape.Name.IndexOf(shapeFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetShapes(Func<UMABlendShapeSource.Shape, bool> choose)
        {
            foreach (UMABlendShapeSource.Shape shape in source.Shapes)
            {
                if (!ShapeShown(shape))
                {
                    continue;
                }
                bool want = choose(shape);
                bool has = selectedShapes.Contains(shape.Name);
                if (want && !has) selectedShapes.Add(shape.Name);
                if (!want && has) selectedShapes.Remove(shape.Name);
            }
            MarkChanged();
        }

        private void DrawTargets()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Target slots ({DistinctTargets().Count})", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Add selected", "Add the slots selected in the Project window, the slots of selected recipes, and the slots in selected folders."), EditorStyles.miniButtonLeft, GUILayout.Width(92f)))
            {
                int added = AddTargets(Selection.objects);
                if (added == 0)
                {
                    ShowNotification(new GUIContent("Select slots, recipes or folders in the Project window first."));
                }
            }
            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(48f)))
                {
                    targets.Clear();
                    MarkChanged();
                }
            }
            EditorGUILayout.EndHorizontal();

            Rect dropArea = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drop slots, recipes or folders here", EditorStyles.helpBox);
            HandleDrop(dropArea);

            if (targets.Count > 6)
            {
                targetScroll = EditorGUILayout.BeginScrollView(targetScroll, GUILayout.Height(6 * 40f));
            }
            for (int i = 0; i < targets.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var value = (SlotDataAsset)EditorGUILayout.ObjectField(targets[i], typeof(SlotDataAsset), false);
                if (value != targets[i])
                {
                    targets[i] = value;
                    MarkChanged();
                }
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    targets.RemoveAt(i);
                    MarkChanged();
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
                string status = TargetStatus(targets[i]);
                if (!string.IsNullOrEmpty(status))
                {
                    EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (targets.Count > 6)
            {
                EditorGUILayout.EndScrollView();
            }
            if (GUILayout.Button("+", GUILayout.Width(22f)))
            {
                targets.Add(null);
            }
        }

        private string TargetStatus(SlotDataAsset slot)
        {
            if (slot == null)
            {
                return null;
            }
            if (IsSourceSlot(slot))
            {
                return "   This is one of the source slots; it is skipped.";
            }
            if (spaceProblems.TryGetValue(slot, out string problem))
            {
                return "   Cannot be used: " + problem;
            }
            UMABlendShapeTransferResult result = ResultFor(slot);
            if (result == null)
            {
                return null;
            }
            int added = 0;
            float max = 0f;
            int poke = 0;
            foreach (UMABlendShapeTransferResult.ShapeResult shape in result.Shapes)
            {
                if (shape.Shape != null)
                {
                    added++;
                    max = Mathf.Max(max, shape.MaxMovement);
                    poke += shape.PokeThrough;
                }
            }
            string text = $"   {(result.Space.ConvertedFromLegacy ? "legacy rig · " : string.Empty)}{added} shape(s) · up to {max * 1000f:0.#} mm · {result.VerticesInReach} of {result.Space.VertexCount} vertices in reach";
            if (poke > 0)
            {
                text += $" · {poke} end under the surface";
            }
            if (result.UnderAtRestCount > 0)
            {
                text += $" · {result.UnderAtRestCount} already under the body at rest";
            }
            if (resultsStale)
            {
                text += " · out of date";
            }
            return text;
        }

        private void HandleDrop(Rect area)
        {
            Event e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !area.Contains(e.mousePosition))
            {
                return;
            }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddTargets(DragAndDrop.objectReferences);
            }
            e.Use();
        }

        private void DrawFit()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Fit", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Defaults", "Put every setting in this section back to its default."), EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                UMABlendShapeExistingMode keep = settings.existing;
                settings = new UMABlendShapeTransferSettings { existing = keep };
                MarkChanged();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            settings.method = (UMABlendShapeTransferMethod)EditorGUILayout.Popup(new GUIContent("Method",
                "Follow the surface: a vertex keeps its place relative to the body; its offset turns with the skin, so cloth keeps its distance where the shape tilts the skin. Copy the movement: a vertex takes the movement of the nearest body point as it is."),
                (int)settings.method, new[] { "Follow the surface", "Copy the movement" });
            settings.strength = EditorGUILayout.Slider(Content("Strength", nameof(settings.strength)), settings.strength, 0f, 2f);

            settings.useFalloff = EditorGUILayout.Toggle(Content("Fade with distance", nameof(settings.useFalloff)), settings.useFalloff);
            using (new EditorGUI.DisabledScope(!settings.useFalloff))
            {
                EditorGUI.indentLevel++;
                settings.fullStrengthDistance = Millimetres(Content("Full strength within", nameof(settings.fullStrengthDistance)), settings.fullStrengthDistance, 0f, 300f);
                settings.fadeOutDistance = Millimetres(Content("No movement beyond", nameof(settings.fadeOutDistance)), settings.fadeOutDistance, 0f, 600f);
                if (settings.fadeOutDistance < settings.fullStrengthDistance)
                {
                    settings.fadeOutDistance = settings.fullStrengthDistance;
                }
                EditorGUI.indentLevel--;
            }

            settings.smoothingIterations = EditorGUILayout.IntSlider(Content("Smoothing passes", nameof(settings.smoothingIterations)), settings.smoothingIterations, 0, 30);
            using (new EditorGUI.DisabledScope(settings.smoothingIterations == 0))
            {
                EditorGUI.indentLevel++;
                settings.smoothingStrength = EditorGUILayout.Slider(Content("Per pass", nameof(settings.smoothingStrength)), settings.smoothingStrength, 0f, 1f);
                EditorGUI.indentLevel--;
            }

            settings.keepClearance = EditorGUILayout.Toggle(Content("Keep clear of the body", nameof(settings.keepClearance)), settings.keepClearance);
            using (new EditorGUI.DisabledScope(!settings.keepClearance))
            {
                EditorGUI.indentLevel++;
                settings.clearance = Millimetres(Content("Clearance", nameof(settings.clearance)), settings.clearance, 0f, 20f);
                EditorGUI.indentLevel--;
            }

            settings.rigidSmallParts = EditorGUILayout.Toggle(Content("Keep small parts rigid", nameof(settings.rigidSmallParts)), settings.rigidSmallParts);
            using (new EditorGUI.DisabledScope(!settings.rigidSmallParts))
            {
                EditorGUI.indentLevel++;
                settings.rigidPartMaxSize = Millimetres(Content("Parts up to", nameof(settings.rigidPartMaxSize)), settings.rigidPartMaxSize, 1f, 500f);
                EditorGUI.indentLevel--;
            }

            settings.normals = (UMABlendShapeNormalMode)EditorGUILayout.Popup(new GUIContent("Normals and tangents",
                "Recalculate: the shape carries normal and tangent deltas worked out from how it bends the slot, so lighting follows. Positions only: no normal or tangent deltas, which is smaller but lights the moved surface as if it had not moved."),
                (int)settings.normals, new[] { "Recalculate", "Positions only" });

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                settings.preferFacingSurfaces = EditorGUILayout.Toggle(Content("Prefer facing surfaces", nameof(settings.preferFacingSurfaces)), settings.preferFacingSurfaces);
                using (new EditorGUI.DisabledScope(!settings.preferFacingSurfaces))
                {
                    settings.facingPenalty = Millimetres(Content("Facing margin", nameof(settings.facingPenalty)), settings.facingPenalty, 0f, 200f);
                }
                settings.minimumMovement = Millimetres(Content("Ignore movement under", nameof(settings.minimumMovement)), settings.minimumMovement, 0f, 2f, "0.###");
                settings.skipShapeBelow = Millimetres(Content("Skip a shape under", nameof(settings.skipShapeBelow)), settings.skipShapeBelow, 0f, 10f, "0.##");
                EditorGUI.indentLevel--;
            }
            if (EditorGUI.EndChangeCheck())
            {
                MarkChanged();
            }
        }

        private void DrawOutput()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            settings.existing = (UMABlendShapeExistingMode)EditorGUILayout.Popup(new GUIContent("A shape the slot already has",
                "Replace: the transferred shape takes the place of the slot's own shape of that name. Keep: the slot's own shape is left as it is."),
                (int)settings.existing, new[] { "Replace it", "Keep the slot's own" });
            writeCopies = EditorGUILayout.Popup(new GUIContent("Write to",
                "The slots themselves: the shapes are added to the slot assets, and everything that uses the slots gets them. Copies: each slot is saved as a new asset with the shapes; recipes keep using the originals until you point them at the copies."),
                writeCopies ? 1 : 0, OutputLabels) == 1;
            if (writeCopies)
            {
                EditorGUI.indentLevel++;
                copySuffix = EditorGUILayout.TextField(new GUIContent("Name suffix", "Added to the copy's file name and slot name."), copySuffix);
                copyFolder = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Folder", "Leave empty to save each copy next to its slot."), copyFolder, typeof(DefaultAsset), false);
                addCopiesToLibrary = EditorGUILayout.Toggle(new GUIContent("Add to the UMA library", "Register the copies in the UMA Global Library."), addCopiesToLibrary);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawActions()
        {
            bool ready = source != null && DistinctTargets().Count > 0 && ShapesToTransfer().Count > 0;
            if (source != null && !ready)
            {
                EditorGUILayout.LabelField(DistinctTargets().Count == 0 ? "Add at least one target slot." : "Tick at least one blend shape.", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button(new GUIContent(previewOpen ? "Update preview" : "Preview", "Works the shapes out and shows them in the Scene view. Nothing is written."), GUILayout.Height(28f)))
                {
                    OpenPreview();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Transfer", "Writes the blend shapes to the slots (or to copies)."), GUILayout.Height(28f)))
                {
                    if (ConfirmTransfer())
                    {
                        RunTransfer();
                    }
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();
            using (new EditorGUI.DisabledScope(source == null || DistinctTargets().Count == 0 || selectedShapes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Remove the ticked shapes from the target slots", "Takes the ticked blend shapes off the target slots again. Can be undone.")))
                {
                    RemoveFromTargets();
                    GUIUtility.ExitGUI();
                }
            }

            // Hand-finishing: the Mesh Modifier editor's sculpt brushes, with the shape loaded.
            string shapeToSculpt = ShapeToSculpt();
            using (new EditorGUI.DisabledScope(!ready || string.IsNullOrEmpty(shapeToSculpt)))
            {
                string label = string.IsNullOrEmpty(shapeToSculpt) ? "Sculpt a shape" : $"Sculpt '{shapeToSculpt}'";
                if (GUILayout.Button(new GUIContent(label,
                        "Opens the sculpt brushes with this blend shape loaded, on a temporary character wearing the target slots, to smooth or correct it by hand. " +
                        "Nothing is written until you press Save there. If the character selected in the Hierarchy wears the target slots, it is used instead."),
                        GUILayout.Height(24f)))
                {
                    SculptShape(shapeToSculpt);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.LabelField(
                "Sculpt opens the brushes on the target slots with the shape dialled in. It is the shape shown in the preview, or the first ticked one; nothing is written until you save there.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>The scene character selected in the Hierarchy, when it wears at least one of the target slots.</summary>
        private DynamicCharacterAvatar SelectedCharacterWearingTargets()
        {
            GameObject selected = Selection.activeGameObject;
            DynamicCharacterAvatar character = selected != null && selected.scene.IsValid() ? selected.GetComponentInParent<DynamicCharacterAvatar>() : null;
            if (character == null || character.activeRace == null || character.activeRace.data != race)
            {
                return null;
            }
            foreach (SlotDataAsset target in DistinctTargets())
            {
                foreach (UMATextRecipe recipe in character.WardrobeRecipes.Values)
                {
                    UMAData.UMARecipe cached = recipe != null ? recipe.GetCachedRecipe(true) : null;
                    if (cached == null || cached.slotDataList == null)
                    {
                        continue;
                    }
                    foreach (SlotData slotData in cached.slotDataList)
                    {
                        if (slotData != null && slotData.asset != null && slotData.asset.slotName == target.slotName)
                        {
                            return character;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>The shape the sculpt button opens: the one being previewed, else the first ticked one.</summary>
        private string ShapeToSculpt()
        {
            if (previewOpen && !string.IsNullOrEmpty(previewShape) && selectedShapes.Contains(previewShape))
            {
                return previewShape;
            }
            List<string> names = ShapesToTransfer();
            return names.Count > 0 ? names[0] : null;
        }

        /// <summary>
        /// Opens the Mesh Modifier editor's Sculpt mode with a blend shape loaded on the target slots:
        /// on the selected scene character when it wears them, else on a temporary character built for
        /// the purpose. A slot that does not have the shape yet shows the transfer as worked out here;
        /// it is written when the edit is saved in the editor. Returns the stage, or null.
        /// </summary>
        public VertexEditorStage SculptShape(string shapeName, DynamicCharacterAvatar character = null)
        {
            if (string.IsNullOrEmpty(shapeName))
            {
                return null;
            }
            if (results == null || resultsStale)
            {
                if (!ComputeResults(true))
                {
                    report = contextProblem ?? "Nothing to sculpt.";
                    return null;
                }
            }
            // What the transfer made for slots that do not have the shape yet.
            var unsaved = new Dictionary<SlotDataAsset, UMABlendShape>();
            var names = new List<string>();
            var slots = new List<SlotDataAsset>();
            foreach (UMABlendShapeTransferResult result in results)
            {
                if (result.Slot == null)
                {
                    continue;
                }
                slots.Add(result.Slot);
                names.Add(result.Slot.slotName);
                UMABlendShapeTransferResult.ShapeResult shape = result.Find(shapeName);
                UMABlendShape[] existing = result.Slot.meshData.blendShapes;
                bool has = existing != null && Array.Exists(existing, s => s != null && s.shapeName == shapeName);
                if (!has && shape != null && shape.Shape != null)
                {
                    unsaved[result.Slot] = shape.Shape;
                }
            }
            if (slots.Count == 0)
            {
                report = "None of the target slots can be used.";
                return null;
            }

            ClosePreview();
            character = character != null ? character : SelectedCharacterWearingTargets();
            VertexEditorStage stage;
            string note = null;
            if (character != null)
            {
                // The same steps the character's inspector takes before it opens the editor.
                character.ignoreMeshHideAssets = true;
                character.GenerateNow();
                stage = VertexEditorStage.ShowStage(character, null);
                if (stage != null)
                {
                    stage.EditOnlySlots(names);
                }
            }
            else
            {
                stage = VertexEditorStage.ShowStageForSlots(race, slots, out note);
            }
            if (stage == null)
            {
                report = "The sculpt editor could not be opened. " + note;
                Repaint();
                return null;
            }
            stage.RequestBlendshapeEdit(shapeName, unsaved);
            report = $"Sculpting '{shapeName}' " + (character != null ? $"on '{character.name}'" : "on a temporary character") + ". " +
                     (unsaved.Count > 0 ? $"It is not on {unsaved.Count} of the slot(s) yet: Save in the Sculpt panel's Edit Blendshape section writes it. " : string.Empty) +
                     stage.BlendshapeEditStatus + (string.IsNullOrEmpty(note) ? string.Empty : " " + note);
            Repaint();
            return stage;
        }

        private bool ConfirmTransfer()
        {
            int slots = DistinctTargets().Count;
            int shapes = ShapesToTransfer().Count;
            string what = writeCopies
                ? $"Save {slots} slot(s) as copies named '…{copySuffix}' with up to {shapes} blend shape(s) each?"
                : $"Add up to {shapes} blend shape(s) to {slots} slot asset(s)?\n\nA shape that does not move a slot is not added to it. The change can be undone with Undo.";
            return EditorUtility.DisplayDialog("Transfer blend shapes", what, "Transfer", "Cancel");
        }

        /// <summary>Works the transfer out and writes it to the slots or to copies. Returns how many blend shapes were written.</summary>
        public int RunTransfer()
        {
            var log = new StringBuilder();
            int written = 0;
            var saved = new List<SlotDataAsset>();
            if (!ComputeResults(true))
            {
                report = contextProblem ?? "Nothing to transfer.";
                return 0;
            }
            Undo.SetCurrentGroupName("Transfer Blend Shapes");
            try
            {
                string folder = copyFolder != null ? AssetDatabase.GetAssetPath(copyFolder) : null;
                for (int i = 0; i < results.Count; i++)
                {
                    UMABlendShapeTransferResult result = results[i];
                    foreach (string message in result.Messages)
                    {
                        log.AppendLine(message);
                    }
                    if (result.Slot == null)
                    {
                        continue;
                    }
                    EditorUtility.DisplayProgressBar("Writing blend shapes", result.Slot.slotName, (float)i / results.Count);
                    bool anything = false;
                    foreach (UMABlendShapeTransferResult.ShapeResult shape in result.Shapes)
                    {
                        anything |= shape.Shape != null;
                    }
                    if (!anything)
                    {
                        log.AppendLine($"{result.Slot.slotName}: none of the shapes moves it; left as it is.");
                        continue;
                    }
                    SlotDataAsset destination = writeCopies ? UMABlendShapeTransfer.CreateCopy(result.Slot, copySuffix, folder, addCopiesToLibrary) : result.Slot;
                    if (writeCopies && result.Space.ConvertedFromLegacy && context != null && context.Profile != null)
                    {
                        // A rig profile's entries for one slot are found by slot name; a copy has another name.
                        UMARigSlotOverride own = context.Profile.GetSlotOverride(result.Slot.slotName);
                        if (own != null && !own.IsEmpty)
                        {
                            log.AppendLine($"Warning: '{context.Profile.name}' has entries for '{result.Slot.slotName}' alone. The copy '{destination.slotName}' has another name and does not get them, " +
                                           "so it is converted differently and the shapes will be slightly off on it. Add the same entries for the copy's name, or write to the slot itself.");
                        }
                    }
                    var lines = new List<string>();
                    int count = UMABlendShapeTransfer.Apply(result, destination, settings.existing, lines);
                    written += count;
                    foreach (string line in lines)
                    {
                        log.AppendLine(line + (writeCopies ? "  → " + AssetDatabase.GetAssetPath(destination) : string.Empty));
                    }
                    if (count > 0)
                    {
                        saved.Add(destination);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                // Only the slots written here: other unsaved assets are not this tool's to save.
                foreach (SlotDataAsset slot in saved)
                {
                    AssetDatabase.SaveAssetIfDirty(slot);
                }
                if (writeCopies && addCopiesToLibrary && saved.Count > 0 && UMAAssetIndexer.Instance != null)
                {
                    UMAAssetIndexer.Instance.ForceSave();
                }
                // Converted copies of legacy slots were made before the shapes existed.
                UMALegacySlotConverter.ClearCache();
            }
            // The slots changed: what was measured from them is out of date.
            spaces.Clear();
            spaceProblems.Clear();
            AppendShapeTable(log);
            report = $"{written} blend shape(s) written to {saved.Count} slot(s).\n" + log;
            Debug.Log("[UMA] Blend shape transfer\n" + report);
            if (saved.Count > 0)
            {
                EditorGUIUtility.PingObject(saved[0]);
            }
            Repaint();
            return written;
        }

        private void RemoveFromTargets()
        {
            var names = new HashSet<string>(selectedShapes);
            List<SlotDataAsset> slots = DistinctTargets();
            int affected = 0;
            foreach (SlotDataAsset slot in slots)
            {
                UMABlendShape[] shapes = slot.meshData.blendShapes;
                if (shapes == null) continue;
                foreach (UMABlendShape shape in shapes)
                {
                    if (shape != null && names.Contains(shape.shapeName)) { affected++; break; }
                }
            }
            if (affected == 0)
            {
                report = "None of the target slots has any of the ticked blend shapes.";
                return;
            }
            if (!EditorUtility.DisplayDialog("Remove blend shapes", $"Remove the ticked blend shapes from {affected} slot asset(s)?\n\nThis also removes shapes of those names the slots had before any transfer. It can be undone with Undo.", "Remove", "Cancel"))
            {
                return;
            }
            Undo.SetCurrentGroupName("Remove Blend Shapes");
            var log = new StringBuilder();
            int total = 0;
            foreach (SlotDataAsset slot in slots)
            {
                int removed = UMABlendShapeTransfer.RemoveShapes(slot, names);
                if (removed > 0)
                {
                    total += removed;
                    log.AppendLine($"{slot.slotName}: {removed} removed.");
                    AssetDatabase.SaveAssetIfDirty(slot);
                }
            }
            UMALegacySlotConverter.ClearCache();
            spaces.Clear();
            spaceProblems.Clear();
            MarkChanged();
            report = $"{total} blend shape(s) removed.\n" + log;
        }

        private void AppendShapeTable(StringBuilder log)
        {
            if (results == null)
            {
                return;
            }
            foreach (UMABlendShapeTransferResult result in results)
            {
                if (result.Space == null)
                {
                    continue;
                }
                log.AppendLine();
                log.AppendLine(result.Summary());
                foreach (UMABlendShapeTransferResult.ShapeResult shape in result.Shapes)
                {
                    log.AppendLine(shape.Shape != null
                        ? $"   {shape.Name}: up to {shape.MaxMovement * 1000f:0.0} mm, {shape.MovedVertices} vertices" +
                          (shape.ClearanceFixed > 0 ? $", {shape.ClearanceFixed} kept clear" : string.Empty) +
                          (shape.PokeThrough > 0 ? $", {shape.PokeThrough} end under the surface" : string.Empty)
                        : $"   {shape.Name}: not added, {shape.Skipped}");
                }
            }
        }

        private void DrawReport()
        {
            if (string.IsNullOrEmpty(report))
            {
                return;
            }
            EditorGUILayout.Space();
            showReport = EditorGUILayout.Foldout(showReport, "Report", true);
            if (showReport)
            {
                reportScroll = EditorGUILayout.BeginScrollView(reportScroll, GUILayout.MinHeight(80f), GUILayout.MaxHeight(320f));
                EditorGUILayout.TextArea(report, EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndScrollView();
            }
        }

        // --- preview ---------------------------------------------------------------------------------

        /// <summary>Works the shapes out and shows them in the Scene view. Nothing is written.</summary>
        public void OpenPreview()
        {
            if (!ComputeResults(true))
            {
                return;
            }
            previewOpen = true;
            var log = new StringBuilder();
            foreach (UMABlendShapeTransferResult result in results)
            {
                foreach (string message in result.Messages)
                {
                    log.AppendLine(message);
                }
            }
            AppendShapeTable(log);
            report = "Preview (nothing written yet).\n" + log;
            List<string> names = PreviewShapeNames();
            if (!names.Contains(previewShape))
            {
                previewShape = names.Count > 0 ? names[0] : string.Empty;
            }
            bool first = preview == null;
            EnsurePreview();
            if (first)
            {
                FramePreview();
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void ClosePreview()
        {
            previewOpen = false;
            animate = false;
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }
            SceneView.RepaintAll();
        }

        /// <summary>The shapes worth looking at: the ones that moved at least one target.</summary>
        private List<string> PreviewShapeNames()
        {
            var names = new List<string>();
            if (results == null)
            {
                return names;
            }
            foreach (UMABlendShapeTransferResult result in results)
            {
                foreach (UMABlendShapeTransferResult.ShapeResult shape in result.Shapes)
                {
                    if (shape.Shape != null && !names.Contains(shape.Name))
                    {
                        names.Add(shape.Name);
                    }
                }
            }
            return names;
        }

        private void EnsurePreview()
        {
            if (preview == null)
            {
                preview = new UMABlendShapeTransferPreview();
                previewDirty = true;
            }
            if (previewDirty && source != null && results != null)
            {
                preview.SetMeshes(source, results, SlotColors, BodyColor);
                previewDirty = false;
            }
        }

        private void FramePreview()
        {
            if (preview != null && preview.TryGetBounds(out Bounds bounds) && SceneView.lastActiveSceneView != null)
            {
                bounds.center += previewOffset;
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }
        }

        private void DrawPreviewControls()
        {
            if (!previewOpen)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Frame", "Frame the preview in the Scene view."), EditorStyles.miniButtonLeft, GUILayout.Width(52f)))
            {
                FramePreview();
            }
            if (GUILayout.Button("Close", EditorStyles.miniButtonRight, GUILayout.Width(52f)))
            {
                ClosePreview();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            if (resultsStale)
            {
                EditorGUILayout.LabelField(autoUpdate ? "Updating…" : "Settings changed since this preview was made. Press Update preview.", EditorStyles.wordWrappedMiniLabel);
            }

            List<string> names = PreviewShapeNames();
            if (names.Count == 0)
            {
                EditorGUILayout.HelpBox("None of the ticked blend shapes moves any target slot. Check that the slots sit on the source surface, and the fade distances.", MessageType.Warning);
            }
            else
            {
                int index = Mathf.Max(0, names.IndexOf(previewShape));
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                index = EditorGUILayout.Popup(new GUIContent("Shape", "The blend shape shown. Only shapes that move at least one target are listed."), index, names.ToArray());
                if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(26f))) index = (index - 1 + names.Count) % names.Count;
                if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(26f))) index = (index + 1) % names.Count;
                if (EditorGUI.EndChangeCheck() || previewShape != names[index])
                {
                    previewShape = names[index];
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            previewWeight = EditorGUILayout.Slider(new GUIContent("Weight", "How far the shape is dialled in: 0 is the rest shape, 1 the full shape."), previewWeight, 0f, 1f);
            bool wasAnimating = animate;
            animate = GUILayout.Toggle(animate, new GUIContent("Play", "Move the weight back and forth."), EditorStyles.miniButton, GUILayout.Width(44f));
            if (animate && !wasAnimating)
            {
                animateStart = EditorApplication.timeSinceStartup;
            }
            EditorGUILayout.EndHorizontal();
            colorMode = (ColorMode)EditorGUILayout.Popup(new GUIContent("Colours",
                "Shaded: each slot in its own colour. Movement: how far each vertex moves, blue (none) to red (most). Influence: how much of the body's movement each vertex takes, dark (none) to bright (all). Under the surface: red where the shape puts a vertex under the moved body, orange where it already lay under the body at rest."),
                (int)colorMode, ColorModeLabels);
            EditorGUILayout.BeginHorizontal();
            showBody = EditorGUILayout.ToggleLeft(new GUIContent("Show the body", "Draw the source surface, moved by the same shape."), showBody, GUILayout.Width(120f));
            autoUpdate = EditorGUILayout.ToggleLeft(new GUIContent("Update as I change settings", "Work the preview out again shortly after any change. Turn off for many or heavy slots."), autoUpdate);
            EditorGUILayout.EndHorizontal();
            previewOffset = EditorGUILayout.Vector3Field(new GUIContent("Position", "Where the preview is drawn in the scene."), previewOffset);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }

        private void OnEditorUpdate()
        {
            if (!previewOpen)
            {
                return;
            }
            if (animate)
            {
                float t = (float)((EditorApplication.timeSinceStartup - animateStart) % 2.4 / 2.4);
                previewWeight = 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI * 2f);
                SceneView.RepaintAll();
                Repaint();
            }
            if (resultsStale && autoUpdate && EditorApplication.timeSinceStartup - lastChange > AutoUpdateDelay)
            {
                if (source == null && !EnsureSource())
                {
                    resultsStale = false;
                    return;
                }
                if (DistinctTargets().Count == 0 || ShapesToTransfer().Count == 0)
                {
                    resultsStale = false;
                    results = new List<UMABlendShapeTransferResult>();
                    previewDirty = true;
                }
                else
                {
                    ComputeResults(false);
                }
                SceneView.RepaintAll();
                Repaint();
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            // An editing stage (the sculpt editor, the Face Editor) has the Scene view to itself.
            if (!previewOpen || Event.current.type != EventType.Repaint || results == null || source == null ||
                !(UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is UnityEditor.SceneManagement.MainStage))
            {
                return;
            }
            EnsurePreview();
            Camera camera = sceneView.camera;
            Vector3 light = camera != null ? -camera.transform.forward : Vector3.back;
            preview.Update(previewShape, previewWeight, (int)colorMode, light, showBody);
            preview.Draw(Matrix4x4.Translate(previewOffset));

            Handles.BeginGUI();
            string legend = string.IsNullOrEmpty(previewShape) ? "Blend shape transfer preview" : $"{previewShape}  ·  weight {previewWeight:0.00}";
            if (colorMode == ColorMode.Movement)
            {
                legend += $"\nMovement: blue 0 mm → red {preview.MaxMovement * 1000f:0.0} mm";
            }
            else if (colorMode == ColorMode.Influence)
            {
                legend += "\nInfluence: dark none → bright full";
            }
            else if (colorMode == ColorMode.UnderTheSurface)
            {
                legend += "\nRed: the shape puts it under the body  ·  Orange: already under the body at rest";
            }
            var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11, richText = false };
            Vector2 size = style.CalcSize(new GUIContent(legend));
            GUI.Label(new Rect(10f, 10f, size.x + 8f, size.y + 4f), legend, style);
            Handles.EndGUI();
        }

        // --- small GUI helpers -----------------------------------------------------------------------

        private static GUIContent Content(string label, string field)
        {
            var info = typeof(UMABlendShapeTransferSettings).GetField(field);
            var tooltip = info != null ? (TooltipAttribute)Attribute.GetCustomAttribute(info, typeof(TooltipAttribute)) : null;
            return new GUIContent(label, tooltip != null ? tooltip.tooltip : string.Empty);
        }

        /// <summary>A slider that shows a distance stored in metres as millimetres.</summary>
        private static float Millimetres(GUIContent label, float metres, float minMm, float maxMm, string format = null)
        {
            var content = new GUIContent(label.text + " (mm)", label.tooltip);
            float mm = EditorGUILayout.Slider(content, metres * 1000f, minMm, maxMm);
            return mm / 1000f;
        }
    }

    /// <summary>
    /// Draws a blend shape transfer in the Scene view: the source surface and every target slot in
    /// avatar space, moved by one shape at a chosen weight. Colours are baked into the vertices, so
    /// the unlit handle shader draws it in every render pipeline.
    /// </summary>
    public sealed class UMABlendShapeTransferPreview : IDisposable
    {
        private sealed class Part
        {
            public Mesh Mesh;
            public Vector3[] Rest;
            public Vector3[] Work;
            public Color32[] Colors;
            public Color Base;
            public UMABlendShapeTransferResult Result;   // null for the body
        }

        private readonly List<Part> parts = new List<Part>();
        private readonly List<Vector3> normalBuffer = new List<Vector3>();
        private UMABlendShapeSource source;
        private Material material;
        private string shownShape;
        private float shownWeight = -1f;
        private int shownMode = -1;
        private Vector3 shownLight;
        private bool shownBody;
        private bool dirty = true;

        /// <summary>The largest movement of the shape shown, over the targets, in metres.</summary>
        public float MaxMovement { get; private set; }

        public void SetMeshes(UMABlendShapeSource newSource, IList<UMABlendShapeTransferResult> results, Color[] palette, Color bodyColor)
        {
            Clear();
            source = newSource;
            parts.Add(CreatePart("Blend Shape Transfer Body", source.Positions, source.Triangles, bodyColor, null));
            int colour = 0;
            foreach (UMABlendShapeTransferResult result in results)
            {
                if (result.Space == null)
                {
                    continue;
                }
                parts.Add(CreatePart("Blend Shape Transfer " + result.Space.Name, result.Space.Positions, result.Space.Triangles, palette[colour++ % palette.Length], result));
            }
            dirty = true;
        }

        private static Part CreatePart(string name, Vector3[] positions, int[] triangles, Color color, UMABlendShapeTransferResult result)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.vertices = positions;
            mesh.triangles = triangles;
            return new Part
            {
                Mesh = mesh,
                Rest = positions,
                Work = new Vector3[positions.Length],
                Colors = new Color32[positions.Length],
                Base = color,
                Result = result,
            };
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Part part in parts)
            {
                if (part.Result == null && parts.Count > 1)
                {
                    continue;   // frame the slots, not the whole body
                }
                part.Mesh.RecalculateBounds();
                if (!any)
                {
                    bounds = part.Mesh.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(part.Mesh.bounds);
                }
            }
            return any;
        }

        /// <summary>Moves the meshes to a shape and weight and bakes the colours. Does nothing when nothing changed.</summary>
        public void Update(string shapeName, float weight, int colorMode, Vector3 lightDirection, bool showBody)
        {
            if (!dirty && shapeName == shownShape && Mathf.Approximately(weight, shownWeight) && colorMode == shownMode &&
                showBody == shownBody && Vector3.Dot(lightDirection, shownLight) > 0.9995f)
            {
                return;
            }
            dirty = false;
            shownShape = shapeName;
            shownWeight = weight;
            shownMode = colorMode;
            shownLight = lightDirection;
            shownBody = showBody;

            float max = 0f;
            foreach (Part part in parts)
            {
                UMABlendShapeTransferResult.ShapeResult shape = part.Result != null ? part.Result.Find(shapeName) : null;
                if (shape != null && shape.Shape != null)
                {
                    max = Mathf.Max(max, shape.MaxMovement);
                }
            }
            MaxMovement = max;

            UMABlendShapeSource.Shape sourceShape = source != null && !string.IsNullOrEmpty(shapeName) ? source.FindShape(shapeName) : null;
            foreach (Part part in parts)
            {
                Vector3[] deltas = null;
                UMABlendShapeTransferResult.ShapeResult shape = null;
                if (part.Result == null)
                {
                    deltas = sourceShape != null ? sourceShape.Frames[sourceShape.Frames.Length - 1].Deltas : null;
                }
                else
                {
                    shape = part.Result.Find(shapeName);
                    deltas = shape != null && shape.Shape != null ? shape.AvatarDeltas : null;
                }
                for (int v = 0; v < part.Rest.Length; v++)
                {
                    part.Work[v] = deltas != null ? part.Rest[v] + deltas[v] * weight : part.Rest[v];
                }
                part.Mesh.vertices = part.Work;
                part.Mesh.RecalculateNormals();
                part.Mesh.GetNormals(normalBuffer);
                for (int v = 0; v < part.Colors.Length; v++)
                {
                    Color color = part.Base;
                    if (part.Result != null)
                    {
                        if (colorMode == 1)
                        {
                            float amount = deltas != null && max > 0f ? deltas[v].magnitude / max : 0f;
                            color = Heat(amount);
                        }
                        else if (colorMode == 2)
                        {
                            float influence = part.Result.Influence != null ? part.Result.Influence[v] : 0f;
                            color = Color.Lerp(new Color(0.12f, 0.12f, 0.16f), new Color(1f, 0.92f, 0.55f), influence);
                        }
                        else if (colorMode == 3)
                        {
                            bool under = shape != null && shape.Under != null && shape.Under[v];
                            bool atRest = part.Result.UnderAtRest != null && part.Result.UnderAtRest[v];
                            color = under ? new Color(1f, 0.15f, 0.1f) : atRest ? new Color(1f, 0.6f, 0.1f) : new Color(0.62f, 0.64f, 0.68f);
                        }
                    }
                    // Two-sided headlight shading, so the shape reads without a lit shader.
                    float shade = 0.28f + 0.72f * Mathf.Abs(Vector3.Dot(normalBuffer[v], lightDirection));
                    part.Colors[v] = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
                }
                part.Mesh.colors32 = part.Colors;
            }
        }

        private static Color Heat(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.33f) return Color.Lerp(new Color(0.15f, 0.25f, 0.85f), new Color(0.15f, 0.8f, 0.35f), t / 0.33f);
            if (t < 0.66f) return Color.Lerp(new Color(0.15f, 0.8f, 0.35f), new Color(0.98f, 0.9f, 0.2f), (t - 0.33f) / 0.33f);
            return Color.Lerp(new Color(0.98f, 0.9f, 0.2f), new Color(0.95f, 0.15f, 0.1f), (t - 0.66f) / 0.34f);
        }

        /// <summary>Draws the meshes. Call from a Scene view callback during Repaint, after <see cref="Update"/>.</summary>
        public void Draw(Matrix4x4 matrix)
        {
            if (parts.Count == 0 || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (material == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    return;
                }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.Zero);
                material.SetInt("_Cull", (int)CullMode.Off);
                material.SetInt("_ZWrite", 1);
                material.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            }
            material.SetColor("_Color", Color.white);
            material.SetPass(0);
            foreach (Part part in parts)
            {
                if (part.Result == null && !shownBody)
                {
                    continue;
                }
                Graphics.DrawMeshNow(part.Mesh, matrix);
            }
        }

        /// <summary>The meshes as drawn, for tools and tests: the body first, then one per target.</summary>
        public IEnumerable<Mesh> Meshes
        {
            get
            {
                foreach (Part part in parts)
                {
                    yield return part.Mesh;
                }
            }
        }

        private void Clear()
        {
            foreach (Part part in parts)
            {
                if (part.Mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(part.Mesh);
                }
            }
            parts.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (material != null)
            {
                UnityEngine.Object.DestroyImmediate(material);
                material = null;
            }
        }
    }
}
