using System;
using UnityEngine;
using System.Collections.Generic;
using UMA;

public abstract class UMARecipeBase : ScriptableObject
{
	public abstract void Load(UMA.UMAData.UMARecipe umaRecipe, UMAContext context);
	public abstract void Save(UMA.UMAData.UMARecipe umaRecipe, UMAContext context);
	public abstract string GetInfo();
	public abstract byte[] GetBytes();
	public abstract void SetBytes(byte[] data);
	public override string ToString() { return GetInfo(); }
	public static Type[] GetRecipeFormats()
	{
		var formats = new List<Type>(20);
		var types = typeof(UMARecipeBase).Assembly.GetTypes();
		for (int i = 0; i < types.Length; i++)
		{
			var type = types[i];
			if (type.IsSubclassOf(typeof(UMARecipeBase)) && !type.IsAbstract)
			{
				formats.Add(type);
			}
		}
		return formats.ToArray();
	}
}
