using System.Collections;
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

        UmaTPose source;

        void OnEnable()
        {
            source = target as UmaTPose;
            source.DeSerialize();
        }

        public override void OnInspectorGUI()
        {
            if (source == null)
                return;

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


            humanInfoFoldout = EditorGUILayout.Foldout(humanInfoFoldout, "Human Info");
            if (source.humanInfo != null)
            {
                if (humanInfoFoldout)
                {

                    EditorGUI.indentLevel++;
                    for (int i = 0; i < source.humanInfo.Length; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(source.humanInfo[i].humanName);
                        EditorGUILayout.LabelField(source.humanInfo[i].boneName);
                        EditorGUILayout.EndHorizontal();
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
