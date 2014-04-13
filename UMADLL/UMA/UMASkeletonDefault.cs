using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
    public class UMASkeletonDefault : UMASkeleton 
    {
        Dictionary<int, Transform> boneHashData;
        [Obsolete("UMASkeletonDefault(Dictionary<int, UMAData.BoneData> boneHashData) is obsolete and will be removed in UMA 1.3", false)]
        public UMASkeletonDefault(Dictionary<int, UMAData.BoneData> boneHashData)
        {
            this.boneHashData = new Dictionary<int, Transform>();
            foreach (var entry in boneHashData)
            {
                this.boneHashData.Add(entry.Key, entry.Value.boneTransform);
            }
        }

        public UMASkeletonDefault(Transform rootBone)
        {
            this.boneHashData = new Dictionary<int, Transform>();
            AddBonesRecursive(rootBone);
        }

        private void AddBonesRecursive(Transform transform)
        {
            var hash = UMASkeleton.StringToHash(transform.name);
            boneHashData[hash] = transform;
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
            Transform res;
            if (boneHashData.TryGetValue(nameHash, out res))
            {
                return res.gameObject;
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

        public override void Set(int nameHash, Vector3 Position, Vector3 scale, Quaternion rotation)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.localPosition = Position;
                db.localRotation = rotation;
                db.localScale = scale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetPosition(int nameHash, Vector3 Position)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.localPosition = Position;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetScale(int nameHash, Vector3 scale)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.localScale = scale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override void SetRotation(int nameHash, Quaternion rotation)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                db.localRotation = rotation;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

		public override Vector3 GetPosition(int nameHash)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.localPosition;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override Vector3 GetScale(int nameHash)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.localScale;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }

        public override Quaternion GetRotation(int nameHash)
        {
            Transform db;
            if (boneHashData.TryGetValue(nameHash, out db))
            {
                return db.localRotation;
            }
            else
            {
                throw new Exception("Bone not found.");
            }
        }
    }
}
