using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.TexturePaint.Editor
{
    internal sealed class TexturePaintPoseDialog : EditorWindow
    {
        private Action<TexturePaintCharacterPoseMode> choose;

        internal static TexturePaintCharacterPoseMode? Ask()
        {
            TexturePaintCharacterPoseMode? result = null;
            var window = CreateInstance<TexturePaintPoseDialog>();
            window.titleContent = new GUIContent("Overlay Painter Pose");
            window.minSize = new Vector2(440,300);
            window.maxSize = new Vector2(440,300);
            window.choose = mode => { result = mode; window.Close(); };
            window.ShowModalUtility();
            return result;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = root.style.paddingRight = 12;
            root.style.paddingTop = root.style.paddingBottom = 12;
            var description = new Label("Choose the pose for the painter preview. The scene character's pose is restored afterward.");
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginBottom = 8;
            root.Add(description);
            AddChoice("Build in Current Pose", TexturePaintCharacterPoseMode.Current,
                "Preserve the visible bone pose and blendshape weights through the rebuild.");
            AddChoice("Build in A-Pose", TexturePaintCharacterPoseMode.APose,
                "Use UMA's neutral A-pose animation. Requires a Humanoid avatar and UMA_APose clip.");
            AddChoice("Build in Default Pose", TexturePaintCharacterPoseMode.Default,
                "Generate the DNA-adjusted base pose with animation disabled and no animation controller.");
            var note = new Label("For saved world-space paths and projections, choose the pose used when authoring them.");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginTop = 8;
            note.style.flexGrow = 1;
            root.Add(note);
            var cancel = new Button(Close) {text = "Cancel"};
            cancel.style.height = 26;
            root.Add(cancel);
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); }
            });
        }

        private void AddChoice(string label, TexturePaintCharacterPoseMode mode, string tooltip)
        {
            var button = new Button(() => choose?.Invoke(mode)) {text = label, tooltip = tooltip};
            button.style.height = 32;
            button.style.marginBottom = 5;
            rootVisualElement.Add(button);
        }
    }
}
