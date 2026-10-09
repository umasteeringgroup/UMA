using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors.PackageSupport
{
    internal sealed class UMAContentConflictDialog : EditorWindow
    {
        internal enum Choice { Cancel, BackupAndReplace, ReplacePackagedFiles, ReplaceEverything }

        private string message;
        private string reportPath;
        private Action<Choice> choose;

        internal static Choice Show(string message, string reportPath)
        {
            var result = Choice.Cancel;
            var window = CreateInstance<UMAContentConflictDialog>();
            window.titleContent = new GUIContent("Local UMA Content Changes Detected");
            window.message = message;
            window.reportPath = reportPath;
            window.choose = choice => result = choice;
            window.minSize = new Vector2(740, 340);
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center.x - 370, main.center.y - 170, 740, 340);
            try { window.ShowModalUtility(); }
            finally { if (window != null) DestroyImmediate(window); }
            return result;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.paddingTop = root.style.paddingBottom = 12;
            root.style.paddingLeft = root.style.paddingRight = 12;
            var content = new ScrollView();
            content.style.flexGrow = 1;
            content.Add(new HelpBox(message ?? string.Empty, HelpBoxMessageType.Warning));
            var report = new Label(reportPath ?? string.Empty);
            report.style.whiteSpace = WhiteSpace.Normal;
            report.style.marginTop = 8;
            content.Add(report);
            var removeExtraFiles = new Toggle("Also remove extra files from this package's folder")
            {
                value = false,
                tooltip = "Applies to Replace everything. Installed companion packages and project data outside this package remain intact."
            };
            removeExtraFiles.style.marginTop = 12;
            content.Add(removeExtraFiles);
            var explanation = new Label("Both replacement options keep a recovery backup. Back Up and Replace always preserves extra files.");
            explanation.style.whiteSpace = WhiteSpace.Normal;
            explanation.style.marginTop = 6;
            content.Add(explanation);
            root.Add(content);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 12;
            root.Add(buttons);
            Button AddButton(string label, Action action)
            {
                var button = new Button(action) { text = label };
                button.style.marginLeft = 6;
                button.style.minHeight = 26;
                buttons.Add(button);
                return button;
            }
            var cancel = AddButton("Cancel", () => Finish(Choice.Cancel));
            AddButton("Review Report", () => EditorUtility.RevealInFinder(reportPath));
            AddButton("Back Up and Replace", () => Finish(Choice.BackupAndReplace));
            AddButton("Replace everything", () => Finish(removeExtraFiles.value ? Choice.ReplaceEverything : Choice.ReplacePackagedFiles));
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                evt.StopPropagation();
                Finish(Choice.Cancel);
            });
            root.schedule.Execute(() => cancel.Focus());
        }

        private void Finish(Choice choice)
        {
            choose?.Invoke(choice);
            Close();
        }
    }
}
