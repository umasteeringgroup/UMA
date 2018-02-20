using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA.
	/// </summary>
	[System.Serializable]
	public abstract class UMADnaBase
	{
		public const float MISSING_DNA_VALUE = 0.5f;

		public abstract int Count { get; }
		public abstract float[] Values
		{
			get; set;
		}

		public abstract string[] Names
		{
			get;
		}

		public abstract int GetIndex(string name);

		public UMADnaAccessor GetAccessor(string name)
		{
			int index = GetIndex(name);

			if (index >= 0)
			{
				return new UMADnaAccessor(this, index);
			}

			return null;
		}
		public UMADnaAccessor GetAccessor(int idx)
		{
			if ((idx >= 0) && (idx < Count))
			{
				return new UMADnaAccessor(this, idx);
			}

			return null;
		}

		public abstract float GetValue(int idx);
		public abstract float GetValue(string name);
		public abstract void SetValue(int idx, float value);
		public abstract void SetValue(string name, float value);

		[SerializeField]
		protected int dnaTypeHash;
		public virtual int DNATypeHash
		{
			set {
					dnaTypeHash = value;
				}
			get {
					if (dnaTypeHash == 0)
						dnaTypeHash = UMAUtils.StringToHash(GetType().Name);
					return dnaTypeHash;
				}
		}

#region DNAACESSOR
		/// <summary>
		/// A UMADnaAccessor is used to wrap a specific entry in a DNA object
		/// </summary>
		public class UMADnaAccessor
		{
			protected UMADnaBase dna;	// DNA object
			protected int index;		// Index of specfic DNA value

			/// <summary>
			/// Construct a UMADnaAccessor
			/// </summary>
			/// <param name="dnaObject"></param>
			/// <param name="dnaIndex"></param>
			public UMADnaAccessor(UMADnaBase dnaObject, int dnaIndex)
			{
				dna = dnaObject;
				index = dnaIndex;
			}

			/// <summary>
			/// Set the current DNA value.
			/// </summary>
			public void Set(float val)
			{
				dna.SetValue(index, val);
			}
				
			/// <summary>
			/// Gets the current DNA value.
			/// </summary>
			public float Get()
			{
				return dna.GetValue(index);
			}
		}
#endregion

	}
}