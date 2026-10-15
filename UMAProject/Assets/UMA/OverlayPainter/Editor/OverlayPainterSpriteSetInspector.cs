using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    [CustomEditor(typeof(OverlayPainterSpriteSet))]
    public sealed class OverlayPainterSpriteSetInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new PropertyField(serializedObject.FindProperty("setName")));
            root.Add(new Label("Categories (drop creates this layer type)"));
            var set = (OverlayPainterSpriteSet)target;
            foreach (string category in OverlayPainterSpriteSet.UsageTypes)
            {
                var toggle = new Toggle(category) { value = set.HasCategory(category) };
                toggle.RegisterValueChangedCallback(e =>
                {
                    Undo.RecordObject(set, "Change Sprite Set Categories");
                    var categories = OverlayPainterSpriteSet.UsageTypes.Where(c => c == category ? e.newValue : set.HasCategory(c)).ToArray();
                    if (categories.Length == 0) { toggle.SetValueWithoutNotify(true); return; }
                    set.categories = categories;
                    EditorUtility.SetDirty(set);
                });
                root.Add(toggle);
            }
            root.Add(new PropertyField(serializedObject.FindProperty("spriteSheets")));
            root.Add(new PropertyField(serializedObject.FindProperty("spriteNames")));
            root.Add(new Button(() => SpriteMaterialThumbnails.Enqueue(set, true)) { text = "Rebuild Sphere and Cube Thumbnails" });
            root.Add(new Button(TexturePaintSpriteMaterialsWindow.ShowWindow) { text = "Open Material Browser" });
            return root;
        }
    }
}
