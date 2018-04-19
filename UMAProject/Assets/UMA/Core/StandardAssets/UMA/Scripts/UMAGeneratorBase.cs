using System;
using UnityEngine;
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
        [Tooltip("Convert this to a normal texture.")]
		public bool convertRenderTexture;
        [Tooltip("Create Mipmaps for the generated texture. Checking this is a good idea.")]
		public bool convertMipMaps;
        [Tooltip("Initial size of the texture atlas (square)")]
		public int atlasResolution;
		[Tooltip("The default overlay to display if a slot has meshData and no overlays assigned")]
		public OverlayDataAsset defaultOverlayAsset;

		protected OverlayData _defaultOverlayData;
		public OverlayData defaultOverlaydata
		{
			get { return _defaultOverlayData; }
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
			AnimatorControllerParameter[] parameters;

			public void SaveAnimatorState(Animator animator)
			{
				int layerCount = animator.layerCount;
				stateHashes = new int[layerCount];
				stateTimes = new float[layerCount];
				parameters = new AnimatorControllerParameter[animator.parameterCount];

				for (int i = 0; i < layerCount; i++)
				{
					var state = animator.GetCurrentAnimatorStateInfo(i);
					stateHashes[i] = state.fullPathHash;
					stateTimes[i] = Mathf.Max(0, state.normalizedTime + Time.deltaTime / state.length);
				}

				Array.Copy(animator.parameters, parameters, animator.parameterCount);

				foreach(AnimatorControllerParameter param in parameters)
				{
					switch(param.type)
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

			public void RestoreAnimatorState(Animator animator)
			{
				if (animator.layerCount == stateHashes.Length)
				{
					for (int i = 0; i < animator.layerCount; i++)
					{
						animator.Play(stateHashes[i], i, stateTimes[i]);
					}
				}

				foreach(AnimatorControllerParameter param in parameters)
				{
					if (!animator.IsParameterControlledByCurve(param.nameHash))
                	{
						switch(param.type)
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

                if (animator.enabled == true)
				    animator.Update(Time.deltaTime);
                else
                    animator.Update(0);
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
				if (umaData.animationController != null)
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
							animator = umaData.gameObject.AddComponent<Animator>();
						SetAvatar(umaData, animator);
						animator.runtimeAnimatorController = umaData.animationController;
						umaData.animator = animator;
					}
					else
					{
						AnimatorState snapshot = new AnimatorState();
						snapshot.SaveAnimatorState(animator);
						UMAUtils.DestroySceneObject(animator.avatar);
						SetAvatar(umaData, animator);
						if(animator.runtimeAnimatorController != null)
							snapshot.RestoreAnimatorState(animator);
					}

					umaTransform.SetParent(oldParent, false);
					umaTransform.localRotation = originalRot;
					umaTransform.localPosition = originalPos;
				}
			}
		}

		/// <summary>
		/// Creates a new avatar for a UMA character.
		/// </summary>
		/// <param name="umaData">UMA data.</param>
		/// <param name="animator">Animator.</param>
		public static void SetAvatar(UMAData umaData, Animator animator)
		{
			var umaTPose = umaData.umaRecipe.raceData.TPose;

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
			res.name = umaData.name;
			return res;
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
			res.skeleton = umaTPose.boneInfo;
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

		private static List<SkeletonBone> newBones = new List<SkeletonBone>();
		private static void SkeletonModifier(UMAData umaData, ref SkeletonBone[] bones, HumanBone[] human)
		{
			int missingBoneCount = 0;
			newBones.Clear();

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
#pragma warning restore 618
	}
}
