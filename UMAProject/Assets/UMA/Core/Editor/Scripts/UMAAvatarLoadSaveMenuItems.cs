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
    public class UMAAvatarLoadSaveMenuItems : Editor
	{
		[MenuItem("Assets/UMA/Examine Wearables", false, 2001)]
		private static void AssignLocationsToWearablesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Assign Locations", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}

			ExamineWearables.Open(selectedRecipes);
		}

		[MenuItem("Assets/UMA/Examine Wearables", true)]
		private static bool AssignLocationsToWearablesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		[MenuItem("Assets/UMA/Consolidate Textures", false, 2002)]
		private static void ConsolidateTexturesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Textures", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}

			UmaConsolidateTexturesWindow.Open(selectedRecipes);
		}

		[MenuItem("Assets/UMA/Consolidate Textures", true)]
		private static bool ConsolidateTexturesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		[MenuItem("Assets/UMA/Consolidate texture for recipe", false, 2002)]
		private static void ConsolidateTexturesForTextRecipeMenu()
		{
			var selectedRecipes = GetSelectedTextRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Consolidate texture for recipe", "Select one or more UMATextRecipe assets in the Project window.", "OK");
				return;
			}

			string defaultFolder = "Assets";
			string pickedFolder = EditorUtility.OpenFolderPanel("Select destination folder", Application.dataPath, string.Empty);
			if (string.IsNullOrEmpty(pickedFolder))
			{
				return;
			}

			string destFolderPath = GetAssetFolderPathFromAbsolutePath(pickedFolder);
			if (string.IsNullOrEmpty(destFolderPath) || !AssetDatabase.IsValidFolder(destFolderPath))
			{
				EditorUtility.DisplayDialog("Consolidate texture for recipe", "Select a folder under the project's Assets folder.", "OK");
				return;
			}

			CopyOverlayTexturesForRecipes(selectedRecipes, destFolderPath, "Consolidate texture for recipe");
		}

		[MenuItem("Assets/UMA/Consolidate texture for recipe", true)]
		private static bool ConsolidateTexturesForTextRecipeMenu_Validate()
		{
			return GetSelectedTextRecipes().Count > 0;
		}

		[MenuItem("UMA/Consolidate Current Scene Assets", false, 2300)]
		private static void ConsolidateCurrentSceneAssetsMenu()
		{
			UmaConsolidateCurrentSceneAssetsWindow.Open();
		}

		[MenuItem("Assets/UMA/Examine Overlays", false, 2003)]
		private static void ExamineOverlaysMenu()
		{
			var overlays = GetSelectedOverlays();
			if (overlays.Count == 0)
			{
				EditorUtility.DisplayDialog("Examine Overlays", "Select one or more OverlayDataAsset assets in the Project window.", "OK");
				return;
			}

			UmaExamineOverlaysWindow.Open(overlays);
		}

		[MenuItem("Assets/UMA/Examine Overlays", true)]
		private static bool ExamineOverlaysMenu_Validate()
		{
			return GetSelectedOverlays().Count > 0;
		}

		[MenuItem("Assets/UMA/Examine Slots", false, 2005)]
		private static void ExamineSlotsMenu()
		{
			var slots = GetSelectedSlots();
			if (slots.Count == 0)
			{
				EditorUtility.DisplayDialog("Examine Slots", "Select one or more SlotDataAsset assets in the Project window.", "OK");
				return;
			}

			UmaExamineSlotsWindow.Open(slots);
		}

		[MenuItem("Assets/UMA/Examine Slots", true)]
		private static bool ExamineSlotsMenu_Validate()
		{
			return GetSelectedSlots().Count > 0;
		}

		[MenuItem("Assets/UMA/Extract T-Pose", false, 1999)]
		private static void ExtractTPoseMenu()
		{
			if (!TPoseExtracter.TryExtractSelectedTPose())
			{
				EditorUtility.DisplayDialog("Extract T-Pose", "Select one or more model assets in the Project window.", "OK");
			}
		}

		[MenuItem("Assets/UMA/Extract T-Pose", true)]
		private static bool ExtractTPoseMenu_Validate()
		{
			return Selection.objects != null && Selection.objects.Length > 0;
		}

		[MenuItem("Assets/UMA/Convert selected textures to PNG", false, 2004)]
		private static void ConvertSelectedTexturesToPngMenu()
		{
			var textures = GetSelectedTextures();
			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Convert textures", "Select one or more Texture2D assets in the Project window.", "OK");
				return;
			}

			UmaConvertTexturesToPngWindow.Open(textures);
		}

		[MenuItem("Assets/UMA/Convert selected textures to PNG", true)]
		private static bool ConvertSelectedTexturesToPngMenu_Validate()
		{
			return GetSelectedTextures().Count > 0;
		}

		[MenuItem("Assets/UMA/Add Race(s) to Selected Recipes", false, 2000)]
		private static void AddRacesToSelectedRecipesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Add Races", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}
			UmaAddRacesToRecipesWindow.Open(selectedRecipes);
		}

		[MenuItem("Assets/UMA/Create UMAMaterials for selected materials", false, 2006)]
		private static void CreateUmaMaterialsForSelectedMaterialsMenu()
		{
			var materials = GetSelectedMaterials();
			if (materials.Count == 0)
			{
				EditorUtility.DisplayDialog("Create UMAMaterials", "Select one or more Material assets in the Project window.", "OK");
				return;
			}

			for (int i = 0; i < materials.Count; i++)
			{
				var mat = materials[i];
				if (mat == null)
				{
					continue;
				}

				string matPath = AssetDatabase.GetAssetPath(mat);
				if (string.IsNullOrEmpty(matPath))
				{
					continue;
				}

				string dir = Path.GetDirectoryName(matPath);
				if (string.IsNullOrEmpty(dir))
				{
					dir = "Assets";
				}

				string baseName = "UMAMaterial_" + mat.name;
				string assetPath = Path.Combine(dir, baseName + ".asset").Replace('\\', '/');

				var umaMat = UMA.CustomAssetUtility.CreateAsset<UMAMaterial>(assetPath, false, baseName, false);
				if (umaMat == null)
				{
					continue;
				}

				umaMat.name = baseName;
				umaMat.material = mat;
				umaMat.materialType = UMAMaterial.MaterialType.Atlas;
				umaMat.MaterialName = mat.name;
				if (mat.shader != null)
				{
					umaMat.ShaderName = mat.shader.name;
				}
				else
				{
					umaMat.ShaderName = string.Empty;
				}

				var channels = BuildChannelsForMaterial(mat);
				umaMat.channels = channels;
				EditorUtility.SetDirty(umaMat);
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		[MenuItem("Assets/UMA/Create UMAMaterials for selected materials", true)]
		private static bool CreateUmaMaterialsForSelectedMaterialsMenu_Validate()
		{
			return GetSelectedMaterials().Count > 0;
		}

		internal class UmaExamineSlotsWindow : EditorWindow
		{
			private enum SlotSortMode
			{
				None = 0,
				Name = 1,
				SlotName = 2
			}

			private readonly List<UMA.SlotDataAsset> _slots = new List<UMA.SlotDataAsset>();
			private bool[] _slotSelected = new bool[0];
			private Vector2 _leftScroll;
			private Vector2 _rightScroll;
			private DefaultAsset _destFolder;
			private string _destFolderPath;
			private SlotSortMode _sortMode = SlotSortMode.None;
			private bool _setMaterial;
			private UMA.UMAMaterial _targetMaterial;
			private bool _setOverlayScale;
			private float _overlayScale = 1f;
			private bool _addTags;
			private string _tagsText = string.Empty;
			private bool _setWildcard;
			private bool _wildcardValue;
			private bool _addWildcardRaces;
			private string _racesText = string.Empty;

			public static void Open(List<UMA.SlotDataAsset> slots)
			{
				var window = GetWindow<UmaExamineSlotsWindow>(false, "Examine Slots", true);
				window.minSize = new Vector2(860f, 420f);
				window._slots.Clear();
				if (slots != null)
				{
					window._slots.AddRange(slots);
				}
				window._slotSelected = new bool[window._slots.Count];
				for (int i = 0; i < window._slotSelected.Length; i++)
				{
					window._slotSelected[i] = true;
				}
				window._destFolder = null;
				window._destFolderPath = string.Empty;
				window.Show();
				window.Focus();
			}

			private void RefreshFromSelection()
			{
				var selected = GetSelectedSlots();
				_slots.Clear();
				_slots.AddRange(selected);
				_slotSelected = new bool[_slots.Count];
				for (int i = 0; i < _slotSelected.Length; i++)
				{
					_slotSelected[i] = true;
				}
				SortSlots();
				Repaint();
			}

			private void OnGUI()
			{
				EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
				GUILayout.Label("Examine Slots", EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
				{
					RefreshFromSelection();
				}
				EditorGUILayout.EndHorizontal();

				if (_slots.Count == 0)
				{
					EditorGUILayout.HelpBox("Select one or more SlotDataAsset assets in the Project window.", MessageType.Info);
					return;
				}

				EditorGUILayout.BeginHorizontal();
				DrawSlotsColumn();
				GUILayout.Space(10);
				DrawOptionsColumn();
				EditorGUILayout.EndHorizontal();
			}

			private void DrawOptionsColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.42f));
				EditorGUILayout.LabelField("Slot Updates", EditorStyles.boldLabel);

				_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
				_setMaterial = EditorGUILayout.ToggleLeft("Set UMAMaterial", _setMaterial);
				using (new EditorGUI.DisabledScope(!_setMaterial))
				{
					_targetMaterial = (UMA.UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _targetMaterial, typeof(UMA.UMAMaterial), false);
				}

				_setOverlayScale = EditorGUILayout.ToggleLeft("Set OverlayScale", _setOverlayScale);
				using (new EditorGUI.DisabledScope(!_setOverlayScale))
				{
					_overlayScale = EditorGUILayout.FloatField("OverlayScale", _overlayScale);
				}

				_addTags = EditorGUILayout.ToggleLeft("Add Tags", _addTags);
				using (new EditorGUI.DisabledScope(!_addTags))
				{
					_tagsText = EditorGUILayout.TextField("Tags (comma/semicolon)", _tagsText);
				}

				_setWildcard = EditorGUILayout.ToggleLeft("Set Wildcard", _setWildcard);
				using (new EditorGUI.DisabledScope(!_setWildcard))
				{
					_wildcardValue = EditorGUILayout.Toggle("Wildcard Value", _wildcardValue);
				}

				_addWildcardRaces = EditorGUILayout.ToggleLeft("Add Wildcard Races", _addWildcardRaces);
				using (new EditorGUI.DisabledScope(!_addWildcardRaces))
				{
					_racesText = EditorGUILayout.TextField("Races (comma/semicolon)", _racesText);
				}

				EditorGUILayout.Space(6);
				EditorGUILayout.LabelField("Destination Folder", EditorStyles.boldLabel);
				EditorGUI.BeginChangeCheck();
				_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
				if (EditorGUI.EndChangeCheck())
				{
					_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : string.Empty;
					if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
					{
						_destFolder = null;
						_destFolderPath = string.Empty;
					}
				}

				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.TextField("Path", _destFolderPath);
				}

				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(8);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Apply Updates", GUILayout.Width(140), GUILayout.Height(28)))
				{
					ApplyUpdates();
				}
				if (GUILayout.Button("Replace Slots In Folder", GUILayout.Width(180), GUILayout.Height(28)))
				{
					ReplaceSlotsInFolder();
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
			}

			private void DrawSlotsColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				EditorGUILayout.LabelField("Selected Slots", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Sort By", GUILayout.Width(50));
				EditorGUI.BeginChangeCheck();
				_sortMode = (SlotSortMode)EditorGUILayout.EnumPopup(_sortMode, GUILayout.Width(120));
				if (EditorGUI.EndChangeCheck())
				{
					SortSlots();
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Select All", GUILayout.Width(90)))
				{
					SetAllSelections(true);
				}
				if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
				{
					SetAllSelections(false);
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space(4);

				_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _slots.Count; i++)
				{
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					_slotSelected[i] = EditorGUILayout.Toggle(_slotSelected[i], GUILayout.Width(18));
					EditorGUILayout.ObjectField(slot, typeof(UMA.SlotDataAsset), false);
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

			private void SetAllSelections(bool value)
			{
				for (int i = 0; i < _slotSelected.Length; i++)
				{
					_slotSelected[i] = value;
				}
			}

			private void SortSlots()
			{
				if (_sortMode == SlotSortMode.None || _slots.Count == 0)
				{
					return;
				}

				var entries = new List<SlotEntry>(_slots.Count);
				for (int i = 0; i < _slots.Count; i++)
				{
					entries.Add(new SlotEntry { Slot = _slots[i], Selected = (i < _slotSelected.Length && _slotSelected[i]) });
				}

				if (_sortMode == SlotSortMode.Name)
				{
					entries.Sort((a, b) => string.Compare(a.GetName(), b.GetName(), System.StringComparison.OrdinalIgnoreCase));
				}
				else if (_sortMode == SlotSortMode.SlotName)
				{
					entries.Sort((a, b) => string.Compare(a.GetSlotName(), b.GetSlotName(), System.StringComparison.OrdinalIgnoreCase));
				}

				_slots.Clear();
				_slotSelected = new bool[entries.Count];
				for (int i = 0; i < entries.Count; i++)
				{
					_slots.Add(entries[i].Slot);
					_slotSelected[i] = entries[i].Selected;
				}
			}

			private struct SlotEntry
			{
				public UMA.SlotDataAsset Slot;
				public bool Selected;

				public string GetName()
				{
					if (Slot == null)
					{
						return string.Empty;
					}
					return Slot.name ?? string.Empty;
				}

				public string GetSlotName()
				{
					if (Slot == null)
					{
						return string.Empty;
					}
					return Slot.slotName ?? string.Empty;
				}
			}

			private void ApplyUpdates()
			{
				bool anySaved = false;
				var tagsToAdd = ParseTokens(_tagsText);
				var racesToAdd = ParseTokens(_racesText);

				for (int i = 0; i < _slots.Count; i++)
				{
					if (i >= _slotSelected.Length || !_slotSelected[i])
					{
						continue;
					}
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					bool changed = false;
					Undo.RecordObject(slot, "Update Slot");

					if (_setMaterial)
					{
						slot.material = _targetMaterial;
						if (_targetMaterial != null)
						{
							slot.materialName = _targetMaterial.name;
						}
						else
						{
							slot.materialName = string.Empty;
						}
						changed = true;
					}

					if (_setOverlayScale)
					{
						slot.overlayScale = _overlayScale;
						changed = true;
					}

					if (_addTags && tagsToAdd.Count > 0)
					{
						var merged = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
						if (slot.tags != null)
						{
							for (int t = 0; t < slot.tags.Length; t++)
							{
								var tag = slot.tags[t];
								if (!string.IsNullOrEmpty(tag))
								{
									merged.Add(tag.Trim());
								}
							}
						}
						for (int t = 0; t < tagsToAdd.Count; t++)
						{
							merged.Add(tagsToAdd[t]);
						}
						if (merged.Count > 0)
						{
							slot.tags = new List<string>(merged).ToArray();
							changed = true;
						}
					}

					if (_setWildcard)
					{
						slot.isWildCardSlot = _wildcardValue;
						changed = true;
					}

					if (_addWildcardRaces && racesToAdd.Count > 0)
					{
						var merged = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
						if (slot.Races != null)
						{
							for (int r = 0; r < slot.Races.Length; r++)
							{
								var race = slot.Races[r];
								if (!string.IsNullOrEmpty(race))
								{
									merged.Add(race.Trim());
								}
							}
						}
						for (int r = 0; r < racesToAdd.Count; r++)
						{
							merged.Add(racesToAdd[r]);
						}
						if (merged.Count > 0)
						{
							slot.Races = new List<string>(merged).ToArray();
							changed = true;
						}
					}

					if (changed)
					{
						EditorUtility.SetDirty(slot);
#if UNITY_2021_1_OR_NEWER
						AssetDatabase.SaveAssetIfDirty(slot);
#endif
						anySaved = true;
					}
				}

				if (anySaved)
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
				}
			}

			private void ReplaceSlotsInFolder()
			{
				if (string.IsNullOrEmpty(_destFolderPath))
				{
					EditorUtility.DisplayDialog("Replace Slots In Folder", "Select a destination folder.", "OK");
					return;
				}

				int updated = 0;
				for (int i = 0; i < _slots.Count; i++)
				{
					if (i >= _slotSelected.Length || !_slotSelected[i])
					{
						continue;
					}
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					var searchNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
					if (!string.IsNullOrEmpty(slot.name))
					{
						searchNames.Add(slot.name);
					}
					if (!string.IsNullOrEmpty(slot.slotName))
					{
						searchNames.Add(slot.slotName);
					}

					string[] guids = AssetDatabase.FindAssets("t:SlotDataAsset", new[] { _destFolderPath });
					for (int g = 0; g < guids.Length; g++)
					{
						string path = AssetDatabase.GUIDToAssetPath(guids[g]);
						if (string.IsNullOrEmpty(path))
						{
							continue;
						}
						var target = AssetDatabase.LoadAssetAtPath<UMA.SlotDataAsset>(path);
						if (target == null)
						{
							continue;
						}
						if (target == slot)
						{
							continue;
						}
						if (!searchNames.Contains(target.name) && !searchNames.Contains(target.slotName))
						{
							continue;
						}

						Undo.RecordObject(target, "Replace Slot In Folder");
						EditorUtility.CopySerialized(slot, target);
						EditorUtility.SetDirty(target);
						AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
#if UNITY_2021_1_OR_NEWER
						AssetDatabase.SaveAssetIfDirty(target);
#endif
						updated++;
					}
				}

				if (updated > 0)
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
					UMAAssetIndexer.RebuildAllUMAS();
				}
				EditorUtility.DisplayDialog("Replace Slots In Folder", "Updated slots: " + updated, "OK");
			}

			private static List<string> ParseTokens(string input)
			{
				var results = new List<string>();
				if (string.IsNullOrEmpty(input))
				{
					return results;
				}
				char[] separators = new[] { ',', ';', '\n', '\r', '\t' };
				string[] parts = input.Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < parts.Length; i++)
				{
					string token = parts[i].Trim();
					if (!string.IsNullOrEmpty(token))
					{
						results.Add(token);
					}
				}
				return results;
			}
		}

	internal class UmaConsolidateTexturesWindow : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _recipes = new List<UMAWardrobeRecipe>();
		private DefaultAsset _destFolder;
		private string _destFolderPath;
		private Vector2 _scroll;

		public static void Open(List<UMAWardrobeRecipe> recipes)
		{
			var window = GetWindow<UmaConsolidateTexturesWindow>(true, "Consolidate Textures", true);
			window.minSize = new Vector2(520f, 180f);
			window._recipes.Clear();
			if (recipes != null)
			{
				window._recipes.AddRange(recipes);
			}
			window._destFolder = null;
			window._destFolderPath = "";
			window.ShowUtility();
			window.Focus();
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Consolidate Textures", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Copies textures referenced by overlays in the selected UMAWardrobeRecipe assets into a chosen folder.", MessageType.Info);
			EditorGUILayout.Space(6);

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			EditorGUILayout.LabelField("Destination Folder", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : "";
				if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
				{
					_destFolder = null;
					_destFolderPath = "";
				}
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Path", _destFolderPath);
			}
			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space(8);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_destFolderPath) || _recipes.Count == 0))
			{
				if (GUILayout.Button("Move Textures", GUILayout.Width(140), GUILayout.Height(28)))
				{
					CopyTextures();
				}
			}
			if (GUILayout.Button("Cancel", GUILayout.Width(140), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void CopyTextures()
		{
			var textures = new HashSet<string>();
			var overlaysToSave = new HashSet<UMA.OverlayDataAsset>();
			var overlayTextureRefs = new Dictionary<string, List<(UMA.OverlayDataAsset overlay, int index)>>(System.StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int i = 0; i < _recipes.Count; i++)
				{
					var recipe = _recipes[i];
					if (recipe == null)
					{
						continue;
					}

					EditorUtility.DisplayProgressBar("Consolidate Textures", "Scanning recipes...", Mathf.Clamp01((float)i / Mathf.Max(1, _recipes.Count)));

					var umaRecipe = new UMA.UMAData.UMARecipe();
					recipe.Load(umaRecipe, true);
					if (umaRecipe.slotDataList == null)
					{
						continue;
					}

					for (int s = 0; s < umaRecipe.slotDataList.Length; s++)
					{
						var slot = umaRecipe.slotDataList[s];
						if (slot == null)
						{
							continue;
						}

						for (int o = 0; o < slot.OverlayCount; o++)
						{
							var overlay = slot.GetOverlay(o);
							if (overlay == null)
							{
								continue;
							}
							var overlayAsset = overlay.asset;
							if (overlayAsset == null)
							{
								continue;
							}

							if (overlayAsset.textureList != null)
							{
								for (int t = 0; t < overlayAsset.textureList.Length; t++)
								{
									var tex = overlayAsset.textureList[t];
									if (tex == null)
									{
										continue;
									}
									string srcPath = AssetDatabase.GetAssetPath(tex);
									if (!string.IsNullOrEmpty(srcPath))
									{
										textures.Add(srcPath);
										if (!overlayTextureRefs.TryGetValue(srcPath, out var refsForPath) || refsForPath == null)
										{
											refsForPath = new List<(UMA.OverlayDataAsset overlay, int index)>();
											overlayTextureRefs[srcPath] = refsForPath;
										}
										refsForPath.Add((overlayAsset, t));
									}
								}
							}

							if (overlayAsset.alphaMask != null)
							{
								string srcPath = AssetDatabase.GetAssetPath(overlayAsset.alphaMask);
								if (!string.IsNullOrEmpty(srcPath))
								{
									textures.Add(srcPath);
								}
							}
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Textures", "No textures were found in overlays for the selected recipes.", "OK");
				return;
			}

			int moved = 0;
			int total = textures.Count;
			int index = 0;
			try
			{
				foreach (string srcPath in textures)
				{
					index++;
					EditorUtility.DisplayProgressBar("Consolidate Textures", "Moving textures...", Mathf.Clamp01((float)index / Mathf.Max(1, total)));
					if (string.IsNullOrEmpty(srcPath))
					{
						continue;
					}

					// Skip textures that are already inside the destination folder
					if (!string.IsNullOrEmpty(_destFolderPath) && srcPath.StartsWith(_destFolderPath + "/", System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string fileName = Path.GetFileName(srcPath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					string destPathAlready = _destFolderPath + "/" + fileName;
					var existingDestTexture = AssetDatabase.LoadAssetAtPath<Texture>(destPathAlready);
					if (existingDestTexture != null)
					{
						if (overlayTextureRefs.TryGetValue(srcPath, out var refsForPath) && refsForPath != null)
						{
							for (int r = 0; r < refsForPath.Count; r++)
							{
								var (overlay, texIndex) = refsForPath[r];
								if (overlay == null)
								{
									continue;
								}
								var list = overlay.textureList;
								if (list == null || texIndex < 0 || texIndex >= list.Length)
								{
									continue;
								}
								if (list[texIndex] == existingDestTexture)
								{
									continue;
								}
								Undo.RecordObject(overlay, "Relink overlay texture");
								list[texIndex] = existingDestTexture;
								overlay.textureList = list;
								EditorUtility.SetDirty(overlay);
								overlaysToSave.Add(overlay);
							}
						}
						continue;
					}

					string destPath = AssetDatabase.GenerateUniqueAssetPath(destPathAlready);
					string moveError = AssetDatabase.MoveAsset(srcPath, destPath);
					if (!string.IsNullOrEmpty(moveError))
					{
						continue;
					}

					moved++;
					var movedTexture = AssetDatabase.LoadAssetAtPath<Texture>(destPath);
					if (movedTexture == null)
					{
						continue;
					}

					if (overlayTextureRefs.TryGetValue(srcPath, out var refsAfterMove) && refsAfterMove != null)
					{
						for (int r = 0; r < refsAfterMove.Count; r++)
						{
							var (overlay, texIndex) = refsAfterMove[r];
							if (overlay == null)
							{
								continue;
							}
							var list = overlay.textureList;
							if (list == null || texIndex < 0 || texIndex >= list.Length)
							{
								continue;
							}
							if (list[texIndex] == movedTexture)
							{
								continue;
							}
							Undo.RecordObject(overlay, "Relink overlay texture");
							list[texIndex] = movedTexture;
							overlay.textureList = list;
							EditorUtility.SetDirty(overlay);
							overlaysToSave.Add(overlay);
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (overlaysToSave.Count > 0)
			{
				AssetDatabase.SaveAssets();
			}
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Consolidate Textures", "Moved " + moved + " texture asset(s) into: " + _destFolderPath + "\nUpdated overlays: " + overlaysToSave.Count, "OK");
		}
	}

	internal class UmaConsolidateCurrentSceneAssetsWindow : EditorWindow
	{
       private class ConsolidateCandidate
		{
			public string Name;
			public string Path;
			public string TypeName;
			public string Category;
			public bool Selected = true;
		}

		private const string DefaultDestinationFolder = "Assets/UMA/UMA3/Examples/ExampleAssets";
       private const string DefaultSourceFolder = "Assets";
		private DefaultAsset _destFolder;
		private string _destFolderPath = DefaultDestinationFolder;
		private DefaultAsset _sourceFolder;
		private string _sourceFolderPath = DefaultSourceFolder;
		private readonly List<ConsolidateCandidate> _candidates = new List<ConsolidateCandidate>();
		private Vector2 _candidateScroll;

		public static void Open()
		{
			var window = GetWindow<UmaConsolidateCurrentSceneAssetsWindow>(true, "Consolidate Current Scene Assets", true);
            window.minSize = new Vector2(820f, 420f);
			window._destFolderPath = DefaultDestinationFolder;
            window._sourceFolderPath = DefaultSourceFolder;
			window.TryInitializeDefaultFolder();
           window.TryInitializeSourceFolder();
           window.RebuildCandidateList();
			window.ShowUtility();
			window.Focus();
		}

		private void TryInitializeDefaultFolder()
		{
			if (!AssetDatabase.IsValidFolder(_destFolderPath))
			{
				_destFolder = null;
				return;
			}

			_destFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_destFolderPath);
		}

		private void TryInitializeSourceFolder()
		{
			if (!AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				_sourceFolder = null;
				return;
			}

			_sourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_sourceFolderPath);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Consolidate Current Scene Assets", EditorStyles.boldLabel);
         EditorGUILayout.HelpBox("Copies allowed assets referenced by the current scene into category subfolders under a destination folder.", MessageType.Info);
			EditorGUILayout.Space(6);

			EditorGUILayout.LabelField("Destination Folder (under Assets)", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : DefaultDestinationFolder;
				if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
				{
					_destFolder = null;
					_destFolderPath = DefaultDestinationFolder;
				}
               RebuildCandidateList();
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Path", _destFolderPath);
			}

			EditorGUILayout.Space(6);
			EditorGUILayout.LabelField("Source Folder (under Assets)", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(_sourceFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_sourceFolderPath = _sourceFolder != null ? AssetDatabase.GetAssetPath(_sourceFolder) : DefaultSourceFolder;
				if (!string.IsNullOrEmpty(_sourceFolderPath) && !AssetDatabase.IsValidFolder(_sourceFolderPath))
				{
					_sourceFolder = null;
					_sourceFolderPath = DefaultSourceFolder;
				}
               RebuildCandidateList();
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Source Path", _sourceFolderPath);
			}

			EditorGUILayout.Space(10);
          DrawCandidateList();

			EditorGUILayout.Space(10);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
           using (new EditorGUI.DisabledScope(CountSelectedCandidates() == 0))
			{
                if (GUILayout.Button("Consolidate", GUILayout.Width(120), GUILayout.Height(28)))
				{
					ContinueConsolidation();
				}
			}
			if (GUILayout.Button("Cancel", GUILayout.Width(120), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawCandidateList()
		{
			EditorGUILayout.LabelField("Items To Consolidate", EditorStyles.boldLabel);
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select All", GUILayout.Width(100)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = true;
				}
			}
			if (GUILayout.Button("Clear Selection", GUILayout.Width(120)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = false;
				}
			}
			if (GUILayout.Button("Invert Selection", GUILayout.Width(120)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = !_candidates[i].Selected;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label("Selected: " + CountSelectedCandidates() + " / " + _candidates.Count, EditorStyles.miniLabel);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(4);
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("", GUILayout.Width(20));
			GUILayout.Label("Object Name", EditorStyles.boldLabel, GUILayout.Width(220));
			GUILayout.Label("Path", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(120));
			EditorGUILayout.EndHorizontal();

			float listHeight = Mathf.Max(180f, position.height - 220f);
			_candidateScroll = EditorGUILayout.BeginScrollView(_candidateScroll, GUILayout.Height(listHeight));
			if (_candidates.Count == 0)
			{
				EditorGUILayout.HelpBox("No allowed scene dependencies were found under the selected source folder.", MessageType.Info);
			}
			else
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					var candidate = _candidates[i];
					if (candidate == null)
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					candidate.Selected = EditorGUILayout.Toggle(candidate.Selected, GUILayout.Width(20));
					EditorGUILayout.SelectableLabel(candidate.Name ?? string.Empty, GUILayout.Width(220), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.SelectableLabel(candidate.Path ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.SelectableLabel(candidate.TypeName ?? string.Empty, GUILayout.Width(120), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.EndHorizontal();
				}
			}
			EditorGUILayout.EndScrollView();
		}

		private int CountSelectedCandidates()
		{
			int count = 0;
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null && _candidates[i].Selected)
				{
					count++;
				}
			}
			return count;
		}

		private void RebuildCandidateList()
		{
			_candidates.Clear();
			_candidateScroll = Vector2.zero;

			if (string.IsNullOrEmpty(_sourceFolderPath) || !AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				return;
			}

			var activeScene = SceneManager.GetActiveScene();
			if (!activeScene.IsValid())
			{
				return;
			}

			var rootObjects = activeScene.GetRootGameObjects();
			if (rootObjects == null || rootObjects.Length == 0)
			{
				return;
			}

			var dependencies = EditorUtility.CollectDependencies(rootObjects);
			if (dependencies == null || dependencies.Length == 0)
			{
				return;
			}

			var processed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < dependencies.Length; i++)
			{
				var dep = dependencies[i];
				if (dep == null)
				{
					continue;
				}

				string sourcePath = AssetDatabase.GetAssetPath(dep);
				if (!IsCandidatePathAllowed(sourcePath))
				{
					continue;
				}
				if (processed.Contains(sourcePath))
				{
					continue;
				}
				if (!TryGetAllowedCategoryForAsset(sourcePath, dep, out string category))
				{
					continue;
				}

				processed.Add(sourcePath);
				_candidates.Add(new ConsolidateCandidate
				{
					Name = dep.name,
					Path = sourcePath,
					TypeName = dep.GetType().Name,
					Category = category,
					Selected = true
				});
			}

			_candidates.Sort((a, b) =>
			{
				int pathCompare = string.Compare(a != null ? a.Path : string.Empty, b != null ? b.Path : string.Empty, System.StringComparison.OrdinalIgnoreCase);
				if (pathCompare != 0)
				{
					return pathCompare;
				}
				return string.Compare(a != null ? a.Name : string.Empty, b != null ? b.Name : string.Empty, System.StringComparison.OrdinalIgnoreCase);
			});
		}

		private bool IsCandidatePathAllowed(string sourcePath)
		{
			if (string.IsNullOrEmpty(sourcePath))
			{
				return false;
			}
			if (!sourcePath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (AssetDatabase.IsValidFolder(sourcePath))
			{
				return false;
			}
			if (sourcePath.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!sourcePath.StartsWith(_sourceFolderPath + "/", System.StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(sourcePath, _sourceFolderPath, System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			return true;
		}

		private void ContinueConsolidation()
		{
			if (!EnsureFolderPathExists(_destFolderPath))
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Could not create destination folder:\n" + _destFolderPath, "OK");
				return;
			}

			if (string.IsNullOrEmpty(_sourceFolderPath) || !AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Select a valid source folder under Assets.", "OK");
				return;
			}

			if (_candidates.Count == 0)
			{
             EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "No allowed scene dependencies were found.", "OK");
				return;
			}
			if (CountSelectedCandidates() == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Select at least one item to consolidate.", "OK");
				return;
			}

         var movedByCategory = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
			{
				["Textures"] = 0,
				["Models"] = 0,
				["Sounds"] = 0,
				["Materials"] = 0,
             ["Prefabs"] = 0,
				["Slots"] = 0,
				["Overlays"] = 0,
			};
         int moveErrors = 0;

			try
			{
               for (int i = 0; i < _candidates.Count; i++)
				{
                  EditorUtility.DisplayProgressBar("Consolidate Current Scene Assets", "Moving selected assets...", Mathf.Clamp01((float)(i + 1) / Mathf.Max(1, _candidates.Count)));
					var candidate = _candidates[i];
					if (candidate == null || !candidate.Selected)
					{
						continue;
					}

                    string sourcePath = candidate.Path;
					if (!IsCandidatePathAllowed(sourcePath))
					{
						continue;
					}
					if (sourcePath.StartsWith(_destFolderPath + "/", System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
                 string categoryPath = _destFolderPath + "/" + candidate.Category;
					if (!EnsureFolderPathExists(categoryPath))
					{
                       moveErrors++;
						continue;
					}

					string fileName = Path.GetFileName(sourcePath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					string destPath = categoryPath + "/" + fileName;
					if (string.Equals(sourcePath, destPath, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

                    string uniqueDestPath = AssetDatabase.GenerateUniqueAssetPath(destPath);
					string moveError = AssetDatabase.MoveAsset(sourcePath, uniqueDestPath);
					if (!string.IsNullOrEmpty(moveError))
					{
                       moveErrors++;
						continue;
					}

                  movedByCategory[candidate.Category] = movedByCategory[candidate.Category] + 1;
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"Consolidate Current Scene Assets",
            "Moved Textures: " + movedByCategory["Textures"] +
				"\nMoved Models: " + movedByCategory["Models"] +
				"\nMoved Sounds: " + movedByCategory["Sounds"] +
				"\nMoved Materials: " + movedByCategory["Materials"] +
				"\nMoved Prefabs: " + movedByCategory["Prefabs"] +
				"\nMoved Slots: " + movedByCategory["Slots"] +
				"\nMoved Overlays: " + movedByCategory["Overlays"] +
				"\nMove errors: " + moveErrors,
				"OK");

			Close();
		}

       private static bool TryGetAllowedCategoryForAsset(string assetPath, UnityEngine.Object asset, out string category)
		{
          category = null;

			if (asset is UMA.SlotDataAsset)
			{
				category = "Slots";
				return true;
			}
			if (asset is UMA.OverlayDataAsset)
			{
				category = "Overlays";
				return true;
			}
			if (asset is Material)
			{
             category = "Materials";
				return true;
			}
			if (asset is Texture)
			{
              category = "Textures";
				return true;
			}
			if (asset is AudioClip)
			{
                category = "Sounds";
				return true;
			}

			if (asset is GameObject)
			{
				string gameObjectExt = Path.GetExtension(assetPath);
				if (!string.IsNullOrEmpty(gameObjectExt) && string.Equals(gameObjectExt, ".prefab", System.StringComparison.OrdinalIgnoreCase))
				{
					category = "Prefabs";
					return true;
				}
			}

			var importer = AssetImporter.GetAtPath(assetPath);
			if (importer is ModelImporter)
			{
                category = "Models";
				return true;
			}

			string ext = Path.GetExtension(assetPath);
			if (!string.IsNullOrEmpty(ext))
			{
				ext = ext.ToLowerInvariant();
				if (ext == ".fbx" || ext == ".obj" || ext == ".dae" || ext == ".3ds" || ext == ".blend")
				{
                    category = "Models";
					return true;
				}
				if (ext == ".prefab")
				{
					category = "Prefabs";
					return true;
				}
			}

            return false;
		}

		private static bool EnsureFolderPathExists(string folderPath)
		{
			if (string.IsNullOrEmpty(folderPath))
			{
				return false;
			}

			folderPath = folderPath.Replace('\\', '/').Trim('/');
			if (!folderPath.StartsWith("Assets", System.StringComparison.OrdinalIgnoreCase))
			{
				folderPath = "Assets/" + folderPath;
			}

			if (AssetDatabase.IsValidFolder(folderPath))
			{
				return true;
			}

			string[] parts = folderPath.Split('/');
			if (parts.Length == 0 || !string.Equals(parts[0], "Assets", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string current = "Assets";
			for (int i = 1; i < parts.Length; i++)
			{
				string part = parts[i];
				if (string.IsNullOrEmpty(part))
				{
					continue;
				}

				string next = current + "/" + part;
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, part);
				}
				current = next;
			}

			return AssetDatabase.IsValidFolder(folderPath);
		}
	}

		[MenuItem("Assets/UMA/Add Race(s) to Selected Recipes", true)]
		private static bool AddRacesToSelectedRecipesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		private static List<UMAWardrobeRecipe> GetSelectedWardrobeRecipes()
		{
			var selected = Selection.GetFiltered(typeof(UMAWardrobeRecipe), SelectionMode.Assets);
			var recipes = new List<UMAWardrobeRecipe>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var r = selected[i] as UMAWardrobeRecipe;
				if (r != null)
				{
					recipes.Add(r);
				}
			}
			return recipes;
		}

		private static List<UMATextRecipe> GetSelectedTextRecipes()
		{
			var selected = Selection.GetFiltered(typeof(UMATextRecipe), SelectionMode.Assets);
			var recipes = new List<UMATextRecipe>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var recipe = selected[i] as UMATextRecipe;
				if (recipe != null)
				{
					recipes.Add(recipe);
				}
			}
			return recipes;
		}

		private static string GetAssetFolderPathFromAbsolutePath(string absoluteFolderPath)
		{
			if (string.IsNullOrEmpty(absoluteFolderPath))
			{
				return string.Empty;
			}

			string normalizedAssetsPath = Application.dataPath.Replace('\\', '/');
			string normalizedFolderPath = absoluteFolderPath.Replace('\\', '/');
			if (!normalizedFolderPath.StartsWith(normalizedAssetsPath, System.StringComparison.OrdinalIgnoreCase))
			{
				return string.Empty;
			}

			if (string.Equals(normalizedFolderPath, normalizedAssetsPath, System.StringComparison.OrdinalIgnoreCase))
			{
				return "Assets";
			}

			return "Assets" + normalizedFolderPath.Substring(normalizedAssetsPath.Length);
		}

		private static void CopyOverlayTexturesForRecipes(List<UMATextRecipe> recipes, string destFolderPath, string dialogTitle)
		{
			var textures = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int i = 0; i < recipes.Count; i++)
				{
					var recipe = recipes[i];
					if (recipe == null)
					{
						continue;
					}

					EditorUtility.DisplayProgressBar(dialogTitle, "Scanning recipes...", Mathf.Clamp01((float)i / Mathf.Max(1, recipes.Count)));
					var umaRecipe = new UMA.UMAData.UMARecipe();
					recipe.Load(umaRecipe, true);
					if (umaRecipe.slotDataList == null)
					{
						continue;
					}

					for (int s = 0; s < umaRecipe.slotDataList.Length; s++)
					{
						var slot = umaRecipe.slotDataList[s];
						if (slot == null)
						{
							continue;
						}

						for (int o = 0; o < slot.OverlayCount; o++)
						{
							var overlay = slot.GetOverlay(o);
							if (overlay == null || overlay.asset == null)
							{
								continue;
							}

							var overlayAsset = overlay.asset;
							if (overlayAsset.textureList != null)
							{
								for (int t = 0; t < overlayAsset.textureList.Length; t++)
								{
									var tex = overlayAsset.textureList[t];
									if (tex == null)
									{
										continue;
									}

									string srcPath = AssetDatabase.GetAssetPath(tex);
									if (!string.IsNullOrEmpty(srcPath))
									{
										textures.Add(srcPath);
									}
								}
							}

							if (overlayAsset.alphaMask != null)
							{
								string alphaPath = AssetDatabase.GetAssetPath(overlayAsset.alphaMask);
								if (!string.IsNullOrEmpty(alphaPath))
								{
									textures.Add(alphaPath);
								}
							}
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog(dialogTitle, "No textures were found in overlays for the selected recipes.", "OK");
				return;
			}

			int copied = 0;
			int skippedDuplicates = 0;
			int total = textures.Count;
			int index = 0;
			try
			{
				foreach (string srcPath in textures)
				{
					index++;
					EditorUtility.DisplayProgressBar(dialogTitle, "Copying textures...", Mathf.Clamp01((float)index / Mathf.Max(1, total)));
					if (string.IsNullOrEmpty(srcPath))
					{
						continue;
					}

					string fileName = Path.GetFileName(srcPath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					string destPath = destFolderPath + "/" + fileName;
					if (string.Equals(srcPath, destPath, System.StringComparison.OrdinalIgnoreCase))
					{
						skippedDuplicates++;
						continue;
					}

					if (File.Exists(destPath) || AssetDatabase.LoadAssetAtPath<Texture>(destPath) != null)
					{
						skippedDuplicates++;
						continue;
					}

					if (AssetDatabase.CopyAsset(srcPath, destPath))
					{
						copied++;
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(dialogTitle, "Copied texture asset(s): " + copied + "\nIgnored duplicates: " + skippedDuplicates + "\nDestination: " + destFolderPath, "OK");
		}

		private static List<UMA.OverlayDataAsset> GetSelectedOverlays()
		{
			var selected = Selection.GetFiltered(typeof(UMA.OverlayDataAsset), SelectionMode.Assets);
			var overlays = new List<UMA.OverlayDataAsset>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var o = selected[i] as UMA.OverlayDataAsset;
				if (o != null)
				{
					overlays.Add(o);
				}
			}
			return overlays;
		}

		private static List<UMA.SlotDataAsset> GetSelectedSlots()
		{
			var selected = Selection.GetFiltered(typeof(UMA.SlotDataAsset), SelectionMode.Assets);
			var slots = new List<UMA.SlotDataAsset>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var s = selected[i] as UMA.SlotDataAsset;
				if (s != null)
				{
					slots.Add(s);
				}
			}
			return slots;
		}

		private static List<Texture2D> GetSelectedTextures()
		{
			var selected = Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets);
			var textures = new List<Texture2D>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var tex = selected[i] as Texture2D;
				if (tex != null)
				{
					textures.Add(tex);
				}
			}
			return textures;
		}

		private static List<Material> GetSelectedMaterials()
		{
			var selected = Selection.GetFiltered(typeof(Material), SelectionMode.Assets);
			var materials = new List<Material>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var mat = selected[i] as Material;
				if (mat != null)
				{
					materials.Add(mat);
				}
			}
			return materials;
		}

		private static UMAMaterial.MaterialChannel[] BuildChannelsForMaterial(Material material)
		{
			if (material == null)
			{
				return new UMAMaterial.MaterialChannel[0];
			}
			var shader = material.shader;
			if (shader == null)
			{
				return new UMAMaterial.MaterialChannel[0];
			}

			var channels = new List<UMAMaterial.MaterialChannel>();
          var propertyNames = new List<string>();
			var textureProperties = material.GetTexturePropertyNames();
			if (textureProperties != null && textureProperties.Length > 0)
			{
				for (int i = 0; i < textureProperties.Length; i++)
				{
					if (!string.IsNullOrEmpty(textureProperties[i]))
					{
						propertyNames.Add(textureProperties[i]);
					}
				}
			}
			else
			{
				int count = shader.GetPropertyCount();
				for (int i = 0; i < count; i++)
				{
					if (shader.GetPropertyType(i) != ShaderPropertyType.Texture)
					{
						continue;
					}
					string propName = shader.GetPropertyName(i);
					if (!string.IsNullOrEmpty(propName))
					{
						propertyNames.Add(propName);
					}
				}
			}

			var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < propertyNames.Count; i++)
			{
				string propName = propertyNames[i];
				if (string.IsNullOrEmpty(propName))
				{
					continue;
				}
				if (propName.StartsWith("unity", System.StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (seen.Contains(propName))
				{
					continue;
				}
				seen.Add(propName);
				if (!material.HasProperty(propName))
				{
					continue;
				}

				UMAMaterial.ChannelType channelType = UMAMaterial.ChannelType.Texture;
				if (propName.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
					propName.IndexOf("bump", System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					channelType = UMAMaterial.ChannelType.NormalMap;
				}

				var channel = new UMAMaterial.MaterialChannel();
				channel.channelType = channelType;
				channel.textureFormat = RenderTextureFormat.ARGB32;
				channel.materialPropertyName = propName;
				channel.sourceTextureName = propName;
				channel.Compression = UMAMaterial.CompressionSettings.None;
				channel.DownSample = 1;
				channel.ConvertRenderTexture = false;
				channel.NonShaderTexture = false;

				channels.Add(channel);
			}

			return channels.ToArray();
		}

		internal class UmaConvertTexturesToPngWindow : EditorWindow
		{
			private class TextureEntry
			{
				public Texture2D Texture;
				public string AssetPath;
				public bool Selected;
				public long BeforeBytes;
				public long AfterBytes;
			}

			private readonly List<TextureEntry> _entries = new List<TextureEntry>();
			private readonly List<string> _log = new List<string>();
			private Vector2 _leftScroll;
			private Vector2 _rightScroll;
			private bool _isRunning;
			private System.Collections.IEnumerator _convertRoutine;
			private long _beforeTotalBytes;
			private long _afterTotalBytes;
			private bool _overwriteExistingPng = true;
			private bool _keepOriginalFiles;
			private bool _replaceInIndexedOverlays;

			public static void Open(List<Texture2D> textures)
			{
				var window = GetWindow<UmaConvertTexturesToPngWindow>(true, "Convert selected textures to PNG", true);
				window.minSize = new Vector2(820f, 360f);
				window._entries.Clear();
				window._log.Clear();
				window._beforeTotalBytes = 0;
				window._afterTotalBytes = 0;
				window._isRunning = false;
				window._convertRoutine = null;
				if (textures != null)
				{
					window.LoadTextures(textures);
				}
				window.ShowUtility();
				window.Focus();
			}

			private void LoadTextures(List<Texture2D> textures)
			{
				for (int i = 0; i < textures.Count; i++)
				{
					var tex = textures[i];
					if (tex == null)
					{
						continue;
					}

					string path = AssetDatabase.GetAssetPath(tex);
					long size = GetFileSize(path);
					_entries.Add(new TextureEntry
					{
						Texture = tex,
						AssetPath = path,
						Selected = true,
						BeforeBytes = size,
						AfterBytes = size
					});
				}
				RecalculateTotals();
			}

			private void OnDisable()
			{
				StopConversion();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField("Convert selected textures to PNG", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Converts selected Texture2D assets to PNG using Unity's PNG encoder (RGBA32).", MessageType.Info);
				EditorGUILayout.Space(6);

				EditorGUILayout.LabelField("PNG Options", EditorStyles.boldLabel);
				using (new EditorGUI.DisabledScope(_isRunning))
				{
					_overwriteExistingPng = EditorGUILayout.ToggleLeft("Overwrite existing .png", _overwriteExistingPng);
					_keepOriginalFiles = EditorGUILayout.ToggleLeft("Keep original file (create *_converted.png)", _keepOriginalFiles);
					_replaceInIndexedOverlays = EditorGUILayout.ToggleLeft("Replace references in indexed overlays", _replaceInIndexedOverlays);
					EditorGUILayout.HelpBox("Unity's built-in encoder does not expose PNG options like interlacing or compression level.", MessageType.None);
				}
				EditorGUILayout.Space(6);

				EditorGUILayout.BeginHorizontal();
				DrawTextureList();
				GUILayout.Space(10);
				DrawLogList();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(6);
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				using (new EditorGUI.DisabledScope(_isRunning || _entries.Count == 0))
				{
					if (GUILayout.Button("Convert selected textures to PNG", GUILayout.Width(260), GUILayout.Height(28)))
					{
						StartConversion();
					}
				}
				using (new EditorGUI.DisabledScope(!_isRunning))
				{
					if (GUILayout.Button("Stop", GUILayout.Width(100), GUILayout.Height(28)))
					{
						StopConversion();
					}
				}
				if (GUILayout.Button("Close", GUILayout.Width(100), GUILayout.Height(28)))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}

			private void DrawTextureList()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.58f));
				EditorGUILayout.LabelField("Selected Texture2D", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(70)))
				{
					SetAllSelections(true);
					RecalculateTotals();
				}
				if (GUILayout.Button("None", GUILayout.Width(70)))
				{
					SetAllSelections(false);
					RecalculateTotals();
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space(4);

				_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null)
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					bool newSelected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18));
					if (newSelected != entry.Selected)
					{
						entry.Selected = newSelected;
						RecalculateTotals();
					}
					EditorGUILayout.ObjectField(entry.Texture, typeof(Texture2D), false);
					GUILayout.Label(FormatBytes(entry.AfterBytes), GUILayout.Width(90));
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(4);
				EditorGUILayout.LabelField("Before total (selected): " + FormatBytes(_beforeTotalBytes));
				EditorGUILayout.LabelField("After total (selected): " + FormatBytes(_afterTotalBytes));
				EditorGUILayout.EndVertical();
			}

			private void DrawLogList()
			{
				EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				EditorGUILayout.LabelField("Conversion Log", EditorStyles.boldLabel);
				_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _log.Count; i++)
				{
					EditorGUILayout.LabelField(_log[i], EditorStyles.wordWrappedLabel);
				}
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

			private void SetAllSelections(bool value)
			{
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry != null)
					{
						entry.Selected = value;
					}
				}
			}

			private void StartConversion()
			{
				if (_isRunning)
				{
					return;
				}
				_log.Clear();
				RecalculateTotals();
				_convertRoutine = ConvertTextures();
				_isRunning = true;
				EditorApplication.update += UpdateConversion;
			}

			private void StopConversion()
			{
				if (!_isRunning)
				{
					return;
				}
				EditorApplication.update -= UpdateConversion;
				_convertRoutine = null;
				_isRunning = false;
			}

			private void UpdateConversion()
			{
				if (_convertRoutine == null)
				{
					StopConversion();
					return;
				}
				if (!_convertRoutine.MoveNext())
				{
					StopConversion();
					RecalculateTotals();
					return;
				}
				Repaint();
			}

			private System.Collections.IEnumerator ConvertTextures()
			{
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null || !entry.Selected || entry.Texture == null)
					{
						continue;
					}

					string srcPath = entry.AssetPath;
					if (string.IsNullOrEmpty(srcPath))
					{
						LogLine("Skipped: Missing asset path for " + entry.Texture.name);
						yield return null;
						continue;
					}

					string fileName = Path.GetFileNameWithoutExtension(srcPath);
					string folder = Path.GetDirectoryName(srcPath) ?? string.Empty;
					string destPath;
					if (_keepOriginalFiles)
					{
						destPath = Path.Combine(folder, fileName + "_converted.png");
					}
					else
					{
						destPath = Path.Combine(folder, fileName + ".png");
					}
					destPath = CustomAssetUtility.UnityFriendlyPath(destPath);
					if (!_overwriteExistingPng && File.Exists(destPath))
					{
						LogLine("Skipped (exists): " + destPath);
						yield return null;
						continue;
					}

					LogLine("Starting: " + srcPath);
					yield return null;

					Texture2D readable = null;
					Texture2D rgba32 = null;
					byte[] data = null;
					string error = null;
					try
					{
						readable = GetReadableTexture(entry.Texture, false);
						if (readable == null)
						{
							error = "could not read";
						}
						else
						{
							rgba32 = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false, false);
							rgba32.SetPixels32(readable.GetPixels32());
							rgba32.Apply(false, false);
							data = rgba32.EncodeToPNG();
							if (data == null || data.Length == 0)
							{
								error = "PNG encode returned empty data";
							}
						}
					}
					catch (System.Exception ex)
					{
						error = ex.Message;
					}
					finally
					{
						if (readable != null)
						{
							UnityEngine.Object.DestroyImmediate(readable);
						}
						if (rgba32 != null)
						{
							UnityEngine.Object.DestroyImmediate(rgba32);
						}
					}

					if (!string.IsNullOrEmpty(error))
					{
						LogLine("Failed: " + srcPath + " (" + error + ")");
						yield return null;
						continue;
					}

					File.WriteAllBytes(destPath, data);
					string absDestPath = GetAbsolutePathFromAssetPath(destPath);
					if (!string.IsNullOrEmpty(absDestPath))
					{
						File.WriteAllBytes(absDestPath, data);
					}
					else
					{
						File.WriteAllBytes(destPath, data);
					}
					AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
					var pngTex = AssetDatabase.LoadAssetAtPath<Texture2D>(destPath);
					if (pngTex != null)
					{
						if (_replaceInIndexedOverlays)
						{
							ReplaceInIndexedOverlays(entry.Texture, pngTex);
						}
						entry.Texture = pngTex;
						entry.AssetPath = destPath;
					}
					entry.AfterBytes = GetFileSize(destPath);
					LogLine("Done: " + destPath);
					yield return null;
				}

				RecalculateTotals();
			}

			private void ReplaceInIndexedOverlays(Texture2D oldTexture, Texture2D newTexture)
			{
				if (oldTexture == null || newTexture == null)
				{
					return;
				}

				var idx = UMA.UMAAssetIndexer.Instance;
				if (idx == null)
				{
					LogLine("Overlay relink skipped: UMAAssetIndexer not ready.");
					return;
				}

				var overlays = idx.GetAssetItems<UMA.OverlayDataAsset>();
				if (overlays == null || overlays.Count == 0)
				{
					return;
				}

				int updated = 0;
				for (int i = 0; i < overlays.Count; i++)
				{
					var ai = overlays[i];
					if (ai == null)
					{
						continue;
					}

					var overlay = ai.Item as UMA.OverlayDataAsset;
					if (overlay == null)
					{
						continue;
					}

					var list = overlay.textureList;
					if (list == null || list.Length == 0)
					{
						continue;
					}

					bool changed = false;
					for (int t = 0; t < list.Length; t++)
					{
						if (list[t] == oldTexture)
						{
							list[t] = newTexture;
							if (overlay.textureNames != null && t < overlay.textureNames.Length)
							{
								overlay.textureNames[t] = newTexture.name;
							}
							changed = true;
						}
					}

					if (overlay.alphaMask == oldTexture)
					{
						overlay.alphaMask = newTexture;
						changed = true;
					}

					if (!changed)
					{
						continue;
					}

					Undo.RecordObject(overlay, "Replace overlay texture");
					overlay.textureList = list;

					EditorUtility.SetDirty(overlay);
					updated++;
				}

				if (updated > 0)
				{
					AssetDatabase.SaveAssets();
					LogLine("Overlay relink updated overlays: " + updated);
				}
			}

			private void LogLine(string message)
			{
				_log.Add(message);
				_rightScroll.y = float.MaxValue;
			}

			private void RecalculateTotals()
			{
				long beforeTotalSelected = 0;
				long afterTotalSelected = 0;
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null || !entry.Selected)
					{
						continue;
					}
					beforeTotalSelected += entry.BeforeBytes;
					afterTotalSelected += entry.AfterBytes;
				}
				_beforeTotalBytes = beforeTotalSelected;
				_afterTotalBytes = afterTotalSelected;
			}

			private static long GetFileSize(string assetPath)
			{
				if (string.IsNullOrEmpty(assetPath))
				{
					return 0;
				}
				try
				{
					string absPath = GetAbsolutePathFromAssetPath(assetPath);
					if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
					{
						return new FileInfo(absPath).Length;
					}
					if (File.Exists(assetPath))
					{
						return new FileInfo(assetPath).Length;
					}
				}
				catch
				{
				}
				return 0;
			}

			private static string GetAbsolutePathFromAssetPath(string assetPath)
			{
				if (string.IsNullOrEmpty(assetPath))
				{
					return null;
				}
				if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				string projectRoot = Path.GetDirectoryName(Application.dataPath);
				if (string.IsNullOrEmpty(projectRoot))
				{
					return null;
				}
				string relative = assetPath.Substring("Assets/".Length);
				return Path.Combine(projectRoot, "Assets", relative);
			}

			private static string FormatBytes(long bytes)
			{
				if (bytes < 1024)
				{
					return bytes + " B";
				}
				float kb = bytes / 1024f;
				if (kb < 1024f)
				{
					return kb.ToString("F1") + " KB";
				}
				float mb = kb / 1024f;
				return mb.ToString("F1") + " MB";
			}
		}

	internal class UmaExamineOverlaysWindow : EditorWindow
	{
		private readonly List<UMA.OverlayDataAsset> _overlays = new List<UMA.OverlayDataAsset>();
		private readonly List<UMA.OverlayDataAsset> _filteredOverlays = new List<UMA.OverlayDataAsset>();
		private UMA.OverlayDataAsset _selectedOverlay;
		private Vector2 _leftScroll;
		private Vector2 _rightScroll;
     private DefaultAsset _textureFolder;
		private string _textureFolderPath;
		private bool _includeSubfolders;
		private bool _skipWhenSameAsset = true;
     private UMAMaterial _utilitiesTargetMaterial;
		private static readonly GUIContent _completeLabel = new GUIContent("Complete");
		private static readonly GUIContent _missingTexturesLabel = new GUIContent("missing textures");
		private static readonly GUIContent _missingTexturesAndOvlLabel = new GUIContent("missing textures and UMAT");
		private static readonly GUIContent _missingOvlLabel = new GUIContent("missing UMAMaterial");
		private enum OverlayFilter { All, Complete, Incomplete }
		private OverlayFilter _filter = OverlayFilter.All;

		public static void Open(List<UMA.OverlayDataAsset> overlays)
		{
			var window = GetWindow<UmaExamineOverlaysWindow>(false, "Examine Overlays", true);
			window.minSize = new Vector2(860f, 420f);
			window._overlays.Clear();
			if (overlays != null)
			{
				window._overlays.AddRange(overlays);
			}
			window.SortOverlays();
			window._selectedOverlay = window._overlays.Count > 0 ? window._overlays[0] : null;
			window.Show();
			window.Focus();
		}

		private void RefreshFromSelection()
		{
			var selected = Selection.GetFiltered(typeof(UMA.OverlayDataAsset), SelectionMode.Assets);
			_overlays.Clear();
			for (int i = 0; i < selected.Length; i++)
			{
				var o = selected[i] as UMA.OverlayDataAsset;
				if (o != null)
				{
					_overlays.Add(o);
				}
			}
			SortOverlays();
			if (_selectedOverlay != null && !_overlays.Contains(_selectedOverlay))
			{
				_selectedOverlay = null;
			}
			RebuildFilteredOverlays(_selectedOverlay);
			Repaint();
		}

		private void OnSelectionChange()
		{
			// Intentionally no-op: we only refresh the window contents when the user presses Refresh.
		}

		private void SortOverlays()
		{
			_overlays.Sort((a, b) => string.Compare(a != null ? a.name : "", b != null ? b.name : "", System.StringComparison.OrdinalIgnoreCase));
		}

		private void RebuildFilteredOverlays(UMA.OverlayDataAsset keepSelected)
		{
			_filteredOverlays.Clear();
			for (int i = 0; i < _overlays.Count; i++)
			{
				var overlay = _overlays[i];
				if (overlay == null)
				{
					continue;
				}
              bool isComplete = GetOverlayStatus(overlay) == OverlayStatus.Complete;
				switch (_filter)
				{
					case OverlayFilter.Complete:
						if (!isComplete) continue;
						break;
					case OverlayFilter.Incomplete:
						if (isComplete) continue;
						break;
				}
				_filteredOverlays.Add(overlay);
			}

			if (_filteredOverlays.Count == 0)
			{
				_selectedOverlay = null;
				return;
			}

			if (keepSelected != null)
			{
				if (_filteredOverlays.Contains(keepSelected))
				{
					_selectedOverlay = keepSelected;
					return;
				}
			}
			_selectedOverlay = _filteredOverlays[0];
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("Examine Overlays", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
			{
				RefreshFromSelection();
			}
			EditorGUILayout.EndHorizontal();

			if (_overlays.Count == 0)
			{
				EditorGUILayout.HelpBox("Select one or more OverlayDataAsset assets in the Project window.", MessageType.Info);
				return;
			}

			RebuildFilteredOverlays(_selectedOverlay);

            DrawUtilitiesPanel();
          DrawRelinkPanel();

			EditorGUILayout.BeginHorizontal();
			DrawOverlayList();
			GUILayout.Space(10);
			DrawOverlayDetails();
			EditorGUILayout.EndHorizontal();
		}

		private void DrawUtilitiesPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
			EditorGUILayout.BeginHorizontal();
			_utilitiesTargetMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _utilitiesTargetMaterial, typeof(UMAMaterial), false);
           using (new EditorGUI.DisabledScope(_utilitiesTargetMaterial == null || _overlays.Count == 0))
			{
				if (GUILayout.Button("Assign UMAMaterial to selected", GUILayout.Width(220), GUILayout.Height(22)))
				{
					AssignMaterialToSelectedOverlays();
				}
               if (GUILayout.Button("Assign UMAMaterial to ALL", GUILayout.Width(200), GUILayout.Height(22)))
				{
					AssignMaterialToAllOverlaysInList();
				}
			}
            using (new EditorGUI.DisabledScope(_overlays.Count == 0))
			{
				if (GUILayout.Button("Sync Material Texture Channels", GUILayout.Width(230), GUILayout.Height(22)))
				{
					SyncMaterialTextureChannels();
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void AssignMaterialToSelectedOverlays()
		{
			if (_utilitiesTargetMaterial == null)
			{
				EditorUtility.DisplayDialog("Assign UMAMaterial", "Select a UMAMaterial.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _overlays.Count; i++)
			{
				var overlay = _overlays[i];
				if (overlay == null)
				{
					continue;
				}

				if (overlay.material == _utilitiesTargetMaterial)
				{
                    Undo.RecordObject(overlay, "Sync Overlay Channels");
				   if (SyncOverlayChannelsToMaterial(overlay, _utilitiesTargetMaterial))
					{
						EditorUtility.SetDirty(overlay);
						updated++;
					}
					continue;
				}

				Undo.RecordObject(overlay, "Assign Overlay UMAMaterial");
				overlay.material = _utilitiesTargetMaterial;
				overlay.materialName = _utilitiesTargetMaterial.name;
                SyncOverlayChannelsToMaterial(overlay, _utilitiesTargetMaterial);
				EditorUtility.SetDirty(overlay);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Assign UMAMaterial", "Updated overlays: " + updated, "OK");
		}

		private void AssignMaterialToAllOverlaysInList()
		{
			if (_utilitiesTargetMaterial == null)
			{
				EditorUtility.DisplayDialog("Assign UMAMaterial", "Select a UMAMaterial.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _filteredOverlays.Count; i++)
			{
				var overlay = _filteredOverlays[i];
				if (overlay == null)
				{
					continue;
				}

				if (overlay.material == _utilitiesTargetMaterial)
				{
                    Undo.RecordObject(overlay, "Sync Overlay Channels");
				   if (SyncOverlayChannelsToMaterial(overlay, _utilitiesTargetMaterial))
					{
						EditorUtility.SetDirty(overlay);
						updated++;
					}
					continue;
				}

				Undo.RecordObject(overlay, "Assign Overlay UMAMaterial");
				overlay.material = _utilitiesTargetMaterial;
				overlay.materialName = _utilitiesTargetMaterial.name;
                SyncOverlayChannelsToMaterial(overlay, _utilitiesTargetMaterial);
				EditorUtility.SetDirty(overlay);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Assign UMAMaterial", "Updated overlays in list: " + updated, "OK");
		}

		private void SyncMaterialTextureChannels()
		{
			int updated = 0;
			for (int i = 0; i < _overlays.Count; i++)
			{
				var overlay = _overlays[i];
				if (overlay == null || overlay.material == null)
				{
					continue;
				}

				Undo.RecordObject(overlay, "Sync Overlay Channels");
				if (SyncOverlayChannelsToMaterial(overlay, overlay.material))
				{
					EditorUtility.SetDirty(overlay);
					updated++;
				}
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Sync Material Texture Channels", "Updated overlays: " + updated, "OK");
		}

		private static bool SyncOverlayChannelsToMaterial(UMA.OverlayDataAsset overlay, UMAMaterial targetMaterial)
		{
			if (overlay == null || targetMaterial == null)
			{
				return false;
			}

			int channelCount = 0;
			if (targetMaterial.channels != null)
			{
				channelCount = targetMaterial.channels.Length;
			}

			Texture[] oldTextures = overlay.textureList ?? new Texture[0];
			string[] oldTextureNames = overlay.textureNames ?? new string[0];
			UMA.OverlayDataAsset.OverlayBlend[] oldBlends = overlay.overlayBlend ?? new UMA.OverlayDataAsset.OverlayBlend[0];

			bool changed = false;

			if (oldTextures.Length != channelCount)
			{
				Texture[] newTextures = new Texture[channelCount];
				int copyCount = Mathf.Min(oldTextures.Length, newTextures.Length);
				for (int i = 0; i < copyCount; i++)
				{
					newTextures[i] = oldTextures[i];
				}
				overlay.textureList = newTextures;
				changed = true;
			}

			if (oldTextureNames.Length != channelCount)
			{
				string[] newTextureNames = new string[channelCount];
				int copyCount = Mathf.Min(oldTextureNames.Length, newTextureNames.Length);
				for (int i = 0; i < copyCount; i++)
				{
					newTextureNames[i] = oldTextureNames[i];
				}
				overlay.textureNames = newTextureNames;
				changed = true;
			}

			if (oldBlends.Length != channelCount)
			{
				UMA.OverlayDataAsset.OverlayBlend[] newBlends = new UMA.OverlayDataAsset.OverlayBlend[channelCount];
				int copyCount = Mathf.Min(oldBlends.Length, newBlends.Length);
				for (int i = 0; i < copyCount; i++)
				{
					newBlends[i] = oldBlends[i];
				}
				for (int i = copyCount; i < newBlends.Length; i++)
				{
					newBlends[i] = UMA.OverlayDataAsset.OverlayBlend.Normal;
				}
				overlay.overlayBlend = newBlends;
				changed = true;
			}

			Texture[] syncedTextures = overlay.textureList ?? new Texture[0];
			string[] syncedTextureNames = overlay.textureNames ?? new string[0];
			int syncCount = Mathf.Min(syncedTextures.Length, syncedTextureNames.Length);
			for (int i = 0; i < syncCount; i++)
			{
				Texture tex = syncedTextures[i];
				string desiredName = tex != null ? tex.name : string.Empty;
				if (syncedTextureNames[i] != desiredName)
				{
					syncedTextureNames[i] = desiredName;
					changed = true;
				}
			}

			return changed;
		}

		private void DrawRelinkPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("Relink Textures", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Replaces textures on the selected OverlayDataAsset list by name, using textures found in the specified folder.", MessageType.Info);
			EditorGUI.BeginChangeCheck();
			_textureFolder = (DefaultAsset)EditorGUILayout.ObjectField("Texture Folder", _textureFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_textureFolderPath = _textureFolder != null ? AssetDatabase.GetAssetPath(_textureFolder) : string.Empty;
				if (!string.IsNullOrEmpty(_textureFolderPath) && !AssetDatabase.IsValidFolder(_textureFolderPath))
				{
					_textureFolder = null;
					_textureFolderPath = string.Empty;
				}
			}
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Path", _textureFolderPath ?? string.Empty);
			}
			_includeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", _includeSubfolders);
			_skipWhenSameAsset = EditorGUILayout.ToggleLeft("Skip if already same asset", _skipWhenSameAsset);

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_textureFolderPath) || _overlays.Count == 0))
			{
				if (GUILayout.Button("Replace textures in selected overlays", GUILayout.Width(260), GUILayout.Height(24)))
				{
					ReplaceTexturesInSelectedOverlays();
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void ReplaceTexturesInSelectedOverlays()
		{
			if (string.IsNullOrEmpty(_textureFolderPath))
			{
				EditorUtility.DisplayDialog("Relink Textures", "Select a valid texture folder.", "OK");
				return;
			}
			if (_overlays.Count == 0)
			{
				return;
			}

			var nameToTexture = BuildTextureLookup(_textureFolderPath, _includeSubfolders);
			if (nameToTexture.Count == 0)
			{
				EditorUtility.DisplayDialog("Relink Textures", "No textures found in folder: " + _textureFolderPath, "OK");
				return;
			}

            int overlaysUpdated = 0;
			int texturesReplaced = 0;
			int texturesMissing = 0;
         int alphaMasksReplaced = 0;
         var missingNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int i = 0; i < _overlays.Count; i++)
				{
					var overlay = _overlays[i];
					if (overlay == null) continue;

					var list = overlay.textureList;
					if (list == null || list.Length == 0) continue;

                    bool anyChanged = false;
					Undo.RecordObject(overlay, "Relink overlay textures");

					if (overlay.alphaMask != null)
					{
						string alphaBaseName = GetTextureBaseName(overlay.alphaMask);
						if (!string.IsNullOrEmpty(alphaBaseName) && nameToTexture.TryGetValue(alphaBaseName, out var alphaReplacement) && alphaReplacement != null)
						{
							if (!_skipWhenSameAsset || alphaReplacement != overlay.alphaMask)
							{
								overlay.alphaMask = alphaReplacement;
								alphaMasksReplaced++;
								anyChanged = true;
							}
						}
						else
						{
							texturesMissing++;
                           if (!string.IsNullOrEmpty(alphaBaseName))
							{
								missingNames.Add(alphaBaseName);
							}
						}
					}

					for (int t = 0; t < list.Length; t++)
					{
						var current = list[t];
						if (current == null) continue;

						string baseName = GetTextureBaseName(current);
						if (string.IsNullOrEmpty(baseName)) continue;

						if (!nameToTexture.TryGetValue(baseName, out var replacement) || replacement == null)
						{
							texturesMissing++;
                           missingNames.Add(baseName);
							continue;
						}

						if (_skipWhenSameAsset && replacement == current)
						{
							continue;
						}

						list[t] = replacement;
						texturesReplaced++;
						anyChanged = true;
						if (overlay.textureNames != null && t < overlay.textureNames.Length)
						{
							overlay.textureNames[t] = replacement.name;
						}
					}

					if (!anyChanged) continue;
					overlay.textureList = list;
					EditorUtility.SetDirty(overlay);
					overlaysUpdated++;
				}
			}
			finally
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"Relink Textures",
				"Overlays updated: " + overlaysUpdated +
				"\nTextures replaced: " + texturesReplaced +
             "\nAlpha masks replaced: " + alphaMasksReplaced +
				"\nTextures not found: " + texturesMissing,
				"OK");

			if (missingNames.Count > 0)
			{
				var list = new List<string>(missingNames);
				list.Sort(System.StringComparer.OrdinalIgnoreCase);
				string details = string.Join("\n", list);
				EditorUtility.DisplayDialog("Textures not found", details, "OK");
			}
		}

        private static Dictionary<string, Texture> BuildTextureLookup(string folderPath, bool includeSubfolders)
		{
			var result = new Dictionary<string, Texture>(System.StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folderPath)) return result;
			folderPath = folderPath.Replace('\\', '/');

			string[] search = new[] { folderPath };
          const string filter = "t:Texture";
			// `FindAssets` will search recursively within provided folder(s).
			string[] guids = AssetDatabase.FindAssets(filter, search);
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (string.IsNullOrEmpty(path)) continue;
				if (!includeSubfolders)
				{
                   string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
					if (!string.Equals(dir, folderPath, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
				}
             var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
				if (tex == null) continue;
				string key = Path.GetFileNameWithoutExtension(path);
				if (string.IsNullOrEmpty(key)) continue;
               if (result.TryGetValue(key, out var existing) && existing != null)
				{
					if (GetExtensionPriority(path) >= GetExtensionPriority(AssetDatabase.GetAssetPath(existing)))
					{
						continue;
					}
					result[key] = tex;
				}
				else
				{
					result[key] = tex;
				}
			}
			return result;
		}

		private static int GetExtensionPriority(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return int.MaxValue;
			string ext = Path.GetExtension(assetPath);
			if (string.IsNullOrEmpty(ext)) return int.MaxValue;
			ext = ext.TrimStart('.').ToLowerInvariant();
			switch (ext)
			{
				case "png": return 0;
				case "jpg":
				case "jpeg": return 1;
				case "tga": return 2;
				case "tif":
				case "tiff": return 3;
				default: return 10;
			}
		}

		private static string GetTextureBaseName(Texture texture)
		{
			if (texture == null) return null;
			string path = AssetDatabase.GetAssetPath(texture);
			if (!string.IsNullOrEmpty(path))
			{
				return Path.GetFileNameWithoutExtension(path);
			}
			// Fallback: if texture is generated/unassigned to disk, use object name.
			return texture.name;
		}

		private void DrawOverlayList()
		{
			EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.40f));
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Selected Overlays", EditorStyles.boldLabel);
			if (GUILayout.Button("Refresh", GUILayout.Width(70)))
			{
				RefreshFromSelection();
			}
			EditorGUILayout.EndHorizontal();
			var previouslySelected = _selectedOverlay;
			EditorGUI.BeginChangeCheck();
			string[] filterLabels = { "all", "complete", "incomplete" };
			_filter = (OverlayFilter)EditorGUILayout.Popup((int)_filter, filterLabels);
			if (EditorGUI.EndChangeCheck())
			{
				RebuildFilteredOverlays(previouslySelected);
				GUI.FocusControl(null);
			}
			EditorGUILayout.Space(2);
			_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < _filteredOverlays.Count; i++)
			{
				var overlay = _filteredOverlays[i];
				if (overlay == null)
				{
					continue;
				}

				EditorGUILayout.BeginHorizontal();
				bool selected = (overlay == _selectedOverlay);
				if (GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(18)) != selected)
				{
					_selectedOverlay = overlay;
					GUI.FocusControl(null);
				}
				var buttonStyle = selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
				if (GUILayout.Button(overlay.name, buttonStyle, GUILayout.ExpandWidth(true)))
				{
					_selectedOverlay = overlay;
					GUI.FocusControl(null);
				}
              GUILayout.Label(GetOverlayStatusLabel(overlay), GUILayout.Width(170));
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void DrawOverlayDetails()
		{
			EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			EditorGUILayout.LabelField("Overlay Textures", EditorStyles.boldLabel);

			if (_selectedOverlay == null)
			{
				EditorGUILayout.HelpBox("Select an overlay to view its textures.", MessageType.Info);
				EditorGUILayout.EndVertical();
				return;
			}

			var overlay = _selectedOverlay;
			if (overlay == null)
			{
				EditorGUILayout.HelpBox("Selected overlay is missing.", MessageType.Warning);
				EditorGUILayout.EndVertical();
				return;
			}

			EditorGUILayout.LabelField("Overlay", overlay.name);
			EditorGUILayout.Space(4);

			var mat = overlay.GetMaterial();

			var texList = overlay.textureList;
			int displayCount = texList != null ? texList.Length : 0;
			_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < displayCount; i++)
			{
				Texture current = texList[i];
				string paramName = "Texture " + i;
				if (mat != null && mat.channels != null && i < mat.channels.Length && !string.IsNullOrEmpty(mat.channels[i].materialPropertyName))
				{
					paramName = mat.channels[i].materialPropertyName;
				}
				string texName = current != null ? current.name : "<Not Set>";
				const float rowHeight = 128f;
				const float previewSize = 96f;

				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label(i.ToString(), GUILayout.Width(26));
				GUILayout.Label(paramName, EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				GUILayout.Label(texName, EditorStyles.miniLabel, GUILayout.Width(180));
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
				EditorGUI.BeginChangeCheck();
				var newTex = (Texture)EditorGUILayout.ObjectField(current, typeof(Texture), false, GUILayout.Width(previewSize), GUILayout.Height(previewSize));
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(overlay, "Set overlay texture");
					var list = overlay.textureList;
					if (list != null && i < list.Length)
					{
						list[i] = newTex;
						overlay.textureList = list;
						EditorUtility.SetDirty(overlay);
						AssetDatabase.SaveAssets();
					}
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

        private enum OverlayStatus
		{
			Complete = 0,
			MissingTextures = 1,
			MissingTexturesAndOvl = 2,
			MissingOvl = 3
		}

		private static OverlayStatus GetOverlayStatus(UMA.OverlayDataAsset overlay)
		{
			if (overlay == null)
			{
               return OverlayStatus.MissingTexturesAndOvl;
			}

			bool missingOvl = overlay.material == null;
			bool missingTextures = false;
			var list = overlay.textureList;
			if (list == null || list.Length == 0)
			{
               missingTextures = true;
			}
           else
			{
                for (int i = 0; i < list.Length; i++)
				{
                   if (list[i] == null)
					{
						missingTextures = true;
						break;
					}
				}
			}

			if (missingTextures && missingOvl)
			{
				return OverlayStatus.MissingTexturesAndOvl;
			}
			if (missingTextures)
			{
				return OverlayStatus.MissingTextures;
			}
			if (missingOvl)
			{
				return OverlayStatus.MissingOvl;
			}
			return OverlayStatus.Complete;
		}

		private static GUIContent GetOverlayStatusLabel(UMA.OverlayDataAsset overlay)
		{
			switch (GetOverlayStatus(overlay))
			{
				case OverlayStatus.MissingTextures:
					return _missingTexturesLabel;
				case OverlayStatus.MissingTexturesAndOvl:
					return _missingTexturesAndOvlLabel;
				case OverlayStatus.MissingOvl:
					return _missingOvlLabel;
				default:
					return _completeLabel;
			}
		}


	}

	internal class ExamineWearables : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _recipes = new List<UMAWardrobeRecipe>();
		private bool[] _recipeSelected = new bool[0];
		private Vector2 _recipesScroll;

		private readonly List<string> _slots = new List<string>();
		private int _selectedSlotIndex = -1;
		private Vector2 _slotsScroll;

		private UMAMaterial _targetMaterial;
		private string _matchText = "";
      private UMA.OverlayDataAsset _overlayToAdd;
		private bool _useSharedColorForAddedOverlay;
		private string _sharedColorName = "NewSharedColor";
		private int _sharedColorChannelCount = 3;
		private enum MatchMode
		{
			Contains = 0,
			StartsWith = 1,
			EndsWith = 2,
		}
		private MatchMode _matchMode = MatchMode.Contains;
		private static GUIContent _inspectContent;
		private static GUIContent InspectContent
		{
			get
			{
				if (_inspectContent == null)
				{
					var icon = EditorGUIUtility.FindTexture("ViewToolOrbit");
					_inspectContent = new GUIContent("", icon, "Inspect");
				}
				return _inspectContent;
			}
		}

		public static void Open(List<UMAWardrobeRecipe> recipes)
		{
			var window = GetWindow<ExamineWearables>(false, "Examine Wearables", true);
			window.minSize = new Vector2(700f, 420f);
			window._recipes.Clear();
			if (recipes != null)
			{
				window._recipes.AddRange(recipes);
			}
			window._recipeSelected = new bool[window._recipes.Count];
			window.RebuildSlots();
			window.Show();
			window.Focus();
		}

		private void OnSelectionChange()
		{
			// Intentionally no-op: we only refresh the window contents when the user presses Refresh.
		}

		private void RebuildSlots()
		{
			_slots.Clear();
			var slotSet = new HashSet<string>();

			var idx = UMAAssetIndexer.Instance;
			if (idx == null)
			{
				_selectedSlotIndex = -1;
				return;
			}

			for (int i = 0; i < _recipes.Count; i++)
			{
				var recipe = _recipes[i];
				if (recipe == null || recipe.compatibleRaces == null)
				{
					continue;
				}

				for (int r = 0; r < recipe.compatibleRaces.Count; r++)
				{
					string raceName = recipe.compatibleRaces[r];
					if (string.IsNullOrEmpty(raceName))
					{
						continue;
					}

					RaceData race = null;
					try
					{
						race = idx.GetAsset<RaceData>(raceName);
					}
					catch
					{
						race = null;
					}

					if (race == null || race.wardrobeSlots == null)
					{
						continue;
					}

					for (int s = 0; s < race.wardrobeSlots.Count; s++)
					{
						string slot = race.wardrobeSlots[s];
						if (!string.IsNullOrEmpty(slot))
						{
							slotSet.Add(slot);
						}
					}
				}
			}

			_slots.AddRange(slotSet);
			_slots.Sort(System.StringComparer.OrdinalIgnoreCase);
			if (_selectedSlotIndex >= _slots.Count)
			{
				_selectedSlotIndex = -1;
			}
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("Examine Wearables", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
			{
				RebuildSlots();
			}
			EditorGUILayout.EndHorizontal();

			if (_recipes.Count == 0)
			{
				EditorGUILayout.HelpBox("Select one or more UMAWardrobeRecipe assets in the Project window.", MessageType.Info);
				EditorGUILayout.Space(8);
				if (GUILayout.Button("Close", GUILayout.Height(26)))
				{
					Close();
				}
				return;
			}

			EditorGUILayout.Space(6);
			DrawUtilitiesPanel();
			EditorGUILayout.BeginHorizontal();
			DrawRecipesColumn();
			GUILayout.Space(10);
			DrawSlotsColumn();
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(10);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(!HasAnyRecipeChecked() || _selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count))
			{
				if (GUILayout.Button("Assign", GUILayout.Width(120), GUILayout.Height(28)))
				{
					AssignSelectedSlot();
				}
			}
			if (GUILayout.Button("Close", GUILayout.Width(120), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawUtilitiesPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Set UMAMaterial on slots and overlays based on overlay texture[0] name matching.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
			_targetMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _targetMaterial, typeof(UMAMaterial), false);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			_matchMode = (MatchMode)EditorGUILayout.EnumPopup("Match Mode", _matchMode);
			_matchText = EditorGUILayout.TextField("Texture[0] Match Text", _matchText);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(_targetMaterial == null || string.IsNullOrEmpty(_matchText) || !HasAnyRecipeChecked()))
			{
				if (GUILayout.Button("Process", GUILayout.Width(140), GUILayout.Height(24)))
				{
					ProcessMaterialUpdates();
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(8);
			EditorGUILayout.LabelField("Add Overlay To First Slot", EditorStyles.boldLabel);
			_overlayToAdd = (UMA.OverlayDataAsset)EditorGUILayout.ObjectField("OverlayDataAsset", _overlayToAdd, typeof(UMA.OverlayDataAsset), false);
			_useSharedColorForAddedOverlay = EditorGUILayout.ToggleLeft("Use Shared Color", _useSharedColorForAddedOverlay);
			using (new EditorGUI.DisabledScope(!_useSharedColorForAddedOverlay))
			{
				_sharedColorName = EditorGUILayout.TextField("Shared Color Name", _sharedColorName);
				_sharedColorChannelCount = EditorGUILayout.IntField("Shared Color Channels", _sharedColorChannelCount);
			}

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(_overlayToAdd == null || !HasAnyRecipeChecked() || (_useSharedColorForAddedOverlay && (string.IsNullOrEmpty(_sharedColorName) || _sharedColorChannelCount < 1))))
			{
				if (GUILayout.Button("Add overlay to first slot", GUILayout.Width(200), GUILayout.Height(24)))
				{
					AddOverlayToFirstSlot();
				}
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void AddOverlayToFirstSlot()
		{
			if (_overlayToAdd == null)
			{
				EditorUtility.DisplayDialog("Add Overlay", "Select an OverlayDataAsset.", "OK");
				return;
			}

			if (_useSharedColorForAddedOverlay)
			{
				if (string.IsNullOrEmpty(_sharedColorName))
				{
					EditorUtility.DisplayDialog("Add Overlay", "Enter a Shared Color Name.", "OK");
					return;
				}
				if (_sharedColorChannelCount < 1)
				{
					EditorUtility.DisplayDialog("Add Overlay", "Shared Color Channels must be at least 1.", "OK");
					return;
				}
			}

			int updated = 0;
			int skippedNoSlot = 0;
			for (int i = 0; i < _recipes.Count; i++)
			{
				if (i >= _recipeSelected.Length || !_recipeSelected[i])
				{
					continue;
				}

				var recipe = _recipes[i];
				if (recipe == null)
				{
					continue;
				}

				var umaRecipe = new UMA.UMAData.UMARecipe();
				recipe.Load(umaRecipe, true);
				var firstSlot = umaRecipe.GetFirstSlot();
				if (firstSlot == null)
				{
					skippedNoSlot++;
					continue;
				}

				Undo.RecordObject(recipe, "Add overlay to first slot");
				var overlayData = new UMA.OverlayData(_overlayToAdd);
				if (_useSharedColorForAddedOverlay)
				{
					var sharedColor = GetOrCreateSharedColor(umaRecipe, _sharedColorName, _sharedColorChannelCount);
					overlayData.colorData = sharedColor;
				}
				firstSlot.AddOverlay(overlayData);
				recipe.Save(umaRecipe);
				EditorUtility.SetDirty(recipe);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Add Overlay", "Updated recipe(s): " + updated + "\nSkipped (no slot): " + skippedNoSlot, "OK");
		}

		private static UMA.OverlayColorData GetOrCreateSharedColor(UMA.UMAData.UMARecipe umaRecipe, string sharedColorName, int channels)
		{
			if (umaRecipe.sharedColors == null)
			{
				umaRecipe.sharedColors = new UMA.OverlayColorData[0];
			}

			for (int i = 0; i < umaRecipe.sharedColors.Length; i++)
			{
				var existing = umaRecipe.sharedColors[i];
				if (existing != null && string.Equals(existing.name, sharedColorName, System.StringComparison.Ordinal))
				{
					return existing;
				}
			}

			int insertIndex = umaRecipe.sharedColors.Length;
			System.Array.Resize(ref umaRecipe.sharedColors, insertIndex + 1);
			var created = new UMA.OverlayColorData(channels);
			created.name = sharedColorName;
			umaRecipe.sharedColors[insertIndex] = created;
			return created;
		}

		private bool DoesOverlayMatch(UMA.OverlayDataAsset overlayAsset)
		{
			if (overlayAsset == null)
			{
				return false;
			}
			var texList = overlayAsset.textureList;
			if (texList == null || texList.Length == 0 || texList[0] == null)
			{
				return false;
			}
			string texName = texList[0].name;
			if (string.IsNullOrEmpty(texName))
			{
				return false;
			}
			if (string.IsNullOrEmpty(_matchText))
			{
				return false;
			}

			if (_matchMode == MatchMode.StartsWith)
			{
				return texName.StartsWith(_matchText, System.StringComparison.OrdinalIgnoreCase);
			}
			if (_matchMode == MatchMode.EndsWith)
			{
				return texName.EndsWith(_matchText, System.StringComparison.OrdinalIgnoreCase);
			}
			return texName.IndexOf(_matchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void ProcessMaterialUpdates()
		{
			if (_targetMaterial == null)
			{
				EditorUtility.DisplayDialog("Process", "Select an UMAMaterial.", "OK");
				return;
			}
			if (string.IsNullOrEmpty(_matchText))
			{
				EditorUtility.DisplayDialog("Process", "Enter text to match against overlay texture[0].name.", "OK");
				return;
			}

			var changedSlots = new HashSet<UMA.SlotDataAsset>();
			var changedOverlays = new HashSet<UMA.OverlayDataAsset>();
			int processedRecipes = 0;
			int matchedOverlays = 0;

			try
			{
				for (int i = 0; i < _recipes.Count; i++)
				{
					if (i >= _recipeSelected.Length || !_recipeSelected[i])
					{
						continue;
					}
					var recipe = _recipes[i];
					if (recipe == null)
					{
						continue;
					}
					processedRecipes++;
					EditorUtility.DisplayProgressBar("Process", "Scanning recipes...", Mathf.Clamp01((float)processedRecipes / Mathf.Max(1, CountCheckedRecipes())));

					var umaRecipe = new UMA.UMAData.UMARecipe();
					recipe.Load(umaRecipe, true);
					if (umaRecipe.slotDataList == null)
					{
						continue;
					}

					for (int s = 0; s < umaRecipe.slotDataList.Length; s++)
					{
						var slot = umaRecipe.slotDataList[s];
						if (slot == null)
						{
							continue;
						}
						var slotAsset = slot.asset;
						if (slotAsset == null)
						{
							continue;
						}

						bool anyOverlayMatchedOnSlot = false;
						for (int o = 0; o < slot.OverlayCount; o++)
						{
							var overlay = slot.GetOverlay(o);
							if (overlay == null)
							{
								continue;
							}
							var overlayAsset = overlay.asset;
							if (overlayAsset == null)
							{
								continue;
							}
							if (!DoesOverlayMatch(overlayAsset))
							{
								continue;
							}
							matchedOverlays++;

							if (overlayAsset.material != _targetMaterial)
							{
								Undo.RecordObject(overlayAsset, "Update Overlay UMAMaterial");
								overlayAsset.material = _targetMaterial;
								overlayAsset.materialName = _targetMaterial != null ? _targetMaterial.name : "";
								changedOverlays.Add(overlayAsset);
							}

							anyOverlayMatchedOnSlot = true;
						}

						if (anyOverlayMatchedOnSlot && slotAsset.material != _targetMaterial)
						{
							Undo.RecordObject(slotAsset, "Update Slot UMAMaterial");
							slotAsset.material = _targetMaterial;
							slotAsset.materialName = _targetMaterial != null ? _targetMaterial.name : "";
							changedSlots.Add(slotAsset);
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			foreach (var overlayAsset in changedOverlays)
			{
				if (overlayAsset != null)
				{
					EditorUtility.SetDirty(overlayAsset);
				}
			}
			foreach (var slotAsset in changedSlots)
			{
				if (slotAsset != null)
				{
					EditorUtility.SetDirty(slotAsset);
				}
			}
			if (changedOverlays.Count > 0 || changedSlots.Count > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Process", "Matched overlays: " + matchedOverlays + "\nUpdated overlays: " + changedOverlays.Count + "\nUpdated slots: " + changedSlots.Count, "OK");
		}

		private int CountCheckedRecipes()
		{
			int count = 0;
			for (int i = 0; i < _recipeSelected.Length; i++)
			{
				if (_recipeSelected[i])
				{
					count++;
				}
			}
			return count;
		}

			private void DrawRecipesColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.62f));
				EditorGUILayout.LabelField("Wardrobe Recipes", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(60)))
				{
					for (int i = 0; i < _recipeSelected.Length; i++) _recipeSelected[i] = true;
				}
				if (GUILayout.Button("None", GUILayout.Width(60)))
				{
					for (int i = 0; i < _recipeSelected.Length; i++) _recipeSelected[i] = false;
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(4);
				_recipesScroll = EditorGUILayout.BeginScrollView(_recipesScroll, GUILayout.ExpandHeight(true));
				EditorGUILayout.BeginVertical();
				for (int i = 0; i < _recipes.Count; i++)
				{
					var recipe = _recipes[i];
					if (recipe == null) continue;

					EditorGUILayout.BeginHorizontal();
					_recipeSelected[i] = EditorGUILayout.Toggle(_recipeSelected[i], GUILayout.Width(18));
					if (GUILayout.Button(InspectContent, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
					{
						UMA.InspectorUtlity.InspectTarget(recipe);
					}
					EditorGUILayout.ObjectField(recipe, typeof(UMAWardrobeRecipe), false);
                 if (GUILayout.Button("Inspect", GUILayout.Width(60), GUILayout.Height(18)))
					{
						UMA.InspectorUtlity.InspectTarget(recipe);
					}
					string slot = string.IsNullOrEmpty(recipe.wardrobeSlot) ? "<Unassigned>" : recipe.wardrobeSlot;
					GUILayout.Label(slot, GUILayout.Width(160));
					if (!RecipeHasAnySlots(recipe))
					{
						GUILayout.Label("Warning - no slots", EditorStyles.miniLabel, GUILayout.Width(120));
					}
					else
					{
						GUILayout.Label("Slots look OK", EditorStyles.miniLabel, GUILayout.Width(120));
					}
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndVertical();
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

		private static bool RecipeHasAnySlots(UMAWardrobeRecipe recipe)
		{
			if (recipe == null)
			{
				return false;
			}
			try
			{
				var umaRecipe = new UMA.UMAData.UMARecipe();
				recipe.Load(umaRecipe, true);
				if (umaRecipe.slotDataList == null)
				{
					return false;
				}
				for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
				{
					if (umaRecipe.slotDataList[i] != null)
					{
						return true;
					}
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

		private void DrawSlotsColumn()
		{
			EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			EditorGUILayout.LabelField("Wardrobe Slots (union of slots across compatible races)", EditorStyles.boldLabel);
			if (_slots.Count == 0)
			{
				EditorGUILayout.HelpBox("No wardrobe slots found from compatible races on the selected recipes.", MessageType.Warning);
				EditorGUILayout.EndVertical();
				return;
			}

			_slotsScroll = EditorGUILayout.BeginScrollView(_slotsScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < _slots.Count; i++)
			{
				bool isSelected = (_selectedSlotIndex == i);
				bool newSelected = EditorGUILayout.ToggleLeft(_slots[i], isSelected);
				if (newSelected != isSelected)
				{
					_selectedSlotIndex = newSelected ? i : -1;
				}
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private bool HasAnyRecipeChecked()
		{
			for (int i = 0; i < _recipeSelected.Length; i++)
			{
				if (_recipeSelected[i]) return true;
			}
			return false;
		}

		private void AssignSelectedSlot()
		{
			if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count)
			{
				EditorUtility.DisplayDialog("Assign", "Select exactly one wardrobe slot.", "OK");
				return;
			}

			string slot = _slots[_selectedSlotIndex];
			if (string.IsNullOrEmpty(slot))
			{
				EditorUtility.DisplayDialog("Assign", "Selected wardrobe slot is empty.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _recipes.Count; i++)
			{
				if (i >= _recipeSelected.Length || !_recipeSelected[i])
				{
					continue;
				}
				var recipe = _recipes[i];
				if (recipe == null)
				{
					continue;
				}

				if (recipe.wardrobeSlot == slot)
				{
					continue;
				}

				Undo.RecordObject(recipe, "Assign wardrobe slot");
				recipe.wardrobeSlot = slot;
				EditorUtility.SetDirty(recipe);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
			}

			EditorUtility.DisplayDialog("Assign", "Updated " + updated + " recipe(s).", "OK");
		}
	}

		public static void ConvertToNonUMA(GameObject baseObject, UMAAvatarBase avatar, string Folder, bool ConvertNormalMaps, string CharName, bool AddStandaloneDNA, bool replaceExisting)
		{
			Folder = Folder + "/" + CharName;

			if (!System.IO.Directory.Exists(Folder))
			{
				System.IO.Directory.CreateDirectory(Folder);
			}

			SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
			int meshno = 0;
			foreach (SkinnedMeshRenderer smr in renderers)
			{
				Material[] omats = smr.sharedMaterials;
                Material[] mats = new Material[omats.Length];
                for (int i = 0; i < omats.Length; i++)
                {
                    mats[i] = new Material(omats[i]);
                }

                int Material = 0;
				foreach (Material m in mats)
				{
					// get each texture.
					// if the texture has been generated (has no path) then we need to convert to Texture2D (if needed) save that asset.
					// update the material with that material.
					List<Texture> allTexture = new List<Texture>();
					Shader shader = m.shader;
					for (int i = 0; i < shader.GetPropertyCount(); i++)
					{
						if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
						{
							string propertyName = shader.GetPropertyName(i);
							Texture texture = m.GetTexture(propertyName);
							if (texture is Texture2D || texture is RenderTexture)
							{
								string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
								if (string.IsNullOrEmpty(path))
								{
									bool isNormal = (propertyName.ToLower().Contains("bumpmap") || propertyName.ToLower().Contains("normal"));

                                    if (ConvertNormalMaps)
									{
										if (isNormal)
										{
											texture = sconvertNormalMap(texture);
										}
									}
									string texName = Path.Combine(Folder, CharName + "_Mat_" + Material + propertyName + ".png");
									if (texture is RenderTexture)
                                    {
										Debug.Log("Saving Render Texture " + texName);
                                        LinearSave(texture as RenderTexture, texName,isNormal);
                                    }
                                    else
                                    {
										Debug.Log("Saving texture " + texName);
                                        SaveTexture2D(texture as Texture2D, texName, isNormal);
                                    }
                                    //SaveTexture(texture, texName);
									AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
									if (isNormal)
                                    {
										TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(texName);
										importer.isReadable = true;
										importer.textureType = TextureImporterType.NormalMap;
										importer.maxTextureSize = 1024; // or whatever
										importer.textureCompression = TextureImporterCompression.CompressedHQ;
										EditorUtility.SetDirty(importer);
										importer.SaveAndReimport();
									}
                                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CustomAssetUtility.UnityFriendlyPath(texName));

                                    m.SetTexture(propertyName, tex);
								}
								else
								{
									m.SetTexture(propertyName, texture);
                                }
							}
						}
					}
					string matname = Folder + "/"+CharName+"_Mat_" + Material + ".mat"; 
                    CustomAssetUtility.SaveAsset<Material>(m, matname);
					Material++;
					// Save the material to disk?
					// update the SMR
				}

				string meshName = Folder + "/"+CharName+"_Mesh_" + meshno + ".asset";
				meshno++;
				// Save Mesh to disk.
				// smr.sharedMesh.Optimize(); This blows up some versions of Unity.
				CustomAssetUtility.SaveAsset<Mesh>(smr.sharedMesh, meshName);
				smr.sharedMaterials = mats;
				smr.materials = mats;
			}

            // save Animator Avatar.
            var animator = baseObject.GetComponent<Animator>();
            string avatarName = Folder + "/" + CharName + "_Avatar.asset";
            CustomAssetUtility.SaveAsset<Avatar>(animator.avatar, avatarName);


            if (replaceExisting)
			{
				DestroyImmediate(avatar);
				var lod = baseObject.GetComponent<UMASimpleLOD>();
				if (lod != null)
				{
					DestroyImmediate(lod);
				}

				if (AddStandaloneDNA)
				{
					UMAData uda = baseObject.GetComponent<UMAData>();
					StandAloneDNA sda = baseObject.AddComponent<UMA.StandAloneDNA>();
					sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
					if (avatar is DynamicCharacterAvatar)
					{
						DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
						sda.avatarDefinition = avt.GetAvatarDefinition(true);
					}
					sda.umaData = uda;
				}
				else
				{
					var ud = baseObject.GetComponent<UMAData>();
					if (ud != null)
					{
						DestroyImmediate(ud);
					}
				}
				var ue = baseObject.GetComponent<UMAExpressionPlayer>();
				if (ue != null)
				{
					DestroyImmediate(ue);
				}

				baseObject.name = CharName;
				string prefabName = Folder + "/" + CharName + ".prefab";
				prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
				PrefabUtility.SaveAsPrefabAssetAndConnect(baseObject, prefabName, InteractionMode.AutomatedAction);
			}
			else
			{
                // Create a new GameObject to hold the converted avatar.
                GameObject newAvatar = GameObject.Instantiate(baseObject);
                var lod = newAvatar.GetComponent<UMASimpleLOD>();
                if (lod != null)
                {
                    DestroyImmediate(lod);
                }

                if (AddStandaloneDNA)
                {
                    UMAData uda = newAvatar.GetComponent<UMAData>();
                    StandAloneDNA sda = newAvatar.AddComponent<UMA.StandAloneDNA>();
                    sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
                    if (avatar is DynamicCharacterAvatar)
                    {
                        DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
                        sda.avatarDefinition = avt.GetAvatarDefinition(true);
                    }
                    sda.umaData = uda;
                }
                else
                {
                    var ud = newAvatar.GetComponent<UMAData>();
                    if (ud != null)
                    {
                        DestroyImmediate(ud);
                    }
                }
                var ue = newAvatar.GetComponent<UMAExpressionPlayer>();
                if (ue != null)
                {
                    DestroyImmediate(ue);
                }

                newAvatar.name = CharName;
                string prefabName = Folder + "/" + CharName + ".prefab";
                prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
                PrefabUtility.SaveAsPrefabAssetAndConnect(newAvatar, prefabName, InteractionMode.AutomatedAction);
            }
        }


		[UnityEditor.MenuItem("GameObject/UMA/Save Atlas Textures")]
		[MenuItem("CONTEXT/DynamicCharacterAvatar/Save Selected Avatars generated textures to PNG", false, 10)]
		[MenuItem("UMA/Runtime/Save Selected Avatar Atlas Textures")]
		public static void SaveSelectedAvatarsPNG()
		{
			if (Selection.gameObjects.Length != 1)
			{
				EditorUtility.DisplayDialog("Notice", "Only one Avatar can be selected.", "OK");
				return;
			}

			var selectedTransform = Selection.gameObjects[0].transform;
			var avatar = selectedTransform.GetComponent<UMAAvatarBase>();

			if (avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "An Avatar must be selected to use this function", "OK");
				return;
			}

			SkinnedMeshRenderer smr = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
			if (smr == null)
			{
				EditorUtility.DisplayDialog("Warning", "Could not find SkinnedMeshRenderer in Avatar hierarchy", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", "Texture.png", "png", "Base Filename to save PNG files to.");
			if (!string.IsNullOrEmpty(path))
			{
				string basename = System.IO.Path.GetFileNameWithoutExtension(path);
				string pathname = System.IO.Path.GetDirectoryName(path);

                // Get the UMAMaterials for each atlas.

                UMAData umaData = avatar.umaData;
                GeneratedMaterials gmatContainer = umaData.generatedMaterials;

				int i = 0;
				foreach (var gm in gmatContainer.materials)
				{
					UMAMaterial umat = gm.umaMaterial;
					Material mat = gm.skinnedMeshRenderer.sharedMaterials[gm.materialIndex];
                    Material omat = gm.material;
					foreach(var tex in umat.GetTexturePropertyNames())
                    {
                        Texture texture = mat.GetTexture(tex);
                        if (texture != null)
						{
							string tname = $"{pathname}/{basename}_{i}_{umat.name}{tex}.PNG";
							string altName = $"{pathname}/{basename}_alt_{i}_{umat.name}{tex}.PNG";

                            try
							{
								if (tex.ToLower().Contains("normal") || tex.ToLower().Contains("bump"))
                                {
                                    SaveTexture(texture, tname, true);
                                }
                                else
                                {
                                    SaveTexture(texture, tname);
                                }
							}
                            catch  
                            { 
								// Not a readable texture. This is actually OK. Wish isReadable wasn't broken.
                            }
                        }
                    }
					i++;
                }
            }
		}


        private static Texture2D GetReadableTexture(RenderTexture texture, bool isNormal)
        {
            RenderTexture tmp;

            if (isNormal)
            {
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            }
            else
            {
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.sRGB);
            }

            Graphics.Blit(texture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

            Texture2D readableTexture = new Texture2D(texture.width, texture.height,TextureFormat.RGBA32, false, isNormal);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            return readableTexture;
        }
        // Thanks, Brooklyn!
        private static Texture2D GetReadableTexture(Texture2D texture, bool isNormal)
        {
			RenderTexture tmp;

            if (isNormal)
			{
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            }
            else
			{
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.sRGB);
            }

            Graphics.Blit(texture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

			// Always read back into a CPU RGBA32 texture; using `texture.format` can be incompatible
			// with `ReadPixels` (e.g., compressed/HDR formats) and can produce garbage data.
			Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, isNormal);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            return readableTexture;
        }

        private static void SaveTexture(Texture texture, string diffuseName, bool isNormal = false)
		{
			if (isNormal)
			{
				texture = sconvertNormalMap(texture);
                SaveTexture(texture, diffuseName, false);
				return;
			}

			if (texture is RenderTexture)
			{
                //Debug.Log("Saving render texture: " + diffuseName);
                //SaveRenderTexture(texture as RenderTexture, diffuseName, isNormal);
                Texture2D tex = GetReadableTexture(texture as RenderTexture, isNormal);
                SaveTexture2D(tex, diffuseName, isNormal);
                DestroyImmediate(tex);
                return;
			}
			else if (texture is Texture2D)
			{
                Texture2D tex = GetReadableTexture(texture as Texture2D, isNormal);
                SaveTexture2D(tex, diffuseName, isNormal);
                DestroyImmediate(tex);
                return;
			}
			EditorUtility.DisplayDialog("Error", "Texture is not RenderTexture or Texture2D", "OK");
		}

		/// <param name="normalMap"></param>
		/// <returns></returns>
		private static Texture2D SConvertNormalMap(Texture2D normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalCvt");
			// RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			var normalMapRenderTex = RenderTexture.GetTemporary(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
            //normalMapRenderTex.Create();

            normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width / 8, normalMap.height / 8, 1);
            Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
            RenderTexture.active = normalMapRenderTex;
            convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			RenderTexture.ReleaseTemporary(normalMapRenderTex);
			return convertedNormalMap;
		}

		public static Texture2D SConvertNormalMap(RenderTexture normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalCvt");
			RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
			normalMapRenderTex.Create();
			normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width/8, normalMap.height/8, 1);
			RenderTexture.active = normalMapRenderTex;

			Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
			convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			DestroyImmediate(normalMapRenderTex);
			return convertedNormalMap;
		}

		private static Texture2D sconvertNormalMap2(RenderTexture rt)
		{
			Texture2D tex = GetRTPixels(rt);
			Texture2D result = SConvertNormalMap(tex);
			DestroyImmediate(tex);
			return result;
		}

		private static Texture2D sconvertNormalMap(Texture tex)
		{
			if (tex is RenderTexture)
            {
                return SConvertNormalMap(tex as RenderTexture);
            }
            return SConvertNormalMap(tex as Texture2D);
		}

		static public Texture2D GetRTPixels(RenderTexture rt)
		{
            // Remember crrently active render texture
            RenderTexture currentActiveRT = RenderTexture.active;

            /// Some goofiness ends up with the texture being too dark unless
            /// I send it to a new render texture.
            RenderTexture outputMap = new RenderTexture(rt.width, rt.height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); 
			outputMap.enableRandomWrite = true;
			outputMap.Create();
			RenderTexture.active = outputMap;
			GL.Clear(true, true, Color.black);
			Graphics.Blit(rt, outputMap);



			// Set the supplied RenderTexture as the active one
			RenderTexture.active = outputMap;

			// Create a new Texture2D and read the RenderTexture image into it
			Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
			tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

			// Restore previously active render texture
			RenderTexture.active = currentActiveRT;
			DestroyImmediate(outputMap);
			return tex;
		}

        static public void LinearSave(RenderTexture rt, string textureName, bool isNormal)
        {
            // Remember crrently active render texture
            RenderTexture currentActiveRT = RenderTexture.active;

            // Set the supplied RenderTexture as the active one
            RenderTexture.active = rt;

            // Create a new Texture2D and read the RenderTexture image into it
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

            // Restore previously active render texture
            RenderTexture.active = currentActiveRT;
            SaveTexture2D(tex, textureName, isNormal);
            
        }

        public static void SaveRenderTexture(RenderTexture texture, string textureName, bool isNormal = false)
		{
			Texture2D tex;

			if (isNormal)
			{
				tex = SConvertNormalMap(texture);
			}
			else
			{
				tex = GetRTPixels(texture);
			}
			SaveTexture2D(tex, textureName, isNormal);
		}

		private static void SaveTexture2D(Texture2D texture, string textureName, bool isNormal)
		{
            Texture2D convertedTexture = GetReadableTexture(texture, isNormal);
			byte[] data = convertedTexture.EncodeToPNG();
			DestroyImmediate(convertedTexture);
            System.IO.File.WriteAllBytes(textureName, data);
        }

        [UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as UMA Preset")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as UMA Preset")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar as UMA Preset", priority = 1)]
		public static void SaveSelectedAvatarsPreset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save avatar preset", "Assets", avatar.name + ".umapreset", "umapreset");
					if (path.Length != 0)
					{

						UMAPreset prs = new UMAPreset();
						prs.DefaultColors = avatar.characterColors;
						var DNA = avatar.GetDNA();
						prs.PredefinedDNA = new UMAPredefinedDNA();
						foreach (DnaSetter d in DNA.Values)
						{
							prs.PredefinedDNA.AddDNA(d.Name, d.Value);
						}
						prs.DefaultWardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
						foreach (UMATextRecipe utr in avatar.WardrobeRecipes.Values)
						{
							prs.DefaultWardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(utr));
						}
						string presetstring = JsonUtility.ToJson(prs);
						System.IO.File.WriteAllText(path, presetstring);
					}
				}
			}
		}


		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Character text file (runtime only)")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Text file (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) Txt", priority = 1)]
		public static void SaveSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is UMA.CharacterSystem.DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe,(avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe);
						}
						System.IO.File.WriteAllText(path, asset.recipeString);
						UMAUtils.DestroySceneObject(asset);
					}
				}
			}
		}


		[UnityEditor.MenuItem("GameObject/UMA/Show Mesh Info (runtime only)")]
		public static void ShowSelectedAvatarStats()
		{
			if (Selection.gameObjects.Length == 1)
			{
				var selectedTransform = Selection.gameObjects[0].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					SkinnedMeshRenderer sk = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
					if (sk != null)
					{
						List<string> info = new List<string>
					{
						sk.gameObject.name,
						"Mesh index type: " + sk.sharedMesh.indexFormat.ToString(),
						"VertexLength: " + sk.sharedMesh.vertices.Length,
						"Submesh Count: " + sk.sharedMesh.subMeshCount
					};
						for (int i = 0; i < sk.sharedMesh.subMeshCount; i++)
						{
							int[] tris = sk.sharedMesh.GetTriangles(i);
							info.Add("Submesh " + i + " Tri count: " + tris.Length);
						}
						Rect R = new Rect(200.0f, 200.0f, 300.0f, 600.0f);
						DisplayListWindow.ShowDialog("Mesh Info", R, info);
					}
				}
			}

		}

		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Asset (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) asset", priority = 1)]
		public static void SaveSelectedAvatarsAsset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", avatar.name + ".asset", "asset", "Message 2");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe, (avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe);
						}
						AssetDatabase.CreateAsset(asset, path);
						AssetDatabase.SaveAssets();
						Debug.Log("Recipe size: " + asset.recipeString.Length + " chars");

					}
				}
			}
		}

		[UnityEditor.MenuItem("GameObject/UMA/Load from AvatarDefinition file (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from an AvatarDefinition file (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt", priority = 1)]
		public static void LoadSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt,adf");
					if (path.Length != 0)
					{
						string recipeString = FileUtils.ReadAllText(path);
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							(avatar as DynamicCharacterAvatar).LoadAvatarDefinition(recipeString);
						}
					}
				}
			}
		}


		//@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method
		[UnityEditor.MenuItem("GameObject/UMA/Save as AvatarDefinition (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized AvatarDefinition File")]
		[MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) AvatarDefinition (optimized)", priority = 1)]
		public static void SaveSelectedAvatarsDefinition()
		{
			if (!Application.isPlaying)
			{
				EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
				return;
			}

			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save DynamicCharacterAvatar Text", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						avatar.DoSave(false, path);
					}
				}
			}
		}



		[UnityEditor.MenuItem("Assets/Add Selected Assets to UMA Global Library")]
		public static void AddSelectedToGlobalLibrary()
		{
			int added = 0;
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;

			foreach (Object o in Selection.objects)
			{
				System.Type type = o.GetType();
				if (UAI.IsIndexedType(type))
				{
					if (UAI.EvilAddAsset(type, o))
                    {
                        added++;
                    }
                }
			}
			UAI.ForceSave();
			EditorUtility.DisplayDialog("Success", added + " item(s) added to Global Library", "OK");
		}
	}

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

	public class UmaPrefabSaverWindow : EditorWindow
	{
		[Tooltip("The character that you want to convert")]
		public UMAAvatarBase baseObject;
		[Tooltip("If true, will replace the UMA with the generated prefab in the scene")]
        public bool replaceExisting = false;
        [Tooltip("Convert Swizzled normal maps back to standard normal maps")]
		public bool UnswizzleNormalMaps = true;
		[Tooltip("If True, will keep the umaData, and add a Standalone DNA component allowing you to load/save/Deform skeletal DNA")]
		public bool AddStandaloneDNA = true;
		[Tooltip("The prefab will be named this, and it will be added to all assets saved")]
		public string CharacterName;
		[Tooltip("The folder where the prefab folder will be created")]
		public UnityEngine.Object prefabFolder;
		public string CheckFolder(ref UnityEngine.Object folderObject)
		{
			if (folderObject != null)
			{
				string destpath = AssetDatabase.GetAssetPath(folderObject);
				if (string.IsNullOrEmpty(destpath))
				{
					folderObject = null;
				}
				else if (!System.IO.Directory.Exists(destpath))
				{
					destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
				}
				return destpath;
			}
			return null;
		}

		void OnGUI()
		{
			EditorGUILayout.LabelField("UMA Prefab Saver", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("This will convert an UMA avatar into a non-UMA prefab. Once converted, it can be reused with little overhead, but all UMA functionality will be lost.", MessageType.None, false);
			baseObject = (UMAAvatarBase)EditorGUILayout.ObjectField("UMA Avatar",baseObject, typeof(UMAAvatarBase),true);
			EditorGUILayout.HelpBox("If you unswizzle normals (recommended) then they can be used in other applications, and UMA will automatically mark them as normal maps in the import settings.", MessageType.None);
			UnswizzleNormalMaps = EditorGUILayout.Toggle("Unswizzle Normals", UnswizzleNormalMaps);
			EditorGUILayout.HelpBox("Adding Standalone DNA will allow you to adjust most DNA of the character, without it being an UMA. However, it will require that you have the UMA system in the project.",MessageType.None);
			AddStandaloneDNA = EditorGUILayout.Toggle("Add Standalone DNA", AddStandaloneDNA);

            replaceExisting = EditorGUILayout.Toggle("Replace Existing UMA", replaceExisting);
            if (replaceExisting)
            {
                EditorGUILayout.HelpBox("If you replace the existing UMA, it will be removed from the scene. If you do not replace it, you will need to manually add the prefab to the scene.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("If you do not replace the existing UMA, you will need to manually add the prefab to the scene.", MessageType.None);
            }
			CharacterName = EditorGUILayout.TextField("Prefab Name", CharacterName);
			prefabFolder = EditorGUILayout.ObjectField("Prefab Base Folder", prefabFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

			string folder = CheckFolder(ref prefabFolder);

			if (prefabFolder != null && baseObject != null && !string.IsNullOrEmpty(CharacterName))
			{
				if (GUILayout.Button("Make Prefab") && prefabFolder != null)
				{
					UMAAvatarLoadSaveMenuItems.ConvertToNonUMA(baseObject.gameObject, baseObject, folder, UnswizzleNormalMaps, CharacterName,AddStandaloneDNA,replaceExisting);
					EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
				}
			}
			else
            {
				if (baseObject == null)
				{
					EditorGUILayout.HelpBox("A valid character with DynamicCharacterAvatar or DynamicAvatar must be supplied",MessageType.Error);
				}
				if (string.IsNullOrEmpty(CharacterName))
                {
					EditorGUILayout.HelpBox("Prefab Name cannot be empty", MessageType.Error);
                }
				if (prefabFolder == null)
                {
					EditorGUILayout.HelpBox("A valid base folder must be supplied", MessageType.Error);
                }
            }
		}

		[MenuItem("UMA/Prefab Maker", priority = 20)]
		public static void OpenUmaPrefabWindow()
		{
			UmaPrefabSaverWindow window = (UmaPrefabSaverWindow)EditorWindow.GetWindow(typeof(UmaPrefabSaverWindow));
			window.titleContent.text = "UMA Prefab Maker";
		}
	}
}
