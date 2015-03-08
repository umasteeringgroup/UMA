using System;
using UnityEditor;
using UMA.ReorderableList;
using System.Collections.Generic;
using UnityEngine;

[CustomPropertyDrawer(typeof(UMACrowdRandomSet.CrowdRaceData))]
public class CrowdRaceDataEditor : PropertyDrawer
{
	public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var innerEntriesProp = prop.FindPropertyRelative("slotElements");
		return ReorderableListGUI.CalculateListFieldHeight(innerEntriesProp) + 20;
	}

	public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
	{

		var height = position.height;
		var race = prop.FindPropertyRelative("raceID");
		position.height = 18;
		EditorGUI.PropertyField(position, race);
		position.y += 20;


		position.height = height - 20;
		var innerEntriesProp = prop.FindPropertyRelative("slotElements");
		ReorderableListGUI.ListFieldAbsolute(position, innerEntriesProp, ReorderableListFlags.DisableReordering);
	}
}

[CustomPropertyDrawer(typeof(UMACrowdRandomSet.CrowdSlotElement))]
public class CrowdSlotElementEditor : PropertyDrawer
{
	public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var innerEntriesProp = prop.FindPropertyRelative("possibleSlots");
		return ReorderableListGUI.CalculateListFieldHeight(innerEntriesProp) + 45;
	}

	public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
	{
		var height = position.height;
		position.height = 18;
		var requirement = prop.FindPropertyRelative("requirement");
		EditorGUI.PropertyField(position, requirement);
		position.y += 22;

		EditorGUI.LabelField(position, "Possible slots", LabelHelper.HeaderStyle);
		position.y += 18;
		position.height = height - 45;
		var possibleSlots = prop.FindPropertyRelative("possibleSlots");

		//ReorderableListGUI.Title("Possible Slots");
		ReorderableListGUI.ListFieldAbsolute(position, possibleSlots, (pos) =>
		{
			pos.height = 20;
			EditorGUI.LabelField(pos, "No slots");
		}, ReorderableListFlags.DisableReordering);
	}
}

[CustomPropertyDrawer(typeof(UMACrowdRandomSet.CrowdSlotData))]
public class CrowdSlotDataEditor : PropertyDrawer
{
	public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var innerEntriesProp = prop.FindPropertyRelative("overlayElements");
		return ReorderableListGUI.CalculateListFieldHeight(innerEntriesProp) + 65;
	}

	public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
	{
		var height = position.height;

		position.height = 18;
		var requirement = prop.FindPropertyRelative("slotID");
		EditorGUI.PropertyField(position, requirement);
		position.y += 20;

		var overlayListSource = prop.FindPropertyRelative("overlayListSource");
		EditorGUI.PropertyField(position, overlayListSource);
		position.y += 20;

		EditorGUI.LabelField(position, "Possible overlays", LabelHelper.HeaderStyle);
		position.y += 18;

		position.height = height - 65;

		var innerEntriesProp = prop.FindPropertyRelative("overlayElements");
		ReorderableListGUI.ListFieldAbsolute(position, innerEntriesProp, ReorderableListFlags.DisableReordering);
	}
}

[CustomPropertyDrawer(typeof(UMACrowdRandomSet.CrowdOverlayElement))]
public class OuterListEntryEditor : PropertyDrawer
{
	public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var innerEntriesProp = prop.FindPropertyRelative("possibleOverlays");
		return ReorderableListGUI.CalculateListFieldHeight(innerEntriesProp) + 25;
	}

	public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
	{
		var height = position.height;
		var innerEntriesProp = prop.FindPropertyRelative("possibleOverlays");
		position.y += 5;
		position.height = 18;
		EditorGUI.LabelField(position, "Overlay Data", LabelHelper.HeaderStyle);
		position.y += 18;
		position.height = height - 25;
		ReorderableListGUI.ListFieldAbsolute(position, innerEntriesProp, ReorderableListFlags.DisableReordering);
	}
}

[CustomPropertyDrawer(typeof(UMACrowdRandomSet.CrowdOverlayData))]
public class InnerListEntryEditor : PropertyDrawer
{
	public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
	{
		var overlayType = prop.FindPropertyRelative("overlayType");
		float size = 40f;
		CleanProperty(overlayType, prop);

		switch ((UMACrowdRandomSet.OverlayType)overlayType.enumValueIndex)
		{
			case UMACrowdRandomSet.OverlayType.Color:
				size += 40f;
				break;
			case UMACrowdRandomSet.OverlayType.Texture:
				size += 0;
				break;
			case UMACrowdRandomSet.OverlayType.Hair:
				size += 40;
				break;
			case UMACrowdRandomSet.OverlayType.Skin:
				size += 60;
				break;
			case UMACrowdRandomSet.OverlayType.Random:
				size += 60;
				break;
		}
		var colorChannelUse = prop.FindPropertyRelative("colorChannelUse");
		if (colorChannelUse.enumValueIndex != 0)
		{
			size += 20f;
		}
		return size;
	}

