#define USING_BAKEMESH
using System;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;


namespace UMA
{
    public class UMAUVAttachedItem : MonoBehaviour
    {
        // TODO:
        // Should take an array of UV, and save an array of vertex indexes.
        // and a array of prefabs...

        public DynamicCharacterAvatar avatar;
        public Vector2 uVLocation;
        public string slotName;
        public Quaternion rotation;
        public Vector3 translation;
        public GameObject prefab;

        private GameObject prefabInstance;
        public int VertexNumber;
        public int subMeshNumber;
        public SkinnedMeshRenderer skin;
        private Mesh tempMesh;

#if !USING_BAKEMESH
    // Only used when not baking mesh
    private Matrix4x4[] boneMatrices;
    private BoneWeight[] meshBoneWeights;
    private Matrix4x4[] meshBindposes; 
    private Transform[] skinnedBones; 
    private Vector3 UntransformedPosition;
    private Vector3 UntransformedNormal;
#endif
        private bool worldTransform;

        // Start is called before the first frame update
        void Start()
        {
            tempMesh = new Mesh();
            VertexNumber = -1;
            subMeshNumber = -1;
            if (avatar == null)
            {
                avatar = GetComponent<DynamicCharacterAvatar>();
            }

            if (avatar != null)
            {
                avatar.CharacterUpdated.AddListener(UMAUpdated);
            }
        }

        public void UMAUpdated(UMAData umaData)
        {
            // find the slot in the recipe.
            // find the overlay in the recipe
            // calculate the new UV coordinates (in case it changed).
            // Loop through the vertexes, and find the one with the closest UV.
            skin = umaData.GetRenderer(0);
#if !USING_BAKEMESH
        boneMatrices = new Matrix4x4[skin.bones.Length];
#endif
            foreach (var slotData in umaData.umaRecipe.slotDataList)
            {
                if (slotData != null)
                {
                    if (slotData.slotName == slotName)
                    {
                        ProcessSlot(umaData, slotData);
                        break;
                    }
                }
            }
            if (prefabInstance == null)
            {
                prefabInstance = Instantiate(prefab,umaData.gameObject.transform);
                UMAUVAttachedItem umaUV = prefabInstance.GetComponent<UMAUVAttachedItem>();
                if (umaUV)
                {
                    umaUV.VertexNumber = VertexNumber;
                    umaUV.subMeshNumber = subMeshNumber;
                    umaUV.avatar = avatar;

                }
            }
        }

