using UnityEngine;
using UnityEditor;
using System;
using UnityEditorInternal;

namespace UMA
{
	public class UMAPropertyAssetInspector : Editor
	{
		private bool _showProperties;
		UMAPropertyAsset asset { get { return target as UMAPropertyAsset; } }
		ReorderableList propertiesROL;
		protected GUIStyle alternateBackground;
		protected bool showBaseInspector;

		public virtual void RefreshCachedData()
		{
		}

		protected virtual void OnEnable()
		{
			asset.RecordState();
			_showProperties = EditorPrefs.GetBool("UMAPieceInspector_ShowProperties", true);

			propertiesROL = new ReorderableList(asset.Properties, typeof(BasePieceProperty));
			propertiesROL.drawElementCallback = ROL_properties_drawPropertyMapping;
			propertiesROL.drawElementBackgroundCallback = ROL_AlternatingBackground;
			propertiesROL.drawHeaderCallback = ROL_properties_drawHeader;
			propertiesROL.onAddCallback = ROL_properties_addCallBack;
			propertiesROL.onRemoveCallback = ROL_properties_removeCallBack;
			propertiesROL.elementHeightCallback = ROL_properties_heightCallback;

			if (asset.Properties == null)
			{
				asset.Properties = new BasePieceProperty[0];
				EditorUtility.SetDirty(asset);
			}
		}

		private float ROL_properties_heightCallback(int index)
		{
			return CalculateElementHeight(3, asset.Properties[index].GetInspectorHeight());
		}


		private void ROL_properties_addCallBack(ReorderableList list)
		{
			var newProperty = BasePieceProperty.CreateProperty(BaseProperty.PropertyTypes[0], asset);
			newProperty.propertyName ="Added";
			ArrayUtility.Insert(ref asset.Properties, asset.Properties.Length, newProperty);
			list.list = asset.Properties;
		}

		private void ROL_properties_removeCallBack(ReorderableList list)
		{
			asset.Properties[list.index].DestroyImmediate();
			ArrayUtility.RemoveAt(ref asset.Properties, list.index);
			list.list = asset.Properties;
		}

		private void ROL_properties_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Properties");
		}

		private void ROL_properties_drawPropertyMapping(Rect uRect, int index, bool isActive, bool isFocused)
		{
			var rect = new InspectorRect(uRect);
			var property = asset.Properties[index];

			var nameRect = rect.GetLineRect();

			var newName = EditorGUI.TextField(nameRect, "Name", property.propertyName);
			if (newName != property.propertyName)
			{
				property.propertyName = newName;
				property.data.name = newName+"_data";
			}

			property.propertyType = (PropertyType)EditorGUI.EnumPopup(rect.GetLineRect(), "Input Type", property.propertyType);

			EditorGUI.BeginChangeCheck();
			var propertyType = UMAEditorGUILayout.PropertyTypeField(rect.GetLineRect(), "Value Type", property.GetPropertyType());
			if (EditorGUI.EndChangeCheck())
			{
				property.ChangePropertyDataType(propertyType);
			}
			asset.Properties[index].DrawInspectorProperties(rect, isActive, isFocused);
		}

		protected void ROL_AlternatingBackground(Rect rect, int index, bool isActive, bool isFocused)
		{
			if (Event.current.type != EventType.Repaint) return;

			if (alternateBackground == null || alternateBackground.normal.background == null)
			{
				alternateBackground = new GUIStyle("RL Element");
				var background = new Texture2D(4, 4, TextureFormat.ARGB32, false);
				var colors = new Color32[16];
				var color = new Color32(196, 196, 196, 255);
				for (int i = 0; i < 16; i++)
					colors[i] = color;
				background.SetPixels32(colors);
				alternateBackground.normal.background = background;
			}

			if ((index & 1) == 1)
			{
				alternateBackground.Draw(rect, false, isActive, isActive, isFocused);
			}
			else
			{
				ReorderableList.defaultBehaviours.elementBackground.Draw(rect, false, isActive, isActive, isFocused);
			}
		}

		private bool ShowProperties
		{
			get
			{
				return _showProperties;
			}
			set
			{
				if (value != _showProperties)
				{
					_showProperties = value;
					EditorPrefs.SetBool("UMAPieceInspector_ShowProperties", value);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			ShowProperties = EditorGUILayout.Foldout(ShowProperties, "Properties");
			if (ShowProperties)
			{
				propertiesROL.list = asset.Properties;
				propertiesROL.DoLayoutList();
			}
			if (showBaseInspector)
			{
				base.OnInspectorGUI();
			}
		}

		public static float CalculateElementHeight(int lines, float custom = 0)
		{
			return InspectableAsset.CalculateElementHeight(lines, custom);
		}

		public static void DrawScriptableObject(Rect uRect, ScriptableObject so)
		{
			var rect = new InspectorRect(uRect);
			var serialized = new SerializedObject(so);
			var iterator = serialized.GetIterator();
			iterator.NextVisible(true);
			GUI.Label(rect.GetLineRect(), iterator.objectReferenceValue.name);
			iterator.NextVisible(true);
			while (iterator.NextVisible(true))
			{
				if (iterator.propertyType == SerializedPropertyType.Generic)
				{
					continue;
				}
				var height = EditorGUI.GetPropertyHeight(iterator, false);
				EditorGUI.PropertyField(rect.GetLineRect(height), iterator, false);
			}
		}

		public static void DrawScriptableObject(ScriptableObject so)
		{
			Editor editor = Editor.CreateEditor(so);
			editor.OnInspectorGUI();
		}

		public static void AddScriptableObjectToAsset(ScriptableObject asset, ScriptableObject so)
		{
			AssetDatabase.AddObjectToAsset(so, AssetDatabase.GetAssetPath(asset));
		}
	}
}