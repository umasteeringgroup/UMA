using UMA.Editors;
using UnityEditor;

namespace UMA.Dynamics.Editors
{
    [CustomEditor(typeof(UMAPhysicsElement))]
    public class UMAPhysicsElementEditor : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMAPhysicsElement));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool advanced = inspectorView.DrawSelector();
            using (inspectorView.Section("Bone and body",
                "Bone Name identifies the skeleton transform receiving this rigidbody. Mark exactly the root hips/pelvis element as Root. Mass is measured in kilograms and should remain proportionate across the complete ragdoll."))
            {
                inspectorView.Field(serializedObject, "isRoot", "Root Element");
                inspectorView.Field(serializedObject, "boneName", "Bone Name");
                inspectorView.Field(serializedObject, "mass", "Mass");
                SerializedProperty bone = inspectorView.Property(
                    serializedObject, "boneName");
                SerializedProperty mass = inspectorView.Property(
                    serializedObject, "mass");
                if (bone != null && string.IsNullOrWhiteSpace(
                    bone.stringValue))
                    EditorGUILayout.HelpBox("Bone Name is required.",
                        MessageType.Warning);
                if (mass != null && mass.floatValue <= 0f)
                    EditorGUILayout.HelpBox(
                        "Mass should be greater than zero.",
                        MessageType.Warning);
            }
            using (inspectorView.Section("Colliders",
                "Colliders approximate the body volume attached to this bone. Capsule and sphere colliders can also participate in supported cloth collision; box colliders cannot affect Unity cloth."))
                DrawColliders(serializedObject.FindProperty("colliders"));
            using (inspectorView.Section("Joint limits",
                "Parent Bone creates the connected joint. Axis and Swing Axis establish the joint frame; twist and swing limits constrain motion in degrees. Enable Preprocessing can stabilize difficult joints, but disabling it may behave better for intentionally extreme ragdolls."))
            {
                inspectorView.Field(serializedObject, "parentBone",
                    "Parent Bone");
                if (advanced)
                {
                    inspectorView.Field(serializedObject, "axis", "Twist Axis");
                    inspectorView.Field(serializedObject, "swingAxis",
                        "Swing Axis");
                }
                inspectorView.Field(serializedObject, "lowTwistLimit",
                    "Low Twist Limit");
                inspectorView.Field(serializedObject, "highTwistLimit",
                    "High Twist Limit");
                inspectorView.Field(serializedObject, "swing1Limit",
                    "Swing 1 Limit");
                inspectorView.Field(serializedObject, "swing2Limit",
                    "Swing 2 Limit");
                if (advanced)
                    inspectorView.Field(serializedObject,
                        "enablePreprocessing", "Enable Preprocessing");
            }
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawColliders(SerializedProperty list)
        {
            EditorGUILayout.PropertyField(list, true);
            if (!list.isExpanded) return;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty collider = list.GetArrayElementAtIndex(i);
                SerializedProperty type = collider.FindPropertyRelative(
                    "colliderType");
                if (collider.isExpanded && type != null &&
                    type.enumValueIndex == 0)
                    EditorGUILayout.HelpBox(
                        "Box colliders cannot be used to affect cloth.",
                        MessageType.Warning);
            }
        }
    }
}
