using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public enum UmaMoveUnusedTextureStatus
    {
        FoundInOverlay,
        Moved,
        Skipped,
        Error
    }

    public sealed class UmaMoveUnusedTextureResult
    {
        public Texture2D Texture { get; internal set; }
        public string SourcePath { get; internal set; }
        public string DestinationPath { get; internal set; }
        public UmaMoveUnusedTextureStatus Status { get; internal set; }
        public List<string> Details { get; } = new List<string>();
    }

    /// <summary>
    /// Scans indexed UMA overlays and safely moves selected texture assets that
    /// are not referenced by any of them.
    /// </summary>
    public static class UmaMoveUnusedTexturesUtility
    {
        private sealed class TextureCandidate
        {
            public Texture2D Texture;
            public string Name;
            public string Path;
            public string Guid;
        }

        public static bool TryLoadIndexedOverlays(
            out List<OverlayDataAsset> overlays, out string error)
        {
            overlays = new List<OverlayDataAsset>();
            error = string.Empty;

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                error = "The UMA Asset Indexer is unavailable. No textures were moved.";
                return false;
            }

            List<AssetItem> indexedItems;
            try
            {
                indexedItems = indexer.GetAssetItems<OverlayDataAsset>();
            }
            catch (Exception exception)
            {
                error = "The indexed overlay list could not be read. No textures were moved.\n" +
                    exception.Message;
                return false;
            }

            if (indexedItems == null)
            {
                error = "The UMA Asset Indexer returned no overlay list. No textures were moved.";
                return false;
            }

            var missingItems = new List<string>();
            var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < indexedItems.Count; i++)
            {
                AssetItem item = indexedItems[i];
                if (item == null)
                {
                    missingItems.Add("index entry " + i + " is null");
                    continue;
                }

                OverlayDataAsset overlay;
                try
                {
                    overlay = item.GetItem<OverlayDataAsset>();
                }
                catch (Exception exception)
                {
                    missingItems.Add(DescribeAssetItem(item) + " (" + exception.Message + ")");
                    continue;
                }

                if (overlay == null)
                {
                    missingItems.Add(DescribeAssetItem(item));
                    continue;
                }

                string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(overlay));
                if (string.IsNullOrEmpty(path) || loadedPaths.Add(path))
                {
                    overlays.Add(overlay);
                }
            }

            if (missingItems.Count > 0)
            {
                error = "The scan stopped because " + missingItems.Count +
                    " indexed overlay(s) could not be loaded. No textures were moved.\n\n" +
                    string.Join("\n", missingItems);
                overlays.Clear();
                return false;
            }

            overlays.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                GetOverlaySortKey(left), GetOverlaySortKey(right)));
            return true;
        }

        public static List<UmaMoveUnusedTextureResult> ProcessTextures(
            IList<Texture2D> textures,
            string destinationFolder,
            IList<OverlayDataAsset> overlays,
            Action<float, string> progress = null)
        {
            var candidates = BuildCandidates(textures);
            var results = new List<UmaMoveUnusedTextureResult>(candidates.Count);
            string normalizedDestination = NormalizeAssetPath(destinationFolder).TrimEnd('/');

            string validationError = GetValidationError(normalizedDestination, overlays);
            if (!string.IsNullOrEmpty(validationError))
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    results.Add(CreateResult(candidates[i],
                        UmaMoveUnusedTextureStatus.Error, validationError));
                }
                return results;
            }

            var usageByGuid = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            var usageByFallbackName = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < overlays.Count; i++)
            {
                OverlayDataAsset overlay = overlays[i];
                progress?.Invoke(
                    overlays.Count == 0 ? 0.5f : 0.5f * i / overlays.Count,
                    "Scanning overlay " + (i + 1) + " of " + overlays.Count +
                    ": " + overlay.overlayName);
                AddOverlayUsage(overlay, usageByGuid, usageByFallbackName);
            }

            bool movedAny = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                TextureCandidate candidate = candidates[i];
                progress?.Invoke(
                    0.5f + 0.5f * i / Math.Max(1, candidates.Count),
                    "Processing texture " + (i + 1) + " of " + candidates.Count +
                    ": " + candidate.Name);

                if (candidate.Texture == null || string.IsNullOrEmpty(candidate.Path))
                {
                    results.Add(CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Error,
                        "The selection is not a saved Texture2D asset."));
                    continue;
                }

                if (!IsPathUnderAssets(candidate.Path))
                {
                    results.Add(CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Skipped,
                        "Only texture assets under this project's Assets folder can be moved."));
                    continue;
                }

                List<string> usages = CollectUsages(
                    candidate, usageByGuid, usageByFallbackName);
                if (usages.Count > 0)
                {
                    var foundResult = CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.FoundInOverlay,
                        "Kept in place. Found in " + usages.Count +
                        " indexed overlay reference(s):");
                    foundResult.Details.AddRange(usages);
                    results.Add(foundResult);
                    continue;
                }

                string destinationPath = normalizedDestination + "/" +
                    Path.GetFileName(candidate.Path);
                if (string.Equals(candidate.Path, destinationPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Skipped,
                        "The texture is already in the selected destination folder."));
                    continue;
                }

                if (AssetPathExists(destinationPath))
                {
                    var collisionResult = CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Error,
                        "Not moved because an asset already exists at the destination. " +
                        "Existing assets are never overwritten.");
                    collisionResult.DestinationPath = destinationPath;
                    results.Add(collisionResult);
                    continue;
                }

                try
                {
                    string moveError = AssetDatabase.MoveAsset(candidate.Path, destinationPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        var errorResult = CreateResult(candidate,
                            UmaMoveUnusedTextureStatus.Error,
                            "AssetDatabase.MoveAsset failed: " + moveError);
                        errorResult.DestinationPath = destinationPath;
                        results.Add(errorResult);
                        continue;
                    }

                    var movedResult = CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Moved,
                        "Moved from " + candidate.Path + " to " + destinationPath + ".");
                    movedResult.DestinationPath = destinationPath;
                    results.Add(movedResult);
                    movedAny = true;
                }
                catch (Exception exception)
                {
                    var errorResult = CreateResult(candidate,
                        UmaMoveUnusedTextureStatus.Error,
                        "Move failed: " + exception.Message);
                    errorResult.DestinationPath = destinationPath;
                    results.Add(errorResult);
                }
            }

            if (movedAny)
            {
                AssetDatabase.Refresh();
            }
            progress?.Invoke(1f, "Finished processing selected textures.");
            return results;
        }

        private static List<TextureCandidate> BuildCandidates(IList<Texture2D> textures)
        {
            var candidates = new List<TextureCandidate>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (textures == null)
            {
                return candidates;
            }

            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D texture = textures[i];
                string path = texture != null
                    ? NormalizeAssetPath(AssetDatabase.GetAssetPath(texture))
                    : string.Empty;
                string identity = !string.IsNullOrEmpty(path)
                    ? path
                    : "<missing>:" + i;
                if (!seenPaths.Add(identity))
                {
                    continue;
                }

                candidates.Add(new TextureCandidate
                {
                    Texture = texture,
                    Name = texture != null ? texture.name : "<missing texture>",
                    Path = path,
                    Guid = !string.IsNullOrEmpty(path)
                        ? AssetDatabase.AssetPathToGUID(path)
                        : string.Empty
                });
            }

            return candidates;
        }

        private static string GetValidationError(
            string destinationFolder, IList<OverlayDataAsset> overlays)
        {
            if (!IsPathUnderAssets(destinationFolder) ||
                !AssetDatabase.IsValidFolder(destinationFolder))
            {
                return "Select an existing folder under this project's Assets folder. " +
                    "No textures were moved.";
            }

            if (overlays == null)
            {
                return "The indexed overlay list is unavailable. No textures were moved.";
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                if (overlays[i] == null)
                {
                    return "The overlay scan contains a missing overlay at position " + i +
                        ". No textures were moved.";
                }
            }

            return string.Empty;
        }

        private static void AddOverlayUsage(
            OverlayDataAsset overlay,
            Dictionary<string, List<string>> usageByGuid,
            Dictionary<string, List<string>> usageByFallbackName)
        {
            string overlayDescription = DescribeOverlay(overlay);
            Texture[] textureList = overlay.textureList;
            string[] textureNames = overlay.textureNames;
            int textureCount = textureList != null ? textureList.Length : 0;
            int nameCount = textureNames != null ? textureNames.Length : 0;
            int channelCount = Math.Max(textureCount, nameCount);

            for (int i = 0; i < channelCount; i++)
            {
                Texture texture = i < textureCount ? textureList[i] : null;
                if (texture != null)
                {
                    AddTextureUsage(texture,
                        overlayDescription + " — textureList[" + i + "]",
                        usageByGuid);
                    continue;
                }

                string fallbackName = i < nameCount ? textureNames[i] : string.Empty;
                if (!string.IsNullOrWhiteSpace(fallbackName))
                {
                    AddUsage(usageByFallbackName, fallbackName.Trim(),
                        overlayDescription + " — textureNames[" + i +
                        "] (name-only reference)");
                }
            }

            if (overlay.alphaMask != null)
            {
                AddTextureUsage(overlay.alphaMask,
                    overlayDescription + " — alphaMask", usageByGuid);
            }
        }

        private static void AddTextureUsage(
            Texture texture, string detail,
            Dictionary<string, List<string>> usageByGuid)
        {
            string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(texture));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
            {
                AddUsage(usageByGuid, guid, detail);
            }
        }

        private static void AddUsage(
            Dictionary<string, List<string>> usageLookup,
            string key, string detail)
        {
            if (!usageLookup.TryGetValue(key, out List<string> details))
            {
                details = new List<string>();
                usageLookup.Add(key, details);
            }

            if (!details.Contains(detail))
            {
                details.Add(detail);
            }
        }

        private static List<string> CollectUsages(
            TextureCandidate candidate,
            Dictionary<string, List<string>> usageByGuid,
            Dictionary<string, List<string>> usageByFallbackName)
        {
            var usages = new List<string>();
            if (!string.IsNullOrEmpty(candidate.Guid) &&
                usageByGuid.TryGetValue(candidate.Guid, out List<string> guidUsages))
            {
                usages.AddRange(guidUsages);
            }

            if (!string.IsNullOrEmpty(candidate.Name) &&
                usageByFallbackName.TryGetValue(candidate.Name,
                    out List<string> nameUsages))
            {
                for (int i = 0; i < nameUsages.Count; i++)
                {
                    if (!usages.Contains(nameUsages[i]))
                    {
                        usages.Add(nameUsages[i]);
                    }
                }
            }

            usages.Sort(StringComparer.OrdinalIgnoreCase);
            return usages;
        }

        private static UmaMoveUnusedTextureResult CreateResult(
            TextureCandidate candidate,
            UmaMoveUnusedTextureStatus status,
            string detail)
        {
            var result = new UmaMoveUnusedTextureResult
            {
                Texture = candidate.Texture,
                SourcePath = candidate.Path,
                Status = status
            };
            if (!string.IsNullOrEmpty(detail))
            {
                result.Details.Add(detail);
            }
            return result;
        }

        private static bool AssetPathExists(string path)
        {
            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null;
        }

        private static bool IsPathUnderAssets(string path)
        {
            return string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(path) && path.StartsWith(
                    "Assets/", StringComparison.OrdinalIgnoreCase));
        }

        private static string DescribeAssetItem(AssetItem item)
        {
            string name = !string.IsNullOrEmpty(item._Name)
                ? item._Name
                : "<unnamed overlay>";
            string path = !string.IsNullOrEmpty(item._Path)
                ? item._Path
                : "<no path>";
            string guid = !string.IsNullOrEmpty(item._Guid)
                ? item._Guid
                : "<no GUID>";
            return name + " (path: " + path + ", GUID: " + guid + ")";
        }

        private static string DescribeOverlay(OverlayDataAsset overlay)
        {
            string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(overlay));
            return !string.IsNullOrEmpty(path)
                ? overlay.overlayName + " (" + path + ")"
                : overlay.overlayName + " (<unsaved overlay>)";
        }

        private static string GetOverlaySortKey(OverlayDataAsset overlay)
        {
            string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(overlay));
            return path + "\n" + overlay.overlayName;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrEmpty(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimEnd('/');
        }
    }

    internal sealed class UmaMoveUnusedTexturesWindow : EditorWindow
    {
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<UmaMoveUnusedTextureResult> _results =
            new List<UmaMoveUnusedTextureResult>();
        private Vector2 _selectionScroll;
        private Vector2 _resultsScroll;
        private string _destinationFolder = "Assets/UMA/Temp";
        private DefaultAsset _destinationFolderAsset;
        private string _folderError = string.Empty;
        private string _scanError = string.Empty;
        private bool _hasProcessed;
        private int _scannedOverlayCount;

        public static void Open(IList<Texture2D> textures)
        {
            var window = CreateInstance<UmaMoveUnusedTexturesWindow>();
            window.titleContent = new GUIContent("Move Unused Textures");
            window.minSize = new Vector2(760f, 500f);
            window._textures.Clear();
            if (textures != null)
            {
                window._textures.AddRange(textures);
            }

            if (!AssetDatabase.IsValidFolder(window._destinationFolder))
            {
                window._destinationFolder = "Assets";
            }
            window.UpdateDestinationAsset();
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.LabelField("Move Unused Textures", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Each selected Texture2D is checked against every OverlayDataAsset in " +
                "the UMA index. References in textureList, alphaMask, and stripped " +
                "textureNames are treated as used. Unused texture files are moved with " +
                "AssetDatabase so their GUIDs and references are preserved.",
                MessageType.Info);

            DrawDestinationFolder();
            EditorGUILayout.Space(6f);
            DrawSelectedTextures();

            if (!string.IsNullOrEmpty(_scanError))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_scanError, MessageType.Error);
            }

            if (_hasProcessed)
            {
                EditorGUILayout.Space(6f);
                DrawResults();
            }

            EditorGUILayout.Space(8f);
            DrawButtons();
        }

        private void DrawDestinationFolder()
        {
            EditorGUILayout.LabelField("Destination Folder", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_hasProcessed))
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                DefaultAsset selectedFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                    _destinationFolderAsset, typeof(DefaultAsset), false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetDestinationFromObject(selectedFolder);
                }

                if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                {
                    BrowseForDestination();
                }
            }

            EditorGUILayout.SelectableLabel(_destinationFolder,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (!string.IsNullOrEmpty(_folderError))
            {
                EditorGUILayout.HelpBox(_folderError, MessageType.Error);
            }
        }

        private void DrawSelectedTextures()
        {
            EditorGUILayout.LabelField(
                "Selected Texture2D Assets (" + _textures.Count + ")",
                EditorStyles.boldLabel);
            float maxHeight = _hasProcessed ? 120f : 230f;
            _selectionScroll = EditorGUILayout.BeginScrollView(
                _selectionScroll, EditorStyles.helpBox, GUILayout.MaxHeight(maxHeight));
            for (int i = 0; i < _textures.Count; i++)
            {
                Texture2D texture = _textures[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(texture, typeof(Texture2D), false,
                        GUILayout.Width(220f));
                    EditorGUILayout.SelectableLabel(
                        texture != null ? AssetDatabase.GetAssetPath(texture) : "<missing>",
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawResults()
        {
            int found = 0;
            int moved = 0;
            int skipped = 0;
            int errors = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                switch (_results[i].Status)
                {
                    case UmaMoveUnusedTextureStatus.FoundInOverlay: found++; break;
                    case UmaMoveUnusedTextureStatus.Moved: moved++; break;
                    case UmaMoveUnusedTextureStatus.Skipped: skipped++; break;
                    case UmaMoveUnusedTextureStatus.Error: errors++; break;
                }
            }

            EditorGUILayout.LabelField(
                "Results — Moved: " + moved + ", Found in overlays: " + found +
                ", Skipped: " + skipped + ", Errors: " + errors,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Scanned " + _scannedOverlayCount + " indexed overlay(s).");
            _resultsScroll = EditorGUILayout.BeginScrollView(
                _resultsScroll, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _results.Count; i++)
            {
                UmaMoveUnusedTextureResult result = _results[i];
                EditorGUILayout.LabelField(
                    GetStatusLabel(result.Status) + " — " + result.SourcePath,
                    EditorStyles.boldLabel);
                for (int detailIndex = 0;
                    detailIndex < result.Details.Count; detailIndex++)
                {
                    EditorGUILayout.LabelField(
                        "  " + result.Details[detailIndex], EditorStyles.wordWrappedLabel);
                }
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (!_hasProcessed)
                {
                    using (new EditorGUI.DisabledScope(
                        _textures.Count == 0 || !string.IsNullOrEmpty(_folderError)))
                    {
                        if (GUILayout.Button("Process Selected Textures",
                            GUILayout.Width(210f), GUILayout.Height(28f)))
                        {
                            ProcessSelectedTextures();
                        }
                    }

                    if (GUILayout.Button("Cancel", GUILayout.Width(100f),
                        GUILayout.Height(28f)))
                    {
                        Close();
                    }
                }
                else if (GUILayout.Button("Close", GUILayout.Width(100f),
                    GUILayout.Height(28f)))
                {
                    Close();
                }
            }
        }

        private void ProcessSelectedTextures()
        {
            _scanError = string.Empty;
            _results.Clear();
            if (!UmaMoveUnusedTexturesUtility.TryLoadIndexedOverlays(
                out List<OverlayDataAsset> overlays, out string error))
            {
                _scanError = error;
                return;
            }

            try
            {
                _scannedOverlayCount = overlays.Count;
                _results.AddRange(UmaMoveUnusedTexturesUtility.ProcessTextures(
                    _textures, _destinationFolder, overlays,
                    (progress, message) => EditorUtility.DisplayProgressBar(
                        "Move Unused Textures", message, progress)));
                _hasProcessed = true;
            }
            catch (Exception exception)
            {
                _scanError = "Processing stopped unexpectedly. Inspect the project before " +
                    "retrying.\n" + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SetDestinationFromObject(DefaultAsset selectedFolder)
        {
            if (selectedFolder == null)
            {
                _destinationFolderAsset = null;
                _folderError = "Select an existing folder under this project's Assets folder.";
                return;
            }

            string path = AssetDatabase.GetAssetPath(selectedFolder).Replace('\\', '/');
            SetDestination(path);
        }

        private void BrowseForDestination()
        {
            string initialFolder = GetAbsoluteFolderPath(_destinationFolder);
            string selectedFolder = EditorUtility.OpenFolderPanel(
                "Select destination for unused textures", initialFolder, string.Empty);
            if (string.IsNullOrEmpty(selectedFolder))
            {
                return;
            }

            if (!TryGetAssetFolderPath(selectedFolder, out string assetFolder))
            {
                _folderError = "Select a folder under this project's Assets folder.";
                return;
            }

            SetDestination(assetFolder);
        }

        private void SetDestination(string assetFolder)
        {
            string normalized = string.IsNullOrEmpty(assetFolder)
                ? string.Empty
                : assetFolder.Replace('\\', '/').TrimEnd('/');
            if ((!string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase) &&
                 !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) ||
                !AssetDatabase.IsValidFolder(normalized))
            {
                _folderError = "Select an existing folder under this project's Assets folder.";
                return;
            }

            _destinationFolder = normalized;
            _folderError = string.Empty;
            UpdateDestinationAsset();
        }

        private void UpdateDestinationAsset()
        {
            _destinationFolderAsset =
                AssetDatabase.LoadAssetAtPath<DefaultAsset>(_destinationFolder);
            _folderError = _destinationFolderAsset == null
                ? "Select an existing folder under this project's Assets folder."
                : string.Empty;
        }

        private static bool TryGetAssetFolderPath(
            string absoluteFolder, out string assetFolder)
        {
            assetFolder = string.Empty;
            if (string.IsNullOrEmpty(absoluteFolder))
            {
                return false;
            }

            string assetsPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            string normalizedFolder = absoluteFolder.Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalizedFolder, assetsPath,
                StringComparison.OrdinalIgnoreCase))
            {
                assetFolder = "Assets";
                return true;
            }

            if (!normalizedFolder.StartsWith(
                assetsPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            assetFolder = "Assets" + normalizedFolder.Substring(assetsPath.Length);
            return AssetDatabase.IsValidFolder(assetFolder);
        }

        private static string GetAbsoluteFolderPath(string assetFolder)
        {
            if (string.Equals(assetFolder, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Application.dataPath;
            }

            if (!string.IsNullOrEmpty(assetFolder) && assetFolder.StartsWith(
                "Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return Application.dataPath + assetFolder.Substring("Assets".Length);
            }

            return Application.dataPath;
        }

        private static string GetStatusLabel(UmaMoveUnusedTextureStatus status)
        {
            switch (status)
            {
                case UmaMoveUnusedTextureStatus.FoundInOverlay: return "FOUND IN OVERLAY";
                case UmaMoveUnusedTextureStatus.Moved: return "MOVED";
                case UmaMoveUnusedTextureStatus.Skipped: return "SKIPPED";
                default: return "ERROR";
            }
        }
    }
}
