using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
	/// <summary>
	/// Class links UMA's bone data with Unity's transform hierarchy.
	/// </summary>
	[Serializable]
	public class UMASkeleton
	{
		/// <summary>
		/// Internal class for storing bone and transform information.
		/// </summary>
		[Serializable]
		protected class BoneData
		{
			public Transform boneTransform;
			public UMATransform umaTransform;
			public Quaternion rotation;
			public Vector3 position;
			public Vector3 scale;

			public Quaternion dnaRotation;
			public Vector3 dnaPosition;
			public Vector3 dnaScale;

			//			[NonSerialized]
			//			public Matrix4x4 localToRoot;

			// HACK
			// Make this localToRetained
			// Then we don't need the other way
			// and it will be faster to build
			// and friendlier when there aren't
			// any Transforms on non-retained
			[NonSerialized]
			public int retainedHash;
			[NonSerialized]
			public Matrix4x4 localToRetained;
		}

		public IEnumerable<int> BoneHashes { get { return GetBoneHashes(); } }

		//DynamicUMADna:: DynamicUMADnaConverterCustomizer Editor interface needs to have an array of bone names
		public string[] BoneNames { get { return GetBoneNames(); } }

		/// <value>The hash for the root bone of the skeleton.</value>
		public int rootBoneHash { get; protected set; }
		/// <value>The bone count.</value>
		public virtual int boneCount { get { return boneDictionary.Count; } }

		/// <summary>
		/// Is the skeleton inside a BeginSkeletonUpdate() to EndSkeletonUpdate() pair?
		/// </summary>
		public bool isUpdating { get { return updating; } }
		protected bool updating;

		protected SerializedDictionary<int, BoneData> boneDictionary;
		protected SerializedDictionary<int, int> skinningDictionary;
		protected bool skinningValid = true;

		protected Matrix4x4[] skinningBinds;
		protected Transform[] skinningTransforms;

		public UMASkeleton()
		{
			boneDictionary = new SerializedDictionary<int, BoneData>();
			skinningDictionary = new SerializedDictionary<int, int>();
		}

		/// <summary>
		/// Initializes an UMASkeleton from a transform hierarchy.
		/// </summary>
		/// <param name="umaRenderer">Skinned mesh renderer.</param>
		public void Initialize(SkinnedMeshRenderer umaRenderer)
		{
			rootBoneHash = UMAUtils.StringToHash(umaRenderer.rootBone.name);

			BeginSkeletonUpdate();
			AddBonesRecursive(umaRenderer.rootBone);
			EndSkeletonUpdate();
		}

		/// <summary>
		/// Initializes an UMASkeleton from the recipe in UMAData.
		/// </summary>
		/// <param name="umaData">UMAData.</param>
		/// <param name="umaTPose">TPose containing retained bones (optional)</param>
		public void Initialize(UMAData umaData, UmaTPose umaTPose = null)
		{
			BeginSkeletonUpdate();

			// HACK this crap needs to go, or at worst be in one place only
			if (umaData.umaRoot == null)
			{
				GameObject newRoot = new GameObject("Root");
				newRoot.layer = umaData.gameObject.layer;
				newRoot.transform.parent = umaData.transform;
				newRoot.transform.localPosition = Vector3.zero;
				newRoot.transform.localRotation = Quaternion.Euler(270f, 0, 0f);
				newRoot.transform.localScale = Vector3.one;

				GameObject newGlobal = new GameObject("Global");
				newGlobal.transform.parent = newRoot.transform;
				newGlobal.transform.localPosition = Vector3.zero;
				newGlobal.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);

				umaData.umaRoot = newRoot;
				rootBoneHash = UMAUtils.StringToHash(umaData.umaRoot.name);
				AddBone(newRoot.transform, rootBoneHash, 0);
				SetRetainedBone(rootBoneHash);

				int globalHash = UMAUtils.StringToHash(newGlobal.name);
				AddBone(newGlobal.transform, globalHash, rootBoneHash);
				SetRetainedBone(globalHash);
			}

			foreach (SlotData slot in umaData.umaRecipe.slotDataList) 
			{
				if (slot != null)
				{
					UMAMeshData meshData = slot.asset.meshData;
					if (meshData == null) continue;

					foreach (UMATransform umaBone in meshData.umaBones)
					{
						//This won't happen if we recalc the bindToBone matrices
						//we don't have which seems to work - see SlotDataAsset
						if (boneDictionary.ContainsKey(umaBone.hash))
						{
						//	BoneData currentBone = boneDictionary[umaBone.hash];
						//	if (currentBone.umaTransform.bindToBone == Matrix4x4.zero)
						//	{
						//		if (umaBone.bindToBone != Matrix4x4.zero)
						//		{
						//			Debug.Log("Found better bind for: " + umaBone.name + " in slot: " + slot.slotName);
						//			currentBone.umaTransform = umaBone;
						//		}
						//	}
						}
						else
						{
							AddBone(umaBone);
						}

						if (umaBone.retained)
						{
							SetRetainedBone(umaBone.hash);
						}
					}
				}
			}

			if (umaTPose != null)
			{
				for (int i = 0; i < umaTPose.humanBoneInfo.Length; i++)
				{
					int hash = umaTPose.humanBoneInfo[i].boneHash;
					if (hash != 0)
					{
						SetRetainedBone(hash);
					}
				}
			}

			EnsureBoneHierarchy();

			EndSkeletonUpdate();
		}

		/// <summary>
		/// Marks the skeleton as being updated.
		/// </summary>
		public virtual void BeginSkeletonUpdate()
		{
			updating = true;
		}

		/// <summary>
		/// Marks the skeleton update as complete.
		/// </summary>
		public virtual void EndSkeletonUpdate()
		{
			foreach (BoneData bone in boneDictionary.Values)
			{
				//bone.position = bone.boneTransform.localPosition;
				//bone.rotation = bone.boneTransform.localRotation;
				//bone.scale = bone.boneTransform.localScale;

				bone.position = bone.dnaPosition;
				bone.rotation = bone.dnaRotation;
				bone.scale = bone.dnaScale;
				if (bone.boneTransform != null)
				{
					bone.boneTransform.localPosition = bone.dnaPosition;
					bone.boneTransform.localRotation = bone.dnaRotation;
					bone.boneTransform.localScale = bone.dnaScale;
				}

				//bone.localToWorld = bone.boneTransform.localToWorldMatrix;
				//bone.worldToLocal = bone.boneTransform.worldToLocalMatrix;

				//if (bone.boneTransform != null)
				if (skinningDictionary.ContainsKey(bone.umaTransform.hash))
				{
					bone.retainedHash = bone.umaTransform.hash;
					bone.localToRetained = Matrix4x4.identity;
				}
				else
				{
					bone.retainedHash = 0;
					bone.localToRetained = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale);
				}
			}

			foreach (BoneData bone in boneDictionary.Values)
			{
				if (bone.retainedHash == 0)
				{
					int parentHash = bone.umaTransform.parent;
					BoneData parentBone;
					while (boneDictionary.TryGetValue(parentHash, out parentBone))
					{
						bone.localToRetained = parentBone.localToRetained * bone.localToRetained;
						bone.retainedHash = parentBone.retainedHash;
						parentHash = parentBone.umaTransform.parent;

						if (bone.retainedHash != 0) break;
					}
				}
			}

			updating = false;
		}

		protected void RepairSkinningDictionary()
		{
			if (!skinningValid)
			{
				int[] hashes = new int[skinningDictionary.Count];
				skinningDictionary.Keys.CopyTo(hashes, 0);
				skinningDictionary.Clear();
				for (int i = 0; i < hashes.Length; i++)
				{
					skinningDictionary.Add(hashes[i], i);
				}

				skinningValid = true;
			}
		}

		/// <summary>
		/// Marks the bone as retained.
		/// </summary>
		/// <param name="nameHash">Hash of bone name.</param>
		public virtual void SetRetainedBone(int nameHash)
		{
			//BoneData bone;
			//if (boneDictionary.TryGetValue(nameHash, out bone))
			//{
			//	if (!skinningDictionary.ContainsKey(nameHash))
			//	{
			//		skinningDictionary.Add(nameHash, skinningDictionary.Count);
			//	}
			//	if (bone.boneTransform == null)
			//	{
			//		bone.boneTransform = new GameObject(bone.umaTransform.name).transform;
			//	}
			//}
			// HACK doesn't make sense for a floating bone to be retained
			SetRetainedBoneHierachy(nameHash);
		}

		/// <summary>
		/// Marks the bone and all parents as retained.
		/// </summary>
		/// <param name="nameHash">Hash of bone name.</param>
		public virtual void SetRetainedBoneHierachy(int nameHash)
		{
			BoneData bone;
			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				if (!skinningDictionary.ContainsKey(nameHash))
				{
					skinningDictionary.Add(nameHash, skinningDictionary.Count);
				}

				if (bone.boneTransform == null)
				{
					bone.boneTransform = new GameObject(bone.umaTransform.name).transform;
				}

				SetRetainedBoneHierachy(bone.umaTransform.parent);
			}
		}

		/// <summary>
		/// Marks the bone as unretained.
		/// </summary>
		/// <param name="nameHash">Hash of bone name.</param>
		public virtual void ClearRetainedBone(int nameHash)
		{
			//if (skinningDictionary.ContainsKey(nameHash))
			//{
			//	skinningDictionary.Remove(nameHash);
			//	skinningValid = false;
			//}

			//BoneData bone;
			//if (boneDictionary.TryGetValue(nameHash, out bone))
			//{
			//	if (bone.boneTransform != null)
			//	{
			//		UMAUtils.DestroySceneObject(bone.boneTransform.gameObject);
			//		bone.boneTransform = null;
			//	}
			//}

			// HACK doesn't make sense for a child bones to be retained
			ClearRetainedBoneHierachy(nameHash);
		}

		/// <summary>
		/// Marks the bone and all children unretained.
		/// </summary>
		/// <param name="nameHash">Hash of bone name.</param>
		public virtual void ClearRetainedBoneHierachy(int nameHash)
		{
			if (skinningDictionary.ContainsKey(nameHash))
			{
				skinningDictionary.Remove(nameHash);
				skinningValid = false;

				BoneData bone;
				if (boneDictionary.TryGetValue(nameHash, out bone) && (bone.boneTransform != null))
				{
					UMAUtils.DestroySceneObject(bone.boneTransform.gameObject);
					bone.boneTransform = null;
				}

				foreach (BoneData child in boneDictionary.Values)
				{
					if (child.umaTransform.parent == nameHash)
					{
						ClearRetainedBoneHierachy(child.umaTransform.hash);
					}
				}
			}
		}

		/// <summary>
		/// Creates a human (biped) avatar for a UMA character.
		/// </summary>
		/// <returns>The human avatar.</returns>
		/// <param name="umaTPose">UMA TPose.</param>
		public Avatar CreateAvatar(UmaTPose umaTPose, GameObject root)
		{
			HumanDescription description = new HumanDescription();
			description.armStretch = umaTPose.armStretch;
			description.feetSpacing = umaTPose.feetSpacing;
			description.legStretch = umaTPose.legStretch;
			description.lowerArmTwist = umaTPose.lowerArmTwist;
			description.lowerLegTwist = umaTPose.lowerLegTwist;
			description.upperArmTwist = umaTPose.upperArmTwist;
			description.upperLegTwist = umaTPose.upperLegTwist;

			UmaTPose.HumanBoneInfo[] boneInfo = umaTPose.humanBoneInfo;
			List<HumanBone> humanBones = new List<HumanBone>(boneInfo.Length);
			List<SkeletonBone> skeletonBones = new List<SkeletonBone>(boneInfo.Length);

			for (int i = 0; i < boneInfo.Length; i++)
			{
				BoneData bone;
				BoneData parentBone;

				//if (HumanTrait.RequiredBone(i) && !skinningDictionary.ContainsKey(boneInfo[i].boneHash))
				//{
				//}

				if (boneDictionary.TryGetValue(boneInfo[i].boneHash, out bone) && (bone.boneTransform != null))
				{
					Transform boneTransform = bone.boneTransform;
					Transform parentTransform;
					HumanBone humanBone;
					SkeletonBone skeletonBone;

					int parentIndex = HumanTrait.GetParentBone(i);
					if (parentIndex < 0)
					{
						// It isn't documented, but the transform structure
						// between the root game object and the Mecanim bones
						// MUST be at the beginning of the array
						parentTransform = boneTransform;
						while (parentTransform != root.transform)
						{
							parentTransform = parentTransform.parent;

							skeletonBone = new SkeletonBone();
							skeletonBone.name = parentTransform.name;
							skeletonBone.position = parentTransform.localPosition;
							skeletonBone.rotation = parentTransform.localRotation;
							skeletonBone.scale = parentTransform.localScale;
							skeletonBones.Add(skeletonBone);
						}
						// Doesn't seem to be required, but it's tidier
						skeletonBones.Reverse();
					}
					else
					{
						// Check for optional Mecanim bones
						// (or extraneous, but harmless extras)
						// which must be added to the skeleton
						int parentHash = bone.umaTransform.parent;
						while (parentHash != boneInfo[parentIndex].boneHash)
						{
							if (!HumanTrait.RequiredBone(parentIndex))
							{
								parentIndex = HumanTrait.GetParentBone(parentIndex);
							}
							else
							{
								// HACK - not safe!
								if (boneDictionary.TryGetValue(parentHash, out parentBone))
								{
									parentTransform = parentBone.boneTransform;

									skeletonBone = new SkeletonBone();
									skeletonBone.name = parentTransform.name;
									skeletonBone.position = parentTransform.localPosition;
									skeletonBone.rotation = parentTransform.localRotation;
									skeletonBone.scale = parentTransform.localScale;
									skeletonBones.Add(skeletonBone);

									parentHash = parentBone.umaTransform.parent;
									//Debug.LogFormat("Missing parent on: {0}. Expected: {1}, Found: {2}",
										//HumanTrait.BoneName[i], HumanTrait.BoneName[parentIndex], parentTransform.name);
								}
							}
						}
					}

					humanBone = new HumanBone();
					humanBone.boneName = boneTransform.name;
					humanBone.humanName = HumanTrait.BoneName[i];
					humanBone.limit = boneInfo[i].limit;
					humanBones.Add(humanBone);

					skeletonBone = new SkeletonBone();
					skeletonBone.name = boneTransform.name;
					skeletonBone.position = boneTransform.localPosition;
					skeletonBone.rotation = boneTransform.localRotation;
					skeletonBone.scale = boneTransform.localScale;
					skeletonBones.Add(skeletonBone);
				}
				else if (HumanTrait.RequiredBone(i))
				{
					Debug.LogError("Missing required bone: " + HumanTrait.BoneName[i]);
				}
				else
				{
					//Debug.Log("Missing optional bone: " + HumanTrait.BoneName[i]);
				}
			}

			description.human = humanBones.ToArray();
			description.skeleton = skeletonBones.ToArray();

			Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, description);
			if (!avatar.isValid)
			{
				Debug.LogError("Avatar is invalid!");
			}

			return avatar;
		}

		/// <summary>
		/// Gets the hash of a retained parent bone in the skeleton.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <returns>Hash of the closest retained parent (may be the original)</returns>
		public virtual int GetSkinningTarget(int nameHash)
		{
			if (!skinningValid) RepairSkinningDictionary();

			BoneData bone;

			//int hash = nameHash;
			//while (!skinningDictionary.ContainsKey(hash))
			//{
			//	if (boneDictionary.TryGetValue(hash, out bone))
			//	{
			//		hash = bone.umaTransform.parent;
			//	}
			//	else
			//	{
			//		Debug.LogError("Couldn't find bone in skeleton!");
			//		return nameHash;
			//	}
			//}
			//return hash;

			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				return bone.retainedHash;
			}
			else
			{
				Debug.LogError("Couldn't find bone in skeleton!");
				return 0;
			}
		}

		/// <summary>
		/// Gets the index of a retained bone in the skinning array.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <returns>Index in skinning array of bone</returns>
		public virtual int GetSkinningIndex(int nameHash)
		{
			if (!skinningValid) RepairSkinningDictionary();

			int index;
			if (!skinningDictionary.TryGetValue(nameHash, out index))
			{
				Debug.LogWarning("Had to add bone to skinning data.");
				index = skinningDictionary.Count;
				skinningDictionary.Add(nameHash, index);
			}

			return index;
		}

		/// <summary>
		/// Gets the bind matrix of a retained bone in the skinning array.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual Matrix4x4 GetSkinningBindToBone(int nameHash)
		{
			BoneData bone;
			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				//if (bone.umaTransform.bindToBone == Matrix4x4.zero)
				//{
				//	Debug.LogWarning("Skinning with bad data on: " + bone.umaTransform.name);
				//	return Matrix4x4.identity;
				//}
				return bone.umaTransform.bindToBone;
			}

			Debug.LogError("Could not find skinning bone in skeleton!");
			return Matrix4x4.identity;
		}

		/// <summary>
		/// Gets the matrix transferring from original to target bone.
		/// </summary>
		/// <param name="bone">Original bone.</param>
		/// <param name="targetHash">Name hash of target bone.</param>
		/// <returns>Matrix from original to target</returns>
		public virtual Matrix4x4 GetSkinningBoneToTarget(UMATransform bone, int targetHash)
		{
			BoneData oldBone;
			BoneData newBone;

			if (boneDictionary.TryGetValue(bone.hash, out oldBone) && boneDictionary.TryGetValue(targetHash, out newBone))
			{
				Matrix4x4 matrix = oldBone.umaTransform.boneToRoot.inverse * bone.boneToRoot;

				if (bone.hash == targetHash) return matrix;

				//return newBone.boneTransform.worldToLocalMatrix * oldBone.boneTransform.localToWorldMatrix * matrix;
				return oldBone.localToRetained * matrix;
			}
			else
			{
				Debug.LogError("Couldn't find skinning bone!");
				return Matrix4x4.identity;
			}
		}

		/// <summary>
		/// Gets the array of skinning binds.
		/// </summary>
		public virtual Matrix4x4[] GetSkinningBinds()
		{
			EnsureSkinningData();

			return skinningBinds;
		}

		/// <summary>
		/// Gets the array of skinning transforms.
		/// </summary>
		public virtual Transform[] GetSkinningTransforms()
		{
			EnsureSkinningData();

			return skinningTransforms;
		}

		/// <summary>
		/// Builds the skinning arrays if they are invalid.
		/// </summary>
		private void EnsureSkinningData()
		{
			if (!skinningValid) RepairSkinningDictionary();

			if ((skinningBinds == null) || (skinningBinds.Length != skinningDictionary.Count))
			{
				skinningBinds = new Matrix4x4[skinningDictionary.Count];
				skinningTransforms = new Transform[skinningDictionary.Count];

				foreach (KeyValuePair<int, int> skinning in skinningDictionary)
				{
					BoneData bone;
					if (boneDictionary.TryGetValue(skinning.Key, out bone) && (bone.boneTransform != null))
					{
						skinningBinds[skinning.Value] = bone.umaTransform.bindToBone;
						skinningTransforms[skinning.Value] = bone.boneTransform;
					}
					else
					{
						Debug.LogError("Couldn't find skinning bone in skeleton!");
					}
				}
			}
		}

		private void AddBonesRecursive(Transform transform)
		{
			var hash = UMAUtils.StringToHash(transform.name);
			var parentHash = transform.parent != null ? UMAUtils.StringToHash(transform.parent.name) : 0;
			AddBone(transform, hash, parentHash);

			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		protected virtual BoneData GetBone(int nameHash)
		{
			BoneData bone = null;
			boneDictionary.TryGetValue(nameHash, out bone);
			return bone;
		}

		/// <summary>
		/// Does this skeleton contains bone with specified name hash?
		/// </summary>
		/// <returns><c>true</c> if this instance has bone the specified name hash; otherwise, <c>false</c>.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool HasBone(int nameHash)
		{
			return boneDictionary.ContainsKey(nameHash);
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="parentHash">Hash of parent transform name.</param>
		/// <param name="hash">Hash of transform name.</param>
		/// <param name="transform">Transform.</param>
		protected virtual void AddBone(Transform transform, int hash, int parentHash)
		{
			BoneData newBone = new BoneData()
			{
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash)
			};

			if (!boneDictionary.ContainsKey(hash))
			{
				boneDictionary.Add(hash, newBone);
			}
			else
			{
				Debug.LogError("AddBone: " + transform.name + " already exists in the dictionary!");
			}

			// Any bone with a transform will default to retained 
			SetRetainedBone(hash);
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="umaTransform">UMA Transform.</param>
		protected virtual void AddBone(UMATransform umaTransform)
		{
			BoneData newBone = new BoneData()
			{
				umaTransform = umaTransform.Duplicate()
			};
			// HACK
			//newBone.boneTransform = new GameObject(umaTransform.name).transform;

			if (!boneDictionary.ContainsKey(umaTransform.hash))
			{
				boneDictionary.Add(umaTransform.hash, newBone);
			}
			else
			{
				Debug.LogError("AddBone: " + umaTransform.name + " already exists in the dictionary!");
			}
		}

		/// <summary>
		/// Removes the bone with the given name hash.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual void RemoveBone(int nameHash)
		{
			if (boneDictionary.ContainsKey(nameHash))
			{
				boneDictionary.Remove(nameHash);
			}

			if (skinningDictionary.ContainsKey(nameHash))
			{
				skinningDictionary.Remove(nameHash);
			}
		}

		/// <summary>
		/// Tries to find bone transform in skeleton.
		/// </summary>
		/// <returns><c>true</c>, if transform was found, <c>false</c> otherwise.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="boneTransform">Bone transform.</param>
		public virtual bool TryGetBoneTransform(int nameHash, out Transform boneTransform)
		{
			BoneData bone;
			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				boneTransform = bone.boneTransform;
			}
			else
			{
				boneTransform = null;
			}

			return (boneTransform != null);
		}

		/// <summary>
		/// Gets the transform for a bone in the skeleton.
		/// </summary>
		/// <returns>The transform or null, if not found.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Transform GetBoneTransform(int nameHash)
		{
			BoneData bone;
			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				return bone.boneTransform;
			}

			return null;
		}

		/// <summary>
		/// Gets the game object for a transform in the skeleton.
		/// </summary>
		/// <returns>The game object or null, if not found.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual GameObject GetBoneGameObject(int nameHash)
		{
			BoneData bone;
			if (boneDictionary.TryGetValue(nameHash, out bone))
			{
				if (bone.boneTransform != null)
					return bone.boneTransform.gameObject;
			}

			return null;
		}

		protected virtual IEnumerable<int> GetBoneHashes()
		{
			foreach (int hash in boneDictionary.Keys)
			{
				yield return hash;
			}
		}
		// HACK
		//DynamicUMADna:: a method to return a string of bonenames for use in editor intefaces
		private string[] GetBoneNames()
		{
			string[] boneNames = new string[boneDictionary.Count];
			int index = 0;

			foreach (BoneData bone in boneDictionary.Values)
			{
				boneNames[index++] = bone.umaTransform.name;
			}

			return boneNames;
		}

		public virtual void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localPosition = position;
				//db.boneTransform.localRotation = rotation;
				//db.boneTransform.localScale = scale;

				db.dnaPosition = position;
				db.dnaRotation = rotation;
				db.dnaScale = scale;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Sets the position of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="position">Position.</param>
		public virtual void SetPosition(int nameHash, Vector3 position)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localPosition = position;

				db.dnaPosition = position;
			}
		}

		/// <summary>
		/// Sets the position of a bone relative to it's old position.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="delta">Position delta.</param>
		public virtual void SetPositionRelative(int nameHash, Vector3 delta)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localPosition = db.boneTransform.localPosition + delta;

				db.dnaPosition += delta;
			}
		}

		/// <summary>
		/// Sets the scale of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="scale">Scale.</param>
		public virtual void SetScale(int nameHash, Vector3 scale)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localScale = scale;

				db.dnaScale = scale;
			}
		}

		/// <summary>
		/// DynamicUMADnaConverterBahaviour:: Sets the scale of a bone relatively.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="scale">Scale.</param>
		public virtual void SetScaleRelative(int nameHash, Vector3 scale)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//var fullScale = scale;
				//fullScale.Scale(db.boneTransform.localScale);
				//db.boneTransform.localScale = fullScale;

				db.dnaScale.Scale(db.dnaScale);
			}
		}

		/// <summary>
		/// Sets the rotation of a bone.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="rotation">Rotation.</param>
		public virtual void SetRotation(int nameHash, Quaternion rotation)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localRotation = rotation;

				db.dnaRotation = rotation;
			}
		}

		/// <summary>
		/// DynamicUMADnaConverterBahaviour:: Sets the rotation of a bone relative to its initial rotation.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="rotation">Rotation.</param>
		/// <param name="weight">Weight.</param>
		public virtual void SetRotationRelative(int nameHash, Quaternion rotation, float weight /*, bool hasAnimator = true*/)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				//db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);

				Quaternion fullRotation = db.dnaRotation * rotation;
				db.dnaRotation = Quaternion.Slerp(db.dnaRotation, fullRotation, weight);
			}
		}

		/// <summary>
		/// Lerp the specified bone toward a new position, rotation, and scale.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="position">Position.</param>
		/// <param name="scale">Scale.</param>
		/// <param name="rotation">Rotation.</param>
		/// <param name="weight">Weight.</param>
		public virtual void Lerp(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localPosition = Vector3.Lerp(db.boneTransform.localPosition, position, weight);
				//db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, rotation, weight);
				//db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);

				db.dnaPosition = Vector3.Lerp(db.dnaPosition, position, weight);
				db.dnaRotation = Quaternion.Slerp(db.dnaRotation, rotation, weight);
				db.dnaScale = Vector3.Lerp(db.dnaScale, scale, weight);
			}
		}

		/// <summary>
		/// Morph the specified bone toward a relative position, rotation, and scale.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="position">Position.</param>
		/// <param name="scale">Scale.</param>
		/// <param name="rotation">Rotation.</param>
		/// <param name="weight">Weight.</param>
		public virtual void Morph(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//db.boneTransform.localPosition += position * weight;
				//if (rotation != Quaternion.identity)
				//{
				//	Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				//	db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
				//}
				//if (scale != Vector3.one)
				//{
				//	var fullScale = scale;
				//	fullScale.Scale(db.boneTransform.localScale);
				//	db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, fullScale, weight);
				//}

				db.dnaPosition += position * weight;
				if (rotation != Quaternion.identity)
				{
					Quaternion fullRotation = db.dnaRotation * rotation;
					db.dnaRotation = Quaternion.Slerp(db.dnaRotation, fullRotation, weight);
				}
				if (scale != Vector3.one)
				{
					Vector3 fullScale = scale;
					fullScale.Scale(db.dnaScale);
					db.dnaScale = Vector3.Lerp(db.dnaScale, fullScale, weight);
				}
			}
		}

		/// <summary>
		/// Reset the specified transform to the pre-DNA state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Reset(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				// HACK ???
				if (db.boneTransform != null)
				{
					db.boneTransform.localPosition = db.umaTransform.position;
					db.boneTransform.localRotation = db.umaTransform.rotation;
					db.boneTransform.localScale = db.umaTransform.scale;
				}

				db.dnaPosition = db.umaTransform.position;
				db.dnaRotation = db.umaTransform.rotation;
				db.dnaScale = db.umaTransform.scale;

				return true;
			}

			return false;
		}

		/// <summary>
		/// Reset all transforms to the pre-DNA state.
		/// </summary>
		public virtual void ResetAll()
		{
			foreach (BoneData db in boneDictionary.Values)
			{
				// HACK ???
				if (db.boneTransform != null)
				{
					db.boneTransform.localPosition = db.umaTransform.position;
					db.boneTransform.localRotation = db.umaTransform.rotation;
					db.boneTransform.localScale = db.umaTransform.scale;
				}

				db.dnaPosition = db.umaTransform.position;
				db.dnaRotation = db.umaTransform.rotation;
				db.dnaScale = db.umaTransform.scale;
			}
		}

		/// <summary>
		/// Restore the specified transform to the post-DNA state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Restore(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				//db.boneTransform.localPosition = db.position;
				//db.boneTransform.localRotation = db.rotation;
				//db.boneTransform.localScale = db.scale;

				db.dnaPosition = db.position;
				db.dnaRotation = db.rotation;
				db.dnaScale = db.scale;

				return true;
			}

			return false;
		}

		/// <summary>
		/// Restore all transforms to the post-DNA state.
		/// </summary>
		public virtual void RestoreAll()
		{
			foreach (BoneData db in boneDictionary.Values)
			{
				if (db.boneTransform != null)
				{
					//db.boneTransform.localPosition = db.position;
					//db.boneTransform.localRotation = db.rotation;
					//db.boneTransform.localScale = db.scale;

					db.dnaPosition = db.position;
					db.dnaRotation = db.rotation;
					db.dnaScale = db.scale;
				}
			}
		}

		// HACK
		/*
		/// <summary>
		/// Gets the position of a bone.
		/// </summary>
		/// <returns>The position.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Vector3 GetPosition(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//return db.boneTransform.localPosition;
				return db.dnaPosition;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the rotation of a bone.
		/// </summary>
		/// <returns>The rotation.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Quaternion GetRotation(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//return db.boneTransform.localRotation;
				return db.dnaRotation;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the scale of a bone.
		/// </summary>
		/// <returns>The scale.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Vector3 GetScale(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				//return db.boneTransform.localScale;
				return db.dnaScale;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}
		*/

		/// <summary>
		/// Ensures the bone exists in the skeleton.
		/// </summary>
		/// <param name="umaTransform">UMA transform.</param>
		public virtual void EnsureBone(UMATransform umaTransform)
		{
			if (!boneDictionary.ContainsKey(umaTransform.hash))
			{
				AddBone(umaTransform);
			}
		}

		/// <summary>
		/// Ensures all bones are properly initialized and parented.
		/// </summary>
		public virtual void EnsureBoneHierarchy()
		{
			foreach (BoneData entry in boneDictionary.Values)
			{
				if (boneDictionary.ContainsKey(entry.umaTransform.parent) && (entry.boneTransform != null))
				{
					entry.boneTransform.parent = boneDictionary[entry.umaTransform.parent].boneTransform;
				}
			}

			ResetAll();
		}

		//public virtual Transform[] HashesToTransforms(int[] boneNameHashes)
		//{
		//	Transform[] res = new Transform[boneNameHashes.Length];
		//	for (int i = 0; i < boneNameHashes.Length; i++)
		//	{
		//		res[i] = boneDictionary[boneNameHashes[i]].boneTransform;
		//	}
		//	return res;
		//}

		//public virtual Transform[] HashesToTransforms(List<int> boneNameHashes)
		//{
		//	Transform[] res = new Transform[boneNameHashes.Count];
		//	for (int i = 0; i < boneNameHashes.Count; i++)
		//	{
		//		res[i] = boneDictionary[boneNameHashes[i]].boneTransform;
		//	}
		//	return res;
		//}

		//public virtual Quaternion GetTPoseCorrectedRotation(int nameHash, Quaternion tPoseRotation)
		//{
		//	return boneDictionary[nameHash].boneTransform.localRotation;
		//}
	}
}
