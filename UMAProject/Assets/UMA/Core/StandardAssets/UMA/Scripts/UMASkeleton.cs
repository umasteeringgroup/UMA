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
		public class BoneData
		{
			// Just use the ones in the UMATransform
//			public int boneNameHash;
//			public int parentBoneNameHash;
			public Transform boneTransform;
			public UMATransform umaTransform;
			public Quaternion rotation;
			public Vector3 position;
			public Vector3 scale;
//			public Matrix4x4 bind;
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

		protected SerializableDictionary<int, BoneData> boneDictionary;
		protected SerializableDictionary<int, int> skinningDictionary;

		/// <summary>
		/// Initializes a new UMASkeleton from a transform hierarchy.
		/// </summary>
		/// <param name="umaRenderer">Skinned mesh renderer.</param>
		public UMASkeleton(SkinnedMeshRenderer umaRenderer)
		{
			rootBoneHash = UMAUtils.StringToHash(umaRenderer.rootBone.name);
			boneDictionary = new SerializableDictionary<int, BoneData>();
			skinningDictionary = new SerializableDictionary<int, int>();
			BeginSkeletonUpdate();
			AddBonesRecursive(umaRenderer.rootBone);
			EndSkeletonUpdate();
		}

		/// <summary>
		/// Initializes a new UMASkeleton from the recipe in UMAData.
		/// </summary>
		/// <param name="umaData">UMAData.</param>
		public UMASkeleton(UMAData umaData)
		{
			boneDictionary = new SerializableDictionary<int, BoneData>();
			skinningDictionary = new SerializableDictionary<int, int>();

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
				AddBone(newGlobal.transform, UMAUtils.StringToHash(newGlobal.name), rootBoneHash);
			}


			foreach (SlotData slot in umaData.umaRecipe.slotDataList) 
			{
				if (slot != null)
				{
					UMAMeshData meshData = slot.asset.meshData;
					if (meshData == null) continue;

					for (int i = 0; i < meshData.umaBoneCount; i++)
					{
						UMATransform bone = meshData.umaBones[i];
						if (!boneDictionary.ContainsKey(bone.hash))
						{
							AddBone(bone);
						}
					}
				}
			}
			EnsureBoneHierarchy();
			EndSkeletonUpdate();
		}

		protected UMASkeleton()
		{
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
			foreach (var bd in boneDictionary.Values)
			{
				bd.rotation = bd.boneTransform.localRotation;
				bd.position = bd.boneTransform.localPosition;
				bd.scale = bd.boneTransform.localScale;
			}

			// HACK need to fix things here???

			updating = false;
		}

		public virtual void SetAnimatedBone(int nameHash)
		{
			// The default MeshCombiner is ignoring the animated bones, virtual method added to share common interface.
		}

		public virtual void SetAnimatedBoneHierachy(int nameHash)
		{
			// The default MeshCombiner is ignoring the animated bones, virtual method added to share common interface.
		}

		public virtual void ClearAnimatedBoneHierachy(int nameHash, bool recursive)
		{
			// The default MeshCombiner is ignoring the animated bones, virtual method added to share common interface.
		}

		private void AddBonesRecursive(Transform transform)
		{
			var hash = UMAUtils.StringToHash(transform.name);
			var parentHash = transform.parent != null ? UMAUtils.StringToHash(transform.parent.name) : 0;
			BoneData data = new BoneData()
			{
//				parentBoneNameHash = parentHash,
//				boneNameHash = hash,
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash)
			};

			if (!boneDictionary.ContainsKey(hash))
			{
				boneDictionary.Add(hash, data);
			}
			else
				Debug.LogError("AddBonesRecursive: " + transform.name + " already exists in the dictionary!");

			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		protected virtual BoneData GetBone(int nameHash)
		{
			BoneData data = null;
			boneDictionary.TryGetValue(nameHash, out data);
			return data;
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
		public virtual void AddBone(Transform transform, int hash, int parentHash)
		{
			BoneData newBone = new BoneData()
			{
//				parentBoneNameHash = parentHash,
//				boneNameHash = hash,
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash),
			};

			if (!boneDictionary.ContainsKey(hash))
			{
				boneDictionary.Add(hash, newBone);
			}
			else
				Debug.LogError("AddBone: " + transform.name + " already exists in the dictionary!");
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="transform">Transform.</param>
		public virtual void AddBone(UMATransform transform)
		{
			var go = new GameObject(transform.name);
			BoneData newBone = new BoneData()
			{
//				parentBoneNameHash = transform.parent,
//				boneNameHash = transform.hash,
				boneTransform = go.transform,
				umaTransform = transform.Duplicate(),
			};

			if (!boneDictionary.ContainsKey(transform.hash))
			{
				boneDictionary.Add(transform.hash, newBone);
			}
			else
				Debug.LogError("AddBone: " + transform.name + " already exists in the dictionary!");
		}

		/// <summary>
		/// Removes the bone with the given name hash.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual void RemoveBone(int nameHash)
		{
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				boneDictionary.Remove(nameHash);
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
			BoneData res;
			if (boneDictionary.TryGetValue(nameHash, out res))
			{
				boneTransform = res.boneTransform;
				return true;
			}

			boneTransform = null;
			return false;
		}

		/// <summary>
		/// Gets the transform for a bone in the skeleton.
		/// </summary>
		/// <returns>The transform or null, if not found.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Transform GetBoneTransform(int nameHash)
		{
			BoneData res;
			if (boneDictionary.TryGetValue(nameHash, out res))
			{
				return res.boneTransform;
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
			BoneData res;
			if (boneDictionary.TryGetValue(nameHash, out res))
			{
				return res.boneTransform.gameObject;
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
		//DynamicUMADna:: a method to return a string of bonenames for use in editor intefaces
		private string[] GetBoneNames()
		{
			string[] boneNames = new string[boneDictionary.Count];
			int index = 0;
			foreach (KeyValuePair<int, BoneData> kp in boneDictionary)
			{
				boneNames[index] = kp.Value.boneTransform.gameObject.name;
				index++;
			}
			return boneNames;
		}

		public virtual void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db))
			{
				db.boneTransform.localPosition = position;
				db.boneTransform.localRotation = rotation;
				db.boneTransform.localScale = scale;
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
				db.boneTransform.localPosition = position;
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
				db.boneTransform.localPosition = db.boneTransform.localPosition + delta;
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
				db.boneTransform.localScale = scale;
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
				var fullScale = scale;
				fullScale.Scale(db.boneTransform.localScale);
				db.boneTransform.localScale = fullScale;
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
				db.boneTransform.localRotation = rotation;
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
				Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
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
				db.boneTransform.localPosition = Vector3.Lerp(db.boneTransform.localPosition, position, weight);
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, db.boneTransform.localRotation, weight);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
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
				db.boneTransform.localPosition += position * weight;
				if (rotation != Quaternion.identity)
				{
					Quaternion fullRotation = db.boneTransform.localRotation * rotation;
					db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
				}
				if (scale != Vector3.one)
				{
					var fullScale = scale;
					fullScale.Scale(db.boneTransform.localScale);
					db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, fullScale, weight);
				}
			}
		}

		/// <summary>
		/// Reset the specified transform to the pre-dna state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Reset(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.boneTransform.localPosition = db.umaTransform.position;
				db.boneTransform.localRotation = db.umaTransform.rotation;
				db.boneTransform.localScale = db.umaTransform.scale;

				return true;
			}

			return false;
		}

		/// <summary>
		/// Reset all transforms to the pre-dna state.
		/// </summary>
		public virtual void ResetAll()
		{
			foreach (BoneData db in boneDictionary.Values)
			{
				if (db.boneTransform != null)
				{
					db.boneTransform.localPosition = db.umaTransform.position;
					db.boneTransform.localRotation = db.umaTransform.rotation;
					db.boneTransform.localScale = db.umaTransform.scale;
				}
			}
		}

		/// <summary>
		/// Restore the specified transform to the post-dna state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Restore(int nameHash)
		{
			BoneData db;
			if (boneDictionary.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.boneTransform.localPosition = db.position;
				db.boneTransform.localRotation = db.rotation;
				db.boneTransform.localScale = db.scale;

				return true;
			}

			return false;
		}

		/// <summary>
		/// Restore all transforms to the post-dna state.
		/// </summary>
		public virtual void RestoreAll()
		{
			foreach (BoneData db in boneDictionary.Values)
			{
				if (db.boneTransform != null)
				{
					db.boneTransform.localPosition = db.position;
					db.boneTransform.localRotation = db.rotation;
					db.boneTransform.localScale = db.scale;
				}
			}
		}
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
				return db.boneTransform.localPosition;
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
				return db.boneTransform.localScale;
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
				return db.boneTransform.localRotation;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		public static int StringToHash(string name) { return UMAUtils.StringToHash(name); }

		public virtual Transform[] HashesToTransforms(int[] boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Length];
			for (int i = 0; i < boneNameHashes.Length; i++)
			{
				res[i] = boneDictionary[boneNameHashes[i]].boneTransform;
			}
			return res;
		}

		public virtual Transform[] HashesToTransforms(List<int> boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Count];
			for (int i = 0; i < boneNameHashes.Count; i++)
			{
				res[i] = boneDictionary[boneNameHashes[i]].boneTransform;
			}
			return res;
		}

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
				entry.boneTransform.localPosition = entry.umaTransform.position;
				entry.boneTransform.localRotation = entry.umaTransform.rotation;
				entry.boneTransform.localScale = entry.umaTransform.scale;
				if (boneDictionary.ContainsKey(entry.umaTransform.parent))
				{
					entry.boneTransform.parent = boneDictionary[entry.umaTransform.parent].boneTransform;
				}
//				else
//					Debug.LogError("EnsureBoneHierarchy: " + entry.umaTransform.name + " parent not found in dictionary!");
			}
		}

		public virtual Quaternion GetTPoseCorrectedRotation(int nameHash, Quaternion tPoseRotation)
		{
			return boneDictionary[nameHash].boneTransform.localRotation;
		}
	}
}
