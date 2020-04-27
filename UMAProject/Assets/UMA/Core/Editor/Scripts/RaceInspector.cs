#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System;

namespace UMA.Editors
{
	[CustomEditor(typeof(RaceData))]
	public class RaceInspector : Editor 
	{
		[MenuItem("Assets/Create/UMA/Core/RaceData")]
		public static void CreateRaceMenuItem()
		{
			CustomAssetUtility.CreateAsset<RaceData>();
		}

		protected RaceData race;
		protected bool _needsUpdate;
		protected string _errorMessage;
		//we dont really want to use delayedFields because if the user does not change focus from the field in the inspector but instead selects another asset in their projects their changes dont save
		//Instead what we really want to do is set a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;
		//pRaceInspector needs to get unpacked UMATextRecipes so we might need a virtual UMAContextBase
		GameObject EditorUMAContextBase;

		#region DCS variables
		private ReorderableList wardrobeSlotList;
		private bool wardrobeSlotListInitialized = false;

		private int compatibleRacePickerID;
		static bool[] _BCFoldouts = new bool[0];
		List<SlotData> baseSlotsList = new List<SlotData>();
		List<string> baseSlotsNamesList = new List<string>();
		#endregion

		public void OnEnable() {
			race = target as RaceData;
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
			if (EditorUMAContextBase != null)
				DestroyEditorUMAContextBase();

		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(race);
				AssetDatabase.SaveAssets();
			}
		}

		private void DestroyEditorUMAContextBase()
		{
			if (EditorUMAContextBase != null)
			{
				foreach (Transform child in EditorUMAContextBase.transform)
				{
					DestroyImmediate(child.gameObject);
				}
				DestroyImmediate(EditorUMAContextBase);
			}
		}

