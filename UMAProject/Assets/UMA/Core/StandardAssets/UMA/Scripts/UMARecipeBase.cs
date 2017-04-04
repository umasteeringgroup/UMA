using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Base class for serialized UMA recipes.
	/// </summary>
	public abstract class UMARecipeBase : ScriptableObject
	{
		/// <summary>
		/// Load data into the specified umaRecipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public abstract void Load(UMAData.UMARecipe umaRecipe, UMAContext context);
		/// <summary>
		/// Save data from the specified umaRecipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public abstract void Save(UMAData.UMARecipe umaRecipe, UMAContext context);
		public abstract string GetInfo();
		public abstract byte[] GetBytes();
		public abstract void SetBytes(byte[] data);
		public override string ToString() { return GetInfo(); }
		public virtual int GetTypeNameHash() { return UMAUtils.StringToHash(GetType().Name); }

		protected UMAData.UMARecipe umaRecipe;
		protected bool cached = false;

	#if UNITY_EDITOR

		//This is used as a base for UMATextRecipe to override, because we cannt get what we need from this assembly- but the method needs to exist here to work in RecipeEditor
		public virtual UMAContext CreateEditorContext()
		{
			return null;
		}
	#endif
		/// <summary>
		/// Return a cached version of the UMA recipe, Load if required.
		/// </summary>
		/// <returns>The cached recipe.</returns>
		/// <param name="context">Context.</param>
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
		/// <summary>
		/// Gets the list of all existing recipe formats.
		/// </summary>
		/// <returns>The recipe formats.</returns>
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

		/// <summary>
		/// Finds the recipe format for a give name hash.
		/// </summary>
		/// <returns>The recipe format.</returns>
		/// <param name="typeNameHash">Name hash.</param>
		public static Type FindRecipeFormat(int typeNameHash)
		{
			foreach(var format in GetRecipeFormats())
			{
				if (UMAUtils.StringToHash(format.Name) == typeNameHash) return format;
			}
			return null;
		}
	}
}