        private void ProcessSlot(UMAData umaData, SlotData slotData)
        {
            Vector2 UVInAtlas = slotData.ConvertToAtlasUV(uVLocation);
            SkinnedMeshRenderer smr = umaData.GetRenderer(slotData.skinnedMeshRenderer);

            Mesh mesh = smr.sharedMesh;
            subMeshNumber = slotData.submeshIndex;
            var smd = mesh.GetSubMesh(subMeshNumber);
            int maxVert = slotData.asset.meshData.vertexCount + slotData.vertexOffset;
#if !USING_BAKEMESH
            meshBoneWeights = mesh.boneWeights;
#endif
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                Mesh.MeshData dat = dataArray[0];
                var allUVS = new NativeArray<Vector2>(mesh.vertexCount, Allocator.Temp);
                var allNormals = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                var allVerts = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp);
                dat.GetUVs(0, allUVS);
                dat.GetNormals(allNormals);
                dat.GetVertices(allVerts);
                VertexNumber = FindVert(slotData, maxVert, UVInAtlas, allUVS);
#if !USING_BAKEMESH
            UntransformedNormal = allNormals[VertexNumber];
            UntransformedPosition = allVerts[VertexNumber];
#endif
            }
        }

        private int FindVert(SlotData slotData, int maxVert, Vector2 UV, NativeArray<Vector2> allUVS)
        {
            int v = slotData.vertexOffset;
            float shortestDistance = Mathf.Abs((allUVS[slotData.vertexOffset] - UV).magnitude);
            for (int i = slotData.vertexOffset + 1; i < maxVert; i++)
            {
                float thisDist = Mathf.Abs((allUVS[i] - UV).magnitude);
                if (thisDist < shortestDistance)
                {
                    v = i;
                    shortestDistance = thisDist;
                }
            }

            return v;
        }

        void LateUpdate()
        {
            if (avatar != null && prefabInstance != null && VertexNumber >= 0 && subMeshNumber >= 0)
            {
                Vector3 position;
                Vector3 normal;

#if USING_BAKEMESH
                skin.BakeMesh(tempMesh);
                Mesh.MeshData data = Mesh.AcquireReadOnlyMeshData(tempMesh)[0];

                var allVerts = new NativeArray<Vector3>(tempMesh.vertexCount, Allocator.TempJob);
                data.GetVertices(allVerts);
                var allNormals = new NativeArray<Vector3>(tempMesh.vertexCount, Allocator.TempJob);
                position = allVerts[VertexNumber];
                normal = allNormals[VertexNumber];
#else
                for (int i = 0; i < boneMatrices.Length; i++)
                {
                    boneMatrices[i] = skinnedBones[i].localToWorldMatrix * meshBindposes[i];
                }

                BoneWeight weight;
                Matrix4x4 bm0;
                Matrix4x4 bm1;
                Matrix4x4 bm2;
                Matrix4x4 bm3;
                Matrix4x4 vm = new Matrix4x4();
                
                weight = meshBoneWeights[VertexNumber];
                bm0 = boneMatrices[weight.boneIndex0];
                bm1 = boneMatrices[weight.boneIndex1];
                bm2 = boneMatrices[weight.boneIndex2];
                bm3 = boneMatrices[weight.boneIndex3];

                vm.m00 = bm0.m00 * weight.weight0 + bm1.m00 * weight.weight1 + bm2.m00 * weight.weight2 + bm3.m00 * weight.weight3;
                vm.m01 = bm0.m01 * weight.weight0 + bm1.m01 * weight.weight1 + bm2.m01 * weight.weight2 + bm3.m01 * weight.weight3;
                vm.m02 = bm0.m02 * weight.weight0 + bm1.m02 * weight.weight1 + bm2.m02 * weight.weight2 + bm3.m02 * weight.weight3;
                vm.m03 = bm0.m03 * weight.weight0 + bm1.m03 * weight.weight1 + bm2.m03 * weight.weight2 + bm3.m03 * weight.weight3;

                vm.m10 = bm0.m10 * weight.weight0 + bm1.m10 * weight.weight1 + bm2.m10 * weight.weight2 + bm3.m10 * weight.weight3;
                vm.m11 = bm0.m11 * weight.weight0 + bm1.m11 * weight.weight1 + bm2.m11 * weight.weight2 + bm3.m11 * weight.weight3;
                vm.m12 = bm0.m12 * weight.weight0 + bm1.m12 * weight.weight1 + bm2.m12 * weight.weight2 + bm3.m12 * weight.weight3;
                vm.m13 = bm0.m13 * weight.weight0 + bm1.m13 * weight.weight1 + bm2.m13 * weight.weight2 + bm3.m13 * weight.weight3;

                vm.m20 = bm0.m20 * weight.weight0 + bm1.m20 * weight.weight1 + bm2.m20 * weight.weight2 + bm3.m20 * weight.weight3;
                vm.m21 = bm0.m21 * weight.weight0 + bm1.m21 * weight.weight1 + bm2.m21 * weight.weight2 + bm3.m21 * weight.weight3;
                vm.m22 = bm0.m22 * weight.weight0 + bm1.m22 * weight.weight1 + bm2.m22 * weight.weight2 + bm3.m22 * weight.weight3;
                vm.m23 = bm0.m23 * weight.weight0 + bm1.m23 * weight.weight1 + bm2.m23 * weight.weight2 + bm3.m23 * weight.weight3;

                position = vm.MultiplyPoint3x4(UntransformedPosition);
                normal = vm.MultiplyVector(UntransformedNormal).normalized;
#endif

                if (worldTransform)
                {
                    position = transform.TransformPoint(position);
                    normal = transform.TransformDirection(normal).normalized;
                    prefabInstance.transform.position = position;
                    prefabInstance.transform.rotation = Quaternion.Euler(normal);
                }
                else
                {
                    prefabInstance.transform.localPosition = position;
                    prefabInstance.transform.localRotation = Quaternion.Euler(normal);
                }
                return;
            }
        }
    }
}

