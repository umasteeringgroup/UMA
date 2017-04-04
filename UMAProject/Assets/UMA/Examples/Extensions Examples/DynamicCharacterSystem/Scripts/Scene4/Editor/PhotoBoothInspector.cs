using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using System.IO;
using UMA;
using UMA.CharacterSystem;

namespace UMA.CharacterSystem.Examples
{
	[CustomEditor(typeof(PhotoBooth), true)]
	public class PhotoBoothEditor : Editor
	{
		protected PhotoBooth thisPB;

		public void OnEnable()
		{
			thisPB = target as PhotoBooth;
		}

		public override void OnInspectorGUI()
		{
			//DrawDefaultInspector ();
			Editor.DrawPropertiesExcluding(serializedObject, new string[] {"doingTakePhoto","animationFreezeFrame", "autoPhotosEnabled", "textureToPhoto","dimAllButTarget","dimToColor", "dimToMetallic", "neutralizeTargetColors","neutralizeToColor", "neutralizeToMetallic", "addUnderwearToBasePhoto","overwriteExistingPhotos","destinationFolder","photoName" });
			serializedObject.ApplyModifiedProperties();
			bool freezeAnimation = serializedObject.FindProperty("freezeAnimation").boolValue;
			bool doingTakePhoto = serializedObject.FindProperty("doingTakePhoto").boolValue;
			if (freezeAnimation)
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("animationFreezeFrame"));
			}
			EditorGUILayout.Space();
			serializedObject.FindProperty("dimAllButTarget").isExpanded = EditorGUILayout.Foldout(serializedObject.FindProperty("dimAllButTarget").isExpanded, "Color Change Options");
			if (serializedObject.FindProperty("dimAllButTarget").isExpanded)
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("dimAllButTarget"));
				if (serializedObject.FindProperty("dimAllButTarget").boolValue)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("dimToColor"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("dimToMetallic"));
				}
				EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeTargetColors"));
				if (serializedObject.FindProperty("neutralizeTargetColors").boolValue)
				{
					EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeToColor"));
					EditorGUILayout.PropertyField(serializedObject.FindProperty("neutralizeToMetallic"));
				}
			}
			EditorGUILayout.Space();
			bool autoPhotosEnabled = serializedObject.FindProperty("autoPhotosEnabled").boolValue;
			EditorGUILayout.PropertyField(serializedObject.FindProperty("autoPhotosEnabled"));
			if (autoPhotosEnabled)
			{
				//EditorGUILayout.PropertyField(serializedObject.FindProperty("addUnderwearToBasePhoto"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExistingPhotos"));
			}
			else
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("textureToPhoto"));
			}
			EditorGUILayout.PropertyField(serializedObject.FindProperty("photoName"));
			
			if (Application.isPlaying)
			{
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.PropertyField(serializedObject.FindProperty("destinationFolder"));
				EditorGUI.EndDisabledGroup();
				if (GUILayout.Button("Choose DestinationFolder"))
				{
					var path = EditorUtility.OpenFolderPanel("Destination Folder for Photos", Application.dataPath, "");
					if (path != "")
					{
						(target as PhotoBooth).destinationFolder = path;
						serializedObject.FindProperty("destinationFolder").stringValue = path;
						serializedObject.ApplyModifiedProperties();
					}
				}
				EditorGUILayout.Space();
				if (doingTakePhoto)
				{
					EditorGUI.BeginDisabledGroup(true);
				}
				if (GUILayout.Button("Take Photo(s)"))
				{
					if ((target as PhotoBooth).destinationFolder == "")
					{
						EditorUtility.DisplayDialog("No Destination folder chosen","Please choose your destination folder","Ok");
					}
					else
					{
						thisPB.TakePhotos();
					}
				}
				if (doingTakePhoto)
				{
					EditorGUI.EndDisabledGroup();
				}
			}
			serializedObject.ApplyModifiedProperties();
		}
	}
}
