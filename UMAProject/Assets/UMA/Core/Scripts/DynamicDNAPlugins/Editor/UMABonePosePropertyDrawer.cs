using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.PoseTools
{
	[CustomPropertyDrawer(typeof(UMABonePose),true)]
	public class UMABonePosePropertyDrawer : PropertyDrawer
	{
		float inspectBtnWidth = 25f;
		Texture inspectIcon;
		GUIContent inspectContent;
		GUIStyle inspectStyle;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);
			if(inspectIcon == null)
			{
				inspectIcon = EditorGUIUtility.FindTexture("ViewToolOrbit");
				inspectContent = new GUIContent("", "Inspect");
				inspectContent.image = inspectIcon;
				inspectStyle = new GUIStyle(EditorStyles.miniButton);

				inspectStyle.contentOffset = new Vector2(0f, 0f);
				inspectStyle.padding = new RectOffset(0, 0, 0, 0);
				inspectStyle.margin = new RectOffset(0, 0, 0, 0);
			}
			var objectFieldRect = position;
			var inspectBtnRect = Rect.zero;
			if (property.objectReferenceValue)
			{
				objectFieldRect = new Rect(position.xMin, position.yMin, position.width - inspectBtnWidth - 4f, position.height);
				inspectBtnRect = new Rect(objectFieldRect.xMax + 4f, objectFieldRect.yMin, inspectBtnWidth, objectFieldRect.height);
			}
			EditorGUI.ObjectField(objectFieldRect, property, label);
			if (property.objectReferenceValue)
			{
				if (GUI.Button(inspectBtnRect, inspectIcon, inspectStyle))
				{
					//Couldnt use this because I need other inspectors to be able to set up the UMABonePoseContext at runtime
					//so the pose can be editied with gizmos on the selected character
					//InspectorUtlity.InspectTarget(property.objectReferenceValue);
					var inspectorWindow = UMABonePoseInspectorWindow.Init(property.objectReferenceValue as UMABonePose);
					inspectorWindow.Show();
				}
			}
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return (EditorGUIUtility.singleLineHeight);
		}
	}

	public class UMABonePoseInspectorWindow : EditorWindow
	{
		public static UMABonePose inspectedPose;
		public static UMABonePoseEditorContext context = new UMABonePoseEditorContext();
		public static UMABonePoseEditor UBPEditor;
		public static bool dynamicDNAConverterMode;

		private static Vector2 _scrollView = Vector2.zero;
		private static UMABonePoseInspectorWindow _bonePoseInspectorWindow;

		private void OnGUI()
		{
			if (UBPEditor == null || UBPEditor.target != inspectedPose)
				UBPEditor = Editor.CreateEditor(inspectedPose, typeof(UMABonePoseEditor)) as UMABonePoseEditor;
			if (!Application.isPlaying)
			{
				context = null;
				dynamicDNAConverterMode = false;
			}
			UBPEditor.context = context;
			UBPEditor.dynamicDNAConverterMode = dynamicDNAConverterMode;

			_scrollView = EditorGUILayout.BeginScrollView(_scrollView);
			UBPEditor.OnInspectorGUI();
			EditorGUILayout.EndScrollView();
		}

		public static UMABonePoseInspectorWindow Init(UMABonePose inspectedPose)
		{
			_bonePoseInspectorWindow = GetWindow<UMABonePoseInspectorWindow>();
			UMABonePoseInspectorWindow.inspectedPose = inspectedPose;
			return _bonePoseInspectorWindow;
		}
	}
}
