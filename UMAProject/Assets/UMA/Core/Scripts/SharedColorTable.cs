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
		[Tooltip("If true, all colors will have the same name, copied from sharedColorName")]
        public bool copyColorName = true;
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
                for (int i = 0; i < colors.Length; i++)
				{
                    OverlayColorData color = colors[i];
                    color.EnsureChannelsExact(channelCount);
					if (copyColorName && !string.IsNullOrEmpty(sharedColorName))
					{
						color.name = sharedColorName;
					}
				}
			}
		}

		#endregion
	}
}