	public static void CleanProperty(SerializedProperty overlayType, SerializedProperty prop)
	{
		if ((UMACrowdRandomSet.OverlayType)overlayType.enumValueIndex == UMACrowdRandomSet.OverlayType.Unknown)
		{
			var useSkinColor = prop.FindPropertyRelative("useSkinColor");
			var useHairColor = prop.FindPropertyRelative("useHairColor");
			var minRGB = prop.FindPropertyRelative("minRGB");
			var maxRGB = prop.FindPropertyRelative("maxRGB");
			if (useSkinColor.boolValue)
			{
				overlayType.enumValueIndex = (int)UMACrowdRandomSet.OverlayType.Skin;
			}
			else if (useHairColor.boolValue)
			{
				overlayType.enumValueIndex = (int)UMACrowdRandomSet.OverlayType.Hair;
			}
			else
			{
				if (minRGB.colorValue == maxRGB.colorValue)
				{
					if (minRGB.colorValue == Color.white)
					{
						overlayType.enumValueIndex = (int)UMACrowdRandomSet.OverlayType.Texture;
					}
					else
					{
						overlayType.enumValueIndex = (int)UMACrowdRandomSet.OverlayType.Color;
					}
				}
				else
				{
					overlayType.enumValueIndex = (int)UMACrowdRandomSet.OverlayType.Random;
				}
			}
		}
	}

	public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
	{
		var width = position.width;
		var x = position.x;
		var labelWidth = 90;

		position.height = 16;

		var overlayId = prop.FindPropertyRelative("overlayID");
		EditorGUI.LabelField(position, "Id");
		position.width = labelWidth;
		position.x += labelWidth + 5;
		position.width = width - (labelWidth + 5);
		overlayId.stringValue = EditorGUI.TextField(position, overlayId.stringValue);

		position.y += 20;
		position.x = x;
		position.width = labelWidth;
		var overlayType = prop.FindPropertyRelative("overlayType");
		EditorGUI.LabelField(position, "Type");
		position.x += labelWidth + 5;
		position.width = width - (labelWidth + 5);
		overlayType.enumValueIndex =
			(int)(UMACrowdRandomSet.OverlayType)EditorGUI.EnumPopup(
				position,
				(UMACrowdRandomSet.OverlayType)Enum.Parse(typeof(UMACrowdRandomSet.OverlayType), overlayType.enumNames[overlayType.enumValueIndex]));

		var overlayTypeEnum = (UMACrowdRandomSet.OverlayType)overlayType.enumValueIndex;
		switch (overlayTypeEnum)
		{
			case UMACrowdRandomSet.OverlayType.Color:
			{
				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var minRGB = prop.FindPropertyRelative("minRGB");
				EditorGUI.LabelField(position, "Color");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				minRGB.colorValue = EditorGUI.ColorField(position, minRGB.colorValue);
				break;
			}
			case UMACrowdRandomSet.OverlayType.Texture:
			{
				break;
			}
			case UMACrowdRandomSet.OverlayType.Hair:
			{
				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var hairColorMultiplier = prop.FindPropertyRelative("hairColorMultiplier");
				EditorGUI.LabelField(position, "Hair Mult.");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				hairColorMultiplier.floatValue = EditorGUI.FloatField(position, hairColorMultiplier.floatValue);
				break;
			}
			case UMACrowdRandomSet.OverlayType.Skin:
			{
				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var minRGB = prop.FindPropertyRelative("minRGB");
				EditorGUI.LabelField(position, "Add Min RGB");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				minRGB.colorValue = EditorGUI.ColorField(position, minRGB.colorValue);

				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var maxRGB = prop.FindPropertyRelative("maxRGB");
				EditorGUI.LabelField(position, "Add Max RGB");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				maxRGB.colorValue = EditorGUI.ColorField(position, maxRGB.colorValue);
				break;
			}
			case UMACrowdRandomSet.OverlayType.Random:
			{
				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var minRGB = prop.FindPropertyRelative("minRGB");
				EditorGUI.LabelField(position, "Min RGB");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				minRGB.colorValue = EditorGUI.ColorField(position, minRGB.colorValue);

				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				var maxRGB = prop.FindPropertyRelative("maxRGB");
				EditorGUI.LabelField(position, "Max RGB");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				maxRGB.colorValue = EditorGUI.ColorField(position, maxRGB.colorValue);
				break;
			}
		}

		if (overlayTypeEnum != UMACrowdRandomSet.OverlayType.Texture)
		{
			position.y += 20;
			position.x = x;
			position.width = labelWidth;
			var colorChannelUse = prop.FindPropertyRelative("colorChannelUse");
			EditorGUI.LabelField(position, "Extra Channel");
			position.x += labelWidth + 5;
			position.width = width - (labelWidth + 5);
			colorChannelUse.enumValueIndex =
				(int)(UMACrowdRandomSet.ChannelUse)EditorGUI.EnumPopup(
					position,
					(UMACrowdRandomSet.ChannelUse)Enum.Parse(typeof(UMACrowdRandomSet.ChannelUse), colorChannelUse.enumNames[colorChannelUse.enumValueIndex]));

			if (colorChannelUse.enumValueIndex != 0)
			{
				position.y += 20;
				position.x = x;
				position.width = labelWidth;
				EditorGUI.LabelField(position, "Channel");
				position.x += labelWidth + 5;
				position.width = width - (labelWidth + 5);
				var colorChannel = prop.FindPropertyRelative("colorChannel");
				colorChannel.intValue = EditorGUI.IntField(position, colorChannel.intValue);
			}
		}
	}
}

