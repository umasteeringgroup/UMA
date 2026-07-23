using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Gathers and caches mesh information from a SkinnedMeshRenderer or MeshFilter
    /// on this GameObject for display in the editor. No public properties.
    /// </summary>
    [DisallowMultipleComponent]
    public class UMAMeshInformation : MonoBehaviour
    {
#pragma warning disable CS0414 // fields assigned via Unity serialization, read via inspector
        [SerializeField] private string _meshType = "None";
        [SerializeField] private string _meshName = "";
        [SerializeField] private int _vertexCount;
        [SerializeField] private int _boneCount;
        [SerializeField] private int _boneWeightCount;
        [SerializeField] private int _bindPoseCount;
        [SerializeField] private string _indexFormat = "";
        [SerializeField] private int _blendShapeCount;
        [SerializeField] private int _subMeshCount;
        [SerializeField] private int[] _subMeshTriangleCounts = new int[0];
        [SerializeField] private string[] _subMeshMaterialNames = new string[0];

        // Dynamically sized vertex data presence + counts
        [SerializeField] private bool _hasNormals;
        [SerializeField] private int _normalCount;
        [SerializeField] private bool _hasTangents;
        [SerializeField] private int _tangentCount;
        [SerializeField] private bool _hasColors;
        [SerializeField] private int _colorCount;
        [SerializeField] private int _uvChannelCount;
        [SerializeField] private int[] _uvChannelVertexCounts = new int[0];
#pragma warning restore CS0414

        private void Awake()
        {
            GatherMeshInfo();
        }

        private void Reset()
        {
            GatherMeshInfo();
        }

        [ContextMenu("Refresh Mesh Info")]
        public void GatherMeshInfo()
        {
            ClearData();

            // Try SkinnedMeshRenderer first (UMA primary mesh type)
            SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                smr = GetComponentInChildren<SkinnedMeshRenderer>();
            }

            if (smr != null && smr.sharedMesh != null)
            {
                PopulateFromRenderer(smr, smr.sharedMesh);
                return;
            }

            // Fallback to MeshFilter + MeshRenderer
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = GetComponentInChildren<MeshFilter>();
            }

            if (mf != null && mf.sharedMesh != null)
            {
                MeshRenderer mr = GetComponent<MeshRenderer>();
                if (mr == null)
                {
                    mr = GetComponentInChildren<MeshRenderer>();
                }

                PopulateFromMeshFilter(mf, mr, mf.sharedMesh);
                return;
            }

            _meshType = "None";
        }

        private void ClearData()
        {
            _meshType = "None";
            _meshName = "";
            _vertexCount = 0;
            _boneCount = 0;
            _boneWeightCount = 0;
            _bindPoseCount = 0;
            _indexFormat = "";
            _blendShapeCount = 0;
            _subMeshCount = 0;
            _subMeshTriangleCounts = new int[0];
            _subMeshMaterialNames = new string[0];
            _hasNormals = false;
            _normalCount = 0;
            _hasTangents = false;
            _tangentCount = 0;
            _hasColors = false;
            _colorCount = 0;
            _uvChannelCount = 0;
            _uvChannelVertexCounts = new int[0];
        }

        private void PopulateFromRenderer(SkinnedMeshRenderer smr, Mesh mesh)
        {
            _meshType = "SkinnedMeshRenderer";
            _meshName = mesh.name;
            _vertexCount = mesh.vertexCount;
            _boneCount = smr.bones != null ? smr.bones.Length : 0;
            _blendShapeCount = mesh.blendShapeCount;

            // Bone weight data
            var boneWeights = mesh.GetAllBoneWeights();
            _boneWeightCount = boneWeights != null ? boneWeights.Length : 0;

            // Bind poses
            _bindPoseCount = mesh.bindposes != null ? mesh.bindposes.Length : 0;

            // Index format
            _indexFormat = mesh.indexFormat.ToString();

            // Submesh data
            _subMeshCount = mesh.subMeshCount;
            _subMeshTriangleCounts = new int[_subMeshCount];
            _subMeshMaterialNames = new string[_subMeshCount];

            Material[] materials = smr.sharedMaterials;
            for (int i = 0; i < _subMeshCount; i++)
            {
                _subMeshTriangleCounts[i] = (int)mesh.GetIndexCount(i) / 3;
                _subMeshMaterialNames[i] = (materials != null && i < materials.Length && materials[i] != null)
                    ? materials[i].name
                    : "(Missing)";
            }

            // Dynamically sized vertex data
            PopulateVertexDataPresence(mesh);
        }

        private void PopulateFromMeshFilter(MeshFilter mf, MeshRenderer mr, Mesh mesh)
        {
            _meshType = "MeshFilter";
            _meshName = mesh.name;
            _vertexCount = mesh.vertexCount;
            _boneCount = 0;
            _boneWeightCount = 0;
            _blendShapeCount = mesh.blendShapeCount;

            // Submesh data
            _subMeshCount = mesh.subMeshCount;
            _subMeshTriangleCounts = new int[_subMeshCount];
            _subMeshMaterialNames = new string[_subMeshCount];

            Material[] materials = mr != null ? mr.sharedMaterials : null;
            for (int i = 0; i < _subMeshCount; i++)
            {
                _subMeshTriangleCounts[i] = (int)mesh.GetIndexCount(i) / 3;
                _subMeshMaterialNames[i] = (materials != null && i < materials.Length && materials[i] != null)
                    ? materials[i].name
                    : "(Missing)";
            }

            // Bind poses and index format
            _bindPoseCount = mesh.bindposes != null ? mesh.bindposes.Length : 0;
            _indexFormat = mesh.indexFormat.ToString();

            // Dynamically sized vertex data
            PopulateVertexDataPresence(mesh);
        }

        private void PopulateVertexDataPresence(Mesh mesh)
        {
            // Normals
            var normals = mesh.normals;
            _hasNormals = normals != null && normals.Length > 0;
            _normalCount = _hasNormals ? normals.Length : 0;

            // Tangents
            var tangents = mesh.tangents;
            _hasTangents = tangents != null && tangents.Length > 0;
            _tangentCount = _hasTangents ? tangents.Length : 0;

            // Vertex colors
            var colors = mesh.colors;
            _hasColors = colors != null && colors.Length > 0;
            _colorCount = _hasColors ? colors.Length : 0;

            // UV channels
            int populatedChannels = 0;
            int[] uvCounts = new int[8];
            for (int ch = 0; ch < 8; ch++)
            {
                var uvList = new System.Collections.Generic.List<Vector2>();
                mesh.GetUVs(ch, uvList);
                if (uvList.Count > 0)
                {
                    uvCounts[populatedChannels] = uvList.Count;
                    populatedChannels++;
                }
            }
            _uvChannelCount = populatedChannels;
            _uvChannelVertexCounts = new int[populatedChannels];
            for (int i = 0; i < populatedChannels; i++)
            {
                _uvChannelVertexCounts[i] = uvCounts[i];
            }
        }
    }
}
