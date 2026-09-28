using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAPreset))]
    public sealed class UMAPresetInspector : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var preset = (UMAPreset)target;
            var root = new VisualElement();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(UMAEditorUtilities.FindUMAFullPath() + "/Core/Editor/Scripts/UMAPresetEditorWindow.uss");
            if (style != null) root.styleSheets.Add(style);
            var icon = new Image { image = preset.Icon, scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("uma-preset-icon");
            root.Add(icon);
            root.Add(new Label("Race: " + preset.Definition.RaceName));
            root.Add(new Label($"DNA: {preset.Definition.Dna?.Length ?? 0}   Colors: {preset.Definition.Colors?.Length ?? 0}   Wardrobe: {preset.Definition.Wardrobe?.Length ?? 0}"));
            root.Add(new HelpBox("Choose a scene avatar to author this preset from its current appearance, or apply this preset to it. Only included values change.", HelpBoxMessageType.Info));
            var avatar = new ObjectField("Source / target avatar") { objectType = typeof(DynamicCharacterAvatar), allowSceneObjects = true };
            root.Add(avatar);
            root.Add(new Button(() =>
            {
                if (UMAPresetAssetUtility.ImportLegacy(preset))
                {
                    var refreshed = CreateInspectorGUI();
                    root.Clear();
                    root.Add(refreshed);
                }
            }) { text = "Import legacy .umapreset..." });
            root.Add(new Button(() => UMAPresetEditorWindow.Open(avatar.value as DynamicCharacterAvatar, preset)) { text = "Edit in preset popup..." });
            root.Add(new Button(() =>
            {
                var targetAvatar = avatar.value as DynamicCharacterAvatar;
                if (targetAvatar == null) return;
                Undo.RecordObject(targetAvatar, "Apply UMA preset");
                preset.ApplyTo(targetAvatar, false);
                if (Application.isPlaying) targetAvatar.BuildCharacter(true);
                else if (targetAvatar.editorTimeGeneration) targetAvatar.GenerateSingleUMA();
                EditorUtility.SetDirty(targetAvatar);
            }) { text = "Apply to avatar" });
            return root;
        }
    }
}
