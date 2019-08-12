using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Examples{

	[System.Serializable]
	public class JiggleElement {

		public Transform Bone;
		//Note: these three Vectors define the bone direction, desired up vector and an extra rotation to compensate for UMA bones.
		//Note: these default values are for the female rig.
		//Note: the values included here are defaults for breast setup as assumed the most common.
		public string BoneType;
		//public Vector3 BoneAxis = new Vector3(-1, 0, 0);//outer breast needs 0,0,1
		//public Vector3 UpDirection = new Vector3(0, 0, -1);//outer breast needs -1,0,0
		public Vector3 BoneAxis = new Vector3(0, 0, 1);//outer breast needs 0,0,1
		public Vector3 UpDirection = new Vector3(-1, 0, 0);//outer breast needs -1,0,0
		public Vector3 ExtraRotation = new Vector3(67, 0, -90);//left inner breast looks better with y=5, right with y=-5; left inner with outer: 60,-273,0; right inner with outer 90,-215,0
		public float Stiffness;
		public float Mass;
		public float Damping;
		public float Gravity;
		public bool SquashAndStretch = true;
		public float SideStretch;
		public float FrontStretch;
		public float AnatomyScaleFactor = 1f;
		public Vector3 Force = Vector3.zero;
		public Vector3 Velocity = Vector3.zero;
		public Vector3 Acceleration = Vector3.zero;
		public Vector3 DynamicPosition = Vector3.zero;
	}
}


