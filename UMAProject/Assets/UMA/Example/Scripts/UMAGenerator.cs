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
}
