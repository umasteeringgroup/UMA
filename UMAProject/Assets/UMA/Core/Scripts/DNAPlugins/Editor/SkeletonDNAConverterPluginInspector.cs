using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using UnityEditor.IMGUI.Controls;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	[CustomEditor(typeof(SkeletonDNAConverterPlugin), true)]
	public class SkeletonDNAConverterPluginInspector : DynamicDNAPluginInspector
	{
		private enum searchFilterTypeOpts { 
			BoneName,
			PositionModifiers,
			RotationModifiers,
			ScaleModifiers,
			DNA,
			AdjustBones,
			NonAdjustBones
		};

		private searchFilterTypeOpts _searchFilterType = searchFilterTypeOpts.BoneName;

		private string _chosenBoneNameToAdd = "";

		private int _chosenBoneHashToAdd;

		private int _chosenPropertyToAdd = 0;

		string[] _propertyArray = new string[] { "Position", "Rotation", "Scale" };

		GUIStyle _placeholderTextStyle;

		Texture _warningIcon;

		protected override void InitPlugin()
		{
			base.InitPlugin();
			_placeholderTextStyle = new GUIStyle(EditorStyles.textArea);
			_placeholderTextStyle.fontStyle = FontStyle.Italic;
			_warningIcon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
		}

		protected override GenericMenu GetHeaderToolsMenuOptions(GenericMenu toolsMenu)
		{
			toolsMenu = base.GetHeaderToolsMenuOptions(toolsMenu);
			toolsMenu.AddSeparator("");
			toolsMenu.AddItem(new GUIContent("Expand Non-Default Initial Values"), false, ExpandNonDefaultInitialValues);
			toolsMenu.AddItem(new GUIContent("Reset Initial Values to Defaults"), false, ResetInitialValuesToDefaults);
			return toolsMenu;
		}

		private void ExpandNonDefaultInitialValues()
		{
			bool expandMain = false;
			SerializedProperty thisSkelEl = null;
			string thisSkeModProp = null;
			SerializedObject thisModObj = null;
			for (int i = 0; i < _cachedArrayElementsByIndex.Count; i++)
			{
				thisSkelEl = _cachedArrayElementsByIndex[i].element;
				if (thisModObj == null)
					thisModObj = thisSkelEl.serializedObject;
				expandMain = false;
				thisSkeModProp = thisSkelEl.FindPropertyRelative("_property").enumNames[thisSkelEl.FindPropertyRelative("_property").enumValueIndex];
				if (thisSkeModProp != "")
				{
					if (thisSkeModProp == "Position" || thisSkeModProp == "Rotation")
					{
						thisSkelEl.FindPropertyRelative("_valuesX").isExpanded = thisSkelEl.FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 0f;
						thisSkelEl.FindPropertyRelative("_valuesY").isExpanded = thisSkelEl.FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 0f;
						thisSkelEl.FindPropertyRelative("_valuesZ").isExpanded = thisSkelEl.FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 0f;
						if (thisSkelEl.FindPropertyRelative("_valuesX").isExpanded || thisSkelEl.FindPropertyRelative("_valuesY").isExpanded || thisSkelEl.FindPropertyRelative("_valuesZ").isExpanded)
							expandMain = true;
					}
					if (thisSkeModProp == "Scale")
					{
						thisSkelEl.FindPropertyRelative("_valuesX").isExpanded = thisSkelEl.FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 1f;
						thisSkelEl.FindPropertyRelative("_valuesY").isExpanded = thisSkelEl.FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 1f;
						thisSkelEl.FindPropertyRelative("_valuesZ").isExpanded = thisSkelEl.FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue != 1f;
						if (thisSkelEl.FindPropertyRelative("_valuesX").isExpanded || thisSkelEl.FindPropertyRelative("_valuesY").isExpanded || thisSkelEl.FindPropertyRelative("_valuesZ").isExpanded)
							expandMain = true;
					}
				}
				thisSkelEl.isExpanded = expandMain;
			}
			if (thisModObj != null)
				thisModObj.ApplyModifiedProperties();
			CacheArrayElementsByIndex(true);
		}

		private void ResetInitialValuesToDefaults()
		{
			if (EditorUtility.DisplayDialog("Confirm Reset", "Will reset only the Initial Values of each modifier, other settings will remain intact. There is no undo for this action, are you sure?", "Yes", "Cancel"))
			{
				SerializedProperty thisSkelEl = null;
				string thisSkeModProp = null;
				SerializedObject thisModObj = null;
				for (int i = 0; i < _cachedArrayElementsByIndex.Count; i++)
				{
					thisSkelEl = _cachedArrayElementsByIndex[i].element;
					if (thisModObj == null)
						thisModObj = thisSkelEl.serializedObject;
					thisSkeModProp = thisSkelEl.FindPropertyRelative("_property").enumNames[thisSkelEl.FindPropertyRelative("_property").enumValueIndex];
					if (thisSkeModProp != "")
					{
						if (thisSkeModProp == "Position" || thisSkeModProp == "Rotation")
						{
							thisSkelEl.FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
							thisSkelEl.FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
							thisSkelEl.FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 0f;
						}
						if (thisSkeModProp == "Scale")
						{
							thisSkelEl.FindPropertyRelative("_valuesX").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
							thisSkelEl.FindPropertyRelative("_valuesY").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
							thisSkelEl.FindPropertyRelative("_valuesZ").FindPropertyRelative("_val").FindPropertyRelative("_value").floatValue = 1f;
						}
					}
				}
				if (thisModObj != null)
					thisModObj.ApplyModifiedProperties();
				CacheArrayElementsByIndex(true);
			}
		}

		protected override void DrawElementsSearch(Rect rect)
		{
			var searchRect = new Rect(rect.xMin, rect.yMin, rect.width - 124f, rect.height);
			var searchTypeRect = new Rect(searchRect.xMax + 4f, rect.yMin, 120f, rect.height);
			base.DrawElementsSearch(searchRect);
			EditorGUI.BeginChangeCheck();
			_searchFilterType =(searchFilterTypeOpts)EditorGUI.EnumPopup(searchTypeRect, _searchFilterType);
			if (EditorGUI.EndChangeCheck())
				CacheArrayElementsByIndex(true);
		}

		protected override bool HandleElementSearch(int index)
		{
			if (elementSearchString == "" && (_searchFilterType == searchFilterTypeOpts.BoneName || _searchFilterType == searchFilterTypeOpts.DNA))
				return true;
			if (_searchFilterType == searchFilterTypeOpts.BoneName || _searchFilterType == searchFilterTypeOpts.AdjustBones || _searchFilterType == searchFilterTypeOpts.NonAdjustBones)
			{
				if (_searchFilterType == searchFilterTypeOpts.AdjustBones &&
					_cachedArrayElementsByIndex[index].element.displayName.IndexOf("Adjust", StringComparison.CurrentCultureIgnoreCase) == -1)
					return false;
				if (_searchFilterType == searchFilterTypeOpts.NonAdjustBones &&
					_cachedArrayElementsByIndex[index].element.displayName.IndexOf("Adjust", StringComparison.CurrentCultureIgnoreCase) > -1)
					return false;
				return base.HandleElementSearch(index);
			}
			else
			{
				if(_searchFilterType == searchFilterTypeOpts.PositionModifiers || _searchFilterType == searchFilterTypeOpts.RotationModifiers || _searchFilterType == searchFilterTypeOpts.ScaleModifiers)
				{
					var thisSkelEl = _cachedArrayElementsByIndex[index].element;
					string thisProperty = thisSkelEl.FindPropertyRelative("_property").enumNames[thisSkelEl.FindPropertyRelative("_property").enumValueIndex];
					if (_searchFilterType == searchFilterTypeOpts.PositionModifiers && thisProperty.IndexOf("Position", StringComparison.CurrentCultureIgnoreCase) == -1)
						return false;
					if (_searchFilterType == searchFilterTypeOpts.RotationModifiers && thisProperty.IndexOf("Rotation", StringComparison.CurrentCultureIgnoreCase) == -1)
						return false;
					if (_searchFilterType == searchFilterTypeOpts.ScaleModifiers && thisProperty.IndexOf("Scale", StringComparison.CurrentCultureIgnoreCase) == -1)
						return false;
					return base.HandleElementSearch(index);
				}
				else 
				{
					var thisSkelEl = _cachedArrayElementsByIndex[index].element;
					//In the plugin we are never using the legacy _modifiers but instead use _modifyingDNA.
					string[] XYZ = new string[] { "X", "Y", "Z" };
					SerializedProperty mods;
					SerializedProperty thisMod;
					//int modsi;
					bool _continue = true;
					foreach (string xyz in XYZ)
					{
						mods = thisSkelEl.FindPropertyRelative("_values" + xyz).FindPropertyRelative("_val").FindPropertyRelative("_modifyingDNA").FindPropertyRelative("_dnaEvaluators");
						for (int mi = 0; mi < mods.arraySize; mi++)
						{
							thisMod = mods.GetArrayElementAtIndex(mi);
							if (thisMod.FindPropertyRelative("_dnaName").stringValue.IndexOf(elementSearchString, StringComparison.CurrentCultureIgnoreCase) > -1)
								_continue = false;
						}
					}
					if (_continue)
					{
						return false;
					}
					return true;
				}
			}
		}

		protected override void DrawElementsListFooterCallback(Rect rect)
		{
			var ROLDefaults = new ReorderableList.Defaults();
			var padding = 4f;
			var _addBtnWidth = 50f;
			var _labelWidth = 78f;

			Rect addRect = rect;
			
			addRect.xMin = addRect.xMax - 420 > addRect.xMin ? addRect.xMax - 420 : addRect.xMin;
			addRect.height = (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2);
			var addBtnRect = new Rect(rect.xMax - _addBtnWidth - (padding * 2), addRect.yMin, _addBtnWidth, EditorGUIUtility.singleLineHeight);
			var fieldRect = new Rect(addRect.xMin + (padding * 2), addRect.yMin, addRect.width - _addBtnWidth - (padding * 6), EditorGUIUtility.singleLineHeight);
			var labelRect = new Rect(fieldRect.xMin, fieldRect.yMin, _labelWidth, fieldRect.height);
			var boneNameRect = new Rect(labelRect.xMax + (padding * 2), fieldRect.yMin, ((fieldRect.width - _labelWidth) / 2) - (padding * 2), fieldRect.height);
			var propertyRect = new Rect(boneNameRect.xMax + (padding * 2), fieldRect.yMin, ((fieldRect.width - _labelWidth) / 2) - (padding * 2), fieldRect.height);


			if (Event.current.type == EventType.Repaint)
			{
				var prevFooterFixedHeight = ROLDefaults.footerBackground.fixedHeight;
				ROLDefaults.footerBackground.fixedHeight = addRect.height;
				ROLDefaults.footerBackground.Draw(addRect, false, false, false, false);
				ROLDefaults.footerBackground.fixedHeight = prevFooterFixedHeight;
			}

			EditorGUI.LabelField(labelRect, new GUIContent("Add Modifier:", "Add a Skeleton Modifier to the list"));
			GUI.SetNextControlName("BoneNameField");
			_chosenBoneNameToAdd = EditorGUI.TextField(boneNameRect, _chosenBoneNameToAdd);
			if(_chosenBoneNameToAdd == "" && GUI.GetNameOfFocusedControl() != "BoneNameField")
			{
				EditorGUI.BeginDisabledGroup(true);
				EditorGUI.TextArea(boneNameRect, "Bone Name", _placeholderTextStyle);
				EditorGUI.EndDisabledGroup();
			}
			_chosenPropertyToAdd = EditorGUI.Popup(propertyRect, _chosenPropertyToAdd, _propertyArray);

			//users need a message why they cant add (a warning icon in the button maybe?
			string warningMessage = CanAddBoneForProp();
			GUIContent addBtnGUI = new GUIContent("Add", "Choose the bone name and property to add first");
			if (warningMessage != "")
				addBtnGUI.image = _warningIcon;
			addBtnGUI.tooltip = warningMessage;
			EditorGUI.BeginDisabledGroup(warningMessage != "");
			if (GUI.Button(addBtnRect,addBtnGUI))
			{
				//do it!
				Debug.Log("Created a New Modifier");
				var newModifier = new SkeletonModifier(_chosenBoneNameToAdd, UMAUtils.StringToHash(_chosenBoneNameToAdd), (SkeletonModifier.SkeletonPropType)_chosenPropertyToAdd);
				(_target as SkeletonDNAConverterPlugin).AddModifier(newModifier);
				_chosenBoneNameToAdd = "";
				serializedObject.Update();
				CacheArrayElementsByIndex(true);
			}
			EditorGUI.EndDisabledGroup();
		}
		private string CanAddBoneForProp()
		{
			if (_chosenBoneNameToAdd == "")
				return "";
			_chosenBoneHashToAdd = UMAUtils.StringToHash(_chosenBoneNameToAdd);
			for (int i = 0; i < _cachedArrayElementsByIndex.Count; i++)
			{
				var thisSkelMod = _cachedArrayElementsByIndex[i].element;
				if (thisSkelMod.FindPropertyRelative("_property").enumValueIndex == _chosenPropertyToAdd && thisSkelMod.FindPropertyRelative("_hash").intValue == _chosenBoneHashToAdd)
				{
					return "There was already a Skeleton Modifier for bone: "+_chosenBoneNameToAdd+" for property: "+ _propertyArray[_chosenPropertyToAdd];
				}
			}
			return "";
		}
	}
}
