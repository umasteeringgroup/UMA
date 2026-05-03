using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class UMATextureUtilitiesWindow : EditorWindow
    {
        private class PresetNamePromptWindow : EditorWindow
        {
            private string presetName;
            private Action<string> onSave;
            private bool focused;

            public static void Open(string initialName, Action<string> onSave)
            {
                PresetNamePromptWindow window = CreateInstance<PresetNamePromptWindow>();
                window.titleContent = new GUIContent("Save Preset");
                window.presetName = initialName ?? string.Empty;
                window.onSave = onSave;
                window.minSize = new Vector2(320f, 92f);
                window.maxSize = new Vector2(320f, 92f);
                window.ShowUtility();
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("Preset Name");
                GUI.SetNextControlName("PresetName");
                presetName = EditorGUILayout.TextField(presetName);

                if (!focused)
                {
                    focused = true;
                    EditorGUI.FocusTextInControl("PresetName");
                }

                Event currentEvent = Event.current;
                if (currentEvent.type == EventType.KeyDown)
                {
                    if (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter)
                    {
                        SaveAndClose();
                        currentEvent.Use();
                    }
                    else if (currentEvent.keyCode == KeyCode.Escape)
                    {
                        Close();
                        currentEvent.Use();
                    }
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(presetName)))
                {
                    if (GUILayout.Button("Save", GUILayout.Width(80f)))
                    {
                        SaveAndClose();
                    }
                }
                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    Close();
                }
                EditorGUILayout.EndHorizontal();
            }

            private void SaveAndClose()
            {
                string trimmedName = presetName.Trim();
                if (string.IsNullOrEmpty(trimmedName))
                {
                    return;
                }

                onSave?.Invoke(trimmedName);
                Close();
            }
        }

        [Serializable]
        private class TextureParameterPreset
        {
            public string name;
            public float brightness;
            public float contrast;
            public float saturation;
            public float hueDegrees;
            public bool hasAlphaFromLuminanceCutoff;
            public float alphaFromLuminanceCutoff;
            public int alphaFillRadiusPixels;
            public float alphaFillAlphaThreshold;
            public GradientMode gradientMode;
            public GradientFrom gradientFrom;
            public float solidPercent;
            public float gradientPercent;
            public float radialSolidHorizontalPercent;
            public float radialSolidVerticalPercent;
            public float radialGradientHorizontalPercent;
            public float radialGradientVerticalPercent;
            public float radialCenterOffsetHorizontalPercent;
            public float radialCenterOffsetVerticalPercent;
        }

        [Serializable]
        private class TextureParameterPresetCollection
        {
            public List<TextureParameterPreset> presets = new List<TextureParameterPreset>();
        }

        private class QueuedTextureState
        {
            public Texture2D workingTexture;
            public bool dirty;
        }

        private class ColorDistributionStats
        {
            public readonly int[] luminanceHistogram = new int[256];
            public int pixelCount;
            public float luminanceSum;
            public float saturationSum;
            public float hueVectorX;
            public float hueVectorY;
            public float hueWeight;

            public float MeanLuminance => pixelCount > 0 ? luminanceSum / pixelCount : 0f;
            public float MeanSaturation => pixelCount > 0 ? saturationSum / pixelCount : 0f;
        }

        private class DetailCurvePoint
        {
            public Vector2 position;
            public Vector2 inHandle;
            public Vector2 outHandle;

            public DetailCurvePoint(Vector2 position, Vector2 inHandle, Vector2 outHandle)
            {
                this.position = position;
                this.inHandle = inHandle;
                this.outHandle = outHandle;
            }
        }

        private class DetailAreaMask
        {
            public int minX;
            public int maxX;
            public int minY;
            public int maxY;
            public int boxWidth;
            public int boxHeight;
            public int insidePixelCount;
            public byte[] strengths;

            public float GetStrength(int x, int y)
            {
                if (strengths == null || x < minX || x > maxX || y < minY || y > maxY)
                {
                    return 0f;
                }

                int index = ((y - minY) * boxWidth) + (x - minX);
                return strengths[index] * (1f / 255f);
            }
        }

        private struct DetailSpot
        {
            public Vector2 center;
            public float radius;
            public Color color;

            public DetailSpot(Vector2 center, float radius, Color color)
            {
                this.center = center;
                this.radius = radius;
                this.color = color;
            }
        }

        private struct DetailSpotApplication
        {
            public DetailSpot spot;
            public float radius;
            public float radiusSquared;
            public int minX;
            public int maxX;
            public int minY;
            public int maxY;
        }

        private struct MagnifiedPreviewLayout
        {
            public Rect viewportRect;
            public Rect textureRect;
            public Vector2 contentSize;
        }

        private enum Tool
        {
            Split,
            AdjustTexture,
            AlphaGradient,
            AlphaFill,
            Touchup,
            AddDetails,
        }

        private enum TouchupMode
        {
            Erase,
        }

        private enum TouchupBrushShape
        {
            Round,
            Square,
            Bitmap,
        }

        private enum DetailEffectMode
        {
            Spots,
            Blush,
        }

        private enum DetailCurveDragTarget
        {
            None,
            Anchor,
            InHandle,
            OutHandle,
        }

        private enum SplitDirection
        {
            Vertical,
            Horizontal,
            Both,
        }

        private enum GradientFrom
        {
            Left,
            Right,
            Up,
            Down,
        }

        private enum GradientMode
        {
            Linear,
            Radial,
        }

        private enum BackgroundScaleMode
        {
            MatchWidth,
            MatchHeight,
        }

        private enum PreviewDisplayMode
        {
            Fit,
            Magnify,
        }

        private const float ToolPanelWidth = 190f;
        private const float DroppedTextureListWidth = 220f;
        private const float PreviewImageDefaultHeight = 512f;
        private const float PreviewImageMinHeight = 120f;
        private const float PreviewToolsScrollMinHeight = 600f;
        private const float PreviewResizeHandleHeight = 7f;
        private const float PreviewLayoutFixedHeight = 96f;
        private const float PreviewMagnifyMinZoom = 0.25f;
        private const float PreviewMagnifyMaxZoom = 8f;
        private const float DetailCircleKappa = 0.55228475f;
        private const float DetailDefaultCircleRadiusScale = 0.22f;
        private const float DetailMirrorCenterX = 0.5f;
        private const float DetailMirrorCenterEpsilon = 0.0005f;
        private const float DetailMirrorSimplifyEpsilon = 0.003f;
        private const float DetailAnchorHitRadius = 8f;
        private const float DetailHandleHitRadius = 6f;
        private const float DetailCurveHitRadius = 9f;
        private const int DetailMinCurvePoints = 3;
        private const int DetailCurveSamplesPerSegment = 18;
        private const int DetailMirrorSamplesPerSegment = 32;
        private const int DetailMirrorMaxPoints = 80;
        private const int CheckerTile = 16;
        private const int BcsParallelPixelThreshold = 65536;
        private const int BcsParallelMinPixelsPerWorker = 16384;
        private const int AutoMatchAlphaThreshold = 8;
        private const float AutoMatchMinLuminanceSpread = 0.05f;
        private const float AutoMatchMaxBrightness = 0.25f;
        private const float AutoMatchBrightnessInfluence = 0.65f;
        private const float AutoMatchContrastInfluence = 0.35f;
        private const float AutoMatchMinContrast = -0.18f;
        private const float AutoMatchMaxContrast = 0.22f;
        private const float AutoMatchMaxSaturation = 0.9f;
        private const float AutoMatchMaxHueDegrees = 90f;
        private const float ResizeDetailPreserveAmount = 0.55f;
        private const string PresetPrefsKey = "UMA.TextureUtilities.ParameterPresets";
        private const string BackgroundSectionExpandedPrefsKey = "UMA.TextureUtilities.BackgroundSectionExpanded";
        private const string AdjustmentsSectionExpandedPrefsKey = "UMA.TextureUtilities.AdjustmentsSectionExpanded";
        private const string ToolSectionExpandedPrefsKey = "UMA.TextureUtilities.ToolSectionExpanded";
        private static readonly Color CheckerLight = new Color(1f, 1f, 1f, 1f);
        private static readonly Color CheckerDark = new Color(0.75f, 0.75f, 0.75f, 1f);

        private Tool currentTool = Tool.Split;

        // Source / current texture state
        private Texture2D sourceAsset;
        private Texture2D backgroundAsset;
        private Texture2D currentTexture;     // editable RGBA32 working copy (the "current texture")
        private Texture2D diskOriginalTexture;
        private string diskOriginalTextureDirectory;
        private Texture2D previewTexture;     // displayed texture: currentTexture or BCS-adjusted copy
        private readonly List<Texture2D> droppedTextureAssets = new List<Texture2D>();
        private readonly Dictionary<Texture2D, QueuedTextureState> queuedTextureStates = new Dictionary<Texture2D, QueuedTextureState>();
        private int selectedDroppedTextureIndex = -1;
        private bool dirty;                   // currentTexture has unsaved baked changes
        private bool showBackgroundTexture;
        private bool combineBackgroundOnSave;
        private BackgroundScaleMode backgroundScaleMode = BackgroundScaleMode.MatchWidth;
        private PreviewDisplayMode previewDisplayMode = PreviewDisplayMode.Fit;
        private Vector2 previewCenterNormalized = new Vector2(0.5f, 0.5f);
        private bool previewMousePanning;
        private int previewMousePanButton = -1;
        private float previewMagnifyZoom = 1f;
        private float previewImageHeight = PreviewImageDefaultHeight;
        private bool previewResizeDragging;
        private bool backgroundSectionExpanded = true;
        private bool adjustmentsSectionExpanded = true;
        private bool toolSectionExpanded = true;

        // Cached pixel buffers used by live preview and destructive edits.
        private Color32[] cachedCurrentPixels;
        private Color32[] previewPixelBuffer;
        private int cachedPixelWidth;
        private int cachedPixelHeight;

        // Adjustments (live preview; -1..1 except hue which is -180..180 degrees; default 0)
        private float brightness = 0f;
        private float contrast = 0f;
        private float saturation = 0f;
        private float hueDegrees = 0f;
        private float alphaFromLuminanceCutoff = 0.5f;
        private float lastBrightness = 0f;
        private float lastContrast = 0f;
        private float lastSaturation = 0f;
        private float lastHueDegrees = 0f;

        // Adjust Texture tool state
        private int resizeWidth;
        private int resizeHeight;
        private int resizeSourceWidth;
        private int resizeSourceHeight;
        private bool resizePreserveDetails = true;
        private bool resizeSmoother = true;
        private float sharpenPower = 1f;
        private float blurPower = 1f;
        private float normalMapStrength = 4f;

        // Split tool state
        private SplitDirection splitDirection = SplitDirection.Vertical;
        private string splitBaseName = "Piece";

        // Gradient tool state
        private GradientMode gradientMode = GradientMode.Linear;
        private GradientFrom gradientFrom = GradientFrom.Left;
        private float solidPercent = 10f;
        private float gradientPercent = 10f;
        private float radialSolidHorizontalPercent = 10f;
        private float radialSolidVerticalPercent = 10f;
        private float radialGradientHorizontalPercent = 10f;
        private float radialGradientVerticalPercent = 10f;
        private float radialCenterOffsetHorizontalPercent = 0f;
        private float radialCenterOffsetVerticalPercent = 0f;
        // Track last applied gradient settings so we know when to re-bake live.
        private bool gradientApplied = false;
        private GradientMode lastGradientMode = GradientMode.Linear;
        private GradientFrom lastGradientFrom = GradientFrom.Left;
        private float lastSolidPercent = float.NaN;
        private float lastGradientPercent = float.NaN;
        private float lastRadialSolidHorizontalPercent = float.NaN;
        private float lastRadialSolidVerticalPercent = float.NaN;
        private float lastRadialGradientHorizontalPercent = float.NaN;
        private float lastRadialGradientVerticalPercent = float.NaN;
        private float lastRadialCenterOffsetHorizontalPercent = float.NaN;
        private float lastRadialCenterOffsetVerticalPercent = float.NaN;

        // Alpha fill tool state
        private int alphaFillRadiusPixels = 8;
        private float alphaFillAlphaThreshold = 0.01f;

        // Touchup tool state
        private TouchupMode touchupMode = TouchupMode.Erase;
        private TouchupBrushShape touchupBrushShape = TouchupBrushShape.Round;
        private int touchupBrushSizePixels = 32;
        private Texture2D touchupBrushBitmap;
        private Texture2D cachedTouchupBrushBitmap;
        private Color32[] cachedTouchupBrushPixels;
        private int cachedTouchupBrushWidth;
        private int cachedTouchupBrushHeight;
        private bool touchupPainting;

        // Add Details tool state
        private DetailEffectMode detailEffectMode = DetailEffectMode.Spots;
        private readonly List<DetailCurvePoint> detailPoints = new List<DetailCurvePoint>();
        private int detailAreaTextureWidth;
        private int detailAreaTextureHeight;
        private DetailCurveDragTarget detailDragTarget = DetailCurveDragTarget.None;
        private int detailDragPointIndex = -1;
        private int detailSelectedPointIndex = -1;
        private bool detailCurveDragging;
        private int detailSeed = 12345;
        private float detailStrength = 0.65f;
        private bool detailUseEdgeFalloff = true;
        private float detailFalloffDistancePixels = 48f;
        private Color detailSpotColor = new Color(0.38f, 0.16f, 0.10f, 1f);
        private float detailSpotColorVariation = 0.25f;
        private float detailSpotDensityPer10kPixels = 18f;
        private float detailSpotDensityVariation = 0.35f;
        private float detailSpotSizePixels = 3.5f;
        private float detailSpotSizeVariation = 0.45f;
        private Color detailBlushColor = new Color(1f, 0.32f, 0.28f, 1f);
        private float detailBlushOpacity = 0.25f;

        // Display-only quadrant visibility for the Adjust Texture tool.
        private bool visibleAreaTopLeft = true;
        private bool visibleAreaTopRight = true;
        private bool visibleAreaBottomLeft = true;
        private bool visibleAreaBottomRight = true;

        // Saved parameter presets
        private List<TextureParameterPreset> parameterPresets = new List<TextureParameterPreset>();
        private string[] parameterPresetOptions = new[] { "(No Presets)" };
        private int selectedParameterPresetIndex;
        private string presetName = string.Empty;

        // Cached checkerboard / hue strip
        private static Texture2D s_checker;
        private static Texture2D s_hueStrip;

        private Vector2 droppedTextureListScroll;
        private Vector2 scrollRight;

        [MenuItem("UMA/Texture Utilities", priority = 25)]
        public static void ShowWindow()
        {
            Open();
        }

        public static UMATextureUtilitiesWindow Open()
        {
            var window = GetWindow<UMATextureUtilitiesWindow>();
            window.titleContent = new GUIContent("UMA Texture Utilities");
            window.minSize = new Vector2(720f, 480f);
            window.Show();
            return window;
        }

        public static void Open(IList<Texture2D> textures)
        {
            UMATextureUtilitiesWindow window = Open();
            window.AddDroppedTexturesAndLoadFirst(textures, "loading selected textures");
        }

        private void OnDisable()
        {
            touchupPainting = false;
            detailCurveDragging = false;
            previewResizeDragging = false;
            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref diskOriginalTexture);
            diskOriginalTextureDirectory = null;
            DestroyTexture(ref previewTexture);
            DestroyQueuedTextureStates();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            LoadToolAreaFoldoutState();
            LoadParameterPresets();
        }

        private void OnGUI()
        {
            EnsureChecker();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawToolPanel();
            DrawRightPane();
            EditorGUILayout.EndHorizontal();
        }

        // ---------- Layout ----------

        private void DrawToolPanel()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(ToolPanelWidth), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            DrawToolToggle(Tool.Split, "Split Texture");
            DrawToolToggle(Tool.AdjustTexture, "Adjust Texture");
            DrawToolToggle(Tool.AlphaGradient, "Alpha Gradient");
            DrawToolToggle(Tool.AlphaFill, "Alpha Fill");
            DrawToolToggle(Tool.Touchup, "Touchup");
            DrawToolToggle(Tool.AddDetails, "Add Details");
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("(More tools may be added here.)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolToggle(Tool tool, string label)
        {
            bool isCurrent = currentTool == tool;
            bool pressed = GUILayout.Toggle(isCurrent, label, "Button");
            if (pressed && !isCurrent)
            {
                currentTool = tool;
            }
        }

        private void DrawRightPane()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            DrawHeaderBar();
            EditorGUILayout.Space();

            DrawPreviewArea(GetClampedPreviewImageHeight());
            DrawPreviewResizeHandle();
            EditorGUILayout.Space();

            scrollRight = EditorGUILayout.BeginScrollView(scrollRight, GUILayout.ExpandHeight(true));

            DrawBackgroundSection();
            EditorGUILayout.Space();

            DrawAdjustmentsSection();
            EditorGUILayout.Space();

            if (DrawCollapsibleSectionHeader(GetToolLabel(currentTool), ref toolSectionExpanded, ToolSectionExpandedPrefsKey))
            {
                GUIHelper.BeginVerticalPadded(8, new Color(0.85f, 0.92f, 1f), EditorStyles.helpBox);
                switch (currentTool)
                {
                    case Tool.Split: DrawSplitTool(); break;
                    case Tool.AdjustTexture: DrawAdjustTextureTool(); break;
                    case Tool.AlphaGradient: DrawAlphaGradientTool(); break;
                    case Tool.AlphaFill: DrawAlphaFillTool(); break;
                    case Tool.Touchup: DrawTouchupTool(); break;
                    case Tool.AddDetails: DrawAddDetailsTool(); break;
                }
                GUIHelper.EndVerticalPadded();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private bool DrawCollapsibleSectionHeader(string label, ref bool expanded, string prefsKey)
        {
            EditorGUI.BeginChangeCheck();
            expanded = EditorGUILayout.Foldout(expanded, label, true);
            if (EditorGUI.EndChangeCheck())
            {
                if (!string.IsNullOrEmpty(prefsKey))
                {
                    EditorPrefs.SetBool(prefsKey, expanded);
                }

                Repaint();
            }

            return expanded;
        }

        private void LoadToolAreaFoldoutState()
        {
            backgroundSectionExpanded = EditorPrefs.GetBool(BackgroundSectionExpandedPrefsKey, backgroundSectionExpanded);
            adjustmentsSectionExpanded = EditorPrefs.GetBool(AdjustmentsSectionExpandedPrefsKey, adjustmentsSectionExpanded);
            toolSectionExpanded = EditorPrefs.GetBool(ToolSectionExpandedPrefsKey, toolSectionExpanded);
        }

        private float GetClampedPreviewImageHeight()
        {
            return Mathf.Clamp(previewImageHeight, PreviewImageMinHeight, GetMaxPreviewImageHeight());
        }

        private float GetMaxPreviewImageHeight()
        {
            return position.height;
//            return Mathf.Max(PreviewImageMinHeight, position.height - PreviewToolsScrollMinHeight - PreviewLayoutFixedHeight);
        }

        private void DrawPreviewResizeHandle()
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(PreviewResizeHandleHeight), GUILayout.ExpandWidth(true));
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            Event currentEvent = Event.current;
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical, controlId);

            if (currentEvent.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(0.32f, 0.32f, 0.32f, 0.35f));
                float y = rect.y + Mathf.Floor(rect.height * 0.5f);
                EditorGUI.DrawRect(new Rect(rect.x + 16f, y, Mathf.Max(0f, rect.width - 32f), 1f), new Color(1f, 1f, 1f, 0.35f));
            }

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (rect.Contains(currentEvent.mousePosition) && currentEvent.button == 0)
                    {
                        previewResizeDragging = true;
                        GUIUtility.hotControl = controlId;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (previewResizeDragging && GUIUtility.hotControl == controlId)
                    {
                        previewImageHeight = Mathf.Clamp(GetClampedPreviewImageHeight() + currentEvent.delta.y, PreviewImageMinHeight, GetMaxPreviewImageHeight());
                        currentEvent.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (previewResizeDragging && GUIUtility.hotControl == controlId && currentEvent.button == 0)
                    {
                        previewResizeDragging = false;
                        GUIUtility.hotControl = 0;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseLeaveWindow:
                    if (previewResizeDragging && GUIUtility.hotControl == controlId)
                    {
                        previewResizeDragging = false;
                        GUIUtility.hotControl = 0;
                    }
                    break;
            }
        }

        private static string GetToolLabel(Tool tool)
        {
            switch (tool)
            {
                case Tool.Split: return "Split Texture";
                case Tool.AdjustTexture: return "Adjust Texture";
                case Tool.AlphaGradient: return "Alpha Gradient";
                case Tool.AlphaFill: return "Alpha Fill";
                case Tool.Touchup: return "Touchup";
                case Tool.AddDetails: return "Add Details";
                default: return tool.ToString();
            }
        }

        private void DrawHeaderBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            Texture2D newAsset = (Texture2D)EditorGUILayout.ObjectField(sourceAsset, typeof(Texture2D), false, GUILayout.Width(220));
            if (EditorGUI.EndChangeCheck())
            {
                TryLoadTextureAsset(newAsset, FindDroppedTextureIndex(newAsset), "loading a new texture");
            }

            if (GUILayout.Button("Load From Disk...", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                LoadFromDisk();
            }

            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                if (GUILayout.Button("Save As PNG...", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    SaveCurrentAsPng();
                }
            }

            using (new EditorGUI.DisabledScope(!CanQuickSaveOverwrite()))
            {
                if (GUILayout.Button("Quick Save and Overwrite", EditorStyles.toolbarButton, GUILayout.Width(170)))
                {
                    QuickSaveAndOverwrite();
                }
            }

            GUILayout.FlexibleSpace();

            if (currentTexture != null)
            {
                EditorGUILayout.LabelField($"{currentTexture.width} x {currentTexture.height}{(dirty ? " *" : "")}", GUILayout.Width(120));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBackgroundSection()
        {
            if (!DrawCollapsibleSectionHeader("Background", ref backgroundSectionExpanded, BackgroundSectionExpandedPrefsKey))
            {
                return;
            }

            GUIHelper.BeginVerticalPadded(8, new Color(0.95f, 0.92f, 0.88f), EditorStyles.helpBox);

            Texture2D newBackground = (Texture2D)EditorGUILayout.ObjectField("Background Texture", backgroundAsset, typeof(Texture2D), false);
            if (newBackground != backgroundAsset)
            {
                backgroundAsset = newBackground;
                if (backgroundAsset == null)
                {
                    showBackgroundTexture = false;
                    combineBackgroundOnSave = false;
                }
                InvalidatePreview();
            }

            using (new EditorGUI.DisabledScope(backgroundAsset == null))
            {
                EditorGUI.BeginChangeCheck();
                showBackgroundTexture = EditorGUILayout.Toggle("Show Background", showBackgroundTexture);
                backgroundScaleMode = (BackgroundScaleMode)EditorGUILayout.EnumPopup("Scale Background", backgroundScaleMode);
                combineBackgroundOnSave = EditorGUILayout.Toggle("Combine On Save", combineBackgroundOnSave);
                if (EditorGUI.EndChangeCheck())
                {
                    InvalidatePreview();
                }
            }

            GUIHelper.EndVerticalPadded();
        }

        private void DrawPreviewArea(float imageHeight)
        {
            DrawPreviewControls();

            EditorGUILayout.BeginHorizontal();
            if (droppedTextureAssets.Count > 0)
            {
                DrawDroppedTextureList(imageHeight);
                EditorGUILayout.Space(6f, false);
            }

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(imageHeight));
            EditorGUILayout.EndHorizontal();

            // Draw checkerboard background tiled.
            if (s_checker != null && Event.current.type == EventType.Repaint)
            {
                Vector2 tex = new Vector2(rect.width / s_checker.width, rect.height / s_checker.height);
                GUI.DrawTextureWithTexCoords(rect, s_checker, new Rect(0, 0, tex.x, tex.y), false);
            }

            EnsurePreviewTexture();
            Texture2D toShow = previewTexture != null ? previewTexture : currentTexture;
            if (toShow != null)
            {
                if (previewDisplayMode == PreviewDisplayMode.Magnify)
                {
                    DrawMagnifiedPreview(rect, toShow);
                }
                else
                {
                    DrawFitPreview(rect, toShow);
                }
            }
            else
            {
                if (showBackgroundTexture && backgroundAsset != null)
                {
                    Rect fit = FitRect(rect, backgroundAsset.width, backgroundAsset.height);
                    GUI.DrawTexture(fit, backgroundAsset, ScaleMode.StretchToFill, true);
                }

                GUI.Label(rect, "No texture loaded.\nUse the Object field, Load From Disk, or drag a project texture here.", CenteredStyle());
            }

            HandlePreviewDragAndDrop(rect);
        }

        private void DrawPreviewControls()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Display", GUILayout.Width(48f));
            EditorGUI.BeginChangeCheck();
            previewDisplayMode = (PreviewDisplayMode)GUILayout.Toolbar((int)previewDisplayMode, new[] { "Fit", "Magnify" }, EditorStyles.toolbarButton, GUILayout.Width(150f));
            if (EditorGUI.EndChangeCheck())
            {
                ResetMagnifiedPreviewCenter();
                previewMousePanning = false;
                previewMousePanButton = -1;
            }

            using (new EditorGUI.DisabledScope(previewDisplayMode != PreviewDisplayMode.Magnify))
            {
                GUILayout.Space(8f);
                EditorGUILayout.LabelField("Zoom", GUILayout.Width(36f));
                EditorGUI.BeginChangeCheck();
                previewMagnifyZoom = EditorGUILayout.Slider(previewMagnifyZoom, PreviewMagnifyMinZoom, PreviewMagnifyMaxZoom, GUILayout.Width(180f));
                EditorGUILayout.LabelField($"{previewMagnifyZoom * 100f:0}%", GUILayout.Width(44f));
                bool resetZoom = GUILayout.Button("100%", EditorStyles.toolbarButton, GUILayout.Width(48f));
                if (resetZoom)
                {
                    previewMagnifyZoom = 1f;
                }

                if (previewDisplayMode == PreviewDisplayMode.Magnify)
                {
                    bool centerPreview = GUILayout.Button("Center", EditorStyles.toolbarButton, GUILayout.Width(58f));
                    if (centerPreview)
                    {
                        ResetMagnifiedPreviewCenter();
                        Repaint();
                    }

                    GUILayout.Space(8f);
                    EditorGUILayout.LabelField("Pan with middle mouse button", EditorStyles.miniLabel, GUILayout.Width(170f));
                }

                bool zoomControlChanged = EditorGUI.EndChangeCheck() || resetZoom;
                if (zoomControlChanged)
                {
                    previewMagnifyZoom = Mathf.Clamp(previewMagnifyZoom, PreviewMagnifyMinZoom, PreviewMagnifyMaxZoom);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void ResetMagnifiedPreviewCenter()
        {
            previewCenterNormalized = new Vector2(0.5f, 0.5f);
        }

        private void DrawFitPreview(Rect rect, Texture2D toShow)
        {
            Rect fit = FitRect(rect, toShow.width, toShow.height);
            if (showBackgroundTexture && backgroundAsset != null)
            {
                Rect backgroundRect = FitBackgroundRect(fit, backgroundAsset.width, backgroundAsset.height, backgroundScaleMode);
                GUI.DrawTexture(backgroundRect, backgroundAsset, ScaleMode.StretchToFill, true);
            }

            DrawPreviewTextureWithVisibleAreas(fit, toShow);
            HandleTouchupPreview(fit, toShow.width, toShow.height);
            HandleAddDetailsPreview(fit, toShow.width, toShow.height);
        }

        private void DrawMagnifiedPreview(Rect rect, Texture2D toShow)
        {
            float zoom = Mathf.Clamp(previewMagnifyZoom, PreviewMagnifyMinZoom, PreviewMagnifyMaxZoom);
            MagnifiedPreviewLayout layout = GetMagnifiedPreviewLayout(rect, toShow, zoom, previewCenterNormalized);
            previewCenterNormalized = GetMagnifiedPreviewCenterNormalized(layout.textureRect, layout.viewportRect.size);

            GUI.BeginGroup(layout.viewportRect);
            Rect localViewportRect = new Rect(0f, 0f, layout.viewportRect.width, layout.viewportRect.height);
            if (showBackgroundTexture && backgroundAsset != null)
            {
                Rect backgroundRect = FitBackgroundRect(layout.textureRect, backgroundAsset.width, backgroundAsset.height, backgroundScaleMode);
                GUI.DrawTexture(backgroundRect, backgroundAsset, ScaleMode.StretchToFill, true);
            }

            DrawPreviewTextureWithVisibleAreas(layout.textureRect, toShow);
            HandleTouchupPreview(layout.textureRect, toShow.width, toShow.height);
            HandleAddDetailsPreview(layout.textureRect, toShow.width, toShow.height);
            HandleMagnifiedPreviewMousePan(localViewportRect, layout);
            GUI.EndGroup();
        }

        private static MagnifiedPreviewLayout GetMagnifiedPreviewLayout(Rect rect, Texture2D texture, float zoom, Vector2 centerNormalized)
        {
            Rect fit = FitRect(new Rect(0f, 0f, rect.width, rect.height), texture.width, texture.height);
            float clampedZoom = Mathf.Clamp(zoom, PreviewMagnifyMinZoom, PreviewMagnifyMaxZoom);
            float contentWidth = Mathf.Max(1f, fit.width * clampedZoom);
            float contentHeight = Mathf.Max(1f, fit.height * clampedZoom);
            Rect viewportRect = new Rect(rect.x, rect.y, Mathf.Max(1f, rect.width), Mathf.Max(1f, rect.height));
            Vector2 contentSize = new Vector2(contentWidth, contentHeight);

            Rect textureRect = new Rect(
                (viewportRect.width * 0.5f) - (Mathf.Clamp01(centerNormalized.x) * contentWidth),
                (viewportRect.height * 0.5f) - (Mathf.Clamp01(centerNormalized.y) * contentHeight),
                contentWidth,
                contentHeight);
            textureRect = ClampMagnifiedTextureRect(textureRect, viewportRect.size);

            return new MagnifiedPreviewLayout
            {
                viewportRect = viewportRect,
                textureRect = textureRect,
                contentSize = contentSize,
            };
        }

        private static Rect ClampMagnifiedTextureRect(Rect textureRect, Vector2 viewportSize)
        {
            if (textureRect.width <= viewportSize.x)
            {
                textureRect.x = (viewportSize.x - textureRect.width) * 0.5f;
            }
            else
            {
                textureRect.x = Mathf.Clamp(textureRect.x, viewportSize.x - textureRect.width, 0f);
            }

            if (textureRect.height <= viewportSize.y)
            {
                textureRect.y = (viewportSize.y - textureRect.height) * 0.5f;
            }
            else
            {
                textureRect.y = Mathf.Clamp(textureRect.y, viewportSize.y - textureRect.height, 0f);
            }

            return textureRect;
        }

        private static Vector2 GetMagnifiedPreviewCenterNormalized(Rect textureRect, Vector2 viewportSize)
        {
            return new Vector2(
                textureRect.width <= viewportSize.x ? 0.5f : Mathf.Clamp01(((viewportSize.x * 0.5f) - textureRect.xMin) / textureRect.width),
                textureRect.height <= viewportSize.y ? 0.5f : Mathf.Clamp01(((viewportSize.y * 0.5f) - textureRect.yMin) / textureRect.height));
        }

        private void DrawPreviewTextureWithVisibleAreas(Rect textureRect, Texture2D texture)
        {
            if (!UseVisibleAreaMask())
            {
                GUI.DrawTexture(textureRect, texture, ScaleMode.StretchToFill, true);
                return;
            }

            float halfWidth = textureRect.width * 0.5f;
            float halfHeight = textureRect.height * 0.5f;
            if (visibleAreaTopLeft)
            {
                GUI.DrawTextureWithTexCoords(new Rect(textureRect.x, textureRect.y, halfWidth, halfHeight), texture, new Rect(0f, 0.5f, 0.5f, 0.5f), true);
            }
            if (visibleAreaTopRight)
            {
                GUI.DrawTextureWithTexCoords(new Rect(textureRect.x + halfWidth, textureRect.y, halfWidth, halfHeight), texture, new Rect(0.5f, 0.5f, 0.5f, 0.5f), true);
            }
            if (visibleAreaBottomLeft)
            {
                GUI.DrawTextureWithTexCoords(new Rect(textureRect.x, textureRect.y + halfHeight, halfWidth, halfHeight), texture, new Rect(0f, 0f, 0.5f, 0.5f), true);
            }
            if (visibleAreaBottomRight)
            {
                GUI.DrawTextureWithTexCoords(new Rect(textureRect.x + halfWidth, textureRect.y + halfHeight, halfWidth, halfHeight), texture, new Rect(0.5f, 0f, 0.5f, 0.5f), true);
            }
        }

        private bool UseVisibleAreaMask()
        {
            return currentTool == Tool.AdjustTexture
                && (!visibleAreaTopLeft || !visibleAreaTopRight || !visibleAreaBottomLeft || !visibleAreaBottomRight);
        }

        private void HandleMagnifiedPreviewMousePan(Rect localViewportRect, MagnifiedPreviewLayout layout)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event evt = Event.current;
            bool canPan = layout.contentSize.x > localViewportRect.width || layout.contentSize.y > localViewportRect.height;
            bool mouseInViewport = localViewportRect.Contains(evt.mousePosition);

            if (canPan)
            {
                EditorGUIUtility.AddCursorRect(localViewportRect, MouseCursor.MoveArrow, controlId);
            }

            switch (evt.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (canPan && evt.button == 2 && mouseInViewport)
                    {
                        previewMousePanning = true;
                        previewMousePanButton = evt.button;
                        GUIUtility.hotControl = controlId;
                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (previewMousePanning && GUIUtility.hotControl == controlId)
                    {
                        Rect movedTextureRect = layout.textureRect;
                        movedTextureRect.position += evt.delta;
                        movedTextureRect = ClampMagnifiedTextureRect(movedTextureRect, localViewportRect.size);
                        previewCenterNormalized = GetMagnifiedPreviewCenterNormalized(movedTextureRect, localViewportRect.size);
                        evt.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (previewMousePanning && GUIUtility.hotControl == controlId && evt.button == previewMousePanButton)
                    {
                        previewMousePanning = false;
                        previewMousePanButton = -1;
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
                case EventType.MouseLeaveWindow:
                    if (previewMousePanning && GUIUtility.hotControl == controlId)
                    {
                        previewMousePanning = false;
                        previewMousePanButton = -1;
                        GUIUtility.hotControl = 0;
                    }
                    break;
            }
        }

        private void HandleTouchupPreview(Rect textureRect, int textureWidth, int textureHeight)
        {
            if (currentTool != Tool.Touchup || currentTexture == null || textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event currentEvent = Event.current;
            bool mouseInTexture = textureRect.Contains(currentEvent.mousePosition);
            bool ownsControl = touchupPainting && GUIUtility.hotControl == controlId;

            if (mouseInTexture || ownsControl)
            {
                EditorGUIUtility.AddCursorRect(textureRect, MouseCursor.Arrow, controlId);
            }

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (mouseInTexture && currentEvent.button == 0)
                    {
                        touchupPainting = true;
                        GUIUtility.hotControl = controlId;
                        GUI.FocusControl(null);
                        PaintTouchupAtPreviewPosition(currentEvent.mousePosition, textureRect);
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (ownsControl)
                    {
                        PaintTouchupAtPreviewPosition(currentEvent.mousePosition, textureRect);
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (ownsControl && currentEvent.button == 0)
                    {
                        touchupPainting = false;
                        GUIUtility.hotControl = 0;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseMove:
                    if (mouseInTexture)
                    {
                        Repaint();
                    }
                    break;
                case EventType.MouseLeaveWindow:
                    if (ownsControl)
                    {
                        touchupPainting = false;
                        GUIUtility.hotControl = 0;
                    }
                    break;
            }

            if (currentEvent.type == EventType.Repaint && (mouseInTexture || ownsControl))
            {
                DrawTouchupBrushOutline(currentEvent.mousePosition, textureRect, textureWidth, textureHeight);
            }
        }

        private void PaintTouchupAtPreviewPosition(Vector2 previewPosition, Rect textureRect)
        {
            if (currentTexture == null || touchupMode != TouchupMode.Erase)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            bool changed = EraseAlphaWithBrush(cachedCurrentPixels, currentTexture.width, currentTexture.height, previewPosition, textureRect);
            if (!changed)
            {
                return;
            }

            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private bool EraseAlphaWithBrush(Color32[] pixels, int width, int height, Vector2 previewPosition, Rect textureRect)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0 || textureRect.width <= 0f || textureRect.height <= 0f)
            {
                return false;
            }

            if (touchupBrushShape == TouchupBrushShape.Bitmap && !EnsureTouchupBrushPixels())
            {
                return false;
            }

            float brushSize = Mathf.Max(1f, touchupBrushSizePixels);
            float halfBrushSize = brushSize * 0.5f;
            float normalizedX = (previewPosition.x - textureRect.xMin) / textureRect.width;
            float normalizedYFromTop = (previewPosition.y - textureRect.yMin) / textureRect.height;
            float centerX = normalizedX * width;
            float centerY = (1f - normalizedYFromTop) * height;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - halfBrushSize));
            int maxX = Mathf.Min(width, Mathf.CeilToInt(centerX + halfBrushSize));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - halfBrushSize));
            int maxY = Mathf.Min(height, Mathf.CeilToInt(centerY + halfBrushSize));
            if (minX >= maxX || minY >= maxY)
            {
                return false;
            }

            bool changed = false;
            float radiusSquared = halfBrushSize * halfBrushSize;
            float left = centerX - halfBrushSize;
            float bottom = centerY - halfBrushSize;
            for (int y = minY; y < maxY; y++)
            {
                float pixelCenterY = y + 0.5f;
                for (int x = minX; x < maxX; x++)
                {
                    float pixelCenterX = x + 0.5f;
                    float mask = GetTouchupBrushMask(pixelCenterX, pixelCenterY, centerX, centerY, left, bottom, brushSize, radiusSquared);
                    if (mask <= 0f)
                    {
                        continue;
                    }

                    int pixelIndex = (y * width) + x;
                    Color32 pixel = pixels[pixelIndex];
                    byte alpha = (byte)Mathf.RoundToInt(pixel.a * (1f - Mathf.Clamp01(mask)));
                    if (alpha >= pixel.a)
                    {
                        continue;
                    }

                    pixel.a = alpha;
                    pixels[pixelIndex] = pixel;
                    changed = true;
                }
            }

            return changed;
        }

        private float GetTouchupBrushMask(
            float pixelCenterX,
            float pixelCenterY,
            float centerX,
            float centerY,
            float left,
            float bottom,
            float brushSize,
            float radiusSquared)
        {
            switch (touchupBrushShape)
            {
                case TouchupBrushShape.Round:
                    float deltaX = pixelCenterX - centerX;
                    float deltaY = pixelCenterY - centerY;
                    return (deltaX * deltaX) + (deltaY * deltaY) <= radiusSquared ? 1f : 0f;
                case TouchupBrushShape.Bitmap:
                    return GetTouchupBitmapBrushMask((pixelCenterX - left) / brushSize, (pixelCenterY - bottom) / brushSize);
                case TouchupBrushShape.Square:
                default:
                    return 1f;
            }
        }

        private float GetTouchupBitmapBrushMask(float u, float v)
        {
            if (cachedTouchupBrushPixels == null || cachedTouchupBrushWidth <= 0 || cachedTouchupBrushHeight <= 0 || u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return 0f;
            }

            int x = Mathf.Clamp(Mathf.FloorToInt(u * cachedTouchupBrushWidth), 0, cachedTouchupBrushWidth - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * cachedTouchupBrushHeight), 0, cachedTouchupBrushHeight - 1);
            Color32 brushPixel = cachedTouchupBrushPixels[(y * cachedTouchupBrushWidth) + x];
            const float inv255 = 1f / 255f;
            float alpha = brushPixel.a * inv255;
            float luminance = ((0.2126f * brushPixel.r) + (0.7152f * brushPixel.g) + (0.0722f * brushPixel.b)) * inv255;
            return Mathf.Clamp01(luminance * alpha);
        }

        private bool EnsureTouchupBrushPixels()
        {
            if (touchupBrushBitmap == null)
            {
                InvalidateTouchupBrushCache();
                return false;
            }

            if (cachedTouchupBrushBitmap == touchupBrushBitmap && cachedTouchupBrushPixels != null)
            {
                return true;
            }

            InvalidateTouchupBrushCache();
            Texture2D readableCopy = null;
            try
            {
                readableCopy = MakeReadableCopy(touchupBrushBitmap);
                cachedTouchupBrushBitmap = touchupBrushBitmap;
                cachedTouchupBrushPixels = readableCopy.GetPixels32();
                cachedTouchupBrushWidth = readableCopy.width;
                cachedTouchupBrushHeight = readableCopy.height;
                return cachedTouchupBrushPixels.Length > 0;
            }
            catch
            {
                InvalidateTouchupBrushCache();
                return false;
            }
            finally
            {
                DestroyTexture(ref readableCopy);
            }
        }

        private void InvalidateTouchupBrushCache()
        {
            cachedTouchupBrushBitmap = null;
            cachedTouchupBrushPixels = null;
            cachedTouchupBrushWidth = 0;
            cachedTouchupBrushHeight = 0;
        }

        private void DrawTouchupBrushOutline(Vector2 center, Rect textureRect, int textureWidth, int textureHeight)
        {
            float widthScale = textureRect.width / textureWidth;
            float heightScale = textureRect.height / textureHeight;
            float brushWidth = Mathf.Max(1f, touchupBrushSizePixels * widthScale);
            float brushHeight = Mathf.Max(1f, touchupBrushSizePixels * heightScale);

            if (touchupBrushShape == TouchupBrushShape.Round)
            {
                DrawTouchupRoundOutline(center, (brushWidth + brushHeight) * 0.25f);
            }
            else
            {
                DrawTouchupSquareOutline(new Rect(center.x - (brushWidth * 0.5f), center.y - (brushHeight * 0.5f), brushWidth, brushHeight));
            }
        }

        private static void DrawTouchupRoundOutline(Vector2 center, float radius)
        {
            Handles.BeginGUI();
            Color oldColor = Handles.color;
            Vector3 center3 = new Vector3(center.x, center.y, 0f);
            Handles.color = new Color(0f, 0f, 0f, 0.85f);
            Handles.DrawWireDisc(center3, Vector3.forward, radius + 1f);
            Handles.color = new Color(1f, 1f, 1f, 0.95f);
            Handles.DrawWireDisc(center3, Vector3.forward, radius);
            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private static void DrawTouchupSquareOutline(Rect rect)
        {
            DrawRectOutline(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), new Color(0f, 0f, 0f, 0.85f));
            DrawRectOutline(rect, new Color(1f, 1f, 1f, 0.95f));
        }

        private static void DrawRectOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
        }

        private void HandleAddDetailsPreview(Rect textureRect, int textureWidth, int textureHeight)
        {
            if (currentTool != Tool.AddDetails || currentTexture == null || textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            EnsureDetailAreaInitialized(textureWidth, textureHeight);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event currentEvent = Event.current;
            bool mouseInTexture = textureRect.Contains(currentEvent.mousePosition);
            bool ownsControl = detailCurveDragging && GUIUtility.hotControl == controlId;

            if (mouseInTexture || ownsControl)
            {
                EditorGUIUtility.AddCursorRect(textureRect, MouseCursor.Arrow, controlId);
            }

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (mouseInTexture && currentEvent.button == 0)
                    {
                        if (currentEvent.control || currentEvent.command)
                        {
                            if (TryDeleteDetailPointAtPreviewPosition(currentEvent.mousePosition, textureRect))
                            {
                                currentEvent.Use();
                                Repaint();
                            }
                        }
                        else if (currentEvent.shift)
                        {
                            if (TryInsertDetailPointAtPreviewPosition(currentEvent.mousePosition, textureRect))
                            {
                                currentEvent.Use();
                                Repaint();
                            }
                        }
                        else if (TryBeginDetailCurveDrag(currentEvent.mousePosition, textureRect))
                        {
                            GUIUtility.hotControl = controlId;
                            GUI.FocusControl(null);
                            currentEvent.Use();
                            Repaint();
                        }
                    }
                    break;
                case EventType.MouseDrag:
                    if (ownsControl)
                    {
                        MoveDetailDragToPreviewPosition(currentEvent.mousePosition, textureRect);
                        currentEvent.Use();
                        Repaint();
                    }
                    break;
                case EventType.MouseUp:
                    if (ownsControl && currentEvent.button == 0)
                    {
                        detailCurveDragging = false;
                        detailDragTarget = DetailCurveDragTarget.None;
                        detailDragPointIndex = -1;
                        GUIUtility.hotControl = 0;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseMove:
                    if (mouseInTexture)
                    {
                        Repaint();
                    }
                    break;
                case EventType.MouseLeaveWindow:
                    if (ownsControl)
                    {
                        detailCurveDragging = false;
                        detailDragTarget = DetailCurveDragTarget.None;
                        detailDragPointIndex = -1;
                        GUIUtility.hotControl = 0;
                    }
                    break;
            }

            if (currentEvent.type == EventType.Repaint)
            {
                DrawAddDetailsCurveOverlay(textureRect);
            }
        }

        private bool CanApplyAddDetails()
        {
            return currentTexture != null && detailPoints.Count >= DetailMinCurvePoints;
        }

        private void EnsureDetailAreaInitialized(int textureWidth, int textureHeight)
        {
            if (detailPoints.Count >= DetailMinCurvePoints
                && detailAreaTextureWidth == textureWidth
                && detailAreaTextureHeight == textureHeight)
            {
                return;
            }

            ResetDetailAreaToDefaultCircle(textureWidth, textureHeight);
        }

        private void ResetDetailAreaToDefaultCircle(int textureWidth, int textureHeight)
        {
            detailPoints.Clear();
            detailAreaTextureWidth = textureWidth;
            detailAreaTextureHeight = textureHeight;
            detailSelectedPointIndex = -1;

            if (textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            float radiusPixels = Mathf.Max(8f, Mathf.Min(textureWidth, textureHeight) * DetailDefaultCircleRadiusScale);
            float radiusX = radiusPixels / Mathf.Max(1f, textureWidth);
            float radiusY = radiusPixels / Mathf.Max(1f, textureHeight);
            float handleX = radiusX * DetailCircleKappa;
            float handleY = radiusY * DetailCircleKappa;
            Vector2 center = new Vector2(0.5f, 0.5f);

            Vector2 right = ClampNormalizedPoint(new Vector2(center.x + radiusX, center.y));
            Vector2 top = ClampNormalizedPoint(new Vector2(center.x, center.y + radiusY));
            Vector2 left = ClampNormalizedPoint(new Vector2(center.x - radiusX, center.y));
            Vector2 bottom = ClampNormalizedPoint(new Vector2(center.x, center.y - radiusY));

            detailPoints.Add(new DetailCurvePoint(
                right,
                ClampNormalizedPoint(new Vector2(right.x, right.y - handleY)),
                ClampNormalizedPoint(new Vector2(right.x, right.y + handleY))));
            detailPoints.Add(new DetailCurvePoint(
                top,
                ClampNormalizedPoint(new Vector2(top.x + handleX, top.y)),
                ClampNormalizedPoint(new Vector2(top.x - handleX, top.y))));
            detailPoints.Add(new DetailCurvePoint(
                left,
                ClampNormalizedPoint(new Vector2(left.x, left.y + handleY)),
                ClampNormalizedPoint(new Vector2(left.x, left.y - handleY))));
            detailPoints.Add(new DetailCurvePoint(
                bottom,
                ClampNormalizedPoint(new Vector2(bottom.x - handleX, bottom.y)),
                ClampNormalizedPoint(new Vector2(bottom.x + handleX, bottom.y))));
        }

        private void DrawAddDetailsCurveOverlay(Rect textureRect)
        {
            if (detailPoints.Count < DetailMinCurvePoints)
            {
                return;
            }

            Handles.BeginGUI();
            Color oldColor = Handles.color;

            for (int i = 0; i < detailPoints.Count; i++)
            {
                DetailCurvePoint point = detailPoints[i];
                DetailCurvePoint nextPoint = detailPoints[GetNextDetailPointIndex(i)];
                Vector2 p0 = NormalizedTextureToPreviewPoint(point.position, textureRect);
                Vector2 p1 = NormalizedTextureToPreviewPoint(point.outHandle, textureRect);
                Vector2 p2 = NormalizedTextureToPreviewPoint(nextPoint.inHandle, textureRect);
                Vector2 p3 = NormalizedTextureToPreviewPoint(nextPoint.position, textureRect);
                Handles.DrawBezier(p0, p3, p1, p2, new Color(0f, 0f, 0f, 0.8f), null, 4f);
                Handles.DrawBezier(p0, p3, p1, p2, new Color(0.1f, 0.65f, 1f, 0.95f), null, 2f);
            }

            for (int i = 0; i < detailPoints.Count; i++)
            {
                DetailCurvePoint point = detailPoints[i];
                Vector2 anchor = NormalizedTextureToPreviewPoint(point.position, textureRect);
                Vector2 inHandle = NormalizedTextureToPreviewPoint(point.inHandle, textureRect);
                Vector2 outHandle = NormalizedTextureToPreviewPoint(point.outHandle, textureRect);

                Handles.color = new Color(1f, 1f, 1f, 0.32f);
                Handles.DrawLine(anchor, inHandle);
                Handles.DrawLine(anchor, outHandle);

                bool selected = i == detailSelectedPointIndex || (detailCurveDragging && i == detailDragPointIndex);
                DrawDetailDisc(inHandle, DetailHandleHitRadius - 1f, new Color(0f, 0f, 0f, 0.85f), new Color(1f, 0.95f, 0.35f, 0.95f));
                DrawDetailDisc(outHandle, DetailHandleHitRadius - 1f, new Color(0f, 0f, 0f, 0.85f), new Color(1f, 0.95f, 0.35f, 0.95f));
                DrawDetailDisc(anchor, DetailAnchorHitRadius - 1f, new Color(0f, 0f, 0f, 0.9f), selected ? new Color(1f, 0.58f, 0.2f, 1f) : new Color(0.1f, 0.65f, 1f, 1f));
            }

            Handles.color = oldColor;
            Handles.EndGUI();
        }

        private static void DrawDetailDisc(Vector2 center, float radius, Color borderColor, Color fillColor)
        {
            Vector3 center3 = new Vector3(center.x, center.y, 0f);
            Handles.color = borderColor;
            Handles.DrawSolidDisc(center3, Vector3.forward, radius + 1f);
            Handles.color = fillColor;
            Handles.DrawSolidDisc(center3, Vector3.forward, radius);
        }

        private bool TryBeginDetailCurveDrag(Vector2 previewPosition, Rect textureRect)
        {
            if (!FindDetailHitTarget(previewPosition, textureRect, out DetailCurveDragTarget hitTarget, out int pointIndex))
            {
                return false;
            }

            detailCurveDragging = true;
            detailDragTarget = hitTarget;
            detailDragPointIndex = pointIndex;
            detailSelectedPointIndex = pointIndex;
            return true;
        }

        private bool FindDetailHitTarget(Vector2 previewPosition, Rect textureRect, out DetailCurveDragTarget hitTarget, out int pointIndex)
        {
            hitTarget = DetailCurveDragTarget.None;
            pointIndex = -1;
            float anchorHitRadiusSquared = DetailAnchorHitRadius * DetailAnchorHitRadius;
            float handleHitRadiusSquared = DetailHandleHitRadius * DetailHandleHitRadius;

            for (int i = 0; i < detailPoints.Count; i++)
            {
                Vector2 anchor = NormalizedTextureToPreviewPoint(detailPoints[i].position, textureRect);
                if ((anchor - previewPosition).sqrMagnitude <= anchorHitRadiusSquared)
                {
                    hitTarget = DetailCurveDragTarget.Anchor;
                    pointIndex = i;
                    return true;
                }
            }

            for (int i = 0; i < detailPoints.Count; i++)
            {
                Vector2 inHandle = NormalizedTextureToPreviewPoint(detailPoints[i].inHandle, textureRect);
                if ((inHandle - previewPosition).sqrMagnitude <= handleHitRadiusSquared)
                {
                    hitTarget = DetailCurveDragTarget.InHandle;
                    pointIndex = i;
                    return true;
                }

                Vector2 outHandle = NormalizedTextureToPreviewPoint(detailPoints[i].outHandle, textureRect);
                if ((outHandle - previewPosition).sqrMagnitude <= handleHitRadiusSquared)
                {
                    hitTarget = DetailCurveDragTarget.OutHandle;
                    pointIndex = i;
                    return true;
                }
            }

            return false;
        }

        private void MoveDetailDragToPreviewPosition(Vector2 previewPosition, Rect textureRect)
        {
            if (detailDragPointIndex < 0 || detailDragPointIndex >= detailPoints.Count)
            {
                return;
            }

            DetailCurvePoint point = detailPoints[detailDragPointIndex];
            Vector2 normalizedPosition = ClampNormalizedPoint(PreviewToNormalizedTexturePoint(previewPosition, textureRect));
            switch (detailDragTarget)
            {
                case DetailCurveDragTarget.Anchor:
                    Vector2 oldPosition = point.position;
                    Vector2 delta = normalizedPosition - oldPosition;
                    point.position = normalizedPosition;
                    point.inHandle = ClampNormalizedPoint(point.inHandle + delta);
                    point.outHandle = ClampNormalizedPoint(point.outHandle + delta);
                    break;
                case DetailCurveDragTarget.InHandle:
                    point.inHandle = normalizedPosition;
                    break;
                case DetailCurveDragTarget.OutHandle:
                    point.outHandle = normalizedPosition;
                    break;
            }
        }

        private bool TryDeleteDetailPointAtPreviewPosition(Vector2 previewPosition, Rect textureRect)
        {
            if (detailPoints.Count <= DetailMinCurvePoints)
            {
                return false;
            }

            int pointIndex = FindDetailAnchorAtPreviewPosition(previewPosition, textureRect);
            if (pointIndex < 0)
            {
                return false;
            }

            detailPoints.RemoveAt(pointIndex);
            detailSelectedPointIndex = Mathf.Clamp(pointIndex, 0, detailPoints.Count - 1);
            NormalizeDetailHandlesAroundIndex(detailSelectedPointIndex);
            NormalizeDetailHandlesAroundIndex(GetPreviousDetailPointIndex(detailSelectedPointIndex));
            return true;
        }

        private int FindDetailAnchorAtPreviewPosition(Vector2 previewPosition, Rect textureRect)
        {
            float anchorHitRadiusSquared = DetailAnchorHitRadius * DetailAnchorHitRadius;
            for (int i = 0; i < detailPoints.Count; i++)
            {
                Vector2 anchor = NormalizedTextureToPreviewPoint(detailPoints[i].position, textureRect);
                if ((anchor - previewPosition).sqrMagnitude <= anchorHitRadiusSquared)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryInsertDetailPointAtPreviewPosition(Vector2 previewPosition, Rect textureRect)
        {
            if (!FindNearestDetailCurveSegment(previewPosition, textureRect, out int segmentIndex, out float segmentT))
            {
                return false;
            }

            SplitDetailCurveSegment(segmentIndex, segmentT);
            detailSelectedPointIndex = segmentIndex + 1;
            return true;
        }

        private bool FindNearestDetailCurveSegment(Vector2 previewPosition, Rect textureRect, out int segmentIndex, out float segmentT)
        {
            segmentIndex = -1;
            segmentT = 0f;
            if (detailPoints.Count < DetailMinCurvePoints)
            {
                return false;
            }

            float bestDistanceSquared = DetailCurveHitRadius * DetailCurveHitRadius;
            const int HitTestSamples = 28;
            for (int i = 0; i < detailPoints.Count; i++)
            {
                DetailCurvePoint point = detailPoints[i];
                DetailCurvePoint nextPoint = detailPoints[GetNextDetailPointIndex(i)];
                Vector2 previous = NormalizedTextureToPreviewPoint(point.position, textureRect);
                for (int sample = 1; sample <= HitTestSamples; sample++)
                {
                    float t = (float)sample / HitTestSamples;
                    Vector2 current = NormalizedTextureToPreviewPoint(EvaluateCubic(point.position, point.outHandle, nextPoint.inHandle, nextPoint.position, t), textureRect);
                    float distanceSquared = DistancePointToSegmentSquared(previewPosition, previous, current, out float segmentLocalT);
                    if (distanceSquared < bestDistanceSquared)
                    {
                        bestDistanceSquared = distanceSquared;
                        segmentIndex = i;
                        segmentT = Mathf.Lerp((float)(sample - 1) / HitTestSamples, t, segmentLocalT);
                    }
                    previous = current;
                }
            }

            return segmentIndex >= 0;
        }

        private void SplitDetailCurveSegment(int segmentIndex, float t)
        {
            if (segmentIndex < 0 || segmentIndex >= detailPoints.Count)
            {
                return;
            }

            int nextIndex = GetNextDetailPointIndex(segmentIndex);
            DetailCurvePoint point = detailPoints[segmentIndex];
            DetailCurvePoint nextPoint = detailPoints[nextIndex];
            Vector2 p0 = point.position;
            Vector2 p1 = point.outHandle;
            Vector2 p2 = nextPoint.inHandle;
            Vector2 p3 = nextPoint.position;
            float clampedT = Mathf.Clamp01(t);

            Vector2 p01 = Vector2.Lerp(p0, p1, clampedT);
            Vector2 p12 = Vector2.Lerp(p1, p2, clampedT);
            Vector2 p23 = Vector2.Lerp(p2, p3, clampedT);
            Vector2 p012 = Vector2.Lerp(p01, p12, clampedT);
            Vector2 p123 = Vector2.Lerp(p12, p23, clampedT);
            Vector2 p0123 = Vector2.Lerp(p012, p123, clampedT);

            point.outHandle = ClampNormalizedPoint(p01);
            nextPoint.inHandle = ClampNormalizedPoint(p23);
            DetailCurvePoint insertedPoint = new DetailCurvePoint(
                ClampNormalizedPoint(p0123),
                ClampNormalizedPoint(p012),
                ClampNormalizedPoint(p123));
            detailPoints.Insert(segmentIndex + 1, insertedPoint);
        }

        private void MirrorDetailAreaRight()
        {
            if (currentTexture == null)
            {
                return;
            }

            EnsureDetailAreaInitialized(currentTexture.width, currentTexture.height);
            List<Vector2> sampledBoundary = SampleDetailBoundaryNormalized(DetailMirrorSamplesPerSegment);
            List<Vector2> clippedLeftBoundary = ClipDetailPolygonToLeftHalf(sampledBoundary);
            List<Vector2> leftBoundary = ExtractDetailMirrorLeftBoundary(clippedLeftBoundary);
            if (leftBoundary.Count < 2)
            {
                EditorUtility.DisplayDialog("Add Details", "The current detail area does not have enough left-side boundary to mirror.", "OK");
                return;
            }

            List<Vector2> mirroredBoundary = BuildDetailMirroredRightBoundary(leftBoundary);
            SimplifyDetailMirrorBoundary(mirroredBoundary);
            if (mirroredBoundary.Count < DetailMinCurvePoints)
            {
                EditorUtility.DisplayDialog("Add Details", "The mirrored detail area is too small to build a valid curve.", "OK");
                return;
            }

            RebuildDetailAreaFromBoundary(mirroredBoundary);
            Repaint();
        }

        private List<Vector2> SampleDetailBoundaryNormalized(int samplesPerSegment)
        {
            List<Vector2> points = new List<Vector2>();
            if (detailPoints.Count < DetailMinCurvePoints)
            {
                return points;
            }

            int clampedSamplesPerSegment = Mathf.Max(2, samplesPerSegment);
            for (int pointIndex = 0; pointIndex < detailPoints.Count; pointIndex++)
            {
                DetailCurvePoint point = detailPoints[pointIndex];
                DetailCurvePoint nextPoint = detailPoints[GetNextDetailPointIndex(pointIndex)];
                if (pointIndex == 0)
                {
                    AddDetailPointIfDistinct(points, point.position);
                }

                for (int sampleIndex = 1; sampleIndex <= clampedSamplesPerSegment; sampleIndex++)
                {
                    float sampleT = (float)sampleIndex / clampedSamplesPerSegment;
                    AddDetailPointIfDistinct(points, EvaluateCubic(point.position, point.outHandle, nextPoint.inHandle, nextPoint.position, sampleT));
                }
            }

            RemoveDuplicateDetailClosingPoint(points);
            return points;
        }

        private static List<Vector2> ClipDetailPolygonToLeftHalf(List<Vector2> polygon)
        {
            List<Vector2> clipped = new List<Vector2>();
            int count = polygon == null ? 0 : polygon.Count;
            if (count < DetailMinCurvePoints)
            {
                return clipped;
            }

            Vector2 previousPoint = polygon[count - 1];
            bool previousInside = IsDetailPointOnMirrorLeft(previousPoint);
            for (int pointIndex = 0; pointIndex < count; pointIndex++)
            {
                Vector2 currentPoint = polygon[pointIndex];
                bool currentInside = IsDetailPointOnMirrorLeft(currentPoint);
                if (currentInside)
                {
                    if (!previousInside)
                    {
                        AddDetailPointIfDistinct(clipped, GetDetailMirrorCenterIntersection(previousPoint, currentPoint));
                    }

                    AddDetailPointIfDistinct(clipped, currentPoint);
                }
                else if (previousInside)
                {
                    AddDetailPointIfDistinct(clipped, GetDetailMirrorCenterIntersection(previousPoint, currentPoint));
                }

                previousPoint = currentPoint;
                previousInside = currentInside;
            }

            RemoveDuplicateDetailClosingPoint(clipped);
            return clipped;
        }

        private static List<Vector2> ExtractDetailMirrorLeftBoundary(List<Vector2> clippedPolygon)
        {
            List<Vector2> boundary = new List<Vector2>();
            int count = clippedPolygon == null ? 0 : clippedPolygon.Count;
            if (count < 2)
            {
                return boundary;
            }

            int firstCenterIndex = -1;
            int secondCenterIndex = -1;
            float widestCenterSpan = -1f;
            for (int leftIndex = 0; leftIndex < count; leftIndex++)
            {
                if (!IsDetailPointOnMirrorCenter(clippedPolygon[leftIndex]))
                {
                    continue;
                }

                for (int rightIndex = leftIndex + 1; rightIndex < count; rightIndex++)
                {
                    if (!IsDetailPointOnMirrorCenter(clippedPolygon[rightIndex]))
                    {
                        continue;
                    }

                    float centerSpan = Mathf.Abs(clippedPolygon[leftIndex].y - clippedPolygon[rightIndex].y);
                    if (centerSpan > widestCenterSpan)
                    {
                        widestCenterSpan = centerSpan;
                        firstCenterIndex = leftIndex;
                        secondCenterIndex = rightIndex;
                    }
                }
            }

            if (firstCenterIndex < 0 || secondCenterIndex < 0)
            {
                boundary.AddRange(clippedPolygon);
                return boundary;
            }

            List<Vector2> forwardPath = GetDetailPolygonPath(clippedPolygon, firstCenterIndex, secondCenterIndex, true);
            List<Vector2> backwardPath = GetDetailPolygonPath(clippedPolygon, firstCenterIndex, secondCenterIndex, false);
            float forwardLeftness = GetDetailMirrorPathLeftness(forwardPath);
            float backwardLeftness = GetDetailMirrorPathLeftness(backwardPath);
            if (backwardLeftness > forwardLeftness)
            {
                return backwardPath;
            }

            if (Mathf.Approximately(backwardLeftness, forwardLeftness) && GetDetailPathLength(backwardPath) > GetDetailPathLength(forwardPath))
            {
                return backwardPath;
            }

            return forwardPath;
        }

        private static List<Vector2> GetDetailPolygonPath(List<Vector2> polygon, int startIndex, int endIndex, bool forward)
        {
            List<Vector2> path = new List<Vector2>();
            int count = polygon.Count;
            int pointIndex = startIndex;
            int guard = 0;
            while (guard <= count)
            {
                AddDetailPointIfDistinct(path, polygon[pointIndex]);
                if (pointIndex == endIndex)
                {
                    break;
                }

                pointIndex = forward ? (pointIndex + 1) % count : (pointIndex + count - 1) % count;
                guard++;
            }

            return path;
        }

        private static List<Vector2> BuildDetailMirroredRightBoundary(List<Vector2> leftBoundary)
        {
            List<Vector2> mirroredBoundary = new List<Vector2>();
            if (leftBoundary == null || leftBoundary.Count == 0)
            {
                return mirroredBoundary;
            }

            for (int pointIndex = 0; pointIndex < leftBoundary.Count; pointIndex++)
            {
                AddDetailPointIfDistinct(mirroredBoundary, ClampNormalizedPoint(leftBoundary[pointIndex]));
            }

            bool lastPointOnCenter = IsDetailPointOnMirrorCenter(leftBoundary[leftBoundary.Count - 1]);
            bool firstPointOnCenter = IsDetailPointOnMirrorCenter(leftBoundary[0]);
            int mirrorStartIndex = lastPointOnCenter ? leftBoundary.Count - 2 : leftBoundary.Count - 1;
            int mirrorEndIndex = firstPointOnCenter ? 1 : 0;
            for (int pointIndex = mirrorStartIndex; pointIndex >= mirrorEndIndex; pointIndex--)
            {
                AddDetailPointIfDistinct(mirroredBoundary, MirrorDetailPointRight(leftBoundary[pointIndex]));
            }

            RemoveDuplicateDetailClosingPoint(mirroredBoundary);
            return mirroredBoundary;
        }

        private static void SimplifyDetailMirrorBoundary(List<Vector2> boundary)
        {
            if (boundary == null)
            {
                return;
            }

            RemoveDuplicateDetailClosingPoint(boundary);
            float simplifyDistanceSquared = DetailMirrorSimplifyEpsilon * DetailMirrorSimplifyEpsilon;
            bool removedPoint;
            do
            {
                removedPoint = false;
                int removeIndex = FindDetailMirrorSimplifyIndex(boundary, simplifyDistanceSquared, boundary.Count > DetailMirrorMaxPoints);
                if (removeIndex >= 0)
                {
                    boundary.RemoveAt(removeIndex);
                    removedPoint = true;
                }
            }
            while (removedPoint && boundary.Count > DetailMinCurvePoints);
        }

        private static int FindDetailMirrorSimplifyIndex(List<Vector2> boundary, float simplifyDistanceSquared, bool forceRemove)
        {
            if (boundary.Count <= DetailMinCurvePoints)
            {
                return -1;
            }

            int bestIndex = -1;
            float bestDistanceSquared = float.MaxValue;
            for (int pointIndex = 0; pointIndex < boundary.Count; pointIndex++)
            {
                if (IsDetailPointOnMirrorCenter(boundary[pointIndex]))
                {
                    continue;
                }

                Vector2 previousPoint = boundary[(pointIndex + boundary.Count - 1) % boundary.Count];
                Vector2 nextPoint = boundary[(pointIndex + 1) % boundary.Count];
                float distanceSquared = DistancePointToSegmentSquared(boundary[pointIndex], previousPoint, nextPoint);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestIndex = pointIndex;
                }
            }

            if (bestIndex < 0)
            {
                return -1;
            }

            return forceRemove || bestDistanceSquared <= simplifyDistanceSquared ? bestIndex : -1;
        }

        private void RebuildDetailAreaFromBoundary(List<Vector2> boundary)
        {
            detailPoints.Clear();
            if (currentTexture != null)
            {
                detailAreaTextureWidth = currentTexture.width;
                detailAreaTextureHeight = currentTexture.height;
            }

            int count = boundary.Count;
            for (int pointIndex = 0; pointIndex < count; pointIndex++)
            {
                Vector2 previousPoint = ClampNormalizedPoint(boundary[(pointIndex + count - 1) % count]);
                Vector2 point = ClampNormalizedPoint(boundary[pointIndex]);
                Vector2 nextPoint = ClampNormalizedPoint(boundary[(pointIndex + 1) % count]);
                Vector2 tangent = nextPoint - previousPoint;
                detailPoints.Add(new DetailCurvePoint(
                    point,
                    ClampNormalizedPoint(point - (tangent * (1f / 6f))),
                    ClampNormalizedPoint(point + (tangent * (1f / 6f)))));
            }

            detailSelectedPointIndex = detailPoints.Count > 0 ? 0 : -1;
        }

        private static float GetDetailMirrorPathLeftness(List<Vector2> path)
        {
            if (path == null || path.Count == 0)
            {
                return 0f;
            }

            float leftness = 0f;
            for (int pointIndex = 0; pointIndex < path.Count; pointIndex++)
            {
                leftness += Mathf.Max(0f, DetailMirrorCenterX - path[pointIndex].x);
            }

            return leftness / path.Count;
        }

        private static float GetDetailPathLength(List<Vector2> path)
        {
            if (path == null || path.Count < 2)
            {
                return 0f;
            }

            float length = 0f;
            for (int pointIndex = 1; pointIndex < path.Count; pointIndex++)
            {
                length += Vector2.Distance(path[pointIndex - 1], path[pointIndex]);
            }

            return length;
        }

        private static bool IsDetailPointOnMirrorLeft(Vector2 point)
        {
            return point.x <= DetailMirrorCenterX + DetailMirrorCenterEpsilon;
        }

        private static bool IsDetailPointOnMirrorCenter(Vector2 point)
        {
            return Mathf.Abs(point.x - DetailMirrorCenterX) <= DetailMirrorCenterEpsilon;
        }

        private static Vector2 MirrorDetailPointRight(Vector2 point)
        {
            return ClampNormalizedPoint(new Vector2(1f - point.x, point.y));
        }

        private static Vector2 GetDetailMirrorCenterIntersection(Vector2 startPoint, Vector2 endPoint)
        {
            float deltaX = endPoint.x - startPoint.x;
            if (Mathf.Abs(deltaX) <= 1e-6f)
            {
                return ClampNormalizedPoint(new Vector2(DetailMirrorCenterX, Mathf.Lerp(startPoint.y, endPoint.y, 0.5f)));
            }

            float intersectionT = Mathf.Clamp01((DetailMirrorCenterX - startPoint.x) / deltaX);
            Vector2 intersection = Vector2.Lerp(startPoint, endPoint, intersectionT);
            intersection.x = DetailMirrorCenterX;
            return ClampNormalizedPoint(intersection);
        }

        private static void AddDetailPointIfDistinct(List<Vector2> points, Vector2 point)
        {
            Vector2 clampedPoint = ClampNormalizedPoint(point);
            if (points.Count == 0 || (points[points.Count - 1] - clampedPoint).sqrMagnitude > 1e-8f)
            {
                points.Add(clampedPoint);
            }
        }

        private static void RemoveDuplicateDetailClosingPoint(List<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            if ((points[0] - points[points.Count - 1]).sqrMagnitude <= 1e-8f)
            {
                points.RemoveAt(points.Count - 1);
            }
        }

        private void NormalizeDetailHandlesAroundIndex(int pointIndex)
        {
            if (pointIndex < 0 || pointIndex >= detailPoints.Count || detailPoints.Count < DetailMinCurvePoints)
            {
                return;
            }

            DetailCurvePoint point = detailPoints[pointIndex];
            Vector2 previous = detailPoints[GetPreviousDetailPointIndex(pointIndex)].position;
            Vector2 next = detailPoints[GetNextDetailPointIndex(pointIndex)].position;
            point.inHandle = ClampNormalizedPoint(point.position + ((previous - point.position) * 0.33f));
            point.outHandle = ClampNormalizedPoint(point.position + ((next - point.position) * 0.33f));
        }

        private int GetNextDetailPointIndex(int pointIndex)
        {
            return detailPoints.Count == 0 ? 0 : (pointIndex + 1) % detailPoints.Count;
        }

        private int GetPreviousDetailPointIndex(int pointIndex)
        {
            if (detailPoints.Count == 0)
            {
                return 0;
            }

            return (pointIndex + detailPoints.Count - 1) % detailPoints.Count;
        }

        private static Vector2 PreviewToNormalizedTexturePoint(Vector2 previewPosition, Rect textureRect)
        {
            float x = textureRect.width <= 0f ? 0f : (previewPosition.x - textureRect.xMin) / textureRect.width;
            float yFromTop = textureRect.height <= 0f ? 0f : (previewPosition.y - textureRect.yMin) / textureRect.height;
            return new Vector2(x, 1f - yFromTop);
        }

        private static Vector2 NormalizedTextureToPreviewPoint(Vector2 normalizedPoint, Rect textureRect)
        {
            return new Vector2(
                textureRect.xMin + (normalizedPoint.x * textureRect.width),
                textureRect.yMax - (normalizedPoint.y * textureRect.height));
        }

        private static Vector2 NormalizedTextureToPixelPoint(Vector2 normalizedPoint, int textureWidth, int textureHeight)
        {
            return new Vector2(normalizedPoint.x * textureWidth, normalizedPoint.y * textureHeight);
        }

        private static Vector2 ClampNormalizedPoint(Vector2 point)
        {
            point.x = Mathf.Clamp01(point.x);
            point.y = Mathf.Clamp01(point.y);
            return point;
        }

        private static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float oneMinusT = 1f - t;
            return (oneMinusT * oneMinusT * oneMinusT * p0)
                + (3f * oneMinusT * oneMinusT * t * p1)
                + (3f * oneMinusT * t * t * p2)
                + (t * t * t * p3);
        }

        private static float DistancePointToSegmentSquared(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            return DistancePointToSegmentSquared(point, segmentStart, segmentEnd, out _);
        }

        private static float DistancePointToSegmentSquared(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd, out float segmentT)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 1e-6f)
            {
                segmentT = 0f;
                return (point - segmentStart).sqrMagnitude;
            }

            segmentT = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            Vector2 closest = segmentStart + (segment * segmentT);
            return (point - closest).sqrMagnitude;
        }

        private void DrawDroppedTextureList(float height)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(DroppedTextureListWidth), GUILayout.Height(height));
            EditorGUILayout.LabelField("Queue", EditorStyles.boldLabel);
            droppedTextureListScroll = EditorGUILayout.BeginScrollView(droppedTextureListScroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < droppedTextureAssets.Count; i++)
            {
                DrawDroppedTextureRow(i);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDroppedTextureRow(int index)
        {
            Texture2D texture = droppedTextureAssets[index];
            if (texture == null)
            {
                RemoveDroppedTextureAt(index);
                GUIUtility.ExitGUI();
                return;
            }

            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            Rect markerRect = new Rect(rowRect.x, rowRect.y, 24f, rowRect.height);
            Rect fieldRect = new Rect(rowRect.x + 26f, rowRect.y, rowRect.width - 50f, rowRect.height);
            Rect removeRect = new Rect(rowRect.xMax - 20f, rowRect.y, 20f, rowRect.height);
            bool isCurrent = index == selectedDroppedTextureIndex && sourceAsset == texture;
            bool isDirty = IsQueuedTextureDirty(texture, index);
            if (Event.current.type == EventType.Repaint && isCurrent)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.5f, 0.72f, 1f, 0.45f));
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rowRect.Contains(currentEvent.mousePosition) && !removeRect.Contains(currentEvent.mousePosition))
            {
                TryLoadTextureAsset(texture, index, "selecting a queued texture");
                GUI.FocusControl(null);
                currentEvent.Use();
                GUIUtility.ExitGUI();
            }

            EditorGUI.LabelField(markerRect, (isCurrent ? ">" : " ") + (isDirty ? "*" : string.Empty), EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.ObjectField(fieldRect, GUIContent.none, texture, typeof(Texture2D), false);
            EditorGUI.EndDisabledGroup();

            if (GUI.Button(removeRect, "X"))
            {
                RemoveDroppedTextureAt(index);
                GUIUtility.ExitGUI();
            }
        }

        private void DrawAdjustmentsSection()
        {
            if (!DrawCollapsibleSectionHeader("Adjustments (live preview)", ref adjustmentsSectionExpanded, AdjustmentsSectionExpandedPrefsKey))
            {
                return;
            }

            GUIHelper.BeginVerticalPadded(8, new Color(0.92f, 0.92f, 0.92f), EditorStyles.helpBox);

            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                brightness = EditorGUILayout.Slider("Brightness", brightness, -1f, 1f);
                contrast = EditorGUILayout.Slider("Contrast", contrast, -1f, 1f);
                saturation = EditorGUILayout.Slider("Saturation", saturation, -1f, 1f);

                hueDegrees = EditorGUILayout.Slider(new GUIContent("Hue", "Hue rotation in degrees (-180..180)."), hueDegrees, -180f, 180f);
                DrawHueStrip(hueDegrees);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Apply"))
                {
                    BakeAdjustmentsToCurrent();
                }
                if (GUILayout.Button("Reset"))
                {
                    RestoreCurrentTextureToUnmodified();
                }
                using (new EditorGUI.DisabledScope(backgroundAsset == null))
                {
                    if (GUILayout.Button("Auto-match", GUILayout.Width(95f)))
                    {
                        AutoMatchAdjustmentsToBackground();
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4f);
                alphaFromLuminanceCutoff = EditorGUILayout.Slider(
                    new GUIContent("Alpha Luminance Cutoff", "Only pixels with luminance at or below this value can lower the existing alpha."),
                    alphaFromLuminanceCutoff,
                    0f,
                    1f);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Invert Colors", "Invert RGB values while preserving the alpha channel.")))
                {
                    InvertCurrentColors();
                }
                if (GUILayout.Button(new GUIContent("Alpha From Luminance", "Lower alpha from RGB luminance when luminance is at or below the cutoff.")))
                {
                    ApplyAlphaFromLuminance();
                }
                if (GUILayout.Button(new GUIContent("Fill with White", "Set RGB to white while preserving the alpha channel.")))
                {
                    FillCurrentColors(255);
                }
                if (GUILayout.Button(new GUIContent("Fill with Black", "Set RGB to black while preserving the alpha channel.")))
                {
                    FillCurrentColors(0);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4f);
            DrawAdjustmentPresetControls();

            GUIHelper.EndVerticalPadded();
        }

        private void DrawAdjustmentPresetControls()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save Preset", GUILayout.Width(100f)))
            {
                PromptAndSaveCurrentPreset();
            }

            using (new EditorGUI.DisabledScope(parameterPresets.Count == 0))
            {
                selectedParameterPresetIndex = EditorGUILayout.Popup(selectedParameterPresetIndex, parameterPresetOptions, GUILayout.MinWidth(120f));
                if (GUILayout.Button("Apply", GUILayout.Width(70f)))
                {
                    ApplySelectedPreset();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawHueStrip(float currentHueDegrees)
        {
            EnsureHueStrip();
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(14f), GUILayout.ExpandWidth(true));
            // small label-width inset to roughly align with slider track
            float labelWidth = EditorGUIUtility.labelWidth;
            Rect strip = new Rect(r.x + labelWidth, r.y + 2f, r.width - labelWidth - 4f, r.height - 4f);
            if (Event.current.type == EventType.Repaint && s_hueStrip != null)
            {
                GUI.DrawTexture(strip, s_hueStrip, ScaleMode.StretchToFill, false);
                // marker for current hue (-180..180 -> 0..1)
                float t = Mathf.Clamp01((currentHueDegrees + 180f) / 360f);
                Rect marker = new Rect(strip.x + t * strip.width - 1f, strip.y - 1f, 2f, strip.height + 2f);
                EditorGUI.DrawRect(marker, Color.black);
            }
        }

        private static void EnsureHueStrip()
        {
            if (s_hueStrip != null) return;
            const int w = 256;
            s_hueStrip = new Texture2D(w, 1, TextureFormat.RGBA32, false, false);
            s_hueStrip.hideFlags = HideFlags.HideAndDontSave;
            s_hueStrip.wrapMode = TextureWrapMode.Clamp;
            s_hueStrip.filterMode = FilterMode.Bilinear;
            Color[] px = new Color[w];
            for (int i = 0; i < w; i++)
            {
                px[i] = Color.HSVToRGB((float)i / (w - 1), 1f, 1f);
            }
            s_hueStrip.SetPixels(px);
            s_hueStrip.Apply(false, false);
        }

        // ---------- Tool UIs ----------

        private void DrawSplitTool()
        {
            EditorGUILayout.HelpBox("Split the current texture into halves or quadrants. The original asset is not modified — pieces are saved as new PNG files numbered from 1.", MessageType.Info);
            splitDirection = (SplitDirection)EditorGUILayout.EnumPopup("Direction", splitDirection);
            splitBaseName = EditorGUILayout.TextField("Base Name", splitBaseName);

            using (new EditorGUI.DisabledScope(currentTexture == null || string.IsNullOrWhiteSpace(splitBaseName)))
            {
                if (GUILayout.Button("Split..."))
                {
                    SplitCurrentTexture();
                }
            }
        }

        private void DrawAdjustTextureTool()
        {
            EditorGUILayout.HelpBox("Adjust RGB preview controls, resize the working texture, or generate a Unity normal map from luminance. Visible Area only changes the preview display.", MessageType.Info);
            EditorGUILayout.LabelField("Visible Area", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            visibleAreaTopLeft = EditorGUILayout.ToggleLeft("Top Left", visibleAreaTopLeft, GUILayout.Width(90f));
            visibleAreaTopRight = EditorGUILayout.ToggleLeft("Top Right", visibleAreaTopRight, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            visibleAreaBottomLeft = EditorGUILayout.ToggleLeft("Bottom Left", visibleAreaBottomLeft, GUILayout.Width(90f));
            visibleAreaBottomRight = EditorGUILayout.ToggleLeft("Bottom Right", visibleAreaBottomRight, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Resize", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                EnsureResizeFieldsInitialized();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("X 2", GUILayout.Width(60f)))
                {
                    resizeWidth *= 2;
                    resizeHeight *= 2;
                }
                if (GUILayout.Button("/ 2", GUILayout.Width(60f)))
                {
                    resizeWidth = Mathf.Max(1, resizeWidth / 2);
                    resizeHeight = Mathf.Max(1, resizeHeight / 2);
                }
                resizeWidth = Mathf.Max(1, EditorGUILayout.DelayedIntField("Width", resizeWidth));
                resizeHeight = Mathf.Max(1, EditorGUILayout.DelayedIntField("Height", resizeHeight));
                EditorGUILayout.EndHorizontal();

                resizePreserveDetails = EditorGUILayout.Toggle("Preserve Details", resizePreserveDetails);
                resizeSmoother = EditorGUILayout.Toggle("Smoother", resizeSmoother);

                using (new EditorGUI.DisabledScope(currentTexture == null || resizeWidth <= 0 || resizeHeight <= 0))
                {
                    if (GUILayout.Button("Resize Texture"))
                    {
                        ResizeCurrentTexture();
                    }
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Sharpen", EditorStyles.boldLabel);
                sharpenPower = EditorGUILayout.Slider("Power", sharpenPower, 0.1f, 5f);
                if (GUILayout.Button("Sharpen Texture"))
                {
                    SharpenCurrentTexture();
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Blur", EditorStyles.boldLabel);
                blurPower = EditorGUILayout.Slider("Power", blurPower, 0.1f, 12f);
                if (GUILayout.Button("Blur Texture"))
                {
                    BlurCurrentTexture();
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Hairify", EditorStyles.boldLabel);
                if (GUILayout.Button("Hairify (experimental)"))
                {
                    HairifyCurrentTexture();
                }

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Normal Map", EditorStyles.boldLabel);
                normalMapStrength = EditorGUILayout.Slider("Strength", normalMapStrength, 0.1f, 16f);
                if (GUILayout.Button("Generate Normal Map from Texture"))
                {
                    GenerateNormalMapFromTexture();
                }
            }
        }

        private void DrawAlphaGradientTool()
        {
            EditorGUILayout.HelpBox("Replaces the alpha channel of the current texture with a gradient mask. RGB is preserved. Linear mode uses 'Coming From' as the fully-opaque edge. Changes apply live as you adjust the values.", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            gradientMode = (GradientMode)EditorGUILayout.EnumPopup("Mode", gradientMode);
            if (gradientMode == GradientMode.Linear)
            {
                gradientFrom = (GradientFrom)EditorGUILayout.EnumPopup("Coming From", gradientFrom);
                solidPercent = EditorGUILayout.Slider("Solid (%)", solidPercent, 0f, 100f);
                gradientPercent = EditorGUILayout.Slider("Gradient Length (%)", gradientPercent, 0f, 100f);
            }
            else
            {
                radialSolidHorizontalPercent = EditorGUILayout.Slider("Solid Horizontal (%)", radialSolidHorizontalPercent, 0f, 100f);
                radialSolidVerticalPercent = EditorGUILayout.Slider("Solid Vertical (%)", radialSolidVerticalPercent, 0f, 100f);
                radialGradientHorizontalPercent = EditorGUILayout.Slider("Gradient Horizontal (%)", radialGradientHorizontalPercent, 0f, 100f);
                radialGradientVerticalPercent = EditorGUILayout.Slider("Gradient Vertical (%)", radialGradientVerticalPercent, 0f, 100f);
                radialCenterOffsetHorizontalPercent = EditorGUILayout.Slider("Center Offset Horizontal (%)", radialCenterOffsetHorizontalPercent, -100f, 100f);
                radialCenterOffsetVerticalPercent = EditorGUILayout.Slider("Center Offset Vertical (%)", radialCenterOffsetVerticalPercent, -100f, 100f);
            }
            bool changed = EditorGUI.EndChangeCheck();

            if (gradientMode == GradientMode.Linear && solidPercent + gradientPercent > 100f)
            {
                EditorGUILayout.HelpBox("Solid + Gradient exceeds 100%. Values will be clamped.", MessageType.Warning);
            }
            else if (gradientMode == GradientMode.Radial &&
                (radialSolidHorizontalPercent + radialGradientHorizontalPercent > 100f
                || radialSolidVerticalPercent + radialGradientVerticalPercent > 100f))
            {
                EditorGUILayout.HelpBox("Solid + Gradient exceeds 100% on one or more axes. Values will be clamped.", MessageType.Warning);
            }

            if (currentTexture != null && (changed || AreGradientSettingsChanged()))
            {
                ApplyAlphaGradient();
            }
        }

        private void DrawAlphaFillTool()
        {
            EditorGUILayout.HelpBox("Fills RGB color into transparent padding from nearby opaque pixels while preserving the original alpha channel. Use it to reduce edge fringes and mip seam artifacts.", MessageType.Info);

            alphaFillRadiusPixels = EditorGUILayout.IntSlider(new GUIContent("Radius (px)", "Maximum number of pixels to expand nearby RGB into transparent padding."), alphaFillRadiusPixels, 1, 256);
            alphaFillAlphaThreshold = EditorGUILayout.Slider(new GUIContent("Alpha Threshold", "Pixels at or below this alpha receive nearby RGB; pixels above it are treated as color sources."), alphaFillAlphaThreshold, 0f, 1f);

            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                if (GUILayout.Button("Apply Alpha Fill"))
                {
                    ApplyAlphaFill();
                }
            }
        }

        private void DrawTouchupTool()
        {
            EditorGUILayout.HelpBox("Paint alpha-only touchups directly on the preview. Erase lowers alpha using the selected brush.", MessageType.Info);

            touchupMode = (TouchupMode)GUILayout.Toolbar((int)touchupMode, new[] { "Erase" });
            touchupBrushShape = (TouchupBrushShape)EditorGUILayout.EnumPopup("Brush", touchupBrushShape);
            touchupBrushSizePixels = EditorGUILayout.IntSlider(new GUIContent("Size (px)", "Brush diameter or square side length in texture pixels."), touchupBrushSizePixels, 1, 512);

            if (touchupBrushShape == TouchupBrushShape.Bitmap)
            {
                EditorGUI.BeginChangeCheck();
                Texture2D newBrush = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Bitmap Brush", "Grayscale mask: black is invisible, white is visible."), touchupBrushBitmap, typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck())
                {
                    touchupBrushBitmap = newBrush;
                    InvalidateTouchupBrushCache();
                }

                if (touchupBrushBitmap == null)
                {
                    EditorGUILayout.HelpBox("Assign a bitmap brush to paint with the Bitmap brush type.", MessageType.Info);
                }
            }

            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                EditorGUILayout.LabelField("Drag on the preview to erase alpha.", EditorStyles.miniLabel);
            }
        }

        private void DrawAddDetailsTool()
        {
            EditorGUILayout.HelpBox("Define a closed Bezier area on the preview, then apply spots or blush to RGB while preserving alpha.", MessageType.Info);

            using (new EditorGUI.DisabledScope(currentTexture == null))
            {
                if (currentTexture != null)
                {
                    EnsureDetailAreaInitialized(currentTexture.width, currentTexture.height);
                }

                detailEffectMode = (DetailEffectMode)GUILayout.Toolbar((int)detailEffectMode, new[] { "Spots", "Blush" });
                detailSeed = EditorGUILayout.IntField(new GUIContent("Seed", "The same seed and settings generate the same details."), detailSeed);
                detailStrength = EditorGUILayout.Slider("Strength", detailStrength, 0f, 1f);
                detailUseEdgeFalloff = EditorGUILayout.Toggle("Edge Falloff", detailUseEdgeFalloff);
                using (new EditorGUI.DisabledScope(!detailUseEdgeFalloff))
                {
                    detailFalloffDistancePixels = EditorGUILayout.Slider(new GUIContent("Falloff Distance (px)", "Distance inward from the Bezier boundary before full strength is reached."), detailFalloffDistancePixels, 0f, 512f);
                }

                EditorGUILayout.Space(4f);
                if (detailEffectMode == DetailEffectMode.Spots)
                {
                    detailSpotColor = EditorGUILayout.ColorField("Spot Color", detailSpotColor);
                    detailSpotColorVariation = EditorGUILayout.Slider("Color Variation", detailSpotColorVariation, 0f, 1f);
                    detailSpotDensityPer10kPixels = EditorGUILayout.Slider(new GUIContent("Density", "Approximate spots per 10,000 affected pixels."), detailSpotDensityPer10kPixels, 0f, 120f);
                    detailSpotDensityVariation = EditorGUILayout.Slider("Density Variation", detailSpotDensityVariation, 0f, 1f);
                    detailSpotSizePixels = EditorGUILayout.Slider(new GUIContent("Size (px)", "Average spot radius in texture pixels."), detailSpotSizePixels, 0.5f, 64f);
                    detailSpotSizeVariation = EditorGUILayout.Slider("Size Variation", detailSpotSizeVariation, 0f, 1f);
                }
                else
                {
                    detailBlushColor = EditorGUILayout.ColorField("Blush Color", detailBlushColor);
                    detailBlushOpacity = EditorGUILayout.Slider("Opacity", detailBlushOpacity, 0f, 1f);
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reset Area"))
                {
                    ResetDetailAreaToDefaultCircle(currentTexture.width, currentTexture.height);
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(!CanApplyAddDetails()))
                {
                    if (GUILayout.Button("Mirror Area Right"))
                    {
                        MirrorDetailAreaRight();
                    }
                }
                EditorGUILayout.EndHorizontal();

                string applyLabel = detailEffectMode == DetailEffectMode.Spots ? "Apply Spots" : "Apply Blush";
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!CanApplyAddDetails()))
                {
                    if (GUILayout.Button(applyLabel))
                    {
                        ApplyAddDetails(false);
                    }

                    if (GUILayout.Button("Mirror Effect Right"))
                    {
                        ApplyAddDetails(true);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private bool AreGradientSettingsChanged()
        {
            return !gradientApplied
                || gradientMode != lastGradientMode
                || gradientFrom != lastGradientFrom
                || !Mathf.Approximately(solidPercent, lastSolidPercent)
                || !Mathf.Approximately(gradientPercent, lastGradientPercent)
                || !Mathf.Approximately(radialSolidHorizontalPercent, lastRadialSolidHorizontalPercent)
                || !Mathf.Approximately(radialSolidVerticalPercent, lastRadialSolidVerticalPercent)
                || !Mathf.Approximately(radialGradientHorizontalPercent, lastRadialGradientHorizontalPercent)
                || !Mathf.Approximately(radialGradientVerticalPercent, lastRadialGradientVerticalPercent)
                || !Mathf.Approximately(radialCenterOffsetHorizontalPercent, lastRadialCenterOffsetHorizontalPercent)
                || !Mathf.Approximately(radialCenterOffsetVerticalPercent, lastRadialCenterOffsetVerticalPercent);
        }

        private void PromptAndSaveCurrentPreset()
        {
            string initialName = presetName;
            if (selectedParameterPresetIndex >= 0 && selectedParameterPresetIndex < parameterPresets.Count)
            {
                initialName = parameterPresets[selectedParameterPresetIndex].name;
            }

            PresetNamePromptWindow.Open(initialName, SaveCurrentPreset);
        }

        private void SaveCurrentPreset(string presetNameToSave)
        {
            string trimmedName = presetNameToSave.Trim();
            TextureParameterPreset preset = CreateCurrentPreset(trimmedName);
            int existingIndex = parameterPresets.FindIndex(p => string.Equals(p.name, trimmedName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                parameterPresets[existingIndex] = preset;
            }
            else
            {
                parameterPresets.Add(preset);
            }

            parameterPresets.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            SaveParameterPresets();
            selectedParameterPresetIndex = GetPresetPopupIndex(trimmedName);
            presetName = trimmedName;
            Repaint();
        }

        private TextureParameterPreset CreateCurrentPreset(string name)
        {
            return new TextureParameterPreset
            {
                name = name,
                brightness = brightness,
                contrast = contrast,
                saturation = saturation,
                hueDegrees = hueDegrees,
                hasAlphaFromLuminanceCutoff = true,
                alphaFromLuminanceCutoff = alphaFromLuminanceCutoff,
                alphaFillRadiusPixels = alphaFillRadiusPixels,
                alphaFillAlphaThreshold = alphaFillAlphaThreshold,
                gradientMode = gradientMode,
                gradientFrom = gradientFrom,
                solidPercent = solidPercent,
                gradientPercent = gradientPercent,
                radialSolidHorizontalPercent = radialSolidHorizontalPercent,
                radialSolidVerticalPercent = radialSolidVerticalPercent,
                radialGradientHorizontalPercent = radialGradientHorizontalPercent,
                radialGradientVerticalPercent = radialGradientVerticalPercent,
                radialCenterOffsetHorizontalPercent = radialCenterOffsetHorizontalPercent,
                radialCenterOffsetVerticalPercent = radialCenterOffsetVerticalPercent,
            };
        }

        private void ApplyPreset(TextureParameterPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            presetName = preset.name;
            brightness = preset.brightness;
            contrast = preset.contrast;
            saturation = preset.saturation;
            hueDegrees = preset.hueDegrees;
            if (preset.hasAlphaFromLuminanceCutoff)
            {
                alphaFromLuminanceCutoff = Mathf.Clamp01(preset.alphaFromLuminanceCutoff);
            }
            InvalidatePreview();
        }

        private void ApplySelectedPreset()
        {
            if (selectedParameterPresetIndex < 0 || selectedParameterPresetIndex >= parameterPresets.Count)
            {
                return;
            }

            ApplyPreset(parameterPresets[selectedParameterPresetIndex]);
        }

        private void LoadParameterPresets()
        {
            parameterPresets.Clear();
            string json = EditorPrefs.GetString(PresetPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                TextureParameterPresetCollection collection = JsonUtility.FromJson<TextureParameterPresetCollection>(json);
                if (collection != null && collection.presets != null)
                {
                    foreach (TextureParameterPreset preset in collection.presets)
                    {
                        if (preset != null && !string.IsNullOrWhiteSpace(preset.name))
                        {
                            parameterPresets.Add(preset);
                        }
                    }
                }
            }

            parameterPresets.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            RefreshParameterPresetOptions();
        }

        private void SaveParameterPresets()
        {
            TextureParameterPresetCollection collection = new TextureParameterPresetCollection
            {
                presets = parameterPresets,
            };
            EditorPrefs.SetString(PresetPrefsKey, JsonUtility.ToJson(collection));
            RefreshParameterPresetOptions();
        }

        private void RefreshParameterPresetOptions()
        {
            if (parameterPresets.Count == 0)
            {
                parameterPresetOptions = new[] { "(No Presets)" };
                selectedParameterPresetIndex = 0;
                return;
            }

            parameterPresetOptions = new string[parameterPresets.Count];
            for (int i = 0; i < parameterPresets.Count; i++)
            {
                parameterPresetOptions[i] = parameterPresets[i].name;
            }

            selectedParameterPresetIndex = Mathf.Clamp(selectedParameterPresetIndex, 0, parameterPresetOptions.Length - 1);
        }

        private int GetPresetPopupIndex(string name)
        {
            for (int i = 0; i < parameterPresets.Count; i++)
            {
                if (string.Equals(parameterPresets[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        // ---------- Loading / Saving ----------

        private bool LoadFromAsset(Texture2D asset)
        {
            Texture2D loadedTexture = null;
            QueuedTextureState loadedState = null;
            if (asset != null)
            {
                loadedState = GetQueuedTextureState(asset, false);
                if (loadedState != null && loadedState.workingTexture != null)
                {
                    loadedTexture = loadedState.workingTexture;
                }
                else
                {
                    try
                    {
                        loadedTexture = MakeReadableCopy(asset);
                    }
                    catch (Exception ex)
                    {
                        EditorUtility.DisplayDialog("Load Texture", "Error: " + ex.Message, "OK");
                        return false;
                    }
                }
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref diskOriginalTexture);
            diskOriginalTextureDirectory = null;
            DestroyTexture(ref previewTexture);
            dirty = loadedState != null && loadedState.dirty;
            ResetAdjustments();
            ResetMagnifiedPreviewCenter();
            currentTexture = loadedTexture;
            return true;
        }

        private void LoadFromDisk()
        {
            string path = EditorUtility.OpenFilePanel("Load Texture", Application.dataPath, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Texture2D previousSourceAsset = sourceAsset;
            int previousDroppedTextureIndex = selectedDroppedTextureIndex;
            bool preservedCurrentQueuedTexture = PreserveCurrentQueuedTextureState();
            if (!preservedCurrentQueuedTexture && !PromptSaveIfDirty("loading a new texture"))
            {
                return;
            }

            Texture2D tex = null;
            Texture2D originalTexture = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    DestroyTexture(ref tex);
                    if (preservedCurrentQueuedTexture)
                    {
                        RestoreQueuedTextureState(previousSourceAsset, previousDroppedTextureIndex);
                    }

                    EditorUtility.DisplayDialog("Load Texture", "Failed to load image at: " + path, "OK");
                    return;
                }

                tex.name = Path.GetFileNameWithoutExtension(path);
                originalTexture = DuplicateReadableTexture(tex);
                originalTexture.hideFlags = HideFlags.HideAndDontSave;

                InvalidateCachedPixels();
                DestroyTexture(ref currentTexture);
                DestroyTexture(ref diskOriginalTexture);
                DestroyTexture(ref previewTexture);
                sourceAsset = null;
                selectedDroppedTextureIndex = -1;
                currentTexture = tex;
                tex = null;
                diskOriginalTexture = originalTexture;
                originalTexture = null;
                diskOriginalTextureDirectory = Path.GetDirectoryName(path);
                SetCurrentDirty(false);
                ResetAdjustments();
                ResetMagnifiedPreviewCenter();
            }
            catch (Exception ex)
            {
                DestroyTexture(ref tex);
                DestroyTexture(ref originalTexture);
                if (preservedCurrentQueuedTexture)
                {
                    RestoreQueuedTextureState(previousSourceAsset, previousDroppedTextureIndex);
                }

                EditorUtility.DisplayDialog("Load Texture", "Error: " + ex.Message, "OK");
            }
        }

        private bool SaveCurrentAsPng()
        {
            if (currentTexture == null)
            {
                return false;
            }

            // If adjustments are non-zero, bake first so the saved PNG matches the preview.
            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            string defaultName = string.IsNullOrEmpty(currentTexture.name) ? "Texture" : currentTexture.name;
            string path = EditorUtility.SaveFilePanel("Save Texture As PNG", GetSaveAsInitialDirectory(), defaultName + ".png", "png");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                byte[] png;
                Texture2D outputTexture = null;
                if (combineBackgroundOnSave && backgroundAsset != null)
                {
                    outputTexture = CreateCombinedOutputTexture();
                    png = outputTexture.EncodeToPNG();
                }
                else
                {
                    png = currentTexture.EncodeToPNG();
                }

                File.WriteAllBytes(path, png);
                ImportIfInProject(path);
                SetCurrentDirty(false);
                DestroyTexture(ref outputTexture);
                EditorUtility.DisplayDialog("Save Texture", "Saved: " + path, "OK");
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Save Texture", "Error: " + ex.Message, "OK");
                return false;
            }
        }

        private string GetSaveAsInitialDirectory()
        {
            if (sourceAsset != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(sourceAsset);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    string sourceDirectory = Path.GetDirectoryName(GetAbsoluteProjectPath(assetPath));
                    if (!string.IsNullOrEmpty(sourceDirectory) && Directory.Exists(sourceDirectory))
                    {
                        return sourceDirectory;
                    }
                }
            }

            if (!string.IsNullOrEmpty(diskOriginalTextureDirectory) && Directory.Exists(diskOriginalTextureDirectory))
            {
                return diskOriginalTextureDirectory;
            }

            return Application.dataPath;
        }

        private bool CanQuickSaveOverwrite()
        {
            if (currentTexture == null || sourceAsset == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(sourceAsset);
            return !string.IsNullOrEmpty(assetPath)
                && string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase);
        }

        private bool QuickSaveAndOverwrite(bool confirmOverwrite = true)
        {
            if (!CanQuickSaveOverwrite())
            {
                return false;
            }

            if (confirmOverwrite)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Overwrite Original Texture?",
                    "This will overwrite the original texture. Are you sure?",
                    "Yes",
                    "No");
                if (!confirmed)
                {
                    return false;
                }
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            string assetPath = AssetDatabase.GetAssetPath(sourceAsset);
            string absolutePath = GetAbsoluteProjectPath(assetPath);
            try
            {
                byte[] png = currentTexture.EncodeToPNG();
                File.WriteAllBytes(absolutePath, png);
                ImportIfInProject(absolutePath);
                AssetDatabase.Refresh();
                SetCurrentDirty(false);
                return true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Save Texture", "Error: " + ex.Message, "OK");
                return false;
            }
        }

        private Texture2D CreateCombinedOutputTexture()
        {
            Texture2D backgroundCopy = MakeReadableCopy(backgroundAsset);
            try
            {
                int width = currentTexture.width;
                int height = currentTexture.height;
                Texture2D output = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = currentTexture.name,
                };

                Rect backgroundRect = FitBackgroundRect(new Rect(0f, 0f, width, height), backgroundCopy.width, backgroundCopy.height, backgroundScaleMode);
                Color[] composed = new Color[width * height];
                Color32[] foregroundPixels = currentTexture.GetPixels32();

                for (int y = 0; y < height; y++)
                {
                    float yCenter = y + 0.5f;
                    for (int x = 0; x < width; x++)
                    {
                        float xCenter = x + 0.5f;
                        Color backgroundColor = Color.clear;
                        if (backgroundRect.Contains(new Vector2(xCenter, yCenter)))
                        {
                            float u = Mathf.InverseLerp(backgroundRect.xMin, backgroundRect.xMax, xCenter);
                            float v = Mathf.InverseLerp(backgroundRect.yMin, backgroundRect.yMax, yCenter);
                            backgroundColor = backgroundCopy.GetPixelBilinear(u, v);
                        }

                        Color foregroundColor = foregroundPixels[(y * width) + x];
                        composed[(y * width) + x] = CompositeOver(backgroundColor, foregroundColor);
                    }
                }

                output.SetPixels(composed);
                output.Apply(false, false);
                return output;
            }
            finally
            {
                DestroyTexture(ref backgroundCopy);
            }
        }

        private static Texture2D MakeReadableCopy(Texture2D src)
        {
            int w = src.width;
            int h = src.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                copy.name = src.name;
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void ImportIfInProject(string absolutePath)
        {
            string dataPath = Application.dataPath.Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(dataPath))
            {
                string assetPath = "Assets" + normalized.Substring(dataPath.Length);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        private static string GetAbsoluteProjectPath(string assetPath)
        {
            string normalizedAssetPath = assetPath.Replace('\\', '/');
            if (!normalizedAssetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(assetPath);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, normalizedAssetPath);
        }

        // ---------- Split ----------

        private void SplitCurrentTexture()
        {
            if (currentTexture == null) return;

            string folder = EditorUtility.OpenFolderPanel("Choose Output Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(folder)) return;

            // Bake any pending live adjustments so split pieces match what user sees.
            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            int w = currentTexture.width;
            int h = currentTexture.height;
            Color[] src = currentTexture.GetPixels();

            int pieceCount;
            int pw, ph;
            int[] xs, ys;
            switch (splitDirection)
            {
                case SplitDirection.Vertical:
                    pieceCount = 2;
                    pw = w / 2; ph = h;
                    xs = new[] { 0, pw }; ys = new[] { 0, 0 };
                    break;
                case SplitDirection.Horizontal:
                    pieceCount = 2;
                    pw = w; ph = h / 2;
                    // numbering top -> bottom
                    xs = new[] { 0, 0 }; ys = new[] { ph, 0 };
                    break;
                case SplitDirection.Both:
                default:
                    pieceCount = 4;
                    pw = w / 2; ph = h / 2;
                    // TL, TR, BL, BR (1..4)
                    xs = new[] { 0, pw, 0, pw };
                    ys = new[] { ph, ph, 0, 0 };
                    break;
            }

            if (pw <= 0 || ph <= 0)
            {
                EditorUtility.DisplayDialog("Split Texture", "Texture is too small to split in the chosen direction.", "OK");
                return;
            }

            try
            {
                for (int i = 0; i < pieceCount; i++)
                {
                    Color[] pixels = ExtractRegion(src, w, h, xs[i], ys[i], pw, ph);
                    Texture2D piece = new Texture2D(pw, ph, TextureFormat.RGBA32, false, false);
                    piece.SetPixels(pixels);
                    piece.Apply(false, false);
                    string fileName = $"{splitBaseName}{i + 1}.png";
                    string outPath = Path.Combine(folder, fileName).Replace('\\', '/');
                    File.WriteAllBytes(outPath, piece.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(piece);
                    ImportIfInProject(outPath);
                }
                EditorUtility.DisplayDialog("Split Texture", $"Wrote {pieceCount} piece(s) to:\n{folder}", "OK");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Split Texture", "Error: " + ex.Message, "OK");
            }
        }

        private static Color[] ExtractRegion(Color[] src, int srcW, int srcH, int x, int y, int w, int h)
        {
            Color[] dst = new Color[w * h];
            for (int row = 0; row < h; row++)
            {
                int srcRow = y + row;
                Array.Copy(src, srcRow * srcW + x, dst, row * w, w);
            }
            return dst;
        }

        // ---------- Alpha gradient ----------

        private void ApplyAlphaGradient()
        {
            if (currentTexture == null) return;

            // Bake pending live adjustments first so behavior is predictable.
            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            int w = currentTexture.width;
            int h = currentTexture.height;
            float solidNorm = Mathf.Clamp01(solidPercent / 100f);
            float gradNorm = Mathf.Clamp01(gradientPercent / 100f);
            if (solidNorm + gradNorm > 1f)
            {
                gradNorm = Mathf.Max(0f, 1f - solidNorm);
            }

            EnsureCachedPixels();
            Color32[] pixels = cachedCurrentPixels;
            if (gradientMode == GradientMode.Linear)
            {
                ApplyLinearAlphaGradient(pixels, w, h, solidNorm, gradNorm);
            }
            else
            {
                ApplyRadialAlphaGradient(pixels, w, h);
            }

            currentTexture.SetPixels32(pixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            gradientApplied = true;
            lastGradientMode = gradientMode;
            lastGradientFrom = gradientFrom;
            lastSolidPercent = solidPercent;
            lastGradientPercent = gradientPercent;
            lastRadialSolidHorizontalPercent = radialSolidHorizontalPercent;
            lastRadialSolidVerticalPercent = radialSolidVerticalPercent;
            lastRadialGradientHorizontalPercent = radialGradientHorizontalPercent;
            lastRadialGradientVerticalPercent = radialGradientVerticalPercent;
            lastRadialCenterOffsetHorizontalPercent = radialCenterOffsetHorizontalPercent;
            lastRadialCenterOffsetVerticalPercent = radialCenterOffsetVerticalPercent;
            InvalidatePreview();
        }

        private void ApplyLinearAlphaGradient(Color32[] pixels, int width, int height, float solidNorm, float gradNorm)
        {
            ApplyAlphaGradientInRanges(
                pixels,
                (rangePixels, start, end) => ApplyLinearAlphaGradientRange(rangePixels, start, end, width, height, solidNorm, gradNorm));
        }

        private void ApplyLinearAlphaGradientRange(Color32[] pixels, int start, int end, int width, int height, float solidNorm, float gradNorm)
        {
            for (int index = start; index < end; index++)
            {
                int x = index % width;
                int y = index / width;
                float t;
                switch (gradientFrom)
                {
                    case GradientFrom.Left: t = width <= 1 ? 0f : (float)x / (width - 1); break;
                    case GradientFrom.Right: t = width <= 1 ? 0f : (float)(width - 1 - x) / (width - 1); break;
                    case GradientFrom.Down: t = height <= 1 ? 0f : (float)y / (height - 1); break;
                    case GradientFrom.Up:
                    default: t = height <= 1 ? 0f : (float)(height - 1 - y) / (height - 1); break;
                }

                float alpha;
                if (t <= solidNorm)
                {
                    alpha = 1f;
                }
                else if (gradNorm > 0f && t <= solidNorm + gradNorm)
                {
                    alpha = 1f - ((t - solidNorm) / gradNorm);
                }
                else
                {
                    alpha = 0f;
                }

                Color32 color = pixels[index];
                color.a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[index] = color;
            }
        }

        private void ApplyRadialAlphaGradient(Color32[] pixels, int width, int height)
        {
            float solidHorizontalRadius = Mathf.Clamp01(radialSolidHorizontalPercent / 200f);
            float solidVerticalRadius = Mathf.Clamp01(radialSolidVerticalPercent / 200f);
            float gradientHorizontalRadius = Mathf.Clamp01(radialGradientHorizontalPercent / 200f);
            float gradientVerticalRadius = Mathf.Clamp01(radialGradientVerticalPercent / 200f);
            float outerHorizontalRadius = Mathf.Clamp01(solidHorizontalRadius + gradientHorizontalRadius);
            float outerVerticalRadius = Mathf.Clamp01(solidVerticalRadius + gradientVerticalRadius);
            float centerX = Mathf.Clamp01(0.5f + (radialCenterOffsetHorizontalPercent / 200f));
            float centerY = Mathf.Clamp01(0.5f + (radialCenterOffsetVerticalPercent / 200f));

            ApplyAlphaGradientInRanges(
                pixels,
                (rangePixels, start, end) => ApplyRadialAlphaGradientRange(
                    rangePixels,
                    start,
                    end,
                    width,
                    height,
                    solidHorizontalRadius,
                    solidVerticalRadius,
                    outerHorizontalRadius,
                    outerVerticalRadius,
                    centerX,
                    centerY));
        }

        private static void ApplyRadialAlphaGradientRange(
            Color32[] pixels,
            int start,
            int end,
            int width,
            int height,
            float solidHorizontalRadius,
            float solidVerticalRadius,
            float outerHorizontalRadius,
            float outerVerticalRadius,
            float centerX,
            float centerY)
        {
            for (int index = start; index < end; index++)
            {
                int x = index % width;
                int y = index / width;
                float yNorm = height <= 1 ? 0.5f : (float)y / (height - 1);
                float xNorm = width <= 1 ? 0.5f : (float)x / (width - 1);
                float alpha = EvaluateRadialAlpha(
                    xNorm - centerX,
                    yNorm - centerY,
                    solidHorizontalRadius,
                    solidVerticalRadius,
                    outerHorizontalRadius,
                    outerVerticalRadius);

                Color32 color = pixels[index];
                color.a = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[index] = color;
            }
        }

        private static void ApplyAlphaGradientInRanges(Color32[] pixels, Action<Color32[], int, int> applyRange)
        {
            int pixelCount = pixels.Length;
            if (pixelCount == 0)
            {
                return;
            }

            if (pixelCount < BcsParallelPixelThreshold)
            {
                applyRange(pixels, 0, pixelCount);
                return;
            }

            int maxUsefulWorkers = Math.Max(1, pixelCount / BcsParallelMinPixelsPerWorker);
            int workerCount = Math.Min(Environment.ProcessorCount, maxUsefulWorkers);
            if (workerCount <= 1)
            {
                applyRange(pixels, 0, pixelCount);
                return;
            }

            int pixelsPerWorker = (pixelCount + workerCount - 1) / workerCount;
            Parallel.For(0, workerCount, workerIndex =>
            {
                int start = workerIndex * pixelsPerWorker;
                int end = Math.Min(start + pixelsPerWorker, pixelCount);
                if (start < end)
                {
                    applyRange(pixels, start, end);
                }
            });
        }

        private static float EvaluateRadialAlpha(
            float deltaX,
            float deltaY,
            float solidHorizontalRadius,
            float solidVerticalRadius,
            float outerHorizontalRadius,
            float outerVerticalRadius)
        {
            const float Epsilon = 1e-5f;
            float pointDistance = Mathf.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
            if (pointDistance <= Epsilon)
            {
                return 1f;
            }

            float directionX = deltaX / pointDistance;
            float directionY = deltaY / pointDistance;
            float innerBoundary = ComputeEllipseBoundaryDistance(directionX, directionY, solidHorizontalRadius, solidVerticalRadius, Epsilon);
            float outerBoundary = ComputeEllipseBoundaryDistance(directionX, directionY, outerHorizontalRadius, outerVerticalRadius, Epsilon);

            if (pointDistance <= innerBoundary)
            {
                return 1f;
            }

            if (outerBoundary <= innerBoundary + Epsilon)
            {
                return 0f;
            }

            if (pointDistance >= outerBoundary)
            {
                return 0f;
            }

            return 1f - ((pointDistance - innerBoundary) / (outerBoundary - innerBoundary));
        }

        private static float ComputeEllipseBoundaryDistance(float directionX, float directionY, float radiusX, float radiusY, float epsilon)
        {
            float clampedRadiusX = Mathf.Max(radiusX, epsilon);
            float clampedRadiusY = Mathf.Max(radiusY, epsilon);
            float denominator = Mathf.Sqrt(
                ((directionX * directionX) / (clampedRadiusX * clampedRadiusX))
                + ((directionY * directionY) / (clampedRadiusY * clampedRadiusY)));
            return denominator <= epsilon ? 0f : 1f / denominator;
        }

        // ---------- Alpha fill ----------

        private void ApplyAlphaFill()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            int radiusPixels = Mathf.Clamp(alphaFillRadiusPixels, 1, 256);
            bool changed = FillTransparentRgbFromNearestOpaque(
                cachedCurrentPixels,
                currentTexture.width,
                currentTexture.height,
                radiusPixels,
                alphaFillAlphaThreshold);

            if (!changed)
            {
                EditorUtility.DisplayDialog("Alpha Fill", "No pixels were filled. Try a larger radius or a lower alpha threshold.", "OK");
                return;
            }

            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private static bool FillTransparentRgbFromNearestOpaque(Color32[] pixels, int width, int height, int radiusPixels, float alphaThreshold)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0 || radiusPixels <= 0)
            {
                return false;
            }

            int pixelCount = pixels.Length;
            int threshold = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(alphaThreshold) * 255f), 0, 254);
            byte thresholdByte = (byte)threshold;
            int[] distances = new int[pixelCount];
            int[] queue = new int[pixelCount];
            int head = 0;
            int tail = 0;

            for (int index = 0; index < pixelCount; index++)
            {
                if (pixels[index].a > thresholdByte)
                {
                    distances[index] = 0;
                    queue[tail++] = index;
                }
                else
                {
                    distances[index] = -1;
                }
            }

            if (tail == 0 || tail == pixelCount)
            {
                return false;
            }

            bool changed = false;
            while (head < tail)
            {
                int sourceIndex = queue[head++];
                int nextDistance = distances[sourceIndex] + 1;
                if (nextDistance > radiusPixels)
                {
                    continue;
                }

                int x = sourceIndex % width;
                int y = sourceIndex / width;
                bool hasLeft = x > 0;
                bool hasRight = x < width - 1;
                bool hasDown = y > 0;
                bool hasUp = y < height - 1;

                if (hasLeft)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex - 1, nextDistance);
                }
                if (hasRight)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex + 1, nextDistance);
                }
                if (hasDown)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex - width, nextDistance);
                }
                if (hasUp)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex + width, nextDistance);
                }
                if (hasLeft && hasDown)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex - width - 1, nextDistance);
                }
                if (hasRight && hasDown)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex - width + 1, nextDistance);
                }
                if (hasLeft && hasUp)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex + width - 1, nextDistance);
                }
                if (hasRight && hasUp)
                {
                    changed |= TryQueueAlphaFillPixel(pixels, distances, queue, ref tail, sourceIndex, sourceIndex + width + 1, nextDistance);
                }
            }

            return changed;
        }

        private static bool TryQueueAlphaFillPixel(
            Color32[] pixels,
            int[] distances,
            int[] queue,
            ref int tail,
            int sourceIndex,
            int targetIndex,
            int distance)
        {
            if (distances[targetIndex] >= 0)
            {
                return false;
            }

            Color32 source = pixels[sourceIndex];
            Color32 target = pixels[targetIndex];
            target.r = source.r;
            target.g = source.g;
            target.b = source.b;
            pixels[targetIndex] = target;
            distances[targetIndex] = distance;
            queue[tail++] = targetIndex;
            return true;
        }

        // ---------- Add Details ----------

        private void ApplyAddDetails(bool mirrorEffectRight)
        {
            if (currentTexture == null)
            {
                return;
            }

            EnsureDetailAreaInitialized(currentTexture.width, currentTexture.height);
            if (detailPoints.Count < DetailMinCurvePoints)
            {
                EditorUtility.DisplayDialog("Add Details", "The detail area needs at least three points.", "OK");
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            DetailAreaMask mask = BuildDetailAreaMask(currentTexture.width, currentTexture.height);
            if (mask == null || mask.insidePixelCount <= 0)
            {
                EditorUtility.DisplayDialog("Add Details", "The selected area is too small to affect any pixels.", "OK");
                return;
            }

            bool changed;
            try
            {
                changed = detailEffectMode == DetailEffectMode.Spots
                    ? ApplySpotDetails(cachedCurrentPixels, currentTexture.width, currentTexture.height, mask, mirrorEffectRight)
                    : ApplyBlushDetails(cachedCurrentPixels, currentTexture.width, currentTexture.height, mask, mirrorEffectRight);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!changed)
            {
                EditorUtility.DisplayDialog("Add Details", "No pixels were changed. Try stronger settings or a larger area.", "OK");
                return;
            }

            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private DetailAreaMask BuildDetailAreaMask(int textureWidth, int textureHeight)
        {
            List<Vector2> boundaryPixels = SampleDetailBoundaryPixels(textureWidth, textureHeight);
            if (boundaryPixels.Count < DetailMinCurvePoints)
            {
                return null;
            }

            float minXFloat = textureWidth;
            float maxXFloat = 0f;
            float minYFloat = textureHeight;
            float maxYFloat = 0f;
            for (int i = 0; i < boundaryPixels.Count; i++)
            {
                Vector2 point = boundaryPixels[i];
                minXFloat = Mathf.Min(minXFloat, point.x);
                maxXFloat = Mathf.Max(maxXFloat, point.x);
                minYFloat = Mathf.Min(minYFloat, point.y);
                maxYFloat = Mathf.Max(maxYFloat, point.y);
            }

            int minX = Mathf.Clamp(Mathf.FloorToInt(minXFloat) - 1, 0, textureWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(maxXFloat) + 1, 0, textureWidth - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt(minYFloat) - 1, 0, textureHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(maxYFloat) + 1, 0, textureHeight - 1);
            if (minX > maxX || minY > maxY)
            {
                return null;
            }

            DetailAreaMask mask = new DetailAreaMask
            {
                minX = minX,
                maxX = maxX,
                minY = minY,
                maxY = maxY,
                boxWidth = (maxX - minX) + 1,
                boxHeight = (maxY - minY) + 1,
            };
            mask.strengths = new byte[mask.boxWidth * mask.boxHeight];

            float falloffDistance = detailUseEdgeFalloff ? Mathf.Max(0.001f, detailFalloffDistancePixels) : 0f;
            for (int y = minY; y <= maxY; y++)
            {
                float pixelCenterY = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 pixelCenter = new Vector2(x + 0.5f, pixelCenterY);
                    if (!IsPointInsidePolygon(pixelCenter, boundaryPixels))
                    {
                        continue;
                    }

                    float strength = falloffDistance > 0f ? GetBoundaryFalloffStrength(pixelCenter, boundaryPixels, falloffDistance) : 1f;
                    byte strengthByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f);
                    if (strengthByte == 0)
                    {
                        continue;
                    }

                    int maskIndex = ((y - minY) * mask.boxWidth) + (x - minX);
                    mask.strengths[maskIndex] = strengthByte;
                    mask.insidePixelCount++;
                }
            }

            return mask;
        }

        private List<Vector2> SampleDetailBoundaryPixels(int textureWidth, int textureHeight)
        {
            List<Vector2> points = new List<Vector2>();
            if (detailPoints.Count < DetailMinCurvePoints)
            {
                return points;
            }

            for (int i = 0; i < detailPoints.Count; i++)
            {
                DetailCurvePoint point = detailPoints[i];
                DetailCurvePoint nextPoint = detailPoints[GetNextDetailPointIndex(i)];
                if (i == 0)
                {
                    points.Add(NormalizedTextureToPixelPoint(point.position, textureWidth, textureHeight));
                }

                for (int sample = 1; sample <= DetailCurveSamplesPerSegment; sample++)
                {
                    float t = (float)sample / DetailCurveSamplesPerSegment;
                    Vector2 sampled = EvaluateCubic(point.position, point.outHandle, nextPoint.inHandle, nextPoint.position, t);
                    points.Add(NormalizedTextureToPixelPoint(sampled, textureWidth, textureHeight));
                }
            }

            return points;
        }

        private static bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int count = polygon == null ? 0 : polygon.Count;
            if (count < DetailMinCurvePoints)
            {
                return false;
            }

            int previousIndex = count - 1;
            for (int i = 0; i < count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 previous = polygon[previousIndex];
                bool crosses = (current.y > point.y) != (previous.y > point.y);
                if (crosses)
                {
                    float intersectionX = ((previous.x - current.x) * (point.y - current.y) / (previous.y - current.y)) + current.x;
                    if (point.x < intersectionX)
                    {
                        inside = !inside;
                    }
                }

                previousIndex = i;
            }

            return inside;
        }

        private static float GetBoundaryFalloffStrength(Vector2 point, List<Vector2> boundaryPixels, float falloffDistance)
        {
            float maxDistanceSquared = falloffDistance * falloffDistance;
            float minDistanceSquared = maxDistanceSquared;
            int count = boundaryPixels.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 start = boundaryPixels[i];
                Vector2 end = boundaryPixels[(i + 1) % count];
                float distanceSquared = DistancePointToSegmentSquared(point, start, end);
                if (distanceSquared < minDistanceSquared)
                {
                    minDistanceSquared = distanceSquared;
                }
            }

            return Mathf.Clamp01(Mathf.Sqrt(minDistanceSquared) / falloffDistance);
        }

        private bool ApplyBlushDetails(Color32[] pixels, int textureWidth, int textureHeight, DetailAreaMask mask, bool mirrorEffectRight)
        {
            if (pixels == null || mask == null)
            {
                return false;
            }

            float baseAmount = Mathf.Clamp01(detailStrength) * Mathf.Clamp01(detailBlushOpacity);
            if (baseAmount <= 0f)
            {
                return false;
            }

            int maxSourceX = mirrorEffectRight ? Mathf.Min(mask.maxX, GetDetailMirrorSourceMaxX(textureWidth)) : mask.maxX;
            if (mask.minX > maxSourceX)
            {
                return false;
            }

            Color blushColor = detailBlushColor;
            return ApplyDetailRowsWithProgress(
                "Apply Blush",
                "Applying blush details",
                mask.minY,
                mask.maxY,
                0f,
                1f,
                pixelY => ApplyBlushDetailsRow(pixels, textureWidth, mask, maxSourceX, mirrorEffectRight, baseAmount, blushColor, pixelY));
        }

        private bool ApplySpotDetails(Color32[] pixels, int textureWidth, int textureHeight, DetailAreaMask mask, bool mirrorEffectRight)
        {
            if (pixels == null || mask == null)
            {
                return false;
            }

            float density = Mathf.Max(0f, detailSpotDensityPer10kPixels);
            float strength = Mathf.Clamp01(detailStrength);
            if (density <= 0f || strength <= 0f)
            {
                return false;
            }

            System.Random random = new System.Random(detailSeed);
            int sourcePixelCount = mirrorEffectRight ? GetDetailMirrorSourcePixelCount(mask, textureWidth) : mask.insidePixelCount;
            if (sourcePixelCount <= 0)
            {
                return false;
            }

            float densityVariation = Mathf.Clamp01(detailSpotDensityVariation);
            float densityScale = 1f + (GetRandomRange(random, -0.5f, 0.5f) * densityVariation);
            int targetSpotCount = Mathf.RoundToInt(sourcePixelCount * density * densityScale / 10000f);
            targetSpotCount = Mathf.Clamp(targetSpotCount, 0, 100000);
            if (targetSpotCount <= 0)
            {
                return false;
            }

            List<DetailSpot> spots = GenerateDetailSpots(mask, targetSpotCount, random, mirrorEffectRight, textureWidth);
            if (spots.Count == 0)
            {
                return false;
            }

            int maxSourceX = mirrorEffectRight ? Mathf.Min(mask.maxX, GetDetailMirrorSourceMaxX(textureWidth)) : mask.maxX;
            EditorUtility.DisplayProgressBar("Apply Spots", "Preparing spots", 0.16f);
            DetailSpotApplication[] spotApplications = BuildDetailSpotApplications(spots, mask, maxSourceX);
            if (spotApplications.Length == 0)
            {
                return false;
            }

            EditorUtility.DisplayProgressBar("Apply Spots", "Preparing spot rows", 0.18f);
            List<int>[] spotRows = BuildDetailSpotRows(spotApplications, mask);

            return ApplyDetailRowsWithProgress(
                "Apply Spots",
                "Applying spots",
                mask.minY,
                mask.maxY,
                0.2f,
                1f,
                pixelY => ApplySpotDetailsRow(pixels, textureWidth, mask, spotApplications, spotRows, maxSourceX, mirrorEffectRight, strength, pixelY));
        }

        private List<DetailSpot> GenerateDetailSpots(DetailAreaMask mask, int targetSpotCount, System.Random random, bool mirrorEffectRight, int textureWidth)
        {
            List<DetailSpot> spots = new List<DetailSpot>(targetSpotCount);
            int maxAttempts = Mathf.Max(1000, targetSpotCount * 80);
            float sizeVariation = Mathf.Clamp01(detailSpotSizeVariation);
            int maxSourceX = mirrorEffectRight ? Mathf.Min(mask.maxX, GetDetailMirrorSourceMaxX(textureWidth)) : mask.maxX;
            if (mask.minX > maxSourceX)
            {
                return spots;
            }

            int progressInterval = Mathf.Max(256, maxAttempts / 100);
            for (int attempt = 0; attempt < maxAttempts && spots.Count < targetSpotCount; attempt++)
            {
                if (attempt % progressInterval == 0)
                {
                    float progress = Mathf.Lerp(0.02f, 0.16f, (float)attempt / maxAttempts);
                    EditorUtility.DisplayProgressBar("Apply Spots", $"Generating spots ({spots.Count}/{targetSpotCount})", progress);
                }

                int pixelX = random.Next(mask.minX, maxSourceX + 1);
                int pixelY = random.Next(mask.minY, mask.maxY + 1);
                if (mask.GetStrength(pixelX, pixelY) <= 0f)
                {
                    continue;
                }

                float densityAcceptance = GetDetailDensityAcceptance(pixelX, pixelY);
                if (GetRandomFloat(random) > densityAcceptance)
                {
                    continue;
                }

                float radius = Mathf.Max(0.35f, detailSpotSizePixels * (1f + GetRandomRange(random, -sizeVariation, sizeVariation)));
                Vector2 center = new Vector2(pixelX + GetRandomFloat(random), pixelY + GetRandomFloat(random));
                Color color = GetVariedDetailSpotColor(random);
                spots.Add(new DetailSpot(center, radius, color));
            }

            return spots;
        }

        private static bool ApplyBlushDetailsRow(
            Color32[] pixels,
            int textureWidth,
            DetailAreaMask mask,
            int maxSourceX,
            bool mirrorEffectRight,
            float baseAmount,
            Color blushColor,
            int pixelY)
        {
            bool changed = false;
            for (int pixelX = mask.minX; pixelX <= maxSourceX; pixelX++)
            {
                float amount = baseAmount * mask.GetStrength(pixelX, pixelY);
                if (amount <= 0f)
                {
                    continue;
                }

                int pixelIndex = (pixelY * textureWidth) + pixelX;
                if (BlendDetailRgbPixel(pixels, pixelIndex, blushColor, amount))
                {
                    changed = true;
                }

                if (mirrorEffectRight)
                {
                    int mirroredX = GetDetailMirrorTargetX(pixelX, textureWidth);
                    if (mirroredX != pixelX && BlendDetailRgbPixel(pixels, (pixelY * textureWidth) + mirroredX, blushColor, amount))
                    {
                        changed = true;
                    }
                }
            }

            return changed;
        }

        private static DetailSpotApplication[] BuildDetailSpotApplications(List<DetailSpot> spots, DetailAreaMask mask, int maxSourceX)
        {
            List<DetailSpotApplication> applications = new List<DetailSpotApplication>(spots.Count);
            for (int i = 0; i < spots.Count; i++)
            {
                DetailSpot spot = spots[i];
                float radius = Mathf.Max(0.35f, spot.radius);
                int minX = Mathf.Clamp(Mathf.FloorToInt(spot.center.x - radius), mask.minX, mask.maxX);
                int maxX = Mathf.Clamp(Mathf.CeilToInt(spot.center.x + radius), mask.minX, maxSourceX);
                int minY = Mathf.Clamp(Mathf.FloorToInt(spot.center.y - radius), mask.minY, mask.maxY);
                int maxY = Mathf.Clamp(Mathf.CeilToInt(spot.center.y + radius), mask.minY, mask.maxY);
                if (minX > maxX || minY > maxY)
                {
                    continue;
                }

                applications.Add(new DetailSpotApplication
                {
                    spot = spot,
                    radius = radius,
                    radiusSquared = radius * radius,
                    minX = minX,
                    maxX = maxX,
                    minY = minY,
                    maxY = maxY,
                });
            }

            return applications.ToArray();
        }

        private static List<int>[] BuildDetailSpotRows(DetailSpotApplication[] spotApplications, DetailAreaMask mask)
        {
            int rowCount = (mask.maxY - mask.minY) + 1;
            List<int>[] spotRows = new List<int>[rowCount];
            for (int spotIndex = 0; spotIndex < spotApplications.Length; spotIndex++)
            {
                if ((spotIndex & 255) == 0)
                {
                    float progress = Mathf.Lerp(0.18f, 0.2f, (float)spotIndex / spotApplications.Length);
                    EditorUtility.DisplayProgressBar("Apply Spots", $"Preparing spot rows ({spotIndex}/{spotApplications.Length})", progress);
                }

                DetailSpotApplication application = spotApplications[spotIndex];
                for (int pixelY = application.minY; pixelY <= application.maxY; pixelY++)
                {
                    int rowIndex = pixelY - mask.minY;
                    List<int> row = spotRows[rowIndex];
                    if (row == null)
                    {
                        row = new List<int>();
                        spotRows[rowIndex] = row;
                    }

                    row.Add(spotIndex);
                }
            }

            return spotRows;
        }

        private static bool ApplySpotDetailsRow(
            Color32[] pixels,
            int textureWidth,
            DetailAreaMask mask,
            DetailSpotApplication[] spotApplications,
            List<int>[] spotRows,
            int maxSourceX,
            bool mirrorEffectRight,
            float strength,
            int pixelY)
        {
            int rowIndex = pixelY - mask.minY;
            List<int> rowSpots = rowIndex >= 0 && rowIndex < spotRows.Length ? spotRows[rowIndex] : null;
            if (rowSpots == null || rowSpots.Count == 0)
            {
                return false;
            }

            bool changed = false;
            float pixelCenterY = pixelY + 0.5f;
            for (int pixelX = mask.minX; pixelX <= maxSourceX; pixelX++)
            {
                float areaStrength = mask.GetStrength(pixelX, pixelY);
                if (areaStrength <= 0f)
                {
                    continue;
                }

                float pixelCenterX = pixelX + 0.5f;
                for (int i = 0; i < rowSpots.Count; i++)
                {
                    DetailSpotApplication application = spotApplications[rowSpots[i]];
                    if (pixelX < application.minX || pixelX > application.maxX)
                    {
                        continue;
                    }

                    float deltaX = pixelCenterX - application.spot.center.x;
                    float deltaY = pixelCenterY - application.spot.center.y;
                    float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
                    if (distanceSquared > application.radiusSquared)
                    {
                        continue;
                    }

                    float radial = 1f - (Mathf.Sqrt(distanceSquared) / application.radius);
                    radial = radial * radial * (3f - (2f * radial));
                    float amount = Mathf.Clamp01(strength * areaStrength * radial);
                    if (amount <= 0f)
                    {
                        continue;
                    }

                    int pixelIndex = (pixelY * textureWidth) + pixelX;
                    if (BlendDetailRgbPixel(pixels, pixelIndex, application.spot.color, amount))
                    {
                        changed = true;
                    }

                    if (mirrorEffectRight)
                    {
                        int mirroredX = GetDetailMirrorTargetX(pixelX, textureWidth);
                        if (mirroredX != pixelX && BlendDetailRgbPixel(pixels, (pixelY * textureWidth) + mirroredX, application.spot.color, amount))
                        {
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        private static bool ApplyDetailRowsWithProgress(
            string title,
            string message,
            int minY,
            int maxY,
            float progressStart,
            float progressEnd,
            Func<int, bool> applyRow)
        {
            if (applyRow == null || minY > maxY)
            {
                return false;
            }

            int rowCount = (maxY - minY) + 1;
            int workerCount = GetDetailWorkerCount(rowCount);
            bool[] workerChanged = new bool[workerCount];
            int completedRows = 0;
            EditorUtility.DisplayProgressBar(title, message, Mathf.Clamp01(progressStart));

            try
            {
                if (workerCount <= 1)
                {
                    bool singleThreadChanged = false;
                    for (int pixelY = minY; pixelY <= maxY; pixelY++)
                    {
                        singleThreadChanged |= applyRow(pixelY);
                        completedRows++;
                        if ((completedRows & 7) == 0 || completedRows == rowCount)
                        {
                            DisplayDetailProgress(title, message, completedRows, rowCount, progressStart, progressEnd);
                        }
                    }

                    return singleThreadChanged;
                }

                int rowsPerWorker = (rowCount + workerCount - 1) / workerCount;
                Task[] tasks = new Task[workerCount];
                for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
                {
                    int capturedWorkerIndex = workerIndex;
                    int startY = minY + (workerIndex * rowsPerWorker);
                    int endYExclusive = Math.Min(startY + rowsPerWorker, maxY + 1);
                    tasks[workerIndex] = Task.Run(() =>
                    {
                        bool localChanged = false;
                        int pendingRows = 0;
                        for (int pixelY = startY; pixelY < endYExclusive; pixelY++)
                        {
                            localChanged |= applyRow(pixelY);
                            pendingRows++;
                            if (pendingRows >= 4)
                            {
                                Interlocked.Add(ref completedRows, pendingRows);
                                pendingRows = 0;
                            }
                        }

                        if (pendingRows > 0)
                        {
                            Interlocked.Add(ref completedRows, pendingRows);
                        }

                        workerChanged[capturedWorkerIndex] = localChanged;
                    });
                }

                while (!Task.WaitAll(tasks, 50))
                {
                    int done = Math.Min(rowCount, Interlocked.CompareExchange(ref completedRows, 0, 0));
                    DisplayDetailProgress(title, message, done, rowCount, progressStart, progressEnd);
                }

                Task.WaitAll(tasks);
                DisplayDetailProgress(title, message, rowCount, rowCount, progressStart, progressEnd);

                bool threadedChanged = false;
                for (int i = 0; i < workerChanged.Length; i++)
                {
                    threadedChanged |= workerChanged[i];
                }

                return threadedChanged;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static int GetDetailWorkerCount(int rowCount)
        {
            int processorCount = Math.Max(1, Environment.ProcessorCount);
            return Math.Max(1, Math.Min(processorCount, Math.Max(1, rowCount)));
        }

        private static void DisplayDetailProgress(string title, string message, int completedRows, int rowCount, float progressStart, float progressEnd)
        {
            float rowProgress = rowCount <= 0 ? 1f : Mathf.Clamp01((float)completedRows / rowCount);
            float progress = Mathf.Lerp(Mathf.Clamp01(progressStart), Mathf.Clamp01(progressEnd), rowProgress);
            EditorUtility.DisplayProgressBar(title, $"{message} ({completedRows}/{rowCount} rows)", progress);
        }

        private static bool RunHairifyRowsWithProgress(
            string title,
            string message,
            int minY,
            int maxY,
            float progressStart,
            float progressEnd,
            Func<int, bool> applyRow,
            ref bool cancelRequested)
        {
            if (applyRow == null || minY > maxY)
            {
                return false;
            }

            int rowCount = (maxY - minY) + 1;
            int workerCount = GetDetailWorkerCount(rowCount);
            bool[] workerChanged = new bool[workerCount];
            int completedRows = 0;
            int cancelFlag = cancelRequested ? 1 : 0;

            if (DisplayHairifyProgress(title, message, completedRows, rowCount, progressStart, progressEnd, cancelFlag != 0))
            {
                Volatile.Write(ref cancelFlag, 1);
            }

            if (workerCount <= 1)
            {
                bool singleThreadChanged = false;
                for (int pixelY = minY; pixelY <= maxY; pixelY++)
                {
                    if (Volatile.Read(ref cancelFlag) != 0)
                    {
                        break;
                    }

                    singleThreadChanged |= applyRow(pixelY);
                    completedRows++;
                    if ((completedRows & 7) == 0 || completedRows == rowCount)
                    {
                        if (DisplayHairifyProgress(title, message, completedRows, rowCount, progressStart, progressEnd, false))
                        {
                            Volatile.Write(ref cancelFlag, 1);
                        }
                    }
                }

                cancelRequested = Volatile.Read(ref cancelFlag) != 0;
                return singleThreadChanged;
            }

            int rowsPerWorker = (rowCount + workerCount - 1) / workerCount;
            Task[] tasks = new Task[workerCount];
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int capturedWorkerIndex = workerIndex;
                int startY = minY + (workerIndex * rowsPerWorker);
                int endYExclusive = Math.Min(startY + rowsPerWorker, maxY + 1);
                tasks[workerIndex] = Task.Run(() =>
                {
                    bool localChanged = false;
                    int pendingRows = 0;
                    for (int pixelY = startY; pixelY < endYExclusive; pixelY++)
                    {
                        if (Volatile.Read(ref cancelFlag) != 0)
                        {
                            break;
                        }

                        localChanged |= applyRow(pixelY);
                        pendingRows++;
                        if (pendingRows >= 4)
                        {
                            Interlocked.Add(ref completedRows, pendingRows);
                            pendingRows = 0;
                        }
                    }

                    if (pendingRows > 0)
                    {
                        Interlocked.Add(ref completedRows, pendingRows);
                    }

                    workerChanged[capturedWorkerIndex] = localChanged;
                });
            }

            while (!Task.WaitAll(tasks, 50))
            {
                int done = Math.Min(rowCount, Interlocked.CompareExchange(ref completedRows, 0, 0));
                bool canceling = Volatile.Read(ref cancelFlag) != 0;
                if (DisplayHairifyProgress(title, message, done, rowCount, progressStart, progressEnd, canceling))
                {
                    Volatile.Write(ref cancelFlag, 1);
                }
            }

            Task.WaitAll(tasks);
            cancelRequested = Volatile.Read(ref cancelFlag) != 0;
            if (!cancelRequested)
            {
                DisplayHairifyProgress(title, message, rowCount, rowCount, progressStart, progressEnd, false);
            }

            bool threadedChanged = false;
            for (int i = 0; i < workerChanged.Length; i++)
            {
                threadedChanged |= workerChanged[i];
            }

            return threadedChanged;
        }

        private static bool DisplayHairifyProgress(string title, string message, int completedRows, int rowCount, float progressStart, float progressEnd, bool canceling)
        {
            float rowProgress = rowCount <= 0 ? 1f : Mathf.Clamp01((float)completedRows / rowCount);
            float progress = Mathf.Lerp(Mathf.Clamp01(progressStart), Mathf.Clamp01(progressEnd), rowProgress);
            string displayMessage = canceling ? "Canceling..." : $"{message} ({completedRows}/{rowCount} rows)";
            return EditorUtility.DisplayCancelableProgressBar(title, displayMessage, progress);
        }

        private static int GetDetailMirrorSourcePixelCount(DetailAreaMask mask, int textureWidth)
        {
            if (mask == null)
            {
                return 0;
            }

            int count = 0;
            int maxSourceX = Mathf.Min(mask.maxX, GetDetailMirrorSourceMaxX(textureWidth));
            if (mask.minX > maxSourceX)
            {
                return 0;
            }

            for (int pixelY = mask.minY; pixelY <= mask.maxY; pixelY++)
            {
                for (int pixelX = mask.minX; pixelX <= maxSourceX; pixelX++)
                {
                    if (mask.GetStrength(pixelX, pixelY) > 0f)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static int GetDetailMirrorSourceMaxX(int textureWidth)
        {
            return Mathf.Max(0, (textureWidth - 1) / 2);
        }

        private static int GetDetailMirrorTargetX(int sourceX, int textureWidth)
        {
            return Mathf.Clamp((textureWidth - 1) - sourceX, 0, textureWidth - 1);
        }

        private static bool BlendDetailRgbPixel(Color32[] pixels, int pixelIndex, Color targetColor, float amount)
        {
            Color32 original = pixels[pixelIndex];
            Color32 blended = BlendRgb(original, targetColor, amount);
            if (!HasDifferentRgb(original, blended))
            {
                return false;
            }

            pixels[pixelIndex] = blended;
            return true;
        }

        private float GetDetailDensityAcceptance(int x, int y)
        {
            float variation = Mathf.Clamp01(detailSpotDensityVariation);
            if (variation <= 0f)
            {
                return 1f;
            }

            float seedX = (detailSeed & 1023) * 0.037f;
            float seedY = ((detailSeed >> 8) & 1023) * 0.041f;
            float noise = Mathf.PerlinNoise((x * 0.0125f) + seedX, (y * 0.0125f) + seedY);
            float clustered = Mathf.Clamp01(noise * 1.45f);
            return Mathf.Lerp(1f, clustered, variation);
        }

        private Color GetVariedDetailSpotColor(System.Random random)
        {
            float variation = Mathf.Clamp01(detailSpotColorVariation);
            if (variation <= 0f)
            {
                return detailSpotColor;
            }

            Color.RGBToHSV(detailSpotColor, out float hue, out float saturationValue, out float value);
            hue = Mathf.Repeat(hue + GetRandomRange(random, -0.08f * variation, 0.08f * variation), 1f);
            saturationValue = Mathf.Clamp01(saturationValue * (1f + GetRandomRange(random, -0.5f * variation, 0.5f * variation)));
            value = Mathf.Clamp01(value * (1f + GetRandomRange(random, -0.45f * variation, 0.45f * variation)));
            Color varied = Color.HSVToRGB(hue, saturationValue, value);
            varied.a = detailSpotColor.a;
            return varied;
        }

        private static float GetRandomFloat(System.Random random)
        {
            return (float)random.NextDouble();
        }

        private static float GetRandomRange(System.Random random, float min, float max)
        {
            return Mathf.Lerp(min, max, GetRandomFloat(random));
        }

        private static Color32 BlendRgb(Color32 original, Color targetColor, float amount)
        {
            float clampedAmount = Mathf.Clamp01(amount);
            byte red = (byte)Mathf.RoundToInt(Mathf.Lerp(original.r, Mathf.Clamp01(targetColor.r) * 255f, clampedAmount));
            byte green = (byte)Mathf.RoundToInt(Mathf.Lerp(original.g, Mathf.Clamp01(targetColor.g) * 255f, clampedAmount));
            byte blue = (byte)Mathf.RoundToInt(Mathf.Lerp(original.b, Mathf.Clamp01(targetColor.b) * 255f, clampedAmount));
            return new Color32(red, green, blue, original.a);
        }

        private static bool HasDifferentRgb(Color32 left, Color32 right)
        {
            return left.r != right.r || left.g != right.g || left.b != right.b;
        }

        // ---------- Adjustments (BCS) ----------

        private bool HasPendingAdjustments()
        {
            return Mathf.Abs(brightness) > 1e-5f
                || Mathf.Abs(contrast) > 1e-5f
                || Mathf.Abs(saturation) > 1e-5f
                || Mathf.Abs(hueDegrees) > 1e-3f;
        }

        private void EnsurePreviewTexture()
        {
            if (currentTexture == null)
            {
                DestroyTexture(ref previewTexture);
                return;
            }

            if (!HasPendingAdjustments())
            {
                DestroyTexture(ref previewTexture);
                return;
            }

            bool changed = previewTexture == null
                || previewTexture.width != currentTexture.width
                || previewTexture.height != currentTexture.height
                || lastBrightness != brightness
                || lastContrast != contrast
                || lastSaturation != saturation
                || lastHueDegrees != hueDegrees;
            if (!changed) return;

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (previewTexture == null
                || previewTexture.width != currentTexture.width
                || previewTexture.height != currentTexture.height)
            {
                DestroyTexture(ref previewTexture);
                previewTexture = new Texture2D(currentTexture.width, currentTexture.height, TextureFormat.RGBA32, false, false);
                previewTexture.hideFlags = HideFlags.HideAndDontSave;
            }

            EnsureCachedPixels();
            EnsurePreviewBuffer();
            Array.Copy(cachedCurrentPixels, previewPixelBuffer, cachedCurrentPixels.Length);
            ApplyBcs32(previewPixelBuffer, brightness, contrast, saturation, hueDegrees);
            previewTexture.SetPixels32(previewPixelBuffer);
            previewTexture.Apply(false, false);

            lastBrightness = brightness;
            lastContrast = contrast;
            lastSaturation = saturation;
            lastHueDegrees = hueDegrees;
        }

        private void BakeAdjustmentsToCurrent()
        {
            if (currentTexture == null || !HasPendingAdjustments())
            {
                return;
            }

            EnsureCachedPixels();
            ApplyBcs32(cachedCurrentPixels, brightness, contrast, saturation, hueDegrees);
            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            ResetAdjustments();
            InvalidatePreview();
        }

        private void RestoreCurrentTextureToUnmodified()
        {
            if (currentTexture == null)
            {
                return;
            }

            Texture2D restoredTexture = null;
            try
            {
                restoredTexture = CreateUnmodifiedTextureCopy();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Reset Texture", "Error: " + ex.Message, "OK");
                return;
            }

            ResetAdjustments();
            if (restoredTexture == null)
            {
                InvalidatePreview();
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = restoredTexture;
            SetCurrentDirty(false);
            ResetMagnifiedPreviewCenter();
            Repaint();
        }

        private void EnsureResizeFieldsInitialized()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (resizeWidth <= 0
                || resizeHeight <= 0
                || resizeSourceWidth != currentTexture.width
                || resizeSourceHeight != currentTexture.height)
            {
                resizeWidth = currentTexture.width;
                resizeHeight = currentTexture.height;
                resizeSourceWidth = currentTexture.width;
                resizeSourceHeight = currentTexture.height;
            }
        }

        private void ResizeCurrentTexture()
        {
            if (currentTexture == null)
            {
                return;
            }

            int targetWidth = Mathf.Max(1, resizeWidth);
            int targetHeight = Mathf.Max(1, resizeHeight);
            if (targetWidth == currentTexture.width && targetHeight == currentTexture.height)
            {
                EditorUtility.DisplayDialog("Resize Texture", "The target size matches the current texture size.", "OK");
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            Texture2D resizedTexture = null;
            try
            {
                resizedTexture = ResizeTexture(currentTexture, targetWidth, targetHeight, resizePreserveDetails, resizeSmoother);
            }
            catch (Exception ex)
            {
                DestroyTexture(ref resizedTexture);
                EditorUtility.DisplayDialog("Resize Texture", "Error: " + ex.Message, "OK");
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = resizedTexture;
            resizeSourceWidth = currentTexture.width;
            resizeSourceHeight = currentTexture.height;
            resizeWidth = currentTexture.width;
            resizeHeight = currentTexture.height;
            SetCurrentDirty(true);
            ResetMagnifiedPreviewCenter();
            InvalidatePreview();
            Repaint();
        }

        private void SharpenCurrentTexture()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            Texture2D sharpenedTexture = null;
            try
            {
                sharpenedTexture = SharpenTexture(currentTexture, sharpenPower);
            }
            catch (Exception ex)
            {
                DestroyTexture(ref sharpenedTexture);
                EditorUtility.DisplayDialog("Sharpen Texture", "Error: " + ex.Message, "OK");
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = sharpenedTexture;
            SetCurrentDirty(true);
            ResetMagnifiedPreviewCenter();
            InvalidatePreview();
            Repaint();
        }

        private void BlurCurrentTexture()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            Texture2D blurredTexture = null;
            try
            {
                blurredTexture = BlurTexture(currentTexture, blurPower);
            }
            catch (Exception ex)
            {
                DestroyTexture(ref blurredTexture);
                EditorUtility.DisplayDialog("Blur Texture", "Error: " + ex.Message, "OK");
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = blurredTexture;
            SetCurrentDirty(true);
            ResetMagnifiedPreviewCenter();
            InvalidatePreview();
            Repaint();
        }

        private void HairifyCurrentTexture()
        {
            if (currentTexture == null)
            {
                return;
            }

            Texture2D hairifiedTexture = null;
            bool canceled = false;
            try
            {
                try
                {
                    if (HasPendingAdjustments())
                    {
                        EditorUtility.DisplayProgressBar("Hairify (experimental)", "Baking pending adjustments", 0f);
                        BakeAdjustmentsToCurrent();
                    }

                    hairifiedTexture = HairifyTexture(currentTexture, out canceled);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            catch (Exception ex)
            {
                DestroyTexture(ref hairifiedTexture);
                EditorUtility.DisplayDialog("Hairify (experimental)", "Error: " + ex.Message, "OK");
                return;
            }

            if (canceled)
            {
                DestroyTexture(ref hairifiedTexture);
                return;
            }

            if (hairifiedTexture == null)
            {
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = hairifiedTexture;
            SetCurrentDirty(true);
            ResetMagnifiedPreviewCenter();
            InvalidatePreview();
            Repaint();
        }

        private void GenerateNormalMapFromTexture()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            Texture2D normalTexture = null;
            try
            {
                normalTexture = GenerateNormalMapTexture(currentTexture, normalMapStrength);
            }
            catch (Exception ex)
            {
                DestroyTexture(ref normalTexture);
                EditorUtility.DisplayDialog("Generate Normal Map", "Error: " + ex.Message, "OK");
                return;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            currentTexture = normalTexture;
            SetCurrentDirty(true);
            ResetMagnifiedPreviewCenter();
            InvalidatePreview();
            Repaint();
        }

        private Texture2D CreateUnmodifiedTextureCopy()
        {
            if (sourceAsset != null)
            {
                return MakeReadableCopy(sourceAsset);
            }

            if (diskOriginalTexture != null)
            {
                return DuplicateReadableTexture(diskOriginalTexture);
            }

            return null;
        }

        private static Texture2D DuplicateReadableTexture(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            Texture2D copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false)
            {
                name = source.name,
            };
            copy.SetPixels32(source.GetPixels32());
            copy.Apply(false, false);
            return copy;
        }

        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight, bool preserveDetails, bool smoother)
        {
            if (source == null)
            {
                return null;
            }

            int sourceWidth = Mathf.Max(1, source.width);
            int sourceHeight = Mathf.Max(1, source.height);
            int width = Mathf.Max(1, targetWidth);
            int height = Mathf.Max(1, targetHeight);
            Color32[] sourcePixels = source.GetPixels32();
            Color32[] resizedPixels = new Color32[width * height];
            bool useAreaSampling = smoother && (width < sourceWidth || height < sourceHeight);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    resizedPixels[(y * width) + x] = useAreaSampling
                        ? SampleArea(sourcePixels, sourceWidth, sourceHeight, x, y, width, height)
                        : SampleBilinear(sourcePixels, sourceWidth, sourceHeight, x, y, width, height, smoother);
                }
            }

            if (preserveDetails && width > 2 && height > 2)
            {
                ApplySharpen(resizedPixels, width, height, ResizeDetailPreserveAmount);
            }

            Texture2D resizedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = source.name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            resizedTexture.SetPixels32(resizedPixels);
            resizedTexture.Apply(false, false);
            return resizedTexture;
        }

        private static Texture2D SharpenTexture(Texture2D source, float power)
        {
            if (source == null)
            {
                return null;
            }

            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            Color32[] pixels = source.GetPixels32();
            if (width > 2 && height > 2)
            {
                ApplySharpen(pixels, width, height, Mathf.Max(0f, power));
            }

            Texture2D sharpenedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = source.name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            sharpenedTexture.SetPixels32(pixels);
            sharpenedTexture.Apply(false, false);
            return sharpenedTexture;
        }

        private static Texture2D BlurTexture(Texture2D source, float power)
        {
            if (source == null)
            {
                return null;
            }

            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            Color32[] pixels = source.GetPixels32();
            ApplyBlur(pixels, width, height, Mathf.Max(0f, power));

            Texture2D blurredTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = source.name,
                hideFlags = HideFlags.HideAndDontSave,
            };
            blurredTexture.SetPixels32(pixels);
            blurredTexture.Apply(false, false);
            return blurredTexture;
        }

        private static Texture2D HairifyTexture(Texture2D source, out bool canceled)
        {
            canceled = false;
            if (source == null)
            {
                return null;
            }

            const string title = "Hairify (experimental)";
            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            Color32[] sourcePixels = source.GetPixels32();
            int pixelCount = sourcePixels.Length;
            if (pixelCount == 0)
            {
                return null;
            }

            if (EditorUtility.DisplayCancelableProgressBar(title, "Preparing source pixels", 0.01f))
            {
                canceled = true;
                return null;
            }

            int guideRadius = GetHairifyGuideRadius(width, height);
            Color32[] horizontalGuidePixels = new Color32[pixelCount];
            Color32[] guidePixels = new Color32[pixelCount];
            RunHairifyRowsWithProgress(
                title,
                "Softening color guide",
                0,
                height - 1,
                0.02f,
                0.16f,
                pixelY =>
                {
                    BuildHairifyHorizontalGuideRow(sourcePixels, horizontalGuidePixels, width, pixelY, guideRadius);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            RunHairifyRowsWithProgress(
                title,
                "Blending color guide",
                0,
                height - 1,
                0.16f,
                0.30f,
                pixelY =>
                {
                    BuildHairifyVerticalGuideRow(horizontalGuidePixels, guidePixels, width, height, pixelY, guideRadius);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            float[] guideLuminance = new float[pixelCount];
            RunHairifyRowsWithProgress(
                title,
                "Reading luminance guide",
                0,
                height - 1,
                0.30f,
                0.38f,
                pixelY =>
                {
                    BuildHairifyLuminanceRow(guidePixels, guideLuminance, width, pixelY);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            float[] directionX = new float[pixelCount];
            float[] directionY = new float[pixelCount];
            float[] confidence = new float[pixelCount];
            RunHairifyRowsWithProgress(
                title,
                "Finding strand direction",
                0,
                height - 1,
                0.38f,
                0.50f,
                pixelY =>
                {
                    BuildHairifyDirectionRow(guideLuminance, directionX, directionY, confidence, width, height, pixelY);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            Color32[] strandPixels = new Color32[pixelCount];
            RunHairifyRowsWithProgress(
                title,
                "Drawing hair strands",
                0,
                height - 1,
                0.50f,
                0.86f,
                pixelY =>
                {
                    BuildHairifyStrandRow(sourcePixels, guidePixels, directionX, directionY, confidence, strandPixels, width, pixelY);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            Color32[] smoothedPixels = new Color32[pixelCount];
            RunHairifyRowsWithProgress(
                title,
                "Connecting strand flow",
                0,
                height - 1,
                0.86f,
                0.98f,
                pixelY =>
                {
                    BuildHairifyDirectionalSmoothRow(strandPixels, smoothedPixels, directionX, directionY, width, height, pixelY);
                    return true;
                },
                ref canceled);
            if (canceled)
            {
                return null;
            }

            if (EditorUtility.DisplayCancelableProgressBar(title, "Creating hairified texture", 0.99f))
            {
                canceled = true;
                return null;
            }

            Texture2D hairifiedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = string.IsNullOrEmpty(source.name) ? "Hairified" : source.name + "_Hairified",
                hideFlags = HideFlags.HideAndDontSave,
            };
            hairifiedTexture.SetPixels32(smoothedPixels);
            hairifiedTexture.Apply(false, false);
            return hairifiedTexture;
        }

        private static Texture2D GenerateNormalMapTexture(Texture2D source, float strength)
        {
            if (source == null)
            {
                return null;
            }

            int width = Mathf.Max(1, source.width);
            int height = Mathf.Max(1, source.height);
            Color32[] sourcePixels = source.GetPixels32();
            float[] luminance = new float[sourcePixels.Length];
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                luminance[i] = GetLuminance01(sourcePixels[i]);
            }

            float normalStrength = Mathf.Max(0.001f, strength);
            Color32[] normalPixels = new Color32[sourcePixels.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float left = GetLuminanceClamped(luminance, width, height, x - 1, y);
                    float right = GetLuminanceClamped(luminance, width, height, x + 1, y);
                    float down = GetLuminanceClamped(luminance, width, height, x, y - 1);
                    float up = GetLuminanceClamped(luminance, width, height, x, y + 1);
                    float deltaX = (right - left) * normalStrength;
                    float deltaY = (up - down) * normalStrength;
                    Vector3 normal = new Vector3(-deltaX, -deltaY, 1f).normalized;
                    normalPixels[(y * width) + x] = new Color32(
                        FloatToByte((normal.x * 0.5f) + 0.5f),
                        FloatToByte((normal.y * 0.5f) + 0.5f),
                        FloatToByte((normal.z * 0.5f) + 0.5f),
                        255);
                }
            }

            Texture2D normalTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = string.IsNullOrEmpty(source.name) ? "NormalMap" : source.name + "_Normal",
                hideFlags = HideFlags.HideAndDontSave,
            };
            normalTexture.SetPixels32(normalPixels);
            normalTexture.Apply(false, false);
            return normalTexture;
        }

        private static Color32 SampleBilinear(Color32[] pixels, int sourceWidth, int sourceHeight, int targetX, int targetY, int targetWidth, int targetHeight, bool smoother)
        {
            float sourceX = ((targetX + 0.5f) * sourceWidth / targetWidth) - 0.5f;
            float sourceY = ((targetY + 0.5f) * sourceHeight / targetHeight) - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(sourceX), 0, sourceWidth - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(sourceY), 0, sourceHeight - 1);
            int x1 = Mathf.Min(x0 + 1, sourceWidth - 1);
            int y1 = Mathf.Min(y0 + 1, sourceHeight - 1);
            float tx = Mathf.Clamp01(sourceX - x0);
            float ty = Mathf.Clamp01(sourceY - y0);
            if (smoother)
            {
                tx = SmoothStep01(tx);
                ty = SmoothStep01(ty);
            }

            Color32 bottomLeft = pixels[(y0 * sourceWidth) + x0];
            Color32 bottomRight = pixels[(y0 * sourceWidth) + x1];
            Color32 topLeft = pixels[(y1 * sourceWidth) + x0];
            Color32 topRight = pixels[(y1 * sourceWidth) + x1];
            return LerpColor32(LerpColor32(bottomLeft, bottomRight, tx), LerpColor32(topLeft, topRight, tx), ty);
        }

        private static Color32 SampleArea(Color32[] pixels, int sourceWidth, int sourceHeight, int targetX, int targetY, int targetWidth, int targetHeight)
        {
            float scaleX = (float)sourceWidth / targetWidth;
            float scaleY = (float)sourceHeight / targetHeight;
            float minX = targetX * scaleX;
            float maxX = (targetX + 1) * scaleX;
            float minY = targetY * scaleY;
            float maxY = (targetY + 1) * scaleY;
            int startX = Mathf.Clamp(Mathf.FloorToInt(minX), 0, sourceWidth - 1);
            int endX = Mathf.Clamp(Mathf.CeilToInt(maxX) - 1, 0, sourceWidth - 1);
            int startY = Mathf.Clamp(Mathf.FloorToInt(minY), 0, sourceHeight - 1);
            int endY = Mathf.Clamp(Mathf.CeilToInt(maxY) - 1, 0, sourceHeight - 1);

            float r = 0f;
            float g = 0f;
            float b = 0f;
            float a = 0f;
            float weightSum = 0f;
            for (int y = startY; y <= endY; y++)
            {
                float weightY = Mathf.Max(0f, Mathf.Min(maxY, y + 1f) - Mathf.Max(minY, y));
                if (weightY <= 0f)
                {
                    continue;
                }

                for (int x = startX; x <= endX; x++)
                {
                    float weightX = Mathf.Max(0f, Mathf.Min(maxX, x + 1f) - Mathf.Max(minX, x));
                    float weight = weightX * weightY;
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    Color32 pixel = pixels[(y * sourceWidth) + x];
                    r += pixel.r * weight;
                    g += pixel.g * weight;
                    b += pixel.b * weight;
                    a += pixel.a * weight;
                    weightSum += weight;
                }
            }

            if (weightSum <= 0f)
            {
                return SampleBilinear(pixels, sourceWidth, sourceHeight, targetX, targetY, targetWidth, targetHeight, true);
            }

            float invWeight = 1f / weightSum;
            return new Color32(
                ByteFromChannel(r * invWeight),
                ByteFromChannel(g * invWeight),
                ByteFromChannel(b * invWeight),
                ByteFromChannel(a * invWeight));
        }

        private static void ApplySharpen(Color32[] pixels, int width, int height, float amount)
        {
            Color32[] original = new Color32[pixels.Length];
            Array.Copy(pixels, original, pixels.Length);
            float detailAmount = Mathf.Max(0f, amount);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    float blurR = 0f;
                    float blurG = 0f;
                    float blurB = 0f;
                    float blurA = 0f;
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        int sampleY = Mathf.Clamp(y + offsetY, 0, height - 1);
                        for (int offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            int sampleX = Mathf.Clamp(x + offsetX, 0, width - 1);
                            Color32 sample = original[(sampleY * width) + sampleX];
                            blurR += sample.r;
                            blurG += sample.g;
                            blurB += sample.b;
                            blurA += sample.a;
                            count++;
                        }
                    }

                    float invCount = 1f / count;
                    int index = (y * width) + x;
                    Color32 center = original[index];
                    pixels[index] = new Color32(
                        ByteFromChannel(center.r + ((center.r - (blurR * invCount)) * detailAmount)),
                        ByteFromChannel(center.g + ((center.g - (blurG * invCount)) * detailAmount)),
                        ByteFromChannel(center.b + ((center.b - (blurB * invCount)) * detailAmount)),
                        ByteFromChannel(center.a + ((center.a - (blurA * invCount)) * detailAmount)));
                }
            }
        }

        private static void ApplyBlur(Color32[] pixels, int width, int height, float amount)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
            {
                return;
            }

            float blurAmount = Mathf.Max(0f, amount);
            if (blurAmount <= 0f)
            {
                return;
            }

            int radius = Mathf.Clamp(Mathf.CeilToInt(blurAmount), 1, 32);
            float blend = Mathf.Clamp01(blurAmount);
            Color32[] original = new Color32[pixels.Length];
            Color32[] horizontal = new Color32[pixels.Length];
            Array.Copy(pixels, original, pixels.Length);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    float r = 0f;
                    float g = 0f;
                    float b = 0f;
                    float a = 0f;
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        int sampleX = Mathf.Clamp(x + offsetX, 0, width - 1);
                        Color32 sample = original[(y * width) + sampleX];
                        r += sample.r;
                        g += sample.g;
                        b += sample.b;
                        a += sample.a;
                        count++;
                    }

                    float invCount = 1f / count;
                    horizontal[(y * width) + x] = new Color32(
                        ByteFromChannel(r * invCount),
                        ByteFromChannel(g * invCount),
                        ByteFromChannel(b * invCount),
                        ByteFromChannel(a * invCount));
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int count = 0;
                    float r = 0f;
                    float g = 0f;
                    float b = 0f;
                    float a = 0f;
                    for (int offsetY = -radius; offsetY <= radius; offsetY++)
                    {
                        int sampleY = Mathf.Clamp(y + offsetY, 0, height - 1);
                        Color32 sample = horizontal[(sampleY * width) + x];
                        r += sample.r;
                        g += sample.g;
                        b += sample.b;
                        a += sample.a;
                        count++;
                    }

                    float invCount = 1f / count;
                    int index = (y * width) + x;
                    Color32 sourcePixel = original[index];
                    Color32 blurPixel = new Color32(
                        ByteFromChannel(r * invCount),
                        ByteFromChannel(g * invCount),
                        ByteFromChannel(b * invCount),
                        ByteFromChannel(a * invCount));
                    pixels[index] = LerpColor32(sourcePixel, blurPixel, blend);
                }
            }
        }

        private static int GetHairifyGuideRadius(int width, int height)
        {
            int shortSide = Math.Min(Mathf.Max(1, width), Mathf.Max(1, height));
            return Mathf.Clamp(Mathf.RoundToInt(shortSide / 160f), 1, 8);
        }

        private static void BuildHairifyHorizontalGuideRow(Color32[] sourcePixels, Color32[] targetPixels, int width, int pixelY, int radius)
        {
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                int sampleCount = 0;
                float red = 0f;
                float green = 0f;
                float blue = 0f;
                float alpha = 0f;
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    int sampleX = Mathf.Clamp(pixelX + offsetX, 0, width - 1);
                    Color32 samplePixel = sourcePixels[rowStart + sampleX];
                    red += samplePixel.r;
                    green += samplePixel.g;
                    blue += samplePixel.b;
                    alpha += samplePixel.a;
                    sampleCount++;
                }

                float invSampleCount = 1f / sampleCount;
                targetPixels[rowStart + pixelX] = new Color32(
                    ByteFromChannel(red * invSampleCount),
                    ByteFromChannel(green * invSampleCount),
                    ByteFromChannel(blue * invSampleCount),
                    ByteFromChannel(alpha * invSampleCount));
            }
        }

        private static void BuildHairifyVerticalGuideRow(Color32[] sourcePixels, Color32[] targetPixels, int width, int height, int pixelY, int radius)
        {
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                int sampleCount = 0;
                float red = 0f;
                float green = 0f;
                float blue = 0f;
                float alpha = 0f;
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    int sampleY = Mathf.Clamp(pixelY + offsetY, 0, height - 1);
                    Color32 samplePixel = sourcePixels[(sampleY * width) + pixelX];
                    red += samplePixel.r;
                    green += samplePixel.g;
                    blue += samplePixel.b;
                    alpha += samplePixel.a;
                    sampleCount++;
                }

                float invSampleCount = 1f / sampleCount;
                targetPixels[rowStart + pixelX] = new Color32(
                    ByteFromChannel(red * invSampleCount),
                    ByteFromChannel(green * invSampleCount),
                    ByteFromChannel(blue * invSampleCount),
                    ByteFromChannel(alpha * invSampleCount));
            }
        }

        private static void BuildHairifyLuminanceRow(Color32[] guidePixels, float[] luminance, int width, int pixelY)
        {
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                int index = rowStart + pixelX;
                luminance[index] = GetLuminance01(guidePixels[index]);
            }
        }

        private static void BuildHairifyDirectionRow(float[] luminance, float[] directionX, float[] directionY, float[] confidence, int width, int height, int pixelY)
        {
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                float topLeft = GetLuminanceClamped(luminance, width, height, pixelX - 1, pixelY + 1);
                float top = GetLuminanceClamped(luminance, width, height, pixelX, pixelY + 1);
                float topRight = GetLuminanceClamped(luminance, width, height, pixelX + 1, pixelY + 1);
                float left = GetLuminanceClamped(luminance, width, height, pixelX - 1, pixelY);
                float right = GetLuminanceClamped(luminance, width, height, pixelX + 1, pixelY);
                float bottomLeft = GetLuminanceClamped(luminance, width, height, pixelX - 1, pixelY - 1);
                float bottom = GetLuminanceClamped(luminance, width, height, pixelX, pixelY - 1);
                float bottomRight = GetLuminanceClamped(luminance, width, height, pixelX + 1, pixelY - 1);

                float gradientX = (topRight + (2f * right) + bottomRight) - (topLeft + (2f * left) + bottomLeft);
                float gradientY = (topLeft + (2f * top) + topRight) - (bottomLeft + (2f * bottom) + bottomRight);
                float gradientMagnitude = (float)Math.Sqrt((gradientX * gradientX) + (gradientY * gradientY));
                float confidenceValue = Mathf.Clamp01(gradientMagnitude * 3.5f);

                float fallbackX = (HairifyHash01(pixelX / 16, pixelY / 16, 17) - 0.5f) * 0.35f;
                float fallbackY = 1f;
                NormalizeHairifyDirection(ref fallbackX, ref fallbackY);

                float strandX = fallbackX;
                float strandY = fallbackY;
                if (gradientMagnitude > 0.00001f)
                {
                    strandX = -gradientY / gradientMagnitude;
                    strandY = gradientX / gradientMagnitude;
                    if (strandY < 0f)
                    {
                        strandX = -strandX;
                        strandY = -strandY;
                    }
                }

                float blendedX = Mathf.Lerp(fallbackX, strandX, confidenceValue);
                float blendedY = Mathf.Lerp(fallbackY, strandY, confidenceValue);
                NormalizeHairifyDirection(ref blendedX, ref blendedY);

                int index = rowStart + pixelX;
                directionX[index] = blendedX;
                directionY[index] = blendedY;
                confidence[index] = confidenceValue;
            }
        }

        private static void BuildHairifyStrandRow(Color32[] sourcePixels, Color32[] guidePixels, float[] directionX, float[] directionY, float[] confidence, Color32[] targetPixels, int width, int pixelY)
        {
            const float inv255 = 1f / 255f;
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                int index = rowStart + pixelX;
                Color32 sourcePixel = sourcePixels[index];
                if (sourcePixel.a <= 2)
                {
                    targetPixels[index] = sourcePixel;
                    continue;
                }

                Color32 guidePixel = guidePixels[index];
                float alphaMask = sourcePixel.a * inv255;
                float confidenceValue = confidence[index];
                float strandDirectionX = directionX[index];
                float strandDirectionY = directionY[index];
                float perpendicularX = -strandDirectionY;
                float perpendicularY = strandDirectionX;
                float crossPosition = (pixelX * perpendicularX) + (pixelY * perpendicularY);
                float alongPosition = (pixelX * strandDirectionX) + (pixelY * strandDirectionY);

                float localSpacingNoise = HairifyHash01(HairifyFloorToInt(pixelX / 17f), HairifyFloorToInt(pixelY / 17f), 41);
                float strandSpacing = Mathf.Lerp(2.2f, 5.2f, localSpacingNoise);
                int strandCell = HairifyFloorToInt(crossPosition / strandSpacing);
                int alongCell = HairifyFloorToInt(alongPosition / 18f);
                float strandOffset = (HairifyHash01(strandCell, alongCell, 73) - 0.5f) * 0.6f;
                float strandPhase = HairifyRepeat01((crossPosition / strandSpacing) + strandOffset);
                float distanceFromStrandCenter = Mathf.Abs(strandPhase - 0.5f) * 2f;
                float strandLine = 1f - HairifySmoothRange(0.08f, 0.82f, distanceFromStrandCenter);
                float phaseOffset = HairifyHash01(strandCell, alongCell, 91) * 6.2831853f;
                float alongWave = (float)Math.Sin((alongPosition * 0.18f) + phaseOffset);
                float fineNoise = HairifyHash01(pixelX, pixelY, 123) - 0.5f;
                float hairAmount = alphaMask * (0.55f + (confidenceValue * 0.45f));
                float guideBlend = 0.58f + (confidenceValue * 0.18f);
                float red = Mathf.Lerp(sourcePixel.r, guidePixel.r, guideBlend);
                float green = Mathf.Lerp(sourcePixel.g, guidePixel.g, guideBlend);
                float blue = Mathf.Lerp(sourcePixel.b, guidePixel.b, guideBlend);
                float highlight = ((strandLine - 0.32f) * 34f) + (alongWave * 9f) + (fineNoise * 8f);
                float troughDarkening = (1f - strandLine) * (0.35f + confidenceValue) * -9f;
                float brightness = (highlight + troughDarkening) * hairAmount;
                float colorShift = (HairifyHash01(strandCell, alongCell, 157) - 0.5f) * 10f * hairAmount;

                targetPixels[index] = new Color32(
                    ByteFromChannel(red + brightness + colorShift),
                    ByteFromChannel(green + (brightness * 0.95f) + (colorShift * 0.25f)),
                    ByteFromChannel(blue + (brightness * 0.9f) - (colorShift * 0.65f)),
                    sourcePixel.a);
            }
        }

        private static void BuildHairifyDirectionalSmoothRow(Color32[] sourcePixels, Color32[] targetPixels, float[] directionX, float[] directionY, int width, int height, int pixelY)
        {
            int rowStart = pixelY * width;
            for (int pixelX = 0; pixelX < width; pixelX++)
            {
                int index = rowStart + pixelX;
                Color32 centerPixel = sourcePixels[index];
                if (centerPixel.a <= 2)
                {
                    targetPixels[index] = centerPixel;
                    continue;
                }

                int offsetX = HairifyDirectionToOffset(directionX[index]);
                int offsetY = HairifyDirectionToOffset(directionY[index]);
                if (offsetX == 0 && offsetY == 0)
                {
                    offsetY = 1;
                }

                Color32 previousPixel = GetColorClamped(sourcePixels, width, height, pixelX - offsetX, pixelY - offsetY);
                Color32 nextPixel = GetColorClamped(sourcePixels, width, height, pixelX + offsetX, pixelY + offsetY);
                Color32 averagePixel = new Color32(
                    ByteFromChannel((previousPixel.r + (2f * centerPixel.r) + nextPixel.r) * 0.25f),
                    ByteFromChannel((previousPixel.g + (2f * centerPixel.g) + nextPixel.g) * 0.25f),
                    ByteFromChannel((previousPixel.b + (2f * centerPixel.b) + nextPixel.b) * 0.25f),
                    centerPixel.a);
                Color32 smoothedPixel = LerpColor32(centerPixel, averagePixel, 0.3f);
                smoothedPixel.a = centerPixel.a;
                targetPixels[index] = smoothedPixel;
            }
        }

        private static Color32 GetColorClamped(Color32[] pixels, int width, int height, int pixelX, int pixelY)
        {
            int clampedX = Mathf.Clamp(pixelX, 0, width - 1);
            int clampedY = Mathf.Clamp(pixelY, 0, height - 1);
            return pixels[(clampedY * width) + clampedX];
        }

        private static int HairifyDirectionToOffset(float direction)
        {
            if (direction > 0.33f)
            {
                return 1;
            }

            return direction < -0.33f ? -1 : 0;
        }

        private static void NormalizeHairifyDirection(ref float directionX, ref float directionY)
        {
            float length = (float)Math.Sqrt((directionX * directionX) + (directionY * directionY));
            if (length <= 0.00001f)
            {
                directionX = 0f;
                directionY = 1f;
                return;
            }

            float invLength = 1f / length;
            directionX *= invLength;
            directionY *= invLength;
        }

        private static int HairifyFloorToInt(float value)
        {
            return (int)Math.Floor(value);
        }

        private static float HairifyRepeat01(float value)
        {
            return value - (float)Math.Floor(value);
        }

        private static float HairifySmoothRange(float edge0, float edge1, float value)
        {
            if (edge1 <= edge0)
            {
                return value >= edge1 ? 1f : 0f;
            }

            return SmoothStep01((value - edge0) / (edge1 - edge0));
        }

        private static float HairifyHash01(int first, int second, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)first) * 16777619u;
                hash = (hash ^ (uint)second) * 16777619u;
                hash = (hash ^ (uint)salt) * 16777619u;
                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0x00FFFFFFu) * (1f / 16777215f);
            }
        }

        private static Color32 LerpColor32(Color32 left, Color32 right, float t)
        {
            float clampedT = Mathf.Clamp01(t);
            return new Color32(
                ByteFromChannel(Mathf.Lerp(left.r, right.r, clampedT)),
                ByteFromChannel(Mathf.Lerp(left.g, right.g, clampedT)),
                ByteFromChannel(Mathf.Lerp(left.b, right.b, clampedT)),
                ByteFromChannel(Mathf.Lerp(left.a, right.a, clampedT)));
        }

        private static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - (2f * t));
        }

        private static float GetLuminance01(Color32 pixel)
        {
            const float inv255 = 1f / 255f;
            return ((0.2126f * pixel.r) + (0.7152f * pixel.g) + (0.0722f * pixel.b)) * inv255;
        }

        private static float GetLuminanceClamped(float[] luminance, int width, int height, int x, int y)
        {
            int clampedX = Mathf.Clamp(x, 0, width - 1);
            int clampedY = Mathf.Clamp(y, 0, height - 1);
            return luminance[(clampedY * width) + clampedX];
        }

        private static byte FloatToByte(float value)
        {
            return ByteFromChannel(Mathf.Clamp01(value) * 255f);
        }

        private static byte ByteFromChannel(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
        }

        private void InvertCurrentColors()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            InvertColors32(cachedCurrentPixels);
            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private void FillCurrentColors(byte colorValue)
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            FillColors32(cachedCurrentPixels, colorValue);
            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private void ApplyAlphaFromLuminance()
        {
            if (currentTexture == null)
            {
                return;
            }

            if (HasPendingAdjustments())
            {
                BakeAdjustmentsToCurrent();
            }

            EnsureCachedPixels();
            bool changed = ApplyAlphaFromLuminance(cachedCurrentPixels, alphaFromLuminanceCutoff);
            if (!changed)
            {
                EditorUtility.DisplayDialog("Alpha From Luminance", "No alpha values were lowered. Try a higher cutoff or a darker texture area.", "OK");
                return;
            }

            currentTexture.SetPixels32(cachedCurrentPixels);
            currentTexture.Apply(false, false);
            SetCurrentDirty(true);
            InvalidatePreview();
        }

        private static void InvertColors32(Color32[] pixels)
        {
            if (pixels == null)
            {
                return;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                pixel.r = (byte)(255 - pixel.r);
                pixel.g = (byte)(255 - pixel.g);
                pixel.b = (byte)(255 - pixel.b);
                pixels[i] = pixel;
            }
        }

        private static void FillColors32(Color32[] pixels, byte colorValue)
        {
            if (pixels == null)
            {
                return;
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                pixel.r = colorValue;
                pixel.g = colorValue;
                pixel.b = colorValue;
                pixels[i] = pixel;
            }
        }

        private static bool ApplyAlphaFromLuminance(Color32[] pixels, float cutoff)
        {
            if (pixels == null || pixels.Length == 0)
            {
                return false;
            }

            bool changed = false;
            float clampedCutoff = Mathf.Clamp01(cutoff);
            const float inv255 = 1f / 255f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                float luminance = Mathf.Clamp01(((0.2126f * pixel.r) + (0.7152f * pixel.g) + (0.0722f * pixel.b)) * inv255);
                if (luminance > clampedCutoff)
                {
                    continue;
                }

                byte luminanceAlpha = (byte)Mathf.RoundToInt(luminance * 255f);
                if (luminanceAlpha >= pixel.a)
                {
                    continue;
                }

                pixel.a = luminanceAlpha;
                pixels[i] = pixel;
                changed = true;
            }

            return changed;
        }

        private void AutoMatchAdjustmentsToBackground()
        {
            if (currentTexture == null || backgroundAsset == null)
            {
                return;
            }

            Texture2D backgroundCopy = null;
            try
            {
                EnsureCachedPixels();
                ColorDistributionStats sourceStats = AnalyzeColorDistribution(cachedCurrentPixels);
                backgroundCopy = MakeReadableCopy(backgroundAsset);
                ColorDistributionStats targetStats = AnalyzeColorDistribution(backgroundCopy.GetPixels32());
                if (sourceStats.pixelCount == 0 || targetStats.pixelCount == 0)
                {
                    EditorUtility.DisplayDialog("Auto-match", "Unable to analyze enough opaque pixels in the source or background texture.", "OK");
                    return;
                }

                ApplyAutoMatchSettings(sourceStats, targetStats);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Auto-match", "Error: " + ex.Message, "OK");
            }
            finally
            {
                DestroyTexture(ref backgroundCopy);
            }
        }

        private void ApplyAutoMatchSettings(ColorDistributionStats sourceStats, ColorDistributionStats targetStats)
        {
            float sourceLow = GetHistogramPercentile(sourceStats.luminanceHistogram, sourceStats.pixelCount, 0.1f);
            float sourceMid = GetHistogramPercentile(sourceStats.luminanceHistogram, sourceStats.pixelCount, 0.5f);
            float sourceHigh = GetHistogramPercentile(sourceStats.luminanceHistogram, sourceStats.pixelCount, 0.9f);
            float targetLow = GetHistogramPercentile(targetStats.luminanceHistogram, targetStats.pixelCount, 0.1f);
            float targetMid = GetHistogramPercentile(targetStats.luminanceHistogram, targetStats.pixelCount, 0.5f);
            float targetHigh = GetHistogramPercentile(targetStats.luminanceHistogram, targetStats.pixelCount, 0.9f);

            float sourceSpread = Mathf.Max(AutoMatchMinLuminanceSpread, sourceHigh - sourceLow);
            float targetSpread = Mathf.Max(AutoMatchMinLuminanceSpread, targetHigh - targetLow);
            float rawContrastMultiplier = Mathf.Clamp(targetSpread / sourceSpread, 1f + AutoMatchMinContrast, 1f + AutoMatchMaxContrast);
            float contrastMultiplier = Mathf.Lerp(1f, rawContrastMultiplier, AutoMatchContrastInfluence);

            contrast = contrastMultiplier - 1f;
            float brightnessOffset = ((targetMid - 0.5f) / contrastMultiplier) + 0.5f - sourceMid;
            brightness = Mathf.Clamp(brightnessOffset * AutoMatchBrightnessInfluence, -AutoMatchMaxBrightness, AutoMatchMaxBrightness);
            saturation = Mathf.Clamp((targetStats.MeanSaturation / Mathf.Max(0.05f, sourceStats.MeanSaturation)) - 1f, -AutoMatchMaxSaturation, AutoMatchMaxSaturation);

            float sourceHue = GetCircularHueDegrees(sourceStats);
            float targetHue = GetCircularHueDegrees(targetStats);
            hueDegrees = Mathf.Clamp(Mathf.DeltaAngle(sourceHue, targetHue), -AutoMatchMaxHueDegrees, AutoMatchMaxHueDegrees);

            InvalidatePreview();
        }

        private static ColorDistributionStats AnalyzeColorDistribution(Color32[] pixels)
        {
            ColorDistributionStats stats = new ColorDistributionStats();
            if (pixels == null)
            {
                return stats;
            }

            const float inv255 = 1f / 255f;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.a <= AutoMatchAlphaThreshold)
                {
                    continue;
                }

                float r = pixel.r * inv255;
                float g = pixel.g * inv255;
                float b = pixel.b * inv255;
                float luminance = Mathf.Clamp01((0.2126f * r) + (0.7152f * g) + (0.0722f * b));
                int luminanceBin = Mathf.Clamp(Mathf.RoundToInt(luminance * 255f), 0, 255);
                stats.luminanceHistogram[luminanceBin]++;
                stats.luminanceSum += luminance;

                Color.RGBToHSV(new Color(r, g, b, 1f), out float hue, out float saturationValue, out _);
                stats.saturationSum += saturationValue;
                if (saturationValue > 0.05f)
                {
                    float hueRadians = hue * Mathf.PI * 2f;
                    stats.hueVectorX += Mathf.Cos(hueRadians) * saturationValue;
                    stats.hueVectorY += Mathf.Sin(hueRadians) * saturationValue;
                    stats.hueWeight += saturationValue;
                }

                stats.pixelCount++;
            }

            return stats;
        }

        private static float GetHistogramPercentile(int[] histogram, int pixelCount, float percentile)
        {
            if (histogram == null || histogram.Length == 0 || pixelCount <= 0)
            {
                return 0f;
            }

            int target = Mathf.Clamp(Mathf.RoundToInt((pixelCount - 1) * Mathf.Clamp01(percentile)), 0, pixelCount - 1);
            int cumulative = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                cumulative += histogram[i];
                if (cumulative > target)
                {
                    return i / 255f;
                }
            }

            return 1f;
        }

        private static float GetCircularHueDegrees(ColorDistributionStats stats)
        {
            if (stats == null || stats.hueWeight <= 1e-5f)
            {
                return 0f;
            }

            float degrees = Mathf.Atan2(stats.hueVectorY, stats.hueVectorX) * Mathf.Rad2Deg;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        private static void ApplyBcs32(Color32[] pixels, float b, float c, float s, float hueDeg)
        {
            int pixelCount = pixels.Length;
            if (pixelCount == 0)
            {
                return;
            }

            if (pixelCount < BcsParallelPixelThreshold)
            {
                ApplyBcs32Range(pixels, 0, pixelCount, b, c, s, hueDeg);
                return;
            }

            int maxUsefulWorkers = Math.Max(1, pixelCount / BcsParallelMinPixelsPerWorker);
            int workerCount = Math.Min(Environment.ProcessorCount, maxUsefulWorkers);
            if (workerCount <= 1)
            {
                ApplyBcs32Range(pixels, 0, pixelCount, b, c, s, hueDeg);
                return;
            }

            int pixelsPerWorker = (pixelCount + workerCount - 1) / workerCount;
            Parallel.For(0, workerCount, workerIndex =>
            {
                int start = workerIndex * pixelsPerWorker;
                int end = Math.Min(start + pixelsPerWorker, pixelCount);
                if (start < end)
                {
                    ApplyBcs32Range(pixels, start, end, b, c, s, hueDeg);
                }
            });
        }

        private static void ApplyBcs32Range(Color32[] pixels, int start, int end, float b, float c, float s, float hueDeg)
        {
            // brightness: additive in [-1,1]
            // contrast: scale around 0.5; multiplier = 1 + c
            // saturation: lerp(luma, color, 1 + s)
            // hue: rotate in RGB space using a precomputed hue matrix.
            const float inv255 = 1f / 255f;
            float contrastMul = 1f + c;
            float satMul = 1f + s;
            bool doHue = Mathf.Abs(hueDeg) > 1e-3f;

            float m00 = 1f;
            float m01 = 0f;
            float m02 = 0f;
            float m10 = 0f;
            float m11 = 1f;
            float m12 = 0f;
            float m20 = 0f;
            float m21 = 0f;
            float m22 = 1f;

            if (doHue)
            {
                float radians = hueDeg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);

                m00 = 0.213f + 0.787f * cos - 0.213f * sin;
                m01 = 0.715f - 0.715f * cos - 0.715f * sin;
                m02 = 0.072f - 0.072f * cos + 0.928f * sin;

                m10 = 0.213f - 0.213f * cos + 0.143f * sin;
                m11 = 0.715f + 0.285f * cos + 0.140f * sin;
                m12 = 0.072f - 0.072f * cos - 0.283f * sin;

                m20 = 0.213f - 0.213f * cos - 0.787f * sin;
                m21 = 0.715f - 0.715f * cos + 0.715f * sin;
                m22 = 0.072f + 0.928f * cos + 0.072f * sin;
            }

            for (int i = start; i < end; i++)
            {
                Color32 px = pixels[i];
                float r = px.r * inv255 + b;
                float g = px.g * inv255 + b;
                float bl = px.b * inv255 + b;

                r = (r - 0.5f) * contrastMul + 0.5f;
                g = (g - 0.5f) * contrastMul + 0.5f;
                bl = (bl - 0.5f) * contrastMul + 0.5f;

                float luma = 0.2126f * r + 0.7152f * g + 0.0722f * bl;
                r = luma + (r - luma) * satMul;
                g = luma + (g - luma) * satMul;
                bl = luma + (bl - luma) * satMul;

                r = Mathf.Clamp01(r);
                g = Mathf.Clamp01(g);
                bl = Mathf.Clamp01(bl);

                if (doHue)
                {
                    float hueR = m00 * r + m01 * g + m02 * bl;
                    float hueG = m10 * r + m11 * g + m12 * bl;
                    float hueB = m20 * r + m21 * g + m22 * bl;
                    r = hueR;
                    g = hueG;
                    bl = hueB;
                }

                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(bl) * 255f),
                    px.a);
            }
        }

        private void ResetAdjustments()
        {
            brightness = contrast = saturation = 0f;
            hueDegrees = 0f;
            lastBrightness = lastContrast = lastSaturation = 0f;
            lastHueDegrees = 0f;
            gradientApplied = false;
            lastGradientMode = GradientMode.Linear;
            lastGradientFrom = GradientFrom.Left;
            lastSolidPercent = float.NaN;
            lastGradientPercent = float.NaN;
            lastRadialSolidHorizontalPercent = float.NaN;
            lastRadialSolidVerticalPercent = float.NaN;
            lastRadialGradientHorizontalPercent = float.NaN;
            lastRadialGradientVerticalPercent = float.NaN;
            lastRadialCenterOffsetHorizontalPercent = float.NaN;
            lastRadialCenterOffsetVerticalPercent = float.NaN;
        }

        private void InvalidatePreview()
        {
            DestroyTexture(ref previewTexture);
            Repaint();
        }

        private void InvalidateCachedPixels()
        {
            cachedCurrentPixels = null;
            previewPixelBuffer = null;
            cachedPixelWidth = 0;
            cachedPixelHeight = 0;
        }

        private void EnsureCachedPixels()
        {
            if (currentTexture == null)
            {
                cachedCurrentPixels = null;
                cachedPixelWidth = 0;
                cachedPixelHeight = 0;
                return;
            }

            if (cachedCurrentPixels != null
                && cachedPixelWidth == currentTexture.width
                && cachedPixelHeight == currentTexture.height)
            {
                return;
            }

            cachedCurrentPixels = currentTexture.GetPixels32();
            cachedPixelWidth = currentTexture.width;
            cachedPixelHeight = currentTexture.height;
        }

        private void EnsurePreviewBuffer()
        {
            EnsureCachedPixels();
            if (cachedCurrentPixels == null)
            {
                previewPixelBuffer = null;
                return;
            }

            if (previewPixelBuffer == null || previewPixelBuffer.Length != cachedCurrentPixels.Length)
            {
                previewPixelBuffer = new Color32[cachedCurrentPixels.Length];
            }
        }

        // ---------- Helpers ----------

        private bool PromptSaveIfDirty(string action)
        {
            if (!dirty)
            {
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Save Unsaved Changes?",
                "The current texture has unsaved baked changes. Do you want to save before " + action + "?",
                "Save",
                "Discard",
                "Ignore");

            switch (choice)
            {
                case 0:
                    return SaveBeforeReplacingTexture();
                case 1:
                    return true;
                default:
                    return false;
            }
        }

        private bool SaveBeforeReplacingTexture()
        {
            if (CanQuickSaveOverwrite())
            {
                return QuickSaveAndOverwrite(false);
            }

            return SaveCurrentAsPng();
        }

        private QueuedTextureState GetQueuedTextureState(Texture2D texture, bool create)
        {
            if (texture == null)
            {
                return null;
            }

            if (!queuedTextureStates.TryGetValue(texture, out QueuedTextureState state) && create)
            {
                state = new QueuedTextureState();
                queuedTextureStates.Add(texture, state);
            }

            return state;
        }

        private bool IsQueuedTextureDirty(Texture2D texture, int index)
        {
            if (texture == null)
            {
                return false;
            }

            if (index == selectedDroppedTextureIndex && texture == sourceAsset)
            {
                return dirty;
            }

            QueuedTextureState state = GetQueuedTextureState(texture, false);
            return state != null && state.dirty;
        }

        private void SetCurrentDirty(bool isDirty)
        {
            dirty = isDirty;
            if (sourceAsset == null || FindDroppedTextureIndex(sourceAsset) < 0)
            {
                return;
            }

            QueuedTextureState state = GetQueuedTextureState(sourceAsset, true);
            state.dirty = isDirty;
            if (currentTexture != null)
            {
                state.workingTexture = currentTexture;
            }
        }

        private bool PreserveCurrentQueuedTextureState()
        {
            if (sourceAsset == null || currentTexture == null || FindDroppedTextureIndex(sourceAsset) < 0)
            {
                return false;
            }

            QueuedTextureState state = GetQueuedTextureState(sourceAsset, true);
            if (state.workingTexture != currentTexture)
            {
                DestroyTexture(ref state.workingTexture);
                state.workingTexture = currentTexture;
            }

            state.dirty = dirty;
            currentTexture = null;
            return true;
        }

        private bool RestoreQueuedTextureState(Texture2D asset, int droppedTextureIndex)
        {
            QueuedTextureState state = GetQueuedTextureState(asset, false);
            if (state == null || state.workingTexture == null)
            {
                return false;
            }

            InvalidateCachedPixels();
            DestroyTexture(ref previewTexture);
            sourceAsset = asset;
            selectedDroppedTextureIndex = droppedTextureIndex;
            currentTexture = state.workingTexture;
            dirty = state.dirty;
            ResetAdjustments();
            ResetMagnifiedPreviewCenter();
            return true;
        }

        private void DestroyQueuedTextureState(Texture2D texture, QueuedTextureState state)
        {
            if (texture != null)
            {
                queuedTextureStates.Remove(texture);
            }

            if (state != null)
            {
                DestroyTexture(ref state.workingTexture);
            }
        }

        private void DestroyQueuedTextureStates()
        {
            foreach (QueuedTextureState state in queuedTextureStates.Values)
            {
                DestroyTexture(ref state.workingTexture);
            }

            queuedTextureStates.Clear();
        }

        private bool TryLoadTextureAsset(Texture2D asset, int droppedTextureIndex, string action)
        {
            return TryLoadTextureAsset(asset, droppedTextureIndex, action, true);
        }

        private bool TryLoadTextureAsset(Texture2D asset, int droppedTextureIndex, string action, bool promptIfDirty)
        {
            if (asset == sourceAsset)
            {
                selectedDroppedTextureIndex = droppedTextureIndex;
                return true;
            }

            Texture2D previousSourceAsset = sourceAsset;
            int previousDroppedTextureIndex = selectedDroppedTextureIndex;
            bool preservedCurrentQueuedTexture = PreserveCurrentQueuedTextureState();
            if (!preservedCurrentQueuedTexture && promptIfDirty && !PromptSaveIfDirty(action))
            {
                return false;
            }

            if (!LoadFromAsset(asset))
            {
                if (preservedCurrentQueuedTexture)
                {
                    RestoreQueuedTextureState(previousSourceAsset, previousDroppedTextureIndex);
                }

                return false;
            }

            sourceAsset = asset;
            selectedDroppedTextureIndex = droppedTextureIndex;
            return true;
        }

        private int FindDroppedTextureIndex(Texture2D texture)
        {
            if (texture == null)
            {
                return -1;
            }

            return droppedTextureAssets.IndexOf(texture);
        }

        private int AddDroppedTextures(IList<Texture2D> textures)
        {
            int firstTextureIndex = -1;
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D texture = textures[i];
                if (texture == null)
                {
                    continue;
                }

                int existingIndex = FindDroppedTextureIndex(texture);
                if (existingIndex < 0)
                {
                    droppedTextureAssets.Add(texture);
                    existingIndex = droppedTextureAssets.Count - 1;
                }

                if (firstTextureIndex < 0)
                {
                    firstTextureIndex = existingIndex;
                }
            }

            return firstTextureIndex;
        }

        private bool RemoveDroppedTextureAt(int index)
        {
            if (index < 0 || index >= droppedTextureAssets.Count)
            {
                return false;
            }

            Texture2D removedTexture = droppedTextureAssets[index];
            bool removingCurrent = index == selectedDroppedTextureIndex || removedTexture == sourceAsset;
            if (removingCurrent && !PromptSaveIfDirty("removing the current texture"))
            {
                return false;
            }

            QueuedTextureState removedState = GetQueuedTextureState(removedTexture, false);

            droppedTextureAssets.RemoveAt(index);
            if (removingCurrent)
            {
                if (droppedTextureAssets.Count == 0)
                {
                    TryLoadTextureAsset(null, -1, "clearing the current texture", false);
                }
                else
                {
                    int nextIndex = index < droppedTextureAssets.Count ? index : 0;
                    TryLoadTextureAsset(droppedTextureAssets[nextIndex], nextIndex, "loading the next queued texture", false);
                }

                DestroyQueuedTextureState(removedTexture, removedState);
                Repaint();
                return true;
            }

            DestroyQueuedTextureState(removedTexture, removedState);
            if (selectedDroppedTextureIndex > index)
            {
                selectedDroppedTextureIndex--;
            }

            Repaint();
            return true;
        }

        private void HandlePreviewDragAndDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
            {
                return;
            }

            List<Texture2D> droppedTextures = GetDroppedProjectTextures();
            if (droppedTextures.Count == 0)
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                AddDroppedTexturesAndLoadFirst(droppedTextures, "loading a new texture");
            }

            currentEvent.Use();
        }

        private void AddDroppedTexturesAndLoadFirst(IList<Texture2D> textures, string action)
        {
            if (textures == null || textures.Count == 0)
            {
                return;
            }

            int firstTextureIndex = AddDroppedTextures(textures);
            if (firstTextureIndex >= 0)
            {
                TryLoadTextureAsset(droppedTextureAssets[firstTextureIndex], firstTextureIndex, action);
                Repaint();
            }
        }

        private static List<Texture2D> GetDroppedProjectTextures()
        {
            List<Texture2D> textures = new List<Texture2D>();
            UnityEngine.Object[] references = DragAndDrop.objectReferences;
            for (int i = 0; i < references.Length; i++)
            {
                Texture2D texture = references[i] as Texture2D;
                if (texture == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    textures.Add(texture);
                }
            }

            return textures;
        }

        private static void EnsureChecker()
        {
            if (s_checker != null) return;
            int size = CheckerTile * 2;
            s_checker = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            s_checker.hideFlags = HideFlags.HideAndDontSave;
            s_checker.filterMode = FilterMode.Point;
            s_checker.wrapMode = TextureWrapMode.Repeat;
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool a = (x / CheckerTile + y / CheckerTile) % 2 == 0;
                    px[y * size + x] = a ? CheckerLight : CheckerDark;
                }
            }
            s_checker.SetPixels(px);
            s_checker.Apply(false, false);
        }

        private static Rect FitRect(Rect outer, int texW, int texH)
        {
            float ar = (float)texW / texH;
            float w = outer.width;
            float h = outer.height;
            float fitW = w;
            float fitH = w / ar;
            if (fitH > h)
            {
                fitH = h;
                fitW = h * ar;
            }
            float x = outer.x + (w - fitW) * 0.5f;
            float y = outer.y + (h - fitH) * 0.5f;
            return new Rect(x, y, fitW, fitH);
        }

        private static Rect FitBackgroundRect(Rect outer, int texW, int texH, BackgroundScaleMode scaleMode)
        {
            float aspectRatio = (float)texW / texH;
            float fitWidth;
            float fitHeight;
            if (scaleMode == BackgroundScaleMode.MatchHeight)
            {
                fitHeight = outer.height;
                fitWidth = fitHeight * aspectRatio;
            }
            else
            {
                fitWidth = outer.width;
                fitHeight = fitWidth / aspectRatio;
            }

            float x = outer.x + (outer.width - fitWidth) * 0.5f;
            float y = outer.y + (outer.height - fitHeight) * 0.5f;
            return new Rect(x, y, fitWidth, fitHeight);
        }

        private static Color CompositeOver(Color background, Color foreground)
        {
            float outAlpha = foreground.a + (background.a * (1f - foreground.a));
            if (outAlpha <= 1e-5f)
            {
                return Color.clear;
            }

            return new Color(
                ((foreground.r * foreground.a) + (background.r * background.a * (1f - foreground.a))) / outAlpha,
                ((foreground.g * foreground.a) + (background.g * background.a * (1f - foreground.a))) / outAlpha,
                ((foreground.b * foreground.a) + (background.b * background.a * (1f - foreground.a))) / outAlpha,
                outAlpha);
        }

        private static GUIStyle s_centered;
        private static GUIStyle CenteredStyle()
        {
            if (s_centered == null)
            {
                s_centered = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
                s_centered.normal.textColor = Color.black;
                s_centered.hover.textColor = Color.black;
                s_centered.active.textColor = Color.black;
                s_centered.focused.textColor = Color.black;
            }
            return s_centered;
        }

        private static void DestroyTexture(ref Texture2D t)
        {
            if (t != null)
            {
                UnityEngine.Object.DestroyImmediate(t);
                t = null;
            }
        }
    }
}
