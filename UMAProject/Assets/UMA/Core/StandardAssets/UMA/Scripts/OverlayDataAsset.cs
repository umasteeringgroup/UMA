using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditorInternal;
#endif


namespace UMA
{
    /// <summary>
    /// Contains the immutable data shared between overlays of the same type.
    /// </summary>
    [PreferBinarySerialization]
	[System.Serializable]
	public partial class OverlayDataAsset : ScriptableObject, IUMAIndexOptions, INameProvider
    {

		[FormerlySerializedAs("overlayName")]
		public string _oldOverlayName;
		[Tooltip("The name of this overlay.")]
		public string overlayName
		{
			get
			{
				if (!string.IsNullOrEmpty(_oldOverlayName))
				{
					return _oldOverlayName;
				}
				return this.name;
            }
		}

		private int _nameHash = 0;
        public int nameHash
		{
			get
			{
				if (_nameHash == 0)
				{
					_nameHash = UMAUtils.StringToHash(overlayName);
				}
				return _nameHash;
			}
		}

#if UNITY_EDITOR
		public float lastActionTime { get; set; } = 0;
		public bool doSave { get; set; } = false;
		public bool additionalFoldout { get; set; } = false;
		public bool textureFoldout { get; set; } = false;
		public bool tagsFoldout { get; set; } = false;
		public bool occlusionFoldout { get; set; } = false;

		[System.NonSerialized]
		public ReorderableList tagsList;
#endif
		public enum OverlayType
		{
			Normal = 0,
			Cutout = 1,
		}

		public enum OverlayBlend
		{
			Normal = 0,
			Multiply = BlendOp.Multiply,
			Overlay = BlendOp.Overlay,
			Screen = BlendOp.Screen,
			Darken = BlendOp.Darken,
			Lighten = BlendOp.Lighten,
			ColorDodge = BlendOp.ColorDodge,
			ColorBurn = BlendOp.ColorBurn,
			SoftLight = BlendOp.SoftLight,
			HardLight = BlendOp.HardLight,
			Subtract = BlendOp.Subtract
		}

		/// <summary>
		/// How should this overlay be processed.
		/// </summary>
		[Tooltip("Normal or Cutout overlay type. This determines whether or not to use a cutout shader during the texture merging process.")]
		public OverlayType overlayType;

        /// <summary>
        /// When true, this overlay will not be merged with other overlays that share the same material.
        /// </summary>
        public bool dontMergeDuplicates = false;
        /// <summary>
        /// Destination rectangle for drawing overlay textures.
        /// </summary>
        [Tooltip("Destination rectangle for drawing overlay textures.")]
		public Rect rect;
		/// <summary>
		/// Optional Alpha mask, if alpha mask is not set the texture[0].alpha is used instead.
		/// Using a alpha mask also allows you to write alpha values from the texture[0] to cut holes
		/// </summary>
		[Tooltip("Optional Alpha mask, if alpha mask is not set the texture[0].alpha is used instead.")]
		public Texture alphaMask;
		/// <summary>
		/// Array of textures required for the overlay material.
		/// </summary>
		[Tooltip("Array of textures required for the overlay material.")]
		[SerializeField]
		[FormerlySerializedAs("textureList")]
		private Texture[] _textureList = new Texture[1];
        public Texture[] textureList
		{
			get
			{
#if UNITY_EDITOR
#if UMA_ADDRESSABLES
				for (int i = 0; i < _textureList.Length; i++)
				{
					if (_textureList[i] == null && i < textureNames.Length)
					{
						string texName = textureNames[i];
						if (!string.IsNullOrEmpty(texName))
						{
							_textureList[i] = UMAAssetIndexer.Instance.GetAsset<Texture2D>(texName);
						}
					}
				}
#endif
#endif
				return _textureList;
			}
			set
			{
				_textureList = value;
			}
		}
        [Tooltip("Optional names for the textures, used for reloading textures when stripped.")]
        public string[] textureNames = new string[1];
		[Tooltip("Overlay Blend Mode. Not used on the base overlay. Similar to standard blend modes on paint apps. Use the alpha channel ")]
        public OverlayBlend[] overlayBlend = new OverlayBlend[1];

