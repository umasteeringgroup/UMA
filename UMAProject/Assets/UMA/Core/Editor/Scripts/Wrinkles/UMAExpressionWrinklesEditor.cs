using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAExpressionWrinkles))]
    public class UMAExpressionWrinklesEditor : Editor
    {
        static readonly string[] ZoneLabels =
        {
            "1 Forehead Left", "2 Forehead Right", "3 Glabella", "4 Nose Root", "5 Bunny Lines",
            "6 Crow's Feet Left", "7 Crow's Feet Right", "8 Nasolabial Left", "9 Nasolabial Right",
            "10 Mouth Corner Left", "11 Mouth Corner Right", "12 Lip Lines", "13 Chin", "14 Neck Left", "15 Neck Right",
        };
        static bool showWeights = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wrinkleOverlay"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wrinkleMaterial"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("headSlots"), true);

            var wrinkles = (UMAExpressionWrinkles)target;
            var material = wrinkles.ResolvedMaterial;
            if (wrinkles.ResolvedOverlay == null)
                EditorGUILayout.HelpBox("No wrinkle overlay. Bake the UMA 3 maps (UMA > Skin > Bake Procedural Wrinkle Maps) or assign one.", MessageType.Warning);
            else if (!UMAExpressionWrinkles.HasWrinkleChannels(material))
                EditorGUILayout.HelpBox("The UMA Material '" + (material != null ? material.name : "none") + "' has no wrinkle channels.", MessageType.Warning);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"), new GUIContent("Expression Profile", "Sets the zone weights from the character's facial expressions every frame. Empty: set them by hand or from code."));

            var weights = serializedObject.FindProperty("zoneWeights");
            bool driven = wrinkles.profile != null;
            showWeights = EditorGUILayout.Foldout(showWeights, driven ? "Zone Weights (from the expression profile)" : "Zone Weights (-1 stretched, 1 compressed)", true);
            if (showWeights && weights != null)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(driven))
                {
                    for (int i = 0; i < weights.arraySize && i < ZoneLabels.Length; i++)
                        EditorGUILayout.Slider(weights.GetArrayElementAtIndex(i), -1f, 1f, ZoneLabels[i]);
                }
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();
            if (driven && Application.isPlaying) Repaint();

            using (new EditorGUI.DisabledScope(!wrinkles.isActiveAndEnabled || driven))
            {
                if (GUILayout.Button("Clear Weights"))
                {
                    Undo.RecordObject(wrinkles, "Clear Wrinkle Weights");
                    wrinkles.ClearWeights();
                    EditorUtility.SetDirty(wrinkles);
                }
            }
            if (GUILayout.Button("Rebuild Character")) wrinkles.RebuildCharacter();
        }
    }
}
