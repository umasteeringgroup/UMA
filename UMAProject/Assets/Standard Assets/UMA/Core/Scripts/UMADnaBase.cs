using UnityEngine;
using System.Collections;

namespace UMA
{
	[System.Serializable]
	public partial class UMADnaBase{
		public virtual int Count { get { return 0; } }
		public virtual float[] Values
		{
			get
			{
				return new float[0];
			}
			set
			{
			}
		}
	}
}