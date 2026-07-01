using UnityEngine;

namespace UMA
{
	public class UMAGenerator : UMAGeneratorBuiltin 
	{
		public UMAMeshCombiner[] availableMeshCombiners = new UMAMeshCombiner[0];

		public void setMeshComber(UMAMeshCombiner meshCombiner)
		{
			if (meshCombiner == null)
			{
				Debug.LogError("UMAGenerator.setMeshComber: meshCombiner is null");
				return;
			}
		    this.meshCombiner = meshCombiner;
		}

		public override void Awake()
		{
			base.Awake();
            // must turn off the "Copy Render Texture" by default on mobile.
		}

		public override void addDirtyUMA(UMAData umaToAdd)
		{
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
