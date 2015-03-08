using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
    public abstract class UMASkeleton
    {
		public IEnumerable<int> BoneHashes { get{ return GetBoneHashes(); } }
		protected abstract IEnumerable<int> GetBoneHashes();
		public abstract bool HasBone(int nameHash);
		public abstract void RemoveBone(int nameHash);
		public abstract GameObject GetBoneGameObject(int nameHash);

		public abstract void Set(int nameHash, Vector3 Position, Vector3 scale, Quaternion rotation);
        public abstract void SetPosition(int nameHash, Vector3 Position);
        public abstract void SetScale(int nameHash, Vector3 scale);
        public abstract void SetRotation(int nameHash, Quaternion rotation);
		public abstract void Lerp(int nameHash, Vector3 Position, Vector3 scale, Quaternion rotation, float weight);

        public abstract Vector3 GetPosition(int nameHash);
        public abstract Vector3 GetScale(int nameHash);
        public abstract Quaternion GetRotation(int nameHash);

        public abstract bool Reset(int nameHash);

        public static int StringToHash(string name) { return Animator.StringToHash(name); }
    }
}
