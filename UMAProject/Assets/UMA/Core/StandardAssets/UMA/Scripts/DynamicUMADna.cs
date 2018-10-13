using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UMA.CharacterSystem;

namespace UMA
{
    public class DynamicUMADna : DynamicUMADnaBase
    {
        #region Constructor
        public DynamicUMADna()
        {
        }
        public DynamicUMADna(int typeHash)
        {
            base.dnaTypeHash = typeHash;
        }
		#endregion

		#region Properties

		public override DynamicUMADnaAsset dnaAsset
        {
            get { return _dnaAsset; }
            set
            {
                if (value != null)
                {
                    ValidateValues(value.Names);
                    dnaAssetName = value.name;
                    _dnaAsset = value;
                }
                else
                {
                    dnaAssetName = "";
                    _dnaAsset = null;
                }
            }
        }

        public override int Count
        {
            get
            {
                return Values.Length;
            }
        }
        public override float[] Values
        {
            get
            {
                if (_values.Length > 0)
                    return _values;
                else
                {
					return new float[0];
                }
                    
            }
            set
            {
                _values = value;
            }
        }

        public override string[] Names
        {
            get
            {
                if (_names.Length != 0)
                {
                    if(dnaAsset != null)
                    {
						//this just checks if the names have changed while the game is actually running
                        if(_names.Length != dnaAsset.Names.Length)
                        {
                            ValidateValues(dnaAsset.Names);
                        }
                    }
                    return _names;
                }
                else
                {
					return new string[0];
                }
            }
        }

		/// <summary>
		/// This is a placeholder method to conform to the requirements for the DNA template code, results are not valid
		/// </summary>
		public static string[] GetNames()
		{
			Debug.LogWarning("Calling the static GetNames() method of Dynamic DNA, result will be empty");

			return new string[0];
		}

        #endregion

        #region Methods
        /// <summary>
        /// Convert a recipes UMADnaHumanoid values to DynamicUMADna values. This will need to be done if the user switches a races converter from a HumanoidDNAConverterBehaviour to a DynamicDNAConverterBehaviour
        /// </summary>
        /// <param name="umaDna"></param>
        public override int ImportUMADnaValues(UMADnaBase umaDna)
        {
            int dnaImported = 0;
			string[] dnaNames = umaDna.Names;

			for (int i = 0; i < umaDna.Count; i++)
            {
				var thisValueName = dnaNames[i];
                for (int ii = 0; ii < Names.Length; ii++)
                {
					//if (Names[ii].Equals(thisValueName, StringComparison.OrdinalIgnoreCase))
					//actually I think they should be exactly equal
					if (Names[ii] == thisValueName)
					{
                        Values[ii] = umaDna.Values[i];
                        dnaImported++;
                    }
                }
            }

            return dnaImported;
        }
        /// <summary>
        /// Regenerates the _names and _values array when a DynamicUMADnaAsset is added matching existing values to the assets names, adding any names that dont exist and removing names that no longer exist
        /// </summary>
        /// <param name="requiredNames"></param>
        void ValidateValues(string[] requiredNames)
        {
            List<float> newValues = new List<float>(requiredNames.Length);
            for (int i = 0; i < requiredNames.Length; i++)
            {
                bool valueFound = false;
				var currentNames = _names.Length > 0 ? _names : new string[0];
				for (int ii = 0; ii < currentNames.Length; ii++)
                {
                    if (currentNames[ii] == requiredNames[i])
                    {
                        newValues.Insert(i, Values[ii]);
                        valueFound = true;
                        break;
                    }
                }
                if (valueFound == false)
                {
                    newValues.Insert(i, 0.5f);
                }
            }
            _names = requiredNames;
            _values = newValues.ToArray();
        }

