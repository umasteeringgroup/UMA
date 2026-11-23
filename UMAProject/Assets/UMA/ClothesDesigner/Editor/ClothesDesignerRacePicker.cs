using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>A small window that asks which race to make clothes for.</summary>
    internal sealed class ClothesDesignerRacePicker : EditorWindow
    {
        private List<RaceData> races = new List<RaceData>();
        private RaceData selected;
        private Action<RaceData> onPicked;
        private string filter = string.Empty;
        private Vector2 scroll;

        /// <summary>Shows the picker. <paramref name="preselected"/> is highlighted when it is given.</summary>
        internal static void Show(RaceData preselected, Action<RaceData> onPicked)
        {
            var window = CreateInstance<ClothesDesignerRacePicker>();
            window.titleContent = new GUIContent("Create Clothes on Race");
            window.races = ClothesDesignerLauncher.GetAllRaces();
            window.selected = preselected != null ? preselected : (window.races.Count == 1 ? window.races[0] : null);
            window.onPicked = onPicked;
            window.minSize = new Vector2(340f, 320f);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Which race are the clothes for?", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "A body of that race is built for you to fit the clothes on. Nothing needs to be in the scene.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

            if (races.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No race with a base recipe was found in the UMA Global Library. Add your race to the library first (UMA > Global Library).",
                    MessageType.Warning);
            }

            filter = EditorGUILayout.TextField(new GUIContent("Search", "Show only races whose name contains this text."), filter);
            scroll = EditorGUILayout.BeginScrollView(scroll, EditorStyles.helpBox);
            bool open = false;
            foreach (RaceData race in races)
            {
                if (!string.IsNullOrEmpty(filter) && race.raceName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                bool isSelected = race == selected;
                if (GUILayout.Toggle(isSelected, race.raceName, EditorStyles.radioButton) && !isSelected)
                {
                    selected = race;
                }
            }
            EditorGUILayout.EndScrollView();

            // A race that is not in the library can still be chosen from the project.
            selected = (RaceData)EditorGUILayout.ObjectField(new GUIContent("Race", "Or pick any RaceData asset in the project."), selected, typeof(RaceData), false);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
            {
                Close();
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(selected == null))
            {
                open = GUILayout.Button(new GUIContent("Create Clothes", selected == null ? "Choose a race first." : "Open the Clothes Designer on " + selected.raceName + "."), GUILayout.Width(130f));
            }
            EditorGUILayout.EndHorizontal();

            if (open && selected != null)
            {
                RaceData race = selected;
                Action<RaceData> callback = onPicked;
                Close();
                // After this window has gone: opening the stage changes what the Editor shows.
                EditorApplication.delayCall += () => callback?.Invoke(race);
                GUIUtility.ExitGUI();
            }
        }
    }
}
