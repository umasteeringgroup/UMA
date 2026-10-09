using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;

namespace UMA.Dismemberment.Editor
{
    internal static class DismembermentPluginActions
    {
        [UMAPlugin(typeof(DynamicCharacterAvatar), "Dismemberment", Order = 120,
            Tooltip = "Add or select this avatar's dismemberment configuration.", ValidateMethod = nameof(CanConfigure))]
        private static void Configure(UnityEditor.Editor editor)
        {
            var avatar = (DynamicCharacterAvatar)editor.target;
            var component = avatar.GetComponent<UmaDismemberment>();
            if (component == null) component = Undo.AddComponent<UmaDismemberment>(avatar.gameObject);
            Selection.activeObject = component;
            EditorGUIUtility.PingObject(component);
        }

        private static bool CanConfigure(UnityEditor.Editor editor) => editor.targets.Length == 1 &&
            editor.target is DynamicCharacterAvatar avatar && !EditorUtility.IsPersistent(avatar);
    }
}
