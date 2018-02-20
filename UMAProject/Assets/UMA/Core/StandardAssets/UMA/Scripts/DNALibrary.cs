using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA libraries.
	/// </summary>
	public class DNALibrary : MonoBehaviour 
	{
		[SerializeField]
		protected DNADataAsset[] dnaAssetArray = new DNADataAsset[0];
		[NonSerialized]
		private Dictionary<int, DNADataAsset> dnaDictionary;

		void Awake()
		{
			ValidateDictionary();
		}

		public void UpdateDictionary()
		{
			ValidateDictionary();
			dnaDictionary.Clear();
			for (int i = 0; i < dnaAssetArray.Length; i++)
			{
				if (dnaAssetArray[i])
				{
					int hash = dnaAssetArray[i].umaHash;
					if (!dnaDictionary.ContainsKey(hash))
					{
						dnaDictionary.Add(hash, dnaAssetArray[i]);
					}
				}
			}
		}

		public void ValidateDictionary()
		{
			if (dnaDictionary == null)
			{
				dnaDictionary = new Dictionary<int, DNADataAsset>();
				UpdateDictionary();
			}
		}

		/// <summary>
		/// Add a DNA Asset to the library.
		/// </summary>
		/// <param name="dna">DNA Asset.</param>
		public void AddDNAAsset(DNADataAsset dna)
		{
			ValidateDictionary();

			dnaDictionary[dna.umaHash] = dna;
			dnaAssetArray = new DNADataAsset[dnaDictionary.Count];

			// HACK, only do this crap on serialization
			dnaDictionary.Values.CopyTo(dnaAssetArray, 0);
		}

		public bool HasDNA(string name)
		{
			ValidateDictionary();
			return dnaDictionary.ContainsKey(UMAUtils.StringToHash(name));
		}

		public bool HasDNA(int nameHash)
		{
			ValidateDictionary();
			return dnaDictionary.ContainsKey(nameHash);
		}

		public UMADnaBase InstantiateDNA(string name)
		{
			var res = Internal_InstantiateDNA(UMAUtils.StringToHash(name));
			if (res == null)
			{
				throw new UMAResourceNotFoundException("DNALibrary: Unable to find: " + name);
			}
			return res;
		}
		public UMADnaBase InstantiateDNA(int nameHash)
		{
			var res = Internal_InstantiateDNA(nameHash);
			if (res == null)
			{
				throw new UMAResourceNotFoundException("DNALibrary: Unable to find hash: " + nameHash);
			}
			return res;
		}

		public DNADataAsset[] GetAllDNAAssets()
		{
			#pragma warning disable 618
			return dnaAssetArray;
			#pragma warning restore 618
		}

		private UMADnaBase Internal_InstantiateDNA(int nameHash)
		{
			ValidateDictionary();
			DNADataAsset asset;
			if (dnaDictionary.TryGetValue(nameHash, out asset))
			{
				DynamicUMADna dynamicDNA = new DynamicUMADna(nameHash);
				// HACK
				//dynamicDNA.dnaAsset = asset;

				return dynamicDNA;
			}
			else
			{
				// HACK HACK HACK HACK
//				if (nameHash == UMAUtils.StringToHash("UMADnaHumanoid")) {return new UMADnaHumanoid();}
//				if (nameHash == UMAUtils.StringToHash("UMADnaTutorial")) {return new UMADnaTutorial();}

				return null;
			}
		}
	}
}