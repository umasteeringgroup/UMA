using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UMA.CharacterSystem;
using UMA.PoseTools;

namespace UMA.Editors
{
	internal class DuplicateRaceWizardWindow : EditorWindow
	{
		private class BlendshapeEntry
		{
			public string Name;
			public bool Selected;
			public float DefaultValue;
		}

		private class CompatibilityRaceEntry
		{
			public string RaceName;
			public bool Selected;
			public bool IsSourceRace;
		}

		private const int SourceRacePage = 0;
		private const int BlendshapeCollectionPage = 1;
		private const int SummaryPage = 2;
		private const float IntroColumnWidth = 260f;
		private const float WizardHorizontalPadding = 8f;
		private const float WizardColumnSpacing = 8f;

		private RaceData sourceRace;
		private UMARecipeBase sourceBaseRecipe;
		private string newRaceName;
		private string newBaseRecipeName;
		private UMABonePose selectedBonePose;
		private bool keepExistingPoseGroups = true;
		private bool baseRecipeNameUsesDefault = true;
		private int pageIndex;
		private Vector2 scrollPosition;
		private Vector2 compatibilityListScrollPosition;
		private Vector2 blendshapeListScrollPosition;
		private readonly List<BlendshapeEntry> blendshapeEntries = new List<BlendshapeEntry>();
		private readonly List<CompatibilityRaceEntry> compatibilityRaceEntries = new List<CompatibilityRaceEntry>();
		private readonly Dictionary<string, float> sourceBlendshapeDefaults = new Dictionary<string, float>(StringComparer.Ordinal);
		private readonly List<string> sourceUnbakedPatterns = new List<string>();

		public static void Open(RaceData race)
		{
			if (race == null)
			{
				EditorUtility.DisplayDialog("Duplicate Race", "Select one or more RaceData assets in the Project window.", "OK");
				return;
			}

			DuplicateRaceWizardWindow window = CreateInstance<DuplicateRaceWizardWindow>();
			window.titleContent = new GUIContent("Duplicate Race");
			window.minSize = new Vector2(800f, 300f);
			window.Initialize(race);
			window.ShowUtility();
		}

		private void Initialize(RaceData race)
		{
			sourceRace = race;
			sourceBaseRecipe = race != null ? race.baseRaceRecipe : null;

			string sourceRaceDisplayName = GetSafeSourceRaceName();

			newRaceName = sourceRaceDisplayName + "_Copy";
			newBaseRecipeName = GetDefaultBaseRecipeName(newRaceName);
			selectedBonePose = null;
			keepExistingPoseGroups = true;
			baseRecipeNameUsesDefault = true;

			RefreshCompatibilityRaceEntries();
			CacheSourceBlendshapeState();
			RefreshBlendshapeEntries();
		}

		private void OnGUI()
		{
			GUILayout.Space(WizardHorizontalPadding);
			DrawWizardPage();
			GUILayout.Space(6f);
			DrawNavigationButtons();
			GUILayout.Space(WizardHorizontalPadding);
		}

		private void DrawWizardPage()
		{
			EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
			GUILayout.Space(WizardHorizontalPadding);
			DrawWizardIntroColumn();
			GUILayout.Space(WizardColumnSpacing);
			DrawWizardSettingsColumn();
			GUILayout.Space(WizardHorizontalPadding);
			EditorGUILayout.EndHorizontal();
		}

