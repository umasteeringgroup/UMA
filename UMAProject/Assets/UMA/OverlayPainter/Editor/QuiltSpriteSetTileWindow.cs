using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal sealed class QuiltSpriteSetTileWindow : EditorWindow
    {
        private OverlayPainterSpriteSet source;
        private List<Sprite> sprites;
        private HashSet<int> enabled;
        private Vector2 scroll;
        private int[] result;

        internal static int[] Select(OverlayPainterSpriteSet source, TexturePaintPluginParameterValue value)
        {
            var sprites = TexturePaintSpriteSetSource.Resolve(source)[TexturePaintChannel.Albedo];
            var window = CreateInstance<QuiltSpriteSetTileWindow>();
            window.source = source;
            window.sprites = sprites;
            window.enabled = new HashSet<int>();
            // Also lets users repair a selection after the sheets have been re-sliced.
            for (int i = 0; i < sprites.Count; i++)
                if (!value.spriteSetSelectionExplicit || value.enabledSpriteIndices?.Contains(i) == true)
                    window.enabled.Add(i);
            window.titleContent = new GUIContent("Quilt Fabric Tiles");
            window.minSize = new Vector2(380, 300);
            window.position = new Rect(200, 150, 640, 540);
            window.ShowModal();
            return window.result;
        }

        internal static int[] SortedSelection(HashSet<int> selection)
        {
            var result = new int[selection.Count];
            selection.CopyTo(result);
            System.Array.Sort(result);
            return result;
        }

        private void OnGUI()
        {
            if (sprites == null || source == null) { Close(); return; }
            EditorGUILayout.LabelField(source.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Click tiles to enable or disable them. Each quilt panel uses one enabled tile across all channels.", EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", GUILayout.Width(65)))
                    for (int i = 0; i < sprites.Count; i++) enabled.Add(i);
                if (GUILayout.Button("None", GUILayout.Width(65))) enabled.Clear();
                GUILayout.FlexibleSpace();
                GUILayout.Label(enabled.Count + " / " + sprites.Count + " enabled");
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 28) / 112));
            for (int row = 0; row < sprites.Count; row += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                    for (int i = row; i < Mathf.Min(row + columns, sprites.Count); i++)
                    {
                        Rect cell = GUILayoutUtility.GetRect(108, 132, GUILayout.Width(108), GUILayout.Height(132));
                        bool selected = enabled.Contains(i);
                        EditorGUI.DrawRect(cell, selected ? new Color(.15f, .4f, .55f) : new Color(.18f, .18f, .18f));
                        var texture = TexturePaintSpriteSource.GetTexture(sprites[i]);
                        Rect preview = new Rect(cell.x + 4, cell.y + 4, 100, 100);
                        if (texture != null) GUI.DrawTexture(preview, texture, ScaleMode.ScaleToFit);
                        GUI.Label(new Rect(cell.x + 3, cell.y + 106, 102, 22),
                            new GUIContent((selected ? "✓ " : "○ ") + (i + 1) + ": " + source.GetSpriteName(i, sprites[i].name), sprites[i].name),
                            EditorStyles.miniLabel);
                        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && cell.Contains(Event.current.mousePosition))
                        {
                            if (!enabled.Add(i)) enabled.Remove(i);
                            Event.current.Use(); Repaint();
                        }
                    }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
                using (new EditorGUI.DisabledScope(enabled.Count == 0))
                    if (GUILayout.Button("Apply", GUILayout.Width(90)))
                    { result = SortedSelection(enabled); Close(); }
            }
        }
    }
}
