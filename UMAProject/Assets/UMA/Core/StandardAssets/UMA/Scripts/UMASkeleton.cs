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
			public int boneNameHash;
			public int parentBoneNameHash;
			public Transform boneTransform;
			public UMATransform umaTransform;
			public Quaternion rotation;
			public Vector3 position;
			public Vector3 scale;
			public int accessedFrame;
		}

		public IEnumerable<int> BoneHashes { get { return GetBoneHashes(); } }

		//DynamicUMADna:: DynamicUMADnaConverterCustomizer Editor interface needs to have an array of bone names
		public string[] BoneNames { get { return GetBoneNames(); } }

		protected bool updating;
		protected int frame;
		/// <value>The hash for the root bone of the skeleton.</value>
		public int rootBoneHash { get; protected set; }
		/// <value>The bone count.</value>
		public virtual int boneCount { get { return boneHashData.Count; } }

		public bool isUpdating { get { return updating; } }

		private Dictionary<int, BoneData> boneHashDataLookup;

#if UNITY_EDITOR
		// Dictionary backup to support code reload
		private List<BoneData> boneHashDataBackup = new List<BoneData>();
#endif

		public Dictionary<int, BoneData> boneHashData
		{
			get
			{
				if (boneHashDataLookup == null)
				{
					boneHashDataLookup = new Dictionary<int, BoneData>();
#if UNITY_EDITOR
					foreach (BoneData tData in boneHashDataBackup)
					{
						boneHashDataLookup.Add(tData.boneNameHash, tData);
					}
#endif
				}

				return boneHashDataLookup;
			}

			set
			{
				boneHashDataLookup = value;
#if UNITY_EDITOR
				boneHashDataBackup = new List<BoneData>(value.Values);
#endif
			}
		}


		/// <summary>
		/// Initializes a new UMASkeleton from a transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		public UMASkeleton(Transform rootBone)
		{
			rootBoneHash = UMAUtils.StringToHash(rootBone.name);
			this.boneHashData = new Dictionary<int, BoneData>();
			BeginSkeletonUpdate();
			AddBonesRecursive(rootBone);
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
			frame++;
			if (frame < 0) frame = 0;
			updating = true;
		}

		/// <summary>
		/// Marks the skeleton update as complete.
		/// </summary>
		public virtual void EndSkeletonUpdate()
		{
			foreach (var bd in boneHashData.Values)
			{
				if (bd != null && bd.boneTransform != null)
				{
					bd.rotation = bd.boneTransform.localRotation;
					bd.position = bd.boneTransform.localPosition;
					bd.scale = bd.boneTransform.localScale;
				}
			}
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
			if (transform.tag == UMAContextBase.IgnoreTag)
				return;

			var hash = UMAUtils.StringToHash(transform.name);
			var parentHash = transform.parent != null ? UMAUtils.StringToHash(transform.parent.name) : 0;
			BoneData data = new BoneData()
			{
				parentBoneNameHash = parentHash,
				boneNameHash = hash,
				accessedFrame = frame,
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash)
			};

			if (!boneHashData.ContainsKey(hash))
			{
				boneHashData.Add(hash, data);
#if UNITY_EDITOR
				boneHashDataBackup.Add(data);
#endif
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.LogError("AddBonesRecursive: " + transform.name + " already exists in the dictionary! Consider renaming those bones. For example, `Items` under each hand bone can become `LeftItems` and `RightItems`.");
			}

			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		protected virtual BoneData GetBone(int nameHash)
		{
			BoneData data = null;
			boneHashData.TryGetValue(nameHash, out data);
			return data;
		}

		/// <summary>
		/// Does this skeleton contains bone with specified name hash?
		/// </summary>
		/// <returns><c>true</c> if this instance has bone the specified name hash; otherwise, <c>false</c>.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool HasBone(int nameHash)
		{
			return boneHashData.ContainsKey(nameHash);
		}

		/// <summary>
		/// Check if the bone exists and is valid.
		/// </summary>
		/// <param name="nameHash">the namehash of the bone to check</param>
		/// <returns>true if the bone exists and is valid</returns>
		public virtual bool BoneExists(int nameHash)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				return db.boneTransform != null;
			}
			return false;
		}

		/// <summary>
		/// Adds the transform into the skeleton.
		/// </summary>
		/// <param name="parentHash">Hash of parent transform name.</param>
		/// <param name="hash">Hash of transform name.</param>
		/// <param name="transform">Transform.</param>
		public virtual void AddBone(int parentHash, int hash, Transform transform)
		{
			BoneData newBone = new BoneData()
			{
				accessedFrame = frame,
				parentBoneNameHash = parentHash,
				boneNameHash = hash,
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash),
			};

			if (!boneHashData.ContainsKey(hash))
			{
				boneHashData.Add(hash, newBone);
#if UNITY_EDITOR
				boneHashDataBackup.Add(newBone);
#endif
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.LogError("AddBone: " + transform.name + " already exists in the dictionary! Consider renaming those bones. For example, `Items` under each hand bone can become `LeftItems` and `RightItems`.");
			}
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
				accessedFrame = -1,
				parentBoneNameHash = transform.parent,
				boneNameHash = transform.hash,
				boneTransform = go.transform,
				umaTransform = transform.Duplicate(),
			};

			if (!boneHashData.ContainsKey(transform.hash))
			{
				boneHashData.Add(transform.hash, newBone);
#if UNITY_EDITOR
				boneHashDataBackup.Add(newBone);
#endif
			}
			else
			{
				if (Debug.isDebugBuild)
					Debug.LogError("AddBone: " + transform.name + " already exists in the dictionary! Consider renaming those bones. For example, `Items` under each hand bone can become `LeftItems` and `RightItems`.");
			}
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
				boneHashData.Remove(nameHash);
