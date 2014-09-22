using UnityEngine;
using System.Collections;

public class UMATestEventsScript : MonoBehaviour 
{
	public void CreatedEvent(UMA.UMAData data)
	{
		Debug.Log("UMA Created", data.gameObject);
	}
	public void DestroyedEvent(UMA.UMAData data)
	{
		Debug.Log("UMA Destoyed", data.gameObject);
	}
	public void UpdatedEvent(UMA.UMAData data)
	{
		Debug.Log("UMA Updated", data.gameObject);
	}
}
