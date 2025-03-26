using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAMaterial)),CanEditMultipleObjects]
    public class UMAMaterialInspector : Editor 
    {
        public static bool showHelp = false;
        private Shader _lastSelectedShader;
        private string[] _shaderProperties;
        private GUIStyle _centeredStyle;
        private SerializedProperty _shaderParms;
        private bool[] channelExpanded = new bool[3];

        private bool shaderParmsFoldout = false;

        private List<UnityEngine.Object> _inspectedObjects = new List<UnityEngine.Object>();

        public void OnEnable()
        { 
            _shaderParms = serializedObject.FindProperty("shaderParms");
            EditorApplication.update += DoInspectors;
        }

        public void OnDisable()
        {
            EditorApplication.update -= DoInspectors;
        }



        public void DoInspectors()
        {
            if (_inspectedObjects.Count > 0)
            {
                for(int i=0;i<_inspectedObjects.Count; i++)
                {
                    InspectorUtlity.InspectTarget(_inspectedObjects[i]);
                }
                _inspectedObjects.Clear();
            }
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
            SerializedProperty materialTypeProperty = serializedObject.FindProperty("materialType");

            UMAMaterial.MaterialType MatType = (UMAMaterial.MaterialType)materialTypeProperty.intValue;

            showHelp = EditorGUILayout.Toggle("Show Help", showHelp);

            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_material"), new GUIContent( "Default Material", "The Unity Material to link to."),GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Inspect", GUILayout.Width(60)))
            {
                _inspectedObjects.Add(serializedObject.FindProperty("_material").objectReferenceValue);
                // InspectorUtlity.InspectTarget(serializedObject.FindProperty("_material").objectReferenceValue);
            }
            GUILayout.EndHorizontal();
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Default Material: This is the material that will be used if no other material is found.", MessageType.Info);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("srpMaterials"), new GUIContent("SRP Materials", "Materials for SRP pipelines. If no SRP materials are found, the default material will be used."));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("SRP Materials: These are the materials that will be used for the various SRP pipelines. If no SRP materials are found, the default material will be used.", MessageType.Info);
            }
            GUILayout.BeginHorizontal(); 
            if (GUILayout.Button("Create from default"))
            {
                serializedObject.ApplyModifiedProperties();
                source.srpMaterials.Add(source.CreateSRPMaterial(UMAUtils.PipelineType.BuiltInPipeline));
                serializedObject.Update();
                source.SetupSRP(true);
            }
            if (GUILayout.Button("Force save Materials"))
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssetIfDirty(target);
            }
            if (GUILayout.Button("Refresh Materials"))
            {
                serializedObject.Update();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Init for each SRP"))
            {
                serializedObject.ApplyModifiedProperties();
                source.srpMaterials.Clear();
                source.srpMaterials.Add(source.CreateSRPMaterial(UMAUtils.PipelineType.BuiltInPipeline));
                source.srpMaterials.Add(source.CreateSRPMaterial(UMAUtils.PipelineType.UniversalPipeline));
                source.srpMaterials.Add(source.CreateSRPMaterial(UMAUtils.PipelineType.HDPipeline));
                serializedObject.Update();
            }
            if (GUILayout.Button("Clear SRP Materials"))
            {
                serializedObject.ApplyModifiedProperties();
                source.srpMaterials.Clear();
                serializedObject.Update();
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(materialTypeProperty, new GUIContent( "Material Type", "To atlas or not to atlas- that is the question."));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Atlas: Combine all textures using this material into a single atlas. Each channel will be a separate atlas - ie, normal maps will not be combine with albedo\nNo Atlas: Create a single texture for each channel, compositing all layers and colorizing as needed.\nUseExistingMaterial: use the material assigned directly. No channels, layering or colorizing will be done. This type has no texture channels.\nUseExistingTextures: Generates a new material, assigns the textures from the overlay to the appropriate channel. No layering can be done, but you can colorize the texture using Color 0 on the overlay. This will set all channels to type TintedTexture.", MessageType.Info);
            }
            if (MatType == UMAMaterial.MaterialType.UseExistingTextures)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_secondPass"), new GUIContent("Second Pass", "The Unity Material for a second pass. Usually NULL."));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("translateSRP"), new GUIContent("Translate SRP", "When checked, this will automatically translate the UMAMaterial property names to URP/HDRP names (ie - _MainTex becomes _BaseMap etc.)"));

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Generated Texture Settings", _centeredStyle);
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MipMapBias"), new GUIContent("Mip Map Bias", "Negative values have sharper bias"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("AnisoLevel"), new GUIContent("Aniso Level", "Anisotropic level"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MatFilterMode"),  new GUIContent("Texture Filter Mode", "Select the filter mode of Point, Bilinear or Trilinear"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MaskWithCurrentColor"), new GUIContent("Mask with Current Color", "When this is checked, the background of the atlas is filled with this color for alpha blending."));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Mask With Current Color is set, then the overlay is composited using the color on the overlay as the mask color. This is to address possible halo effects during compositing", MessageType.Info);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maskMultiplier"), new GUIContent("Mask Multiplier", "When Masking with current color, the current color is multiplied by this color."));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Masking color can be darkened to address source colorizing issues", MessageType.Info);
            }
            //EditorGUILayout.PropertyField(serializedObject.FindProperty("Compression"), new GUIContent("Texture Compression", "Compress the atlas texture to DXT1 or DXT5"));
            EditorGUILayout.EndVertical();

            if (!serializedObject.isEditingMultipleObjects)
            {
                shaderParmsFoldout = EditorGUILayout.Foldout(shaderParmsFoldout, "Shader Parameter Mapping", true);
                if (shaderParmsFoldout)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_shaderParms, true);
                    if (showHelp)
                    {
                        EditorGUILayout.HelpBox("These shader values are passed directly to the generated material at runtime", MessageType.Info);
                    }
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space(20);

                if (MatType == UMAMaterial.MaterialType.UseExistingMaterial)
                {
                    EditorGUILayout.HelpBox("Materials of type 'Use Existing Material' do not have texture channels, and do not allow compositing.", MessageType.Info);
                }
                else
                {
                    int channelCount = serializedObject.FindProperty("channels").arraySize;
                    if (channelCount != (target as UMAMaterial).channels.Length)
                    {
                        serializedObject.ApplyModifiedProperties();
                    }
                    DrawChannelList(serializedObject.FindProperty("channels"), (UMAMaterial.MaterialType)materialTypeProperty.intValue);
                }

                GUILayout.Space(20);

                if (GUILayout.Button(new GUIContent("Select Matching OverlayDataAssets", "This will select all OverlayDataAssets found in the project that use this UMAMaterial."), GUILayout.Height(40)))
                {
                    FindMatchingOverlayDataAssets();
                }
            }
            else
            {
                EditorGUILayout.LabelField("Channel properties cannot be edited multi-object");
            }

            bool wasChanged = serializedObject.ApplyModifiedProperties();
            if (wasChanged)
            {
                serializedObject.ApplyModifiedProperties();
                source.SetupSRP(true);
                UMAMaterial.MaterialType NewMatType = (UMAMaterial.MaterialType)materialTypeProperty.intValue;
                if (MatType != NewMatType)
                {
                    if (NewMatType != UMAMaterial.MaterialType.UseExistingTextures)
                    {
                        // second Pass only used for UseExistingTextures
                        var secondPassProperty = serializedObject.FindProperty("_secondPass");
                        secondPassProperty.SetValue<Object>(null);
                        if (MatType == UMAMaterial.MaterialType.UseExistingTextures)
                        {
                            var list = serializedObject.FindProperty("channels");
                            for (int i = 0; i < list.arraySize; i++)
                            {
                                SerializedProperty channel = list.GetArrayElementAtIndex(i);
                                var channelProperty = channel.FindPropertyRelative("channelType");
                                channelProperty.intValue = (int)UMAMaterial.ChannelType.Texture;
                            }
                            serializedObject.ApplyModifiedProperties();
                        }
                    }
                    if (NewMatType == UMAMaterial.MaterialType.UseExistingMaterial)
                    {
                        var channelsProperty = serializedObject.FindProperty("channels");
                        channelsProperty.ClearArray();
                        serializedObject.ApplyModifiedProperties();
                    }
                    if (NewMatType == UMAMaterial.MaterialType.UseExistingTextures)
                    {
                        // When changing to UseExistingTexture, all channels are forced to UseExistingTexture and no atlas is created.
                        var list = serializedObject.FindProperty("channels");
                        for (int i=0;i<list.arraySize;i++)
                        {
                            SerializedProperty channel = list.GetArrayElementAtIndex(i);
                            var channelProperty = channel.FindPropertyRelative("channelType");
                            channelProperty.intValue = (int)UMAMaterial.ChannelType.TintedTexture;
                        }
                        serializedObject.ApplyModifiedProperties();
                    }
                    Repaint();
                }
            }
        }

        public bool IsChannelValid(int channel)
        {
            UMAMaterial source = target as UMAMaterial;
            if (channel >= source.channels.Length)
            {
                return false;
            }
            var matchan = source.channels[channel];

            if (!string.IsNullOrEmpty(matchan.materialPropertyName) && source.material != null)
            {
                if (!source.material.HasProperty(matchan.materialPropertyName) && !matchan.NonShaderTexture)
                {
                    return false;
                }
            }
            return true;
        }
        
 
        //Maybe eventually we can use the new IMGUI classes once older unity version are no longer supported.
        private void DrawChannelList(SerializedProperty list, UMAMaterial.MaterialType materialType)
        {
            // EditorGUILayout.PropertyField(list, new GUIContent("Texture Channels", "List of texture channels to be used in this material."));
            // channelListExpanded = GUIHelper.FoldoutBar(channelListExpanded, "Texture Channels");
            //if (channelListExpanded)
            //GUIHelper.FoldoutBar(channelListExpanded, "Texture Channels");
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
                GUILayout.Label("Texture Channels", _centeredStyle);
                EditorGUILayout.PropertyField(list.FindPropertyRelative("Array.size"));
                if (channelExpanded.Length != list.arraySize )
                {
                    channelExpanded = new bool[list.arraySize];
                }

                for (int i = 0; i < list.arraySize; i++)
                {
                    SerializedProperty channel = list.GetArrayElementAtIndex(i);
                    SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");//Let's get this eary to be able to use it in the element header.
                                          
                    // EditorGUILayout.PropertyField(channel, new GUIContent("Channel " + i + ": " + materialPropertyName.stringValue));
                    
                    string error = "";
                    if (!IsChannelValid(i))
                    {
                           error = " - Error: Not Found!";
                    }
                        
                        
                        // EditorGUILayout.LabelField(new GUIContent("Channel " + i + ": " + materialPropertyName.stringValue),EditorStyles.toolbar);

                    channelExpanded[i] = GUIHelper.FoldoutBar(channelExpanded[i],"Channel " + i + ": " + materialPropertyName.stringValue + error );
                    if (channelExpanded[i])
                    {
                        GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.85f, 0.85f));
                        var channelProperty = channel.FindPropertyRelative("channelType");

                        // if MaterialType ==  UseExistingTextures = 8
                        // don't show "channelProperty"
                        if (materialType != UMAMaterial.MaterialType.UseExistingTextures)
                        {
                            EditorGUILayout.PropertyField(channelProperty, new GUIContent("Channel Type", "The channel type. Affects the texture atlassing process."));
                            if (showHelp)
                            {
                                EditorGUILayout.HelpBox("Texture type is the base type. Overlays are composited using the alpha mask. Alpha from the overlays are composited into the texture. This preserves alpha channel contents. To composite with this type would require an alpha mask\n"+
                                    "NormalMap - this is an atlassed normal map.\n"+
                                    "MaterialColor will set the Material Color only\n "+
                                    "TintedTexture Will set the texture from the first overlay on the material without compositing. The color from the first color will be passed to the _Color parameter on the shader\n"+
                                    "DiffuseTexture is similar to base Texture type, but the alpha is not composited into the texture, but used for masking This is the normal texture type, and can use the alpha mask, or the alpha of the first texture\n "+
                                    "DetailNormalMap - use this for Detail Normal Maps"
                                    , MessageType.Info);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Materials of type 'UseExistingTextures' use TintedTexture type");
                        }
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
                            string oldValue = materialPropertyName.stringValue;
                            int selection = EditorGUILayout.Popup(0, _shaderProperties, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
                            if (oldValue != materialPropertyName.stringValue) 
                            {
                                UpdateCurrentSRP(i, materialPropertyName.stringValue, serializedObject);
                            }
                            if (selection > 0)
                            {
                                materialPropertyName.stringValue = _shaderProperties[selection];
                                UpdateCurrentSRP(i, _shaderProperties[selection],serializedObject);
                            }
                        }
                        EditorGUILayout.EndHorizontal();

                        SerializedProperty NonShaderProperty = channel.FindPropertyRelative("NonShaderTexture");
                        UMAMaterial source = target as UMAMaterial;
                        if( source.material != null )
                        {
                            if (!source.material.HasProperty(materialPropertyName.stringValue) && !NonShaderProperty.boolValue)
                            {
                                EditorGUILayout.HelpBox("This name is not found in the shader! Are you sure it is correct?", MessageType.Warning);
                            }
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
                        if (showHelp)
                        {
                            EditorGUILayout.HelpBox("NonShaderTexture is For having a texture get merged by the UMA texture merging process but not used in a shader.E.G.Pixel / UV based ID lookup.The Material Property Name should be empty when this is true.", MessageType.Info);

                        }
                        if (NonShaderProperty.boolValue && !string.IsNullOrEmpty(materialPropertyName.stringValue))
                        {
                            EditorGUILayout.HelpBox("A NonShader Texture shouldn't have a Material Property Name value.", MessageType.Warning);
                        }
                        GUIHelper.EndVerticalPadded(10);
                    }
                    GUILayout.Space(8);
                }
                GUIHelper.EndVerticalPadded(10);
            }
        }

        private void UpdateCurrentSRP(int i, string v, SerializedObject sobj)
        {
            // Update the current SRP property name from the array
            UMAMaterial source = target as UMAMaterial;
            if (source.srpMaterials != null)
            {
                foreach (UMAMaterial.SRPMaterial srpMaterial in source.srpMaterials)
                {
                    if (srpMaterial.SRP == UMAUtils.CurrentPipeline)
                    {
                        srpMaterial.alternateKeywords[i] = v;
                        sobj.Update();
                    }
                }
            }
        }

        private static string[] FindTexProperties( Shader shader)
        {
            int count = ShaderUtil.GetPropertyCount(shader);
            if (count <= 0)
            {
                return null;
            }

            List<string> texProperties = new List<string>();
            texProperties.Add("Select");
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    texProperties.Add(ShaderUtil.GetPropertyName(shader, i));
                }
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
                {
                    continue;
                }

                if (UMAMaterial.Equals(overlay.material, target as UMAMaterial))
                {
                    selectedAssets.Add(overlay);
                }
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
