using UnityEngine;

namespace UMA
{
	public class UMAGenerator : UMAGeneratorBuiltin 
	{
		public override void Awake()
		{
			base.Awake();
            // must turn off the "Copy Render Texture" by default on mobile.
		}

		public override void addDirtyUMA(UMAData umaToAdd)
		{
			// Debug.Log(Debug.isDebugBuild ? "Adding Dirty UMA: " + umaToAdd.gameObject.name : string.Empty);
            if (!gameObject.activeInHierarchy)
			{
				if (Debug.isDebugBuild)
				{
					Debug.LogError("Adding Dirty UMA to a Generator that is not an active scene object, UMA generators must be active scene objects!", gameObject);
					Debug.LogError("UMA Data ", umaToAdd.gameObject);
				}
				return;
			} 
			base.addDirtyUMA(umaToAdd);
		}
	}
}
