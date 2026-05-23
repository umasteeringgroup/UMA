using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UMA.CharacterSystem;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAMaterial)),CanEditMultipleObjects]
    public class UMAMaterialInspector : Editor 
    {
        public DynamicCharacterAvatar dca;
        public static bool showHelp = false;
        private Shader _lastSelectedShader;
        private string[] _shaderProperties;
        private GUIStyle _centeredStyle;
        private SerializedProperty _shaderParms;
        private SerializedProperty _shaderKeywords;
        private bool[] channelExpanded = new bool[3];
        private static bool showMaterialInspector = false;
        Editor innerEditor = null;
        private bool shaderParmsFoldout = false;
        private bool shaderKeywordsFoldout = false;
        private int _selectedShaderKeywordIndex = 0;
        private static readonly RenderTextureFormat[] _supportedChannelTextureFormats = UMAMaterial.GetSupportedChannelTextureFormats();
        private static readonly string[] _supportedChannelTextureFormatNames = BuildSupportedChannelTextureFormatNames();

        private List<UnityEngine.Object> _inspectedObjects = new List<UnityEngine.Object>();

        private struct ShaderKeywordInfo
        {
            public ShaderKeywordInfo(string name, string type)
            {
                Name = name;
                Type = type;
            }

            public string Name;
            public string Type;
        }

        public void OnEnable()
        { 
            _shaderParms = serializedObject.FindProperty("shaderParms");
            _shaderKeywords = serializedObject.FindProperty("shaderKeywords");
            EditorApplication.update += DoInspectors;
        }

        public void OnDisable()
        {
            EditorApplication.update -= DoInspectors;
            if (innerEditor != null)
            {
                DestroyImmediate(innerEditor);
                innerEditor = null;
            }
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
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Inspect", GUILayout.Width(60)))
            {
                _inspectedObjects.Add(serializedObject.FindProperty("_HDRPMaterial").objectReferenceValue);
            }
            GUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_HDRPMaterial"), new GUIContent("HDRP Material", "The Unity Material for HDRP."), GUILayout.ExpandWidth(true));


            GUILayout.EndHorizontal();



            if (showHelp)
            {
                EditorGUILayout.HelpBox("Default Material: This is the material that will be used if no other material is found.", MessageType.Info);
            }
            showMaterialInspector = GUIHelper.FoldoutBar(showMaterialInspector, "Material Preview");
            if (showMaterialInspector && source.material != null)
            {
                if (innerEditor == null)
                {
                    Material m = source.material;

                    innerEditor = Editor.CreateEditor(source.material, typeof(MaterialEditor));
                    if (innerEditor == null)
                    {
                        Debug.LogError("Failed to create MaterialEditor for " + source.material.name);
                        return;
                    }
                }
                //GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.85f, 0.85f));
                EditorGUILayout.HelpBox("Select an avatar in the scene, and use the material properties below to adjust the template material and see the changes in real time",MessageType.Info);
                dca = EditorGUILayout.ObjectField("Preview Avatar", dca, typeof(DynamicCharacterAvatar), true) as DynamicCharacterAvatar;
                DrawFoldoutInspector(source.material, ref innerEditor);
                //GUILayout.Label("This is the material inspector for the material used by this UMAMaterial.", _centeredStyle);
                //GUIHelper.EndVerticalPadded(10);

                if (Event.current.type == EventType.Repaint && innerEditor != null && dca != null)
                {
                    SkinnedMeshRenderer[] smr = dca.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    if (smr != null && smr.Length > 0)
                    {
                        foreach (SkinnedMeshRenderer s in smr)
                        {
                            foreach(Material mat in s.sharedMaterials)
                            {
                                if (mat == null)
                                {
                                    continue;
                                }
                                string sourceMatName = source.material.name;
                                // If the source material has a second pass, we will ignore _Pass1 and _Pass2 suffixes when matching to the materials on the renderer, to allow for two pass shader setups.
                                if (source.secondPass != null)
                                {
                                    if (source.material.name.ToLowerInvariant().EndsWith("_pass1"))
                                    {
                                        sourceMatName = source.material.name.Substring(0, source.material.name.Length - "_Pass1".Length);
                                    }
                                    else if (source.material.name.ToLowerInvariant().EndsWith("_pass2"))
                                    {
                                        sourceMatName = source.material.name.Substring(0, source.material.name.Length - "_Pass2".Length);
                                    }
                                }
                                if (mat.name.StartsWith(sourceMatName) && mat.shader == source.material.shader)
                                {
                                    List<KeyValuePair<string,Texture>> savedTextures = new List<KeyValuePair<string,Texture>>();
                                    foreach (var chan in source.channels)
                                    {
                                        string prop = chan.materialPropertyName;
                                        Texture tex = mat.GetTexture(prop);
                                        if (tex != null)
                                        {
                                            savedTextures.Add(new KeyValuePair<string, Texture>(prop,tex));
                                        }
                                    }
                                    mat.CopyMatchingPropertiesFromMaterial(source.material);
                                    // restore textures
                                    foreach (var kvp in savedTextures)
                                    {
                                        if (mat.HasProperty(kvp.Key))
                                        {
                                            mat.SetTexture(kvp.Key, kvp.Value);
                                        }
                                    }
                                }
                                else
                                {
                                    //Debug.LogWarning("Material " + mat.name + " does not match " + source.material.name + ". Skipping copy of properties.");
                                    continue;
                                }
                            }
                        }
                    }
                }
                if (GUILayout.Button("Save material changes to disk"))
                {
                    if (innerEditor == null)
                    {
                        Debug.LogError("No inner editor found for material " + source.material.name);
                        return;
                    }
                    innerEditor.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(source.material);
                    AssetDatabase.SaveAssetIfDirty(source.material);
                }
            }

            EditorGUILayout.PropertyField(materialTypeProperty, new GUIContent( "Material Type", "To atlas or not to atlas- that is the question."));
            if (showHelp)
            {
                EditorGUILayout.HelpBox("Atlas: Combine all textures using this material into a single atlas. Each channel will be a separate atlas - ie, normal maps will not be combine with albedo\nNo Atlas: Create a single texture for each channel, compositing all layers and colorizing as needed.\nUseExistingMaterial: use the material assigned directly. No channels, layering or colorizing will be done. This type has no texture channels.\nUseExistingTextures: Generates a new material, assigns the textures from the overlay to the appropriate channel. No layering can be done, but you can colorize the texture using Color 0 on the overlay. This will set all channels to type TintedTexture.", MessageType.Info);
            }
            if (MatType == UMAMaterial.MaterialType.UseExistingTextures)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_secondPass"), new GUIContent("Second Pass", "The Unity Material for a second pass. Usually NULL."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_HDRPSecondPass"), new GUIContent("HDRP Second Pass", "The Unity Material for a second pass in HDRP. Usually NULL."));
            }

            GUILayout.Space(20);
            EditorGUILayout.LabelField("Generated Texture Settings", _centeredStyle);
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("generateMipMaps"), new GUIContent("Generate Mip Maps", "Enable or disable mip map generation."));
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

                shaderKeywordsFoldout = EditorGUILayout.Foldout(shaderKeywordsFoldout, "Shader Keywords", true);
                if (shaderKeywordsFoldout)
                {
                    EditorGUI.indentLevel++;
                    DrawShaderKeywordSection(source, _shaderKeywords);
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
                UMAMaterial.MaterialType NewMatType = (UMAMaterial.MaterialType)materialTypeProperty.intValue;
                if (MatType != NewMatType)
                {
                    if (MatType == UMAMaterial.MaterialType.UseExistingTextures)
                    {
                        // When changing from UseExistingTexture, all channels are forced to Texture type and the second pass is cleared since they aren't used in that mode.
                        var list = serializedObject.FindProperty("channels");
                        for (int i = 0; i < list.arraySize; i++)
                        {
                            SerializedProperty channel = list.GetArrayElementAtIndex(i);
                            var channelProperty = channel.FindPropertyRelative("channelType");
                            channelProperty.intValue = (int)UMAMaterial.ChannelType.Texture;
                        }
                        serializedObject.ApplyModifiedProperties();
                    }
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
                        
                        
                   EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    channelExpanded[i] = EditorGUILayout.Foldout(channelExpanded[i], "Channel " + i + ": " + materialPropertyName.stringValue + error, true);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("X", EditorStyles.toolbarButton, GUILayout.Width(22)))
                    {
                        Undo.RecordObject(target, "Remove Channel");
                        list.DeleteArrayElementAtIndex(i);
                        list.serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
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
                        SerializedProperty textureFormatProperty = channel.FindPropertyRelative("textureFormat");
                        DrawTextureFormatPopup(textureFormatProperty);

                        RenderTextureFormat selectedFormat = (RenderTextureFormat)textureFormatProperty.intValue;
                        if (!SystemInfo.SupportsRenderTextureFormat(selectedFormat))
                        {
                            EditorGUILayout.HelpBox("This Texture Format is not supported on this system. UMA will fall back to ARGB32 at runtime.", MessageType.Warning);
                        }

                        EditorGUILayout.BeginHorizontal();

                        EditorGUILayout.PropertyField( materialPropertyName, new GUIContent("Material Property Name", "The name of the property this texture corresponds to in the shader used by this material."), GUILayout.MinWidth(300));
                        if (_shaderProperties != null)
                        {
                            string oldValue = materialPropertyName.stringValue;
                            int selection = EditorGUILayout.Popup(0, _shaderProperties, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
                            if (selection > 0)
                            {
                                materialPropertyName.stringValue = _shaderProperties[selection];
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

        private void DrawShaderKeywordSection(UMAMaterial source, SerializedProperty shaderKeywordsProperty)
        {
            GUIHelper.BeginVerticalPadded(10, new Color(0.85f, 0.9f, 0.85f));

            if (source == null)
            {
                EditorGUILayout.HelpBox("Source material is NULL!", MessageType.Info);
                GUIHelper.EndVerticalPadded(10);
                return;
            }

            if (shaderKeywordsProperty == null)
            {
                EditorGUILayout.HelpBox("Shader keyword Property is NULL!", MessageType.Info);
                GUIHelper.EndVerticalPadded(10);
                return;
            }


            if (source.material == null || source.material.shader == null)
            {
                EditorGUILayout.HelpBox("Assign a material with a shader to manage stored shader keywords.", MessageType.Info);
                GUIHelper.EndVerticalPadded(10);
                return;
            }
            else
            {
                EditorGUILayout.HelpBox("Add shader keywords to this list to have them stored on the UMAMaterial. This is useful for copying shader parameters and values to Shared Colors in the editor", MessageType.Info);
            }

            ShaderKeywordInfo[] shaderKeywordInfos = FindShaderKeywords(source.material);
            DrawShaderKeywordGridHeader();

            if (shaderKeywordsProperty.arraySize == 0)
            {
                EditorGUILayout.LabelField("No shader keywords added.");
            }
            else
            {
                for (int i = 0; i < shaderKeywordsProperty.arraySize; i++)
                {
                    SerializedProperty shaderKeyword = shaderKeywordsProperty.GetArrayElementAtIndex(i);
                    if (DrawShaderKeywordRow(shaderKeyword.stringValue, GetShaderKeywordType(shaderKeyword.stringValue, shaderKeywordInfos), i, shaderKeywordsProperty))
                    {
                        GUIUtility.ExitGUI();
                    }
                }
            }

            string[] addableKeywordNames = GetAddableShaderKeywordNames(shaderKeywordInfos, shaderKeywordsProperty);

            EditorGUI.BeginDisabledGroup(addableKeywordNames.Length == 0);
            EditorGUILayout.BeginHorizontal();
            if (addableKeywordNames.Length > 0)
            {
                if (_selectedShaderKeywordIndex >= addableKeywordNames.Length)
                {
                    _selectedShaderKeywordIndex = 0;
                }

                _selectedShaderKeywordIndex = EditorGUILayout.Popup(new GUIContent("Add Keyword", "Adds a shader keyword to the stored keyword list."), _selectedShaderKeywordIndex, addableKeywordNames);
            }
            else
            {
                EditorGUILayout.Popup(new GUIContent("Add Keyword", "Adds a shader keyword to the stored keyword list."), 0, new string[] { "No available keywords" });
            }

            if (GUILayout.Button("Add", GUILayout.Width(60)) && addableKeywordNames.Length > 0)
            {
                int newIndex = shaderKeywordsProperty.arraySize;
                shaderKeywordsProperty.InsertArrayElementAtIndex(newIndex);
                shaderKeywordsProperty.GetArrayElementAtIndex(newIndex).stringValue = addableKeywordNames[_selectedShaderKeywordIndex];
                _selectedShaderKeywordIndex = 0;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();

            if (showHelp)
            {
                EditorGUILayout.HelpBox("Shader keywords added here are stored on the UMAMaterial and can be copied when generating material variants.", MessageType.Info);
            }

            GUIHelper.EndVerticalPadded(10);
        }

        private static void DrawShaderKeywordGridHeader()
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float removeWidth = 24f;
            float typeWidth = Mathf.Min(160f, rowRect.width * 0.35f);
            Rect keywordRect = new Rect(rowRect.x, rowRect.y, rowRect.width - typeWidth - removeWidth - 8f, rowRect.height);
            Rect typeRect = new Rect(keywordRect.xMax + 4f, rowRect.y, typeWidth, rowRect.height);

            EditorGUI.LabelField(keywordRect, "Keyword", EditorStyles.miniBoldLabel);
            EditorGUI.LabelField(typeRect, "Type", EditorStyles.miniBoldLabel);
        }

        private bool DrawShaderKeywordRow(string keywordName, string keywordType, int index, SerializedProperty shaderKeywordsProperty)
        {
            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float removeWidth = 24f;
            float typeWidth = Mathf.Min(160f, rowRect.width * 0.35f);
            Rect keywordRect = new Rect(rowRect.x, rowRect.y, rowRect.width - typeWidth - removeWidth - 8f, rowRect.height);
            Rect typeRect = new Rect(keywordRect.xMax + 4f, rowRect.y, typeWidth, rowRect.height);
            Rect removeRect = new Rect(typeRect.xMax + 4f, rowRect.y, removeWidth, rowRect.height);

            EditorGUI.LabelField(keywordRect, string.IsNullOrEmpty(keywordName) ? "(Empty)" : keywordName);
            EditorGUI.LabelField(typeRect, keywordType);

            if (GUI.Button(removeRect, "X", EditorStyles.miniButton))
            {
                Undo.RecordObject(target, "Remove Shader Keyword");
                shaderKeywordsProperty.DeleteArrayElementAtIndex(index);
                shaderKeywordsProperty.serializedObject.ApplyModifiedProperties();
                return true;
            }

            return false;
        }

        private static ShaderKeywordInfo[] FindShaderKeywords(Material material)
        {
            if (material == null || material.shader == null)
            {
                return new ShaderKeywordInfo[0];
            }

            List<ShaderKeywordInfo> shaderKeywordInfos = new List<ShaderKeywordInfo>();
            HashSet<string> seenKeywords = new HashSet<string>();
            MaterialProperty[] materialProperties = MaterialEditor.GetMaterialProperties(new Object[] { material });

            for (int i = 0; i < materialProperties.Length; i++)
            {
                MaterialProperty materialProperty = materialProperties[i];
                if (materialProperty == null || string.IsNullOrEmpty(materialProperty.name) || !seenKeywords.Add(materialProperty.name))
                {
                    continue;
                }

                string propertyType = GetSupportedMaterialPropertyType(materialProperty);
                if (string.IsNullOrEmpty(propertyType))
                {
                    continue;
                }
                if (materialProperty.name.StartsWith("_Queue") || materialProperty.name.StartsWith("_XR"))
                {
                    continue;
                }

                shaderKeywordInfos.Add(new ShaderKeywordInfo(materialProperty.name, propertyType));
            }

            return shaderKeywordInfos.ToArray();
        }

        private static string GetSupportedMaterialPropertyType(MaterialProperty materialProperty)
        {
            if (materialProperty == null)
            {
                return null;
            }

            string propertyTypeName = materialProperty.propertyType.ToString();
            switch (propertyTypeName)
            {
                case "Int":
                    return "Int";
                case "Float":
                    return "Float";
                case "Color":
                    return "Color";
                case "Vector":
                    return IsVector2Property(materialProperty) ? "Vector2" : "Vector4";
                default:
                    return null;
            }
        }

        private static bool IsVector2Property(MaterialProperty materialProperty)
        {
            Vector4 value = materialProperty.vectorValue;
            return Mathf.Approximately(value.z, 0f) && Mathf.Approximately(value.w, 0f);
        }

        private static string GetShaderKeywordType(string keywordName, ShaderKeywordInfo[] shaderKeywordInfos)
        {
            if (string.IsNullOrEmpty(keywordName))
            {
                return "Missing";
            }

            for (int i = 0; i < shaderKeywordInfos.Length; i++)
            {
                if (shaderKeywordInfos[i].Name == keywordName)
                {
                    return shaderKeywordInfos[i].Type;
                }
            }

            return "Missing";
        }

        private static string[] GetAddableShaderKeywordNames(ShaderKeywordInfo[] shaderKeywordInfos, SerializedProperty shaderKeywordsProperty)
        {
            HashSet<string> existingKeywords = new HashSet<string>();
            for (int i = 0; i < shaderKeywordsProperty.arraySize; i++)
            {
                string existingKeyword = shaderKeywordsProperty.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrEmpty(existingKeyword))
                {
                    existingKeywords.Add(existingKeyword);
                }
            }

            List<string> addableKeywords = new List<string>();
            for (int i = 0; i < shaderKeywordInfos.Length; i++)
            {
                if (!existingKeywords.Contains(shaderKeywordInfos[i].Name))
                {
                    addableKeywords.Add(shaderKeywordInfos[i].Name);
                }
            }

            return addableKeywords.ToArray();
        }

        private static string[] FindTexProperties( Shader shader)
        {
            int count = shader.GetPropertyCount();
            if (count <= 0)
            {
                return null;
            }

            List<string> texProperties = new List<string>();
            texProperties.Add("Select");
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
                {
                    texProperties.Add(shader.GetPropertyName(i));
                }
            }

            return texProperties.ToArray();
        }

        private static string[] BuildSupportedChannelTextureFormatNames()
        {
            string[] names = new string[_supportedChannelTextureFormats.Length];
            for (int i = 0; i < _supportedChannelTextureFormats.Length; i++)
            {
                names[i] = _supportedChannelTextureFormats[i].ToString();
            }
            return names;
        }

        private static void DrawTextureFormatPopup(SerializedProperty textureFormatProperty)
        {
            if (textureFormatProperty == null)
            {
                return;
            }

            RenderTextureFormat currentFormat = (RenderTextureFormat)textureFormatProperty.intValue;
            int selectedIndex = 0;
            bool found = false;

            for (int i = 0; i < _supportedChannelTextureFormats.Length; i++)
            {
                if (_supportedChannelTextureFormats[i] == currentFormat)
                {
                    selectedIndex = i;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                currentFormat = UMAMaterial.DefaultChannelTextureFormat;
                textureFormatProperty.intValue = (int)currentFormat;
            }

            int newIndex = EditorGUILayout.Popup(new GUIContent("Texture Format", "Cross-target RenderTexture format used for this channel."), selectedIndex, _supportedChannelTextureFormatNames);
            if (newIndex < 0 || newIndex >= _supportedChannelTextureFormats.Length)
            {
                newIndex = 0;
            }

            RenderTextureFormat newFormat = _supportedChannelTextureFormats[newIndex];
            if (newFormat != currentFormat)
            {
                textureFormatProperty.intValue = (int)newFormat;
            }
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

    internal class UMAMaterialOverlayUsageWindow : EditorWindow
    {
        private class MaterialUsageRow
        {
            public UMAMaterial Material;
            public readonly List<OverlayDataAsset> Overlays = new List<OverlayDataAsset>();

            public int UseCount => Overlays.Count;
            public string Name => Material != null ? Material.name : string.Empty;
        }

        private readonly List<MaterialUsageRow> _materials = new List<MaterialUsageRow>();
        private readonly List<UMAMaterial> _materialFilter = new List<UMAMaterial>();
        private DefaultAsset _overlayFolderAsset;
        private string _overlayFolderPath = string.Empty;
        private Vector2 _materialScroll;
        private Vector2 _overlayScroll;
        private int _selectedMaterialIndex = -1;
        private int _scannedOverlayCount;
        private int _unassignedOverlayCount;

        [MenuItem("UMA/Find UMAMaterial in Overlays", priority = 26)]
        public static void Open()
        {
            Open(null);
        }

        [MenuItem("Assets/UMA/Find Selected UMAMaterials in Overlays", false, 2008)]
        private static void OpenForSelectedMaterials()
        {
            Open(GetSelectedMaterialsFromSelection());
        }

        [MenuItem("Assets/UMA/Find Selected UMAMaterials in Overlays", true)]
        private static bool ValidateOpenForSelectedMaterials()
        {
            return GetSelectedMaterialsFromSelection().Count > 0;
        }

        private static void Open(IList<UMAMaterial> materialFilter)
        {
            UMAMaterialOverlayUsageWindow window = GetWindow<UMAMaterialOverlayUsageWindow>(true, "Find UMAMaterial in Overlays", true);
            window.minSize = new Vector2(860f, 420f);
            window.SetMaterialFilter(materialFilter);
            window.RefreshUsage();
            window.ShowUtility();
            window.Focus();
        }

        private void OnEnable()
        {
            if (_materials.Count == 0)
            {
                RefreshUsage();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawFolderFilterBar();
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            DrawMaterialColumn();
            DrawOverlayColumn();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Overlay UMAMaterial Usage", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (_materialFilter.Count > 0)
            {
                GUILayout.Label("Filtered to " + _materialFilter.Count + " selected material(s)", EditorStyles.miniLabel);
            }
            GUILayout.Label(_materials.Count + " material(s), " + _scannedOverlayCount + " overlay(s)", EditorStyles.miniLabel);
            if (_unassignedOverlayCount > 0)
            {
                GUILayout.Label(_unassignedOverlayCount + " unassigned", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                RefreshUsage();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFolderFilterBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            DefaultAsset newFolderAsset = EditorGUILayout.ObjectField(
                new GUIContent("Overlay Folder", "Only OverlayDataAssets in this folder and its subfolders are included."),
                _overlayFolderAsset,
                typeof(DefaultAsset),
                false,
                GUILayout.MinWidth(240f)) as DefaultAsset;
            if (EditorGUI.EndChangeCheck())
            {
                SetOverlayFolderFilter(newFolderAsset);
            }

            using (new EditorGUI.DisabledScope(_overlayFolderAsset == null))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                {
                    SetOverlayFolderFilter(null);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(string.IsNullOrEmpty(_overlayFolderPath) ? "All overlay folders" : _overlayFolderPath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawMaterialColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(430f));
            EditorGUILayout.LabelField("UMAMaterials", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(22f));
            GUILayout.Label("Material", EditorStyles.miniBoldLabel, GUILayout.Width(220f));
            GUILayout.Label("", GUILayout.Width(50f));
            GUILayout.Label("Status", EditorStyles.miniBoldLabel, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();

            _materialScroll = EditorGUILayout.BeginScrollView(_materialScroll, EditorStyles.helpBox);
            for (int i = 0; i < _materials.Count; i++)
            {
                DrawMaterialRow(i);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawMaterialRow(int index)
        {
            MaterialUsageRow usage = _materials[index];
            bool selected = index == _selectedMaterialIndex;
            EditorGUILayout.BeginHorizontal(selected ? EditorStyles.helpBox : GUIStyle.none);
            if (GUILayout.Button(selected ? ">" : string.Empty, EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                _selectedMaterialIndex = index;
                GUI.FocusControl(null);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(usage.Material, typeof(UMAMaterial), false, GUILayout.Width(220f));
            }

            using (new EditorGUI.DisabledScope(usage.Material == null))
            {
                if (GUILayout.Button("Ping", GUILayout.Width(50f)))
                {
                    Selection.activeObject = usage.Material;
                    EditorGUIUtility.PingObject(usage.Material);
                }
            }

            GUILayout.Label(GetUseStatus(usage), GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOverlayColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            MaterialUsageRow selectedUsage = GetSelectedUsage();
            string header = selectedUsage != null && selectedUsage.Material != null ? selectedUsage.Material.name : "Overlays";
            EditorGUILayout.LabelField(header, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("OverlayDataAsset", EditorStyles.miniBoldLabel);
            GUILayout.Label("", GUILayout.Width(70f));
            EditorGUILayout.EndHorizontal();

            _overlayScroll = EditorGUILayout.BeginScrollView(_overlayScroll, EditorStyles.helpBox);
            if (selectedUsage == null)
            {
                EditorGUILayout.HelpBox("Select a UMAMaterial to see matching OverlayDataAssets.", MessageType.Info);
            }
            else if (selectedUsage.Overlays.Count == 0)
            {
                EditorGUILayout.HelpBox("No OverlayDataAssets use this UMAMaterial.", MessageType.Info);
            }
            else
            {
                for (int i = 0; i < selectedUsage.Overlays.Count; i++)
                {
                    DrawOverlayRow(selectedUsage.Overlays[i]);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawOverlayRow(OverlayDataAsset overlay)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(overlay, typeof(OverlayDataAsset), false, GUILayout.ExpandWidth(true));
            }

            using (new EditorGUI.DisabledScope(overlay == null))
            {
                if (GUILayout.Button("Inspect", GUILayout.Width(70f)))
                {
                    OverlayDataAsset overlayToInspect = overlay;
                    EditorApplication.delayCall += () =>
                    {
                        if (overlayToInspect != null)
                        {
                            UMA.InspectorUtlity.InspectTarget(overlayToInspect);
                        }
                    };
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void RefreshUsage()
        {
            UMAMaterial previouslySelected = GetSelectedUsage()?.Material;
            _materials.Clear();
            _scannedOverlayCount = 0;
            _unassignedOverlayCount = 0;

            Dictionary<UMAMaterial, MaterialUsageRow> usageByMaterial = new Dictionary<UMAMaterial, MaterialUsageRow>();
            Dictionary<string, UMAMaterial> materialByName = new Dictionary<string, UMAMaterial>();

            List<UMAMaterial> materials = _materialFilter.Count > 0 ? new List<UMAMaterial>(_materialFilter) : LoadAssets<UMAMaterial>();
            materials.Sort(CompareAssetsByName);
            for (int i = 0; i < materials.Count; i++)
            {
                UMAMaterial material = materials[i];
                if (material == null)
                {
                    continue;
                }

                GetOrCreateUsage(material, usageByMaterial);
                if (!string.IsNullOrEmpty(material.name) && !materialByName.ContainsKey(material.name))
                {
                    materialByName.Add(material.name, material);
                }
            }

            List<OverlayDataAsset> overlays = LoadAssets<OverlayDataAsset>(_overlayFolderPath);
            _scannedOverlayCount = overlays.Count;
            for (int i = 0; i < overlays.Count; i++)
            {
                OverlayDataAsset overlay = overlays[i];
                if (overlay == null)
                {
                    continue;
                }

                UMAMaterial material = ResolveOverlayMaterial(overlay, materialByName);
                if (material == null)
                {
                    _unassignedOverlayCount++;
                    continue;
                }

                MaterialUsageRow usage = GetOrCreateUsage(material, usageByMaterial);
                usage.Overlays.Add(overlay);
            }

            for (int i = 0; i < _materials.Count; i++)
            {
                _materials[i].Overlays.Sort(CompareAssetsByName);
            }
            _materials.Sort(CompareMaterialRowsByName);

            _selectedMaterialIndex = FindMaterialIndex(previouslySelected);
            if (_selectedMaterialIndex < 0 && _materials.Count > 0)
            {
                _selectedMaterialIndex = 0;
            }
            _materialScroll = Vector2.zero;
            _overlayScroll = Vector2.zero;
            Repaint();
        }

        private void SetOverlayFolderFilter(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                if (_overlayFolderAsset == null && string.IsNullOrEmpty(_overlayFolderPath))
                {
                    return;
                }

                _overlayFolderAsset = null;
                _overlayFolderPath = string.Empty;
                RefreshUsage();
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(folderAsset);
            if (string.IsNullOrEmpty(assetPath) || !AssetDatabase.IsValidFolder(assetPath))
            {
                EditorUtility.DisplayDialog("Overlay Folder Filter", "Please select a folder inside the project.", "OK");
                return;
            }

            if (_overlayFolderAsset == folderAsset && string.Equals(_overlayFolderPath, assetPath, System.StringComparison.Ordinal))
            {
                return;
            }

            _overlayFolderAsset = folderAsset;
            _overlayFolderPath = assetPath;
            RefreshUsage();
        }

        private void SetMaterialFilter(IList<UMAMaterial> materialFilter)
        {
            _materialFilter.Clear();
            if (materialFilter == null || materialFilter.Count == 0)
            {
                return;
            }

            HashSet<UMAMaterial> seen = new HashSet<UMAMaterial>();
            for (int i = 0; i < materialFilter.Count; i++)
            {
                UMAMaterial material = materialFilter[i];
                if (material == null || !seen.Add(material))
                {
                    continue;
                }

                _materialFilter.Add(material);
            }
        }

        private MaterialUsageRow GetOrCreateUsage(UMAMaterial material, Dictionary<UMAMaterial, MaterialUsageRow> usageByMaterial)
        {
            if (usageByMaterial.TryGetValue(material, out MaterialUsageRow usage))
            {
                return usage;
            }

            usage = new MaterialUsageRow { Material = material };
            usageByMaterial.Add(material, usage);
            _materials.Add(usage);
            return usage;
        }

        private static UMAMaterial ResolveOverlayMaterial(OverlayDataAsset overlay, Dictionary<string, UMAMaterial> materialByName)
        {
            if (overlay.material != null)
            {
                return overlay.material;
            }

            if (!string.IsNullOrEmpty(overlay.materialName) && materialByName.TryGetValue(overlay.materialName, out UMAMaterial material))
            {
                return material;
            }

            return null;
        }

        private static List<T> LoadAssets<T>(string rootFolder = null) where T : UnityEngine.Object
        {
            List<T> assets = new List<T>();
            string[] guids = string.IsNullOrEmpty(rootFolder)
                ? AssetDatabase.FindAssets("t:" + typeof(T).Name)
                : AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { rootFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    assets.Add(asset);
                }
            }

            assets.Sort(CompareAssetsByName);
            return assets;
        }

        private static List<UMAMaterial> GetSelectedMaterialsFromSelection()
        {
            UnityEngine.Object[] selectedObjects = Selection.GetFiltered(typeof(UMAMaterial), SelectionMode.Assets);
            List<UMAMaterial> materials = new List<UMAMaterial>(selectedObjects.Length);
            HashSet<UMAMaterial> seen = new HashSet<UMAMaterial>();
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                UMAMaterial material = selectedObjects[i] as UMAMaterial;
                if (material == null || !seen.Add(material))
                {
                    continue;
                }

                materials.Add(material);
            }

            materials.Sort(CompareAssetsByName);
            return materials;
        }

        private MaterialUsageRow GetSelectedUsage()
        {
            if (_selectedMaterialIndex < 0 || _selectedMaterialIndex >= _materials.Count)
            {
                return null;
            }

            return _materials[_selectedMaterialIndex];
        }

        private int FindMaterialIndex(UMAMaterial material)
        {
            if (material == null)
            {
                return -1;
            }

            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i].Material == material)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string GetUseStatus(MaterialUsageRow usage)
        {
            if (usage.UseCount == 0)
            {
                return "Unused";
            }

            return usage.UseCount + (usage.UseCount == 1 ? " use" : " uses");
        }

        private static int CompareMaterialRowsByName(MaterialUsageRow left, MaterialUsageRow right)
        {
            string leftName = left != null ? left.Name : string.Empty;
            string rightName = right != null ? right.Name : string.Empty;
            return string.Compare(leftName, rightName, true);
        }

        private static int CompareAssetsByName(UnityEngine.Object left, UnityEngine.Object right)
        {
            string leftName = left != null ? left.name : string.Empty;
            string rightName = right != null ? right.name : string.Empty;
            return string.Compare(leftName, rightName, true);
        }
    }
}
