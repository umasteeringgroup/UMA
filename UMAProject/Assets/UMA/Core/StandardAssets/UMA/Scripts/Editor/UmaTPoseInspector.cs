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
