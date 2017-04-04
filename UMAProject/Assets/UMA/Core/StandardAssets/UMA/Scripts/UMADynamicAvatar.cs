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
				if (umaAdditionalRecipes == null || umaAdditionalRecipes.Length == 0)
				{
					Load(umaRecipe);
				}
				else
				{
					Load(umaRecipe, umaAdditionalRecipes);
				}
			}
		}
	#if UNITY_EDITOR
		[UnityEditor.MenuItem("GameObject/Create Other/UMA/Dynamic Avatar")]
		static void CreateDynamicAvatarMenuItem()
		{
			var res = new GameObject("New Dynamic Avatar");
			var da = res.AddComponent<UMADynamicAvatar>();
			da.context = UMAContext.FindInstance();
			da.umaGenerator = Component.FindObjectOfType<UMAGeneratorBase>();
			UnityEditor.Selection.activeGameObject = res;
		}
	#endif
	}
}
