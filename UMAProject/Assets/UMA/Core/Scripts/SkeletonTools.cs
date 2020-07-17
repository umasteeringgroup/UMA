using UnityEngine;

namespace UMA
{
	public static class SkeletonTools
	{
#if UNITY_EDITOR
		[UnityEditor.MenuItem("UMA/Verify Slot Mesh")]
		static void Start()
		{
			var transforms = UnityEditor.Selection.GetTransforms(UnityEditor.SelectionMode.Editable);
			if (transforms.Length != 2)
			{
				if(Debug.isDebugBuild)
					Debug.LogError("To Compare Skeletons you need to select two characters in your hierarchy.");
				return;
			}

			var root1 = LocateRoot(transforms[0]);
			var root2 = LocateRoot(transforms[1]);
			int failure = 0;
			CompareSkeletonRecursive(root1, root2, ref failure);
		}
#endif

		private static void CompareRootBone(Transform raceRoot, Transform slotRoot, ref int failure)
		{
			var rootIterator = slotRoot;
			while (rootIterator.parent != null)
			{
				rootIterator = rootIterator.parent;
			}
			if (RecursiveFindBone(rootIterator, raceRoot) == null)
			{
				if (Debug.isDebugBuild)
					Debug.LogError("Race root: " + raceRoot.name + " not found in the slot hierarchy");
				failure++;
			}
		}

		private static Transform RecursiveFindBone(Transform bone, Transform raceRoot)
		{
			if (bone.name == raceRoot.name) return bone;
			for(int i = 0; i < bone.childCount; i++)
			{
				var result = RecursiveFindBone(bone.GetChild(i), raceRoot);
				if (result != null)
					return result;
			}
			return null;
		}

		public static Transform RecursiveFindBone(Transform bone, string Name)
		{
			if (bone.name == Name) return bone;
			for (int i = 0; i < bone.childCount; i++)
			{
				var result = RecursiveFindBone(bone.GetChild(i), Name);
				if (result != null)
					return result;
			}
			return null;
		}

		private static void CompareSkeletonRecursive(Transform race, Transform slot, ref int failure)
		{
			if ((race.localScale - slot.localScale).sqrMagnitude > 0.0001f)
			{
				failure++;
				if (Debug.isDebugBuild)
					Debug.LogError("Scale on " + race.name + " differs by " + (race.localScale - slot.localScale), slot);
			}
			if ((race.localPosition - slot.localPosition).sqrMagnitude > 0.0001f)
			{
				failure++;
				if (Debug.isDebugBuild)
					Debug.LogError("Position on " + race.name + " differs by " + (race.localPosition - slot.localPosition), slot);
			}
			if (race.localRotation != slot.localRotation)
			{
				failure++;
				if (Debug.isDebugBuild)
					Debug.LogError("Rotation on " + race.name + " differs by " + Quaternion.Angle(race.localRotation, slot.localRotation) + " degrees", slot);
			}
			for (int i = 0; i < race.childCount; i++)
			{
				var raceChild = race.GetChild(i);
				var slotChild = slot.Find(raceChild.name);
				if (slotChild != null)
				{
					CompareSkeletonRecursive(raceChild, slotChild, ref failure);
				}
				else
				{
					failure++;
					if (Debug.isDebugBuild)
						Debug.LogError("Bone is missing: " + raceChild.name + " on bone: " + slot.name, slot);
				}
				if (failure >= 50) return;
			}
		}

		public static Transform LocateRoot(Transform parent)
		{
			for (int i = 0; i < parent.childCount; i++)
			{
				var child = parent.GetChild(i);
				if (child.childCount == 0) continue;
				return child;
			}
			return null;
		}

		public enum ValidateResult
		{
			Ok,
			InvalidScale,
			SkeletonProblem,
		}

		public static ValidateResult ValidateSlot(SkinnedMeshRenderer RaceSMR, SkinnedMeshRenderer SlotSMR, out string description)
		{
			var slotMesh = new Mesh();
#if UMA_32BITBUFFERS
				slotMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
#endif
			SlotSMR.BakeMesh(slotMesh);
			var bounds = slotMesh.bounds;
			if (bounds.max.y < 0.05f)
			{
				description = "Scale Factor on the Model Import Settings seems to be wrong!";
				return ValidateResult.InvalidScale;
			}

			int failure = 0;
			CompareSkeletonRecursive(LocateRoot(RaceSMR.transform.parent), LocateRoot(SlotSMR.transform.parent), ref failure);
			CompareRootBone(RaceSMR.rootBone, SlotSMR.rootBone, ref failure);

			if (failure > 0)
			{
				description = "The Skeleton Hierarchy seems off, check the log for more info.";

				return ValidateResult.SkeletonProblem;
			}

			description = "Everything seems fine.";
			return ValidateResult.Ok;
		}

		public static void ForceSkeleton(SkinnedMeshRenderer SourceSMR, SkinnedMeshRenderer DestSMR)
		{
			ForceSkeletonRecursive(LocateRoot(SourceSMR.transform.parent), LocateRoot(DestSMR.transform.parent));
		}

		private static void ForceSkeletonRecursive(Transform source, Transform dest)
		{
			dest.localScale = source.localScale;
			dest.localPosition = source.localPosition;
			dest.localRotation = source.localRotation;
			for (int i = 0; i < source.childCount; i++)
			{
				var raceChild = source.GetChild(i);
				var slotChild = dest.Find(raceChild.name);
				if (slotChild != null)
				{
					ForceSkeletonRecursive(raceChild, slotChild);
				}
			}
		}

	}
}