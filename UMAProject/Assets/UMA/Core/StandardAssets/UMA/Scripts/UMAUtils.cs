﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UMA
{
	/// <summary>
	/// UMA utility class with various static methods.
	/// </summary>
	public static class UMAUtils
	{
		/// <summary>
		/// Hash value for a string.
		/// </summary>
		/// <returns>Hash value.</returns>
		/// <param name="name">String to hash.</param>
		public static int StringToHash(string name) { return Animator.StringToHash(name); }

		/// <summary>
		/// Gaussian random value.
		/// </summary>
		/// <returns>Random value centered on mean.</returns>
		/// <param name="mean">Mean.</param>
		/// <param name="dev">Deviation.</param>
		static public float GaussianRandom(float mean, float dev)
		{
			float u1 = Random.value;
			float u2 = Random.value;
			
			float rand_std_normal = Mathf.Sqrt(-2.0f * Mathf.Log(u1)) * Mathf.Sin(2.0f * Mathf.PI * u2);
			
			return mean + dev * rand_std_normal;
		}

    /// <summary>
    ///  Fast way to get the number of bits set to true.
    /// </summary>
    /// <returns>Number of bits set to true.</returns>
    /// <param name="bitArray">Bit array.</param>
    //https://stackoverflow.com/questions/5063178/counting-bits-set-in-a-net-bitarray-class
    public static System.Int32 GetCardinality(BitArray bitArray)
    {

        System.Int32[] ints = new System.Int32[(bitArray.Count >> 5) + 1];

        bitArray.CopyTo(ints, 0);

        System.Int32 count = 0;

        // fix for not truncated bits in last integer that may have been set to true with SetAll()
        ints[ints.Length - 1] &= ~(-1 << (bitArray.Count % 32));

        for (System.Int32 i = 0; i < ints.Length; i++)
        {

            System.Int32 c = ints[i];

            // magic (http://graphics.stanford.edu/~seander/bithacks.html#CountBitsSetParallel)
            unchecked
            {
                c = c - ((c >> 1) & 0x55555555);
                c = (c & 0x33333333) + ((c >> 2) & 0x33333333);
                c = ((c + (c >> 4) & 0xF0F0F0F) * 0x1010101) >> 24;
            }

            count += c;

        }

        return count;
    }		
    
    public static string GetAssetFolder(string path)
    {
        int index = path.LastIndexOf('/');
        if( index > 0 )
        {
            return path.Substring(0, index);
        }
        return "";
    }

		public static void DestroySceneObject(UnityEngine.Object obj)
		{
#if UNITY_EDITOR
			if (Application.isPlaying)
			{
				UnityEngine.Object.Destroy(obj);
			}
			else
			{
				UnityEngine.Object.DestroyImmediate(obj, false);
			}
#else
			UnityEngine.Object.Destroy(obj);
#endif
		}
	}

	// Extension class for System.Collections.Generic.List<T> to get
	// its backing array field via reflection.
	// Author: Jackson Dunstan, http://JacksonDunstan.com/articles/3066
	public static class ListBackingArrayGetter
	{
		// Name of the backing array field
		private const string FieldName = "_items";

		// Flags passed to Type.GetField to get the backing array field
		private const BindingFlags GetFieldFlags = BindingFlags.NonPublic | BindingFlags.Instance;

		// Cached backing array FieldInfo instances per Type
		private static readonly Dictionary<System.Type, FieldInfo> itemsFields = new Dictionary<System.Type, FieldInfo>();

		// Get a List's backing array
		public static TElement[] GetBackingArray<TElement>(this List<TElement> list)
		{
			// Check if the FieldInfo is already in the cache
			var listType = typeof(List<TElement>);
			FieldInfo fieldInfo;
			if (itemsFields.TryGetValue(listType, out fieldInfo) == false)
			{
				// Generate the FieldInfo and add it to the cache
				fieldInfo = listType.GetField(FieldName, GetFieldFlags);
				itemsFields.Add(listType, fieldInfo);
			}

			// Get the backing array of the given List
			var items = (TElement[])fieldInfo.GetValue(list);
			return items;
		}
	}

	// Extension class for System.Collections.Generic.List<T> to set
	// the value of its active size field via reflection.
	public static class ListSizeSetter
	{
		// Name of the size field
		private const string FieldName = "_size";

		// Flags passed to Type.GetField to get the size field
		private const BindingFlags GetFieldFlags = BindingFlags.NonPublic | BindingFlags.Instance;

		// Cached backing array FieldInfo instances per Type
		private static readonly Dictionary<System.Type, FieldInfo> itemsFields = new Dictionary<System.Type, FieldInfo>();

		// Set a List's active size
		public static void SetActiveSize<TElement>(this List<TElement> list, int size)
		{
			// Check if the FieldInfo is already in the cache
			var listType = typeof(List<TElement>);
			FieldInfo fieldInfo;
			if (itemsFields.TryGetValue(listType, out fieldInfo) == false)
			{
				// Generate the FieldInfo and add it to the cache
				fieldInfo = listType.GetField(FieldName, GetFieldFlags);
				itemsFields.Add(listType, fieldInfo);
			}

			// Set the active size of the given List
			int newSize = size;
			if (newSize < 0) newSize = 0;
			if (newSize > list.Capacity) newSize = list.Capacity;

			fieldInfo.SetValue(list, newSize);
		}
	}
}
