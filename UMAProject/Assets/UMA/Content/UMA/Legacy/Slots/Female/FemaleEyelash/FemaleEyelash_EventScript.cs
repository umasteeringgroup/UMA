using UnityEngine;
using System.Collections;

namespace UMA.Assets
{
	/// <summary>
	/// Auxillary slot which will set the material color of the shared eyelash overlay.
	/// </summary>
	public class FemaleEyelash_EventScript : MonoBehaviour
	{
		public void SlotAtlasEvent(UMA.UMAData umaData, UMA.SlotData slotData, Material material, Rect atlasRect)
		{
			var overlay = slotData.GetOverlay(0);
			if (overlay != null)
			{
				material.color = overlay.colorData.color;
			}
		}
	}
}
