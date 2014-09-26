using UnityEngine;
using System.Collections;

namespace UMA.Assets
{
	public class FemaleEyelash_EventScript : MonoBehaviour
	{
		public void SlotAtlasEvent(UMA.UMAData umaData, UMA.SlotData slotData, Material material, Rect atlasRect)
		{
			var overlay = slotData.GetOverlay(0);
			if (overlay != null)
			{
				material.color = overlay.color;
			}
		}
	}
}
