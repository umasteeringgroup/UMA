#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using UnityEditorInternal;
using UMA;

namespace UMAEditor
{
	public partial class RaceInspector
	{

		private ReorderableList wardrobeSlotList;
		private bool wardrobeSlotListInitialized = false;

        partial void  PreInspectorGUI(ref bool result)
		{
			if(!wardrobeSlotListInitialized){
				InitWardrobeSlotList();
			}
			result = AddExtraStuff();
		}

		private void InitWardrobeSlotList(){
			var thisWardrobeSlotList = serializedObject.FindProperty ("wardrobeSlots");
			if (thisWardrobeSlotList.arraySize == 0) {
				race.ValidateWardrobeSlots (true);
				thisWardrobeSlotList = serializedObject.FindProperty ("wardrobeSlots");
			}
			wardrobeSlotList = new ReorderableList (serializedObject, thisWardrobeSlotList, true, true, true, true);
				wardrobeSlotList.drawHeaderCallback = (Rect rect) =>{
					EditorGUI.LabelField(rect,"Wardrobe Slots");
				};
			wardrobeSlotList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>{
				var element = wardrobeSlotList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				element.stringValue = EditorGUI.TextField(new Rect(rect.x+10, rect.y, rect.width-10, EditorGUIUtility.singleLineHeight),element.stringValue);
			};
			wardrobeSlotListInitialized = true;
		}

		public bool AddExtraStuff(){
			SerializedProperty baseRaceRecipe = serializedObject.FindProperty("baseRaceRecipe");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(baseRaceRecipe, true);
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}
			if (wardrobeSlotList == null) {
				InitWardrobeSlotList ();
			}
			EditorGUI.BeginChangeCheck();
			wardrobeSlotList.DoLayoutList();
			if(EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
				if (!race.ValidateWardrobeSlots ()) {
					EditorUtility.SetDirty(race);
				}
			}
			//backwardsCompatabilityList
			SerializedProperty backwardsCompatibleWith = serializedObject.FindProperty("backwardsCompatibleWith");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(backwardsCompatibleWith, true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("raceThumbnails"), true);
            if (EditorGUI.EndChangeCheck()) {
				serializedObject.ApplyModifiedProperties();
			}
			return false;
		}
	}
}
#endif
