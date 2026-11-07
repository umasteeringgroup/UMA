using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    // Retained for callers that open an atlas on its own. Card setup embeds the same editor.
    public sealed class HairAtlasRegionEditorWindow : EditorWindow
    {
        [SerializeField] private HairAtlasProfileAsset atlas;
        [SerializeField] private HairAtlasEditorPanel panel = new HairAtlasEditorPanel();

        public static void Open(HairAtlasProfileAsset profile)
        {
            HairAtlasRegionEditorWindow window = GetWindow<HairAtlasRegionEditorWindow>();
            window.titleContent = new GUIContent("Hair UV Sets");
            window.atlas = profile;
            window.minSize = new Vector2(540f, 540f);
            window.Show();
        }

        internal static void ResetOpenPreferences()
        {
            foreach (HairAtlasRegionEditorWindow window in Resources.FindObjectsOfTypeAll<HairAtlasRegionEditorWindow>())
            { window.panel?.ResetPreferences(); window.Repaint(); }
        }

        public static Texture ResolveDisplayTexture(HairAtlasProfileAsset profile)
        {
            if (profile == null) return null;
            if (profile.albedo != null) return profile.albedo;
            Material material = profile.material;
            if (material == null) return null;
            if (material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null)
                return material.GetTexture("_BaseMap");
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
                return material.GetTexture("_MainTex");
            return material.mainTexture;
        }

        private void OnEnable() => Undo.undoRedoPerformed += OnUndoRedo;
        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            panel?.Dispose();
            if (atlas != null && EditorUtility.IsPersistent(atlas)) AssetDatabase.SaveAssetIfDirty(atlas);
        }
        private void OnUndoRedo()
        {
            panel?.CancelInteraction();
            HairCardStage.ActiveStage?.QueueRebuild();
            Repaint();
        }
        private void OnLostFocus() => panel?.CancelInteraction();
        private void OnGUI()
        {
            atlas = (HairAtlasProfileAsset)EditorGUILayout.ObjectField("Atlas Profile", atlas,
                typeof(HairAtlasProfileAsset), false);
            panel ??= new HairAtlasEditorPanel();
            panel.Draw(atlas, null, null, position.width - 20f, Repaint);
        }
    }

    [Serializable]
    internal sealed class HairAtlasEditorPanel : IDisposable
    {
        private const int CanvasHint = 0x48415641;
        private enum PreviewChannel { ColorAndAlpha, Color, Alpha }
        private enum EditTool { Select, Draw, Redraw }
        private enum Gesture { None, New, Redraw, Move, Resize, Pan }

        [SerializeField] private HairAtlasProfileAsset atlas;
        [SerializeField] private string selectedId;
        [SerializeField] private PreviewChannel channel;
        [SerializeField] private bool checkerboard = true;
        [SerializeField] private Color checkerLight = new Color(0.75f, 0.75f, 0.75f, 1f);
        [SerializeField] private Color checkerDark = new Color(0.55f, 0.55f, 0.55f, 1f);
        [SerializeField] private Color solidBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
        [SerializeField] private float checkerSize = 12f;
        [SerializeField] private bool backgroundExpanded;
        [SerializeField] private bool snapToPixels = true;
        [SerializeField] private bool showOutlines = true;
        [SerializeField] private bool pixelUnits;
        [SerializeField] private float zoom = 1f;
        [SerializeField] private Vector2 pan;
        [SerializeField] private Vector2 listScroll;

        [SerializeField] private EditTool tool;
        [NonSerialized] private Gesture gesture;
        [NonSerialized] private HairGroomAsset groom;
        [NonSerialized] private HairGroup group;
        [NonSerialized] private Action repaint;
        [NonSerialized] private Texture2D checkerTexture;
        [NonSerialized] private GUIStyle badgeStyle;
        [NonSerialized] private int ownedControl;
        [NonSerialized] private Vector2 startUv;
        [NonSerialized] private Vector2 startMouse;
        [NonSerialized] private Vector2 startPan;
        [NonSerialized] private Rect originalRect;
        [NonSerialized] private Rect pendingRect;
        [NonSerialized] private int corner;
        [NonSerialized] private string status;
        [NonSerialized] private Vector2 fittedCanvasSize;
        [NonSerialized] private PreviewRenderUtility materialPreview;
        [NonSerialized] private HairCardMeshBuildResult previewCard;
        [NonSerialized] private HairPreviewMaterialSet previewMaterials;
        [NonSerialized] private string previewSignature;
        [NonSerialized] private HairCardProfileAsset previewProfile;
        [NonSerialized] private HairAtlasProfileAsset previewAtlas;
        [NonSerialized] internal Texture lastRenderedCard;
        [SerializeField] private bool materialPreviewExpanded = true;
        [SerializeField] private float previewYaw;
        [NonSerialized] private bool preferencesLoaded;
        [NonSerialized] private string preferenceContext;
        [NonSerialized] private double nextPreferencesSave;

        private void LoadPreferences(HairAtlasProfileAsset nextAtlas)
        {
            string context = HairEditorPreferences.Context(nextAtlas);
            if (preferencesLoaded && context == preferenceContext) return;
            if (preferencesLoaded) SavePreferences();
            preferenceContext = context;
            selectedId = null; zoom = 1f; pan = Vector2.zero;
            HairEditorPreferences.instance.Restore(this, "atlas-panel", context);
            preferencesLoaded = true;
            ReleaseCheckerTexture();
        }

        internal void SavePreferences()
        {
            if (preferencesLoaded && !HairEditorPreferences.Suspended)
                HairEditorPreferences.instance.Remember(this, "atlas-panel", preferenceContext);
        }

        internal void ResetPreferences()
        {
            CancelInteraction();
            HairPreferenceCodec.Reset(this);
            ReleaseCheckerTexture();
            SavePreferences();
        }

        internal HairAtlasRegion Selected => atlas?.regions?.Find(region => region != null && region.Id == selectedId);
        internal bool IsInteracting => gesture != Gesture.None;
        internal void SelectSet(HairAtlasProfileAsset profile, string id)
        {
            CancelInteraction();
            LoadPreferences(profile);
            atlas = profile;
            selectedId = id;
        }

        internal void Draw(HairAtlasProfileAsset nextAtlas, HairGroomAsset nextGroom,
            HairGroup nextGroup, float availableWidth, Action repaintOwner)
        {
            // Allocate before the variable set list/properties: mouse capture must survive selection changes.
            int control = GUIUtility.GetControlID(CanvasHint, FocusType.Keyboard);
            if (atlas != nextAtlas || group != nextGroup)
            {
                CancelInteraction();
                if (atlas != nextAtlas)
                {
                    LoadPreferences(nextAtlas);
                }
                atlas = nextAtlas;
                status = null;
            }
            groom = nextGroom;
            if (!preferencesLoaded) LoadPreferences(nextAtlas);
            group = nextGroup;
            repaint = repaintOwner;
            if (atlas == null)
            {
                EditorGUILayout.HelpBox("Create or assign an Atlas Profile above to start defining UV sets.", MessageType.Info);
                return;
            }
            atlas.EnsureIntegrity();
            if (Selected == null)
                selectedId = atlas.regions.Find(region => region != null)?.Id;
            Texture texture = HairAtlasRegionEditorWindow.ResolveDisplayTexture(atlas);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UV Sets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Each set is a named rectangle on this atlas. Draw a set, then drag it or its corners to refine it.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("UV sets belong to the Atlas Profile; edits affect all groups using that profile.",
                EditorStyles.wordWrappedMiniLabel);
            DrawToolbar(texture);
            DrawPreviewSettings();
            bool wide = availableWidth >= 730f;
            if (wide)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                        DrawCanvas(texture, control, Mathf.Clamp((availableWidth - 245f) * 0.75f, 300f, 460f));
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(225f)))
                        DrawSetList(control, 285f);
                }
            }
            else
            {
                DrawCanvas(texture, control, Mathf.Clamp(availableWidth * 0.7f, 270f, 400f));
                DrawSetList(control, Mathf.Clamp(atlas.regions.Count * 28f + 8f, 60f, 156f));
            }
            DrawSelectedProperties(texture);
            DrawMaterialPreview();
            if (groom != null) HairCardStage.ActiveStage?.HighlightUvSet(atlas, selectedId);
            HairCardStage.ActiveStage?.SetWorkspaceInteraction(IsInteracting || GUIUtility.hotControl != 0 || EditorGUIUtility.editingTextField);
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
            if (!IsInteracting && Event.current.type == EventType.Repaint && EditorApplication.timeSinceStartup >= nextPreferencesSave)
            {
                nextPreferencesSave = EditorApplication.timeSinceStartup + 2d;
                SavePreferences();
            }
        }

        private void DrawToolbar(Texture texture)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditTool nextTool = (EditTool)GUILayout.Toolbar((int)tool,
                    new[] { "Select / Move", "+ Draw Set", "Redraw" }, GUILayout.Height(25f));
                if (nextTool != tool)
                {
                    CancelInteraction();
                    tool = nextTool == EditTool.Redraw && Selected == null ? EditTool.Draw : nextTool;
                    status = null;
                }
                if (GUILayout.Button("Fit", GUILayout.Width(38f), GUILayout.Height(25f)))
                {
                    zoom = 1f;
                    pan = Vector2.zero;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                channel = (PreviewChannel)GUILayout.Toolbar((int)channel,
                    new[] { "Color + Alpha", "Color", "Alpha" });
                GUILayout.Label(texture != null ? $"{texture.width} × {texture.height}" : "No texture",
                    EditorStyles.miniLabel, GUILayout.Width(95f));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                snapToPixels = GUILayout.Toggle(snapToPixels, "Snap to pixels");
                showOutlines = GUILayout.Toggle(showOutlines, "UV outlines");
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{zoom * 100f:0}%", EditorStyles.miniLabel);
            }
            if (texture == null)
                EditorGUILayout.HelpBox("Assign Albedo Atlas or a material base texture. UV sets can still be drawn in the empty canvas.",
                    MessageType.Info);
        }

        private void DrawPreviewSettings()
        {
            backgroundExpanded = EditorGUILayout.Foldout(backgroundExpanded, "Preview background", true);
            if (!backgroundExpanded) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                checkerboard = GUILayout.Toolbar(checkerboard ? 0 : 1, new[] { "Checkerboard", "Solid color" }) == 0;
                EditorGUI.BeginChangeCheck();
                if (checkerboard)
                {
                    checkerLight = EditorGUILayout.ColorField("Light squares", checkerLight);
                    checkerDark = EditorGUILayout.ColorField("Dark squares", checkerDark);
                    checkerSize = EditorGUILayout.Slider("Square size", checkerSize, 6f, 32f);
                }
                else solidBackground = EditorGUILayout.ColorField("Background color", solidBackground);
                if (EditorGUI.EndChangeCheck()) ReleaseCheckerTexture();
                if (GUILayout.Button("Reset background"))
                {
                    checkerboard = true;
                    checkerLight = new Color(0.75f, 0.75f, 0.75f, 1f);
                    checkerDark = new Color(0.55f, 0.55f, 0.55f, 1f);
                    checkerSize = 12f;
                    solidBackground = new Color(0.22f, 0.22f, 0.22f, 1f);
                    ReleaseCheckerTexture();
                }
                EditorGUILayout.LabelField("Preview only. Alpha uses the atlas texture's alpha channel; the scene uses the Card Material.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawCanvas(Texture texture, int control, float height)
        {
            Rect viewport = GUILayoutUtility.GetRect(100f, height, GUILayout.ExpandWidth(true));
            GUI.BeginGroup(viewport);
            try
            {
                Rect localViewport = new Rect(0f, 0f, viewport.width, viewport.height);
                EditorGUI.DrawRect(localViewport, new Color(0.12f, 0.12f, 0.12f, 1f));
                Rect fit = FitAspect(new Rect(8f, 8f, viewport.width - 16f, viewport.height - 16f),
                    texture != null ? texture.width / (float)Mathf.Max(1, texture.height) : 1f);
                fittedCanvasSize = fit.size;
                Rect canvas = new Rect(localViewport.center + pan - fit.size * zoom * 0.5f, fit.size * zoom);
                // Clip all texture and overlay drawing to the viewport; UVs stay normalized during zoom/pan.
                DrawBackground(canvas);
                if (texture != null) DrawTexture(canvas, texture);
                if (showOutlines) DrawOverlays(canvas);
                HandleInput(texture, localViewport, canvas, control);
                if (Event.current.type == EventType.Repaint && (gesture == Gesture.New || gesture == Gesture.Redraw))
                    DrawOutline(HairAtlasEditingUtility.UvToCanvas(pendingRect, canvas), Color.cyan, 2f);
            }
            finally { GUI.EndGroup(); }
            string instruction = tool == EditTool.Draw ? "Drag to add a UV set. Esc cancels." :
                tool == EditTool.Redraw ? "Drag to replace the selected rectangle. Esc cancels." :
                "Drag a set to move; drag its corners to resize. Wheel zooms; middle-drag pans.";
            EditorGUILayout.LabelField(instruction, EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawBackground(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!checkerboard)
            {
                Color color = solidBackground;
                color.a = 1f;
                EditorGUI.DrawRect(rect, color);
                return;
            }
            if (checkerTexture == null)
            {
                checkerTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "Hair UV Preview Checkerboard",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Repeat
                };
                Color light = checkerLight; light.a = 1f;
                Color dark = checkerDark; dark.a = 1f;
                checkerTexture.SetPixels(new[] { light, dark, dark, light });
                checkerTexture.Apply(false, true);
            }
            GUI.DrawTextureWithTexCoords(rect, checkerTexture,
                new Rect(0f, 0f, rect.width / (checkerSize * 2f), rect.height / (checkerSize * 2f)), false);
        }

        private void DrawTexture(Rect rect, Texture texture)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (channel == PreviewChannel.Alpha)
                EditorGUI.DrawTextureAlpha(rect, texture, ScaleMode.StretchToFill);
            else
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, channel == PreviewChannel.ColorAndAlpha);
        }

        private void DrawOverlays(Rect canvas)
        {
            for (int index = 0; index < atlas.regions.Count; index++)
            {
                HairAtlasRegion region = atlas.regions[index];
                if (region == null || region.Id == selectedId) continue;
                DrawRegion(region, index, canvas, false);
            }
            HairAtlasRegion selected = Selected;
            if (selected != null)
                DrawRegion(selected, atlas.regions.IndexOf(selected), canvas, true);
        }

        private void DrawRegion(HairAtlasRegion region, int index, Rect canvas, bool selected)
        {
            Rect uv = selected && (gesture == Gesture.Move || gesture == Gesture.Resize) ? pendingRect : region.uvRect;
            Rect rect = HairAtlasEditingUtility.UvToCanvas(uv, canvas);
            bool used = group == null || group.atlasRegionSelection == HairAtlasRegionSelectionMode.All ||
                        group.atlasRegionIds.Contains(region.Id);
            Color color = selected ? Color.cyan : used ? new Color(1f, 0.8f, 0.3f) : Color.gray;
            DrawOutline(rect, Color.black, selected ? 4f : 3f);
            DrawOutline(rect, color, selected ? 2f : 1f);
            Rect badge = new Rect(rect.x + 4f, rect.y + 4f, Mathf.Min(170f, Mathf.Max(32f, rect.width - 8f)), 18f);
            EditorGUI.DrawRect(badge, new Color(0f, 0f, 0f, 0.8f));
            badgeStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { clipping = TextClipping.Clip };
            badgeStyle.normal.textColor = color;
            GUI.Label(badge, new GUIContent($" {index + 1}. {region.name}", region.name),
                badgeStyle);
            if (!selected) return;
            for (int i = 0; i < 4; i++)
            {
                Vector2 point = Corner(rect, i);
                EditorGUI.DrawRect(new Rect(point.x - 4f, point.y - 4f, 8f, 8f), Color.black);
                EditorGUI.DrawRect(new Rect(point.x - 3f, point.y - 3f, 6f, 6f), Color.cyan);
                EditorGUIUtility.AddCursorRect(new Rect(point.x - 6f, point.y - 6f, 12f, 12f),
                    i == 0 || i == 3 ? MouseCursor.ResizeUpLeft : MouseCursor.ResizeUpRight);
            }
        }

        private void DrawSetList(int control, float height)
        {
            EditorGUILayout.LabelField($"UV Sets ({atlas.regions.Count})", EditorStyles.boldLabel);
            if (group != null)
            {
                int mode = GUILayout.Toolbar((int)group.atlasRegionSelection, new[] { "Use all", "Use checked" });
                if (mode != (int)group.atlasRegionSelection)
                {
                    Undo.RecordObject(groom, "Change Hair UV Set Assignment");
                    // Preserve the current effective selection when first switching from All.
                    if (mode == (int)HairAtlasRegionSelectionMode.Selected && group.atlasRegionIds.Count == 0)
                        foreach (HairAtlasRegion region in atlas.regions)
                            if (region != null) group.atlasRegionIds.Add(region.Id);
                    group.atlasRegionSelection = (HairAtlasRegionSelectionMode)mode;
                    HairGroomCommands.Commit(groom, HairPreviewChange.Uvs);
                }
            }
            listScroll = EditorGUILayout.BeginScrollView(listScroll, GUILayout.Height(height));
            for (int i = 0; i < atlas.regions.Count; i++)
            {
                HairAtlasRegion region = atlas.regions[i];
                if (region == null) continue;
                using (new EditorGUILayout.HorizontalScope(GUILayout.Height(32f)))
                {
                    if (group != null)
                    {
                        bool used = group.atlasRegionSelection == HairAtlasRegionSelectionMode.All ||
                                    group.atlasRegionIds.Contains(region.Id);
                        using (new EditorGUI.DisabledScope(group.atlasRegionSelection == HairAtlasRegionSelectionMode.All))
                        {
                            bool next = EditorGUILayout.Toggle(used, GUILayout.Width(18f));
                            if (next != used)
                            {
                                Undo.RecordObject(groom, "Assign Hair UV Set");
                                if (next) group.atlasRegionIds.Add(region.Id);
                                else group.atlasRegionIds.Remove(region.Id);
                                HairGroomCommands.Commit(groom, HairPreviewChange.Uvs);
                            }
                        }
                    }
                    bool selected = region.Id == selectedId;
                    Rect thumbnail = GUILayoutUtility.GetRect(26f, 30f, GUILayout.Width(26f));
                    if (Event.current.type == EventType.Repaint)
                    {
                        DrawBackground(thumbnail);
                        Texture texture = HairAtlasRegionEditorWindow.ResolveDisplayTexture(atlas);
                        if (texture != null)
                        {
                            Rect uv = region.uvRect;
                            if (region.flipU) { uv.x += uv.width; uv.width = -uv.width; }
                            if (region.flipV) { uv.y += uv.height; uv.height = -uv.height; }
                            GUI.DrawTextureWithTexCoords(thumbnail, texture, uv, true);
                        }
                    }
                    if (GUILayout.Toggle(selected, new GUIContent($"{i + 1}. {region.name}",
                            $"UV: {region.uvRect}\nWeight: {region.weight:0.##}"), "Button") && !selected)
                    {
                        CancelInteraction();
                        selectedId = region.Id;
                        GUIUtility.keyboardControl = control;
                        repaint?.Invoke();
                    }
                }
            }
            if (atlas.regions.Count == 0)
                EditorGUILayout.LabelField("No sets yet. Choose + Draw Set and drag on the atlas.",
                    EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("+ Add Set", "Draw a rectangle on the atlas to add a UV set.")))
                {
                    CancelInteraction();
                    tool = EditTool.Draw;
                    status = "Drag a rectangle on the atlas to create the new UV set.";
                }
                using (new EditorGUI.DisabledScope(Selected == null))
                {
                    if (GUILayout.Button("Duplicate")) DuplicateSelected();
                    if (GUILayout.Button("Remove")) RemoveSelected();
                }
            }
            using (new EditorGUI.DisabledScope(Selected == null))
                if (GUILayout.Button("Frame selected (F)")) FrameSelected();
            EditorGUILayout.LabelField("Focus canvas: arrows = 1 pixel · Shift = 10 · Ctrl/Cmd+D = duplicate", EditorStyles.wordWrappedMiniLabel);
            if (group != null && group.atlasRegionSelection == HairAtlasRegionSelectionMode.Selected &&
                !atlas.regions.Exists(region => region != null && group.atlasRegionIds.Contains(region.Id)))
                EditorGUILayout.HelpBox("Check at least one set to assign atlas UVs to this group.", MessageType.Warning);
        }

        private void DrawSelectedProperties(Texture texture)
        {
            HairAtlasRegion selected = Selected;
            if (selected == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Selected UV Set", EditorStyles.boldLabel);
                pixelUnits = GUILayout.Toolbar(pixelUnits ? 1 : 0, new[] { "Normalized UV", "Pixels" }) == 1;
                Vector2 size = texture != null ? new Vector2(texture.width, texture.height) : Vector2.one;
                Rect display = pixelUnits ? HairAtlasEditingUtility.ScaleRect(selected.uvRect, size) : selected.uvRect;
                EditorGUI.BeginChangeCheck();
                string name = EditorGUILayout.DelayedTextField("Name", selected.name);
                Rect rect = EditorGUILayout.RectField(new GUIContent(pixelUnits ? "Pixels (bottom-left)" : "UV Rectangle",
                    "Origin is the bottom-left corner. Drag corners on the atlas to resize visually."), display);
                float weight = Mathf.Max(0f, EditorGUILayout.FloatField(new GUIContent("Selection Weight",
                    "Relative frequency when generated cards choose between eligible UV sets."), selected.weight));
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool flipU = EditorGUILayout.ToggleLeft("Flip U", selected.flipU);
                    bool flipV = EditorGUILayout.ToggleLeft("Flip V", selected.flipV);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(atlas, "Edit Hair UV Set");
                        selected.name = string.IsNullOrWhiteSpace(name) ? "UV Set" : name;
                        selected.uvRect = HairAtlasEditingUtility.ClampRect(pixelUnits
                            ? HairAtlasEditingUtility.ScaleRect(rect, new Vector2(1f / size.x, 1f / size.y)) : rect);
                        selected.weight = weight;
                        selected.flipU = flipU;
                        selected.flipV = flipV;
                        selected.EnsureIntegrity();
                        Changed();
                    }
                }
                if (texture == null && pixelUnits)
                    EditorGUILayout.LabelField("Assign a texture to use its pixel dimensions.", EditorStyles.wordWrappedMiniLabel);
                string tags = string.Join(", ", selected.tags ?? Array.Empty<string>());
                string editedTags = EditorGUILayout.DelayedTextField("Tags", tags);
                if (editedTags != tags)
                {
                    Undo.RecordObject(atlas, "Edit Hair UV Set Tags");
                    List<string> parsed = new List<string>();
                    foreach (string part in editedTags.Split(','))
                    {
                        string tag = part.Trim();
                        if (tag.Length > 0 && !parsed.Contains(tag)) parsed.Add(tag);
                    }
                    selected.tags = parsed.ToArray();
                    Changed();
                }
            }
        }

        private void HandleInput(Texture texture, Rect viewport, Rect canvas, int control)
        {
            Event evt = Event.current;
            if (evt.type == EventType.KeyDown && GUIUtility.keyboardControl == control &&
                !EditorGUIUtility.editingTextField && gesture == Gesture.None && Selected != null)
            {
                if (evt.keyCode == KeyCode.F) { FrameSelected(); evt.Use(); return; }
                if (evt.keyCode == KeyCode.D && (evt.control || evt.command)) { DuplicateSelected(); evt.Use(); return; }
                Vector2 direction = evt.keyCode switch
                { KeyCode.LeftArrow => Vector2.left, KeyCode.RightArrow => Vector2.right, KeyCode.UpArrow => Vector2.up, KeyCode.DownArrow => Vector2.down, _ => Vector2.zero };
                if (direction != Vector2.zero)
                {
                    Vector2 step = texture != null ? new Vector2(1f / texture.width, 1f / texture.height) : Vector2.one * 0.001f;
                    NudgeSelected(Vector2.Scale(direction, step) * (evt.shift ? 10f : 1f));
                    evt.Use(); return;
                }
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape &&
                (gesture != Gesture.None || tool != EditTool.Select))
            {
                CancelInteraction();
                evt.Use();
                repaint?.Invoke();
                return;
            }
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Delete &&
                GUIUtility.keyboardControl == control && !EditorGUIUtility.editingTextField)
            {
                RemoveSelected();
                evt.Use();
                return;
            }
            Vector2 mouse = evt.mousePosition;
            Vector2 uv = new Vector2((mouse.x - canvas.x) / canvas.width, 1f - (mouse.y - canvas.y) / canvas.height);
            if (evt.type == EventType.ScrollWheel && viewport.Contains(mouse) && gesture == Gesture.None)
            {
                float nextZoom = Mathf.Clamp(zoom * Mathf.Exp(-evt.delta.y * 0.08f), 1f, 8f);
                pan = mouse - viewport.center - (mouse - viewport.center - pan) * (nextZoom / zoom);
                zoom = nextZoom;
                evt.Use();
                repaint?.Invoke();
            }
            else if (evt.type == EventType.MouseDown && viewport.Contains(mouse) &&
                     GUIUtility.hotControl == 0 && (evt.button == 0 || evt.button == 2))
            {
                if (evt.button == 2) gesture = Gesture.Pan;
                else if (tool != EditTool.Select && canvas.Contains(mouse))
                {
                    gesture = tool == EditTool.Draw ? Gesture.New : Gesture.Redraw;
                    pendingRect = new Rect(ClampUv(uv), Vector2.zero);
                }
                else if (tool == EditTool.Select && showOutlines)
                {
                    corner = HitCorner(mouse, canvas);
                    HairAtlasRegion hit = corner >= 0 ? Selected : HitRegion(mouse, canvas);
                    if (hit == null) return;
                    selectedId = hit.Id;
                    originalRect = hit.uvRect;
                    pendingRect = originalRect;
                    gesture = corner >= 0 ? Gesture.Resize : Gesture.Move;
                }
                else return;
                GUIUtility.keyboardControl = control;
                GUIUtility.hotControl = control;
                ownedControl = control;
                startUv = ClampUv(uv);
                if (snapToPixels && texture != null && (gesture == Gesture.New || gesture == Gesture.Redraw))
                    startUv = HairAtlasEditingUtility.SnapUv(startUv, texture.width, texture.height);
                startMouse = mouse;
                startPan = pan;
                evt.Use();
                repaint?.Invoke();
            }
            else if (evt.type == EventType.MouseDrag && GUIUtility.hotControl == control && gesture != Gesture.None)
            {
                UpdateGesture(texture, uv, mouse);
                evt.Use();
                repaint?.Invoke();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == control && gesture != Gesture.None &&
                     evt.button == (gesture == Gesture.Pan ? 2 : 0))
            {
                // Use the actual release position even when Unity coalesces the final drag event.
                UpdateGesture(texture, uv, mouse);
                Gesture completed = gesture;
                Rect result = pendingRect;
                CancelInteraction();
                if (completed != Gesture.Pan)
                {
                    if ((completed == Gesture.New || completed == Gesture.Redraw) &&
                        (result.width * canvas.width < 2f || result.height * canvas.height < 2f))
                        status = "That set is too small. Drag a larger rectangle.";
                    else if (completed == Gesture.New)
                    {
                        int previousCount = atlas.regions.Count;
                        HairAtlasRegion added = HairAtlasEditingUtility.AddSet(atlas, groom, group, result);
                        selectedId = added.Id;
                        status = atlas.regions.Count == previousCount
                            ? "That rectangle matches an existing UV set, which is now selected."
                            : "UV set added. Drag its corners or enter exact coordinates below.";
                        Changed();
                    }
                    else if (Selected != null && Selected.uvRect != result)
                    {
                        Undo.RecordObject(atlas, "Reshape Hair UV Set");
                        Selected.uvRect = HairAtlasEditingUtility.ClampRect(result);
                        Changed();
                    }
                }
                evt.Use();
                repaint?.Invoke();
                // Adding the first set changes the properties/list layout. Resume with a fresh Layout event.
                if (completed == Gesture.New) GUIUtility.ExitGUI();
            }
        }

        private void UpdateGesture(Texture texture, Vector2 uv, Vector2 mouse)
        {
            if (gesture == Gesture.Pan)
            {
                pan = startPan + mouse - startMouse;
                return;
            }
            Vector2 end = ClampUv(uv);
            if (snapToPixels && texture != null)
                end = HairAtlasEditingUtility.SnapUv(end, texture.width, texture.height);
            if (gesture == Gesture.Move)
            {
                Vector2 delta = uv - startUv;
                if (snapToPixels && texture != null)
                    delta = HairAtlasEditingUtility.SnapUv(delta, texture.width, texture.height);
                pendingRect = HairAtlasEditingUtility.MoveRect(originalRect, delta);
            }
            else if (gesture == Gesture.Resize)
                pendingRect = HairAtlasEditingUtility.ResizeRect(originalRect, corner, end);
            else pendingRect = Rect.MinMaxRect(Mathf.Min(startUv.x, end.x), Mathf.Min(startUv.y, end.y),
                Mathf.Max(startUv.x, end.x), Mathf.Max(startUv.y, end.y));
        }

        internal void CancelInteraction()
        {
            if (ownedControl != 0 && GUIUtility.hotControl == ownedControl) GUIUtility.hotControl = 0;
            ownedControl = 0;
            gesture = Gesture.None;
            tool = EditTool.Select;
            HairCardStage.ActiveStage?.SetWorkspaceInteraction(false);
        }

        internal void NudgeSelected(Vector2 delta)
        {
            if (Selected == null) return;
            Rect rect = HairAtlasEditingUtility.MoveRect(Selected.uvRect, delta);
            if (rect == Selected.uvRect) return;
            Undo.RecordObject(atlas, "Nudge Hair UV Set");
            Selected.uvRect = rect;
            Changed();
        }

        internal void DuplicateSelected()
        {
            HairAtlasRegion source = Selected;
            if (source == null) return;
            CancelInteraction();
            Undo.RecordObject(atlas, "Duplicate Hair UV Set");
            HairAtlasRegion copy = atlas.CreateRegion(source.name + " Copy", source.uvRect, source.weight);
            copy.flipU = source.flipU; copy.flipV = source.flipV;
            copy.tags = source.tags != null ? (string[])source.tags.Clone() : Array.Empty<string>();
            if (group != null && group.atlasRegionSelection == HairAtlasRegionSelectionMode.Selected && group.atlasRegionIds.Contains(source.Id))
            { Undo.RecordObject(groom, "Assign Duplicated UV Set"); group.atlasRegionIds.Add(copy.Id); HairGroomCommands.Commit(groom, HairPreviewChange.Uvs); }
            selectedId = copy.Id;
            status = "Set duplicated with independent coordinates and a new ID.";
            Changed();
        }

        private void FrameSelected()
        {
            if (Selected == null || fittedCanvasSize.x <= 0f) return;
            Rect uv = Selected.uvRect;
            zoom = Mathf.Clamp(0.8f / Mathf.Max(uv.width, uv.height), 1f, 8f);
            pan = -Vector2.Scale(new Vector2(uv.center.x - 0.5f, 0.5f - uv.center.y), fittedCanvasSize) * zoom;
            repaint?.Invoke();
        }

        private void DrawMaterialPreview()
        {
            materialPreviewExpanded = EditorGUILayout.Foldout(materialPreviewExpanded, "Material-rendered card · selected UV set", true);
            if (!materialPreviewExpanded) return;
            if (atlas.material == null || Selected == null)
            { EditorGUILayout.HelpBox("Assign a Card Material and select a UV set to inspect alpha, taper, and front/back rendering here.", MessageType.Info); return; }
            previewYaw = EditorGUILayout.Slider("Turn card", previewYaw, -180f, 180f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Front")) previewYaw = 0f;
                if (GUILayout.Button("Back")) previewYaw = 180f;
            }
            Rect viewport = GUILayoutUtility.GetRect(100f, 205f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            string signature = selectedId + ":" + EditorUtility.GetDirtyCount(atlas) + ":" +
                (group?.profile != null ? group.profile.ProfileId + ":" + EditorUtility.GetDirtyCount(group.profile) : "default");
            if (previewCard == null || signature != previewSignature || previewProfile != group?.profile || previewAtlas != atlas)
            {
                previewCard?.Dispose();
                HairEvaluationResult sample = new HairEvaluationResult();
                HairEvaluatedCurve curve = new HairEvaluatedCurve
                {
                    curveId = "inline-card", groupId = group?.Id, profile = group?.profile,
                    atlas = atlas, atlasRegionSelection = HairAtlasRegionSelectionMode.Selected,
                    atlasRegionIds = new[] { selectedId }, rootNormal = Vector3.forward, groupColor = Color.white
                };
                curve.points.Add(new HairCurvePoint(new Vector3(0f, -0.5f, 0f), 0.35f, 0f));
                curve.points.Add(new HairCurvePoint(new Vector3(0f, 0.5f, 0f), 0.35f, 0f));
                sample.curves.Add(curve);
                previewCard = HairCardMeshGenerator.Build(sample, "Inline Hair Card Preview");
                previewSignature = signature;
                previewProfile = group?.profile;
                previewAtlas = atlas;
            }
            materialPreview ??= new PreviewRenderUtility();
            materialPreview.BeginPreview(viewport, GUIStyle.none);
            Camera camera = materialPreview.camera;
            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = solidBackground;
            camera.orthographic = true; camera.orthographicSize = 0.63f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 10f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -2f), Quaternion.identity);
            materialPreview.lights[0].intensity = 1.1f;
            materialPreview.lights[0].transform.rotation = Quaternion.Euler(30f, 30f, 0f);
            materialPreview.lights[1].intensity = 0.8f;
            materialPreview.ambientColor = new Color(0.4f, 0.4f, 0.4f);
            previewMaterials ??= new HairPreviewMaterialSet();
            Material[] renderedMaterials = previewMaterials.Update(previewCard);
            for (int pass = 0; pass < renderedMaterials.Length; pass++)
                materialPreview.DrawMesh(previewCard.mesh, Matrix4x4.Rotate(Quaternion.Euler(0f, previewYaw, 0f)), renderedMaterials[pass], pass);
            materialPreview.Render(true);
            Texture rendered = materialPreview.EndPreview();
            lastRenderedCard = rendered;
            GUI.DrawTexture(viewport, rendered, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(viewport.x + 6f, viewport.y + 4f, viewport.width - 12f, 20f), "TIP ↑", EditorStyles.whiteMiniLabel);
            GUI.Label(new Rect(viewport.x + 6f, viewport.yMax - 22f, viewport.width - 12f, 20f), "ROOT · illustrative width · actual assigned material", EditorStyles.whiteMiniLabel);
        }

        private void RemoveSelected()
        {
            HairAtlasRegion selected = Selected;
            if (selected == null) return;
            CancelInteraction();
            HairAtlasEditingUtility.RemoveSet(atlas, groom, selected.Id);
            selectedId = atlas.regions.Find(region => region != null)?.Id;
            status = "UV set removed. Undo restores the set and its group assignments.";
            Changed();
            GUIUtility.ExitGUI();
        }

        private void Changed()
        {
            EditorUtility.SetDirty(atlas);
            HairCardStage.ActiveStage?.TrackResourceEdit(atlas);
            HairCardStage.ActiveStage?.QueuePreviewChange(HairPreviewChange.Uvs);
            repaint?.Invoke();
        }

        private HairAtlasRegion HitRegion(Vector2 mouse, Rect canvas)
        {
            // Smallest containing set wins, so a full-atlas default cannot trap smaller sets.
            HairAtlasRegion best = null;
            float area = float.MaxValue;
            foreach (HairAtlasRegion region in atlas.regions)
            {
                if (region == null) continue;
                Rect rect = HairAtlasEditingUtility.UvToCanvas(region.uvRect, canvas);
                if (!rect.Contains(mouse) || rect.width * rect.height >= area) continue;
                area = rect.width * rect.height;
                best = region;
            }
            return best;
        }

        private int HitCorner(Vector2 mouse, Rect canvas)
        {
            if (Selected == null) return -1;
            Rect rect = HairAtlasEditingUtility.UvToCanvas(Selected.uvRect, canvas);
            for (int i = 0; i < 4; i++)
                if ((Corner(rect, i) - mouse).sqrMagnitude <= 64f) return i;
            return -1;
        }

        private static Vector2 Corner(Rect rect, int index) => new Vector2(
            (index & 1) == 0 ? rect.xMin : rect.xMax, index < 2 ? rect.yMin : rect.yMax);
        private static Vector2 ClampUv(Vector2 uv) => new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
        private static Rect FitAspect(Rect rect, float aspect)
        {
            rect.width = Mathf.Max(1f, rect.width);
            rect.height = Mathf.Max(1f, rect.height);
            Vector2 size = rect.width / rect.height > aspect
                ? new Vector2(rect.height * aspect, rect.height) : new Vector2(rect.width, rect.width / aspect);
            return new Rect(rect.center - size * 0.5f, size);
        }
        private static void DrawOutline(Rect rect, Color color, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - width, rect.width, width), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, width, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - width, rect.y, width, rect.height), color);
        }
        private void ReleaseCheckerTexture()
        {
            if (checkerTexture != null) UnityEngine.Object.DestroyImmediate(checkerTexture);
            checkerTexture = null;
        }
        public void Dispose()
        {
            CancelInteraction();
            SavePreferences();
            ReleaseCheckerTexture();
            previewCard?.Dispose(); previewCard = null;
            materialPreview?.Cleanup(); materialPreview = null;
            previewMaterials?.Dispose(); previewMaterials = null;
            lastRenderedCard = null;
        }
    }

    internal static class HairAtlasEditingUtility
    {
        internal static Vector2 SnapUv(Vector2 uv, int width, int height) => new Vector2(
            Mathf.Round(uv.x * Mathf.Max(1, width)) / Mathf.Max(1, width),
            Mathf.Round(uv.y * Mathf.Max(1, height)) / Mathf.Max(1, height));
        internal static Rect UvToCanvas(Rect uv, Rect canvas) => new Rect(
            canvas.x + uv.x * canvas.width, canvas.y + (1f - uv.yMax) * canvas.height,
            uv.width * canvas.width, uv.height * canvas.height);

        internal static Rect ScaleRect(Rect rect, Vector2 scale) => new Rect(
            rect.x * scale.x, rect.y * scale.y, rect.width * scale.x, rect.height * scale.y);

        internal static Rect ClampRect(Rect rect)
        {
            const float minimumSize = 0.00001f;
            float x = Mathf.Clamp(rect.x, 0f, 1f - minimumSize);
            float y = Mathf.Clamp(rect.y, 0f, 1f - minimumSize);
            return new Rect(x, y, Mathf.Clamp(rect.width, minimumSize, 1f - x),
                Mathf.Clamp(rect.height, minimumSize, 1f - y));
        }

        internal static Rect MoveRect(Rect rect, Vector2 delta) => new Rect(
            Mathf.Clamp(rect.x + delta.x, 0f, 1f - rect.width),
            Mathf.Clamp(rect.y + delta.y, 0f, 1f - rect.height), rect.width, rect.height);

        internal static Rect ResizeRect(Rect rect, int corner, Vector2 target)
        {
            const float minimumSize = 0.00001f;
            if ((corner & 1) == 0) rect.xMin = Mathf.Clamp(target.x, 0f, rect.xMax - minimumSize);
            else rect.xMax = Mathf.Clamp(target.x, rect.xMin + minimumSize, 1f);
            if (corner < 2) rect.yMax = Mathf.Clamp(target.y, rect.yMin + minimumSize, 1f);
            else rect.yMin = Mathf.Clamp(target.y, 0f, rect.yMax - minimumSize);
            return rect;
        }

        internal static HairAtlasRegion AddSet(HairAtlasProfileAsset atlas, HairGroomAsset groom,
            HairGroup group, Rect rect)
        {
            rect = ClampRect(rect);
            HairAtlasRegion duplicate = atlas.regions.Find(region => region != null &&
                Mathf.Abs(region.uvRect.x - rect.x) < 0.00001f &&
                Mathf.Abs(region.uvRect.y - rect.y) < 0.00001f &&
                Mathf.Abs(region.uvRect.width - rect.width) < 0.00001f &&
                Mathf.Abs(region.uvRect.height - rect.height) < 0.00001f);
            if (duplicate != null) return duplicate;
            if (groom != null) Undo.RecordObjects(new UnityEngine.Object[] { atlas, groom }, "Add Hair UV Set");
            else Undo.RecordObject(atlas, "Add Hair UV Set");
            int number = atlas.regions.Count + 1;
            while (atlas.regions.Exists(region => region != null && region.name == $"UV Set {number}")) number++;
            HairAtlasRegion added = atlas.CreateRegion($"UV Set {number}", rect);
            if (group != null && group.atlas == atlas && group.atlasRegionSelection == HairAtlasRegionSelectionMode.Selected)
                group.atlasRegionIds.Add(added.Id);
            EditorUtility.SetDirty(atlas);
            if (groom != null) HairGroomCommands.Commit(groom, HairPreviewChange.Uvs);
            return added;
        }

        internal static void RemoveSet(HairAtlasProfileAsset atlas, HairGroomAsset groom, string id)
        {
            HairAtlasRegion region = atlas.regions.Find(item => item != null && item.Id == id);
            if (region == null) return;
            if (groom != null) Undo.RecordObjects(new UnityEngine.Object[] { atlas, groom }, "Remove Hair UV Set");
            else Undo.RecordObject(atlas, "Remove Hair UV Set");
            atlas.regions.Remove(region);
            if (groom != null)
            {
                foreach (HairGroup group in groom.Groups)
                    if (group?.atlas == atlas) group.atlasRegionIds.Remove(id);
                HairGroomCommands.Commit(groom, HairPreviewChange.Uvs);
            }
            EditorUtility.SetDirty(atlas);
        }
    }
}
