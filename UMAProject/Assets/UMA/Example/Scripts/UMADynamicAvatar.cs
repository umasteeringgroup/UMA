using UnityEngine;
using System;
using System.IO;
using System.Runtime.Serialization;
using LitJson;
using System.Collections;
using System.Collections.Generic;
using UMA;

public class UMADynamicAvatar : UMAAvatarBase
{
	public bool loadOnStart;
	public override void Start()
	{
		base.Start();
		if (loadOnStart)
		{
			Load(umaRecipe);
		}
	}
#if UNITY_EDITOR
	[UnityEditor.MenuItem("GameObject/Create Other/UMA/Dynamic Avatar")]
	static void CreateDynamicAvatarMenuItem()
	{
		var res = new GameObject("New Dynamic Avatar");
		var da = res.AddComponent<UMADynamicAvatar>();
		da.context = UMAContext.FindInstance();
		da.umaGenerator = Component.FindObjectOfType<UMAGeneratorBase>();
		UnityEditor.Selection.activeGameObject = res;
	}
#endif
}
