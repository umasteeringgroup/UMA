using UnityEngine;

namespace UMA
{
	public class UMAGeneratorGLib : UMAGeneratorBuiltin
	{
		[Tooltip("Turn this on to auto cleanup items that are not currently used. WARNING. This will cause behavior changes, and may cause issues if you are not careful.")]
		public bool EnableCacheCleanup;

		[Tooltip("Number of seconds to keep cached items")]
		public float CachedItemsLife = 3.0f;
		
		public override void Awake()
		{
			base.Awake();
		}

		public override void Update()
		{
			base.Update();
#if UMA_ADDRESSABLES
			if (EnableCacheCleanup)
			{
				UMAAssetIndexer.DefaultLife = CachedItemsLife;
				UMAAssetIndexer.Instance.CheckCache();
			}
#endif
		}
	}
}