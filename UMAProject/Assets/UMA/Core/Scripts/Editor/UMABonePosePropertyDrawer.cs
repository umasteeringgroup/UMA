using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA.PoseTools
{
	[CustomPropertyDrawer(typeof(UMABonePose),true)]
	public class UMABonePosePropertyDrawer : PropertyDrawer
	{
		static EditorWindow inspectorPopup;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			GUIHelper.InspectableObjectField(position, property, label, OnShowPopupInspector);

			//We need to check this a few times because when the inspector window is created by InspectorUtility.InspectTarget
			//when the user clicks the inspect button next to the field that is drawn above,
			//GetInspectorsEditors doesnt return the actual editors correctly until the popup window repaints
			if (inspectorPopup != null && UMABonePoseEditor.livePopupEditor == null)
			{
				var editors = InspectorUtlity.GetInspectorsEditors(inspectorPopup);
				for (int i = 0; i < editors.Length; i++)
				{
					if (editors[i].GetType() == typeof(UMABonePoseEditor))
					{
						if(editors[i].target == property.objectReferenceValue)
							UMABonePoseEditor.SetLivePopupEditor(editors[i] as UMABonePoseEditor);
					}
				}
			}
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return (EditorGUIUtility.singleLineHeight);
		}

		public void OnShowPopupInspector(EditorWindow newInspectorPopup)
		{
			if (inspectorPopup != null && inspectorPopup != newInspectorPopup)
			{
				inspectorPopup.Close();
				UMABonePoseEditor.SetLivePopupEditor(null);
			}
			inspectorPopup = newInspectorPopup;
		}
	}
}
