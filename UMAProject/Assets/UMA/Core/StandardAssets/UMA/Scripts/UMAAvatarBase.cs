using UnityEngine;
using UnityEngine.Profiling;

namespace UMA
{
	/// <summary>
	/// Base class for UMA character.
	/// </summary>
	public abstract class UMAAvatarBase : MonoBehaviour
	{
		public UMAContextBase context;
		public UMAData umaData;
		[Tooltip("The default renderer asset to use for this avatar. This lets you set parameters for the generated SkinnedMeshRenderer")]
		public UMARendererAsset defaultRendererAsset; // this can be null if no default renderers need to be applied.

		/// <summary>
		/// The serialized basic UMA recipe.
		/// </summary>
		public UMARecipeBase umaRecipe;
		/// <summary>
		/// Additional partial UMA recipes (not serialized).
		/// </summary>
		public UMARecipeBase[] umaAdditionalRecipes;
		public UMAGeneratorBase umaGenerator;
		public RuntimeAnimatorController animationController;

		protected RaceData umaRace = null;

		/// <summary>
		/// Callback event when character is created.
		/// </summary>
		public UMADataEvent CharacterCreated;
		/// <summary>
		/// Callback event when character is started.
		/// </summary>
		public UMADataEvent CharacterBegun;
		/// <summary>
		/// Callback event when character is destroyed.
		/// </summary>
		public UMADataEvent CharacterDestroyed;
		/// <summary>
		/// Callback event when character is updated.
		/// </summary>
		public UMADataEvent CharacterUpdated;
		/// <summary>
		/// Callback event when character DNA is updated.
		/// </summary>
		public UMADataEvent CharacterDnaUpdated;

		public UMADataEvent AnimatorStateSaved;
		public UMADataEvent AnimatorStateRestored;

		public virtual void Start()
		{
			Initialize();
		}
		public void Initialize()
		{
			if (context == null)
			{
				context = UMAContextBase.Instance;
			}

			if (umaData == null)
			{
				umaData = GetComponent<UMAData>();
				if (umaData == null)
				{
					umaData = gameObject.AddComponent<UMAData>();
					umaData.umaRecipe = new UMAData.UMARecipe(); // TEST JRRM
					if (umaGenerator != null && !umaGenerator.gameObject.activeInHierarchy)
					{
						if (Debug.isDebugBuild)
						{
							Debug.LogError("Invalid UMA Generator on Avatar.", gameObject);
							Debug.LogError("UMA generators must be active scene objects!", umaGenerator.gameObject);
						}
						umaGenerator = null;
					}
				}
			}
			if (umaGenerator != null)
			{
				umaData.umaGenerator = umaGenerator;
			}
			
			if (CharacterCreated != null)
            {
                umaData.CharacterCreated = CharacterCreated;
            }

            if (CharacterBegun != null)
            {
                umaData.CharacterBegun = CharacterBegun;
            }

            if (CharacterDestroyed != null)
            {
                umaData.CharacterDestroyed = CharacterDestroyed;
            }

            if (CharacterUpdated != null)
            {
                umaData.CharacterUpdated = CharacterUpdated;
            }

            if (CharacterDnaUpdated != null)
            {
                umaData.CharacterDnaUpdated = CharacterDnaUpdated;
            }

            if (AnimatorStateSaved != null)
            {
                umaData.AnimatorStateSaved = AnimatorStateSaved;
            }

            if (AnimatorStateRestored != null)
            {
                umaData.AnimatorStateRestored = AnimatorStateRestored;
            }
        }

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

			this.umaRecipe = umaRecipe;

			umaRecipe.Load(umaData.umaRecipe, context);
			umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);

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
			if (animationController != null)
			{
				umaData.animationController = animationController;
			}
			umaData.Dirty(true, true, true);
		}

		public void UpdateNewRace()
		{
#if SUPER_LOGGING
			Debug.Log("UpdateNewRace on DynamicCharacterAvatar: " + gameObject.name);
#endif

			umaRace = umaData.umaRecipe.raceData;
			if (animationController != null)
			{
				umaData.animationController = animationController;
			}

			umaData.umaGenerator = umaGenerator;

			umaData.Dirty(true, true, true);
		}

		public virtual void Hide()
		{
			Hide(true);
		}

		/// <summary>
		/// Hide the avatar and clean up its components.
		/// </summary>
		public virtual void Hide(bool DestroyRoot = true)
		{
			if (umaData != null)
			{
				umaData.CleanTextures();
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
		public virtual void Show()
		{
			if (umaRecipe != null)
			{
				Load(umaRecipe);
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
