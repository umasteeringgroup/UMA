using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    /// <summary>
    /// Provides a dockable list of the Markdown documentation shipped with UMA.
    /// </summary>
    public class UMADocumentationWindow : EditorWindow
    {
        private const string WindowTitle = "UMA Documentation";
        private const string PlansWindowTitle = "UMA Plans";
        private const float MinimumWidth = 260f;
        private const float MinimumHeight = 180f;
        private const float FavoriteHitWidth = 30f;
        private const string ConfigurationAssetName = "UMADocumentConfiguration.asset";
        private const string StyleSheetName = "UMADocumentationWindow.uss";

        [SerializeField]
        private bool showPlans;

        private readonly List<string> documentationPaths = new List<string>();
        private ScrollView documentList;
        private Label locationLabel;
        private Label messageLabel;
        private string docsDirectory;
        private string scanError;
        private UMADocumentConfiguration configuration;

        public static void ShowWindow()
        {
            ShowWindow(false);
        }

        public static void ShowPlansWindow()
        {
            ShowWindow(true);
        }

        private static void ShowWindow(bool displayPlans)
        {
            string windowTitle = displayPlans ? PlansWindowTitle : WindowTitle;
            UMADocumentationWindow window = GetWindow<UMADocumentationWindow>(windowTitle);
            window.showPlans = displayPlans;
            window.titleContent = new GUIContent(windowTitle, EditorGUIUtility.IconContent("TextAsset Icon").image);
            window.minSize = new Vector2(MinimumWidth, MinimumHeight);
            window.RefreshDocuments();
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            string windowTitle = showPlans ? PlansWindowTitle : WindowTitle;
            titleContent = new GUIContent(windowTitle, EditorGUIUtility.IconContent("TextAsset Icon").image);
            configuration = LoadOrCreateConfiguration();
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            SortDocuments();
            RefreshView();
        }

        private void OnProjectChange()
        {
            RefreshDocuments();
        }

        private void CreateGUI()
        {
            BuildRoot();
            RefreshDocuments();
        }

        private void BuildRoot()
        {
            rootVisualElement.Clear();
            AddStyleSheet();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1f;

            Toolbar toolbar = new Toolbar();
            rootVisualElement.Add(toolbar);

            locationLabel = new Label();
            locationLabel.style.flexGrow = 1f;
            locationLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            locationLabel.style.paddingLeft = 4f;
            locationLabel.style.overflow = Overflow.Hidden;
            toolbar.Add(locationLabel);

            Button refreshButton = new Button(RefreshDocuments)
            {
                text = "Refresh"
            };
            refreshButton.style.width = 58f;
            refreshButton.style.height = 18f;
            toolbar.Add(refreshButton);

            messageLabel = new Label();
            messageLabel.style.paddingLeft = 8f;
            messageLabel.style.paddingRight = 8f;
            messageLabel.style.paddingTop = 6f;
            messageLabel.style.paddingBottom = 4f;
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(messageLabel);

            documentList = new ScrollView(ScrollViewMode.Vertical);
            documentList.style.flexGrow = 1f;
            documentList.style.paddingLeft = 4f;
            documentList.style.paddingRight = 4f;
            rootVisualElement.Add(documentList);
        }

        private void RefreshDocuments()
        {
            documentationPaths.Clear();
            scanError = null;

            try
            {
                string umaPath = UMAEditorUtilities.FindUMAFullPath();
                string folderName = showPlans ? "Plans" : "Docs";
                docsDirectory = string.IsNullOrEmpty(umaPath) ? null :
                    UMAPathUtility.Normalize(umaPath + "/" + folderName);

                if (!string.IsNullOrEmpty(docsDirectory) && AssetDatabase.IsValidFolder(docsDirectory))
                {
                    string[] documentGuids = AssetDatabase.FindAssets("t:TextAsset", new[] { docsDirectory });
                    for (int fileIndex = 0; fileIndex < documentGuids.Length; fileIndex++)
                    {
                        string documentPath = AssetDatabase.GUIDToAssetPath(documentGuids[fileIndex]);
                        if (string.Equals(Path.GetExtension(documentPath), ".md", StringComparison.OrdinalIgnoreCase))
                            documentationPaths.Add(documentPath);
                    }

                    documentationPaths.Sort((left, right) =>
                        StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right)));

                    EnsureConfigurationEntries();
                    SortDocuments();
                }
            }
            catch (Exception exception)
            {
                docsDirectory = null;
                scanError = exception.Message;
            }

            RefreshView();
        }

        private void RefreshView()
        {
            if (locationLabel == null || documentList == null || messageLabel == null)
            {
                return;
            }

            string folderName = showPlans ? "Plans" : "Docs";
            locationLabel.text = string.IsNullOrEmpty(docsDirectory) ? "UMA " + folderName : docsDirectory.Replace('\\', '/');
            documentList.Clear();

            if (documentationPaths.Count == 0)
            {
                messageLabel.text = !string.IsNullOrEmpty(scanError)
                    ? "Unable to scan the UMA " + folderName + " folder: " + scanError
                    : string.IsNullOrEmpty(docsDirectory) || !Directory.Exists(docsDirectory)
                    ? "The UMA " + folderName + " folder could not be found."
                    : "No Markdown documents were found in the UMA " + folderName + " folder.";
                return;
            }

            int favoriteCount = CountFavorites();
            messageLabel.text = documentationPaths.Count + " document" + (documentationPaths.Count == 1 ? "" : "s") +
                (favoriteCount > 0 ? " · " + favoriteCount + " favorite" + (favoriteCount == 1 ? "" : "s") : "");
            for (int pathIndex = 0; pathIndex < documentationPaths.Count; pathIndex++)
            {
                string documentationPath = documentationPaths[pathIndex];
                string documentGuid = AssetDatabase.AssetPathToGUID(documentationPath);
                UMADocumentConfiguration.DocumentEntry entry = configuration != null
                    ? configuration.Find(documentGuid)
                    : null;

                Button documentButton = new Button
                {
                    text = (entry != null && entry.favorite ? "★  " : "☆  ") +
                        Path.GetFileNameWithoutExtension(documentationPath)
                };
                documentButton.tooltip = "Click the star to favorite. Click the name to open. Drag to reorder.\n" +
                    documentationPath;
                documentButton.AddToClassList("uma-document-open-button");
                // Our manipulator owns pointer capture; the default Clickable consumes these events.
                documentButton.clickable = null;
                documentButton.userData = documentGuid;
                documentButton.AddManipulator(new DocumentItemManipulator(
                    FavoriteHitWidth,
                    () => ToggleFavorite(documentGuid),
                    () => OpenDocument(documentationPath),
                    pointerY => PreviewDocumentDrop(documentGuid, pointerY),
                    pointerY => DropDocument(documentGuid, pointerY),
                    ClearDropHighlight));
                documentButton.RegisterCallback<NavigationSubmitEvent>(_ => OpenDocument(documentationPath));
                documentList.Add(documentButton);
            }
        }

        private void EnsureConfigurationEntries()
        {
            if (configuration == null)
                configuration = LoadOrCreateConfiguration();
            if (configuration == null)
                return;

            bool changed = false;
            int nextOrder = GetNextOrder();
            for (int index = 0; index < documentationPaths.Count; index++)
            {
                string documentGuid = AssetDatabase.AssetPathToGUID(documentationPaths[index]);
                if (string.IsNullOrEmpty(documentGuid) || configuration.Find(documentGuid) != null)
                    continue;

                configuration.GetOrAdd(documentGuid, nextOrder++);
                changed = true;
            }

            if (changed)
                SaveConfiguration();
        }

        private int GetNextOrder()
        {
            int nextOrder = 0;
            if (configuration == null)
                return nextOrder;

            IReadOnlyList<UMADocumentConfiguration.DocumentEntry> entries = configuration.Documents;
            for (int index = 0; index < entries.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = entries[index];
                if (entry != null)
                    nextOrder = Math.Max(nextOrder, entry.order + 1);
            }

            return nextOrder;
        }

        private void SortDocuments()
        {
            documentationPaths.Sort(CompareDocuments);
        }

        private int CompareDocuments(string leftPath, string rightPath)
        {
            UMADocumentConfiguration.DocumentEntry left = GetEntry(leftPath);
            UMADocumentConfiguration.DocumentEntry right = GetEntry(rightPath);
            bool leftFavorite = left != null && left.favorite;
            bool rightFavorite = right != null && right.favorite;

            if (leftFavorite != rightFavorite)
                return leftFavorite ? -1 : 1;

            int orderComparison = (left?.order ?? int.MaxValue).CompareTo(right?.order ?? int.MaxValue);
            return orderComparison != 0
                ? orderComparison
                : StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(leftPath), Path.GetFileName(rightPath));
        }

        private UMADocumentConfiguration.DocumentEntry GetEntry(string documentationPath)
        {
            return configuration?.Find(AssetDatabase.AssetPathToGUID(documentationPath));
        }

        private int CountFavorites()
        {
            int count = 0;
            for (int index = 0; index < documentationPaths.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = GetEntry(documentationPaths[index]);
                if (entry != null && entry.favorite)
                    count++;
            }

            return count;
        }

        private void ToggleFavorite(string documentGuid)
        {
            UMADocumentConfiguration.DocumentEntry entry = configuration?.Find(documentGuid);
            if (entry == null)
                return;

            Undo.RecordObject(configuration, entry.favorite ? "Remove Documentation Favorite" : "Add Documentation Favorite");
            entry.favorite = !entry.favorite;
            entry.order = GetLastOrderInSection(entry.favorite) + 1;
            SaveConfiguration();
            SortDocuments();
            RefreshView();
        }

        private int GetLastOrderInSection(bool favorite)
        {
            int lastOrder = -1;
            for (int index = 0; index < documentationPaths.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = GetEntry(documentationPaths[index]);
                if (entry != null && entry.favorite == favorite)
                    lastOrder = Math.Max(lastOrder, entry.order);
            }

            return lastOrder;
        }

        private void PreviewDocumentDrop(string documentGuid, float pointerY)
        {
            ClearDropHighlight();
            int insertionIndex = GetDropInsertionIndex(documentGuid, pointerY);
            int highlightIndex = Math.Min(insertionIndex, documentationPaths.Count - 1);
            if (highlightIndex >= 0 && highlightIndex < documentList.contentContainer.childCount)
                documentList.contentContainer[highlightIndex].AddToClassList("uma-document-drop-target");
        }

        private void DropDocument(string documentGuid, float pointerY)
        {
            SortDocuments();
            int currentIndex = FindVisibleDocumentIndex(documentGuid);
            if (currentIndex < 0)
                return;

            int insertionIndex = GetDropInsertionIndex(documentGuid, pointerY);
            if (insertionIndex == currentIndex)
            {
                ClearDropHighlight();
                return;
            }

            Undo.RecordObject(configuration, "Reorder UMA Documentation");
            string movedPath = documentationPaths[currentIndex];
            documentationPaths.RemoveAt(currentIndex);
            insertionIndex = Mathf.Clamp(insertionIndex, 0, documentationPaths.Count);
            documentationPaths.Insert(insertionIndex, movedPath);
            RewriteVisibleOrder();
            SaveConfiguration();
            RefreshView();
        }

        private int GetDropInsertionIndex(string documentGuid, float pointerY)
        {
            int currentIndex = FindVisibleDocumentIndex(documentGuid);
            if (currentIndex < 0)
                return 0;

            UMADocumentConfiguration.DocumentEntry current = GetEntry(documentationPaths[currentIndex]);
            bool favorite = current != null && current.favorite;
            int sectionStart = 0;
            int sectionEnd = documentationPaths.Count;

            for (int index = 0; index < documentationPaths.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = GetEntry(documentationPaths[index]);
                bool itemFavorite = entry != null && entry.favorite;
                if (itemFavorite == favorite)
                {
                    sectionStart = index;
                    break;
                }
            }

            for (int index = sectionStart; index < documentationPaths.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = GetEntry(documentationPaths[index]);
                bool itemFavorite = entry != null && entry.favorite;
                if (itemFavorite != favorite)
                {
                    sectionEnd = index;
                    break;
                }
            }

            int insertionIndex = sectionStart;
            for (int index = sectionStart; index < sectionEnd; index++)
            {
                if (index == currentIndex)
                    continue;

                VisualElement item = documentList.contentContainer[index];
                if (pointerY > item.worldBound.center.y)
                    insertionIndex++;
            }

            return insertionIndex;
        }

        private void RewriteVisibleOrder()
        {
            for (int index = 0; index < documentationPaths.Count; index++)
            {
                UMADocumentConfiguration.DocumentEntry entry = GetEntry(documentationPaths[index]);
                if (entry != null)
                    entry.order = index;
            }
        }

        private void ClearDropHighlight()
        {
            if (documentList == null)
                return;

            VisualElement content = documentList.contentContainer;
            for (int index = 0; index < content.childCount; index++)
                content[index].RemoveFromClassList("uma-document-drop-target");
        }

        private int FindVisibleDocumentIndex(string documentGuid)
        {
            for (int index = 0; index < documentationPaths.Count; index++)
            {
                if (string.Equals(AssetDatabase.AssetPathToGUID(documentationPaths[index]), documentGuid,
                    StringComparison.Ordinal))
                    return index;
            }

            return -1;
        }

        private void SaveConfiguration()
        {
            EditorUtility.SetDirty(configuration);
            AssetDatabase.SaveAssetIfDirty(configuration);
        }

        private static UMADocumentConfiguration LoadOrCreateConfiguration()
        {
            string[] configurationGuids = AssetDatabase.FindAssets("t:UMADocumentConfiguration");
            for (int index = 0; index < configurationGuids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(configurationGuids[index]);
                if (string.Equals(Path.GetFileName(path), ConfigurationAssetName, StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadAssetAtPath<UMADocumentConfiguration>(path);
            }

            string umaPath = UMAEditorUtilities.FindUMAFullPath();
            if (string.IsNullOrEmpty(umaPath))
                return null;

            string assetPath = UMAPathUtility.Normalize(umaPath + "/Core/Editor/" + ConfigurationAssetName);
            UMADocumentConfiguration created = CreateInstance<UMADocumentConfiguration>();
            created.name = Path.GetFileNameWithoutExtension(ConfigurationAssetName);
            AssetDatabase.CreateAsset(created, assetPath);
            AssetDatabase.SaveAssetIfDirty(created);
            return created;
        }

        private void AddStyleSheet()
        {
            string[] styleGuids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(StyleSheetName) + " t:StyleSheet");
            for (int index = 0; index < styleGuids.Length; index++)
            {
                string stylePath = AssetDatabase.GUIDToAssetPath(styleGuids[index]);
                if (!string.Equals(Path.GetFileName(stylePath), StyleSheetName, StringComparison.OrdinalIgnoreCase))
                    continue;

                StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
                if (styleSheet != null && !rootVisualElement.styleSheets.Contains(styleSheet))
                    rootVisualElement.styleSheets.Add(styleSheet);
                return;
            }
        }

        private static void OpenDocument(string documentationPath)
        {
            // UMAMarkdownViewer.Open reuses an existing viewer and replaces its current document.
            UMAMarkdownViewer.Open(documentationPath);
        }

        private sealed class DocumentItemManipulator : PointerManipulator
        {
            private const float DragThreshold = 5f;

            private readonly float favoriteHitWidth;
            private readonly Action favoriteAction;
            private readonly Action openAction;
            private readonly Action<float> previewDropAction;
            private readonly Action<float> dropAction;
            private readonly Action clearDropAction;

            private int pointerId = -1;
            private Vector2 pointerStart;
            private bool favoritePressed;
            private bool dragging;

            public DocumentItemManipulator(
                float favoriteHitWidth,
                Action favoriteAction,
                Action openAction,
                Action<float> previewDropAction,
                Action<float> dropAction,
                Action clearDropAction)
            {
                this.favoriteHitWidth = favoriteHitWidth;
                this.favoriteAction = favoriteAction;
                this.openAction = openAction;
                this.previewDropAction = previewDropAction;
                this.dropAction = dropAction;
                this.clearDropAction = clearDropAction;
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnPointerDown);
                target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                target.RegisterCallback<PointerUpEvent>(OnPointerUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
                target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
                target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
            }

            private void OnPointerDown(PointerDownEvent evt)
            {
                if (!CanStartManipulation(evt))
                    return;

                pointerId = evt.pointerId;
                pointerStart = new Vector2(evt.position.x, evt.position.y);
                favoritePressed = evt.localPosition.x <= favoriteHitWidth;
                dragging = false;
                target.CapturePointer(pointerId);
                evt.StopPropagation();
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;

                Vector2 pointerPosition = new Vector2(evt.position.x, evt.position.y);
                float distance = Vector2.Distance(pointerStart, pointerPosition);
                if (!dragging && distance < DragThreshold)
                    return;

                dragging = true;
                float offsetY = pointerPosition.y - pointerStart.y;
                target.style.translate = new StyleTranslate(new Translate(0f, offsetY, 0f));
                target.AddToClassList("uma-document-dragging");
                previewDropAction?.Invoke(pointerPosition.y);
                evt.StopPropagation();
            }

            private void OnPointerUp(PointerUpEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;

                bool wasDragging = dragging;
                Vector2 localPosition = new Vector2(evt.localPosition.x, evt.localPosition.y);
                bool releasedInside = target.ContainsPoint(localPosition);
                bool invokeOpen = releasedInside && !favoritePressed && localPosition.x > favoriteHitWidth;
                bool invokeFavorite = releasedInside && favoritePressed && localPosition.x <= favoriteHitWidth;
                float pointerY = evt.position.y;
                ResetInteraction(evt.pointerId);

                if (wasDragging)
                    dropAction?.Invoke(pointerY);
                else if (invokeFavorite)
                    favoriteAction?.Invoke();
                else if (invokeOpen)
                    openAction?.Invoke();

                evt.StopPropagation();
            }

            private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                if (evt.pointerId != pointerId)
                    return;

                ResetVisualState();
                pointerId = -1;
                favoritePressed = false;
                dragging = false;
            }

            private void ResetInteraction(int releasedPointerId)
            {
                ResetVisualState();
                pointerId = -1;
                favoritePressed = false;
                dragging = false;
                if (target.HasPointerCapture(releasedPointerId))
                    target.ReleasePointer(releasedPointerId);
            }

            private void ResetVisualState()
            {
                target.style.translate = new StyleTranslate(StyleKeyword.Null);
                target.RemoveFromClassList("uma-document-dragging");
                clearDropAction?.Invoke();
            }
        }

    }
}
