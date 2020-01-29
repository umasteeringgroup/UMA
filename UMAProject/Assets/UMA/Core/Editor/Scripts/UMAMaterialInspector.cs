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
        private GUIStyle _centeredStyle;
        private SerializedProperty _shaderParms;

        private bool shaderParmsFoldout = false;
        public void OnEnable()
        {
            _shaderParms = serializedObject.FindProperty("shaderParms");
        }
        public override void OnInspectorGUI()
        {
            UMAMaterial source = target as UMAMaterial;
            serializedObject.Update();

            if (_centeredStyle == null)
            {
                _centeredStyle = new GUIStyle(GUI.skin.label);
                _centeredStyle.alignment = TextAnchor.MiddleCenter;
                _centeredStyle.fontStyle = FontStyle.Bold;
            }

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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("materialType"), new GUIContent( "Material Type", "To atlas or not to atlas- that is the question."));

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Generated Texture Settings", _centeredStyle);
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MipMapBias"), new GUIContent("Mip Map Bias", "Negative values have sharper bias"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AnisoLevel"), new GUIContent("Aniso Level", "Anisotropic level"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MatFilterMode"),  new GUIContent("Texture Filter Mode", "Select the filter mode of Point, Bilinear or Trilinear"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MaskWithCurrentColor"), new GUIContent("Mask with Current Color", "When this is checked, the background of the atlas is filled with this color for alpha blending."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maskMultiplier"), new GUIContent("Mask Multiplier", "When Masking with current color, the current color is multiplied by this color."));
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("Compression"), new GUIContent("Texture Compression", "Compress the atlas texture to DXT1 or DXT5"));
            EditorGUILayout.EndVertical();

            GUILayout.Space(20f);
            shaderParmsFoldout = EditorGUILayout.Foldout(shaderParmsFoldout, "Shader Parameter Mapping");
            if (shaderParmsFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_shaderParms, true);
                EditorGUI.indentLevel--;
            }

            GUILayout.Space(20);

            DrawChannelList(serializedObject.FindProperty("channels"));

            GUILayout.Space(20);

            if(GUILayout.Button( new GUIContent("Select Matching OverlayDataAssets", "This will select all OverlayDataAssets found in the project that use this UMAMaterial."), GUILayout.Height(40)))
            {
                FindMatchingOverlayDataAssets();
            }

            serializedObject.ApplyModifiedProperties();
        }

        //Maybe eventually we can use the new IMGUI classes once older unity version are no longer supported.
        private void DrawChannelList(SerializedProperty list)
        {
            EditorGUILayout.PropertyField(list, new GUIContent("Texture Channels","List of texture channels to be used in this material."));
            EditorGUI.indentLevel += 1;
            if (list.isExpanded)
            {
                EditorGUILayout.PropertyField(list.FindPropertyRelative("Array.size"));
                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty channel = list.GetArrayElementAtIndex(i);
                    SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");//Let's get this eary to be able to use it in the element header.
                    EditorGUILayout.PropertyField(channel, new GUIContent("Channel " + i + ": " + materialPropertyName.stringValue));
                    EditorGUI.indentLevel += 1;
                    if (channel.isExpanded)
                    {                     
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("channelType"), new GUIContent("Channel Type", "The channel type. Affects the texture atlassing process."));
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("textureFormat"), new GUIContent("Texture Format", "Format used for the texture in this channel."));

                        if (channel.FindPropertyRelative("textureFormat") != null && i < ((UMAMaterial)target).channels.Length)
                        {
                            RenderTextureFormat format = ((UMAMaterial)target).channels[i].textureFormat;
                            if (!SystemInfo.SupportsRenderTextureFormat(format))
                            {
                                EditorGUILayout.HelpBox("This Texture Format is not supported on this system!", MessageType.Error);
                            }
                        }

                        EditorGUILayout.BeginHorizontal();

                        EditorGUILayout.PropertyField( materialPropertyName, new GUIContent("Material Property Name", "The name of the property this texture corresponds to in the shader used by this material."), GUILayout.MinWidth(300));
                        if (_shaderProperties != null)
                        {
                            int selection = EditorGUILayout.Popup(0, _shaderProperties, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
                            if (selection > 0)
                                materialPropertyName.stringValue = _shaderProperties[selection];
                                
                        }
                        EditorGUILayout.EndHorizontal();

                        SerializedProperty NonShaderProperty = channel.FindPropertyRelative("NonShaderTexture");
                        UMAMaterial source = target as UMAMaterial;
                        if( source.material != null )
                        {
                            if (!source.material.HasProperty(materialPropertyName.stringValue) && !NonShaderProperty.boolValue)
                                EditorGUILayout.HelpBox("This name is not found in the shader! Are you sure it is correct?", MessageType.Warning);
                        }

                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("ConvertRenderTexture"), new GUIContent("Convert RenderTexture", "Convert the Render Texture to a Texture2D (so it can be compressed)"));
                        SerializedProperty ConvertRenderTextureProperty = channel.FindPropertyRelative("ConvertRenderTexture");
                        if (ConvertRenderTextureProperty.boolValue == true)
                        {
                            EditorGUILayout.PropertyField(channel.FindPropertyRelative("Compression"), new GUIContent("Texture Compression", "Compress the atlas texture to DXT1 or DXT5"));
                        }
                        
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("DownSample"), new GUIContent("Down Sample", "Decrease size to save texture memory"));
                        EditorGUILayout.PropertyField(channel.FindPropertyRelative("sourceTextureName"), new GUIContent("Source Texture Name", "For use with procedural materials, leave empty otherwise."));

                        EditorGUILayout.PropertyField(NonShaderProperty, new GUIContent("NonShader Texture", "For having a texture get merged by the UMA texture merging process but not used in a shader. E.G. Pixel/UV based ID lookup. The Material Property Name should be empty when this is true."));
                        if(NonShaderProperty.boolValue && !string.IsNullOrEmpty(materialPropertyName.stringValue))
                        {
                            EditorGUILayout.HelpBox("A NonShader Texture shouldn't have a Material Property Name value.", MessageType.Warning);
                        }

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
            texProperties.Add("Select");
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    texProperties.Add(ShaderUtil.GetPropertyName(shader, i));
            }

            return texProperties.ToArray();
        }

        private void FindMatchingOverlayDataAssets()
        {
            HashSet<Object> selectedAssets = new HashSet<Object>();
            string[] guids = AssetDatabase.FindAssets("t:OverlayDataAsset");

            //TODO add progress bar.
            for(int i = 0; i < guids.Length; i++)
            {
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (overlay == null)
                    continue;

                if(UMAMaterial.Equals(overlay.material, target as UMAMaterial))
                    selectedAssets.Add(overlay);
            }

            if (selectedAssets.Count > 0)
            {
                Debug.Log(selectedAssets.Count + " matching OverlayDataAssets found.");
                Object[] selected = new Object[selectedAssets.Count];
                selectedAssets.CopyTo(selected);
                Selection.objects = selected;
            }
            else
            {
                EditorUtility.DisplayDialog("None found", "No matching OverlayDataAssets were found.", "OK");
            }
        }
    }
}
