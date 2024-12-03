using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEngine;
using UnityEngine.XR;

namespace UMA
{
    // MeshModifier is a ScriptableObject that contains lists of VertexAdjustments.
    // Note: This is added to recipes *by name*
    public class MeshModifier : ScriptableObject
    {
        [Tooltip("The name of the slot this modifier is applied to.")]
        public string SlotName;
        public List<VertexColorAdjustment> vertexColorAdjustments = new List<VertexColorAdjustment>();
        public List<VertexDeltaAdjustment> vertexDeltaAdjustments = new List<VertexDeltaAdjustment>();
        public List<VertexScaleAdjustment> vertexScaleAdjustments = new List<VertexScaleAdjustment>();
        public List<VertexNormalAdjustment> vertexNormalAdjustments = new List<VertexNormalAdjustment>();
        public List<VertexUVAdjustment> vertexUVAdjustments = new List<VertexUVAdjustment>();
        public List<VertexUserAdjustment> vertexUserAdjustments = new List<VertexUserAdjustment>();

        public void Process(MeshDetails Src)
        {
            MeshDetails Working = Src.ShallowCopy();
            if (vertexDeltaAdjustments.Count > 0)
            {
                VertexColorAdjustment.Apply(Working, vertexColorAdjustments.ToArray());
            }

        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/UMA/Misc/MeshModifier")]
        public static void CreateMeshModifier()
        {
            UMA.CustomAssetUtility.CreateAsset<MeshModifier>();
        }
#endif
    }
}