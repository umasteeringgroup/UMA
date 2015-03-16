using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UMA
{
	[Obsolete("UMASkeletonDefault is obsolete, use UMASkeleton instead.")]
	public class UMASkeletonDefault : UMASkeleton
    {
		[Obsolete("UMASkeletonDefault.BoneData is obsolete, use UMASkeleton.BoneData instead.")]
		public new class BoneData
		{
			public Transform boneTransform;
			public Vector3 originalBoneScale;
			public Vector3 originalBonePosition;
			public Quaternion originalBoneRotation;
		}
		[Obsolete("UMASkeletonDefault is obsolete, use UMASkeleton instead.")]
		public UMASkeletonDefault(Transform rootBone)
			: base(rootBone)
        {
        }
   }
}