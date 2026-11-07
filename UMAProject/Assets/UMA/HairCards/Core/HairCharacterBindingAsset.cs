using System;
using UnityEngine;

namespace UMA.HairCards
{
    /// <summary>Owned skinning donor snapshot. Never replaces the groom's painted source mesh.</summary>
    public sealed class HairCharacterBindingAsset : ScriptableObject
    {
        [Serializable] public sealed class Slot
        {
            public UnityEngine.Object asset;
            public string name, topology;
            public int vertexStart, vertexCount;
            public Material material;
        }
        public UnityEngine.Object race;
        public string sourceTopology, sourceGeometry, rootBoneName = "Global";
        public Mesh donorMesh, weightedSource;
        public string[] boneNames = Array.Empty<string>();
        public int[] boneParents = Array.Empty<int>();
        public Slot[] slots = Array.Empty<Slot>();
        public Matrix4x4 characterToSource = Matrix4x4.identity;
        public int axisPreset;
        public Vector3 alignmentPosition, alignmentRotation;
        public float alignmentScale=1;
        public float maximumDistance = .03f;
        public float maximumMatchedDistance;
        // Per donor vertex -> original authoring surface. Used only for scalp colors.
        public HairSurfaceSample[] scalpSamples = Array.Empty<HairSurfaceSample>();
        public bool Matches(HairGroomAsset groom) => groom != null && donorMesh != null && weightedSource != null &&
            sourceTopology == groom.SourceTopologySignature && weightedSource.vertexCount == groom.SourceVertexCount &&
            boneNames != null && donorMesh.bindposeCount == boneNames.Length && boneNames.Length > 0 &&
            scalpSamples != null && scalpSamples.Length == donorMesh.vertexCount && slots != null && slots.Length == donorMesh.subMeshCount;
        public static string GeometrySignature(Mesh mesh)
        {
            var hash=new Hash128();
            foreach(var p in mesh.vertices) {hash.Append(p.x);hash.Append(p.y);hash.Append(p.z);}
            return hash.ToString();
        }
    }

    [Serializable] public struct HairSurfaceSample
    {
        public int a, b, c;
        public Vector3 weights;
        public float distance;
        public bool valid;
    }
}
