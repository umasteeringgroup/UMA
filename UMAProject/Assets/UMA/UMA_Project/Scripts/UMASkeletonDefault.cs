using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
    public class UMASkeletonDefault : UMASkeleton
    {
        Dictionary<int, UMAData.BoneData> boneHashData;

        [Obsolete("UMASkeletonDefault(Dictionary<int, UMAData.BoneData> boneHashData) is obsolete and will be removed in UMA 1.3", false)]
        public UMASkeletonDefault(Dictionary<int, UMAData.BoneData> boneHashData)
        {
            this.boneHashData = boneHashData;
        }

        public UMASkeletonDefault(Transform rootBone)
        {
            this.boneHashData = new Dictionary<int, UMAData.BoneData>();
            AddBonesRecursive(rootBone);
        }

        private void AddBonesRecursive(Transform transform)
        {
            var hash = UMASkeleton.StringToHash(transform.name);
            boneHashData[hash] = new UMAData.BoneData()
            {
                boneTransform = transform,
                originalBonePosition = transform.localPosition,
                originalBoneScale = transform.localScale,
                originalBoneRotation = transform.localRotation
            };
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                AddBonesRecursive(child);
            }
        }

        public override bool HasBone(int nameHash)
        {
            return boneHashData.ContainsKey(nameHash);
        }

        public override void RemoveBone(int nameHash)
        {
            boneHashData.Remove(nameHash);
        }

        public override GameObject GetBoneGameObject(int nameHash)
        {
            UMAData.BoneData res;
            if (boneHashData.TryGetValue(nameHash, out res))
            {
                return res.boneTransform.gameObject;
            }
            return null;
        }

        protected override IEnumerable<int> GetBoneHashes()
        {
            foreach (int hash in boneHashData.Keys)
            {
                yield return hash;
            }
        }

        public override void Set(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
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

        public override void SetPosition(int nameHash, Vector3 position)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.boneTransform.localPosition = position;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetScale(int nameHash, Vector3 scale)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.boneTransform.localScale = scale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetRotation(int nameHash, Quaternion rotation)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.boneTransform.localRotation = rotation;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

		public override void Lerp(int nameHash, Vector3 position, Vector3 scale, Quaternion rotation, float weight)
		{
			UMAData.BoneData db;
			if (boneHashData.TryGetValue(nameHash, out db))
			{
				db.boneTransform.localPosition += position * weight;
				Quaternion fullRotation = db.boneTransform.localRotation * rotation;
				db.boneTransform.localRotation = Quaternion.Slerp(db.boneTransform.localRotation, fullRotation, weight);
				db.boneTransform.localScale = Vector3.Lerp(db.boneTransform.localScale, scale, weight);
			}
		}

        public override bool Reset(int nameHash)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db) && (db.boneTransform != null))
            {
                db.boneTransform.localPosition = db.originalBonePosition;
                db.boneTransform.localRotation = db.originalBoneRotation;
                db.boneTransform.localScale = db.originalBoneScale;

                return true;
            }

            return false;
        }

        public override Vector3 GetPosition(int nameHash)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.boneTransform.localPosition;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override Vector3 GetScale(int nameHash)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.boneTransform.localScale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override Quaternion GetRotation(int nameHash)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.boneTransform.localRotation;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }
    }
}