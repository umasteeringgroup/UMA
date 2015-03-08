using UnityEngine;
using System.Collections;

namespace UMA
{
	public delegate int DNAValueCountMethod();
	public delegate void DNAValuesMethod(float[] values);
	[System.Serializable]
	public partial class UMADnaBase{
		public DNAValueCountMethod GetValuesCount;
		public DNAValuesMethod GetValues;
		public DNAValuesMethod SetValues;		
	}
}