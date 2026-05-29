using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMARandomizer))]
	public class UMARandomizerEditor : Editor
	{
		UMARandomizer currentTarget = null;               // Randomizer Inspector target
		private List<RandomColors> colorsToDelete = default;    // SharedColorTables temp var
		private SerializedProperty definitionProperty = default;// Randomizer Definition is drawn using a custom Property drawer
		private EditorApplication.CallbackFunction delayedEnableHandler = default;   // Defer init until editor/domain reload settles

		private bool displayHelp = false;                   // Not used ATM
		private int copyFromRace = 0, copyToRace = 0;       // For Randomizer Copy From / To Race Utility
		private bool autoSave = false;                      // Does Randomizer requires saving ?
		private double autoSavePeriod = 3f, nextSave = 0f;  // Handle SaveAssets Delay

		private static bool IsEditorBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

		/// <summary>
		/// Adds Context Menu to turn off Character Definition and Global Colors
		/// </summary>
		public static class ContextMenu
		{
			private const string definitionMenuName = "CONTEXT/UMARandomizer/Use Definition";
			private const string globalColorsMenuName = "CONTEXT/UMARandomizer/Use GlobalColors";
			public static System.Action<bool> OnUseDefinitionChange;
			public static System.Action<bool> OnUseGlobalColorsChange;
			private static bool useDefinition;
			private static bool useGlobalColors;

			public static bool UseDefinition
			{
				get { return useDefinition; }
				set { useDefinition = value; ToggleUseDefinitionValidate(); }
			}

			[MenuItem(definitionMenuName, priority = 101)]
			private static void ToggleUseDefinition()
			{
				UseDefinition = !UseDefinition;
				OnUseDefinitionChange?.Invoke(UseDefinition);
			}

			[MenuItem(definitionMenuName, true, priority = 101)]
			private static bool ToggleUseDefinitionValidate()
			{
				Menu.SetChecked(definitionMenuName, UseDefinition);
				return true;
			}

			public static bool UseGlobalColors
			{
				get { return useGlobalColors; }
				set { useGlobalColors = value; ToggleUseGlobalColorsValidate(); }
			}

			[MenuItem(globalColorsMenuName, priority = 102)]
			private static void ToggleUseGlobalColors()
			{
				UseGlobalColors = !UseGlobalColors;
				OnUseGlobalColorsChange?.Invoke(UseGlobalColors);
			}

			[MenuItem(globalColorsMenuName, true, priority = 102)]
			private static bool ToggleUseGlobalColorsValidate()
			{
				Menu.SetChecked(globalColorsMenuName, UseGlobalColors);
				return true;
			}
		}

		/// <summary>
		/// Tooltips used in RandomizerEditor
		/// </summary>
		private static class Tooltips
		{
			internal static GUIContent GlobalColors = new GUIContent("", "Optional : Define SharedColor tables common to all races");

			internal static GUIContent Utilities = new GUIContent("Utilities", "Randomizer Editor Utilities:" +
				"\n> Copy an existing Race Randomizer to an other Race" +
				"\n> Update DNA List");
			internal static GUIContent FromRace = new GUIContent("", "Select a Race with an existing randomizer to copy from");

			internal static GUIContent ToRace = new GUIContent("", "Select a Race you wish to copy existing Randomizer to. If a randomizer exists for target Race, it will be overwritten. It copy DNAs range even if DNAs are not available in target Race (those will be discarded by the randomizer).");

			internal static GUIContent UpdateDNA = new GUIContent("Update DNA List", "Use \"Update DNA List\" if you have modified the list of DNAs from a Race and the new/modified DNAs are not updated in the DNA list of the Race Randomizer");

			internal static GUIContent NewPresets = new GUIContent("New Presets", "Creates a new Random Avatar for selected Race");

			internal static GUIContent DropArea(string race) => new GUIContent("Then Drag Wardrobe Recipe(s) or Collection(s) for " + race + " here", "");
		}

		private void OnUseDefinitionChange(bool newValue)
		{
			currentTarget.useDefinition = newValue;
			autoSave = true;
		}

		private void OnUseGlobalColorsChange(bool newValue)
		{
			currentTarget.useGlobalColors = newValue;
			autoSave = true;
		}

		protected void OnEnable()
		{
			if (!TryInitializeEditor())
			{
				ScheduleDelayedEnable();
			}
		}


		/// <summary>
		/// Saves Randomizer if modifications have been made and net yet saved
		/// </summary>
		protected void OnDisable()
		{
			AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;

			if (delayedEnableHandler != null)
			{
				EditorApplication.delayCall -= delayedEnableHandler;
				delayedEnableHandler = null;
			}

			if (autoSave && currentTarget != null)
				SaveObject();

			ContextMenu.OnUseDefinitionChange -= OnUseDefinitionChange;
			ContextMenu.OnUseGlobalColorsChange -= OnUseGlobalColorsChange;
			currentTarget = null;
			definitionProperty = null;
		}

		public override void OnInspectorGUI()
		{
			if (!EnsureEditorInitialized())
			{
				EditorGUILayout.HelpBox("UMARandomizer is waiting for the editor domain and UMA Asset Indexer to finish loading.", MessageType.Info);
				return;
			}

			if (currentTarget == null)
			{
				EditorGUILayout.HelpBox("UMARandomizer target is missing.", MessageType.Error);
				return;
			}

			// Ensure editor-only, non-serialized fields are initialized.
			if (currentTarget.raceDatas == null || currentTarget.races == null)
			{
				if (!TryInitRaces(currentTarget))
				{
					ScheduleDelayedEnable();
					EditorGUILayout.HelpBox("UMA races are still loading after the domain reload. Please wait a moment.", MessageType.Info);
					return;
				}
			}
			if (currentTarget.droppedItems == null)
			{
				currentTarget.droppedItems = new List<UMAWardrobeRecipe>();
			}
			if (currentTarget.droppedCollections == null)
			{
				currentTarget.droppedCollections = new List<UMAWardrobeCollection>();
			}

			try
			{
				if (Event.current.type == EventType.Layout)
				{
					UpdateObject();
				}
			}
			catch (System.Exception ex)
			{
				EditorGUILayout.HelpBox("UMARandomizer inspector hit an exception while updating. See Console for details.", MessageType.Error);
				Debug.LogException(ex);
				// Don't keep trying every layout pass if the state is bad.
				autoSave = false;
				return;
			}

			if (currentTarget.useDefinition)
				EditorGUILayout.PropertyField(definitionProperty);

			// -- Global Shared Colors --
			if (currentTarget.useGlobalColors)
				SharedColorsGUI(ref currentTarget.Global.ColorsFoldout, currentTarget.Global.SharedColors, "Global Colors", Tooltips.GlobalColors);

			UtilitiesGUI(ref currentTarget.Global.UtilityFoldout, "Utilities", Tooltips.Utilities);

			try
			{
				DragAndDropGUI("Per Race Randomizers");
			}
			catch (System.Exception ex)
			{
				EditorGUILayout.HelpBox("Failed to draw per-race randomizer UI. See Console for details.", MessageType.Error);
				Debug.LogException(ex);
				return;
			}

			if (currentTarget.RandomAvatars != null)
			{
				foreach (RandomAvatar ra in currentTarget.RandomAvatars)
				{
					if (ra != null)
					{
						RandomAvatarGUI(ra);
					}
				}
			}

			if (GUI.changed && !autoSave)
			{
				autoSave = true;
				nextSave = EditorApplication.timeSinceStartup + autoSavePeriod;
			}


			if (autoSave && EditorApplication.timeSinceStartup > nextSave)
				SaveObject();

		}

		#region ------ GUI Methods ------
		bool _helpexpanded;
		/// <summary>
		/// Editor Utilities for Randomizer :
		/// <br>> Copy from a Race Randomizer to another Race </br>
		/// <br>> Update DNA List</br>
		/// </summary>
		private void UtilitiesGUI(ref bool foldout, string label, GUIContent tooltip)
		{
			foldout = GUIHelper.FoldoutBar(foldout, label, tooltip);

			if (!foldout) return;

			GUIHelper.BeginVerticalPadded();

			Rect lineRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight * 2, GUILayout.ExpandWidth(true));

			// Place Button on the right
			Rect button = new Rect(lineRect.xMax - 120f, lineRect.y, 120f, lineRect.height);
			Rect fromRaceLabel = new Rect(lineRect.x, lineRect.y, 40f, EditorGUIUtility.singleLineHeight);
			Rect fromRace = new Rect(fromRaceLabel.xMax, lineRect.y, lineRect.width - button.width - fromRaceLabel.width, EditorGUIUtility.singleLineHeight);


			Rect toRaceLabel = new Rect(lineRect.x + 13f, fromRace.yMax, 27f, EditorGUIUtility.singleLineHeight);
			Rect toRace = new Rect(toRaceLabel.xMax, fromRace.yMax, lineRect.width - button.width - toRaceLabel.width - 13f, EditorGUIUtility.singleLineHeight);


			if (GUI.Button(button, "Copy Race\nFrom -> To"))
			{
				RandomAvatar destRA = FindAvatar(currentTarget.raceDatas[copyToRace]);
				RandomAvatar srcRA = FindAvatar(currentTarget.raceDatas[copyFromRace]);
				if (destRA != null && srcRA != null)
					destRA.CopyFrom(srcRA);
			}
			EditorGUI.LabelField(fromRaceLabel, "From :");
			copyFromRace = EditorGUI.Popup(fromRace, copyFromRace, currentTarget.races);
			EditorGUI.LabelField(fromRace, Tooltips.FromRace);

			EditorGUI.LabelField(toRaceLabel, "To :");
			copyToRace = EditorGUI.Popup(toRace, copyToRace, currentTarget.races);
			EditorGUI.LabelField(toRace, Tooltips.ToRace);

			// Update DNA
			if (GUILayout.Button(Tooltips.UpdateDNA))
			{
				foreach (RandomAvatar ra in currentTarget.RandomAvatars)
					ra.SetupDNA(ra.raceData);
			}

			GUIHelper.EndVerticalPadded();
		}

		private void DragAndDropGUI(string label, string tooltip = default)
		{
			GUIHelper.Separator();
			EditorGUILayout.LabelField("Per Race Randomizer", EditorStyles.boldLabel);

			if (currentTarget.races == null || currentTarget.races.Length == 0 || currentTarget.raceDatas == null || currentTarget.raceDatas.Count == 0)
			{
				EditorGUILayout.HelpBox("No UMA races found in the Asset Indexer. Open the UMA Asset Indexer window and rebuild the index, then re-open this inspector.", MessageType.Warning);
				return;
			}
			if (currentTarget.currentRace < 0)
			{
				currentTarget.currentRace = 0;
			}
			else if (currentTarget.currentRace >= currentTarget.races.Length)
			{
				currentTarget.currentRace = currentTarget.races.Length - 1;
			}

			// Race Selection | New Race Presets Button
			Rect lineRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));

			Rect raceLabelSelectionRect = new Rect(lineRect.x, lineRect.y, 120f, lineRect.height);
			Rect newRacePresetsRect = new Rect(lineRect.xMax - 100f, lineRect.y, 95f, lineRect.height);
			Rect raceSelectionRect = new Rect(raceLabelSelectionRect.xMax, lineRect.y, lineRect.width - newRacePresetsRect.width - raceLabelSelectionRect.width - 5f, lineRect.height);

			EditorGUI.LabelField(raceLabelSelectionRect, "First Select Race");
			currentTarget.currentRace = EditorGUI.Popup(raceSelectionRect, currentTarget.currentRace, currentTarget.races);

			if (GUI.Button(newRacePresetsRect, "Add Race"))
			{
				FindAvatar(currentTarget.raceDatas[currentTarget.currentRace]);
			}

			GUILayout.Space(5);

			// Drop Area
			if (currentTarget.droppedItems == null) currentTarget.droppedItems = new List<UMAWardrobeRecipe>();
			if (currentTarget.droppedCollections == null) currentTarget.droppedCollections = new List<UMAWardrobeCollection>();
			currentTarget.droppedItems.Clear();
			currentTarget.droppedCollections.Clear();
			GUIHelper.DropAreaGUI(DropedItem, height: 50f, label: Tooltips.DropArea(currentTarget.races[currentTarget.currentRace]));

			GUILayout.Space(5);
		}

		private bool DropedItem(Object draggedObject)
		{
			if (draggedObject == null)
			{
				return false;
			}
			if (currentTarget.droppedItems == null) currentTarget.droppedItems = new List<UMAWardrobeRecipe>();
			if (currentTarget.droppedCollections == null) currentTarget.droppedCollections = new List<UMAWardrobeCollection>();

			// Process Recipes
			if (draggedObject is UMAWardrobeRecipe)
			{
				UMAWardrobeRecipe utr = draggedObject as UMAWardrobeRecipe;
				if (utr != null)
				{
					currentTarget.droppedItems.Add(utr);
				}
			}
			// Process Collections
			if (draggedObject is UMAWardrobeCollection)
			{
				UMAWardrobeCollection utr = draggedObject as UMAWardrobeCollection;
				if (utr != null)
				{
					currentTarget.droppedCollections.Add(utr);
				}
			}
			// Process Folders
			var path = AssetDatabase.GetAssetPath(draggedObject);
			if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
			{
				RecursiveScanFoldersForAssets(path);
			}
			return currentTarget.hasDrop;
		}

		public void RandomAvatarGUI(RandomAvatar ra)
		{
			bool del = false;
			GUIHelper.FoldoutBar(ref ra.GuiFoldout, ra.RaceName, out del);

			if (del) ra.Delete = true;

			if (!ra.GuiFoldout) return;

			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

			ra.Chance = EditorGUILayout.IntSlider("Weighted Chance", ra.Chance, 1, 100);

			SharedColorsGUI(ref ra.ColorsFoldout, ra.SharedColors, "Race Colors");

			ra.DnaFoldout = GUIHelper.FoldoutBar(ra.DnaFoldout, "DNA");
			if (ra.DnaFoldout) DNAGUI(ra);


			ra.WardrobeFoldout = GUIHelper.FoldoutBar(ra.WardrobeFoldout, "Wardrobe");
			if (ra.WardrobeFoldout) WardrobeGUI(ra);

			GUIHelper.EndVerticalPadded(10);
		}

		/// <summary>
		/// Handle RandomAvatar List of Wardrobe slots
		/// </summary>
		/// <param name="ra"></param>
		private void WardrobeGUI(RandomAvatar ra)
		{
			if (ra == null || ra.raceData == null || ra.raceData.wardrobeSlots == null || ra.raceData.wardrobeSlots.Count == 0)
			{
				EditorGUILayout.HelpBox("Race has no wardrobe slots (or race data is missing).", MessageType.Warning);
				return;
			}
			if (ra.currentWardrobeSlot < 0)
			{
				ra.currentWardrobeSlot = 0;
			}
			else if (ra.currentWardrobeSlot >= ra.raceData.wardrobeSlots.Count)
			{
				ra.currentWardrobeSlot = ra.raceData.wardrobeSlots.Count - 1;
			}

			// add a null slot for a 
			GUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Select Wardrobe Slot", GUILayout.ExpandWidth(false));
			ra.currentWardrobeSlot = EditorGUILayout.Popup(ra.currentWardrobeSlot, ra.raceData.wardrobeSlots.ToArray(), GUILayout.ExpandWidth(true));
			if (GUILayout.Button("Add Null", GUILayout.ExpandWidth(false)))
			{
				ra.RandomWardrobeSlots.Add(new RandomWardrobeSlot(null, ra.raceData.wardrobeSlots[ra.currentWardrobeSlot]));
				ra.RandomWardrobeSlots.Sort((x, y) => x.SortName.CompareTo(y.SortName));
			}
			GUILayout.EndHorizontal();
			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.75f, 0.75f));

			string lastSlot = "";

			foreach (RandomWardrobeSlot rws in ra.RandomWardrobeSlots)
			{
				if (rws.SlotName != lastSlot)
				{
					GUILayout.Label("[" + rws.SlotName + "]");
					lastSlot = rws.SlotName;
				}
				WardrobeSlotGUI(ra, rws);
			}
			GUIHelper.EndVerticalPadded(10);
		}

		/// <summary>
		/// Handle a Single Wardrobe slot
		/// </summary>
		/// <param name="ra"> RandomAvatar </param>
		/// <param name="rws"> RandomWardrobeSlot </param>
		public void WardrobeSlotGUI(RandomAvatar ra, RandomWardrobeSlot rws)
		{
			if (rws == null)
			{
				return;
			}
			// do random colors
			// show each possible item.
			string name = "<null>";
			if (rws.WardrobeSlot != null)
				name = rws.WardrobeSlot.name;

			GUIHelper.FoldoutBar(ref rws.GuiFoldout, name + " (" + rws.Chance + ")", out rws.Delete);

			if (!rws.GuiFoldout) return;

			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.75f, 0.75f));
			rws.Chance = EditorGUILayout.IntSlider("Weighted Chance", rws.Chance, 1, 100);
			if (rws.PossibleColors != null && rws.PossibleColors.Length > 0)
			{
				if (GUILayout.Button("Add Shared Color"))
				{
					rws.AddColorTable = true;
				}
				RandomColors delme = null;
				foreach (RandomColors rc in rws.Colors)
				{
					if (RandomColorsGUI(ra, rws, rc))
						delme = rc;
				}
				if (delme != null)
				{
					rws.Colors.Remove(delme);
					EditorUtility.SetDirty(this.target);
					AssetDatabase.SaveAssets();
				}
			}
			else
			{
				GUILayout.Label("Wardrobe Recipe has no Shared Colors");
			}
			GUIHelper.EndVerticalPadded(10);

		}

		private static int rangeIndex = 0;
        private void DNAGUI(RandomAvatar ra)
		{
			string[] ranges = { "50%", "60%", "70%", "80%", "90%", "100%" };
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.75f, 0.75f));
			// (popup with DNA names) and "Add" button.
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Select DNA", GUILayout.Width(100));
            ra.SelectedDNA = EditorGUILayout.Popup(ra.SelectedDNA, ra.PossibleDNA, GUILayout.ExpandWidth(true));
			rangeIndex = EditorGUILayout.Popup(rangeIndex, ranges, GUILayout.Width(60));
            bool addDNA = GUILayout.Button("Add DNA", EditorStyles.miniButton, GUILayout.Width(80));// GUIStyles.Popup?
			bool addAllDNA = GUILayout.Button("Add All", EditorStyles.miniButton, GUILayout.Width(80));// GUIStyles.Popup?
            EditorGUILayout.EndHorizontal();

			if (addDNA)
				ra.DNAAdd = ra.PossibleDNA[ra.SelectedDNA];

			if (addAllDNA)
			{
				
				foreach (string dnaName in ra.PossibleDNA)
				{
					bool alreadyExists = false;
					foreach (RandomDNA rd in ra.RandomDna)
					{
						if (rd.DnaName == dnaName)
						{
							alreadyExists = true;
							break;
						}
					}
					if (!alreadyExists)
					{
						RandomDNA r = new RandomDNA(dnaName);
						SetMinMax(r, rangeIndex);
                        ra.RandomDna.Add(r);
						ra.DnaChanged = true;
					}
				}
            }

            if (ra.RandomDna.Count == 0)
			{
				EditorGUILayout.LabelField("No Random DNA has been added");
				GUIHelper.EndVerticalPadded(10);
				return;
			}

			foreach (RandomDNA rd in ra.RandomDna)
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField(rd.DnaName, EditorStyles.miniLabel, GUILayout.Width(100));
				float lastMin = rd.MinValue;
				float lastMax = rd.MaxValue;
				EditorGUILayout.MinMaxSlider(ref rd.MinValue, ref rd.MaxValue, 0.0f, 1.0f);
				if (rd.MinValue != lastMin || rd.MaxValue != lastMax)
					ra.DnaChanged = true;
				rd.Delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
				string vals = rd.MinValue.ToString("N3") + " - " + rd.MaxValue.ToString("N3");
				EditorGUILayout.LabelField(vals, EditorStyles.miniTextField, GUILayout.Width(80));
				EditorGUILayout.EndHorizontal();
			}

			GUIHelper.EndVerticalPadded(10);
		}

		private void SetMinMax(RandomDNA rd, int randomAmount)
		{
			switch (randomAmount)
			{
				case 0:
					rd.MinValue = 0.25f;
					rd.MaxValue = 0.75f;
					break;
				case 1:
					rd.MinValue = 0.20f;
					rd.MaxValue = 0.80f;
					break;
				case 2:
					rd.MinValue = 0.15f;
					rd.MaxValue = 0.75f;
					break;
				case 3:
					rd.MinValue = 0.10f;
					rd.MaxValue = 0.80f;
					break;
				case 4:
					rd.MinValue = 0.05f;
					rd.MaxValue = 0.95f;
					break;
				case 5:
					rd.MinValue = 0.0f;
					rd.MaxValue = 1.0f;
					break;
			}
        }
        private void SharedColorsGUI(ref bool foldout, List<RandomColors> SharedColors, string label, GUIContent tooltip = default)
		{
			foldout = GUIHelper.FoldoutBar(foldout, label, tooltip);

			if (!foldout) return;

			if (displayHelp) GUILayout.Label("Shared Color names with Empty Color Table are discarded");

			if (SharedColors != null && SharedColors.Count > 0)
			{
				// List all Colors
				SharedColorsListGUI(SharedColors);
			}
			else
			{
				// No Colors
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("No colors defined");
				if (GUILayout.Button("Add Color"))
					SharedColors.Add(new RandomColors("", null));
				EditorGUILayout.EndHorizontal();
			}
		}

		/// <summary>
		/// Draw Existing Random Color's List
		/// <br>Handles features : Delete, Add</br>
		/// </summary>
		/// <param name="SharedColors"> List of Random Colors to display </param>
		private void SharedColorsListGUI(List<RandomColors> SharedColors)
		{
			colorsToDelete.Clear();

			GUIHelper.BeginVerticalPadded(8, new Color(0.75f, 0.75f, 0.75f));
			if (SharedColors != null && SharedColors.Count > 0)
			{
				foreach (RandomColors rc in SharedColors)
				{
					if (RandomColorsGUI(rc))
						colorsToDelete.Add(rc);
				}
			}

			if (GUILayout.Button("Add Color"))
				SharedColors.Add(new RandomColors("", null));

			if (colorsToDelete != null && colorsToDelete.Count > 0)
			{
				for (int i = 0; i < colorsToDelete.Count; i++)
					SharedColors.Remove(colorsToDelete[i]);
				colorsToDelete.Clear();
			}
			GUIHelper.EndVerticalPadded(8);
		}

		/// <summary>
		/// Shared Colors Tables GUI for Global Random Colors and Random Avatars
		/// <para>Shared Color Names are not restricted. </para>
		/// </summary>
		/// <param name="rc"> Random Colors </param>
		/// <returns> Delete Order (true : Must Delete) </returns>
		public bool RandomColorsGUI(RandomColors rc)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Name", GUILayout.Width(40));
			rc.ColorName = EditorGUILayout.DelayedTextField(rc.ColorName, EditorStyles.textField, GUILayout.Width(120));
			EditorGUILayout.LabelField("Color Table", GUILayout.Width(80));
			rc.ColorTable = (SharedColorTable)EditorGUILayout.ObjectField(rc.ColorTable, typeof(SharedColorTable), false, GUILayout.ExpandWidth(true));
			bool toBeDeleted = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			EditorGUILayout.EndHorizontal();
			return toBeDeleted;
		}

		/// <summary>
		/// Shared Colors Tables GUI for Wardrobe Slots
		/// <para>Shared Color Names are restricted to Wardrobe Recipes SharedColors Names.</para>
		/// </summary>
		/// <param name="ra"> Random Avatar </param>
		/// <param name="rws"> Random Avatar Wardrobe Slot </param>
		/// <param name="rc"> Random Colors </param>
		/// <returns> Delete Order (true : Must Delete) </returns>
		public bool RandomColorsGUI(RandomAvatar ra, RandomWardrobeSlot rws, RandomColors rc)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Shared Color", GUILayout.Width(80));
			rc.CurrentColor = EditorGUILayout.Popup(rc.CurrentColor, rws.PossibleColors, GUILayout.Width(80));
			rc.ColorName = rws.PossibleColors[rc.CurrentColor];
			EditorGUILayout.LabelField("Color Table", GUILayout.Width(80));
			rc.ColorTable = (SharedColorTable)EditorGUILayout.ObjectField(rc.ColorTable, typeof(SharedColorTable), false, GUILayout.ExpandWidth(true));
			bool retval = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			EditorGUILayout.EndHorizontal();
			return retval;
		}

		#endregion ---- ----- ----

		#region ------ Processing Methods -----

		private void ScheduleDelayedEnable()
		{
			if (delayedEnableHandler == null)
			{
				delayedEnableHandler = () =>
				{
					EditorApplication.delayCall -= delayedEnableHandler;
					delayedEnableHandler = null;

					if (this == null)
					{
						return;
					}

					if (!TryInitializeEditor())
					{
						ScheduleDelayedEnable();
						return;
					}

					Repaint();
				};
			}

			EditorApplication.delayCall -= delayedEnableHandler;
			EditorApplication.delayCall += delayedEnableHandler;
		}

		private bool EnsureEditorInitialized()
		{
			if (currentTarget != null && definitionProperty != null && currentTarget.raceDatas != null && currentTarget.races != null)
			{
				return true;
			}

			if (TryInitializeEditor())
			{
				return true;
			}

			ScheduleDelayedEnable();
			return false;
		}

		private bool TryInitializeEditor()
		{
			if (IsEditorBusy || target == null || serializedObject == null || serializedObject.targetObject == null)
			{
				return false;
			}

			currentTarget = target as UMARandomizer;
			if (currentTarget == null)
			{
				return false;
			}

			definitionProperty = serializedObject.FindProperty("definition");
			if (definitionProperty == null)
			{
				return false;
			}

			autoSave = false;
			if (colorsToDelete == null)
			{
				colorsToDelete = new List<RandomColors>();
			}

			if (!TryInitRaces(currentTarget))
			{
				return false;
			}

			AssemblyReloadEvents.beforeAssemblyReload -= HandleBeforeAssemblyReload;
			AssemblyReloadEvents.beforeAssemblyReload += HandleBeforeAssemblyReload;

			ContextMenu.OnUseDefinitionChange -= OnUseDefinitionChange;
			ContextMenu.OnUseGlobalColorsChange -= OnUseGlobalColorsChange;
			ContextMenu.UseDefinition = currentTarget.useDefinition;
			ContextMenu.UseGlobalColors = currentTarget.useGlobalColors;
			ContextMenu.OnUseDefinitionChange += OnUseDefinitionChange;
			ContextMenu.OnUseGlobalColorsChange += OnUseGlobalColorsChange;
			return true;
		}

		private void HandleBeforeAssemblyReload()
		{
			if (delayedEnableHandler != null)
			{
				EditorApplication.delayCall -= delayedEnableHandler;
				delayedEnableHandler = null;
			}

			ContextMenu.OnUseDefinitionChange -= OnUseDefinitionChange;
			ContextMenu.OnUseGlobalColorsChange -= OnUseGlobalColorsChange;
			currentTarget = null;
			definitionProperty = null;
		}

		private void SaveObject()
		{
			if (currentTarget == null)
			{
				return;
			}

			currentTarget.useDefinition = ContextMenu.UseDefinition;
			currentTarget.useGlobalColors = ContextMenu.UseGlobalColors;
			EditorUtility.SetDirty(currentTarget);
			AssetDatabase.SaveAssets();
			autoSave = false;
		}

		/// <summary>
		/// Fill in Races Drop-Down List with existing UMA Races
		/// </summary>
		/// <param name="randomizer"> Randomizer to initialize </param>
		private bool TryInitRaces(UMARandomizer randomizer)
		{
			if (randomizer == null)
			{
				return false;
			}

			UMAAssetIndexer assetIndexer;
			try
			{
				assetIndexer = UMAAssetIndexer.Instance;
			}
			catch
			{
				return false;
			}

			if (assetIndexer == null)
			{
				return false;
			}

			randomizer.raceDatas = assetIndexer.GetAllAssets<RaceData>() ?? new List<RaceData>();

			List<string> tmpRaces = new List<string>();

			foreach (RaceData race in randomizer.raceDatas)
			{
				if (race != null && !tmpRaces.Contains(race.name))
					tmpRaces.Add(race.name);
			}
			randomizer.races = tmpRaces.ToArray();
			return true;
		}

		protected void RecursiveScanFoldersForAssets(string path)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempRecipe = AssetDatabase.LoadAssetAtPath(assetFile, typeof(UMAWardrobeRecipe)) as UMAWardrobeRecipe;
				if (tempRecipe)
				{
					currentTarget.droppedItems.Add(tempRecipe);
				}
				var tempCollection = AssetDatabase.LoadAssetAtPath(assetFile, typeof(UMAWardrobeCollection)) as UMAWardrobeCollection;
				if (tempCollection)
				{
					currentTarget.droppedCollections.Add(tempCollection);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
			}
		}

		private void UpdateObject()
		{
			if (currentTarget == null)
			{
				return;
			}
			if (currentTarget.droppedItems == null) currentTarget.droppedItems = new List<UMAWardrobeRecipe>();
			if (currentTarget.droppedCollections == null) currentTarget.droppedCollections = new List<UMAWardrobeCollection>();
			if (currentTarget.RandomAvatars == null) currentTarget.RandomAvatars = new List<RandomAvatar>();

			try
			{
				ExtractRecipesFromCollections(currentTarget.droppedCollections, currentTarget.droppedItems);
			}
			catch (System.Exception ex)
			{
				Debug.LogException(ex);
				currentTarget.droppedCollections.Clear();
				currentTarget.droppedItems.Clear();
				return;
			}

			// Add any dropped items.
			int ChangeCount = currentTarget.droppedItems.Count;

			AddRecipesToCurrentRandomAvatar(currentTarget.droppedItems);

			ChangeCount += currentTarget.RandomAvatars.RemoveAll(x => x.Delete);
			foreach (RandomAvatar ra in currentTarget.RandomAvatars)
			{
				if (!string.IsNullOrEmpty(ra.DNAAdd))
				{
					ra.DnaChanged = true;
					ra.RandomDna.Add(new RandomDNA(ra.DNAAdd));
					ra.DNAAdd = "";
					ChangeCount++;
				}

				int DNAChangeCount = ra.RandomDna.RemoveAll(x => x.Delete);
				if (DNAChangeCount > 0)
				{
					ra.DnaChanged = true;
					ChangeCount++;
				}
				ChangeCount += ra.SharedColors.RemoveAll(x => x.Delete);
				ChangeCount += ra.RandomWardrobeSlots.RemoveAll(x => x.Delete);
				foreach (RandomWardrobeSlot rws in ra.RandomWardrobeSlots)
				{
					ChangeCount += rws.Colors.RemoveAll(x => x.Delete);
					if (rws.AddColorTable)
					{
						rws.Colors.Add(new RandomColors(rws));
						rws.AddColorTable = false;
						ChangeCount++;
					}
				}
			}

			if (ChangeCount > 0)
			{
				EditorUtility.SetDirty(currentTarget);
				AssetDatabase.SaveAssets();
			}
		}

		private void AddRecipesToCurrentRandomAvatar(List<UMAWardrobeRecipe> recipes)
		{
			if (recipes == null || recipes.Count == 0)
			{
				return;
			}
			if (currentTarget == null || currentTarget.raceDatas == null || currentTarget.raceDatas.Count == 0)
			{
				recipes.Clear();
				return;
			}
			if (currentTarget.currentRace < 0)
			{
				currentTarget.currentRace = 0;
			}
			else if (currentTarget.currentRace >= currentTarget.raceDatas.Count)
			{
				currentTarget.currentRace = currentTarget.raceDatas.Count - 1;
			}

			// Handle Foldout
			foreach (RandomAvatar rv in currentTarget.RandomAvatars)
			{
				rv.GuiFoldout = false;
				foreach (RandomWardrobeSlot rws in rv.RandomWardrobeSlots)
				{
					rws.GuiFoldout = false;
				}
			}

			// Get Current Avatar
			var raceData = currentTarget.raceDatas[currentTarget.currentRace];
			if (raceData == null)
			{
				recipes.Clear();
				return;
			}
			RandomAvatar ra = FindAvatar(raceData);

			// Add all the wardrobe items to Current Avatar
			foreach (UMAWardrobeRecipe uwr in recipes)
			{
				if (uwr == null)
				{
					continue;
				}
				if (RecipeCompatible(uwr, raceData))
				{
					RandomWardrobeSlot rws = new RandomWardrobeSlot(uwr, uwr.wardrobeSlot);
					ra.GuiFoldout = true;
					ra.RandomWardrobeSlots.Add(rws);
				}
			}

			// Sort the wardrobe slots
			ra.RandomWardrobeSlots.Sort((x, y) => x.SortName.CompareTo(y.SortName));
			recipes.Clear();

		}

		private void ExtractRecipesFromCollections(List<UMAWardrobeCollection> collections, List<UMAWardrobeRecipe> recipes)
		{
			if (collections == null || collections.Count == 0)
			{
				return;
			}
			if (recipes == null)
			{
				return;
			}
			if (currentTarget == null || currentTarget.raceDatas == null || currentTarget.raceDatas.Count == 0)
			{
				collections.Clear();
				return;
			}
			if (currentTarget.currentRace < 0)
			{
				currentTarget.currentRace = 0;
			}
			else if (currentTarget.currentRace >= currentTarget.raceDatas.Count)
			{
				currentTarget.currentRace = currentTarget.raceDatas.Count - 1;
			}
			var raceData = currentTarget.raceDatas[currentTarget.currentRace];
			if (raceData == null)
			{
				collections.Clear();
				return;
			}

			// Add all Recipes from Collections to the Recipes list
			foreach (UMAWardrobeCollection uwr in collections)
			{
				if (uwr == null)
				{
					continue;
				}
				List<WardrobeSettings> wardrobes;
				try
				{
					wardrobes = uwr.GetRacesWardrobeSet(raceData);
				}
				catch (System.Exception ex)
				{
					Debug.LogException(ex);
					continue;
				}
				if (wardrobes == null)
				{
					continue;
				}

				foreach (WardrobeSettings wardrobe in wardrobes)
				{
					if (string.IsNullOrEmpty(wardrobe.recipe))
					{
						continue;
					}
					UMAWardrobeRecipe recipe = UMAAssetIndexer.Instance.GetRecipe(wardrobe.recipe, false) as UMAWardrobeRecipe;
					if (recipe != null && !recipes.Contains(recipe))
						recipes.Add(recipe);
				}
			}
			collections.Clear();
		}

		private bool RecipeCompatible(UMAWardrobeRecipe uwr, RaceData raceData)
		{
			if (uwr == null || raceData == null)
			{
				return false;
			}
			// first, see if the recipe is directly compatible with the race.
			if (uwr.compatibleRaces == null)
			{
				return false;
			}
			foreach (string s in uwr.compatibleRaces)
			{
				if (s == raceData.raceName)
				{
					return true;
				}
				if (raceData.IsCrossCompatibleWith(s))
				{
					return true;
				}
			}
			return false;
		}

		private RandomAvatar FindAvatar(RaceData raceData)
		{
			if (raceData == null)
			{
				return null;
			}
			if (currentTarget == null)
			{
				return null;
			}
			if (currentTarget.RandomAvatars == null)
			{
				currentTarget.RandomAvatars = new List<RandomAvatar>();
			}
			// Is the current race defined?
			foreach (RandomAvatar ra in currentTarget.RandomAvatars)
			{
				if (raceData.raceName == ra.RaceName)
				{
					return ra;
				}
			}
			RandomAvatar rav = new RandomAvatar(raceData);
			currentTarget.RandomAvatars.Add(rav);
			return rav;
		}

		#endregion ------ ------------ -----
	}
}
