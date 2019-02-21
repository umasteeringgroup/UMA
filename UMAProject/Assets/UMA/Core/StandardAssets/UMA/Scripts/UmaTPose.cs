using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;

namespace UMA
{
	/// <summary>
	/// Utility class for avatar setup definitions.
	/// </summary>
	[System.Serializable]
	public class UmaTPose : ScriptableObject
	{
		[NonSerialized]
		public SkeletonBone[] boneInfo;
		[NonSerialized]
		public HumanBone[] humanInfo;

		[NonSerialized]
		public float armStretch = 0f;
		[NonSerialized]
		public float feetSpacing = 0f;
		[NonSerialized]
		public float legStretch = 0f;
		[NonSerialized]
		public float lowerArmTwist = 0.2f;
		[NonSerialized]
		public float lowerLegTwist = 1f;
		[NonSerialized]
		public float upperArmTwist = 0.5f;
		[NonSerialized]
		public float upperLegTwist = 0.1f;

		[NonSerialized]
		public bool extendedInfo;

		public struct HumanBoneInfo
		{
			public HumanLimit limit;
			public int boneHash;
		}
		protected HumanBoneInfo[] _humanBoneInfo;
		public HumanBoneInfo[] humanBoneInfo
		{
			get
			{
				if (_humanBoneInfo == null)
				{
					DeSerialize();
				}
				return _humanBoneInfo;
			}
		}

		public byte[] serializedChunk;

		/// <summary>
		/// Serialize into the binary format used for Mecanim avatars.
		/// </summary>
	    public void Serialize()
	    {
	        var ms = new MemoryStream();
	        var bn = new BinaryWriter(ms);
	        bn.Write(boneInfo.Length);
	        foreach(var bi in boneInfo)
	        {
	            Serialize(bn, bi);
	        }
	        bn.Write(humanInfo.Length);
	        foreach (var hi in humanInfo)
	        {
	            Serialize(bn, hi);
	        }
			if (extendedInfo)
			{
				bn.Write(armStretch);
				bn.Write(feetSpacing);
				bn.Write(legStretch);
				bn.Write(lowerArmTwist);
				bn.Write(lowerLegTwist);
				bn.Write(upperArmTwist);
				bn.Write(upperLegTwist);
			}
	        serializedChunk = ms.ToArray();
	    }

		static Dictionary<string, int> humanBoneIndices;

		/// <summary>
		/// Deserialize from the binary format used by Mecanim avatars.
		/// </summary>
		public void DeSerialize()
	    {
			if (humanBoneIndices == null)
			{
				humanBoneIndices = new Dictionary<string, int>();
				for (int i = 0; i < HumanTrait.BoneCount; i++)
				{
					humanBoneIndices.Add(HumanTrait.BoneName[i], i);
				}
			}

			if (boneInfo == null)
			{
				var ms = new MemoryStream(serializedChunk);
				var br = new BinaryReader(ms);
				int count = br.ReadInt32();
				boneInfo = new SkeletonBone[count];
				for (int i = 0; i < count; i++)
				{
					boneInfo[i] = DeSerializeSkeletonBone(br);
				}
				count = br.ReadInt32();
				humanInfo = new HumanBone[count];
				for (int i = 0; i < count; i++)
				{
					humanInfo[i] = DeSerializeHumanBone(br);
				}
				if (br.PeekChar() >= 0)
				{
					extendedInfo = true;
					armStretch = br.ReadSingle();
					feetSpacing = br.ReadSingle();
					legStretch = br.ReadSingle();
					lowerArmTwist = br.ReadSingle();
					lowerLegTwist = br.ReadSingle();
					upperArmTwist = br.ReadSingle();
					upperLegTwist = br.ReadSingle();
				}

				_humanBoneInfo = new HumanBoneInfo[HumanTrait.BoneCount];
				for (int i = 0; i < humanInfo.Length; i++)
				{
					int index;
					if (humanBoneIndices.TryGetValue(humanInfo[i].humanName, out index))
					{
						_humanBoneInfo[index].limit = humanInfo[i].limit;
						_humanBoneInfo[index].boneHash = UMAUtils.StringToHash(humanInfo[i].boneName);
					}
				}
			}
	    }

	    private SkeletonBone DeSerializeSkeletonBone(BinaryReader br)
	    {
	        var res = new SkeletonBone();
	        res.name = br.ReadString();
	        res.position = DeserializeVector3(br);
	        res.rotation = DeSerializeQuaternion(br);
	        res.scale = DeserializeVector3(br);
	        br.ReadInt32();
	        return res;
	    }

	    private Quaternion DeSerializeQuaternion(BinaryReader br)
	    {
	        var res = new Quaternion();
	        res.x = br.ReadSingle();
	        res.y = br.ReadSingle();
	        res.z = br.ReadSingle();
	        res.w = br.ReadSingle();
	        return res;
	    }

