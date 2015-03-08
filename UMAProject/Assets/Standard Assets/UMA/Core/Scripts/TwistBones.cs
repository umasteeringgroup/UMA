using UnityEngine;
using System.Collections;

public class TwistBones : MonoBehaviour {
	public float twistValue;
	
	public Transform[] twistBone;
	public Transform[] refBone;
	
	private float[] originalRefRotation;
	public float[] twistRotation;
	private Vector3 rotated;
	
	// Use this for initialization
	void Awake () {
		twistRotation = new float[twistBone.Length];
		originalRefRotation = new float[twistBone.Length];
		for(int i = 0; i < twistBone.Length; i++){
			rotated = refBone[i].localRotation * Vector3.up;
			originalRefRotation[i] = Mathf.Atan2(rotated.z, rotated.y) * Mathf.Rad2Deg;
		}
	}
	
	// Update is called once per frame
	void LateUpdate () {
		for(int i = 0; i < twistBone.Length; i++){
			rotated = refBone[i].localRotation * Vector3.up;
			twistRotation[i] = Mathf.DeltaAngle(originalRefRotation[i],Mathf.Atan2(rotated.z, rotated.y) * Mathf.Rad2Deg);
			twistBone[i].localEulerAngles = Vector3.right * Mathf.Lerp(0.0f, twistRotation[i],twistValue);
		}
	}
}