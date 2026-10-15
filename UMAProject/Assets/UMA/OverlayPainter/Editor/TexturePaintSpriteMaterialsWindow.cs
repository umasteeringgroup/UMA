using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    public sealed class TexturePaintSpriteMaterialsWindow : EditorWindow
    {
        internal const string DragKey = "UMA.SpriteMaterial";
        internal sealed class DragMaterial
        {
            public OverlayPainterSpriteSet set;
            public int index;
            public string category;
        }

        [SerializeField] private string category = OverlayPainterSpriteSet.FillMaterial;
        [SerializeField] private OverlayPainterSpriteSet selectedSet;
        [SerializeField] private int thumbnailSize = 112;
        private List<OverlayPainterSpriteSet> sets = new();
        private PopupField<string> categoryField, setField;
        private ScrollView scroll;
        private Label status;

        [MenuItem("Window/UMA/Overlay Painter Materials", false, 2024)]
        public static void ShowWindow() => GetWindow<TexturePaintSpriteMaterialsWindow>().Show();

        internal static void CloseOpenWindowsForLayoutChange()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<TexturePaintSpriteMaterialsWindow>()) window.Close();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Overlay Painter Materials");
            minSize = new Vector2(190, 240);
            EditorApplication.projectChanged += RefreshSets;
            SpriteMaterialThumbnails.Changed += RefreshImages;
        }
        private void OnDisable()
        {
            EditorApplication.projectChanged -= RefreshSets;
            SpriteMaterialThumbnails.Changed -= RefreshImages;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            categoryField = new PopupField<string>("Category", OverlayPainterSpriteSet.UsageTypes.ToList(),
                Mathf.Max(0, Array.IndexOf(OverlayPainterSpriteSet.UsageTypes, category)));
            categoryField.RegisterValueChangedCallback(e => { category = e.newValue; RefreshSets(); });
            root.Add(categoryField);
            setField = new PopupField<string>("Sprite Set", new List<string> { "No sets" }, 0);
            setField.RegisterValueChangedCallback(e =>
            {
                int index = setField.index;
                selectedSet = (uint)index < (uint)sets.Count ? sets[index] : null;
                DrawTiles();
            });
            root.Add(setField);
            var size = new SliderInt("Size", 64, 256) { value = thumbnailSize, showInputField = true };
            size.RegisterValueChangedCallback(e => { thumbnailSize = Mathf.Clamp(e.newValue, 64, 256); ResizeTiles(); });
            root.Add(size);
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.contentContainer.style.flexDirection = FlexDirection.Row;
            scroll.contentContainer.style.flexWrap = Wrap.Wrap;
            root.Add(scroll);
            status = new Label("Drag onto the current paint target.");
            status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(status);
            root.Add(new Button(() => { if (selectedSet != null) SpriteMaterialThumbnails.Enqueue(selectedSet, true); })
                { text = "Rebuild Thumbnails", tooltip = "Render this set again using URP Lit and its material channels." });
            RefreshSets();
        }

        private void RefreshSets()
        {
            if (setField == null) return;
            sets = OverlayPainterSpriteSetEditorUtility.FindAllSpriteSets().Where(s => s.HasCategory(category)).ToList();
            if (!sets.Contains(selectedSet)) selectedSet = sets.FirstOrDefault();
            // Duplicate display names remain selectable without losing their identity.
            var labels = sets.Select(s => sets.Count(other => other.DisplayName == s.DisplayName) > 1
                ? s.DisplayName + " — " + AssetDatabase.GetAssetPath(s) : s.DisplayName).ToList();
            setField.choices = labels.Count > 0 ? labels : new List<string> { "No sets in this category" };
            setField.SetValueWithoutNotify(setField.choices[Mathf.Max(0, sets.IndexOf(selectedSet))]);
            setField.SetEnabled(sets.Count > 0);
            DrawTiles();
        }

        private void DrawTiles()
        {
            if (scroll == null) return;
            scroll.Clear();
            int count = OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(selectedSet);
            for (int i = 0; i < count; i++)
            {
                int index = i;
                OverlayPainterSpriteSetEditorUtility.TryGetSprite(selectedSet.spriteSheets[0], i, out var sprite);
                string name = selectedSet.GetSpriteName(i, sprite != null ? sprite.name : null);
                var tile = new Image { image = selectedSet.GetThumbnail(i, category), scaleMode = ScaleMode.ScaleToFit, tooltip = name, userData = i };
                tile.style.marginLeft = tile.style.marginRight = tile.style.marginTop = tile.style.marginBottom = 3;
                tile.AddManipulator(new MaterialDrag(() => new DragMaterial { set = selectedSet, index = index, category = category }, name));
                scroll.Add(tile);
            }
            ResizeTiles();
            status.text = count == 0 ? "No sprites available in this category." : category == OverlayPainterSpriteSet.Path
                ? "Drag onto the current target to start a path. Shift-click to add points."
                : "Drag onto the current paint target to create a " + (category == OverlayPainterSpriteSet.Projection ? "projection." : "fill layer.");
            if (selectedSet != null) SpriteMaterialThumbnails.Enqueue(selectedSet);
        }
        private void ResizeTiles()
        {
            if (scroll == null) return;
            foreach (var tile in scroll.Children()) { tile.style.width = thumbnailSize; tile.style.height = thumbnailSize; }
        }
        private void RefreshImages(OverlayPainterSpriteSet set)
        {
            if (set != selectedSet || scroll == null) return;
            foreach (var tile in scroll.Children().OfType<Image>()) tile.image = set.GetThumbnail((int)tile.userData, category);
        }

        private sealed class MaterialDrag : PointerManipulator
        {
            private readonly Func<DragMaterial> payload;
            private readonly string label;
            private Vector2 start;
            private bool pressed;
            public MaterialDrag(Func<DragMaterial> payload, string label) { this.payload = payload; this.label = label; }
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down);
                target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down);
                target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost);
            }
            private void Down(PointerDownEvent e)
            { if (e.button != 0) return; pressed = true; start = e.position; target.CapturePointer(e.pointerId); e.StopPropagation(); }
            private void Move(PointerMoveEvent e)
            {
                if (!pressed || Vector2.Distance(start, e.position) < 5) return;
                pressed = false;
                target.ReleasePointer(e.pointerId);
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                DragAndDrop.SetGenericData(DragKey, payload());
                DragAndDrop.StartDrag(label);
                e.StopPropagation();
            }
            private void Up(PointerUpEvent e) { pressed = false; target.ReleasePointer(e.pointerId); }
            private void Lost(PointerCaptureOutEvent e) { pressed = false; }
        }
    }
}
