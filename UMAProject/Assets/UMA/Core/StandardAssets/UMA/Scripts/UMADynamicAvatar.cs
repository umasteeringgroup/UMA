using UnityEngine;

namespace UMA
{
	/// <summary>
	/// UMA avatar which can automatically load on start.
	/// </summary>
	public class UMADynamicAvatar : UMAAvatarBase
	{
		public bool loadOnStart;
		public override void Start()
		{
			base.Start();
			if (loadOnStart)
			{
				DynamicLoad();
			}
		}

		public void DynamicLoad()
		{
				if (umaAdditionalRecipes == null || umaAdditionalRecipes.Length == 0)
				{
					Load(umaRecipe);
				}
				else
				{
					Load(umaRecipe, umaAdditionalRecipes);
				}
			}

	#if UNITY_EDITOR
		[UnityEditor.MenuItem("GameObject/UMA/Create New Dynamic Avatar", false, 10)]
		static void CreateDynamicAvatarMenuItem()
		{
			var res = new GameObject("New Dynamic Avatar");
			var da = res.AddComponent<UMADynamicAvatar>();
			da.context = UMAContextBase.FindInstance();
			da.umaGenerator = Component.FindObjectOfType<UMAGeneratorBase>();
			UnityEditor.Selection.activeGameObject = res;
		}
	#endif
	}
}
