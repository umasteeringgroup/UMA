using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
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

		private class TPoseMixerEntry
		{
			public UMABonePose BonePose;
			public string AssetPath;
			public bool Selected;
			public float Percentage = 100f;
		}

		private const int SourceRacePage = 0;
		private const int BlendshapeCollectionPage = 1;
		private const int SetupTPosePage = 2;
		private const int SummaryPage = 3;
		private const int WizardPageCount = 4;
		private const float IntroColumnWidth = 260f;
		private const float WizardHorizontalPadding = 8f;
		private const float WizardColumnSpacing = 8f;

		private RaceData sourceRace;
		private UMARecipeBase sourceBaseRecipe;
		private string newRaceName;
		private string newBaseRecipeName;
		private bool generateTPose = true;
		private bool baseRecipeNameUsesDefault = true;
		private int pageIndex;
		private Vector2 scrollPosition;
		private Vector2 compatibilityListScrollPosition;
		private Vector2 blendshapeListScrollPosition;
		private ReorderableList tPoseMixerPoseList;
		private readonly List<BlendshapeEntry> blendshapeEntries = new List<BlendshapeEntry>();
		private readonly List<CompatibilityRaceEntry> compatibilityRaceEntries = new List<CompatibilityRaceEntry>();
		private readonly List<TPoseMixerEntry> tPoseMixerEntries = new List<TPoseMixerEntry>();
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
			generateTPose = sourceRace != null && sourceRace.TPose != null;
			baseRecipeNameUsesDefault = true;

			RefreshCompatibilityRaceEntries();
			RefreshTPoseMixerEntries();
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
			EditorGUILayout.LabelField("Step " + (pageIndex + 1) + " of " + WizardPageCount, EditorStyles.miniLabel);
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
			else if (pageIndex == SetupTPosePage)
			{
				DrawSetupTPosePage();
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

		private void DrawSetupTPosePage()
		{
			EditorGUILayout.LabelField("Setup TPose", EditorStyles.boldLabel);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("Source TPose", sourceRace != null ? sourceRace.TPose : null, typeof(UmaTPose), false);
			}

			using (new EditorGUI.DisabledScope(sourceRace == null || sourceRace.TPose == null))
			{
				generateTPose = EditorGUILayout.Toggle("Generate TPose", generateTPose);
			}

			if (sourceRace == null || sourceRace.TPose == null)
			{
				EditorGUILayout.HelpBox("The source race has no TPose to use as a base, so the duplicate will keep its copied TPose reference.", MessageType.Warning);
				return;
			}

			DrawPathPreviewField("Generated TPose Asset Path", GetTargetTPoseAssetPathPreview());
			if (!generateTPose)
			{
				EditorGUILayout.HelpBox("TPose generation is disabled, so the duplicate will keep its copied TPose reference.", MessageType.Info);
			}

			using (new EditorGUI.DisabledScope(!generateTPose))
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
				{
					RefreshTPoseMixerEntries();
				}
				if (GUILayout.Button("Select All", GUILayout.Width(90f)))
				{
					SetAllTPoseMixerSelections(true);
				}
				if (GUILayout.Button("Select None", GUILayout.Width(90f)))
				{
					SetAllTPoseMixerSelections(false);
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				GUILayout.Space(4f);
				if (tPoseMixerEntries.Count == 0)
				{
					EditorGUILayout.HelpBox("No UMABonePose assets with Mixer Pose enabled were found in the project.", MessageType.Info);
				}
				else
				{
					EnsureTPoseMixerPoseList();
					tPoseMixerPoseList.DoLayoutList();
					EditorGUILayout.LabelField("Selected Mixer Poses", GetSelectedTPoseMixerCount().ToString());
				}
			}

			EditorGUILayout.HelpBox("The generated TPose starts as a copy of the source race TPose, then applies each checked mixer pose in list order using its percentage.", MessageType.Info);
			DrawValidationMessages();
		}

		private void DrawSummaryPage()
		{
			EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("New Race Name", GetTrimmedValue(newRaceName));
			DrawPathPreviewField("New RaceData Asset Path", GetTargetRaceAssetPath());
			DrawPathPreviewField("New Base Recipe Asset Path", GetTargetBaseRecipeAssetPathPreview());
			EditorGUILayout.LabelField("Generate TPose", ShouldCreateGeneratedTPose() ? "Yes" : "No");
			DrawPathPreviewField("Generated TPose Asset Path", GetTargetTPoseAssetPathPreview());
			EditorGUILayout.LabelField("Selected Blendshapes", GetSelectedBlendshapeCount().ToString());
			EditorGUILayout.LabelField("Selected Mixer Poses", ShouldCreateGeneratedTPose() ? GetSelectedTPoseMixerCount().ToString() : "0");
			EditorGUILayout.LabelField("Cross Compatible With", GetSelectedCompatibilityRaceCount().ToString());
			EditorGUILayout.LabelField("Selected Races", GetSelectedCompatibilityRaceSummary());

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

		private void EnsureTPoseMixerPoseList()
		{
			if (tPoseMixerPoseList != null)
			{
				return;
			}

			tPoseMixerPoseList = new ReorderableList(tPoseMixerEntries, typeof(TPoseMixerEntry), true, true, false, false)
			{
				drawHeaderCallback = DrawTPoseMixerListHeader,
				drawElementCallback = DrawTPoseMixerListElement,
				elementHeight = EditorGUIUtility.singleLineHeight + 4f
			};
		}

		private void DrawTPoseMixerListHeader(Rect rect)
		{
			EditorGUI.LabelField(new Rect(rect.x + 4f, rect.y, 36f, rect.height), "Use", EditorStyles.miniBoldLabel);
			EditorGUI.LabelField(new Rect(rect.x + 44f, rect.y, rect.width - 206f, rect.height), "Mixer Pose", EditorStyles.miniBoldLabel);
			EditorGUI.LabelField(new Rect(rect.x + rect.width - 154f, rect.y, 150f, rect.height), "Percentage", EditorStyles.miniBoldLabel);
		}

		private void DrawTPoseMixerListElement(Rect rect, int index, bool isActive, bool isFocused)
		{
			if (index < 0 || index >= tPoseMixerEntries.Count)
			{
				return;
			}

			TPoseMixerEntry entry = tPoseMixerEntries[index];
			rect.y += 2f;
			rect.height = EditorGUIUtility.singleLineHeight;

			entry.Selected = EditorGUI.Toggle(new Rect(rect.x + 4f, rect.y, 24f, rect.height), entry.Selected);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUI.ObjectField(new Rect(rect.x + 34f, rect.y, rect.width - 198f, rect.height), GUIContent.none, entry.BonePose, typeof(UMABonePose), false);
			}

			using (new EditorGUI.DisabledScope(!entry.Selected))
			{
				entry.Percentage = EditorGUI.Slider(new Rect(rect.x + rect.width - 154f, rect.y, 150f, rect.height), entry.Percentage, 0f, 100f);
			}
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

			if (pageIndex == SetupTPosePage)
			{
				return "Setup TPose";
			}

			return "Summary and Create";
		}

		private string GetPageIntroText()
		{
			if (pageIndex == SourceRacePage)
			{
				return "Review the source RaceData that was selected in the Project window, choose the new RaceData name, optionally override the default Base Race Recipe name, and decide which races the duplicate should be marked compatible with.";
			}

			if (pageIndex == BlendshapeCollectionPage)
			{
				return "Scan the source race's Base Race Recipe slots, collect every unique blendshape name, and choose which ones should be carried into the duplicated race along with their default values.";
			}

			if (pageIndex == SetupTPosePage)
			{
				return "Generate a TPose asset for the duplicated race by applying checked Mixer Pose bone poses in list order with the selected percentages.";
			}

			return "Review the final target asset paths, overwrite warnings, blendshape count, and TPose setup before duplicating the RaceData and its Base Race Recipe.";
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

		private void RefreshTPoseMixerEntries()
		{
			Dictionary<string, TPoseMixerEntry> previousEntries = new Dictionary<string, TPoseMixerEntry>(StringComparer.Ordinal);
			List<string> previousOrder = new List<string>();
			for (int i = 0; i < tPoseMixerEntries.Count; i++)
			{
				TPoseMixerEntry entry = tPoseMixerEntries[i];
				string assetPath = GetTPoseMixerEntryPath(entry);
				if (string.IsNullOrEmpty(assetPath) || previousEntries.ContainsKey(assetPath))
				{
					continue;
				}

				previousEntries.Add(assetPath, entry);
				previousOrder.Add(assetPath);
			}

			Dictionary<string, UMABonePose> mixerPosesByPath = new Dictionary<string, UMABonePose>(StringComparer.Ordinal);
			string[] poseGuids = AssetDatabase.FindAssets("t:UMABonePose");
			for (int i = 0; i < poseGuids.Length; i++)
			{
				string posePath = AssetDatabase.GUIDToAssetPath(poseGuids[i]);
				UMABonePose bonePose = AssetDatabase.LoadAssetAtPath<UMABonePose>(posePath);
				if (bonePose == null || !bonePose.mixerPose || mixerPosesByPath.ContainsKey(posePath))
				{
					continue;
				}

				mixerPosesByPath.Add(posePath, bonePose);
			}

			List<string> sortedPosePaths = new List<string>(mixerPosesByPath.Keys);
			sortedPosePaths.Sort((pathA, pathB) => CompareMixerPosePaths(pathA, pathB, mixerPosesByPath));

			tPoseMixerEntries.Clear();
			HashSet<string> addedPaths = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < previousOrder.Count; i++)
			{
				string posePath = previousOrder[i];
				if (!mixerPosesByPath.TryGetValue(posePath, out UMABonePose bonePose) || !previousEntries.TryGetValue(posePath, out TPoseMixerEntry previousEntry))
				{
					continue;
				}

				tPoseMixerEntries.Add(new TPoseMixerEntry
				{
					BonePose = bonePose,
					AssetPath = posePath,
					Selected = previousEntry.Selected,
					Percentage = Mathf.Clamp(previousEntry.Percentage, 0f, 100f)
				});
				addedPaths.Add(posePath);
			}

			for (int i = 0; i < sortedPosePaths.Count; i++)
			{
				string posePath = sortedPosePaths[i];
				if (!addedPaths.Add(posePath))
				{
					continue;
				}

				tPoseMixerEntries.Add(new TPoseMixerEntry
				{
					BonePose = mixerPosesByPath[posePath],
					AssetPath = posePath,
					Selected = false,
					Percentage = 100f
				});
			}

			tPoseMixerPoseList = null;
		}

		private static int CompareMixerPosePaths(string pathA, string pathB, Dictionary<string, UMABonePose> mixerPosesByPath)
		{
			mixerPosesByPath.TryGetValue(pathA, out UMABonePose poseA);
			mixerPosesByPath.TryGetValue(pathB, out UMABonePose poseB);
			string nameA = poseA != null ? poseA.name : pathA;
			string nameB = poseB != null ? poseB.name : pathB;
			int nameCompare = string.Compare(nameA, nameB, StringComparison.Ordinal);
			return nameCompare != 0 ? nameCompare : string.Compare(pathA, pathB, StringComparison.Ordinal);
		}

		private static string GetTPoseMixerEntryPath(TPoseMixerEntry entry)
		{
			if (entry == null)
			{
				return string.Empty;
			}

			if (!string.IsNullOrEmpty(entry.AssetPath))
			{
				return entry.AssetPath;
			}

			return entry.BonePose != null ? AssetDatabase.GetAssetPath(entry.BonePose) : string.Empty;
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

		private void SetAllTPoseMixerSelections(bool selected)
		{
			for (int i = 0; i < tPoseMixerEntries.Count; i++)
			{
				tPoseMixerEntries[i].Selected = selected;
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

		private int GetSelectedTPoseMixerCount()
		{
			int count = 0;
			for (int i = 0; i < tPoseMixerEntries.Count; i++)
			{
				if (tPoseMixerEntries[i].Selected)
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

			if (ShouldCreateGeneratedTPose())
			{
				string targetTPosePath = GetTargetTPoseAssetPath();
				if (PathsEqual(targetTPosePath, GetSourceTPoseAssetPath()))
				{
					messages.Add("The generated TPose asset path matches the source TPose asset. Choose a different New Race Name.");
				}

				if (!CanUseTargetPath(targetTPosePath, typeof(UmaTPose), out string tPoseTargetMessage))
				{
					messages.Add(tPoseTargetMessage);
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

			if (ShouldCreateGeneratedTPose() && TargetExistsAsCompatibleType(GetTargetTPoseAssetPath(), typeof(UmaTPose)))
			{
				warnings.Add("The target TPose asset already exists and will require overwrite confirmation before the duplicate is saved.");
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
			string tPoseAssetPath = GetTargetTPoseAssetPath();
			RaceData duplicatedRace = null;
			UMARecipeBase duplicatedRecipe = null;
			UmaTPose generatedTPose = null;
			bool createdRaceAsset = false;
			bool createdRecipeAsset = false;
			bool createdTPoseAsset = false;

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
                duplicatedRace.expressionGroup = sourceRace.expressionGroup;
				duplicatedRace.useManualRendererBounds = sourceRace.useManualRendererBounds;
				duplicatedRace.manualRendererBounds = sourceRace.manualRendererBounds;
				duplicatedRace.manualRendererBoundsCenter = sourceRace.manualRendererBoundsCenter;

				if (duplicatedRecipe != null)
				{
					DuplicateBaseRaceRecipe(duplicatedRecipe, duplicatedRace);
					duplicatedRace.baseRaceRecipe = duplicatedRecipe;
				}

				if (ShouldCreateGeneratedTPose())
				{
					generatedTPose = GetOrCreateTPoseAsset(tPoseAssetPath, out createdTPoseAsset);
					if (generatedTPose == null)
					{
						throw new InvalidOperationException("Unable to create or load the generated TPose asset.");
					}

					ConfigureGeneratedTPoseAsset(generatedTPose);
					duplicatedRace.TPose = generatedTPose;
				}

				ApplySelectedBlendshapeSettings(duplicatedRace);
				duplicatedRace.SetCrossCompatibleRaces(GetSelectedCompatibilityRaceNames());

				EditorUtility.SetDirty(duplicatedRace);
				if (duplicatedRecipe != null)
				{
					EditorUtility.SetDirty(duplicatedRecipe);
				}
				if (generatedTPose != null)
				{
					EditorUtility.SetDirty(generatedTPose);
				}

				AssetDatabase.SaveAssets();
				if (duplicatedRecipe is UMATextRecipe duplicatedTextRecipe)
				{
					UMAUpdateProcessor.UpdateRecipe(duplicatedTextRecipe);
				}
				UMAUpdateProcessor.UpdateRace(duplicatedRace);
				AddAssetsToGlobalLibrary(duplicatedRace, duplicatedRecipe, generatedTPose);
				AssetDatabase.Refresh();

				Close();
				InspectorUtlity.InspectTarget(duplicatedRace);
			}
			catch (Exception ex)
			{
				if (createdTPoseAsset && !string.IsNullOrEmpty(tPoseAssetPath))
				{
					AssetDatabase.DeleteAsset(tPoseAssetPath);
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

		private void ConfigureGeneratedTPoseAsset(UmaTPose generatedTPose)
		{
			if (generatedTPose == null || sourceRace == null || sourceRace.TPose == null)
			{
				throw new InvalidOperationException("A source TPose is required to generate the duplicated race TPose.");
			}

			EditorUtility.CopySerialized(sourceRace.TPose, generatedTPose);
			generatedTPose.name = GetDefaultTPoseAssetName();
			generatedTPose.boneInfo = null;
			generatedTPose.humanInfo = null;
			generatedTPose.DeSerialize();
			ApplySelectedMixerPosesToTPose(generatedTPose);
			generatedTPose.Serialize();
			EditorUtility.SetDirty(generatedTPose);
		}

		private int ApplySelectedMixerPosesToTPose(UmaTPose tPose)
		{
			if (tPose == null)
			{
				return 0;
			}

			tPose.DeSerialize();
			if (tPose.boneInfo == null || tPose.boneInfo.Length == 0)
			{
				return 0;
			}

			Dictionary<string, int> tPoseBoneIndices = new Dictionary<string, int>(StringComparer.Ordinal);
			for (int boneIndex = 0; boneIndex < tPose.boneInfo.Length; boneIndex++)
			{
				string boneName = tPose.boneInfo[boneIndex].name;
				if (!string.IsNullOrEmpty(boneName) && !tPoseBoneIndices.ContainsKey(boneName))
				{
					tPoseBoneIndices.Add(boneName, boneIndex);
				}
			}

			int appliedBoneCount = 0;
			for (int entryIndex = 0; entryIndex < tPoseMixerEntries.Count; entryIndex++)
			{
				TPoseMixerEntry entry = tPoseMixerEntries[entryIndex];
				if (entry == null || !entry.Selected || entry.BonePose == null || entry.BonePose.poses == null)
				{
					continue;
				}

				float weight = Mathf.Clamp01(entry.Percentage / 100f);
				if (weight <= 0f)
				{
					continue;
				}

				for (int poseIndex = 0; poseIndex < entry.BonePose.poses.Length; poseIndex++)
				{
					UMABonePose.PoseBone poseBone = entry.BonePose.poses[poseIndex];
					if (poseBone == null || !poseBone.enabled || string.IsNullOrEmpty(poseBone.bone) || !tPoseBoneIndices.TryGetValue(poseBone.bone, out int tPoseBoneIndex))
					{
						continue;
					}

					SkeletonBone skeletonBone = tPose.boneInfo[tPoseBoneIndex];
					skeletonBone.position += poseBone.position * weight;
					skeletonBone.rotation = NormalizeSafe(skeletonBone.rotation * Quaternion.Slerp(Quaternion.identity, NormalizeSafe(poseBone.rotation), weight));
					skeletonBone.scale = Vector3.Scale(skeletonBone.scale, Vector3.Lerp(Vector3.one, poseBone.scale, weight));
					tPose.boneInfo[tPoseBoneIndex] = skeletonBone;
					appliedBoneCount++;
				}
			}

			return appliedBoneCount;
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

		private string GetDefaultTPoseAssetName()
		{
			string trimmedRaceName = GetTrimmedValue(newRaceName);
			return string.IsNullOrEmpty(trimmedRaceName)
				? "TPose"
				: "TPose_" + trimmedRaceName;
		}

		private static Quaternion NormalizeSafe(Quaternion rotation)
		{
			float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
			if (magnitude <= Mathf.Epsilon)
			{
				return Quaternion.identity;
			}

			return new Quaternion(rotation.x / magnitude, rotation.y / magnitude, rotation.z / magnitude, rotation.w / magnitude);
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

		private UmaTPose GetOrCreateTPoseAsset(string assetPath, out bool createdAsset)
		{
			createdAsset = false;
			UmaTPose existingTPose = AssetDatabase.LoadMainAssetAtPath(assetPath) as UmaTPose;
			if (existingTPose != null)
			{
				return existingTPose;
			}

			UmaTPose newTPose = ScriptableObject.CreateInstance<UmaTPose>();
			if (newTPose == null)
			{
				return null;
			}

			AssetDatabase.CreateAsset(newTPose, assetPath);
			createdAsset = true;
			return newTPose;
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

			if (ShouldCreateGeneratedTPose() && TargetExistsAsCompatibleType(GetTargetTPoseAssetPath(), typeof(UmaTPose)))
			{
				overwriteTargets.Add(GetTargetTPoseAssetPath());
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

		private string GetSourceTPoseAssetPath()
		{
			return sourceRace != null && sourceRace.TPose != null ? AssetDatabase.GetAssetPath(sourceRace.TPose) : string.Empty;
		}

		private bool ShouldCreateGeneratedTPose()
		{
			return generateTPose && sourceRace != null && sourceRace.TPose != null;
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

		private string GetTargetTPoseAssetPath()
		{
			if (!ShouldCreateGeneratedTPose())
			{
				return string.Empty;
			}

			string folder = GetFolderFromAssetPath(GetSourceRaceAssetPath());
			return Path.Combine(folder, GetDefaultTPoseAssetName() + ".asset").Replace('\\', '/');
		}

		private string GetTargetTPoseAssetPathPreview()
		{
			return ShouldCreateGeneratedTPose() ? GetTargetTPoseAssetPath() : "(No generated TPose will be created)";
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
