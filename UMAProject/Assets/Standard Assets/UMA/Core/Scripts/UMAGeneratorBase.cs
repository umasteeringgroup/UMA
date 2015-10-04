using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace UMA
{
	/// <summary>
	/// Base class for UMA character generators.
	/// </summary>
	public abstract class UMAGeneratorBase : MonoBehaviour
	{
		public bool fitAtlas;
		[HideInInspector]
		public TextureMerge textureMerge;
		public bool convertRenderTexture;
		public bool convertMipMaps;
		public int atlasResolution;
#if !UMA2_LEAN_AND_CLEAN 
		public string[] textureNameList;
#endif
		/// <summary>
		/// Adds the dirty UMA to the update queue.
		/// </summary>
		/// <param name="umaToAdd">UMA data to add.</param>
		public abstract void addDirtyUMA(UMAData umaToAdd);
		/// <summary>
		/// Is the dirty queue empty?.
		/// </summary>
		/// <returns><c>true</c> if dirty queue is empty; otherwise, <c>false</c>.</returns>
		public abstract bool IsIdle();

		/// <summary>
		/// Dirty queue size.
		/// </summary>
		/// <returns>The number of items in the dirty queue.</returns>
		public abstract int QueueSize();

		/// <summary>
		/// Try to finds the static generator in the scene.
		/// </summary>
		/// <returns>The instance.</returns>
		public static UMAGeneratorBase FindInstance()
		{
			var generatorGO = GameObject.Find("UMAGenerator");
			if (generatorGO == null) return null;
			return generatorGO.GetComponent<UMAGeneratorBase>();
		}

		/// <summary>
		/// Utility class to store data about active animator.
		/// </summary>
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
				
				animator.Update(0.00001f);
				animator.enabled = animating;
			}
		}	

		/// <summary>
		/// Update the avatar of a UMA character.
		/// </summary>
		/// <param name="umaData">UMA data.</param>
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
					var umaTransform = umaData.transform;
					var oldParent = umaTransform.parent;
					var originalRot = umaTransform.localRotation;
					var originalPos = umaTransform.localPosition;
					umaTransform.parent = null;
					umaTransform.localRotation = Quaternion.identity;
					umaTransform.localPosition = Vector3.zero;
					animator = CreateAnimator(umaData, umaData.umaRecipe.raceData.TPose, umaData.animationController);
					umaData.animator = animator;
					umaTransform.parent = oldParent;
					umaTransform.localRotation = originalRot;
					umaTransform.localPosition = originalPos;
					snapshot.RestoreAnimatorState(animator);
				}
			}
		}

		/// <summary>
		/// Creates a new animator for a UMA character.
		/// </summary>
		/// <returns>The animator.</returns>
		/// <param name="umaData">UMA data.</param>
		/// <param name="umaTPose">UMA TPose.</param>
		/// <param name="controller">Animation controller.</param>
		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller)
		{
			var animator = umaData.gameObject.AddComponent<Animator>();
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

		/// <summary>
		/// Creates a human (biped) avatar for a UMA character.
		/// </summary>
		/// <returns>The human avatar.</returns>
		/// <param name="umaData">UMA data.</param>
		/// <param name="umaTPose">UMA TPose.</param>
		public static Avatar CreateAvatar(UMAData umaData, UmaTPose umaTPose)
		{
			umaTPose.DeSerialize();
			HumanDescription description = CreateHumanDescription(umaData, umaTPose);
			//DebugLogHumanAvatar(umaData.gameObject, description);
			Avatar res = AvatarBuilder.BuildHumanAvatar(umaData.gameObject, description);
			return res;
		}

		/// <summary>
		/// Creates a generic avatar for a UMA character.
		/// </summary>
		/// <returns>The generic avatar.</returns>
		/// <param name="umaData">UMA data.</param>
		public static Avatar CreateGenericAvatar(UMAData umaData)
		{
			Avatar res = AvatarBuilder.BuildGenericAvatar(umaData.umaRoot, umaData.umaRecipe.GetRace().genericRootMotionTransformName);
			return res;
		}

		/// <summary>
		/// Creates a Mecanim human description for a UMA character.
		/// </summary>
		/// <returns>The human description.</returns>
		/// <param name="umaData">UMA data.</param>
		/// <param name="umaTPose">UMA TPose.</param>
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

			//var animatedBones = umaData.GetAnimatedBones();
			//if (animatedBones.Length > 0)
			//{
			//	List<SkeletonBone> animatedSkeleton = new List<SkeletonBone>(umaTPose.boneInfo);

			//	foreach (var animatedBoneHash in animatedBones)
			//	{
			//		var animatedBone = umaData.GetBoneGameObject(animatedBoneHash).transform;

			//		var sb = new SkeletonBone();
			//		sb.name = animatedBone.name;
			//		sb.position = animatedBone.localPosition;
			//		sb.rotation = animatedBone.localRotation;
			//		sb.scale = animatedBone.localScale;
			//		animatedSkeleton.Add(sb);
			//	}
			//	res.skeleton = animatedSkeleton.ToArray();
			//} else
			//{
			res.skeleton = umaTPose.boneInfo;
			//}

			res.human = umaTPose.humanInfo;

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
			int missingBoneCount = 0;
			var newBones = new List<SkeletonBone>(bones.Length);

			while (!umaData.skeleton.HasBone(UMAUtils.StringToHash(bones[missingBoneCount].name)))
			{
				missingBoneCount++;
			}
			if (missingBoneCount > 0)
			{
				// force the two root transforms, reuse old bones entries to ensure any humanoid identifiers stay intact
				var realRootBone = umaData.transform;
				var newBone = bones[missingBoneCount - 2];
				newBone.position = realRootBone.localPosition;
				newBone.rotation = realRootBone.localRotation;
				newBone.scale = realRootBone.localScale;
				//				Debug.Log(newBone.name + "<-"+realRootBone.name);
				newBone.name = realRootBone.name;
				newBones.Add(newBone);

				var rootBoneTransform = umaData.umaRoot.transform;
				newBone = bones[missingBoneCount - 1];
				newBone.position = rootBoneTransform.localPosition;
				newBone.rotation = rootBoneTransform.localRotation;
				newBone.scale = rootBoneTransform.localScale;
				//				Debug.Log(newBone.name + "<-" + rootBoneTransform.name);
				newBone.name = rootBoneTransform.name;
				newBones.Add(newBone);
			}

			for (var i = missingBoneCount; i < bones.Length; i++)
			{
				var skeletonbone = bones[i];
				int boneHash = UMAUtils.StringToHash(skeletonbone.name);
				GameObject boneGO = umaData.skeleton.GetBoneGameObject(boneHash);
				if (boneGO != null)
				{
					skeletonbone.position = boneGO.transform.localPosition;
					skeletonbone.scale = boneGO.transform.localScale;
					skeletonbone.rotation = umaData.skeleton.GetTPoseCorrectedRotation(boneHash, skeletonbone.rotation);
					newBones.Add(skeletonbone);
				}
			}
			bones = newBones.ToArray();
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
