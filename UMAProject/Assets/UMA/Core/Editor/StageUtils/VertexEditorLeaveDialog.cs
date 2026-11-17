using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Asked when the Vertex Editor closes with unsaved work: says what is not saved and offers each way
    /// of saving it that fits, then Leave Without Saving or Stay. Modal: <see cref="Ask"/> returns the
    /// choice once the window closes.
    /// </summary>
    internal sealed class VertexEditorLeaveDialog : EditorWindow
    {
        internal const int Stay = -1;
        internal const int LeaveWithoutSaving = -2;

        internal struct Option
        {
            public int id;
            public string label;
            public string help;
            public bool enabled;
            public string reason;
        }

        private static int result;

        private string summary;
        private List<Option> options;
        private bool canStay;
        private Vector2 scroll;
        private GUIStyle helpStyle;

        /// <summary>
        /// Shows the dialog and waits for a choice: an option's id, <see cref="Stay"/> or
        /// <see cref="LeaveWithoutSaving"/>. Closing the window counts as Stay when staying is possible.
        /// </summary>
        internal static int Ask(string summary, List<Option> options, bool canStay)
        {
            if (Application.isBatchMode)
            {
                return LeaveWithoutSaving;
            }
            result = canStay ? Stay : LeaveWithoutSaving;
            var window = CreateInstance<VertexEditorLeaveDialog>();
            window.titleContent = new GUIContent("Leave the Vertex Editor");
            window.summary = summary;
            window.options = options ?? new List<Option>();
            window.canStay = canStay;
            var size = new Vector2(480f, Mathf.Clamp(170f + (window.options.Count * 58f), 260f, 640f));
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center - (size * 0.5f), size);
            window.minSize = new Vector2(380f, 240f);
            window.ShowModal();
            return result;
        }

        private void OnGUI()
        {
            if (helpStyle == null)
            {
                helpStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { padding = new RectOffset(6, 4, 0, 4) };
            }
            GUILayout.Space(6f);
            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("How do you want to save it?", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Option option in options)
            {
                using (new EditorGUI.DisabledScope(!option.enabled))
                {
                    if (GUILayout.Button(option.label, GUILayout.Height(24f)))
                    {
                        Choose(option.id);
                    }
                }
                string text = option.enabled || string.IsNullOrEmpty(option.reason) ? option.help : option.reason;
                if (!string.IsNullOrEmpty(text))
                {
                    GUILayout.Label(text, helpStyle);
                }
            }
            EditorGUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Leave Without Saving", "Close the Vertex Editor. Unsaved work is lost."), GUILayout.Height(24f)))
            {
                Choose(LeaveWithoutSaving);
            }
            if (canStay && GUILayout.Button(new GUIContent("Stay", "Go back to the Vertex Editor."), GUILayout.Height(24f), GUILayout.Width(90f)))
            {
                Choose(Stay);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void Choose(int choice)
        {
            result = choice;
            Close();
            GUIUtility.ExitGUI();
        }
    }
}