static class LabelHelper
{
	public static GUIStyle HeaderStyle;

	static LabelHelper()
	{
		HeaderStyle = new GUIStyle();
		HeaderStyle.border = new RectOffset(2, 2, 2, 1);
		HeaderStyle.margin = new RectOffset(5, 5, 5, 0);
		HeaderStyle.padding = new RectOffset(5, 5, 0, 0);
		HeaderStyle.alignment = TextAnchor.MiddleLeft;
		HeaderStyle.normal.background = EditorGUIUtility.isProSkin
			? LoadTexture(s_DarkSkin)
				: LoadTexture(s_LightSkin);
		HeaderStyle.normal.textColor = EditorGUIUtility.isProSkin
			? new Color(0.8f, 0.8f, 0.8f)
				: new Color(0.2f, 0.2f, 0.2f);
	}

	static Texture2D LoadTexture(string textureData)
	{
		byte[] imageData = Convert.FromBase64String(textureData);

		// Gather image size from image data.
		int texWidth, texHeight;
		GetImageSize(imageData, out texWidth, out texHeight);

		// Generate texture asset.
		var tex = new Texture2D(texWidth, texHeight, TextureFormat.ARGB32, false);
		tex.hideFlags = HideFlags.HideAndDontSave;
		tex.name = "ReorderableList";
		tex.filterMode = FilterMode.Point;
		tex.LoadImage(imageData);

		return tex;
	}

	private static void GetImageSize(byte[] imageData, out int width, out int height)
	{
		width = ReadInt(imageData, 3 + 15);
		height = ReadInt(imageData, 3 + 15 + 2 + 2);
	}

	private static int ReadInt(byte[] imageData, int offset)
	{
		return (imageData[offset] << 8) | imageData[offset + 1];
	}

	/// <summary>
	/// Resource assets for light skin.
	/// </summary>
	/// <remarks>
	/// <para>Resource assets are PNG images which have been encoded using a base-64
	/// string so that actual asset files are not necessary.</para>
	/// </remarks>
	private static string s_LightSkin =
		"iVBORw0KGgoAAAANSUhEUgAAAAUAAAAECAYAAABGM/VAAAAAGXRFWHRTb2Z0d2FyZQBBZG9iZSBJbWFnZVJlYWR5ccllPAAAAEFJREFUeNpi/P//P0NxcfF/BgRgZP78+fN/VVVVhpCQEAZjY2OGs2fPNrCApBwdHRkePHgAVwoWnDVrFgMyAAgwAAt4E1dCq1obAAAAAElFTkSuQmCC";
	/// <summary>
	/// Resource assets for dark skin.
	/// </summary>
	/// <remarks>
	/// <para>Resource assets are PNG images which have been encoded using a base-64
	/// string so that actual asset files are not necessary.</para>
	/// </remarks>
	private static string s_DarkSkin =

		"iVBORw0KGgoAAAANSUhEUgAAAAUAAAAECAYAAABGM/VAAAAAGXRFWHRTb2Z0d2FyZQBBZG9iZSBJbWFnZVJlYWR5ccllPAAAADtJREFUeNpi/P//P4OKisp/Bii4c+cOIwtIQE9Pj+HLly9gQRCfBcQACbx69QqmmAEseO/ePQZkABBgAD04FXsmmijSAAAAAElFTkSuQmCC";
}
