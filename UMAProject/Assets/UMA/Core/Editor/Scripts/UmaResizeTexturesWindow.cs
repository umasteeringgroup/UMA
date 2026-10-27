using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    public sealed class UmaResizeTexturesWindow : EditorWindow
    {
        [SerializeField] private List<string> textureGuids = new List<string>();
        [SerializeField] private int maximumSize = 1024;
        private List<UmaResizeTexturesUtility.Entry> entries;
        private readonly List<Label> rows = new List<Label>();
        private DropdownField sizeField;
        private ScrollView list;
        private Label summary;
        private Button resize, close;
        private bool running;
        private int next, saved, skipped, failed;

        public static void Open(IEnumerable<Texture2D> textures)
        {
            var window = GetWindow<UmaResizeTexturesWindow>(true, "Resize Selected Textures", true);
            window.Stop();
            window.textureGuids = textures.Where(t => t != null).Select(AssetDatabase.GetAssetPath)
                .Select(path => AssetDatabase.AssetPathToGUID(path)).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            window.RefreshPreview();
            window.ShowUtility();
            window.Focus();
        }

        public void CreateGUI()
        {
            minSize = new Vector2(520, 380);
            rootVisualElement.Clear();
            var template = UMAPathUtility.LoadInstallAsset<VisualTreeAsset>("Core/Editor/Scripts/UmaResizeTexturesWindow.uxml");
            if (template == null)
            {
                rootVisualElement.Add(new HelpBox("The Resize Selected dialog files are missing. Reimport UMA Core.", HelpBoxMessageType.Error));
                return;
            }
            template.CloneTree(rootVisualElement);
            sizeField = rootVisualElement.Q<DropdownField>("maximumSize");
            sizeField.choices = new List<string> { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192", "16384" };
            sizeField.SetValueWithoutNotify(maximumSize.ToString());
            sizeField.RegisterValueChangedCallback(change => { maximumSize = int.Parse(change.newValue); RefreshPreview(); });
            list = rootVisualElement.Q<ScrollView>("entries");
            summary = rootVisualElement.Q<Label>("summary");
            resize = rootVisualElement.Q<Button>("resize");
            close = rootVisualElement.Q<Button>("close");
            resize.clicked += StartResize;
            close.clicked += () => { if (running) { Stop(); ShowResults("Stopped"); } else Close(); };
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (list == null || running) return;
            entries = textureGuids.Select(g => UmaResizeTexturesUtility.Inspect(g, maximumSize)).ToList();
            list.Clear(); rows.Clear();
            foreach (var entry in entries)
            {
                var row = new Label(Describe(entry)) { tooltip = entry.Path };
                row.AddToClassList("resize-entry");
                rows.Add(row); list.Add(row);
            }
            int count = entries.Count(e => e.CanResize);
            summary.text = $"{count} to resize; {entries.Count - count} unchanged or skipped.";
            resize.SetEnabled(count > 0);
        }

        private static string Describe(UmaResizeTexturesUtility.Entry entry) =>
            $"{Path.GetFileName(entry.Path)}: imported {entry.CurrentSize.x} × {entry.CurrentSize.y}\n{entry.Message}";

        private void StartResize()
        {
            if (running) return;
            RefreshPreview();
            if (!entries.Any(e => e.CanResize)) return;
            running = true;
            next = saved = skipped = failed = 0;
            sizeField.SetEnabled(false); resize.SetEnabled(false); close.text = "Stop";
            EditorApplication.update += ResizeNext;
        }

        private void ResizeNext()
        {
            if (next >= entries.Count)
            {
                Stop(); ShowResults("Finished"); return;
            }
            var entry = entries[next];
            if (!entry.CanResize) skipped++;
            else
            {
                try
                {
                    entry = UmaResizeTexturesUtility.Resize(entry.Guid, maximumSize);
                    if (entry.CanResize) saved++; else skipped++;
                }
                catch (Exception exception) { entry.Message = "Failed: " + exception.Message; failed++; }
            }
            entries[next] = entry;
            rows[next].text = Describe(entry);
            next++;
            summary.text = $"Processed {next} of {entries.Count}; {saved} resized, {skipped} skipped, {failed} failed.";
        }

        private void ShowResults(string state) => summary.text =
            $"{state}: {saved} importers updated; {skipped} unchanged or skipped; {failed} failed; {entries.Count - next} not processed.";

        private void Stop()
        {
            EditorApplication.update -= ResizeNext;
            running = false;
            sizeField?.SetEnabled(true);
            if (close != null) close.text = "Close";
        }

        private void OnDisable() => Stop();
    }
}
