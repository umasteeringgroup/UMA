using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Utility class for aligning meshes with the same rig but different binds.
	/// </summary>
	public static class SkinnedMeshAligner
	{
	    public static void AlignBindPose(SkinnedMeshRenderer template, SkinnedMeshRenderer data)
	    {
	        var dataBones = data.bones;
	        var templateBones = template.bones;
	        Dictionary<Transform, Transform> boneMap = new Dictionary<Transform, Transform>(dataBones.Length);
	        Dictionary<Transform, int> templateIndex = new Dictionary<Transform, int>(dataBones.Length);
	        Dictionary<int, Matrix4x4> boneTransforms = new Dictionary<int, Matrix4x4>(dataBones.Length);
	        int index = 0;
	        foreach (var boneT in templateBones)
	        {
	            templateIndex.Add(boneT, index++);
	        }

	        var templateMesh = template.sharedMesh;
	        var templateBindPoses = templateMesh.bindposes;
	        var dataMesh = data.sharedMesh;
	        var dataBindPoses = dataMesh.bindposes;
	        var destDataBindPoses = dataMesh.bindposes;
	        int sourceIndex = 0;
	        foreach (var bone in dataBones)
	        {
	            var destIndex = FindBoneIndexInHierarchy(bone, template.rootBone, boneMap, templateIndex);
	            if (destIndex == -1)
	            {
                    if (Debug.isDebugBuild)
                        Debug.Log(bone.name, bone);
	                sourceIndex++;
	                continue;
	            }

	            var dataup = dataBindPoses[sourceIndex].MultiplyVector(Vector3.up);
	            var dataright = dataBindPoses[sourceIndex].MultiplyVector(Vector3.right);

	            var templateup = templateBindPoses[destIndex].MultiplyVector(Vector3.up);
	            var templateright = templateBindPoses[destIndex].MultiplyVector(Vector3.right);
	            if (Mathf.Abs(Vector3.Angle(dataup, templateup)) > 1 || Mathf.Abs(Vector3.Angle(dataright, templateright)) > 1)
	            {
	                // rotation differs significantly
	                Matrix4x4 convertMatrix = templateBindPoses[destIndex].inverse * dataBindPoses[sourceIndex];
	                boneTransforms.Add(sourceIndex, convertMatrix);
	                destDataBindPoses[sourceIndex] = templateBindPoses[destIndex];
	            }
	            sourceIndex++;
	        }
	        dataMesh.bindposes = destDataBindPoses;
	        var dataWeights = dataMesh.boneWeights;
	        var dataVertices = dataMesh.vertices;
            var dataNormals = dataMesh.normals;
            sourceIndex = 0;
	//        Vector3 oldPos = Vector3.zero;
	//        Vector3 oldPosT = Vector3.zero;
	        foreach (var boneweight in dataWeights)
	        {
	            Vector3 oldV = dataVertices[sourceIndex];
	            Vector3 newV = Vector3.zero;
                Vector3 oldN = dataNormals[sourceIndex];
                Vector3 newN = Vector3.zero;
                Matrix4x4 temp;
	            if (boneTransforms.TryGetValue(boneweight.boneIndex0, out temp))
	            {
	                newV += temp.MultiplyPoint(oldV) * boneweight.weight0;
                    newN += temp.MultiplyVector(oldN) * boneweight.weight0;
                }
	            else
	            {
	                newV += oldV * boneweight.weight0;
                    newN += oldN * boneweight.weight0;
                }
	            if (boneTransforms.TryGetValue(boneweight.boneIndex1, out temp))
	            {
	                newV += temp.MultiplyPoint(oldV) * boneweight.weight1;
                    newN += temp.MultiplyVector(oldN) * boneweight.weight1;
                }
	            else
	            {
	                newV += oldV * boneweight.weight1;
                    newN += oldN * boneweight.weight1;
                }
	            if (boneTransforms.TryGetValue(boneweight.boneIndex2, out temp))
	            {
	                newV += temp.MultiplyPoint(oldV) * boneweight.weight2;
                    newN += temp.MultiplyVector(oldN) * boneweight.weight2;
                }
	            else
	            {
	                newV += oldV * boneweight.weight2;
                    newN += oldN * boneweight.weight2;
                }
	            if (boneTransforms.TryGetValue(boneweight.boneIndex3, out temp))
	            {
	                newV += temp.MultiplyPoint(oldV) * boneweight.weight3;
                    newN += temp.MultiplyVector(oldN) * boneweight.weight3;
                }
	            else
	            {
	                newV += oldV * boneweight.weight3;
                    newN += oldN * boneweight.weight3;
                }
	            dataVertices[sourceIndex] = newV;
                dataNormals[sourceIndex] = newN;
                sourceIndex++;
	        }
	        dataMesh.vertices = dataVertices;
            dataMesh.normals = dataNormals;
        }

	    private static int FindBoneIndexInHierarchy(Transform bone, Transform hierarchyRoot, Dictionary<Transform, Transform> boneMap, Dictionary<Transform, int> boneIndexes)
	    {
	        var res = RecursiveFindBoneInHierarchy(bone, hierarchyRoot, boneMap);
	        int idx;
	        if (res != null && boneIndexes.TryGetValue(res, out idx))
	        {
	            return idx;
	        }
	        return -1;
	    }


	    private static Transform RecursiveFindBoneInHierarchy(Transform bone, Transform hierarchyRoot, Dictionary<Transform, Transform> boneMap)
	    {
			if (bone == null)
			{
				return null;
			}

	        Transform res;
	        if (boneMap.TryGetValue(bone, out res))
	        {
	            return res;
	        }
	        if (string.Compare(hierarchyRoot.name, bone.name) == 0)
	        {
	            boneMap.Add(bone, hierarchyRoot);
	            return hierarchyRoot;
	        }
	        else
	        {
				res = null;
				var parent = RecursiveFindBoneInHierarchy(bone.parent, hierarchyRoot, boneMap);
	            if (parent != null)
				{
					res = parent.Find(bone.name);
					if (res != null)
					{
						boneMap.Add(bone, res);
					}
				}

	            return res;
	        }
	    }
	}
}