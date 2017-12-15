using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	public abstract class UMAPropertyAsset : ScriptableObject
	{
		public BasePieceProperty[] Properties = new BasePieceProperty[0];

#if UNITY_EDITOR
		public int GetPublicPropertyCount()
		{
			int publicProperties = 0;
			for (int i = 0; i < Properties.Length; i++)
			{
				if (Properties[i].propertyType != BasePieceProperty.PropertyType.Constant)
					publicProperties++;
			}
			return publicProperties;
		}

		public int GetPublicProperties(IList<BasePieceProperty> properties)
		{
			int publicProperties = 0;
			for (int i = 0; i < Properties.Length; i++)
			{
				if (Properties[i].propertyType != BasePieceProperty.PropertyType.Constant)
				{
					properties[i] = Properties[i];
					publicProperties++;
				}
			}
			return publicProperties;
		}


		public virtual void RecordState() { }
		public virtual void OnChange() { popupStrings = null; }

		[NonSerialized]
		public string[] popupStrings;
		public BasePieceProperty PropertyGUIPopup(Rect rect, string title, BasePieceProperty value)
		{
			var oldIndex = Array.IndexOf(Properties, value) + 1;
			
			if (popupStrings == null)
			{
				popupStrings = new string[Properties.Length+1];
				popupStrings[0] = "None";
			}
			for (int i = 0; i < Properties.Length; i++)
			{
				popupStrings[i+1] = Properties[i].name;
			}
			var newIndex = UnityEditor.EditorGUI.Popup(rect, title, oldIndex, popupStrings);
			if (newIndex != oldIndex)
				return newIndex == 0 ? null : Properties[newIndex-1];			
			
			return value;
		}

		public void AddScriptableObjectToAsset(ScriptableObject so)
		{
			UnityEditor.AssetDatabase.AddObjectToAsset(so, UnityEditor.AssetDatabase.GetAssetPath(this));
		}

#endif
	}
}
