using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UMA.HairCards.Editor
{
    internal static class HairCardsPluginActions
    {
        [UMAPlugin(typeof(DynamicCharacterAvatar), "Hair Card Editor", Order = 110,
            Tooltip = "Create and groom hair cards on this character.", ValidateMethod = nameof(CanOpenAvatar))]
        private static void OpenAvatar(UnityEditor.Editor editor)
        {
            var avatar = (DynamicCharacterAvatar)editor.target;
            avatar.GenerateNow();
            HairCardMenu.OpenAvatar(avatar);
        }

        private static bool CanOpenAvatar(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is DynamicCharacterAvatar avatar &&
            !EditorUtility.IsPersistent(avatar) && avatar.gameObject.scene.IsValid() &&
            PrefabStageUtility.GetPrefabStage(avatar.gameObject) == null;
    }
}
