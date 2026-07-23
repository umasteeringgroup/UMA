//	============================================================
//	Name:		UMABonePose
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;

namespace UMA.PoseTools
{
	[System.Serializable]
	public class UMABonePose : ScriptableObject
	{
		[System.Serializable]
		public class PoseBone
		{
			public string bone;
			public int hash;
			public Vector3 position;
			public Quaternion rotation;
			public Vector3 scale;
			public string category;
			public bool enabled = true;
		}

		public PoseBone[] poses;

		public UMABonePose[] tweenPoses = null;
		public float[] tweenWeights = null;

		// If true, this pose is intended for use as a mixer pose, and will be available for selection in the race wizard's wizard pose creator. This is just a hint and does not affect runtime behavior.
		public bool mixerPose = false;

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
			for (int i = 0; i < poses.Length; i++)
			{
				var p = poses[i];
				if (p.hash == 0 && !string.IsNullOrEmpty(p.bone))
				{
					p.hash = UMAUtils.StringToHash(p.bone);
				}
			}
			// Optional: clean tweens on load
			SanitizeTweens();
		}

		public int PoseCount()
		{
			return poses != null ? poses.Length : 0;
		}

#if UNITY_EDITOR
		public void AddBone(Transform bone, Vector3 position, Quaternion rotation, Vector3 scale, string category)
		{
			PoseBone pose = new PoseBone
			{
				bone = bone.name,
				hash = UMAUtils.StringToHash(bone.name),
				position = position - bone.localPosition,
				rotation = Quaternion.Inverse(bone.localRotation) * rotation,
				scale = new Vector3(
					safeDiv(scale.x, bone.localScale.x),
					safeDiv(scale.y, bone.localScale.y),
					safeDiv(scale.z, bone.localScale.z)),
				category = category,
				enabled = true
			};
			ArrayUtility.Add(ref poses, pose);

			static float safeDiv(float a, float b) => Mathf.Approximately(b, 0f) ? 1f : a / b;
		}

		[MenuItem("Assets/Create/UMA/DNA/Bone Pose")]
		public static void CreateBonePoseAsset()
		{
			UMA.CustomAssetUtility.CreateAsset<UMABonePose>();
		}
#endif

		private void SanitizeTweens()
		{
			if (tweenPoses == null || tweenPoses.Length == 0)
			{
				return;
			}

			// Build compact lists skipping missing/null
			List<UMABonePose> validPoses = new List<UMABonePose>();
			List<float> validWeights = new List<float>();

			for (int i = 0; i < tweenPoses.Length; i++)
			{
				var tp = tweenPoses[i];
				bool missing = tp == null;
				// Unity MissingReference check
				if (!missing && tp.Equals(null))
				{
					missing = true;
				}

				if (!missing)
				{
					validPoses.Add(tp);
					// Match weight index if available
					if (tweenWeights != null && i < tweenWeights.Length)
					{
						validWeights.Add(tweenWeights[i]);
					}
				}
			}

			bool changed = validPoses.Count != tweenPoses.Length;
			if (changed)
			{
				tweenPoses = validPoses.ToArray();
				tweenWeights = validWeights.ToArray();
#if UNITY_EDITOR
				if (Debug.isDebugBuild)
				{
					//Debug.LogWarning($"[UMABonePose] Removed missing tween references in '{name}'.");
				}
				EditorUtility.SetDirty(this);
#endif
			}

			// Final consistency check
			if (tweenPoses.Length == 0)
			{
				tweenWeights = null;
			}
		}

		protected float ApplyPoseTweens(UMASkeleton umaSkeleton, float weight)
		{
			// Guard and sanitize once
			SanitizeTweens();

			if (tweenPoses == null || tweenPoses.Length == 0 || tweenWeights == null || tweenWeights.Length == 0)
			{
				return weight;
			}

			int tweenCount = tweenPoses.Length;
			if (tweenWeights.Length != tweenCount)
			{
#if UNITY_EDITOR
				if (Debug.isDebugBuild)
				{
					Debug.LogError($"[UMABonePose] Tween pose/weight mismatch on '{name}'. Skipping tween interpolation.");
				}
#endif
				return weight;
			}

			// Ensure weights are sorted ascending (defensive)
			bool sorted = true;
			for (int i = 1; i < tweenWeights.Length; i++)
			{
				if (tweenWeights[i] < tweenWeights[i - 1])
				{
					sorted = false;
					break;
				}
			}
			if (!sorted)
			{
				var pairs = new List<(float w, UMABonePose p)>();
				for (int i = 0; i < tweenCount; i++)
				{
					pairs.Add((tweenWeights[i], tweenPoses[i]));
				}
				pairs.Sort((a, b) => a.w.CompareTo(b.w));
				for (int i = 0; i < tweenCount; i++)
				{
					tweenWeights[i] = pairs[i].w;
					tweenPoses[i] = pairs[i].p;
				}
			}

			// If any tween pose missing after sanitize, skip safely
			for (int i = 0; i < tweenCount; i++)
			{
				if (tweenPoses[i] == null || tweenPoses[i].Equals(null))
				{
					return weight;
				}
			}

			// weight <= first tween weight
			if (weight <= tweenWeights[0])
			{
				float adj = weight / Mathf.Max(tweenWeights[0], Mathf.Epsilon);
				tweenPoses[0].ApplyPose(umaSkeleton, adj);
				return 0f;
			}
			// weight >= last tween weight
			int last = tweenCount - 1;
			if (weight >= tweenWeights[last])
			{
				float weightRange = 1f - tweenWeights[last];
				float lowerWeight = weightRange > Mathf.Epsilon ? (1f - weight) / weightRange : 0f;
				tweenPoses[last].ApplyPose(umaSkeleton, lowerWeight);
				return 1f - lowerWeight;
			}

			// Between weights
			int idx = 1;
			while (idx < tweenCount && weight > tweenWeights[idx])
			{
				idx++;
			}
			int upperIndex = idx;
			int lowerIndex = idx - 1;
			float lowerW = tweenWeights[lowerIndex];
			float upperW = tweenWeights[upperIndex];
			float span = Mathf.Max(upperW - lowerW, Mathf.Epsilon);
			float tUpper = (weight - lowerW) / span;
			float tLower = 1f - tUpper;

			tweenPoses[lowerIndex].ApplyPose(umaSkeleton, tLower);
			tweenPoses[upperIndex].ApplyPose(umaSkeleton, tUpper);
			return 0f;
		}

		public void ApplyPose(UMASkeleton umaSkeleton, float weight)
		{
			if (umaSkeleton == null || poses == null)
			{
#if UNITY_EDITOR
				if (Debug.isDebugBuild)
				{
					/*Debug.LogError($"[UMABonePose] Missing skeleton or pose data on '{name}'."); */
				}
#endif
				return;
			}

			if (Mathf.Approximately(weight, 0f))
			{
				return;
			}

			// Interpolate through tweens if provided (only for positive weights; tween curves aren't designed for inverse interpolation).
			if (weight > 0f && tweenPoses != null && tweenPoses.Length > 0 && weight < 1f)
			{
				weight = ApplyPoseTweens(umaSkeleton, weight);
				// If tweens consumed entire weight (returned 0) exit early
				if (weight <= 0f) return;
			}

			for (int i = 0; i < poses.Length; i++)
			{
				var pb = poses[i];
				if (pb == null || !pb.enabled) continue;
				umaSkeleton.Morph(pb.hash, pb.position, pb.scale, pb.rotation, weight);
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
