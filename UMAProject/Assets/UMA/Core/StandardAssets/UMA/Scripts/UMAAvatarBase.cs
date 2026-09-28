using UnityEngine;
using UnityEngine.Profiling;

namespace UMA
{
	/// <summary>
	/// Base class for UMA character.
	/// </summary>
	public abstract class UMAAvatarBase : UMAData 
	{
		// public UMAData umaData;
		public UMAData umaData
		{
			get
			{
				return this as UMAData;
			}
		}

		/// <summary>
		/// The serialized basic UMA recipe.
		/// </summary>
		public UMARecipeBase serializedRecipe;
		/// <summary>
		/// Additional partial UMA recipes (not serialized).
		/// </summary>
		public UMARecipeBase[] umaAdditionalRecipes;
		protected RaceData umaRace = null;

		//public UMADataEvent AnimatorStateSaved;
		//public UMADataEvent AnimatorStateRestored;

		/// <summary>
		/// Load a UMA recipe into the avatar.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		public virtual void Load(UMARecipeBase umaRecipe)
		{
			Load(umaRecipe, null);
		}
		/// <summary>
		/// Load a UMA recipe and additional recipes into the avatar.
		/// </summary>
		/// <param name="umaRecipe">UMA recipe.</param>
		/// <param name="umaAdditionalRecipes">Additional recipes.</param>
		public virtual void Load(UMARecipeBase umaRecipe, params UMARecipeBase[] umaAdditionalRecipes)
		{
			if (umaRecipe == null)
            {
                return;
            }

            if (umaData == null)
			{
				Initialize();
			}
			Profiler.BeginSample("Load");

			this.serializedRecipe = umaRecipe;

			umaRecipe.Load(umaData.umaRecipe);
			umaData.AddAdditionalRecipes(umaAdditionalRecipes);

			if (umaRace != umaData.umaRecipe.raceData)
			{
				UpdateNewRace();
			}
			else
			{
				UpdateSameRace();
			}
			Profiler.EndSample();
		}

		public void UpdateSameRace()
		{
#if SUPER_LOGGING
			Debug.Log("UpdateSameRace on DynamicCharacterAvatar: " + gameObject.name);
#endif
			umaData.Dirty(true, true, true);
		}

		public void UpdateNewRace()
		{
#if SUPER_LOGGING
			Debug.Log("UpdateNewRace on DynamicCharacterAvatar: " + gameObject.name);
#endif
			umaRace = umaData.umaRecipe.raceData;
			Dirty(true, true, true);
		}

		public virtual void HideAndCleanup()
		{
			HideAndCleanup(true);
		}

		/// <summary>
		/// Hide the avatar and clean up its components.
		/// </summary>
		public virtual void HideAndCleanup(bool DestroyRoot = true)
		{
			if (umaData != null)
			{
				umaData.CleanTextures();
				// GeneratedMaterials is runtime-only. Retaining its renderer asset
				// list across a domainless play-mode reset causes the next build to
				// allocate another renderer for stale material state.
				umaData.generatedMaterials = new UMAData.GeneratedMaterials();
				umaData.CleanMesh(true);
				umaData.CleanAvatar();
				if (DestroyRoot)
				{
					UMAUtils.DestroySceneObject(umaData.umaRoot);
					umaData.umaRoot = null;
					umaData.skeleton = null;
				}
				umaData.SetRenderers(null);
				umaData.SetRendererAssets(null);
				umaData.animator = null;
				umaData.firstBake = true;
			}
			umaRace = null;
		}

		/// <summary>
		/// Load the avatar recipe and create components.
		/// </summary>
		public virtual void ShowAndLoad()
		{
			if (serializedRecipe != null)
			{
				Load(serializedRecipe);
			}
			else
			{
				if (umaRace != umaData.umaRecipe.raceData)
				{
					UpdateNewRace();
				}
				else
				{
					UpdateSameRace();
				}
			}
		}

		void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.white;
			Gizmos.DrawWireCube(transform.position, new Vector3(0.6f, 0.2f, 0.6f));
		}
	}
}
