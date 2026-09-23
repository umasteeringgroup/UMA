using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.Dynamics.Editors
{
    [CustomEditor(typeof(UMAPhysicsAvatar))]
    public class UMAPhysicsAvatarEditor : Editor
    {
        private readonly UMAInspectorView inspectorView =
            new UMAInspectorView(typeof(UMAPhysicsAvatar));
        private SerializedProperty ragdollLayer;
        private SerializedProperty playerLayer;
        private SerializedProperty onRagdollStarted;
        private SerializedProperty onRagdollEnded;

        private void OnEnable()
        {
            ragdollLayer = serializedObject.FindProperty("ragdollLayer");
            playerLayer = serializedObject.FindProperty("playerLayer");
            onRagdollStarted = serializedObject.FindProperty(
                "onRagdollStarted");
            onRagdollEnded = serializedObject.FindProperty("onRagdollEnded");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool advanced = inspectorView.DrawSelector();
            UMAPhysicsAvatar avatar = (UMAPhysicsAvatar)target;

            using (inspectorView.Section("Ragdoll state",
                "Ragdolled switches the generated body between animated and physics-driven states. Blend Amount is experimental partial blending. Follow Hips moves the avatar root to the hips when leaving ragdoll, while Update Renderer Bounds prevents a displaced ragdoll from being culled."))
            {
                EditorGUI.BeginChangeCheck();
                bool ragdolled = EditorGUILayout.Toggle("Ragdolled",
                    avatar.ragdolled);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(avatar, "Toggle UMA ragdoll");
                    avatar.ragdolled = ragdolled;
                }
                inspectorView.Field(serializedObject,
                    "UpdateTransformAfterRagdoll", "Follow Hips On Recovery");
                inspectorView.Field(serializedObject,
                    "UpdateRendererBoundsWhileRagdolled",
                    "Update Renderer Bounds");
                if (advanced)
                    inspectorView.Field(serializedObject,
                        "ragdollBlendAmount", "Physics Blend Amount");
            }

            using (inspectorView.Section("Bones and colliders",
                "Physics Elements define participating bones, rigidbody masses, colliders and joint limits. Simple Player Collider uses the normal capsule/rigidbody setup; Collider Triggers makes the non-ragdolled body collider operate as a trigger."))
            {
                inspectorView.Field(serializedObject, "elements",
                    "Physics Elements");
                inspectorView.Field(serializedObject, "simplePlayerCollider",
                    "Simple Player Collider");
                inspectorView.Field(serializedObject,
                    "enableColliderTriggers", "Collider Triggers");
                SerializedProperty elements = inspectorView.Property(
                    serializedObject, "elements");
                if (elements != null && elements.arraySize == 0)
                    EditorGUILayout.HelpBox(
                        "No Physics Elements are assigned; this avatar cannot build a ragdoll.",
                        MessageType.Warning);
            }

            using (inspectorView.Section("Layers and collision",
                "Player Layer is used while animated; Ragdoll Layer is assigned to physics bodies. Configure the Physics collision matrix so ragdoll bodies collide only with intended world and ragdoll layers. Auto Set Layers reads the player layer from the GameObject and the ragdoll layer by name."))
            {
                inspectorView.Field(serializedObject, "AutoSetLayers",
                    "Auto Set Layers");
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
                    AddDefaultLayers(ragdollLayer, playerLayer);
            }

            using (inspectorView.Section("Events",
                "Ragdoll Started fires after physics takes ownership. Ragdoll Ended fires after animation ownership and avatar placement are restored."))
            {
                EditorGUILayout.PropertyField(onRagdollStarted);
                EditorGUILayout.PropertyField(onRagdollEnded);
            }
            serializedObject.ApplyModifiedProperties();
        }

        public static void AddDefaultLayers(SerializedProperty ragdoll,
            SerializedProperty player)
        {
            ragdoll.intValue = UMAUtils.CreateLayer("Ragdoll");
            player.intValue = UMAUtils.CreateLayer("Player");
            for (int i = 8; i < 32; i++)
                if (i != ragdoll.intValue)
                    Physics.IgnoreLayerCollision(ragdoll.intValue, i, true);
        }
    }
}
