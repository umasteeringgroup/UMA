using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAMaterial))]
    public class UMAMaterialInspector : Editor 
    {
        private Shader _lastSelectedShader;
        private string[] _shaderProperties;

        public override void OnInspectorGUI()
        {
            UMAMaterial source = target as UMAMaterial;
            serializedObject.Update();

            //base.OnInspectorGUI();

            //Feature, lets list the available Tex2D properties in the selected shader
            if (source.material != null && source.material.shader != null)
            {
                if (_lastSelectedShader == null)
                {
                    _shaderProperties = FindTexProperties(source.material.shader);
                    _lastSelectedShader = source.material.shader;
                }
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("material"), new GUIContent( "Material", "The Unity Material to link to."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("materialType"), new GUIContent( "Material Type", "Atlas this material or do not atlas it."));

            GUILayout.Space(20);

            DrawChannelList(serializedObject.FindProperty("channels"));

            GUILayout.Space(20);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("clothProperties"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("RequireSeperateRenderer"));

            //Another feature, detect whether existing Material Property Names exist in the selected shader or not

            serializedObject.ApplyModifiedProperties();
        }

        //Maybe eventually we can use the new IMGUI classes once older unity version are no longer supported.
        private void DrawChannelList(SerializedProperty list)
        {
            EditorGUILayout.PropertyField(list);
            EditorGUI.indentLevel += 1;
            if (list.isExpanded)
            {
                EditorGUILayout.PropertyField(list.FindPropertyRelative("Array.size"));
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty channel = list.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(channel);
                    EditorGUI.indentLevel += 1;
                    if (channel.isExpanded)
                    {                     
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("channelType"));
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("textureFormat"));

                        EditorGUILayout.BeginHorizontal();

                        SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");
                        EditorGUILayout.PropertyField( materialPropertyName, GUILayout.MinWidth(300));
                        if (_shaderProperties != null)
                        {
                            int selection = EditorGUILayout.Popup(-1, _shaderProperties, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
                            if (selection >= 0)
                                materialPropertyName.stringValue = _shaderProperties[selection];
                                
                        }
                        EditorGUILayout.EndHorizontal();

                        UMAMaterial source = target as UMAMaterial;
                        if( source.material != null )
                        {
                            if (!source.material.HasProperty(materialPropertyName.stringValue))
                                EditorGUILayout.HelpBox("This name is not found in the shader! Are you sure it is correct?", MessageType.Warning);
                        }


                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("sourceTextureName"));
                    }
                    EditorGUI.indentLevel -= 1;
                }
            }
            EditorGUI.indentLevel -= 1;
        }

        private static string[] FindTexProperties( Shader shader)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            if (count <= 0)
                return null;

            List<string> texProperties = new List<string>();
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    texProperties.Add(ShaderUtil.GetPropertyName(shader, i));
            }

            return texProperties.ToArray();
        }
    }
}
