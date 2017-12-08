using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	/// <summary>
	/// Serializable dictionary for race assets
	/// </summary>
	[CustomPropertyDrawer(typeof(RaceAssetDictionary),true)]
	public class RaceAssetDictionaryPropertyDrawer : SerializedDictionaryPropertyDrawer { }

	/// <summary>
	/// Serializable dictionary for slot assets
	/// </summary>
	[CustomPropertyDrawer(typeof(SlotAssetDictionary),true)]
	public class SlotAssetDictionaryPropertyDrawer : SerializedDictionaryPropertyDrawer { }

	/// <summary>
	/// Serializable dictionary for overlay assets
	/// </summary>
	[CustomPropertyDrawer(typeof(OverlayAssetDictionary),true)]
	public class OverlayAssetDictionaryPropertyDrawer : SerializedDictionaryPropertyDrawer { }

	/// <summary>
	/// Serializable dictionary for DNA assets
	/// </summary>
	[CustomPropertyDrawer(typeof(DNAAssetDictionary),true)]
	public class DNAAssetDictionaryPropertyDrawer : SerializedDictionaryPropertyDrawer { }

	/// <summary>
	/// Serializable dictionary for occlusion assets
	/// </summary>
	[CustomPropertyDrawer(typeof(OcclusionAssetDictionary),true)]
	public class OcclusionAssetDictionaryPropertyDrawer : SerializedDictionaryPropertyDrawer { }

	public class SerializedDictionaryPropertyDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.BeginProperty(position, label, property);

			Rect propPosition = position;
			propPosition.height = EditorGUIUtility.singleLineHeight;
			EditorGUI.PropertyField(propPosition, property);
			position.y += EditorGUIUtility.singleLineHeight;

			if (property.isExpanded)
			{
				SerializedProperty keys = property.FindPropertyRelative("keys");
				SerializedProperty values = property.FindPropertyRelative("values");
				if ((keys != null) && (values != null))
				{
					EditorGUI.indentLevel++;
					Rect sizePosition = EditorGUI.PrefixLabel(position, new GUIContent("Size"));
					EditorGUI.LabelField(sizePosition, values.arraySize.ToString());
					position.y += EditorGUIUtility.singleLineHeight;

					Rect itemPosition = position;
					itemPosition.height = GetEntryHeight();
					Rect buttonPosition = itemPosition;
					buttonPosition.width = 18;
					itemPosition.width -= buttonPosition.width;
					buttonPosition.x += itemPosition.width;
					itemPosition.width -= 4;

					for (int i = 0; i < keys.arraySize; i++)
					{

						SerializedProperty key = keys.GetArrayElementAtIndex(i);
						SerializedProperty value = values.GetArrayElementAtIndex(i);
						DrawEntry(itemPosition, key, value);

						if (GUI.Button(buttonPosition, "\u2212", EditorStyles.miniButton))
						{
							/// <remarks>
							/// The serialization and deserialization happens between these two calls making it
							/// impossible to keep the two lists synchronized using DeleteArrayElementAtIndex()
							/// </remarks>
							// keys.DeleteArrayElementAtIndex(i);
							// values.DeleteArrayElementAtIndex(i);
							SerializedProperty deleteIndex = property.FindPropertyRelative("deleteIndex");
							if (deleteIndex != null)
							{
								deleteIndex.intValue = i;
							}
						}

						itemPosition.y += itemPosition.height + EditorGUIUtility.standardVerticalSpacing;
						buttonPosition.y += buttonPosition.height + EditorGUIUtility.standardVerticalSpacing;
					}
					position.y = itemPosition.y;
					EditorGUI.indentLevel--;
				}
			}

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			if (property.isExpanded)
			{
				float entriesSize = EditorGUIUtility.singleLineHeight * 2;

				SerializedProperty values = property.FindPropertyRelative("values");
				if (values != null)
				{
					entriesSize += values.arraySize * (GetEntryHeight() + EditorGUIUtility.standardVerticalSpacing);
				}

				return  entriesSize;
			}
			else
			{
				return EditorGUIUtility.singleLineHeight;
			}
		}
	
		/// <summary>
		/// Gets the height of a single dictionary entry (with padding).
		/// </summary>
		/// <returns>The entry height.</returns>
		public virtual float GetEntryHeight()
		{
			return EditorGUIUtility.singleLineHeight;
		}

		/// <summary>
		/// Draws a single key value pair from the dictionary.
		/// </summary>
		/// <param name="position">Rectangle where the entry will be drawn.</param>
		/// <param name="key">Dictionary key.</param>
		/// <param name="value">Dictionary value.</param>
		/// <remarks>
		/// UMA dictionaries generally use hash as a key, so default 
		/// is to only draw just the value, and make it read only
		/// </remarks>
		public virtual void DrawEntry(Rect position, SerializedProperty key, SerializedProperty value)
		{
			EditorGUI.BeginDisabledGroup(true);
			EditorGUI.ObjectField(position, GUIContent.none, value.objectReferenceValue, value.objectReferenceValue.GetType(), false);
			EditorGUI.EndDisabledGroup();
		}
	}
}
