using UnityEngine;
using UMA;

public class UMAGenerator : UMAGeneratorBuiltin
{
	public override void Awake()
	{
		if (usePRO)
		{
#if UNITY_EDITOR
			if (!UnityEditorInternal.InternalEditorUtility.HasPro())
			{
				Debug.LogError("You told the Generator to usePRO but you don't have pro license. Toggling to false.", gameObject);
				usePRO = false;
			}
#endif
		}
		base.Awake();
	}

    public override void addDirtyUMA(UMAData umaToAdd)
    {
        if (!gameObject.activeInHierarchy)
        {
            Debug.LogError("Adding Dirty UMA to a Generator that is not an active scene object, UMA generators must be active scene objects!", gameObject);
            Debug.LogError("UMA Data ", umaToAdd.gameObject);
            return;
        }
        base.addDirtyUMA(umaToAdd);
    }
}
