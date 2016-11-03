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
        public bool minimalMode = false;
        UMABonePose thisUBP = null;
        public UMAData umaData;
        private string bonePoseFilter = "";
        private string newBonePoseName = "";
        private int newBonePoseNameIndex = 0;

        public void Init()
        {
            if(thisUBP == null)
            {
                thisUBP = target as UMABonePose;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            if (minimalMode == false)
                DrawDefaultInspector();
            else
            {
                if(thisUBP == null)
                {
                    Init();
                }
                //just for use with DynamicDNAConverterBehaviourInspector. Displays the list of poses, allows them to be deleted and new ones to be added based on a set skeleton.
                SerializedProperty poses = serializedObject.FindProperty("poses");
                poses.isExpanded = EditorGUILayout.Foldout(poses.isExpanded, "Bone Poses ("+poses.arraySize+")");
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
                    if(umaData != null)//we can show an interface for adding bones based on the current skeleton
                    {
                        EditorGUILayout.BeginHorizontal();
                        var buttonDisabled = newBonePoseName == "";
                        bool canAdd = true;
                        bool notFoundInSkeleton = false;
                        bool didAdd = false;
                        EditorGUI.BeginChangeCheck();
                        var boneNames = umaData.skeleton.BoneNames;
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
                            if (umaData.skeleton.HasBone(UMAUtils.StringToHash(newBonePoseName)) == false)
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
                                umaData.skeleton.ResetAll();//use reset to the get the bone in its unmodified state
                                var thisRawBone = umaData.skeleton.GetBoneGameObject(newHash);
                                thisUBP.AddBone(thisRawBone.transform, thisRawBone.transform.localPosition, thisRawBone.transform.localRotation, thisRawBone.transform.localScale);
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

        void PoseBoneDrawer(SerializedProperty property)
        {
            int startingIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel++;
            //EditorGUILayout.PropertyField(property.FindPropertyRelative("position"));//these are far to sensitive just as they are
            SerializedProperty position = property.FindPropertyRelative("position");
            Vector3 currentPosX100 = (Vector3)position.vector3Value;
            currentPosX100 = currentPosX100 * 100;
            Vector3 newPosX100 = currentPosX100;
            EditorGUI.BeginChangeCheck();
            newPosX100 = EditorGUILayout.Vector3Field("Position", newPosX100);
            if (EditorGUI.EndChangeCheck())
            {
                if(newPosX100 != currentPosX100)
                {
                    position.vector3Value = newPosX100 / 100;
                }
            }
			//these maybe easier to use if they were euler
			SerializedProperty rotation = property.FindPropertyRelative("rotation");
			Vector3 currentRotationEuler = ((Quaternion)rotation.quaternionValue).eulerAngles;
			Vector3 newRotationEuler = currentRotationEuler;
			EditorGUI.BeginChangeCheck();
			newRotationEuler = EditorGUILayout.Vector3Field("Rotation", newRotationEuler);
			if (EditorGUI.EndChangeCheck())
			{
				if(newRotationEuler != currentRotationEuler)
				{
					rotation.quaternionValue = Quaternion.Euler(newRotationEuler);
				}

			}
			EditorGUILayout.PropertyField(property.FindPropertyRelative("scale"));
			//but sometimes rotation doesn't do what you would expect in euler so maybe still have quaternion anyway
			EditorGUILayout.PropertyField(property.FindPropertyRelative("rotation"),new GUIContent("qRotation"), true);
			EditorGUI.indentLevel = startingIndent;
        }
    }
}
