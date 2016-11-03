using UnityEngine;
using System.Collections;


namespace UMA
{
	/// <summary>
	/// Base class for DNA converters.
	/// </summary>
	public class DnaConverterBehaviour : MonoBehaviour
	{
		public DnaConverterBehaviour()
		{
			Prepare();
		}
		public System.Type DNAType;
		public int dnaTypeHash;
		public virtual int GetDnaTypeHash()
		{
			if (dnaTypeHash == 0)
				dnaTypeHash = UMAUtils.StringToHash(DNAType.Name);
			return dnaTypeHash;
		}
		public delegate void DNAConvertDelegate(UMAData data, UMASkeleton skeleton);
		/// <summary>
		/// Called on the DNA converter to adjust avatar from DNA values.
		/// </summary>
		public DNAConvertDelegate ApplyDnaAction;

		public virtual void Prepare()
		{
		}
	}
}
