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
using UMA.Dynamics;
using System;

namespace UMA.Editors
{
    public partial class UMAAvatarLoadSaveMenuItems : Editor
	{
		private class CreateOverlaysForTexturesWindow : EditorWindow
		{
			private enum OverlayTargetMode
			{
				Slot = 0,
				Tag = 1
			}

			private List<Texture2D> selectedTextures;
			private UMAMaterial selectedMaterial;
			private RaceData selectedRace;
			private int selectedWardrobeSlotIndex;
			private string[] wardrobeSlotOptions = new string[0];
			private OverlayTargetMode targetMode;
			private int selectedBaseSlotIndex;
			private string[] baseSlotOptions = new string[0];
			private SlotData[] baseSlots = new SlotData[0];
			private int selectedBaseTagIndex;
			private string[] baseTagOptions = new string[0];
			private bool useAlphaMask;
			private Texture2D selectedAlphaMask;
			private string sharedColorName = "Color";

			public static void Open(List<Texture2D> textures)
			{
				if (textures == null || textures.Count == 0)
				{
					EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select one or more Texture2D assets in the Project window.", "OK");
					return;
				}

				CreateOverlaysForTexturesWindow window = CreateInstance<CreateOverlaysForTexturesWindow>();
				window.titleContent = new GUIContent("Create Overlay and Recipe");
				window.selectedTextures = new List<Texture2D>(textures);
				window.minSize = new Vector2(560f, 240f);
				window.ShowUtility();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField("Create overlay and recipe for base alternates", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("This will create an overlay with a single channel for each texture. Select the UMAMaterial to use for these Overlays.", MessageType.Info);
				EditorGUILayout.LabelField("Selected Textures", selectedTextures != null ? selectedTextures.Count.ToString() : "0");
				selectedMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", selectedMaterial, typeof(UMAMaterial), false);
				EditorGUI.BeginChangeCheck();
				selectedRace = (RaceData)EditorGUILayout.ObjectField("Race", selectedRace, typeof(RaceData), false);
				if (EditorGUI.EndChangeCheck())
				{
					RefreshBaseSlots();
				}

				using (new EditorGUI.DisabledScope(wardrobeSlotOptions.Length == 0))
				{
					selectedWardrobeSlotIndex = EditorGUILayout.Popup("Wardrobe Region", selectedWardrobeSlotIndex, wardrobeSlotOptions);
				}

				using (new EditorGUI.DisabledScope(baseSlotOptions.Length == 0 && baseTagOptions.Length == 0))
				{
					targetMode = (OverlayTargetMode)EditorGUILayout.EnumPopup("Target Type", targetMode);
				}

				if (targetMode == OverlayTargetMode.Slot)
				{
					using (new EditorGUI.DisabledScope(baseSlotOptions.Length == 0))
					{
						selectedBaseSlotIndex = EditorGUILayout.Popup("Slot Data", selectedBaseSlotIndex, baseSlotOptions);
					}
				}
				else
				{
					using (new EditorGUI.DisabledScope(baseTagOptions.Length == 0))
					{
						selectedBaseTagIndex = EditorGUILayout.Popup("Tag", selectedBaseTagIndex, baseTagOptions);
					}
				}
				sharedColorName = EditorGUILayout.TextField("Shared Color", sharedColorName);
				useAlphaMask = EditorGUILayout.Toggle("Set Alpha Mask", useAlphaMask);
				using (new EditorGUI.DisabledScope(!useAlphaMask))
				{
					selectedAlphaMask = (Texture2D)EditorGUILayout.ObjectField("Alpha Mask", selectedAlphaMask, typeof(Texture2D), false);
				}

				GUILayout.FlexibleSpace();
				EditorGUILayout.BeginHorizontal();
				using (new EditorGUI.DisabledScope(selectedMaterial == null || selectedRace == null || !HasValidSelections() || string.IsNullOrWhiteSpace(sharedColorName) || (useAlphaMask && selectedAlphaMask == null)))
				{
					if (GUILayout.Button("Create"))
					{
						CreateAssets();
					}
				}
				if (GUILayout.Button("Cancel"))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}

			private void CreateAssets()
			{
				if (selectedTextures == null || selectedTextures.Count == 0)
				{
					Close();
					return;
				}

				int createdOverlays = 0;
				int createdWardrobeRecipes = 0;
				int skipped = 0;
				List<string> skippedItems = new List<string>();

				try
				{
					string selectedWardrobeSlot = GetSelectedWardrobeSlot();
					SlotData selectedBaseSlot = targetMode == OverlayTargetMode.Slot ? GetSelectedBaseSlot() : null;
					string selectedTag = targetMode == OverlayTargetMode.Tag ? GetSelectedBaseTag() : string.Empty;
					if (string.IsNullOrWhiteSpace(selectedWardrobeSlot))
					{
						EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select a race and a valid wardrobe slot.", "OK");
						return;
					}

					if (targetMode == OverlayTargetMode.Slot && (selectedBaseSlot == null || selectedBaseSlot.asset == null))
					{
						EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select a race and a valid base slot.", "OK");
						return;
					}

					if (targetMode == OverlayTargetMode.Tag && string.IsNullOrWhiteSpace(selectedTag))
					{
						EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select a race and a valid tag.", "OK");
						return;
					}

					if (useAlphaMask && selectedAlphaMask == null)
					{
						EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select a texture to use as the alpha mask, or turn off Set Alpha Mask.", "OK");
						return;
					}

					for (int i = 0; i < selectedTextures.Count; i++)
					{
						Texture2D texture = selectedTextures[i];
						if (texture == null)
						{
							continue;
						}

						string texturePath = AssetDatabase.GetAssetPath(texture);
						if (string.IsNullOrEmpty(texturePath))
						{
							skipped++;
							skippedItems.Add(texture.name + " (no asset path)");
							continue;
						}

						string folder = Path.GetDirectoryName(texturePath);
						string baseName = Path.GetFileNameWithoutExtension(texturePath);
						string overlayPath = Path.Combine(folder, baseName + "_Overlay.asset").Replace('\\', '/');
						string wardrobePath = Path.Combine(folder, baseName + "_Wardrobe.asset").Replace('\\', '/');

						if (AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath) != null)
						{
							skipped++;
							skippedItems.Add(baseName + " (overlay already exists)");
							continue;
						}

						if (AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(wardrobePath) != null)
						{
							skipped++;
							skippedItems.Add(baseName + " (wardrobe recipe already exists)");
							continue;
						}

						OverlayDataAsset overlayAsset = CustomAssetUtility.CreateAsset<OverlayDataAsset>(overlayPath, false, baseName + "_Overlay", false);
						if (overlayAsset == null)
						{
							skipped++;
							skippedItems.Add(baseName + " (overlay asset creation failed)");
							continue;
						}

						InitializeOverlayAsset(overlayAsset, texture, selectedMaterial, useAlphaMask ? selectedAlphaMask : null, baseName);
						EditorUtility.SetDirty(overlayAsset);
						createdOverlays++;

						UMAWardrobeRecipe wardrobeRecipe = CustomAssetUtility.CreateAsset<UMAWardrobeRecipe>(wardrobePath, false, baseName + "_Wardrobe", false);
						if (wardrobeRecipe == null)
						{
							skipped++;
							skippedItems.Add(baseName + " (wardrobe asset creation failed)");
							continue;
						}

						InitializeWardrobeRecipe(wardrobeRecipe, overlayAsset, selectedMaterial, selectedRace, selectedWardrobeSlot, selectedBaseSlot, selectedTag, sharedColorName, baseName);
						EditorUtility.SetDirty(wardrobeRecipe);
						createdWardrobeRecipes++;
					}
				}
				finally
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
				}

				string message = "Created overlays: " + createdOverlays
					+ "\nCreated wardrobe recipes: " + createdWardrobeRecipes
					+ "\nSkipped: " + skipped;

				if (skippedItems.Count > 0)
				{
					message += "\n\nSkipped items:";
					for (int i = 0; i < skippedItems.Count; i++)
					{
						message += "\n- " + skippedItems[i];
					}
				}

				EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", message, "OK");
				Close();
			}

			private void RefreshBaseSlots()
			{
				selectedWardrobeSlotIndex = 0;
				selectedBaseSlotIndex = 0;
				selectedBaseTagIndex = 0;
				wardrobeSlotOptions = new string[0];
				baseSlotOptions = new string[0];
				baseSlots = new SlotData[0];
				baseTagOptions = new string[0];

				if (selectedRace == null)
				{
					return;
				}

				if (selectedRace.wardrobeSlots != null && selectedRace.wardrobeSlots.Count > 0)
				{
					wardrobeSlotOptions = selectedRace.wardrobeSlots.ToArray();
				}

				if (selectedRace.baseRaceRecipe == null)
				{
					return;
				}

				UMAData.UMARecipe baseRecipe = selectedRace.baseRaceRecipe.GetCachedRecipe(true);
				if (baseRecipe == null || baseRecipe.slotDataList == null || baseRecipe.slotDataList.Length == 0)
				{
					return;
				}

				List<SlotData> slots = new List<SlotData>();
				List<string> slotNames = new List<string>();
				HashSet<string> tagNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
				for (int i = 0; i < baseRecipe.slotDataList.Length; i++)
				{
					SlotData slot = baseRecipe.slotDataList[i];
					if (slot == null)
					{
						continue;
					}

					if (slot.asset != null)
					{
						slots.Add(slot);
						slotNames.Add(slot.slotName);
					}

					if (slot.tags == null)
					{
						continue;
					}

					for (int tagIndex = 0; tagIndex < slot.tags.Length; tagIndex++)
					{
						string tag = slot.tags[tagIndex];
						if (!string.IsNullOrWhiteSpace(tag))
						{
							tagNames.Add(tag);
						}
					}
				}

				baseSlots = slots.ToArray();
				baseSlotOptions = slotNames.ToArray();
				baseTagOptions = new List<string>(tagNames).ToArray();
				Array.Sort(baseTagOptions, System.StringComparer.OrdinalIgnoreCase);

				if (targetMode == OverlayTargetMode.Slot && baseSlotOptions.Length == 0 && baseTagOptions.Length > 0)
				{
					targetMode = OverlayTargetMode.Tag;
				}
				else if (targetMode == OverlayTargetMode.Tag && baseTagOptions.Length == 0 && baseSlotOptions.Length > 0)
				{
					targetMode = OverlayTargetMode.Slot;
				}
			}

