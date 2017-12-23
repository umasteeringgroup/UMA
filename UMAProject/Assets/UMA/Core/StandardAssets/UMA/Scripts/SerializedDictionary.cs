using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Dictionary that will work with Unity serialization.
	/// </summary>
	/// <remarks>
	/// Generics won't work with the Unity inspectors, so there are concrete subclasses for various UMA types.
	/// </remarks>

	/// <summary>
	/// Serializable dictionary for race assets
	/// </summary>
	[Serializable]
	public class RaceAssetDictionary : SerializedDictionary<int, RaceDataAsset> { }

	/// <summary>
	/// Serializable dictionary for slot assets
	/// </summary>
	[Serializable]
	public class SlotAssetDictionary : SerializedDictionary<int, SlotDataAsset> { }

	/// <summary>
	/// Serializable dictionary for overlay assets
	/// </summary>
	[Serializable]
	public class OverlayAssetDictionary : SerializedDictionary<int, OverlayDataAsset> { }

	/// <summary>
	/// Serializable dictionary for DNA assets
	/// </summary>
	[Serializable]
	public class DNAAssetDictionary : SerializedDictionary<int, DynamicUMADnaAsset> { }

	/// <summary>
	/// Serializable dictionary for occlusion assets
	/// </summary>
	[Serializable]
	public class OcclusionAssetDictionary : SerializedDictionary<int, MeshHideAsset> { }

	/// <summary>
	/// Serializable dictionary for asset bundle references
	/// </summary>
	[Serializable]
	public class AssetReferenceDictionary : SerializedDictionary<int, UMAAssetBundleContext.AssetReference> { }

	[Serializable]
	public class SerializedDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
	{
		[SerializeField]
		private List<TKey> keys = new List<TKey>();

		[SerializeField]
		private List<TValue> values = new List<TValue>();

		/// <remarks>
		/// The serialization and deserialization happens so often it's impossible to keep the
		/// two lists synchronized using SerializedProperty.DeleteArrayElementAtIndex().
		/// Therefore this is used to mark a pair for deletion in OnAfterDeserialize()
		/// </remarks>
		[SerializeField]
		private int deleteIndex = -1;

		// Save the dictionary to lists
		public void OnBeforeSerialize()
		{
			keys.Clear();
			values.Clear();
			foreach(KeyValuePair<TKey, TValue> pair in this)
			{
				keys.Add(pair.Key);
				values.Add(pair.Value);
			}
		}

		// Load dictionary from lists
		public void OnAfterDeserialize()
		{
			this.Clear();

			if (keys.Count == values.Count)
			{
				for(int i = 0; i < keys.Count; i++)
				{
					if (i == deleteIndex) continue;
					this.Add(keys[i], values[i]);
				}

				deleteIndex = -1;
			}
			else
			{
				Debug.LogError("Bad arrays in SerializableDictionary!");
			}
		}
	}

}