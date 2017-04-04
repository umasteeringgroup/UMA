using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	[System.Serializable]
	public class SharedColorTable : ScriptableObject, ISerializationCallbackReceiver
	{
	#if UNITY_EDITOR
		[MenuItem("Assets/Create/UMA/Core/Shared Color List")]
		public static void CreateSharedColor()
		{
			UMA.CustomAssetUtility.CreateAsset<SharedColorTable>();
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
}
