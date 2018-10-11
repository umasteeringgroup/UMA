using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
    /// <summary>
    /// UMA data asset containing occlusion data for overlapping slots.  
    /// </summary>
	public class OcclusionDataAsset : UMADataAsset, ISerializationCallbackReceiver
    {
		// HACK
		public override string umaName { get { return ""; } }
		public override int umaHash { get { return 0; } }

		public int occluderHash { get { return 0; } }
		public int targetHash { get { return 0; } }

		/// <summary>
		/// BitArray of occluded vertices.
		/// </summary>
		// HACK - save or generate?
		public BitArray vertexFlags { get { return _vertexFlags; }}
		private BitArray _vertexFlags;

		[SerializeField]
		private int vertexCount;
		[SerializeField]
		private int[] serializedFlags;

		/// <summary>
		/// Gets the vertex count.
		/// </summary>
		/// <value>The vertex count.</value>
		public int VertexCount 
		{ 
			get 
			{
				if (_vertexFlags != null)
				{
					return vertexCount;
				}
				else
					return 0;
			}
		}   

		/// <summary>
		/// Gets the hidden vertices count.
		/// </summary>
		/// <value>The hidden vertices count.</value>
		public int HiddenVertexCount
		{
			get
			{
				if (_vertexFlags != null)
				{
					return UMAUtils.GetCardinality(_vertexFlags);
				}
				else
					return 0;
			}
		}

        /// <summary>
        /// Custom serialization to write the BitArray to a boolean array.
        /// </summary>
        public void OnBeforeSerialize()
        {
        }

        /// <summary>
        /// Custom deserialization to write the boolean array to the BitArray.
        /// </summary>
        public void OnAfterDeserialize()
        {            
        }

        /// <summary>
        ///  Set a vertex flag's boolean value
        /// </summary>
        /// <param name="index">The first index for the triangle to set.</param>
        /// <param name="flag">Bool to set the vertex flag to.</param>
        [ExecuteInEditMode]
        public void SetVertexFlag(int index, bool flag)
        {
			if (_vertexFlags == null)
            {
                Debug.LogError("Vertex Array not initialized!");
                return;
            }
        }

         #if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/Occlusion Data Asset")]
        public static void CreateOcclusionDataAsset()
        {
			UMA.CustomAssetUtility.CreateAsset<OcclusionDataAsset>();
        }
        #endif
    }
}