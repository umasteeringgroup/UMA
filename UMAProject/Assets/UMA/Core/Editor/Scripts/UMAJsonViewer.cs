using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace UMA.Editors
{
    public class UMAJsonViewer : EditorWindow
    {
        [SerializeField] private string json = "";
        [SerializeField] private bool formatted = true;
        private TextField jsonField;

        public static UMAJsonViewer ShowJson(string json, string title = "UMA JSON")
        {
            var window = CreateInstance<UMAJsonViewer>();
            window.json = json ?? "";
            window.titleContent = new GUIContent(title);
            window.minSize = new Vector2(360, 240);
            window.position = new Rect(100, 100, 800, 600);
            window.Show();
            return window;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar();
            toolbar.style.flexShrink = 0;
            var formatToggle = new ToolbarToggle { text = "Format JSON", value = formatted };
            formatToggle.RegisterValueChangedCallback(evt =>
            {
                formatted = evt.newValue;
                RefreshText();
            });
            toolbar.Add(formatToggle);
            toolbar.Add(new ToolbarButton(() => EditorGUIUtility.systemCopyBuffer = json ?? "")
            {
                text = "Copy JSON",
                tooltip = "Copy the complete original JSON, without changing its formatting."
            });
            rootVisualElement.Add(toolbar);

            jsonField = new TextField
            {
                name = "jsonText",
                multiline = true,
                isReadOnly = true,
                verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible
            };
            jsonField.style.flexGrow = 1;
            jsonField.style.flexShrink = 1;
            jsonField.style.flexBasis = 0;
            jsonField.style.minHeight = 0;
            jsonField.style.whiteSpace = WhiteSpace.Normal;
            rootVisualElement.Add(jsonField);
            RefreshText();
        }

        private void RefreshText()
        {
            jsonField?.SetValueWithoutNotify(formatted ? FormatJson(json) : json ?? "");
        }

        private static string FormatJson(string source)
        {
            if (string.IsNullOrEmpty(source)) return "";
            var result = new StringBuilder(source.Length);
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            char previous = '\0';
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (inString)
                {
                    result.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    previous = c;
                    continue;
                }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '"')
                {
                    inString = true;
                    result.Append(c);
                }
                else if (c == '{' || c == '[')
                {
                    result.Append(c);
                    depth++;
                    int next = i + 1;
                    while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
                    if (next < source.Length && source[next] != '}' && source[next] != ']')
                        AppendLine(result, depth);
                }
                else if (c == '}' || c == ']')
                {
                    depth = Mathf.Max(0, depth - 1);
                    if (previous != '{' && previous != '[') AppendLine(result, depth);
                    result.Append(c);
                }
                else if (c == ',')
                {
                    result.Append(c);
                    AppendLine(result, depth);
                }
                else if (c == ':') result.Append(": ");
                else result.Append(c);
                previous = c;
            }
            return result.ToString();
        }

        private static void AppendLine(StringBuilder result, int depth)
        {
            result.Append('\n');
            result.Append(' ', Mathf.Min(depth, 64) * 2);
        }

        [MenuItem("Assets/UMA/View Recipe JSON", false, 2003)]
        private static void ViewRecipeJson()
        {
            if (Selection.activeObject is UMATextRecipe recipe)
                ShowJson(recipe.recipeString, recipe.name + " - JSON");
        }

        [MenuItem("Assets/UMA/View Recipe JSON", true)]
        private static bool ValidateViewRecipeJson()
        {
            return Selection.activeObject is UMATextRecipe;
        }
    }
}
