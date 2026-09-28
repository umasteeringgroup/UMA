using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.Dynamics.Editors
{
    [CustomEditor(typeof(UMAPhysicsSlotDefinition))]
    public class UMAPhysicsSlotEditor : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMAPhysicsSlotDefinition));
        private SerializedProperty ragdollLayer;
        private SerializedProperty playerLayer;

        private void OnEnable()
        {
            ragdollLayer = serializedObject.FindProperty("ragdollLayer");
            playerLayer = serializedObject.FindProperty("playerLayer");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool advanced = inspectorView.DrawSelector();
            using (inspectorView.Section("Collider behavior",
                "These values are copied to the generated Physics Avatar when the slot becomes available. Simple Player Collider uses the standard capsule/rigidbody setup. Collider Triggers makes the animated body collider a trigger. Follow Hips moves the avatar root after recovering from ragdoll."))
            {
                inspectorView.Field(serializedObject, "simplePlayerCollider",
                    "Simple Player Collider");
                inspectorView.Field(serializedObject,
                    "enableColliderTriggers", "Collider Triggers");
                inspectorView.Field(serializedObject,
                    "UpdateTransformAfterRagdoll", "Follow Hips On Recovery");
            }
            using (inspectorView.Section("Physics elements",
                "Physics Elements define the bones, masses, colliders and joint constraints installed by this slot. Keep the root element and its descendants consistent with the target race's skeleton names."))
            {
                inspectorView.Field(serializedObject, "PhysicsElements",
                    "Physics Elements");
                SerializedProperty elements = inspectorView.Property(
                    serializedObject, "PhysicsElements");
                if (elements != null && elements.arraySize == 0)
                    EditorGUILayout.HelpBox(
                        "No Physics Elements are assigned.",
                        MessageType.Warning);
            }
            using (inspectorView.Section("Layers and collision",
                "Player and Ragdoll layers are copied to the generated Physics Avatar. Configure their Physics collision matrix explicitly; the default-layer utility is provided only for projects using UMA's conventional layer names."))
            {
                ragdollLayer.intValue = EditorGUILayout.LayerField(
                    "Ragdoll Layer", ragdollLayer.intValue);
                playerLayer.intValue = EditorGUILayout.LayerField(
                    "Player Layer", playerLayer.intValue);
                if (ragdollLayer.intValue == playerLayer.intValue)
                    EditorGUILayout.HelpBox(
                        "Player and Ragdoll layers should be different.",
                        MessageType.Warning);
                if (advanced && GUILayout.Button(
                    "Create Default Ragdoll / Player Layers"))
                    UMAPhysicsAvatarEditor.AddDefaultLayers(
                        ragdollLayer, playerLayer);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}