			private bool HasValidSelections()
			{
				if (wardrobeSlotOptions == null || wardrobeSlotOptions.Length == 0)
				{
					return false;
				}

				if (targetMode == OverlayTargetMode.Tag)
				{
					return baseTagOptions != null && baseTagOptions.Length > 0;
				}

				return baseSlotOptions != null && baseSlotOptions.Length > 0;
			}

			private string GetSelectedWardrobeSlot()
			{
				if (wardrobeSlotOptions == null || wardrobeSlotOptions.Length == 0)
				{
					return string.Empty;
				}

				if (selectedWardrobeSlotIndex < 0 || selectedWardrobeSlotIndex >= wardrobeSlotOptions.Length)
				{
					selectedWardrobeSlotIndex = 0;
				}

				return wardrobeSlotOptions[selectedWardrobeSlotIndex];
			}

			private SlotData GetSelectedBaseSlot()
			{
				if (baseSlots == null || baseSlots.Length == 0)
				{
					return null;
				}

				if (selectedBaseSlotIndex < 0 || selectedBaseSlotIndex >= baseSlots.Length)
				{
					selectedBaseSlotIndex = 0;
				}

				return baseSlots[selectedBaseSlotIndex];
			}

			private string GetSelectedBaseTag()
			{
				if (baseTagOptions == null || baseTagOptions.Length == 0)
				{
					return string.Empty;
				}

				if (selectedBaseTagIndex < 0 || selectedBaseTagIndex >= baseTagOptions.Length)
				{
					selectedBaseTagIndex = 0;
				}

				return baseTagOptions[selectedBaseTagIndex];
			}

			private void InitializeOverlayAsset(OverlayDataAsset overlayAsset, Texture2D texture, UMAMaterial material, Texture2D alphaMask, string baseName)
			{
				int channelCount = material != null && material.channels != null && material.channels.Length > 0 ? material.channels.Length : 1;
				string overlayName = baseName + "_Overlay";
				SerializedObject serializedOverlay = new SerializedObject(overlayAsset);
				SerializedProperty alphaMaskProperty = serializedOverlay.FindProperty("alphaMask");
				SerializedProperty materialProperty = serializedOverlay.FindProperty("material");
				SerializedProperty materialNameProperty = serializedOverlay.FindProperty("materialName");
				SerializedProperty textureListProperty = serializedOverlay.FindProperty("_textureList");
				SerializedProperty textureNamesProperty = serializedOverlay.FindProperty("textureNames");
				SerializedProperty overlayBlendProperty = serializedOverlay.FindProperty("overlayBlend");

				overlayAsset.name = overlayName;
				serializedOverlay.Update();

				if (alphaMaskProperty != null)
				{
					alphaMaskProperty.objectReferenceValue = alphaMask;
				}

				if (materialProperty != null)
				{
					materialProperty.objectReferenceValue = material;
				}

				if (materialNameProperty != null)
				{
					materialNameProperty.stringValue = material != null ? material.name : string.Empty;
				}

				if (textureListProperty != null)
				{
					textureListProperty.arraySize = channelCount;
					for (int i = 0; i < channelCount; i++)
					{
						SerializedProperty textureProperty = textureListProperty.GetArrayElementAtIndex(i);
						if (textureProperty != null)
						{
							textureProperty.objectReferenceValue = i == 0 ? texture : null;
						}
					}
				}

				if (textureNamesProperty != null)
				{
					textureNamesProperty.arraySize = channelCount;
					for (int i = 0; i < channelCount; i++)
					{
						SerializedProperty textureNameProperty = textureNamesProperty.GetArrayElementAtIndex(i);
						if (textureNameProperty != null)
						{
							textureNameProperty.stringValue = i == 0 && texture != null ? texture.name : string.Empty;
						}
					}
				}

				if (overlayBlendProperty != null)
				{
					overlayBlendProperty.arraySize = channelCount;
					for (int i = 0; i < channelCount; i++)
					{
						SerializedProperty blendProperty = overlayBlendProperty.GetArrayElementAtIndex(i);
						if (blendProperty != null)
						{
							blendProperty.enumValueIndex = (int)OverlayDataAsset.OverlayBlend.Normal;
						}
					}
				}

				serializedOverlay.ApplyModifiedPropertiesWithoutUndo();
				overlayAsset.ValidateBlendList();
				UMAUpdateProcessor.UpdateOverlay(overlayAsset);
			}

			private void InitializeWardrobeRecipe(UMAWardrobeRecipe wardrobeRecipe, OverlayDataAsset overlayAsset, UMAMaterial material, RaceData race, string selectedWardrobeSlot, SlotData baseSlot, string selectedTag, string colorName, string baseName)
			{
				wardrobeRecipe.name = baseName + "_Wardrobe";
				wardrobeRecipe.DisplayValue = baseName;
				wardrobeRecipe.wardrobeSlot = !string.IsNullOrWhiteSpace(selectedWardrobeSlot) ? selectedWardrobeSlot : "None";
				wardrobeRecipe.compatibleRaces = new List<string>();
				if (race != null && !string.IsNullOrEmpty(race.raceName))
				{
					wardrobeRecipe.compatibleRaces.Add(race.raceName);
				}

				UMAData.UMARecipe recipe = new UMAData.UMARecipe();
				recipe.raceData = race;
				recipe.sharedColors = new OverlayColorData[1];
				int channelCount = material != null && material.channels != null && material.channels.Length > 0 ? material.channels.Length : 1;
				OverlayColorData colorData = new OverlayColorData(channelCount);
				colorData.name = colorName;
				recipe.sharedColors[0] = colorData;

				SlotData slot = CreateRecipeSlot(baseSlot, selectedTag, race, baseName);
				OverlayData overlayData = new OverlayData(overlayAsset);
				overlayData.colorData = new OverlayColorData(channelCount);
				overlayData.colorData.name = colorName;
				slot.SetOverlay(0, overlayData);
				recipe.slotDataList = new SlotData[] { slot };

				wardrobeRecipe.Save(recipe);
			}

			private SlotData CreateRecipeSlot(SlotData baseSlot, string selectedTag, RaceData race, string baseName)
			{
				if (targetMode == OverlayTargetMode.Tag)
				{
					string placeholderName = string.IsNullOrWhiteSpace(selectedTag)
						? baseName + "_Placeholder"
						: baseName + "_" + selectedTag + "_Placeholder";
					string[] matchingTags = string.IsNullOrWhiteSpace(selectedTag) ? new string[0] : new string[] { selectedTag };
					string[] matchingRaces = race != null && !string.IsNullOrWhiteSpace(race.raceName)
						? new string[] { race.raceName }
						: new string[0];
					return SlotData.CreatePlaceholder(placeholderName, matchingTags, matchingRaces);
				}

				SlotData slot = new SlotData(baseSlot.asset);
				slot.overlayScale = baseSlot.overlayScale;
				slot.tags = baseSlot.tags != null ? (string[])baseSlot.tags.Clone() : new string[0];
				slot.Races = baseSlot.Races != null ? (string[])baseSlot.Races.Clone() : null;
				slot.blendShapeTargetSlot = baseSlot.blendShapeTargetSlot;
				slot.UVSet = baseSlot.UVSet;
				return slot;
			}
		}

     private class CreateUMAMaterialFromMaterialWindow : EditorWindow
		{
			private class TextureSelection
			{
				public string PropertyName;
				public Texture Texture;
				public bool Selected;
			}

			private const int MaterialTypePage = 0;
			private const int GeneratedTextureSettingsPage = 1;
			private const int TextureOverridePage = 2;
			private const float IntroColumnWidth = 260f;
			private const float WizardHorizontalPadding = 8f;
			private const float WizardColumnSpacing = 8f;

			private Material sourceMaterial;
			private string umaMaterialName;
			private UMAMaterial.MaterialType materialType = UMAMaterial.MaterialType.Atlas;
			private bool generateMipMaps = true;
			private float mipMapBias = 0f;
			private int anisoLevel = 1;
			private FilterMode textureFilterMode = FilterMode.Bilinear;
			private bool maskWithCurrentColor;
			private Color maskMultiplier = Color.white;
			private int pageIndex;
			private Vector2 scrollPosition;
			private List<TextureSelection> textureSelections = new List<TextureSelection>();

