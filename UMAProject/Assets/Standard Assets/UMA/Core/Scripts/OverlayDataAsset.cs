using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Contains the immutable data shared between overlays of the same type.
	/// </summary>
	[System.Serializable]
	public partial class OverlayDataAsset : ScriptableObject, ISerializationCallbackReceiver
	{
	    public string overlayName;
		[System.NonSerialized]
		public int nameHash;

		/// <summary>
		/// Destination rectangle for drawing overlay textures.
		/// </summary>
		public Rect rect;

		/// <summary>
		/// Array of textures required for the overlay material.
		/// </summary>
	    public Texture[] textureList;
        /// <summary>
        /// Use this to identify what kind of overlay this is and what it fits
        /// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
        /// </summary>
        public string[] tags;

		/// <summary>
		/// The UMA material.
		/// </summary>
		/// <remarks>
		/// The UMA material contains both a reference to the Unity material
		/// used for drawing and information needed for matching the textures
		/// and colors to the various material properties.
		/// </remarks>
		[UMAAssetFieldVisible]
		public UMAMaterial material;

		public OverlayDataAsset()
	    {

	    }

		public void OnAfterDeserialize()
		{
			nameHash = UMAUtils.StringToHash(overlayName);
		}
		public void OnBeforeSerialize()	{ }
	}
}