using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
	public class UMASkeleton
	{
		public class BoneData
		{
			public int boneNameHash;
			public int parentBoneNameHash;
			public Transform boneTransform;
			public UMATransform umaTransform;
			public int accessedFrame;
		}

		public IEnumerable<int> BoneHashes { get { return GetBoneHashes(); } }

		int frame;
		public int rootBoneHash { get; private set; }

		Dictionary<int, BoneData> boneHashData;

		public UMASkeleton(Transform rootBone)
		{
			rootBoneHash = UMASkeleton.StringToHash(rootBone.name);
			this.boneHashData = new Dictionary<int, BoneData>();
			AddBonesRecursive(rootBone);
		}

		public virtual void BeginSkeletonUpdate()
		{
			frame++;
			if (frame < 0) frame = 0;
		}

		public virtual void EndSkeletonUpdate()
		{
		}

		private void AddBonesRecursive(Transform transform)
		{
			var hash = UMASkeleton.StringToHash(transform.name);
			var parentHash = transform.parent != null ? UMASkeleton.StringToHash(transform.parent.name) : 0;
			boneHashData[hash] = new BoneData()
			{
				parentBoneNameHash = parentHash,
				boneNameHash = hash,
				accessedFrame = frame,
				boneTransform = transform,
				umaTransform = new UMATransform(transform, hash, parentHash)
			};
			for (int i = 0; i < transform.childCount; i++)
			{
				var child = transform.GetChild(i);
				AddBonesRecursive(child);
			}
		}

		public virtual bool HasBone(int nameHash)
		{
			return boneHashData.ContainsKey(nameHash);
		}

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

			boneHashData.Add(hash, newBone);
		}

		public virtual void AddBone(UMATransform transform)
		{
			var go = new GameObject(transform.name);
			BoneData newBone = new BoneData()
			{
				accessedFrame = -1,
				parentBoneNameHash = transform.parent,
				boneNameHash = transform.hash,
				boneTransform = go.transform,
				umaTransform = transform,
			};

			boneHashData.Add(transform.hash, newBone);
		}

		public virtual void RemoveBone(int nameHash)
		{
			boneHashData.Remove(nameHash);
		}

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

		public virtual GameObject GetBoneGameObject(int nameHash)
		{
			BoneData res;
			if (boneHashData.TryGetValue(nameHash, out res))
			{
				res.accessedFrame = frame;
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

		public virtual void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = position;
				db.boneTransform.localRotation = rotation;
				db.boneTransform.localScale = scale;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		public virtual void SetPosition(int nameHash, Vector3 position)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition = position;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		public virtual void SetScale(int nameHash, Vector3 scale)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localScale = scale;
			}
			else
			{
				Debug.LogWarning("NameHash not found: "+nameHash);
				Debug.LogWarning("Skeleton contains: " + boneHashData.Count + " bones");
				throw new Exception("Bone not found.");
			}
		}

		public virtual void SetRotation(int nameHash, Quaternion rotation)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localRotation = rotation;
			}
			else
			{
				throw new Exception("Bone not found.");
			}
		}

		public virtual void Lerp(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.accessedFrame = frame;
				db.boneTransform.localPosition += position * weight;
				Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
			}
		}

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

		public virtual void ResetAll()
		{
			foreach (int hash in boneHashData.Keys)
			{
				Reset(hash);
			}
		}

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
				throw new Exception("Bone not found.");
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

		public virtual void EnsureBone(UMATransform umaTransform)
		{
			if (!boneHashData.ContainsKey(umaTransform.hash))
			{
				AddBone(umaTransform);
			}
		}

		public virtual void EnsureBoneHierarchy()
		{
			foreach (var entry in boneHashData.Values)
			{
				if (entry.accessedFrame == -1)
				{
					entry.boneTransform.parent = boneHashData[entry.umaTransform.parent].boneTransform;
					entry.boneTransform.localPosition = entry.umaTransform.position;
					entry.boneTransform.localRotation = entry.umaTransform.rotation;
					entry.boneTransform.localScale = entry.umaTransform.scale;
				}
			}
		}
	}
}
