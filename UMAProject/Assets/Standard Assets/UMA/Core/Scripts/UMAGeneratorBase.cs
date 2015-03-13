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
        public bool AtlasCrop;
        [NonSerialized]
        public TextureMerge textureMerge;
        public bool convertRenderTexture;
        public bool convertMipMaps;
        public int atlasResolution;
        public string[] textureNameList;
        public abstract void addDirtyUMA(UMAData umaToAdd);
        public abstract bool IsIdle();

		public abstract int QueueSize();


        private struct AnimationState
        {
            public int stateHash;
            public float stateTime;
        }	

        public virtual void UpdateAvatar(UMAData umaData)
        {
            if (umaData)
            {
                AnimationState[] snapshot = null;
                if (umaData.animationController)
                {
                    var animator = umaData.animator;

					bool animating = false;
                    bool applyRootMotion = false;
                    AnimatorUpdateMode updateMode = AnimatorUpdateMode.Normal;
                    AnimatorCullingMode cullingMode = AnimatorCullingMode.AlwaysAnimate;

					if (animator)
                    {
						animating = animator.enabled;
                        applyRootMotion = animator.applyRootMotion;
						updateMode = animator.updateMode;
                        cullingMode = animator.cullingMode;
                        
						if (umaData.animationController == animator.runtimeAnimatorController)
						{
							snapshot = new AnimationState[animator.layerCount];
							for (int i = 0; i < animator.layerCount; i++)
							{
								var state = animator.GetCurrentAnimatorStateInfo(i);
#if UNITY_4_6
								snapshot[i].stateHash = state.nameHash;
#else
								snapshot[i].stateHash = state.fullPathHash;
#endif
								snapshot[i].stateTime = Mathf.Max(0, state.normalizedTime - Time.deltaTime / state.length);
							}
						}
						
						Object.DestroyImmediate(animator);
                    }
                    var oldParent = umaData.umaRoot.transform.parent;
                    umaData.umaRoot.transform.parent = null;
					var originalRot = umaData.umaRoot.transform.localRotation;
                    animator = CreateAnimator(umaData, umaData.umaRecipe.raceData.TPose, umaData.animationController, applyRootMotion, updateMode, cullingMode);
                    umaData.animator = animator;
                    umaData.umaRoot.transform.parent = oldParent;
//					umaData.umaRoot.transform.localRotation = Quaternion.identity;
					umaData.umaRoot.transform.GetChild(0).localRotation = Quaternion.identity;
                    if (snapshot != null)
                    {
                        for (int i = 0; i < animator.layerCount; i++)
                        {
                            animator.Play(snapshot[i].stateHash, i, snapshot[i].stateTime);
                        }
                
                        animator.Update(0);
                        animator.enabled = animating;
                    }
                }
            }
        }

		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller, bool applyRootMotion, AnimatorUpdateMode updateMode, AnimatorCullingMode cullingMode)
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
            animator.applyRootMotion = applyRootMotion;
            animator.updateMode = updateMode;
            animator.cullingMode = cullingMode;
            return animator;
        }

		[Obsolete("CreateAnimator(... bool applyRootMotion ...) is obsolete, use CreateAnimator(... AnimatorUpdateMode updateMode ...) instead.", false)]
		public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller, bool applyRootMotion, bool animatePhysics, AnimatorCullingMode cullingMode)
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
			animator.applyRootMotion = applyRootMotion;
#pragma warning disable 618
			animator.animatePhysics = animatePhysics;
#pragma warning restore 618
			animator.cullingMode = cullingMode;
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
                }
                else
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
            }
            else
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
#pragma warning restore 618
	}
}
