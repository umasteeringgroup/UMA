using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace UMA.TexturePaint.Editor
{
    internal static class TexturePaintPluginActions
    {
        [UMAPlugin(typeof(DynamicCharacterAvatar), "Overlay Painter",
            Tooltip = "Generate this character and paint its overlays.", ValidateMethod = nameof(CanPaintAvatar))]
        private static void PaintAvatar(UnityEditor.Editor editor)
        {
            var avatar = (DynamicCharacterAvatar)editor.target;
            avatar.GenerateNow();
            TexturePaintStageWindow.ShowStage(avatar);
        }

        private static bool CanPaintAvatar(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is DynamicCharacterAvatar avatar &&
            !EditorUtility.IsPersistent(avatar) && avatar.gameObject.scene.IsValid() &&
            PrefabStageUtility.GetPrefabStage(avatar.gameObject) == null;

        [UMAPlugin(typeof(SlotDataAsset), "Overlay Painter",
            Tooltip = "Paint this slot or its complete UDIM group.", ValidateMethod = nameof(CanPaintSlot))]
        private static void PaintSlot(UnityEditor.Editor editor) =>
            TexturePaintStandaloneSetupWindow.ShowForSlot((SlotDataAsset)editor.target);

        private static bool CanPaintSlot(UnityEditor.Editor editor) =>
            editor.targets.Length == 1 && editor.target is SlotDataAsset slot &&
            !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData);
    }
}
