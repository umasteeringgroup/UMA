using UnityEngine;
using UnityEngine.Profiling;

namespace UMA
{
	/// <summary>
	/// Base class for UMA character.
	/// </summary>
	public abstract class UMAAvatarBase : MonoBehaviour
	{
		public UMAContext context;
		public UMAData umaData;
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

		public virtual void Start()
		{
			Initialize();
		}
		public void Initialize()
		{
			if (context == null)
			{
				context = UMAContext.FindInstance();
			}

			if (umaData == null)
			{
				umaData = GetComponent<UMAData>();
				if (umaData == null)
				{
					umaData = gameObject.AddComponent<UMAData>();
					if (umaGenerator != null && !umaGenerator.gameObject.activeInHierarchy)
					{
						Debug.LogError("Invalid UMA Generator on Avatar.", gameObject);
						Debug.LogError("UMA generators must be active scene objects!", umaGenerator.gameObject);
						umaGenerator = null;
					}
				}
			}
			if (umaGenerator != null)
			{
				umaData.umaGenerator = umaGenerator;
			}
			if (CharacterCreated != null) umaData.CharacterCreated = CharacterCreated;
			if (CharacterBegun != null) umaData.CharacterBegun = CharacterBegun;
			if (CharacterDestroyed != null) umaData.CharacterDestroyed = CharacterDestroyed;
			if (CharacterUpdated != null) umaData.CharacterUpdated = CharacterUpdated;
			if (CharacterDnaUpdated != null) umaData.CharacterDnaUpdated = CharacterDnaUpdated;
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
			if (umaRecipe == null) return;
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
			umaData.Dirty(true, true, true);
		}

		public void UpdateNewRace()
		{
			umaRace = umaData.umaRecipe.raceData;
			if (animationController != null)
			{
				umaData.animationController = animationController;
	//				umaData.animator.runtimeAnimatorController = animationController;
			}
			umaData.umaGenerator = umaGenerator;

			umaData.Dirty(true, true, true);
		}

		/// <summary>
		/// Hide the avatar and clean up its components.
		/// </summary>
		public virtual void Hide()
		{
			if (umaData != null)
			{
				umaData.CleanTextures();
				umaData.CleanMesh(true);
				umaData.CleanAvatar();
				UMAUtils.DestroySceneObject(umaData.umaRoot);
				umaData.umaRoot = null;
				umaData.SetRenderers(null);
				umaData.animator = null;
				umaData.firstBake = true;
				umaData.skeleton = null;
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
