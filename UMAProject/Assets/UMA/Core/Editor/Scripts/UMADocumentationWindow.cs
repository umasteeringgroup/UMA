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

        [SerializeField]
        private bool showPlans;

        private readonly List<string> documentationPaths = new List<string>();
        private ScrollView documentList;
        private Label locationLabel;
        private Label messageLabel;
        private string docsDirectory;
        private string scanError;

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

            messageLabel.text = documentationPaths.Count + " document" + (documentationPaths.Count == 1 ? "" : "s");
            for (int pathIndex = 0; pathIndex < documentationPaths.Count; pathIndex++)
            {
                string documentationPath = documentationPaths[pathIndex];
                Button documentButton = new Button(() => OpenDocument(documentationPath))
                {
                    text = Path.GetFileNameWithoutExtension(documentationPath)
                };
                documentButton.tooltip = documentationPath;
                documentButton.style.unityTextAlign = TextAnchor.MiddleLeft;
                documentButton.style.alignSelf = Align.Stretch;
                documentButton.style.marginBottom = 2f;
                documentList.Add(documentButton);
            }
        }

        private static void OpenDocument(string documentationPath)
        {
            // UMAMarkdownViewer.Open reuses an existing viewer and replaces its current document.
            UMAMarkdownViewer.Open(documentationPath);
        }

    }
}
