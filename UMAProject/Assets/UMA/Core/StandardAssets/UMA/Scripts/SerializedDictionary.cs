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
	public class RaceAssetDictionary : SerializableDictionary<int, RaceData> { }

	/// <summary>
	/// Serializable dictionary for slot assets
	/// </summary>
	[Serializable]
	public class SlotAssetDictionary : SerializableDictionary<int, SlotDataAsset> { }

	/// <summary>
	/// Serializable dictionary for overlay assets
	/// </summary>
	[Serializable]
	public class OverlayAssetDictionary : SerializableDictionary<int, OverlayDataAsset> { }

	/// <summary>
	/// Serializable dictionary for DNA assets
	/// </summary>
	[Serializable]
	public class DNAAssetDictionary : SerializableDictionary<int, DynamicUMADnaAsset> { }

	/// <summary>
	/// Serializable dictionary for occlusion assets
	/// </summary>
	[Serializable]
	public class OcclusionAssetDictionary : SerializableDictionary<int, MeshHideAsset> { }

	[Serializable]
	public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
	{
		[SerializeField]
		private List<TKey> keys = new List<TKey>();

		[SerializeField]
		private List<TValue> values = new List<TValue>();

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

			if(keys.Count != values.Count)
				throw new System.Exception(string.Format("there are {0} keys and {1} values after deserialization. Make sure that both key and value types are serializable."));

			for(int i = 0; i < keys.Count; i++)
				this.Add(keys[i], values[i]);
		}
	}

}