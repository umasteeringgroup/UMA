using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;
using System.Collections.Generic;
using System.IO;
using UMA.Examples;
using UMA.PoseTools;
using static UMA.UMAData;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
internal class UmaAddRacesToRecipesWindow : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _targetRecipes = new List<UMAWardrobeRecipe>();
		private readonly List<RaceData> _allRaces = new List<RaceData>();
		private bool[] _raceSelected = new bool[0];
		private Vector2 _scroll;

		public static void Open(List<UMAWardrobeRecipe> targetRecipes)
		{
			var window = GetWindow<UmaAddRacesToRecipesWindow>(true, "Add Races to Recipes", true);
			window.minSize = new Vector2(420f, 420f);

			window._targetRecipes.Clear();
			if (targetRecipes != null)
			{
				window._targetRecipes.AddRange(targetRecipes);
			}

			window.RefreshRaces();
			window.ShowUtility();
			window.Focus();
		}

		private void RefreshRaces()
		{
			_allRaces.Clear();

			var idx = UMAAssetIndexer.Instance;
			if (idx != null)
			{
				var races = idx.GetAllAssets<RaceData>();
				if (races != null)
				{
					for (int i = 0; i < races.Count; i++)
					{
						if (races[i] != null)
						{
							_allRaces.Add(races[i]);
						}
					}
				}
			}

			_allRaces.Sort((a, b) => string.Compare(a != null ? a.raceName : "", b != null ? b.raceName : "", System.StringComparison.OrdinalIgnoreCase));
			_raceSelected = new bool[_allRaces.Count];
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Add Race(s) to Selected UMAWardrobeRecipe", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox($"Selected recipes: {_targetRecipes.Count}", MessageType.Info);

			EditorGUILayout.Space(8);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("RaceData", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", GUILayout.Width(80)))
			{
				RefreshRaces();
			}
			EditorGUILayout.EndHorizontal();

			if (_allRaces.Count == 0)
			{
				EditorGUILayout.HelpBox("No RaceData found (UMAAssetIndexer not ready, or project has no RaceData).", MessageType.Warning);
			}
			else
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(80)))
				{
					for (int i = 0; i < _raceSelected.Length; i++)
					{
						_raceSelected[i] = true;
					}
				}
				if (GUILayout.Button("None", GUILayout.Width(80)))
				{
					for (int i = 0; i < _raceSelected.Length; i++)
					{
						_raceSelected[i] = false;
					}
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(4);
				_scroll = EditorGUILayout.BeginScrollView(_scroll);
				for (int i = 0; i < _allRaces.Count; i++)
				{
					var race = _allRaces[i];
					if (race == null)
					{
						continue;
					}

					string label = !string.IsNullOrEmpty(race.raceName) ? race.raceName : race.name;
					_raceSelected[i] = EditorGUILayout.ToggleLeft(label, _raceSelected[i]);
				}
				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.Space(8);
			using (new EditorGUI.DisabledScope(_targetRecipes.Count == 0 || _allRaces.Count == 0))
			{
				if (GUILayout.Button("Apply to Selected Recipes", GUILayout.Height(28)))
				{
					Apply();
				}
			}
		}

		private void Apply()
		{
			var selectedRaces = new List<RaceData>();
			for (int i = 0; i < _allRaces.Count; i++)
			{
				if (i < _raceSelected.Length && _raceSelected[i])
				{
					if (_allRaces[i] != null)
					{
						selectedRaces.Add(_allRaces[i]);
					}
				}
			}

			if (selectedRaces.Count == 0)
			{
				EditorUtility.DisplayDialog("Add Races", "Select one or more RaceData entries to apply.", "OK");
				return;
			}

			int added = 0;
			for (int i = 0; i < _targetRecipes.Count; i++)
			{
				var recipe = _targetRecipes[i];
				if (recipe == null)
				{
					continue;
				}

				Undo.RecordObject(recipe, "Add Races to Recipe");
				foreach (var race in selectedRaces)
				{
					if (race == null || string.IsNullOrEmpty(race.raceName))
					{
						continue;
					}

					if (!recipe.compatibleRaces.Contains(race.raceName))
					{
						recipe.compatibleRaces.Add(race.raceName);
						added++;
					}
				}
				EditorUtility.SetDirty(recipe);
			}

			AssetDatabase.SaveAssets();
			var idx = UMAAssetIndexer.Instance;
			if (idx != null)
			{
				idx.ForceSave();
			}

			EditorUtility.DisplayDialog("Add Races", $"Added {added} race assignment(s) to {_targetRecipes.Count} recipe(s).", "OK");
			Close();
		}
	}
}
