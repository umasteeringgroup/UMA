using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif
using System.Collections;
using System.Collections.Generic;
using UMA;

[System.Serializable]
public class SharedColorTable : ScriptableObject, ISerializationCallbackReceiver
{
#if UNITY_EDITOR
	[MenuItem("Assets/Create/Shared Color List")]
	public static void CreateSharedColor()
	{
		UMAEditor.CustomAssetUtility.CreateAsset<SharedColorTable>();
	}
#endif
	public int channelCount;
	public string sharedColorName;
	public OverlayColorData[] colors;

	#region ISerializationCallbackReceiver Members

	public void OnAfterDeserialize()
	{
	}

	public void OnBeforeSerialize()
	{
		if (colors != null)
		{
			foreach (var color in colors)
			{
				color.EnsureChannels(channelCount);
				color.name = sharedColorName;
			}
		}
	}

	#endregion
}
