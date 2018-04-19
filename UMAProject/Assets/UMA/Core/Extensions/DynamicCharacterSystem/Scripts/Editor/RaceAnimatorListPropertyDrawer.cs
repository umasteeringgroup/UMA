#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.CharacterSystem.Editors
{
	[CustomPropertyDrawer (typeof(DynamicCharacterAvatar.RaceAnimatorList))]
	public class RaceAnimatorListPropertyDrawer : PropertyDrawer
	{
		float padding = 2f;
		public DynamicCharacterAvatar thisDCA;
		Texture2D warningIcon;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label){
			if (warningIcon == null)
			{
				warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
			}
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
					var warningStyle = new GUIStyle(EditorStyles.label);
					warningStyle.fixedHeight = warningIcon.height + 4f;
					warningStyle.contentOffset = new Vector2(0, -2f);
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
						}
						else
						{
							EditorGUI.BeginDisabledGroup (true);
							EditorGUI.TextField (rFieldR, thisAnimtorProp.FindPropertyRelative ("raceName").stringValue);
							EditorGUI.EndDisabledGroup ();
						}
						EditorGUI.LabelField (aLabelR, "Animator");
						var thisAnimatorName = thisAnimtorProp.FindPropertyRelative("animatorControllerName").stringValue;
						if (thisAnimatorName == "")
						{
							//draw an object field for RunTimeAnimatorController
							EditorGUI.BeginChangeCheck();
							RuntimeAnimatorController thisRC = null;
							thisRC = (RuntimeAnimatorController)EditorGUI.ObjectField (aFieldR, thisRC, typeof(RuntimeAnimatorController), false);
							//if this gets filled set the values
							if(EditorGUI.EndChangeCheck()){
								if (thisRC != null) {
									thisAnimatorsProp.GetArrayElementAtIndex (i).FindPropertyRelative ("animatorControllerName").stringValue = thisRC.name;
								}
							}
						}
						else
						{
							if (DynamicAssetLoader.Instance)
							{
								if (!CheckAnimatorAvailability(thisAnimatorName))
								{
									var warningRect = new Rect((removeR.xMin - 20f), removeR.yMin, 20f, removeR.height);
									aFieldR.xMax = aFieldR.xMax - 20f;
									//if its in an assetbundle we need a different message (i.e "turn on 'Dynamically Add From AssetBundles"') 
									var warningGUIContent = new GUIContent("", thisAnimtorProp.FindPropertyRelative("animatorControllerName").stringValue + " was not Live. If the asset is in an assetBundle check 'Dynamically Add from AssetBundles' below otherwise click this button to add it to the Global Library.");
									warningGUIContent.image = warningIcon;
									if (GUI.Button(warningRect, warningGUIContent, warningStyle))
									{
										var thisAnimator = FindMissingAnimator(thisAnimatorName);
										if (thisAnimator != null)
											UMAAssetIndexer.Instance.EvilAddAsset(thisAnimator.GetType(), thisAnimator);
										else
											UMAAssetIndexerEditor.ShowWindow();
									}
								}
							}
							EditorGUI.BeginDisabledGroup(true);
							EditorGUI.TextField(aFieldR, thisAnimtorProp.FindPropertyRelative("animatorControllerName").stringValue);
							EditorGUI.EndDisabledGroup();
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
				dynamicallyAddFromResources = EditorGUI.ToggleLeft(valR,"Dynamically Add from Global Library", dynamicallyAddFromResources);
				if(EditorGUI.EndChangeCheck()){
					property.FindPropertyRelative ("dynamicallyAddFromResources").boolValue = dynamicallyAddFromResources;
					property.serializedObject.ApplyModifiedProperties ();
				}
				valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.PropertyField (valR,property.FindPropertyRelative ("resourcesFolderPath"), new GUIContent("Global Library Folder Filter"));
				valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
				var dynamicallyAddFromAssetBundles = property.FindPropertyRelative ("dynamicallyAddFromAssetBundles").boolValue;
				EditorGUI.BeginChangeCheck();
				dynamicallyAddFromAssetBundles = EditorGUI.ToggleLeft(valR,"Dynamically Add from Asset Bundles", dynamicallyAddFromAssetBundles);
				if(EditorGUI.EndChangeCheck()){
					property.FindPropertyRelative ("dynamicallyAddFromAssetBundles").boolValue = dynamicallyAddFromAssetBundles;
					property.serializedObject.ApplyModifiedProperties ();
				}
				valR = new Rect (valR.xMin, valR.yMax + padding, valR.width, EditorGUIUtility.singleLineHeight);
				EditorGUI.PropertyField (valR,property.FindPropertyRelative ("assetBundleNames"), new GUIContent("AssetBundles to Search"));
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
		/// <summary>
		/// with RuntimeAnimatorControllers, DynamicCharacterAvatar ony has a direct refrence for the default animator
		/// (so that other animators can be assigned that can exist in asset bundles). The only way to get the others is from DynamicAssetLoader
		/// so they MUST be in an assetBundle or in GlobalIndex or there is no way of finding them, if they are not, show a warning.
		/// </summary>
		/// <param name="racName">RuntimeAnimatorController name.</param>
		/// <returns></returns>
		private bool CheckAnimatorAvailability(string racName)
		{
			if (Application.isPlaying)
				return true;
			bool found = false;
			bool searchResources = true;
			bool searchAssetBundles = true;
			string resourcesFolderPath = "";
			string assetBundlesToSearch = "";
			RuntimeAnimatorController defaultController = null;
			if (thisDCA != null)
			{
				searchResources = thisDCA.raceAnimationControllers.dynamicallyAddFromResources;
				searchAssetBundles = thisDCA.raceAnimationControllers.dynamicallyAddFromAssetBundles;
				resourcesFolderPath = thisDCA.raceAnimationControllers.resourcesFolderPath;
				assetBundlesToSearch = thisDCA.raceAnimationControllers.assetBundleNames;
				defaultController = thisDCA.raceAnimationControllers.defaultAnimationController != null ? thisDCA.raceAnimationControllers.defaultAnimationController : (thisDCA.animationController != null ? thisDCA.animationController : null);
			}
			if (defaultController)
				if (defaultController.name == racName)
					return true;
            if (UMAAssetIndexer.Instance.GetAssetDictionary(typeof(RuntimeAnimatorController)).ContainsKey(racName))
            {
                return true;
            }
			var dalDebug = DynamicAssetLoader.Instance.debugOnFail;
			DynamicAssetLoader.Instance.debugOnFail = false;
			found = DynamicAssetLoader.Instance.AddAssets<RuntimeAnimatorController>(searchResources, searchAssetBundles, true, assetBundlesToSearch, resourcesFolderPath, null, racName, null);
			DynamicAssetLoader.Instance.debugOnFail = dalDebug;
			return found;
		}

		private RuntimeAnimatorController FindMissingAnimator(string animatorName)
		{
			RuntimeAnimatorController foundAnimator = null;
			//the following will find things like femaleHair1 if 'maleHair1' is the recipe name
			var foundWardrobeGUIDS = AssetDatabase.FindAssets("t:RuntimeAnimatorController " + animatorName);
			if (foundWardrobeGUIDS.Length > 0)
			{
				foreach (string guid in foundWardrobeGUIDS)
				{
					var tempAsset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
					if (tempAsset.name == animatorName)
					{
						foundAnimator = tempAsset;
						break;
					}
				}
			}
			return foundAnimator;
		}
	}
	#endif
}
