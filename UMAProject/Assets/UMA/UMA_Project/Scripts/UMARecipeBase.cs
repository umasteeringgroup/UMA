using UnityEngine;
using System.Collections;

public abstract class UMARecipeBase : ScriptableObject
{
	public abstract void Load(UMA.UMAData umaData, UMAContext context);
	public abstract void Save(UMA.UMAData umaData, UMAContext context);
}
