using UnityEngine;
using System.Collections;


namespace UMA
{
	public class DnaConverterBehaviour : MonoBehaviour 
	{
	    public System.Type DNAType;
        public delegate void DNAConvertDelegate(UMAData data, UMASkeleton skeleton);
        public DNAConvertDelegate ApplyDnaAction;
	    public void ApplyDna(UMAData data, UMASkeleton skeleton)
	    {
	        ApplyDnaAction(data, skeleton);
	    }

        public virtual void Prepare()
        {
        }
    }
}