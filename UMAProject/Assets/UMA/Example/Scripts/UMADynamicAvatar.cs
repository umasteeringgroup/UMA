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
}
