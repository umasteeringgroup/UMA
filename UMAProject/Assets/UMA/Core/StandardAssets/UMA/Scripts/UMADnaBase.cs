using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Base class for UMA DNA.
	/// </summary>
	[System.Serializable]
	public abstract class UMADnaBase 
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
        public abstract int DNATypeHash
        {
            get;
			set;
        }
	}
}
