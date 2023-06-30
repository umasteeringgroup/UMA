using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UMA;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.WSA;

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

    public static Mesh ConvertSlotToMesh(SlotDataAsset slot)
    {
        Mesh mesh = new Mesh();
        mesh.vertices = slot.meshData.vertices;
        mesh.uv = slot.meshData.uv;
        mesh.normals = slot.meshData.normals;
        mesh.tangents = slot.meshData.tangents;
        mesh.subMeshCount = slot.meshData.subMeshCount;
        for (int i = 0; i < slot.meshData.subMeshCount; i++)
        {
            var tris = GetTriangles(slot.meshData, i);
            mesh.subMeshCount = slot.meshData.subMeshCount;
            mesh.SetIndices(tris, MeshTopology.Triangles, i);
        }

        return mesh;
    }

    public static int[] GetTriangles(UMAMeshData meshData, int subMesh)
    {
        return meshData.submeshes[subMesh].getBaseTriangles();
    }
}