using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using System;

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
			public int accessedFrame;
		}

		public IEnumerable<int> BoneHashes { get { return GetBoneHashes(); } }

		protected bool updating;
		protected int frame;
		/// <value>The hash for the root bone of the skeleton.</value>
		public int rootBoneHash { get; protected set; }
		/// <value>The bone count.</value>
		public virtual int boneCount { get { return boneHashData.Count; } }

		List<BoneData> boneHashData = new List<BoneData>();
		Dictionary<int, BoneData> boneHashDataLookup = new Dictionary<int, BoneData>();

		/// <summary>
		/// Initializes a new UMASkeleton from a transform hierarchy.
		/// </summary>
		/// <param name="rootBone">Root transform.</param>
		public UMASkeleton(Transform rootBone)
		{
			rootBoneHash = UMAUtils.StringToHash(rootBone.name);
			AddBonesRecursive(rootBone);
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


		private void AddBonesRecursive(Transform transform)
		{
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

			boneHashData.Add(data);
			boneHashDataLookup.Add(hash, data);

			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		protected virtual BoneData GetBone(int nameHash)
		{
			if (boneHashDataLookup == null)
				boneHashDataLookup = new Dictionary<int,BoneData>();

			BoneData data = null;
			bool inLookup = boneHashDataLookup.TryGetValue(nameHash, out data);

			if (!inLookup)
			{
				// Search the fallback list for the correct hash
				foreach (BoneData tData in boneHashData)
				{
					if (tData.boneNameHash == nameHash)
					{
						data = tData;
						boneHashDataLookup.Add(nameHash, tData);
						break;
					}
				}
			}

			return data;
		}

		/// <summary>
		/// Does this skeleton contains bone with specified name hash?
		/// </summary>
		/// <returns><c>true</c> if this instance has bone the specified name hash; otherwise, <c>false</c>.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool HasBone(int nameHash)
		{
			return GetBone(nameHash) != null;
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

			boneHashData.Add(newBone);
			boneHashDataLookup.Add(hash, newBone);
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

			boneHashData.Add(newBone);
			boneHashDataLookup.Add(transform.hash, newBone);
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
				boneHashData.Remove(bd);
				boneHashDataLookup.Remove(nameHash);
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
			BoneData res = GetBone(nameHash);
			if (res != null)
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
		/// Gets the game object for a transform in the skeleton.
		/// </summary>
		/// <returns>The game object or null, if not found.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual GameObject GetBoneGameObject(int nameHash)
		{
			BoneData res = GetBone(nameHash);
			if (res != null)
			{
				res.accessedFrame = frame;
				return res.boneTransform.gameObject;
			}
			return null;
		}

		protected virtual IEnumerable<int> GetBoneHashes()
		{
			foreach (BoneData data in boneHashData)
			{
				yield return data.boneNameHash;
			}
		}

		public virtual void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localPosition = position;
				bd.boneTransform.localRotation = rotation;
				bd.boneTransform.localScale = scale;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localPosition = position;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localPosition = bd.boneTransform.localPosition + delta;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localScale = scale;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localRotation = rotation;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localPosition += position * weight;
				Quaternion fullRotation = bd.boneTransform.localRotation * rotation;
				bd.boneTransform.localRotation = Quaternion.Slerp(bd.boneTransform.localRotation, fullRotation, weight);
				bd.boneTransform.localScale = Vector3.Lerp(bd.boneTransform.localScale, scale, weight);
			}
		}

		/// <summary>
		/// Reset the specified transform to the default state.
		/// </summary>
		/// <param name="nameHash">Name hash.</param>
		public virtual bool Reset(int nameHash)
		{
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				bd.boneTransform.localPosition = bd.umaTransform.position;
				bd.boneTransform.localRotation = bd.umaTransform.rotation;
				bd.boneTransform.localScale = bd.umaTransform.scale;

				return true;
			}

			return false;
		}

		/// <summary>
		/// Reset all transforms to the default state.
		/// </summary>
		public virtual void ResetAll()
		{
			foreach (BoneData db in boneHashData)
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
		/// Gets the position of a bone.
		/// </summary>
		/// <returns>The position.</returns>
		/// <param name="nameHash">Name hash.</param>
		public virtual Vector3 GetPosition(int nameHash)
		{
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				return bd.boneTransform.localPosition;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				return bd.boneTransform.localScale;
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
			BoneData bd = GetBone(nameHash);
			if (bd != null)
			{
				bd.accessedFrame = frame;
				return bd.boneTransform.localRotation;
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
				BoneData bd = GetBone(boneNameHashes[i]);
				res[i] = bd.boneTransform;
			}
			return res;
		}

		/// <summary>
		/// Ensures the bone exists in the skeleton.
		/// </summary>
		/// <param name="umaTransform">UMA transform.</param>
		public virtual void EnsureBone(UMATransform umaTransform)
		{
			BoneData bd = GetBone(umaTransform.hash);
			if (bd != null)
			{
				bd.accessedFrame = -1;
				bd.umaTransform.Assign(umaTransform);
			}
			else
			{
				AddBone(umaTransform);
			}
		}

		/// <summary>
		/// Ensures all bones are properly initialized and parented.
		/// </summary>
		public virtual void EnsureBoneHierarchy()
		{
			foreach (BoneData entry in boneHashData)
			{
				if (entry.accessedFrame == -1)
				{
					BoneData parent = GetBone(entry.umaTransform.parent);

					if (parent == null)
					{
						Debug.LogError("Bone: \"" + entry + "\" missing parent");
						return;
					}

					entry.boneTransform.parent = parent.boneTransform;
					entry.boneTransform.localPosition = entry.umaTransform.position;
					entry.boneTransform.localRotation = entry.umaTransform.rotation;
					entry.boneTransform.localScale = entry.umaTransform.scale;
					entry.accessedFrame = frame;
				}
			}
		}

		public virtual Quaternion GetTPoseCorrectedRotation(int nameHash, Quaternion tPoseRotation)
		{
			return tPoseRotation;
		}
	}
}
