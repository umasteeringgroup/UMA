using System.Collections.Generic;
using UMA;
using Unity.Collections;
using UnityEngine;

namespace UMA
{
    public class DecalDefinition
    {
        public string Name;
        public int InitialIndex;
        public GameObject DecalMeshObject;
        public Mesh bakedMesh;
        public Plane[] planesInWorldSpace;
        public int VertexNumber;
        public Vector3 WorldImpactPoint;
        public Vector3 LocalImpactPoint;

        public Material material;
        public float offset;       // zbias or offset for rendering. vertex = vertex + normal * offset.

        List<DecalInstance> Instances = new List<DecalInstance>();

        public GameObject InstantiateSimpleDecal(GameObject umaParent, SkinnedMeshRenderer baseRenderer)
        {
            // instantiate a new one here???

            GameObject newDecal = GameObject.Instantiate(DecalMeshObject, umaParent.transform);
            SkinnedMeshRenderer smr = newDecal.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogWarning("Unable to instantiate decal - no SMR");
                return null;
            }

            // Copy bindposes from main.
            // copy bones from main.
            smr.sharedMesh.bindposes = baseRenderer.sharedMesh.bindposes;
            smr.bones = baseRenderer.bones;

            return newDecal;
        }

        public void AddInstance(UMAData umaData, List<int> Vertexes)
        {

        }

        public void AddSubmesh(SkinnedMeshRenderer smr)
        {
            // we can't reuse the vertexes because the UV coordinates are different...

            if (Instances == null)
            {
                return;
            }

            if (Instances.Count == 0)
            {
                return;
            }

            List<Material> mats = new List<Material>();
            smr.GetMaterials(mats);
            mats.Add(material);

            // read old data
            List<Vector3> Vertexes = new List<Vector3>();
            List<Vector3> Normals = new List<Vector3>();
            List<Vector4> Tangents = new List<Vector4>();
            List<Color32> Colors = new List<Color32>();
            List<Vector2> UV = new List<Vector2>();
            List<Vector2> UV2 = new List<Vector2>();
            List<Vector2> UV3 = new List<Vector2>();
            List<Vector2> UV4 = new List<Vector2>();
            List<int> Tris = new List<int>();
            // TODO: these should be NativeArray. Size to mesh size + all DI sizes.
            List<byte> bonesPerVertex = new List<byte>();
            List<BoneWeight1> boneWeights = new List<BoneWeight1>();

            Mesh mesh = smr.sharedMesh;

            mesh.GetVertices(Vertexes);
            mesh.GetNormals(Normals);
            mesh.GetTangents(Tangents);
            mesh.GetColors(Colors);
            mesh.GetUVs(0, UV);
            mesh.GetUVs(1, UV2);
            mesh.GetUVs(2, UV3);
            mesh.GetUVs(3, UV4);
            bonesPerVertex.AddRange(mesh.GetBonesPerVertex());
            boneWeights.AddRange(mesh.GetAllBoneWeights());

            // boneweights
            // bonespervertex
            // blendshapes <-- later?

            int baseVertex = Vertexes.Count;

            foreach (DecalInstance di in Instances)
            {
                Vertexes.AddRange(di.vertexes);

                // add the triangles
                for (int i = 0; i < di.TriangleList.Length; i++)
                {
                    Tris.Add(di.TriangleList[i] + baseVertex);
                }
                // add the boneweights
                for (int i = 0; i < di.boneWeights.Length; i++)
                {
                    BoneWeight1 bw = di.boneWeights[i];
                    bw.boneIndex += baseVertex;
                    boneWeights.Add(bw);
                }
                bonesPerVertex.AddRange(di.bonesPerVertex);
                // go to next DecalInstance
                baseVertex += di.vertexes.Length;
            }

            mesh.SetVertices(Vertexes);
            mesh.SetNormals(Normals);
            mesh.SetTangents(Tangents);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, UV);
            mesh.SetUVs(1, UV2);
            mesh.SetUVs(2, UV3);
            mesh.SetUVs(3, UV4);

