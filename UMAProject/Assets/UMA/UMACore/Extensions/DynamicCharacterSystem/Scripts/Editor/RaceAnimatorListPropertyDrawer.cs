#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMACharacterSystem;

[CustomPropertyDrawer (typeof(DynamicCharacterAvatar.RaceAnimatorList))]
public class RaceAnimatorListPropertyDrawer : PropertyDrawer {
	float padding = 2f;
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label){
		EditorGUI.BeginProperty (position, label, property);
		var r0 = new Rect (position.xMin, position.yMin, position.width, EditorGUIUtility.singleLineHeight);
		SerializedProperty foldoutProp1 = property.FindPropertyRelative ("defaultAnimationController");
		foldoutProp1.isExpanded = EditorGUI.Foldout (r0, foldoutProp1.isExpanded, "Race Animation Controllers");
		if (foldoutProp1.isExpanded) {
			EditorGUI.indentLevel++;
			var valR = r0;
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			EditorGUI.PropertyField (valR,property.FindPropertyRelative ("defaultAnimationController"));
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			SerializedProperty foldoutProp2 = property.FindPropertyRelative ("animators");
			foldoutProp2.isExpanded = EditorGUI.Foldout (valR, foldoutProp2.isExpanded, "Race Animators");
			//we cant delete elements in the loop so ...
			List<int> willDeleteArrayElementAtIndex = new List<int> ();
			if (foldoutProp2.isExpanded) {
				EditorGUI.indentLevel++;
				var thisAnimatorsProp = property.FindPropertyRelative ("animators");
				var numAnimators = thisAnimatorsProp.arraySize;
				for (int i = 0; i < numAnimators; i++) {
					var thisAnimtorProp = thisAnimatorsProp.GetArrayElementAtIndex (i);
					valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
					var propsR = valR;
					propsR.width = propsR.width - 20f;
					var rPropR = propsR;
					rPropR.width = rPropR.width / 2;
					var aPropR = rPropR;
					aPropR.x = propsR.x + rPropR.width;
					var rLabelR = rPropR;
					rLabelR.width = (float)(rLabelR.width * 0.3)+(15f * (EditorGUI.indentLevel -1));
					var rFieldR = rPropR;
					rFieldR.x = rFieldR.x + rLabelR.width;
					rFieldR.width = rFieldR.width - rLabelR.width;
					//
					var aLabelR = aPropR;
					aLabelR.width = (float)(aLabelR.width * 0.3);
					var aFieldR = aPropR;
					aFieldR.x = aFieldR.x + aLabelR.width;
					aFieldR.width = aFieldR.width - aLabelR.width;
					var removeR = propsR;
					removeR.x = aFieldR.xMax;
					removeR.width = 20f;

					EditorGUI.LabelField (rLabelR, "Race");
					EditorGUI.indentLevel--;
					EditorGUI.indentLevel--;
					if (thisAnimtorProp.FindPropertyRelative ("raceName").stringValue == "") {
						//draw an object field for RaceData
						EditorGUI.BeginChangeCheck();
						RaceData thisRD = null;
						thisRD = (RaceData)EditorGUI.ObjectField (rFieldR, thisRD, typeof(RaceData),false);
						//if this gets filled set the values
						if(EditorGUI.EndChangeCheck()){
							if (thisRD != null) {
								thisAnimatorsProp.GetArrayElementAtIndex (i).FindPropertyRelative ("raceName").stringValue = thisRD.raceName;
							}
						}
					} else {
						EditorGUI.BeginDisabledGroup (true);
						EditorGUI.TextField (rFieldR, thisAnimtorProp.FindPropertyRelative ("raceName").stringValue);
						EditorGUI.EndDisabledGroup ();
					}
					EditorGUI.LabelField (aLabelR, "Animator");
					if (thisAnimtorProp.FindPropertyRelative ("animatorControllerName").stringValue == "") {
						//draw an object field for RunTimeAnimatorController
						EditorGUI.BeginChangeCheck();
						RuntimeAnimatorController thisRC = null;
						thisRC = (RuntimeAnimatorController)EditorGUI.ObjectField (aFieldR, thisRC, typeof(RuntimeAnimatorController), false);
						//if this gets filled set the values
						if(EditorGUI.EndChangeCheck()){
							if (thisRC != null) {
								thisAnimatorsProp.GetArrayElementAtIndex (i).FindPropertyRelative ("animatorControllerName").stringValue = thisRC.name;
								thisAnimatorsProp.GetArrayElementAtIndex (i).FindPropertyRelative ("animatorController").objectReferenceValue = thisRC;
							}
						}
					} else {
						EditorGUI.BeginDisabledGroup (true);
						EditorGUI.TextField (aFieldR, thisAnimtorProp.FindPropertyRelative ("animatorControllerName").stringValue);
						EditorGUI.EndDisabledGroup ();
					}
					if(GUI.Button(removeR,"X")){
						willDeleteArrayElementAtIndex.Add(i);
					}
					EditorGUI.indentLevel++;
					EditorGUI.indentLevel++;
				}
				if (willDeleteArrayElementAtIndex.Count > 0) {
					foreach (int i in willDeleteArrayElementAtIndex) {
						thisAnimatorsProp.DeleteArrayElementAtIndex (i);
					}
				}
				thisAnimatorsProp.serializedObject.ApplyModifiedProperties();
				valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
				var butValR = valR;
				//GUI doesn't know EditorGUI.indentLevel
				butValR.xMin = valR.xMin + (15 * EditorGUI.indentLevel);
				if(GUI.Button(butValR,"Add Race Animator")){
					//add a new element to the list
					thisAnimatorsProp.InsertArrayElementAtIndex(numAnimators);
					thisAnimatorsProp.serializedObject.ApplyModifiedProperties();
					//make sure its blank
					thisAnimatorsProp.GetArrayElementAtIndex(numAnimators).FindPropertyRelative("raceName").stringValue = "";
					thisAnimatorsProp.GetArrayElementAtIndex(numAnimators).FindPropertyRelative("animatorControllerName").stringValue = "";
					thisAnimatorsProp.GetArrayElementAtIndex(numAnimators).FindPropertyRelative("animatorController").objectReferenceValue = null;
					thisAnimatorsProp.serializedObject.ApplyModifiedProperties();
				}
				EditorGUI.indentLevel--;
			}
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			var dynamicallyAddFromResources = property.FindPropertyRelative ("dynamicallyAddFromResources").boolValue;
			EditorGUI.BeginChangeCheck();
			dynamicallyAddFromResources = EditorGUI.ToggleLeft(valR,"Dynamically Add from Resources", dynamicallyAddFromResources);
			if(EditorGUI.EndChangeCheck()){
				property.FindPropertyRelative ("dynamicallyAddFromResources").boolValue = dynamicallyAddFromResources;
				property.serializedObject.ApplyModifiedProperties ();
			}
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			EditorGUI.PropertyField (valR,property.FindPropertyRelative ("resourcesFolderPath"));
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			var dynamicallyAddFromAssetBundles = property.FindPropertyRelative ("dynamicallyAddFromAssetBundles").boolValue;
			EditorGUI.BeginChangeCheck();
			dynamicallyAddFromAssetBundles = EditorGUI.ToggleLeft(valR,"Dynamically Add from Asset Bundles", dynamicallyAddFromAssetBundles);
			if(EditorGUI.EndChangeCheck()){
				property.FindPropertyRelative ("dynamicallyAddFromAssetBundles").boolValue = dynamicallyAddFromAssetBundles;
				property.serializedObject.ApplyModifiedProperties ();
			}
			valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
			EditorGUI.PropertyField (valR,property.FindPropertyRelative ("assetBundleNames"));
			EditorGUI.indentLevel--;
		}
		EditorGUI.EndProperty ();
	}
	public override float GetPropertyHeight(SerializedProperty property, GUIContent label){
		float h = EditorGUIUtility.singleLineHeight + padding;
		int extraLines = 0;
		SerializedProperty foldoutProp1 = property.FindPropertyRelative ("defaultAnimationController");
		SerializedProperty foldoutProp2 = property.FindPropertyRelative ("animators");
		if (foldoutProp1.isExpanded) {
			extraLines += 6;
			if (foldoutProp2.isExpanded) {
				var thisAnimatorsProp = property.FindPropertyRelative ("animators");
				extraLines += thisAnimatorsProp.arraySize;
                extraLines++;
			}
			h *= (extraLines);
			h += 10 + (extraLines * padding);
		}
		return h;
	}
}
#endif
