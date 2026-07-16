using System;
using UnityEngine;
using System.Collections.Generic;
using System.Buffers;

namespace UMA
{
    /// <summary>
    /// Base class for UMA character generators.
    /// </summary>
    public abstract class UMAGeneratorBase : MonoBehaviour
    {
        public enum FitMethod { DecreaseResolution, BestFitSquare, MultipleHeuristics };

        public bool fitAtlas;
        [HideInInspector]
        public TextureMerge textureMerge;
        [Header("Convert Render Texture should not be used on mobile devices")]
        [Tooltip("Convert this to a normal texture. This should be OFF for mobile devices or devices that have unified memory")]
#if UNITY_ANDROID || UNITY_IOS
		public bool convertRenderTexture = false;
#else
        public bool convertRenderTexture = true;
#endif
        [Tooltip("Use Async RT conversion to avoid GPU stalls")]
        public bool useAsyncConversion = true;
        [Tooltip("Regenerate Mipmaps on conversion to avoid copying mips from GPU")]
        public bool asyncMipRegen = true;

        [Tooltip("Create Mipmaps for the generated texture. Checking this is a good idea.")]
        public bool convertMipMaps;
        [Tooltip("Initial size of the texture atlas (square)")]
        public int atlasResolution;
        [Tooltip("In Editor Initial size of the texture atlas (square)")]
        public int editorAtlasResolution = 1024;

        [Tooltip("How the textures are fit in the atlas if they are too large to fit normally")]
        public FitMethod AtlasOverflowFitMethod = FitMethod.DecreaseResolution;

        [Tooltip("The percentage to shrink the textures if using DecreaseResolution fit method")]
        [Range(0.1f, 0.9f)]
        public float FitPercentageDecrease = 0.5f;

        [Tooltip("When true, the rescaled textures will use a higher mipmap when being downsampled. This will result in a more detailed texture.")]
        public bool SharperFitTextures = true;

        [Tooltip("The default overlay to display if a slot has meshData and no overlays assigned")]
        public OverlayDataAsset defaultOverlayAsset;

        public string ignoreTag
        {
            get { return UMASettings.GetIgnoreTag(); }
        }

        [Tooltip("UMA will keep items with this tag when rebuilding the skeleton. Any new bone created during the build process will be replaced with the previous copy, keeping components and references intact.")]
        public string keepTag = "UMAKeepChain";

        [Tooltip("Default Renderer Asset to use for the generated SkinnedMeshRenderer")]
        public UMARendererAsset defaultRendererAsset;

        public bool MultiThreadTextureConversion = true;
        public int MaxQueuedConversionsPerFrame = 8;

        [Tooltip("Use 32 bit buffers for the generated meshes. This is required for meshes with more than 65535 vertices,boneweights, or trianges. Will use more memory.")]
        public bool Use32BitBuffers = true;

        [Tooltip("Always regenerate the renderers when updating the UMA. This is needed if you attach objects to the renderer.")]
        public bool alwaysRegenerateRenderers = false;

        [NonSerialized]
        public bool FreezeTime;

        // Partial edit-time builds need to rebuild the Avatar without immediately
        // sampling its Animator. Sampling here can overwrite the freshly applied DNA
        // skeleton with a retargeted animation pose.
        protected bool evaluateAnimatorPoseAfterAvatarUpdate = true;

        public bool SaveAndRestoreIgnoredItems;

        protected OverlayData _defaultOverlayData;
        public OverlayData defaultOverlaydata
        {
            get { return _defaultOverlayData; }
        }

#if UNITY_EDITOR
#if UMA_ADDRESSABLES
        [Tooltip("If Addressables are enabled, skip the recipe lookup and just load the items. This should be for testing only.")]
        public bool skipAddressableRecipeLookupInEditor;
#endif
#endif

        public static HashSet<EntityId> CreatedAvatars = new HashSet<EntityId>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void StaticInitializeOnLoad()
        {
            CreatedAvatars = new HashSet<EntityId>();
            newBones = new List<SkeletonBone>();
        }

