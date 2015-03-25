using UnityEngine;
using System.Collections;


namespace UMA
{
	public class DnaConverterBehaviour : MonoBehaviour 
	{
		public DnaConverterBehaviour()
		{
			Prepare();
		}
	    public System.Type DNAType;
        public delegate void DNAConvertDelegate(UMAData data, UMASkeleton skeleton);
        public DNAConvertDelegate ApplyDnaAction;

        public virtual void Prepare()
        {
		}
    }
}
