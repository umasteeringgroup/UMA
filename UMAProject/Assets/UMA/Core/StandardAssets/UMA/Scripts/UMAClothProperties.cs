using UnityEngine;
using System;

namespace UMA
{
	[Serializable]
	public class UMAClothProperties : ScriptableObject
	{
		#if UNITY_EDITOR
		[UnityEditor.MenuItem("Assets/Create/UMA/Misc/Cloth Properties")]
		public static void CreateClothPropertiesAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMAClothProperties>();
		}
		#endif

		public float bendingStiffness;
		public float clothSolverFrequency;
		public float collisionMassScale;
		public float damping;
		public bool enableContinuousCollision;
		//public bool enableTethers;
		public float friction;
		public float sleepThreshold;
		public float stretchingStiffness;
		public bool useGravity;
		public float useVirtualParticles;
        public Vector3 externalAcceleration;
        public Vector3 randomAcceleration;
		public float worldAccelerationScale;
		public float worldVelocityScale;

		public void ApplyValues(Cloth cloth)
		{
			cloth.bendingStiffness = bendingStiffness;
			cloth.clothSolverFrequency = clothSolverFrequency;
			cloth.collisionMassScale = collisionMassScale;
            cloth.damping = damping;
			cloth.enableContinuousCollision = enableContinuousCollision;
			//cloth.enableTethers = enableTethers;
			cloth.friction = friction;
			cloth.sleepThreshold = sleepThreshold;
			cloth.stretchingStiffness = stretchingStiffness;
			cloth.useGravity = useGravity;
			cloth.useVirtualParticles = useVirtualParticles;
            cloth.externalAcceleration = externalAcceleration;
            cloth.randomAcceleration = randomAcceleration;
			cloth.worldAccelerationScale = worldAccelerationScale;
			cloth.worldVelocityScale = worldVelocityScale;
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(cloth);
#endif
		}

		public void ReadValues(Cloth cloth)
		{
			bendingStiffness = cloth.bendingStiffness;
			clothSolverFrequency = cloth.clothSolverFrequency;
			collisionMassScale = cloth.collisionMassScale;
			damping = cloth.damping;
			enableContinuousCollision = cloth.enableContinuousCollision;
			//enableTethers = cloth.enableTethers;
			friction = cloth.friction;
			sleepThreshold = cloth.sleepThreshold;
			stretchingStiffness = cloth.stretchingStiffness;
			useGravity = cloth.useGravity;
			useVirtualParticles = cloth.useVirtualParticles;
            externalAcceleration = cloth.externalAcceleration;
            randomAcceleration = cloth.randomAcceleration;
			worldAccelerationScale = cloth.worldAccelerationScale;
			worldVelocityScale = cloth.worldVelocityScale;
#if UNITY_EDITOR
			UnityEditor.EditorUtility.SetDirty(this);
#endif
		}
	}
}