		private void DrawWizardIntroColumn()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(IntroColumnWidth), GUILayout.ExpandHeight(true));
			EditorGUILayout.LabelField("Step " + (pageIndex + 1) + " of 3", EditorStyles.miniLabel);
			EditorGUILayout.LabelField(GetPageTitle(), EditorStyles.boldLabel);
			GUILayout.Space(6f);
			GUILayout.Label(GetPageIntroText(), EditorStyles.wordWrappedLabel);
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndVertical();
		}

		private void DrawWizardSettingsColumn()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			DrawSourceSummary();
			GUILayout.Space(6f);

			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, false, false);
			if (pageIndex == SourceRacePage)
			{
				DrawSourceRacePage();
			}
			else if (pageIndex == BlendshapeCollectionPage)
			{
				DrawBlendshapeCollectionPage();
			}
			else
			{
				DrawSummaryPage();
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void DrawSourceSummary()
		{
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("Source RaceData", sourceRace, typeof(RaceData), false);
				EditorGUILayout.ObjectField("Source Base Recipe", sourceBaseRecipe, typeof(UMARecipeBase), false);
			}

			EditorGUILayout.LabelField("Source Race Name", GetSafeSourceRaceName());
		}

		private void DrawSourceRacePage()
		{
			EditorGUILayout.LabelField("Source Race", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("RaceData", sourceRace != null ? sourceRace.name : "(None)");
			EditorGUILayout.LabelField("Race Name", GetSafeSourceRaceName());

			GUILayout.Space(8f);
			EditorGUILayout.LabelField("New Names", EditorStyles.boldLabel);
			string updatedRaceName = EditorGUILayout.TextField("New Race Name", newRaceName);
			if (!string.Equals(updatedRaceName, newRaceName, StringComparison.Ordinal))
			{
				newRaceName = updatedRaceName;
				if (baseRecipeNameUsesDefault)
				{
					newBaseRecipeName = GetDefaultBaseRecipeName(newRaceName);
				}
			}

			EditorGUILayout.HelpBox("The duplicated RaceData asset uses New Race Name for both the asset name and runtime race name. The legacy Race Name field is left empty.", MessageType.None);
			using (new EditorGUI.DisabledScope(sourceBaseRecipe == null))
			{
				string updatedBaseRecipeName = EditorGUILayout.TextField("New Base Race Recipe Name", newBaseRecipeName);
				if (!string.Equals(updatedBaseRecipeName, newBaseRecipeName, StringComparison.Ordinal))
				{
					newBaseRecipeName = updatedBaseRecipeName;
				}

				baseRecipeNameUsesDefault = string.Equals(GetTrimmedValue(newBaseRecipeName), GetDefaultBaseRecipeName(newRaceName), StringComparison.Ordinal);
			}

			if (sourceBaseRecipe == null)
			{
				EditorGUILayout.HelpBox("The source race does not reference a Base Race Recipe. The duplicated race will keep a null recipe reference, which is valid for FBX-route races and other partial setups.", MessageType.Info);
			}

			GUILayout.Space(8f);
			DrawPoseSetupSection();

			GUILayout.Space(8f);
			DrawCompatibilitySelectionPage();

			GUILayout.Space(8f);
			EditorGUILayout.LabelField("Target Assets", EditorStyles.boldLabel);
			DrawPathPreviewField("RaceData Asset Path", GetTargetRaceAssetPath());
			DrawPathPreviewField("Base Recipe Asset Path", GetTargetBaseRecipeAssetPathPreview());

			DrawValidationMessages();
		}

		private void DrawBlendshapeCollectionPage()
		{
			EditorGUILayout.LabelField("Blendshape Collection", EditorStyles.boldLabel);

			if (sourceBaseRecipe == null)
			{
				EditorGUILayout.HelpBox("No Base Race Recipe is assigned to the source race, so there are no slots to scan for blendshapes.", MessageType.Warning);
				return;
			}

			if (blendshapeEntries.Count == 0)
			{
				EditorGUILayout.HelpBox("No unique blendshapes were found on the slots referenced by the source race's Base Race Recipe.", MessageType.Info);
				return;
			}

			EditorGUILayout.LabelField("Unique Blendshapes", blendshapeEntries.Count.ToString());
			EditorGUILayout.LabelField("Selected", GetSelectedBlendshapeCount().ToString());

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select All", GUILayout.Width(90f)))
			{
				SetAllBlendshapeSelections(true);
			}
			if (GUILayout.Button("Select None", GUILayout.Width(90f)))
			{
				SetAllBlendshapeSelections(false);
			}
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();

			GUILayout.Space(4f);
			DrawBlendshapeHeader();
			blendshapeListScrollPosition = EditorGUILayout.BeginScrollView(blendshapeListScrollPosition, GUILayout.MinHeight(240f));
			for (int i = 0; i < blendshapeEntries.Count; i++)
			{
				DrawBlendshapeRow(blendshapeEntries[i]);
			}
			EditorGUILayout.EndScrollView();

			EditorGUILayout.HelpBox("Selected blendshape names are copied into the duplicated race. Non-zero default values are stored in RaceData.PrebakedBlendshapes because that is the UMA runtime field that carries name/value bake defaults.", MessageType.Info);
		}

		private void DrawSummaryPage()
		{
			EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("New Race Name", GetTrimmedValue(newRaceName));
			DrawPathPreviewField("New RaceData Asset Path", GetTargetRaceAssetPath());
			DrawPathPreviewField("New Base Recipe Asset Path", GetTargetBaseRecipeAssetPathPreview());
			EditorGUILayout.LabelField("Selected Blendshapes", GetSelectedBlendshapeCount().ToString());
			EditorGUILayout.LabelField("Cross Compatible With", GetSelectedCompatibilityRaceCount().ToString());
			EditorGUILayout.LabelField("Selected Races", GetSelectedCompatibilityRaceSummary());
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("Bone Pose", selectedBonePose, typeof(UMABonePose), false);
			}
			EditorGUILayout.LabelField("Keep Existing Pose Groups", keepExistingPoseGroups ? "Yes" : "No");
			if (selectedBonePose != null)
			{
				DrawPathPreviewField("Pose DNA Asset Path", GetTargetPoseDnaAssetPathPreview());
				DrawPathPreviewField("Pose Group Asset Path", GetTargetPoseGroupAssetPathPreview());
			}

			if (sourceBaseRecipe == null)
			{
				EditorGUILayout.HelpBox("The source race has no Base Race Recipe. The duplicated race will be created without one unless you add a recipe later.", MessageType.Info);
			}

			DrawValidationMessages();
			DrawOverwriteWarnings();
		}

		private void DrawBlendshapeHeader()
		{
			Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
			EditorGUI.LabelField(new Rect(rowRect.x + 4f, rowRect.y, 36f, rowRect.height), "Use", EditorStyles.miniBoldLabel);
			EditorGUI.LabelField(new Rect(rowRect.x + 44f, rowRect.y, rowRect.width - 196f, rowRect.height), "Blendshape", EditorStyles.miniBoldLabel);
			EditorGUI.LabelField(new Rect(rowRect.x + rowRect.width - 144f, rowRect.y, 144f, rowRect.height), "Default (0..1)", EditorStyles.miniBoldLabel);
		}

		private void DrawBlendshapeRow(BlendshapeEntry entry)
		{
			EditorGUILayout.BeginHorizontal();
			entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(36f));
			EditorGUILayout.LabelField(entry.Name, GUILayout.MinWidth(180f));
			using (new EditorGUI.DisabledScope(!entry.Selected))
			{
				entry.DefaultValue = GUILayout.HorizontalSlider(entry.DefaultValue, 0f, 1f, GUILayout.Width(104f));
				GUILayout.Label(entry.DefaultValue.ToString("0.00"), GUILayout.Width(36f));
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawCompatibilitySelectionPage()
		{
			EditorGUILayout.LabelField("Cross Compatibility", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Mark race as compatible with one or more existing races. The source race is selected by default and the chosen race names are written into the duplicated race's Cross Compatibility Settings.", MessageType.Info);

			if (compatibilityRaceEntries.Count == 0)
			{
				EditorGUILayout.HelpBox("No RaceData assets were found to populate the cross-compatibility list.", MessageType.Warning);
				return;
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Source Only", GUILayout.Width(90f)))
			{
				SelectSourceRaceOnly();
			}
			if (GUILayout.Button("Select None", GUILayout.Width(90f)))
			{
				SetAllCompatibilitySelections(false);
			}
			GUILayout.FlexibleSpace();
			EditorGUILayout.EndHorizontal();

			compatibilityListScrollPosition = EditorGUILayout.BeginScrollView(compatibilityListScrollPosition, GUILayout.MinHeight(140f));
			for (int i = 0; i < compatibilityRaceEntries.Count; i++)
			{
				DrawCompatibilityRaceRow(compatibilityRaceEntries[i]);
			}
			EditorGUILayout.EndScrollView();
		}

		private void DrawCompatibilityRaceRow(CompatibilityRaceEntry entry)
		{
			EditorGUILayout.BeginHorizontal();
			entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18f));
			EditorGUILayout.LabelField(entry.IsSourceRace ? entry.RaceName + " (source)" : entry.RaceName);
			EditorGUILayout.EndHorizontal();
		}

		private void DrawPoseSetupSection()
		{
			EditorGUILayout.LabelField("Pose Setup", EditorStyles.boldLabel);
			selectedBonePose = EditorGUILayout.ObjectField("Bone Pose", selectedBonePose, typeof(UMABonePose), false) as UMABonePose;
			keepExistingPoseGroups = EditorGUILayout.Toggle("Keep Existing Pose Groups", keepExistingPoseGroups);

			if (selectedBonePose == null)
			{
				EditorGUILayout.HelpBox("Optionally assign a bone pose to create a pose DNA asset and a pose group for the duplicated race. Keep Existing Pose Groups only affects copied bone-pose converter controllers.", MessageType.None);
				return;
			}

			if (sourceRace != null && sourceRace.useNewDNA)
			{
				EditorGUILayout.HelpBox("This source race uses the new DNA system. The wizard can only create pose groups for races that use legacy DNA converters.", MessageType.Warning);
				return;
			}

			if (sourceRace != null && sourceRace.disableDNAConverters)
			{
				EditorGUILayout.HelpBox("The source race has legacy DNA converters disabled. The duplicate will re-enable them so the new pose group can work.", MessageType.Info);
			}

			DrawPathPreviewField("Pose DNA Asset Path", GetTargetPoseDnaAssetPathPreview());
			DrawPathPreviewField("Pose Group Asset Path", GetTargetPoseGroupAssetPathPreview());
			EditorGUILayout.HelpBox("The wizard will create a Dynamic DNA asset with one pose DNA name and a pose-group controller that drives the selected bone pose from that DNA.", MessageType.Info);
		}

		private void DrawNavigationButtons()
		{
			EditorGUILayout.BeginHorizontal();
			GUILayout.Space(WizardHorizontalPadding);
			using (new EditorGUI.DisabledScope(pageIndex == SourceRacePage))
			{
				if (GUILayout.Button("Previous", GUILayout.Width(90f)))
				{
					SetPage(Mathf.Max(SourceRacePage, pageIndex - 1));
					GUI.FocusControl(null);
				}
			}

			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Cancel", GUILayout.Width(90f)))
			{
				Close();
			}

			using (new EditorGUI.DisabledScope(!CanContinue()))
			{
				if (pageIndex == SummaryPage)
				{
					if (GUILayout.Button("Create", GUILayout.Width(90f)))
					{
						CreateDuplicateRace();
					}
				}
				else if (GUILayout.Button("Next", GUILayout.Width(90f)))
				{
					SetPage(Mathf.Min(SummaryPage, pageIndex + 1));
					GUI.FocusControl(null);
				}
			}
			GUILayout.Space(WizardHorizontalPadding);
			EditorGUILayout.EndHorizontal();
		}

		private void SetPage(int newPageIndex)
		{
			if (pageIndex == newPageIndex)
			{
				return;
			}

			pageIndex = newPageIndex;
			scrollPosition = Vector2.zero;
			if (pageIndex != SourceRacePage)
			{
				compatibilityListScrollPosition = Vector2.zero;
			}
			if (pageIndex != BlendshapeCollectionPage)
			{
				blendshapeListScrollPosition = Vector2.zero;
			}
		}

		private string GetPageTitle()
		{
			if (pageIndex == SourceRacePage)
			{
				return "Source Race and Names";
			}

			if (pageIndex == BlendshapeCollectionPage)
			{
				return "Blendshape Collection";
			}

			return "Summary and Create";
		}

		private string GetPageIntroText()
		{
			if (pageIndex == SourceRacePage)
			{
				return "Review the source RaceData that was selected in the Project window, choose the new RaceData name, optionally override the default Base Race Recipe name, optionally create a bone-pose-driven pose group, and decide which races the duplicate should be marked compatible with.";
			}

			if (pageIndex == BlendshapeCollectionPage)
			{
				return "Scan the source race's Base Race Recipe slots, collect every unique blendshape name, and choose which ones should be carried into the duplicated race along with their default values.";
			}

			return "Review the final target asset paths, overwrite warnings, and blendshape count before duplicating the RaceData and its Base Race Recipe.";
		}

		private void CacheSourceBlendshapeState()
		{
			sourceBlendshapeDefaults.Clear();
			sourceUnbakedPatterns.Clear();

			if (sourceRace == null)
			{
				return;
			}

			if (sourceRace.PrebakedBlendshapes != null)
			{
				for (int i = 0; i < sourceRace.PrebakedBlendshapes.Count; i++)
				{
					SlotBurnOptions option = sourceRace.PrebakedBlendshapes[i];
					if (option == null || string.IsNullOrWhiteSpace(option.BlendShape))
					{
						continue;
					}

					if (!sourceBlendshapeDefaults.ContainsKey(option.BlendShape))
					{
						sourceBlendshapeDefaults.Add(option.BlendShape, option.value);
					}
				}
			}

			if (sourceRace.UnbakedShapesToInclude != null)
			{
				for (int i = 0; i < sourceRace.UnbakedShapesToInclude.Count; i++)
				{
					string pattern = sourceRace.UnbakedShapesToInclude[i];
					if (!string.IsNullOrWhiteSpace(pattern))
					{
						sourceUnbakedPatterns.Add(pattern);
					}
				}
			}
		}

		private void RefreshBlendshapeEntries()
		{
			blendshapeEntries.Clear();

			if (sourceBaseRecipe == null)
			{
				return;
			}

			UMAData.UMARecipe sourceRecipe = sourceBaseRecipe.GetCachedRecipe(true);
			if (sourceRecipe == null)
			{
				return;
			}

			SlotData[] slots = sourceRecipe.GetAllSlots();
			if (slots == null || slots.Length == 0)
			{
				return;
			}

			HashSet<string> uniqueBlendshapes = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < slots.Length; i++)
			{
				SlotData slot = slots[i];
				if (slot == null || slot.asset == null || UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData))
				{
					continue;
				}

				UMABlendShape[] shapes = slot.asset.meshData.blendShapes;
				if (shapes == null || shapes.Length == 0)
				{
					continue;
				}

				for (int shapeIndex = 0; shapeIndex < shapes.Length; shapeIndex++)
				{
					UMABlendShape shape = shapes[shapeIndex];
					if (shape == null || string.IsNullOrWhiteSpace(shape.shapeName))
					{
						continue;
					}

					uniqueBlendshapes.Add(shape.shapeName);
				}
			}

			List<string> sortedBlendshapes = new List<string>(uniqueBlendshapes);
			sortedBlendshapes.Sort(StringComparer.Ordinal);
			for (int i = 0; i < sortedBlendshapes.Count; i++)
			{
				string blendshapeName = sortedBlendshapes[i];
				float defaultValue = sourceBlendshapeDefaults.TryGetValue(blendshapeName, out float storedValue) ? Mathf.Clamp01(storedValue) : 0f;
				bool selected = sourceBlendshapeDefaults.ContainsKey(blendshapeName) || MatchesSourceUnbakedPattern(blendshapeName);
				blendshapeEntries.Add(new BlendshapeEntry
				{
					Name = blendshapeName,
					Selected = selected,
					DefaultValue = defaultValue
				});
			}
		}

		private void RefreshCompatibilityRaceEntries()
		{
			compatibilityRaceEntries.Clear();
			string sourceRaceName = GetSafeSourceRaceName();
			HashSet<string> seenRaceNames = new HashSet<string>(StringComparer.Ordinal);
			string[] raceGuids = AssetDatabase.FindAssets("t:RaceData");
			List<string> raceNames = new List<string>();

			for (int i = 0; i < raceGuids.Length; i++)
			{
				string racePath = AssetDatabase.GUIDToAssetPath(raceGuids[i]);
				RaceData raceAsset = AssetDatabase.LoadAssetAtPath<RaceData>(racePath);
				if (raceAsset == null)
				{
					continue;
				}

				string raceName = !string.IsNullOrWhiteSpace(raceAsset.raceName) ? raceAsset.raceName : raceAsset.name;
				if (string.IsNullOrWhiteSpace(raceName) || !seenRaceNames.Add(raceName))
				{
					continue;
				}

				raceNames.Add(raceName);
			}

			raceNames.Sort(StringComparer.Ordinal);
			for (int i = 0; i < raceNames.Count; i++)
			{
				string raceName = raceNames[i];
				compatibilityRaceEntries.Add(new CompatibilityRaceEntry
				{
					RaceName = raceName,
					Selected = string.Equals(raceName, sourceRaceName, StringComparison.Ordinal),
					IsSourceRace = string.Equals(raceName, sourceRaceName, StringComparison.Ordinal)
				});
			}

			if (!string.IsNullOrWhiteSpace(sourceRaceName) && compatibilityRaceEntries.FindIndex(entry => entry.IsSourceRace) < 0)
			{
				compatibilityRaceEntries.Insert(0, new CompatibilityRaceEntry
				{
					RaceName = sourceRaceName,
					Selected = true,
					IsSourceRace = true
				});
			}
		}

		private bool MatchesSourceUnbakedPattern(string blendshapeName)
		{
			for (int i = 0; i < sourceUnbakedPatterns.Count; i++)
			{
				string pattern = sourceUnbakedPatterns[i];
				if (string.IsNullOrWhiteSpace(pattern))
				{
					continue;
				}

				try
				{
					if (Regex.IsMatch(blendshapeName, pattern))
					{
						return true;
					}
				}
				catch (ArgumentException)
				{
					if (string.Equals(blendshapeName, pattern, StringComparison.Ordinal))
					{
						return true;
					}
				}
			}

			return false;
		}

		private void SetAllBlendshapeSelections(bool selected)
		{
			for (int i = 0; i < blendshapeEntries.Count; i++)
			{
				blendshapeEntries[i].Selected = selected;
			}
		}

		private void SetAllCompatibilitySelections(bool selected)
		{
			for (int i = 0; i < compatibilityRaceEntries.Count; i++)
			{
				compatibilityRaceEntries[i].Selected = selected;
			}
		}

		private void SelectSourceRaceOnly()
		{
			for (int i = 0; i < compatibilityRaceEntries.Count; i++)
			{
				compatibilityRaceEntries[i].Selected = compatibilityRaceEntries[i].IsSourceRace;
			}
		}

		private int GetSelectedBlendshapeCount()
		{
			int count = 0;
			for (int i = 0; i < blendshapeEntries.Count; i++)
			{
				if (blendshapeEntries[i].Selected)
				{
					count++;
				}
			}

			return count;
		}

		private int GetSelectedCompatibilityRaceCount()
		{
			int count = 0;
			for (int i = 0; i < compatibilityRaceEntries.Count; i++)
			{
				if (compatibilityRaceEntries[i].Selected)
				{
					count++;
				}
			}

			return count;
		}

		private string GetSelectedCompatibilityRaceSummary()
		{
			List<string> selectedRaceNames = GetSelectedCompatibilityRaceNames();
			if (selectedRaceNames.Count == 0)
			{
				return "(None)";
			}

			return string.Join(", ", selectedRaceNames.ToArray());
		}

		private List<string> GetSelectedCompatibilityRaceNames()
		{
			List<string> selectedRaceNames = new List<string>();
			for (int i = 0; i < compatibilityRaceEntries.Count; i++)
			{
				CompatibilityRaceEntry entry = compatibilityRaceEntries[i];
				if (!entry.Selected || string.IsNullOrWhiteSpace(entry.RaceName))
				{
					continue;
				}

				selectedRaceNames.Add(entry.RaceName);
			}

			return selectedRaceNames;
		}

		private bool CanContinue()
		{
			return GetBlockingMessages().Count == 0;
		}

		private List<string> GetBlockingMessages()
		{
			List<string> messages = new List<string>();
			if (sourceRace == null)
			{
				messages.Add("No source RaceData asset is available. Select a RaceData asset in the Project window and reopen the wizard.");
				return messages;
			}

			if (!IsValidName(newRaceName))
			{
				messages.Add("Enter a valid New Race Name.");
			}
			else if (string.Equals(GetTrimmedValue(newRaceName), sourceRace.raceName, StringComparison.OrdinalIgnoreCase))
			{
				messages.Add("New Race Name must be different from the source race name.");
			}

			if (sourceBaseRecipe != null && !IsValidName(newBaseRecipeName))
			{
				messages.Add("Enter a valid New Base Race Recipe Name.");
			}

			if (selectedBonePose != null && sourceRace.useNewDNA)
			{
				messages.Add("Bone Pose setup is only supported for races that use legacy DNA converters.");
			}

			string targetRacePath = GetTargetRaceAssetPath();
			if (PathsEqual(targetRacePath, GetSourceRaceAssetPath()))
			{
				messages.Add("The new RaceData asset path matches the source RaceData asset. Choose a different New Race Name.");
			}

			if (!CanUseTargetPath(targetRacePath, sourceRace.GetType(), out string raceTargetMessage))
			{
				messages.Add(raceTargetMessage);
			}

			if (sourceBaseRecipe != null)
			{
				string targetRecipePath = GetTargetBaseRecipeAssetPath();
				if (PathsEqual(targetRecipePath, GetSourceBaseRecipeAssetPath()))
				{
					messages.Add("The new Base Race Recipe asset path matches the source recipe asset. Choose a different Base Race Recipe name.");
				}

				if (!CanUseTargetPath(targetRecipePath, sourceBaseRecipe.GetType(), out string recipeTargetMessage))
				{
					messages.Add(recipeTargetMessage);
				}
			}

			if (selectedBonePose != null)
			{
				string targetPoseDnaPath = GetTargetPoseDnaAssetPath();
				if (!CanUseTargetPath(targetPoseDnaPath, typeof(DynamicUMADnaAsset), out string poseDnaTargetMessage))
				{
					messages.Add(poseDnaTargetMessage);
				}

				string targetPoseGroupPath = GetTargetPoseGroupAssetPath();
				if (!CanUseTargetPath(targetPoseGroupPath, typeof(DynamicDNAConverterController), out string poseGroupTargetMessage))
				{
					messages.Add(poseGroupTargetMessage);
				}
			}

			return messages;
		}

		private void DrawValidationMessages()
		{
			List<string> messages = GetBlockingMessages();
			for (int i = 0; i < messages.Count; i++)
			{
				EditorGUILayout.HelpBox(messages[i], MessageType.Error);
			}
		}

		private void DrawOverwriteWarnings()
		{
			List<string> overwriteWarnings = GetOverwriteWarnings();
			for (int i = 0; i < overwriteWarnings.Count; i++)
			{
				EditorGUILayout.HelpBox(overwriteWarnings[i], MessageType.Warning);
			}
		}

		private List<string> GetOverwriteWarnings()
		{
			List<string> warnings = new List<string>();
			if (TargetExistsAsCompatibleType(GetTargetRaceAssetPath(), sourceRace != null ? sourceRace.GetType() : typeof(RaceData)))
			{
				warnings.Add("The target RaceData asset already exists and will require overwrite confirmation before the duplicate is saved.");
			}

			if (sourceBaseRecipe != null && TargetExistsAsCompatibleType(GetTargetBaseRecipeAssetPath(), sourceBaseRecipe.GetType()))
			{
				warnings.Add("The target Base Race Recipe asset already exists and will require overwrite confirmation before the duplicate is saved.");
			}

			if (selectedBonePose != null && TargetExistsAsCompatibleType(GetTargetPoseDnaAssetPath(), typeof(DynamicUMADnaAsset)))
			{
				warnings.Add("The target Pose DNA asset already exists and will require overwrite confirmation before the duplicate is saved.");
			}

			if (selectedBonePose != null && TargetExistsAsCompatibleType(GetTargetPoseGroupAssetPath(), typeof(DynamicDNAConverterController)))
			{
				warnings.Add("The target Pose Group asset already exists and will require overwrite confirmation before the duplicate is saved.");
			}

			return warnings;
		}

		private void CreateDuplicateRace()
		{
			List<string> blockingMessages = GetBlockingMessages();
			if (blockingMessages.Count > 0)
			{
				EditorUtility.DisplayDialog("Duplicate Race", string.Join("\n\n", blockingMessages.ToArray()), "OK");
				return;
			}

			if (!ConfirmOverwriteIfNeeded())
			{
				return;
			}

			string raceAssetPath = GetTargetRaceAssetPath();
			string recipeAssetPath = GetTargetBaseRecipeAssetPath();
			string poseDnaAssetPath = GetTargetPoseDnaAssetPath();
			string poseGroupAssetPath = GetTargetPoseGroupAssetPath();
			RaceData duplicatedRace = null;
			UMARecipeBase duplicatedRecipe = null;
			DynamicUMADnaAsset poseDnaAsset = null;
			DynamicDNAConverterController poseGroupAsset = null;
			bool createdRaceAsset = false;
			bool createdRecipeAsset = false;
			bool createdPoseDnaAsset = false;
			bool createdPoseGroupAsset = false;

			try
			{
				duplicatedRace = GetOrCreateRaceAsset(raceAssetPath, out createdRaceAsset);
				if (duplicatedRace == null)
				{
					throw new InvalidOperationException("Unable to create or load the duplicated RaceData asset.");
				}

				if (sourceBaseRecipe != null)
				{
					duplicatedRecipe = GetOrCreateRecipeAsset(recipeAssetPath, out createdRecipeAsset);
					if (duplicatedRecipe == null)
					{
						throw new InvalidOperationException("Unable to create or load the duplicated Base Race Recipe asset.");
					}
				}

				// Copy the full serialized RaceData first, then patch the fields that must change for the duplicate.
				EditorUtility.CopySerialized(sourceRace, duplicatedRace);
				duplicatedRace.name = GetTrimmedValue(newRaceName);
				duplicatedRace._oldRaceName = string.Empty;
				duplicatedRace.baseRaceRecipe = duplicatedRecipe;
				duplicatedRace.FixupRotations = sourceRace.FixupRotations;
				duplicatedRace.useFbxRoute = sourceRace.useFbxRoute;
				duplicatedRace.useNewDNA = sourceRace.useNewDNA;
				duplicatedRace.expressionSet = sourceRace.expressionSet;
				duplicatedRace.useManualRendererBounds = sourceRace.useManualRendererBounds;
				duplicatedRace.manualRendererBounds = sourceRace.manualRendererBounds;
				duplicatedRace.manualRendererBoundsCenter = sourceRace.manualRendererBoundsCenter;

				if (duplicatedRecipe != null)
				{
					DuplicateBaseRaceRecipe(duplicatedRecipe, duplicatedRace);
					duplicatedRace.baseRaceRecipe = duplicatedRecipe;
				}

				if (selectedBonePose != null)
				{
					poseDnaAsset = GetOrCreatePoseDnaAsset(poseDnaAssetPath, out createdPoseDnaAsset);
					if (poseDnaAsset == null)
					{
						throw new InvalidOperationException("Unable to create or load the duplicated Pose DNA asset.");
					}

					poseGroupAsset = GetOrCreatePoseGroupAsset(poseGroupAssetPath, out createdPoseGroupAsset);
					if (poseGroupAsset == null)
					{
						throw new InvalidOperationException("Unable to create or load the duplicated Pose Group asset.");
					}
				}

				ApplySelectedBlendshapeSettings(duplicatedRace);
				ApplyPoseGroupSettings(duplicatedRace, poseDnaAsset, poseGroupAsset);
				duplicatedRace.SetCrossCompatibleRaces(GetSelectedCompatibilityRaceNames());

				EditorUtility.SetDirty(duplicatedRace);
				if (duplicatedRecipe != null)
				{
					EditorUtility.SetDirty(duplicatedRecipe);
				}
				if (poseDnaAsset != null)
				{
					EditorUtility.SetDirty(poseDnaAsset);
				}
				if (poseGroupAsset != null)
				{
					EditorUtility.SetDirty(poseGroupAsset);
				}

				AssetDatabase.SaveAssets();
				if (duplicatedRecipe is UMATextRecipe duplicatedTextRecipe)
				{
					UMAUpdateProcessor.UpdateRecipe(duplicatedTextRecipe);
				}
				UMAUpdateProcessor.UpdateRace(duplicatedRace);
				AddAssetsToGlobalLibrary(duplicatedRace, duplicatedRecipe, poseDnaAsset, poseGroupAsset);
				AssetDatabase.Refresh();

				Close();
				InspectorUtlity.InspectTarget(duplicatedRace);
			}
			catch (Exception ex)
			{
				if (createdPoseGroupAsset && !string.IsNullOrEmpty(poseGroupAssetPath))
				{
					AssetDatabase.DeleteAsset(poseGroupAssetPath);
				}
				if (createdPoseDnaAsset && !string.IsNullOrEmpty(poseDnaAssetPath))
				{
					AssetDatabase.DeleteAsset(poseDnaAssetPath);
				}
				if (createdRecipeAsset && !string.IsNullOrEmpty(recipeAssetPath))
				{
					AssetDatabase.DeleteAsset(recipeAssetPath);
				}
				if (createdRaceAsset && !string.IsNullOrEmpty(raceAssetPath))
				{
					AssetDatabase.DeleteAsset(raceAssetPath);
				}
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				Debug.LogException(ex);
				EditorUtility.DisplayDialog("Duplicate Race", "Failed to duplicate the selected race.\n\n" + ex.Message, "OK");
			}
		}

		private void ApplyPoseGroupSettings(RaceData duplicatedRace, DynamicUMADnaAsset poseDnaAsset, DynamicDNAConverterController poseGroupAsset)
		{
			if (duplicatedRace == null || duplicatedRace.useNewDNA)
			{
				return;
			}

			bool originalDisableDNAConverters = duplicatedRace.disableDNAConverters;
			if (originalDisableDNAConverters)
			{
				duplicatedRace.disableDNAConverters = false;
			}

			List<DynamicDNAConverterController> converters = new List<DynamicDNAConverterController>();
			DynamicDNAConverterController[] existingConverters = duplicatedRace.dnaConverterList;
			if (existingConverters != null)
			{
				for (int i = 0; i < existingConverters.Length; i++)
				{
					DynamicDNAConverterController converter = existingConverters[i];
					if (converter == null)
					{
						continue;
					}

					if (!keepExistingPoseGroups && ControllerContainsBonePosePlugin(converter))
					{
						continue;
					}

					converters.Add(converter);
				}
			}

			if (selectedBonePose != null)
			{
				if (poseDnaAsset == null || poseGroupAsset == null)
				{
					throw new InvalidOperationException("Pose setup assets were not created correctly.");
				}

				ConfigurePoseDnaAsset(poseDnaAsset);
				ConfigurePoseGroupAsset(poseGroupAsset, poseDnaAsset);
				converters.Add(poseGroupAsset);
				duplicatedRace.disableDNAConverters = false;
			}
			else
			{
				duplicatedRace.disableDNAConverters = originalDisableDNAConverters;
			}

			duplicatedRace.dnaConverterList = converters.ToArray();
		}

		private void AddAssetsToGlobalLibrary(params UnityEngine.Object[] assets)
		{
			UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
			if (indexer == null)
			{
				Debug.LogWarning("Duplicate Race: UMAAssetIndexer.Instance was unavailable, so the duplicated assets were not added to the UMA Global Library.");
				return;
			}

			for (int i = 0; i < assets.Length; i++)
			{
				UnityEngine.Object asset = assets[i];
				if (asset == null)
				{
					continue;
				}

				Type assetType = asset.GetType();
				if (!indexer.IsIndexedType(assetType))
				{
					continue;
				}

				indexer.EvilAddAsset(assetType, asset);
			}

			indexer.ForceSave();
		}

		private string GetDefaultBaseRecipeName(string raceName)
		{
			string trimmedRaceName = GetTrimmedValue(raceName);
			return string.IsNullOrEmpty(trimmedRaceName)
				? "BaseRaceRecipe_Recipe"
				: "BaseRaceRecipe_" + trimmedRaceName + "_Recipe";
		}

		private string GetDefaultPoseDnaAssetName()
		{
			string trimmedRaceName = GetTrimmedValue(newRaceName);
			return string.IsNullOrEmpty(trimmedRaceName)
				? "PoseDNA"
				: "PoseDNA_" + trimmedRaceName;
		}

		private string GetDefaultPoseGroupAssetName()
		{
			string trimmedRaceName = GetTrimmedValue(newRaceName);
			return string.IsNullOrEmpty(trimmedRaceName)
				? "PoseGroup"
				: "PoseGroup_" + trimmedRaceName;
		}

		private string GetPoseDnaName()
		{
			if (selectedBonePose != null && !string.IsNullOrWhiteSpace(selectedBonePose.name))
			{
				return selectedBonePose.name;
			}

			string trimmedRaceName = GetTrimmedValue(newRaceName);
			return string.IsNullOrEmpty(trimmedRaceName) ? "Pose" : trimmedRaceName + "_Pose";
		}

		private void ConfigurePoseDnaAsset(DynamicUMADnaAsset poseDnaAsset)
		{
			poseDnaAsset.name = GetDefaultPoseDnaAssetName();
			poseDnaAsset.Names = new[] { GetPoseDnaName() };
			poseDnaAsset.SetCurrentAssetPath();
			EditorUtility.SetDirty(poseDnaAsset);
		}

		private void ConfigurePoseGroupAsset(DynamicDNAConverterController poseGroupAsset, DynamicUMADnaAsset poseDnaAsset)
		{
			poseGroupAsset.name = GetDefaultPoseGroupAssetName();
			poseGroupAsset.DNAAsset = poseDnaAsset;
			SetPoseGroupDisplayValue(poseGroupAsset, "Pose");
			ClearPlugins(poseGroupAsset);

			DNAEvaluatorList modifyingDna = new DNAEvaluatorList(new List<DNAEvaluator>
			{
				new DNAEvaluator(GetPoseDnaName(), DNAEvaluationGraph.Default, 1f)
			});
			poseGroupAsset.AddBonePoseConverter(selectedBonePose, 0f, modifyingDna);
			poseGroupAsset.ValidatePlugins();
			EditorUtility.SetDirty(poseGroupAsset);
		}

		private void SetPoseGroupDisplayValue(DynamicDNAConverterController poseGroupAsset, string displayValue)
		{
			SerializedObject controllerObject = new SerializedObject(poseGroupAsset);
			SerializedProperty displayValueProperty = controllerObject.FindProperty("_displayValue");
			if (displayValueProperty != null)
			{
				displayValueProperty.stringValue = displayValue;
				controllerObject.ApplyModifiedPropertiesWithoutUndo();
			}
		}

		private void ClearPlugins(DynamicDNAConverterController controller)
		{
			List<DynamicDNAPlugin> plugins = new List<DynamicDNAPlugin>(controller.GetPlugins());
			for (int i = plugins.Count - 1; i >= 0; i--)
			{
				if (plugins[i] != null)
				{
					controller.DeletePlugin(plugins[i]);
				}
			}
		}

		private bool ControllerContainsBonePosePlugin(DynamicDNAConverterController controller)
		{
			return controller != null && controller.GetPlugins(typeof(BonePoseDNAConverterPlugin)).Count > 0;
		}

		private void DuplicateBaseRaceRecipe(UMARecipeBase duplicatedRecipe, RaceData duplicatedRace)
		{
			// Copy the concrete recipe asset first so non-packed serialized fields survive, then repack with the duplicated race name.
			EditorUtility.CopySerialized(sourceBaseRecipe, duplicatedRecipe);
			duplicatedRecipe.name = GetTrimmedValue(newBaseRecipeName);

			UMAData.UMARecipe recipeCopy = new UMAData.UMARecipe();
			sourceBaseRecipe.Load(recipeCopy, true);
			recipeCopy.raceData = duplicatedRace;
			duplicatedRecipe.Save(recipeCopy);

			if (duplicatedRecipe is UMATextRecipe duplicatedTextRecipe)
			{
				ReplaceCompatibleRaceNames(duplicatedTextRecipe.compatibleRaces, sourceRace.raceName, duplicatedRace.raceName);
			}
		}

		private void ApplySelectedBlendshapeSettings(RaceData duplicatedRace)
		{
			if (duplicatedRace == null)
			{
				return;
			}

			if (blendshapeEntries.Count == 0)
			{
				return;
			}

			duplicatedRace.PrebakedBlendshapes = new List<SlotBurnOptions>();
			duplicatedRace.UnbakedShapesToInclude = new List<string>();

			for (int i = 0; i < blendshapeEntries.Count; i++)
			{
				BlendshapeEntry entry = blendshapeEntries[i];
				if (!entry.Selected)
				{
					continue;
				}

				// UMA stores value-bearing prebaked defaults here; keep zero-value selections unbaked-only.
				if (Mathf.Abs(entry.DefaultValue) > 0.0001f)
				{
					duplicatedRace.PrebakedBlendshapes.Add(new SlotBurnOptions
					{
						BlendShape = entry.Name,
						value = Mathf.Clamp01(entry.DefaultValue)
					});
					continue;
				}

				duplicatedRace.UnbakedShapesToInclude.Add(entry.Name);
			}
		}

		private void ReplaceCompatibleRaceNames(List<string> compatibleRaces, string sourceRaceName, string duplicatedRaceName)
		{
			if (compatibleRaces == null)
			{
				return;
			}

			for (int i = 0; i < compatibleRaces.Count; i++)
			{
				if (string.Equals(compatibleRaces[i], sourceRaceName, StringComparison.OrdinalIgnoreCase))
				{
					compatibleRaces[i] = duplicatedRaceName;
				}
			}
		}

		private RaceData GetOrCreateRaceAsset(string assetPath, out bool createdAsset)
		{
			createdAsset = false;
			RaceData existingRace = AssetDatabase.LoadMainAssetAtPath(assetPath) as RaceData;
			if (existingRace != null)
			{
				return existingRace;
			}

			RaceData newRace = ScriptableObject.CreateInstance(sourceRace.GetType()) as RaceData;
			if (newRace == null)
			{
				return null;
			}

			AssetDatabase.CreateAsset(newRace, assetPath);
			createdAsset = true;
			return newRace;
		}

		private UMARecipeBase GetOrCreateRecipeAsset(string assetPath, out bool createdAsset)
		{
			createdAsset = false;
			UMARecipeBase existingRecipe = AssetDatabase.LoadMainAssetAtPath(assetPath) as UMARecipeBase;
			if (existingRecipe != null)
			{
				return existingRecipe;
			}

			UMARecipeBase newRecipe = ScriptableObject.CreateInstance(sourceBaseRecipe.GetType()) as UMARecipeBase;
			if (newRecipe == null)
			{
				return null;
			}

			AssetDatabase.CreateAsset(newRecipe, assetPath);
			createdAsset = true;
			return newRecipe;
		}

		private DynamicUMADnaAsset GetOrCreatePoseDnaAsset(string assetPath, out bool createdAsset)
		{
			createdAsset = false;
			DynamicUMADnaAsset existingPoseDnaAsset = AssetDatabase.LoadMainAssetAtPath(assetPath) as DynamicUMADnaAsset;
			if (existingPoseDnaAsset != null)
			{
				return existingPoseDnaAsset;
			}

			DynamicUMADnaAsset newPoseDnaAsset = ScriptableObject.CreateInstance<DynamicUMADnaAsset>();
			if (newPoseDnaAsset == null)
			{
				return null;
			}

			AssetDatabase.CreateAsset(newPoseDnaAsset, assetPath);
			createdAsset = true;
			return newPoseDnaAsset;
		}

		private DynamicDNAConverterController GetOrCreatePoseGroupAsset(string assetPath, out bool createdAsset)
		{
			createdAsset = false;
			DynamicDNAConverterController existingPoseGroupAsset = AssetDatabase.LoadMainAssetAtPath(assetPath) as DynamicDNAConverterController;
			if (existingPoseGroupAsset != null)
			{
				return existingPoseGroupAsset;
			}

			DynamicDNAConverterController newPoseGroupAsset = ScriptableObject.CreateInstance<DynamicDNAConverterController>();
			if (newPoseGroupAsset == null)
			{
				return null;
			}

			AssetDatabase.CreateAsset(newPoseGroupAsset, assetPath);
			createdAsset = true;
			return newPoseGroupAsset;
		}

		private bool ConfirmOverwriteIfNeeded()
		{
			List<string> overwriteTargets = new List<string>();
			if (TargetExistsAsCompatibleType(GetTargetRaceAssetPath(), sourceRace.GetType()))
			{
				overwriteTargets.Add(GetTargetRaceAssetPath());
			}

			if (sourceBaseRecipe != null && TargetExistsAsCompatibleType(GetTargetBaseRecipeAssetPath(), sourceBaseRecipe.GetType()))
			{
				overwriteTargets.Add(GetTargetBaseRecipeAssetPath());
			}

			if (selectedBonePose != null && TargetExistsAsCompatibleType(GetTargetPoseDnaAssetPath(), typeof(DynamicUMADnaAsset)))
			{
				overwriteTargets.Add(GetTargetPoseDnaAssetPath());
			}

			if (selectedBonePose != null && TargetExistsAsCompatibleType(GetTargetPoseGroupAssetPath(), typeof(DynamicDNAConverterController)))
			{
				overwriteTargets.Add(GetTargetPoseGroupAssetPath());
			}

			if (overwriteTargets.Count == 0)
			{
				return true;
			}

			string message = "The following assets already exist and will be overwritten:\n\n" + string.Join("\n", overwriteTargets.ToArray()) + "\n\nContinue?";
			return EditorUtility.DisplayDialog("Overwrite Existing Assets", message, "Overwrite", "Cancel");
		}

		private bool CanUseTargetPath(string assetPath, Type expectedType, out string message)
		{
			message = string.Empty;
			if (string.IsNullOrEmpty(assetPath))
			{
				message = "A valid target asset path could not be generated.";
				return false;
			}

			UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (existingAsset == null)
			{
				return true;
			}

			if (existingAsset.GetType() != expectedType)
			{
				message = "The target path '" + assetPath + "' already contains a " + existingAsset.GetType().Name + ". Choose a different asset name.";
				return false;
			}

			return true;
		}

		private bool TargetExistsAsCompatibleType(string assetPath, Type expectedType)
		{
			if (string.IsNullOrEmpty(assetPath))
			{
				return false;
			}

			UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			return existingAsset != null && existingAsset.GetType() == expectedType;
		}

		private string GetSourceRaceAssetPath()
		{
			return sourceRace != null ? AssetDatabase.GetAssetPath(sourceRace) : string.Empty;
		}

		private string GetSourceBaseRecipeAssetPath()
		{
			return sourceBaseRecipe != null ? AssetDatabase.GetAssetPath(sourceBaseRecipe) : string.Empty;
		}

		private string GetTargetRaceAssetPath()
		{
			string folder = GetFolderFromAssetPath(GetSourceRaceAssetPath());
			return Path.Combine(folder, GetTrimmedValue(newRaceName) + ".asset").Replace('\\', '/');
		}

		private string GetTargetBaseRecipeAssetPath()
		{
			if (sourceBaseRecipe == null)
			{
				return string.Empty;
			}

			string sourceRecipePath = GetSourceBaseRecipeAssetPath();
			string folder = !string.IsNullOrEmpty(sourceRecipePath) ? GetFolderFromAssetPath(sourceRecipePath) : GetFolderFromAssetPath(GetSourceRaceAssetPath());
			return Path.Combine(folder, GetTrimmedValue(newBaseRecipeName) + ".asset").Replace('\\', '/');
		}

		private string GetTargetBaseRecipeAssetPathPreview()
		{
			if (sourceBaseRecipe == null)
			{
				return "(No duplicated base recipe will be created)";
			}

			return GetTargetBaseRecipeAssetPath();
		}

		private string GetTargetPoseDnaAssetPath()
		{
			if (selectedBonePose == null)
			{
				return string.Empty;
			}

			string folder = GetFolderFromAssetPath(GetSourceRaceAssetPath());
			return Path.Combine(folder, GetDefaultPoseDnaAssetName() + ".asset").Replace('\\', '/');
		}

		private string GetTargetPoseGroupAssetPath()
		{
			if (selectedBonePose == null)
			{
				return string.Empty;
			}

			string folder = GetFolderFromAssetPath(GetSourceRaceAssetPath());
			return Path.Combine(folder, GetDefaultPoseGroupAssetName() + ".asset").Replace('\\', '/');
		}

		private string GetTargetPoseDnaAssetPathPreview()
		{
			return selectedBonePose == null ? "(No pose DNA asset will be created)" : GetTargetPoseDnaAssetPath();
		}

		private string GetTargetPoseGroupAssetPathPreview()
		{
			return selectedBonePose == null ? "(No pose group asset will be created)" : GetTargetPoseGroupAssetPath();
		}

		private string GetFolderFromAssetPath(string assetPath)
		{
			string folder = Path.GetDirectoryName(assetPath);
			if (string.IsNullOrEmpty(folder))
			{
				return "Assets";
			}

			return folder.Replace('\\', '/');
		}

		private void DrawPathPreviewField(string label, string value)
		{
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField(label, value);
			}
		}

		private string GetSafeSourceRaceName()
		{
			if (sourceRace == null)
			{
				return "(Missing)";
			}

			return !string.IsNullOrWhiteSpace(sourceRace.raceName) ? sourceRace.raceName : sourceRace.name;
		}

		private static string GetTrimmedValue(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
		}

		private static bool IsValidName(string value)
		{
			string trimmedValue = GetTrimmedValue(value);
			if (string.IsNullOrEmpty(trimmedValue))
			{
				return false;
			}

			char[] invalidChars = Path.GetInvalidFileNameChars();
			for (int i = 0; i < invalidChars.Length; i++)
			{
				if (trimmedValue.IndexOf(invalidChars[i]) >= 0)
				{
					return false;
				}
			}

			return trimmedValue.IndexOf('/') < 0 && trimmedValue.IndexOf('\\') < 0;
		}

		private static bool PathsEqual(string pathA, string pathB)
		{
			return !string.IsNullOrEmpty(pathA) && !string.IsNullOrEmpty(pathB) && string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase);
		}
	}
}