using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA
{
    [CustomEditor(typeof(UMAMaterialAnimator))]
    public class UMAMaterialAnimatorInspector : Editor
    {
        string[] excludedProperties = new string[] { "animations" };
        public override void OnInspectorGUI()
        {
            Editor.DrawPropertiesExcluding(serializedObject, excludedProperties);

            UMAMaterialAnimator matAnim = (UMAMaterialAnimator)target;
            if (matAnim.animations == null)
            {
                matAnim.animations = new List<UMAMaterialAnimator.MaterialAnimation>();
            }


            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Float Animation"))
            {
                var anim = new UMAMaterialAnimator.MaterialAnimation();
                anim.type = UMAMaterialAnimator.MaterialAnimationType.Float;
                matAnim.animations.Add(anim);
            }
            if (GUILayout.Button("Add Color Animation"))
            {
                var anim = new UMAMaterialAnimator.MaterialAnimation();
                anim.type = UMAMaterialAnimator.MaterialAnimationType.Color;
                matAnim.animations.Add(anim);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Animations", GUILayout.Width(100));

            // track if deleted so we can delete outside of the foreach
            // and avoid the error about modifying the collection while iterating
            UMAMaterialAnimator.MaterialAnimation deletedAnimation = null;
            foreach (UMAMaterialAnimator.MaterialAnimation anim in matAnim.animations)
            {
                bool deleted = false;
                GUIHelper.FoldoutBar(ref anim.show, anim.ToString(), out deleted);
                if (anim.show)
                {
                    EditorGUI.indentLevel++;
                    anim.overlayTag = EditorGUILayout.TextField("Overlay Tag", anim.overlayTag);
                    anim.propertyName = EditorGUILayout.TextField("Property Name", anim.propertyName);
                    anim.curve = EditorGUILayout.CurveField("Curve", anim.curve);
                    anim.useChannel = EditorGUILayout.Toggle("Use Channel", anim.useChannel);
                    if (anim.useChannel)
                    {
                        anim.channelNumber = EditorGUILayout.IntField("Channel Number", anim.channelNumber);
                    }
                    if (anim.type == UMAMaterialAnimator.MaterialAnimationType.Float)
                    {
                        anim.MinFloatValue = EditorGUILayout.FloatField("Min Value", anim.MinFloatValue);
                        anim.MaxFloatValue = EditorGUILayout.FloatField("Max Value", anim.MaxFloatValue);
                    }
                    if (anim.type == UMAMaterialAnimator.MaterialAnimationType.Color)
                    {
                        anim.MinColorValue = EditorGUILayout.ColorField("MinColor", anim.MinColorValue);
                        anim.MaxColorValue = EditorGUILayout.ColorField("MaxColor", anim.MaxColorValue);
                    }
                    EditorGUI.indentLevel--;
                }
                if (deleted)
                {
                    deletedAnimation = anim;
                }
            }

            if (deletedAnimation != null)
            {
                matAnim.animations.Remove(deletedAnimation);
                Repaint();
            }

            if (GUILayout.Button("Clear All Animations"))
            {
                matAnim.animations.Clear();
            }
            if (GUILayout.Button("Save All Animations"))
            {
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            GUIHelper.EndVerticalPadded(10);

            if (GUI.changed)
            {
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }
        }
    }
}