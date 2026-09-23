using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    [CustomEditor(typeof(UMAMaterialAnimator))]
    public class UMAMaterialAnimatorInspector : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMAMaterialAnimator));
        private SerializedProperty slotTag;
        private SerializedProperty animations;

        private void OnEnable()
        {
            slotTag = serializedObject.FindProperty("slotTag");
            animations = serializedObject.FindProperty("animations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool advanced = inspectorView.DrawSelector();
            using (inspectorView.Section("Slot selection",
                "Slot Tag limits animation to generated slots carrying this tag. Keep tags consistent with Slot Data Assets; an empty or unmatched tag produces no material targets."))
                EditorGUILayout.PropertyField(slotTag, new GUIContent("Slot Tag"));

            using (inspectorView.Section("Material animations",
                "Each entry animates one shader property on matching overlays. Overlay Tag can narrow the target further. The curve is sampled over normalized animation time. Float entries interpolate numeric values; Color entries interpolate colors."))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Float Animation"))
                        AddAnimation(
                            UMAMaterialAnimator.MaterialAnimationType.Float);
                    if (GUILayout.Button("Add Color Animation"))
                        AddAnimation(
                            UMAMaterialAnimator.MaterialAnimationType.Color);
                }
                for (int i = 0; i < animations.arraySize; i++)
                    if (DrawAnimation(i, advanced)) break;
            }

            if (advanced)
                using (inspectorView.Section("Advanced targeting",
                    "Use Channel writes through an overlay material channel rather than addressing the property directly. Channel Number is zero-based: zero is the base overlay and subsequent values address added overlay layers. Use this only when the material layout requires channel-specific animation."))
                    EditorGUILayout.HelpBox(
                        "Advanced targeting is configured per animation above.",
                        MessageType.None);

            using (inspectorView.Section("Validation",
                "Every animation needs a shader property name. Empty slot or overlay tags broaden matching; that can be intentional, but verify the animation is not creating unnecessary per-character material instances."))
            {
                int missingProperties = 0;
                for (int i = 0; i < animations.arraySize; i++)
                    if (string.IsNullOrWhiteSpace(animations
                        .GetArrayElementAtIndex(i)
                        .FindPropertyRelative("propertyName").stringValue))
                        missingProperties++;
                EditorGUILayout.LabelField("Animation Count",
                    animations.arraySize.ToString());
                if (missingProperties > 0)
                    EditorGUILayout.HelpBox(missingProperties +
                        " animation(s) have no shader property.",
                        MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void AddAnimation(
            UMAMaterialAnimator.MaterialAnimationType type)
        {
            int index = animations.arraySize++;
            SerializedProperty element = animations.GetArrayElementAtIndex(
                index);
            element.FindPropertyRelative("type").enumValueIndex = (int)type;
            element.FindPropertyRelative("show").boolValue = true;
            element.FindPropertyRelative("overlayTag").stringValue =
                string.Empty;
            element.FindPropertyRelative("propertyName").stringValue =
                string.Empty;
            element.FindPropertyRelative("curve").animationCurveValue =
                AnimationCurve.Linear(0f, 0f, 1f, 1f);
            element.FindPropertyRelative("useChannel").boolValue = false;
            element.FindPropertyRelative("channelNumber").intValue = 0;
            element.FindPropertyRelative("MinFloatValue").floatValue = 0f;
            element.FindPropertyRelative("MaxFloatValue").floatValue = 1f;
            element.FindPropertyRelative("MinColorValue").colorValue =
                Color.black;
            element.FindPropertyRelative("MaxColorValue").colorValue =
                Color.white;
        }

        private bool DrawAnimation(int index, bool advanced)
        {
            SerializedProperty item = animations.GetArrayElementAtIndex(index);
            SerializedProperty expanded = item.FindPropertyRelative("show");
            SerializedProperty type = item.FindPropertyRelative("type");
            string title = type.enumValueIndex ==
                (int)UMAMaterialAnimator.MaterialAnimationType.Color
                    ? "Color Animation " + (index + 1)
                    : "Float Animation " + (index + 1);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    expanded.boolValue = EditorGUILayout.Foldout(
                        expanded.boolValue, title, true);
                    if (GUILayout.Button("Remove", GUILayout.Width(64f)))
                    {
                        animations.DeleteArrayElementAtIndex(index);
                        return true;
                    }
                }
                if (!expanded.boolValue) return false;
                EditorGUILayout.PropertyField(type, new GUIContent("Type"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "overlayTag"), new GUIContent("Overlay Tag"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "propertyName"), new GUIContent("Shader Property"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative(
                    "curve"), new GUIContent("Animation Curve"));

                if (type.enumValueIndex ==
                    (int)UMAMaterialAnimator.MaterialAnimationType.Float)
                {
                    EditorGUILayout.PropertyField(item.FindPropertyRelative(
                        "MinFloatValue"), new GUIContent("Minimum"));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative(
                        "MaxFloatValue"), new GUIContent("Maximum"));
                }
                else
                {
                    EditorGUILayout.PropertyField(item.FindPropertyRelative(
                        "MinColorValue"), new GUIContent("Minimum"));
                    EditorGUILayout.PropertyField(item.FindPropertyRelative(
                        "MaxColorValue"), new GUIContent("Maximum"));
                }

                if (advanced)
                {
                    SerializedProperty useChannel = item.FindPropertyRelative(
                        "useChannel");
                    EditorGUILayout.PropertyField(useChannel,
                        new GUIContent("Use Overlay Channel"));
                    if (useChannel.boolValue)
                        EditorGUILayout.PropertyField(
                            item.FindPropertyRelative("channelNumber"),
                            new GUIContent("Channel Number"));
                }
            }
            return false;
        }
    }
}
