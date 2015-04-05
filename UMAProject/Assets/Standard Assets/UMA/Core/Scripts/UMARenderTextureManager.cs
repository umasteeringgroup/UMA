using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Utility class to aid with DirectX context switching texture losses.
	/// </summary>
	public class UMARenderTextureManager : MonoBehaviour 
	{
		Dictionary<UMAData, RenderTexture> allUMACharacters;
		RenderTexture anyRT;
		int updatingCount;

		public void OnCreated(UMAData umaData)
		{
			var RT = umaData.GetFirstRenderTexture();
			if( RT == null) return;
			if (allUMACharacters == null) allUMACharacters = new Dictionary<UMAData, RenderTexture>();
			allUMACharacters.Add(umaData, RT);
			anyRT = RT;
			if (!enabled)
			{
				enabled = true;
			}
		}

		public void OnUpdate(UMAData umaData)
		{
			if (updatingCount > 0) updatingCount--;
			var RT = umaData.GetFirstRenderTexture();
			if (RT == null) return;
			if (allUMACharacters == null) allUMACharacters = new Dictionary<UMAData, RenderTexture>();

			allUMACharacters[umaData] = RT;
			anyRT = RT;
			if (!enabled)
			{
				enabled = true;
			}
		}

		public void OnDestroyed(UMAData umaData)
		{
			if (allUMACharacters != null)
			{
				RenderTexture rt;
				if (allUMACharacters.TryGetValue(umaData, out rt))
				{
					allUMACharacters.Remove(umaData);
					if (anyRT == rt)
					{
						anyRT = null;
					}					
				}				
			}
		}

		public void Update()
		{
			if (updatingCount > 0) return;
			if (anyRT == null)
			{
				if (allUMACharacters != null && allUMACharacters.Count > 0)
				{
					var enumerator = allUMACharacters.GetEnumerator();
					while (enumerator.MoveNext())
					{
						anyRT = enumerator.Current.Value;
						if (anyRT != null) break;
					}
					if (anyRT == null)
					{
						enabled = false;
						return;
					}
				}
				else
				{
					enabled = false;
					return;
				}
			}
			if( !anyRT.IsCreated() )
			{
				RebuildAllTextures();
			}
		}

		private void RebuildAllTextures()
		{
			if (allUMACharacters != null)
			{
				updatingCount = allUMACharacters.Count;
				foreach (var entry in allUMACharacters)
				{
					entry.Key.Dirty(false, true, false);
				}
			}
		}

	}
}