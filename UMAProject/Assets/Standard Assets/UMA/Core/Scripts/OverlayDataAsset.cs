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

		public enum OverlayType
		{
			Normal = 0,
			Cutout = 1,
		}
		/// <summary>
		/// How should this overlay be processed.
		/// </summary>
		public OverlayType overlayType;
		/// <summary>
		/// Destination rectangle for drawing overlay textures.
		/// </summary>
		public Rect rect;
		/// <summary>
		/// Optional Alpha mask, if alpha mask is not set the texture[0].alpha is used instead.
		/// Using a alpha mask also allows you to write alpha values from the texture[0] to cut holes
		/// </summary>
		public Texture alphaMask;
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

		public Texture GetAlphaMask()
		{
			return alphaMask != null ? alphaMask : textureList[0];
		}
	}
}