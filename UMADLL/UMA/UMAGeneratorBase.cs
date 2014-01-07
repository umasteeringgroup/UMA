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
        public bool usePRO;
        public bool AtlasCrop;
        [NonSerialized]
        public TextureMerge textureMerge;
        public int maxPixels;
        public bool convertRenderTexture;
        public bool convertMipMaps;
        public int atlasResolution;
        public string[] textureNameList;
        public abstract void addDirtyUMA(UMAData umaToAdd);

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
                    if (animator != null)
                    {
                        snapshot = new AnimationState[animator.layerCount];
                        for (int i = 0; i < animator.layerCount; i++)
                        {
                            var state = animator.GetCurrentAnimatorStateInfo(i);
                            snapshot[i].stateHash = state.nameHash;
                            snapshot[i].stateTime = Mathf.Max(0, state.normalizedTime - Time.deltaTime / state.length);
                        }
                    }
                }
                if (umaData.animationController)
                {
                    var animator = umaData.animator;

                    bool applyRootMotion = false;
                    bool animatePhysics = false;
                    AnimatorCullingMode cullingMode = AnimatorCullingMode.AlwaysAnimate;

                    if (animator)
                    {
                        applyRootMotion = animator.applyRootMotion;
                        animatePhysics = animator.animatePhysics;
                        cullingMode = animator.cullingMode;
                        Object.DestroyImmediate(animator);
                    }
                    var oldParent = umaData.umaRoot.transform.parent;
                    umaData.umaRoot.transform.parent = null;
                    animator = CreateAnimator(umaData, umaData.umaRecipe.raceData.TPose, umaData.animationController, applyRootMotion, animatePhysics, cullingMode);
                    umaData.animator = animator;
                    umaData.umaRoot.transform.parent = oldParent;
                    if (snapshot != null)
                    {
                        for (int i = 0; i < animator.layerCount; i++)
                        {
                            animator.Play(snapshot[i].stateHash, i, snapshot[i].stateTime);
                        }
                        animator.Update(0);
                    }
                }
            }
        }

        public static Animator CreateAnimator(UMAData umaData, UmaTPose umaTPose, RuntimeAnimatorController controller, bool applyRootMotion, bool animatePhysics, AnimatorCullingMode cullingMode)
        {
            umaTPose.DeSerialize();
            var animator = umaData.umaRoot.AddComponent<Animator>();
            animator.avatar = CreateAvatar(umaData, umaTPose);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = applyRootMotion;
            animator.animatePhysics = animatePhysics;
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

            res.human = umaTPose.humanInfo;
            res.skeleton = umaTPose.boneInfo;
            res.skeleton[0].name = umaData.umaRoot.name;
            SkeletonModifier(umaData, ref res.skeleton);
            return res;
        }

        private static void SkeletonModifier(UMAData umaData, ref SkeletonBone[] bones)
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var skeletonbone = bones[i];
                UMAData.BoneData entry;
                if (umaData.boneHashList.TryGetValue(UMASkeleton.StringToHash(skeletonbone.name), out entry))
                {
                    //var entry = umaData.boneList[skeletonbone.name];
                    skeletonbone.position = entry.boneTransform.localPosition;
                    //skeletonbone.rotation = entry.boneTransform.localRotation;
                    skeletonbone.scale = entry.boneTransform.localScale;
                    bones[i] = skeletonbone;
                }
            }
        }
    }
}
