using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public sealed class UmaUnusedAssetsWindow : EditorWindow
    {
        [SerializeField] private DefaultAsset folder;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private bool showUmaMaterials = true;
        [SerializeField] private bool showMaterials = true;
        [SerializeField] private bool showShaders = true;
        [SerializeField] private bool unusedOnly = true;
        private readonly HashSet<string> selected = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<UmaUnusedAssetResult> visible = new List<UmaUnusedAssetResult>();
        private UmaUnusedAssetScan scan;
        private IEnumerator<string> work;
        private Vector2 scroll;
        private int revision;
        private int scanRevision;
        private bool stale;
        private bool filterDirty = true;
        private string status = "Choose a folder (or all Assets), then scan.";
        private string detailsGuid;
        private Dictionary<string, string> pendingDeletion;
        private const float RowHeight = 30f;

        [MenuItem("UMA/Asset Management/Find Unused Materials and Shaders", false, 2301)]
        public static void Open()
        {
            var window = GetWindow<UmaUnusedAssetsWindow>("Unused Assets");
            window.minSize = new Vector2(900f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(900f, 520f);
            EditorApplication.update += Tick;
            EditorApplication.projectChanged += Changed;
            EditorApplication.hierarchyChanged += Changed;
            Undo.undoRedoPerformed += Changed;
            ObjectChangeEvents.changesPublished += ObjectsChanged;
        }

        private void OnDisable()
        {
            Cancel();
            EditorApplication.update -= Tick;
            EditorApplication.projectChanged -= Changed;
            EditorApplication.hierarchyChanged -= Changed;
            Undo.undoRedoPerformed -= Changed;
            ObjectChangeEvents.changesPublished -= ObjectsChanged;
        }

        private void ObjectsChanged(ref ObjectChangeEventStream changes)
        {
            if (changes.length > 0) Changed();
        }

        private void Changed()
        {
            revision++;
            if (scan != null) stale = true;
            Repaint();
        }

        private string Scope => folder == null ? "Assets" : AssetDatabase.GetAssetPath(folder);

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Unused Materials & Shaders", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(work != null || EditorApplication.isPlayingOrWillChangePlaymode ||
                    EditorApplication.isCompiling || EditorApplication.isUpdating || !UmaUnusedAssetUtility.IsAssetsFolder(Scope)))
                    if (GUILayout.Button("Scan / Refresh", EditorStyles.toolbarButton, GUILayout.Width(110f))) StartScan(false);
                using (new EditorGUI.DisabledScope(work == null))
                    if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(60f))) Cancel();
            }

            using (new EditorGUI.DisabledScope(work != null))
            {
                EditorGUI.BeginChangeCheck();
                folder = (DefaultAsset)EditorGUILayout.ObjectField(new GUIContent("Search Folder", "Empty means all Assets. References are ALWAYS searched across the entire project and packages."), folder, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck()) { stale = scan != null; selected.Clear(); }
            }
            if (!UmaUnusedAssetUtility.IsAssetsFolder(Scope))
                EditorGUILayout.HelpBox("Select a folder inside Assets, not a file or a package.", MessageType.Error);
            EditorGUILayout.HelpBox(
                "Unused = no detected usage references. UMA index registration does not count as usage; matching index entries are removed on deletion. " +
                "Resources, bundles, Addressables, settings and scene references remain protected. Dependencies are kept until a later scan. " +
                "Computed runtime names/paths and external code cannot be proven safe; review before deleting.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                showUmaMaterials = GUILayout.Toggle(showUmaMaterials, "UMAMaterials", EditorStyles.toolbarButton, GUILayout.Width(110f));
                showMaterials = GUILayout.Toggle(showMaterials, "Materials", EditorStyles.toolbarButton, GUILayout.Width(85f));
                showShaders = GUILayout.Toggle(showShaders, "Shaders", EditorStyles.toolbarButton, GUILayout.Width(80f));
                unusedOnly = GUILayout.Toggle(unusedOnly, "Unused only", EditorStyles.toolbarButton, GUILayout.Width(100f));
                GUILayout.Space(12f);
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
            }
            if (EditorGUI.EndChangeCheck()) filterDirty = true;
            UpdateVisibleRows();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!Ready))
                {
                    if (GUILayout.Button("Check visible unused", GUILayout.Width(150f)))
                        foreach (var item in visible) if (item.IsUnused) selected.Add(item.Guid);
                    if (GUILayout.Button("Clear checks", GUILayout.Width(100f))) selected.Clear();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{visible.Count} shown  •  {selected.Count} checked", EditorStyles.miniLabel);
            }
            if (stale && scan != null && work == null)
                EditorGUILayout.HelpBox("Project or scene state changed. Scan again before deleting.", MessageType.Warning);
            if (work != null)
            {
                Rect progress = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.ProgressBar(progress, scan.Total == 0 ? 0f : scan.Processed / (float)scan.Total,
                    pendingDeletion == null ? "Scanning references…" : "Rechecking selected assets before deletion…");
            }
            EditorGUILayout.LabelField(new GUIContent(status, status), EditorStyles.miniLabel);
            DrawGrid();
            DrawDetails();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Deletion uses the system trash; it is not an Undo operation.", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!Ready || selected.Count == 0))
                    if (GUILayout.Button($"Delete checked… ({selected.Count})", EditorStyles.toolbarButton, GUILayout.Width(170f))) StartScan(true);
            }
        }

        private bool Ready => work == null && scan != null && scan.Complete && !stale &&
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        private void UpdateVisibleRows()
        {
            if (!filterDirty) return;
            filterDirty = false;
            visible.Clear();
            if (scan == null || !scan.Complete) return;
            foreach (var result in scan.Results)
            {
                if ((result.Kind == UmaUnusedAssetKind.UMAMaterial && !showUmaMaterials) ||
                    (result.Kind == UmaUnusedAssetKind.Material && !showMaterials) ||
                    (result.Kind == UmaUnusedAssetKind.Shader && !showShaders) || (unusedOnly && !result.IsUnused)) continue;
                if (!string.IsNullOrEmpty(search) && result.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    result.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                visible.Add(result);
            }
        }

        private void DrawGrid()
        {
            Rect header = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, EditorGUIUtility.isProSkin ? new Color(.18f, .18f, .18f) : new Color(.75f, .75f, .75f));
            GUI.Label(new Rect(header.x + 30f, header.y, 205f, 22f), "Asset", EditorStyles.boldLabel);
            GUI.Label(new Rect(header.x + 235f, header.y, 100f, 22f), "Type", EditorStyles.boldLabel);
            GUI.Label(new Rect(header.x + 335f, header.y, header.width - 520f, 22f), "Project Path", EditorStyles.boldLabel);
            GUI.Label(new Rect(header.xMax - 180f, header.y, 120f, 22f), "Status", EditorStyles.boldLabel);

            Rect viewport = GUILayoutUtility.GetRect(0f, 120f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float width = Mathf.Max(860f, viewport.width - 16f);
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0f, 0f, width, visible.Count * RowHeight));
            int first = Mathf.Max(0, (int)(scroll.y / RowHeight));
            int last = Mathf.Min(visible.Count, first + Mathf.CeilToInt(viewport.height / RowHeight) + 1);
            for (int i = first; i < last; i++)
            {
                var item = visible[i];
                Rect row = new Rect(0f, i * RowHeight, width, RowHeight);
                if ((i & 1) == 0) EditorGUI.DrawRect(row, new Color(.5f, .5f, .5f, .08f));
                using (new EditorGUI.DisabledScope(!Ready || !item.IsUnused))
                {
                    bool check = GUI.Toggle(new Rect(6f, row.y + 6f, 20f, 20f), selected.Contains(item.Guid), GUIContent.none);
                    if (check) selected.Add(item.Guid); else selected.Remove(item.Guid);
                }
                if (GUI.Button(new Rect(30f, row.y + 3f, 200f, 24f),
                    new GUIContent(item.Name, AssetDatabase.GetCachedIcon(item.Path), item.Path), EditorStyles.label))
                    detailsGuid = item.Guid;
                GUI.Label(new Rect(235f, row.y + 5f, 95f, 22f), item.Kind.ToString());
                GUI.Label(new Rect(335f, row.y + 5f, width - 525f, 22f), new GUIContent(item.Path, item.Path), EditorStyles.miniLabel);
                string reason = !string.IsNullOrEmpty(item.Protection) ? "Protected" : item.References.Count > 0 ? $"Used ({item.References.Count})" :
                    item.RegisteredIn.Count > 0 ? "Unused (indexed)" : "Unused";
                string tooltip = item.IsUnused && item.RegisteredIn.Count > 0 ? "UMA registration only is not usage. Matching index entries will be removed when this asset is deleted." : item.Protection;
                if (GUI.Button(new Rect(width - 180f, row.y + 4f, 100f, 22f), new GUIContent(reason, tooltip), EditorStyles.miniButton))
                    detailsGuid = item.Guid;
                if (GUI.Button(new Rect(width - 74f, row.y + 4f, 68f, 22f), "Inspect", EditorStyles.miniButton))
                {
                    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(item.Path);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                }
            }
            GUI.EndScrollView();
        }

        private void DrawDetails()
        {
            var item = scan?.Results.Find(result => result.Guid == detailsGuid);
            if (item == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(item.Path, EditorStyles.boldLabel);
                string explanation = item.IsUnused ? "No detected incoming references. Runtime-computed references still require your review." :
                    !string.IsNullOrEmpty(item.Protection) ? item.Protection : "Referenced by:";
                EditorGUILayout.LabelField(explanation, EditorStyles.wordWrappedMiniLabel);
                for (int i = 0; i < Mathf.Min(4, item.References.Count); i++)
                    EditorGUILayout.LabelField(item.References[i], EditorStyles.miniLabel);
                if (item.References.Count > 4) EditorGUILayout.LabelField($"…and {item.References.Count - 4} more references.", EditorStyles.miniLabel);
                if (item.RegisteredIn.Count > 0)
                {
                    EditorGUILayout.LabelField("UMA index entries (not usage; removed with deletion):", EditorStyles.wordWrappedMiniLabel);
                    foreach (string path in item.RegisteredIn.Take(4)) EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                    if (item.RegisteredIn.Count > 4) EditorGUILayout.LabelField($"…and {item.RegisteredIn.Count - 4} more indexes.", EditorStyles.miniLabel);
                }
            }
        }

        private void StartScan(bool forDeletion)
        {
            pendingDeletion = forDeletion ? scan.Results.Where(item => selected.Contains(item.Guid))
                .ToDictionary(item => item.Guid, item => item.Path) : null;
            if (!forDeletion) selected.Clear();
            scan = new UmaUnusedAssetScan(Scope);
            work = scan.Run().GetEnumerator();
            scanRevision = revision;
            stale = false;
            filterDirty = true;
            scroll = Vector2.zero;
            status = "Gathering candidates…";
        }

        private void Cancel()
        {
            if (work == null) return;
            work.Dispose();
            work = null;
            pendingDeletion = null;
            stale = true;
            status = "Scan canceled. Incomplete results cannot be deleted.";
            Repaint();
        }

        private void Tick()
        {
            if (work == null) return;
            if (revision != scanRevision || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Cancel();
                status = "Project/scene changed during scanning. Please scan again when editing/importing has finished.";
                return;
            }
            try
            {
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 12)
                {
                    if (!work.MoveNext())
                    {
                        work.Dispose();
                        work = null;
                        filterDirty = true;
                        int unused = scan.Results.Count(item => item.IsUnused);
                        status = $"{unused} unused candidates; {scan.Results.Count - unused} used/protected. Checked {scan.Total} project/package/settings files.";
                        if (pendingDeletion != null) ConfirmDeletion();
                        break;
                    }
                    status = work.Current;
                }
            }
            catch (Exception exception)
            {
                Cancel();
                stale = true;
                status = "Scan/deletion stopped: " + exception.Message + ". No further assets will be removed.";
                UnityEngine.Debug.LogException(exception);
            }
            Repaint();
        }

        private void ConfirmDeletion()
        {
            var expected = pendingDeletion;
            pendingDeletion = null;
            if (!Ready || revision != scanRevision) return;
            var targets = scan.Results.Where(item => expected.ContainsKey(item.Guid)).ToList();
            var validationErrors = new List<string>();
            if (targets.Count != expected.Count) validationErrors.Add("A checked asset is moved or missing.");
            foreach (var item in targets)
                if (expected[item.Guid] != item.Path) validationErrors.Add(item.Path + ": asset moved.");
                else if (!UmaUnusedAssetUtility.CanTrash(item, out string reason)) validationErrors.Add(item.Path + ": " + reason);
            if (validationErrors.Count > 0)
            {
                selected.Clear();
                EditorUtility.DisplayDialog("Cannot delete — nothing changed", string.Join("\n\n", validationErrors.Take(8)) +
                    "\n\nReview the refreshed results and check the assets again.", "OK");
                return;
            }
            string paths = string.Join("\n", targets.Take(12).Select(item => item.Path));
            if (targets.Count > 12) paths += $"\n…and {targets.Count - 12} more checked assets.";
            if (!EditorUtility.DisplayDialog("Delete checked assets?",
                $"Move these {targets.Count} checked asset files and their metadata to the system trash?\n\n{paths}\n\n" +
                $"Matching UMA index entries will also be removed for {targets.Count(item => item.RegisteredIn.Count > 0)} registered assets. " +
                "No usage references were detected in the fresh scan. You must still review computed runtime names/paths and external code. " +
                "This cannot be undone with Ctrl+Z; recovery depends on the system trash or source control.", "Move to Trash", "Cancel")) return;
            if (!Ready || revision != scanRevision)
            {
                EditorUtility.DisplayDialog("Project changed — nothing deleted",
                    "The project or scene changed while confirming. Scan again before deleting.", "OK");
                return;
            }
            var failures = new List<string>();
            var notices = new List<string>();
            int moved = 0;
            foreach (var item in targets)
            {
                try
                {
                    if (UmaUnusedAssetUtility.TrashAndUnregister(item, out string reason))
                    {
                        moved++;
                        if (!string.IsNullOrEmpty(reason)) notices.Add(item.Path + ": " + reason);
                    }
                    else failures.Add(item.Path + ": " + reason);
                }
                catch (Exception exception) { failures.Add(item.Path + ": " + exception.Message); }
            }
            selected.Clear();
            stale = true;
            status = $"Moved {moved} files to trash and removed their matching UMA index entries; {failures.Count} kept/failed. Scan again to find newly unreferenced dependencies.";
            EditorUtility.DisplayDialog("Unused asset cleanup", status +
                (failures.Count == 0 ? "" : "\n\n" + string.Join("\n", failures.Take(12))) +
                (notices.Count == 0 ? "" : "\n\n" + string.Join("\n", notices.Take(12))), "OK");
        }
    }
}
