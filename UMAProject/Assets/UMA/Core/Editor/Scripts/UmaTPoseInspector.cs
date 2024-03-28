using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA
{
    [CustomEditor(typeof(UmaTPose))]
    public class UmaTPoseInspector : Editor
    {
        bool boneInfoFoldout = false;
        bool humanInfoFoldout = false;
        bool mecanimInfoFoldout = false;
        List<bool> foldouts = new List<bool>();

        UmaTPose source;

        void OnEnable()
        {
            source = target as UmaTPose;
            source.DeSerialize();
        }

        public override void OnInspectorGUI()
        {
            if (source == null)
            {
                return;
            }

            //base.DrawDefaultInspector();
            mecanimInfoFoldout = EditorGUILayout.Foldout(mecanimInfoFoldout, "Mecanim Adjustments");
            if (mecanimInfoFoldout)
            {
                source.armStretch = EditorGUILayout.FloatField("Arm Stretch", source.armStretch);
                source.legStretch = EditorGUILayout.FloatField("Leg Stretch", source.legStretch);
                source.feetSpacing = EditorGUILayout.FloatField("Feet Spacing", source.feetSpacing);
                source.lowerArmTwist = EditorGUILayout.FloatField("Lower Arm Twist", source.lowerArmTwist);
                source.upperArmTwist = EditorGUILayout.FloatField("Upper Arm Twist", source.upperArmTwist);
                source.lowerLegTwist = EditorGUILayout.FloatField("Lower Leg Twist", source.lowerLegTwist);
                source.upperLegTwist = EditorGUILayout.FloatField("Upper Leg Twist", source.upperLegTwist);
            }

            boneInfoFoldout = EditorGUILayout.Foldout(boneInfoFoldout, "Bone Info");
            if (source.boneInfo != null)
            {
                if (boneInfoFoldout)
                {

                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.boneInfo.Length; i++)
                    {
                        EditorGUILayout.LabelField(source.boneInfo[i].name);
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Bone Info is empty!", MessageType.Error);
            }

            if (foldouts.Count != source.humanInfo.Length)
            {
                foldouts.Clear();
                for (int i = 0; i < source.humanInfo.Length; i++)
                {
                    foldouts.Add(false);
                }
            }

            humanInfoFoldout = EditorGUILayout.Foldout(humanInfoFoldout, "Human Info");
            if (source.humanInfo != null)
            {
                if (humanInfoFoldout)
                {

                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.humanInfo.Length; i++)
                    {
                        //EditorGUILayout.BeginHorizontal();
                        foldouts[i]  = EditorGUILayout.Foldout(foldouts[i], $"{source.humanInfo[i].humanName} -> {source.humanInfo[i].boneName}");
                        // EditorGUILayout.LabelField(source.humanInfo[i].humanName);
                        // EditorGUILayout.LabelField(source.humanInfo[i].boneName);
                        if (foldouts[i])
                        {
                            UMA.Editors.GUIHelper.BeginVerticalPadded();
                            EditorGUI.BeginChangeCheck();
                            EditorGUILayout.LabelField("humanName", source.humanInfo[i].humanName);
                            source.humanInfo[i].boneName = EditorGUILayout.DelayedTextField("boneName", source.humanInfo[i].boneName);
                            EditorGUILayout.LabelField("limits");
                            EditorGUI.indentLevel ++;
                            source.humanInfo[i].limit.useDefaultValues = EditorGUILayout.Toggle("useDefault", source.humanInfo[i].limit.useDefaultValues);
                            if (!source.humanInfo[i].limit.useDefaultValues)
                            {
                                source.humanInfo[i].limit.axisLength = EditorGUILayout.FloatField("axisLength", source.humanInfo[i].limit.axisLength);
                                source.humanInfo[i].limit.min = EditorGUILayout.Vector3Field("min", source.humanInfo[i].limit.min, GUILayout.ExpandWidth(false));
                                source.humanInfo[i].limit.max = EditorGUILayout.Vector3Field("max", source.humanInfo[i].limit.max, GUILayout.ExpandWidth(false));
                                source.humanInfo[i].limit.center = EditorGUILayout.Vector3Field("center", source.humanInfo[i].limit.center, GUILayout.ExpandWidth(false));
                            }
                            EditorGUI.indentLevel--;
                            if (EditorGUI.EndChangeCheck())
                            {
                                serializedObject.ApplyModifiedProperties();
                                source.Serialize();
                                EditorUtility.SetDirty(source);
                            }
                            UMA.Editors.GUIHelper.EndVerticalPadded();
                        }
                       // EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Human Info is empty!", MessageType.Error);
            }


        }
    }
}