        /// <summary>
        /// returns true if the UMAData is in the update queue.
        /// Note that this will return false if the UMA is currently being processed!
        /// </summary>
        /// <param name="umaToCheck"></param>
        /// <returns></returns>
        public abstract bool updatePending(UMAData umaToCheck);

        /// <summary>
        /// Returns true if the UMA is at pos 0 in the DirtyList -
        /// this means it's the UMA that is currently being processed
        /// </summary>
        /// <param name="umaToCheck"></param>
        /// <returns></returns>
        public abstract bool updateProcessing(UMAData umaToCheck);

        /// <summary>
        /// removes the UMAData if it exists in the update queue.
        /// Use this if you need to delete the UMA after scheduling an update for it.
        /// </summary>
        /// <param name="umaToRemove"></param>
        public abstract void removeUMA(UMAData umaToRemove);

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
        /// Call this method to force the generator to work right now.
        /// </summary>
        public abstract void Work();

        /// <summary>
        /// Try to finds the static generator in the scene.
        /// </summary>
        /// <returns>The instance.</returns>
        public static UMAGeneratorBase FindInstance()
        {
            var generatorGO = GameObject.Find("UMAGenerator");
            if (generatorGO == null)
            {
                return null;
            }

            return generatorGO.GetComponent<UMAGeneratorBase>();
        }

        /// <summary>
        /// Utility class to store data about active animator.
        /// </summary>
        public class AnimatorState
        {
            public bool wasCopied = false;
            public bool FreezeTime;
            private bool wasInitialized;

            // Pooled arrays and counts
            private int[] stateHashes = null;
            private float[] stateTimes = null;
            private int stateLayerCount = 0;

            private AnimatorControllerParameter[] parameters = null;
            private int parametersCount = 0;

            private Dictionary<int, float> layerWeights = new Dictionary<int, float>();

            private void ReleaseBuffers()
            {
                if (stateHashes != null)
                {
                    ArrayPool<int>.Shared.Return(stateHashes, clearArray: false);
                    stateHashes = null;
                    stateLayerCount = 0;
                }
                if (stateTimes != null)
                {
                    ArrayPool<float>.Shared.Return(stateTimes, clearArray: false);
                    stateTimes = null;
                }
                if (parameters != null)
                {
                    ArrayPool<AnimatorControllerParameter>.Shared.Return(parameters, clearArray: false);
                    parameters = null;
                    parametersCount = 0;
                }
            }

