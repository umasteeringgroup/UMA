using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace UMA.Editors
{

    public class UMAGenericPopupSelection : EditorWindow
    {
        const int titleBarHeight = 24;
        const int borderSize = 4;
        const int padding = 3;

        private static List<UMAGenericPopupChoice> Choices;

        public float lastHeight = 0f;
        public static void ShowWindow(string Title, List<UMAGenericPopupChoice> choices)
        {
            Choices = choices;
            var window = EditorWindow.GetWindow(typeof(UMAGenericPopupSelection));
            window.titleContent.text = Title;

            float height = titleBarHeight + borderSize;
            float width = 200.0f;
            var EditorPos = EditorGUIUtility.GetMainWindowPosition();
            //GUIStyle style = EditorStyles.iconButton;
            GUIStyle style = EditorStyles.miniButton;
            foreach(var v in choices)
            {
                var r = style.CalcSize(v.Content);
                height += r.y;
                height += padding;
                if (r.x > width)
                {
                    width = r.x;
                }
            }

            var WindowPos = new Rect(EditorPos.x + (EditorPos.width / 2) - (width / 2), EditorPos.y + (EditorPos.height / 2) - (height / 2), width, height);

            window.position = WindowPos;
            window.Focus();
            window.ShowModalUtility();
        }

        private void OnGUI()
        {
            GUILayout.Label("Select a category to add to", EditorStyles.boldLabel);
            foreach (UMAGenericPopupChoice c in Choices)
            {
                if (c.isSeperator)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.Separator();
                    GUILayout.Space(5);
                }
                else
                {
                    if (GUILayout.Button(c.Content))
                    {
                        c.FireEvent();
                        this.Close();
                    }
                }
            }
        }
    }
}
