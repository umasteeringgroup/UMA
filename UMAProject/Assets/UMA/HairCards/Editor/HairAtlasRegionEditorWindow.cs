using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public sealed class HairAtlasRegionEditorWindow : EditorWindow
    {
        private const float SidebarWidth = 310f;
        private const int CanvasControlHint = 0x48415641;
        private const float DuplicateTolerance = 0.003f;

        private enum DrawOperation
        {
            None,
            NewArea,
            RedrawSelected
        }

        [SerializeField] private HairAtlasProfileAsset atlas;
        [SerializeField] private int selectedRegionIndex = -1;
        [SerializeField] private DrawOperation drawOperation;
        [SerializeField] private Vector2 sidebarScroll;

        private Vector2 dragStart;
        private Vector2 dragCurrent;
        private bool isDragging;

        public static void Open(HairAtlasProfileAsset profile)
        {
            HairAtlasRegionEditorWindow window = GetWindow<HairAtlasRegionEditorWindow>(true,
                "Hair Atlas UV Areas", true);
            window.atlas = profile;
            window.selectedRegionIndex = profile != null && profile.regions != null && profile.regions.Count > 0
                ? 0
                : -1;
            window.drawOperation = DrawOperation.None;
            window.isDragging = false;
            window.minSize = new Vector2(760f, 520f);
            window.Show();
            window.Focus();
        }

        public static Texture ResolveDisplayTexture(HairAtlasProfileAsset profile)
        {
            if (profile == null) return null;
            if (profile.albedo != null) return profile.albedo;
            Material material = profile.material;
            if (material == null) return null;
            if (material.HasProperty("_BaseMap"))
            {
                Texture baseMap = material.GetTexture("_BaseMap");
                if (baseMap != null) return baseMap;
            }
            if (material.HasProperty("_MainTex"))
            {
                Texture mainTexture = material.GetTexture("_MainTex");
                if (mainTexture != null) return mainTexture;
            }
            return material.mainTexture;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        private void OnUndoRedo()
        {
            ClampSelection();
            NotifyAtlasChanged();
        }

        private void OnGUI()
        {
            DrawHeader();
            if (atlas == null)
            {
                EditorGUILayout.HelpBox("Assign a Hair Atlas Profile to define UV areas.", MessageType.Info);
                return;
            }

            atlas.EnsureIntegrity();
            ClampSelection();
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawCanvasPanel();
                DrawSidebar();
            }
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                HairAtlasProfileAsset nextAtlas = (HairAtlasProfileAsset)EditorGUILayout.ObjectField(
                    atlas, typeof(HairAtlasProfileAsset), false, GUILayout.MinWidth(180f));
                if (EditorGUI.EndChangeCheck())
                {
                    atlas = nextAtlas;
                    selectedRegionIndex = atlas != null && atlas.regions != null && atlas.regions.Count > 0 ? 0 : -1;
                    drawOperation = DrawOperation.None;
                    isDragging = false;
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("Drag directly over the atlas to define normalized UV rectangles.",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawCanvasPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                Texture texture = ResolveDisplayTexture(atlas);
                Rect container = GUILayoutUtility.GetRect(320f, 320f,
                    GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(container, new Color(0.075f, 0.075f, 0.075f, 1f));
                float aspect = texture != null && texture.height > 0
                    ? texture.width / (float)texture.height
                    : 1f;
                Rect canvas = FitAspect(container, aspect);
                DrawCheckerboard(canvas);
                if (texture != null)
                    GUI.DrawTexture(canvas, texture, ScaleMode.StretchToFill, false);
                else
                    GUI.Label(canvas, "No Albedo Atlas or material base texture assigned", CenteredLabel());

                DrawRegionOverlays(canvas);
                DrawActiveDrag(canvas);
                HandleCanvasInput(canvas);
                DrawCanvasStatus(canvas);
            }
        }

        private void DrawSidebar()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(SidebarWidth)))
            {
                sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);
                DrawAtlasResources();
                EditorGUILayout.Space(6f);
                DrawDrawingControls();
                EditorGUILayout.Space(6f);
                DrawDuplicateWarnings();
                DrawAreaList();
                DrawSelectedAreaProperties();
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawAtlasResources()
        {
            EditorGUILayout.LabelField("Atlas Preview", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Texture2D albedo = (Texture2D)EditorGUILayout.ObjectField("Albedo Atlas", atlas.albedo,
                typeof(Texture2D), false);
            Material material = (Material)EditorGUILayout.ObjectField("Card Material", atlas.material,
                typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(atlas, "Edit Hair Atlas Preview");
                atlas.albedo = albedo;
                atlas.material = material;
                EditorUtility.SetDirty(atlas);
                NotifyAtlasChanged();
            }
            if (atlas.albedo == null && ResolveDisplayTexture(atlas) != null)
            {
                EditorGUILayout.HelpBox(
                    "The preview is using the base texture from the Card Material. Assign Albedo Atlas explicitly to make the atlas source unambiguous.",
                    MessageType.Info);
            }
        }

        private void DrawDrawingControls()
        {
            EditorGUILayout.LabelField("Draw UV Area", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool drawingNew = drawOperation == DrawOperation.NewArea;
                bool nextDrawingNew = GUILayout.Toggle(drawingNew, "Draw New Area", "Button");
                if (nextDrawingNew != drawingNew)
                    SetDrawOperation(nextDrawingNew ? DrawOperation.NewArea : DrawOperation.None);
                using (new EditorGUI.DisabledScope(GetSelectedRegion() == null))
                {
                    bool redrawing = drawOperation == DrawOperation.RedrawSelected;
                    bool nextRedrawing = GUILayout.Toggle(redrawing, "Redraw Selected", "Button");
                    if (nextRedrawing != redrawing)
                        SetDrawOperation(nextRedrawing ? DrawOperation.RedrawSelected : DrawOperation.None);
                }
            }
            if (drawOperation != DrawOperation.None && GUILayout.Button("Cancel Drawing"))
                SetDrawOperation(DrawOperation.None);

            string instructions = drawOperation switch
            {
                DrawOperation.NewArea => "Drag a rectangle over the texture. Releasing creates and selects a new numbered area.",
                DrawOperation.RedrawSelected => "Drag a replacement rectangle for the selected area. The old rectangle remains until you release.",
                _ => "Click an existing rectangle or its list entry to select it. Use Draw New Area or Redraw Selected before dragging."
            };
            EditorGUILayout.HelpBox(instructions, MessageType.Info);
        }

        private void DrawDuplicateWarnings()
        {
            List<string> duplicates = FindExistingDuplicateDescriptions();
            if (duplicates.Count == 0) return;
            EditorGUILayout.HelpBox(
                "Nearly identical UV areas already exist: " + string.Join(", ", duplicates) +
                ". Select one and redraw or remove it.", MessageType.Warning);
        }

        private void DrawAreaList()
        {
            EditorGUILayout.LabelField($"Defined UV Areas ({atlas.regions.Count})", EditorStyles.boldLabel);
            if (atlas.regions.Count == 0)
            {
                EditorGUILayout.HelpBox("No UV areas are defined. Click Draw New Area, then drag over the atlas.",
                    MessageType.Warning);
                return;
            }

            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region == null) continue;
                bool selected = selectedRegionIndex == regionIndex;
                using (new EditorGUILayout.HorizontalScope())
                {
                    Rect swatch = GUILayoutUtility.GetRect(13f, 13f, GUILayout.Width(13f), GUILayout.Height(13f));
                    EditorGUI.DrawRect(swatch, RegionColor(regionIndex, selected));
                    string name = string.IsNullOrWhiteSpace(region.name) ? "Unnamed" : region.name;
                    if (GUILayout.Toggle(selected, $"{regionIndex + 1}. {name}", "Button"))
                    {
                        selectedRegionIndex = regionIndex;
                        if (drawOperation == DrawOperation.RedrawSelected) isDragging = false;
                        Repaint();
                    }
                }
                EditorGUILayout.LabelField(
                    $"    X {region.uvRect.x:F3}   Y {region.uvRect.y:F3}   W {region.uvRect.width:F3}   H {region.uvRect.height:F3}",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawSelectedAreaProperties()
        {
            HairAtlasRegion region = GetSelectedRegion();
            if (region == null) return;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField($"Selected: Area {selectedRegionIndex + 1}", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string regionName = EditorGUILayout.TextField("Name", region.name);
            Rect uvRect = EditorGUILayout.RectField("UV Rectangle", region.uvRect);
            float weight = Mathf.Max(0f, EditorGUILayout.FloatField("Selection Weight", region.weight));
            bool flipU = EditorGUILayout.Toggle("Flip U", region.flipU);
            bool flipV = EditorGUILayout.Toggle("Flip V", region.flipV);
            string tags = EditorGUILayout.TextField("Tags", string.Join(", ", region.tags ?? Array.Empty<string>()));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(atlas, "Edit Hair UV Area");
                region.name = string.IsNullOrWhiteSpace(regionName)
                    ? $"Area {selectedRegionIndex + 1}"
                    : regionName;
                region.uvRect = uvRect;
                region.weight = weight;
                region.flipU = flipU;
                region.flipV = flipV;
                region.tags = ParseTags(tags);
                region.EnsureIntegrity();
                EditorUtility.SetDirty(atlas);
                NotifyAtlasChanged();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Redraw on Atlas")) SetDrawOperation(DrawOperation.RedrawSelected);
                if (GUILayout.Button("Remove Area")) RemoveSelectedRegion();
            }
        }

        private void DrawRegionOverlays(Rect canvas)
        {
            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region == null) continue;
                Rect rectangle = UvToCanvas(region.uvRect, canvas);
                bool selected = selectedRegionIndex == regionIndex;
                Color color = RegionColor(regionIndex, selected);
                Color fill = color;
                fill.a = selected ? 0.2f : 0.08f;
                EditorGUI.DrawRect(rectangle, fill);
                DrawOutline(rectangle, color, selected ? 3f : 2f);

                string name = string.IsNullOrWhiteSpace(region.name) ? "" : "  " + region.name;
                GUIContent label = new GUIContent($"{regionIndex + 1}{name}");
                Vector2 labelSize = EditorStyles.miniBoldLabel.CalcSize(label);
                Rect labelRect = new Rect(rectangle.x + 3f, rectangle.y + 3f,
                    Mathf.Min(labelSize.x + 8f, Mathf.Max(0f, rectangle.width - 6f)), labelSize.y + 3f);
                if (labelRect.width > 8f)
                {
                    EditorGUI.DrawRect(labelRect, new Color(0f, 0f, 0f, 0.72f));
                    GUI.Label(labelRect, label, WhiteMiniBoldLabel());
                }
            }
        }

        private void DrawActiveDrag(Rect canvas)
        {
            if (!isDragging || drawOperation == DrawOperation.None) return;
            Rect rectangle = MakeRect(ClampToRect(dragStart, canvas), ClampToRect(dragCurrent, canvas));
            EditorGUI.DrawRect(rectangle, new Color(0.05f, 0.9f, 1f, 0.16f));
            DrawOutline(rectangle, new Color(0.05f, 0.95f, 1f, 1f), 3f);
        }

        private void DrawCanvasStatus(Rect canvas)
        {
            string status = drawOperation switch
            {
                DrawOperation.NewArea => "DRAW NEW AREA: left-drag on the atlas; Esc cancels",
                DrawOperation.RedrawSelected => $"REDRAW AREA {selectedRegionIndex + 1}: left-drag on the atlas; Esc cancels",
                _ => "Click an outlined area to select it"
            };
            Rect statusRect = new Rect(canvas.x + 6f, canvas.yMax - 27f,
                Mathf.Max(0f, canvas.width - 12f), 21f);
            EditorGUI.DrawRect(statusRect, new Color(0f, 0f, 0f, 0.72f));
            GUI.Label(statusRect, status, WhiteMiniBoldLabel());
        }

        private void HandleCanvasInput(Rect canvas)
        {
            Event current = Event.current;
            int controlId = GUIUtility.GetControlID(CanvasControlHint, FocusType.Passive);
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape &&
                drawOperation != DrawOperation.None)
            {
                SetDrawOperation(DrawOperation.None);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0 && canvas.Contains(current.mousePosition))
            {
                if (drawOperation == DrawOperation.None)
                {
                    selectedRegionIndex = HitTestRegion(current.mousePosition, canvas);
                    Repaint();
                }
                else
                {
                    GUIUtility.hotControl = controlId;
                    dragStart = ClampToRect(current.mousePosition, canvas);
                    dragCurrent = dragStart;
                    isDragging = true;
                }
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && current.button == 0 &&
                     GUIUtility.hotControl == controlId && isDragging)
            {
                dragCurrent = ClampToRect(current.mousePosition, canvas);
                Repaint();
                current.Use();
            }
            else if (current.type == EventType.MouseUp && current.button == 0 &&
                     GUIUtility.hotControl == controlId && isDragging)
            {
                dragCurrent = ClampToRect(current.mousePosition, canvas);
                GUIUtility.hotControl = 0;
                isDragging = false;
                CompleteDraw(canvas);
                current.Use();
            }
        }

        private void CompleteDraw(Rect canvas)
        {
            Rect pixelRectangle = MakeRect(dragStart, dragCurrent);
            if (pixelRectangle.width < 4f || pixelRectangle.height < 4f)
            {
                ShowNotification(new GUIContent("The UV area is too small. Drag a larger rectangle."));
                Repaint();
                return;
            }

            Rect uvRectangle = CanvasToUv(pixelRectangle, canvas);
            int ignoredIndex = drawOperation == DrawOperation.RedrawSelected ? selectedRegionIndex : -1;
            int duplicateIndex = FindDuplicate(uvRectangle, ignoredIndex);
            if (duplicateIndex >= 0)
            {
                selectedRegionIndex = duplicateIndex;
                drawOperation = DrawOperation.None;
                ShowNotification(new GUIContent(
                    $"That matches Area {duplicateIndex + 1}; the existing area was selected instead."), 3f);
                Repaint();
                return;
            }

            if (drawOperation == DrawOperation.NewArea)
            {
                Undo.RecordObject(atlas, "Draw Hair UV Area");
                atlas.CreateRegion($"Area {atlas.regions.Count + 1}", uvRectangle);
                selectedRegionIndex = atlas.regions.Count - 1;
            }
            else if (drawOperation == DrawOperation.RedrawSelected)
            {
                HairAtlasRegion region = GetSelectedRegion();
                if (region == null) return;
                Undo.RecordObject(atlas, "Redraw Hair UV Area");
                region.uvRect = uvRectangle;
                region.EnsureIntegrity();
            }

            drawOperation = DrawOperation.None;
            EditorUtility.SetDirty(atlas);
            NotifyAtlasChanged();
        }

        private void RemoveSelectedRegion()
        {
            HairAtlasRegion selectedRegion = GetSelectedRegion();
            if (selectedRegion == null) return;
            int removedNumber = selectedRegionIndex + 1;
            HairGroomAsset activeGroom = HairCardStage.ActiveStage?.Groom;
            if (activeGroom != null)
                Undo.RecordObjects(new UnityEngine.Object[] { atlas, activeGroom }, "Remove Hair UV Area");
            else
                Undo.RecordObject(atlas, "Remove Hair UV Area");
            atlas.regions.RemoveAt(selectedRegionIndex);
            if (activeGroom != null)
            {
                for (int groupIndex = 0; groupIndex < activeGroom.Groups.Count; groupIndex++)
                {
                    HairGroup group = activeGroom.Groups[groupIndex];
                    if (group != null && group.atlas == atlas)
                        group.atlasRegionIds?.Remove(selectedRegion.Id);
                }
                HairGroomCommands.Commit(activeGroom);
            }
            selectedRegionIndex = Mathf.Clamp(selectedRegionIndex, 0, atlas.regions.Count - 1);
            if (atlas.regions.Count == 0) selectedRegionIndex = -1;
            drawOperation = DrawOperation.None;
            EditorUtility.SetDirty(atlas);
            ShowNotification(new GUIContent($"Removed Area {removedNumber}. Undo is available."));
            NotifyAtlasChanged();
        }

        private void SetDrawOperation(DrawOperation operation)
        {
            if (operation == DrawOperation.RedrawSelected && GetSelectedRegion() == null) operation = DrawOperation.None;
            drawOperation = operation;
            isDragging = false;
            Repaint();
        }

        private int HitTestRegion(Vector2 mousePosition, Rect canvas)
        {
            int bestIndex = -1;
            float smallestArea = float.MaxValue;
            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region == null) continue;
                Rect rectangle = UvToCanvas(region.uvRect, canvas);
                float area = rectangle.width * rectangle.height;
                if (rectangle.Contains(mousePosition) && area < smallestArea)
                {
                    bestIndex = regionIndex;
                    smallestArea = area;
                }
            }
            return bestIndex;
        }

        private int FindDuplicate(Rect rectangle, int ignoredIndex)
        {
            for (int regionIndex = 0; regionIndex < atlas.regions.Count; regionIndex++)
            {
                if (regionIndex == ignoredIndex) continue;
                HairAtlasRegion region = atlas.regions[regionIndex];
                if (region != null && NearlyEqual(region.uvRect, rectangle)) return regionIndex;
            }
            return -1;
        }

        private List<string> FindExistingDuplicateDescriptions()
        {
            List<string> duplicates = new List<string>();
            for (int first = 0; first < atlas.regions.Count; first++)
            {
                HairAtlasRegion firstRegion = atlas.regions[first];
                if (firstRegion == null) continue;
                for (int second = first + 1; second < atlas.regions.Count; second++)
                {
                    HairAtlasRegion secondRegion = atlas.regions[second];
                    if (secondRegion != null && NearlyEqual(firstRegion.uvRect, secondRegion.uvRect))
                        duplicates.Add($"{first + 1} and {second + 1}");
                }
            }
            return duplicates;
        }

        private HairAtlasRegion GetSelectedRegion()
        {
            if (atlas?.regions == null || selectedRegionIndex < 0 || selectedRegionIndex >= atlas.regions.Count)
                return null;
            return atlas.regions[selectedRegionIndex];
        }

        private void ClampSelection()
        {
            if (atlas?.regions == null || atlas.regions.Count == 0)
            {
                selectedRegionIndex = -1;
                if (drawOperation == DrawOperation.RedrawSelected) drawOperation = DrawOperation.None;
                return;
            }
            selectedRegionIndex = Mathf.Clamp(selectedRegionIndex, 0, atlas.regions.Count - 1);
        }

        private void NotifyAtlasChanged()
        {
            HairCardStage.ActiveStage?.QueueRebuild();
            HairGroomWorkspace.RepaintOpenWindows();
            Repaint();
        }

        private static Rect CanvasToUv(Rect pixelRectangle, Rect canvas)
        {
            float x = Mathf.Clamp01((pixelRectangle.xMin - canvas.xMin) / canvas.width);
            float xMax = Mathf.Clamp01((pixelRectangle.xMax - canvas.xMin) / canvas.width);
            float y = Mathf.Clamp01(1f - (pixelRectangle.yMax - canvas.yMin) / canvas.height);
            float yMax = Mathf.Clamp01(1f - (pixelRectangle.yMin - canvas.yMin) / canvas.height);
            return Rect.MinMaxRect(x, y, xMax, yMax);
        }

        private static Rect UvToCanvas(Rect uvRectangle, Rect canvas)
        {
            return new Rect(
                canvas.x + uvRectangle.x * canvas.width,
                canvas.y + (1f - uvRectangle.y - uvRectangle.height) * canvas.height,
                uvRectangle.width * canvas.width,
                uvRectangle.height * canvas.height);
        }

        private static Rect FitAspect(Rect container, float aspect)
        {
            if (container.width <= 0f || container.height <= 0f) return container;
            aspect = Mathf.Max(0.01f, aspect);
            float containerAspect = container.width / container.height;
            if (containerAspect > aspect)
            {
                float width = container.height * aspect;
                return new Rect(container.center.x - width * 0.5f, container.y, width, container.height);
            }
            float height = container.width / aspect;
            return new Rect(container.x, container.center.y - height * 0.5f, container.width, height);
        }

        private static Rect MakeRect(Vector2 first, Vector2 second)
        {
            return Rect.MinMaxRect(Mathf.Min(first.x, second.x), Mathf.Min(first.y, second.y),
                Mathf.Max(first.x, second.x), Mathf.Max(first.y, second.y));
        }

        private static Vector2 ClampToRect(Vector2 point, Rect rectangle)
        {
            return new Vector2(Mathf.Clamp(point.x, rectangle.xMin, rectangle.xMax),
                Mathf.Clamp(point.y, rectangle.yMin, rectangle.yMax));
        }

        private static bool NearlyEqual(Rect first, Rect second)
        {
            return Mathf.Abs(first.x - second.x) <= DuplicateTolerance &&
                   Mathf.Abs(first.y - second.y) <= DuplicateTolerance &&
                   Mathf.Abs(first.width - second.width) <= DuplicateTolerance &&
                   Mathf.Abs(first.height - second.height) <= DuplicateTolerance;
        }

        private static Color RegionColor(int index, bool selected)
        {
            if (selected) return new Color(0.05f, 0.95f, 1f, 1f);
            Color color = Color.HSVToRGB(Mathf.Repeat(index * 0.61803398875f, 1f), 0.72f, 1f);
            color.a = 1f;
            return color;
        }

        private static void DrawOutline(Rect rectangle, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.y, rectangle.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.yMax - thickness, rectangle.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rectangle.x, rectangle.y, thickness, rectangle.height), color);
            EditorGUI.DrawRect(new Rect(rectangle.xMax - thickness, rectangle.y, thickness, rectangle.height), color);
        }

        private static void DrawCheckerboard(Rect rectangle)
        {
            const float size = 16f;
            Color first = new Color(0.19f, 0.19f, 0.19f, 1f);
            Color second = new Color(0.25f, 0.25f, 0.25f, 1f);
            int columns = Mathf.CeilToInt(rectangle.width / size);
            int rows = Mathf.CeilToInt(rectangle.height / size);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    Rect tile = new Rect(rectangle.x + column * size, rectangle.y + row * size,
                        Mathf.Min(size, rectangle.xMax - (rectangle.x + column * size)),
                        Mathf.Min(size, rectangle.yMax - (rectangle.y + row * size)));
                    EditorGUI.DrawRect(tile, ((row + column) & 1) == 0 ? first : second);
                }
            }
        }

        private static GUIStyle CenteredLabel()
        {
            return new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f, 1f) }
            };
        }

        private static GUIStyle WhiteMiniBoldLabel()
        {
            return new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 0, 0),
                normal = { textColor = Color.white }
            };
        }

        private static string[] ParseTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags)) return Array.Empty<string>();
            string[] parts = tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> parsed = new List<string>(parts.Length);
            for (int index = 0; index < parts.Length; index++)
            {
                string tag = parts[index].Trim();
                if (!string.IsNullOrEmpty(tag) && !parsed.Contains(tag)) parsed.Add(tag);
            }
            return parsed.ToArray();
        }
    }
}
