using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
	public abstract class UMAGeneratorBase : MonoBehaviour
	{
		public bool fitAtlas;
		[NonSerialized]
		public TextureMerge textureMerge;
		public bool convertRenderTexture;
		public bool convertMipMaps;
		public int atlasResolution;
#if !UMA2_LEAN_AND_CLEAN 
		public string[] textureNameList;
#endif
		public abstract void addDirtyUMA(UMAData umaToAdd);
		public abstract bool IsIdle();

		public abstract int QueueSize();

		public class AnimatorState
		{
			private int[] stateHashes = new int[0];
			private float[] stateTimes = new float[0];
			bool animating = true;
			bool applyRootMotion = true;
			AnimatorUpdateMode updateMode = AnimatorUpdateMode.Normal;
			AnimatorCullingMode cullingMode = AnimatorCullingMode.AlwaysAnimate;

			public void SaveAnimatorState(Animator animator)
			{
				animating = animator.enabled;
				applyRootMotion = animator.applyRootMotion;
				updateMode = animator.updateMode;
				cullingMode = animator.cullingMode;

				int layerCount = animator.layerCount;
				stateHashes = new int[layerCount];
				stateTimes = new float[layerCount];
				for (int i = 0; i < layerCount; i++)
				{
					var state = animator.GetCurrentAnimatorStateInfo(i);
#if UNITY_4_6
					stateHashes[i] = state.nameHash;
#else
					stateHashes[i] = state.fullPathHash;
#endif
					stateTimes[i] = Mathf.Max(0, state.normalizedTime - Time.deltaTime / state.length);
				}
			}

			public void RestoreAnimatorState(Animator animator)
			{
				animator.applyRootMotion = applyRootMotion;
				animator.updateMode = updateMode;
				animator.cullingMode = cullingMode;

				if (animator.layerCount == stateHashes.Length)
				{
					for (int i = 0; i < animator.layerCount; i++)
					{
						animator.Play(stateHashes[i], i, stateTimes[i]);
					}
				}
				
				animator.Update(0);
				animator.enabled = animating;
			}
		}	

		public virtual void UpdateAvatar(UMAData umaData)
		{
			if (umaData)
			{
				AnimatorState snapshot = new AnimatorState();
				if (umaData.animationController != null)
				{
					var animator = umaData.animator;
					if (animator != null)
					{
						if (umaData.animationController == animator.runtimeAnimatorController)
						{
							snapshot = new AnimatorState();
							snapshot.SaveAnimatorState(animator);
						}

						Avatar avatar = animator.avatar;
						Object.DestroyImmediate(animator);
						Object.Destroy(avatar);
					}
					var oldParent = umaData.umaRoot.transform.parent;
					var originalRot = umaData.umaRoot.transform.localRotation;
					umaData.umaRoot.transform.parent = null;
					umaData.umaRoot.transform.localRotation = Quaternion.identity;
					animator = CreateAnimator(umaData, umaData.umaRecipe.raceData.TPose, umaData.animationController);
					umaData.animator = animator;
					umaData.umaRoot.transform.parent = oldParent;
					umaData.umaRoot.transform.localRotation = originalRot;
					snapshot.RestoreAnimatorState(animator);
				}
			}
		}

		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller)
		{
			var animator = umaData.umaRoot.AddComponent<Animator>();
			switch (umaData.umaRecipe.raceData.umaTarget)
			{
				case RaceData.UMATarget.Humanoid:
					umaTPose.DeSerialize();
					animator.avatar = CreateAvatar(umaData, umaTPose);
					break;
				case RaceData.UMATarget.Generic:
					animator.avatar = CreateGenericAvatar(umaData);
					break;
			}
			animator.runtimeAnimatorController = controller;
			return animator;
		}

		public static void DebugLogHumanAvatar(GameObject root, HumanDescription description)
		{
			Debug.Log("***", root);
			Dictionary<String, String> bones = new Dictionary<String, String>();
			foreach (var sb in description.skeleton)
			{
				Debug.Log(sb.name);
				bones[sb.name] = sb.name;
			}
			Debug.Log("----");
			foreach (var hb in description.human)
			{
				string boneName;
				if (bones.TryGetValue(hb.boneName, out boneName))
				{
					Debug.Log(hb.humanName + " -> " + boneName);
				} else
				{
					Debug.LogWarning(hb.humanName + " !-> " + hb.boneName);
				}
			}
			Debug.Log("++++");
		}

		public static Avatar CreateAvatar(UMAData umaData, UmaTPose umaTPose)
		{
			umaTPose.DeSerialize();
			HumanDescription description = CreateHumanDescription(umaData, umaTPose);
			//DebugLogHumanAvatar(umaData.umaRoot, description);
			Avatar res = AvatarBuilder.BuildHumanAvatar(umaData.umaRoot, description);
			return res;
		}

		public static Avatar CreateGenericAvatar(UMAData umaData)
		{
			Avatar res = AvatarBuilder.BuildGenericAvatar(umaData.umaRoot, umaData.umaRecipe.GetRace().genericRootMotionTransformName);
			return res;
		}

		public static HumanDescription CreateHumanDescription(UMAData umaData, UmaTPose umaTPose)
		{
			var res = new HumanDescription();
			res.armStretch = 0;
			res.feetSpacing = 0;
			res.legStretch = 0;
			res.lowerArmTwist = 0.2f;
			res.lowerLegTwist = 1f;
			res.upperArmTwist = 0.5f;
			res.upperLegTwist = 0.1f;

			var animatedBones = umaData.GetAnimatedBones();
			if (animatedBones.Length > 0)
			{
				List<SkeletonBone> animatedSkeleton = new List<SkeletonBone>(umaTPose.boneInfo);

				foreach (var animatedBoneHash in animatedBones)
				{
					var animatedBone = umaData.GetBoneGameObject(animatedBoneHash).transform;

					var sb = new SkeletonBone();
					sb.name = animatedBone.name;
					sb.position = animatedBone.localPosition;
					sb.rotation = animatedBone.localRotation;
					sb.scale = animatedBone.localScale;
					animatedSkeleton.Add(sb);
				}
				res.skeleton = animatedSkeleton.ToArray();
			} else
			{
				res.skeleton = umaTPose.boneInfo;
			}

			res.human = umaTPose.humanInfo;

//            res.skeleton[0].name = umaData.umaRoot.name;
			SkeletonModifier(umaData, ref res.skeleton, res.human);
			return res;
		}

