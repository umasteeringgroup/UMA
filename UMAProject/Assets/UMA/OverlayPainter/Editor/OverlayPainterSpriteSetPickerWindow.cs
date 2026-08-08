using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal static class OverlayPainterSpriteSetEditorUtility
    {
        public static List<OverlayPainterSpriteSet> FindAllSpriteSets()
        {
            string[] guids = AssetDatabase.FindAssets("t:OverlayPainterSpriteSet");
            var result = new List<OverlayPainterSpriteSet>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                OverlayPainterSpriteSet spriteSet = AssetDatabase.LoadAssetAtPath<OverlayPainterSpriteSet>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (spriteSet != null) result.Add(spriteSet);
            }
            result.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName,
                StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static List<Sprite> GetOrderedSprites(Texture2D spriteSheet)
        {
            var result = new List<Sprite>();
            if (spriteSheet == null) return result;
            string path = AssetDatabase.GetAssetPath(spriteSheet);
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
                if (assets[i] is Sprite sprite) result.Add(sprite);
            result.Sort(CompareSprites);
            return result;
        }

        public static int GetCommonSpriteCount(OverlayPainterSpriteSet spriteSet)
        {
            if (spriteSet?.spriteSheets == null || spriteSet.spriteSheets.Count == 0) return 0;
            int count = int.MaxValue;
            for (int i = 0; i < spriteSet.spriteSheets.Count; i++)
            {
                OverlayPainterSpriteSheet sheet = spriteSet.spriteSheets[i];
                if (sheet?.spriteSheet == null) return 0;
                count = Mathf.Min(count, GetOrderedSprites(sheet.spriteSheet).Count);
            }
            return count == int.MaxValue ? 0 : count;
        }

        public static bool TryGetSprite(OverlayPainterSpriteSheet sheet, int spriteIndex,
            out Sprite sprite)
        {
            List<Sprite> sprites = GetOrderedSprites(sheet?.spriteSheet);
            sprite = (uint)spriteIndex < (uint)sprites.Count ? sprites[spriteIndex] : null;
            return sprite != null;
        }

        internal static int ParseTrailingIndex(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return -1;
            int separator = spriteName.LastIndexOf('_');
            return separator >= 0 && separator + 1 < spriteName.Length &&
                int.TryParse(spriteName.Substring(separator + 1), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int index)
                    ? index
                    : -1;
        }

        private static int CompareSprites(Sprite left, Sprite right)
        {
            int leftIndex = ParseTrailingIndex(left?.name);
            int rightIndex = ParseTrailingIndex(right?.name);
            if (leftIndex >= 0 && rightIndex >= 0 && leftIndex != rightIndex)
                return leftIndex.CompareTo(rightIndex);
            if ((leftIndex >= 0) != (rightIndex >= 0)) return leftIndex >= 0 ? -1 : 1;
            if (left != null && right != null)
            {
                int row = right.rect.y.CompareTo(left.rect.y);
                if (row != 0) return row;
                int column = left.rect.x.CompareTo(right.rect.x);
                if (column != 0) return column;
            }
            return string.Compare(left?.name, right?.name, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class OverlayPainterSpriteSetPickerWindow : EditorWindow
    {
        private const float SetColumnWidth = 190f;
        private const float RowHeight = 58f;

        private readonly List<OverlayPainterSpriteSet> spriteSets = new List<OverlayPainterSpriteSet>();
        private readonly List<Sprite> previewSprites = new List<Sprite>();
        private Action<OverlayPainterSpriteSet, int, Vector2> onAdd;
        private Vector2 setScroll;
        private Vector2 spriteScroll;
        private Vector2 initialTiling = Vector2.one;
        private int selectedSetIndex;
        private int selectedSpriteIndex = -1;

        public static void Show(Action<OverlayPainterSpriteSet, int, Vector2> addCallback)
        {
            var window = CreateInstance<OverlayPainterSpriteSetPickerWindow>();
            window.titleContent = new GUIContent("Add from Sprite Set");
            window.minSize = new Vector2(620f, 390f);
            window.onAdd = addCallback;
            window.RefreshAssets();
            window.ShowUtility();
        }

        private void OnFocus()
        {
            if (spriteSets.Count == 0) RefreshAssets();
        }

        private void RefreshAssets()
        {
            OverlayPainterSpriteSet previous = SelectedSet;
            spriteSets.Clear();
            spriteSets.AddRange(OverlayPainterSpriteSetEditorUtility.FindAllSpriteSets());
            selectedSetIndex = previous == null ? Mathf.Clamp(selectedSetIndex, 0,
                Mathf.Max(0, spriteSets.Count - 1)) : Mathf.Max(0, spriteSets.IndexOf(previous));
            RefreshSprites();
            Repaint();
        }

        private OverlayPainterSpriteSet SelectedSet =>
            (uint)selectedSetIndex < (uint)spriteSets.Count ? spriteSets[selectedSetIndex] : null;

        private void RefreshSprites()
        {
            previewSprites.Clear();
            OverlayPainterSpriteSet spriteSet = SelectedSet;
            if (spriteSet?.spriteSheets != null && spriteSet.spriteSheets.Count > 0)
                previewSprites.AddRange(OverlayPainterSpriteSetEditorUtility.GetOrderedSprites(
                    spriteSet.spriteSheets[0]?.spriteSheet));
            int commonCount = OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(spriteSet);
            if (previewSprites.Count > commonCount)
                previewSprites.RemoveRange(commonCount, previewSprites.Count - commonCount);
            if (previewSprites.Count == 0) selectedSpriteIndex = -1;
            else if (selectedSpriteIndex >= previewSprites.Count) selectedSpriteIndex = 0;
        }

        private void OnGUI()
        {
            const float padding = 6f;
            const float introductionHeight = 38f;
            const float footerHeight = 30f;
            Rect introduction = new Rect(padding, padding,
                Mathf.Max(0f, position.width - padding * 2f), introductionHeight);
            GUI.Label(introduction, "Choose one material tile. Its configured channel sprites will be " +
                "assigned to the selected Paint, Fill, or Path layer.", EditorStyles.wordWrappedLabel);

            float columnsTop = introduction.yMax + padding;
            float footerTop = Mathf.Max(columnsTop, position.height - footerHeight - padding);
            Rect columns = new Rect(padding, columnsTop,
                Mathf.Max(0f, position.width - padding * 2f),
                Mathf.Max(0f, footerTop - columnsTop - padding));
            Rect setsRect = new Rect(columns.x, columns.y, SetColumnWidth, columns.height);
            Rect spritesRect = new Rect(setsRect.xMax + 6f, columns.y,
                Mathf.Max(0f, columns.width - SetColumnWidth - 6f), columns.height);
            DrawSets(setsRect);
            DrawSprites(spritesRect);

            Rect footer = new Rect(padding, footerTop,
                Mathf.Max(0f, position.width - padding * 2f), footerHeight);
            DrawFooter(footer);

            Event current = Event.current;
            if (current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) &&
                CanAddSelectedSprite)
            {
                AddSelectedSprite();
                current.Use();
            }
        }

        private void DrawSets(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect header = new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, 20f);
            GUI.Label(header, "Sprite Sets", EditorStyles.boldLabel);
            Rect view = new Rect(rect.x + 4f, header.yMax + 2f, rect.width - 8f,
                rect.height - header.height - 12f);
            float height = Mathf.Max(view.height, spriteSets.Count * 24f);
            setScroll = GUI.BeginScrollView(view, setScroll, new Rect(0f, 0f,
                Mathf.Max(0f, view.width - 16f), height));
            for (int i = 0; i < spriteSets.Count; i++)
            {
                Rect row = new Rect(0f, i * 24f, Mathf.Max(0f, view.width - 18f), 22f);
                if (GUI.Toggle(row, i == selectedSetIndex, spriteSets[i].DisplayName,
                    EditorStyles.miniButton) && i != selectedSetIndex)
                {
                    selectedSetIndex = i;
                    selectedSpriteIndex = -1;
                    RefreshSprites();
                }
            }
            GUI.EndScrollView();
            if (spriteSets.Count == 0)
                GUI.Label(view, "No OverlayPainterSpriteSet assets found.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawSprites(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            Rect header = new Rect(rect.x + 6f, rect.y + 5f, rect.width - 12f, 20f);
            GUI.Label(header, SelectedSet == null ? "Sprites" : SelectedSet.DisplayName + " Sprites",
                EditorStyles.boldLabel);
            Rect view = new Rect(rect.x + 4f, header.yMax + 2f, rect.width - 8f,
                rect.height - header.height - 12f);
            float height = Mathf.Max(view.height, previewSprites.Count * RowHeight);
            spriteScroll = GUI.BeginScrollView(view, spriteScroll, new Rect(0f, 0f,
                Mathf.Max(0f, view.width - 16f), height));
            for (int i = 0; i < previewSprites.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, Mathf.Max(0f, view.width - 18f),
                    RowHeight - 2f);
                bool selected = i == selectedSpriteIndex;
                GUIStyle rowStyle = selected ? GUI.skin.FindStyle("SelectionRect") : EditorStyles.helpBox;
                GUI.Box(row, GUIContent.none, rowStyle ?? EditorStyles.helpBox);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    selectedSpriteIndex = i;
                    GUI.FocusControl(null);
                    Repaint();
                }
                Sprite sprite = previewSprites[i];
                Rect thumbnail = new Rect(row.x + 4f, row.y + 4f, 48f, 48f);
                Texture2D preview = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
                if (preview != null) GUI.DrawTexture(thumbnail, preview, ScaleMode.ScaleToFit, true);
                string label = SelectedSet.GetSpriteName(i, sprite.name);
                GUI.Label(new Rect(thumbnail.xMax + 8f, row.y + 7f,
                    row.width - thumbnail.width - 16f, 20f), label);
                GUI.Label(new Rect(thumbnail.xMax + 8f, row.y + 28f,
                    row.width - thumbnail.width - 16f, 18f), $"Sprite {i + 1}",
                    EditorStyles.miniLabel);
            }
            GUI.EndScrollView();
            if (SelectedSet != null && previewSprites.Count == 0)
                GUI.Label(view, "Every configured sheet must contain the selected sprite index.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        private void DrawFooter(Rect rect)
        {
            const float buttonWidth = 88f;
            const float gap = 6f;
            Rect refresh = new Rect(rect.x, rect.y, buttonWidth, EditorGUIUtility.singleLineHeight + 4f);
            Rect add = new Rect(rect.xMax - buttonWidth, rect.y, buttonWidth,
                EditorGUIUtility.singleLineHeight + 4f);
            Rect cancel = new Rect(add.x - gap - buttonWidth, rect.y, buttonWidth,
                EditorGUIUtility.singleLineHeight + 4f);
            float tileFieldsWidth = Mathf.Max(0f, cancel.x - refresh.xMax - gap * 3f);
            float tileFieldWidth = Mathf.Max(0f, (tileFieldsWidth - gap) * 0.5f);
            Rect initialX = new Rect(refresh.xMax + gap * 2f, rect.y, tileFieldWidth,
                EditorGUIUtility.singleLineHeight);
            Rect initialY = new Rect(initialX.xMax + gap, rect.y, tileFieldWidth,
                EditorGUIUtility.singleLineHeight);
            if (GUI.Button(refresh, "Refresh")) RefreshAssets();
            initialTiling.x = Mathf.Clamp(EditorGUI.FloatField(initialX,
                new GUIContent("Initial X Tile", "Initial horizontal tiling for generated Fill channels."),
                initialTiling.x), 0.01f, 1000f);
            initialTiling.y = Mathf.Clamp(EditorGUI.FloatField(initialY,
                new GUIContent("Initial Y Tile", "Initial vertical tiling for generated Fill channels."),
                initialTiling.y), 0.01f, 1000f);
            if (GUI.Button(cancel, "Cancel")) Close();
            using (new EditorGUI.DisabledScope(!CanAddSelectedSprite))
            {
                if (GUI.Button(add, "Add")) AddSelectedSprite();
            }
        }

        private bool CanAddSelectedSprite => SelectedSet != null &&
            (uint)selectedSpriteIndex < (uint)previewSprites.Count;

        private void AddSelectedSprite()
        {
            if (!CanAddSelectedSprite) return;
            Action<OverlayPainterSpriteSet, int, Vector2> callback = onAdd;
            OverlayPainterSpriteSet spriteSet = SelectedSet;
            int spriteIndex = selectedSpriteIndex;
            Vector2 tiling = initialTiling;
            Close();
            callback?.Invoke(spriteSet, spriteIndex, tiling);
        }
    }
}
