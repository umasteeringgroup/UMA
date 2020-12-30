using UnityEngine;
using UnityEditor;
using System.Linq;
using UMA;
using UMA.CharacterSystem;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections;

namespace UMA.Editors
{
	[CustomPropertyDrawer(typeof(OverlayColorData),true)]
	public class OverlayColorDataPropertyDrawer : PropertyDrawer
	{
		bool showAdvanced;
		GUIContent Modulate = new GUIContent("Multiplier");
		GUIContent Additive = new GUIContent("Additive");
		GUIContent Channels = new GUIContent("Channel Count");


		public static object GetDeepPropertyValue(object src, string propName)
		{
			if (propName.Contains('.'))
			{
				string[] Split = propName.Split('.');
				string RemainingProperty = propName.Substring(propName.IndexOf('.') + 1);
				return GetDeepPropertyValue(src.GetType().GetProperty(Split[0]).GetValue(src, null), RemainingProperty);
			}
			else
				return src.GetType().GetProperty(propName).GetValue(src, null);
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			OverlayColorData ocd = null;
			DynamicCharacterAvatar dca = property.serializedObject.targetObject as DynamicCharacterAvatar;


            ocd = property.GetValue<OverlayColorData>();
			if (ocd == null && dca != null)
			{
				string Name = property.FindPropertyRelative("name").stringValue;
				foreach( OverlayColorData o in dca.characterColors._colors)
				{
					if (o.name == Name)
					{
						ocd = o;
					}
				}
			}

			EditorGUI.BeginProperty(position, label, property);
			var name = property.FindPropertyRelative("name");
			var mask = property.FindPropertyRelative("channelMask");
			var additive = property.FindPropertyRelative("channelAdditiveMask");
			var propblock = property.FindPropertyRelative("propertyBlock");
			EditorGUILayout.BeginHorizontal();
			name.isExpanded = EditorGUILayout.Foldout(name.isExpanded, label);
			if (!name.isExpanded)
				name.stringValue = EditorGUILayout.TextField(new GUIContent(""), name.stringValue);
			EditorGUILayout.EndHorizontal();
			if (name.isExpanded)
			{
				EditorGUILayout.PropertyField(property.FindPropertyRelative("name"));

				if (ocd != null)
				{
					string Name = property.FindPropertyRelative("name").stringValue;
					int ChannelCount = EditorGUILayout.IntSlider(Channels, ocd.channelCount, 0, 16);
					if (ChannelCount != ocd.channelCount)
					{
						ocd.SetChannels(ChannelCount);
						EditorUtility.SetDirty(dca);
					}
				}

				showAdvanced = EditorGUILayout.Toggle("Show Extended Ranges", showAdvanced);

				GUILayout.Space(5);

				for (int i = 0; i < mask.arraySize; i++)
				{
					if (showAdvanced)
					{
						var channelMask = mask.GetArrayElementAtIndex(i);
						var channelColor = ToVector4(channelMask.colorValue);
						var newchannelColor = EditorGUILayout.Vector4Field("Multiplier (" + i + ")", channelColor);
						if (channelColor != newchannelColor)
						{
							channelMask.colorValue = ToColor(newchannelColor);
						}

						var AdditiveMask = additive.GetArrayElementAtIndex(i);
						var AdditiveColor = ToVector4(AdditiveMask.colorValue);
						var newAdditiveColor = EditorGUILayout.Vector4Field("Additive (" + i + ")", AdditiveColor);
						if (newAdditiveColor != AdditiveColor)
						{
							AdditiveMask.colorValue = ToColor(newAdditiveColor);
						}
					}
					else
					{
						Modulate.text = "Multiplier (" + i + ")";
						EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(i), Modulate);
						Additive.text = "Additive (" + i + ")";
						EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(i), Additive);
					}
					GUILayout.Space(5);
				}
				if (ocd != null)
                {
					if (ocd.PropertyBlock != null)
                    {
						if (UMAMaterialPropertyBlockDrawer.OnGUI(ocd.PropertyBlock))
                        {
							EditorUtility.SetDirty(dca);
							AssetDatabase.SaveAssets();
                        }
					}
					else
                    {
						if (GUILayout.Button("Add Properties Block"))
                        {
							ocd.PropertyBlock = new UMAMaterialPropertyBlock();
							EditorUtility.SetDirty(dca);
							AssetDatabase.SaveAssets();
							property.serializedObject.Update();
						}
					}
				}
			}
			else
			{
					if (mask.arraySize > 0)
					{
						EditorGUILayout.PropertyField(mask.GetArrayElementAtIndex(0), new GUIContent("BaseColor"));
						if (additive.arraySize >= 3)
							EditorGUILayout.PropertyField(additive.GetArrayElementAtIndex(2), new GUIContent("Metallic/Gloss", "Color is metallicness (Black is not metallic), Alpha is glossiness (Black is not glossy)"));
						else
						{
							//color didn't have a metallic gloss channel so show button to add one?
						}
					}
				//	if (ocd.HasProperties)
				//	{
				//		EditorGUILayout.LabelField("Has Properties");
				//	}
			}
			EditorGUILayout.Space();
			EditorGUI.EndProperty();
		}
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			/*var name = property.FindPropertyRelative("name");
			if (!name.isExpanded)
			{
				return (EditorGUIUtility.singleLineHeight * 3f) - 2f;
			}*/
			return -2f;
		}


		private Color ToColor(Vector4 colorVector)
		{
			return new Color(colorVector.x, colorVector.y, colorVector.z, colorVector.w);
		}

		private Vector4 ToVector4(Color color)
		{
			return new Vector4(color.r, color.g, color.b, color.a);
		}
	}
	public class PropertyDrawerUtility
	{
		public static OverlayColorData GetOverlayDataAsset(System.Reflection.FieldInfo fieldInfo, SerializedProperty property)
		{ 
			DynamicCharacterAvatar dca = property.serializedObject.targetObject as DynamicCharacterAvatar;


			return new OverlayColorData();
			/* T actualObject = null;
			if (obj.GetType().IsArray)
			{
				var index = System.Convert.ToInt32(new string(property.propertyPath.Where(c => char.IsDigit(c)).ToArray()));
				actualObject = ((T[])obj)[index];
			}
			else
			{
				actualObject = obj as T;
			}
			
			return actualObject; */
		}
	}
}
