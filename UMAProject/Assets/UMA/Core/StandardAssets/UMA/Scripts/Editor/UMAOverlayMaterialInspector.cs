using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using UnityEditorInternal;
using System;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAOverlayMaterial))]
	public class UMAOverlayMaterialInspector : UMAPropertyAssetInspector
	{
		private Shader _lastSelectedShader;
		private string[] _shaderProperties;
		ReorderableList textureChannelsROL;
		string[] _propertyStrings;
		private bool _showChannels;
		private bool ShowChannels
		{
			get
			{
				return _showChannels;
			}
			set
			{
				if (value != _showChannels)
				{
					_showChannels = value;
					EditorPrefs.SetBool("UMAOverlayMaterialInspector_ShowChannels", value);
				}
			}
		}

		private UMAOverlayMaterial asset { get { return target as UMAOverlayMaterial; } }

		protected override void OnEnable()
		{
			base.OnEnable();
			asset.UpdateDestinationProperties();

			_showChannels = EditorPrefs.GetBool("UMAOverlayMaterialInspector_ShowChannels", true);

			textureChannelsROL = new ReorderableList(new UMAOverlayMaterial.MaterialChannel[0], typeof(UMAOverlayMaterial.MaterialChannel));
			textureChannelsROL.drawElementBackgroundCallback = ROL_AlternatingBackground;
			textureChannelsROL.drawElementCallback = ROL_channels_drawElement;
			textureChannelsROL.drawHeaderCallback = ROL_channels_drawHeader;
			textureChannelsROL.elementHeightCallback = ROL_channels_heightCallback;
			textureChannelsROL.displayAdd = false;
			textureChannelsROL.displayRemove = false;
			textureChannelsROL.draggable = false;
		}

		public override void OnInspectorGUI()
		{
			if (_propertyStrings == null || _propertyStrings.Length != asset.Properties.Length + 1)
				_propertyStrings = new string[asset.Properties.Length + 1];
			_propertyStrings[0] = "None";
			for (int i = 0; i < asset.Properties.Length; i++)
			{
				_propertyStrings[i + 1] = asset.Properties[i].propertyName;
			}

			EditorGUI.BeginChangeCheck();


			asset.atlas = EditorGUILayout.ObjectField(new GUIContent("Atlas", "The Unity Atlas Material where these overlays ends up."), asset.atlas, typeof(UMAAtlasMaterial), false) as UMAAtlasMaterial;
			asset.RequireSeperateRenderer = EditorGUILayout.ToggleLeft(new GUIContent("Require Seperate Renderer", "This material will always be combined into it's own atlas."), asset.RequireSeperateRenderer);
			asset.materialType = (UMAOverlayMaterial.MaterialType)EditorGUILayout.EnumPopup("Material Type", asset.materialType);

			base.OnInspectorGUI();

			DrawChannelList(asset.channels);

			if (EditorGUI.EndChangeCheck())
			{
				asset.UpdateDestinationProperties();
				EditorUtility.SetDirty(target);
			}
		}

		//Maybe eventually we can use the new IMGUI classes once older unity version are no longer supported.
		private void DrawChannelList(UMAOverlayMaterial.MaterialChannel[] list)
		{
			ShowChannels= EditorGUILayout.Foldout(ShowChannels, new GUIContent("Texture Channels", "List of texture channels to be used in this material."));
			if (ShowChannels)
			{
				textureChannelsROL.list = list;
				textureChannelsROL.DoLayoutList();
			}


			//	EditorGUILayout.PropertyField(list.FindPropertyRelative("Array.size"));
			//	for (int i = 0; i < list.arraySize; i++)
			//	{
			//		SerializedProperty channel = list.GetArrayElementAtIndex(i);
			//		SerializedProperty materialPropertyName = channel.FindPropertyRelative("materialPropertyName");//Let's get this eary to be able to use it in the element header.
			//		EditorGUILayout.PropertyField(materialPropertyName, new GUIContent("Channel " + i + ": " + materialPropertyName.stringValue));
			//		EditorGUI.indentLevel += 1;
			//		if (channel.isExpanded)
			//		{
			//			EditorGUILayout.PropertyField(channel.FindPropertyRelative("channelType"), new GUIContent("Channel Type", "The channel type. Affects the texture atlassing process."));
			//			EditorGUILayout.PropertyField(channel.FindPropertyRelative("textureFormat"), new GUIContent("Texture Format", "Format used for the texture in this channel."));

			//			EditorGUILayout.BeginHorizontal();

			//			EditorGUILayout.PropertyField(materialPropertyName, new GUIContent("Material Property Name", "The name of the property this texture corresponds to in the shader used by this material."), GUILayout.MinWidth(300));
			//			if (_shaderProperties != null)
			//			{
			//				int selection = EditorGUILayout.Popup(0, _shaderProperties, GUILayout.MinWidth(100), GUILayout.MaxWidth(200));
			//				if (selection > 0)
			//					materialPropertyName.stringValue = _shaderProperties[selection];

			//			}
			//			EditorGUILayout.EndHorizontal();

			//			UMAMaterial source = target as UMAMaterial;
			//			if (source.material != null)
			//			{
			//				if (!source.material.HasProperty(materialPropertyName.stringValue))
			//					EditorGUILayout.HelpBox("This name is not found in the shader! Are you sure it is correct?", MessageType.Warning);
			//			}

			//			EditorGUILayout.PropertyField(channel.FindPropertyRelative("sourceTextureName"), new GUIContent("Source Texture Name", "For use with procedural materials, leave empty otherwise."));
			//		}
			//		EditorGUI.indentLevel -= 1;
			//	}
			//	EditorGUI.indentLevel -= 1;
			//}
		}

		private static string[] FindTexProperties(Shader shader)
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

		private float ROL_channels_heightCallback(int index)
		{
			int lines = 2;
			var channel = asset.channels[index];
			if (channel.combiner != null)
			{
				var publicProperties = channel.combiner.GetPublicPropertyCount();
				if (publicProperties > 0)
				{
					lines += publicProperties + 1;
				}
			}
			return InspectableAsset.CalculateElementHeight(lines, channel.target.GetInspectorHeight()+EditorGUIUtility.standardVerticalSpacing);
		}

		private void ROL_channels_drawElement(Rect uRect, int index, bool isActive, bool isFocused)
		{
			var channel = asset.channels[index];
			var rect = new InspectorRect(uRect);

			EditorGUI.LabelField(rect.GetLineRect(), "Atlas property", channel.target.propertyName);
			channel.target.DrawInspectorProperties(new InspectorRect(rect.GetLineRect(channel.target.GetInspectorHeight())), isActive, isFocused);
			
			channel.combiner = EditorGUI.ObjectField(rect.GetLineRect(), "Channel Combiner", channel.combiner, typeof(UMATextureChannelCombiner), false) as UMATextureChannelCombiner;

			if (channel.combiner != null)
			{
				var publicProperties = channel.combiner.GetPublicPropertyCount();
				if (publicProperties > 0)
				{
					var properties = new BasePieceProperty[publicProperties];
					channel.combiner.GetPublicProperties(properties);

					EditorGUI.LabelField(rect.GetLineRect(), "Combiner Property Mappings");

					EditorGUI.indentLevel++;
					for (int i = 0; i < publicProperties; i++)
					{
						var destProperty = properties[i];
						int propertyIndex = 0;
						int mapIndex = 0;
						for (int j = 0; j < channel.properties.Length; j++)
						{
							if (channel.properties[j].Dest == destProperty)
							{
								propertyIndex = Array.IndexOf(_propertyStrings, channel.properties[j].Source.propertyName);
								mapIndex = j;
								break;
							}
						}

						var newPropertyIndex = EditorGUI.Popup(rect.GetLineRect(), destProperty.propertyName, propertyIndex, _propertyStrings);
						if (propertyIndex != newPropertyIndex)
						{
							if (propertyIndex == 0)
							{
								// add
								if (destProperty.GetValue().CanSetValueFrom(asset.Properties[newPropertyIndex - 1].GetValue()))
								{
									ArrayUtility.Add(ref channel.properties, new PropertyMapping() { Source = asset.Properties[newPropertyIndex - 1], Dest = destProperty });
								}
							}
							else if (newPropertyIndex == 0)
							{
								// remove
								ArrayUtility.RemoveAt(ref channel.properties, mapIndex);
							}
							else
							{
								// modify
								if (destProperty.GetValue().CanSetValueFrom(asset.Properties[newPropertyIndex - 1].GetValue()))
								{
									channel.properties[mapIndex].Source = asset.Properties[newPropertyIndex - 1];
								}
							}
						}
					}
					EditorGUI.indentLevel--;
				}
			}
		}

		private void ROL_channels_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Mappings");
		}
	}
}
