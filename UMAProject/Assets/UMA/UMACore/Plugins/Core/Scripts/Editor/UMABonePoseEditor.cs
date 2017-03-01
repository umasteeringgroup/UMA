using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    [CustomEditor(typeof(UMABonePose),true)]
    public class UMABonePoseEditor : Editor
    {
		UMABonePose targetPose = null;
		public UMABonePoseEditorContext context = null;
		public bool haveValidContext
		{
			get { return ((context != null) && (context.activeUMA != null)); }
		}
        public bool minimalMode = false;

		private int drawBoneIndex = -1;
		private int editBoneIndex = -1;
		private int contextBoneIndex = -1;
		private int mirrorBoneIndex = -1;
		private bool mirrorActive = true;

		// HACK for testing
		public UMAData sourceUMA;

//		private bool inspectorLocked = false;

		private int deleteIndex = -1;
		private string[] deleteOptions;

		private static Texture warningIcon = EditorGUIUtility.FindTexture("console.warnicon.sml");
//		private static Texture trashIcon = EditorGUIUtility.FindTexture("TreeEditor.Trash");
//		private GUIStyle guiFixedWidthButton = null;

        private string bonePoseFilter = "";
        private string newBonePoseName = "";
        private int newBonePoseNameIndex = 0;

		private static GUIContent positionGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Position"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local position when pose is applied."));
		private static GUIContent rotationGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Rotation"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local rotation when pose is applied."));
		private static GUIContent scaleGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Scale"),
			LocalizationDatabase.GetLocalizedString("The change in this bone's local scale when pose is applied."));
		private static GUIContent scaleWarningGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("WARNING: Non-uniform scale."),
			LocalizationDatabase.GetLocalizedString("Non-uniform scaling can cause errors on bones that are animated. Use only with adjustment bones."));
		private static GUIContent removeBoneGUIContent = new GUIContent(
			LocalizationDatabase.GetLocalizedString("Remove Bone"),
			LocalizationDatabase.GetLocalizedString("Remove the selected bone from the pose."));
		
		public void OnEnable()
		{
			targetPose = target as UMABonePose;
//			inspectorLocked = ActiveEditorTracker.sharedTracker.isLocked;
//			ActiveEditorTracker.sharedTracker.isLocked = true;
			SceneView.onSceneGUIDelegate += this.OnSceneGUI;
		}

		public void OnDisable()
		{
			SceneView.onSceneGUIDelegate -= this.OnSceneGUI;
//			ActiveEditorTracker.sharedTracker.isLocked = inspectorLocked;
		}

		void OnSceneGUI(SceneView scene)
		{
			if (haveValidContext)
			{
				if (contextBoneIndex != editBoneIndex)
				{
					contextBoneIndex = -1;
					mirrorBoneIndex = -1;
					if ((editBoneIndex >= 0) && (editBoneIndex < targetPose.poses.Length))
					{
						int boneHash = targetPose.poses[editBoneIndex].hash;
						context.activeTransform = context.activeUMA.skeleton.GetBoneTransform(boneHash);
						if (context.activeTransform != null)
						{
							contextBoneIndex = editBoneIndex;
						}

						if (context.mirrorTransform != null)
						{
							int mirrorHash = UMASkeleton.StringToHash(context.mirrorTransform.name);
							for (int i = 0; i < targetPose.poses.Length; i++)
							{
								if (targetPose.poses[i].hash == mirrorHash)
								{
									mirrorBoneIndex = i;
									break;
								}
							}
						}
					}
					else
					{
						context.activeTransform = null;
					}

				}

//				EditorGUI.BeginChangeCheck( );
				Transform activeTrans = context.activeTransform;
				Transform mirrorTrans = context.mirrorTransform;
				if (activeTrans != null)
				{
					if (context.activeTransChanged)
					{
						scene.pivot = activeTrans.position;
						context.activeTransChanged = false;
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Position)
					{
						Vector3 newPos = Handles.PositionHandle(activeTrans.position, activeTrans.rotation);
						if (newPos != activeTrans.position)
						{
							Vector3 newLocalPos = activeTrans.parent.InverseTransformPoint(newPos);
							Vector3 deltaPos = newLocalPos - activeTrans.localPosition;
	//						Debug.Log("Moved active bone by: " + localDelta);
							activeTrans.localPosition += deltaPos;

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaPos.x = -deltaPos.x;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaPos.y = -deltaPos.y;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaPos.z = -deltaPos.z;
										break;
								}

								mirrorTrans.localPosition += deltaPos;
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Rotation)
					{
						Quaternion newRotation = Handles.RotationHandle(activeTrans.rotation, activeTrans.position);
						if (newRotation != activeTrans.rotation)
						{
							Quaternion deltaRot = Quaternion.Inverse(activeTrans.rotation) * newRotation;
							activeTrans.localRotation *= deltaRot;

							if (mirrorTrans != null)
							{
								switch(context.mirrorPlane)
								{
									case UMABonePoseEditorContext.MirrorPlane.Mirror_X:
										deltaRot.y = -deltaRot.y;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Y:
										deltaRot.x = -deltaRot.x;
										deltaRot.z = -deltaRot.z;
										break;
									case UMABonePoseEditorContext.MirrorPlane.Mirror_Z:
										deltaRot.x = -deltaRot.x;
										deltaRot.y = -deltaRot.y;
										break;
								}

								mirrorTrans.localRotation *= deltaRot;
							}
						}
					}

					if (context.activeTool == UMABonePoseEditorContext.EditorTool.Tool_Scale)
					{
						Vector3 newScale = Handles.ScaleHandle(activeTrans.localScale, activeTrans.position, activeTrans.rotation, HandleUtility.GetHandleSize(activeTrans.position));
						if (newScale != activeTrans.localScale)
						{
							activeTrans.localScale = newScale;

							if (mirrorTrans != null)
							{
								mirrorTrans.localScale = activeTrans.localScale;
							}
						}
					}
				}

//				if( EditorGUI.EndChangeCheck( ) )
//				{
//					Undo.RecordObject( target, "Changed Look Target" );
//					t.lookTarget = lookTarget;
//					t.Update( );
//				}
			}
			// HACK
			else if (sourceUMA != null)
			{
				if (context == null) {
					context = new UMABonePoseEditorContext();
				}
				context.activeUMA = sourceUMA;
			}

		}

        public override void OnInspectorGUI()
        {
//			if (guiFixedWidthButton == null)
//			{
//				guiFixedWidthButton = new GUIStyle(GUI.skin.button);
//			}

            serializedObject.Update();
			SerializedProperty poses = serializedObject.FindProperty("poses");

            if (minimalMode == false)
			{
				sourceUMA = EditorGUILayout.ObjectField("Source UMA", sourceUMA, typeof(UMAData), true) as UMAData;

//				string controlName = GUI.GetNameOfFocusedControl();
//				if ((controlName != null) && (controlName.Length > 0))
//					Debug.Log(controlName);

				if (deleteOptions == null)
				{
					List<string> deleteList = new List<string>(targetPose.poses.Length + 1);
					deleteList.Add(" ");
					for (int i = 0; i < targetPose.poses.Length; i++)
					{
						deleteList.Add(targetPose.poses[i].bone);
					}

					deleteOptions = deleteList.ToArray();
				}

				poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Pose Bones ("+poses.arraySize+")");
				if (poses.isExpanded)
				{
					for (int i = 0; i < poses.arraySize; i++)
					{
						var pose = poses.GetArrayElementAtIndex(i);
						drawBoneIndex = i;
						PoseBoneDrawer(pose);
					}
				}

				GUILayout.Space(EditorGUIUtility.singleLineHeight);
				const float addRemovePadding = 20f;

				EditorGUILayout.BeginHorizontal();
				GUILayout.Space(addRemovePadding);
				EditorGUI.BeginDisabledGroup(deleteIndex == 0);
				if (GUILayout.Button(removeBoneGUIContent, GUILayout.Width(90f)))
				{
				}
				EditorGUI.EndDisabledGroup();
				EditorGUILayout.BeginVertical();
				GUILayout.Space(4f); // Can't be calculated because the popup doesn't fill its rect.
				deleteIndex = EditorGUILayout.Popup(deleteIndex, deleteOptions);
				EditorGUILayout.EndVertical();
				GUILayout.Space(addRemovePadding);
				EditorGUILayout.EndHorizontal();

//				EditorGUILayout.BeginHorizontal();
//				if (haveValidContext)
//				{
//					EditorGUILayout.Popup(selectedIndex, displayedOptions);
//					EditorGUI.BeginDisabledGroup(selectedIndex == 0);
//					if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Add Bone"), EditorStyles.miniButton))
//					{
//					}
//					EditorGUI.EndDisabledGroup();
//				}
//				else
//				{
//				}
//				EditorGUILayout.EndHorizontal();

			}
            else
            {
               //just for use with DynamicDNAConverterBehaviourInspector. Displays the list of poses, allows them to be deleted and new ones to be added based on a set skeleton.
                poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Pose Bones ("+poses.arraySize+")");
                if (poses.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    bonePoseFilter = EditorGUILayout.TextField("Bone Pose Filter", bonePoseFilter);
                    for (int i = 0; i < poses.arraySize; i++)
                    {
                        var thisBonePoseEl = poses.GetArrayElementAtIndex(i);
                        //Search Bone Hashes Method
                        if (bonePoseFilter.Length >= 3)
                        {
                            if (thisBonePoseEl.displayName.IndexOf(bonePoseFilter, StringComparison.CurrentCultureIgnoreCase) == -1)
                                continue;
                        }
                        EditorGUILayout.BeginHorizontal();
                        thisBonePoseEl.isExpanded = EditorGUILayout.Foldout(thisBonePoseEl.isExpanded, thisBonePoseEl.displayName);
                        //DeleteButton
                        Rect bpDelButR = EditorGUILayout.GetControlRect(false);
                        bpDelButR.x = bpDelButR.x + bpDelButR.width - 100f;
                        bpDelButR.width = 100f;
                        if (GUI.Button(bpDelButR, "Delete"))
                        {
                            poses.DeleteArrayElementAtIndex(i);
                            continue;
                        }
                        EditorGUILayout.EndHorizontal();
                        if (thisBonePoseEl.isExpanded)
                        {
                            PoseBoneDrawer(thisBonePoseEl);
                        }
                    }
					if(haveValidContext)//we can show an interface for adding bones based on the current skeleton
                    {
                        EditorGUILayout.BeginHorizontal();
                        var buttonDisabled = newBonePoseName == "";
                        bool canAdd = true;
                        bool notFoundInSkeleton = false;
                        bool didAdd = false;
                        EditorGUI.BeginChangeCheck();
						var boneNames = context.activeUMA.skeleton.BoneNames;
                        Array.Sort(boneNames);
                        List<string> thisBoneNames = new List<string>(boneNames);
                        thisBoneNames.Insert(0, "ChooseBone");
                        for (int i = 0; i < poses.arraySize; i++)
                        {
                            if (thisBoneNames.Contains(poses.GetArrayElementAtIndex(i).displayName))
                            {
                                thisBoneNames.Remove(poses.GetArrayElementAtIndex(i).displayName);
                            }
                        }
                        newBonePoseNameIndex = EditorGUILayout.Popup(newBonePoseNameIndex, thisBoneNames.ToArray());
                        if (EditorGUI.EndChangeCheck())
                        {
                            if(newBonePoseNameIndex != 0)
                            {
                                newBonePoseName = thisBoneNames[newBonePoseNameIndex];
                            }
                            else
                            {
                                newBonePoseName = "";
                            }
                            if (newBonePoseName != "" && canAdd)
                            {
                                buttonDisabled = false;
                            }
                        }
                        if (newBonePoseName != "")
                        {
                            /* Assuming I can have the BoneNames list we dont need this check
                            if (context.activeUMA.skeleton.HasBone(UMAUtils.StringToHash(newBonePoseName)) == false)
                            {
                                canAdd = false;
                                buttonDisabled = true;
                                notFoundInSkeleton = true;
                            }*/
                            //we also need to check if there is a pose for this name already
                            //we can actually fiter these from the list
                            /*for (int i = 0; i < poses.arraySize; i++)
                            {
                                if(poses.GetArrayElementAtIndex(i).displayName == newBonePoseName)
                                {
                                    canAdd = false;
                                    buttonDisabled = true;
                                }
                            }*/

                        }
                        if (buttonDisabled)
                        {
                            EditorGUI.BeginDisabledGroup(true);
                        }

                        if (GUILayout.Button("Add Bone Pose"))
                        {
                            if (canAdd)
                            {
                                var newHash = UMAUtils.StringToHash(newBonePoseName);
								context.activeUMA.skeleton.ResetAll();//use reset to the get the bone in its unmodified state
								var thisRawBone = context.activeUMA.skeleton.GetBoneGameObject(newHash);
                                targetPose.AddBone(thisRawBone.transform, thisRawBone.transform.localPosition, thisRawBone.transform.localRotation, thisRawBone.transform.localScale);
                                serializedObject.ApplyModifiedProperties();
                                didAdd = true;
                            }
                        }
                        if (buttonDisabled)
                        {
                            EditorGUI.EndDisabledGroup();
                        }
                        EditorGUILayout.EndHorizontal();
                        if (canAdd == false)
                        {
                            if (notFoundInSkeleton == true)
                            {
                                EditorGUILayout.HelpBox("That name was not found in the skeleton. (Standard Bone names start with a capital letter in CamelCase)", MessageType.Warning);
                            }
                            else
                            {
                                EditorGUILayout.HelpBox("There was already a BonePose for that bone. You can the filter at the top of the list to find it.", MessageType.Warning);
                            }
                        }
                        if (didAdd)
                        {
                            newBonePoseName = "";
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        private void PoseBoneDrawer(SerializedProperty property)
        {
			EditorGUI.indentLevel++;

			SerializedProperty bone = property.FindPropertyRelative("bone");
			GUIContent boneGUIContent = new GUIContent(
				bone.stringValue,
				LocalizationDatabase.GetLocalizedString("The name of the bone being modified by pose."));
			EditorGUILayout.BeginHorizontal();
			bone.isExpanded = EditorGUILayout.Foldout(bone.isExpanded, boneGUIContent);
//			Rect buttonRect = EditorGUILayout.GetControlRect(false);
//			float buttonWidth = 60f;
//			buttonRect.x += (buttonRect.width - buttonWidth);
//			buttonRect.width = buttonWidth;
			Color currentColor = GUI.color;
			if (drawBoneIndex == editBoneIndex)
			{
				GUI.color = Color.green;
				if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Editing"), EditorStyles.miniButton, GUILayout.Width(60f)))
//				if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Editing"), EditorStyles.miniButton))
				{
					editBoneIndex = -1;
				}
			}
			else if (drawBoneIndex == mirrorBoneIndex)
			{
				Color lightBlue = Color.Lerp(Color.blue, Color.cyan, 0.66f);
				if (mirrorActive)
				{
					GUI.color = lightBlue;
					if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Mirroring"), EditorStyles.miniButton, GUILayout.Width(60f)))
//					if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Mirroring"), EditorStyles.miniButton))
					{
						mirrorActive = false;
					}
				}
				else
				{
					GUI.color = Color.Lerp(lightBlue, Color.white, 0.66f);
					if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Mirror"), EditorStyles.miniButton, GUILayout.Width(60f)))
//					if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Mirror"), EditorStyles.miniButton))
					{
						mirrorActive = true;
					}
				}
			}
			else
			{
				if (GUILayout.Button(LocalizationDatabase.GetLocalizedString("Edit"), EditorStyles.miniButton, GUILayout.Width(60f)))
//				if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Edit"), EditorStyles.miniButton))
				{
					editBoneIndex = drawBoneIndex;
				}
			}
			GUI.color = currentColor;
//			buttonRect.x += 64f;
//			buttonRect.width = 20f;
//			if (GUI.Button(buttonRect, "×", EditorStyles.miniButton))
//			{
//			}
			EditorGUILayout.EndHorizontal();

			if (bone.isExpanded)
			{
				EditorGUI.BeginDisabledGroup(drawBoneIndex != editBoneIndex);
				EditorGUI.indentLevel++;
//				GUI.SetNextControlName(bone.stringValue + "_position");
				int controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				EditorGUILayout.PropertyField(property.FindPropertyRelative("position"), positionGUIContent);
				int controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Position;
				}

				// Show Euler angles for rotation
				SerializedProperty rotation = property.FindPropertyRelative("rotation");
				// Use BeginProperty() with fake rect to enable Undo but keep layout correct
				Rect rotationRect = new Rect(0, 0, 0, 0);
				EditorGUI.BeginProperty(rotationRect, GUIContent.none, rotation);

				Vector3 currentRotationEuler = ((Quaternion)rotation.quaternionValue).eulerAngles;
				Vector3 newRotationEuler = currentRotationEuler;
				EditorGUI.BeginChangeCheck();
//				GUI.SetNextControlName(bone.stringValue + "_rotation");
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				newRotationEuler = EditorGUILayout.Vector3Field(rotationGUIContent, newRotationEuler);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Rotation;
				}
				if (EditorGUI.EndChangeCheck())
				{
					if(newRotationEuler != currentRotationEuler)
					{
						rotation.quaternionValue = Quaternion.Euler(newRotationEuler);
					}

				}
				EditorGUI.EndProperty();

//				GUI.SetNextControlName(property.FindPropertyRelative("scale").displayName);
				SerializedProperty scaleProperty = property.FindPropertyRelative("scale");
				controlIDLow = GUIUtility.GetControlID(0, FocusType.Passive);
				EditorGUILayout.PropertyField(scaleProperty, scaleGUIContent);
				controlIDHigh = GUIUtility.GetControlID(0, FocusType.Passive);
				if ((GUIUtility.keyboardControl > controlIDLow) && (GUIUtility.keyboardControl < controlIDHigh))
				{
					if (context != null) context.activeTool = UMABonePoseEditorContext.EditorTool.Tool_Scale;
				}

				// Warn if there's a non-uniform scale
				Vector3 scaleValue = scaleProperty.vector3Value;
				if (!Mathf.Approximately(scaleValue.x, scaleValue.y) || !Mathf.Approximately(scaleValue.y, scaleValue.z))
				{
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(EditorGUIUtility.labelWidth / 2f);
					if (warningIcon != null)
					{
						scaleWarningGUIContent.image = warningIcon;
						EditorGUILayout.LabelField(scaleWarningGUIContent, GUILayout.MinHeight(warningIcon.height + 4f));
					}
					else
					{
						EditorGUILayout.LabelField(scaleWarningGUIContent);
					}
					EditorGUILayout.EndHorizontal();
				}
					
				EditorGUI.indentLevel--;
				EditorGUI.EndDisabledGroup();

			}

			EditorGUI.indentLevel--;

//			if (drawBoneIndex == editBoneIndex)
//			{
//				buttonRect = EditorGUILayout.GetControlRect(false);
//				buttonRect.width = 100f;
//				buttonRect.center = new Vector2(EditorGUIUtility.currentViewWidth / 2f, buttonRect.center.y);
//				if (GUI.Button(buttonRect, LocalizationDatabase.GetLocalizedString("Remove Bone")))
//				{
//				}
//
//			}
        }
    }
}
