using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
    [System.Serializable]
	public class DNADataAsset : UMADataAsset
    {
		[SerializeField]
		protected string _umaName;
		public override string umaName
		{
			get { return _umaName; }
		}

		[SerializeField]
		protected int _umaHash;
		public override int umaHash
		{
			get { return _umaHash; }
		}

		[SerializeField]
		protected byte _dnaVersion;
		public byte dnaVersion
		{
			get { return _dnaVersion; }
		}

		[SerializeField]
		protected string[] _dnaNames;
		public string[] dnaNames
		{
			get { return _dnaNames; }
		}

		[SerializeField]
		protected List<string[]> oldNames;

		protected List<int[]> oldRemaps;

		public int[] GetRemap(byte oldVersion)
		{
			if ((oldVersion < 0) || (oldVersion >= oldNames.Count))
			{
				return null;
			}

			if (oldRemaps == null)
			{
				oldRemaps = new List<int[]>(oldNames.Count);
			}

			if (oldRemaps[oldVersion] == null)
			{
				string[] origNames = oldNames[oldVersion];
				int[] remap = new int[origNames.Length];

				for (int i = 0; i < origNames.Length; i++)
				{
					remap[i] = System.Array.IndexOf(_dnaNames, origNames[i]);
				}
				oldRemaps[oldVersion] = remap;
			}

			return oldRemaps[oldVersion];
		}

		public float[] RemapValues(float[] origValues, byte origVersion, float defaultVal)
		{
			if (origVersion == _dnaVersion)
			{
				return origValues;
			}

			float[] newValues = new float[_dnaNames.Length];
			for (int i = 0; i < newValues.Length; i++)
			{
				newValues[i] = defaultVal;
			}

			int[] remap = GetRemap(origVersion);
			if (remap != null)
			{
				for (int i = 0; i < remap.Length; i++)
				{
					int j = remap[i];
					if (j >= 0)
					{
						newValues[j] = origValues[i];
					}
				}
			}

			return newValues;
		}
	}
}
