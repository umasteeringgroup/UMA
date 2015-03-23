using System;
using UnityEngine;
using System.Collections.Generic;
using UMA;

public abstract class UMARecipeBase : ScriptableObject
{
	public abstract void Load(UMAData.UMARecipe umaRecipe, UMAContext context);
	public abstract void Save(UMAData.UMARecipe umaRecipe, UMAContext context);
	public abstract string GetInfo();
	public abstract byte[] GetBytes();
	public abstract void SetBytes(byte[] data);
	public override string ToString() { return GetInfo(); }
	public virtual int GetTypeNameHash() { return UMASkeleton.StringToHash(GetType().Name); }

	protected UMAData.UMARecipe umaRecipe;
	protected bool cached = false;
	public UMAData.UMARecipe GetCachedRecipe(UMAContext context)
	{
		if (!cached)
		{
			umaRecipe = new UMAData.UMARecipe();
			Load(umaRecipe, context);
		}

		return umaRecipe;
	}

	[NonSerialized]
	private static Type[] recipeFormats;
	public static Type[] GetRecipeFormats()
	{
		if (recipeFormats == null)
		{
			var formats = new List<Type>(20);
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (var assembly in assemblies)
			{
				var types = assembly.GetTypes();
				for (int i = 0; i < types.Length; i++)
				{
					var type = types[i];
					if (type.IsSubclassOf(typeof(UMARecipeBase)) && !type.IsAbstract)
					{
						formats.Add(type);
					}
				}
			}
			recipeFormats = formats.ToArray();
		}
		return recipeFormats;
	}
	public static Type FindRecipeFormat(int typeNameHash)
	{
		foreach(var format in GetRecipeFormats())
		{
			if (UMASkeleton.StringToHash(format.Name) == typeNameHash) return format;
		}
		return null;
	}

}
