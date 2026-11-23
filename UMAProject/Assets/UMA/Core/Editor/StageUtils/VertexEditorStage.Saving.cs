using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Saving in the Vertex Editor. Every way of saving is in the Save panel of the Vertex Editor
    /// window (the floating window), each with what it writes; the left panel only says what is not
    /// saved yet. Leaving with unsaved work asks how to save it.
    /// </summary>
    /// <remarks>
    /// A save into a slot asset writes the sculpt into that asset, so the sculpt's starting point
    /// moves to what was written (a later save adds only what was sculpted since). Saves that keep
    /// the whole sculpt somewhere else (a Mesh Modifier, a blendshape, a new slot) remember the
    /// sculpt as saved, so leaving does not ask about it again until it changes.
    /// </remarks>
    public partial class VertexEditorStage
    {
        internal enum SaveMethod
        {
            MeshModifier,
            BaseSlot,
            NewSlot,
            Blendshape,
            BlendshapeEdit,
            ColorsToSlot,
            Selection,
        }

        /// <summary>The work that no save has kept yet.</summary>
        internal sealed class UnsavedWork
        {
            public readonly List<string> sculptSlots = new List<string>();
            public readonly List<string> paintSlots = new List<string>();
            public bool blendshapeEdit;
            public string blendshapeName;
            public bool advanced;
            public bool selection;

            public bool Any => sculptSlots.Count > 0 || paintSlots.Count > 0 || blendshapeEdit || advanced || selection;

            public string Describe()
            {
                var parts = new List<string>();
                if (blendshapeEdit) parts.Add("changes to the blendshape '" + blendshapeName + "'");
                else if (sculptSlots.Count > 0) parts.Add("sculpt, normals or UVs on " + ListNames(sculptSlots));
                if (paintSlots.Count > 0) parts.Add("vertex colors on " + ListNames(paintSlots));
                if (advanced) parts.Add("Advanced adjustments or vertex-set modifiers");
                if (selection) parts.Add("the vertex selection");
                return parts.Count == 0 ? "Everything is saved." : "Not saved: " + string.Join("; ", parts) + ".";
            }

            private static string ListNames(List<string> names)
            {
                if (names.Count <= 3) return string.Join(", ", names);
                return string.Join(", ", names.GetRange(0, 3)) + " and " + (names.Count - 3) + " more";
            }
        }

        [NonSerialized] private string saveStatusMessage = string.Empty;
        [NonSerialized] private MessageType saveStatusType = MessageType.Info;
        [NonSerialized] private UnityEngine.Object saveStatusAsset;
        [NonSerialized] private UnsavedWork unsavedWorkCache;
        [NonSerialized] private double unsavedWorkNextRefresh;

        private void SetSaveStatus(string message, MessageType type, UnityEngine.Object asset = null)
        {
            saveStatusMessage = message ?? string.Empty;
            saveStatusType = type;
            saveStatusAsset = asset;
            unsavedWorkCache = null;
            sculptChangesUiDirty = true;
            RepaintLinkedEditors();
        }

        // ---- What is not saved --------------------------------------------------

        /// <summary>
        /// What no save has kept yet. Comparing every slot is too slow for every GUI event, so the
        /// answer is reused for half a second unless <paramref name="refresh"/> is set.
        /// </summary>
        internal UnsavedWork GetUnsavedWork(bool refresh = false)
        {
            if (!refresh && unsavedWorkCache != null && EditorApplication.timeSinceStartup < unsavedWorkNextRefresh)
            {
                return unsavedWorkCache;
            }
            var work = new UnsavedWork();
            if (BakedMesh != null)
            {
                BakedMesh.GetVertices(sculptChangeVertices);
                List<Vector3> normals = GetSculptChangeNormals();
                List<Vector2> uvs = GetSculptChangeUVs();
                foreach (SculptSlotEditState state in sculptSlotStates.Values)
                {
                    if (state != null && state.slot != null && IsSculptStateUnsaved(state, sculptChangeVertices, normals, uvs))
                    {
                        work.sculptSlots.Add(state.slot.slotName);
                    }
                }
            }
            work.sculptSlots.Sort(StringComparer.Ordinal);
            if (IsEditingBlendshape)
            {
                work.blendshapeName = editBlendshapeName;
                work.blendshapeEdit = unsavedShapes.Count > 0 || work.sculptSlots.Count > 0;
            }
            if (vertexPaintDirty)
            {
                work.paintSlots.AddRange(vertexPaintDirtySlots);
                if (work.paintSlots.Count == 0) work.paintSlots.Add(vertexPaintSlot != null ? vertexPaintSlot.slotName : "a slot");
                work.paintSlots.Sort(StringComparer.Ordinal);
            }
            work.advanced = modifierEditor != null && modifierEditor.HasUnsavedModifierEdits();
            work.selection = HasUnsavedSelectionChanges();
            unsavedWorkCache = work;
            unsavedWorkNextRefresh = EditorApplication.timeSinceStartup + 0.5;
            return work;
        }

        private bool IsSculptStateUnsaved(SculptSlotEditState state, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
        {
            if (!HasSculptChanges(state, vertices, normals, uvs))
            {
                return false;
            }
            return !state.hasSavedChecksum || SculptStateChecksum(state, vertices, normals, uvs) != state.savedChecksum;
        }

        private static long SculptStateChecksum(SculptSlotEditState state, List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs)
        {
            unchecked
            {
                long hash = (long)14695981039346656037UL;
                int end = Mathf.Min(state.start + state.count, vertices.Count);
                for (int i = state.start; i < end; i++)
                {
                    hash = (hash ^ vertices[i].GetHashCode()) * 1099511628211L;
                }
                if (state.normalsEdited && normals != null)
                {
                    end = Mathf.Min(state.start + state.count, normals.Count);
                    for (int i = state.start; i < end; i++)
                    {
                        hash = (hash ^ normals[i].GetHashCode()) * 1099511628211L;
                    }
                }
                if (state.uvEdited && uvs != null)
                {
                    end = Mathf.Min(state.start + state.count, uvs.Count);
                    for (int i = state.start; i < end; i++)
                    {
                        hash = (hash ^ uvs[i].GetHashCode()) * 1099511628211L;
                    }
                }
                return hash;
            }
        }

        /// <summary>Remembers these slots' sculpt as saved: leaving does not ask about it until it changes.</summary>
        private void MarkSculptStatesSaved(IEnumerable<SculptSlotEditState> states)
        {
            if (BakedMesh == null || states == null)
            {
                return;
            }
            BakedMesh.GetVertices(sculptChangeVertices);
            BakedMesh.GetNormals(sculptChangeNormals);
            BakedMesh.GetUVs(0, sculptChangeUVs);
            foreach (SculptSlotEditState state in states)
            {
                if (state == null) continue;
                state.savedChecksum = SculptStateChecksum(state, sculptChangeVertices, sculptChangeNormals, sculptChangeUVs);
                state.hasSavedChecksum = true;
            }
            unsavedWorkCache = null;
            sculptChangesUiDirty = true;
        }

        /// <summary>
        /// After the sculpt was written into the slot's asset: the slot as it is now is the starting
        /// point, so a later save adds only what is sculpted from here on.
        /// </summary>
        private void RebaseSculptState(SculptSlotEditState state)
        {
            if (state == null || BakedMesh == null || state.start < 0 || state.start + state.count > BakedMesh.vertexCount)
            {
                return;
            }
            Array.Copy(BakedMesh.vertices, state.start, state.originalVertices, 0, state.count);
            Vector3[] normals = BakedMesh.normals;
            if (state.originalNormals != null && normals != null && state.start + state.count <= normals.Length)
            {
                Array.Copy(normals, state.start, state.originalNormals, 0, state.count);
                state.hasOriginalNormals = true;
            }
            state.normalsEdited = false;
            Vector2[] uvs = BakedMesh.uv;
            if (state.originalUVs != null && uvs != null && state.start + state.count <= uvs.Length)
            {
                Array.Copy(uvs, state.start, state.originalUVs, 0, state.count);
            }
            Vector4[] tangents = BakedMesh.tangents;
            if (state.originalTangents != null && tangents != null && state.start + state.count <= tangents.Length)
            {
                Array.Copy(tangents, state.start, state.originalTangents, 0, state.count);
            }
            state.uvEdited = false;
            state.hasSavedChecksum = false;
            unsavedWorkCache = null;
            sculptChangesUiDirty = true;
        }

        /// <summary>Called by the Vertex Editor window after it saved a Mesh Modifier with everything in it.</summary>
        internal void NoteMeshModifierSaved(MeshModifier asset)
        {
            MarkSculptStatesSaved(sculptSlotStates.Values);
            MarkVertexPaintSaved();
            SetSaveStatus("Saved the Mesh Modifier '" + (asset != null ? asset.name : "?") + "'" +
                          (asset != null ? " at " + AssetDatabase.GetAssetPath(asset) : string.Empty) + ".",
                MessageType.Info, asset);
        }

        /// <summary>
        /// Called by the Vertex Editor window after it saved the session as one Mesh Modifier per wardrobe
        /// recipe (and one for slots no recipe has): everything is in them.
        /// </summary>
        internal void NoteMeshModifiersSaved(List<MeshModifier> assets, bool addedToRecipes)
        {
            if (assets == null || assets.Count == 0)
            {
                return;
            }
            if (assets.Count == 1 && !addedToRecipes)
            {
                NoteMeshModifierSaved(assets[0]);
                return;
            }
            MarkSculptStatesSaved(sculptSlotStates.Values);
            MarkVertexPaintSaved();
            var names = new List<string>();
            foreach (MeshModifier asset in assets)
            {
                if (asset != null) names.Add("'" + asset.name + "'");
            }
            SetSaveStatus("Saved the Mesh Modifier" + (names.Count == 1 ? " " : "s ") + string.Join(", ", names) +
                          (addedToRecipes ? " and added each to its wardrobe recipe. The character wears them through the recipes when you leave the editor." : "."),
                MessageType.Info, assets[0]);
        }

        // ---- Save methods -----------------------------------------------------------

        private bool HasMeshModifierContent()
        {
            return HasSculptChanges() || vertexPaintDirty ||
                   (modifierEditor != null && modifierEditor.HasModifierContent());
        }

        /// <summary>
        /// Call after writing a slot asset. The Global Library lets go of what it holds for the slot
        /// and drops the slot's race-baked copies, which were made from the slot as it was, so the next
        /// character build uses the slot as saved. Without this a race with prebaked blendshapes keeps
        /// wearing the old mesh until the Editor restarts.
        /// </summary>
        private static void ReleaseSlotFromLibrary(SlotDataAsset slot)
        {
            // A copy that only exists in memory has nothing behind it to load again.
            if (slot == null || !EditorUtility.IsPersistent(slot))
            {
                return;
            }
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer != null)
            {
                indexer.ReleaseReference(slot);
            }
        }

        /// <summary>Whether a way of saving can be used now, and if not, why.</summary>
        internal bool CanSave(SaveMethod method, out string reason)
        {
            reason = null;
            switch (method)
            {
                case SaveMethod.MeshModifier:
                    if (modifierEditor == null) { reason = "The Vertex Editor window is not open."; return false; }
                    if (!HasMeshModifierContent()) { reason = "Nothing to save yet."; return false; }
                    return true;

                case SaveMethod.BaseSlot:
                case SaveMethod.NewSlot:
                    if (IsEditingBlendshape) { reason = "A blendshape is loaded: its strokes are saved to the blendshape."; return false; }
                    // Outside Sculpt nothing keeps the sculpt session in step with the Slots panel, and
                    // these saves work on its slot.
                    EnsureSculptSession();
                    if (IsSculptMultiSlotMode) { reason = "Solo one slot in the Slots panel (S): this saves one slot."; return false; }
                    if (activeSculptState == null || sculptSlot == null || !HasSculptChanges(activeSculptState))
                    {
                        reason = sculptSlot != null ? "Nothing has been sculpted on '" + sculptSlot.slotName + "'." : "No slot is editable.";
                        return false;
                    }
                    if (method == SaveMethod.NewSlot && string.IsNullOrWhiteSpace(sculptNewSlotName)) { reason = "Enter a name for the new slot."; return false; }
                    return true;

                case SaveMethod.Blendshape:
                    if (string.IsNullOrWhiteSpace(sculptBlendshapeName)) { reason = "Enter a blendshape name."; return false; }
                    if (!HasSculptChanges()) { reason = "Nothing has been sculpted."; return false; }
                    if (!HasSculptPositionChanges()) { reason = "No vertex has moved. A blendshape holds movement, not UV or normal changes."; return false; }
                    return true;

                case SaveMethod.BlendshapeEdit:
                    if (!IsEditingBlendshape) { reason = "No blendshape is loaded (Edit Blendshape, in Sculpt)."; return false; }
                    if (!HasBlendshapeEditChanges()) { reason = "Nothing has changed since '" + editBlendshapeName + "' was loaded or saved."; return false; }
                    return true;

                case SaveMethod.ColorsToSlot:
                    if (!vertexPaintDirty) { reason = "No vertex colors have been painted."; return false; }
                    if (IsVertexPaintAllSlotsMode || vertexPaintSlot == null) { reason = "Solo the painted slot in the Slots panel (S): this saves one slot."; return false; }
                    if (!vertexPaintDirtySlots.Contains(GetVertexPaintSlotKey())) { reason = "'" + vertexPaintSlot.slotName + "' has no unsaved colors; solo the painted slot."; return false; }
                    return true;

                case SaveMethod.Selection:
                    if (SelectedVertexes == null || SelectedVertexes.Count == 0) { reason = "No vertices are selected."; return false; }
                    return true;
            }
            return false;
        }

        /// <summary>Runs one way of saving. Returns true when something was saved.</summary>
        internal bool RunSave(SaveMethod method)
        {
            if (!CanSave(method, out string reason))
            {
                SetSaveStatus(reason, MessageType.Warning);
                return false;
            }
            EndSculptStroke(true);
            EndVertexPaintStroke(true);
            bool saved;
            switch (method)
            {
                case SaveMethod.MeshModifier: saved = SaveSculptModifier(); break;
                case SaveMethod.BaseSlot: saved = SaveSculptToBaseSlot(); break;
                case SaveMethod.NewSlot: saved = SaveSculptToNewSlot(); break;
                case SaveMethod.Blendshape:
                    saved = SaveSculptAsBlendshape();
                    SetSaveStatus(sculptBlendshapeStatusMessage, sculptBlendshapeStatusType);
                    break;
                case SaveMethod.BlendshapeEdit:
                    saved = SaveBlendshapeEdit() > 0;
                    SetSaveStatus(blendshapeEditStatus, saved ? MessageType.Info : MessageType.Warning);
                    break;
                case SaveMethod.ColorsToSlot: saved = SaveVertexPaintToSlotDataAsset(); break;
                case SaveMethod.Selection:
                    saved = TrySaveSelectionsForClose();
                    if (saved) SetSaveStatus("Saved the vertex selection.", MessageType.Info);
                    break;
                default: saved = false; break;
            }
            unsavedWorkCache = null;
            RepaintLinkedEditors();
            SceneView.RepaintAll();
            return saved;
        }

        private bool SaveSculptModifier()
        {
            if (modifierEditor == null)
            {
                EditorUtility.DisplayDialog("Save as Mesh Modifier", "The Vertex Editor window is not available.", "OK");
                return false;
            }
            string defaultName = !string.IsNullOrWhiteSpace(sculptModifierName) && HasSculptChanges() ? sculptModifierName
                : !string.IsNullOrWhiteSpace(vertexPaintModifierName) && vertexPaintDirty ? vertexPaintModifierName
                : !string.IsNullOrWhiteSpace(sculptModifierName) ? sculptModifierName
                : "MeshModifier";
            return modifierEditor.SaveToAsset(defaultName, "Save as Mesh Modifier");
        }

        // ---- The Save panel (drawn in the Vertex Editor window) -----------------------

        private static GUIStyle saveTitleStyle;

        /// <summary>Starts the help of the saves that need exactly one editable (soloed) slot.</summary>
        private const string SoloSlotsOnly = "Available for solo slots only. ";

        /// <summary>Every way of saving, each with what it writes. Drawn by the Vertex Editor window.</summary>
        internal void DrawSavePanel()
        {
            if (saveTitleStyle == null)
            {
                saveTitleStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
            }
            UnsavedWork work = GetUnsavedWork();
            EditorGUILayout.HelpBox(work.Describe(), work.Any ? MessageType.Warning : MessageType.None);
            if (!string.IsNullOrEmpty(saveStatusMessage))
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(saveStatusMessage, saveStatusType);
                if (saveStatusAsset != null && GUILayout.Button(new GUIContent("Show", "Show the saved asset in the Project window."), GUILayout.Width(44f), GUILayout.Height(38f)))
                {
                    EditorGUIUtility.PingObject(saveStatusAsset);
                }
                GUILayout.EndHorizontal();
            }

            DrawSaveEntry(
                "Save as Mesh Modifier",
                "Writes a MeshModifier asset with everything changed in this session: sculpted positions, changed normals and UVs, painted vertex colors, and the Advanced adjustments and vertex-set modifiers. " +
                "It covers every slot you changed, also slots you have since locked. When the changed slots belong to wardrobe recipes the character wears, there is one Mesh Modifier per recipe " +
                "(named after the file you choose, plus the recipe's name), and you are asked whether to add each to its recipe. Slots no wardrobe recipe has (the body) go in the file you choose.",
                SaveMethod.MeshModifier, "Save as Mesh Modifier...",
                () =>
                {
                    sculptModifierName = EditorGUILayout.TextField(
                        new GUIContent("Name", "The asset's suggested name, and the name of the sculpt modifiers in it."), sculptModifierName);
                    if (vertexPaintDirty)
                    {
                        vertexPaintModifierName = EditorGUILayout.TextField(
                            new GUIContent("Paint Name", "The name of the vertex color modifier in the asset."), vertexPaintModifierName);
                    }
                });

            if (IsEditingBlendshape)
            {
                DrawSaveEntry(
                    "Save to '" + editBlendshapeName + "'",
                    "Adds the strokes made since the blendshape was loaded (or last saved) to it, on every slot they touched, with normal and tangent changes worked out again. " +
                    "A slot that did not have the shape gets it. The slot assets are changed; Undo puts them back.",
                    SaveMethod.BlendshapeEdit, "Save to '" + editBlendshapeName + "'");
            }

            DrawSaveEntry(
                "Save to Base Slot",
                SoloSlotsOnly + "Writes the sculpt (positions, normals and UVs) of the one editable slot into its SlotDataAsset in the project: the base slot behind a race-baked copy, the original behind a slot worn through legacy rig conversion. " +
                "Every character wearing the slot changes. Undo puts it back. The editor stays open; later saves add only what you sculpt after this.",
                SaveMethod.BaseSlot, "Save to Base Slot");

            DrawSaveEntry(
                "Save as New Slot",
                SoloSlotsOnly + "Creates a new SlotDataAsset: a copy of the one editable slot, as the character wears it, with the sculpt. The original slot is not changed. Add the new slot to a recipe to use it.",
                SaveMethod.NewSlot, "Save as New Slot...",
                () => sculptNewSlotName = EditorGUILayout.TextField(new GUIContent("New Slot Name", "The new slot's name and suggested file name."), sculptNewSlotName));

            DrawSaveEntry(
                "Save as Blendshape",
                "Adds the sculpt as a blendshape at 100% on every slot asset it changed, or replaces a blendshape of that name. The rest shape of the slots is not changed. " +
                "A blendshape holds movement only: UV changes are not in it. " +
                "Regenerate the character to dial the blendshape in.",
                SaveMethod.Blendshape, "Save as Blendshape",
                () => sculptBlendshapeName = EditorGUILayout.TextField(new GUIContent("Blendshape Name", "The blendshape to add or replace."), sculptBlendshapeName));

            DrawSaveEntry(
                "Save Vertex Colors to Slot",
                SoloSlotsOnly + "Writes the painted vertex colors of the one editable slot (including Hair Root to Tip) into its SlotDataAsset. Positions and other mesh data are not changed. " +
                "The colors are then taken out of this session's Mesh Modifier, so they are not applied twice.",
                SaveMethod.ColorsToSlot, "Save Vertex Colors to Slot");

            DrawSaveEntry(
                "Save Vertex Selection",
                "Writes the selected vertices (Advanced) to a JSON file, to load again later with Load in the Advanced panel. Nothing in the project changes.",
                SaveMethod.Selection, "Save Vertex Selection...");
        }

        private void DrawSaveEntry(string title, string help, SaveMethod method, string buttonLabel, Action drawFields = null)
        {
            GUILayout.Space(6f);
            GUILayout.Label(title, saveTitleStyle);
            EditorGUILayout.HelpBox(help, MessageType.None);
            drawFields?.Invoke();
            bool can = CanSave(method, out string reason);
            using (new EditorGUI.DisabledScope(!can))
            {
                if (GUILayout.Button(buttonLabel))
                {
                    RunSave(method);
                    GUIUtility.ExitGUI();
                }
            }
            if (!can && !string.IsNullOrEmpty(reason))
            {
                EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>
        /// The left panel's last line: what is not saved, and a button to the Save panel. The saves
        /// themselves are all in the Vertex Editor window.
        /// </summary>
        private void DrawSaveStatusBar()
        {
            GUILayout.Space(2f);
            UnsavedWork work = GetUnsavedWork();
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                new GUIContent(work.Any ? "Unsaved changes" : "Everything is saved", work.Describe()),
                work.Any ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(modifierEditor == null))
            {
                if (GUILayout.Button(new GUIContent("Save...", "Open the Save panel in the Vertex Editor window. Every way of saving is there."), GUILayout.Width(70f)))
                {
                    modifierEditor.ShowSavePanel();
                }
            }
            GUILayout.EndHorizontal();
        }

        // ---- Leaving --------------------------------------------------------------------

        /// <summary>
        /// Asks how to save the work that is not saved yet, until it is all saved or the user chooses to
        /// leave without saving. Returns false when the user chose to stay.
        /// </summary>
        internal bool ResolveUnsavedWorkBeforeLeaving(bool canStay)
        {
            EndSculptStroke(true);
            EndVertexPaintStroke(true);
            if (!ResolveWorkflowUnsavedWork(canStay))
            {
                return false;
            }
            for (int round = 0; round < 12; round++)
            {
                UnsavedWork work = GetUnsavedWork(true);
                if (!work.Any)
                {
                    return true;
                }
                List<VertexEditorLeaveDialog.Option> options = BuildLeaveOptions(work);
                int choice = VertexEditorLeaveDialog.Ask(work.Describe(), options, canStay);
                if (choice == VertexEditorLeaveDialog.Stay)
                {
                    return false;
                }
                if (choice == VertexEditorLeaveDialog.LeaveWithoutSaving)
                {
                    return true;
                }
                // Whatever the save did, ask again about what is left.
                RunSave((SaveMethod)choice);
            }
            return true;
        }

        /// <summary>The ways of saving that fit the unsaved work, for the leave dialog.</summary>
        internal List<VertexEditorLeaveDialog.Option> BuildLeaveOptions(UnsavedWork work)
        {
            var options = new List<VertexEditorLeaveDialog.Option>();
            void Add(SaveMethod method, string label, string help)
            {
                bool can = CanSave(method, out string reason);
                options.Add(new VertexEditorLeaveDialog.Option { id = (int)method, label = label, help = help, enabled = can, reason = reason });
            }
            if (work.blendshapeEdit)
            {
                Add(SaveMethod.BlendshapeEdit, "Save to '" + work.blendshapeName + "'", "Add the strokes to the loaded blendshape on the slot assets.");
            }
            if (work.sculptSlots.Count > 0 || work.paintSlots.Count > 0 || work.advanced)
            {
                Add(SaveMethod.MeshModifier, "Save as Mesh Modifier...", "MeshModifier assets, one per wardrobe recipe whose slots changed, which can be added to those recipes. The slots are not changed.");
            }
            if (work.sculptSlots.Count > 0 && !work.blendshapeEdit)
            {
                Add(SaveMethod.BaseSlot, "Save to Base Slot", "Write the sculpt into the soloed slot's SlotDataAsset.");
                Add(SaveMethod.NewSlot, "Save as New Slot ('" + sculptNewSlotName + "')...", "A copy of the soloed slot with the sculpt.");
                Add(SaveMethod.Blendshape, "Save as Blendshape ('" + sculptBlendshapeName + "')", "The sculpt as a blendshape on the slots it changed.");
            }
            if (work.paintSlots.Count > 0)
            {
                Add(SaveMethod.ColorsToSlot, "Save Vertex Colors to Slot", "Write the painted colors into the soloed slot's SlotDataAsset.");
            }
            if (work.selection)
            {
                Add(SaveMethod.Selection, "Save Vertex Selection...", "The selected vertices, to a JSON file.");
            }
            return options;
        }
    }
}
