using UnityEngine;
using System.Collections;

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
}