            // Set the submesh
            mesh.subMeshCount++;
            int newSubmesh = mesh.subMeshCount - 1;
            mesh.SetIndices(Tris, MeshTopology.Triangles, newSubmesh);
            // todo: these should not use temp NativeArrays, but always be NativeArrays.
            var unityBonesPerVertex = new NativeArray<byte>(bonesPerVertex.ToArray(), Allocator.Persistent);
            var unityBoneWeights = new NativeArray<BoneWeight1>(boneWeights.ToArray(), Allocator.Persistent);
            mesh.SetBoneWeights(unityBonesPerVertex, unityBoneWeights);
            unityBonesPerVertex.Dispose();
            unityBoneWeights.Dispose();
        }
    }

    public struct faceData
    {
        int oldFaceNumber;
        Vector3 Normal;
    }

    public class DecalInstance
    {
        float offset; // z bias, or add to vertexes?
        public Vector3[] vertexes; // copied from slot(s)
        public Vector3[] normals;  // copied from slot(s)
        public Vector4[] tangents; // copied from slot(s)
        public Color32[] colors32; // copied from slot(s)
        public Vector2[] uv;       // calculated at capture time by projecting to plane
        public int[] TriangleList; // calculated at capture time (each triangle found is translated to local triangles and added to list).
        public byte[] bonesPerVertex;
        public BoneWeight1[] boneWeights;

        /// <summary>
        /// Creates a skinned decal
        /// </summary>
        /// <param name="t">t is the transform of the meshes game object in the scene.</param>
        /// <param name="m">m is the mesh as it is *right now* in the scene, captured at the current frame. 
        /// It's used in place of the meshdata, and is used for everything *except* as the vertex position 
        /// when creating the skinned mesh!!!</param>
        /// <param name="RayOrigin">RayOrigin is the origin of the ray. Used to determine if a face is "facing" the origin of the decal.</param>
        /// <param name="meshData"> This is the UMAMeshData that holds all the data. This is the information 
        /// that is pre-bound to the rig. It's used for constructing the submesh</param>
        /// <param name="planes">planes is the list of planes in world space that define the bounds</param>
        /// <returns></returns>
        public bool Create(Transform t, Mesh m, Vector3 RayOrigin, UMAMeshData meshData, Plane[] planes)
        {
            List<Vector3> newVerts = new List<Vector3>();
            List<Vector3> newNormals = new List<Vector3>();
            List<Vector4> newTangents = new List<Vector4>();
            List<Color32> newColors32 = new List<Color32>();
            List<Vector2> newUv = new List<Vector2>();
            List<int> newTriangleList = new List<int>();
            List<byte> newBonesPerVertex = new List<byte>();
            List<BoneWeight1> newBoneWeights = new List<BoneWeight1>();

            List<int> oldVertexNumbers = new List<int>();
            HashSet<int> vertHash = new HashSet<int>();

            // calculate the face normals for each vertex.
            // int = the source vertex number from the mesh.
            // List<Vector3> = the list of normals for this vertex (one from each face it's connected to).
            //Dictionary<int, List<faceData>> oldVertexnumberFaceNormals = CalculateFaceNormals(m, meshData);

            List<Vector3> meshVerts = new List<Vector3>();
            m.GetVertices(meshVerts);

            for (int i = 0; i < m.vertexCount; i++)
            {
                // this vertex is in world space.  
                Vector3 vert = t.TransformPoint(meshVerts[i]);
                if (OnRight(vert, planes))
                {
                    oldVertexNumbers.Add(i);
                    vertHash.Add(i);
                }
            }

            // now look through every face that has the vertexes in it.
            // if the face is facing the origin, then add it. 
            Plane p = new Plane();
            for (int i = 0; i < m.subMeshCount; i++)
            {
                var smd = m.GetSubMesh(i);

                // UMA only creates triangle lists, so if this fails, then something has changed...
                if (smd.topology == MeshTopology.Triangles)
                {
                    int[] submeshtris = m.GetIndices(i);
                    for (int v = 0; v < smd.indexCount; v += 3)
                    {
                        int i1 = submeshtris[v];
                        int i2 = submeshtris[v + 1];
                        int i3 = submeshtris[v + 2];

                        p.Set3Points(meshVerts[i1], meshVerts[i2], meshVerts[i3]);
                        // Does the triangle face the origin?
                        if (p.GetDistanceToPoint(RayOrigin) >= 0.0f)
                        {
                            if (vertHash.Contains(i1) || vertHash.Contains(i2) || vertHash.Contains(i3))
                            {
                                // Add this triangle.
                                // add meshVerts[i1], meshVerts[i2], meshVerts[i3] to new triangle list.
                                // add translation for i1,i2,i3 to lookup (new, old)
                                // Calculate UV for each one based on distance to UV planes
                            }
                        }
                    }
                }
            }


            return false;
        }

        // Return true if vertex is inside plane group.
        private bool OnRight(Vector3 vert, Plane[] planes)
        {
            foreach (Plane p in planes)
            {
                if (p.GetDistanceToPoint(vert) <= 0.0f)
                {
                    return false;
                }
            }
            return true;
        }





        private Dictionary<int, List<faceData>> CalculateFaceNormals(Mesh m, UMAMeshData meshData)
        {
            Dictionary<int, List<faceData>> retval = new Dictionary<int, List<faceData>>();


            return retval;
        }

        // index of vertexes in the slot. 
        // These need to be translate d to the mesh index after the build is complete.
        // To do this, we will need to track for each slot in the UMAData (during the build process)
        //    What SMR the slot is actually in, in case there are multiples
        //    what vertex position the slot starts at in the SMR
    }
}