		public override void OnInspectorGUI()
		{
			if (lastActionTime == 0)
				lastActionTime = Time.realtimeSinceStartup;

			race.raceName = EditorGUILayout.TextField("Race Name", race.raceName);
			race.umaTarget = (UMA.RaceData.UMATarget)EditorGUILayout.EnumPopup(new GUIContent("UMA Target", "The Mecanim animation rig type."), race.umaTarget);
			race.genericRootMotionTransformName = EditorGUILayout.TextField("Root Motion Transform", race.genericRootMotionTransformName);
			race.TPose = EditorGUILayout.ObjectField(new GUIContent("T-Pose", "The UMA T-Pose asset can be created by selecting the race fbx and choosing the Extract T-Pose dropdown. Only needs to be done once per race."), race.TPose, typeof(UmaTPose), false) as UmaTPose;
			race.expressionSet = EditorGUILayout.ObjectField(new GUIContent("Expression Set", "The Expression Set asset is used by the Expression player."), race.expressionSet, typeof(UMA.PoseTools.UMAExpressionSet), false) as UMA.PoseTools.UMAExpressionSet;

			EditorGUILayout.Space();

			SerializedProperty dnaConverterListprop = serializedObject.FindProperty("_dnaConverterList");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaConverterListprop, true);
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}

			SerializedProperty dnaRanges = serializedObject.FindProperty("dnaRanges");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaRanges, true);
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}

			foreach (var field in race.GetType().GetFields())
			{
				foreach (var attribute in System.Attribute.GetCustomAttributes(field))
				{
					if (attribute is UMAAssetFieldVisible)
					{
						SerializedProperty serializedProp = serializedObject.FindProperty(field.Name);
						EditorGUI.BeginChangeCheck();
						EditorGUILayout.PropertyField(serializedProp);
						if (EditorGUI.EndChangeCheck())
						{
							serializedObject.ApplyModifiedProperties();
						}
						break;
					}
				}
			}

			try {
				PreInspectorGUI(ref _needsUpdate);
				if(_needsUpdate == true){
						DoUpdate();
				}
			}catch (UMAResourceNotFoundException e){
				_errorMessage = e.Message;
			}

			if (GUI.changed)
			{
				doSave = true;
				lastActionTime = Time.realtimeSinceStartup;
			}
		}

		/// <summary>
		/// Add to this method in extender editors if you need to do anything extra when updating the data.
		/// </summary>
		protected virtual void DoUpdate() { }

		#region DCS functions
		// Drop area for Backwards Compatible Races
		private void CompatibleRacesDropArea(Rect dropArea, SerializedProperty crossCompatibilitySettingsData)
		{
			Event evt = Event.current;
			//make the box clickable so that the user can select raceData assets from the asset selection window
			if (evt.type == EventType.MouseUp)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					compatibleRacePickerID = EditorGUIUtility.GetControlID(new GUIContent("crfObjectPicker"), FocusType.Passive);
					EditorGUIUtility.ShowObjectPicker<RaceData>(null, false, "", compatibleRacePickerID);
					Event.current.Use();//stops the Mismatched LayoutGroup errors
					return;
				}
			}
			if (evt.commandName == "ObjectSelectorUpdated" && EditorGUIUtility.GetObjectPickerControlID() == compatibleRacePickerID)
			{
				RaceData tempRaceDataAsset = EditorGUIUtility.GetObjectPickerObject() as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
				}
				if (Event.current.type != EventType.Layout)
					Event.current.Use();//stops the Mismatched LayoutGroup errors
				return;
			}
			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();

					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
					for (int i = 0; i < draggedObjects.Length; i++)
					{
						if (draggedObjects[i])
						{
							RaceData tempRaceDataAsset = draggedObjects[i] as RaceData;
							if (tempRaceDataAsset)
							{
								AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
								continue;
							}

							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path, crossCompatibilitySettingsData);
							}
						}
					}
				}
			}
		}

		private void RecursiveScanFoldersForAssets(string path, SerializedProperty crossCompatibilitySettingsData)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				var tempRaceDataAsset = AssetDatabase.LoadAssetAtPath(assetFile, typeof(RaceData)) as RaceData;
				if (tempRaceDataAsset)
				{
					AddRaceDataAsset(tempRaceDataAsset, crossCompatibilitySettingsData);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'), crossCompatibilitySettingsData);
			}
		}

		private void AddRaceDataAsset(RaceData raceDataAsset, SerializedProperty crossCompatibilitySettingsData)
		{
			if (raceDataAsset.raceName == serializedObject.FindProperty("raceName").stringValue)
				return;

			bool found = false;
			for (int i = 0; i < crossCompatibilitySettingsData.arraySize; i++)
			{
				var ccRaceName = crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccRace").stringValue;
				if (ccRaceName == raceDataAsset.raceName)
					found = true;
			}
			if (!found)
			{
				crossCompatibilitySettingsData.InsertArrayElementAtIndex(crossCompatibilitySettingsData.arraySize);
				crossCompatibilitySettingsData.GetArrayElementAtIndex(crossCompatibilitySettingsData.arraySize - 1).FindPropertyRelative("ccRace").stringValue = raceDataAsset.raceName;
				serializedObject.ApplyModifiedProperties();
			}
			//if (!compatibleRaces.Contains(raceDataAsset.raceName))
			//	compatibleRaces.Add(raceDataAsset.raceName);
		}

		/// <summary>
		/// Add to PreInspectorGUI in any derived editors to allow editing of new properties added to races.
		/// </summary>
		//partial void PreInspectorGUI(ref bool result);
		protected virtual void PreInspectorGUI(ref bool result)
		{
			if (!wardrobeSlotListInitialized)
			{
				InitWardrobeSlotList();
			}
			result = AddExtraStuff();
		}

		private void InitWardrobeSlotList()
		{
			var thisWardrobeSlotList = serializedObject.FindProperty("wardrobeSlots");
			if (thisWardrobeSlotList.arraySize == 0)
			{
				race.ValidateWardrobeSlots(true);
				thisWardrobeSlotList = serializedObject.FindProperty("wardrobeSlots");
			}
			wardrobeSlotList = new ReorderableList(serializedObject, thisWardrobeSlotList, true, true, true, true);
			wardrobeSlotList.drawHeaderCallback = (Rect rect) => {
				EditorGUI.LabelField(rect, "Wardrobe Slots");
			};
			wardrobeSlotList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
				var element = wardrobeSlotList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				element.stringValue = EditorGUI.TextField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), element.stringValue);
			};
			wardrobeSlotListInitialized = true;
		}

		public bool AddExtraStuff()
		{
			SerializedProperty baseRaceRecipe = serializedObject.FindProperty("baseRaceRecipe");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(baseRaceRecipe, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			if (wardrobeSlotList == null)
			{
				InitWardrobeSlotList();
			}

			EditorGUILayout.Space();

			EditorGUI.BeginChangeCheck();
			wardrobeSlotList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				if (!race.ValidateWardrobeSlots())
				{
					EditorUtility.SetDirty(race);
				}
			}
			//new CrossCompatibilitySettings
			//To push any old settings in RaceData.backwardsCompatibleWith into the new crossCompatibilitySettings we have to call GetCrossCompatibleRaces() directly on the target
#pragma warning disable 618
			if (race.backwardsCompatibleWith.Count > 0)
			{
				var cc = race.GetCrossCompatibleRaces();
				if (cc.Count > 0)
					serializedObject.Update();
			}
#pragma warning restore 618
			SerializedProperty _crossCompatibilitySettings = serializedObject.FindProperty("_crossCompatibilitySettings");
			SerializedProperty _crossCompatibilitySettingsData = _crossCompatibilitySettings.FindPropertyRelative("settingsData");
			//draw the new version of the crossCompatibility list that allows users to define what slots in this races base recipe equate to in the backwards compatible races base recipe
			_crossCompatibilitySettings.isExpanded = EditorGUILayout.Foldout(_crossCompatibilitySettings.isExpanded, "Cross Compatibility Settings");
			if (_crossCompatibilitySettings.isExpanded)
			{
				//draw an info foldout
				EditorGUI.indentLevel++;
				_crossCompatibilitySettingsData.isExpanded = EditorGUILayout.Foldout(_crossCompatibilitySettingsData.isExpanded, "Help");
				if (_crossCompatibilitySettingsData.isExpanded)
				{
					var helpText = "CrossCompatibilitySettings allows this race to wear wardrobe slots from another race, if this race has a wardrobe slot that the recipe is set to.";
					helpText += " You can further configure the compatibility settings for each compatible race to define 'equivalent' slotdatas in the races' base recipes.";
					helpText += " For example you could define that this races 'highpolyMaleChest' slotdata in its base recipe is equivalent to HumanMales 'MaleChest' slot data in its base recipe.";
					helpText += " This would mean that any recipes which hid or applied an overlay to 'MaleChest' would hide or apply an overlay to 'highPolyMaleChest' on this race.";
					helpText += " If 'Overlays Match' is unchecked then overlays in a recipe wont be applied.";
					EditorGUILayout.HelpBox(helpText, MessageType.Info);
				}
				EditorGUI.indentLevel--;
				if (baseRaceRecipe.objectReferenceValue != null)
				{
					Rect dropArea = new Rect();
					dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
					GUI.Box(dropArea, "Drag cross compatible Races here. Click to pick.");
					CompatibleRacesDropArea(dropArea, _crossCompatibilitySettingsData);
					EditorGUILayout.Space();
					//update the foldouts list if the dropbox changes anything
					if (_BCFoldouts.Length != _crossCompatibilitySettingsData.arraySize)
					{
						Array.Resize<bool>(ref _BCFoldouts, _crossCompatibilitySettingsData.arraySize);
					}
					//we need an uptodate list of the slots in THIS races base recipe
					baseSlotsList.Clear();
					baseSlotsNamesList.Clear();
					//editing a race will require a context too because we need to get the base recipes and their slots
					if (UMAContextBase.Instance == null)
					{
						EditorUMAContextBase = UMAContextBase.CreateEditorContext();
					}
					UMAData.UMARecipe thisBaseRecipe = (baseRaceRecipe.objectReferenceValue as UMARecipeBase).GetCachedRecipe(UMAContextBase.Instance);
					SlotData[] thisBaseSlots = thisBaseRecipe.GetAllSlots();
					foreach (SlotData slot in thisBaseSlots)
					{
						if (slot != null)
						{
							baseSlotsList.Add(slot);
							baseSlotsNamesList.Add(slot.slotName);
						}
					}
					List<int> crossCompatibleSettingsToDelete = new List<int>();
					//draw a foldout area for each compatible race that will show an entry for each slot in this races base recipe 
					//with a picker to choose the slot from the compatible race's base recipe that it equates to
					for (int i = 0; i < _crossCompatibilitySettingsData.arraySize; i++)
					{
						bool del = false;
						var thisCCSettings = _crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccSettings");
						var ccRaceName = _crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccRace").stringValue;
						//this could be missing- we should show that
						var label = ccRaceName;
						if (GetCompatibleRaceData(ccRaceName) == null)
							label += " (missing)";
						GUIHelper.FoldoutBar(ref _BCFoldouts[i], label, out del);
						if (del)
						{
							crossCompatibleSettingsToDelete.Add(i);
						}
						if (_BCFoldouts[i])
						{
							DrawCCUI(ccRaceName, baseRaceRecipe, thisCCSettings);
						}
					}
					if (crossCompatibleSettingsToDelete.Count > 0)
					{
						foreach (int del in crossCompatibleSettingsToDelete)
						{
							_crossCompatibilitySettingsData.DeleteArrayElementAtIndex(del);
							serializedObject.ApplyModifiedProperties();
						}

					}
				}
				else
				{
					EditorGUILayout.HelpBox("Please define this races baseRaceRecipe before trying to define its cross compatibility settings.", MessageType.Info);
				}
			}

			EditorGUILayout.Space();

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(serializedObject.FindProperty("raceThumbnails"), true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
			return false;
		}

		private RaceData GetCompatibleRaceData(string raceName)
		{
			RaceData foundRace = null;
			string[] foundRacesStrings = AssetDatabase.FindAssets("t:RaceData");
			for (int i = 0; i < foundRacesStrings.Length; i++)
			{
				RaceData thisFoundRace = AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GUIDToAssetPath(foundRacesStrings[i]));
				if (thisFoundRace.raceName == raceName)
				{
					foundRace = thisFoundRace;
					break;
				}
			}
			return foundRace;
		}

		private void DrawCCUI(string ccRaceName, SerializedProperty baseRaceRecipe, SerializedProperty thisCCSettings)
		{
			GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
			EditorGUILayout.LabelField("Equivalent Slots with " + ccRaceName, EditorStyles.centeredGreyMiniLabel);
			if (baseRaceRecipe.objectReferenceValue == null)
			{
				EditorGUILayout.HelpBox("Please set this Races 'Base Race Recipe' before trying to set equivalent Slots.", MessageType.Warning);
			}
			else
			{
				//we need to get the base raceRecipeSlots for this compatible race
				var ccRaceData = GetCompatibleRaceData(ccRaceName);
				if (ccRaceData != null)
				{
					if (ccRaceData.baseRaceRecipe == null)
					{
						EditorGUILayout.HelpBox("Please set " + ccRaceData.raceName + " Races 'Base Race Recipe' before trying to set equivalent Slots.", MessageType.Warning);
					}
					else
					{
						var ccSlotsList = new List<SlotData>();
						var ccSlotsNamesList = new List<string>();
						UMAData.UMARecipe ccBaseRecipe = ccRaceData.baseRaceRecipe.GetCachedRecipe(UMAContextBase.Instance);
						SlotData[] ccBaseSlots = ccBaseRecipe.GetAllSlots();
						foreach (SlotData slot in ccBaseSlots)
						{
							if (slot != null)
							{
								ccSlotsList.Add(slot);
								ccSlotsNamesList.Add(slot.slotName);
							}
						}
						//if that worked we can draw the UI for any set values and a button to add new ones
						GUIHelper.BeginVerticalPadded(2, new Color(1f, 1f, 1f, 0.5f));
						var headerRect = GUILayoutUtility.GetRect(0.0f, (EditorGUIUtility.singleLineHeight * 2), GUILayout.ExpandWidth(true));
						var slotLabelRect = headerRect;
						var gapRect = headerRect;
						var cSlotLabelRect = headerRect;
						var overlaysMatchLabelRect = headerRect;
						var deleteRect = headerRect;
						slotLabelRect.width = (headerRect.width - 50f - 22f - 22f) / 2;
						gapRect.xMin = slotLabelRect.xMax;
						gapRect.width = 22f;
						cSlotLabelRect.xMin = gapRect.xMax;
						cSlotLabelRect.width = slotLabelRect.width;
						overlaysMatchLabelRect.xMin = cSlotLabelRect.xMax;
						overlaysMatchLabelRect.width = 50f;
						deleteRect.xMin = overlaysMatchLabelRect.xMax;
						deleteRect.width = 22f;
						//move this up
						var tableHeaderStyle = EditorStyles.wordWrappedMiniLabel;
						tableHeaderStyle.alignment = TextAnchor.MiddleCenter;
						//we need a gui style for this that wraps the text and vertically centers it in the space
						EditorGUI.LabelField(slotLabelRect, "This Races Slot", tableHeaderStyle);
						EditorGUI.LabelField(gapRect, "", tableHeaderStyle);
						EditorGUI.LabelField(cSlotLabelRect, "Compatible Races Slot", tableHeaderStyle);
						EditorGUI.LabelField(overlaysMatchLabelRect, "Overlays Match", tableHeaderStyle);
						GUIHelper.EndVerticalPadded(2);
						GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.875f, 1f));
						if (thisCCSettings.arraySize > 0)
						{
							for (int ccsd = 0; ccsd < thisCCSettings.arraySize; ccsd++)
							{
								if (DrawCCUISetting(ccsd, thisCCSettings, ccSlotsNamesList))
									serializedObject.ApplyModifiedProperties();
							}

						}
						else
						{
							EditorGUILayout.LabelField("No equivalent slots defined", EditorStyles.miniLabel);
						}
						GUIHelper.EndVerticalPadded(2);
						var addButtonRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
						addButtonRect.xMin = addButtonRect.xMax - 70f;
						addButtonRect.width = 70f;
						if (GUI.Button(addButtonRect, "Add"))
						{
							thisCCSettings.InsertArrayElementAtIndex(thisCCSettings.arraySize);
							serializedObject.ApplyModifiedProperties();
						}
					}
				}
				else
				{
					EditorGUILayout.HelpBox("The cross compatible race " + ccRaceName + " could not be found!", MessageType.Warning);
				}
			}
			GUIHelper.EndVerticalPadded(5);
		}

		private bool DrawCCUISetting(int ccsd, SerializedProperty thisCCSettings, List<string> ccSlotsNamesList)
		{
			var changed = false;
			var startingRect = GUILayoutUtility.GetRect(0.0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
			var thisSlot = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("raceSlot").stringValue;
			var thisSlotIndex = baseSlotsNamesList.IndexOf(thisSlot);
			var thisCompatibleSlot = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlot").stringValue;
			var thisCompatibleSlotIndex = ccSlotsNamesList.IndexOf(thisCompatibleSlot);
			var thisOverlaysMatch = thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("overlaysMatch").boolValue;
			var thisSlotRect = startingRect;
			var thisEqualsLabelRect = startingRect;
			var thisCompatibleSlotRect = startingRect;
			//var thisOverlaysLabelRect = startingRect;
			var thisOverlaysMatchRect = startingRect;
			var thisDeleteRect = startingRect;
			thisSlotRect.width = (startingRect.width - 50f - 22f - 22f) / 2;
			thisEqualsLabelRect.xMin = thisSlotRect.xMax;
			thisEqualsLabelRect.width = 22f;
			thisCompatibleSlotRect.xMin = thisEqualsLabelRect.xMax;
			thisCompatibleSlotRect.width = thisSlotRect.width;
			thisOverlaysMatchRect.xMin = thisCompatibleSlotRect.xMax + 22f;
			thisOverlaysMatchRect.width = 50f - 22f;
			thisDeleteRect.xMin = thisOverlaysMatchRect.xMax;
			thisDeleteRect.width = 22f;
			EditorGUI.BeginChangeCheck();
			var newSlotIndex = EditorGUI.Popup(thisSlotRect, "", thisSlotIndex, baseSlotsNamesList.ToArray());
			if (EditorGUI.EndChangeCheck())
			{
				if (newSlotIndex != thisSlotIndex)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("raceSlot").stringValue = baseSlotsNamesList[newSlotIndex];
					changed = true;
				}
			}
			EditorGUI.LabelField(thisEqualsLabelRect, "==");
			EditorGUI.BeginChangeCheck();
			var newCompatibleSlotIndex = EditorGUI.Popup(thisCompatibleSlotRect, "", thisCompatibleSlotIndex, ccSlotsNamesList.ToArray());
			if (EditorGUI.EndChangeCheck())
			{
				if (newCompatibleSlotIndex != thisCompatibleSlotIndex)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlot").stringValue = ccSlotsNamesList[newCompatibleSlotIndex];
					/*var ccSlotsOverlays = ccSlotsList[newCompatibleSlotIndex].GetOverlayList();
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlotOverlays").arraySize = ccSlotsOverlays.Count;
					for (int ccai = 0; ccai < ccSlotsOverlays.Count; ccai++)
						thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("compatibleRaceSlotOverlays").GetArrayElementAtIndex(ccai).stringValue = ccSlotsOverlays[ccai].overlayName;*/
					changed = true;
				}
			}
			//we need a gui style for this that centers this horizontally
			EditorGUI.BeginChangeCheck();
			var newOverlaysMatch = EditorGUI.ToggleLeft(thisOverlaysMatchRect, " ", thisOverlaysMatch);
			if (EditorGUI.EndChangeCheck())
			{
				if (newOverlaysMatch != thisOverlaysMatch)
				{
					thisCCSettings.GetArrayElementAtIndex(ccsd).FindPropertyRelative("overlaysMatch").boolValue = newOverlaysMatch;
					changed = true;
				}
			}
			if (GUI.Button(thisDeleteRect, "X", EditorStyles.miniButton))
			{
				thisCCSettings.DeleteArrayElementAtIndex(ccsd);
				changed = true;
			}
			//******NEEDS TO BE IN THE RETURN***//
			//if (changed)
			//	serializedObject.ApplyModifiedProperties();
			//GUILayout.EndHorizontal();
			GUILayout.Space(2f);
			return changed;
		}
		#endregion
	}
}
#endif
