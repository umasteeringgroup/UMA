using UnityEngine;
using UnityEditor;
using System;
using UnityEditorInternal;

namespace UMA
{
	public class UMAMappedPropertyAssetInspector : UMAPropertyAssetInspector
	{
		private bool _showMappings;
		UMAMappedPropertyAsset asset { get { return target as UMAMappedPropertyAsset; } }

		string[] _destinationStrings;
		string[] _sourceStrings;
		ReorderableList propertyMappingROL;

		public override void RefreshCachedData()
		{
			asset.UpdateMappedProperties();

			_destinationStrings = new string[asset.DestinationProperties.Length + 1];
			_destinationStrings[0] = "None";
			for (int i = 0; i < asset.DestinationProperties.Length; i++)
			{
				_destinationStrings[i + 1] = asset.DestinationProperties[i].name;
			}

			_sourceStrings = new string[asset.Properties.Length + 1];
			_sourceStrings[0] = "None";
			for (int i = 0; i < asset.Properties.Length; i++)
			{
				_sourceStrings[i + 1] = asset.Properties[i].name;
			}

			propertyMappingROL = new UnityEditorInternal.ReorderableList(asset.Mappings, typeof(PropertyMapping));
			propertyMappingROL.drawElementCallback = propertyMapping_drawPropertyMapping;
			propertyMappingROL.drawElementBackgroundCallback = propertyMapping_drawElementBackground;
			propertyMappingROL.drawHeaderCallback = propertyMapping_drawHeader;
			propertyMappingROL.onAddCallback = propertyMapping_addCallBack;
			propertyMappingROL.onRemoveCallback = propertyMapping_removeCallBack;
		}


		protected override void OnEnable()
		{
			base.OnEnable();
			_showMappings = EditorPrefs.GetBool("UMAPieceInspector_ShowMappings", true);
			RefreshCachedData();
		}

		private void propertyMapping_addCallBack(ReorderableList list)
		{
			var newProperty = new PropertyMapping();
			newProperty.Dest = null;
			newProperty.Source = null;
			ArrayUtility.Insert(ref asset.Mappings, asset.Mappings.Length, newProperty);
			list.list = asset.Mappings;
		}

		private void propertyMapping_removeCallBack(ReorderableList list)
		{
			ArrayUtility.RemoveAt(ref asset.Mappings, list.index);
			list.list = asset.Mappings;
		}

		private void propertyMapping_drawHeader(Rect rect)
		{
			GUI.Label(rect, "Property Mappings");
		}

		private void propertyMapping_drawElementBackground(Rect rect, int index, bool isActive, bool isFocused)
		{
			if (Event.current.type != EventType.Repaint) return;

			if (alternateBackground == null)
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

		private void propertyMapping_drawPropertyMapping(Rect rect, int index, bool isActive, bool isFocused)
		{
			var mapping = asset.Mappings[index];

			rect.yMin += 3;

			var arrowRect = rect;
			arrowRect.xMin += (rect.width - 20) / 2;
			arrowRect.width = 40;

			var line1 = rect;
			line1.width = (rect.width - 40) / 2;

			var line2 = rect;
			line2.xMin += (rect.width + 40) / 2;


			int sourceIndex = mapping.Source == null ? 0 : Array.IndexOf(asset.Properties, mapping.Source) + 1;
			int destIndex = mapping.Dest == null ? 0 : Array.IndexOf(asset.DestinationProperties, mapping.Dest) + 1;

			EditorGUI.LabelField(arrowRect, "->");
			var newSourceIndex = EditorGUI.Popup(line1, sourceIndex, _sourceStrings);
			var newDestIndex = EditorGUI.Popup(line2, destIndex, _destinationStrings);

			if (newSourceIndex != sourceIndex)
			{
				mapping.Source = newSourceIndex == 0 ? null : asset.Properties[newSourceIndex - 1];
			}
			if (newDestIndex != destIndex)
			{
				mapping.Dest = newDestIndex == 0 ? null : asset.DestinationProperties[newDestIndex - 1];
			}
			asset.Mappings[index] = mapping;
		}

		private bool ShowMappings
		{
			get
			{
				return _showMappings;
			}
			set
			{
				if (value != _showMappings)
				{
					_showMappings = value;
					EditorPrefs.SetBool("UMAPieceInspector_ShowMappings", value);
				}
			}
		}

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			if (asset.Mappings == null)
			{
				asset.Mappings = new PropertyMapping[0];
			}

			ShowMappings = EditorGUILayout.Foldout(ShowMappings, "Mappings");
			if (ShowMappings)
			{
				propertyMappingROL.list = asset.Mappings;
				propertyMappingROL.DoLayoutList();
			}
		}

		private void DrawMapping(PropertyMapping mapping, int mappingIndex)
		{
			EditorGUI.BeginChangeCheck();

			int sourceIndex = mapping.Source == null ? 0 : Array.IndexOf(asset.Properties, mapping.Source) + 1;
			int destIndex = mapping.Dest == null ? 0 : Array.IndexOf(asset.DestinationProperties, mapping.Dest) + 1;

			var newSourceIndex = EditorGUILayout.Popup("Source", sourceIndex, _sourceStrings);
			var newDestIndex = EditorGUILayout.Popup("Destination", destIndex, _destinationStrings);

			if (newSourceIndex != sourceIndex)
			{
				mapping.Source = newSourceIndex == 0 ? null : asset.Properties[newSourceIndex - 1];
			}
			if (newDestIndex != destIndex)
			{
				mapping.Dest = newDestIndex == 0 ? null : asset.DestinationProperties[newDestIndex - 1];
			}
			if (GUILayout.Button("-", GUILayout.Width(15), GUILayout.Height(15)))
			{
				ArrayUtility.RemoveAt(ref asset.Mappings, mappingIndex);
			}
		}
	}
}