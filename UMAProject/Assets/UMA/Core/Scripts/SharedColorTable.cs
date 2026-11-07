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

        [Tooltip("The palette entry to use as the default. -1 means no default is selected.")]
        public int defaultColorIndex = -1;

        /// <summary>The selected entry in colors, or null when no valid default is selected.</summary>
        public OverlayColorData DefaultColor => colors != null && defaultColorIndex >= 0 &&
            defaultColorIndex < colors.Length ? colors[defaultColorIndex] : null;

        #region ISerializationCallbackReceiver Members

        public void OnAfterDeserialize()
		{
		}

		public void OnBeforeSerialize()
		{
			if (DefaultColor == null) defaultColorIndex = -1;
			if (colors != null)
			{
                for (int i = 0; i < colors.Length; i++)
				{
                    OverlayColorData color = colors[i];
                    color?.EnsureChannelsExact(channelCount);
				}
			}
		}

		#endregion
	}
}
