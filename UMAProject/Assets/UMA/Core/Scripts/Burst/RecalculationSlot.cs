using UMA;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace UMA
{
    public class RecalculationSlot : MonoBehaviour
    {
        public float angle = 60f;
        public string blendShapeNameStartsWith = "";

        public void Recalculate(UMAData umaDATA)
        {
#if UMA_BURSTCOMPILE
            int rendererCount = umaDATA.rendererCount;
            for(int i=0;i<rendererCount;i++)
            {
                var smr = umaDATA.GetRenderer(i);
                if (smr != null)
                {
                    RecalculateMesh(smr);
                }
            }
#endif
        }

#if UMA_BURSTCOMPILE
        private void RecalculateMesh(SkinnedMeshRenderer smr)
        {
            if (smr != null)
            {
                NativeArray<Vector3> vertices = new NativeArray<Vector3>(smr.sharedMesh.vertices, Allocator.TempJob);
                NativeArray<Vector3> normals = new NativeArray<Vector3>(smr.sharedMesh.normals, Allocator.TempJob);
                NativeArray<Vector2> uvs = new NativeArray<Vector2>(smr.sharedMesh.uv, Allocator.TempJob);
                NativeArray<Vector4> tangents = new NativeArray<Vector4>(smr.sharedMesh.tangents, Allocator.TempJob);
                NativeArray<int> triangles = new NativeArray<int>(smr.sharedMesh.triangles, Allocator.TempJob);
                Vector3[] deltaVertices = new Vector3[vertices.Length];
                JobHandle handle = default;

                int blendShapeCount = smr.sharedMesh.blendShapeCount;
                for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
                {
                    float weight = smr.GetBlendShapeWeight(shapeIndex);
                    if (weight > 0f)
                    {
                        weight /= 100f; //bring the weight into 0-1 range.

                        string blendShapeName = smr.sharedMesh.GetBlendShapeName(shapeIndex);
                        if (blendShapeName.StartsWith(blendShapeNameStartsWith))
                        {
                            smr.sharedMesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaVertices, null, null);
                            NativeArray<Vector3> blendShapeDeltaVertices = new NativeArray<Vector3>(deltaVertices, Allocator.TempJob);

                            handle = MeshUtilities.BakeOneFramePositionBlendShape(vertices, blendShapeDeltaVertices, weight, handle);
                        }
                    }
                }

                handle = MeshUtilities.RecalculateNormalsTangentsJobified(vertices, normals, uvs, tangents, triangles, angle, handle);

                handle.Complete();

                //We don't need to do this if we know our mesh is a single instance!
                //Mesh mesh = Instantiate<Mesh>(smr.sharedMesh);
                Mesh mesh = smr.sharedMesh;

                //mesh.SetVertices(vertices); //don't update the vertices if the blendshapes are going to continue...
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTangents(tangents);

                smr.sharedMesh = mesh;

                vertices.Dispose();
                normals.Dispose();
                uvs.Dispose();
                tangents.Dispose();
                triangles.Dispose();
            }
        }
#endif
    }
}