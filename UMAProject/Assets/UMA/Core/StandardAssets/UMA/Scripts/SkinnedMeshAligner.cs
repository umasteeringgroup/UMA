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
            for (int i = 0; i < templateBones.Length; i++)
	        {
                Transform boneT = templateBones[i];
                templateIndex.Add(boneT, index++);
	        }

	        var templateMesh = template.sharedMesh;
	        var templateBindPoses = templateMesh.bindposes;
	        var dataMesh = data.sharedMesh;
	        var dataBindPoses = dataMesh.bindposes;
	        var destDataBindPoses = dataMesh.bindposes;
	        int sourceIndex = 0;
            for (int i = 0; i < dataBones.Length; i++)
	        {
                Transform bone = dataBones[i];
                var destIndex = FindBoneIndexInHierarchy(bone, template.rootBone, boneMap, templateIndex);
	            if (destIndex == -1)
	            {
                    if (Debug.isDebugBuild)
                    {
                        Debug.Log(bone.name, bone);
                    }

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
	        var dataWeights = dataMesh.GetAllBoneWeights();
	        var dataVertices = dataMesh.vertices;
            var dataNormals = dataMesh.normals;
			// Get the number of bone weights per vertex
			var bonesPerVertex = dataMesh.GetBonesPerVertex();

			sourceIndex = 0;
			//        Vector3 oldPos = Vector3.zero;
			//        Vector3 oldPosT = Vector3.zero;
			// Iterate over the vertices
			int bonesIndex = 0;
			for (var vertIndex = 0; vertIndex < dataMesh.vertexCount; vertIndex++)
			{
				Vector3 oldV = dataVertices[sourceIndex];
				Vector3 newV = Vector3.zero;
				Vector3 oldN = dataNormals[sourceIndex];
				Vector3 newN = Vector3.zero;
				var numberOfBonesForThisVertex = bonesPerVertex[vertIndex];

				Matrix4x4 temp;
				// For each vertex, iterate over the array of bones the correct number of times
				for (var i = 0; i < numberOfBonesForThisVertex; i++)
				{
					BoneWeight1 boneweight = dataWeights[bonesIndex];
					if (boneTransforms.TryGetValue(boneweight.boneIndex, out temp))
					{
						newV += temp.MultiplyPoint(oldV) * boneweight.weight;
						newN += temp.MultiplyVector(oldN) * boneweight.weight;
					}
					else
					{
						newV += oldV * boneweight.weight;
						newN += oldN * boneweight.weight;
					}
					bonesIndex++;
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