using UnityEngine;
using System.Collections.Generic;

namespace UMA {
public abstract class OverlayLibraryBase : MonoBehaviour
{
	public abstract void AddOverlay(OverlayData overlay);
	public abstract OverlayData InstantiateOverlay(string name);
	public abstract OverlayData InstantiateOverlay(int nameHash);
	public abstract OverlayData InstantiateOverlay(string name, Color color);
	public abstract OverlayData InstantiateOverlay(int nameHash, Color color);

	public abstract void UpdateDictionary();
}
}
