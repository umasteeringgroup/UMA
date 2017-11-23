using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA.
	/// </summary>
	[System.Serializable]
	public abstract class UMADnaBase
	{
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
	}
}