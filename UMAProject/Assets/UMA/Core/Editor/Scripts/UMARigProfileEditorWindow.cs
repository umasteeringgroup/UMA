using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TreeView = UnityEditor.IMGUI.Controls.TreeView<int>;
using TreeViewItem = UnityEditor.IMGUI.Controls.TreeViewItem<int>;
using TreeViewState = UnityEditor.IMGUI.Controls.TreeViewState<int>;

namespace UMA.Editors
{
    /// <summary>
    /// Edits a <see cref="UMASlotRigConversionProfile"/> against a live character. Bones are picked
    /// from the race's skeleton tree (searchable, filterable, with starred favourites kept in the
    /// profile), edited with numeric fields or Scene view handles, and every change converts the
    /// previewed slots again and rebuilds the preview character's mesh.
    /// </summary>
    public sealed partial class UMARigProfileEditorWindow : EditorWindow
    {
        private enum BoneFilter { All, Favorites, Edited, UsedByPreview }
        private enum HandleMode { Move, Rotate, Scale }

        private struct BoneRow
        {
            public int Id;
            public int Depth;
            public string Name;
        }

        private const string MenuPath = "UMA/Tools/Slot Tools/Rig Profile Editor";
        private const string PreviewPrefsPrefix = "UMA.RigProfileEditor.Preview.";
        private const string MirrorPrefsKey = "UMA.RigProfileEditor.Mirror";
        private const double MinimumRefreshInterval = 0.03;
        private const int MaximumSceneDots = 80;
        private static readonly string[] FilterLabels = { "All", "Favorites", "Edited", "Used by preview" };
        private static readonly string[] HandleLabels = { "Move", "Rotate", "Scale" };
        private static readonly string[] PoseLabels = { "Rest pose", "T-pose" };
        private static readonly string[] BrushLabels = { "Off", "Show", "Hide" };
        private static readonly string[] EffectorHandleLabels = { "Move", "Rotate", "Size" };
        private static readonly string[] EffectorActionLabels = { "Push out", "Move", "Scale", "Clear the body" };
        private static readonly Color EffectorColor = new Color(0.35f, 0.9f, 1f, 0.95f);
        // Dots on the vertices the selected effector holds: dark at its edge, bright in its core, and
        // grey on a slot it leaves alone.
        private static readonly Color EffectorVertexWeak = new Color(0.6f, 0.42f, 0.05f, 1f);
        private static readonly Color EffectorVertexStrong = new Color(1f, 0.95f, 0.2f, 1f);
        private static readonly Color EffectorVertexLeftAlone = new Color(0.62f, 0.62f, 0.62f, 1f);
        private const float EffectorVertexLift = 0.004f;   // metres toward the camera, so a dot on the cloth is not cut by it
        private static readonly Color HiddenColor = new Color(1f, 0.12f, 0.08f, 0.55f);
        private static readonly int BrushControlHint = "UMARigProfileHideBrush".GetHashCode();
        private const int BrushOff = 0;
        private const int BrushShow = 1;
        private const int BrushHide = 2;
        private static readonly string[] FastFilters = { "shoulder", "arm", "hand", "spine", "leg", "foot", "neck", "adjust" };

        [SerializeField] private RaceData race;
        [SerializeField] private UMASlotRigConversionProfile profile;
        [SerializeField] private List<UMATextRecipe> previewRecipes = new List<UMATextRecipe>();
        // Recipes kept in the list but switched off: the preview does not wear them and no hides are made for them.
        [SerializeField] private List<UMATextRecipe> disabledPreviewRecipes = new List<UMATextRecipe>();
        [SerializeField] private List<SlotDataAsset> previewSlots = new List<SlotDataAsset>();
        [SerializeField] private string search = string.Empty;
        [SerializeField] private BoneFilter filter = BoneFilter.All;
        [SerializeField] private string selectedBone = string.Empty;
        [SerializeField] private bool restPose = true;
        [SerializeField] private bool livePreview = true;
        [SerializeField] private bool showPreviewSetup = true;
        [SerializeField] private HandleMode handleMode = HandleMode.Move;
        [SerializeField] private bool mirror = true;
        [SerializeField] private bool showMeshHideSection;
        [SerializeField] private bool showMeshHidesInPreview = true;
        [SerializeField] private UMAMeshHideOcclusionSettings meshHideSettings = new UMAMeshHideOcclusionSettings();
        [SerializeField] private List<string> meshHideReport = new List<string>();
        [SerializeField] private string scopeSlot = string.Empty;
        [SerializeField] private bool showClothing = true;
        [SerializeField] private bool showHideOverlay = true;
        [SerializeField] private bool overlayThroughClothing;
        [SerializeField] private int brushMode = BrushOff;
        [SerializeField] private float brushRadius = 0.02f;
        [SerializeField] private UMATextRecipe brushRecipe;
        [SerializeField] private bool showEffectorSection;
        // The effector being edited: the slot it is kept to (empty: it acts on all slots) and its place in that list.
        [SerializeField] private string selectedEffectorSlot = string.Empty;
        [SerializeField] private int selectedEffector = -1;
        [SerializeField] private bool markEffectorVertices = true;
        [SerializeField] private TreeViewState treeState;
        [SerializeField] private Vector2 scroll;

        [NonSerialized] private UMARigProfilePreview preview;
        [NonSerialized] private UMARigRestPose targetRig;
        [NonSerialized] private UMARigRestPose legacyRig;
        [NonSerialized] private RaceData rigRace;
        [NonSerialized] private string rigProblem;
        [NonSerialized] private List<string> boneNames;
        [NonSerialized] private Dictionary<string, int> boneIds;
        [NonSerialized] private List<BoneRow> visibleRows = new List<BoneRow>();
        [NonSerialized] private HashSet<int> usedBones;
        [NonSerialized] private BoneTree tree;
        [NonSerialized] private bool treeDirty = true;
        [NonSerialized] private bool previewDirty;
        [NonSerialized] private double lastRefreshTime;
        [NonSerialized] private Tool lastUnityTool = Tool.None;
        [NonSerialized] private bool toolsHiddenByUs;
        [NonSerialized] private UMAMeshHidePainter painter;
        [NonSerialized] private bool painterDirty = true;
        [NonSerialized] private bool inFaceEditor;
        // Which previewed slots the selected effector reaches, measured again when the profile changes.
        [NonSerialized] private readonly List<UMARigProfilePreview.EffectorReach> effectorReach = new List<UMARigProfilePreview.EffectorReach>();
        [NonSerialized] private UMARigEffector effectorReachFor;
        [NonSerialized] private bool effectorReachDirty = true;
        // The vertices the selected effector holds, for the dots in the Scene view.
        [NonSerialized] private readonly List<UMARigProfilePreview.EffectorVertex> effectorVertices = new List<UMARigProfilePreview.EffectorVertex>();
        [NonSerialized] private UMARigEffector effectorVerticesFor;
        [NonSerialized] private int effectorVerticesConvert = -1;
        [NonSerialized] private bool effectorVerticesDirty = true;

        // --- opening -----------------------------------------------------------------------------

        [MenuItem(MenuPath)]
        private static void OpenFromMenu()
        {
            if (Selection.activeObject is UMASlotRigConversionProfile selectedProfile)
            {
                Open(null, selectedProfile);
            }
            else
            {
                Open(Selection.activeObject as RaceData);
            }
        }

        /// <summary>
        /// Opens the editor. With a race it edits that race's Legacy Rig Profile; pass a profile to
        /// edit that one instead.
        /// </summary>
        public static UMARigProfileEditorWindow Open(RaceData race, UMASlotRigConversionProfile profile = null)
        {
            var window = GetWindow<UMARigProfileEditorWindow>("Rig Profile");
            window.minSize = new Vector2(380f, 520f);
            if (race != null)
            {
                window.SetRace(race);
            }
            if (profile != null)
            {
                window.SetProfile(profile);
            }
            window.Show();
            window.Focus();
            return window;
        }

        /// <summary>The preview character, when one is built. For tests and tools.</summary>
        public UMARigProfilePreview Preview => preview;

        private void SetRace(RaceData newRace)
        {
            if (race == newRace)
            {
                return;
            }
            ClosePreview();
            race = newRace;
            rigRace = null;
            if (race != null && race.legacyRigProfile != null)
            {
                profile = race.legacyRigProfile;
            }
            LoadPreviewSelection();
            treeDirty = true;
            Repaint();
        }

        private void SetProfile(UMASlotRigConversionProfile newProfile)
        {
            if (profile == newProfile)
            {
                return;
            }
            bool rebuild = preview != null && preview.IsBuilt;
            profile = newProfile;
            treeDirty = true;
            if (rebuild)
            {
                BuildPreview();
            }
            Repaint();
        }

        private void OnEnable()
        {
            if (treeState == null)
            {
                treeState = new TreeViewState();
            }
            rigRace = null;
            tree = null;
            treeDirty = true;
            mirror = EditorPrefs.GetBool(MirrorPrefsKey, true);
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChangedForRecipeFolders;
            EditorApplication.projectChanged += OnProjectChangedForRecipeFolders;
            recipeFoldersDirty = true;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.projectChanged -= OnProjectChangedForRecipeFolders;
            ClosePreview();
        }

        // --- window GUI --------------------------------------------------------------------------

