using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMANormalViewer))]
    public sealed class UMANormalViewerEditor : Editor
    {
        private readonly HashSet<SlotDataAsset> selected = new HashSet<SlotDataAsset>();
        private UMANormalRepair repair;
        private UMANormalRepair sourceSnapshot;
        private DynamicCharacterAvatar scannedAvatar;
        private VisualElement panel;
        private float tolerance = .0001f, angle = 1f;
        private bool previewing, initializedSelection;
        private string message;

        private DynamicCharacterAvatar Avatar
        {
            get
            {
                var viewer = (UMANormalViewer)target;
                if (viewer == null) return null;
                if (viewer.avatar != null) return viewer.avatar;
                return viewer.GetComponentInParent<DynamicCharacterAvatar>() ?? viewer.GetComponentInChildren<DynamicCharacterAvatar>(true);
            }
        }

        private void OnEnable()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed += OnUndo;
        }

        private void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= Cancel;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= OnUndo;
            Cancel();
        }

        private void OnPlayModeChanged(PlayModeStateChange state) { Cancel(); }
        private void OnUndo() { UMANormalRepairSources.RefreshBakedCopies(); Cancel(); if (Avatar != null && !Application.isPlaying) Rebuild(Avatar); DrawPanel(); }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            root.TrackPropertyValue(serializedObject.FindProperty("avatar"), _ =>
            { Cancel(); selected.Clear(); initializedSelection = false; message = null; DrawPanel(); });
            var foldout = new Foldout { text = "Fix Normals", value = true };
            panel = new VisualElement(); foldout.Add(panel); root.Add(foldout);
            DrawPanel();
            return root;
        }

        private void DrawPanel()
        {
            if (panel == null) return;
            panel.Clear();
            var avatar = Avatar;
            if (Application.isPlaying || avatar == null || avatar.umaData?.umaRecipe?.slotDataList == null)
            {
                panel.Add(new HelpBox("Select a built DynamicCharacterAvatar in Edit mode to scan its equipped slots.", HelpBoxMessageType.Info));
                return;
            }
            panel.Add(new HelpBox("Scan shared vertex positions in slot bind-pose space. Perform fix queues a group's settings; View previews all queued fixes. Apply saves the shared slot assets. Vertex positions are never moved.", HelpBoxMessageType.Info));
            if (!string.IsNullOrEmpty(message)) panel.Add(new HelpBox(message, HelpBoxMessageType.Info));
            var slots = avatar.umaData.umaRecipe.slotDataList.Where(s => s?.asset != null).Select(s => s.asset).Distinct().ToArray();
            if (!initializedSelection)
            { foreach (var a in slots.Where(UMANormalRepair.CanScan)) selected.Add(a); initializedSelection = true; }
            var list = new ScrollView { style = { maxHeight = 220 } };
            foreach (var a in slots)
            {
                bool usable = UMANormalRepair.CanScan(a);
                var sourceAsset = UMANormalRepairSources.Resolve(a);
                string suffix = !usable ? " (no usable mesh/normals)" : sourceAsset != null && sourceAsset != a ? " (shared base source)" : "";
                var toggle = new Toggle(a.slotName + suffix) { value = selected.Contains(a), tooltip = sourceAsset != null ? "Saves to " + AssetDatabase.GetAssetPath(sourceAsset) : "Preview only: no persistent source asset" };
                toggle.SetEnabled(usable && repair == null);
                toggle.RegisterValueChangedCallback(e => { if (e.newValue) selected.Add(a); else selected.Remove(a); });
                list.Add(toggle);
            }
            panel.Add(list);
            if (slots.Any(a => a.isBaked && !EditorUtility.IsPersistent(a)))
                panel.Add(new HelpBox("Race-baked slot fixes are saved to their shared base slots and affect other races using those assets. View still changes only this character's temporary preview.", HelpBoxMessageType.Info));
            AddButton(panel, "Refresh slots", () =>
            { Cancel(); selected.Clear(); initializedSelection = false; message = null; DrawPanel(); }, repair == null);
            var distance = new FloatField("Position tolerance") { value = tolerance, tooltip = "Maximum separation in slot mesh units (default 0.0001). Slots must use the same bind-pose coordinate system." };
            distance.SetEnabled(repair == null); distance.RegisterValueChangedCallback(e => tolerance = e.newValue); panel.Add(distance);
            var degrees = new Slider("Mismatch angle (degrees)", 0, 45) { value = angle, showInputField = true };
            degrees.SetEnabled(repair == null); degrees.RegisterValueChangedCallback(e => angle = e.newValue); panel.Add(degrees);
            AddButton(panel, "Scan", () => Scan(avatar, slots), repair == null);
            if (repair != null)
            {
                foreach (var group in repair.Groups) DrawGroup(group);
                panel.Add(new HelpBox(previewing ? "Viewing temporary repairs. Source slot assets are unchanged." :
                    "View, Apply and Cancel operate on all queued groups. Leaving this inspector cancels an unsaved preview.", HelpBoxMessageType.Info));
                AddButton(panel, "View", Preview, repair.HasPending);
                AddButton(panel, "Apply all changes", Apply, repair.HasPending);
                AddButton(panel, "Cancel", () =>
                {
                    var prior = scannedAvatar; bool wasPreviewing = previewing;
                    Cancel(); if (!wasPreviewing) Rebuild(prior);
                    message = "Pending repairs discarded and character rebuilt."; DrawPanel();
                });
            }
        }

        private void DrawGroup(UMANormalRepair.Group group)
        {
            var box = new Box(); box.style.marginTop = 6;
            string a = repair.Sources[group.First].Asset.slotName, b = repair.Sources[group.Second].Asset.slotName;
            var title = new Label(a + " / " + b + " — " + group.Seams.Count + " mismatched shared position(s)");
            title.style.whiteSpace = WhiteSpace.Normal;
            box.Add(title);
            var mode = new DropdownField("Fix", new List<string> { "Average vertexes", "Use Normals from" }, (int)group.Mode);
            var source = new DropdownField("Source slot", new List<string> { a, b }, group.Source == group.First ? 0 : 1);
            source.SetEnabled(group.Mode == UMANormalRepair.FixMode.Copy);
            var status = new Label(group.Pending ? "Queued" : "Not queued");
            void Changed() { group.Pending = false; status.text = "Settings changed — click Perform fix"; }
            mode.RegisterValueChangedCallback(e => { group.Mode = (UMANormalRepair.FixMode)mode.index; source.SetEnabled(mode.index == 1); Changed(); });
            source.RegisterValueChangedCallback(e => { group.Source = source.index == 0 ? group.First : group.Second; Changed(); });
            box.Add(mode); box.Add(source);
            var smooth = new Toggle("Smooth normals") { value = group.Smooth, tooltip = "Blend the normals in neighbouring triangle rings on both slots, while keeping shared seam normals identical." };
            var rings = new SliderInt("Neighbour rings", 1, 8) { value = group.Rings, showInputField = true };
            var strength = new Slider("Smoothing strength", 0, 1) { value = group.Strength, showInputField = true };
            rings.SetEnabled(group.Smooth); strength.SetEnabled(group.Smooth);
            smooth.RegisterValueChangedCallback(e => { group.Smooth = e.newValue; rings.SetEnabled(e.newValue); strength.SetEnabled(e.newValue); Changed(); });
            rings.RegisterValueChangedCallback(e => { group.Rings = e.newValue; Changed(); });
            strength.RegisterValueChangedCallback(e => { group.Strength = e.newValue; Changed(); });
            box.Add(smooth); box.Add(rings); box.Add(strength); box.Add(status);
            AddButton(box, "Perform fix", () =>
            {
                group.Pending = true;
                try { repair.Calculate(); message = "Fix queued. View to preview, or Apply all changes to save."; }
                catch { group.Pending = false; throw; }
                DrawPanel();
            });
            panel.Add(box);
        }

        private void AddButton(VisualElement parent, string label, Action action, bool enabled = true)
        {
            var button = new Button(() =>
            {
                try { action(); }
                catch (OperationCanceledException) { message = "Scan canceled. No assets changed."; DrawPanel(); }
                catch (Exception e) { message = e.Message; Debug.LogException(e); DrawPanel(); }
            }) { text = label };
            button.SetEnabled(enabled); parent.Add(button);
        }

        private void Scan(DynamicCharacterAvatar avatar, SlotDataAsset[] slots)
        {
            var scanSlots = slots.Where(selected.Contains).ToArray();
            if (scanSlots.Length < 2) throw new InvalidOperationException("Select at least two slots.");
            try
            {
                repair = UMANormalRepair.Scan(scanSlots, tolerance, angle, progress =>
                    EditorUtility.DisplayCancelableProgressBar("Fix Normals", "Scanning shared slot vertices…", progress));
                scannedAvatar = avatar;
                sourceSnapshot = UMANormalRepair.Scan(scanSlots.Select(UMANormalRepairSources.Resolve).Where(a => a != null).Distinct(), tolerance, angle);
                message = repair.Groups.Count == 0 ? "No mismatched normals found within the selected tolerances." : repair.Groups.Count + " slot pair(s) have mismatched normals.";
            }
            finally { EditorUtility.ClearProgressBar(); }
            DrawPanel();
        }

        private void CheckAvatar()
        {
            if (scannedAvatar == null || Avatar != scannedAvatar || scannedAvatar.umaData?.umaRecipe?.slotDataList == null)
                throw new InvalidOperationException("The avatar changed. Cancel and scan again.");
            var current = scannedAvatar.umaData.umaRecipe.slotDataList;
            if (repair.Sources.Any(s => !current.Any(slot => slot?.asset == s.Asset)))
                throw new InvalidOperationException("Equipped slots changed. Cancel and scan again.");
        }

        public static void Rebuild(DynamicCharacterAvatar avatar)
        {
            if (avatar == null || avatar.umaData == null) return;
            var generator = avatar.umaGenerator;
            if (generator == null || avatar.umaData.umaRecipe?.raceData == null)
                throw new InvalidOperationException("The avatar needs a generator and a built race before normals can be previewed.");
            // Use the actual built recipe: activeRace.racedata can be null after an editor reload.
            // BuildNow reloads wardrobe; RegenerateNow silently skips when that cached race is null.
            bool freeze = generator.FreezeTime;
            int scale = generator.InitialScaleFactor, resolution = generator.atlasResolution;
            try
            {
                generator.FreezeTime = true;
                generator.InitialScaleFactor = generator.editorInitialScaleFactor;
                generator.atlasResolution = generator.editorAtlasResolution;
                avatar.Dirty(false, false, true);
                if (!generator.GenerateSingleUMA(avatar, false, false))
                    throw new InvalidOperationException("The generator could not rebuild the current recipe.");
            }
            finally
            {
                generator.FreezeTime = freeze; generator.InitialScaleFactor = scale;
                generator.atlasResolution = resolution; generator.Clear();
            }
            SceneView.RepaintAll();
        }

        public static void WithTemporarySlots(SlotData[] slots, Dictionary<SlotDataAsset, Vector3[]> output, Action rebuild)
        {
            var copies = new Dictionary<SlotDataAsset, SlotDataAsset>();
            var originals = new Dictionary<SlotData, SlotDataAsset>();
            try
            {
                foreach (var pair in output)
                {
                    var clone = Object.Instantiate(pair.Key);
                    copies.Add(pair.Key, clone);
                    clone.name = pair.Key.name;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    UMANormalRepair.SetNormals(clone, pair.Value);
                }
                foreach (var slot in slots)
                    if (slot?.asset != null && copies.TryGetValue(slot.asset, out var clone))
                    { originals[slot] = slot.asset; slot.asset = clone; }
                rebuild();
            }
            finally
            {
                foreach (var original in originals) original.Key.asset = original.Value;
                foreach (var clone in copies.Values) if (clone != null) Object.DestroyImmediate(clone);
            }
        }

        private void Preview()
        {
            CheckAvatar();
            var output = repair.Calculate();
            if (output.Count == 0) throw new InvalidOperationException("Click Perform fix for a group first.");
            sourceSnapshot?.ValidateUnchanged();
            previewing = true; // A failed rebuild must also be restored by Cancel.
            WithTemporarySlots(scannedAvatar.umaData.umaRecipe.slotDataList, output, () => Rebuild(scannedAvatar));
            message = "Preview rebuilt. No slot assets have been saved."; DrawPanel();
        }

        private void Apply()
        {
            CheckAvatar();
            var output = repair.Calculate();
            if (output.Count == 0) throw new InvalidOperationException("Click Perform fix for a group first.");
            sourceSnapshot?.ValidateUnchanged();
            output = UMANormalRepairSources.ToSourceNormals(output, scannedAvatar.umaData.umaRecipe.raceData);
            foreach (var asset in output.Keys)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) || !AssetDatabase.IsOpenForEdit(asset) ||
                    (System.IO.File.GetAttributes(path) & System.IO.FileAttributes.ReadOnly) != 0)
                    throw new InvalidOperationException("Cannot save slot: " + asset.name + " (" + path + "). Make this asset writable first.");
            }
            UMANormalRepairSources.Track(output.Keys);
            SaveRepairs(output);
            UMANormalRepairSources.RefreshBakedCopies();
            int count = output.Count;
            repair = null; sourceSnapshot = null; previewing = false;
            Rebuild(scannedAvatar);
            message = "Saved repaired normals to " + count + " slot asset(s) and rebuilt the character.";
            scannedAvatar = null; DrawPanel();
        }

        public static void SaveRepairs(Dictionary<SlotDataAsset, Vector3[]> output)
        {
            var backups = output.Keys.ToDictionary(a => a, a =>
                (normals: a.meshData.normals, tangents: a.meshData.tangents,
                 normalFlag: a.meshData.normalsModified, tangentFlag: a.meshData.tangentsModified, dirty: EditorUtility.IsDirty(a)));
            Undo.RegisterCompleteObjectUndo(output.Keys.Cast<Object>().ToArray(), "Fix slot seam normals");
            var attempted = new List<SlotDataAsset>();
            try
            {
                foreach (var pair in output)
                { UMANormalRepair.SetNormals(pair.Key, pair.Value); EditorUtility.SetDirty(pair.Key); }
                // Save only modified slots, never unrelated dirty scenes or assets.
                foreach (var asset in output.Keys)
                { attempted.Add(asset); AssetDatabase.SaveAssetIfDirty(asset); }
            }
            catch (Exception error)
            {
                foreach (var pair in backups)
                {
                    var m = pair.Key.meshData; var b = pair.Value;
                    m.normals = b.normals; m.tangents = b.tangents;
                    m.normalsModified = b.normalFlag; m.tangentsModified = b.tangentFlag; m.InvalidateBuildData();
                    EditorUtility.SetDirty(pair.Key);
                    if (attempted.Contains(pair.Key))
                        try { AssetDatabase.SaveAssetIfDirty(pair.Key); }
                        catch (Exception rollback) { Debug.LogError("Could not restore slot file " + AssetDatabase.GetAssetPath(pair.Key) + ": " + rollback.Message); }
                    if (b.dirty) EditorUtility.SetDirty(pair.Key);
                    else if (!attempted.Contains(pair.Key)) EditorUtility.ClearDirty(pair.Key);
                }
                throw new InvalidOperationException("Could not save slot " +
                    (attempted.Count > 0 ? AssetDatabase.GetAssetPath(attempted.Last()) : "data") + ": " + error.Message, error);
            }
        }

        private void Cancel()
        {
            var avatar = scannedAvatar;
            bool restore = previewing;
            repair = null; sourceSnapshot = null; scannedAvatar = null; previewing = false;
            // Originals were never modified by View, so cancel needs no asset writes.
            if (restore && avatar != null)
                try { Rebuild(avatar); }
                catch (Exception e) { Debug.LogException(e); }
        }
    }
}