            public void SaveAnimatorState(Animator animator, UMAData umaData)
            {
                // ensure any previous rented buffers are returned
                ReleaseBuffers();
                layerWeights.Clear();

                if (animator == null)
                {
                    wasCopied = false;
                    return;
                }
                umaData.FireAnimatorStateSavedEvent();

                if (animator.runtimeAnimatorController == null)
                {
                    wasCopied = false;
                    return;
                }

                int layerCount = 0;
                if (animator.isInitialized)
                {
                    layerCount = animator.layerCount;
                }

                // Rent arrays sized to layerCount (if any)
                if (layerCount > 0)
                {
                    stateHashes = ArrayPool<int>.Shared.Rent(layerCount);
                    stateTimes = ArrayPool<float>.Shared.Rent(layerCount);
                    stateLayerCount = layerCount;
                }
                else
                {
                    stateHashes = null;
                    stateTimes = null;
                    stateLayerCount = 0;
                }

                if (animator.isInitialized)
                {
                    parametersCount = animator.parameterCount;
                    if (parametersCount > 0)
                    {
                        // Copy parameter references into rented array so we can write default values
                        parameters = ArrayPool<AnimatorControllerParameter>.Shared.Rent(parametersCount);
                        var srcParams = animator.parameters;
                        Array.Copy(srcParams, parameters, parametersCount);

                        for (int i = 0; i < parametersCount; i++)
                        {
                            AnimatorControllerParameter param = parameters[i];
                            switch (param.type)
                            {
                                case AnimatorControllerParameterType.Bool:
                                    param.defaultBool = animator.GetBool(param.nameHash);
                                    break;
                                case AnimatorControllerParameterType.Float:
                                    param.defaultFloat = animator.GetFloat(param.nameHash);
                                    break;
                                case AnimatorControllerParameterType.Int:
                                    param.defaultInt = animator.GetInteger(param.nameHash);
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    parameters = null;
                    parametersCount = 0;
                }

                for (int i = 0; i < layerCount; i++)
                {
                    var state = animator.GetCurrentAnimatorStateInfo(i);
                    stateHashes[i] = state.fullPathHash;
#if UNITY_EDITOR
                    float time = state.normalizedTime;
                    if (!FreezeTime)
                    {
                        time += Time.deltaTime / state.length;

                    }
#else
					float time = state.normalizedTime + Time.deltaTime / state.length;
#endif
                    stateTimes[i] = Mathf.Max(0, time);
                    layerWeights.Add(i, animator.GetLayerWeight(i));
                }

                wasCopied = true;
            }

            public void RestoreAnimatorState(Animator animator, UMAData umaData, bool evaluatePose = true)
            {
                if (wasCopied == false)
                {
                    ReleaseBuffers();
                    return;
                }

                if (animator == null)
                {
                    ReleaseBuffers();
                    return;
                }

                if (animator.isActiveAndEnabled == false)
                {
                    ReleaseBuffers();
                    return;
                }

                if (animator.layerCount == stateLayerCount && stateLayerCount > 0)
                {
                    for (int i = 0; i < stateLayerCount; i++)
                    {
                        if (animator.GetLayerName(i).ToLower().Contains("sync") == false)
                        {
                            animator.Play(stateHashes[i], i, stateTimes[i]);
                            if (layerWeights != null && layerWeights.TryGetValue(i, out float lw))
                            {
                                animator.SetLayerWeight(i, lw);
                            }
                        }
                    }
                }
                if (parameters != null && parametersCount > 0)
                {
                    for (int i = 0; i < parametersCount; i++)
                    {
                        AnimatorControllerParameter param = parameters[i];
                        if (!animator.IsParameterControlledByCurve(param.nameHash))
                        {
                            switch (param.type)
                            {
                                case AnimatorControllerParameterType.Bool:
                                    animator.SetBool(param.nameHash, param.defaultBool);
                                    break;
                                case AnimatorControllerParameterType.Float:
                                    animator.SetFloat(param.nameHash, param.defaultFloat);
                                    break;
                                case AnimatorControllerParameterType.Int:
                                    animator.SetInteger(param.nameHash, param.defaultInt);
                                    break;
                            }
                        }
                    }
                }

                if (!animator.gameObject.activeInHierarchy)
                {
                    ReleaseBuffers();
                    return;
                }

                umaData.FireAnimatorStateRestoredEvent();
                if (animator.isInitialized && evaluatePose)
                {
#if UNITY_EDITOR
                    if (FreezeTime || animator.enabled == false)
                    {
                        animator.Update(0);
                    }
                    else
                    {
                        animator.Update(Time.deltaTime);
                    }

#else
					if (animator.enabled == true)
						animator.Update(Time.deltaTime);
					else
						animator.Update(0);
#endif 
                }

                // release rented arrays
                ReleaseBuffers();
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
                if (umaData.rawAvatar)
                {
                    return;
                }

                if (umaData.animationController != null || umaData.disableAnimation != true)
                {
                    var umaTransform = umaData.transform;
                    var oldParent = umaTransform.parent;
                    var originalRot = umaTransform.localRotation;
                    var originalPos = umaTransform.localPosition;
                    var animator = umaData.animator;

                    umaTransform.SetParent(null, false);
                    umaTransform.localRotation = Quaternion.identity;
                    umaTransform.localPosition = Vector3.zero;
                    if (animator == null)
                    {
                        animator = umaData.gameObject.GetComponent<Animator>();
                        if (animator == null)
                        {
                            animator = umaData.gameObject.AddComponent<Animator>();
                        }
                        SetAvatar(umaData, animator, !evaluateAnimatorPoseAfterAvatarUpdate);
                        animator.runtimeAnimatorController = umaData.animationController;
                        umaData.animator = animator;

                        umaTransform.SetParent(oldParent, false);
                        umaTransform.localRotation = originalRot;
                        umaTransform.localPosition = originalPos;
                    }
                    else
                    {
                        AnimatorState snapshot = new AnimatorState();
#if UNITY_EDITOR
                        snapshot.FreezeTime = FreezeTime;
#endif
                        snapshot.SaveAnimatorState(animator, umaData);
                        if (!umaData.KeepAvatar || animator.avatar == null)
                        {
                            UMAUtils.DestroyAvatar(animator.avatar);
                            SetAvatar(umaData, animator, !evaluateAnimatorPoseAfterAvatarUpdate);
                        }

                        umaTransform.SetParent(oldParent, false);
                        umaTransform.localRotation = originalRot;
                        umaTransform.localPosition = originalPos;

                        if (animator.runtimeAnimatorController != null)
                        {
                            snapshot.RestoreAnimatorState(animator, umaData, evaluateAnimatorPoseAfterAvatarUpdate);
                        }
                        if (umaData.ForceRebindAnimator)
                        {
                            animator.Rebind();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new avatar for a UMA character.
        /// </summary>
        /// <param name="umaData">UMA data.</param>
        /// <param name="animator">Animator.</param>
        public static void SetAvatar(UMAData umaData, Animator animator, bool preserveDnaHipsTransform = false)
        {
            var umaTPose = umaData.GetTPose();

            switch (umaData.umaRecipe.raceData.umaTarget)
            {
                case RaceData.UMATarget.Humanoid:
                    umaTPose.DeSerialize();
                    var avatar = CreateAvatar(umaData, umaTPose, preserveDnaHipsTransform);
                    if (avatar != null)
                    {
                        animator.avatar = avatar;
                    }
                    break;
                case RaceData.UMATarget.Generic:
                    animator.avatar = CreateGenericAvatar(umaData);
                    break;
            }
        }
#if UNITY_EDITOR
        public static void DebugLogHumanAvatar(GameObject root, HumanDescription description)
        {
            if (Debug.isDebugBuild)
            {
                Debug.Log("***", root);
            }

            Dictionary<String, String> bones = new Dictionary<String, String>();
            for (int i = 0; i < description.skeleton.Length; i++)
            {
                SkeletonBone sb = description.skeleton[i];
                if (Debug.isDebugBuild)
                {
                    Debug.Log(sb.name);
                }

                bones[sb.name] = sb.name;
            }
            if (Debug.isDebugBuild)
            {
                Debug.Log("----");
            }

            for (int i = 0; i < description.human.Length; i++)
            {
                HumanBone hb = description.human[i];
                string boneName;
                if (bones.TryGetValue(hb.boneName, out boneName))
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.Log(hb.humanName + " -> " + boneName);
                    }
                }
                else
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.LogWarning(hb.humanName + " !-> " + hb.boneName);
                    }
                }
            }
            if (Debug.isDebugBuild)
            {
                Debug.Log("++++");
            }
        }
#endif

        /// <summary>
        /// Creates a human (biped) avatar for a UMA character.
        /// </summary>
        /// <returns>The human avatar.</returns>
        /// <param name="umaData">UMA data.</param>
        /// <param name="umaTPose">UMA TPose.</param>
        public static Avatar CreateAvatar(UMAData umaData, UmaTPose umaTPose, bool preserveDnaHipsTransform = false)
        {
            //Debug.Log("Avatar: Creating humanoid avatar for " + umaData.name);
            umaTPose.DeSerialize();
            HumanDescription description = CreateHumanDescription(umaData, umaTPose);
            //DebugLogHumanAvatar(umaData.gameObject, description);
            try
            {
                Avatar res = AvatarBuilder.BuildHumanAvatar(umaData.gameObject, description);
                CreatedAvatars.Add(res.GetEntityId());
                res.name = umaData.name;
                if (umaTPose.HasExtractedHumanPose())
                {
                    // SetHumanPose retargets its normalized bodyPosition against the
                    // newly proportioned Avatar. During a partial edit-time DNA build
                    // that can move Hips even when the active DNA touches only child
                    // bones. Preserve the already-correct post-DNA Hips transform while
                    // still restoring the serialized muscle pose.
                    Transform hips = null;
                    Vector3 hipsPosition = default;
                    Quaternion hipsRotation = default;
                    Vector3 hipsScale = default;
                    if (preserveDnaHipsTransform && umaData.skeleton != null)
                    {
                        string hipsBoneName = umaTPose.BoneNameFromHumanName("Hips");
                        if (string.IsNullOrEmpty(hipsBoneName))
                        {
                            hipsBoneName = "Hips";
                        }
                        hips = umaData.skeleton.GetBoneTransform(hipsBoneName);
                        if (hips != null)
                        {
                            hipsPosition = hips.position;
                            hipsRotation = hips.rotation;
                            hipsScale = hips.localScale;
                        }
                    }

                    HumanPose pose = umaTPose.GetHumanPose();
                    var handler = new HumanPoseHandler(res, umaData.transform);
                    try
                    {
                        handler.SetHumanPose(ref pose);
                    }
                    finally
                    {
                        handler.Dispose();
                        if (hips != null)
                        {
                            hips.SetPositionAndRotation(hipsPosition, hipsRotation);
                            hips.localScale = hipsScale;
                        }
                    }
                }
                return res;
            }
            catch (Exception ex)
            {
                Debug.LogError("Error creating avatar: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Creates a generic avatar for a UMA character.
        /// </summary>
        /// <returns>The generic avatar.</returns>
        /// <param name="umaData">UMA data.</param>
        public static Avatar CreateGenericAvatar(UMAData umaData)
        {
            Avatar res = AvatarBuilder.BuildGenericAvatar(umaData.gameObject, umaData.umaRecipe.GetRace().genericRootMotionTransformName);
            res.name = umaData.name;
            CreatedAvatars.Add(res.GetEntityId());
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
            res.armStretch = umaTPose.armStretch == 0.0f ? 0.05f : umaTPose.armStretch; // this is for compatiblity with the existing tpose. 
            res.legStretch = umaTPose.legStretch == 0.0f ? 0.05f : umaTPose.legStretch;
            res.feetSpacing = umaTPose.feetSpacing;
            res.lowerArmTwist = umaTPose.lowerArmTwist == 0.0f ? 0.5f : umaTPose.lowerArmTwist;
            res.lowerLegTwist = umaTPose.lowerLegTwist == 0.0f ? 0.5f : umaTPose.lowerLegTwist;
            res.upperArmTwist = umaTPose.upperArmTwist == 0.0f ? 0.5f : umaTPose.upperArmTwist;
            res.upperLegTwist = umaTPose.upperLegTwist == 0.0f ? 0.5f : umaTPose.upperLegTwist;
            res.skeleton = umaTPose.boneInfo;
            if (umaTPose.mapJaw)
            {
                //Debug.Log("Avatar: mapping jaw bone in human description because mapJaw is true. This is to allow Mecanim to open the jaw if a jaw bone is mapped and the animation does not animate the jaw.");
                res.human = umaTPose.humanInfo;
            }
            else
            {
                //Debug.Log("Avatar: removing jaw bone mapping from human description because mapJaw is false. This is to prevent Mecanim from opening the jaw if a jaw bone is mapped and the animation does not animate the jaw.");
                // Remove the jaw bone mapping if mapJaw is false. This is because Mecanim will open the jaw if a jaw bone is mapped, and the animation does not animate the jaw.
                List<HumanBone> humanBones = new List<HumanBone>(umaTPose.humanInfo);
                humanBones.RemoveAll(hb => hb.humanName.ToLower().Contains("jaw"));
                res.human = humanBones.ToArray();
            }

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

        private static List<SkeletonBone> newBones = new List<SkeletonBone>();

        private static void SkeletonModifier(UMAData umaData, ref SkeletonBone[] bones, HumanBone[] human)
        {
            int missingBoneCount = 0;
            newBones.Clear();


            while (missingBoneCount < bones.Length)
            {
                int boneHash = UMAUtils.StringToHash(bones[missingBoneCount].name);
                if (umaData.skeleton.HasBone(boneHash))
                {
                    break;
                }
                missingBoneCount++;
            }

            /*while (!umaData.skeleton.HasBone(UMAUtils.StringToHash(bones[missingBoneCount].name)))
			{
				missingBoneCount++;
			}*/
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
#pragma warning restore 618
    }
}
