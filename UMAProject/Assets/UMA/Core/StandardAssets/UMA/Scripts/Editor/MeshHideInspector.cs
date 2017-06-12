using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.Editors
{
    [CustomEditor(typeof(MeshHideAsset))]
    public class MeshHideInspector : Editor 
    {
        Editor meshPreviewWindow;
        Mesh meshPreview;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            MeshHideAsset source = target as MeshHideAsset;

            //DrawDefaultInspector();
            if (source.asset == null)
                EditorGUILayout.HelpBox("No SlotDataAsset set!", MessageType.Warning);

            var obj = EditorGUILayout.ObjectField("SlotDataAsset", source.asset, typeof(SlotDataAsset), false);
            if (obj != null && obj != source.asset)
            {
                source.asset = obj as SlotDataAsset;
                source.Initialize();
                EditorUtility.SetDirty(target);
            }

            string vertexInfo;
            if (source.VertexCount > 0)
                vertexInfo = "Vertex Count: " + source.VertexCount.ToString();
            else
                vertexInfo = "No vertex array found";

            EditorGUILayout.HelpBox(vertexInfo.ToString(), MessageType.Info);

            if (GUILayout.Button("Create Scene Object"))
            {
                CreateSceneEditObject();
            }

            if(source.asset != null)
            {
                if (meshPreviewWindow == null)
                {
                    if (meshPreview == null)
                    {
                        meshPreview = new Mesh();
                        meshPreview.vertices = source.asset.meshData.vertices;
                        meshPreview.triangles = source.asset.meshData.submeshes[0].triangles;
                        //meshPreview.colors32; //Vertex Colors need a special shader?
                    }

                    meshPreviewWindow = Editor.CreateEditor(meshPreview);
                }

                meshPreviewWindow.OnPreviewGUI(GUILayoutUtility.GetRect(300, 300), EditorStyles.whiteLabel);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void CreateSceneEditObject()
        {
            GameObject obj = new GameObject();
            obj.name = "MeshHideEditObject";
            MeshHideEditObject meshHide = obj.AddComponent<MeshHideEditObject>();
            meshHide.pickCollider = obj.AddComponent<BoxCollider>(); //for object picking

            meshHide.HideAsset = target as MeshHideAsset;
            Selection.activeGameObject = obj;

            meshHide.pickCollider.size = new Vector3( 0.01f, 0.01f, 0.01f);
            meshHide.pickCollider.center = new Vector3(0, 0, 0);
        }
    }
}