			public static void Open(Material material)
			{
				if (material == null)
				{
					EditorUtility.DisplayDialog("Create UMAMaterial", "Select a Material asset in the Project window.", "OK");
					return;
				}

				CreateUMAMaterialFromMaterialWindow window = CreateInstance<CreateUMAMaterialFromMaterialWindow>();
				window.titleContent = new GUIContent("Create UMAMaterial");
				window.sourceMaterial = material;
				window.umaMaterialName = "UMAMaterial_" + material.name;
				window.minSize = new Vector2(800f, 300f);
				window.RefreshTextureSelections();
				window.ShowUtility();
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
				DrawMaterialSummary();
				GUILayout.Space(6f);

				scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, false, false);
				if (pageIndex == MaterialTypePage)
				{
					DrawMaterialTypePage();
				}
				else if (pageIndex == GeneratedTextureSettingsPage)
				{
					DrawGeneratedTextureSettingsPage();
				}
				else
				{
					DrawTextureOverridePage();
				}
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

			private void DrawMaterialSummary()
			{
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.ObjectField("Material", sourceMaterial, typeof(Material), false);
				}

				string shaderName = sourceMaterial != null && sourceMaterial.shader != null ? sourceMaterial.shader.name : "(None)";
				EditorGUILayout.LabelField("Shader", shaderName);
			}

			private string GetPageTitle()
			{
				if (pageIndex == MaterialTypePage)
				{
					return "Choose Material Type";
				}

				if (pageIndex == GeneratedTextureSettingsPage)
				{
					return "Generated Textures";
				}

				return "Texture Overrides";
			}

			private string GetPageIntroText()
			{
				if (pageIndex == MaterialTypePage)
				{
					return "Name the UMAMaterial and choose how UMA should use the selected Unity Material at runtime. The material type controls whether UMA builds atlases, composites textures without an atlas, or keeps existing material or texture assignments.";
				}

				if (pageIndex == GeneratedTextureSettingsPage)
				{
					return "Set the texture output options UMA should apply when it generates textures for this UMAMaterial. These values control mipmaps, filtering, anisotropic sampling, and optional color masking during compositing.";
				}

				return "Choose which shader texture properties UMA should override. Selected properties become UMAMaterial channels; unselected textures remain on the Unity Material as-is. Keep the base color texture first for overlay compatibility.";
			}

			private void DrawMaterialTypePage()
			{
				EditorGUILayout.LabelField("Material", EditorStyles.boldLabel);
				umaMaterialName = EditorGUILayout.TextField("UMAMaterial Name", umaMaterialName);

				if (!IsValidAssetName(umaMaterialName))
				{
					EditorGUILayout.HelpBox("Enter a valid asset name. Avoid empty names and path separator characters.", MessageType.Warning);
				}

				GUILayout.Space(8f);
				EditorGUILayout.LabelField("Material Type", EditorStyles.boldLabel);
				DrawMaterialTypeRadio(UMAMaterial.MaterialType.Atlas, "Atlas");
				DrawMaterialTypeRadio(UMAMaterial.MaterialType.NoAtlas, "No Atlas");
				DrawMaterialTypeRadio(UMAMaterial.MaterialType.UseExistingMaterial, "Use Existing Material");
				DrawMaterialTypeRadio(UMAMaterial.MaterialType.UseExistingTextures, "Use Existing Textures");

				EditorGUILayout.HelpBox(GetMaterialTypeHelp(materialType), MessageType.Info);
			}

			private void DrawMaterialTypeRadio(UMAMaterial.MaterialType type, string label)
			{
				bool selected = materialType == type;
				bool newSelected = GUILayout.Toggle(selected, label, EditorStyles.radioButton);
				if (newSelected && !selected)
				{
					materialType = type;
				}
			}

			private void DrawGeneratedTextureSettingsPage()
			{
				EditorGUILayout.LabelField("Generated Texture Settings", EditorStyles.boldLabel);
				generateMipMaps = EditorGUILayout.Toggle("Generate Mip Maps", generateMipMaps);
				mipMapBias = EditorGUILayout.Slider("Mip Map Bias", mipMapBias, -2f, 2f);
				anisoLevel = EditorGUILayout.IntSlider("Aniso Level", anisoLevel, 1, 16);
				textureFilterMode = (FilterMode)EditorGUILayout.EnumPopup("Texture Filter Mode", textureFilterMode);
				maskWithCurrentColor = EditorGUILayout.Toggle("Mask with Current Color", maskWithCurrentColor);
				using (new EditorGUI.DisabledScope(!maskWithCurrentColor))
				{
					maskMultiplier = EditorGUILayout.ColorField("Mask Multiplier", maskMultiplier);
				}
			}

