using UnityEngine;
using System.Collections;

public class UMAAssetIndexFileRef : ScriptableObject
{
	public UnityEngine.Object objectRef = null;

	public UMAAssetIndexFileRef() { }

	public UMAAssetIndexFileRef(UnityEngine.Object thisObjectRef)
	{
		objectRef = thisObjectRef;
		//The following causes CRASH
		//this.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
	}

}
