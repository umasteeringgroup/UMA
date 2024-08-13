using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA.
	/// </summary>
	[System.Serializable]
	public class UMADnaBase 
	{
		public virtual int Count { get; }
		public virtual float[] Values
		{
			get; set;
		}

		public virtual string[] Names
		{
			get;
		}

		public virtual float GetValue(int idx)
        {
			return 0.0f;
        }

		public virtual void SetValue(int idx, float value)
        {
			return;
        }

		[SerializeField]
		protected int dnaTypeHash;
		public virtual int DNATypeHash
		{
			set {
					dnaTypeHash = value;
				}
			get {
					if (dnaTypeHash == 0)
                {
                    dnaTypeHash = UMAUtils.StringToHash(GetType().Name);
                }

                return dnaTypeHash;
				}
		}
	}
}
