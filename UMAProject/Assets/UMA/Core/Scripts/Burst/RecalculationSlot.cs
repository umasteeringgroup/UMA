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
            int rendererCount = umaDATA.RendererCount;
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
            Mesh mesh = smr != null ? smr.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0 || !mesh.isReadable)
            {
                return;
            }

            // A reused mesh is owned by the generated-resource cache and may be
            // bound to other avatars with different blendshape weights. Never
            // write per-renderer results into it.
            if (UMAResourceLeaseOwner.IsSharedMesh(smr))
            {
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"RecalculationSlot: skipped '{smr.name}' because its mesh is shared by the generated-resource cache.", smr);
                }
                return;
            }

            NativeArray<Vector3> vertices = default;
            NativeArray<Vector3> normals = default;
            NativeArray<Vector2> uvs = default;
            NativeArray<Vector4> tangents = default;
            NativeArray<int> triangles = default;
            JobHandle handle = default;
            try
            {
                vertices = new NativeArray<Vector3>(mesh.vertices, Allocator.TempJob);
                normals = new NativeArray<Vector3>(mesh.normals, Allocator.TempJob);
                uvs = new NativeArray<Vector2>(mesh.uv, Allocator.TempJob);
                tangents = new NativeArray<Vector4>(mesh.tangents, Allocator.TempJob);
                triangles = new NativeArray<int>(mesh.triangles, Allocator.TempJob);
                Vector3[] deltaVertices = new Vector3[vertices.Length];

                int blendShapeCount = mesh.blendShapeCount;
                for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
                {
                    float weight = smr.GetBlendShapeWeight(shapeIndex);
                    if (weight > 0f)
                    {
                        weight /= 100f; //bring the weight into 0-1 range.

                        string blendShapeName = mesh.GetBlendShapeName(shapeIndex);
                        if (blendShapeName.StartsWith(blendShapeNameStartsWith))
                        {
                            mesh.GetBlendShapeFrameVertices(shapeIndex, 0, deltaVertices, null, null);
                            // Deallocated by the job on completion.
                            NativeArray<Vector3> blendShapeDeltaVertices = new NativeArray<Vector3>(deltaVertices, Allocator.TempJob);
                            try
                            {
                                handle = MeshUtilities.BakeOneFramePositionBlendShape(vertices, blendShapeDeltaVertices, weight, handle);
                            }
                            catch
                            {
                                blendShapeDeltaVertices.Dispose();
                                throw;
                            }
                        }
                    }
                }

                handle = MeshUtilities.RecalculateNormalsTangentsJobified(vertices, normals, uvs, tangents, triangles, angle, handle);
                handle.Complete();

                //mesh.SetVertices(vertices); //don't update the vertices if the blendshapes are going to continue...
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTangents(tangents);
            }
            finally
            {
                handle.Complete();
                if (vertices.IsCreated) vertices.Dispose();
                if (normals.IsCreated) normals.Dispose();
                if (uvs.IsCreated) uvs.Dispose();
                if (tangents.IsCreated) tangents.Dispose();
                if (triangles.IsCreated) triangles.Dispose();
            }
        }
#endif
    }
}