        public override float GetValue(string dnaName, bool failSilently = false)
        {
            int idx = -1;
			if(Names.Length > 0 && dnaName != "")
				for (int i = 0; i < Names.Length; i++)
				{
					if (Names[i] == dnaName)
					{
						idx = i;
					}
				}
            if (idx == -1 && failSilently == false)
                throw new System.ArgumentOutOfRangeException();
            else if (idx == -1 && failSilently == true)
                return 0.5f;
            return GetValue(idx);
        }
        public override float GetValue(int idx)
        {
            if (idx < Count)
            {
                return Values[idx];
            }
            throw new System.ArgumentOutOfRangeException();
        }
        public override void SetValue(string dnaName, float value)
        {
            int idx = -1;
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i] == dnaName)
                {
                    idx = i;
                }
            }
            if (idx == -1)
                throw new System.ArgumentOutOfRangeException();
            SetValue(idx, value);
        }
        public override void SetValue(int idx, float value)
        {
            if (idx < Count)
            {
                _values[idx] = value;
                return;
            }
            throw new System.ArgumentOutOfRangeException();
        }

		/// <summary>
		/// Method for finding a DynamicUMADnaAsset by name using DynamicAssetLoader. This can happen when a recipe tries to load load an asset based on an instance ID that may have changed or if the Asset is in an AssetBundle and was not available when the dna was loaded
		/// </summary>
		/// <param name="dnaAssetName"></param>
		public override void FindMissingDnaAsset(string dnaAssetName)
		{
			InitializeDynamicDNADictionary();
			if (DynamicDNADictionary.TryGetValue(dnaAssetName, out _dnaAsset))
				return;
			//if we are in the editor and the application is not running find the Asset using AssetDatabase
#if UNITY_EDITOR
				if (!Application.isPlaying)
			{
				var allDNAAssetsGUIDs = AssetDatabase.FindAssets("t:DynamicUMADnaAsset");
				for (int i = 0; i < allDNAAssetsGUIDs.Length; i++)
				{
					var thisDNAPath = AssetDatabase.GUIDToAssetPath(allDNAAssetsGUIDs[i]);
					var thisDNA = AssetDatabase.LoadAssetAtPath<DynamicUMADnaAsset>(thisDNAPath);
					if (thisDNA.name == dnaAssetName)
					{
						//Debug.Log("DynamicUMADna found  " + _dnaAssetName + " from AssetDatabase");
						SetMissingDnaAsset(new DynamicUMADnaAsset[1] { thisDNA });
						break;
					}
				}
				//This just makes it super slow *every* time a recipe is loaded
				//Resources.UnloadUnusedAssets();
				return;
			}
#endif
			didDnaAssetUpdate = false;
			didDnaAssetUpdate = DynamicAssetLoader.Instance.AddAssets<UMA.DynamicUMADnaAsset>(true, true, true, "", "", null, dnaAssetName, SetMissingDnaAsset);
			if (didDnaAssetUpdate == false)
			{
				Debug.LogWarning("DynamicUMADna could not find DNAAsset " + dnaAssetName + "!");
			}
		}

		public static DynamicUMADna LoadInstance(string data)
        {
            return UnityEngine.JsonUtility.FromJson<DynamicUMADna_Byte>(data).ToDna();
        }
        public static string SaveInstance(DynamicUMADnaBase instance)
        {
            return UnityEngine.JsonUtility.ToJson(DynamicUMADna_Byte.FromDna(instance));
        }

        #endregion
    }

   // Class to store dynamic settings as name value pairs. 
   // We need this because the DynamicUMADnaAssets values may change and so we need to match any existing values to names even if the array size has changed
    [System.Serializable]
    public class DNASettings
    {
        public string name;
        public System.Byte value;
        public DNASettings()
        {

        }
        public DNASettings(string _name, System.Byte _value)
        {
            name = _name;
            value = _value;
        }
    }
    [System.Serializable]
    public class DynamicUMADna_Byte
    {
        public DynamicUMADnaAsset bDnaAsset;
        public string bDnaAssetName;
        public DNASettings[] bDnaSettings;

        public DynamicUMADna ToDna()
        {
			var res = new DynamicUMADna();
            //Do names and values first
            res._names = new string[bDnaSettings.Length];
            for (int i = 0; i < bDnaSettings.Length; i++)
            {
                res._names[i] = bDnaSettings[i].name;
            }
            res._values = new float[bDnaSettings.Length];
            for (int ii = 0; ii < bDnaSettings.Length; ii++)
            {
                res._values[ii] = bDnaSettings[ii].value * (1f / 255f);
            }
            res.dnaAssetName = bDnaAssetName;
            //Then set the asset using dnaAsset.set so that everything is validated and any new dna gets added with default values
            //Usually we need to find the asset because the instance id in the recipe will not be the same in different sessions of Unity
            if (bDnaAsset == null && bDnaAssetName != "")
            {
                res.FindMissingDnaAsset(bDnaAssetName);
            }
            else
            {
                res.dnaAsset = bDnaAsset;
            }

			if (res.dnaAsset != null)
			{
				res.SetDnaTypeHash(res.dnaAsset.dnaTypeHash);
			}
			else
			{
				Debug.LogWarning("Deserialized DynamicUMADna with no matching asset!");
			}

            return res;
        }
        public static DynamicUMADna_Byte FromDna(DynamicUMADnaBase dna )
        {
            var res = new DynamicUMADna_Byte();
            res.bDnaAsset = dna.dnaAsset;
            if(dna.dnaAsset != null)
                res.bDnaAssetName = dna.dnaAsset.name;
            res.bDnaSettings = new DNASettings[dna._values.Length];
            for (int i = 0; i < dna._values.Length; i++)
            {
                res.bDnaSettings[i] = new DNASettings(dna._names[i], (System.Byte)(dna._values[i] * 255f + 0.5f));
            }
            return res;
        }
    }
}