#pragma warning disable 618
		private void ModifySkeletonBone(ref SkeletonBone bone, Transform trans)
		{
			bone.position = trans.localPosition;
			bone.rotation = trans.localRotation;
			bone.scale = trans.localScale;
		}

		private static void SkeletonModifier(UMAData umaData, ref SkeletonBone[] bones, HumanBone[] human)
		{
//            Dictionary<Transform, Transform> animatedBones = new Dictionary<Transform,Transform>();
			Dictionary<int, Transform> animatedBones = new Dictionary<int, Transform>();
			for (var i = 0; i < umaData.animatedBones.Length; i++)
			{
//                animatedBones.Add(umaData.animatedBones[i], umaData.animatedBones[i]);
				animatedBones.Add(UMASkeleton.StringToHash(umaData.animatedBones[i].name), umaData.animatedBones[i]);
			}

			for (int i = 0; i < human.Length; i++)
			{
				int boneHash = UMASkeleton.StringToHash(human[i].boneName);
				animatedBones[boneHash] = null;
			}

			for (var i = 0; i < bones.Length; i++)
			{
				var skeletonbone = bones[i];
				UMAData.BoneData entry;
				int boneHash = UMASkeleton.StringToHash(skeletonbone.name);
				if (umaData.boneHashList.TryGetValue(boneHash, out entry))
				{
					skeletonbone.position = entry.boneTransform.localPosition;
					//skeletonbone.rotation = entry.boneTransform.localRotation;
					skeletonbone.scale = entry.boneTransform.localScale;
					bones[i] = skeletonbone;
					animatedBones.Remove(boneHash);
				}
			}
			bool foundSkelRoot = umaData.skeleton.HasBone(UMASkeleton.StringToHash(bones[0].name));
			if ((animatedBones.Count > 0) || !foundSkelRoot)
			{
				var newBones = new List<SkeletonBone>(bones);

				if (!foundSkelRoot)
				{
					int missingBoneCount = 0;
					int rootBoneHash = 0;
					while (!foundSkelRoot)
					{
						missingBoneCount++;
						rootBoneHash = UMASkeleton.StringToHash(bones[missingBoneCount].name);
						foundSkelRoot = umaData.skeleton.HasBone(rootBoneHash);
					}
					if (missingBoneCount > 0)
					{
						newBones.RemoveRange(0, missingBoneCount);
						var realRootBone = umaData.skeleton.GetBoneGameObject(rootBoneHash).transform;
						var newBone = newBones[0];
						newBone.position = realRootBone.localPosition;
						newBone.rotation = realRootBone.localRotation;
						newBone.scale = realRootBone.localScale;
						newBones[0] = newBone;
					}
				}

				// iterate original list rather than dictionary to ensure that relative order is preserved
				for (var i = 0; i < umaData.animatedBones.Length; i++)
				{
					var animatedBone = umaData.animatedBones[i];
					var animatedBoneHash = UMASkeleton.StringToHash(animatedBone.name);
					if (animatedBones.ContainsKey(animatedBoneHash))
					{
						var newBone = new SkeletonBone();
						newBone.name = animatedBone.name;
						newBone.position = animatedBone.localPosition;
						newBone.rotation = animatedBone.localRotation;
						newBone.scale = animatedBone.localScale;
						newBones.Add(newBone);
					}
				}
				bones = newBones.ToArray();
			}

		}
		
		[Obsolete("CreateAnimator(... bool applyRootMotion ...) is obsolete, use CreateAnimator(UMAData, UmaTPose, RuntimeAnimatorController) instead.", false)]
		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller, bool applyRootMotion, AnimatorUpdateMode updateMode, AnimatorCullingMode cullingMode)
		{
			var animator = CreateAnimator(umaData, umaTPose, controller);
			animator.applyRootMotion = applyRootMotion;
			animator.updateMode = updateMode;
			animator.cullingMode = cullingMode;
			return animator;
		}
		
		[Obsolete("CreateAnimator(... bool applyRootMotion, bool animatePhysics ...) is obsolete, use CreateAnimator(... AnimatorUpdateMode updateMode ...) instead.", false)]
		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller, bool applyRootMotion, bool animatePhysics, AnimatorCullingMode cullingMode)
		{
			var animator = CreateAnimator(umaData, umaTPose, controller);
			animator.applyRootMotion = applyRootMotion;
			animator.animatePhysics = animatePhysics;
			animator.cullingMode = cullingMode;
			return animator;
		}
#pragma warning restore 618
	}
}