        /// <summary>
        /// Use this to identify what kind of overlay this is and what it fits
        /// Eg. BaseMeshSkin, BaseMeshOverlays, GenericPlateArmor01
        /// </summary>
        [Tooltip("Use this to identify what kind of overlay this is and what it fits.")]
		public string[] tags;

		[Tooltip("This is used to identify overlays that share the same UV layout and are interchangeable (eg. for decals that target whatever overlay is currently active on the body, etc.).	")]
        public string overlayGroup;



        /// <summary>
        /// The UMA material.
        /// </summary>
        /// <remarks>
        /// The UMA material contains both a reference to the Unity material
        /// used for drawing and information needed for matching the textures
        /// and colors to the various material properties.
        /// </remarks>
        [Tooltip("The UMA material contains both a reference to the Unity material used for drawing and information needed for matching the textures and colors to the various material properties.")]
		[UMAAssetFieldVisible]
		public UMAMaterial material;

		/// <summary>
		/// materialName is used to save the name of the material, but ONLY if we have cleared the material when building bundles.
		/// You can't count on this field to contain a value unless it was set during the cleanup phase by the indexer!
		/// </summary>
		public string materialName;

		/// <summary>
		/// Occlusion Entries for occluding triangles, currently only supported by powertools.
		/// </summary>
		[System.Serializable]
		public class OcclusionEntry
		{
			/// <summary>
			/// This entry works only on one particular slot identified by it's hash
			/// </summary>
			public int slotNameHash;
			/// <summary>
			/// each of the slots submeshes has an array of UInt32 that contains a boolean mask for which triangles this overlay occludes. The triangle masks are ascending (1,2,4...)
			/// </summary>
			public SubMeshOcclusion[] occlusion;
			[System.Serializable]
			public struct SubMeshOcclusion
			{
				[System.NonSerialized]
				public System.Int32[][] occlusionLODs;
			}

			public class OcclusionEntryComparer : IComparer
			{
				static OcclusionEntryComparer _instance;
				private OcclusionEntryComparer() { }

				[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
				private static void StaticInitializeOnLoad()
				{
					_instance = null;
				}

				public static OcclusionEntryComparer Instance
				{
					get
					{
						if (_instance == null) _instance = new OcclusionEntryComparer();
						return _instance;
					}
				}

				public int Compare(object x, object y)
				{
					var xo = (x as OcclusionEntry);
					var xv = (xo == null) ? (int)x : xo.slotNameHash;

					var yo = (y as OcclusionEntry);
					var yv = (yo == null) ? (int)y : yo.slotNameHash;

					if (xv < yv)
						return -1;
					if (xv > yv)
						return 1;
					return 0;
				}
			}
		}
		/// <summary>
		/// Occlusion Entries for occluding triangles, currently only supported by powertools.
		/// It is important that the OcclusionEntries be sorted by slotNameHash ascending to allow fast binary lookup
		/// </summary>
		public OcclusionEntry[] OcclusionEntries;

		/// <summary>
		/// This overlay was auto generated as a LOD overlay based on another overlay.
		/// </summary>
		[SerializeField]
		[HideInInspector]
		public bool autoGeneratedLOD;

		/// <summary>
		/// The number of textures in the texture array.
		/// </summary>
		public int textureCount
		{
			get
			{
				if (textureList == null)
                {
                    return 0;
                }

                return textureList.Length;
			}
		}

		public UMAMaterial GetMaterial() {
			if(material == null) {
				material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(materialName);
			}
			return material;	
		}

