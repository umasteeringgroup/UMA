using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA.CharacterSystem.Examples
{
    [CustomEditor(typeof(PhotoBooth), true)]
	public class PhotoBoothEditor : Editor
	{
		protected PhotoBooth thisPB;
        private bool foldOut = false;

		public void OnEnable()
		{
			thisPB = target as PhotoBooth;
		}

		public override void OnInspectorGUI()
		{
            //DrawDefaultInspector ();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("To take photos you must be in play mode. Select the destination folder, choose 'auto photo' mode, and press 'Take Photo'", MessageType.Info);
            }

            foldOut = GUIHelper.FoldoutBar(foldOut, "Texture and Camera Setup");

            if (foldOut)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

                Editor.DrawPropertiesExcluding(serializedObject, new string[] { "doingTakePhoto", "doubleSideReplacements", "avatarToPhoto","freezeAnimation", "animationFreezeFrame", "autoPhotosEnabled", "textureToPhoto", "dimAllButTarget", "dimToColor", "dimToMetallic", "neutralizeTargetColors", "neutralizeToColor", "neutralizeToMetallic", "addUnderwearToBasePhoto", "overwriteExistingPhotos", "destinationFolder", "photoName", "hideRaceBody", "gammaCorrection", "linearCorrection" });
                serializedObject.ApplyModifiedProperties();
                GUIHelper.EndVerticalPadded(10);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("avatarToPhoto"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("freezeAnimation"));
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("doubleSidedReplacements"));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("hideRaceBody"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("gammaCorrection"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("linearCorrection"));
			bool autoPhotosEnabled = serializedObject.FindProperty("autoPhotosEnabled").boolValue;
			EditorGUILayout.PropertyField(serializedObject.FindProperty("autoPhotosEnabled"));
			if (autoPhotosEnabled)
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("addUnderwearToBasePhoto"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("overwriteExistingPhotos"));
				if (Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Auto photos is enabled. A photo for each wardrobe item will be generated. Select the destination folder, and press 'Take Photo'", MessageType.Info);
                }
            }
			else
			{
				EditorGUILayout.PropertyField(serializedObject.FindProperty("textureToPhoto"));
				if (Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Auto photos is disabled. Select the destination folder, add the wardrobe item, and select the texture you want to take. The press 'Take Photo'.", MessageType.Info);
                }
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
				if (GUILayout.Button("Disable Culling"))
                {
					thisPB.ForceNoCulling();
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
