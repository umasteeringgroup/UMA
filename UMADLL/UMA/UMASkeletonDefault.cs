using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
    public class UMASkeletonDefault : UMASkeleton 
    {
        Dictionary<int, UMAData.BoneData> boneHashData;
        public UMASkeletonDefault(Dictionary<int, UMAData.BoneData> boneHashData)
        {
            this.boneHashData = boneHashData;
        }

		public override bool HasBone(int nameHash)
		{
			return boneHashData.ContainsKey(nameHash);
		}

		protected override IEnumerable<int> GetBoneHashes()
		{
			foreach (int hash in boneHashData.Keys)
			{
				yield return hash;
			}
		}

        public override void Set(int nameHash, Vector3 Position, Vector3 scale, Quaternion rotation)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.boneTransform.localPosition = Position;
                db.boneTransform.localRotation = rotation;
                db.boneTransform.localScale = scale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetPosition(int nameHash, Vector3 Position)
        {
            UMAData.BoneData db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.boneTransform.localPosition = Position;
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
