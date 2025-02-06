using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using System.IO;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
    public class SimpleDecal : MonoBehaviour, IUMAEventHookup
    {
         // These need to be set by the editor.
        // ****************************************************************************
        public int[] boneHashes; // hash of bone names
        public string[] boneNames; // the names of the bones.
        public byte[] bonesPerVertex; // 1 per vertex
        public BoneWeight1[] capturedBoneWeights; // weight / boneIndex
        // ****************************************************************************
        public BoneWeight1[] finalBoneWeights = new BoneWeight1[0]; // weight / boneIndex
        private Dictionary<int, int> NameToBone = new Dictionary<int, int>();
        Vector3[] translated = new Vector3[0];
        public UMAMeshData meshData;
        public Vector3 Offset = Vector3.zero;
        public Vector3 Rotation = Vector3.zero;
        //        public Quaternion Orientation = Quaternion.identity;

        // temp
        private GameObject vmarker;
        private GameObject sceneRoot;
        private Scene editorScene;
        private Vector3 InitialSpot;


        public void Configure(string[] _boneNames, int[] _boneHashes, byte[] _bonesPerVertex, BoneWeight1[] _boneWeights)
        {
            // this
            boneHashes = _boneHashes;
            boneNames = _boneNames;
            capturedBoneWeights = _boneWeights;
            bonesPerVertex = _bonesPerVertex;
        }

#if UNITY_EDITOR
        public void SaveDecal(string Name, string newAssetPath, bool addToLibrary, SkinnedMeshRenderer smr, DecalDefinition decal,GameObject marker1, Scene editScene,GameObject Root)
        {
            if (newAssetPath.StartsWith(Application.dataPath))
            {
                newAssetPath = "Assets"+ newAssetPath.Substring(Application.dataPath.Length);
            }

            string meshPath = Path.Combine(newAssetPath, Name + "_Mesh.asset");
            string prefabPath = Path.Combine(newAssetPath, Name+".prefab");
            string slotPath = Path.Combine(newAssetPath, Name + ".asset");

            /*
            MeshFilter mf = decal.GetComponent<MeshFilter>();
            AssetDatabase.CreateAsset(mf.mesh, meshPath );
            AssetDatabase.SaveAssets();
            */

            InitialSpot = decal.WorldImpactPoint;
            vmarker = marker1;
            editScene = editorScene;
            sceneRoot = Root;

            meshData = new UMAMeshData();
#if USE_TestMesh
            MeshFilter mf = this.gameObject.GetComponent<MeshFilter>();
            meshData.RetrieveDataFromUnityMesh(mf.sharedMesh);

            // local to world (object)
            // world to local (root)
            Matrix4x4 mat = new Matrix4x4();
            Transform root = smr.rootBone;
            if (root == null)
            {
                foreach(Transform t in smr.transform.parent)
                {
                    if (t.name.ToLower() == "root")
                    {
                        root = t;
                        break;
                    }
                }
            }
            if (root == null)
            {
                foreach (Transform t in smr.transform.parent)
                {
                    if (t.gameObject.GetComponent<SkinnedMeshRenderer>() == null)
                    {
                        // Maybe it's this one?
                        if (t.childCount > 0)
                        {
                            root = t;
                            break;
                        }
                    }
                }
            }
            if (root == null)
            {
                mat.SetTRS(Vector3.zero, Quaternion.identity, Vector3.one);
            }
            else
            {
                mat.SetTRS(Vector3.zero, root.localRotation, Vector3.one);
            }
            /*
            for(int i=0;i<meshData.vertices.Length;i++)
            {
                meshData.vertices[i] = mat.inverse * meshData.vertices[i];
            }
            */

            meshData.UpdateBones(smr.rootBone, smr.bones);
            meshData.ManagedBoneWeights = this.finalBoneWeights;    // Not right
            meshData.ManagedBonesPerVertex = this.bonesPerVertex; 
            meshData.bindPoses = smr.sharedMesh.bindposes;
            meshData.SlotName = "Decal";
            meshData.clothSkinningSerialized = new Vector2[0];
#else
            // get all the triangles inside the radius that face the ray.            

            meshData.subMeshCount = 1;
            meshData.submeshes = new SubMeshTriangles[1];
            meshData.submeshes[0].SetTriangles(AccumulateTriangles(decal.bakedMesh,decal.planesInWorldSpace,smr,decal.WorldImpactPoint));
            meshData.vertices = smr.sharedMesh.vertices;
            meshData.normals = smr.sharedMesh.normals;
            meshData.uv = new Vector2[smr.sharedMesh.vertices.Length];
            meshData.uv2 = smr.sharedMesh.uv2;
            meshData.uv3 = smr.sharedMesh.uv3;
            meshData.uv4 = smr.sharedMesh.uv4;
            meshData.colors32 = smr.sharedMesh.colors32;
            meshData.vertexCount = meshData.vertices.Length;
            meshData.tangents = smr.sharedMesh.tangents;

            meshData.bindPoses = smr.sharedMesh.bindposes;
            meshData.ManagedBoneWeights = smr.sharedMesh.GetAllBoneWeights().ToArray();
            meshData.ManagedBonesPerVertex = smr.sharedMesh.GetBonesPerVertex().ToArray();
            meshData.SlotName = "Decal";
            meshData.clothSkinningSerialized = new Vector2[0];

            ProjectUV(meshData, decal.bakedMesh, decal.planesInWorldSpace,smr);

#endif
            gameObject.name = Name;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(this.gameObject, prefabPath);
            SlotDataAsset sda = CustomAssetUtility.CreateAsset<SlotDataAsset>(slotPath, false, Name);
            sda.slotName = Name;
            sda.SlotObject = prefab;

            /*
            mf = prefab.GetComponent<MeshFilter>();
            mf.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            SkinnedMeshRenderer smr = prefab.GetComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mf.mesh;
            */


            EditorUtility.SetDirty(sda);

            AssetDatabase.SaveAssets();
            if (addToLibrary)
            {
                UMAAssetIndexer.Instance.AddAsset(typeof(SlotDataAsset),Name,  slotPath, sda);
                EditorUtility.DisplayDialog("UMA", "Decal Created and added to library", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("UMA", "Decal Created. Don't forget to add it to the library", "OK");
            }
        }

        private Vector2 GetUV(Plane[] planesinWS,Vector3 vertinWS)
        {
            Plane u0 = planesinWS[0];
            Plane u1 = planesinWS[1];
            Plane v0 = planesinWS[2];
            Plane v1 = planesinWS[3];

            float ud0 = u0.GetDistanceToPoint(vertinWS);
            float ud1 = u1.GetDistanceToPoint(vertinWS);
            float vd0 = v0.GetDistanceToPoint(vertinWS);
            float vd1 = v1.GetDistanceToPoint(vertinWS);

            float U_One = Mathf.Abs((u0.ClosestPointOnPlane(vertinWS) - u1.ClosestPointOnPlane(vertinWS)).magnitude);
            float V_One = Mathf.Abs((v0.ClosestPointOnPlane(vertinWS) - v1.ClosestPointOnPlane(vertinWS)).magnitude);

            return new Vector2(ud0 / U_One, vd0 / V_One);
        }

        /// <summary>
        ///  planes needs to be in worldspace!!!!!
        /// </summary>
        /// <param name="meshData"></param>
        /// <param name="bakedMesh"></param>
        /// <param name="planes"></param>
        /// <param name="smr"></param>
        private void ProjectUV(UMAMeshData meshData, Mesh bakedMesh, Plane[] planes, SkinnedMeshRenderer smr)
        {
            Matrix4x4 mat = smr.gameObject.transform.localToWorldMatrix;
            NativeArray<int> tris = meshData.submeshes[0].GetTriangles();

            for (int tri = 0; tri < tris.Length; tri+=3)
            {
                int v0index = tris[tri];
                int v1index = tris[tri+1];
                int v2index = tris[tri+2];

                Vector3 v0 = mat * meshData.vertices[v0index];
                Vector3 v1 = mat * meshData.vertices[v1index];
                Vector3 v2 = mat * meshData.vertices[v2index];

                Vector2 uv0 = GetUV(planes, v0);
                Vector2 uv1 = GetUV(planes, v1);
                Vector2 uv2 = GetUV(planes, v2);

                meshData.uv[v0index] = uv0;
                meshData.uv[v1index] = uv1;
                meshData.uv[v2index] = uv2;
            }
        }


        private bool PlanesContains(Plane[] planes,Vector3 vert)
        {
            foreach(Plane p in planes)
            {
                float dist = p.GetDistanceToPoint(vert);
                if (dist < 0.0f)
                {
                    return false;
                }
            }
            return true;
        }

        private int[] AccumulateTriangles(Mesh bakedMesh, Plane[] planes, SkinnedMeshRenderer smr,Vector3 worldPoint)
        {
            //List<int> insideVertexes = new List<int>();
            HashSet<int> insideVertexes = new HashSet<int>();
            List<int> newTriangles = new List<int>();
           /* Vector3 VertMax = new Vector3();

            for(int i=0;i<bakedMesh.vertices.Length;i++)
            {

                Vector3 vert = smr.gameObject.transform.TransformPoint(bakedMesh.vertices[i]);
               
                if (vert.y > VertMax.y)
                {
                    VertMax = vert;
                }

                if (PlanesContains(planes,vert))
                {
                    insideVertexes.Add(i);
                }                
            }
            GameObject.Instantiate(vmarker, VertMax, Quaternion.identity,sceneRoot.transform); */

            for (int i=0;i<bakedMesh.subMeshCount;i++)
            {
                int[] triangles = bakedMesh.GetTriangles(i);
                for (int tri = 0; tri < triangles.Length; tri += 3)
                {
                    bool isAffected = false;



                    if (TriangleIntersects(tri, triangles,  bakedMesh, smr, planes, worldPoint))
                    {
                        isAffected = true;
                    }

                    //if (insideVertexes.Contains(triangles[tri])) isAffected = true;
                    //if (insideVertexes.Contains(triangles[tri+1])) isAffected = true;
                    //if (insideVertexes.Contains(triangles[tri+2])) isAffected = true;

                    // if any indexes are inside the space,
                    // then we need that triangle.
                    if (isAffected)
                    {
                        newTriangles.Add(triangles[tri]);
                        newTriangles.Add(triangles[tri+1]);
                        newTriangles.Add(triangles[tri+2]);
                    }
                }
            }
            return newTriangles.ToArray();
        }

        private bool TriangleIntersects(int tri, int[] triangles, Mesh bakedMesh, SkinnedMeshRenderer smr,Plane[] planes, Vector3 worldPoint)
        {
            // if all vertexes are on outside U1 or U2
            // if all vertexes are on outside V1 or V2
            // return false
            //
            // else return true. 

            Transform transform = smr.gameObject.transform;

            int backu1count = 0;
            int backu2count = 0;
            int backv1count = 0;
            int backv2count = 0;
            int backf1count = 0;
            int backb1count = 0;

            Plane u1 = planes[0];
            Plane u2 = planes[1];
            Plane v1 = planes[2];
            Plane v2 = planes[3];
            Plane f1 = planes[4];
            Plane b1 = planes[5];



            for (int i=0;i<3;i++)
            {
                int vertexnum = triangles[tri + i];
                Vector3 worldvert = transform.TransformPoint(bakedMesh.vertices[vertexnum]);
                float u1dist = u1.GetDistanceToPoint(worldvert);
                float u2dist = u2.GetDistanceToPoint(worldvert);
                float v1dist = v1.GetDistanceToPoint(worldvert);
                float v2dist = v2.GetDistanceToPoint(worldvert);
                float f1dist = f1.GetDistanceToPoint(worldvert);
                float b1dist = b1.GetDistanceToPoint(worldvert);


                if (u1dist < 0)
                {
                    backu1count++;
                }

                if (u2dist < 0)
                {
                    backu2count++;
                }

                if (v1dist < 0)
                {
                    backv1count++;
                }

                if (v2dist < 0)
                {
                    backv2count++;
                }

                if (f1dist < 0)
                {
                    backf1count++;
                }

                if (b1dist < 0)
                {
                    backb1count++;
                }
            }



            // Calculate plane from verts. 
            // if ALL vertexes are "too far", then do not include

            if (backu1count == 3 || backu2count == 3)
            {
                return false;
            }

            if (backv1count == 3 || backv2count == 3)
            {
                return false;
            }

            if (backb1count == 3 || backf1count == 3)
            {
                return false;
            }

            // Get the triangle in world space
            Vector3 t1 = transform.TransformPoint(bakedMesh.vertices[triangles[tri]]);
            Vector3 t2 = transform.TransformPoint(bakedMesh.vertices[triangles[tri+1]]);
            Vector3 t3 = transform.TransformPoint(bakedMesh.vertices[triangles[tri+2]]);

            // if it faces away, don't make a decal
            Plane TriWorldPlane = new Plane(t1, t2, t3);
            if (TriWorldPlane.GetDistanceToPoint(worldPoint) < 0.0f)
            {
                return false;
            }

            // in the volume.
            // faces the indicator.
            return true;
        }
#endif

        #region runtime
        public void UpdateBones(UMAData umaData)
        {
            Dictionary<int, int> HashToPosition = new Dictionary<int, int>();

            SkinnedMeshRenderer renderer = umaData.GetRenderer(0);

            // No new bones added
            if (boneHashes.Length == renderer.bones.Length)
            {
                finalBoneWeights = new BoneWeight1[0];
                return;
            }

            // build translation table if needed
            //  if (NameToBone.Count != renderer.bones.Length)
            //  {
            NameToBone.Clear();
                for (int i = 0; i < renderer.bones.Length; i++)
                {
                    Transform t = renderer.bones[i];
                    NameToBone.Add(UMAUtils.StringToHash(t.name), i);
                }
           // }

            // new bones added... need to translate
            finalBoneWeights = new BoneWeight1[capturedBoneWeights.Length];
            for (int i = 0; i < capturedBoneWeights.Length; i++)
            {
                BoneWeight1 oldbw = capturedBoneWeights[i];
                int Hash = capturedBoneWeights[i].boneIndex;

                if (umaData.skeleton.boneHashData.ContainsKey(Hash))
                {
                    var boneData = umaData.skeleton.boneHashData[Hash];

                    if (NameToBone.ContainsKey(boneData.boneNameHash))
                    {
                        finalBoneWeights[i].boneIndex = NameToBone[boneData.boneNameHash];
                        finalBoneWeights[i].weight = capturedBoneWeights[i].weight;
                        //Debug.Log($"UMA Bone Found  with hash {boneData.boneNameHash} name {boneData.boneTransform.name}");
                    }
                    else
                    {
                        Debug.LogError($"UMA Bone not found with hash {boneData.boneNameHash} name {boneData.boneTransform.name}");
                    }
                }
                else
                {
                    Debug.LogError($"Decal bone not found with hash {Hash}");
                } 

                /*
                 int Hash = boneHashes[boneWeights[i].boneIndex];
                int Hash = capturedBoneWeights[i].boneIndex;
                if (umaData.skeleton.boneHashData.ContainsKey(Hash))
                {
                    var boneData = umaData.skeleton.boneHashData[Hash];

                    if (NameToBone.ContainsKey(boneData.boneNameHash))
                    {
                        finalBoneWeights[i].boneIndex = NameToBone[boneData.boneNameHash];
                        finalBoneWeights[i].weight = capturedBoneWeights[i].weight;
                        Debug.Log($"UMA Bone Found  with hash {boneData.boneNameHash} name {boneData.boneTransform.name}");
                    }
                    else
                    {
                        Debug.LogError($"UMA Bone not found with hash {boneData.boneNameHash} name {boneData.boneTransform.name}");
                    }
                }
                else
                {
                    Debug.LogError($"Decal bone not found with hash {Hash}");
                } */

            }
        }

        public bool invert;
        public bool root;
        public bool global;
        public bool position;

        Matrix4x4 GetBoneTransform(UMAData umaData)
        {
            Matrix4x4 mat = Matrix4x4.identity;
            Quaternion rot = Quaternion.Euler(Rotation);
            mat.SetTRS(Offset, rot, Vector3.one);
            if (position)
            {
                Transform pos = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash("Position"));
                if (pos != null)
                {
                    Matrix4x4 posMat = new Matrix4x4();
                    posMat.SetTRS(pos.localPosition, Quaternion.identity /*pos.localRotation*/, Vector3.one);
                    if (invert)
                    {
                        posMat = posMat.inverse;
                    }

                    mat = mat * posMat;
                }
                else
                {
                    Debug.Log("Position bone not found?");
                }
            }
            if (global)
            {
                Transform global = umaData.skeleton.GetBoneTransform(UMAUtils.StringToHash("Global"));
                if (global != null)
                {
                    Matrix4x4 globalMat = new Matrix4x4();
                    globalMat.SetTRS(global.localPosition, Quaternion.identity/*global.localRotation*/, Vector3.one);
                    if (invert)
                    {
                        globalMat = globalMat.inverse;
                    }

                    mat = mat * globalMat;
                }
                else
                {
                    Debug.Log("Global bone not found?");
                }
            }

            if (root)
            {
                Transform root = umaData.umaRoot.transform;
                if (root != null)
                {
                    Matrix4x4 rootmat = new Matrix4x4();
                    rootmat.SetTRS(Vector3.zero, root.localRotation, Vector3.one);
                    if (invert)
                    {
                        rootmat = rootmat.inverse;
                    }

                    mat = mat * rootmat;
                }
                else
                {
                    Debug.Log("Root bone not found?");
                }
            }


            return mat;
        }


        Vector3[] TranslateVertices(Vector3[] verts, UMAData umaData)
        {
            Vector3[] tverts = new Vector3[verts.Length];
            Matrix4x4 mat = GetBoneTransform(umaData);

            for(int i=0;i<verts.Length;i++)
            {
                tverts[i] = mat * (verts[i]+Offset);
            }

            return tverts;
        }

        private void ApplyToMesh(Mesh mesh,UMAData umaData)
        {
            mesh.subMeshCount = 1;
            mesh.triangles = new int[0];
            mesh.vertices = TranslateVertices(meshData.GetVertices(),umaData);
            mesh.normals = meshData.normals;
            mesh.tangents = meshData.tangents;
            mesh.uv = meshData.uv;
            mesh.uv2 = meshData.uv2;
            mesh.uv3 = meshData.uv3;
            mesh.uv4 = meshData.uv4;
            mesh.colors32 = meshData.colors32;
            mesh.bindposes = meshData.bindPoses;

            var subMeshCount = meshData.submeshes.Length;
            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetIndices(meshData.submeshes[i].GetTriangles(), MeshTopology.Triangles, i);
            }
            mesh.RecalculateBounds();
        }


        public GameObject ApplyTo(UMAData umaData, SkinnedMeshRenderer baseRenderer,GameObject slotObject)
        {
            // instantiate a new one here???
            GameObject newDecal = GameObject.Instantiate(slotObject, umaData.transform);
            SkinnedMeshRenderer smr = newDecal.GetComponent<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogWarning("Unable to instantiate decal - no SMR");
                return null;
            }

            Mesh m = new Mesh();
            ApplyToMesh(m,umaData);

            // Copy bindposes from main.
            // copy bones from main.
            smr.sharedMesh = m;
            smr.sharedMesh.bindposes = baseRenderer.sharedMesh.bindposes; 
            smr.bones = baseRenderer.bones;
            smr.sharedMesh = m;
            smr.rootBone = baseRenderer.rootBone;
            
            /* OLD BANDAGE WAY
            NativeArray<byte> bpv = new NativeArray<byte>(bonesPerVertex, Allocator.Temp);
            NativeArray<BoneWeight1> bweights;
            bweights = new NativeArray<BoneWeight1>(finalBoneWeights, Allocator.Temp);
            
            smr.sharedMesh.SetBoneWeights(bpv, bweights); */
            smr.gameObject.SetActive(false);
            smr.gameObject.SetActive(true);
            
            return newDecal;
        }

        public void Begun(UMAData umaData)
        {
        }

        public void Completed(UMAData umaData,GameObject slotObject)
        {
            UpdateBones(umaData);
            ApplyTo(umaData, umaData.GetRenderer(0), slotObject);
        }

        public void HookupEvents(SlotDataAsset slot)
        {

        }
        #endregion
    }
}