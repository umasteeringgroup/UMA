using UnityEngine;
using System.Collections;

public class CameraTrack : MonoBehaviour {
	
	public Transform target;
	
	void LateUpdate () {
		if(target){
			Vector3 relative = transform.InverseTransformPoint(target.position);
			float angle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
			transform.Rotate(0, angle, 0,Space.World);
		}
	}
}