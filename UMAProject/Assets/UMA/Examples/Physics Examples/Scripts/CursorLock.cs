using UnityEngine;
using System.Collections;

public class CursorLock : MonoBehaviour {

	// Use this for initialization
	void Start () {

	}

	void OnApplicationFocus(bool hasFocus )
	{
		if( hasFocus )
			LockMouse ();
	}

	void LockMouse()
	{
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}
	
}