		public void EnsureMaterial() {
			GetMaterial();
			// if the texture list is not loaded, and the overlay has texture names, try to load them
			if(textureList == null && textureNames != null) {
				textureList = new Texture[textureNames.Length];
				for(int i = 0; i < textureNames.Length; i++) {
					if(!string.IsNullOrEmpty(textureNames[i])) {
						textureList[i] = UMAAssetIndexer.Instance.GetAsset<Texture2D>(textureNames[i]);
					}
				}
			}
		}

        public bool forceKeep = false;
        public bool ForceKeep { get { return forceKeep; } set { forceKeep = value; } }

		[Tooltip("If true, this overlay will not be added to the index when adding all")]
        public bool noAutoAdd = false;
        public bool NoAutoAdd { get { return noAutoAdd; } set { noAutoAdd = value; } }

        private bool labelLocalFiles = false;
        public bool LabelLocalFiles { get { return labelLocalFiles; } set { labelLocalFiles = value; } }


		public OverlayBlend GetBlend(int channel)
		{
			if (channel >= overlayBlend.Length)
			{
				return OverlayBlend.Normal;
			}
			return overlayBlend[channel];
		}

		public void ValidateBlendList()
		{
			if (overlayBlend.Length != textureList.Length)
			{
				overlayBlend = new OverlayBlend[textureList.Length];
			}
		}

		public Texture2D GetTexture(string MaterialPropertyName)
		{
			int index = material.GetChannelIndex(MaterialPropertyName);
			if (index >= 0 && index < textureList.Length)
			{
				return textureList[index] as Texture2D;
            }
			return null;
        }

#if false
		/// <summary>
		/// Occlusion Entries for occluding triangles, currently only supported by powertools.
		/// </summary>
		[System.Serializable]
		public class OcclusionEntry
		{
			/// <summary>
			/// This entry works only on one particular slot identified by it's hash
			/// </summary>
			public int slotNameHash;
			/// <summary>
			/// each of the slots submeshes has an array of UInt32 that contains a boolean mask for which triangles this overlay occludes. The triangle masks are ascending (1,2,4...)
			/// </summary>
			public SubMeshOcclusion[] occlusion;
			[System.Serializable]
			public struct SubMeshOcclusion
			{
				public System.Int32[] occlusion;
			}

			/*public class OcclusionEntryComparer : IComparer
			{
				static OcclusionEntryComparer _instance;
				private OcclusionEntryComparer() { }
				public static OcclusionEntryComparer Instance
				{
					get
					{
						if (_instance == null)
                        {
                            _instance = new OcclusionEntryComparer();
                        }

                        return _instance;
					}
				}

				public int Compare(object x, object y)
				{
					var xo = (x as OcclusionEntry);
					var xv = (xo == null) ? (int)x : xo.slotNameHash;

					var yo = (y as OcclusionEntry);
					var yv = (yo == null) ? (int)y : yo.slotNameHash;

					if (xv < yv)
                    {
                        return -1;
                    }

                    if (xv > yv)
                    {
                        return 1;
                    }

                    return 0;
				}
			}*/
		}
#endif
        /// <summary>
        /// Occlusion Entries for occluding triangles, currently only supported by powertools.
        /// It is important that the OcclusionEntries be sorted by slotNameHash ascending to allow fast binary lookup
        /// </summary>
        //[Tooltip("Occlusion Entries for occluding triangles, currently only supported by powertools.")]
        //public OcclusionEntry[] OcclusionEntries;

        public OverlayDataAsset()
		{

		}

		public Texture GetAlphaMask()
		{
			return alphaMask != null ? alphaMask : textureList[0];
		}

        public string GetAssetName()
        {
			return overlayName;
        }

        public int GetNameHash()
        {
			return nameHash;
        }

        public void SortOcclusion()
		{
			if (OcclusionEntries != null)
			{
				System.Array.Sort(OcclusionEntries, OcclusionEntry.OcclusionEntryComparer.Instance);
#if UNITY_EDITOR
				UnityEditor.EditorUtility.SetDirty(this);
#endif
			}
		} 
    }
}
