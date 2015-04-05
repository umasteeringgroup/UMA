//	============================================================
//	Name:		UMABonePose
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;

namespace UMA.PoseTools
{
	/// <summary>
	/// UMA bone pose.
	/// </summary>
	/// <remarks>
	/// A bone pose is a collection of position, rotation, and scale data
	/// which can be applied to the transforms of a skinned mesh to manipulate
	/// the mesh shape as an alternative to a vertex based blendshape.
	/// </remarks>
	[System.Serializable]
	public class UMABonePose : ScriptableObject
	{
		/// <summary>
		/// Pose data for a single transform.
		/// </summary>
		[System.Serializable]
		public class PoseBone
		{
			public string bone;
			public int hash;

			public Vector3 position;
			public Quaternion rotation;
			public Vector3 scale;
		}

		/// <summary>
		/// The set of transfrom changes needed for this pose.
		/// </summary>
		public PoseBone[] poses;

		/// <summary>
		/// Extra poses to be used when this pose is partially applied.
		/// </summary>
		/// <remarks>
		/// Tween poses can be required where a linear path from the base
		/// to the applied pose would cause animation errors.
		/// For example, the tongue out pose will go through the teeth
		/// unless there is an additional pose of it partially extended.
		/// </remarks>
		public UMABonePose[] tweenPoses = null;
		public float[] tweenWeights = null;

		void Reset()
		{
			poses = new PoseBone[0];
		}

		void OnEnable()
		{
			if (poses == null)
			{
				poses = new PoseBone[0];
			}

			foreach (PoseBone pose in poses)
			{
				if (pose.hash == 0)
				{
					pose.hash = UMAUtils.StringToHash(pose.bone);
				}
			}
		}

		public int PoseCount()
		{
			if (poses != null)
			{
				return poses.Length;
			}

			return 0;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Adds a transform into the pose. Editor only.
		/// </summary>
		/// <param name="bone">Transform.</param>
		/// <param name="position">Position.</param>
		/// <param name="rotation">Rotation.</param>
		/// <param name="scale">Scale.</param>
		public void AddBone(Transform bone, Vector3 position, Quaternion rotation, Vector3 scale)
		{
			PoseBone pose = new PoseBone();
			pose.bone = bone.name;
			pose.hash = UMAUtils.StringToHash(bone.name);
			pose.position = position - bone.localPosition;
			pose.rotation = Quaternion.Inverse(bone.localRotation) * rotation;
			pose.scale = new Vector3(scale.x / bone.localScale.x,
									scale.y / bone.localScale.y,
									scale.z / bone.localScale.z);

			ArrayUtility.Add(ref poses, pose);
		}
#endif

		protected float ApplyPoseTweens(UMASkeleton umaSkeleton, float weight)
		{
			int tweenCount = tweenPoses.Length;
			if (tweenWeights.Length != tweenCount)
			{
				Debug.LogError("Tween pose / weight mismatch!");
				return weight;
			}

			// weight <= first tween weight
			if (weight <= tweenWeights[0])
			{
				weight = weight / tweenWeights[0];
				tweenPoses[0].ApplyPose(umaSkeleton, weight);
				return 0f;
			}
			// weight >= last tween weight
			else if (weight >= tweenWeights[tweenCount - 1])
			{
				float weightRange = 1f - tweenWeights[tweenCount - 1];
				float lowerWeight = (1f - weight) / weightRange;
				tweenPoses[tweenCount - 1].ApplyPose(umaSkeleton, lowerWeight);
				return (1f - lowerWeight);
			}
			// first tween weight < weight < last tween weight
			else
			{
				int tween = 1;
				while (weight > tweenWeights[tween])
				{
					tween++;
				}

				float lowerWeight = tweenWeights[tween - 1];
				float upperWeight = tweenWeights[tween];
				float weightRange = upperWeight - lowerWeight;
				lowerWeight = (upperWeight - weight) / weightRange;
				tweenPoses[tween - 1].ApplyPose(umaSkeleton, lowerWeight);
				upperWeight = 1f - lowerWeight;
				tweenPoses[tween].ApplyPose(umaSkeleton, upperWeight);
				return 0f;
			}
		}

		/// <summary>
		/// Applies the pose to the given skeleton.
		/// </summary>
		/// <remarks>
		/// LERP the pose onto a skeleton at the given strength.
		/// Weight is normally in the 0-1 range but is not clamped.
		/// </remarks>
		/// <param name="umaSkeleton">Skeleton.</param>
		/// <param name="weight">Weight.</param>
		public void ApplyPose(UMASkeleton umaSkeleton, float weight)
		{
			if ((poses == null) || (umaSkeleton == null))
			{
				Debug.LogError("Missing poses or skeleton!");
				return;
			}

			if ((tweenPoses != null) && (tweenPoses.Length > 0) && (weight < 1f))
			{
				weight = ApplyPoseTweens(umaSkeleton, weight);
			}

			if (weight <= 0f)
			{
				return;
			}

			foreach (PoseBone pose in poses)
			{
				umaSkeleton.Lerp(pose.hash, pose.position, pose.scale, pose.rotation, weight);
			}
		}

		static private void RecurseTransformsInPrefab(Transform root, List<Transform> transforms)
		{
			for (int i = 0; i < root.childCount; i++)
			{
				Transform child = root.GetChild(i);
				transforms.Add(child);
				RecurseTransformsInPrefab(child, transforms);
			}
		}

		static public Transform[] GetTransformsInPrefab(Transform prefab)
		{
			List<Transform> transforms = new List<Transform>();

			RecurseTransformsInPrefab(prefab, transforms);

			return transforms.ToArray();
		}
	}
}