#if UNITY_EDITOR
				boneHashDataBackup.Remove(bd);
#endif
			}
		}

		/// <summary>
		/// Tries to find bone transform in skeleton.
		/// </summary>
		/// <returns><c>true</c>, if transform was found, <c>false</c> otherwise.</returns>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="boneTransform">Bone transform.</param>
		/// <param name="transformDirty">Transform is dirty.</param>
		/// <param name="parentBoneNameHash">Name hash of parent bone.</param>
		public virtual bool TryGetBoneTransform(int nameHash, out Transform boneTransform, out bool transformDirty, out int parentBoneNameHash)
		{
			BoneData res;
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				transformDirty = res.accessedFrame != frame;
				res.accessedFrame = frame;
				boneTransform = res.boneTransform;
				parentBoneNameHash = res.parentBoneNameHash;
				return true;
			}
			transformDirty = false;
			boneTransform = null;
			parentBoneNameHash = 0;
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
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				res.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				res.accessedFrame = frame;
				if (res.boneTransform == null)
                {
					return null;
                }
				return res.boneTransform.gameObject;
			}
			return null;
		}

		protected virtual IEnumerable<int> GetBoneHashes()
		{
			foreach (int hash in boneHashData.Keys)
			{
				yield return hash;
			}
		}
		//DynamicUMADna:: a method to return a string of bonenames for use in editor intefaces
		private string[] GetBoneNames()
		{
			string[] boneNames = new string[boneHashData.Count];
			int index = 0;
			foreach (KeyValuePair<int, BoneData> kp in boneHashData)
			{
				boneNames[index] = kp.Value.boneTransform.gameObject.name;
				index++;
			}
			return boneNames;
		}

		public virtual void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = position;
				db.boneTransform.localRotation = rotation;
				db.boneTransform.localScale = scale;
				db.umaTransform.rotation = rotation;
				db.umaTransform.position = position;
				db.umaTransform.scale = scale;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = position;
			}
		}

		/// <summary>
		/// Sets the position of a bone relative to it's old position.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="delta">Position delta.</param>
		/// <param name="weight">Optionally set how much to apply the new position</param>
		public virtual void SetPositionRelative(int nameHash, Vector3 delta, float weight = 1f)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = db.boneTransform.localPosition + delta * weight;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localScale = scale;
			}
		}

		/// <summary>
		/// DynamicUMADnaConverterBahaviour:: Sets the scale of a bone relatively.
		/// This method silently fails if the bone doesn't exist! (Desired behaviour in DNA converters due to LOD/Occlusion)
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		/// <param name="scale">Scale.</param>
		/// <param name="weight">Optionally set how much to apply the new scale</param>
		public virtual void SetScaleRelative(int nameHash, Vector3 scale, float weight = 1f)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				scale.Scale(db.boneTransform.localScale);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = Vector3.Lerp(db.boneTransform.localPosition, position, weight);
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, db.boneTransform.localRotation, weight);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
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
		public virtual void Morph(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition += position * weight;
				Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
				var fullScale = scale;
				fullScale.Scale(db.boneTransform.localScale);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, fullScale, weight);
			}
		}

		/// <summary>
		/// Reset the specified transform to the pre-dna state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Reset(int nameHash)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.accessedFrame = frame;
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
			foreach (BoneData db in boneHashData.Values)
			{
				if (db.boneTransform != null)
				{
					db.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out db) && (db.boneTransform != null))
			{
				db.accessedFrame = frame;
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
			foreach (BoneData db in boneHashData.Values)
			{
				if (db.boneTransform != null)
				{
					db.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return db.boneTransform.localPosition;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		/// <summary>
		/// Gets the position of a bone.
		/// </summary>
		/// <returns>The position.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Vector3 GetRelativePosition(int nameHash)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return boneHashData[rootBoneHash].boneTransform.parent.parent.worldToLocalMatrix.MultiplyPoint(db.boneTransform.position);
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
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
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				return db.boneTransform.localRotation;
			}
			else
			{
				throw new Exception("Bone not found. BoneHash: " + nameHash);
			}
		}

		public static int StringToHash(string name) { return UMAUtils.StringToHash(name); }

		public virtual Transform[] HashesToTransforms(int[] boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Length];
			for (int i = 0; i < boneNameHashes.Length; i++)
			{
				res[i] = boneHashData[boneNameHashes[i]].boneTransform;
			}
			return res;
		}

		public virtual Transform[] HashesToTransforms(List<int> boneNameHashes)
		{
			Transform[] res = new Transform[boneNameHashes.Count];
			for (int i = 0; i < boneNameHashes.Count; i++)
			{
				res[i] = boneHashData[boneNameHashes[i]].boneTransform;
			}
			return res;
		}

		/// <summary>
		/// Ensures the bone exists in the skeleton.
		/// </summary>
		/// <param name="umaTransform">UMA transform.</param>
		public virtual void EnsureBone(UMATransform umaTransform)
		{
			if (boneHashData.ContainsKey(umaTransform.hash) == false)
				AddBone(umaTransform);
		}

		/// <summary>
		/// Ensures all bones are properly initialized and parented.
		/// </summary>
		public virtual void EnsureBoneHierarchy()
		{
			foreach (var entry in boneHashData.Values)
			{
				if (entry.accessedFrame == -1)
				{
					if (boneHashData.ContainsKey(entry.umaTransform.parent))
					{
						entry.boneTransform.parent = boneHashData[entry.umaTransform.parent].boneTransform;
						entry.boneTransform.localPosition = entry.umaTransform.position;
						entry.boneTransform.localRotation = entry.umaTransform.rotation;
						entry.boneTransform.localScale = entry.umaTransform.scale;
						entry.accessedFrame = frame;
					}
					else
					{
						if (Debug.isDebugBuild)
							Debug.LogError("EnsureBoneHierarchy: " + entry.umaTransform.name + " parent not found in dictionary!");
					}
				}
			}
		}

		public virtual Quaternion GetTPoseCorrectedRotation(int nameHash, Quaternion tPoseRotation)
		{
			return boneHashData[nameHash].boneTransform.localRotation;
		}
	}
}
