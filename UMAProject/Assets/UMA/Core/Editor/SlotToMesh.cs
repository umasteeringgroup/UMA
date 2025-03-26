using System.Collections.Generic;
using UMA;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA
{
    public class SlotToMesh : EditorWindow
    {

        [Tooltip("The SlotDataAsset that you want to convert")]
        public SlotDataAsset baseObject;
        [Tooltip("The folder where the Mesh will be created")]
        public UnityEngine.Object slotFolder;


        [MenuItem("UMA/Slot To Mesh", priority = 20)]
        public static void OpenSlotToMeshWindow()
        {
            SlotToMesh window = (SlotToMesh)EditorWindow.GetWindow(typeof(SlotToMesh));
            window.titleContent.text = "UMA Slot To Mesh";
        }


        public string GetFolder(ref UnityEngine.Object folderObject)
        {
            if (folderObject != null)
            {
                string destpath = AssetDatabase.GetAssetPath(folderObject);
                if (string.IsNullOrEmpty(destpath))
                {
                    folderObject = null;
                }
                else if (!System.IO.Directory.Exists(destpath))
                {
                    destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
                }
                return destpath;
            }
            return null;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("UMA Slot To Mesh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This will convert an UMA slot into a Mesh. Once converted, it can be then be saved as an FBX using unity tools", MessageType.None, false);
            baseObject = (SlotDataAsset)EditorGUILayout.ObjectField("Slot Data Asset", baseObject, typeof(SlotDataAsset), true);
            slotFolder = EditorGUILayout.ObjectField("Dest Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

            string folder = GetFolder(ref slotFolder);

            if (slotFolder != null && baseObject != null)
            {
                if (GUILayout.Button("Make Mesh") && slotFolder != null)
                {
                    Mesh mesh = ConvertSlotToMesh(baseObject);

                    string meshName = folder + "/" + baseObject.slotName + "_Mesh.asset";
                    string goName = folder + "/" + baseObject.slotName + "_Go.prefab";
                    // Save Mesh to disk.
                    // smr.sharedMesh.Optimize(); This blows up some versions of Unity.
                    //CustomAssetUtility.SaveAsset<Mesh>(mesh, meshName);
                    AssetDatabase.CreateAsset(mesh, meshName);

                    GameObject go = new GameObject(baseObject.slotName);
                    go.hideFlags = HideFlags.DontSaveInEditor;
                    MeshFilter mf = go.AddComponent<MeshFilter>();
                    mf.mesh = mesh;

                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.materials = new Material[mesh.subMeshCount];
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        mr.materials[i] = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
                    }


                    PrefabUtility.SaveAsPrefabAsset(go, goName);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
                }
            }
            else
            {
                if (baseObject == null)
                {
                    EditorGUILayout.HelpBox("A valid SlotDataAsset must be supplied", MessageType.Error);
                }
                if (slotFolder == null)
                {
                    EditorGUILayout.HelpBox("A valid base folder must be supplied", MessageType.Error);
                }
            }
        }

        public static BoneWeight[] ConvertBoneweight1(BoneWeight1[] weights, byte[] bonesPerVertex)
        {
            List<BoneWeight> bones = new List<BoneWeight>();

            int boneIndex = 0;
            for (int i = 0; i < bonesPerVertex.Length; i++)
            {
                int bonecount = bonesPerVertex[boneIndex];
                BoneWeight bw = new BoneWeight();
                for (int j = 0; j < bonecount; j++)
                {
                    if (j == 0)
                    {
                        bw.boneIndex0 = weights[boneIndex].boneIndex;
                        bw.weight0 = weights[boneIndex].weight;
                    }
                    if (j == 1)
                    {
                        bw.boneIndex1 = weights[boneIndex].boneIndex;
                        bw.weight1 = weights[boneIndex].weight;
                    }
                    if (j == 2)
                    {
                        bw.boneIndex2 = weights[boneIndex].boneIndex;
                        bw.weight2 = weights[boneIndex].weight;
                    }
                    if (j == 3)
                    {
                        bw.boneIndex3 = weights[boneIndex].boneIndex;
                        bw.weight3 = weights[boneIndex].weight;
                    }
                    boneIndex++;
                }
            }
            return bones.ToArray();
        }

        public static BoneWeight[] ConvertBoneweights(UMABoneWeight[] umaBones)
        {
            BoneWeight[] boneWeights = new BoneWeight[umaBones.Length];
            for (int i = 0; i < umaBones.Length; i++)
            {
                boneWeights[i].boneIndex0 = umaBones[i].boneIndex0;
                boneWeights[i].boneIndex1 = umaBones[i].boneIndex1;
                boneWeights[i].boneIndex2 = umaBones[i].boneIndex2;
                boneWeights[i].boneIndex3 = umaBones[i].boneIndex3;
                boneWeights[i].weight0 = umaBones[i].weight0;
                boneWeights[i].weight1 = umaBones[i].weight1;
                boneWeights[i].weight2 = umaBones[i].weight2;
                boneWeights[i].weight3 = umaBones[i].weight3;
            }
            return boneWeights;
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot)
        {
            Mesh mesh = new Mesh() { indexFormat = IndexFormat.UInt32 };
            mesh.vertices = slot.meshData.vertices;
            mesh.uv = slot.meshData.uv;
            mesh.normals = slot.meshData.normals;
            mesh.tangents = slot.meshData.tangents;
            mesh.subMeshCount = slot.meshData.subMeshCount;
            /*
            if (slot.meshData.boneWeights != null && slot.meshData.boneWeights.Length > 0)
            {
                mesh.boneWeights = ConvertBoneweights(slot.meshData.boneWeights);
            }
            else
            {
                mesh.boneWeights = ConvertBoneweight1(slot.meshData.ManagedBoneWeights, slot.meshData.ManagedBonesPerVertex);
                var unityBonesPerVertex = new NativeArray<byte>(slot.meshData.ManagedBonesPerVertex, Allocator.Temp);
                var unityBoneWeights = new NativeArray<BoneWeight1>(slot.meshData.ManagedBoneWeights, Allocator.Temp);
                mesh.SetBoneWeights(unityBonesPerVertex,unityBoneWeights);
            } */

            for (int i = 0; i < slot.meshData.subMeshCount; i++)
            {
                var tris = GetTriangles(slot.meshData, i);
                mesh.subMeshCount = slot.meshData.subMeshCount;
                mesh.SetIndices(tris, MeshTopology.Triangles, i);
            }

            return mesh;
        }

        public static Mesh ConvertSlotToMesh(SlotDataAsset slot, Quaternion Rotation, int VertexHighlight)
        {
            Mesh mesh = new Mesh() { indexFormat = IndexFormat.UInt32 };

            mesh.vertices = slot.meshData.vertices;
            mesh.uv = slot.meshData.uv;
            if (slot.meshData.uv2 != null)
            {
                mesh.uv2 = slot.meshData.uv2;
            }
            if (slot.meshData.uv3 != null)
            {
                mesh.uv3 = slot.meshData.uv3;
            }
            if (slot.meshData.uv4 != null)
            {
                mesh.uv4 = slot.meshData.uv4;
            }
            mesh.normals = slot.meshData.normals;
            mesh.tangents = slot.meshData.tangents;
            mesh.subMeshCount = slot.meshData.subMeshCount;

            Matrix4x4 rot = Matrix4x4.TRS(Vector3.zero, Rotation, Vector3.one);

            for (int i = 0; i < slot.meshData.subMeshCount; i++)
            {
                var tris = GetTriangles(slot.meshData, i);
                mesh.subMeshCount = slot.meshData.subMeshCount;
                mesh.SetIndices(tris, MeshTopology.Triangles, i);

            }


            if (VertexHighlight != -1)
            {
                if (VertexHighlight >= mesh.vertices.Length)
                {
                    VertexHighlight = mesh.vertices.Length - 1;
                }
                Vector3 pos = mesh.vertices[VertexHighlight];
                GameObject throwAway = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Mesh sphereMesh = throwAway.GetComponent<MeshFilter>().sharedMesh;
                Mesh Sphere = Object.Instantiate(sphereMesh);

                Vector3[] vertices = Sphere.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = pos + (vertices[i] * 0.005f);
                }
                Sphere.vertices = vertices;

                Mesh combinedMesh = new Mesh() { indexFormat = IndexFormat.UInt32 };

                CombineInstance[] combine = new CombineInstance[2];
                combine[0].mesh = mesh;
                combine[0].transform = rot;
                combine[1].mesh = Sphere;
                combine[1].transform = rot;
                combinedMesh.CombineMeshes(combine, false, true, false);
                GameObject.DestroyImmediate(throwAway);
                DestroyImmediate(mesh);
                return combinedMesh;
            }
            else
            {
                CombineInstance[] combineInstances = new CombineInstance[1];
                combineInstances[0].mesh = mesh;
                combineInstances[0].transform = rot;
                Mesh combinedMesh = new Mesh() { indexFormat = IndexFormat.UInt32 };

                combinedMesh.CombineMeshes(combineInstances, false, true, false);
                DestroyImmediate(mesh);
                return combinedMesh;
            }
        }



        public static int[] GetTriangles(UMAMeshData meshData, int subMesh)
        {
            return meshData.submeshes[subMesh].getBaseTriangles();
        }
    }
}