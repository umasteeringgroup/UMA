using UnityEngine;
using UnityEditor;

namespace UMA.Examples
{
    [CustomEditor(typeof(EnvPresetChooser))]
    public class EnvPresetChooserEd : Editor
    {
        new EnvPresetChooser target { get { return base.target as EnvPresetChooser; } }

        public override void OnInspectorGUI()
        {
            var presets = target.presets;
            var active = target.GetActivePreset();

            for (int i = 0, n = presets.Length; i < n; ++i)
            {
                GUILayout.BeginHorizontal();
                GUI.color = i == active ? new Color(0.5f, 1f, 0.5f) : Color.white;

                if (GUILayout.Button(presets[i].name))
                {
                    target.SetActivePreset(i);
                }

                if (GUILayout.Button("->", GUILayout.MaxWidth(30f)))
                {
                    var go = presets[i].gameObject;
                    var e = go.GetComponentInChildren<EnvPreset>();
                    Selection.activeGameObject = e ? e.gameObject : go;
                }

                GUILayout.EndHorizontal();
            }

            GUI.color = Color.white;
            GUI.enabled = Application.isPlaying;

            GUILayout.Space(25f);
            if (GUILayout.Button("Generate Screenshots"))
            {
                target.DumpAllScreens();
            }
        }
    }
}