			private void DrawTextureOverridePage()
			{
				EditorGUILayout.LabelField("Texture Overrides", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Select the material texture properties UMA should override. Unselected textures will remain on the Unity Material as-is. The first selected texture should be the base color.", MessageType.Info);

				if (textureSelections.Count == 0)
				{
					EditorGUILayout.HelpBox("No texture properties were found on the selected material shader. The UMAMaterial will be created without texture channels.", MessageType.Warning);
					return;
				}

				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Select All", GUILayout.Width(90f)))
				{
					SetAllTextureSelections(true);
				}
				if (GUILayout.Button("Select None", GUILayout.Width(90f)))
				{
					SetAllTextureSelections(false);
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				GUILayout.Space(4f);
				DrawTextureSelectionHeader();
				int firstSelectedIndex = GetFirstSelectedTextureIndex();
				for (int i = 0; i < textureSelections.Count; i++)
				{
					DrawTextureSelectionRow(i, firstSelectedIndex);
				}

				if (firstSelectedIndex >= 0 && IsNormalMapTexture(sourceMaterial, textureSelections[firstSelectedIndex].PropertyName))
				{
					EditorGUILayout.HelpBox("The first selected texture looks like a normal map. UMA overlays usually expect the first texture channel to be the base color.", MessageType.Warning);
				}
			}

			private void DrawTextureSelectionHeader()
			{
				Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
				EditorGUI.LabelField(new Rect(rowRect.x + 24f, rowRect.y, rowRect.width * 0.38f, rowRect.height), "Property", EditorStyles.miniBoldLabel);
				EditorGUI.LabelField(new Rect(rowRect.x + rowRect.width * 0.42f, rowRect.y, rowRect.width * 0.38f, rowRect.height), "Texture", EditorStyles.miniBoldLabel);
				EditorGUI.LabelField(new Rect(rowRect.x + rowRect.width - 88f, rowRect.y, 88f, rowRect.height), "Channel", EditorStyles.miniBoldLabel);
			}

			private void DrawTextureSelectionRow(int index, int firstSelectedIndex)
			{
				TextureSelection selection = textureSelections[index];
				EditorGUILayout.BeginHorizontal();
				selection.Selected = EditorGUILayout.Toggle(selection.Selected, GUILayout.Width(18f));
				string propertyLabel = selection.PropertyName;
				if (index == firstSelectedIndex)
				{
					propertyLabel += " (first)";
				}
				EditorGUILayout.LabelField(propertyLabel, GUILayout.MinWidth(150f));
				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.ObjectField(selection.Texture, typeof(Texture), false, GUILayout.MinWidth(160f));
				}
				EditorGUILayout.LabelField(IsNormalMapTexture(sourceMaterial, selection.PropertyName) ? "NormalMap" : "Texture", GUILayout.Width(88f));
				EditorGUILayout.EndHorizontal();
			}

			private void DrawNavigationButtons()
			{
				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(WizardHorizontalPadding);
				using (new EditorGUI.DisabledScope(pageIndex == MaterialTypePage))
				{
					if (GUILayout.Button("Previous", GUILayout.Width(90f)))
					{
						SetPage(Mathf.Max(MaterialTypePage, pageIndex - 1));
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
					if (pageIndex == TextureOverridePage)
					{
						if (GUILayout.Button("Create", GUILayout.Width(90f)))
						{
							CreateUMAMaterial();
						}
					}
					else if (GUILayout.Button("Next", GUILayout.Width(90f)))
					{
						SetPage(Mathf.Min(TextureOverridePage, pageIndex + 1));
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
			}

			private void RefreshTextureSelections()
			{
				textureSelections.Clear();
				List<string> propertyNames = GetMaterialTexturePropertyNames(sourceMaterial);
				for (int i = 0; i < propertyNames.Count; i++)
				{
					string propertyName = propertyNames[i];
					Texture texture = sourceMaterial != null && sourceMaterial.HasProperty(propertyName) ? sourceMaterial.GetTexture(propertyName) : null;
					textureSelections.Add(new TextureSelection
					{
						PropertyName = propertyName,
						Texture = texture,
						Selected = texture != null
					});
				}
			}

			private void SetAllTextureSelections(bool selected)
			{
				for (int i = 0; i < textureSelections.Count; i++)
				{
					textureSelections[i].Selected = selected;
				}
			}

			private int GetFirstSelectedTextureIndex()
			{
				for (int i = 0; i < textureSelections.Count; i++)
				{
					if (textureSelections[i].Selected)
					{
						return i;
					}
				}
				return -1;
			}

			private List<string> GetSelectedTexturePropertyNames()
			{
				List<string> selectedProperties = new List<string>();
				for (int i = 0; i < textureSelections.Count; i++)
				{
					if (textureSelections[i].Selected)
					{
						selectedProperties.Add(textureSelections[i].PropertyName);
					}
				}
				return selectedProperties;
			}

			private bool CanContinue()
			{
				return sourceMaterial != null && IsValidAssetName(umaMaterialName);
			}

			private void CreateUMAMaterial()
			{
				if (!CanContinue())
				{
					return;
				}

				string materialPath = AssetDatabase.GetAssetPath(sourceMaterial);
				if (string.IsNullOrEmpty(materialPath))
				{
					EditorUtility.DisplayDialog("Create UMAMaterial", "The selected Material is not an asset in the Project window.", "OK");
					return;
				}

				string folder = Path.GetDirectoryName(materialPath);
				if (string.IsNullOrEmpty(folder))
				{
					folder = "Assets";
				}

				string baseName = umaMaterialName.Trim();
				string assetPath = Path.Combine(folder, baseName + ".asset").Replace('\\', '/');
				UMAMaterial umaMaterial = CustomAssetUtility.CreateAsset<UMAMaterial>(assetPath, false, baseName, false);
				if (umaMaterial == null)
				{
					EditorUtility.DisplayDialog("Create UMAMaterial", "Failed to create the UMAMaterial asset.", "OK");
					return;
				}

				umaMaterial.name = baseName;
				umaMaterial.material = sourceMaterial;
				umaMaterial.materialType = materialType;
				umaMaterial.generateMipMaps = generateMipMaps;
				umaMaterial.MipMapBias = mipMapBias;
				umaMaterial.AnisoLevel = anisoLevel;
				umaMaterial.MatFilterMode = textureFilterMode;
				umaMaterial.MaskWithCurrentColor = maskWithCurrentColor;
				umaMaterial.maskMultiplier = maskMultiplier;
				umaMaterial.MaterialName = sourceMaterial.name;
				umaMaterial.ShaderName = sourceMaterial.shader != null ? sourceMaterial.shader.name : string.Empty;
				umaMaterial.channels = BuildChannelsForMaterial(sourceMaterial, GetSelectedTexturePropertyNames());
				UMAMaterial.EnsureSupportedChannelTextureFormats(umaMaterial.channels);

				EditorUtility.SetDirty(umaMaterial);
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
				Close();
				InspectorUtlity.InspectTarget(umaMaterial);
			}

			private static bool IsValidAssetName(string assetName)
			{
				if (string.IsNullOrWhiteSpace(assetName))
				{
					return false;
				}

				char[] invalidChars = Path.GetInvalidFileNameChars();
				for (int i = 0; i < invalidChars.Length; i++)
				{
					if (assetName.IndexOf(invalidChars[i]) >= 0)
					{
						return false;
					}
				}

				return assetName.IndexOf('/') < 0 && assetName.IndexOf('\\') < 0;
			}

			private static string GetMaterialTypeHelp(UMAMaterial.MaterialType type)
			{
				switch (type)
				{
					case UMAMaterial.MaterialType.Atlas:
						return "Atlas combines textures using this UMAMaterial into atlases. Each selected texture property becomes its own UMA channel.";
					case UMAMaterial.MaterialType.NoAtlas:
						return "No Atlas composites texture layers per channel without packing the result into a shared atlas.";
					case UMAMaterial.MaterialType.UseExistingMaterial:
						return "Use Existing Material keeps the selected Unity Material as the final material. UMA will not generate atlased texture output for it.";
					case UMAMaterial.MaterialType.UseExistingTextures:
						return "Use Existing Textures creates a material instance and assigns selected overlay textures to the matching shader texture properties without layer compositing.";
					default:
						return string.Empty;
				}
			}
		}

     private class UpdatePhysicsElementsWindow : EditorWindow
		{
			private enum AxisSource
			{
				X = 0,
				Y = 1,
				Z = 2
			}

			private struct AxisRemapSetting
			{
				public AxisSource Source;
				public bool Invert;
			}

			private List<UMAPhysicsElement> selectedElements;
			private string filePrepend = "U3";
			private ColliderDefinition.Direction capsuleAlignment = ColliderDefinition.Direction.Z;
			private AxisRemapSetting xRemap = new AxisRemapSetting { Source = AxisSource.Y, Invert = false };
			private AxisRemapSetting yRemap = new AxisRemapSetting { Source = AxisSource.X, Invert = true };
			private AxisRemapSetting zRemap = new AxisRemapSetting { Source = AxisSource.Z, Invert = false };
			private bool rotateJointAxis = true;
			private bool rotateJointSwingAxis = true;
			private bool rotateBoxDimensions = true;

			public static void Open(List<UMAPhysicsElement> elements)
			{
				if (elements == null || elements.Count == 0)
				{
					EditorUtility.DisplayDialog("Update Physics Elements", "Select one or more UMAPhysicsElement assets in the Project window.", "OK");
					return;
				}

				UpdatePhysicsElementsWindow window = CreateInstance<UpdatePhysicsElementsWindow>();
				window.titleContent = new GUIContent("Update Physics Elements");
				window.selectedElements = new List<UMAPhysicsElement>(elements);
				window.minSize = new Vector2(460f, 320f);
				window.ShowUtility();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField("Update Selected Physics Elements", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("This updates all selected UMAPhysicsElement assets. Collider centres are remapped using the destination axis settings below. Box dimensions and joint axes can also be remapped.", MessageType.Info);
				EditorGUILayout.LabelField("Selected Assets", selectedElements != null ? selectedElements.Count.ToString() : "0");
				filePrepend = EditorGUILayout.TextField("File Prepend", filePrepend);
				capsuleAlignment = (ColliderDefinition.Direction)EditorGUILayout.EnumPopup("Capsule Alignment", capsuleAlignment);
				rotateBoxDimensions = EditorGUILayout.Toggle("Rotate Box Dimensions", rotateBoxDimensions);
				rotateJointAxis = EditorGUILayout.Toggle("Rotate Joint Axis", rotateJointAxis);
				rotateJointSwingAxis = EditorGUILayout.Toggle("Rotate Swing Axis", rotateJointSwingAxis);

				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Collider Centre Axis Mapping", EditorStyles.boldLabel);
				DrawAxisRemapRow("Destination X", ref xRemap);
				DrawAxisRemapRow("Destination Y", ref yRemap);
				DrawAxisRemapRow("Destination Z", ref zRemap);

				GUILayout.FlexibleSpace();
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Update"))
				{
					ExecuteUpdate();
				}
				if (GUILayout.Button("Cancel"))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}

			private void DrawAxisRemapRow(string label, ref AxisRemapSetting setting)
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.PrefixLabel(label);
				setting.Source = (AxisSource)EditorGUILayout.EnumPopup(setting.Source);
				setting.Invert = EditorGUILayout.ToggleLeft("Invert", setting.Invert, GUILayout.Width(70));
				EditorGUILayout.EndHorizontal();
			}

			private void ExecuteUpdate()
			{
				if (selectedElements == null || selectedElements.Count == 0)
				{
					EditorUtility.DisplayDialog("Update Physics Elements", "No UMAPhysicsElement assets were selected.", "OK");
					Close();
					return;
				}

				int processedCount = 0;
				int renamedCount = 0;
				int skippedRenameCount = 0;
				int colliderCount = 0;
				int renamedCollisionCount = 0;
				List<string> skippedRenameAssets = new List<string>();

				try
				{
					for (int i = 0; i < selectedElements.Count; i++)
					{
						UMAPhysicsElement element = selectedElements[i];
						if (element == null)
						{
							continue;
						}

						Undo.RecordObject(element, "Update UMAPhysicsElement");
						processedCount++;

						TryRenameElement(element, ref renamedCount, ref skippedRenameCount, ref renamedCollisionCount, skippedRenameAssets);

						if (element.colliders != null)
						{
							for (int c = 0; c < element.colliders.Length; c++)
							{
								ColliderDefinition collider = element.colliders[c];
								if (collider == null)
								{
									continue;
								}

								collider.colliderCentre = RemapVector3(collider.colliderCentre);
								collider.capsuleAlignment = capsuleAlignment;
								if (rotateBoxDimensions)
								{
									collider.boxDimensions = RemapVector3(collider.boxDimensions);
								}
								colliderCount++;
							}
						}

						if (rotateJointAxis)
						{
							element.axis = RemapVector3(element.axis);
						}

						if (rotateJointSwingAxis)
						{
							element.swingAxis = RemapVector3(element.swingAxis);
						}

						EditorUtility.SetDirty(element);
					}
				}
				finally
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
				}

				string message = "Processed assets: " + processedCount
					+ "\nRenamed assets: " + renamedCount
					+ "\nSkipped renames: " + skippedRenameCount
					+ "\nUpdated colliders: " + colliderCount;

				if (renamedCollisionCount > 0 && skippedRenameAssets.Count > 0)
				{
					message += "\n\nRename collisions:";
					for (int i = 0; i < skippedRenameAssets.Count; i++)
					{
						message += "\n- " + skippedRenameAssets[i];
					}
				}

				EditorUtility.DisplayDialog("Update Physics Elements", message, "OK");
				Close();
			}

			private void TryRenameElement(UMAPhysicsElement element, ref int renamedCount, ref int skippedRenameCount, ref int renamedCollisionCount, List<string> skippedRenameAssets)
			{
				if (element == null || string.IsNullOrEmpty(filePrepend))
				{
					return;
				}

				string currentPath = AssetDatabase.GetAssetPath(element);
				if (string.IsNullOrEmpty(currentPath))
				{
					return;
				}

				string currentName = Path.GetFileNameWithoutExtension(currentPath);
				if (string.IsNullOrEmpty(currentName) || currentName.StartsWith(filePrepend, System.StringComparison.Ordinal))
				{
					return;
				}

				string targetName = filePrepend + currentName;
				string folder = Path.GetDirectoryName(currentPath);
				string extension = Path.GetExtension(currentPath);
				string targetPath = Path.Combine(folder, targetName + extension).Replace('\\', '/');

				if (AssetDatabase.LoadAssetAtPath<UMAPhysicsElement>(targetPath) != null)
				{
					skippedRenameCount++;
					renamedCollisionCount++;
					skippedRenameAssets.Add(currentName + " -> " + targetName);
					return;
				}

				string error = AssetDatabase.RenameAsset(currentPath, targetName);
				if (string.IsNullOrEmpty(error))
				{
					renamedCount++;
				}
				else
				{
					skippedRenameCount++;
					skippedRenameAssets.Add(currentName + " -> " + targetName + " (" + error + ")");
				}
			}

			private Vector3 RemapVector3(Vector3 value)
			{
				Vector3 source = value;
				Vector3 result = Vector3.zero;
				result.x = GetAxisValue(source, xRemap.Source, xRemap.Invert);
				result.y = GetAxisValue(source, yRemap.Source, yRemap.Invert);
				result.z = GetAxisValue(source, zRemap.Source, zRemap.Invert);
				return result;
			}

			private float GetAxisValue(Vector3 source, AxisSource axis, bool invert)
			{
				float value = 0f;
				if (axis == AxisSource.X)
				{
					value = source.x;
				}
				else if (axis == AxisSource.Y)
				{
					value = source.y;
				}
				else
				{
					value = source.z;
				}

				if (invert)
				{
					value = -value;
				}

				return value;
			}
		}

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

		[MenuItem("Assets/UMA/Repair Text Recipe", false, 2003)]
		private static void RepairTextRecipeMenu()
		{
			var selectedRecipes = GetSelectedTextRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Repair Text Recipe", "Select one or more UMATextRecipe assets in the Project window.", "OK");
				return;
			}

			ExamineWearables.WearablePackedSlotRepairWindow.Open(selectedRecipes[0]);
		}

		[MenuItem("Assets/UMA/Repair Text Recipe", true)]
		private static bool RepairTextRecipeMenu_Validate()
		{
			return GetSelectedTextRecipes().Count > 0;
		}

		[MenuItem("UMA/Asset Management/Consolidate Current Scene Assets", false, 2300)]
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

		[MenuItem("Assets/UMA/View and Edit weights", false, 2006)]
		private static void ViewAndEditSlotWeightsMenu()
		{
			var slots = GetSelectedSlots();
			if (slots.Count == 0)
			{
				EditorUtility.DisplayDialog("View and Edit weights", "Select a SlotDataAsset asset in the Project window.", "OK");
				return;
			}

			if (slots.Count > 1)
			{
				EditorUtility.DisplayDialog("View and Edit weights", "Open one SlotDataAsset at a time for weight editing.", "OK");
				return;
			}

			VertexEditorStage.OpenSlotWeightEditor(slots[0]);
		}

		[MenuItem("Assets/UMA/View and Edit weights", true)]
		private static bool ViewAndEditSlotWeightsMenu_Validate()
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

		[MenuItem("Assets/UMA/Open in Texture Utilities", false, 2003)]
		private static void OpenSelectedTexturesInTextureUtilitiesMenu()
		{
			var textures = GetSelectedTextures();
			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Texture Utilities", "Select one or more Texture2D assets in the Project window.", "OK");
				return;
			}

			UMATextureUtilitiesWindow.Open(textures);
		}

