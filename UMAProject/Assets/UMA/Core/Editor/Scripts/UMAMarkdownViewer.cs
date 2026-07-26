using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    public class UMAMarkdownViewer : EditorWindow
    {
        private const string MenuPath = "Assets/UMA/View Markdown file";
        private const string DocumentationMenuPath = "UMA/View Documentation";
        private const string WindowTitle = "UMA Markdown Viewer";
        private const string WindowPositionPrefsPrefix = "UMA.MarkdownViewer.WindowPosition.";
        private const float ContentPadding = 12f;
        private const float ListIndentWidth = 22f;
        private const float ListMarkerWidth = 42f;
        private const float OutlineDefaultWidth = 220f;
        private const float OutlineMinWidth = 140f;
        private const float OutlineMaxWidth = 640f;
        private const float OutlineIndentPerLevel = 10f;
        private const float OutlineResizeHandleWidth = 7f;
        private const float HeadingScrollPadding = 8f;

        private static readonly Regex HeadingRegex = new Regex("^(#{1,6})[ \\t]+(.+?)\\s*#*\\s*$", RegexOptions.Compiled);
        private static readonly Regex FencedCodeRegex = new Regex("^\\s*(```+|~~~+)\\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex HorizontalRuleRegex = new Regex("^\\s*([-*_])(?:\\s*\\1){2,}\\s*$", RegexOptions.Compiled);
        private static readonly Regex ListItemRegex = new Regex("^(?<indent>[ \\t]*)(?<marker>(?:[-+*])|(?:\\d+[.)]))[ \\t]+(?<body>.*)$", RegexOptions.Compiled);
        private static readonly Regex ImageLineRegex = new Regex("^!\\[(?<alt>.*?)\\]\\((?<path>.*?)(?:\\s+\".*?\")?\\)\\s*$", RegexOptions.Compiled);

        [SerializeField]
        private string assetPath;

        [SerializeField]
        private Vector2 scrollPosition;

        [SerializeField]
        private bool showSource;

        [SerializeField]
        private float zoom = 1f;

        [SerializeField]
        private bool autoReload = true;

        [SerializeField]
        private bool showOutline = true;

        [SerializeField]
        private Vector2 outlineScrollPosition;

        [SerializeField]
        private float outlineWidth = OutlineDefaultWidth;

        private readonly List<MarkdownBlock> blocks = new List<MarkdownBlock>();
        private readonly List<MarkdownBlock> headings = new List<MarkdownBlock>();
        private readonly Dictionary<string, MarkdownBlock> headingLookup = new Dictionary<string, MarkdownBlock>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> headingPositions = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private string markdownText = string.Empty;
        private string loadError;
        private string statusMessage;
        private string pendingAnchorFragment;
        private int pendingAnchorAttempts;
        private DateTime loadedWriteTimeUtc;
        private Styles styles;
        private float styledZoom = -1f;
        private float previewContentWidth;
        private Rect lastSavedPosition;
        private VisualElement contentRoot;
        private VisualElement documentRoot;
        private VisualElement outlineRoot;
        private ScrollView documentScrollView;
        private ScrollView outlineScrollView;
        private VisualElement outlineContainer;
        private Label pathLabel;
        private Label statusLabel;
        private Button previewModeButton;
        private Button sourceModeButton;
        private Button reloadButton;
        private Button pingButton;
        private Button copyPathButton;
        private Button zoomResetButton;
        private Slider zoomSlider;
        private Toggle outlineToggle;
        private Toggle autoReloadToggle;
        private bool resizingOutline;
        private float outlineResizeStartX;
        private float outlineResizeStartWidth;
        private string activeOutlineSlug;
        private readonly Dictionary<string, VisualElement> headingElements = new Dictionary<string, VisualElement>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> outlineButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        [MenuItem(DocumentationMenuPath, priority = 1)]
        public static void ViewDocumentation()
        {
            UMADocumentationWindow.ShowWindow();
        }

        [MenuItem(MenuPath, false, 2000)]
        private static void ViewSelectedMarkdownFile()
        {
            string selectedPath = FindSelectedMarkdownPath();
            if (string.IsNullOrEmpty(selectedPath))
            {
                Debug.LogWarning("Select a Markdown file before opening the UMA Markdown Viewer.");
                return;
            }

            Open(selectedPath);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateViewSelectedMarkdownFile()
        {
            return !string.IsNullOrEmpty(FindSelectedMarkdownPath());
        }

        public static UMAMarkdownViewer Open(string markdownAssetPath)
        {
            bool hadOpenWindow = HasOpenViewerWindow();
            UMAMarkdownViewer window = GetWindow<UMAMarkdownViewer>(WindowTitle);
            window.minSize = new Vector2(420f, 280f);
            if (!hadOpenWindow)
            {
                window.RestoreSavedWindowPosition();
            }

            window.LoadMarkdown(markdownAssetPath);
            window.Show();
            window.Focus();
            return window;
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("TextAsset Icon").image);
            if (!string.IsNullOrEmpty(assetPath))
            {
                Reload();
            }
        }

        private void OnDisable()
        {
            SaveWindowPosition();
        }

        private void OnFocus()
        {
            ReloadIfChanged();
        }

        private void OnProjectChange()
        {
            ReloadIfChanged();
        }

        private void CreateGUI()
        {
            BuildRoot();
            RefreshView();
        }

        private static string FindSelectedMarkdownPath()
        {
            string activePath = GetMarkdownPath(Selection.activeObject);
            if (!string.IsNullOrEmpty(activePath))
            {
                return activePath;
            }

            string[] selectedGuids = Selection.assetGUIDs;
            if (selectedGuids != null)
            {
                for (int guidIndex = 0; guidIndex < selectedGuids.Length; guidIndex++)
                {
                    string guidPath = AssetDatabase.GUIDToAssetPath(selectedGuids[guidIndex]);
                    if (IsMarkdownPath(guidPath))
                    {
                        return guidPath;
                    }
                }
            }

            UnityEngine.Object[] selectedObjects = Selection.objects;
            if (selectedObjects != null)
            {
                for (int objectIndex = 0; objectIndex < selectedObjects.Length; objectIndex++)
                {
                    string objectPath = GetMarkdownPath(selectedObjects[objectIndex]);
                    if (!string.IsNullOrEmpty(objectPath))
                    {
                        return objectPath;
                    }
                }
            }

            return null;
        }

        private static string GetMarkdownPath(UnityEngine.Object selectedObject)
        {
            if (selectedObject == null)
            {
                return null;
            }

            string selectedPath = AssetDatabase.GetAssetPath(selectedObject);
            return IsMarkdownPath(selectedPath) ? selectedPath : null;
        }

        private static bool IsMarkdownPath(string candidatePath)
        {
            if (string.IsNullOrEmpty(candidatePath))
            {
                return false;
            }

            string extension = Path.GetExtension(candidatePath).ToLowerInvariant();
            return extension == ".md" || extension == ".markdown" || extension == ".mdown" || extension == ".mkdn";
        }

        private static bool HasOpenViewerWindow()
        {
            return Resources.FindObjectsOfTypeAll<UMAMarkdownViewer>().Length > 0;
        }

        private void RestoreSavedWindowPosition()
        {
            if (!TryLoadSavedWindowPosition(out Rect savedPosition))
            {
                return;
            }

            savedPosition.width = Mathf.Max(savedPosition.width, minSize.x);
            savedPosition.height = Mathf.Max(savedPosition.height, minSize.y);
            position = savedPosition;
            lastSavedPosition = savedPosition;
        }

        private static bool TryLoadSavedWindowPosition(out Rect savedPosition)
        {
            savedPosition = default;
            if (!EditorPrefs.HasKey(WindowPositionPrefsPrefix + "x")
                || !EditorPrefs.HasKey(WindowPositionPrefsPrefix + "y")
                || !EditorPrefs.HasKey(WindowPositionPrefsPrefix + "width")
                || !EditorPrefs.HasKey(WindowPositionPrefsPrefix + "height"))
            {
                return false;
            }

            savedPosition = new Rect(
                EditorPrefs.GetFloat(WindowPositionPrefsPrefix + "x"),
                EditorPrefs.GetFloat(WindowPositionPrefsPrefix + "y"),
                EditorPrefs.GetFloat(WindowPositionPrefsPrefix + "width"),
                EditorPrefs.GetFloat(WindowPositionPrefsPrefix + "height"));
            return savedPosition.width > 0f && savedPosition.height > 0f;
        }

        private void SaveWindowPositionIfChanged()
        {
            if (Mathf.Abs(position.x - lastSavedPosition.x) < 0.5f
                && Mathf.Abs(position.y - lastSavedPosition.y) < 0.5f
                && Mathf.Abs(position.width - lastSavedPosition.width) < 0.5f
                && Mathf.Abs(position.height - lastSavedPosition.height) < 0.5f)
            {
                return;
            }

            SaveWindowPosition();
        }

        private void SaveWindowPosition()
        {
            if (position.width <= 0f || position.height <= 0f)
            {
                return;
            }

            EditorPrefs.SetFloat(WindowPositionPrefsPrefix + "x", position.x);
            EditorPrefs.SetFloat(WindowPositionPrefsPrefix + "y", position.y);
            EditorPrefs.SetFloat(WindowPositionPrefsPrefix + "width", position.width);
            EditorPrefs.SetFloat(WindowPositionPrefsPrefix + "height", position.height);
            lastSavedPosition = position;
        }

        private void LoadMarkdown(string markdownAssetPath)
        {
            LoadMarkdown(markdownAssetPath, null);
        }

        private void LoadMarkdown(string markdownAssetPath, string anchorFragment)
        {
            if (!IsMarkdownPath(markdownAssetPath))
            {
                loadError = "The selected asset is not a Markdown file.";
                return;
            }

            assetPath = markdownAssetPath.Replace('\\', '/');
            scrollPosition = Vector2.zero;
            pendingAnchorFragment = null;
            pendingAnchorAttempts = 0;
            Reload();
            if (!string.IsNullOrEmpty(anchorFragment))
            {
                ScheduleAnchorScroll(anchorFragment);
            }
        }

        private void Reload()
        {
            loadError = null;
            statusMessage = null;
            markdownText = string.Empty;
            blocks.Clear();
            headings.Clear();
            headingLookup.Clear();
            headingPositions.Clear();

            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    loadError = "Markdown file not found: " + assetPath;
                    return;
                }

                markdownText = File.ReadAllText(fullPath, Encoding.UTF8);
                loadedWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
                blocks.AddRange(ParseBlocks(markdownText));
                RebuildHeadingIndex();
                titleContent = new GUIContent(Path.GetFileName(assetPath), EditorGUIUtility.IconContent("TextAsset Icon").image);
                statusMessage = "Loaded " + assetPath;
                RefreshView();
                Repaint();
            }
            catch (Exception exception)
            {
                loadError = exception.Message;
                RefreshView();
            }
        }

        private void ReloadIfChanged()
        {
            if (!autoReload || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath) && File.GetLastWriteTimeUtc(fullPath) != loadedWriteTimeUtc)
            {
                Reload();
            }
        }

        private void BuildRoot()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                SaveWindowPositionIfChanged();
                ApplyOutlineWidth(outlineWidth);
            });
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(HandleDroppedMarkdownFile);
            rootVisualElement.RegisterCallback<DragPerformEvent>(HandleDroppedMarkdownFile);

            BuildToolbar();

            contentRoot = new VisualElement();
            contentRoot.style.flexGrow = 1f;
            contentRoot.style.flexDirection = FlexDirection.Column;
            rootVisualElement.Add(contentRoot);

            statusLabel = new Label();
            statusLabel.style.paddingLeft = 6f;
            statusLabel.style.paddingRight = 6f;
            statusLabel.style.paddingTop = 2f;
            statusLabel.style.paddingBottom = 2f;
            statusLabel.style.fontSize = 10f;
            statusLabel.style.color = GetSecondaryTextColor();
            rootVisualElement.Add(statusLabel);
        }

        private void BuildToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.style.flexShrink = 0f;
            rootVisualElement.Add(toolbar);

            pathLabel = new Label();
            pathLabel.style.flexGrow = 1f;
            pathLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            pathLabel.style.paddingLeft = 4f;
            pathLabel.style.overflow = Overflow.Hidden;
            toolbar.Add(pathLabel);

            previewModeButton = CreateToolbarButton("Preview", () => SetSourceMode(false), 56f);
            sourceModeButton = CreateToolbarButton("Source", () => SetSourceMode(true), 54f);
            toolbar.Add(previewModeButton);
            toolbar.Add(sourceModeButton);

            reloadButton = CreateToolbarButton("Reload", Reload, 56f);
            pingButton = CreateToolbarButton("Ping", PingCurrentAsset, 42f);
            copyPathButton = CreateToolbarButton("Path", CopyCurrentAssetPath, 42f);
            toolbar.Add(reloadButton);
            toolbar.Add(pingButton);
            toolbar.Add(copyPathButton);

            Label zoomLabel = new Label("Zoom");
            zoomLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            zoomLabel.style.width = 36f;
            toolbar.Add(zoomLabel);

            zoomSlider = new Slider(0.75f, 1.6f);
            zoomSlider.style.width = 90f;
            zoomSlider.SetValueWithoutNotify(zoom);
            zoomSlider.RegisterValueChangedCallback(changeEvent =>
            {
                float newZoom = Mathf.Clamp(changeEvent.newValue, 0.75f, 1.6f);
                if (Mathf.Approximately(zoom, newZoom))
                {
                    return;
                }

                zoom = newZoom;
                RefreshView();
            });
            toolbar.Add(zoomSlider);

            zoomResetButton = CreateToolbarButton(Mathf.RoundToInt(zoom * 100f) + "%", ResetZoom, 48f);
            toolbar.Add(zoomResetButton);

            outlineToggle = new Toggle("Outline");
            outlineToggle.style.width = 74f;
            outlineToggle.SetValueWithoutNotify(showOutline);
            outlineToggle.RegisterValueChangedCallback(changeEvent =>
            {
                showOutline = changeEvent.newValue;
                RefreshView();
            });
            toolbar.Add(outlineToggle);

            autoReloadToggle = new Toggle("Auto");
            autoReloadToggle.style.width = 54f;
            autoReloadToggle.SetValueWithoutNotify(autoReload);
            autoReloadToggle.RegisterValueChangedCallback(changeEvent => autoReload = changeEvent.newValue);
            toolbar.Add(autoReloadToggle);
        }

        private Button CreateToolbarButton(string text, Action clickAction, float width)
        {
            Button button = new Button(clickAction)
            {
                text = text
            };
            button.style.width = width;
            button.style.height = 18f;
            button.style.marginLeft = 1f;
            button.style.marginRight = 1f;
            button.style.paddingLeft = 4f;
            button.style.paddingRight = 4f;
            return button;
        }

        private void RefreshView()
        {
            if (contentRoot == null)
            {
                return;
            }

            UpdateToolbarState();
            SetStatus(statusMessage);
            contentRoot.Clear();
            headingElements.Clear();
            outlineButtons.Clear();

            if (!string.IsNullOrEmpty(loadError))
            {
                contentRoot.Add(CreateMessageBox(loadError, true));
            }

            if (string.IsNullOrEmpty(assetPath))
            {
                BuildEmptyState();
                return;
            }

            if (showSource)
            {
                BuildSourceView();
            }
            else
            {
                BuildPreviewView();
            }
        }

        private void UpdateToolbarState()
        {
            if (pathLabel != null)
            {
                string displayedPath = string.IsNullOrEmpty(assetPath) ? "No Markdown file" : assetPath;
                pathLabel.text = displayedPath;
                pathLabel.tooltip = displayedPath;
            }

            if (previewModeButton != null)
            {
                StyleModeButton(previewModeButton, !showSource);
            }

            if (sourceModeButton != null)
            {
                StyleModeButton(sourceModeButton, showSource);
            }

            bool hasAsset = !string.IsNullOrEmpty(assetPath);
            reloadButton?.SetEnabled(hasAsset);
            pingButton?.SetEnabled(hasAsset);
            copyPathButton?.SetEnabled(hasAsset);

            if (zoomSlider != null)
            {
                zoomSlider.SetValueWithoutNotify(zoom);
            }

            if (zoomResetButton != null)
            {
                zoomResetButton.text = Mathf.RoundToInt(zoom * 100f) + "%";
            }

            outlineToggle?.SetValueWithoutNotify(showOutline);
            autoReloadToggle?.SetValueWithoutNotify(autoReload);
        }

        private void StyleModeButton(Button button, bool selected)
        {
            button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            button.style.backgroundColor = selected ? GetSelectedToolbarColor() : Color.clear;
        }

        private void SetSourceMode(bool sourceMode)
        {
            if (showSource == sourceMode)
            {
                return;
            }

            showSource = sourceMode;
            RefreshView();
        }

        private void ResetZoom()
        {
            zoom = 1f;
            RefreshView();
        }

        private void CopyCurrentAssetPath()
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            EditorGUIUtility.systemCopyBuffer = assetPath;
            SetStatus("Copied path to clipboard.");
        }

        private void SetStatus(string message)
        {
            statusMessage = message;
            if (statusLabel == null)
            {
                return;
            }

            statusLabel.text = string.IsNullOrEmpty(message) ? string.Empty : message;
            statusLabel.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void BuildEmptyState()
        {
            VisualElement emptyState = new VisualElement();
            emptyState.style.flexGrow = 1f;
            emptyState.style.justifyContent = Justify.Center;
            emptyState.style.alignItems = Align.Center;

            Label message = CreateRichTextLabel("Drop a Markdown file here, or right click a Markdown file in the Project window and choose UMA/View Markdown file.", GetParagraphFontSize(), FontStyle.Normal);
            message.style.maxWidth = 560f;
            message.style.paddingLeft = 12f;
            message.style.paddingRight = 12f;
            message.style.paddingTop = 10f;
            message.style.paddingBottom = 10f;
            message.style.borderTopWidth = 1f;
            message.style.borderBottomWidth = 1f;
            message.style.borderLeftWidth = 1f;
            message.style.borderRightWidth = 1f;
            message.style.borderTopColor = GetBorderColor();
            message.style.borderBottomColor = GetBorderColor();
            message.style.borderLeftColor = GetBorderColor();
            message.style.borderRightColor = GetBorderColor();
            message.style.backgroundColor = GetPanelColor();
            emptyState.Add(message);
            contentRoot.Add(emptyState);
        }

        private void BuildSourceView()
        {
            ScrollView sourceScrollView = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            sourceScrollView.style.flexGrow = 1f;
            sourceScrollView.style.paddingLeft = ContentPadding;
            sourceScrollView.style.paddingRight = ContentPadding;
            sourceScrollView.style.paddingTop = ContentPadding;
            sourceScrollView.style.paddingBottom = ContentPadding;

            TextField sourceField = new TextField()
            {
                multiline = true,
                isReadOnly = true,
                value = markdownText
            };
            sourceField.style.flexGrow = 1f;
            sourceField.style.minHeight = Mathf.Max(120f, position.height - 80f);
            sourceField.style.fontSize = GetParagraphFontSize();
            sourceField.style.whiteSpace = WhiteSpace.NoWrap;
            sourceScrollView.Add(sourceField);

            contentRoot.Add(sourceScrollView);
        }

        private void BuildPreviewView()
        {
            VisualElement previewContainer = new VisualElement();
            previewContainer.style.flexDirection = FlexDirection.Row;
            previewContainer.style.flexGrow = 1f;
            contentRoot.Add(previewContainer);

            if (showOutline && headings.Count > 0)
            {
                BuildOutline(previewContainer);
            }

            documentScrollView = new ScrollView(ScrollViewMode.Vertical);
            documentScrollView.style.flexGrow = 1f;
            documentScrollView.verticalScroller.valueChanged += _ => scrollPosition = documentScrollView.scrollOffset;
            previewContainer.Add(documentScrollView);

            documentRoot = new VisualElement();
            documentRoot.style.paddingLeft = ContentPadding;
            documentRoot.style.paddingRight = ContentPadding;
            documentRoot.style.paddingTop = ContentPadding;
            documentRoot.style.paddingBottom = ContentPadding;
            documentRoot.style.flexGrow = 1f;
            documentScrollView.Add(documentRoot);

            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                RenderBlock(blocks[blockIndex], documentRoot, 0);
            }

            documentScrollView.schedule.Execute(() =>
            {
                if (!string.IsNullOrEmpty(pendingAnchorFragment))
                {
                    ApplyPendingAnchorScroll();
                }
                else
                {
                    documentScrollView.scrollOffset = scrollPosition;
                }
            });
        }

        private void BuildOutline(VisualElement parent)
        {
            outlineContainer = new VisualElement();
            ApplyOutlineWidth(outlineWidth);
            outlineContainer.style.flexShrink = 0f;
            outlineContainer.style.borderRightWidth = 1f;
            outlineContainer.style.borderRightColor = GetBorderColor();
            outlineContainer.style.backgroundColor = GetPanelColor();
            parent.Add(outlineContainer);

            VisualElement resizeHandle = CreateOutlineResizeHandle();
            parent.Add(resizeHandle);

            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.height = 24f;
            header.style.paddingLeft = 6f;
            header.style.paddingRight = 4f;
            header.style.borderBottomWidth = 1f;
            header.style.borderBottomColor = GetBorderColor();
            outlineContainer.Add(header);

            Label title = new Label("Outline");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.flexGrow = 1f;
            header.Add(title);

            Button topButton = new Button(() =>
            {
                scrollPosition = Vector2.zero;
                pendingAnchorFragment = null;
                if (documentScrollView != null)
                {
                    documentScrollView.scrollOffset = Vector2.zero;
                }
            })
            {
                text = "Top"
            };
            topButton.style.width = 42f;
            topButton.style.height = 18f;
            header.Add(topButton);

            outlineScrollView = new ScrollView(ScrollViewMode.Vertical);
            outlineScrollView.style.flexGrow = 1f;
            outlineScrollView.verticalScroller.valueChanged += _ => outlineScrollPosition = outlineScrollView.scrollOffset;
            outlineContainer.Add(outlineScrollView);

            outlineRoot = new VisualElement();
            outlineRoot.style.paddingTop = 4f;
            outlineRoot.style.paddingBottom = 4f;
            outlineScrollView.Add(outlineRoot);

            for (int headingIndex = 0; headingIndex < headings.Count; headingIndex++)
            {
                MarkdownBlock heading = headings[headingIndex];
                Button headingButton = CreateOutlineHeadingButton(heading);
                outlineButtons[heading.Slug] = headingButton;
                outlineRoot.Add(headingButton);
            }

            outlineScrollView.schedule.Execute(() => outlineScrollView.scrollOffset = outlineScrollPosition);
        }

        private Button CreateOutlineHeadingButton(MarkdownBlock heading)
        {
            Button headingButton = new Button(() => ScrollToHeading(heading))
            {
                text = heading.PlainText,
                tooltip = heading.Slug
            };

            float indent = Mathf.Max(0, heading.Level - 1) * OutlineIndentPerLevel;
            headingButton.style.alignSelf = Align.Stretch;
            headingButton.style.flexShrink = 0f;
            headingButton.style.minHeight = 20f;
            headingButton.style.marginLeft = 0f;
            headingButton.style.marginRight = 0f;
            headingButton.style.marginTop = 1f;
            headingButton.style.marginBottom = 1f;
            headingButton.style.paddingLeft = indent;
            headingButton.style.paddingRight = 4f;
            headingButton.style.paddingTop = 2f;
            headingButton.style.paddingBottom = 2f;
            headingButton.style.borderTopWidth = 0f;
            headingButton.style.borderBottomWidth = 0f;
            headingButton.style.borderLeftWidth = 0f;
            headingButton.style.borderRightWidth = 0f;
            headingButton.style.backgroundColor = Color.clear;
            headingButton.style.color = GetLinkColor(false);
            headingButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            headingButton.style.unityFontStyleAndWeight = heading.Level <= 2 ? FontStyle.Bold : FontStyle.Normal;
            headingButton.style.fontSize = GetParagraphFontSize();
            headingButton.style.whiteSpace = WhiteSpace.Normal;

            headingButton.RegisterCallback<MouseEnterEvent>(_ =>
            {
                headingButton.style.color = GetLinkColor(true);
                if (!string.Equals(activeOutlineSlug, heading.Slug, StringComparison.OrdinalIgnoreCase))
                {
                    headingButton.style.backgroundColor = GetLinkHoverBackgroundColor();
                }
            });
            headingButton.RegisterCallback<MouseLeaveEvent>(_ => ApplyOutlineButtonState(heading.Slug, headingButton));
            ApplyOutlineButtonState(heading.Slug, headingButton);
            return headingButton;
        }

        private VisualElement CreateOutlineResizeHandle()
        {
            VisualElement resizeHandle = new VisualElement();
            resizeHandle.style.width = OutlineResizeHandleWidth;
            resizeHandle.style.flexShrink = 0f;
            resizeHandle.style.flexDirection = FlexDirection.Row;
            resizeHandle.style.justifyContent = Justify.Center;
            resizeHandle.style.backgroundColor = Color.clear;
            resizeHandle.tooltip = "Resize navigation pane";

            VisualElement guideLine = new VisualElement();
            guideLine.style.width = 1f;
            guideLine.style.alignSelf = Align.Stretch;
            guideLine.style.backgroundColor = GetBorderColor();
            resizeHandle.Add(guideLine);

            resizeHandle.RegisterCallback<MouseDownEvent>(OnOutlineResizeMouseDown);
            resizeHandle.RegisterCallback<MouseMoveEvent>(OnOutlineResizeMouseMove);
            resizeHandle.RegisterCallback<MouseUpEvent>(OnOutlineResizeMouseUp);
            resizeHandle.RegisterCallback<MouseLeaveEvent>(OnOutlineResizeMouseLeave);
            return resizeHandle;
        }

        private void OnOutlineResizeMouseDown(MouseDownEvent mouseDownEvent)
        {
            if (mouseDownEvent.button != 0 || mouseDownEvent.currentTarget is not VisualElement resizeHandle)
            {
                return;
            }

            resizingOutline = true;
            outlineResizeStartX = mouseDownEvent.mousePosition.x;
            outlineResizeStartWidth = outlineWidth;
            resizeHandle.CaptureMouse();
            resizeHandle.style.backgroundColor = GetSelectedToolbarColor();
            mouseDownEvent.StopPropagation();
        }

        private void OnOutlineResizeMouseMove(MouseMoveEvent mouseMoveEvent)
        {
            if (!resizingOutline || mouseMoveEvent.currentTarget is not VisualElement resizeHandle || !resizeHandle.HasMouseCapture())
            {
                return;
            }

            float delta = mouseMoveEvent.mousePosition.x - outlineResizeStartX;
            ApplyOutlineWidth(outlineResizeStartWidth + delta);
            mouseMoveEvent.StopPropagation();
        }

        private void OnOutlineResizeMouseUp(MouseUpEvent mouseUpEvent)
        {
            if (mouseUpEvent.button != 0 || mouseUpEvent.currentTarget is not VisualElement resizeHandle)
            {
                return;
            }

            StopOutlineResize(resizeHandle);
            mouseUpEvent.StopPropagation();
        }

        private void OnOutlineResizeMouseLeave(MouseLeaveEvent mouseLeaveEvent)
        {
            if (mouseLeaveEvent.currentTarget is VisualElement resizeHandle && !resizeHandle.HasMouseCapture())
            {
                StopOutlineResize(resizeHandle);
            }
        }

        private void StopOutlineResize(VisualElement resizeHandle)
        {
            resizingOutline = false;
            if (resizeHandle != null)
            {
                if (resizeHandle.HasMouseCapture())
                {
                    resizeHandle.ReleaseMouse();
                }

                resizeHandle.style.backgroundColor = Color.clear;
            }
        }

        private void ApplyOutlineWidth(float requestedWidth)
        {
            outlineWidth = ClampOutlineWidth(requestedWidth);
            if (outlineContainer == null)
            {
                return;
            }

            outlineContainer.style.width = outlineWidth;
            outlineContainer.style.minWidth = OutlineMinWidth;
            outlineContainer.style.maxWidth = GetMaximumOutlineWidth();
        }

        private float ClampOutlineWidth(float requestedWidth)
        {
            float width = requestedWidth > 0f ? requestedWidth : OutlineDefaultWidth;
            return Mathf.Clamp(width, OutlineMinWidth, GetMaximumOutlineWidth());
        }

        private float GetMaximumOutlineWidth()
        {
            float availableWindowWidth = position.width > 0f ? position.width - 160f : OutlineMaxWidth;
            return Mathf.Max(OutlineMinWidth, Mathf.Min(OutlineMaxWidth, availableWindowWidth));
        }

        private float GetOutlineReservedWidth()
        {
            return showOutline && headings.Count > 0 ? outlineWidth + OutlineResizeHandleWidth : 0f;
        }

        private void RenderBlock(MarkdownBlock block, VisualElement parent, int depth)
        {
            switch (block.Type)
            {
                case MarkdownBlockType.Heading:
                    RenderHeading(block, parent);
                    break;
                case MarkdownBlockType.Paragraph:
                    RenderParagraph(block.Text, parent, 3f, 5f);
                    break;
                case MarkdownBlockType.Code:
                    RenderCodeBlock(block, parent);
                    break;
                case MarkdownBlockType.Quote:
                    RenderQuoteBlock(block, parent, depth);
                    break;
                case MarkdownBlockType.HorizontalRule:
                    RenderHorizontalRule(parent);
                    break;
                case MarkdownBlockType.List:
                    RenderListBlock(block, parent);
                    break;
                case MarkdownBlockType.Table:
                    RenderTableBlock(block, parent);
                    break;
                case MarkdownBlockType.Image:
                    RenderImageBlock(block, parent);
                    break;
            }
        }

        private void RenderHeading(MarkdownBlock block, VisualElement parent)
        {
            Label headingLabel = CreateRichTextLabel(ConvertInlineMarkdown(block.Text, null), GetHeadingFontSize(block.Level), FontStyle.Bold);
            headingLabel.tooltip = string.IsNullOrEmpty(block.Slug) ? block.PlainText : "#" + block.Slug;
            headingLabel.style.marginTop = 4f;
            headingLabel.style.marginBottom = 6f;
            headingLabel.style.paddingTop = 2f;
            headingLabel.style.paddingBottom = 2f;
            parent.Add(headingLabel);

            if (!string.IsNullOrEmpty(block.Slug))
            {
                headingElements[block.Slug] = headingLabel;
            }
        }

        private void RenderParagraph(string markdown, VisualElement parent, float topMargin, float bottomMargin)
        {
            VisualElement paragraphContainer = new VisualElement();
            paragraphContainer.style.marginTop = topMargin;
            paragraphContainer.style.marginBottom = bottomMargin;

            List<LinkTarget> links = new List<LinkTarget>();
            Label paragraphLabel = CreateRichTextLabel(ConvertInlineMarkdown(markdown, links), GetParagraphFontSize(), FontStyle.Normal);
            paragraphContainer.Add(paragraphLabel);
            AddLinkButtons(paragraphContainer, links, 0f);
            parent.Add(paragraphContainer);
        }

        private void RenderCodeBlock(MarkdownBlock block, VisualElement parent)
        {
            VisualElement codeContainer = new VisualElement();
            codeContainer.style.marginTop = 5f;
            codeContainer.style.marginBottom = 6f;
            codeContainer.style.paddingLeft = 6f;
            codeContainer.style.paddingRight = 6f;
            codeContainer.style.paddingTop = 5f;
            codeContainer.style.paddingBottom = 5f;
            codeContainer.style.backgroundColor = GetCodeBackgroundColor();
            codeContainer.style.borderTopWidth = 1f;
            codeContainer.style.borderBottomWidth = 1f;
            codeContainer.style.borderLeftWidth = 1f;
            codeContainer.style.borderRightWidth = 1f;
            codeContainer.style.borderTopColor = GetBorderColor();
            codeContainer.style.borderBottomColor = GetBorderColor();
            codeContainer.style.borderLeftColor = GetBorderColor();
            codeContainer.style.borderRightColor = GetBorderColor();

            if (!string.IsNullOrEmpty(block.Info))
            {
                Label infoLabel = new Label(block.Info);
                infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                infoLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                infoLabel.style.fontSize = Mathf.Max(10f, GetParagraphFontSize() - 2f);
                infoLabel.style.color = GetSecondaryTextColor();
                codeContainer.Add(infoLabel);
            }

            TextField codeField = new TextField()
            {
                multiline = true,
                isReadOnly = true,
                value = block.Text
            };
            codeField.style.fontSize = GetParagraphFontSize();
            codeField.style.whiteSpace = WhiteSpace.NoWrap;
            codeField.style.flexGrow = 1f;
            codeField.style.minHeight = Mathf.Max(28f, (CountLines(block.Text) + 1) * (GetParagraphFontSize() + 4f));
            codeContainer.Add(codeField);
            parent.Add(codeContainer);
        }

        private void RenderQuoteBlock(MarkdownBlock block, VisualElement parent, int depth)
        {
            VisualElement quoteContainer = new VisualElement();
            quoteContainer.style.marginTop = 4f;
            quoteContainer.style.marginBottom = 6f;
            quoteContainer.style.marginLeft = Mathf.Min(depth * 12f, 48f);
            quoteContainer.style.paddingLeft = 12f;
            quoteContainer.style.paddingRight = 8f;
            quoteContainer.style.paddingTop = 6f;
            quoteContainer.style.paddingBottom = 6f;
            quoteContainer.style.borderLeftWidth = 3f;
            quoteContainer.style.borderLeftColor = GetLinkColor(false);
            quoteContainer.style.backgroundColor = GetPanelColor();

            for (int childIndex = 0; childIndex < block.Children.Count; childIndex++)
            {
                RenderBlock(block.Children[childIndex], quoteContainer, depth + 1);
            }

            parent.Add(quoteContainer);
        }

        private void RenderHorizontalRule(VisualElement parent)
        {
            VisualElement rule = new VisualElement();
            rule.style.height = 1f;
            rule.style.marginTop = 8f;
            rule.style.marginBottom = 8f;
            rule.style.backgroundColor = GetBorderColor();
            parent.Add(rule);
        }

        private void RenderListBlock(MarkdownBlock block, VisualElement parent)
        {
            VisualElement listContainer = new VisualElement();
            listContainer.style.marginTop = 2f;
            listContainer.style.marginBottom = 5f;

            for (int itemIndex = 0; itemIndex < block.Items.Count; itemIndex++)
            {
                MarkdownListItem item = block.Items[itemIndex];
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginLeft = item.Level * ListIndentWidth;
                row.style.marginTop = 1f;
                row.style.marginBottom = 1f;

                Label marker = new Label(item.DisplayMarker);
                marker.style.width = ListMarkerWidth;
                marker.style.unityTextAlign = TextAnchor.UpperRight;
                marker.style.paddingRight = 8f;
                marker.style.fontSize = GetParagraphFontSize();
                row.Add(marker);

                VisualElement itemContent = new VisualElement();
                itemContent.style.flexGrow = 1f;
                List<LinkTarget> links = new List<LinkTarget>();
                itemContent.Add(CreateRichTextLabel(ConvertInlineMarkdown(item.Text, links), GetParagraphFontSize(), FontStyle.Normal));
                AddLinkButtons(itemContent, links, 0f);
                row.Add(itemContent);
                listContainer.Add(row);
            }

            parent.Add(listContainer);
        }

        private void RenderTableBlock(MarkdownBlock block, VisualElement parent)
        {
            if (block.Table == null || block.Table.Rows.Count == 0)
            {
                return;
            }

            VisualElement tableContainer = new VisualElement();
            tableContainer.style.marginTop = 6f;
            tableContainer.style.marginBottom = 8f;
            RenderTableRow(block.Table.Headers, block.Table.Alignments, tableContainer, true);
            for (int rowIndex = 0; rowIndex < block.Table.Rows.Count; rowIndex++)
            {
                RenderTableRow(block.Table.Rows[rowIndex], block.Table.Alignments, tableContainer, false);
            }

            parent.Add(tableContainer);
        }

        private void RenderTableRow(List<string> rowCells, List<TableAlignment> alignments, VisualElement parent, bool isHeader)
        {
            int columnCount = Mathf.Max(1, Mathf.Max(rowCells.Count, alignments.Count));
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            for (int cellIndex = 0; cellIndex < columnCount; cellIndex++)
            {
                string cellText = cellIndex < rowCells.Count ? rowCells[cellIndex] : string.Empty;
                TableAlignment alignment = cellIndex < alignments.Count ? alignments[cellIndex] : TableAlignment.Left;
                VisualElement cell = new VisualElement();
                cell.style.flexGrow = 1f;
                cell.style.flexBasis = 0f;
                cell.style.paddingLeft = 6f;
                cell.style.paddingRight = 6f;
                cell.style.paddingTop = 4f;
                cell.style.paddingBottom = 4f;
                cell.style.borderTopWidth = 1f;
                cell.style.borderBottomWidth = 1f;
                cell.style.borderLeftWidth = 1f;
                cell.style.borderRightWidth = 1f;
                cell.style.borderTopColor = GetBorderColor();
                cell.style.borderBottomColor = GetBorderColor();
                cell.style.borderLeftColor = GetBorderColor();
                cell.style.borderRightColor = GetBorderColor();
                cell.style.backgroundColor = isHeader ? GetPanelColor() : Color.clear;

                Label label = CreateRichTextLabel(ConvertInlineMarkdown(cellText, null), GetParagraphFontSize(), isHeader ? FontStyle.Bold : FontStyle.Normal);
                label.style.unityTextAlign = GetTextAnchor(alignment);
                cell.Add(label);
                row.Add(cell);
            }

            parent.Add(row);
        }

        private void RenderImageBlock(MarkdownBlock block, VisualElement parent)
        {
            VisualElement imageContainer = new VisualElement();
            imageContainer.style.marginTop = 6f;
            imageContainer.style.marginBottom = 8f;

            string imageAssetPath = ResolveLinkToAssetPath(block.LinkUrl);
            Texture imageTexture = string.IsNullOrEmpty(imageAssetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(imageAssetPath);
            if (imageTexture == null)
            {
                imageContainer.Add(CreateMessageBox("Image not found: " + block.LinkUrl, true));
                if (IsWebLink(block.LinkUrl))
                {
                    imageContainer.Add(CreateLinkLikeButton(block.LinkUrl, block.LinkUrl, () => Application.OpenURL(block.LinkUrl)));
                }
            }
            else
            {
                UnityEngine.UIElements.Image image = new UnityEngine.UIElements.Image()
                {
                    image = imageTexture,
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.style.maxWidth = Mathf.Max(160f, position.width - GetOutlineReservedWidth() - (ContentPadding * 4f));
                image.style.maxHeight = Mathf.Max(96f, imageTexture.height);
                imageContainer.Add(image);

                if (!string.IsNullOrEmpty(block.Text))
                {
                    Label caption = new Label(block.Text);
                    caption.style.unityTextAlign = TextAnchor.MiddleCenter;
                    caption.style.fontSize = Mathf.Max(10f, GetParagraphFontSize() - 2f);
                    caption.style.color = GetSecondaryTextColor();
                    imageContainer.Add(caption);
                }
            }

            parent.Add(imageContainer);
        }

        private void AddLinkButtons(VisualElement parent, List<LinkTarget> links, float marginLeft)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
            {
                LinkTarget link = links[linkIndex];
                Button linkButton = CreateLinkLikeButton(link.Label, link.Url, () => OpenMarkdownLink(link.Url));
                linkButton.style.marginLeft = marginLeft;
                parent.Add(linkButton);
            }
        }

        private Label CreateRichTextLabel(string text, float fontSize, FontStyle fontStyle)
        {
            Label label = new Label(text ?? string.Empty)
            {
                enableRichText = true
            };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = fontStyle;
            label.style.color = GetTextColor();
            return label;
        }

        private Button CreateLinkLikeButton(string text, string tooltip, Action clickAction)
        {
            Button button = new Button(clickAction)
            {
                text = string.IsNullOrEmpty(text) ? tooltip : text,
                tooltip = tooltip
            };
            button.style.alignSelf = Align.FlexStart;
            button.style.marginTop = 2f;
            button.style.marginBottom = 2f;
            button.style.paddingLeft = 3f;
            button.style.paddingRight = 3f;
            button.style.paddingTop = 1f;
            button.style.paddingBottom = 1f;
            button.style.borderTopWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.backgroundColor = Color.clear;
            button.style.color = GetLinkColor(false);
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = GetParagraphFontSize();
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                button.style.color = GetLinkColor(true);
                button.style.backgroundColor = GetLinkHoverBackgroundColor();
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.color = GetLinkColor(false);
                button.style.backgroundColor = Color.clear;
            });
            return button;
        }

        private VisualElement CreateMessageBox(string message, bool warning)
        {
            Label label = CreateRichTextLabel(message, GetParagraphFontSize(), FontStyle.Normal);
            label.style.marginLeft = ContentPadding;
            label.style.marginRight = ContentPadding;
            label.style.marginTop = ContentPadding;
            label.style.marginBottom = 4f;
            label.style.paddingLeft = 8f;
            label.style.paddingRight = 8f;
            label.style.paddingTop = 6f;
            label.style.paddingBottom = 6f;
            label.style.borderTopWidth = 1f;
            label.style.borderBottomWidth = 1f;
            label.style.borderLeftWidth = 1f;
            label.style.borderRightWidth = 1f;
            label.style.borderTopColor = warning ? GetWarningBorderColor() : GetBorderColor();
            label.style.borderBottomColor = warning ? GetWarningBorderColor() : GetBorderColor();
            label.style.borderLeftColor = warning ? GetWarningBorderColor() : GetBorderColor();
            label.style.borderRightColor = warning ? GetWarningBorderColor() : GetBorderColor();
            label.style.backgroundColor = warning ? GetWarningBackgroundColor() : GetPanelColor();
            return label;
        }

        private float GetParagraphFontSize()
        {
            return Mathf.RoundToInt(13f * Mathf.Clamp(zoom, 0.75f, 1.6f));
        }

        private float GetHeadingFontSize(int level)
        {
            int[] headingSizes = new[] { 26, 22, 19, 17, 15, 14 };
            return Mathf.RoundToInt(headingSizes[Mathf.Clamp(level - 1, 0, headingSizes.Length - 1)] * Mathf.Clamp(zoom, 0.75f, 1.6f));
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            int count = 1;
            for (int characterIndex = 0; characterIndex < text.Length; characterIndex++)
            {
                if (text[characterIndex] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        private static TextAnchor GetTextAnchor(TableAlignment alignment)
        {
            switch (alignment)
            {
                case TableAlignment.Center:
                    return TextAnchor.MiddleCenter;
                case TableAlignment.Right:
                    return TextAnchor.MiddleRight;
                default:
                    return TextAnchor.MiddleLeft;
            }
        }

        private static Color GetTextColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.86f, 0.86f, 0.86f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f);
        }

        private static Color GetSecondaryTextColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f, 1f) : new Color(0.38f, 0.38f, 0.38f, 1f);
        }

        private static Color GetBorderColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.32f, 0.32f, 0.32f, 1f) : new Color(0.65f, 0.65f, 0.65f, 1f);
        }

        private static Color GetPanelColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.20f, 0.20f, 0.20f, 1f) : new Color(0.88f, 0.88f, 0.88f, 1f);
        }

        private static Color GetCodeBackgroundColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f, 1f) : new Color(0.96f, 0.96f, 0.96f, 1f);
        }

        private static Color GetSelectedToolbarColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.26f, 0.36f, 0.48f, 1f) : new Color(0.72f, 0.84f, 1f, 1f);
        }

        private static Color GetWarningBorderColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.85f, 0.62f, 0.24f, 1f) : new Color(0.75f, 0.48f, 0.10f, 1f);
        }

        private static Color GetWarningBackgroundColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.28f, 0.22f, 0.13f, 1f) : new Color(1f, 0.93f, 0.78f, 1f);
        }

        private static Color GetLinkColor(bool hover)
        {
            if (hover)
            {
                return EditorGUIUtility.isProSkin ? new Color(0.78f, 0.9f, 1f, 1f) : new Color(0.0f, 0.18f, 0.55f, 1f);
            }

            return EditorGUIUtility.isProSkin ? new Color(0.44f, 0.68f, 1f, 1f) : new Color(0.05f, 0.28f, 0.68f, 1f);
        }

        private static Color GetLinkHoverBackgroundColor()
        {
            return EditorGUIUtility.isProSkin ? new Color(0.18f, 0.34f, 0.55f, 0.55f) : new Color(0.74f, 0.86f, 1f, 0.8f);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            string displayedPath = string.IsNullOrEmpty(assetPath) ? "No Markdown file" : assetPath;
            GUILayout.Label(new GUIContent(displayedPath, displayedPath), styles.toolbarPath, GUILayout.MinWidth(80f));

            int selectedMode = showSource ? 1 : 0;
            int newMode = GUILayout.Toolbar(selectedMode, new[] { "Preview", "Source" }, EditorStyles.toolbarButton, GUILayout.Width(140f));
            showSource = newMode == 1;

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(assetPath)))
            {
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                {
                    Reload();
                }

                if (GUILayout.Button("Ping", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                {
                    PingCurrentAsset();
                }

                if (GUILayout.Button("Path", EditorStyles.toolbarButton, GUILayout.Width(42f)))
                {
                    EditorGUIUtility.systemCopyBuffer = assetPath;
                    statusMessage = "Copied path to clipboard.";
                }
            }

            GUILayout.Label("Zoom", styles.toolbarLabel, GUILayout.Width(36f));
            zoom = GUILayout.HorizontalSlider(zoom, 0.75f, 1.6f, GUILayout.Width(80f));
            if (GUILayout.Button(Mathf.RoundToInt(zoom * 100f) + "%", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                zoom = 1f;
            }

            showOutline = GUILayout.Toggle(showOutline, "Outline", EditorStyles.toolbarButton, GUILayout.Width(64f));
            autoReload = GUILayout.Toggle(autoReload, "Auto", EditorStyles.toolbarButton, GUILayout.Width(48f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(styles.emptyStateBox);
            GUILayout.Label("Drop a Markdown file here, or right click a Markdown file in the Project window and choose UMA/View Markdown file.", styles.paragraph);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        private void DrawPreview()
        {
            bool drawOutline = showOutline && headings.Count > 0;
            previewContentWidth = Mathf.Max(140f, position.width - (drawOutline ? outlineWidth : 0f) - 24f);

            EditorGUILayout.BeginHorizontal();
            if (drawOutline)
            {
                DrawOutline();
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.BeginVertical(styles.documentPadding);

            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                DrawBlock(blocks[blockIndex], 0f);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();

            ApplyPendingAnchorScroll();
        }

        private void DrawOutline()
        {
            EditorGUILayout.BeginVertical(styles.outlineContainer, GUILayout.Width(outlineWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Outline", styles.outlineTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Top", EditorStyles.toolbarButton, GUILayout.Width(38f)))
            {
                scrollPosition = Vector2.zero;
                pendingAnchorFragment = null;
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            MarkdownBlock activeHeading = FindActiveHeading();
            outlineScrollPosition = EditorGUILayout.BeginScrollView(outlineScrollPosition);
            for (int headingIndex = 0; headingIndex < headings.Count; headingIndex++)
            {
                MarkdownBlock heading = headings[headingIndex];
                GUIStyle outlineStyle = heading == activeHeading ? styles.outlineActiveButton : styles.outlineButton;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(Mathf.Max(0f, heading.Level - 1f) * 10f);
                if (GUILayout.Button(new GUIContent(heading.PlainText, heading.Slug), outlineStyle))
                {
                    ScrollToHeading(heading);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSourceView()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.BeginVertical(styles.documentPadding);
            float availableWidth = Mathf.Max(100f, position.width - (ContentPadding * 2f) - 18f);
            float sourceHeight = Mathf.Max(position.height - 70f, styles.source.CalcHeight(new GUIContent(markdownText), availableWidth));
            EditorGUILayout.SelectableLabel(markdownText, styles.source, GUILayout.MinHeight(sourceHeight));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            if (string.IsNullOrEmpty(statusMessage))
            {
                return;
            }

            EditorGUILayout.LabelField(statusMessage, styles.statusBar);
        }

        private float GetPreviewContentWidth()
        {
            return previewContentWidth > 0f ? previewContentWidth : position.width;
        }

        private MarkdownBlock FindActiveHeading()
        {
            MarkdownBlock activeHeading = null;
            float bestY = float.MinValue;
            float currentY = scrollPosition.y + HeadingScrollPadding;

            for (int headingIndex = 0; headingIndex < headings.Count; headingIndex++)
            {
                MarkdownBlock heading = headings[headingIndex];
                if (!heading.HasRenderPosition)
                {
                    continue;
                }

                if (heading.RenderY <= currentY && heading.RenderY >= bestY)
                {
                    bestY = heading.RenderY;
                    activeHeading = heading;
                }
            }

            return activeHeading ?? (headings.Count > 0 ? headings[0] : null);
        }

        private void ScrollToHeading(MarkdownBlock heading)
        {
            if (heading == null)
            {
                return;
            }

            if (documentScrollView != null && !string.IsNullOrEmpty(heading.Slug) && headingElements.TryGetValue(heading.Slug, out VisualElement headingElement))
            {
                documentScrollView.ScrollTo(headingElement);
                scrollPosition = documentScrollView.scrollOffset;
                pendingAnchorFragment = null;
                SetStatus("Jumped to " + heading.PlainText);
                HighlightOutlineHeading(heading.Slug);
                return;
            }

            ScheduleAnchorScroll(heading.Slug);
        }

        private void ScheduleAnchorScroll(string anchorFragment)
        {
            string normalizedFragment = NormalizeAnchorFragment(anchorFragment);
            if (string.IsNullOrEmpty(normalizedFragment))
            {
                return;
            }

            pendingAnchorFragment = normalizedFragment;
            pendingAnchorAttempts = 0;
            showSource = false;
            RefreshView();
        }

        private void ApplyPendingAnchorScroll()
        {
            if (string.IsNullOrEmpty(pendingAnchorFragment))
            {
                return;
            }

            MarkdownBlock heading = FindHeadingForFragment(pendingAnchorFragment);
            if (heading != null && !string.IsNullOrEmpty(heading.Slug) && documentScrollView != null && headingElements.TryGetValue(heading.Slug, out VisualElement headingElement))
            {
                documentScrollView.ScrollTo(headingElement);
                scrollPosition = documentScrollView.scrollOffset;
                SetStatus("Jumped to " + heading.PlainText);
                pendingAnchorFragment = null;
                HighlightOutlineHeading(heading.Slug);
                return;
            }

            pendingAnchorAttempts++;
            if (pendingAnchorAttempts > 2)
            {
                SetStatus("Anchor not found: #" + pendingAnchorFragment);
                pendingAnchorFragment = null;
            }
            else if (documentScrollView != null)
            {
                documentScrollView.schedule.Execute(ApplyPendingAnchorScroll);
            }
        }

        private void HighlightOutlineHeading(string slug)
        {
            activeOutlineSlug = slug;
            foreach (KeyValuePair<string, Button> outlineButton in outlineButtons)
            {
                ApplyOutlineButtonState(outlineButton.Key, outlineButton.Value);
            }
        }

        private void ApplyOutlineButtonState(string slug, Button button)
        {
            if (button == null)
            {
                return;
            }

            bool active = string.Equals(slug, activeOutlineSlug, StringComparison.OrdinalIgnoreCase);
            button.style.backgroundColor = active ? GetSelectedToolbarColor() : Color.clear;
            button.style.color = GetLinkColor(false);
        }

        private MarkdownBlock FindHeadingForFragment(string anchorFragment)
        {
            string normalizedFragment = NormalizeAnchorFragment(anchorFragment);
            if (string.IsNullOrEmpty(normalizedFragment))
            {
                return null;
            }

            if (headingLookup.TryGetValue(normalizedFragment, out MarkdownBlock directHeading))
            {
                return directHeading;
            }

            string generatedSlug = CreateHeadingSlug(normalizedFragment, false);
            if (headingLookup.TryGetValue(generatedSlug, out MarkdownBlock generatedHeading))
            {
                return generatedHeading;
            }

            string strippedGeneratedSlug = CreateHeadingSlug(normalizedFragment, true);
            return headingLookup.TryGetValue(strippedGeneratedSlug, out MarkdownBlock strippedHeading) ? strippedHeading : null;
        }

        private void DrawBlock(MarkdownBlock block, float indent)
        {
            switch (block.Type)
            {
                case MarkdownBlockType.Heading:
                    DrawHeadingBlock(block, indent);
                    break;
                case MarkdownBlockType.Paragraph:
                    DrawRichLabel(block.Text, styles.paragraph, indent, 3f, 5f);
                    break;
                case MarkdownBlockType.Code:
                    DrawCodeBlock(block, indent);
                    break;
                case MarkdownBlockType.Quote:
                    DrawQuoteBlock(block, indent);
                    break;
                case MarkdownBlockType.HorizontalRule:
                    DrawHorizontalRule(indent);
                    break;
                case MarkdownBlockType.List:
                    DrawListBlock(block, indent);
                    break;
                case MarkdownBlockType.Table:
                    DrawTableBlock(block, indent);
                    break;
                case MarkdownBlockType.Image:
                    DrawImageBlock(block, indent);
                    break;
            }
        }

        private void DrawHeadingBlock(MarkdownBlock block, float indent)
        {
            Rect headingRect = DrawRichLabel(block.Text, styles.headingStyles[Mathf.Clamp(block.Level - 1, 0, 5)], indent, 4f, 6f);
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(block.Slug))
            {
                block.RenderY = headingRect.y;
                block.HasRenderPosition = true;
                headingPositions[block.Slug] = headingRect.y;
            }
        }

        private Rect DrawRichLabel(string markdown, GUIStyle style, float indent, float topSpace, float bottomSpace)
        {
            if (topSpace > 0f)
            {
                GUILayout.Space(topSpace);
            }

            List<LinkTarget> links = new List<LinkTarget>();
            string richText = ConvertInlineMarkdown(markdown, links);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            GUILayout.Label(richText, style);
            Rect labelRect = GUILayoutUtility.GetLastRect();
            EditorGUILayout.EndHorizontal();
            DrawLinks(links, indent);

            if (bottomSpace > 0f)
            {
                GUILayout.Space(bottomSpace);
            }

            return labelRect;
        }

        private void DrawCodeBlock(MarkdownBlock block, float indent)
        {
            GUILayout.Space(5f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            EditorGUILayout.BeginVertical(styles.codeContainer);
            if (!string.IsNullOrEmpty(block.Info))
            {
                GUILayout.Label(block.Info, styles.codeInfo);
            }

            float availableWidth = Mathf.Max(100f, GetPreviewContentWidth() - indent - (ContentPadding * 2f) - 28f);
            float codeHeight = Mathf.Max(styles.code.lineHeight + 8f, styles.code.CalcHeight(new GUIContent(block.Text), availableWidth));
            EditorGUILayout.SelectableLabel(block.Text, styles.code, GUILayout.MinHeight(codeHeight));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void DrawQuoteBlock(MarkdownBlock block, float indent)
        {
            GUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            EditorGUILayout.BeginVertical(styles.quoteBlock);
            for (int childIndex = 0; childIndex < block.Children.Count; childIndex++)
            {
                DrawBlock(block.Children[childIndex], 0f);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void DrawHorizontalRule(float indent)
        {
            GUILayout.Space(8f);
            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1f);
            separatorRect.x += indent;
            separatorRect.width -= indent;
            EditorGUI.DrawRect(separatorRect, styles.ruleColor);
            GUILayout.Space(8f);
        }

        private void DrawListBlock(MarkdownBlock block, float indent)
        {
            GUILayout.Space(2f);
            for (int itemIndex = 0; itemIndex < block.Items.Count; itemIndex++)
            {
                MarkdownListItem item = block.Items[itemIndex];
                float itemIndent = indent + (item.Level * ListIndentWidth);
                List<LinkTarget> links = new List<LinkTarget>();
                string richText = ConvertInlineMarkdown(item.Text, links);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(itemIndent);
                GUILayout.Label(item.DisplayMarker, styles.listMarker, GUILayout.Width(ListMarkerWidth));
                GUILayout.Label(richText, styles.paragraph);
                EditorGUILayout.EndHorizontal();
                DrawLinks(links, itemIndent + ListMarkerWidth);
            }
            GUILayout.Space(5f);
        }

        private void DrawTableBlock(MarkdownBlock block, float indent)
        {
            if (block.Table == null || block.Table.Rows.Count == 0)
            {
                return;
            }

            GUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            EditorGUILayout.BeginVertical();

            DrawTableRow(block.Table.Headers, block.Table.Alignments, styles.tableHeader);
            for (int rowIndex = 0; rowIndex < block.Table.Rows.Count; rowIndex++)
            {
                DrawTableRow(block.Table.Rows[rowIndex], block.Table.Alignments, styles.tableCell);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void DrawTableRow(List<string> rowCells, List<TableAlignment> alignments, GUIStyle baseStyle)
        {
            int columnCount = Mathf.Max(1, Mathf.Max(rowCells.Count, alignments.Count));
            float availableWidth = Mathf.Max(120f, GetPreviewContentWidth() - (ContentPadding * 2f) - 24f);
            float columnWidth = Mathf.Max(70f, availableWidth / columnCount);

            EditorGUILayout.BeginHorizontal();
            for (int cellIndex = 0; cellIndex < columnCount; cellIndex++)
            {
                string cellText = cellIndex < rowCells.Count ? rowCells[cellIndex] : string.Empty;
                GUIStyle cellStyle = GetTableStyle(baseStyle, cellIndex < alignments.Count ? alignments[cellIndex] : TableAlignment.Left);
                GUILayout.Label(ConvertInlineMarkdown(cellText, null), cellStyle, GUILayout.Width(columnWidth));
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawImageBlock(MarkdownBlock block, float indent)
        {
            GUILayout.Space(6f);
            string imageAssetPath = ResolveLinkToAssetPath(block.LinkUrl);
            Texture imageTexture = string.IsNullOrEmpty(imageAssetPath) ? null : AssetDatabase.LoadAssetAtPath<Texture>(imageAssetPath);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(indent);
            EditorGUILayout.BeginVertical();

            if (imageTexture == null)
            {
                EditorGUILayout.HelpBox("Image not found: " + block.LinkUrl, MessageType.Warning);
                if (IsWebLink(block.LinkUrl) && GUILayout.Button(block.LinkUrl, styles.linkButton))
                {
                    Application.OpenURL(block.LinkUrl);
                }
            }
            else
            {
                float maxWidth = Mathf.Max(120f, GetPreviewContentWidth() - indent - (ContentPadding * 2f) - 24f);
                float drawWidth = Mathf.Min(maxWidth, imageTexture.width);
                float drawHeight = Mathf.Max(24f, drawWidth * imageTexture.height / Mathf.Max(1f, imageTexture.width));
                Rect imageRect = GUILayoutUtility.GetRect(drawWidth, drawHeight, GUILayout.ExpandWidth(false));
                GUI.DrawTexture(imageRect, imageTexture, ScaleMode.ScaleToFit);

                if (!string.IsNullOrEmpty(block.Text))
                {
                    GUILayout.Label(block.Text, styles.imageCaption);
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);
        }

        private void DrawLinks(List<LinkTarget> links, float indent)
        {
            if (links == null || links.Count == 0)
            {
                return;
            }

            for (int linkIndex = 0; linkIndex < links.Count; linkIndex++)
            {
                LinkTarget link = links[linkIndex];
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(indent);
                if (GUILayout.Button(new GUIContent(link.Label, link.Url), styles.linkButton))
                {
                    OpenMarkdownLink(link.Url);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private GUIStyle GetTableStyle(GUIStyle baseStyle, TableAlignment alignment)
        {
            GUIStyle tableStyle;
            switch (alignment)
            {
                case TableAlignment.Center:
                    tableStyle = baseStyle == styles.tableHeader ? styles.tableHeaderCenter : styles.tableCellCenter;
                    break;
                case TableAlignment.Right:
                    tableStyle = baseStyle == styles.tableHeader ? styles.tableHeaderRight : styles.tableCellRight;
                    break;
                default:
                    tableStyle = baseStyle;
                    break;
            }

            return tableStyle;
        }

        private void PingCurrentAsset()
        {
            UnityEngine.Object markdownAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (markdownAsset != null)
            {
                Selection.activeObject = markdownAsset;
                EditorGUIUtility.PingObject(markdownAsset);
            }
        }

        private void HandleDroppedMarkdownFile(DragUpdatedEvent dragEvent)
        {
            string droppedMarkdownPath = FindDraggedMarkdownPath();
            if (string.IsNullOrEmpty(droppedMarkdownPath))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dragEvent.StopPropagation();
        }

        private void HandleDroppedMarkdownFile(DragPerformEvent dragEvent)
        {
            string droppedMarkdownPath = FindDraggedMarkdownPath();
            if (string.IsNullOrEmpty(droppedMarkdownPath))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            DragAndDrop.AcceptDrag();
            LoadMarkdown(droppedMarkdownPath);
            dragEvent.StopPropagation();
        }

        private static string FindDraggedMarkdownPath()
        {
            UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
            for (int objectIndex = 0; draggedObjects != null && objectIndex < draggedObjects.Length; objectIndex++)
            {
                string draggedPath = GetMarkdownPath(draggedObjects[objectIndex]);
                if (!string.IsNullOrEmpty(draggedPath))
                {
                    return draggedPath;
                }
            }

            return null;
        }

        private void HandleDroppedMarkdownFile()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null || (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            string droppedMarkdownPath = null;
            UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
            for (int objectIndex = 0; draggedObjects != null && objectIndex < draggedObjects.Length; objectIndex++)
            {
                string draggedPath = GetMarkdownPath(draggedObjects[objectIndex]);
                if (!string.IsNullOrEmpty(draggedPath))
                {
                    droppedMarkdownPath = draggedPath;
                    break;
                }
            }

            if (string.IsNullOrEmpty(droppedMarkdownPath))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                LoadMarkdown(droppedMarkdownPath);
            }

            currentEvent.Use();
        }

        private void OpenMarkdownLink(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            if (IsWebLink(url))
            {
                Application.OpenURL(url);
                return;
            }

            LinkResolution resolvedLink = ResolveMarkdownLink(url);
            if (resolvedLink == null || string.IsNullOrEmpty(resolvedLink.AssetPath))
            {
                SetStatus("Could not resolve link: " + url);
                return;
            }

            if (IsMarkdownPath(resolvedLink.AssetPath))
            {
                if (IsSameAssetPath(assetPath, resolvedLink.AssetPath))
                {
                    if (string.IsNullOrEmpty(resolvedLink.Fragment))
                    {
                        scrollPosition = Vector2.zero;
                        if (documentScrollView != null)
                        {
                            documentScrollView.scrollOffset = Vector2.zero;
                        }
                        SetStatus("Jumped to top.");
                    }
                    else
                    {
                        ScheduleAnchorScroll(resolvedLink.Fragment);
                    }
                }
                else
                {
                    LoadMarkdown(resolvedLink.AssetPath, resolvedLink.Fragment);
                }
                return;
            }

            UnityEngine.Object linkedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedLink.AssetPath);
            if (linkedAsset != null)
            {
                Selection.activeObject = linkedAsset;
                EditorGUIUtility.PingObject(linkedAsset);
                SetStatus("Selected linked asset: " + resolvedLink.AssetPath);
                return;
            }

            string fullPath = Path.GetFullPath(resolvedLink.AssetPath);
            if (File.Exists(fullPath))
            {
                EditorUtility.RevealInFinder(fullPath);
            }
        }

        private string ResolveLinkToAssetPath(string rawUrl)
        {
            LinkResolution resolvedLink = ResolveMarkdownLink(rawUrl);
            return resolvedLink?.AssetPath;
        }

        private LinkResolution ResolveMarkdownLink(string rawUrl)
        {
            if (string.IsNullOrEmpty(rawUrl) || string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            string cleanedUrl = rawUrl.Trim();
            if (IsWebLink(cleanedUrl))
            {
                return null;
            }

            string fragment = null;
            int fragmentIndex = cleanedUrl.IndexOf('#');
            if (fragmentIndex >= 0)
            {
                fragment = NormalizeAnchorFragment(cleanedUrl.Substring(fragmentIndex + 1));
                cleanedUrl = cleanedUrl.Substring(0, fragmentIndex);
            }

            int queryIndex = cleanedUrl.IndexOf('?');
            if (queryIndex >= 0)
            {
                cleanedUrl = cleanedUrl.Substring(0, queryIndex);
            }

            if (string.IsNullOrEmpty(cleanedUrl))
            {
                return new LinkResolution(assetPath, fragment);
            }

            cleanedUrl = Uri.UnescapeDataString(cleanedUrl).Replace('\\', '/');
            if (cleanedUrl.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || cleanedUrl.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return new LinkResolution(cleanedUrl, fragment);
            }

            string baseDirectory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "Assets";
            string combinedAssetPath = NormalizeAssetPath(baseDirectory + "/" + cleanedUrl);
            return new LinkResolution(combinedAssetPath, fragment);
        }

        private static string NormalizeAssetPath(string candidatePath)
        {
            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
            string fullPath = Path.GetFullPath(candidatePath).Replace('\\', '/');
            if (fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(projectRoot.Length + 1);
            }

            return candidatePath.Replace('\\', '/');
        }

        private static bool IsSameAssetPath(string leftPath, string rightPath)
        {
            if (string.IsNullOrEmpty(leftPath) || string.IsNullOrEmpty(rightPath))
            {
                return false;
            }

            return string.Equals(NormalizeAssetPath(leftPath), NormalizeAssetPath(rightPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAnchorFragment(string fragment)
        {
            if (string.IsNullOrEmpty(fragment))
            {
                return null;
            }

            string normalizedFragment = fragment.Trim();
            if (normalizedFragment.StartsWith("#", StringComparison.Ordinal))
            {
                normalizedFragment = normalizedFragment.Substring(1);
            }

            if (string.IsNullOrEmpty(normalizedFragment))
            {
                return null;
            }

            return Uri.UnescapeDataString(normalizedFragment).Trim().ToLowerInvariant();
        }

        private static bool IsWebLink(string url)
        {
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildHeadingIndex()
        {
            headings.Clear();
            headingLookup.Clear();
            headingPositions.Clear();

            Dictionary<string, int> slugCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            CollectHeadings(blocks, slugCounts);
        }

        private void CollectHeadings(List<MarkdownBlock> sourceBlocks, Dictionary<string, int> slugCounts)
        {
            for (int blockIndex = 0; blockIndex < sourceBlocks.Count; blockIndex++)
            {
                MarkdownBlock block = sourceBlocks[blockIndex];
                if (block.Type == MarkdownBlockType.Heading)
                {
                    string baseSlug = CreateHeadingSlug(block.Text, false);
                    string slug = MakeUniqueSlug(baseSlug, slugCounts);
                    block.Slug = slug;
                    block.PlainText = GetPlainInlineText(block.Text);
                    block.HasRenderPosition = false;
                    block.RenderY = 0f;
                    headings.Add(block);
                    AddHeadingLookup(slug, block);

                    string strippedAlias = CreateHeadingSlug(block.Text, true);
                    if (!string.Equals(strippedAlias, baseSlug, StringComparison.OrdinalIgnoreCase))
                    {
                        AddHeadingLookup(strippedAlias, block);
                    }
                }

                if (block.Children.Count > 0)
                {
                    CollectHeadings(block.Children, slugCounts);
                }
            }
        }

        private void AddHeadingLookup(string slug, MarkdownBlock block)
        {
            if (!string.IsNullOrEmpty(slug) && !headingLookup.ContainsKey(slug))
            {
                headingLookup.Add(slug, block);
            }
        }

        private static string MakeUniqueSlug(string baseSlug, Dictionary<string, int> slugCounts)
        {
            if (string.IsNullOrEmpty(baseSlug))
            {
                baseSlug = "section";
            }

            if (!slugCounts.TryGetValue(baseSlug, out int duplicateCount))
            {
                slugCounts.Add(baseSlug, 0);
                return baseSlug;
            }

            duplicateCount++;
            slugCounts[baseSlug] = duplicateCount;
            return baseSlug + "-" + duplicateCount;
        }

        private static string CreateHeadingSlug(string headingText, bool stripHyphens)
        {
            string plainText = GetPlainInlineText(headingText).ToLowerInvariant();
            StringBuilder slugBuilder = new StringBuilder();
            bool previousWasHyphen = false;

            for (int characterIndex = 0; characterIndex < plainText.Length; characterIndex++)
            {
                char character = plainText[characterIndex];
                if (char.IsLetterOrDigit(character))
                {
                    slugBuilder.Append(character);
                    previousWasHyphen = false;
                }
                else if (char.IsWhiteSpace(character))
                {
                    if (!previousWasHyphen && slugBuilder.Length > 0)
                    {
                        slugBuilder.Append('-');
                        previousWasHyphen = true;
                    }
                }
                else if (character == '-' && !stripHyphens)
                {
                    if (!previousWasHyphen && slugBuilder.Length > 0)
                    {
                        slugBuilder.Append('-');
                        previousWasHyphen = true;
                    }
                }
            }

            while (slugBuilder.Length > 0 && slugBuilder[slugBuilder.Length - 1] == '-')
            {
                slugBuilder.Length--;
            }

            return slugBuilder.Length == 0 ? "section" : slugBuilder.ToString();
        }

        private static string GetPlainInlineText(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }

            string text = Regex.Replace(markdown, @"!\[([^\]]*)\]\([^\)]*\)", "$1");
            text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]*\)", "$1");
            text = text.Replace("`", string.Empty)
                .Replace("*", string.Empty)
                .Replace("_", string.Empty)
                .Replace("~", string.Empty)
                .Replace("<", string.Empty)
                .Replace(">", string.Empty)
                .Replace("&lt;", string.Empty)
                .Replace("&gt;", string.Empty);
            return text.Trim();
        }

        private static List<MarkdownBlock> ParseBlocks(string markdown)
        {
            string normalizedMarkdown = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            string[] lines = normalizedMarkdown.Split('\n');
            List<MarkdownBlock> parsedBlocks = new List<MarkdownBlock>();
            int lineIndex = 0;

            while (lineIndex < lines.Length)
            {
                string currentLine = lines[lineIndex];
                string nextLine = lineIndex + 1 < lines.Length ? lines[lineIndex + 1] : null;

                if (string.IsNullOrWhiteSpace(currentLine))
                {
                    lineIndex++;
                    continue;
                }

                if (TryParseSetextHeading(currentLine, nextLine, out MarkdownBlock setextHeading))
                {
                    parsedBlocks.Add(setextHeading);
                    lineIndex += 2;
                    continue;
                }

                if (TryParseHeading(currentLine, out MarkdownBlock headingBlock))
                {
                    parsedBlocks.Add(headingBlock);
                    lineIndex++;
                    continue;
                }

                if (TryParseFencedCode(lines, ref lineIndex, out MarkdownBlock fencedCodeBlock))
                {
                    parsedBlocks.Add(fencedCodeBlock);
                    continue;
                }

                if (HorizontalRuleRegex.IsMatch(currentLine))
                {
                    parsedBlocks.Add(new MarkdownBlock(MarkdownBlockType.HorizontalRule));
                    lineIndex++;
                    continue;
                }

                if (TryParseTable(lines, ref lineIndex, out MarkdownBlock tableBlock))
                {
                    parsedBlocks.Add(tableBlock);
                    continue;
                }

                if (TryParseBlockQuote(lines, ref lineIndex, out MarkdownBlock quoteBlock))
                {
                    parsedBlocks.Add(quoteBlock);
                    continue;
                }

                if (TryParseList(lines, ref lineIndex, out MarkdownBlock listBlock))
                {
                    parsedBlocks.Add(listBlock);
                    continue;
                }

                if (TryParseIndentedCode(lines, ref lineIndex, out MarkdownBlock indentedCodeBlock))
                {
                    parsedBlocks.Add(indentedCodeBlock);
                    continue;
                }

                if (TryParseImage(currentLine, out MarkdownBlock imageBlock))
                {
                    parsedBlocks.Add(imageBlock);
                    lineIndex++;
                    continue;
                }

                parsedBlocks.Add(ParseParagraph(lines, ref lineIndex));
            }

            return parsedBlocks;
        }

        private static bool TryParseHeading(string line, out MarkdownBlock block)
        {
            Match match = HeadingRegex.Match(line);
            if (!match.Success)
            {
                block = null;
                return false;
            }

            block = new MarkdownBlock(MarkdownBlockType.Heading)
            {
                Level = Mathf.Clamp(match.Groups[1].Value.Length, 1, 6),
                Text = match.Groups[2].Value.Trim()
            };
            return true;
        }

        private static bool TryParseSetextHeading(string line, string underline, out MarkdownBlock block)
        {
            block = null;
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(underline))
            {
                return false;
            }

            string trimmedUnderline = underline.Trim();
            if (trimmedUnderline.Length < 3)
            {
                return false;
            }

            bool allEquals = true;
            bool allHyphens = true;
            for (int characterIndex = 0; characterIndex < trimmedUnderline.Length; characterIndex++)
            {
                allEquals &= trimmedUnderline[characterIndex] == '=';
                allHyphens &= trimmedUnderline[characterIndex] == '-';
            }

            if (!allEquals && !allHyphens)
            {
                return false;
            }

            block = new MarkdownBlock(MarkdownBlockType.Heading)
            {
                Level = allEquals ? 1 : 2,
                Text = line.Trim()
            };
            return true;
        }

        private static bool TryParseFencedCode(string[] lines, ref int lineIndex, out MarkdownBlock block)
        {
            Match match = FencedCodeRegex.Match(lines[lineIndex]);
            if (!match.Success)
            {
                block = null;
                return false;
            }

            string fence = match.Groups[1].Value;
            char fenceCharacter = fence[0];
            int minimumFenceLength = fence.Length;
            string info = match.Groups[2].Value.Trim();
            StringBuilder codeBuilder = new StringBuilder();
            lineIndex++;

            while (lineIndex < lines.Length)
            {
                string candidateLine = lines[lineIndex];
                string trimmedCandidate = candidateLine.Trim();
                if (trimmedCandidate.Length >= minimumFenceLength && AllSameFence(trimmedCandidate, fenceCharacter, minimumFenceLength))
                {
                    lineIndex++;
                    break;
                }

                codeBuilder.AppendLine(candidateLine);
                lineIndex++;
            }

            block = new MarkdownBlock(MarkdownBlockType.Code)
            {
                Info = info,
                Text = TrimSingleTrailingNewline(codeBuilder.ToString())
            };
            return true;
        }

        private static bool AllSameFence(string value, char fenceCharacter, int minimumFenceLength)
        {
            int fenceCount = 0;
            for (int characterIndex = 0; characterIndex < value.Length; characterIndex++)
            {
                if (value[characterIndex] != fenceCharacter)
                {
                    return false;
                }

                fenceCount++;
            }

            return fenceCount >= minimumFenceLength;
        }

        private static bool TryParseBlockQuote(string[] lines, ref int lineIndex, out MarkdownBlock block)
        {
            if (!IsBlockQuoteLine(lines[lineIndex]))
            {
                block = null;
                return false;
            }

            StringBuilder quoteBuilder = new StringBuilder();
            while (lineIndex < lines.Length && (IsBlockQuoteLine(lines[lineIndex]) || string.IsNullOrWhiteSpace(lines[lineIndex])))
            {
                string strippedLine = StripBlockQuotePrefix(lines[lineIndex]);
                quoteBuilder.AppendLine(strippedLine);
                lineIndex++;
            }

            block = new MarkdownBlock(MarkdownBlockType.Quote);
            block.Children.AddRange(ParseBlocks(quoteBuilder.ToString()));
            return true;
        }

        private static bool IsBlockQuoteLine(string line)
        {
            return line.TrimStart().StartsWith(">", StringComparison.Ordinal);
        }

        private static string StripBlockQuotePrefix(string line)
        {
            string trimmedStart = line.TrimStart();
            if (!trimmedStart.StartsWith(">", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            string stripped = trimmedStart.Substring(1);
            return stripped.StartsWith(" ", StringComparison.Ordinal) ? stripped.Substring(1) : stripped;
        }

        private static bool TryParseList(string[] lines, ref int lineIndex, out MarkdownBlock block)
        {
            if (!TryParseListItem(lines[lineIndex], out MarkdownListItem firstItem))
            {
                block = null;
                return false;
            }

            block = new MarkdownBlock(MarkdownBlockType.List);
            block.Items.Add(firstItem);
            lineIndex++;

            while (lineIndex < lines.Length)
            {
                if (TryParseListItem(lines[lineIndex], out MarkdownListItem listItem))
                {
                    block.Items.Add(listItem);
                    lineIndex++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                {
                    int lookAheadIndex = lineIndex + 1;
                    if (lookAheadIndex < lines.Length && TryParseListItem(lines[lookAheadIndex], out MarkdownListItem nextItem))
                    {
                        block.Items.Add(nextItem);
                        lineIndex = lookAheadIndex + 1;
                        continue;
                    }
                }

                if (block.Items.Count > 0 && IsListContinuation(lines[lineIndex]))
                {
                    MarkdownListItem lastItem = block.Items[block.Items.Count - 1];
                    lastItem.Text = lastItem.Text + " " + lines[lineIndex].Trim();
                    lineIndex++;
                    continue;
                }

                break;
            }

            return true;
        }

        private static bool TryParseListItem(string line, out MarkdownListItem item)
        {
            Match match = ListItemRegex.Match(line);
            if (!match.Success)
            {
                item = null;
                return false;
            }

            string marker = match.Groups["marker"].Value;
            string body = match.Groups["body"].Value.Trim();
            int indentLevel = Mathf.Clamp(CountIndent(match.Groups["indent"].Value) / 2, 0, 12);
            bool ordered = char.IsDigit(marker[0]);
            bool? taskState = null;

            if (body.StartsWith("[ ] ", StringComparison.Ordinal))
            {
                taskState = false;
                body = body.Substring(4).TrimStart();
            }
            else if (body.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase))
            {
                taskState = true;
                body = body.Substring(4).TrimStart();
            }

            item = new MarkdownListItem()
            {
                Level = indentLevel,
                Ordered = ordered,
                Marker = marker,
                Text = body,
                TaskState = taskState
            };
            return true;
        }

        private static int CountIndent(string indent)
        {
            int count = 0;
            for (int characterIndex = 0; characterIndex < indent.Length; characterIndex++)
            {
                count += indent[characterIndex] == '\t' ? 4 : 1;
            }

            return count;
        }

        private static bool IsListContinuation(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            string trimmedStart = line.TrimStart();
            return line.Length != trimmedStart.Length && !IsBlockQuoteLine(line) && !HorizontalRuleRegex.IsMatch(line);
        }

        private static bool TryParseIndentedCode(string[] lines, ref int lineIndex, out MarkdownBlock block)
        {
            if (!IsIndentedCodeLine(lines[lineIndex]))
            {
                block = null;
                return false;
            }

            StringBuilder codeBuilder = new StringBuilder();
            while (lineIndex < lines.Length && (IsIndentedCodeLine(lines[lineIndex]) || string.IsNullOrWhiteSpace(lines[lineIndex])))
            {
                string line = lines[lineIndex];
                if (line.StartsWith("    ", StringComparison.Ordinal))
                {
                    codeBuilder.AppendLine(line.Substring(4));
                }
                else if (line.StartsWith("\t", StringComparison.Ordinal))
                {
                    codeBuilder.AppendLine(line.Substring(1));
                }
                else
                {
                    codeBuilder.AppendLine();
                }

                lineIndex++;
            }

            block = new MarkdownBlock(MarkdownBlockType.Code)
            {
                Text = TrimSingleTrailingNewline(codeBuilder.ToString())
            };
            return true;
        }

        private static bool IsIndentedCodeLine(string line)
        {
            return line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal);
        }

        private static bool TryParseImage(string line, out MarkdownBlock block)
        {
            Match match = ImageLineRegex.Match(line.Trim());
            if (!match.Success)
            {
                block = null;
                return false;
            }

            block = new MarkdownBlock(MarkdownBlockType.Image)
            {
                Text = match.Groups["alt"].Value,
                LinkUrl = match.Groups["path"].Value.Trim()
            };
            return true;
        }

        private static bool TryParseTable(string[] lines, ref int lineIndex, out MarkdownBlock block)
        {
            block = null;
            if (lineIndex + 1 >= lines.Length || !lines[lineIndex].Contains("|") || !IsTableSeparator(lines[lineIndex + 1]))
            {
                return false;
            }

            MarkdownTable table = new MarkdownTable();
            table.Headers.AddRange(SplitTableRow(lines[lineIndex]));
            table.Alignments.AddRange(ParseTableAlignments(lines[lineIndex + 1]));
            lineIndex += 2;

            while (lineIndex < lines.Length && lines[lineIndex].Contains("|") && !string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                table.Rows.Add(SplitTableRow(lines[lineIndex]));
                lineIndex++;
            }

            block = new MarkdownBlock(MarkdownBlockType.Table)
            {
                Table = table
            };
            return true;
        }

        private static bool IsTableSeparator(string line)
        {
            List<string> cells = SplitTableRow(line);
            if (cells.Count < 2)
            {
                return false;
            }

            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                string cell = cells[cellIndex].Trim();
                if (cell.Length < 3)
                {
                    return false;
                }

                int dashCount = 0;
                for (int characterIndex = 0; characterIndex < cell.Length; characterIndex++)
                {
                    char character = cell[characterIndex];
                    if (character == '-')
                    {
                        dashCount++;
                    }
                    else if (character != ':')
                    {
                        return false;
                    }
                }

                if (dashCount < 3)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> SplitTableRow(string line)
        {
            string trimmedLine = line.Trim();
            if (trimmedLine.StartsWith("|", StringComparison.Ordinal))
            {
                trimmedLine = trimmedLine.Substring(1);
            }

            if (trimmedLine.EndsWith("|", StringComparison.Ordinal))
            {
                trimmedLine = trimmedLine.Substring(0, trimmedLine.Length - 1);
            }

            List<string> cells = new List<string>();
            StringBuilder cellBuilder = new StringBuilder();
            bool escaped = false;

            for (int characterIndex = 0; characterIndex < trimmedLine.Length; characterIndex++)
            {
                char character = trimmedLine[characterIndex];
                if (escaped)
                {
                    cellBuilder.Append(character);
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '|')
                {
                    cells.Add(cellBuilder.ToString().Trim());
                    cellBuilder.Length = 0;
                    continue;
                }

                cellBuilder.Append(character);
            }

            cells.Add(cellBuilder.ToString().Trim());
            return cells;
        }

        private static List<TableAlignment> ParseTableAlignments(string separatorLine)
        {
            List<string> cells = SplitTableRow(separatorLine);
            List<TableAlignment> alignments = new List<TableAlignment>();
            for (int cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                string cell = cells[cellIndex].Trim();
                bool left = cell.StartsWith(":", StringComparison.Ordinal);
                bool right = cell.EndsWith(":", StringComparison.Ordinal);

                if (left && right)
                {
                    alignments.Add(TableAlignment.Center);
                }
                else if (right)
                {
                    alignments.Add(TableAlignment.Right);
                }
                else
                {
                    alignments.Add(TableAlignment.Left);
                }
            }

            return alignments;
        }

        private static MarkdownBlock ParseParagraph(string[] lines, ref int lineIndex)
        {
            StringBuilder paragraphBuilder = new StringBuilder();
            while (lineIndex < lines.Length && !string.IsNullOrWhiteSpace(lines[lineIndex]))
            {
                string currentLine = lines[lineIndex];
                string nextLine = lineIndex + 1 < lines.Length ? lines[lineIndex + 1] : null;
                if (paragraphBuilder.Length > 0 && IsBlockStart(currentLine, nextLine))
                {
                    break;
                }

                if (paragraphBuilder.Length > 0)
                {
                    paragraphBuilder.Append(HasHardLineBreak(paragraphBuilder) ? "\n" : " ");
                }

                paragraphBuilder.Append(currentLine.Trim());
                lineIndex++;
            }

            return new MarkdownBlock(MarkdownBlockType.Paragraph)
            {
                Text = paragraphBuilder.ToString()
            };
        }

        private static bool IsBlockStart(string line, string nextLine)
        {
            return HeadingRegex.IsMatch(line)
                || FencedCodeRegex.IsMatch(line)
                || HorizontalRuleRegex.IsMatch(line)
                || IsBlockQuoteLine(line)
                || ListItemRegex.IsMatch(line)
                || IsIndentedCodeLine(line)
                || ImageLineRegex.IsMatch(line.Trim())
                || (line.Contains("|") && !string.IsNullOrEmpty(nextLine) && IsTableSeparator(nextLine));
        }

        private static bool HasHardLineBreak(StringBuilder paragraphBuilder)
        {
            if (paragraphBuilder.Length == 0)
            {
                return false;
            }

            if (paragraphBuilder[paragraphBuilder.Length - 1] == '\\')
            {
                paragraphBuilder.Length -= 1;
                return true;
            }

            if (paragraphBuilder.Length >= 2 && paragraphBuilder[paragraphBuilder.Length - 1] == ' ' && paragraphBuilder[paragraphBuilder.Length - 2] == ' ')
            {
                paragraphBuilder.Length -= 2;
                return true;
            }

            return false;
        }

        private static string ConvertInlineMarkdown(string text, List<LinkTarget> links)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            StringBuilder output = new StringBuilder();
            int characterIndex = 0;
            while (characterIndex < text.Length)
            {
                if (text[characterIndex] == '\\'
                    && characterIndex + 1 < text.Length
                    && IsMarkdownEscapableCharacter(text[characterIndex + 1]))
                {
                    output.Append(EscapeRichText(text[characterIndex + 1].ToString()));
                    characterIndex += 2;
                    continue;
                }

                if (TryConsumeInlineCode(text, ref characterIndex, output))
                {
                    continue;
                }

                if (TryConsumeInlineLink(text, ref characterIndex, output, links))
                {
                    continue;
                }

                if (TryConsumePairedMarker(text, ref characterIndex, "***", "<b><i>", "</i></b>", output, links)
                    || TryConsumePairedMarker(text, ref characterIndex, "___", "<b><i>", "</i></b>", output, links)
                    || TryConsumePairedMarker(text, ref characterIndex, "**", "<b>", "</b>", output, links)
                    || TryConsumePairedMarker(text, ref characterIndex, "__", "<b>", "</b>", output, links)
                    || TryConsumePairedMarker(text, ref characterIndex, "~~", "<color=#888888>", "</color>", output, links)
                    || TryConsumeItalicMarker(text, ref characterIndex, '*', output, links)
                    || TryConsumeItalicMarker(text, ref characterIndex, '_', output, links))
                {
                    continue;
                }

                output.Append(EscapeRichText(text[characterIndex].ToString()));
                characterIndex++;
            }

            return output.ToString();
        }

        private static bool TryConsumeInlineCode(string text, ref int characterIndex, StringBuilder output)
        {
            if (text[characterIndex] != '`')
            {
                return false;
            }

            int closingIndex = text.IndexOf('`', characterIndex + 1);
            if (closingIndex < 0)
            {
                return false;
            }

            string codeText = text.Substring(characterIndex + 1, closingIndex - characterIndex - 1);
            output.Append("<color=#c7254e>");
            output.Append(EscapeRichText(codeText));
            output.Append("</color>");
            characterIndex = closingIndex + 1;
            return true;
        }

        private static bool TryConsumeInlineLink(string text, ref int characterIndex, StringBuilder output, List<LinkTarget> links)
        {
            if (text[characterIndex] == '<')
            {
                int closingIndex = text.IndexOf('>', characterIndex + 1);
                if (closingIndex > characterIndex)
                {
                    string autoLink = text.Substring(characterIndex + 1, closingIndex - characterIndex - 1);
                    if (IsWebLink(autoLink))
                    {
                        links?.Add(new LinkTarget(autoLink, autoLink));
                        output.Append("<color=#2f75c0><b>");
                        output.Append(EscapeRichText(autoLink));
                        output.Append("</b></color>");
                        characterIndex = closingIndex + 1;
                        return true;
                    }
                }
            }

            if (text[characterIndex] != '[' || (characterIndex > 0 && text[characterIndex - 1] == '!'))
            {
                return false;
            }

            int labelEnd = text.IndexOf(']', characterIndex + 1);
            if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            {
                return false;
            }

            int urlEnd = text.IndexOf(')', labelEnd + 2);
            if (urlEnd < 0)
            {
                return false;
            }

            string label = text.Substring(characterIndex + 1, labelEnd - characterIndex - 1);
            string url = text.Substring(labelEnd + 2, urlEnd - labelEnd - 2).Trim();
            links?.Add(new LinkTarget(label, url));
            output.Append("<color=#2f75c0><b>");
            output.Append(EscapeRichText(label));
            output.Append("</b></color>");
            characterIndex = urlEnd + 1;
            return true;
        }

        private static bool TryConsumePairedMarker(string text, ref int characterIndex, string marker, string openingTag, string closingTag, StringBuilder output, List<LinkTarget> links)
        {
            if (!StartsWithAt(text, marker, characterIndex))
            {
                return false;
            }

            int closingIndex = text.IndexOf(marker, characterIndex + marker.Length, StringComparison.Ordinal);
            if (closingIndex < 0)
            {
                return false;
            }

            string innerText = text.Substring(characterIndex + marker.Length, closingIndex - characterIndex - marker.Length);
            output.Append(openingTag);
            output.Append(ConvertInlineMarkdown(innerText, links));
            output.Append(closingTag);
            characterIndex = closingIndex + marker.Length;
            return true;
        }

        private static bool TryConsumeItalicMarker(string text, ref int characterIndex, char marker, StringBuilder output, List<LinkTarget> links)
        {
            if (text[characterIndex] != marker || IsMarkerInsideWord(text, characterIndex, marker))
            {
                return false;
            }

            int closingIndex = FindClosingItalicMarker(text, characterIndex + 1, marker);
            if (closingIndex < 0)
            {
                return false;
            }

            string innerText = text.Substring(characterIndex + 1, closingIndex - characterIndex - 1);
            output.Append("<i>");
            output.Append(ConvertInlineMarkdown(innerText, links));
            output.Append("</i>");
            characterIndex = closingIndex + 1;
            return true;
        }

        private static int FindClosingItalicMarker(string text, int startIndex, char marker)
        {
            for (int characterIndex = startIndex; characterIndex < text.Length; characterIndex++)
            {
                if (text[characterIndex] == marker && !IsMarkerInsideWord(text, characterIndex, marker))
                {
                    return characterIndex;
                }
            }

            return -1;
        }

        private static bool IsMarkerInsideWord(string text, int characterIndex, char marker)
        {
            if (marker != '_')
            {
                return false;
            }

            bool hasPreviousWord = characterIndex > 0 && char.IsLetterOrDigit(text[characterIndex - 1]);
            bool hasNextWord = characterIndex + 1 < text.Length && char.IsLetterOrDigit(text[characterIndex + 1]);
            return hasPreviousWord && hasNextWord;
        }

        private static bool StartsWithAt(string text, string value, int characterIndex)
        {
            if (characterIndex + value.Length > text.Length)
            {
                return false;
            }

            for (int valueIndex = 0; valueIndex < value.Length; valueIndex++)
            {
                if (text[characterIndex + valueIndex] != value[valueIndex])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMarkdownEscapableCharacter(char character)
        {
            // CommonMark only treats a backslash as an escape when it precedes
            // ASCII punctuation. Preserve separators in paths such as
            // C:\GitHub\UMA instead of consuming each backslash.
            return (character >= '!' && character <= '/')
                || (character >= ':' && character <= '@')
                || (character >= '[' && character <= '`')
                || (character >= '{' && character <= '~');
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // Unity's rich-text renderer does not decode HTML entities, so
            // &lt; and &gt; are shown literally. A greater-than character is
            // safe outside a tag. Use TextCore's noparse scope for a literal
            // less-than character so it cannot begin a rich-text tag.
            return value.Replace("<", "<noparse><</noparse>");
        }

        private static string TrimSingleTrailingNewline(string value)
        {
            if (value.EndsWith("\r\n", StringComparison.Ordinal))
            {
                return value.Substring(0, value.Length - 2);
            }

            if (value.EndsWith("\n", StringComparison.Ordinal))
            {
                return value.Substring(0, value.Length - 1);
            }

            return value;
        }

        private void EnsureStyles()
        {
            if (styles != null && Mathf.Approximately(styledZoom, zoom))
            {
                return;
            }

            zoom = Mathf.Clamp(zoom, 0.75f, 1.6f);
            styledZoom = zoom;
            styles = new Styles(zoom);
        }

        private enum MarkdownBlockType
        {
            Paragraph,
            Heading,
            Code,
            Quote,
            HorizontalRule,
            List,
            Table,
            Image
        }

        private enum TableAlignment
        {
            Left,
            Center,
            Right
        }

        private sealed class MarkdownBlock
        {
            public readonly MarkdownBlockType Type;
            public int Level;
            public string Text = string.Empty;
            public string PlainText = string.Empty;
            public string Slug = string.Empty;
            public string Info = string.Empty;
            public string LinkUrl = string.Empty;
            public float RenderY;
            public bool HasRenderPosition;
            public MarkdownTable Table;
            public readonly List<MarkdownListItem> Items = new List<MarkdownListItem>();
            public readonly List<MarkdownBlock> Children = new List<MarkdownBlock>();

            public MarkdownBlock(MarkdownBlockType type)
            {
                Type = type;
            }
        }

        private sealed class MarkdownListItem
        {
            public int Level;
            public bool Ordered;
            public string Marker;
            public string Text;
            public bool? TaskState;

            public string DisplayMarker
            {
                get
                {
                    if (TaskState.HasValue)
                    {
                        return TaskState.Value ? "[x]" : "[ ]";
                    }

                    return Ordered ? Marker : "-";
                }
            }
        }

        private sealed class MarkdownTable
        {
            public readonly List<string> Headers = new List<string>();
            public readonly List<TableAlignment> Alignments = new List<TableAlignment>();
            public readonly List<List<string>> Rows = new List<List<string>>();
        }

        private sealed class LinkTarget
        {
            public readonly string Label;
            public readonly string Url;

            public LinkTarget(string label, string url)
            {
                Label = string.IsNullOrEmpty(label) ? url : label;
                Url = url;
            }
        }

        private sealed class LinkResolution
        {
            public readonly string AssetPath;
            public readonly string Fragment;

            public LinkResolution(string assetPath, string fragment)
            {
                AssetPath = assetPath;
                Fragment = fragment;
            }
        }

        private sealed class Styles
        {
            public readonly GUIStyle toolbarPath;
            public readonly GUIStyle toolbarLabel;
            public readonly GUIStyle outlineContainer;
            public readonly GUIStyle outlineTitle;
            public readonly GUIStyle outlineButton;
            public readonly GUIStyle outlineActiveButton;
            public readonly GUIStyle documentPadding;
            public readonly GUIStyle paragraph;
            public readonly GUIStyle source;
            public readonly GUIStyle code;
            public readonly GUIStyle codeContainer;
            public readonly GUIStyle codeInfo;
            public readonly GUIStyle quoteBlock;
            public readonly GUIStyle listMarker;
            public readonly GUIStyle linkButton;
            public readonly GUIStyle imageCaption;
            public readonly GUIStyle statusBar;
            public readonly GUIStyle emptyStateBox;
            public readonly GUIStyle tableHeader;
            public readonly GUIStyle tableHeaderCenter;
            public readonly GUIStyle tableHeaderRight;
            public readonly GUIStyle tableCell;
            public readonly GUIStyle tableCellCenter;
            public readonly GUIStyle tableCellRight;
            public readonly GUIStyle[] headingStyles;
            public readonly Color ruleColor;

            public Styles(float zoom)
            {
                int paragraphSize = Mathf.RoundToInt(13f * zoom);
                paragraph = new GUIStyle(EditorStyles.label)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = paragraphSize,
                    padding = new RectOffset(0, 0, 1, 1)
                };

                headingStyles = new GUIStyle[6];
                int[] headingSizes = new[] { 26, 22, 19, 17, 15, 14 };
                for (int styleIndex = 0; styleIndex < headingStyles.Length; styleIndex++)
                {
                    headingStyles[styleIndex] = new GUIStyle(EditorStyles.boldLabel)
                    {
                        richText = true,
                        wordWrap = true,
                        fontSize = Mathf.RoundToInt(headingSizes[styleIndex] * zoom),
                        padding = new RectOffset(0, 0, 3, 3)
                    };
                }

                toolbarPath = new GUIStyle(EditorStyles.toolbarButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip
                };

                toolbarLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };

                outlineContainer = new GUIStyle(EditorStyles.helpBox)
                {
                    margin = new RectOffset(4, 4, 4, 4),
                    padding = new RectOffset(0, 0, 0, 0)
                };

                outlineTitle = new GUIStyle(EditorStyles.toolbarButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold
                };

                outlineButton = CreateOutlineButtonStyle(EditorStyles.label, paragraphSize, false);
                outlineActiveButton = CreateOutlineButtonStyle(EditorStyles.boldLabel, paragraphSize, true);

                documentPadding = new GUIStyle()
                {
                    padding = new RectOffset((int)ContentPadding, (int)ContentPadding, (int)ContentPadding, (int)ContentPadding)
                };

                source = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false,
                    richText = false,
                    fontSize = paragraphSize
                };

                code = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false,
                    richText = false,
                    fontSize = paragraphSize,
                    padding = new RectOffset(6, 6, 5, 5)
                };

                codeContainer = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(6, 6, 5, 5)
                };

                codeInfo = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };

                quoteBlock = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 8, 6, 6),
                    margin = new RectOffset(4, 4, 2, 2)
                };

                listMarker = new GUIStyle(paragraph)
                {
                    alignment = TextAnchor.UpperRight,
                    wordWrap = false,
                    richText = false
                };

                linkButton = CreateLinkButtonStyle(paragraphSize);

                imageCaption = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    wordWrap = true
                };

                statusBar = new GUIStyle(EditorStyles.miniLabel)
                {
                    padding = new RectOffset(6, 6, 2, 2)
                };

                emptyStateBox = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 10),
                    margin = new RectOffset(24, 24, 24, 24)
                };

                tableHeader = CreateTableStyle(EditorStyles.boldLabel, TextAnchor.MiddleLeft, paragraphSize);
                tableHeaderCenter = CreateTableStyle(EditorStyles.boldLabel, TextAnchor.MiddleCenter, paragraphSize);
                tableHeaderRight = CreateTableStyle(EditorStyles.boldLabel, TextAnchor.MiddleRight, paragraphSize);
                tableCell = CreateTableStyle(EditorStyles.label, TextAnchor.UpperLeft, paragraphSize);
                tableCellCenter = CreateTableStyle(EditorStyles.label, TextAnchor.UpperCenter, paragraphSize);
                tableCellRight = CreateTableStyle(EditorStyles.label, TextAnchor.UpperRight, paragraphSize);

                ruleColor = EditorGUIUtility.isProSkin ? new Color(0.32f, 0.32f, 0.32f, 1f) : new Color(0.65f, 0.65f, 0.65f, 1f);
            }

            private static GUIStyle CreateTableStyle(GUIStyle source, TextAnchor alignment, int fontSize)
            {
                return new GUIStyle(EditorStyles.helpBox)
                {
                    richText = true,
                    wordWrap = true,
                    alignment = alignment,
                    fontSize = fontSize,
                    fontStyle = source.fontStyle,
                    padding = new RectOffset(6, 6, 4, 4)
                };
            }

            private static GUIStyle CreateOutlineButtonStyle(GUIStyle source, int fontSize, bool active)
            {
                GUIStyle style = new GUIStyle(source)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = Mathf.Max(10, fontSize - 1),
                    fontStyle = active ? FontStyle.Bold : source.fontStyle,
                    fixedHeight = Mathf.Max(18f, fontSize + 6f),
                    padding = new RectOffset(4, 4, 2, 2)
                };
                return style;
            }

            private static GUIStyle CreateLinkButtonStyle(int fontSize)
            {
                Color normalColor = EditorGUIUtility.isProSkin ? new Color(0.44f, 0.68f, 1f, 1f) : new Color(0.05f, 0.28f, 0.68f, 1f);
                Color hoverColor = EditorGUIUtility.isProSkin ? new Color(0.78f, 0.9f, 1f, 1f) : new Color(0.0f, 0.18f, 0.55f, 1f);
                Color activeColor = EditorGUIUtility.isProSkin ? new Color(0.55f, 0.78f, 1f, 1f) : new Color(0.0f, 0.12f, 0.42f, 1f);
                Color hoverBackground = EditorGUIUtility.isProSkin ? new Color(0.18f, 0.34f, 0.55f, 0.55f) : new Color(0.74f, 0.86f, 1f, 0.8f);

                GUIStyle style = new GUIStyle(EditorStyles.linkLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = true,
                    fontSize = fontSize,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(3, 3, 1, 1)
                };

                style.normal.textColor = normalColor;
                style.hover.textColor = hoverColor;
                style.hover.background = CreateStyleTexture(hoverBackground);
                style.active.textColor = activeColor;
                style.active.background = style.hover.background;
                style.focused.textColor = hoverColor;
                style.focused.background = style.hover.background;
                return style;
            }

            private static Texture2D CreateStyleTexture(Color color)
            {
                Texture2D texture = new Texture2D(1, 1)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point
                };
                texture.SetPixel(0, 0, color);
                texture.Apply();
                return texture;
            }
        }
    }
}
