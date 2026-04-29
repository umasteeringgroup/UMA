using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public class UMATextureUtilitiesWindow : EditorWindow
    {
        [Serializable]
        private class TextureParameterPreset
        {
            public string name;
            public float brightness;
            public float contrast;
            public float saturation;
            public float hueDegrees;
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

        private enum Tool
        {
            Split,
            AlphaGradient,
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

        private const float ToolPanelWidth = 190f;
        private const float DroppedTextureListWidth = 220f;
        private const int CheckerTile = 16;
        private const int BcsParallelPixelThreshold = 65536;
        private const int BcsParallelMinPixelsPerWorker = 16384;
        private const string PresetPrefsKey = "UMA.TextureUtilities.ParameterPresets";
        private static readonly Color CheckerLight = new Color(1f, 1f, 1f, 1f);
        private static readonly Color CheckerDark = new Color(0.75f, 0.75f, 0.75f, 1f);

        private Tool currentTool = Tool.Split;

        // Source / current texture state
        private Texture2D sourceAsset;
        private Texture2D backgroundAsset;
        private Texture2D currentTexture;     // editable RGBA32 working copy (the "current texture")
        private Texture2D previewTexture;     // displayed texture: currentTexture or BCS-adjusted copy
        private readonly List<Texture2D> droppedTextureAssets = new List<Texture2D>();
        private int selectedDroppedTextureIndex = -1;
        private bool dirty;                   // currentTexture has unsaved baked changes
        private bool showBackgroundTexture;
        private bool combineBackgroundOnSave;
        private BackgroundScaleMode backgroundScaleMode = BackgroundScaleMode.MatchWidth;

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
        private float lastBrightness = 0f;
        private float lastContrast = 0f;
        private float lastSaturation = 0f;
        private float lastHueDegrees = 0f;

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

        // Saved parameter presets
        private List<TextureParameterPreset> parameterPresets = new List<TextureParameterPreset>();
        private string[] parameterPresetOptions = new[] { "(Current Settings)" };
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
            var window = GetWindow<UMATextureUtilitiesWindow>();
            window.titleContent = new GUIContent("UMA Texture Utilities");
            window.minSize = new Vector2(720f, 480f);
            window.Show();
        }

        private void OnDisable()
        {
            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
        }

        private void OnEnable()
        {
            LoadParameterPresets();
        }

        private void OnGUI()
        {
            EnsureChecker();

            EditorGUILayout.BeginHorizontal();
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
            DrawToolToggle(Tool.AlphaGradient, "Alpha Gradient");
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
            EditorGUILayout.BeginVertical();

            DrawHeaderBar();
            EditorGUILayout.Space();

            scrollRight = EditorGUILayout.BeginScrollView(scrollRight);

            DrawPreviewArea();
            EditorGUILayout.Space();

            DrawBackgroundSection();
            EditorGUILayout.Space();

            DrawParameterPresetSection();
            EditorGUILayout.Space();

            DrawAdjustmentsSection();
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(currentTool == Tool.Split ? "Split Texture" : "Alpha Gradient", EditorStyles.boldLabel);
            GUIHelper.BeginVerticalPadded(8, new Color(0.85f, 0.92f, 1f), EditorStyles.helpBox);
            switch (currentTool)
            {
                case Tool.Split: DrawSplitTool(); break;
                case Tool.AlphaGradient: DrawAlphaGradientTool(); break;
            }
            GUIHelper.EndVerticalPadded();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawParameterPresetSection()
        {
            EditorGUILayout.LabelField("Parameter Presets", EditorStyles.boldLabel);
            GUIHelper.BeginVerticalPadded(8, new Color(0.9f, 0.96f, 0.9f), EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            presetName = EditorGUILayout.TextField("Preset Name", presetName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(presetName)))
            {
                if (GUILayout.Button("Save Current", GUILayout.Width(110f)))
                {
                    SaveCurrentPreset();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            int newIndex = EditorGUILayout.Popup("Load Preset", selectedParameterPresetIndex, parameterPresetOptions);
            if (EditorGUI.EndChangeCheck())
            {
                selectedParameterPresetIndex = newIndex;
                if (selectedParameterPresetIndex > 0)
                {
                    ApplyPreset(parameterPresets[selectedParameterPresetIndex - 1]);
                }
            }

            GUIHelper.EndVerticalPadded();
        }

        private void DrawHeaderBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            Texture2D newAsset = (Texture2D)EditorGUILayout.ObjectField(sourceAsset, typeof(Texture2D), false, GUILayout.Width(220));
            if (newAsset != sourceAsset)
            {
                TryLoadTextureAsset(newAsset, FindDroppedTextureIndex(newAsset), "loading a new texture");
            }

            if (GUILayout.Button("Load From Disk...", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                if (PromptSaveIfDirty("loading a new texture"))
                {
                    LoadFromDisk();
                }
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
            EditorGUILayout.LabelField("Background", EditorStyles.boldLabel);
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

        private void DrawPreviewArea()
        {
            // Reserve a square-ish area sized to remaining space.
            float minHeight = 200f;
            float desired = Mathf.Max(minHeight, position.height * 0.5f);
            EditorGUILayout.BeginHorizontal();
            if (droppedTextureAssets.Count > 0)
            {
                DrawDroppedTextureList(desired);
                EditorGUILayout.Space(6f, false);
            }

            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(desired));
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
                Rect fit = FitRect(rect, toShow.width, toShow.height);
                if (showBackgroundTexture && backgroundAsset != null)
                {
                    Rect backgroundRect = FitBackgroundRect(fit, backgroundAsset.width, backgroundAsset.height, backgroundScaleMode);
                    GUI.DrawTexture(backgroundRect, backgroundAsset, ScaleMode.StretchToFill, true);
                }
                GUI.DrawTexture(fit, toShow, ScaleMode.StretchToFill, true);
            }
            else if (showBackgroundTexture && backgroundAsset != null)
            {
                Rect fit = FitRect(rect, backgroundAsset.width, backgroundAsset.height);
                GUI.DrawTexture(fit, backgroundAsset, ScaleMode.StretchToFill, true);
            }
            else
            {
                GUI.Label(rect, "No texture loaded.\nUse the Object field, Load From Disk, or drag a project texture here.", CenteredStyle());
            }

            HandlePreviewDragAndDrop(rect);
        }

        private void DrawDroppedTextureList(float height)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(DroppedTextureListWidth), GUILayout.Height(height));
            EditorGUILayout.LabelField("Dropped Textures", EditorStyles.boldLabel);
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
            Rect fieldRect = new Rect(rowRect.x, rowRect.y, rowRect.width - 24f, rowRect.height);
            Rect removeRect = new Rect(rowRect.xMax - 20f, rowRect.y, 20f, rowRect.height);
            bool isSelected = index == selectedDroppedTextureIndex && sourceAsset == texture;
            if (Event.current.type == EventType.Repaint && isSelected)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.72f, 0.84f, 1f, 0.35f));
            }

            EditorGUI.BeginDisabledGroup(true);
            EditorGUI.ObjectField(fieldRect, GUIContent.none, texture, typeof(Texture2D), false);
            EditorGUI.EndDisabledGroup();

            if (GUI.Button(fieldRect, GUIContent.none, GUIStyle.none))
            {
                TryLoadTextureAsset(texture, index, "selecting a dropped texture");
                GUI.FocusControl(null);
            }

            if (GUI.Button(removeRect, "X"))
            {
                RemoveDroppedTextureAt(index);
                GUIUtility.ExitGUI();
            }
        }

        private void DrawAdjustmentsSection()
        {
            EditorGUILayout.LabelField("Adjustments (live preview)", EditorStyles.boldLabel);
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
                    brightness = contrast = saturation = 0f;
                    hueDegrees = 0f;
                    InvalidatePreview();
                }
                EditorGUILayout.EndHorizontal();
            }

            GUIHelper.EndVerticalPadded();
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

        private void SaveCurrentPreset()
        {
            string trimmedName = presetName.Trim();
            if (string.IsNullOrEmpty(trimmedName))
            {
                return;
            }

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
            gradientMode = preset.gradientMode;
            gradientFrom = preset.gradientFrom;
            solidPercent = preset.solidPercent;
            gradientPercent = preset.gradientPercent;
            radialSolidHorizontalPercent = preset.radialSolidHorizontalPercent;
            radialSolidVerticalPercent = preset.radialSolidVerticalPercent;
            radialGradientHorizontalPercent = preset.radialGradientHorizontalPercent;
            radialGradientVerticalPercent = preset.radialGradientVerticalPercent;
            radialCenterOffsetHorizontalPercent = preset.radialCenterOffsetHorizontalPercent;
            radialCenterOffsetVerticalPercent = preset.radialCenterOffsetVerticalPercent;
            InvalidatePreview();
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
            parameterPresetOptions = new string[parameterPresets.Count + 1];
            parameterPresetOptions[0] = "(Current Settings)";
            for (int i = 0; i < parameterPresets.Count; i++)
            {
                parameterPresetOptions[i + 1] = parameterPresets[i].name;
            }

            selectedParameterPresetIndex = Mathf.Clamp(selectedParameterPresetIndex, 0, parameterPresetOptions.Length - 1);
        }

        private int GetPresetPopupIndex(string name)
        {
            for (int i = 0; i < parameterPresets.Count; i++)
            {
                if (string.Equals(parameterPresets[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return 0;
        }

        // ---------- Loading / Saving ----------

        private void LoadFromAsset(Texture2D asset)
        {
            InvalidateCachedPixels();
            DestroyTexture(ref currentTexture);
            DestroyTexture(ref previewTexture);
            dirty = false;
            ResetAdjustments();
            if (asset == null)
            {
                return;
            }
            currentTexture = MakeReadableCopy(asset);
        }

        private void LoadFromDisk()
        {
            string path = EditorUtility.OpenFilePanel("Load Texture", Application.dataPath, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    EditorUtility.DisplayDialog("Load Texture", "Failed to load image at: " + path, "OK");
                    return;
                }

                InvalidateCachedPixels();
                DestroyTexture(ref currentTexture);
                DestroyTexture(ref previewTexture);
                sourceAsset = null;
                selectedDroppedTextureIndex = -1;
                currentTexture = tex;
                currentTexture.name = Path.GetFileNameWithoutExtension(path);
                dirty = false;
                ResetAdjustments();
            }
            catch (Exception ex)
            {
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
            string path = EditorUtility.SaveFilePanel("Save Texture As PNG", Application.dataPath, defaultName + ".png", "png");
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
                dirty = false;
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
                dirty = false;
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
            dirty = true;
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
            dirty = true;
            ResetAdjustments();
            InvalidatePreview();
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

        private bool TryLoadTextureAsset(Texture2D asset, int droppedTextureIndex, string action)
        {
            if (asset == sourceAsset)
            {
                selectedDroppedTextureIndex = droppedTextureIndex;
                return true;
            }

            if (!PromptSaveIfDirty(action))
            {
                return false;
            }

            sourceAsset = asset;
            selectedDroppedTextureIndex = droppedTextureIndex;
            LoadFromAsset(sourceAsset);
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

        private void RemoveDroppedTextureAt(int index)
        {
            if (index < 0 || index >= droppedTextureAssets.Count)
            {
                return;
            }

            droppedTextureAssets.RemoveAt(index);
            if (selectedDroppedTextureIndex == index)
            {
                selectedDroppedTextureIndex = -1;
            }
            else if (selectedDroppedTextureIndex > index)
            {
                selectedDroppedTextureIndex--;
            }
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
                int firstTextureIndex = AddDroppedTextures(droppedTextures);
                if (firstTextureIndex >= 0)
                {
                    TryLoadTextureAsset(droppedTextureAssets[firstTextureIndex], firstTextureIndex, "loading a new texture");
                }
            }

            currentEvent.Use();
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
