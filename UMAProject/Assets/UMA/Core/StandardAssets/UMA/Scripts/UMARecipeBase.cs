using System;
using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Base class for serialized UMA recipes.
	/// </summary>
	public abstract partial class UMARecipeBase : ScriptableObject
	{
		/// <summary>
		/// Load data into the specified umaRecipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public abstract void Load(UMAData.UMARecipe umaRecipe, bool loadSlots = true);
		/// <summary>
		/// Save data from the specified umaRecipe.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="context">Context.</param>
		public abstract void Save(UMAData.UMARecipe umaRecipe);
		public abstract string GetInfo();
		public abstract byte[] GetBytes();
		public abstract void SetBytes(byte[] data);
		public override string ToString() { return GetInfo(); }
		public virtual int GetTypeNameHash() { return UMAUtils.StringToHash(GetType().Name); }

		protected UMAData.UMARecipe umaRecipe;
		protected bool cached = false;
		public string label;   
		public string AssignedLabel
		{
			get
			{
				if (string.IsNullOrEmpty(label))
                {
                    return name;
                }
                else
                {
                    return label;
                }
            }
		}
		[Tooltip("This will be skipped when generating Addressable Groups. This can result in duplicate assets.")]
		public bool resourcesOnly;


#if UNITY_EDITOR

	#endif
		/// <summary>
		/// Return a cached version of the UMA recipe, Load if required.
		/// </summary>
		/// <returns>The cached recipe.</returns>
		/// <param name="context">Context.</param>
		public UMAData.UMARecipe GetCachedRecipe( bool loadSlots = true)
		{
			if (!cached || umaRecipe == null)
			{
				umaRecipe = new UMAData.UMARecipe();
				Load(umaRecipe,loadSlots);
#if !UNITY_EDITOR
#if UMA_ADDRESSABLES
				// don't cache addressables, as they can be unloaded.
				cached = false;
#else
				// do not cache in the editor
				cached = true;
#endif
#endif
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
                for (int i1 = 0; i1 < assemblies.Length; i1++)
				{
                    System.Reflection.Assembly assembly = assemblies[i1];
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
            Type[] array = GetRecipeFormats();
            for (int i = 0; i < array.Length; i++)
			{
                Type format = array[i];
                if (UMAUtils.StringToHash(format.Name) == typeNameHash)
                {
                    return format;
                }
            }
			return null;
		}
	}
}
