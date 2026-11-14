using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    /// <summary>The window's material and last edit, kept per user and project in UserSettings.</summary>
    [FilePath("UserSettings/UMASharedColorParameters.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class SharedColorParameterSettings : ScriptableSingleton<SharedColorParameterSettings>
    {
        public Material material;
        public bool showHidden;
        public ColorParameterEdit edit = new ColorParameterEdit();

        public void Persist() => Save(true);
    }

    /// <summary>
    /// Project window: Assets > UMA > Manage Color Parameters. Adds or updates one shader parameter on every color of
    /// the selected shared color tables, with a fixed value or with a parameter the color already has, multiplied.
    /// </summary>
    public sealed class SharedColorParameterWindow : EditorWindow
    {
        [SerializeField] private List<SharedColorTable> tables = new List<SharedColorTable>();
        private Vector2 previewScroll;

        private Shader cachedShader;
        private bool cachedShowHidden;
        private readonly List<string> parameterNames = new List<string>();
        private readonly List<ColorParameterKind> parameterKinds = new List<ColorParameterKind>();
        private readonly List<string> parameterLabels = new List<string>();

        [MenuItem("Assets/UMA/Manage Color Parameters", false, 2010)]
        private static void OpenFromSelection() => Open(SelectedTables());

        [MenuItem("Assets/UMA/Manage Color Parameters", true)]
        private static bool OpenFromSelectionValidate() => SelectedTables().Count > 0;

        public static SharedColorParameterWindow Open(IEnumerable<SharedColorTable> selected)
        {
            var window = GetWindow<SharedColorParameterWindow>(true, "Manage Color Parameters", true);
            window.minSize = new Vector2(560, 480);
            window.tables.Clear();
            foreach (SharedColorTable table in selected)
                if (table != null && !window.tables.Contains(table)) window.tables.Add(table);
            window.Show();
            return window;
        }

        private static List<SharedColorTable> SelectedTables()
        {
            var result = new List<SharedColorTable>();
            foreach (Object o in Selection.objects)
                if (o is SharedColorTable table && EditorUtility.IsPersistent(table)) result.Add(table);
            return result;
        }

        private void OnGUI()
        {
            SharedColorParameterSettings settings = SharedColorParameterSettings.instance;
            ColorParameterEdit edit = settings.edit ??= new ColorParameterEdit();

            EditorGUILayout.HelpBox("Adds a shader parameter to every color in these tables, or updates it where a color already has one. " +
                "Pick the parameter from a material that uses the shader.", MessageType.None);

            DrawTables();
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            settings.material = (Material)EditorGUILayout.ObjectField(new GUIContent("Material",
                "A material whose shader has the parameter. Remembered for this project."), settings.material, typeof(Material), false);
            settings.showHidden = EditorGUILayout.Toggle(new GUIContent("Show Hidden Parameters",
                "Also list parameters the shader hides from the material inspector, such as blend and surface settings."), settings.showHidden);
            RefreshParameters(settings.material, settings.showHidden);

            if (settings.material == null)
            {
                EditorGUILayout.HelpBox("Choose a material to list its shader parameters.", MessageType.Info);
            }
            else if (parameterNames.Count == 0)
            {
                EditorGUILayout.HelpBox("This material's shader has no color, float, int or vector parameters.", MessageType.Warning);
            }
            else
            {
                DrawEdit(settings.material, edit);
            }
            if (EditorGUI.EndChangeCheck()) settings.Persist();

            EditorGUILayout.Space();
            DrawPreviewAndApply(settings.material, edit);
        }

        private void DrawTables()
        {
            EditorGUILayout.LabelField("Shared Color Tables", EditorStyles.boldLabel);
            int remove = -1;
            for (int i = 0; i < tables.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(tables[i], typeof(SharedColorTable), false);
                    int count = tables[i] != null && tables[i].colors != null ? tables[i].colors.Length : 0;
                    GUILayout.Label(count + (count == 1 ? " color" : " colors"), GUILayout.Width(70));
                    if (GUILayout.Button("Remove", GUILayout.Width(64))) remove = i;
                }
            }
            if (remove >= 0) tables.RemoveAt(remove);

            using (new EditorGUILayout.HorizontalScope())
            {
                var added = (SharedColorTable)EditorGUILayout.ObjectField("Add Table", null, typeof(SharedColorTable), false);
                if (added != null && !tables.Contains(added)) tables.Add(added);
                if (GUILayout.Button(new GUIContent("Add Selected", "Add the shared color tables selected in the Project window."), GUILayout.Width(100)))
                    foreach (SharedColorTable table in SelectedTables())
                        if (!tables.Contains(table)) tables.Add(table);
            }
            tables.RemoveAll(t => t == null);
        }

        private void DrawEdit(Material material, ColorParameterEdit edit)
        {
            int index = parameterNames.IndexOf(edit.parameterName);
            int chosen = EditorGUILayout.Popup(new GUIContent("Parameter", "The shader parameter to add or update on every color."),
                Mathf.Max(index, 0), parameterLabels.ToArray());
            if (chosen != index)
            {
                edit.parameterName = parameterNames[chosen];
                edit.kind = parameterKinds[chosen];
                edit.sourceParameter = edit.parameterName;
                LoadMaterialValue(material, edit);
            }
            edit.kind = parameterKinds[chosen];

            edit.source = (ColorParameterValueSource)EditorGUILayout.Popup(new GUIContent("New Value",
                "Fixed Value: the same value on every color. Existing Parameter x Multiplier: each color's own value of a parameter, multiplied."),
                (int)edit.source, new[] { "Fixed Value", "Existing Parameter x Multiplier" });

            EditorGUI.indentLevel++;
            if (edit.source == ColorParameterValueSource.ScaledParameter)
            {
                var compatible = new List<int>();
                for (int i = 0; i < parameterNames.Count; i++)
                    if (SharedColorParameterEditor.AreCompatible(parameterKinds[i], edit.kind)) compatible.Add(i);
                var labels = new string[compatible.Count];
                int sourceIndex = 0;
                for (int i = 0; i < compatible.Count; i++)
                {
                    labels[i] = parameterLabels[compatible[i]];
                    if (parameterNames[compatible[i]] == edit.sourceParameter) sourceIndex = i;
                }
                int pick = EditorGUILayout.Popup(new GUIContent("Source Parameter", "The parameter each color's new value is calculated from. Usually the parameter itself."),
                    sourceIndex, labels);
                edit.sourceParameter = parameterNames[compatible[pick]];
                edit.multiplier = EditorGUILayout.FloatField(new GUIContent("Multiplier",
                    edit.kind == ColorParameterKind.Color ? "Multiplies red, green and blue. Alpha is kept." : "Multiplies the source value."), edit.multiplier);
                edit.whenSourceMissing = (MissingSourceAction)EditorGUILayout.Popup(new GUIContent("If a Color Lacks It",
                    "Material Value x Multiplier: the value the color showed until now, since it came from the material. " +
                    "Fixed Value: the value below, not multiplied. Skip: leave the color unchanged."),
                    (int)edit.whenSourceMissing, new[] { "Use Material Value x Multiplier", "Use Fixed Value", "Skip the Color" });
            }
            if (edit.source == ColorParameterValueSource.FixedValue || edit.whenSourceMissing == MissingSourceAction.UseFixedValue)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawFixedValue(edit);
                    if (GUILayout.Button(new GUIContent("From Material", "Copy the material's current value."), GUILayout.Width(100)))
                        LoadMaterialValue(material, edit);
                }
            }
            EditorGUI.indentLevel--;
        }

        private static void DrawFixedValue(ColorParameterEdit edit)
        {
            var label = new GUIContent("Fixed Value");
            switch (edit.kind)
            {
                case ColorParameterKind.Int: edit.intValue = EditorGUILayout.IntField(label, edit.intValue); break;
                case ColorParameterKind.Color: edit.colorValue = EditorGUILayout.ColorField(label, edit.colorValue, true, true, true); break;
                case ColorParameterKind.Vector: edit.vectorValue = EditorGUILayout.Vector4Field(label, edit.vectorValue); break;
                default: edit.floatValue = EditorGUILayout.FloatField(label, edit.floatValue); break;
            }
        }

        private static void LoadMaterialValue(Material material, ColorParameterEdit edit)
        {
            if (!SharedColorParameterEditor.TryGetMaterialValue(material, edit.parameterName, out _, out Vector4 value)) return;
            edit.floatValue = value.x;
            edit.intValue = Mathf.RoundToInt(value.x);
            edit.colorValue = value;
            edit.vectorValue = value;
            GUI.FocusControl(null);
        }

        private void DrawPreviewAndApply(Material material, ColorParameterEdit edit)
        {
            bool ready = material != null && parameterNames.Contains(edit.parameterName) && tables.Count > 0;
            List<ColorParameterChange> changes = ready ? SharedColorParameterEditor.Preview(tables, edit, material) : new List<ColorParameterChange>();
            int changing = 0, skipped = 0;
            foreach (ColorParameterChange c in changes)
            {
                if (c.Skipped) skipped++;
                else if (!c.Unchanged) changing++;
            }

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            previewScroll = EditorGUILayout.BeginScrollView(previewScroll, EditorStyles.helpBox);
            SharedColorTable lastTable = null;
            foreach (ColorParameterChange c in changes)
            {
                if (c.Table != lastTable)
                {
                    EditorGUILayout.LabelField(c.Table.name, EditorStyles.miniBoldLabel);
                    lastTable = c.Table;
                }
                string result = c.Skipped ? "unchanged" : c.Unchanged ? c.After + "  (same)" : c.After;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(12);
                    GUILayout.Label(c.ColorName, GUILayout.Width(140));
                    GUILayout.Label(c.Before, GUILayout.Width(110));
                    GUILayout.Label("→", GUILayout.Width(16));
                    GUILayout.Label(result, GUILayout.Width(130));
                    GUILayout.Label(c.Note, EditorStyles.miniLabel);
                }
            }
            if (!ready) GUILayout.Label("Add a table and choose a material and parameter.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{changing} to change, {skipped} skipped, {changes.Count - changing - skipped} already set", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(changing == 0))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(100)) &&
                        EditorUtility.DisplayDialog("Manage Color Parameters",
                            $"Set {edit.parameterName} on {changing} color(s) and save the changed tables? You can undo this with Edit > Undo.", "Apply", "Cancel"))
                    {
                        int done = SharedColorParameterEditor.Apply(tables, edit, material);
                        ShowNotification(new GUIContent($"Updated {done} color(s)"));
                        // The modal dialog broke this layout pass; start a fresh one.
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        private void RefreshParameters(Material material, bool showHidden)
        {
            Shader shader = material != null ? material.shader : null;
            if (shader == cachedShader && showHidden == cachedShowHidden && (shader == null || parameterNames.Count > 0)) return;
            cachedShader = shader;
            cachedShowHidden = showHidden;
            parameterNames.Clear();
            parameterKinds.Clear();
            parameterLabels.Clear();
            if (shader == null) return;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (!showHidden && (shader.GetPropertyFlags(i) & ShaderPropertyFlags.HideInInspector) != 0) continue;
                if (!SharedColorParameterEditor.TryGetKind(shader.GetPropertyType(i), out ColorParameterKind kind)) continue;
                string name = shader.GetPropertyName(i);
                if (parameterNames.Contains(name)) continue;
                string description = shader.GetPropertyDescription(i);
                // A slash would open a submenu in the popup.
                string label = (string.IsNullOrEmpty(description) || description == name ? name : $"{name}  ({description})") + $"  [{kind}]";
                parameterNames.Add(name);
                parameterKinds.Add(kind);
                parameterLabels.Add(label.Replace('/', '∕'));
            }
        }
    }
}
