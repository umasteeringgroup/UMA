using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Batch tool for a set of textures: lists them, shows how overlays use each one (as which
    /// UMA Material keyword), filters by that keyword, and changes import settings, checks them
    /// against the UMA Material, lowers sizes and converts to PNG.
    /// </summary>
    public sealed class UMATextureToolsWindow : EditorWindow
    {
        public const string Title = "UMA Texture Tools";
        public const string UnusedKeyword = "(not used by any overlay)";

        private enum Tab { ImportSettings, MaterialCheck, Resize, ConvertToPng, More }
        private enum SortOrder { Name, Path, SourceSize, FileSize, Issues }

        private static readonly string[] TabNames = { "Import Settings", "UMA Material Check", "Resize", "Convert to PNG", "More" };
        private static readonly int[] Sizes = { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384 };
        private static readonly string[] SizeNames = Sizes.Select(s => s.ToString()).ToArray();
        private static readonly string[] PlatformNames = { "Default", "Standalone", "Android", "iPhone", "WebGL", "tvOS" };
        private const float RowHeight = 44f;
        private const float ToolsHeight = 310f;

        [SerializeField] private List<string> textureGuids = new List<string>();
        [SerializeField] private List<string> uncheckedGuids = new List<string>();
        [SerializeField] private List<string> hiddenKeywords = new List<string>();
        [SerializeField] private string focusedGuid;
        [SerializeField] private string search = string.Empty;
        [SerializeField] private Tab tab;
        [SerializeField] private SortOrder sortOrder;
        [SerializeField] private UMATextureImportEdit importEdit = new UMATextureImportEdit();
        [SerializeField] private int resizeMaximum = 1024;
        [SerializeField] private UMATexturePngConverter.Mode pngMode = UMATexturePngConverter.Mode.ReplaceInPlace;
        [SerializeField] private bool pngRelink = true;
        [SerializeField] private bool fixSuggestions;
        [SerializeField] private bool showAlpha;
        [SerializeField] private bool filterExpanded = true;

        private sealed class Entry
        {
            public string Guid, Path, Name, Summary;
            public TextureImporter Importer;
            public int SourceWidth, SourceHeight;
            public long FileBytes;
            public List<UMATextureUsage> Usages = new List<UMATextureUsage>();
            public List<UMATextureSettingIssue> Issues = new List<UMATextureSettingIssue>();
            public List<string> Keywords = new List<string>();
            public UMATextureSettingIssue.Severity? WorstIssue;
        }

        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private Dictionary<string, List<UMATextureUsage>> usages = new Dictionary<string, List<UMATextureUsage>>(StringComparer.Ordinal);
        private readonly List<string> visible = new List<string>();
        private readonly List<string> log = new List<string>();
        private bool usageScanned, visibleDirty = true;
        private Vector2 listScroll, infoScroll, toolScroll, logScroll, filterScroll;
        private float listViewHeight = 400f;
        private Texture2D focusedTexture;
        private string focusedTextureGuid;
        private List<UmaResizeTexturesUtility.Entry> resizePreview;
        private string resizePreviewKey;

        [MenuItem("UMA/Textures/Texture Tools", priority = 126)]
        public static void ShowWindow() => Open(Selection.GetFiltered<Texture2D>(SelectionMode.Assets));

        /// <summary>Opens the window with <paramref name="textures"/>; an empty list keeps the current contents.</summary>
        public static UMATextureToolsWindow Open(IEnumerable<Texture2D> textures)
        {
            var window = GetWindow<UMATextureToolsWindow>(false, Title, true);
            window.minSize = new Vector2(980f, 680f);
            List<string> guids = ToGuids(textures);
            if (guids.Count > 0) window.SetTextures(guids, true);
            window.Show();
            window.Focus();
            return window;
        }

        private static List<string> ToGuids(IEnumerable<Texture2D> textures) =>
            (textures ?? Enumerable.Empty<Texture2D>()).Where(t => t != null)
            .Select(t => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(t)))
            .Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();

        #region Public state (also used by tests)

        public IReadOnlyList<string> TextureGuids => textureGuids;

        /// <summary>Textures that pass the keyword and name filters, in display order.</summary>
        public IReadOnlyList<string> VisibleGuids { get { UpdateVisible(); return visible; } }

        /// <summary>Textures every operation applies to: visible and ticked.</summary>
        public List<string> TargetGuids => VisibleGuids.Where(IsChecked).ToList();

        /// <summary>Every keyword in the list, with how many textures use it.</summary>
        public List<KeyValuePair<string, int>> KeywordCounts()
        {
            EnsureUsage();
            return entries.Values.SelectMany(e => e.Keywords).GroupBy(k => k)
                .OrderBy(g => g.Key == UnusedKeyword ? 1 : 0).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToList();
        }

        public IReadOnlyList<string> KeywordsOf(string guid)
        {
            EnsureUsage();
            return entries.TryGetValue(guid, out Entry entry) ? entry.Keywords : (IReadOnlyList<string>)Array.Empty<string>();
        }

        public IReadOnlyList<UMATextureUsage> UsagesOf(string guid)
        {
            EnsureUsage();
            return entries.TryGetValue(guid, out Entry entry) ? entry.Usages : (IReadOnlyList<UMATextureUsage>)Array.Empty<UMATextureUsage>();
        }

        public void SetKeywordShown(string keyword, bool shown)
        {
            if (shown) hiddenKeywords.Remove(keyword);
            else if (!hiddenKeywords.Contains(keyword)) hiddenKeywords.Add(keyword);
            visibleDirty = true;
        }

        /// <summary>Shows only the given keywords.</summary>
        public void ShowOnlyKeywords(params string[] keywords)
        {
            hiddenKeywords = KeywordCounts().Select(k => k.Key).Where(k => !keywords.Contains(k)).ToList();
            visibleDirty = true;
        }

        public void SetSearch(string text) { search = text ?? string.Empty; visibleDirty = true; }

        public bool IsChecked(string guid) => !uncheckedGuids.Contains(guid);

        public void SetChecked(string guid, bool value)
        {
            if (value) uncheckedGuids.Remove(guid);
            else if (!uncheckedGuids.Contains(guid)) uncheckedGuids.Add(guid);
        }

        public void SetTextures(IEnumerable<string> guids, bool replace)
        {
            if (replace) { textureGuids.Clear(); uncheckedGuids.Clear(); focusedGuid = null; }
            foreach (string guid in guids)
                if (!string.IsNullOrEmpty(guid) && !textureGuids.Contains(guid)) textureGuids.Add(guid);
            if (focusedGuid == null && textureGuids.Count > 0) focusedGuid = textureGuids[0];
            usageScanned = false;
            RebuildEntries();
        }

        /// <summary>Rescans every overlay in the project for the listed textures.</summary>
        public void RescanUsage(bool showProgress = true)
        {
            try
            {
                usages = UMATextureUsageScanner.Scan(textureGuids, showProgress
                    ? (p, path) => EditorUtility.DisplayCancelableProgressBar("Texture Tools", "Scanning overlays: " + path, p)
                    : (Func<float, string, bool>)null);
            }
            finally { if (showProgress) EditorUtility.ClearProgressBar(); }
            usageScanned = true;
            RebuildEntries();
        }

        #endregion

        #region Data

        private void OnEnable()
        {
            titleContent = new GUIContent(Title, EditorGUIUtility.IconContent("Texture Icon").image);
            importEdit ??= new UMATextureImportEdit();
            usageScanned = false;
            RebuildEntries();
        }

        private void OnDisable() => focusedTexture = null;

        private void OnProjectChange()
        {
            // Textures may have been deleted, moved or reimported elsewhere.
            textureGuids.RemoveAll(g => string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(g)));
            RebuildEntries();
            Repaint();
        }

        private void EnsureUsage()
        {
            if (!usageScanned) RescanUsage(textureGuids.Count > 50);
        }

        private void RebuildEntries()
        {
            entries.Clear();
            foreach (string guid in textureGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                var entry = new Entry { Guid = guid, Path = path, Name = Path.GetFileName(path) };
                entry.Importer = AssetImporter.GetAtPath(path) as TextureImporter;
                entry.FileBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (entry.Importer != null) entry.Importer.GetSourceTextureWidthAndHeight(out entry.SourceWidth, out entry.SourceHeight);
                if (usageScanned && usages.TryGetValue(guid, out List<UMATextureUsage> list)) entry.Usages = list;
                entry.Keywords = entry.Usages.Count == 0
                    ? new List<string> { UnusedKeyword }
                    : entry.Usages.Select(u => u.Keyword).Distinct().ToList();
                entry.Issues = UMATextureSettingsChecker.Check(entry.Importer, entry.Usages);
                entry.WorstIssue = entry.Issues.Count == 0 ? (UMATextureSettingIssue.Severity?)null : entry.Issues.Max(i => i.Level);
                entry.Summary = Summarize(entry);
                entries[guid] = entry;
            }
            visibleDirty = true;
            resizePreview = null;
        }

        private static string Summarize(Entry entry)
        {
            TextureImporter importer = entry.Importer;
            if (importer == null) return "Not imported with the Texture importer";
            TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
            string color = importer.textureType == TextureImporterType.NormalMap ? "Normal map"
                : importer.textureType + (importer.sRGBTexture ? " · sRGB" : " · Linear");
            string alpha = importer.alphaSource == TextureImporterAlphaSource.None ? " · no alpha" : string.Empty;
            return $"{entry.SourceWidth}×{entry.SourceHeight} · {color}{alpha} · max {platform.maxTextureSize} · {ShortCompression(platform)} · {FormatBytes(entry.FileBytes)}";
        }

        private static string ShortCompression(TextureImporterPlatformSettings platform)
        {
            string text = platform.textureCompression switch
            {
                TextureImporterCompression.Uncompressed => "Uncompressed",
                TextureImporterCompression.CompressedHQ => "HQ",
                TextureImporterCompression.CompressedLQ => "LQ",
                _ => "Normal Quality"
            };
            return platform.crunchedCompression ? text + " crunched" : text;
        }

        private void UpdateVisible()
        {
            if (!visibleDirty) return;
            EnsureUsage();
            visibleDirty = false;
            visible.Clear();
            string term = (search ?? string.Empty).Trim();
            IEnumerable<Entry> shown = entries.Values.Where(e =>
                e.Keywords.Any(k => !hiddenKeywords.Contains(k)) &&
                (term.Length == 0 || e.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            shown = sortOrder switch
            {
                SortOrder.Path => shown.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase),
                SortOrder.SourceSize => shown.OrderByDescending(e => (long)e.SourceWidth * e.SourceHeight).ThenBy(e => e.Name),
                SortOrder.FileSize => shown.OrderByDescending(e => e.FileBytes).ThenBy(e => e.Name),
                SortOrder.Issues => shown.OrderByDescending(e => e.WorstIssue.HasValue ? (int)e.WorstIssue.Value + 1 : 0).ThenBy(e => e.Name),
                _ => shown.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            };
            visible.AddRange(shown.Select(e => e.Guid));
        }

        private List<Texture2D> LoadTargets() =>
            TargetGuids.Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t != null).ToList();

        #endregion

        #region GUI

        private void OnGUI()
        {
            HandleDragAndDrop();
            DrawToolbar();
            if (textureGuids.Count == 0)
            {
                EditorGUILayout.HelpBox("Select textures or folders in the Project window and press Add Selection, or drag them here. " +
                    "You can also right-click textures and choose UMA > Texture Tools.", MessageType.Info);
                return;
            }

            UpdateVisible();
            float listWidth = Mathf.Clamp(position.width * 0.42f, 340f, 560f);
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(listWidth)))
                {
                    DrawFilter();
                    DrawList();
                }
                GUILayout.Space(4f);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.ExpandWidth(true)))
                    DrawInfo();
            }
            DrawTools();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent("Add Selection", "Add the textures selected in the Project window, including textures inside selected folders."), EditorStyles.toolbarButton))
                    AddProjectSelection(false);
                if (GUILayout.Button(new GUIContent("Replace with Selection", "Replace the list with the Project window selection."), EditorStyles.toolbarButton))
                    AddProjectSelection(true);
                using (new EditorGUI.DisabledScope(textureGuids.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Remove Targets", "Remove the ticked, visible textures from this list. Files are not changed."), EditorStyles.toolbarButton))
                    {
                        foreach (string guid in TargetGuids) textureGuids.Remove(guid);
                        RebuildEntries();
                    }
                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
                    {
                        textureGuids.Clear(); uncheckedGuids.Clear(); focusedGuid = null;
                        RebuildEntries();
                    }
                    if (GUILayout.Button(new GUIContent("Rescan Overlays", "Search every OverlayDataAsset again for these textures."), EditorStyles.toolbarButton))
                        RescanUsage();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("Sort", EditorStyles.miniLabel);
                var newSort = (SortOrder)EditorGUILayout.EnumPopup(sortOrder, EditorStyles.toolbarDropDown, GUILayout.Width(90f));
                if (newSort != sortOrder) { sortOrder = newSort; visibleDirty = true; }
                string newSearch = GUILayout.TextField(search ?? string.Empty, EditorStyles.toolbarSearchField, GUILayout.Width(200f));
                if (newSearch != search) SetSearch(newSearch);
            }
        }

        private void AddProjectSelection(bool replace)
        {
            List<string> guids = ToGuids(Selection.GetFiltered<Texture2D>(SelectionMode.DeepAssets));
            if (guids.Count == 0)
            {
                ShowNotification(new GUIContent("No textures are selected in the Project window."));
                return;
            }
            SetTextures(guids, replace);
        }

        private void HandleDragAndDrop()
        {
            Event current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return;
            var guids = new List<string>();
            foreach (UnityEngine.Object dragged in DragAndDrop.objectReferences)
            {
                string path = AssetDatabase.GetAssetPath(dragged);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) guids.AddRange(AssetDatabase.FindAssets("t:Texture2D", new[] { path }));
                else if (dragged is Texture2D) guids.Add(AssetDatabase.AssetPathToGUID(path));
            }
            if (guids.Count == 0) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetTextures(guids.Distinct(), false);
            }
            current.Use();
        }

        private void DrawFilter()
        {
            List<KeyValuePair<string, int>> keywords = KeywordCounts();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    filterExpanded = EditorGUILayout.Foldout(filterExpanded, new GUIContent("Filter by overlay use (UMA Material keyword)",
                        "Each texture is listed under the material property its overlay slot feeds. A texture used by several overlays can have more than one keyword."), true);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(40f))) { hiddenKeywords.Clear(); visibleDirty = true; }
                    if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                    { hiddenKeywords = keywords.Select(k => k.Key).ToList(); visibleDirty = true; }
                }
                if (!filterExpanded) return;
                float height = Mathf.Min(keywords.Count * 18f + 4f, 126f);
                filterScroll = EditorGUILayout.BeginScrollView(filterScroll, GUILayout.Height(height));
                foreach (KeyValuePair<string, int> keyword in keywords)
                {
                    bool shown = !hiddenKeywords.Contains(keyword.Key);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool newShown = EditorGUILayout.ToggleLeft($"{keyword.Key}  ({keyword.Value})", shown);
                        if (newShown != shown) SetKeywordShown(keyword.Key, newShown);
                        if (GUILayout.Button(new GUIContent("Only", "Show only this keyword."), EditorStyles.miniButton, GUILayout.Width(40f)))
                            ShowOnlyKeywords(keyword.Key);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawList()
        {
            List<string> targets = TargetGuids;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{visible.Count} of {textureGuids.Count} shown · {targets.Count} ticked targets", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("All", "Tick every visible texture."), EditorStyles.miniButtonLeft, GUILayout.Width(36f)))
                    foreach (string guid in visible) SetChecked(guid, true);
                if (GUILayout.Button(new GUIContent("None", "Untick every visible texture."), EditorStyles.miniButtonMid, GUILayout.Width(40f)))
                    foreach (string guid in visible) SetChecked(guid, false);
                if (GUILayout.Button(new GUIContent("Invert", "Invert the ticks on visible textures."), EditorStyles.miniButtonRight, GUILayout.Width(44f)))
                    foreach (string guid in visible) SetChecked(guid, !IsChecked(guid));
            }

            listScroll = EditorGUILayout.BeginScrollView(listScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            for (int i = 0; i < visible.Count; i++)
            {
                Rect row = GUILayoutUtility.GetRect(10f, RowHeight, GUILayout.ExpandWidth(true));
                if (row.yMax < listScroll.y || row.y > listScroll.y + listViewHeight) continue;
                DrawRow(row, entries[visible[i]], i);
            }
            if (visible.Count == 0) GUILayout.Label("No textures match the filter.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) listViewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void DrawRow(Rect row, Entry entry, int index)
        {
            bool focused = entry.Guid == focusedGuid;
            if (Event.current.type == EventType.Repaint)
            {
                Color background = focused ? new Color(0.24f, 0.48f, 0.9f, 0.35f)
                    : index % 2 == 0 ? new Color(0f, 0f, 0f, 0.06f) : Color.clear;
                EditorGUI.DrawRect(row, background);
            }

            bool isChecked = IsChecked(entry.Guid);
            bool newChecked = GUI.Toggle(new Rect(row.x + 4f, row.y + 13f, 18f, 18f), isChecked, GUIContent.none);
            if (newChecked != isChecked) SetChecked(entry.Guid, newChecked);

            Texture icon = AssetDatabase.GetCachedIcon(entry.Path);
            if (icon != null) GUI.DrawTexture(new Rect(row.x + 24f, row.y + 4f, 36f, 36f), icon, ScaleMode.ScaleToFit);

            float textX = row.x + 66f, textWidth = row.width - 66f - 22f;
            GUI.Label(new Rect(textX, row.y + 1f, textWidth, 16f), entry.Name, EditorStyles.boldLabel);
            GUI.Label(new Rect(textX, row.y + 15f, textWidth, 14f), entry.Summary, EditorStyles.miniLabel);
            string keywords = string.Join(", ", entry.Keywords);
            GUI.Label(new Rect(textX, row.y + 28f, textWidth, 14f), new GUIContent(keywords, keywords), entry.Usages.Count == 0 ? EditorStyles.miniLabel : KeywordStyle);

            if (entry.WorstIssue.HasValue && entry.WorstIssue.Value != UMATextureSettingIssue.Severity.Info)
            {
                GUIContent issueIcon = SeverityIcon(entry.WorstIssue.Value);
                issueIcon.tooltip = string.Join("\n", entry.Issues.Where(i => i.Level != UMATextureSettingIssue.Severity.Info).Select(i => i.Setting + ": " + i.Message));
                GUI.Label(new Rect(row.xMax - 22f, row.y + 12f, 20f, 20f), issueIcon);
            }

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && row.Contains(current.mousePosition))
            {
                focusedGuid = entry.Guid;
                if (current.clickCount == 2) EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(entry.Path));
                current.Use();
                Repaint();
            }
        }

        private static GUIStyle keywordStyle;
        private static GUIStyle KeywordStyle => keywordStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.55f, 0.75f, 1f) : new Color(0.1f, 0.3f, 0.65f) }
        };

        private static GUIStyle greyStyle;
        private static GUIStyle GreyStyle => greyStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f) }
        };

        private static GUIContent SeverityIcon(UMATextureSettingIssue.Severity severity) => new GUIContent(EditorGUIUtility.IconContent(
            severity == UMATextureSettingIssue.Severity.Error ? "console.erroricon.sml"
            : severity == UMATextureSettingIssue.Severity.Warning ? "console.warnicon.sml" : "console.infoicon.sml"));

        private void DrawInfo()
        {
            infoScroll = EditorGUILayout.BeginScrollView(infoScroll);
            if (focusedGuid != null && entries.TryGetValue(focusedGuid, out Entry entry)) DrawFocused(entry);
            else GUILayout.Label("Click a texture to see its details.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(8f);
            DrawTargetSummary();
            EditorGUILayout.EndScrollView();
        }

        private void DrawFocused(Entry entry)
        {
            if (focusedTextureGuid != entry.Guid || focusedTexture == null)
            {
                focusedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.Path);
                focusedTextureGuid = entry.Guid;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                Rect preview = GUILayoutUtility.GetRect(160f, 160f, GUILayout.Width(160f), GUILayout.Height(160f));
                if (focusedTexture != null)
                {
                    if (showAlpha) EditorGUI.DrawTextureAlpha(preview, focusedTexture, ScaleMode.ScaleToFit);
                    else EditorGUI.DrawPreviewTexture(preview, focusedTexture, null, ScaleMode.ScaleToFit);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);
                    EditorGUILayout.SelectableLabel(entry.Path, EditorStyles.miniLabel, GUILayout.Height(16f));
                    showAlpha = EditorGUILayout.ToggleLeft("Preview alpha", showAlpha);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Ping", EditorStyles.miniButtonLeft)) EditorGUIUtility.PingObject(focusedTexture);
                        if (GUILayout.Button("Select", EditorStyles.miniButtonMid)) Selection.activeObject = focusedTexture;
                        if (GUILayout.Button(new GUIContent("Edit Pixels", "Open in UMA Texture Utilities, the pixel editor."), EditorStyles.miniButtonRight) && focusedTexture != null)
                            UMATextureUtilitiesWindow.Open(new[] { focusedTexture });
                    }
                    if (focusedTexture != null)
                    {
                        Detail("Imported", $"{focusedTexture.width}×{focusedTexture.height}, {focusedTexture.format}, {focusedTexture.mipmapCount} mip(s)");
                        Detail("Memory", FormatBytes(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(focusedTexture)));
                    }
                    Detail("Source", $"{entry.SourceWidth}×{entry.SourceHeight}, {FormatBytes(entry.FileBytes)} on disk" +
                        (entry.Importer != null ? entry.Importer.DoesSourceTextureHaveAlpha() ? ", has alpha" : ", no alpha" : string.Empty));
                }
            }

            TextureImporter importer = entry.Importer;
            if (importer != null)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Import settings", EditorStyles.miniBoldLabel);
                TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
                Detail("Type", $"{importer.textureType} ({importer.textureShape})");
                Detail("sRGB", importer.textureType == TextureImporterType.NormalMap ? "n/a (normal map)" : importer.sRGBTexture ? "On (color)" : "Off (linear data)");
                Detail("Alpha", $"{importer.alphaSource}{(importer.alphaIsTransparency ? ", Is Transparency" : string.Empty)}");
                Detail("Mipmaps", importer.mipmapEnabled ? $"On ({importer.mipmapFilter}{(importer.streamingMipmaps ? ", streaming" : string.Empty)})" : "Off");
                Detail("Sampling", $"{importer.filterMode}, {importer.wrapMode}, aniso {importer.anisoLevel}, NPOT {importer.npotScale}");
                Detail("Default", $"max {platform.maxTextureSize} ({platform.resizeAlgorithm}), {platform.textureCompression}{(platform.crunchedCompression ? $", crunch {platform.compressionQuality}" : string.Empty)}");
                foreach (string name in UMATexturePngConverter.GetPlatformNames(importer))
                {
                    TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(name);
                    if (settings.overridden) Detail(name, $"max {settings.maxTextureSize}, {settings.format}, {settings.textureCompression}");
                }
                Detail("Read/Write", importer.isReadable ? "On" : "Off");
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"Used by overlays ({entry.Usages.Count})", EditorStyles.miniBoldLabel);
            if (!usageScanned) GUILayout.Label("Not scanned yet.", EditorStyles.miniLabel);
            else if (entry.Usages.Count == 0) GUILayout.Label("No OverlayDataAsset in the project references this texture.", EditorStyles.miniLabel);
            foreach (UMATextureUsage usage in entry.Usages)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string slot = usage.ChannelIndex < 0 ? "alpha mask" : "texture " + usage.ChannelIndex;
                    GUILayout.Label(new GUIContent($"{usage.Keyword}  ←  {usage.OverlayName} ({slot}, {usage.MaterialName})", usage.OverlayPath), KeywordStyle);
                    if (GUILayout.Button("Ping", EditorStyles.miniButton, GUILayout.Width(40f)))
                        EditorGUIUtility.PingObject(usage.Overlay);
                }
            }

            if (entry.Issues.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Checks", EditorStyles.miniBoldLabel);
                foreach (UMATextureSettingIssue issue in entry.Issues)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(SeverityIcon(issue.Level), GUILayout.Width(18f), GUILayout.Height(18f));
                        GUILayout.Label(issue.Setting + ": " + issue.Message, EditorStyles.wordWrappedMiniLabel);
                        using (new EditorGUI.DisabledScope(issue.Fix == null))
                        {
                            if (GUILayout.Button(new GUIContent("Fix", issue.Fix == null ? "This needs a change to the image or the overlays." : "Change this setting and reimport."),
                                    EditorStyles.miniButton, GUILayout.Width(36f)))
                            {
                                RunImporterChange(new List<string> { entry.Guid }, "Fix " + issue.Setting, importerToFix =>
                                {
                                    issue.Fix(importerToFix);
                                    return new List<string> { "Fixed " + issue.Setting };
                                });
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }
            }
        }

        private static void Detail(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, EditorStyles.miniBoldLabel, GUILayout.Width(80f));
                GUILayout.Label(value, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawTargetSummary()
        {
            List<Entry> targets = TargetGuids.Select(g => entries[g]).ToList();
            EditorGUILayout.LabelField($"Targets: {targets.Count} texture(s), {FormatBytes(targets.Sum(e => e.FileBytes))} on disk", EditorStyles.miniBoldLabel);
            if (targets.Count == 0) return;
            var importers = targets.Where(e => e.Importer != null).Select(e => e.Importer).ToList();
            Detail("Type", Tally(importers, i => i.textureType.ToString()));
            Detail("sRGB", Tally(importers.Where(i => i.textureType != TextureImporterType.NormalMap), i => i.sRGBTexture ? "on" : "off"));
            Detail("Alpha", Tally(importers, i => i.alphaSource.ToString()));
            Detail("Max Size", Tally(importers, i => i.GetDefaultPlatformTextureSettings().maxTextureSize.ToString()));
            Detail("Compression", Tally(importers, i => ShortCompression(i.GetDefaultPlatformTextureSettings())));
            Detail("Mipmaps", Tally(importers, i => i.mipmapEnabled ? "on" : "off"));
            Detail("Read/Write", Tally(importers, i => i.isReadable ? "on" : "off"));
            int errors = targets.Sum(e => e.Issues.Count(i => i.Level == UMATextureSettingIssue.Severity.Error));
            int warnings = targets.Sum(e => e.Issues.Count(i => i.Level == UMATextureSettingIssue.Severity.Warning));
            Detail("Checks", errors + warnings == 0 ? "No problems found." : $"{errors} error(s), {warnings} warning(s). See UMA Material Check.");
        }

        private static string Tally(IEnumerable<TextureImporter> importers, Func<TextureImporter, string> key) =>
            string.Join(", ", importers.GroupBy(key).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}"));

        #endregion

        #region Tools

        private void DrawTools()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(ToolsHeight)))
            {
                tab = (Tab)GUILayout.Toolbar((int)tab, TabNames);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                    {
                        toolScroll = EditorGUILayout.BeginScrollView(toolScroll);
                        switch (tab)
                        {
                            case Tab.ImportSettings: DrawImportSettingsTool(); break;
                            case Tab.MaterialCheck: DrawMaterialCheckTool(); break;
                            case Tab.Resize: DrawResizeTool(); break;
                            case Tab.ConvertToPng: DrawPngTool(); break;
                            default: DrawMoreTool(); break;
                        }
                        EditorGUILayout.EndScrollView();
                    }
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(280f, position.width * 0.32f))))
                        DrawLog();
                }
            }
        }

        private void DrawLog()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Results", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", EditorStyles.miniButtonLeft, GUILayout.Width(44f))) EditorGUIUtility.systemCopyBuffer = string.Join("\n", log);
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(44f))) log.Clear();
            }
            logScroll = EditorGUILayout.BeginScrollView(logScroll, EditorStyles.helpBox);
            foreach (string line in log) GUILayout.Label(line, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Log(string line)
        {
            log.Add(line);
            if (log.Count > 2000) log.RemoveRange(0, log.Count - 2000);
            logScroll.y = float.MaxValue;
        }

        private void TargetHeader(string verb)
        {
            int count = TargetGuids.Count;
            EditorGUILayout.LabelField($"{verb} the {count} ticked, visible texture(s).", count == 0 ? EditorStyles.miniLabel : EditorStyles.miniBoldLabel);
        }

        private bool Confirm(string title, string message, int count)
        {
            if (count == 0)
            {
                ShowNotification(new GUIContent("No targets: tick at least one visible texture."));
                return false;
            }
            return EditorUtility.DisplayDialog(title, message, "Apply", "Cancel");
        }

        private static Func<float, string, bool> ProgressBar(string title) =>
            (p, path) => EditorUtility.DisplayCancelableProgressBar(title, path, p);

        private void LogResults(string title, IEnumerable<UMATextureToolsOperations.Result> results)
        {
            List<UMATextureToolsOperations.Result> list = results.ToList();
            Log($"— {title}: {list.Count(r => r.Changed)} changed, {list.Count(r => !r.Changed && !r.Failed)} unchanged or skipped, {list.Count(r => r.Failed)} failed.");
            foreach (UMATextureToolsOperations.Result result in list.Where(r => r.Changed || r.Failed || r.Message.StartsWith("Skipped", StringComparison.Ordinal)))
                Log($"{Path.GetFileName(result.Path)}: {result.Message}");
        }

        private void RunImporterChange(List<string> guids, string title, Func<TextureImporter, List<string>> change)
        {
            List<UMATextureToolsOperations.Result> results;
            try
            {
                results = new List<UMATextureToolsOperations.Result>();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var result = new UMATextureToolsOperations.Result { Guid = guid, Path = path };
                    results.Add(result);
                    string blocker = UMATextureToolsOperations.GetWriteBlocker(path);
                    if (blocker != null) { result.Message = "Skipped: " + blocker; continue; }
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    List<string> changes = change(importer);
                    result.Changed = changes.Count > 0;
                    result.Message = string.Join("; ", changes);
                    if (result.Changed) importer.SaveAndReimport();
                }
            }
            catch (Exception exception)
            {
                Log(title + " failed: " + exception.Message);
                return;
            }
            LogResults(title, results);
            RebuildEntries();
        }

        private void DrawImportSettingsTool()
        {
            TargetHeader("Change only the ticked settings on");
            UMATextureImportEdit e = importEdit;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330f)))
                {
                    Row(ref e.setType, "Texture Type", () => e.type = (TextureImporterType)EditorGUILayout.EnumPopup(e.type));
                    Row(ref e.setShape, "Texture Shape", () => e.shape = (TextureImporterShape)EditorGUILayout.EnumPopup(e.shape));
                    Row(ref e.setSRGB, "sRGB (Color Texture)", () => e.sRGB = EditorGUILayout.Toggle(e.sRGB));
                    Row(ref e.setAlphaSource, "Alpha Source", () => e.alphaSource = (TextureImporterAlphaSource)EditorGUILayout.EnumPopup(e.alphaSource));
                    Row(ref e.setAlphaIsTransparency, "Alpha Is Transparency", () => e.alphaIsTransparency = EditorGUILayout.Toggle(e.alphaIsTransparency));
                    Row(ref e.setReadable, "Read/Write", () => e.readable = EditorGUILayout.Toggle(e.readable));
                    Row(ref e.setMipmaps, "Generate Mipmaps", () => e.mipmaps = EditorGUILayout.Toggle(e.mipmaps));
                    Row(ref e.setMipFilter, "Mip Filter", () => e.mipFilter = (TextureImporterMipFilter)EditorGUILayout.EnumPopup(e.mipFilter));
                    Row(ref e.setStreaming, "Mip Streaming", () => e.streamingMipmaps = EditorGUILayout.Toggle(e.streamingMipmaps));
                }
                GUILayout.Space(12f);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(330f)))
                {
                    Row(ref e.setWrap, "Wrap Mode", () => e.wrap = (TextureWrapMode)EditorGUILayout.EnumPopup(e.wrap));
                    Row(ref e.setFilter, "Filter Mode", () => e.filter = (FilterMode)EditorGUILayout.EnumPopup(e.filter));
                    Row(ref e.setAniso, "Aniso Level", () => e.aniso = EditorGUILayout.IntSlider(e.aniso, 0, 16));
                    Row(ref e.setNpot, "Non-Power of 2", () => e.npot = (TextureImporterNPOTScale)EditorGUILayout.EnumPopup(e.npot));

                    int platformIndex = Mathf.Max(0, Array.IndexOf(PlatformNames, e.IsDefaultPlatform ? "Default" : e.platform));
                    int newIndex = EditorGUILayout.Popup(new GUIContent("Platform", "Where Max Size, Compression and Format go. Any other platform becomes an override."), platformIndex, PlatformNames);
                    e.platform = newIndex == 0 ? string.Empty : PlatformNames[newIndex];
                    if (!e.IsDefaultPlatform)
                        e.removeOverride = EditorGUILayout.ToggleLeft($"Remove the {e.platform} override instead", e.removeOverride);
                    using (new EditorGUI.DisabledScope(!e.IsDefaultPlatform && e.removeOverride))
                    {
                        Row(ref e.setMaxSize, "Max Size", () => e.maxSize = EditorGUILayout.IntPopup(e.maxSize, SizeNames, Sizes));
                        Row(ref e.setResizeAlgorithm, "Resize Algorithm", () => e.resizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup(e.resizeAlgorithm));
                        Row(ref e.setCompression, "Compression", () => e.compression = (TextureImporterCompression)EditorGUILayout.EnumPopup(e.compression));
                        Row(ref e.setCrunch, "Crunch Compression", () =>
                        {
                            e.crunch = EditorGUILayout.Toggle(e.crunch, GUILayout.Width(18f));
                            using (new EditorGUI.DisabledScope(!e.crunch)) e.crunchQuality = EditorGUILayout.IntSlider(e.crunchQuality, 0, 100);
                        });
                        if (!e.IsDefaultPlatform)
                            Row(ref e.setFormat, "Format", () => e.format = (TextureImporterFormat)EditorGUILayout.EnumPopup(e.format));
                    }
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(focusedGuid == null || !entries.ContainsKey(focusedGuid) || entries[focusedGuid].Importer == null))
                {
                    if (GUILayout.Button(new GUIContent("Copy from Focused Texture", "Fill every value from the texture shown in the details panel and tick the common settings.")))
                        e.CopyFrom(entries[focusedGuid].Importer);
                }
                if (GUILayout.Button("Untick All")) e.ClearFlags();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(!e.Any))
                {
                    if (GUILayout.Button("Apply and Reimport", GUILayout.Width(180f), GUILayout.Height(24f)))
                    {
                        List<string> targets = TargetGuids;
                        if (Confirm("Apply Import Settings", $"Change the ticked import settings on {targets.Count} texture(s) and reimport the ones that change? Source images are not modified.", targets.Count))
                        {
                            try { LogResults("Import settings", UMATextureToolsOperations.ApplyImportEdit(targets, e, ProgressBar("Applying import settings"))); }
                            finally { EditorUtility.ClearProgressBar(); }
                            RebuildEntries();
                        }
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private static void Row(ref bool set, string label, Action drawValue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                set = EditorGUILayout.ToggleLeft(label, set, set ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.Width(160f));
                using (new EditorGUI.DisabledScope(!set)) drawValue();
            }
        }

        private void DrawMaterialCheckTool()
        {
            EditorGUILayout.HelpBox("Compares each target's import settings with what its UMA Material channels expect: Normal map or not, sRGB for color " +
                "versus linear for data (metallic, smoothness, occlusion, masks), and alpha that holds data. Fixes change import settings only, then reimport.", MessageType.None);
            List<Entry> withIssues = TargetGuids.Select(g => entries[g])
                .Where(e => e.Issues.Any(i => i.Level != UMATextureSettingIssue.Severity.Info || fixSuggestions)).ToList();
            int fixable = withIssues.Sum(e => e.Issues.Count(i => i.Fix != null && (i.Level != UMATextureSettingIssue.Severity.Info || fixSuggestions)));
            using (new EditorGUILayout.HorizontalScope())
            {
                fixSuggestions = EditorGUILayout.ToggleLeft(new GUIContent("Include suggestions", "Also show and fix informational items, such as turning Read/Write off."), fixSuggestions, GUILayout.Width(150f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Show Only Problem Textures", "Untick every visible texture without a problem."), GUILayout.Width(190f)))
                    foreach (string guid in visible) SetChecked(guid, entries[guid].Issues.Any(i => i.Level != UMATextureSettingIssue.Severity.Info || fixSuggestions));
                using (new EditorGUI.DisabledScope(fixable == 0))
                {
                    if (GUILayout.Button($"Fix {fixable} Problem(s)", GUILayout.Width(160f), GUILayout.Height(22f)))
                    {
                        List<string> guids = withIssues.Select(e => e.Guid).ToList();
                        if (Confirm("Match UMA Material Settings", $"Fix {fixable} import setting(s) on {guids.Count} texture(s) and reimport them?", guids.Count))
                        {
                            try { LogResults("UMA Material check", UMATextureToolsOperations.ApplyMaterialFixes(guids, usages, ProgressBar("Fixing import settings"), fixSuggestions)); }
                            finally { EditorUtility.ClearProgressBar(); }
                            RebuildEntries();
                        }
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (withIssues.Count == 0)
            {
                GUILayout.Label(TargetGuids.Count == 0 ? "No targets." : "No problems found on the targets.", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            foreach (Entry entry in withIssues)
            {
                if (GUILayout.Button(entry.Name, EditorStyles.miniBoldLabel)) focusedGuid = entry.Guid;
                foreach (UMATextureSettingIssue issue in entry.Issues.Where(i => i.Level != UMATextureSettingIssue.Severity.Info || fixSuggestions))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(12f);
                        GUILayout.Label(SeverityIcon(issue.Level), GUILayout.Width(18f), GUILayout.Height(16f));
                        GUILayout.Label($"{issue.Setting}: {issue.Message}{(issue.Fix == null ? " (not fixable here)" : string.Empty)}", EditorStyles.wordWrappedMiniLabel);
                    }
                }
            }
        }

        private void DrawResizeTool()
        {
            EditorGUILayout.HelpBox("Only sizes down. Lowers the importer's Max Size (Default and enabled platform overrides above the limit) and reimports. " +
                "Source images, smaller limits and all other settings are kept. Package and read-only textures are skipped.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                int newMaximum = EditorGUILayout.IntPopup("Maximum Size (pixels)", resizeMaximum, SizeNames, Sizes, GUILayout.Width(330f));
                if (newMaximum != resizeMaximum) { resizeMaximum = newMaximum; resizePreview = null; }
                GUILayout.FlexibleSpace();
            }

            List<string> targets = TargetGuids;
            string key = resizeMaximum + "|" + string.Join(",", targets);
            if (resizePreview == null || resizePreviewKey != key)
            {
                resizePreview = targets.Select(g => UmaResizeTexturesUtility.Inspect(g, resizeMaximum)).ToList();
                resizePreviewKey = key;
            }
            int count = resizePreview.Count(r => r.CanResize);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{count} to resize; {resizePreview.Count - count} unchanged or skipped.", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(count == 0))
                {
                    if (GUILayout.Button("Apply and Reimport", GUILayout.Width(180f), GUILayout.Height(22f)))
                    {
                        if (Confirm("Resize Textures", $"Lower Max Size to {resizeMaximum} on {count} texture(s) and reimport them? Source images are not modified.", count))
                            RunResize(resizePreview.Where(r => r.CanResize).Select(r => r.Guid).ToList());
                        GUIUtility.ExitGUI();
                    }
                }
            }
            foreach (UmaResizeTexturesUtility.Entry entry in resizePreview)
                GUILayout.Label($"{Path.GetFileName(entry.Path)}: {entry.Message}", entry.CanResize ? EditorStyles.wordWrappedMiniLabel : GreyStyle);
        }

        private void RunResize(List<string> guids)
        {
            int resized = 0, skipped = 0, failed = 0;
            try
            {
                for (int i = 0; i < guids.Count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (EditorUtility.DisplayCancelableProgressBar("Resizing textures", path, (float)i / guids.Count))
                    {
                        Log($"Resize stopped: {guids.Count - i} not processed.");
                        break;
                    }
                    try
                    {
                        UmaResizeTexturesUtility.Entry result = UmaResizeTexturesUtility.Resize(guids[i], resizeMaximum);
                        if (result.CanResize) resized++; else skipped++;
                        Log($"{Path.GetFileName(path)}: {result.Message}");
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Log($"{Path.GetFileName(path)}: Failed: {exception.Message}");
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            Log($"— Resize to {resizeMaximum}: {resized} importer(s) updated, {skipped} skipped, {failed} failed.");
            RebuildEntries();
        }

        private void DrawPngTool()
        {
            EditorGUILayout.HelpBox("Writes 8-bit RGBA PNG from each image's full-resolution, uncompressed source pixels (not the compressed or size-limited import). " +
                "PNG files and HDR images (.exr, .hdr) are skipped.", MessageType.None);
            pngMode = (UMATexturePngConverter.Mode)EditorGUILayout.Popup("Mode", (int)pngMode, new[]
            {
                "Replace in place (keeps GUID, references and import settings)",
                "Create a PNG copy next to each original"
            });
            if (pngMode == UMATexturePngConverter.Mode.CreateCopy)
                pngRelink = EditorGUILayout.ToggleLeft("Point overlays that use the original at the copy", pngRelink);
            else
                EditorGUILayout.LabelField("The original image is replaced by a PNG of the same name. Its .meta (GUID and import settings) goes with it, so nothing that references it changes.", EditorStyles.wordWrappedMiniLabel);

            List<string> targets = TargetGuids;
            var reasons = targets.Select(g => (guid: g, reason: UMATexturePngConverter.GetSkipReason(AssetDatabase.GUIDToAssetPath(g), pngMode))).ToList();
            int convertible = reasons.Count(r => r.reason == null);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{convertible} to convert; {targets.Count - convertible} skipped.", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(convertible == 0))
                {
                    if (GUILayout.Button("Convert", GUILayout.Width(180f), GUILayout.Height(22f)))
                    {
                        string what = pngMode == UMATexturePngConverter.Mode.ReplaceInPlace
                            ? "replace each original file with a PNG (the original image file is removed; GUID and import settings are kept)"
                            : "create a PNG copy of each" + (pngRelink ? " and point overlays at the copies" : string.Empty);
                        if (Confirm("Convert to PNG", $"Convert {convertible} texture(s): {what}?", convertible))
                            RunPngConversion(reasons.Where(r => r.reason == null).Select(r => r.guid).ToList());
                        GUIUtility.ExitGUI();
                    }
                }
            }
            foreach (var (guid, reason) in reasons.Where(r => r.reason != null))
                GUILayout.Label($"{Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid))}: skipped, {reason}", GreyStyle);
        }

        private void RunPngConversion(List<string> guids)
        {
            List<UMATexturePngConverter.Result> results;
            try { results = UMATexturePngConverter.Convert(guids, pngMode, pngRelink, ProgressBar("Converting to PNG")); }
            finally { EditorUtility.ClearProgressBar(); }

            long before = results.Where(r => r.Converted).Sum(r => r.BytesBefore), after = results.Where(r => r.Converted).Sum(r => r.BytesAfter);
            Log($"— Convert to PNG: {results.Count(r => r.Converted)} converted, {results.Count(r => r.Failed)} failed; {FormatBytes(before)} → {FormatBytes(after)}.");
            foreach (UMATexturePngConverter.Result result in results)
                Log($"{Path.GetFileName(result.SourcePath)}: {result.Message}");

            if (pngMode == UMATexturePngConverter.Mode.CreateCopy)
            {
                // Work on the copies from now on; keep the originals listed but unticked.
                foreach (UMATexturePngConverter.Result result in results.Where(r => r.Converted))
                {
                    SetChecked(result.SourceGuid, false);
                    if (!textureGuids.Contains(result.OutputGuid)) textureGuids.Add(result.OutputGuid);
                }
                if (pngRelink) usageScanned = false;
            }
            RebuildEntries();
        }

        private void DrawMoreTool()
        {
            TargetHeader("Work with");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Select in Project", "Select the targets in the Project window.")))
                    Selection.objects = LoadTargets().Cast<UnityEngine.Object>().ToArray();
                if (GUILayout.Button(new GUIContent("Edit Pixels in Texture Utilities", "Open the targets in UMA Texture Utilities, the pixel editor.")))
                {
                    List<Texture2D> textures = LoadTargets();
                    if (textures.Count > 0) UMATextureUtilitiesWindow.Open(textures);
                }
                if (GUILayout.Button(new GUIContent("Move Unused Textures…", "Move targets that no overlay uses to another folder, keeping their GUIDs.")))
                {
                    List<Texture2D> textures = LoadTargets();
                    if (textures.Count > 0) UmaMoveUnusedTexturesWindow.Open(textures);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Copy CSV Report", "Copy a CSV of the targets' sizes, import settings, keywords, overlays and problems.")))
                {
                    EditorGUIUtility.systemCopyBuffer = UMATextureToolsOperations.BuildReport(TargetGuids, usages);
                    ShowNotification(new GUIContent("Report copied to the clipboard."));
                }
                if (GUILayout.Button("Save CSV Report…"))
                {
                    string file = EditorUtility.SaveFilePanel("Save Texture Report", Path.GetDirectoryName(Application.dataPath), "TextureReport.csv", "csv");
                    if (!string.IsNullOrEmpty(file))
                    {
                        File.WriteAllText(file, UMATextureToolsOperations.BuildReport(TargetGuids, usages));
                        Log("Saved report to " + file);
                    }
                }
                if (GUILayout.Button(new GUIContent("Tick Unused", "Tick only the visible textures that no overlay uses.")))
                    foreach (string guid in visible) SetChecked(guid, entries[guid].Usages.Count == 0);
            }
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Tips", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField("• Drag textures or folders onto this window to add them. Double-click a row to ping the texture.\n" +
                "• Keywords come from each overlay's UMA Material: overlay texture slot N feeds the material property of channel N.\n" +
                "• Every operation uses only ticked textures that the filter and search show.", EditorStyles.wordWrappedMiniLabel);
        }

        #endregion

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024d;
            if (kb < 1024d) return kb.ToString("F1") + " KB";
            double mb = kb / 1024d;
            return mb < 1024d ? mb.ToString("F1") + " MB" : (mb / 1024d).ToString("F2") + " GB";
        }
    }
}