        private void OnGUI()
        {
            // The whole window scrolls. A GUILayout scroll view lays its content out beside the
            // scroll bar, never under it, and shows a horizontal bar only when the window is too
            // narrow for a row, so every control stays reachable.
            // GUIUtility.ExitGUI (used by buttons that rebuild the preview) abandons the pass and Unity
            // resets the layout itself, so the view is ended on the normal paths only.
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            EditorGUILayout.Space();
            DrawPreviewSection();
            DrawMeshHideSection();
            EditorGUILayout.Space();
            if (profile == null)
            {
                EditorGUILayout.HelpBox("Assign a Rig Conversion Profile, or press New to create one for this race.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            EnsureRig();
            DrawEffectorSection();
            DrawBoneBrowser();
            DrawBonePanel();
            // Room below the last control, so it does not sit on the window's edge.
            GUILayout.Space(8f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUI.BeginChangeCheck();
            var newRace = (RaceData)EditorGUILayout.ObjectField(new GUIContent("Race", "The race whose rig the legacy slots are converted to."), race, typeof(RaceData), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetRace(newRace);
            }
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(new GUIContent("Legacy Rig", "The older rig this race replaces. Set it on the race."), race != null ? race.legacyRig : null, typeof(UMARigRestPoseAsset), false);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            var newProfile = (UMASlotRigConversionProfile)EditorGUILayout.ObjectField(new GUIContent("Profile", "The per-bone adjustments being edited."), profile, typeof(UMASlotRigConversionProfile), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetProfile(newProfile);
            }
            if (GUILayout.Button(new GUIContent("New", "Create a profile next to the race and edit it."), GUILayout.Width(48f)))
            {
                CreateProfile();
            }
            EditorGUILayout.EndHorizontal();

            if (race != null && profile != null && race.legacyRigProfile != profile)
            {
                EditorGUILayout.HelpBox($"This profile is not {race.raceName}'s Legacy Rig Profile, so characters do not use it when they are built.", MessageType.Info);
                if (GUILayout.Button($"Use this profile for {race.raceName}"))
                {
                    Undo.RecordObject(race, "Assign Legacy Rig Profile");
                    race.legacyRigProfile = profile;
                    EditorUtility.SetDirty(race);
                    UMALegacySlotConverter.ClearCache();
                }
            }

            if (profile != null)
            {
                EditorGUI.BeginChangeCheck();
                var defaultMode = (UMARigBoneCorrectionMode)EditorGUILayout.EnumPopup(new GUIContent("Default mode", "The correction for every bone that has no entry of its own."), profile.defaultMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile, "Change Rig Profile Default Mode");
                    profile.defaultMode = defaultMode;
                    MarkProfileChanged();
                }

                DrawScopeRow();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Mirror all", "Make one whole side the mirror image of the other, replacing what that side had. With one slot chosen above, only that slot's own entries are mirrored."));
                if (GUILayout.Button(new GUIContent("Left to Right", "Copy every left bone's entry, mirrored, onto its right bone."), EditorStyles.miniButtonLeft))
                {
                    MirrorWholeSide(true);
                }
                if (GUILayout.Button(new GUIContent("Right to Left", "Copy every right bone's entry, mirrored, onto its left bone."), EditorStyles.miniButtonRight))
                {
                    MirrorWholeSide(false);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>True while the editor edits one slot's own entries rather than the entries for all slots.</summary>
        private bool InSlotScope => !string.IsNullOrEmpty(scopeSlot);

        /// <summary>The slot name the profile's scoped methods take: null for the entries that apply to all slots.</summary>
        private string Scope => InSlotScope ? scopeSlot : null;

        /// <summary>
        /// The slots the scope switch offers: the ones the preview converts, and every slot the profile
        /// already holds entries for.
        /// </summary>
        private List<string> ScopeSlotNames()
        {
            var names = new List<string>();
            if (preview != null && preview.IsBuilt)
            {
                for (int i = 0; i < preview.Slots.Count; i++)
                {
                    UMARigProfilePreview.PreviewSlot slot = preview.Slots[i];
                    if (slot.Converted && slot.Original != null && !names.Contains(slot.Original.slotName))
                    {
                        names.Add(slot.Original.slotName);
                    }
                }
            }
            for (int i = 0; i < profile.slotOverrides.Count; i++)
            {
                UMARigSlotOverride slot = profile.slotOverrides[i];
                if (slot != null && !string.IsNullOrEmpty(slot.slotName) && !names.Contains(slot.slotName))
                {
                    names.Add(slot.slotName);
                }
            }
            if (InSlotScope && !names.Contains(scopeSlot))
            {
                names.Add(scopeSlot);
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private bool IsInPreview(string slotName)
        {
            if (preview == null || !preview.IsBuilt)
            {
                return false;
            }
            for (int i = 0; i < preview.Slots.Count; i++)
            {
                UMARigProfilePreview.PreviewSlot slot = preview.Slots[i];
                if (slot.Converted && slot.Original != null && slot.Original.slotName == slotName)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Chooses between the entries for all slots and one slot's own entries.</summary>
        private void DrawScopeRow()
        {
            List<string> names = ScopeSlotNames();
            var labels = new string[names.Count + 1];
            labels[0] = "All slots";
            int index = 0;
            for (int i = 0; i < names.Count; i++)
            {
                UMARigSlotOverride existing = profile.GetSlotOverride(names[i]);
                // A slash would turn the name into a submenu.
                string own = existing == null ? string.Empty
                    : "  (" + existing.boneCorrections.Count + " bone" + (existing.boneCorrections.Count == 1 ? "" : "s")
                      + (existing.effectors.Count > 0 ? ", " + existing.effectors.Count + " effector" + (existing.effectors.Count == 1 ? "" : "s") : string.Empty) + " of its own)";
                labels[i + 1] = names[i].Replace('/', '\u2215') + own;
                if (names[i] == scopeSlot)
                {
                    index = i + 1;
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(
                new GUIContent("Edit bone entries for", "All slots: the bone entries every converted slot follows. One slot: bone entries for that slot alone, which replace the ones for all slots bone by bone. Effectors do not follow this switch: each one says which slots it acts on, under Effectors. Build the preview to list its slots here."),
                index, labels);
            if (EditorGUI.EndChangeCheck())
            {
                scopeSlot = picked <= 0 ? string.Empty : names[picked - 1];
                treeDirty = true;
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(!InSlotScope || profile.GetSlotOverride(scopeSlot) == null))
            {
                if (GUILayout.Button(new GUIContent("Clear", "Remove everything this slot has of its own, its bone entries and the effectors kept to it, so it follows the profile for all slots again."), GUILayout.Width(48f)))
                {
                    Undo.RecordObject(profile, "Remove Slot Entries");
                    profile.RemoveSlotOverride(scopeSlot);
                    if (selectedEffectorSlot == scopeSlot)
                    {
                        selectedEffector = -1;   // it went with the slot's entries
                    }
                    MarkProfileChanged();
                    treeDirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (InSlotScope)
            {
                string note = $"Bone changes apply to '{scopeSlot}' only. Bones it has no entry of its own for keep following the entries for all slots. Effectors say which slots they act on themselves, under Effectors.";
                if (preview != null && preview.IsBuilt && !IsInPreview(scopeSlot))
                {
                    note += " This slot is not in the preview, so its changes are not shown.";
                }
                EditorGUILayout.LabelField(note, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void MirrorWholeSide(bool leftToRight)
        {
            EnsureRig();
            Undo.RecordObject(profile, leftToRight ? "Mirror Rig Profile Left to Right" : "Mirror Rig Profile Right to Left");
            int mirrored = profile.MirrorSide(leftToRight, targetRig, Scope);
            if (mirrored > 0)
            {
                MarkProfileChanged();
                treeDirty = true;
            }
            ShowNotification(new GUIContent(mirrored == 1 ? "1 bone mirrored" : mirrored + " bones mirrored"));
        }

        /// <summary>The selected bone's opposite-side bone, when this rig has one.</summary>
        private string OppositeOfSelected()
        {
            string opposite = UMASlotRigConversionProfile.GetOppositeBoneName(selectedBone);
            return opposite != null && boneIds != null && boneIds.ContainsKey(opposite) ? opposite : null;
        }

        /// <summary>
        /// With mirror mode on, makes the opposite bone's entry the mirror image of the selected
        /// bone's, or removes it when the selected bone no longer has one. Call after an edit, inside
        /// the same undo step.
        /// </summary>
        private void MirrorSelectedIfOn()
        {
            if (!mirror || profile == null)
            {
                return;
            }
            string opposite = OppositeOfSelected();
            if (opposite == null)
            {
                return;
            }
            if (HasOwnEntry(selectedBone))
            {
                profile.MirrorCorrection(selectedBone, targetRig, Scope);
            }
            else
            {
                profile.RemoveCorrection(opposite, Scope);
            }
        }

        private void DrawPreviewSection()
        {
            showPreviewSetup = EditorGUILayout.Foldout(showPreviewSetup, "Preview character", true);
            if (showPreviewSetup)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Recipes or slots built for the legacy rig, to dress the preview.", EditorStyles.wordWrappedMiniLabel);
                bool enabledChanged;
                bool changed = DrawRecipeList(out enabledChanged);
                EditorGUILayout.Space(4f);
                changed |= DrawRecipeFolderSection(out bool folderWornChanged);
                enabledChanged |= folderWornChanged;
                EditorGUILayout.Space(4f);
                changed |= DrawList(previewSlots, "Slots (an overlay is picked from the slot's folder)");
                if (GUILayout.Button("Add the recipes and slots selected in the Project window"))
                {
                    foreach (UnityEngine.Object selected in Selection.objects)
                    {
                        if (selected is UMATextRecipe recipe && !previewRecipes.Contains(recipe))
                        {
                            previewRecipes.Add(recipe);
                            changed = true;
                        }
                        else if (selected is SlotDataAsset slot && !previewSlots.Contains(slot))
                        {
                            previewSlots.Add(slot);
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    SavePreviewSelection();
                }
                EditorGUI.indentLevel--;
                if (enabledChanged && livePreview && preview != null && preview.IsBuilt)
                {
                    // Switching a recipe on or off redresses the character; the camera stays where it is.
                    BuildPreview(frame: false);
                    GUIUtility.ExitGUI();
                }
            }

            bool built = preview != null && preview.IsBuilt;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(race == null))
            {
                if (GUILayout.Button(built ? "Rebuild preview" : "Build preview", GUILayout.Height(24f)))
                {
                    BuildPreview();
                    GUIUtility.ExitGUI();
                }
            }
            using (new EditorGUI.DisabledScope(!built))
            {
                if (GUILayout.Button("Close", GUILayout.Height(24f), GUILayout.Width(56f)))
                {
                    ClosePreview();
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button(new GUIContent("Frame", "Frame the preview character in the Scene view."), GUILayout.Height(24f), GUILayout.Width(56f)))
                {
                    FramePreview();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            int pose = GUILayout.Toolbar(restPose ? 0 : 1, PoseLabels);
            if (EditorGUI.EndChangeCheck())
            {
                restPose = pose == 0;
                if (built)
                {
                    preview.SetPose(restPose);
                    painterDirty = true;   // the body moved with the pose
                    SceneView.RepaintAll();
                }
            }
            livePreview = GUILayout.Toggle(livePreview, new GUIContent("Live", "Rebuild the preview after every change. Turn off to rebuild only with the button."), GUILayout.Width(48f));
            EditorGUILayout.EndHorizontal();

            if (preview == null)
            {
                return;
            }
            for (int i = 0; i < preview.Messages.Count; i++)
            {
                EditorGUILayout.HelpBox(preview.Messages[i], MessageType.Warning);
            }
            if (built)
            {
                int converted = 0;
                for (int i = 0; i < preview.Slots.Count; i++)
                {
                    if (preview.Slots[i].Converted) converted++;
                }
                EditorGUILayout.LabelField($"{converted} of {preview.Slots.Count} slot(s) converted in {preview.LastConvertMilliseconds:0} ms; mesh rebuilt in {preview.LastBuildMilliseconds:0} ms.", EditorStyles.miniLabel);
                for (int i = 0; i < preview.Slots.Count; i++)
                {
                    UMARigProfilePreview.PreviewSlot slot = preview.Slots[i];
                    if (!string.IsNullOrEmpty(slot.Note) && slot.Original != null)
                    {
                        EditorGUILayout.LabelField(slot.Original.slotName + ": " + slot.Note, EditorStyles.wordWrappedMiniLabel);
                    }
                }
                if (!restPose)
                {
                    EditorGUILayout.LabelField("Scene handles work in the rest pose, the pose the offsets are defined in.", EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        /// <summary>
        /// Automatic Mesh Hide Assets for the preview's recipes: the body triangles each recipe's
        /// clothing covers, saved next to the recipe and added to it.
        /// </summary>
        private void DrawMeshHideSection()
        {
            showMeshHideSection = EditorGUILayout.Foldout(showMeshHideSection, "Mesh hides", true);
            if (!showMeshHideSection)
            {
                return;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                "Creates a Mesh Hide Asset for each body slot a recipe's clothing covers, saves it in a MeshHides folder next to the recipe, and adds it to the recipe. " +
                "The clothing is converted with this profile first, so run it again after changing the fit.",
                EditorStyles.wordWrappedMiniLabel);

            meshHideSettings.strategy = (MeshHideAsset.TriangleHideStrategy)EditorGUILayout.EnumPopup(
                new GUIContent("Triangle strategy", "Strict hides a triangle only when all three of its vertices are covered, so nothing visible is removed. Weighted needs two and Conservative one: they hide more and leave less to poke through, but can open gaps at collars and cuffs."),
                meshHideSettings.strategy);
            meshHideSettings.outwardDistance = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Outward distance", "How far, in metres, to look along each body vertex's normal for clothing that covers it."), meshHideSettings.outwardDistance));
            meshHideSettings.inwardDistance = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Inward distance", "How far, in metres, to look below the surface for clothing the body pokes through."), meshHideSettings.inwardDistance));
            meshHideSettings.requireSameFacing = EditorGUILayout.Toggle(
                new GUIContent("Only clothing lying over it", "Count clothing only where its surface faces the same way as the body under it. Keeps the inside of a bare arm from being hidden because it faces the side of a vest."),
                meshHideSettings.requireSameFacing);

            EditorGUI.BeginChangeCheck();
            showMeshHidesInPreview = EditorGUILayout.Toggle(
                new GUIContent("Show in preview", "Apply the recipes' mesh hides to the preview character, as a built character does. Turn off to see the whole body and look for poke-through."),
                showMeshHidesInPreview);
            if (EditorGUI.EndChangeCheck() && preview != null && preview.IsBuilt)
            {
                preview.SetShowMeshHides(showMeshHidesInPreview);
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            showClothing = EditorGUILayout.Toggle(
                new GUIContent("Show clothing", "Turn off to take the previewed clothing off the character and look at the body under it. The hides stay applied while Show in preview is on."),
                showClothing);
            if (EditorGUI.EndChangeCheck() && preview != null && preview.IsBuilt)
            {
                preview.SetShowClothing(showClothing);
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            showHideOverlay = EditorGUILayout.Toggle(
                new GUIContent("Mark hidden in red", "Draw every body triangle the preview's recipes hide in red, in the Scene view."),
                showHideOverlay);
            using (new EditorGUI.DisabledScope(!showHideOverlay))
            {
                overlayThroughClothing = EditorGUILayout.Toggle(
                    new GUIContent("Red through clothing", "Draw the red marks over the clothing, so what is hidden under it can be seen without taking the clothing off."),
                    overlayThroughClothing);
            }
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }

            int recipeCount = EnabledPreviewRecipes().Count;
            using (new EditorGUI.DisabledScope(race == null || recipeCount == 0))
            {
                string label = recipeCount == 1 ? "Create mesh hides for the preview recipe" : $"Create mesh hides for the {recipeCount} preview recipes";
                if (GUILayout.Button(new GUIContent(label, "Writes Mesh Hide Assets next to each switched-on recipe and adds them to it. Running it again updates the same assets.")))
                {
                    CreateMeshHides();
                    GUIUtility.ExitGUI();
                }
            }
            if (recipeCount == 0)
            {
                EditorGUILayout.LabelField(previewRecipes.Exists(r => r != null)
                    ? "Every preview recipe is switched off. Tick a recipe under Preview character to make hides for it."
                    : "Add wardrobe recipes under Preview character first. Slots added without a recipe have no recipe to carry a hide.", EditorStyles.wordWrappedMiniLabel);
            }
            if (preview != null && preview.IsBuilt)
            {
                EditorGUILayout.LabelField(
                    preview.ShowMeshHides ? $"The preview hides {preview.HiddenTriangles} body triangle(s)." : "The preview shows the whole body.",
                    EditorStyles.miniLabel);
            }
            for (int i = 0; i < meshHideReport.Count; i++)
            {
                EditorGUILayout.LabelField(meshHideReport[i], EditorStyles.wordWrappedMiniLabel);
            }
            DrawHideBrushControls();
            DrawHideList();
            EditorGUI.indentLevel--;
        }

        /// <summary>The correction brush: small fixes to what the generator hid, painted on the preview character.</summary>
        private void DrawHideBrushControls()
        {
            bool built = preview != null && preview.IsBuilt;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Correction brush", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(!built))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent("Paint", "Show: drag over red triangles to make them visible again. Hide: drag over the body to hide more of it. Alt-drag still turns the view."));
                EditorGUI.BeginChangeCheck();
                int newMode = GUILayout.Toolbar(built ? brushMode : BrushOff, BrushLabels);
                if (EditorGUI.EndChangeCheck())
                {
                    brushMode = newMode;
                    if (brushMode != BrushOff)
                    {
                        showHideOverlay = true;
                    }
                    SceneView.RepaintAll();
                }
                EditorGUILayout.EndHorizontal();

                brushRadius = EditorGUILayout.Slider(
                    new GUIContent("Brush radius (m)", "Triangles whose centre is within this distance of the cursor, and that face the view, are painted. Zero paints the single triangle under the cursor."),
                    brushRadius, 0f, 0.1f);

                if (brushMode == BrushHide && built)
                {
                    IReadOnlyList<UMATextRecipe> recipes = preview.WornRecipes;
                    if (recipes.Count == 0)
                    {
                        EditorGUILayout.LabelField("Hiding needs a recipe to keep the hide on. Add a recipe under Preview character.", EditorStyles.wordWrappedMiniLabel);
                    }
                    else
                    {
                        var labels = new string[recipes.Count];
                        int index = 0;
                        UMATextRecipe current = BrushRecipe();
                        for (int i = 0; i < recipes.Count; i++)
                        {
                            labels[i] = recipes[i] != null ? recipes[i].name : "(missing)";
                            if (recipes[i] == current)
                            {
                                index = i;
                            }
                        }
                        index = EditorGUILayout.Popup(new GUIContent("Hide for recipe", "The recipe whose Mesh Hide Asset takes the triangles you paint hidden."), index, labels);
                        brushRecipe = recipes[index];
                    }
                }
            }
            if (!built)
            {
                EditorGUILayout.LabelField("Build the preview to see and correct what is hidden.", EditorStyles.wordWrappedMiniLabel);
            }
            else if (brushMode != BrushOff)
            {
                EditorGUILayout.LabelField(
                    "Painted triangles are stored in the hide as never-hide or always-hide marks, so creating the hides again keeps them. Bone handles are off while the brush is on.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>The hides the preview's recipes apply to the body, each with its lock and a way into the Face Editor.</summary>
        private void DrawHideList()
        {
            UMAMeshHidePainter current = EnsurePainter();
            if (current == null || current.Hides.Count == 0)
            {
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Hides on the preview ({current.HiddenTriangles} of {current.TotalTriangles} body triangles)", EditorStyles.miniBoldLabel);
            for (int i = 0; i < current.Hides.Count; i++)
            {
                UMAMeshHidePainter.WornHide worn = current.Hides[i];
                MeshHideAsset hide = worn.Asset;
                if (hide == null)
                {
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                bool locked = EditorGUILayout.ToggleLeft(
                    new GUIContent("Lock", "A locked hide is finished by hand: creating mesh hides again leaves it exactly as it is, and the brush does not change it."),
                    hide.autoGenerationLocked, GUILayout.Width(64f));
                if (locked != hide.autoGenerationLocked)
                {
                    Undo.RecordObject(hide, locked ? "Lock Mesh Hide" : "Unlock Mesh Hide");
                    hide.autoGenerationLocked = locked;
                    EditorUtility.SetDirty(hide);
                }
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(hide, typeof(MeshHideAsset), false);
                }
                int marks = hide.autoKeepVisible.Count + hide.autoForceHidden.Count;
                GUILayout.Label(new GUIContent(hide.HiddenCount + (marks > 0 ? " (" + marks + " painted)" : string.Empty),
                    "Triangles this hide hides, and how many of its triangles carry a painted never-hide or always-hide mark."), EditorStyles.miniLabel, GUILayout.Width(96f));
                using (new EditorGUI.DisabledScope(marks == 0 || hide.autoGenerationLocked))
                {
                    if (GUILayout.Button(new GUIContent("Forget", "Drop this hide's painted marks. Its triangles stay as they are until the hides are created again."), EditorStyles.miniButton, GUILayout.Width(52f)))
                    {
                        Undo.RecordObject(hide, "Forget Painted Mesh Hide Marks");
                        hide.autoKeepVisible.Clear();
                        hide.autoForceHidden.Clear();
                        EditorUtility.SetDirty(hide);
                    }
                }
                if (GUILayout.Button(new GUIContent("Face Editor", "Open this hide in the Face Editor on the preview character, for more than the brush is good for. Lock the hide afterwards so it is not generated over."), EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    OpenInFaceEditor(hide);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void CreateMeshHides()
        {
            List<UMATextRecipe> recipes = EnabledPreviewRecipes();
            var messages = new List<string>();
            List<UMAMeshHideAutoGenerator.RecipeResult> results = UMAMeshHideAutoGenerator.GenerateAll(recipes, race, profile, meshHideSettings, messages);
            meshHideReport = new List<string>(messages);
            for (int i = 0; i < results.Count; i++)
            {
                meshHideReport.Add(results[i].Summary());
                meshHideReport.AddRange(results[i].Messages);
            }
            if (preview != null && preview.IsBuilt)
            {
                preview.Refresh();
                if (painter != null)
                {
                    painter.ReloadHides(preview);
                }
                SceneView.RepaintAll();
            }
            Repaint();
        }

        /// <summary>
        /// What the preview wears: the recipes in the list (added by hand or loaded from a recipe folder)
        /// that are switched on, in list order, without blanks or repeats.
        /// </summary>
        private List<UMATextRecipe> EnabledPreviewRecipes()
        {
            var recipes = new List<UMATextRecipe>();
            foreach (UMATextRecipe recipe in previewRecipes)
            {
                if (recipe != null && !disabledPreviewRecipes.Contains(recipe) && !recipes.Contains(recipe))
                {
                    recipes.Add(recipe);
                }
            }
            return recipes;
        }

        /// <summary>
        /// The preview recipes, each with a box that switches it on or off without taking it out of the
        /// list. Returns true when the list or a switch changed; <paramref name="enabledChanged"/> is true
        /// when what the preview wears changed.
        /// </summary>
        private bool DrawRecipeList(out bool enabledChanged)
        {
            enabledChanged = false;
            bool changed = false;
            disabledPreviewRecipes.RemoveAll(r => r == null || !previewRecipes.Contains(r));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Recipes", EditorStyles.miniBoldLabel);
            using (new EditorGUI.DisabledScope(!previewRecipes.Exists(r => r != null)))
            {
                if (GUILayout.Button(new GUIContent("All", "Switch every recipe on."), EditorStyles.miniButtonLeft, GUILayout.Width(40f)) && disabledPreviewRecipes.Count > 0)
                {
                    disabledPreviewRecipes.Clear();
                    changed = enabledChanged = true;
                }
                if (GUILayout.Button(new GUIContent("None", "Switch every recipe off."), EditorStyles.miniButtonMid, GUILayout.Width(44f)))
                {
                    foreach (UMATextRecipe recipe in previewRecipes)
                    {
                        if (recipe != null && !disabledPreviewRecipes.Contains(recipe))
                        {
                            disabledPreviewRecipes.Add(recipe);
                            changed = enabledChanged = true;
                        }
                    }
                }
            }
            using (new EditorGUI.DisabledScope(previewRecipes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("Clear", "Remove every recipe from the list."), EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                {
                    enabledChanged = EnabledPreviewRecipes().Count > 0;
                    previewRecipes.Clear();
                    disabledPreviewRecipes.Clear();
                    loadedRecipeFolder = string.Empty;
                    changed = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < previewRecipes.Count; i++)
            {
                UMATextRecipe current = previewRecipes[i];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                using (new EditorGUI.DisabledScope(current == null))
                {
                    bool on = current != null && !disabledPreviewRecipes.Contains(current);
                    bool now = GUILayout.Toggle(on, new GUIContent(string.Empty, on
                        ? "Worn by the preview. Untick to take it off without removing it from the list."
                        : "Switched off: not worn by the preview, and no mesh hides are made for it."), GUILayout.Width(16f));
                    if (current != null && now != on)
                    {
                        if (now) disabledPreviewRecipes.Remove(current);
                        else disabledPreviewRecipes.Add(current);
                        changed = enabledChanged = true;
                    }
                }
                int indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                var value = (UMATextRecipe)EditorGUILayout.ObjectField(current, typeof(UMATextRecipe), false);
                EditorGUI.indentLevel = indent;
                if (value != current)
                {
                    bool wasOff = current != null && disabledPreviewRecipes.Contains(current);
                    disabledPreviewRecipes.Remove(current);
                    previewRecipes[i] = value;
                    if (wasOff && value != null && !disabledPreviewRecipes.Contains(value))
                    {
                        disabledPreviewRecipes.Add(value);   // a replaced recipe keeps its row's switch
                    }
                    changed = true;
                }
                using (new EditorGUI.DisabledScope(current == null))
                {
                    if (GUILayout.Button(new GUIContent("Inspect", "Open this recipe in a popup Inspector."), EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        InspectRecipe(current);
                    }
                }
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    if (current != null && !disabledPreviewRecipes.Contains(current))
                    {
                        enabledChanged = true;   // it was being worn
                    }
                    previewRecipes.RemoveAt(i);
                    disabledPreviewRecipes.RemoveAll(r => r == null || !previewRecipes.Contains(r));
                    EditorGUILayout.EndHorizontal();
                    return true;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button("+", GUILayout.Width(22f)))
            {
                previewRecipes.Add(null);
                changed = true;
            }
            if (disabledPreviewRecipes.Count > 0)
            {
                GUILayout.Label($"{EnabledPreviewRecipes().Count} of {previewRecipes.FindAll(r => r != null).Count} worn", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
            return changed;
        }

        private static bool DrawList<T>(List<T> list, string label) where T : UnityEngine.Object
        {
            bool changed = false;
            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var value = (T)EditorGUILayout.ObjectField(list[i], typeof(T), false);
                if (value != list[i])
                {
                    list[i] = value;
                    changed = true;
                }
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    list.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    return true;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button("+", GUILayout.Width(22f)))
            {
                list.Add(null);
                changed = true;
            }
            EditorGUILayout.EndHorizontal();
            return changed;
        }

        // --- effectors ---------------------------------------------------------------------------

        /// <summary>The effector being edited, or null.</summary>
        private UMARigEffector SelectedEffector()
        {
            List<UMARigEffector> list = profile != null ? profile.GetEffectorList(string.IsNullOrEmpty(selectedEffectorSlot) ? null : selectedEffectorSlot) : null;
            return list != null && selectedEffector >= 0 && selectedEffector < list.Count ? list[selectedEffector] : null;
        }

        /// <summary>Makes an effector the one being edited, wherever the profile keeps it; null selects none.</summary>
        private void SelectEffector(UMARigEffector effector)
        {
            selectedEffector = -1;
            selectedEffectorSlot = string.Empty;
            if (effector != null && profile.TryGetEffectorSlot(effector, out string slot))
            {
                selectedEffectorSlot = slot ?? string.Empty;
                selectedEffector = profile.GetEffectorList(slot).IndexOf(effector);
            }
            effectorReachDirty = true;
            effectorVerticesDirty = true;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Effectors: regions that reshape converted slots where they pass through, whatever the
        /// vertices there are weighted to. Every effector of the profile is listed, under what it acts
        /// on: all slots, or the one slot it is kept to. The switch for bone entries does not apply here.
        /// </summary>
        private void DrawEffectorSection()
        {
            showEffectorSection = EditorGUILayout.Foldout(showEffectorSection, "Effectors", true);
            if (!showEffectorSection)
            {
                return;
            }
            EditorGUILayout.LabelField(
                "Regions that reshape converted slots where they pass through them, whatever the vertices there are weighted to. They act after the bone entries. "
                + "A new effector acts on every slot; set its Affects to keep it to one.",
                EditorStyles.wordWrappedMiniLabel);

            UMARigEffector current = SelectedEffector();
            DrawEffectorRows(null, profile.effectors, current);
            for (int i = 0; i < profile.slotOverrides.Count; i++)
            {
                UMARigSlotOverride slot = profile.slotOverrides[i];
                if (slot != null && !string.IsNullOrEmpty(slot.slotName))
                {
                    DrawEffectorRows(slot.slotName, slot.effectors, current);
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Add sphere", "A round region, placed at the selected bone. It acts on every slot until its Affects says otherwise.")))
            {
                AddEffector(UMARigEffectorShape.Sphere);
            }
            if (GUILayout.Button(new GUIContent("Add capsule", "A region with length, placed at the selected bone. Its strength can vary along its length. It acts on every slot until its Affects says otherwise.")))
            {
                AddEffector(UMARigEffectorShape.Capsule);
            }
            EditorGUILayout.EndHorizontal();

            UMARigEffector effector = SelectedEffector();
            if (effector != null)
            {
                DrawEffectorPanel(effector);
            }
            EditorGUILayout.Space();
        }

        /// <summary>One group of the list: the effectors that act on all slots (no slot name), or the ones kept to a slot.</summary>
        private void DrawEffectorRows(string slotName, List<UMARigEffector> list, UMARigEffector current)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }
            EditorGUILayout.LabelField(slotName == null
                    ? new GUIContent("Act on all slots", "These reshape every converted slot that passes through them.")
                    : new GUIContent("Act on '" + slotName + "' only", "These reshape this slot and leave every other slot alone, even where another slot passes through them."),
                EditorStyles.miniBoldLabel);
            for (int i = 0; i < list.Count; i++)
            {
                UMARigEffector row = list[i];
                if (row == null)
                {
                    continue;
                }
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12f);
                bool on = GUILayout.Toggle(row.enabled, new GUIContent(string.Empty, "Switch this effector on or off."), GUILayout.Width(18f));
                if (on != row.enabled)
                {
                    Undo.RecordObject(profile, "Toggle Effector");
                    row.enabled = on;
                    MarkProfileChanged();
                }
                string label = string.IsNullOrEmpty(row.name) ? "Effector " + (i + 1) : row.name;
                bool isCurrent = ReferenceEquals(row, current);
                bool selected = GUILayout.Toggle(isCurrent, new GUIContent(label, "Edit this effector here and with its handle in the Scene view."), EditorStyles.miniButton);
                if (selected != isCurrent)
                {
                    SelectEffector(selected ? row : null);
                }
                GUILayout.Label(row.shape + ", " + EffectorActionLabels[(int)row.action] + (row.mirror ? ", mirrored" : string.Empty), EditorStyles.miniLabel, GUILayout.Width(170f));
                if (GUILayout.Button(new GUIContent("-", "Remove this effector."), GUILayout.Width(22f)))
                {
                    Undo.RecordObject(profile, "Remove Effector");
                    list.RemoveAt(i);
                    profile.RemoveSlotOverrideIfEmpty(slotName);
                    SelectEffector(isCurrent ? null : current);   // the one being edited may have moved up a place
                    MarkProfileChanged();
                    EditorGUILayout.EndHorizontal();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void AddEffector(UMARigEffectorShape shape)
        {
            Undo.RecordObject(profile, "Add Effector");
            int count = profile.effectors.Count;
            for (int i = 0; i < profile.slotOverrides.Count; i++)
            {
                count += profile.slotOverrides[i] != null ? profile.slotOverrides[i].effectors.Count : 0;
            }
            string anchor = !string.IsNullOrEmpty(selectedBone) ? selectedBone : (boneIds != null && boneIds.ContainsKey("Hips") ? "Hips" : string.Empty);
            var added = new UMARigEffector
            {
                name = (shape == UMARigEffectorShape.Sphere ? "Sphere " : "Capsule ") + (count + 1),
                shape = shape,
                boneName = anchor,
            };
            // Every slot: an effector is a place, and a profile is reused across whatever is worn there.
            profile.effectors.Add(added);
            SelectEffector(added);
            MarkProfileChanged();
        }

        /// <summary>
        /// Which slots the effector acts on, with what it reaches in the preview. This is its own
        /// control, changed by moving the effector between the profile's lists.
        /// </summary>
        private void DrawEffectorAffects(UMARigEffector effector)
        {
            profile.TryGetEffectorSlot(effector, out string ownSlot);
            List<string> names = ScopeSlotNames();
            if (ownSlot != null && !names.Contains(ownSlot))
            {
                names.Add(ownSlot);
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            var labels = new string[names.Count + 1];
            labels[0] = "All slots";
            int index = 0;
            for (int i = 0; i < names.Count; i++)
            {
                labels[i + 1] = names[i].Replace('/', '\u2215') + " only";   // a slash would turn the name into a submenu
                if (names[i] == ownSlot)
                {
                    index = i + 1;
                }
            }
            int picked = EditorGUILayout.Popup(
                new GUIContent("Affects", "All slots: every converted slot that passes through the effector, whatever the character wears, so the profile can be reused. One slot: that slot alone. Build the preview to list its slots here."),
                index, labels);
            if (picked != index)
            {
                Undo.RecordObject(profile, "Change What Effector Affects");
                profile.SetEffectorSlot(effector, picked <= 0 ? null : names[picked - 1]);
                SelectEffector(effector);
                MarkProfileChanged();
                treeDirty = true;
                GUIUtility.ExitGUI();
            }
            DrawEffectorReach(effector, ownSlot);
            bool mark = EditorGUILayout.ToggleLeft(
                new GUIContent("Mark those vertices in the Scene view",
                    "Puts a dot on every vertex of the previewed slots that stands inside this effector: brighter where the effector is stronger, grey on a slot it leaves alone because of Affects. "
                    + "Dots hidden behind the character are drawn dim. A stretch of cloth with no dots is outside the effector, however the outline looks from where you stand. Shown in the rest pose, with the clothing on."),
                markEffectorVertices);
            if (mark != markEffectorVertices)
            {
                markEffectorVertices = mark;
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// Says which previewed slots have vertices inside the effector, and warns when the slot it is
        /// kept to is not among them: that is an effector that silently does nothing.
        /// </summary>
        private void DrawEffectorReach(UMARigEffector effector, string ownSlot)
        {
            if (preview == null || !preview.IsBuilt)
            {
                EditorGUILayout.LabelField("Build the preview to see which slots this effector reaches.", EditorStyles.wordWrappedMiniLabel);
                return;
            }
            // Measured during layout only, so the controls below are the same for layout and repaint.
            if (Event.current.type == EventType.Layout && (effectorReachDirty || !ReferenceEquals(effectorReachFor, effector)))
            {
                preview.MeasureEffector(effector, effectorReach);
                effectorReachFor = effector;
                effectorReachDirty = false;
            }
            if (!ReferenceEquals(effectorReachFor, effector))
            {
                return;   // not measured yet; the next layout does it
            }

            var reached = new System.Text.StringBuilder();
            var leftAlone = new System.Text.StringBuilder();
            int reachedVertices = 0;
            bool ownInPreview = false;
            for (int i = 0; i < effectorReach.Count; i++)
            {
                UMARigProfilePreview.EffectorReach reach = effectorReach[i];
                bool acts = ownSlot == null || reach.SlotName == ownSlot;
                ownInPreview |= ownSlot != null && reach.SlotName == ownSlot;
                if (reach.Inside <= 0)
                {
                    continue;
                }
                System.Text.StringBuilder list = acts ? reached : leftAlone;
                list.Append(list.Length > 0 ? ", " : string.Empty).Append(reach.SlotName).Append(" (").Append(reach.Inside).Append(')');
                if (acts)
                {
                    reachedVertices += reach.Inside;
                }
            }

            string state = !effector.enabled ? " It is switched off." : !effector.IsActive ? " With its current values it moves nothing." : string.Empty;
            string others = leftAlone.Length > 0 ? " Inside it, and left alone because of Affects: " + leftAlone + "." : string.Empty;
            if (ownSlot != null && !ownInPreview)
            {
                EditorGUILayout.LabelField($"'{ownSlot}' is not in the preview, so what this effector does is not shown." + others + state, EditorStyles.wordWrappedMiniLabel);
            }
            else if (reachedVertices == 0)
            {
                string nothing = ownSlot != null
                    ? $"No vertex of '{ownSlot}' is inside this effector, so it changes nothing."
                    : "No vertex of the previewed slots is inside this effector, so it changes nothing here.";
                EditorGUILayout.HelpBox(nothing + others + (leftAlone.Length > 0 ? " Set Affects to All slots, or to the slot you mean." : string.Empty) + state, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Vertices inside it: " + reached + "." + others + state, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawEffectorPanel(UMARigEffector effector)
        {
            string name = effector.name;
            string bone = effector.boneName;
            UMARigEffectorShape shape = effector.shape;
            Vector3 position = effector.position;
            Vector3 rotation = effector.rotationEuler;
            float radius = effector.radius;
            float falloff = effector.falloff;
            float length = effector.length;
            // A copy: the curve editor changes the curve it is given, and undo must see the old one.
            AnimationCurve curve = effector.axisCurve != null ? new AnimationCurve(effector.axisCurve.keys) : AnimationCurve.Constant(0f, 1f, 1f);
            bool mirrored = effector.mirror;
            UMARigEffectorAction action = effector.action;
            float amount = effector.amount;
            Vector3 move = effector.move;
            Vector3 scale = effector.SafeScale;
            float reach = effector.reach;
            int smoothing = effector.smoothing;
            bool useSelectedBone = false;

            DrawEffectorAffects(effector);
            EditorGUI.BeginChangeCheck();
            name = EditorGUILayout.TextField("Name", name);
            EditorGUILayout.BeginHorizontal();
            bone = EditorGUILayout.TextField(new GUIContent("Placed from bone", "The bone of this race the effector is placed from. Empty places it from the character's origin."), bone);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedBone) || selectedBone == effector.boneName))
            {
                useSelectedBone = GUILayout.Button(new GUIContent("Use selected", "Place the effector from the bone selected in the list below. It stays where it is."), EditorStyles.miniButton, GUILayout.Width(88f));
            }
            EditorGUILayout.EndHorizontal();
            shape = (UMARigEffectorShape)EditorGUILayout.EnumPopup("Shape", shape);
            position = EditorGUILayout.Vector3Field(new GUIContent("Position (m)", "The centre, measured from the bone's joint."), position);
            if (shape == UMARigEffectorShape.Capsule)
            {
                rotation = EditorGUILayout.Vector3Field(new GUIContent("Rotation (deg)", "A capsule runs along its own y axis; with no rotation that is straight up."), rotation);
                length = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Length (m)", "Distance between the centres of the capsule's two rounded ends."), length));
                curve = EditorGUILayout.CurveField(new GUIContent("Along the length", "Strength along the capsule, from its lower end (left) to its upper end (right). Shape it to fade the effect in and out along the capsule."),
                    curve, EffectorColor, new Rect(0f, 0f, 1f, 1f));
            }
            else if (action == UMARigEffectorAction.Scale)
            {
                rotation = EditorGUILayout.Vector3Field(new GUIContent("Rotation (deg)", "Turns the axes the scale works along."), rotation);
            }
            radius = Mathf.Max(0.001f, EditorGUILayout.FloatField(new GUIContent("Radius (m)", "Nothing further than this from the centre (or the capsule's centre line) is touched."), radius));
            falloff = EditorGUILayout.Slider(new GUIContent("Falloff", "How much of the radius the effect fades over. 0 is full strength up to a hard edge; 1 fades all the way from the centre."), falloff, 0f, 1f);
            mirrored = EditorGUILayout.Toggle(new GUIContent("Mirror", "Also apply the mirror image of this effector on the other side of the character."), mirrored);

            action = (UMARigEffectorAction)EditorGUILayout.Popup(new GUIContent("Action", "What happens to the vertices inside."), (int)action, EffectorActionLabels);
            switch (action)
            {
                case UMARigEffectorAction.Push:
                    amount = EditorGUILayout.FloatField(new GUIContent("Distance (m)", "How far the vertices are pushed away from the centre (or the capsule's centre line) at full strength. Negative pulls them in."), amount);
                    break;
                case UMARigEffectorAction.Move:
                    move = EditorGUILayout.Vector3Field(new GUIContent("Offset (m)", "The shift at full strength, in avatar space."), move);
                    break;
                case UMARigEffectorAction.Scale:
                    scale = EditorGUILayout.Vector3Field(new GUIContent("Scale", "About the effector's centre, along its own axes."), scale);
                    break;
                default:
                    amount = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Clearance (m)", "The gap to leave between the skin and the cloth."), amount));
                    reach = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Reach (m)", "How far under the skin cloth is still brought out. Cloth deeper than this is left alone."), reach));
                    smoothing = EditorGUILayout.IntSlider(new GUIContent("Smoothing", "How many times the push is spread to neighbouring vertices, so moved cloth blends into unmoved cloth."), smoothing, 0, 8);
                    break;
            }
            if (EditorGUI.EndChangeCheck() || useSelectedBone)
            {
                Undo.RecordObject(profile, "Edit Effector");
                if (useSelectedBone && targetRig != null)
                {
                    // Keep the effector where it is while it changes the bone it is measured from.
                    effector.GetFrame(targetRig, false, out Vector3 centre, out _);
                    targetRig.TryGetAvatarPosition(UMAUtils.StringToHash(selectedBone), out Vector3 newAnchor);
                    bone = selectedBone;
                    position = centre - newAnchor;
                }
                effector.name = name;
                effector.boneName = bone;
                effector.shape = shape;
                effector.position = position;
                effector.rotationEuler = rotation;
                effector.radius = radius;
                effector.falloff = falloff;
                effector.length = length;
                effector.axisCurve = curve;
                effector.mirror = mirrored;
                effector.action = action;
                effector.amount = amount;
                effector.move = move;
                effector.scale = scale;
                effector.reach = reach;
                effector.smoothing = smoothing;
                MarkProfileChanged();
                SceneView.RepaintAll();
            }

            if (action == UMARigEffectorAction.ClearBody && preview != null && preview.IsBuilt && (preview.BodySurface == null || preview.BodySurface.TriangleCount == 0))
            {
                EditorGUILayout.HelpBox("The preview character has no skin to measure against, so this effector does nothing in the preview.", MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Scene handle", "Which handle the Scene view shows for this effector. W, E and R switch it too. Select a bone in the list to get the bone's handle back."));
            EditorGUI.BeginChangeCheck();
            int newHandle = GUILayout.Toolbar((int)handleMode, EffectorHandleLabels);
            if (EditorGUI.EndChangeCheck())
            {
                handleMode = (HandleMode)newHandle;
                Tools.current = handleMode == HandleMode.Move ? Tool.Move : handleMode == HandleMode.Rotate ? Tool.Rotate : Tool.Scale;
                lastUnityTool = Tools.current;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws every effector of the profile in the Scene view and runs the selected one's handle.
        /// Returns true while an effector is selected, in which case the bone handles step aside.
        /// </summary>
        private bool HandleEffectors(Transform avatar)
        {
            if (!showEffectorSection || profile == null)
            {
                return false;
            }
            UMARigEffector effector = SelectedEffector();
            UMARigRestPose rig = preview.TargetRig;
            float size = avatar.lossyScale.x;
            DrawEffectorOutlines(profile.effectors, effector, avatar, rig, size);
            for (int i = 0; i < profile.slotOverrides.Count; i++)
            {
                if (profile.slotOverrides[i] != null)
                {
                    DrawEffectorOutlines(profile.slotOverrides[i].effectors, effector, avatar, rig, size);
                }
            }
            if (effector == null)
            {
                return false;
            }
            if (markEffectorVertices && preview.RestPose && preview.ShowClothing)
            {
                DrawEffectorVertices(effector, avatar);
            }
            effector.GetFrame(rig, false, out Vector3 centre, out Quaternion rotation);
            Vector3 world = avatar.TransformPoint(centre);
            Quaternion worldRotation = avatar.rotation * rotation;
            Handles.Label(world, " " + (string.IsNullOrEmpty(effector.name) ? "Effector" : effector.name), EditorStyles.whiteBoldLabel);
            if (!preview.RestPose)
            {
                return true;   // placed against the rest pose; shown, not edited, in the T-pose
            }
            if (Tools.current != lastUnityTool)
            {
                lastUnityTool = Tools.current;
                if (lastUnityTool == Tool.Move) handleMode = HandleMode.Move;
                else if (lastUnityTool == Tool.Rotate) handleMode = HandleMode.Rotate;
                else if (lastUnityTool == Tool.Scale) handleMode = HandleMode.Scale;
                Repaint();
            }

            Vector3 newPosition = effector.position;
            Vector3 newEuler = effector.rotationEuler;
            float newRadius = effector.radius;
            float newLength = effector.length;
            EditorGUI.BeginChangeCheck();
            switch (handleMode)
            {
                case HandleMode.Move:
                {
                    Vector3 moved = Handles.PositionHandle(world, avatar.rotation);
                    newPosition = effector.position + (avatar.InverseTransformPoint(moved) - centre);
                    break;
                }
                case HandleMode.Rotate:
                {
                    Quaternion turned = Handles.RotationHandle(worldRotation, world);
                    newEuler = SignedEuler((Quaternion.Inverse(avatar.rotation) * turned).eulerAngles);
                    break;
                }
                default:
                {
                    newRadius = Mathf.Max(0.001f, Handles.RadiusHandle(worldRotation, world, effector.radius * size) / Mathf.Max(size, 1e-6f));
                    if (effector.shape == UMARigEffectorShape.Capsule)
                    {
                        // A cone at each end sets the length; the capsule stays centred.
                        Vector3 axis = worldRotation * Vector3.up;
                        float half = Mathf.Max(0f, effector.length) * 0.5f * size;
                        float cone = HandleUtility.GetHandleSize(world) * 0.12f;
                        Vector3 top = Handles.Slider(world + axis * half, axis, cone, Handles.ConeHandleCap, 0f);
                        Vector3 bottom = Handles.Slider(world - axis * half, -axis, cone, Handles.ConeHandleCap, 0f);
                        float topHalf = Vector3.Dot(top - world, axis);
                        float bottomHalf = Vector3.Dot(world - bottom, axis);
                        float changed = Mathf.Abs(topHalf - half) > Mathf.Abs(bottomHalf - half) ? topHalf : bottomHalf;
                        newLength = Mathf.Max(0f, changed * 2f / Mathf.Max(size, 1e-6f));
                    }
                    break;
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Adjust Effector");
                effector.position = newPosition;
                effector.rotationEuler = newEuler;
                effector.radius = newRadius;
                effector.length = newLength;
                MarkProfileChanged();
            }
            return true;
        }

        /// <summary>
        /// Dots on the vertices the selected effector holds, on the character as the preview shows it.
        /// Dots behind something are drawn dim rather than left out, so what the effector holds on the
        /// far side still shows.
        /// </summary>
        private void DrawEffectorVertices(UMARigEffector effector, Transform avatar)
        {
            if (effectorVerticesDirty || !ReferenceEquals(effectorVerticesFor, effector) || effectorVerticesConvert != preview.ConvertCount)
            {
                preview.CollectEffectorVertices(effector, effectorVertices);
                effectorVerticesFor = effector;
                effectorVerticesConvert = preview.ConvertCount;
                effectorVerticesDirty = false;
            }
            Camera camera = Camera.current;
            if (Event.current.type != EventType.Repaint || camera == null || effectorVertices.Count == 0)
            {
                return;
            }
            Color previousColor = Handles.color;
            UnityEngine.Rendering.CompareFunction previousTest = Handles.zTest;
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraBack = -camera.transform.forward;
            for (int pass = 0; pass < 2; pass++)
            {
                // First the hidden ones, dim; then the ones in view.
                Handles.zTest = pass == 0 ? UnityEngine.Rendering.CompareFunction.Greater : UnityEngine.Rendering.CompareFunction.LessEqual;
                for (int i = 0; i < effectorVertices.Count; i++)
                {
                    UMARigProfilePreview.EffectorVertex vertex = effectorVertices[i];
                    Vector3 world = avatar.TransformPoint(vertex.Position);
                    world += (camera.orthographic ? cameraBack : (cameraPosition - world).normalized) * EffectorVertexLift;
                    Color color = vertex.ActedOn ? Color.Lerp(EffectorVertexWeak, EffectorVertexStrong, vertex.Strength) : EffectorVertexLeftAlone;
                    if (pass == 0)
                    {
                        color.a = 0.22f;
                    }
                    Handles.color = color;
                    float size = HandleUtility.GetHandleSize(world) * (0.022f + 0.012f * vertex.Strength);
                    Handles.DotHandleCap(0, world, Quaternion.identity, size, EventType.Repaint);
                }
            }
            Handles.zTest = previousTest;
            Handles.color = previousColor;
        }

        private static void DrawEffectorOutlines(List<UMARigEffector> list, UMARigEffector selected, Transform avatar, UMARigRestPose rig, float size)
        {
            if (list == null)
            {
                return;
            }
            Color previous = Handles.color;
            for (int i = 0; i < list.Count; i++)
            {
                UMARigEffector other = list[i];
                if (other == null)
                {
                    continue;
                }
                bool isSelected = ReferenceEquals(other, selected);
                for (int side = 0; side < (other.mirror ? 2 : 1); side++)
                {
                    other.GetFrame(rig, side == 1, out Vector3 c, out Quaternion q);
                    Color color = EffectorColor;
                    color.a = !other.enabled ? 0.15f : isSelected ? (side == 0 ? 0.95f : 0.5f) : 0.3f;
                    Handles.color = color;
                    DrawEffectorWire(other, avatar.TransformPoint(c), avatar.rotation * q, size);
                }
            }
            Handles.color = previous;
        }

        /// <summary>The outline of an effector: its outer radius, and its core (full strength) radius where that differs.</summary>
        private static void DrawEffectorWire(UMARigEffector effector, Vector3 centre, Quaternion rotation, float size)
        {
            Vector3 axis = rotation * Vector3.up, right = rotation * Vector3.right, forward = rotation * Vector3.forward;
            float half = effector.shape == UMARigEffectorShape.Capsule ? Mathf.Max(0f, effector.length) * 0.5f * size : 0f;
            Vector3 top = centre + axis * half, bottom = centre - axis * half;
            float outer = Mathf.Max(0f, effector.radius) * size;
            DrawCapsuleWire(top, bottom, axis, right, forward, outer, half > 0f);
            float core = outer * (1f - Mathf.Clamp01(effector.falloff));
            if (core > 0.001f && core < outer - 0.001f)
            {
                Color colour = Handles.color;
                Handles.color = new Color(colour.r, colour.g, colour.b, colour.a * 0.45f);
                DrawCapsuleWire(top, bottom, axis, right, forward, core, half > 0f);
                Handles.color = colour;
            }
        }

        private static void DrawCapsuleWire(Vector3 top, Vector3 bottom, Vector3 axis, Vector3 right, Vector3 forward, float radius, bool sides)
        {
            Handles.DrawWireDisc(top, axis, radius);
            Handles.DrawWireArc(top, right, forward, -180f, radius);
            Handles.DrawWireArc(top, forward, right, 180f, radius);
            Handles.DrawWireArc(bottom, right, forward, 180f, radius);
            Handles.DrawWireArc(bottom, forward, right, -180f, radius);
            if (sides)
            {
                Handles.DrawWireDisc(bottom, axis, radius);
                Handles.DrawLine(top + right * radius, bottom + right * radius);
                Handles.DrawLine(top - right * radius, bottom - right * radius);
                Handles.DrawLine(top + forward * radius, bottom + forward * radius);
                Handles.DrawLine(top - forward * radius, bottom - forward * radius);
            }
        }

        // --- bone tree ---------------------------------------------------------------------------

        private void EnsureRig()
        {
            if (rigRace == race && (targetRig != null || rigProblem != null))
            {
                return;
            }
            rigRace = race;
            targetRig = null;
            legacyRig = null;
            rigProblem = null;
            boneNames = new List<string>();
            boneIds = new Dictionary<string, int>();
            treeDirty = true;
            if (race == null)
            {
                rigProblem = "Choose a race to list its bones.";
                return;
            }
            if (!UMARigRestPose.TryFromRace(race, out targetRig, out string reason))
            {
                rigProblem = reason;
                return;
            }
            legacyRig = race.legacyRig != null ? race.legacyRig.GetRestPose() : null;
            foreach (KeyValuePair<int, UMATransform> entry in targetRig.Bones)
            {
                if (entry.Value != null && !string.IsNullOrEmpty(entry.Value.name))
                {
                    boneNames.Add(entry.Value.name);
                }
            }
            boneNames.Sort(StringComparer.OrdinalIgnoreCase);
            // Ids follow the sorted names, so the tree keeps its expanded state from one rebuild to the next.
            for (int i = 0; i < boneNames.Count; i++)
            {
                boneIds[boneNames[i]] = i + 1;
            }
        }

        private void DrawBoneBrowser()
        {
            EditorGUILayout.LabelField("Bones", EditorStyles.boldLabel);
            if (rigProblem != null)
            {
                EditorGUILayout.HelpBox(rigProblem, MessageType.Warning);
                return;
            }

            string previousSearch = search;
            BoneFilter previousFilter = filter;
            EditorGUILayout.BeginHorizontal();
            search = EditorGUILayout.TextField(new GUIContent("Filter", "Show only bones whose name contains this text."), search);
            if (GUILayout.Button("x", GUILayout.Width(22f)))
            {
                search = string.Empty;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < FastFilters.Length; i++)
            {
                if (GUILayout.Button(FastFilters[i], EditorStyles.miniButton))
                {
                    search = FastFilters[i];
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndHorizontal();
            filter = (BoneFilter)GUILayout.Toolbar((int)filter, FilterLabels);
            if (previousSearch != search || previousFilter != filter)
            {
                treeDirty = true;
                SceneView.RepaintAll();
            }
            if (filter == BoneFilter.UsedByPreview && usedBones == null)
            {
                EditorGUILayout.LabelField("Build the preview to see which bones its slots are weighted to.", EditorStyles.wordWrappedMiniLabel);
            }

            if (tree == null)
            {
                tree = new BoneTree(treeState, this);
                treeDirty = true;
            }
            if (treeDirty)
            {
                visibleRows = BuildRows();
                tree.SetRows(visibleRows);
                if (boneIds.TryGetValue(selectedBone ?? string.Empty, out int selectedId))
                {
                    tree.Select(selectedId);
                }
                treeDirty = false;
            }

            if (IsHierarchyShown)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Expand all", EditorStyles.miniButtonLeft)) tree.ExpandAll();
                if (GUILayout.Button("Collapse all", EditorStyles.miniButtonRight)) tree.CollapseAll();
                EditorGUILayout.EndHorizontal();
            }
            // Inside the window's scroll view the tree cannot stretch to the bottom of the window, so it
            // is sized to its rows, up to most of the window's height; it scrolls its own rows beyond that.
            float treeHeight = Mathf.Clamp(tree.totalHeight + 4f, 140f, Mathf.Max(140f, position.height * 0.6f));
            Rect treeRect = GUILayoutUtility.GetRect(100f, 10000f, treeHeight, treeHeight);
            tree.OnGUI(treeRect);
        }

        private bool IsHierarchyShown => string.IsNullOrEmpty(search) && filter == BoneFilter.All;

        private List<BoneRow> BuildRows()
        {
            var rows = new List<BoneRow>();
            if (targetRig == null)
            {
                return rows;
            }
            if (IsHierarchyShown)
            {
                AddChildren(rows, targetRig.RootBoneHash, 0, 0);
                return rows;
            }

            // A search or a filter flattens the tree to the bones that match, by name.
            for (int i = 0; i < boneNames.Count; i++)
            {
                string name = boneNames[i];
                if (!string.IsNullOrEmpty(search) && name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (filter == BoneFilter.Favorites && !IsFavorite(name)) continue;
                if (filter == BoneFilter.Edited && !IsEdited(name)) continue;
                if (filter == BoneFilter.UsedByPreview && (usedBones == null || !usedBones.Contains(UMAUtils.StringToHash(name)))) continue;
                rows.Add(new BoneRow { Id = boneIds[name], Depth = 0, Name = name });
            }
            return rows;
        }

        private void AddChildren(List<BoneRow> rows, int parentHash, int depth, int guard)
        {
            if (guard > 256)
            {
                return;
            }
            var children = new List<string>();
            foreach (int child in targetRig.GetChildren(parentHash))
            {
                if (targetRig.TryGetBone(child, out UMATransform bone) && boneIds.ContainsKey(bone.name))
                {
                    children.Add(bone.name);
                }
            }
            children.Sort(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < children.Count; i++)
            {
                rows.Add(new BoneRow { Id = boneIds[children[i]], Depth = depth, Name = children[i] });
                AddChildren(rows, UMAUtils.StringToHash(children[i]), depth + 1, guard + 1);
            }
        }

        private bool IsFavorite(string boneName) => profile != null && profile.IsFavorite(boneName);

        /// <summary>True when an entry applies to the bone in the current scope: the scope's own, or the one for all slots.</summary>
        private bool IsEdited(string boneName) => profile != null && profile.TryGetCorrection(boneName, Scope, out _);

        /// <summary>True when the bone has an entry in the current scope itself.</summary>
        private bool HasOwnEntry(string boneName)
        {
            if (profile == null)
            {
                return false;
            }
            return InSlotScope ? profile.HasSlotCorrection(boneName, scopeSlot) : profile.TryGetCorrection(boneName, out _);
        }

        private void ToggleFavorite(string boneName)
        {
            if (profile == null)
            {
                return;
            }
            Undo.RecordObject(profile, "Toggle Favorite Bone");
            profile.SetFavorite(boneName, !profile.IsFavorite(boneName));
            EditorUtility.SetDirty(profile);
            if (filter == BoneFilter.Favorites)
            {
                treeDirty = true;
            }
            Repaint();
        }

        private void SelectBone(string boneName)
        {
            if (selectedBone == boneName)
            {
                return;
            }
            selectedBone = boneName ?? string.Empty;
            selectedEffector = -1;   // the Scene view handle goes back to the bone
            Repaint();
            SceneView.RepaintAll();
        }

        private string NameForId(int id)
        {
            return boneNames != null && id >= 1 && id <= boneNames.Count ? boneNames[id - 1] : null;
        }

        private sealed class BoneTree : TreeView
        {
            private const int EmptyId = int.MaxValue;
            private readonly UMARigProfileEditorWindow owner;
            private List<BoneRow> rows = new List<BoneRow>();
            private GUIContent starContent;

            public BoneTree(TreeViewState state, UMARigProfileEditorWindow owner) : base(state)
            {
                this.owner = owner;
                showAlternatingRowBackgrounds = true;
                rowHeight = 18f;
                Reload();
            }

            public void SetRows(List<BoneRow> newRows)
            {
                rows = newRows ?? new List<BoneRow>();
                Reload();
            }

            public void Select(int id)
            {
                SetSelection(new List<int> { id });
            }

            protected override TreeViewItem BuildRoot()
            {
                var root = new TreeViewItem(0, -1, "Root");
                var items = new List<TreeViewItem>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                {
                    items.Add(new TreeViewItem(rows[i].Id, rows[i].Depth, rows[i].Name));
                }
                if (items.Count == 0)
                {
                    items.Add(new TreeViewItem(EmptyId, 0, "No bones match"));
                }
                SetupParentsAndChildrenFromDepths(root, items);
                return root;
            }

            protected override bool CanMultiSelect(TreeViewItem item)
            {
                return false;
            }

            protected override void SelectionChanged(IList<int> selectedIds)
            {
                if (selectedIds != null && selectedIds.Count > 0 && selectedIds[0] != EmptyId)
                {
                    owner.SelectBone(owner.NameForId(selectedIds[0]));
                }
            }

            protected override void RowGUI(RowGUIArgs args)
            {
                if (args.item.id == EmptyId)
                {
                    base.RowGUI(args);
                    return;
                }

                string boneName = args.item.displayName;
                Rect row = args.rowRect;
                var starRect = new Rect(row.x + GetContentIndent(args.item), row.y + 1f, 16f, 16f);
                bool favorite = owner.IsFavorite(boneName);
                if (starContent == null)
                {
                    Texture icon = EditorGUIUtility.FindTexture("Favorite Icon");
                    starContent = icon != null
                        ? new GUIContent(icon, "Star this bone to find it under Favorites.")
                        : new GUIContent("*", "Star this bone to find it under Favorites.");
                }
                Color previous = GUI.color;
                GUI.color = favorite ? new Color(1f, 0.8f, 0.2f, 1f) : new Color(1f, 1f, 1f, 0.25f);
                if (GUI.Button(starRect, starContent, GUIStyle.none))
                {
                    owner.ToggleFavorite(boneName);
                }
                GUI.color = previous;

                var labelRect = new Rect(starRect.xMax + 4f, row.y, row.xMax - starRect.xMax - 4f, row.height);
                // Bold: an entry in the scope being edited. With one slot chosen, a bone that only
                // follows the entry for all slots is marked instead.
                bool own = owner.HasOwnEntry(boneName);
                bool followsGeneral = !own && owner.IsEdited(boneName);
                GUI.Label(labelRect, followsGeneral ? boneName + "  (all slots)" : boneName, own ? EditorStyles.boldLabel : EditorStyles.label);
            }
        }

        // --- selected bone -----------------------------------------------------------------------

        private void DrawBonePanel()
        {
            EditorGUILayout.Space();
            if (string.IsNullOrEmpty(selectedBone))
            {
                EditorGUILayout.LabelField("Select a bone to adjust it.", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            bool hasEntry = profile.TryGetCorrection(selectedBone, Scope, out UMARigBoneCorrection entry);
            bool ownEntry = HasOwnEntry(selectedBone);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(selectedBone, EditorStyles.boldLabel);
            bool favorite = profile.IsFavorite(selectedBone);
            bool newFavorite = GUILayout.Toggle(favorite, new GUIContent("Favorite", "Star this bone to find it under Favorites."), EditorStyles.miniButton, GUILayout.Width(64f));
            if (newFavorite != favorite)
            {
                ToggleFavorite(selectedBone);
            }
            EditorGUILayout.EndHorizontal();
            if (legacyRig != null && !legacyRig.Contains(UMAUtils.StringToHash(selectedBone)))
            {
                EditorGUILayout.LabelField("The legacy rig has no bone of this name, so only offsets apply here.", EditorStyles.wordWrappedMiniLabel);
            }
            if (InSlotScope)
            {
                string state = ownEntry ? $"'{scopeSlot}' has its own entry for this bone."
                    : hasEntry ? $"Showing the entry for all slots. Changing a value gives '{scopeSlot}' its own entry, starting from these values."
                    : $"No entry. Changing a value gives '{scopeSlot}' its own entry for this bone.";
                EditorGUILayout.LabelField(state, EditorStyles.wordWrappedMiniLabel);
            }

            UMARigBoneCorrectionMode mode = hasEntry ? entry.mode : profile.defaultMode;
            bool includeChildren = !hasEntry || entry.includeChildren;
            Vector3 position = hasEntry ? entry.positionOffset : Vector3.zero;
            Vector3 rotation = hasEntry ? entry.rotationOffsetEuler : Vector3.zero;
            Vector3 scale = hasEntry ? entry.SafeScale : Vector3.one;

            EditorGUI.BeginChangeCheck();
            mode = (UMARigBoneCorrectionMode)EditorGUILayout.EnumPopup(new GUIContent("Mode", "How the computed correction is applied to this bone."), mode);
            includeChildren = EditorGUILayout.Toggle(new GUIContent("Carry to child bones", "Apply this bone's offsets to everything below it too, the way moving a real bone carries its children."), includeChildren);
            position = ChannelField(new GUIContent("Position (m)", "Extra shift in avatar space."), position, Vector3.zero);
            rotation = ChannelField(new GUIContent("Rotation (deg)", "Extra turn about this bone's joint."), rotation, Vector3.zero);
            scale = ChannelField(new GUIContent("Scale", "Extra scale about this bone's joint, along the bone's own axes."), scale, Vector3.one);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Edit Rig Profile Bone");
                entry = profile.GetOrAddCorrection(selectedBone, Scope);
                entry.mode = mode;
                entry.includeChildren = includeChildren;
                entry.positionOffset = position;
                entry.rotationOffsetEuler = rotation;
                entry.scaleOffset = scale;
                MirrorSelectedIfOn();
                MarkProfileChanged();
            }

            DrawMirrorRow(hasEntry);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Scene handle", "Which handle the Scene view shows for this bone. W, E and R switch it too."));
            EditorGUI.BeginChangeCheck();
            int newHandle = GUILayout.Toolbar((int)handleMode, HandleLabels);
            if (EditorGUI.EndChangeCheck())
            {
                handleMode = (HandleMode)newHandle;
                Tools.current = handleMode == HandleMode.Move ? Tool.Move : handleMode == HandleMode.Rotate ? Tool.Rotate : Tool.Scale;
                lastUnityTool = Tools.current;
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!hasEntry))
            {
                if (GUILayout.Button(new GUIContent("Reset", InSlotScope
                    ? "Put position, rotation and scale back to neutral for this slot, whatever the entry for all slots says."
                    : "Put position, rotation and scale back to neutral. The entry and its mode stay.")))
                {
                    Undo.RecordObject(profile, "Reset Rig Profile Bone");
                    profile.GetOrAddCorrection(selectedBone, Scope).ResetOffsets();
                    MirrorSelectedIfOn();
                    MarkProfileChanged();
                }
            }
            using (new EditorGUI.DisabledScope(!ownEntry))
            {
                GUIContent removeLabel = InSlotScope
                    ? new GUIContent("Remove slot entry", "Remove this slot's own entry for the bone, so it follows the entry for all slots again.")
                    : new GUIContent("Remove entry", "Remove this bone from the profile, so it follows the default mode again.");
                if (GUILayout.Button(removeLabel))
                {
                    Undo.RecordObject(profile, "Remove Rig Profile Bone");
                    profile.RemoveCorrection(selectedBone, Scope);
                    MirrorSelectedIfOn();
                    MarkProfileChanged();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>The mirror toggle, with the opposite bone it acts on, and a one-off copy when it is off.</summary>
        private void DrawMirrorRow(bool hasEntry)
        {
            string opposite = OppositeOfSelected();
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            bool newMirror = EditorGUILayout.ToggleLeft(
                new GUIContent(opposite != null ? "Mirror to " + opposite : "Mirror (this bone has no opposite side)",
                    "While on, every change to a left or right bone is applied, mirrored, to the bone on the other side. Bones on the centre line are not affected."),
                mirror);
            if (EditorGUI.EndChangeCheck())
            {
                mirror = newMirror;
                EditorPrefs.SetBool(MirrorPrefsKey, mirror);
                SceneView.RepaintAll();
            }
            using (new EditorGUI.DisabledScope(opposite == null || !hasEntry))
            {
                if (GUILayout.Button(new GUIContent("Copy across", "Make the opposite bone the mirror image of this one now, once."), EditorStyles.miniButton, GUILayout.Width(84f)))
                {
                    Undo.RecordObject(profile, "Mirror Rig Profile Bone");
                    profile.MirrorCorrection(selectedBone, targetRig, Scope);
                    MarkProfileChanged();
                    treeDirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>A Vector3 field with an "O" button that puts the channel back to its neutral value.</summary>
        private static Vector3 ChannelField(GUIContent label, Vector3 value, Vector3 neutral)
        {
            EditorGUILayout.BeginHorizontal();
            value = EditorGUILayout.Vector3Field(label, value);
            if (GUILayout.Button(new GUIContent("O", "Reset this channel."), GUILayout.Width(22f)))
            {
                value = neutral;
                GUI.changed = true;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private void MarkProfileChanged()
        {
            if (profile == null)
            {
                return;
            }
            profile.InvalidateLookup();
            EditorUtility.SetDirty(profile);
            previewDirty = true;
            effectorReachDirty = true;
            effectorVerticesDirty = true;
            if (filter == BoneFilter.Edited || InSlotScope)
            {
                treeDirty = true;
            }
            Repaint();
        }

        private void CreateProfile()
        {
            string folder = "Assets";
            string baseName = "Rig Conversion Profile";
            if (race != null)
            {
                string racePath = AssetDatabase.GetAssetPath(race);
                if (!string.IsNullOrEmpty(racePath))
                {
                    folder = Path.GetDirectoryName(racePath).Replace('\\', '/');
                }
                baseName = race.raceName + " Legacy Rig Profile";
            }
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + baseName + ".asset");
            var created = CreateInstance<UMASlotRigConversionProfile>();
            AssetDatabase.CreateAsset(created, path);
            if (race != null && race.legacyRigProfile == null)
            {
                Undo.RecordObject(race, "Assign Legacy Rig Profile");
                race.legacyRigProfile = created;
                EditorUtility.SetDirty(race);
                UMALegacySlotConverter.ClearCache();
            }
            AssetDatabase.SaveAssets();
            SetProfile(created);
            EditorGUIUtility.PingObject(created);
        }

        // --- preview -----------------------------------------------------------------------------

        private void BuildPreview(bool frame = true)
        {
            ClosePreview();
            preview = new UMARigProfilePreview();
            if (!showMeshHidesInPreview)
            {
                preview.SetShowMeshHides(false);
            }
            if (!showClothing)
            {
                preview.SetShowClothing(false);
            }
            bool built = preview.Build(race, profile, EnabledPreviewRecipes(), previewSlots, restPose);
            painterDirty = true;
            effectorReachDirty = true;
            effectorVerticesDirty = true;
            usedBones = built ? preview.GetSkinnedBoneHashes() : null;
            previewDirty = false;
            treeDirty = true;
            lastRefreshTime = EditorApplication.timeSinceStartup;
            if (built && frame)
            {
                FramePreview();
            }
            SceneView.RepaintAll();
            Repaint();
        }

        private void ClosePreview()
        {
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }
            if (painter != null)
            {
                painter.Dispose();
                painter = null;
            }
            painterDirty = true;
            inFaceEditor = false;
            usedBones = null;
            previewDirty = false;
            if (toolsHiddenByUs)
            {
                Tools.hidden = false;
                toolsHiddenByUs = false;
            }
            if (filter == BoneFilter.UsedByPreview)
            {
                treeDirty = true;
            }
            SceneView.RepaintAll();
        }

        private void FramePreview()
        {
            if (preview != null && preview.TryGetBounds(out Bounds bounds) && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }
        }

        private void OnEditorUpdate()
        {
            if (inFaceEditor)
            {
                if (StageUtility.GetCurrentStage() is FaceEditorStage)
                {
                    return;   // the Face Editor has the character; leave it alone until it closes
                }
                ReturnFromFaceEditor();
            }
            if (!previewDirty || !livePreview || preview == null || !preview.IsBuilt)
            {
                return;
            }
            if (EditorApplication.timeSinceStartup - lastRefreshTime < MinimumRefreshInterval)
            {
                return;
            }
            previewDirty = false;
            preview.Refresh();
            lastRefreshTime = EditorApplication.timeSinceStartup;
            SceneView.RepaintAll();
            Repaint();
        }

        private void OnUndoRedo()
        {
            ReloadPaintedHides();
            if (profile == null)
            {
                return;
            }
            profile.InvalidateLookup();
            previewDirty = true;
            treeDirty = true;
            effectorReachDirty = true;
            effectorVerticesDirty = true;
            Repaint();
        }

        /// <summary>An undo or redo may have put painted hide flags back; read them again.</summary>
        private void ReloadPaintedHides()
        {
            if (painter == null || preview == null || !preview.IsBuilt || inFaceEditor)
            {
                return;
            }
            painter.ReloadHides(preview);
            previewDirty = true;
            SceneView.RepaintAll();
        }

        private string PreviewPrefsKey
        {
            get
            {
                string path = race != null ? AssetDatabase.GetAssetPath(race) : null;
                return string.IsNullOrEmpty(path) ? null : PreviewPrefsPrefix + AssetDatabase.AssetPathToGUID(path);
            }
        }

        /// <summary>Remembers, per race, what the preview character wears.</summary>
        private void SavePreviewSelection()
        {
            string key = PreviewPrefsKey;
            if (key == null)
            {
                return;
            }
            var parts = new List<string>();
            foreach (UMATextRecipe recipe in previewRecipes)
            {
                // "x:" is a recipe kept in the list but switched off.
                if (recipe != null) parts.Add((disabledPreviewRecipes.Contains(recipe) ? "x:" : "r:") + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(recipe)));
            }
            foreach (SlotDataAsset slot in previewSlots)
            {
                if (slot != null) parts.Add("s:" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(slot)));
            }
            AppendRecipeFolderParts(parts);
            EditorPrefs.SetString(key, string.Join(";", parts));
        }

        private void LoadPreviewSelection()
        {
            previewRecipes.Clear();
            disabledPreviewRecipes.Clear();
            previewSlots.Clear();
            // A race with no saved root folder keeps the current one.
            loadedRecipeFolder = string.Empty;
            recipeFoldersDirty = true;
            string key = PreviewPrefsKey;
            if (key == null)
            {
                return;
            }
            foreach (string part in EditorPrefs.GetString(key, string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length < 3)
                {
                    continue;
                }
                string path = AssetDatabase.GUIDToAssetPath(part.Substring(2));
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                if (TryLoadRecipeFolderPart(part, path))
                {
                    continue;
                }
                bool switchedOff = part.StartsWith("x:", StringComparison.Ordinal);
                if (switchedOff || part.StartsWith("r:", StringComparison.Ordinal))
                {
                    var recipe = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(path);
                    if (recipe != null)
                    {
                        previewRecipes.Add(recipe);
                        if (switchedOff && !disabledPreviewRecipes.Contains(recipe))
                        {
                            disabledPreviewRecipes.Add(recipe);
                        }
                    }
                }
                else if (part.StartsWith("s:", StringComparison.Ordinal))
                {
                    var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
                    if (slot != null) previewSlots.Add(slot);
                }
            }
        }

        // --- scene view --------------------------------------------------------------------------

        private void OnSceneGUI(SceneView sceneView)
        {
            // The Face Editor draws its own copy of the character in its own stage; nothing of this
            // window's belongs in that view.
            bool built = preview != null && preview.IsBuilt && !inFaceEditor;
            Transform avatar = built ? preview.AvatarObject.transform : null;
            bool brushing = false;
            if (built)
            {
                DrawHideOverlay(avatar);
                brushing = HandleHideBrush(sceneView, avatar);
            }

            bool editingEffector = built && profile != null && !brushing && HandleEffectors(avatar);
            bool active = built && profile != null && !brushing && !editingEffector;
            if (!active)
            {
                if (brushing || editingEffector)
                {
                    // The brush owns the mouse, and an effector brings its own handle; Unity's own
                    // transform handle would get in the way of either.
                    Tools.hidden = true;
                    toolsHiddenByUs = true;
                }
                else if (toolsHiddenByUs)
                {
                    Tools.hidden = false;
                    toolsHiddenByUs = false;
                }
                return;
            }

            DrawBoneDots(avatar);

            if (string.IsNullOrEmpty(selectedBone))
            {
                return;
            }
            int hash = UMAUtils.StringToHash(selectedBone);
            if (!preview.TargetRig.TryGetAvatarMatrix(hash, out Matrix4x4 boneToAvatar))
            {
                return;
            }

            profile.TryGetCorrection(selectedBone, Scope, out UMARigBoneCorrection entry);
            Vector3 jointAvatar = boneToAvatar.GetColumn(3);
            Vector3 offset = entry != null ? entry.positionOffset : Vector3.zero;
            Vector3 euler = entry != null ? entry.rotationOffsetEuler : Vector3.zero;
            Vector3 scale = entry != null ? entry.SafeScale : Vector3.one;
            Vector3 world = avatar.TransformPoint(jointAvatar + offset);
            Handles.Label(world, " " + selectedBone, EditorStyles.whiteBoldLabel);
            DrawMirrorMarker(avatar);
            if (!preview.RestPose)
            {
                return;
            }

            // The handle replaces Unity's own while a bone is being edited; W, E and R still switch it.
            Tools.hidden = true;
            toolsHiddenByUs = true;
            if (Tools.current != lastUnityTool)
            {
                lastUnityTool = Tools.current;
                if (lastUnityTool == Tool.Move) handleMode = HandleMode.Move;
                else if (lastUnityTool == Tool.Rotate) handleMode = HandleMode.Rotate;
                else if (lastUnityTool == Tool.Scale) handleMode = HandleMode.Scale;
                Repaint();
            }

            EditorGUI.BeginChangeCheck();
            switch (handleMode)
            {
                case HandleMode.Move:
                {
                    Vector3 moved = Handles.PositionHandle(world, avatar.rotation);
                    offset = avatar.InverseTransformPoint(moved) - jointAvatar;
                    break;
                }
                case HandleMode.Rotate:
                {
                    Quaternion current = avatar.rotation * Quaternion.Euler(euler);
                    Quaternion turned = Handles.RotationHandle(current, world);
                    euler = SignedEuler((Quaternion.Inverse(avatar.rotation) * turned).eulerAngles);
                    break;
                }
                default:
                {
                    // Scale acts along the bone's own rest axes, so the handle is turned to match them.
                    Quaternion frame = avatar.rotation * Quaternion.Normalize(boneToAvatar.rotation);
                    scale = Handles.ScaleHandle(scale, world, frame, HandleUtility.GetHandleSize(world));
                    break;
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Adjust Rig Profile Bone");
                entry = profile.GetOrAddCorrection(selectedBone, Scope);
                entry.positionOffset = offset;
                entry.rotationOffsetEuler = euler;
                entry.scaleOffset = scale;
                MirrorSelectedIfOn();
                MarkProfileChanged();
            }
        }

        // --- mesh hide overlay and brush ---------------------------------------------------------

        /// <summary>The painter for the preview character, read again when the character changed. Null without a preview.</summary>
        private UMAMeshHidePainter EnsurePainter()
        {
            if (preview == null || !preview.IsBuilt)
            {
                return null;
            }
            if (painter == null)
            {
                painter = new UMAMeshHidePainter();
                painterDirty = true;
            }
            if (painterDirty)
            {
                painter.Rebuild(preview);
                painterDirty = false;
            }
            return painter;
        }

        private void DrawHideOverlay(Transform avatar)
        {
            if (!showHideOverlay || Event.current.type != EventType.Repaint)
            {
                return;
            }
            UMAMeshHidePainter current = EnsurePainter();
            if (current != null)
            {
                current.Draw(avatar.localToWorldMatrix, HiddenColor, overlayThroughClothing);
            }
        }

        /// <summary>The recipe whose hide takes triangles painted hidden: the chosen one while the preview wears it, else the first.</summary>
        private UMATextRecipe BrushRecipe()
        {
            if (preview == null || !preview.IsBuilt || preview.WornRecipes.Count == 0)
            {
                return null;
            }
            for (int i = 0; i < preview.WornRecipes.Count; i++)
            {
                if (preview.WornRecipes[i] == brushRecipe)
                {
                    return brushRecipe;
                }
            }
            return preview.WornRecipes[0];
        }

        /// <summary>
        /// Runs the correction brush in the Scene view. Returns true while the brush is switched on,
        /// in which case it takes left-button drags (Alt still orbits) and the bone handles step aside.
        /// </summary>
        private bool HandleHideBrush(SceneView sceneView, Transform avatar)
        {
            if (brushMode == BrushOff)
            {
                return false;
            }
            UMAMeshHidePainter current = EnsurePainter();
            if (current == null || current.Parts.Count == 0)
            {
                return false;
            }

            Event e = Event.current;
            int controlId = GUIUtility.GetControlID(BrushControlHint, FocusType.Passive);
            EventType type = e.GetTypeForControl(controlId);
            if (type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(controlId);
                return true;
            }

            Ray world = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var ray = new Ray(avatar.InverseTransformPoint(world.origin), avatar.InverseTransformDirection(world.direction).normalized);
            switch (type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && !e.alt && HandleUtility.nearestControl == controlId)
                    {
                        GUIUtility.hotControl = controlId;
                        PaintHides(current, ray);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        PaintHides(current, ray);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        EndHideStroke(current);
                    }
                    break;
                case EventType.MouseMove:
                    sceneView.Repaint();
                    break;
                case EventType.Repaint:
                    if (current.Raycast(ray, out UMAMeshHidePainter.Hit hit))
                    {
                        Vector3 centre = avatar.TransformPoint(hit.Point);
                        Color previous = Handles.color;
                        Handles.color = brushMode == BrushHide ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(0.3f, 1f, 0.4f, 1f);
                        float size = brushRadius > 0f ? brushRadius * avatar.lossyScale.x : HandleUtility.GetHandleSize(centre) * 0.03f;
                        Handles.DrawWireDisc(centre, -world.direction, size);
                        Handles.color = previous;
                    }
                    break;
            }
            return true;
        }

        private void PaintHides(UMAMeshHidePainter current, Ray ray)
        {
            if (current.Paint(ray, brushRadius, brushMode == BrushHide, BrushRecipe()) > 0)
            {
                SceneView.RepaintAll();
                Repaint();
            }
        }

        /// <summary>A stroke is over: store it in the hides and let the preview character show it.</summary>
        private void EndHideStroke(UMAMeshHidePainter current)
        {
            if (current.CommitStroke() == 0)
            {
                return;
            }
            if (preview != null && preview.IsBuilt && preview.ShowMeshHides)
            {
                preview.Refresh();
            }
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>
        /// Opens a hide in the Face Editor on the preview character, for work the brush is too small for.
        /// The Face Editor works on the character as it is drawn, so the preview is rebuilt with every
        /// triangle present first.
        /// </summary>
        private void OpenInFaceEditor(MeshHideAsset hide)
        {
            if (preview == null || !preview.IsBuilt || hide == null)
            {
                return;
            }
            brushMode = BrushOff;
            preview.SetShowMeshHides(false);
            inFaceEditor = FaceEditorStage.ShowStage(preview.Avatar, hide) != null;
            if (!inFaceEditor)
            {
                preview.SetShowMeshHides(showMeshHidesInPreview);
            }
        }

        /// <summary>Back from the Face Editor: read the hides again and show the character as before.</summary>
        private void ReturnFromFaceEditor()
        {
            inFaceEditor = false;
            if (preview == null || !preview.IsBuilt)
            {
                return;
            }
            if (preview.ShowMeshHides == showMeshHidesInPreview)
            {
                preview.Refresh();
            }
            else
            {
                preview.SetShowMeshHides(showMeshHidesInPreview);
            }
            painterDirty = true;
            SceneView.RepaintAll();
            Repaint();
        }

        /// <summary>Marks the opposite bone in the Scene view while mirror mode is on, so it is clear what else moves.</summary>
        private void DrawMirrorMarker(Transform avatar)
        {
            if (!mirror)
            {
                return;
            }
            string opposite = OppositeOfSelected();
            if (opposite == null || !preview.TargetRig.TryGetAvatarMatrix(UMAUtils.StringToHash(opposite), out Matrix4x4 oppositeToAvatar))
            {
                return;
            }
            profile.TryGetCorrection(opposite, Scope, out UMARigBoneCorrection oppositeEntry);
            Vector3 joint = oppositeToAvatar.GetColumn(3);
            Vector3 world = avatar.TransformPoint(joint + (oppositeEntry != null ? oppositeEntry.positionOffset : Vector3.zero));
            Color previous = Handles.color;
            Handles.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            if (Event.current.type == EventType.Repaint)
            {
                Handles.SphereHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * 0.08f, EventType.Repaint);
            }
            Handles.Label(world, " " + opposite + " (mirror)", EditorStyles.whiteMiniLabel);
            Handles.color = previous;
        }

        /// <summary>Clickable dots on the bones a filter or search has narrowed the list to.</summary>
        private void DrawBoneDots(Transform avatar)
        {
            if (IsHierarchyShown || visibleRows == null || visibleRows.Count == 0 || visibleRows.Count > MaximumSceneDots || !preview.RestPose)
            {
                return;
            }
            for (int i = 0; i < visibleRows.Count; i++)
            {
                string boneName = visibleRows[i].Name;
                if (!preview.TargetRig.TryGetAvatarMatrix(UMAUtils.StringToHash(boneName), out Matrix4x4 boneToAvatar))
                {
                    continue;
                }
                Vector3 world = avatar.TransformPoint(boneToAvatar.GetColumn(3));
                float size = HandleUtility.GetHandleSize(world) * 0.04f;
                bool selected = boneName == selectedBone;
                Handles.color = selected ? Color.yellow : (IsEdited(boneName) ? new Color(0.3f, 0.9f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.6f));
                if (Handles.Button(world, Quaternion.identity, size, size * 1.5f, Handles.DotHandleCap) && !selected)
                {
                    SelectBone(boneName);
                    if (tree != null && boneIds.TryGetValue(boneName, out int id))
                    {
                        tree.Select(id);
                    }
                }
            }
            Handles.color = Color.white;
        }

        private static Vector3 SignedEuler(Vector3 euler)
        {
            return new Vector3(SignedAngle(euler.x), SignedAngle(euler.y), SignedAngle(euler.z));
        }

        private static float SignedAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f) degrees -= 360f;
            if (degrees < -180f) degrees += 360f;
            return degrees;
        }
    }

    /// <summary>Adds an "open the editor" button above the profile's own fields.</summary>
    [CustomEditor(typeof(UMASlotRigConversionProfile))]
    public sealed class UMASlotRigConversionProfileInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            if (GUILayout.Button(new GUIContent("Open Rig Profile Editor", "Edit this profile against a live preview character."), GUILayout.Height(24f)))
            {
                UMARigProfileEditorWindow.Open(null, (UMASlotRigConversionProfile)target);
            }
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
