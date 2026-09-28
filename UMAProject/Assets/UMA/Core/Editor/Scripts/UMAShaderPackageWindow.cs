using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Editors
{
    public sealed class UMAShaderPackageWindow : EditorWindow
    {
        private List<UMAShaderPackageUtility.Package> packages = new List<UMAShaderPackageUtility.Package>();
        private List<UMAShaderPackageUtility.Package> visible = new List<UMAShaderPackageUtility.Package>();
        private MultiColumnListView table;
        private ScrollView details;
        private Label status;
        private string search = string.Empty;
        [SerializeField] private string selectedPath;
        private bool refreshQueued;

        [MenuItem("UMA/Shader Package Manager", priority = 130)]
        public static void Open()
        {
            var window = GetWindow<UMAShaderPackageWindow>("Shader Packages");
            window.minSize = new Vector2(1060, 400);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += QueueRefresh;
            Undo.undoRedoPerformed += QueueRefresh;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= QueueRefresh;
            Undo.undoRedoPerformed -= QueueRefresh;
            EditorApplication.delayCall -= Refresh;
            refreshQueued = false;
        }

        internal static void AddStyle(VisualElement root)
        {
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UMAEditorUtilities.FindUMAFullPath() + "/Core/Editor/Scripts/UMAShaderPackageWindow.uss");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("shader-package-root");
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            AddStyle(rootVisualElement);
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UMAEditorUtilities.FindUMAFullPath() + "/Core/Editor/Scripts/UMAShaderPackageWindow.uxml");
            if (layout == null)
            {
                rootVisualElement.Add(new HelpBox("Shader package window layout could not be loaded. Reimport its UXML file.", HelpBoxMessageType.Error));
                return;
            }
            layout.CloneTree(rootVisualElement);
            details = rootVisualElement.Q<ScrollView>("details");
            status = rootVisualElement.Q<Label>("status");
            var refresh = rootVisualElement.Q<Button>("refresh");
            refresh.tooltip = "Refresh assets and rescan all shader package usage.";
            refresh.clicked += () => { AssetDatabase.Refresh(); QueueRefresh(); };
            var searchField = rootVisualElement.Q<ToolbarSearchField>("search");
            searchField.SetValueWithoutNotify(search);
            searchField.RegisterValueChangedCallback(evt => { search = evt.newValue; Filter(); });
            table = new MultiColumnListView { selectionType = SelectionType.Single, fixedItemHeight = 24, showBorder = true };
            table.AddToClassList("package-table");
            AddTextColumn("package", "Package name", 205, p => p.Name);
            AddTextColumn("shader", "Shader name", 230, p => p.Shader != null ? p.Shader.name : "(unavailable)");
            AddCheckColumn("material", "In Material", p => p.Materials.Count > 0);
            AddCheckColumn("uma", "In UMAMaterial", p => p.Uses.Count > 0);
            table.columns.Add(new Column
            {
                name = "archive", title = "Archive", width = 85, minWidth = 85, stretchable = false,
                makeCell = () =>
                {
                    var button = new Button { text = "Archive" };
                    button.clicked += () => { if (button.userData is UMAShaderPackageUtility.Package package) ArchivePackage(package); };
                    return button;
                },
                bindCell = (element, index) =>
                {
                    var package = visible[index];
                    element.userData = package;
                    string error = UMAShaderPackageArchive.ValidateSource(package.Path);
                    element.SetEnabled(error == null);
                    element.tooltip = error ?? "Archive this package and its .meta file, then remove them from the project.";
                },
                unbindCell = (element, index) => element.userData = null
            });
            table.selectionChanged += selection =>
            {
                var selected = selection.OfType<UMAShaderPackageUtility.Package>().FirstOrDefault();
                selectedPath = selected?.Path;
                ShowDetails(selected);
            };
            rootVisualElement.Q("packages").Add(table);
            QueueRefresh();
        }

        private void ArchivePackage(UMAShaderPackageUtility.Package package)
        {
            try
            {
                string error = UMAShaderPackageArchive.ValidateSource(package.Path);
                if (error != null) throw new InvalidOperationException(error);
                var current = UMAShaderPackageUtility.Scan().FirstOrDefault(item => item.Path == package.Path);
                if (current == null) { QueueRefresh(); return; }
                string usage = current.Shader == null
                    ? "The shader is unavailable, so its usage cannot be determined.\n\n"
                    : current.Materials.Count > 0
                        ? "This shader is still used by " + current.Materials.Count + " Material(s) and " + current.Uses.Count +
                            " UMAMaterial(s). Deleting it will leave those references missing.\n\n"
                        : string.Empty;
                if (!EditorUtility.DisplayDialog("Archive Shader Package",
                    usage + "Archive " + current.Path + " and its .meta file in:\n" + UMAShaderPackageArchive.ArchivePath +
                    "\n\nThe source files will be deleted after the archive is verified. Restore both files from the ZIP to recover them; this action cannot be undone with Undo.",
                    "Archive and delete", "Cancel")) return;
                UMAShaderPackageArchive.Archive(current.Path);
                QueueRefresh();
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Archive failed", exception.Message, "OK");
                Debug.LogException(exception);
                QueueRefresh();
            }
        }

        private void AddTextColumn(string name, string title, float width, Func<UMAShaderPackageUtility.Package, string> value)
        {
            table.columns.Add(new Column
            {
                name = name, title = title, width = width, stretchable = true,
                makeCell = () => new Label(),
                bindCell = (element, index) => { ((Label)element).text = value(visible[index]); element.tooltip = value(visible[index]) + "\n" + visible[index].Path; }
            });
        }

        private void AddCheckColumn(string name, string title, Func<UMAShaderPackageUtility.Package, bool> value)
        {
            table.columns.Add(new Column
            {
                name = name, title = title, width = 105, minWidth = 95, stretchable = false,
                makeCell = () => { var toggle = new Toggle(); toggle.SetEnabled(false); toggle.AddToClassList("usage-check"); return toggle; },
                bindCell = (element, index) => ((Toggle)element).SetValueWithoutNotify(value(visible[index]))
            });
        }

        private void QueueRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            EditorApplication.delayCall += Refresh;
            if (status != null) status.text = "Scanning shader package usage...";
        }

        private void Refresh()
        {
            refreshQueued = false;
            if (this == null || table == null) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueRefresh(); return; }
            try
            {
                packages = UMAShaderPackageUtility.Scan(progress: (value, path) =>
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Shader package usage", path, value)) throw new OperationCanceledException();
                });
                Filter();
            }
            catch (OperationCanceledException) { status.text = "Scan cancelled. The previous results are still shown; use Refresh to scan again."; }
            catch (Exception exception) { status.text = "Scan failed: " + exception.Message; Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        private void Filter()
        {
            if (table == null) return;
            string saved = selectedPath;
            visible = packages.Where(p => string.IsNullOrWhiteSpace(search) ||
                (p.Name + " " + p.Path + " " + (p.Shader != null ? p.Shader.name : string.Empty)).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            table.itemsSource = visible;
            table.Rebuild();
            int index = visible.FindIndex(p => p.Path == saved);
            if (index < 0 && visible.Count > 0) index = 0;
            table.SetSelection(index);
            selectedPath = index >= 0 ? visible[index].Path : null;
            ShowDetails(index >= 0 ? visible[index] : null);
            status.text = visible.Count + " / " + packages.Count + " packages. Usage includes saved Material assets and all default, second-pass and HDRP UMAMaterial references.";
        }

        private void ShowDetails(UMAShaderPackageUtility.Package package)
        {
            details.Clear();
            if (package == null) { details.Add(new Label("Select a shader package.")); return; }
            var title = new Label(package.Name); title.AddToClassList("section-title"); details.Add(title);
            var path = new Label(package.Path); path.AddToClassList("wrapped"); details.Add(path);
            AddAsset(details, "Shader", package.Shader);
            if (package.Shader == null)
            {
                details.Add(new HelpBox("This package has no imported Shader for the current pipeline. Its usage cannot be resolved until the import problem is corrected.", HelpBoxMessageType.Warning));
                return;
            }
            details.Add(new Label("UMAMaterials (" + package.Uses.Count + ")") { name = "usageTitle" });
            if (package.Uses.Count == 0) details.Add(new Label("No UMAMaterial references this package shader."));
            foreach (var use in package.Uses)
            {
                var row = new VisualElement(); row.AddToClassList("usage-row");
                AddAsset(row, string.Empty, use.Owner);
                var button = new Button(() => UMAShaderPackageReplaceWindow.Open(package.Shader, use)) { text = "Replace" };
                row.Add(button);
                details.Add(row);
                var slots = new Label(string.Join(", ", use.Slots.Select(i => UMAShaderPackageUtility.MaterialSlotLabels[i])));
                slots.AddToClassList("slot-label"); details.Add(slots);
            }
            var materials = new Foldout { text = "Materials (" + package.Materials.Count + ")", value = false };
            foreach (var material in package.Materials) AddAsset(materials, string.Empty, material);
            details.Add(materials);
        }

        internal static void AddAsset(VisualElement parent, string label, Object asset)
        {
            var row = new VisualElement(); row.AddToClassList("asset-row");
            var field = new ObjectField(label) { objectType = asset != null ? asset.GetType() : typeof(Object), allowSceneObjects = false };
            field.SetValueWithoutNotify(asset); field.tooltip = asset != null ? AssetDatabase.GetAssetPath(asset) : string.Empty; field.SetEnabled(false); row.Add(field);
            var ping = new Button(() => { if (asset != null) EditorGUIUtility.PingObject(asset); }) { text = "Locate" };
            ping.SetEnabled(asset != null); row.Add(ping); parent.Add(row);
        }
    }

    public sealed class UMAShaderPackageReplaceWindow : EditorWindow
    {
        private const string Unmapped = "Select property...";
        private const string Unused = "Not used by shader";
        private UMAShaderPackageUtility.Replacement request;
        private VisualElement mappingRows;
        private HelpBox validation;
        private Button apply;
        private Button update;
        private string folder;

        private void OnEnable()
        {
            EditorApplication.projectChanged += Validate;
            Undo.undoRedoPerformed += Validate;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= Validate;
            Undo.undoRedoPerformed -= Validate;
        }

        private void OnFocus() => Validate();

        public static void Open(Shader source, UMAShaderPackageUtility.Usage usage)
        {
            var window = CreateInstance<UMAShaderPackageReplaceWindow>();
            window.titleContent = new GUIContent("Replace Shader Package");
            window.minSize = new Vector2(590, 480);
            window.request = new UMAShaderPackageUtility.Replacement
            {
                Owner = usage.Owner, SourceShader = source, OwnerSnapshot = EditorJsonUtility.ToJson(usage.Owner)
            };
            window.request.Slots.AddRange(usage.Slots);
            foreach (var channel in usage.Owner.channels ?? Array.Empty<UMAMaterial.MaterialChannel>())
                window.request.Channels.Add(new UMAShaderPackageUtility.ChannelMapping { Property = channel.materialPropertyName, Unused = channel.NonShaderTexture });
            UMAShaderPackageUtility.RefreshShared(window.request);
            window.folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(usage.Owner))?.Replace('\\', '/');
            if (window.folder == null || !window.folder.StartsWith("Assets", StringComparison.Ordinal)) window.folder = "Assets";
            window.ShowUtility();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            UMAShaderPackageWindow.AddStyle(rootVisualElement);
            if (request?.Owner == null)
            {
                rootVisualElement.Add(new HelpBox("Reopen Replace from the shader package list after scripts reload.", HelpBoxMessageType.Info)); return;
            }
            var scroll = new ScrollView(); scroll.AddToClassList("replacement-body"); rootVisualElement.Add(scroll);
            UMAShaderPackageWindow.AddAsset(scroll, "UMAMaterial", request.Owner);
            UMAShaderPackageWindow.AddAsset(scroll, "Current shader", request.SourceShader);
            scroll.Add(new HelpBox("Create copies and replace makes new Materials for this UMAMaterial. Update changes existing Materials for every object using them, and updates channel mappings on all UMAMaterials sharing those Materials. Channel mappings are shared by all its material slots. Property mapping does not change texture packing; the replacement shader must accept the same texture contents.", HelpBoxMessageType.Info));
            scroll.Add(new Label("Material slots to replace") { name = "slotTitle" });
            for (int i = 0; i < UMAShaderPackageUtility.MaterialSlots.Length; i++)
            {
                int slot = i;
                var material = UMAShaderPackageUtility.GetMaterial(request.Owner, slot);
                if (material == null || material.shader != request.SourceShader) continue;
                var toggle = new Toggle(UMAShaderPackageUtility.MaterialSlotLabels[slot] + " — " + material.name) { value = request.Slots.Contains(slot) };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (new[] { request }.Concat(request.Shared).Any(item => item.Owner == null || item.OwnerSnapshot != EditorJsonUtility.ToJson(item.Owner)))
                    { Validate(); return; }
                    if (evt.newValue) { if (!request.Slots.Contains(slot)) request.Slots.Add(slot); }
                    else request.Slots.Remove(slot);
                    UMAShaderPackageUtility.RefreshShared(request);
                    DrawMappings(); Validate();
                });
                scroll.Add(toggle);
            }
            var shaderField = new ObjectField("Replacement shader") { objectType = typeof(Shader), allowSceneObjects = false };
            shaderField.RegisterValueChangedCallback(evt =>
            {
                if (new[] { request }.Concat(request.Shared).Any(item => item.Owner == null || item.OwnerSnapshot != EditorJsonUtility.ToJson(item.Owner)))
                { Validate(); return; }
                request.Shader = evt.newValue as Shader;
                foreach (var item in new[] { request }.Concat(request.Shared))
                    for (int i = 0; i < item.Channels.Count; i++)
                        if (!item.Channels[i].Unused) item.Channels[i].Property = UMAShaderPackageUtility.SuggestProperty(request.Shader, item.Owner.channels[i]);
                DrawMappings(); Validate();
            });
            scroll.Add(shaderField);
            scroll.Add(new Label("Channel property lookup") { name = "mappingTitle" });
            mappingRows = new VisualElement(); scroll.Add(mappingRows); DrawMappings();
            var folderRow = new VisualElement(); folderRow.AddToClassList("asset-row");
            var folderField = new TextField("Save copies in") { value = folder };
            folderField.RegisterValueChangedCallback(evt => { folder = evt.newValue; Validate(); });
            folderRow.Add(folderField);
            folderRow.Add(new Button(() =>
            {
                string chosen = EditorUtility.OpenFolderPanel("Folder for material copies", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(chosen)) folderField.value = FileUtil.GetProjectRelativePath(chosen);
            }) { text = "Browse..." });
            scroll.Add(folderRow);
            scroll.Add(new HelpBox("Undo restores the UMAMaterial references and mappings, and any Materials changed by Update. Created Material assets remain available for reuse or deletion.", HelpBoxMessageType.Info));
            validation = new HelpBox(string.Empty, HelpBoxMessageType.Info); scroll.Add(validation);
            var footer = new VisualElement(); footer.AddToClassList("dialog-actions");
            footer.Add(new Button(Close) { text = "Cancel" });
            update = new Button(() => Apply(true)) { text = "Update" }; footer.Add(update);
            apply = new Button(() => Apply(false)) { text = "Create copies and replace" }; footer.Add(apply); rootVisualElement.Add(footer);
            Validate();
        }

        private void DrawMappings()
        {
            mappingRows.Clear();
            if (request?.Owner == null || request.OwnerSnapshot != EditorJsonUtility.ToJson(request.Owner))
            { mappingRows.Add(new Label("The UMAMaterial changed. Reopen Replace to refresh its channels.")); return; }
            var items = new[] { request }.Concat(request.Shared).ToList();
            if (items.Any(item => item.Owner == null || item.OwnerSnapshot != EditorJsonUtility.ToJson(item.Owner)))
            { mappingRows.Add(new Label("A shared UMAMaterial changed. Reopen Replace to refresh its channels.")); return; }
            mappingRows.Add(new Label("Update affects " + items.Count + " UMAMaterial(s). Shared source properties are mapped together."));
            foreach (var item in items) UMAShaderPackageWindow.AddAsset(mappingRows, "UMAMaterial", item.Owner);
            var groups = items.SelectMany(item => item.Channels.Select((mapping, index) => new { item, mapping, index }))
                .GroupBy(entry => entry.mapping);
            foreach (var group in groups)
            {
                var entry = group.First();
                var channel = entry.item.Owner.channels[entry.index];
                var mapping = group.Key;
                var box = new VisualElement(); box.AddToClassList("mapping-row");
                var label = new Label("Current: " + (channel.NonShaderTexture ? Unused : channel.materialPropertyName) +
                    " ? " + channel.channelType + "\n" + string.Join(", ", group.Select(e => e.item.Owner.name + " channel " + e.index)) +
                    (group.Any(e => e.item == request) ? string.Empty : " (Update only)"));
                label.AddToClassList("wrapped"); box.Add(label);
                var choices = new List<string> { Unmapped, Unused };
                choices.AddRange(UMAShaderPackageUtility.Properties(request.Shader, channel));
                string selected = mapping.Unused ? Unused : choices.Contains(mapping.Property) ? mapping.Property : Unmapped;
                var popup = new PopupField<string>("Replacement property", choices, selected);
                popup.SetEnabled(request.Shader != null);
                popup.RegisterValueChangedCallback(evt =>
                {
                    mapping.Unused = evt.newValue == Unused;
                    mapping.Property = evt.newValue == Unmapped || mapping.Unused ? string.Empty : evt.newValue;
                    Validate();
                });
                box.Add(popup); mappingRows.Add(box);
            }
        }

        private void Validate()
        {
            if (validation == null || apply == null) return;
            string error = UMAShaderPackageUtility.Validate(request);
            string path = (folder ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            if (error == null && (!(path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(path)))
                error = "Choose an existing folder under Assets for the material copies.";
            string updateError = UMAShaderPackageUtility.ValidateUpdate(request);
            update.SetEnabled(updateError == null);
            update.tooltip = updateError ?? "Update the existing Materials and all sharing UMAMaterials' channel mappings.";
            validation.text = error ?? updateError ?? "Ready. Choose Update to edit existing Materials, or Create copies and replace.";
            validation.messageType = error == null && updateError == null ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
            apply.SetEnabled(error == null);
        }

        private void Apply(bool updateExisting)
        {
            try
            {
                var copies = updateExisting ? UMAShaderPackageUtility.Update(request) : UMAShaderPackageUtility.Replace(request, folder);
                if (copies.Count > 0) EditorGUIUtility.PingObject(copies[0]);
                Close();
            }
            catch (Exception exception)
            {
                validation.text = exception.Message; validation.messageType = HelpBoxMessageType.Error;
                Debug.LogException(exception);
            }
        }
    }
}
