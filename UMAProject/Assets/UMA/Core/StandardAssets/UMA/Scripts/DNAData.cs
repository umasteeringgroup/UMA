using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
    [System.Serializable]
	public class DNAData : UMADnaBase
    {
		/// <summary>
		/// The asset contains the immutable portions of the dna, such as the names.
		/// </summary>
		[SerializeField]
		protected DNADataAsset asset = null;

		/// <summary>
		/// Asset version the DNA data corresponds to.
		/// </summary>
		[SerializeField]
		protected int version = 0;

		/// <summary>
		/// Array of actual DNA data.
		/// </summary>
		[SerializeField]
		protected float[] values = new float[0];

		public DNAData(DNADataAsset dnaAsset)
		{
			asset = dnaAsset;
			version = dnaAsset.dnaVersion;
			values = new float[dnaAsset.dnaCount];

			// HACK
			dnaTypeHash = asset.umaHash;
		}

		public override int Count
		{
			get { return values.Length; }
		}

		public override float[] Values
		{
			get { return values; }

			set
			{
				if ((asset != null) && (value.Length != asset.dnaCount))
				{
					Debug.LogError("Tried to set DNA values to invalid length!");
					return;
				}

				values = value;
			}
		}

		public override string[] Names
		{
			get
			{
				if (asset != null) return asset.dnaNames;

				return null;
			}
		}

		public override int GetIndex(string name)
		{
			if (asset != null) return asset.GetNameIndex(name);

			return -1;
		}

		public override float GetValue(int idx)
		{
			if (idx < 0) return MISSING_DNA_VALUE;
			if (idx >= values.Length) return MISSING_DNA_VALUE;

			return values[idx];
		}

		public override float GetValue(string name)
		{
			int idx = GetIndex(name);
			return GetValue(idx);
		}

		public override void SetValue(int idx, float value)
		{
			if (idx < 0) return;
			if (idx >= values.Length) return;

			values[idx] = value;
		}

		public override void SetValue(string name, float value)
		{
			int idx = GetIndex(name);
			SetValue(idx, value);
		}
	}
}
