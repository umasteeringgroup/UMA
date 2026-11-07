using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace UMA.Editors
{
    // A picker instance belongs to one add field. Browsing never edits the recipe;
    // confirmation is delivered back through that field's normal inspector event.
    internal sealed class IndexedRecipeAssetPicker<T> where T : UnityEngine.Object
    {
        private readonly string command = "UMA.IndexedRecipePicker." + Guid.NewGuid().ToString("N");
        private T pending;
        private PickerDropdown dropdown;

        internal static string LookupName(T asset) => asset is SlotDataAsset slot ? slot.slotName :
            asset is OverlayDataAsset overlay ? overlay.overlayName : asset.name;

        internal static T[] GetChoices(UMAAssetIndexer index)
        {
            if (index == null) return Array.Empty<T>();
            return index.GetAssetItems<T>().Select(item => item.Item as T)
                .Where(asset => asset != null && !string.IsNullOrEmpty(LookupName(asset)) &&
                    index.GetAssetItem<T>(LookupName(asset))?.Item == asset)
                .Distinct().OrderBy(LookupName, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal T Draw(string label)
        {
            var rect = EditorGUILayout.GetControlRect();
            rect = EditorGUI.PrefixLabel(rect, new GUIContent(label));
            var dropRect = new Rect(rect.x, rect.y, Mathf.Min(64f, rect.width * 0.3f), rect.height);
            rect.xMin = dropRect.xMax + 4f;
            GUI.Box(dropRect, new GUIContent("drop here", "Drop a " + typeof(T).Name + " here."), EditorStyles.helpBox);
            bool changed = GUI.changed;
            if (EditorGUI.DropdownButton(rect, new GUIContent("Select indexed asset…",
                "Search assets registered in the UMA Global Index."), FocusType.Keyboard)) Open(rect);
            GUI.changed = changed;
            var currentEvent = Event.current;
            if (GUI.enabled && dropRect.Contains(currentEvent.mousePosition) &&
                (currentEvent.type == EventType.DragUpdated || currentEvent.type == EventType.DragPerform))
            {
                var dropped = DragAndDrop.objectReferences.OfType<T>().FirstOrDefault(asset => asset != null);
                DragAndDrop.visualMode = dropped != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                bool accept = dropped != null && currentEvent.type == EventType.DragPerform;
                if (accept) DragAndDrop.AcceptDrag();
                currentEvent.Use();
                if (accept)
                {
                    GUI.changed = true;
                    return dropped;
                }
            }
            return TakeSelection();
        }

        internal void Open(Rect rect)
        {
            var owner = EditorWindow.focusedWindow;
            pending = null;
            dropdown = new PickerDropdown(GetChoices(UMAAssetIndexer.Instance), asset =>
            {
                pending = asset;
                EditorApplication.delayCall += () =>
                {
                    if (owner != null) owner.SendEvent(EditorGUIUtility.CommandEvent(command));
                };
            });
            dropdown.Show(rect);
        }

        internal T TakeSelection()
        {
            if (Event.current.type != EventType.ExecuteCommand || Event.current.commandName != command) return null;
            var asset = pending;
            pending = null;
            Event.current.Use();
            // The index may have changed while the popup was open.
            if (asset == null || UMAAssetIndexer.Instance.GetAssetItem<T>(LookupName(asset))?.Item != asset) return null;
            GUI.changed = true;
            return asset;
        }

        private sealed class AssetItem : AdvancedDropdownItem
        {
            internal readonly T Asset;
            internal AssetItem(T asset) : base(LookupName(asset) == asset.name ? LookupName(asset) :
                LookupName(asset) + " (" + asset.name + ")")
            {
                Asset = asset;
                icon = AssetPreview.GetMiniThumbnail(asset);
            }
        }

        private sealed class PickerDropdown : AdvancedDropdown
        {
            // AdvancedDropdown sizes its window from the contents. Its internal
            // maximum still does not constrain long item names in Unity 6.3.
            private static readonly FieldInfo WindowField = typeof(AdvancedDropdown)
                .GetField("m_WindowInstance", BindingFlags.Instance | BindingFlags.NonPublic);
            private readonly T[] assets;
            private readonly Action<T> select;
            internal PickerDropdown(T[] assets, Action<T> select) : base(new AdvancedDropdownState())
            {
                this.assets = assets;
                this.select = select;
                minimumSize = new Vector2(336, 288);
            }
            public new void Show(Rect rect)
            {
                base.Show(rect);
                var window = (EditorWindow)WindowField.GetValue(this);
                // Apply fixed bounds through EditorWindow so both axes are capped,
                // retaining Unity's search, keyboard navigation and scrolling.
                window.minSize = minimumSize;
                window.maxSize = minimumSize;
                window.ShowAsDropDown(GUIUtility.GUIToScreenRect(rect), minimumSize);
            }
            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(typeof(T) == typeof(SlotDataAsset) ? "Indexed slots" : "Indexed overlays");
                foreach (var asset in assets) root.AddChild(new AssetItem(asset));
                if (assets.Length == 0) root.AddChild(new AdvancedDropdownItem("No indexed assets available") { enabled = false });
                return root;
            }
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is AssetItem selected && selected.Asset != null) select(selected.Asset);
            }
        }
    }
}
