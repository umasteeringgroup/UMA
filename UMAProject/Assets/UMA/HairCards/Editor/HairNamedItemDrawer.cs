using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    // Unity otherwise labels list entries using their first string field: the stable ID.
    // Keep serialization untouched; display the authored name and protect the identity.
    [CustomPropertyDrawer(typeof(HairGroup))]
    [CustomPropertyDrawer(typeof(HairHelper))]
    [CustomPropertyDrawer(typeof(HairLodSettings))]
    [CustomPropertyDrawer(typeof(HairGuide))]
    [CustomPropertyDrawer(typeof(HairGrowthMap))]
    [CustomPropertyDrawer(typeof(HairSculptLayer))]
    [CustomPropertyDrawer(typeof(HairModifierSettings))]
    [CustomPropertyDrawer(typeof(HairConstraintSettings))]
    [CustomPropertyDrawer(typeof(HairGenerationStage))]
    internal sealed class HairNamedItemDrawer : PropertyDrawer
    {
        internal static string DisplayName(SerializedProperty property)
        {
            string name = property.FindPropertyRelative("name")?.stringValue;
            return string.IsNullOrWhiteSpace(name) ? "Unnamed " + ObjectNames.NicifyVariableName(property.type.Replace("Hair", "")) : name;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;
            var child = property.Copy(); var end = property.GetEndProperty();
            if (child.NextVisible(true)) do
            {
                if (SerializedProperty.EqualContents(child, end)) break;
                height += EditorGUIUtility.standardVerticalSpacing + (child.name == "id" ? EditorGUIUtility.singleLineHeight : EditorGUI.GetPropertyHeight(child, true));
            } while (child.NextVisible(false));
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, new GUIContent(DisplayName(property), "Expand to edit this named item. Stable identifiers are not display names."), true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var child = property.Copy(); var end = property.GetEndProperty();
                if (child.NextVisible(true)) do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    row.y += row.height + EditorGUIUtility.standardVerticalSpacing;
                    row.height = child.name == "id" ? EditorGUIUtility.singleLineHeight : EditorGUI.GetPropertyHeight(child, true);
                    if (child.name == "id")
                    {
                        // A compact, read-only value avoids accidental breakage of references.
                        using (new EditorGUI.DisabledScope(true)) EditorGUI.PropertyField(row, child, new GUIContent("Internal ID", "Stable reference used by maps, modifiers and helpers. Not a name."));
                    }
                    else EditorGUI.PropertyField(row, child, true);
                } while (child.NextVisible(false));
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
