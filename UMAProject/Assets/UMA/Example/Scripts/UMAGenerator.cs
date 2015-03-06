using UnityEngine;
using UMA;

public class UMAGenerator : UMAGeneratorBuiltin
{
	public override void Awake()
	{
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
