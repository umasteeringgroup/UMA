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
		public int channelCount = 3;
		
        public string sharedColorName = "New Shared Color Table";
        [Tooltip("Additional shared color names that can use this palette. Names are case-sensitive.")]
        public string[] AlsoAppliesTo = new string[0];
        [NonReorderable]
		public OverlayColorData[] colors = new OverlayColorData[0];

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
				}
			}
		}

		#endregion
	}
}
