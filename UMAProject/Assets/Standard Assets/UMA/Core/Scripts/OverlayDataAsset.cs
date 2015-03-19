﻿using UnityEngine;
using System.Collections;

namespace UMA
{
	[System.Serializable]
	public partial class OverlayDataAsset : ScriptableObject, ISerializationCallbackReceiver
	{
	    public string overlayName;
		[System.NonSerialized]
		public int nameHash;

	    public Rect rect;
	    public Texture[] textureList;
        /// <summary>
        /// Use this to identify what kind of overlay this is and what it fits
        /// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
        /// </summary>
        public string[] tags;

		public OverlayDataAsset()
	    {

	    }

		public void OnAfterDeserialize()
		{
			nameHash = UMASkeleton.StringToHash(overlayName);
		}
		public void OnBeforeSerialize()	{ }
	}
}