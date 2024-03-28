using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	[System.Serializable]
	public abstract class DynamicUMADnaBase : UMADnaBase
	{

		#region Fields

		public DynamicUMADnaAsset _dnaAsset;

		public string dnaAssetName;
		//bool to make the recipeEditor save if the DNAAsset was updated
		[System.NonSerialized]
		public bool didDnaAssetUpdate = false;
		//bool to make the recipeEditor save if the DNATypeHash was updated
		[System.NonSerialized]
		public bool didDnaTypeHashUpdate = false;

		public float[] _values = new float[0];
		public string[] _names = new string[0];

		#endregion

		#region Properties

		public abstract DynamicUMADnaAsset dnaAsset { get; set; }

		public abstract override int Count { get; }

		public abstract override float[] Values
		{
			get;
			set;
		}

		public abstract override string[] Names
		{
			get;
		}

		#endregion

		#region Static

		protected static Dictionary<string, DynamicUMADnaAsset> DynamicDNADictionary = null;

		protected static void InitializeDynamicDNADictionary()
		{
			if (DynamicDNADictionary != null)
            {
                return;
            }

            DynamicDNADictionary = new Dictionary<string, DynamicUMADnaAsset>();

			List<DynamicUMADnaAsset> AllDNA;// = UMAContext.Instance.GetAllDNA();

			if (UMAContext.Instance == null)
            {
				AllDNA = UMAAssetIndexer.Instance.GetAllAssets<DynamicUMADnaAsset>();
			}
			else
            {
				AllDNA = UMAContext.Instance.GetAllDNA();
			}

            for (int i = 0; i < AllDNA.Count; i++)
			{
                DynamicUMADnaAsset uda = AllDNA[i];
                if (uda != null)
				{
                        DynamicDNADictionary.Add(uda.name, uda);
				}
			}

			return;

			/*
						string umaloc = PlayerPrefs.GetString("RelativeUMA","UMA/");

						DynamicDNADictionary = new Dictionary<string, DynamicUMADnaAsset>();
			#if UNITY_EDITOR
						var allDNAAssetsGUIDs = UnityEditor.AssetDatabase.FindAssets("t:DynamicUMADnaAsset");
						for (int i = 0; i < allDNAAssetsGUIDs.Length; i++)
						{
							var thisDNAPath = UnityEditor.AssetDatabase.GUIDToAssetPath(allDNAAssetsGUIDs [i]);
							var thisDNAAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<DynamicUMADnaAsset>(thisDNAPath);
							DynamicDNADictionary.Add(thisDNAAsset.name, thisDNAAsset);
						}
			#else

						DynamicUMADnaAsset[] foundAssets = Resources.LoadAll<DynamicUMADnaAsset>(umaloc);
						for (int i = 0; i < foundAssets.Length; i++)
						{
							var thisDNAAsset = foundAssets[i];
							DynamicDNADictionary.Add(thisDNAAsset.name, thisDNAAsset);
						}
			#endif
			*/
		}

		public static void DefineDynamicDNAType(DynamicUMADnaAsset asset)
		{
			InitializeDynamicDNADictionary();
			if (DynamicDNADictionary.ContainsKey(asset.name))
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("DynamicDNADictionary already contained DNA asset " + asset.name);
                }
            }
			else
			{
				DynamicDNADictionary.Add(asset.name, asset);
			}
		}

		#endregion

		#region METHODS

		public abstract float GetValue(string dnaName, bool failSilently = false);

		public abstract override float GetValue(int idx);

		public abstract void SetValue(string name, float value);

		public abstract override void SetValue(int idx, float value);

		public abstract int ImportUMADnaValues(UMADnaBase umaDna);


		public virtual void SetDnaTypeHash(int typeHash)
		{
			base.dnaTypeHash = typeHash;
		}

		/// <summary>
		/// Method for finding a DynamicUMADnaAsset by name.
		/// This can happen when a recipe tries to load load an asset based on an instance ID that may have changed or if the Asset is in an AssetBundle and was not available when the dna was loaded
		/// </summary>
		/// <param name="dnaAssetName"></param>
		public virtual void FindMissingDnaAsset(string dnaAssetName)
		{
			_dnaAsset = UMAContext.Instance.GetDNA(dnaAssetName);
			if (_dnaAsset == null)
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("DynamicUMADnaBase could not find DNAAsset " + dnaAssetName + "!");
                }
            }
			/*
			InitializeDynamicDNADictionary();

			if (!DynamicDNADictionary.TryGetValue(dnaAssetName, out _dnaAsset))
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("DynamicUMADnaBase could not find DNAAsset " + dnaAssetName + "!");
			}
			*/
		}

		public virtual void SetMissingDnaAsset(DynamicUMADnaAsset[] foundAssets)
		{
			//we can only use one
			if (foundAssets.Length > 0)
			{
				dnaAsset = foundAssets[0];
				if (DynamicDNADictionary.ContainsKey(dnaAssetName))
                {
                    DynamicDNADictionary[dnaAssetName] = dnaAsset;
                }
                else
                {
                    DynamicDNADictionary.Add(dnaAsset.name, dnaAsset);
                }
            }
		}

		#endregion
	}
}
