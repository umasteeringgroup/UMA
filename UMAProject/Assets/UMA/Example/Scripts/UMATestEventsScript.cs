using UnityEngine;
using System.Collections;

/// <summary>
/// Examples of the character building callbacks.
/// </summary>
public class UMATestEventsScript : MonoBehaviour
{
	public void CreatedEvent(UMA.UMAData data)
	{
		Debug.Log(data.name + " Created", data.gameObject);
	}
	public void DestroyedEvent(UMA.UMAData data)
	{
		Debug.Log(data.name + " Destoyed", data.gameObject);
	}
	public void UpdatedEvent(UMA.UMAData data)
	{
		Debug.Log(data.name + " Updated", data.gameObject);
	}
	public void SlotAtlasEvent(UMA.UMAData umaData, UMA.SlotData slotData, Material material, Rect atlasRect)
	{
		Debug.Log(umaData.name + " got slot " + slotData.asset.slotName);
	}
}
