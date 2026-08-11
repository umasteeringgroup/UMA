using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UMA
{
    public class DualQuaternionSkinnerUMA : MonoBehaviour
    {
        [Serializable]
        public struct DualQuaternion
        {
            public Vector4 real;
            public Vector4 dual;

            public DualQuaternion(Quaternion rotation, Quaternion dualPart)
            {
                real = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                dual = new Vector4(dualPart.x, dualPart.y, dualPart.z, dualPart.w);
            }

            public static DualQuaternion FromMatrix(Matrix4x4 matrix)
            {
                Quaternion rotation = matrix.rotation;
                Vector3 translation = matrix.GetColumn(3);
                Quaternion dualPart = new Quaternion(
                    0.5f * (translation.x * rotation.w + translation.y * rotation.z - translation.z * rotation.y),
                    0.5f * (-translation.x * rotation.z + translation.y * rotation.w + translation.z * rotation.x),
                    0.5f * (translation.x * rotation.y - translation.y * rotation.x + translation.z * rotation.w),
                    -0.5f * (translation.x * rotation.x + translation.y * rotation.y + translation.z * rotation.z));
                return new DualQuaternion(rotation, dualPart);
            }
        }

        private struct Int4
        {
            public int x;
            public int y;
            public int z;
            public int w;
        }

        private class RendererSkinData
        {
            public SkinnedMeshRenderer sourceRenderer;
            public Mesh sourceMesh;
            public Mesh outputMesh;
            public MeshFilter outputFilter;
            public MeshRenderer outputRenderer;
            public Transform[] bones;
            public Matrix4x4[] bindposes;
            public DualQuaternion[] boneDqs;
            public ComputeBuffer boneDqBuffer;
            public ComputeBuffer inPositions;
            public ComputeBuffer inNormals;
            public ComputeBuffer indices0;
            public ComputeBuffer indices1;
            public ComputeBuffer weights0;
            public ComputeBuffer weights1;
            public ComputeBuffer outPositions;
            public ComputeBuffer outNormals;
            public Vector3[] outPositionArray;
            public Vector3[] outNormalArray;
            public int vertexCount;
        }

        public ComputeShader SkinShader;
        public bool EnableSkinning = true;
        public bool DisableSourceRenderers = true;
        public bool UpdateEveryFrame = true;

        private UMAData _umaData;
        private readonly List<RendererSkinData> _renderers = new List<RendererSkinData>();
        private int _kernel;
        private uint _threadGroupSizeX;
        private bool _initialized;

        private void OnEnable()
        {
            _umaData = GetComponent<UMAData>();
            if (_umaData != null)
            {
                _umaData.CharacterUpdated.AddListener(OnCharacterUpdated);
            }
            EnsureShader();
            if (_umaData != null)
            {
                OnCharacterUpdated(_umaData);
            }
        }

        private void OnDisable()
        {
            if (_umaData != null)
            {
                _umaData.CharacterUpdated.RemoveListener(OnCharacterUpdated);
            }
            ReleaseAll();
        }

        private void LateUpdate()
        {
            if (!EnableSkinning || !_initialized)
            {
                return;
            }
            if (!UpdateEveryFrame)
            {
                return;
            }
            UpdateSkinning();
        }

        public void EnableDQS()
        {
            EnableSkinning = true;
            UpdateSkinning();
        }

        public void DisableDQS()
        {
            EnableSkinning = false;
            for (int i = 0; i < _renderers.Count; i++)
            {
                var data = _renderers[i];
                if (data != null)
                {
                    if (data.outputRenderer != null)
                    {
                        data.outputRenderer.enabled = false;
                    }
                    if (data.sourceRenderer != null)
                    {
                        data.sourceRenderer.enabled = true;
                    }
                }
            }
        }

        public void BakeToMeshFilter()
        {
            EnsureInitialized();
            UpdateSkinning();
            EnableSkinning = false;
        }

        private void OnCharacterUpdated(UMAData umaData)
        {
            EnsureShader();
            EnsureInitialized();
            UpdateSkinning();
        }

        private void EnsureShader()
        {
            if (SkinShader == null)
            {
#if UNITY_EDITOR
                SkinShader = UMAPathUtility.LoadInstallAsset<ComputeShader>(
                    "InternalDataStore/InGame/Resources/Shader/DQSkin.compute");
#endif
                if (SkinShader == null)
                {
                    SkinShader =
                        Resources.Load<ComputeShader>("Shader/DQSkin");
                }
            }
        }

        private void EnsureInitialized()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                EnableSkinning = false;
                return;
            }
            if (SkinShader == null)
            {
                EnableSkinning = false;
                return;
            }

            _kernel = SkinShader.FindKernel("DQSkin");
            SkinShader.GetKernelThreadGroupSizes(_kernel, out _threadGroupSizeX, out _, out _);

            BuildRenderers();
            _initialized = _renderers.Count > 0;
        }

        private void BuildRenderers()
        {
            ReleaseAll();

            if (_umaData == null)
            {
                _umaData = GetComponent<UMAData>();
            }
            if (_umaData == null)
            {
                return;
            }

            var renderers = _umaData.GetRenderers();
            if (renderers == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                var smr = renderers[i];
                if (smr == null || smr.sharedMesh == null)
                {
                    continue;
                }
                var mesh = smr.sharedMesh;
                if (!mesh.isReadable)
                {
                    continue;
                }

                RendererSkinData data = new RendererSkinData();
                data.sourceRenderer = smr;
                data.sourceMesh = mesh;
                data.vertexCount = mesh.vertexCount;
                data.bindposes = mesh.bindposes;
                data.bones = smr.bones;
                if (data.bindposes == null || data.bones == null || data.bindposes.Length != data.bones.Length)
                {
                    continue;
                }

                BuildBuffers(data);
                BuildOutputRenderer(data);

                _renderers.Add(data);
            }
        }

        private void BuildOutputRenderer(RendererSkinData data)
        {
            var go = new GameObject(data.sourceRenderer.name + "_DQSkin");
            go.transform.SetParent(data.sourceRenderer.transform, false);
            data.outputFilter = go.AddComponent<MeshFilter>();
            data.outputRenderer = go.AddComponent<MeshRenderer>();
            data.outputRenderer.sharedMaterials = data.sourceRenderer.sharedMaterials;

            data.outputMesh = new Mesh();
            data.outputMesh.name = data.sourceMesh.name + "_DQSkin";
            data.outputMesh.indexFormat = data.sourceMesh.indexFormat;
            data.outputMesh.vertices = data.sourceMesh.vertices;
            data.outputMesh.normals = data.sourceMesh.normals;
            data.outputMesh.subMeshCount = data.sourceMesh.subMeshCount;
            for (int s = 0; s < data.sourceMesh.subMeshCount; s++)
            {
                data.outputMesh.SetIndices(data.sourceMesh.GetIndices(s), data.sourceMesh.GetTopology(s), s);
            }
            data.outputMesh.bounds = data.sourceMesh.bounds;
            data.outputMesh.MarkDynamic();
            data.outputFilter.sharedMesh = data.outputMesh;

            if (DisableSourceRenderers)
            {
                data.sourceRenderer.enabled = false;
            }
        }

        private void BuildBuffers(RendererSkinData data)
        {
            Vector3[] positions = data.sourceMesh.vertices;
            Vector3[] normals = data.sourceMesh.normals;
            if (normals == null || normals.Length != positions.Length)
            {
                normals = new Vector3[positions.Length];
                for (int i = 0; i < normals.Length; i++)
                {
                    normals[i] = Vector3.up;
                }
            }

            data.outPositionArray = new Vector3[data.vertexCount];
            data.outNormalArray = new Vector3[data.vertexCount];

            data.inPositions = new ComputeBuffer(data.vertexCount, sizeof(float) * 3);
            data.inNormals = new ComputeBuffer(data.vertexCount, sizeof(float) * 3);
            data.outPositions = new ComputeBuffer(data.vertexCount, sizeof(float) * 3);
            data.outNormals = new ComputeBuffer(data.vertexCount, sizeof(float) * 3);

            data.inPositions.SetData(positions);
            data.inNormals.SetData(normals);

            BuildInfluenceBuffers(data);

            data.boneDqs = new DualQuaternion[data.bones.Length];
            data.boneDqBuffer = new ComputeBuffer(data.bones.Length, sizeof(float) * 8);
        }

        private void BuildInfluenceBuffers(RendererSkinData data)
        {
            var mesh = data.sourceMesh;
            var bonesPerVertex = mesh.GetBonesPerVertex();
            var allWeights = mesh.GetAllBoneWeights();

            Int4[] indices0 = new Int4[data.vertexCount];
            Int4[] indices1 = new Int4[data.vertexCount];
            Vector4[] weights0 = new Vector4[data.vertexCount];
            Vector4[] weights1 = new Vector4[data.vertexCount];

			int weightIndex = 0;
			float[] tempWeights = new float[8];
			int[] tempIndices = new int[8];
			for (int v = 0; v < data.vertexCount; v++)
			{
				int count = bonesPerVertex[v];
				for (int i = 0; i < 8; i++)
				{
					tempWeights[i] = 0f;
					tempIndices[i] = 0;
				}

				for (int i = 0; i < count; i++)
				{
					var bw = allWeights[weightIndex++];
					InsertInfluence(tempWeights, tempIndices, bw.weight, bw.boneIndex);
				}

				indices0[v] = new Int4 { x = tempIndices[0], y = tempIndices[1], z = tempIndices[2], w = tempIndices[3] };
				indices1[v] = new Int4 { x = tempIndices[4], y = tempIndices[5], z = tempIndices[6], w = tempIndices[7] };
				weights0[v] = new Vector4(tempWeights[0], tempWeights[1], tempWeights[2], tempWeights[3]);
				weights1[v] = new Vector4(tempWeights[4], tempWeights[5], tempWeights[6], tempWeights[7]);
			}

            bonesPerVertex.Dispose();
            allWeights.Dispose();

            data.indices0 = new ComputeBuffer(data.vertexCount, sizeof(int) * 4);
            data.indices1 = new ComputeBuffer(data.vertexCount, sizeof(int) * 4);
            data.weights0 = new ComputeBuffer(data.vertexCount, sizeof(float) * 4);
            data.weights1 = new ComputeBuffer(data.vertexCount, sizeof(float) * 4);

            data.indices0.SetData(indices0);
            data.indices1.SetData(indices1);
            data.weights0.SetData(weights0);
            data.weights1.SetData(weights1);
        }

        private void InsertInfluence(float[] weights, int[] indices, float weight, int index)
        {
            if (weight <= 0f)
            {
                return;
            }
            for (int i = 0; i < 8; i++)
            {
                if (weight > weights[i])
                {
                    for (int j = 7; j > i; j--)
                    {
                        weights[j] = weights[j - 1];
                        indices[j] = indices[j - 1];
                    }
                    weights[i] = weight;
                    indices[i] = index;
                    break;
                }
            }
        }

        private void UpdateSkinning()
        {
            if (!EnableSkinning)
            {
                return;
            }

            for (int i = 0; i < _renderers.Count; i++)
            {
                var data = _renderers[i];
                if (data == null || data.sourceRenderer == null)
                {
                    continue;
                }
				if (data.outputRenderer != null)
				{
					data.outputRenderer.enabled = true;
				}
                UpdateBoneDqs(data);

                SkinShader.SetInt("_VertexCount", data.vertexCount);
                SkinShader.SetBuffer(_kernel, "_InPositions", data.inPositions);
                SkinShader.SetBuffer(_kernel, "_InNormals", data.inNormals);
                SkinShader.SetBuffer(_kernel, "_BoneIndices0", data.indices0);
                SkinShader.SetBuffer(_kernel, "_BoneIndices1", data.indices1);
                SkinShader.SetBuffer(_kernel, "_BoneWeights0", data.weights0);
                SkinShader.SetBuffer(_kernel, "_BoneWeights1", data.weights1);
                SkinShader.SetBuffer(_kernel, "_BoneDqs", data.boneDqBuffer);
                SkinShader.SetBuffer(_kernel, "_OutPositions", data.outPositions);
                SkinShader.SetBuffer(_kernel, "_OutNormals", data.outNormals);

                int groups = Mathf.CeilToInt(data.vertexCount / (float)_threadGroupSizeX);
                SkinShader.Dispatch(_kernel, groups, 1, 1);

                data.outPositions.GetData(data.outPositionArray);
                data.outNormals.GetData(data.outNormalArray);

				data.outputMesh.vertices = data.outPositionArray;
				data.outputMesh.normals = data.outNormalArray;
                data.outputMesh.bounds = data.sourceMesh.bounds;
            }
        }

        private void UpdateBoneDqs(RendererSkinData data)
        {
            for (int i = 0; i < data.bones.Length; i++)
            {
                var bone = data.bones[i];
                if (bone == null)
                {
                    data.boneDqs[i] = new DualQuaternion(Quaternion.identity, Quaternion.identity);
                    continue;
                }
                Matrix4x4 m = bone.localToWorldMatrix * data.bindposes[i];
                data.boneDqs[i] = DualQuaternion.FromMatrix(m);
            }
            data.boneDqBuffer.SetData(data.boneDqs);
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                Release(_renderers[i]);
            }
            _renderers.Clear();
        }

        private void Release(RendererSkinData data)
        {
            if (data == null)
            {
                return;
            }
            data.inPositions?.Release();
            data.inNormals?.Release();
            data.indices0?.Release();
            data.indices1?.Release();
            data.weights0?.Release();
            data.weights1?.Release();
            data.outPositions?.Release();
            data.outNormals?.Release();
            data.boneDqBuffer?.Release();
            data.inPositions = null;
            data.inNormals = null;
            data.indices0 = null;
            data.indices1 = null;
            data.weights0 = null;
            data.weights1 = null;
            data.outPositions = null;
            data.outNormals = null;
            data.boneDqBuffer = null;
            if (data.outputFilter != null)
            {
                data.outputFilter.sharedMesh = null;
            }
            if (data.outputMesh != null)
            {
                DestroyImmediate(data.outputMesh);
            }
            if (data.outputRenderer != null)
            {
				if (Application.isPlaying)
				{
					Destroy(data.outputRenderer.gameObject);
				}
				else
				{
					DestroyImmediate(data.outputRenderer.gameObject);
				}
            }
        }
    }
}
