using UnityEngine;
using System.Collections;

namespace UMA.Dynamics.Examples
{
	public class CursorLock : MonoBehaviour
	{
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
}