		[MenuItem("Assets/UMA/Open in Texture Utilities", true)]
		private static bool OpenSelectedTexturesInTextureUtilitiesMenu_Validate()
		{
			return GetSelectedTextures().Count > 0;
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

		[MenuItem("Assets/UMA/Move Unused Textures", false, 2005)]
		private static void MoveUnusedTexturesMenu()
		{
			var textures = GetSelectedTextures();
			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Move Unused Textures", "Select one or more Texture2D assets in the Project window.", "OK");
				return;
			}

			UmaMoveUnusedTexturesWindow.Open(textures);
		}

		[MenuItem("Assets/UMA/Move Unused Textures", true)]
		private static bool MoveUnusedTexturesMenu_Validate()
		{
			return GetSelectedTextures().Count > 0;
		}

		[MenuItem("Assets/UMA/Create overlay and recipe for base alternates", false, 2005)]
		private static void CreateOverlaysForSelectedItemsMenu()
		{
			var textures = GetSelectedTextures();
			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Create overlay and recipe for base alternates", "Select one or more Texture2D assets in the Project window.", "OK");
				return;
			}

			CreateOverlaysForTexturesWindow.Open(textures);
		}

		[MenuItem("Assets/UMA/Create overlay and recipe for base alternates", true)]
		private static bool CreateOverlaysForSelectedItemsMenu_Validate()
		{
			return GetSelectedTextures().Count > 0;
		}

		[MenuItem("UMA/Textures/Repair Overlays with too many textures", priority = 129)]
		private static void RepairOverlaysWithTooManyTexturesMenu()
		{
			UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
			if (indexer == null)
			{
				EditorUtility.DisplayDialog("Repair Overlays", "UMA Asset Indexer is not available.", "OK");
				return;
			}

			var overlays = indexer.GetAllAssets<OverlayDataAsset>();
			if (overlays == null || overlays.Count == 0)
			{
				EditorUtility.DisplayDialog("Repair Overlays", "No OverlayDataAsset assets were found in the UMA Asset Indexer.", "OK");
				return;
			}

			int checkedCount = 0;
			int repairedCount = 0;
			int skippedNoMaterial = 0;
			List<string> repairedNames = new List<string>();

			try
			{
				for (int i = 0; i < overlays.Count; i++)
				{
					OverlayDataAsset overlay = overlays[i];
					if (overlay == null)
					{
						continue;
					}

					checkedCount++;
					EditorUtility.DisplayProgressBar("Repair Overlays", "Checking " + overlay.name, Mathf.Clamp01((float)i / Mathf.Max(1, overlays.Count)));
					overlay.EnsureMaterial();
					UMAMaterial material = overlay.material;
					if (material == null)
					{
						skippedNoMaterial++;
						continue;
					}

					int materialChannelCount = material.channels != null ? material.channels.Length : 0;
					int overlayTextureCount = overlay.textureCount;
					if (overlayTextureCount <= materialChannelCount)
					{
						continue;
					}

					ResizeOverlayChannelData(overlay, materialChannelCount);
					EditorUtility.SetDirty(overlay);
					AssetDatabase.SaveAssetIfDirty(overlay);
					repairedCount++;
					repairedNames.Add(overlay.name + " (" + overlayTextureCount + " -> " + materialChannelCount + ")");
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			string message = "Checked overlays: " + checkedCount
				+ "\nRepaired overlays: " + repairedCount
				+ "\nSkipped without material: " + skippedNoMaterial;

			if (repairedNames.Count > 0)
			{
				message += "\n\nRepaired:";
				for (int i = 0; i < repairedNames.Count; i++)
				{
					message += "\n- " + repairedNames[i];
				}
			}

			EditorUtility.DisplayDialog("Repair Overlays", message, "OK");
		}

		private static void ResizeOverlayChannelData(OverlayDataAsset overlay, int channelCount)
		{
			overlay.textureList = ResizeArrayPreservingPrefix(overlay.textureList, channelCount);
			overlay.textureNames = ResizeArrayPreservingPrefix(overlay.textureNames, channelCount);
			overlay.overlayBlend = ResizeArrayPreservingPrefix(overlay.overlayBlend, channelCount);
		}

		private static T[] ResizeArrayPreservingPrefix<T>(T[] source, int length)
		{
			if (length < 0)
			{
				length = 0;
			}

			T[] result = new T[length];
			if (source != null)
			{
				Array.Copy(source, result, Math.Min(source.Length, length));
			}

			return result;
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

		[MenuItem("Assets/UMA/Duplicate Race", false, 2005)]
		private static void DuplicateSelectedRaceMenu()
		{
			var selectedRaces = GetSelectedRaces();
			if (selectedRaces.Count == 0)
			{
				EditorUtility.DisplayDialog("Duplicate Race", "Select one or more RaceData assets in the Project window.", "OK");
				return;
			}

			DuplicateRaceWizardWindow.Open(selectedRaces[0]);
		}

		[MenuItem("Assets/UMA/Duplicate Race", true)]
		private static bool DuplicateSelectedRaceMenu_Validate()
		{
			return GetSelectedRaces().Count > 0;
		}

		[MenuItem("Assets/UMA/Create UMAMaterial from Material", false, 2006)]
		private static void CreateUmaMaterialFromSelectedMaterialMenu()
		{
			var materials = GetSelectedMaterials();
			if (materials.Count != 1)
			{
				EditorUtility.DisplayDialog("Create UMAMaterial", "Select a single Material asset in the Project window.", "OK");
				return;
			}

			CreateUMAMaterialFromMaterialWindow.Open(materials[0]);
		}

		[MenuItem("Assets/UMA/Create UMAMaterial from Material", true)]
		private static bool CreateUmaMaterialFromSelectedMaterialMenu_Validate()
		{
			return GetSelectedMaterials().Count == 1;
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

		[MenuItem("Assets/UMA/Update Selected Physics Elements", false, 2007)]
		private static void UpdateSelectedPhysicsElementsMenu()
		{
			var selectedElements = GetSelectedPhysicsElements();
			if (selectedElements.Count == 0)
			{
				EditorUtility.DisplayDialog("Update Physics Elements", "Select one or more UMAPhysicsElement assets in the Project window.", "OK");
				return;
			}

			UpdatePhysicsElementsWindow.Open(selectedElements);
		}

		[MenuItem("Assets/UMA/Update Selected Physics Elements", true)]
		private static bool UpdateSelectedPhysicsElementsMenu_Validate()
		{
			return GetSelectedPhysicsElements().Count > 0;
		}

		[MenuItem("Assets/UMA/Create DNA for selected Modifiers", false, 2008)]
		private static void CreateDnaForSelectedMeshModifiersMenu()
		{
			var meshModifiers = GetSelectedMeshModifiers();
			if (meshModifiers.Count == 0)
			{
				EditorUtility.DisplayDialog("Create DNA for selected Modifiers", "Select one or more MeshModifier assets in the Project window.", "OK");
				return;
			}

			int createdCount = 0;
			List<string> createdNames = new List<string>();
			try
			{
				for (int i = 0; i < meshModifiers.Count; i++)
				{
					MeshModifier meshModifier = meshModifiers[i];
					if (meshModifier == null)
					{
						continue;
					}

					string dnaName = GetDnaNameFromMeshModifier(meshModifier);
					string assetPath = GetDnaAssetPathForMeshModifier(meshModifier, dnaName);
					var dna = ScriptableObject.CreateInstance<DNA>();
					foreach(var modifier in meshModifier.runtimeModifiers)
					{
						modifier.DNAName = dnaName;
					}
					dna.name = dnaName;
					dna.displayName = dnaName;
					dna.effects = new List<DNAEffect>();
					dna.effects.Add(new DNAEffect_MeshModifier
					{
						EffectName = dnaName,
						meshModifier = meshModifier,
						minMapping = 0f,
						maxMapping = 1f,
						curve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
					});

					AssetDatabase.CreateAsset(dna, assetPath);
					EditorUtility.SetDirty(dna);
					createdCount++;
					createdNames.Add(dnaName);
				}
			}
			finally
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			string message = "Created DNA assets: " + createdCount;
			if (createdNames.Count > 0)
			{
				message += "\n\nCreated:";
				for (int i = 0; i < createdNames.Count; i++)
				{
					message += "\n- " + createdNames[i];
				}
			}

			EditorUtility.DisplayDialog("Create DNA for selected Modifiers", message, "OK");
		}

		[MenuItem("Assets/UMA/Create DNA for selected Modifiers", true)]
		private static bool CreateDnaForSelectedMeshModifiersMenu_Validate()
		{
			return GetSelectedMeshModifiers().Count > 0;
		}

		[MenuItem("Assets/UMA/Enable Thumbnail From Texture for selected wardrobe items", false, 2009)]
		private static void EnableThumbnailFromTextureForSelectedWardrobeItemsMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Enable Thumbnail From Texture", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}

			int updatedCount = 0;
			try
			{
				for (int i = 0; i < selectedRecipes.Count; i++)
				{
					var recipe = selectedRecipes[i];
					if (recipe == null)
					{
						continue;
					}

					Undo.RecordObject(recipe, "Enable Thumbnail From Texture");
					recipe.thumbnailFromTexture = true;
					EditorUtility.SetDirty(recipe);
					AssetDatabase.SaveAssetIfDirty(recipe);
					updatedCount++;
				}
			}
			finally
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Enable Thumbnail From Texture", "Enabled thumbnailFromTexture on " + updatedCount + " wardrobe recipe(s).", "OK");
		}

		[MenuItem("Assets/UMA/Enable Thumbnail From Texture for selected wardrobe items", true)]
		private static bool EnableThumbnailFromTextureForSelectedWardrobeItemsMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
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

		private static List<RaceData> GetSelectedRaces()
		{
			var selected = Selection.GetFiltered(typeof(RaceData), SelectionMode.Assets);
			var races = new List<RaceData>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var race = selected[i] as RaceData;
				if (race != null)
				{
					races.Add(race);
				}
			}
			return races;
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

       internal static List<UMA.SlotDataAsset> GetSelectedSlots()
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

		private static List<UMAPhysicsElement> GetSelectedPhysicsElements()
		{
			var selected = Selection.GetFiltered(typeof(UMAPhysicsElement), SelectionMode.Assets);
			var elements = new List<UMAPhysicsElement>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var element = selected[i] as UMAPhysicsElement;
				if (element != null)
				{
					elements.Add(element);
				}
			}
			return elements;
		}

		private static List<MeshModifier> GetSelectedMeshModifiers()
		{
			var selected = Selection.GetFiltered(typeof(MeshModifier), SelectionMode.Assets);
			var meshModifiers = new List<MeshModifier>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var meshModifier = selected[i] as MeshModifier;
				if (meshModifier != null)
				{
					meshModifiers.Add(meshModifier);
				}
			}
			return meshModifiers;
		}

		private static string GetDnaNameFromMeshModifier(MeshModifier meshModifier)
		{
			string modifierName = meshModifier != null ? meshModifier.name : string.Empty;
			if (string.IsNullOrEmpty(modifierName))
			{
				return "DNA";
			}

			int lastUnderscore = modifierName.LastIndexOf('_');
			if (lastUnderscore >= 0 && lastUnderscore < modifierName.Length - 1)
			{
				return modifierName.Substring(lastUnderscore + 1);
			}

			return modifierName;
		}

		private static string GetDnaAssetPathForMeshModifier(MeshModifier meshModifier, string dnaName)
		{
			string meshModifierPath = AssetDatabase.GetAssetPath(meshModifier);
			string folder = string.IsNullOrEmpty(meshModifierPath) ? "Assets" : Path.GetDirectoryName(meshModifierPath);
			if (string.IsNullOrEmpty(folder))
			{
				folder = "Assets";
			}

			string fileName = string.IsNullOrEmpty(dnaName) ? "DNA" : dnaName;
			string assetPath = Path.Combine(folder, fileName + ".asset").Replace('\\', '/');
			return AssetDatabase.GenerateUniqueAssetPath(assetPath);
		}

		private static UMAMaterial.MaterialChannel[] BuildChannelsForMaterial(Material material)
		{
			return BuildChannelsForMaterial(material, GetMaterialTexturePropertyNames(material));
		}

		private static UMAMaterial.MaterialChannel[] BuildChannelsForMaterial(Material material, List<string> propertyNames)
		{
			if (material == null)
			{
				return new UMAMaterial.MaterialChannel[0];
			}
			if (propertyNames == null || propertyNames.Count == 0)
			{
				return new UMAMaterial.MaterialChannel[0];
			}

			var channels = new List<UMAMaterial.MaterialChannel>();
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
				if (!material.HasProperty(propName))
				{
					continue;
				}

				UMAMaterial.ChannelType channelType = IsNormalMapTexture(material, propName)
					? UMAMaterial.ChannelType.NormalMap
					: UMAMaterial.ChannelType.Texture;

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

		private static List<string> GetMaterialTexturePropertyNames(Material material)
		{
			var propertyNames = new List<string>();
			if (material == null)
			{
				return propertyNames;
			}
			var shader = material.shader;
			if (shader == null)
			{
				return propertyNames;
			}

			var rawPropertyNames = new List<string>();
			var textureProperties = material.GetTexturePropertyNames();
			if (textureProperties != null && textureProperties.Length > 0)
			{
				for (int i = 0; i < textureProperties.Length; i++)
				{
					if (!string.IsNullOrEmpty(textureProperties[i]))
					{
						rawPropertyNames.Add(textureProperties[i]);
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
						rawPropertyNames.Add(propName);
					}
				}
			}

			var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < rawPropertyNames.Count; i++)
			{
				string propName = rawPropertyNames[i];
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
				if (!material.HasProperty(propName))
				{
					continue;
				}
				seen.Add(propName);
				propertyNames.Add(propName);
			}

			MoveBaseColorTexturePropertyFirst(material, propertyNames);
			return propertyNames;
		}

		private static void MoveBaseColorTexturePropertyFirst(Material material, List<string> propertyNames)
		{
			if (propertyNames == null || propertyNames.Count < 2)
			{
				return;
			}

			int baseColorIndex = -1;
			for (int i = 0; i < propertyNames.Count; i++)
			{
				if (IsBaseColorTexture(material, propertyNames[i]))
				{
					baseColorIndex = i;
					break;
				}
			}

			if (baseColorIndex <= 0)
			{
				return;
			}

			string baseColorProperty = propertyNames[baseColorIndex];
			propertyNames.RemoveAt(baseColorIndex);
			propertyNames.Insert(0, baseColorProperty);
		}

		private static bool IsBaseColorTexture(Material material, string propertyName)
		{
			if (string.Equals(propertyName, "_MainTex", System.StringComparison.OrdinalIgnoreCase) ||
				string.Equals(propertyName, "_BaseMap", System.StringComparison.OrdinalIgnoreCase) ||
				string.Equals(propertyName, "_BaseColorMap", System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (ContainsTextureNamePart(propertyName, "albedo") ||
				ContainsTextureNamePart(propertyName, "diffuse") ||
				ContainsTextureNamePart(propertyName, "base") ||
				ContainsTextureNamePart(propertyName, "color"))
			{
				return !IsNormalMapTexture(material, propertyName);
			}

			Texture texture = material != null && material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
			if (texture == null)
			{
				return false;
			}

			return (ContainsTextureNamePart(texture.name, "albedo") ||
				ContainsTextureNamePart(texture.name, "diffuse") ||
				ContainsTextureNamePart(texture.name, "base") ||
				ContainsTextureNamePart(texture.name, "color")) && !IsNormalMapTexture(material, propertyName);
		}

		private static bool IsNormalMapTexture(Material material, string propertyName)
		{
			if (ContainsTextureNamePart(propertyName, "norm") || ContainsTextureNamePart(propertyName, "bump"))
			{
				return true;
			}

			Texture texture = material != null && material.HasProperty(propertyName) ? material.GetTexture(propertyName) : null;
			return texture != null && (ContainsTextureNamePart(texture.name, "norm") || ContainsTextureNamePart(texture.name, "bump"));
		}

		private static bool ContainsTextureNamePart(string value, string part)
		{
			return !string.IsNullOrEmpty(value) && value.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static void DestroyUmaDataPreservingGeneratedRenderers(UMAData umaData)
		{
			if (umaData == null)
			{
				return;
			}

			SkinnedMeshRenderer[] renderers = umaData.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			bool[] rendererEnabledStates = new bool[renderers.Length];
			for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
			{
				rendererEnabledStates[rendererIndex] = renderers[rendererIndex].enabled;
			}

			// UMAData.OnDestroy normally cleans generated meshes and renderer components.
			// Prefab conversion needs those generated objects after the UMA component is removed.
			// DynamicCharacterAvatar also hides its renderers during teardown, so restore their state.
			umaData.staticCharacter = true;
			DestroyImmediate(umaData);

			for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
			{
				if (renderers[rendererIndex] != null)
				{
					renderers[rendererIndex].enabled = rendererEnabledStates[rendererIndex];
				}
			}
		}

				
        public static void ConvertToNonUMA(GameObject baseObject, UMAAvatarBase avatar, string Folder, bool ConvertNormalMaps, string CharName, bool AddStandaloneDNA, bool replaceExisting, bool exportAsFbx = false, bool exportAsGltf = false, bool exportGltfAsSlots = false, bool useBoneBakingCombiner = false)
        {
			bool wasAsync = false;
			bool wasConvertRenderTexture = false;

			var generator = UMAAssetIndexer.Instance.Generator;

			if (generator == null)
			{
				EditorUtility.DisplayDialog("UMA Generator Not Found", "The UMA Generator could not be found. Please ensure that the UMA Generator is present in the project settings. This feature will not work as expected without the generator. In fact, UMA itself will not work without the Generator", "OK");
                Debug.LogWarning("UMA Generator not found. UMA itself will not work as expected.");
				return;
			}

			wasConvertRenderTexture = generator.convertRenderTexture;
			wasAsync = generator.useAsyncConversion;
			useBoneBakingCombiner &= !AddStandaloneDNA;

			try
			{
				// We need to disable async conversion and enable render texture conversion to ensure that any dynamically generated textures are properly converted and saved during this process.
				// This is necessary because the conversion and saving of textures needs to happen synchronously to ensure that all assets are correctly processed before we attempt to save them.
				// If async conversion were enabled, there could be timing issues where textures are not fully converted before we try to save them, leading to incomplete or corrupted assets.

				bool rebuildCharacter = wasConvertRenderTexture == false || useBoneBakingCombiner;
				if (rebuildCharacter)
				{
					generator.convertRenderTexture = true;
					generator.useAsyncConversion = false;

					if (avatar is DynamicCharacterAvatar dca)
					{
						UMAMeshCombiner previousMeshCombiner = generator.meshCombiner;
						UMADefaultBoneBakingMeshCombiner boneBakingCombiner = null;
						GameObject temporaryBoneBakingCombinerObject = null;

						try
						{
							if (useBoneBakingCombiner)
							{
								boneBakingCombiner = previousMeshCombiner as UMADefaultBoneBakingMeshCombiner;
								if (boneBakingCombiner == null)
								{
									UMADefaultBoneBakingMeshCombiner[] candidates =
										UMAObjectUtility.FindObjectsByType<UMADefaultBoneBakingMeshCombiner>(FindObjectsInactive.Include);
									for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
									{
										if (candidates[candidateIndex] != null &&
											candidates[candidateIndex].GetType() == typeof(UMADefaultBoneBakingMeshCombiner))
										{
											boneBakingCombiner = candidates[candidateIndex];
											break;
										}
									}
								}
								if (boneBakingCombiner == null)
								{
									temporaryBoneBakingCombinerObject = new GameObject("Temporary UMA Bone Baking Mesh Combiner");
									temporaryBoneBakingCombinerObject.hideFlags = HideFlags.HideAndDontSave;
									boneBakingCombiner = temporaryBoneBakingCombinerObject.AddComponent<UMADefaultBoneBakingMeshCombiner>();
								}
								generator.meshCombiner = boneBakingCombiner;
							}

							Debug.Log("Building DynamicCharacterAvatar synchronously to prepare it for prefab conversion.");
							dca.BuildNow();
						}
						finally
						{
							generator.meshCombiner = previousMeshCombiner;
							if (temporaryBoneBakingCombinerObject != null)
							{
								DestroyImmediate(temporaryBoneBakingCombinerObject);
							}
						}
					}
				}


                Folder = Folder + "/" + CharName;

				if (!System.IO.Directory.Exists(Folder))
				{
					System.IO.Directory.CreateDirectory(Folder);
				}

				SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
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
						Shader shader = m.shader;
						for (int i = 0; i < shader.GetPropertyCount(); i++)
						{
							if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
							{
								string propertyName = shader.GetPropertyName(i);
								Texture texture = m.GetTexture(propertyName);
								if (texture is Texture2D || texture is RenderTexture)
								{
									string path = AssetDatabase.GetAssetPath(texture.GetEntityId());
									if (string.IsNullOrEmpty(path))
									{
										bool isNormal = (propertyName.ToLower().Contains("bumpmap") || propertyName.ToLower().Contains("normal"));

										if (ConvertNormalMaps && isNormal)
										{
											texture = sconvertNormalMap(texture);
										}

										string texName = Path.Combine(Folder, CharName + "_Mat_" + Material + propertyName + ".png");
										if (texture is RenderTexture)
										{
											Debug.Log("Saving Render Texture " + texName);
											LinearSave(texture as RenderTexture, texName, isNormal);
										}
										else
										{
											Debug.Log("Saving texture " + texName);
											SaveTexture2D(texture as Texture2D, texName, isNormal);
										}

										AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
										if (isNormal)
										{
											TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(texName);
											importer.isReadable = true;
											importer.textureType = TextureImporterType.NormalMap;
											importer.maxTextureSize = 1024;
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

						string matname = Folder + "/" + CharName + "_Mat_" + Material + ".mat";
						CustomAssetUtility.SaveAsset<Material>(m, matname);
						Material++;
					}

					smr.sharedMaterials = mats;
					smr.materials = mats;
				}

				List<Material[]> savedMaterialsPerRenderer = new List<Material[]>();
				foreach (SkinnedMeshRenderer smr in renderers)
				{
					savedMaterialsPerRenderer.Add(smr.sharedMaterials);
				}

#if !UMA_FBX_EXPORT
				exportAsFbx = false;
#endif

				if (!exportAsFbx)
				{
					int savedMeshIndex = 0;
					foreach (SkinnedMeshRenderer smr in renderers)
					{
						string meshName = Folder + "/" + CharName + "_Mesh_" + savedMeshIndex + ".asset";
						savedMeshIndex++;
						

						CustomAssetUtility.SaveAsset<Mesh>(smr.sharedMesh, meshName);

						meshName = CustomAssetUtility.UnityFriendlyPath(meshName);
						Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshName);
						
						if (savedMesh != null)
						{
							SerializedObject so = new SerializedObject(savedMesh);
							var prop = so.FindProperty("m_IsReadable");
							if (prop != null)
							{
								prop.boolValue = false;
								so.ApplyModifiedPropertiesWithoutUndo();
							}
						}
					}
					AssetDatabase.SaveAssets();
				}

				var animator = baseObject.GetComponent<Animator>();
				string avatarName = Folder + "/" + CharName + "_Avatar.asset";
				if (animator != null && animator.avatar != null)
				{
					CustomAssetUtility.SaveAsset<Avatar>(animator.avatar, avatarName);
				}

				if (exportAsGltf)
				{
                 if (exportGltfAsSlots)
					{
						UMAGltfExporter.ExportAvatarSlots(avatar, Folder, CharName, true);
					}
					else
					{
						UMAGltfExporter.ExportAvatar(baseObject, Folder, CharName);
					}
				}

#if UMA_FBX_EXPORT
	if (exportAsFbx)
	{
		string fbxPath = Folder + "/" + CharName + ".fbx";
		string fullFbxPath = System.IO.Path.GetFullPath(fbxPath);

		List<Mesh> originalMeshes = new List<Mesh>();
		List<Transform[]> originalBonesArrays = new List<Transform[]>();
		foreach (SkinnedMeshRenderer smr in renderers)
		{
			Transform[] origBones = smr.bones;
			Mesh origMesh = smr.sharedMesh;
			originalBonesArrays.Add(origBones);
			originalMeshes.Add(origMesh);

			Dictionary<UMAObjectId, int> instanceIdToNewIndex = new Dictionary<UMAObjectId, int>();
			Dictionary<int, int> indexRemap = new Dictionary<int, int>();
			List<Transform> uniqueBones = new List<Transform>();
			List<Matrix4x4> uniqueBindPoses = new List<Matrix4x4>();
			Matrix4x4[] origBindPoses = origMesh.bindposes;

			for (int b = 0; b < origBones.Length; b++)
			{
				Transform bone = origBones[b];
				UMAObjectId key = bone != null ? bone.GetUmaObjectId() : ~b;

				int existingNewIndex;
				if (instanceIdToNewIndex.TryGetValue(key, out existingNewIndex))
				{
					indexRemap[b] = existingNewIndex;
				}
				else
				{
					int newIndex = uniqueBones.Count;
					instanceIdToNewIndex[key] = newIndex;
					indexRemap[b] = newIndex;
					uniqueBones.Add(bone);
					if (b < origBindPoses.Length)
					{
						uniqueBindPoses.Add(origBindPoses[b]);
					}
				}
			}

			if (uniqueBones.Count < origBones.Length)
			{
				Mesh dedupedMesh = Object.Instantiate(origMesh);
				BoneWeight[] weights = dedupedMesh.boneWeights;
				for (int w = 0; w < weights.Length; w++)
				{
					weights[w].boneIndex0 = indexRemap.ContainsKey(weights[w].boneIndex0) ? indexRemap[weights[w].boneIndex0] : weights[w].boneIndex0;
					weights[w].boneIndex1 = indexRemap.ContainsKey(weights[w].boneIndex1) ? indexRemap[weights[w].boneIndex1] : weights[w].boneIndex1;
					weights[w].boneIndex2 = indexRemap.ContainsKey(weights[w].boneIndex2) ? indexRemap[weights[w].boneIndex2] : weights[w].boneIndex2;
					weights[w].boneIndex3 = indexRemap.ContainsKey(weights[w].boneIndex3) ? indexRemap[weights[w].boneIndex3] : weights[w].boneIndex3;
				}
				dedupedMesh.boneWeights = weights;
				dedupedMesh.bindposes = uniqueBindPoses.ToArray();

				smr.sharedMesh = dedupedMesh;
				smr.bones = uniqueBones.ToArray();
			}
		}

		UMAFbxExporterBridge.ExportObject(fullFbxPath, baseObject);

		for (int s = 0; s < renderers.Length; s++)
		{
			renderers[s].sharedMesh = originalMeshes[s];
			renderers[s].bones = originalBonesArrays[s];
		}
		AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

		ModelImporter fbxImporter = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
		if (fbxImporter != null)
		{
			fbxImporter.isReadable = false;
			fbxImporter.importAnimation = false;
			fbxImporter.SaveAndReimport();
		}

		GameObject fbxModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
		GameObject newAvatar = GameObject.Instantiate(fbxModel);

		SkinnedMeshRenderer[] fbxRenderers = newAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int j = 0; j < renderers.Length; j++)
		{
			string srcName = renderers[j].gameObject.name;
			SkinnedMeshRenderer targetSmr = null;

			foreach (var fr in fbxRenderers)
			{
				if (fr.gameObject.name == srcName)
				{
					targetSmr = fr;
					break;
				}
			}
			if (targetSmr == null && fbxRenderers.Length == 1 && renderers.Length == 1)
			{
				targetSmr = fbxRenderers[0];
			}

			if (targetSmr != null)
			{
				targetSmr.sharedMaterials = savedMaterialsPerRenderer[j];
				targetSmr.enabled = true;
			}
		}

		var fbxAnimator = newAvatar.GetComponent<Animator>();
		if (fbxAnimator != null)
		{
			Avatar savedAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(avatarName);
			if (savedAvatar != null)
			{
				fbxAnimator.avatar = savedAvatar;
			}
		}

		if (AddStandaloneDNA)
		{
			UMAData srcUda = baseObject.GetComponent<UMAData>();
			if (srcUda != null)
			{
				UMAData newUda = newAvatar.AddComponent<UMAData>();
				StandAloneDNA sda = newAvatar.AddComponent<UMA.StandAloneDNA>();
				sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(srcUda._umaRecipe);
				if (avatar is DynamicCharacterAvatar)
				{
					DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
					sda.avatarDefinition = avt.GetAvatarDefinition(true);
				}
				sda.umaData = newUda;
			}
		}

		newAvatar.name = CharName;
		string prefabName = Folder + "/" + CharName + ".prefab";
		prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
		PrefabUtility.SaveAsPrefabAssetAndConnect(newAvatar, prefabName, InteractionMode.AutomatedAction);

		if (replaceExisting)
		{
			newAvatar.transform.SetPositionAndRotation(baseObject.transform.position, baseObject.transform.rotation);
			newAvatar.transform.localScale = baseObject.transform.localScale;
			DestroyImmediate(baseObject);
		}
		else
		{
			DestroyImmediate(newAvatar);
		}
	}
	else
#endif
				{
					if (replaceExisting)
					{
						DestroyUmaDataPreservingGeneratedRenderers(avatar);
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
						var dca = baseObject.GetComponent<DynamicCharacterAvatar>();
						bool prevEditorTimeGen = false;
						if (dca != null)
						{
							prevEditorTimeGen = dca.editorTimeGeneration;
							dca.editorTimeGeneration = false;
						}

						GameObject newAvatar = null;
						try
						{
							newAvatar = GameObject.Instantiate(baseObject);

							if (dca != null)
							{
								dca.editorTimeGeneration = prevEditorTimeGen;
							}

							var cloneDca = newAvatar.GetComponent<DynamicCharacterAvatar>();
							if (cloneDca != null)
							{
								DestroyUmaDataPreservingGeneratedRenderers(cloneDca);
							}

							var cloneLod = newAvatar.GetComponent<UMASimpleLOD>();
							if (cloneLod != null)
							{
								DestroyImmediate(cloneLod);
							}

							SkinnedMeshRenderer[] cloneRenderers = newAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
							for (int r = 0; r < cloneRenderers.Length; r++)
							{
								cloneRenderers[r].enabled = true;
							}

							if (AddStandaloneDNA)
							{
								if (avatar is DynamicCharacterAvatar)
								{
									DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
									StandAloneDNA sda = newAvatar.AddComponent<UMA.StandAloneDNA>();
									sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(avt.umaData._umaRecipe);
									sda.avatarDefinition = avt.GetAvatarDefinition(true);
									sda.umaData = avt.umaData;
								}
								else
								{
									UMAData uda = newAvatar.GetComponent<UMAData>();
									StandAloneDNA sda = newAvatar.AddComponent<UMA.StandAloneDNA>();
									sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
									Debug.LogWarning("Avatar is not a DynamicCharacterAvatar. AvatarDefinition will not be set on StandAloneDNA.");
									sda.umaData = uda;
								}
							}
							else
							{
								var ud = newAvatar.GetComponent<UMAData>();
								if (ud != null)
								{
									DestroyUmaDataPreservingGeneratedRenderers(ud);
								}
							}

							var cloneExpressionPlayer = newAvatar.GetComponent<UMAExpressionPlayer>();
							if (cloneExpressionPlayer != null)
							{
								DestroyImmediate(cloneExpressionPlayer);
							}

							newAvatar.name = CharName;
							string prefabName = Folder + "/" + CharName + ".prefab";
							prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
							PrefabUtility.SaveAsPrefabAssetAndConnect(newAvatar, prefabName, InteractionMode.AutomatedAction);
						}
						finally
						{
							if (dca != null)
							{
								dca.editorTimeGeneration = prevEditorTimeGen;
							}
							if (newAvatar != null)
							{
								DestroyImmediate(newAvatar);
							}
						}
					}
				}
			}
			finally
			{
				generator.convertRenderTexture = wasConvertRenderTexture;
				generator.useAsyncConversion = wasAsync;
				if (wasConvertRenderTexture == false)
				{
					if (avatar is DynamicCharacterAvatar)
					{
						var dca = avatar as DynamicCharacterAvatar;
						dca.BuildNow();
					}
				}
                Debug.Log("Conversion complete.");
            }
        }

        public static void OldConvertToNonUMA(GameObject baseObject, UMAAvatarBase avatar, string Folder, bool ConvertNormalMaps, string CharName, bool AddStandaloneDNA, bool replaceExisting)
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
								string path = AssetDatabase.GetAssetPath(texture.GetEntityId());
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
		[MenuItem("UMA/Avatar/Runtime/Save Selected Avatar Atlas Textures", priority = 120)]
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


       internal static Texture2D GetReadableTexture(RenderTexture texture, bool isNormal)
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
       internal static Texture2D GetReadableTexture(Texture2D texture, bool isNormal)
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
			ComputeShader normalMapConverter =
                UMAPathUtility.LoadInstallAsset<ComputeShader>(
                    "InternalDataStore/InGame/Resources/Shader/NormalShader.compute");
            if (normalMapConverter == null)
                normalMapConverter =
                    Resources.Load<ComputeShader>("Shader/NormalShader");
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
			ComputeShader normalMapConverter =
                UMAPathUtility.LoadInstallAsset<ComputeShader>(
                    "InternalDataStore/InGame/Resources/Shader/NormalShader.compute");
            if (normalMapConverter == null)
                normalMapConverter =
                    Resources.Load<ComputeShader>("Shader/NormalShader");
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
		[MenuItem("UMA/Avatar/Load and Save/Save Selected Avatar as UMA Preset", priority = 121)]
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
		[MenuItem("UMA/Avatar/Load and Save/Save Selected Avatar(s) Txt", priority = 122)]
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
		[MenuItem("UMA/Avatar/Load and Save/Save Selected Avatar(s) asset", priority = 123)]
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
		[MenuItem("UMA/Avatar/Load and Save/Load Selected Avatar(s) txt", priority = 124)]
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
		[MenuItem("UMA/Avatar/Load and Save/Save DynamicCharacterAvatar(s) AvatarDefinition (optimized)", priority = 125)]
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



		[UnityEditor.MenuItem("UMA/Asset Management/Add Selected Assets to Global Library")]
		public static void AddSelectedToGlobalLibrary()
		{
			int added = 0;
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;

			foreach (var o in Selection.objects)
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
}
