using UnityEngine;

namespace UMA
{
	/// <summary>
	/// UMA avatar which can automatically load on start.
	/// </summary>
	public class UMADynamicAvatar : UMAAvatarBase
	{
		public bool loadOnStart;
		public  void Start()
		{
			if (loadOnStart)
			{
				DynamicLoad();
			}
		}

		public void DynamicLoad()
		{
				if (umaAdditionalRecipes == null || umaAdditionalRecipes.Length == 0)
				{
					Load(serializedRecipe);
				}
				else
				{
					Load(serializedRecipe, umaAdditionalRecipes);
				}
			}

	#if UNITY_EDITOR
		[UnityEditor.MenuItem("GameObject/UMA/Create New Dynamic Avatar", false, 10)]
		static void CreateDynamicAvatarMenuItem()
		{
			var res = new GameObject("New Dynamic Avatar");
			res.AddComponent<UMADynamicAvatar>();
			UnityEditor.Selection.activeGameObject = res;
		}
	#endif
	}
}
