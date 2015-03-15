using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
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

		public abstract float GetValue(int idx);
		public abstract void SetValue(int idx, float value);
	}
}