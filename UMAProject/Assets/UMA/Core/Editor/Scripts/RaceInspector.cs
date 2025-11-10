#pragma warning disable 0472 // disable warnings about result of comparison being unused (because of if/else usage)
#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;

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
		public static bool showRaceGeneration = false;
		protected RaceData race;
		protected bool _needsUpdate;
		protected string _errorMessage;
		//we dont really want to use delayedFields because if the user does not change focus from the field in the inspector but instead selects another asset in their projects their changes dont save
		//Instead what we really want to do is set a short delay on saving so that the asset doesn't save while the user is typing in a field
		private float lastActionTime = 0;
		private bool doSave = false;
		//pRaceInspector needs to get unpacked UMATextRecipes so we might need a virtual UMAContextBase
		GameObject EditorUMAContextBase;
		List<string> ValidationMessages = new List<string>();
		#region DCS variables
		private ReorderableList wardrobeSlotList;
		private bool wardrobeSlotListInitialized = false;

		private ReorderableList prebakedBlendshapeList;
		private bool prebakedBlendshapeListInitialized = false;

		private ReorderableList unbakedShapesList;
		private bool unbakedShapesListInitialized = false;

		private int compatibleRacePickerID;
		static bool[] _BCFoldouts = new bool[0];
		List<SlotData> baseSlotsList = new List<SlotData>();
		List<string> baseSlotsNamesList = new List<string>();

		// Cached blendshape lookup for baseRaceRecipe slots
		private string[] _bsSlotNames = Array.Empty<string>();
		private Dictionary<string, string[]> _bsBySlot = new Dictionary<string, string[]>();
		private int _lastBaseRecipeId = 0;
		private bool _bsCacheValid = false;
		// UI selections for add-from-slot
		private int _prebakeAddSlotIndex = 0;
		private int _prebakeAddShapeIndex = 0;
		private int _unbakedAddSlotIndex = 0;
		private int _unbakedAddShapeIndex = 0;
		#endregion

		public void OnEnable() {
			race = target as RaceData;
			EditorApplication.update += DoDelayedSave;
		}

		void OnDestroy()
		{
			EditorApplication.update -= DoDelayedSave;
		}

		void DoDelayedSave()
		{
			if (doSave && Time.realtimeSinceStartup > (lastActionTime + 0.5f))
			{
				doSave = false;
				lastActionTime = Time.realtimeSinceStartup;
				EditorUtility.SetDirty(race);
				string path = AssetDatabase.GetAssetPath(race.GetInstanceID());
				AssetDatabase.ImportAsset(path);
				UMAUpdateProcessor.UpdateRace(race);
			}
		}

		private void EnsureBlendshapeCache()
		{
			// Pull the baseRaceRecipe reference
			var baseRecipeProp = serializedObject.FindProperty("baseRaceRecipe");
			var baseRecipe = baseRecipeProp != null ? baseRecipeProp.objectReferenceValue as UMARecipeBase : null;
			int recipeId = baseRecipe != null ? baseRecipe.GetInstanceID() : 0;
			if (_bsCacheValid && recipeId == _lastBaseRecipeId)
			{
				return;
			}

			_bsSlotNames = Array.Empty<string>();
			_bsBySlot.Clear();
			_bsCacheValid = true;
			_lastBaseRecipeId = recipeId;

			if (baseRecipe == null)
			{
				return;
			}
			var cached = baseRecipe.GetCachedRecipe();
			if (cached == null)
			{
				return;
			}
			var slots = cached.GetAllSlots();
			if (slots == null)
			{
				return;
			}
			var slotNames = new List<string>();
			for (int i = 0; i < slots.Length; i++)
			{
				var sd = slots[i];
				if (sd == null || sd.asset == null || sd.asset.meshData == null) continue;
				var md = sd.asset.meshData;
				var shapes = md.blendShapes;
				if (shapes == null || shapes.Length == 0) continue;
				// collect unique names for this slot
				var names = new List<string>();
				for (int s = 0; s < shapes.Length; s++)
				{
					var sh = shapes[s];
					if (sh == null || string.IsNullOrEmpty(sh.shapeName)) continue;
					if (!names.Contains(sh.shapeName)) names.Add(sh.shapeName);
				}
				if (names.Count == 0) continue;
				_bsBySlot[sd.slotName] = names.ToArray();
				slotNames.Add(sd.slotName);
			}
			slotNames.Sort(StringComparer.Ordinal);
			_bsSlotNames = slotNames.ToArray();
			// reset indices if out of range
			_prebakeAddSlotIndex = Mathf.Clamp(_prebakeAddSlotIndex, 0, Math.Max(0, _bsSlotNames.Length - 1));
			_unbakedAddSlotIndex = Mathf.Clamp(_unbakedAddSlotIndex, 0, Math.Max(0, _bsSlotNames.Length - 1));
			_prebakeAddShapeIndex = 0;
			_unbakedAddShapeIndex = 0;
		}

		public override void OnInspectorGUI()
		{
			if (lastActionTime == 0)
			{
				lastActionTime = Time.realtimeSinceStartup;
			}

			EditorGUI.BeginChangeCheck();
			race.raceName = EditorGUILayout.TextField("Race Name", race.raceName);
			race.umaTarget = (UMA.RaceData.UMATarget)EditorGUILayout.EnumPopup(new GUIContent("UMA Target", "The Mecanim animation rig type."), race.umaTarget);
			race.genericRootMotionTransformName = EditorGUILayout.TextField("Root Motion Transform", race.genericRootMotionTransformName);
			race.TPose = EditorGUILayout.ObjectField(new GUIContent("T-Pose", "The UMA T-Pose asset can be created by selecting the race fbx and choosing the Extract T-Pose dropdown. Only needs to be done once per race."), race.TPose, typeof(UmaTPose), false) as UmaTPose;
			race.expressionSet = EditorGUILayout.ObjectField(new GUIContent("Expression Set", "The Expression Set asset is used by the Expression player."), race.expressionSet, typeof(UMA.PoseTools.UMAExpressionSet), false) as UMA.PoseTools.UMAExpressionSet;
			EditorGUILayout.HelpBox("Fixup Rotations should be true for Blender FBX slots", MessageType.Info);
			race.FixupRotations = EditorGUILayout.Toggle("Fixup Rotations", race.FixupRotations);

			// Renderer Bounds section
			EditorGUILayout.Space();
			GUILayout.Label("Renderer Bounds", EditorStyles.boldLabel);
			SerializedProperty useManualBoundsProp = serializedObject.FindProperty("useManualRendererBounds");
			SerializedProperty manualBoundsProp = serializedObject.FindProperty("manualRendererBounds");
			SerializedProperty manualBoundsCenterProp = serializedObject.FindProperty("manualRendererBoundsCenter");
			EditorGUILayout.PropertyField(useManualBoundsProp, new GUIContent("Use Manual Renderer Bounds", "When enabled, UMA renderers will use these manual bounds (extents) instead of calculated bounds."));
			using (new EditorGUI.DisabledScope(!useManualBoundsProp.boolValue))
			{
				EditorGUILayout.PropertyField(manualBoundsProp, new GUIContent("Manual Bounds (Extents)", "Extents in local space before scaling by the 'Position' bone."));
				EditorGUILayout.PropertyField(manualBoundsCenterProp, new GUIContent("Manual Bounds Center", "Center offset in local space before scaling by the 'Position' bone."));
			}
			EditorGUILayout.Space();
			SerializedProperty useNewDNA = serializedObject.FindProperty("useNewDNA");
			EditorGUILayout.PropertyField(useNewDNA, new GUIContent("Use New DNA System", "When enabled, the new DNA system using DNA Collections will be used. Otherwise the legacy DNA Converter system will be used."));

            if (useNewDNA.boolValue)
			{
                EditorGUILayout.PropertyField(serializedObject.FindProperty("DNACollection"));
			}
			else
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("disableDNAConverters"));
				SerializedProperty dnaConverterListprop = serializedObject.FindProperty("_dnaConverterList");
				EditorGUILayout.PropertyField(dnaConverterListprop, true);
			}

			showRaceGeneration = EditorGUILayout.Foldout(showRaceGeneration, "Race Generation");
			if (showRaceGeneration)
			{
				EditorGUILayout.HelpBox("Force Rebuild Race Slots should only be enabled for testing during design phase! it forces the slots to be rebuilt every generation!", MessageType.Warning);
				SerializedProperty ForceRebuildRaceSlots = serializedObject.FindProperty("forceRebuildRaceSlots");
                ForceRebuildRaceSlots.boolValue = EditorGUILayout.Toggle(new GUIContent("Force Rebuild Race Slots", "If true, the race slots will be rebuilt when characters are generated."), ForceRebuildRaceSlots.boolValue);
                // Prebaked Blendshapes list
                DrawPrebakedBlendshapeList();

				// Unbaked Shapes To Include list
				DrawUnbakedShapesToIncludeList();
			}

			SerializedProperty dnaRanges = serializedObject.FindProperty("dnaRanges");
			EditorGUILayout.PropertyField(dnaRanges, true);
			/* tags GUI */
			SerializedProperty tags = serializedObject.FindProperty("tags");
			EditorGUILayout.PropertyField(tags, true);
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
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
							_needsUpdate = true;
						}
						break;
					}
				}
			}

			#region Validation
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Validate RaceData"))
			{
				ValidationMessages.Clear();
				DoValidate();
			}
			if (GUILayout.Button(" Clear Messages "))
			{
				ValidationMessages.Clear();
			}
			GUILayout.EndHorizontal();
			if (ValidationMessages.Count > 0)
			{
				// draw the validation messages one by one, with a little space between them. Each message should be in a helpbox.
				// if the message starts with "Error:" it should be a error helpbox, if it starts with "Warning:" it should be a warning helpbox, otherwise it should be an info helpbox.
				// put an "x" button on each message to clear it.
				List<string> displayedMessages = new List<string>();
				displayedMessages.AddRange(ValidationMessages);
				GUILayout.Label("Validation Messages:", EditorStyles.boldLabel);
				for (int i = displayedMessages.Count - 1; i >= 0; i--)
				{
					MessageType messageType = MessageType.Info;
					if (displayedMessages[i].StartsWith("Error:"))
					{
						messageType = MessageType.Error;
					}
					else if (displayedMessages[i].StartsWith("Warning:"))
					{
						messageType = MessageType.Warning;
					}
					GUILayout.BeginHorizontal();
					EditorGUILayout.HelpBox(displayedMessages[i], messageType);
					if (GUILayout.Button("x", GUILayout.Width(20)))
					{
						ValidationMessages.RemoveAt(i);
					}
					GUILayout.EndHorizontal();
				}
			}
			else
			{
				EditorGUILayout.HelpBox("No validation messages.", MessageType.Info);
			}

			void DoValidate()
			{
				RaceData race = target as RaceData;
				if (race == null)
				{
					ValidationMessages.Add("Error: RaceData is null. How is this even possible???");
					return;
				}
				if (race.baseRaceRecipe == null)
				{
					ValidationMessages.Add("Error: baseRaceRecipe is null");
				}
				if (race.TPose == null)
				{
					ValidationMessages.Add("Error: TPose is not set! This is required to build an avatar and store the base bone positions");
				}
				// validate all wardrobe slots are not null or empty
				if (race.wardrobeSlots == null)
				{
					ValidationMessages.Add("Error: wardrobeSlots is null");
				}
				else
				{
					for (int i = 0; i < race.wardrobeSlots.Count; i++)
					{
						if (String.IsNullOrWhiteSpace(race.wardrobeSlots[i]))
						{
							ValidationMessages.Add("Error: wardrobeSlots[" + i + "] is null or empty. This could cause a problem with recipes loading.");
						}
					}
				}
				if (race.umaTarget == RaceData.UMATarget.Generic && String.IsNullOrWhiteSpace(race.genericRootMotionTransformName))
				{
					ValidationMessages.Add("Error: genericRootMotionTransformName is null or empty. This is required for Generic UMA Targets.");
				}

				if (race.dnaConverterList == null)
				{
					ValidationMessages.Add("Error: dnaConverterList is null");
				}
				else
				{
					for (int i = 0; i < race.dnaConverterList.Length; i++)
					{
						var cvt = race.dnaConverterList[i];
						if (cvt == null)
						{
							ValidationMessages.Add("Error: dnaConverterList[" + i + "] is null");
						}
						else
						{
							if (cvt.dnaAsset == null)
							{
								ValidationMessages.Add("Error: dnaConverterList[" + i + "] has a null dnaAsset");
							}
							else
							{
								if (cvt.dnaAsset.Names == null || cvt.dnaAsset.Names.Length == 0)
								{
									ValidationMessages.Add("Error: dnaConverterList[" + i + "] has a dnaAsset with no DNA names");
								}
								if (cvt.dnaAsset.dnaTypeHash == 0)
								{
									ValidationMessages.Add("Error: dnaConverterList[" + i + "] has a dnaAsset with a0 dnaType Hash");
								}
								if (cvt.PluginCount == 0)
								{
									ValidationMessages.Add("Warning: dnaConverterList[" + i + "] has no DNA Converter Plugins. Is that intentional?");
								}
								for (int j = 0; j < cvt.PluginCount; j++)
								{
									var plugin = cvt.GetPlugin(j);
									if (plugin == null)
									{
										ValidationMessages.Add("Error: dnaConverterList[" + i + "] has a null plugin at index " + j);
									}
								}
							}
						}

					}
				}
				if (ValidationMessages.Count == 0)
				{
					ValidationMessages.Add("Info: No problems found. This RaceData looks good!");
				}
			}

			#endregion



			try
			{
				PreInspectorGUI(ref _needsUpdate);
				if (_needsUpdate == true) {
					_needsUpdate = false;
					DoUpdate();
				}
			} catch (UMAResourceNotFoundException e) {
				_errorMessage = e.Message;
			}

			if (GUI.changed)
			{
				doSave = true;
				lastActionTime = Time.realtimeSinceStartup;
				UMAAssetIndexer.RebuildUMAS(SceneManager.GetActiveScene());
			}
		}

		/// <summary>
		/// Add to this method in extender editors if you need to do anything extra when updating the data.
		/// </summary>
		protected virtual void DoUpdate()
		{

		}

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
				{
					Event.current.Use();//stops the Mismatched LayoutGroup errors
				}

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

		private void InitPrebakedBlendshapeList()
		{
			var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
			prebakedBlendshapeList = new ReorderableList(serializedObject, listProp, true, true, true, true);
			prebakedBlendshapeList.drawHeaderCallback = rect =>
			{
				EditorGUI.LabelField(rect, "Prebaked Blendshapes");
			};
			prebakedBlendshapeList.elementHeight = EditorGUIUtility.singleLineHeight + 6;
			prebakedBlendshapeList.drawElementCallback = (rect, index, isActive, isFocused) =>
			{
				var element = prebakedBlendshapeList.serializedProperty.GetArrayElementAtIndex(index);
				var blendShapeProp = element.FindPropertyRelative("BlendShape");
				var valueProp = element.FindPropertyRelative("value");

				rect.y += 2;
				float half = (rect.width - 20f) * 0.6f;
				var nameRect = new Rect(rect.x + 10, rect.y, half, EditorGUIUtility.singleLineHeight);
				var valueRect = new Rect(nameRect.xMax + 5, rect.y, rect.width - nameRect.width - 25f, EditorGUIUtility.singleLineHeight);

				EditorGUI.PropertyField(nameRect, blendShapeProp, GUIContent.none);
				EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);
			};
			prebakedBlendshapeList.onAddCallback = l =>
			{
				var idx = l.serializedProperty.arraySize;
				l.serializedProperty.InsertArrayElementAtIndex(idx);
				var el = l.serializedProperty.GetArrayElementAtIndex(idx);
				el.FindPropertyRelative("BlendShape").stringValue = string.Empty;
				el.FindPropertyRelative("value").floatValue = 0f;
				serializedObject.ApplyModifiedProperties();
			};
			prebakedBlendshapeListInitialized = true;
		}

		private void DrawPrebakedBlendshapeList()
		{
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
            if (!prebakedBlendshapeListInitialized || prebakedBlendshapeList == null)
			{
				InitPrebakedBlendshapeList();
			}
			EditorGUI.BeginChangeCheck();
			prebakedBlendshapeList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
			}

			// Add-from-slot UI
			EnsureBlendshapeCache();
			GUI.enabled = (_bsSlotNames.Length > 0);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Add from Base Recipe");
			if (_bsSlotNames.Length == 0)
			{
				EditorGUILayout.LabelField("No slots with blendshapes found.");
			}
			else
			{
				_prebakeAddSlotIndex = EditorGUILayout.Popup(_prebakeAddSlotIndex, _bsSlotNames, GUILayout.MaxWidth(220));
				var slotName = _bsSlotNames.Length > 0 ? _bsSlotNames[Mathf.Clamp(_prebakeAddSlotIndex, 0, _bsSlotNames.Length - 1)] : null;
				string[] shapes;
				if (!string.IsNullOrEmpty(slotName) && _bsBySlot.TryGetValue(slotName, out var arr0))
				{
					shapes = arr0;
				}
				else
				{
					shapes = Array.Empty<string>();
				}
				_prebakeAddShapeIndex = Mathf.Clamp(_prebakeAddShapeIndex,0, Math.Max(0, shapes.Length -1));
				_prebakeAddShapeIndex = EditorGUILayout.Popup(_prebakeAddShapeIndex, shapes, GUILayout.MaxWidth(260));
				EditorGUI.BeginDisabledGroup(shapes.Length ==0);
				if (GUILayout.Button("Add", GUILayout.Width(60)))
				{
					var shapeName = shapes[_prebakeAddShapeIndex];
					var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
					// prevent duplicates
					bool exists = false;
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && n.stringValue == shapeName) { exists = true; break; }
					}
					// also prevent if the shape exists in UnbakedShapesToInclude
					if (!exists)
					{
						var otherList = serializedObject.FindProperty("UnbakedShapesToInclude");
						for (int i =0; i < otherList.arraySize; i++)
						{
							var el = otherList.GetArrayElementAtIndex(i);
							if (el != null && el.stringValue == shapeName) { exists = true; break; }
						}
					}
					if (!exists)
					{
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.FindPropertyRelative("BlendShape").stringValue = shapeName;
						el.FindPropertyRelative("value").floatValue =0f;
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				// Add all missing shapes from the selected slot
				if (GUILayout.Button("Add all from slot", GUILayout.Width(150)))
				{
					var listProp = serializedObject.FindProperty("PrebakedBlendshapes");
					// Build set of existing names
					var existing = new HashSet<string>(StringComparer.Ordinal);
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && !string.IsNullOrEmpty(n.stringValue)) existing.Add(n.stringValue);
					}
					// include names from UnbakedShapesToInclude to avoid cross-adding
					var otherList = serializedObject.FindProperty("UnbakedShapesToInclude");
					for (int i =0; i < otherList.arraySize; i++)
					{
						var el = otherList.GetArrayElementAtIndex(i);
						if (el != null && !string.IsNullOrEmpty(el.stringValue)) existing.Add(el.stringValue);
					}
					int added =0;
					for (int s =0; s < shapes.Length; s++)
					{
						var shapeName = shapes[s];
						if (string.IsNullOrEmpty(shapeName) || existing.Contains(shapeName)) continue;
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.FindPropertyRelative("BlendShape").stringValue = shapeName;
						el.FindPropertyRelative("value").floatValue =0f;
						existing.Add(shapeName);
						added++;
					}
					if (added >0)
					{
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();
			GUI.enabled = true;
			GUIHelper.EndVerticalPadded(5.0f);
        }

		private void InitUnbakedShapesToIncludeList()
		{
			var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
			unbakedShapesList = new ReorderableList(serializedObject, listProp, true, true, true, true);
			unbakedShapesList.drawHeaderCallback = rect =>
			{
				EditorGUI.LabelField(rect, "Unbaked Shapes To Include");
			};
			unbakedShapesList.elementHeightCallback = index =>
			{
				if (unbakedShapesList.serializedProperty == null || index < 0 || index >= unbakedShapesList.serializedProperty.arraySize)
					return EditorGUIUtility.singleLineHeight + 6;
				var el = unbakedShapesList.serializedProperty.GetArrayElementAtIndex(index);
				var str = el != null ? el.stringValue : string.Empty;
				if (string.IsNullOrEmpty(str)) return EditorGUIUtility.singleLineHeight + 6;
				// Try to validate as regex; if invalid, add a second line for warning
				try { _ = new Regex(str); }
				catch (ArgumentException) { return (EditorGUIUtility.singleLineHeight * 2f) + 10f; }
				return EditorGUIUtility.singleLineHeight + 6;
			};
			unbakedShapesList.drawElementCallback = (rect, index, isActive, isFocused) =>
			{
				var element = unbakedShapesList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				var textRect = new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight);
				EditorGUI.BeginChangeCheck();
				var newVal = EditorGUI.TextField(textRect, GUIContent.none, element.stringValue);
				if (EditorGUI.EndChangeCheck())
				{
					element.stringValue = newVal ?? string.Empty;
				}
				if (!string.IsNullOrEmpty(element.stringValue))
				{
					try { _ = new Regex(element.stringValue); }
					catch (ArgumentException ex)
					{
						var warnRect = new Rect(rect.x + 10, textRect.yMax + 2, rect.width - 10, EditorGUIUtility.singleLineHeight);
						EditorGUI.HelpBox(warnRect, $"Invalid Regex: {ex.Message}", MessageType.Warning);
					}
				}
			};
			unbakedShapesList.onAddCallback = l =>
			{
				var idx = l.serializedProperty.arraySize;
				l.serializedProperty.InsertArrayElementAtIndex(idx);
				var el = l.serializedProperty.GetArrayElementAtIndex(idx);
				el.stringValue = string.Empty;
				serializedObject.ApplyModifiedProperties();
			};
			unbakedShapesListInitialized = true;
		}

		private void DrawUnbakedShapesToIncludeList()
		{
            GUIHelper.BeginVerticalPadded(5, new Color(0.75f, 0.875f, 1f));
            if (!unbakedShapesListInitialized || unbakedShapesList == null)
			{
				InitUnbakedShapesToIncludeList();
			}
			EditorGUILayout.HelpBox(
			"Enter exact blendshape names to include when not prebaked, or use Regular Expressions to match multiple shapes. Examples: '^Eye.*', '.*Smile.*'. Invalid regex patterns will be highlighted.",
			MessageType.Info);
			EditorGUI.BeginChangeCheck();
			unbakedShapesList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				_needsUpdate = true;
			}

			// Add-from-slot UI
			EnsureBlendshapeCache();
			GUI.enabled = (_bsSlotNames.Length > 0);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Add from Base Recipe");
			if (_bsSlotNames.Length == 0)
			{
				EditorGUILayout.LabelField("No slots with blendshapes found.");
			}
			else
			{
				_unbakedAddSlotIndex = EditorGUILayout.Popup(_unbakedAddSlotIndex, _bsSlotNames, GUILayout.MaxWidth(220));
				var slotName = _bsSlotNames.Length > 0 ? _bsSlotNames[Mathf.Clamp(_unbakedAddSlotIndex, 0, _bsSlotNames.Length - 1)] : null;
				string[] shapes;
				if (!string.IsNullOrEmpty(slotName) && _bsBySlot.TryGetValue(slotName, out var arr1))
				{
					shapes = arr1;
				}
				else
				{
					shapes = Array.Empty<string>();
				}
				_unbakedAddShapeIndex = Mathf.Clamp(_unbakedAddShapeIndex,0, Math.Max(0, shapes.Length -1));
				_unbakedAddShapeIndex = EditorGUILayout.Popup(_unbakedAddShapeIndex, shapes, GUILayout.MaxWidth(260));
				EditorGUI.BeginDisabledGroup(shapes.Length ==0);
				if (GUILayout.Button("Add", GUILayout.Width(60)))
				{
					var shapeName = shapes[_unbakedAddShapeIndex];
					var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
					// prevent duplicates
					bool exists = false;
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						if (el != null && el.stringValue == shapeName) { exists = true; break; }
					}
					// also prevent if the shape exists in PrebakedBlendshapes
					if (!exists)
					{
						var otherList = serializedObject.FindProperty("PrebakedBlendshapes");
						for (int i =0; i < otherList.arraySize; i++)
						{
							var el = otherList.GetArrayElementAtIndex(i);
							var n = el.FindPropertyRelative("BlendShape");
							if (n != null && n.stringValue == shapeName) { exists = true; break; }
						}
					}
					if (!exists)
					{
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.stringValue = shapeName;
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				// Add all missing shapes from the selected slot
				if (GUILayout.Button("Add all from slot", GUILayout.Width(150)))
				{
					var listProp = serializedObject.FindProperty("UnbakedShapesToInclude");
					// Build set of existing names
					var existing = new HashSet<string>(StringComparer.Ordinal);
					for (int i =0; i < listProp.arraySize; i++)
					{
						var el = listProp.GetArrayElementAtIndex(i);
						if (el != null && !string.IsNullOrEmpty(el.stringValue)) existing.Add(el.stringValue);
					}
					// include names from PrebakedBlendshapes to avoid cross-adding
					var otherList = serializedObject.FindProperty("PrebakedBlendshapes");
					for (int i =0; i < otherList.arraySize; i++)
					{
						var el = otherList.GetArrayElementAtIndex(i);
						var n = el.FindPropertyRelative("BlendShape");
						if (n != null && !string.IsNullOrEmpty(n.stringValue)) existing.Add(n.stringValue);
					}
					int added =0;
					for (int s =0; s < shapes.Length; s++)
					{
						var shapeName = shapes[s];
						if (string.IsNullOrEmpty(shapeName) || existing.Contains(shapeName)) continue;
						int idx = listProp.arraySize;
						listProp.InsertArrayElementAtIndex(idx);
						var el = listProp.GetArrayElementAtIndex(idx);
						el.stringValue = shapeName;
						existing.Add(shapeName);
						added++;
					}
					if (added >0)
					{
						serializedObject.ApplyModifiedProperties();
						_needsUpdate = true;
					}
				}
				EditorGUI.EndDisabledGroup();
			}
			EditorGUILayout.EndHorizontal();
			GUI.enabled = true;
            GUIHelper.EndVerticalPadded(5.0f);
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
			{
				return;
			}

			bool found = false;
			for (int i = 0; i < crossCompatibilitySettingsData.arraySize; i++)
			{
				var ccRaceName = crossCompatibilitySettingsData.GetArrayElementAtIndex(i).FindPropertyRelative("ccRace").stringValue;
				if (ccRaceName == raceDataAsset.raceName)
				{
					found = true;
				}
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
				_bsCacheValid = false; // force rebuild of blendshape cache when recipe changes
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
				{
					serializedObject.Update();
				}
			}
#pragma warning restore 618
			SerializedProperty _crossCompatibilitySettings = serializedObject.FindProperty("_crossCompatibilitySettings");
			SerializedProperty _crossCompatibilitySettingsData = _crossCompatibilitySettings.FindPropertyRelative("settingsData");
			//draw the new version of the crossCompatibility list that allows users to define what slots in THIS races base recipe equate to in the backwards compatible races base recipe
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

					UMAData.UMARecipe thisBaseRecipe = (baseRaceRecipe.objectReferenceValue as UMARecipeBase).GetCachedRecipe();
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
						{
							label += " (missing)";
						}

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
						UMAData.UMARecipe ccBaseRecipe = ccRaceData.baseRaceRecipe.GetCachedRecipe();
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
								{
									serializedObject.ApplyModifiedProperties();
								}
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
					for (int ccai =0; ccai < ccSlotsOverlays.Count; ccai++)
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
#pragma warning restore 0472
