using UnityEngine;

namespace UMA.Dynamics
{
	[System.Serializable]
	public class UMAPhysicsElement : ScriptableObject 
	{
		[Tooltip("Set to true for root hip definition only")]
		public bool isRoot = false;
		[Tooltip("Name of the bone to add physics")]
		public string boneName;
		[Tooltip("The mass of the bone in kilograms")]
		public float mass;

		[Header("Collider Settings")]
		public ColliderDefinition[] colliders;

		//Joint Definition
		[Header("Joint Settings")]
		public string parentBone;
		public Vector3 axis;
		public Vector3 swingAxis;
		public float lowTwistLimit;
		public float highTwistLimit;
		public float swing1Limit;
		public float swing2Limit;
		public bool enablePreprocessing;

		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/Misc/Physics Element")]
		public static void CreatePhysicsElementAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAPhysicsElement>();
		}
		#endif
	}
}