	    private HumanBone DeSerializeHumanBone(BinaryReader br)
	    {
	        var res = new HumanBone();
	        res.boneName = br.ReadString();
	        res.humanName = br.ReadString();
	        res.limit = DeSerializeHumanLimit(br);
	        return res;
	    }

	    private HumanLimit DeSerializeHumanLimit(BinaryReader br)
	    {
	        var res = new HumanLimit();
	        res.axisLength = br.ReadSingle();
	        res.center = DeserializeVector3(br);
	        res.max = DeserializeVector3(br);
	        res.min = DeserializeVector3(br);
	        res.useDefaultValues = br.ReadBoolean();
	        return res;
	    }

	    private Vector3 DeserializeVector3(BinaryReader br)
	    {
	        var res = new Vector3();
	        res.x = br.ReadSingle();
	        res.y = br.ReadSingle();
	        res.z = br.ReadSingle();
	        return res;
	    }

	    private void Serialize(BinaryWriter bn, HumanBone value)
	    {
	        bn.Write(value.boneName);
	        bn.Write(value.humanName);
	        Serialize(bn, value.limit);
	    }

	    private void Serialize(BinaryWriter bn, HumanLimit value)
	    {
	        bn.Write(value.axisLength);
	        Serialize(bn,value.center);
	        Serialize(bn,value.max);
	        Serialize(bn,value.min);
	        bn.Write(value.useDefaultValues);
	    }

	    private void Serialize(BinaryWriter bn, SkeletonBone bone)
	    {
	        bn.Write(bone.name);
	        Serialize(bn, bone.position);
	        Serialize(bn, bone.rotation);
	        Serialize(bn, bone.scale);
	        bn.Write((int)1);
	    }

	    private void Serialize(BinaryWriter bn, Quaternion value)
	    {
	        bn.Write(value.x);
	        bn.Write(value.y);
	        bn.Write(value.z);
	        bn.Write(value.w);
	    }

	    private void Serialize(BinaryWriter bn, Vector3 value)
	    {
	        bn.Write(value.x);
	        bn.Write(value.y);
	        bn.Write(value.z);
	    }

		/// <summary>
		/// Reads from Mecanim human description.
		/// </summary>
		/// <param name="description">Human description.</param>
		public void ReadFromHumanDescription(HumanDescription description)
		{
			humanInfo = description.human;
			boneInfo = description.skeleton;
			armStretch = description.armStretch;
			feetSpacing = description.feetSpacing;
			legStretch = description.legStretch;
			lowerArmTwist = description.lowerArmTwist;
			lowerLegTwist = description.lowerLegTwist;
			upperArmTwist = description.upperArmTwist;
			upperLegTwist = description.upperLegTwist;
			extendedInfo = true;

			Serialize();
			boneInfo = null;
			humanInfo = null;
		}

		public HumanDescription CreateHumanDescription()
		{
			DeSerialize();

			HumanDescription hd = new HumanDescription();
			hd.armStretch = armStretch;
			hd.feetSpacing = feetSpacing;
			hd.legStretch = legStretch;
			hd.lowerArmTwist = lowerArmTwist;
			hd.lowerLegTwist = lowerLegTwist;
			hd.upperArmTwist = upperArmTwist;
			hd.upperLegTwist = upperLegTwist;

			hd.human = humanInfo;
			hd.skeleton = boneInfo;

			return hd;
		}

		/// <summary>
		/// Recursively create from animator's transform hierarchy.
		/// </summary>
		/// <param name="rootAnimator">Animator.</param>
		public void ReadFromTransform(Animator rootAnimator)
	    {
	        var boneInfoList = new List<SkeletonBone>();
	        AddRecursively(boneInfoList, rootAnimator.transform);
	        boneInfo = boneInfoList.ToArray();
	        var humanInfoList = new List<HumanBone>();
	        ExtractHumanInfo(rootAnimator, humanInfoList);
	        humanInfo = humanInfoList.ToArray();
	        Serialize();
	    }
		
		private void ExtractHumanInfo(Animator animator, List<HumanBone> humanInfoList)
	    {
	        for (int i = 0; i < HumanTrait.BoneCount; i++)
	        {
	            var boneTransform = animator.GetBoneTransform((HumanBodyBones)i);
	            if (boneTransform != null)
	            {
	                humanInfoList.Add(new HumanBone() { boneName = boneTransform.name, humanName = HumanTrait.BoneName[i], limit = new HumanLimit() { useDefaultValues = true } });
	            }
	        }
	    }

	    private void AddRecursively(List<SkeletonBone> boneInfoList, Transform root)
	    {
	        boneInfoList.Add(new SkeletonBone() { name = root.name, position = root.localPosition, rotation = root.localRotation, scale = root.localScale});
	        for (int i = 0; i < root.childCount; i++)
	        {
	            AddRecursively(boneInfoList, root.GetChild(i));
	        }
	    }
	}
}