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
internal class ExamineWearables : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _recipes = new List<UMAWardrobeRecipe>();
		private bool[] _recipeSelected = new bool[0];
		private Vector2 _recipesScroll;

		private readonly List<string> _slots = new List<string>();
		private int _selectedSlotIndex = -1;
		private Vector2 _slotsScroll;
		private enum WardrobeSlotFilter
		{
			All = 0,
			Assigned = 1,
			Unassigned = 2
		}

		private UMAMaterial _targetMaterial;
		private string _matchText = "";
      private UMA.OverlayDataAsset _overlayToAdd;
		private bool _useSharedColorForAddedOverlay;
		private string _sharedColorName = "NewSharedColor";
		private int _sharedColorChannelCount = 3;
      private WardrobeSlotFilter _wardrobeSlotFilter = WardrobeSlotFilter.All;
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
         using (new EditorGUI.DisabledScope(!HasAnyVisibleRecipeChecked() || _selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count))
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

			var changedOverlays = new HashSet<UMA.OverlayDataAsset>();
           int changedRecipes = 0;
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
                 bool recipeChanged = false;
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

                       if (anyOverlayMatchedOnSlot && slot.altMaterial != _targetMaterial)
						{
							slot.altMaterial = _targetMaterial;
							recipeChanged = true;
						}
					}

					if (recipeChanged)
					{
						Undo.RecordObject(recipe, "Update Slot UMAMaterial");
						recipe.Save(umaRecipe);
						EditorUtility.SetDirty(recipe);
						changedRecipes++;
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
         if (changedOverlays.Count > 0 || changedRecipes > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

           EditorUtility.DisplayDialog("Process", "Matched overlays: " + matchedOverlays + "\nUpdated overlays: " + changedOverlays.Count + "\nUpdated slot overrides: " + changedRecipes, "OK");
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

			private bool WardrobeSlotAssigned(UMAWardrobeRecipe recipe)
			{
				if (recipe == null || string.IsNullOrEmpty(recipe.wardrobeSlot))
				{
					return false;
				}
				if (recipe.wardrobeSlot.ToLower() == "none")
				{
					return false;
				}
				return true;
            }

            private void DrawRecipesColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.62f));
				EditorGUILayout.LabelField("Wardrobe Recipes", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
           EditorGUILayout.LabelField("Wardrobe Slot", GUILayout.Width(90));
			_wardrobeSlotFilter = (WardrobeSlotFilter)EditorGUILayout.EnumPopup(_wardrobeSlotFilter, GUILayout.Width(120));
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();

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

                 if (!IsRecipeVisible(recipe))
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					_recipeSelected[i] = EditorGUILayout.Toggle(_recipeSelected[i], GUILayout.Width(18));
					if (GUILayout.Button(InspectContent, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
					{
						UMA.InspectorUtlity.InspectTarget(recipe);
					}
					EditorGUILayout.ObjectField(recipe, typeof(UMAWardrobeRecipe), false);
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
                    if (GUILayout.Button("Repair Slots", GUILayout.Width(100), GUILayout.Height(18)))
					{
						WearablePackedSlotRepairWindow.Open(recipe);
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

		private bool HasAnyVisibleRecipeChecked()
		{
			for (int i = 0; i < _recipes.Count; i++)
			{
				if (i < _recipeSelected.Length && _recipeSelected[i] && IsRecipeVisible(_recipes[i]))
				{
					return true;
				}
			}
			return false;
		}

		private bool IsRecipeVisible(UMAWardrobeRecipe recipe)
		{
			if (recipe == null)
			{
				return false;
			}

			bool isAssigned = WardrobeSlotAssigned(recipe);
			if (_wardrobeSlotFilter == WardrobeSlotFilter.Assigned)
			{
				return isAssigned;
			}

			if (_wardrobeSlotFilter == WardrobeSlotFilter.Unassigned)
			{
				return !isAssigned;
			}

			return true;
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

		internal class WearablePackedSlotRepairWindow : EditorWindow
		{
          private UMATextRecipe _recipe;
			private UMAPackedRecipeBase.UMAPackRecipe _packedRecipe;
			private Vector2 _scroll;

           public static void Open(UMATextRecipe recipe)
			{
				if (recipe == null)
				{
					return;
				}

				var window = CreateInstance<WearablePackedSlotRepairWindow>();
				window.titleContent = new GUIContent("Repair Slots");
				window.minSize = new Vector2(760f, 320f);
				window.Initialize(recipe);
				window.ShowUtility();
				window.Focus();
			}

           private void Initialize(UMATextRecipe recipe)
			{
				_recipe = recipe;
				_packedRecipe = recipe.PackedLoad();
				if (_packedRecipe == null)
				{
					_packedRecipe = new UMAPackedRecipeBase.UMAPackRecipe();
				}
			}

			private void OnGUI()
			{
				if (_recipe == null)
				{
					EditorGUILayout.HelpBox("Recipe is missing.", MessageType.Warning);
					return;
				}

				EditorGUILayout.LabelField("Recipe", _recipe.name);
				EditorGUILayout.Space(4);

				var packedSlots = _packedRecipe != null ? _packedRecipe.slotsV3 : null;
				if (packedSlots == null || packedSlots.Length == 0)
				{
					EditorGUILayout.HelpBox("No PackedSlotDataV3 entries found on this recipe.", MessageType.Info);
					EditorGUILayout.BeginHorizontal();
					GUILayout.FlexibleSpace();
					if (GUILayout.Button("Close", GUILayout.Width(120), GUILayout.Height(24)))
					{
						Close();
					}
					EditorGUILayout.EndHorizontal();
					return;
				}

				EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
				GUILayout.Label("#", EditorStyles.boldLabel, GUILayout.Width(26));
				GUILayout.Label("ID", EditorStyles.boldLabel, GUILayout.Width(220));
				GUILayout.Label("Disabled", EditorStyles.boldLabel, GUILayout.Width(60));
				GUILayout.Label("Placeholder", EditorStyles.boldLabel, GUILayout.Width(80));
				GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(70));
				GUILayout.Label("Repair", EditorStyles.boldLabel, GUILayout.Width(80));
				EditorGUILayout.EndHorizontal();

				_scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < packedSlots.Length; i++)
				{
					var packedSlot = packedSlots[i];
					EditorGUILayout.BeginHorizontal();
					GUILayout.Label(i.ToString(), GUILayout.Width(26));

					if (packedSlot == null)
					{
						GUILayout.Label("<empty>", GUILayout.Width(220));
						using (new EditorGUI.DisabledScope(true))
						{
							EditorGUILayout.Toggle(false, GUILayout.Width(60));
							EditorGUILayout.Toggle(false, GUILayout.Width(80));
							GUILayout.Label("<empty>", GUILayout.Width(70));
							GUILayout.Button("Repair", GUILayout.Width(80));
						}
						EditorGUILayout.EndHorizontal();
						continue;
					}

					packedSlot.id = EditorGUILayout.TextField(packedSlot.id ?? string.Empty, GUILayout.Width(220));
					packedSlot.isDisabled = EditorGUILayout.Toggle(packedSlot.isDisabled, GUILayout.Width(60));
					packedSlot.isPlaceholderSlot = EditorGUILayout.Toggle(packedSlot.isPlaceholderSlot, GUILayout.Width(80));

					bool slotExists = PackedSlotExists(packedSlot.id);
					string status = slotExists ? "<OK>" : "<missing>";
					GUILayout.Label(status, GUILayout.Width(70));

					using (new EditorGUI.DisabledScope(slotExists || string.IsNullOrEmpty(packedSlot.id)))
					{
						if (GUILayout.Button("Repair", GUILayout.Width(80)))
						{
							RepairPackedSlot(packedSlot);
						}
					}

					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(8);
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Save", GUILayout.Width(120), GUILayout.Height(24)))
				{
					SavePackedRecipe();
					Close();
				}
				if (GUILayout.Button("Close", GUILayout.Width(120), GUILayout.Height(24)))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}

			private bool PackedSlotExists(string slotId)
			{
				if (string.IsNullOrEmpty(slotId))
				{
					return false;
				}

				var indexer = UMAAssetIndexer.Instance;
				if (indexer == null)
				{
					return false;
				}

				try
				{
					return indexer.HasAsset<SlotDataAsset>(slotId);
				}
				catch
				{
					return false;
				}
			}

			private void RepairPackedSlot(UMAPackedRecipeBase.PackedSlotDataV3 packedSlot)
			{
				if (packedSlot == null || string.IsNullOrEmpty(packedSlot.id))
				{
					return;
				}

				var indexer = UMAAssetIndexer.Instance;
				if (indexer == null)
				{
					EditorUtility.DisplayDialog("Repair Slots", "UMAAssetIndexer is not available.", "OK");
					return;
				}

               List<string> similar = indexer.FindSimilar<SlotDataAsset>(packedSlot.id, "_slot");
				if (similar == null || similar.Count == 0)
				{
					EditorUtility.DisplayDialog("Repair Slots", "No similar SlotDataAsset names were found for '" + packedSlot.id + "'.", "OK");
					return;
				}

				if (similar.Count == 1)
				{
					packedSlot.id = similar[0];
					SavePackedRecipe();
					return;
				}

				SimilarSlotSelectionWindow.Open(packedSlot.id, similar, selectedId =>
				{
					if (string.IsNullOrEmpty(selectedId))
					{
						return;
					}

					packedSlot.id = selectedId;
					SavePackedRecipe();
					Repaint();
				});
			}

			private void SavePackedRecipe()
			{
				if (_recipe == null || _packedRecipe == null)
				{
					return;
				}

				Undo.RecordObject(_recipe, "Repair packed recipe slots");
				_recipe.PackedSave(_packedRecipe);
				EditorUtility.SetDirty(_recipe);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}
		}

		internal class SimilarSlotSelectionWindow : EditorWindow
		{
			private string _missingId;
			private List<string> _options = new List<string>();
			private Vector2 _scroll;
			private System.Action<string> _onSelected;

			public static void Open(string missingId, List<string> options, System.Action<string> onSelected)
			{
				var window = CreateInstance<SimilarSlotSelectionWindow>();
				window.titleContent = new GUIContent("Select Slot");
				window.minSize = new Vector2(420f, 280f);
				window._missingId = missingId;
				window._options = options != null ? new List<string>(options) : new List<string>();
				window._onSelected = onSelected;
				window.ShowUtility();
				window.Focus();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField("Missing Slot ID", _missingId ?? string.Empty);
				EditorGUILayout.Space(4);
				EditorGUILayout.LabelField("Select replacement slot", EditorStyles.boldLabel);
				_scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _options.Count; i++)
				{
					string option = _options[i];
					if (GUILayout.Button(option, GUILayout.Height(22)))
					{
						_onSelected?.Invoke(option);
						Close();
					}
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(8);
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Cancel", GUILayout.Width(120), GUILayout.Height(24)))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}
		}
	}
}
