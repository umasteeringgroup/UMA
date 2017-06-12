using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.Editors
{
    [CustomEditor(typeof(MeshHideEditObject))]
    public class MeshHideEditObjectInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Done with Edits"))
            {
                DestroySceneEditObject();
            }
        }

        void OnSceneGUI()
        {
            MeshHideEditObject editObject = target as MeshHideEditObject;

            if (Event.current.type == EventType.MouseUp)
            {
                Debug.Log("raycast");
                Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                RaycastHit hit;

                for (int i = 0; i < editObject.HideAsset.asset.meshData.vertexCount; i++)
                {
                    editObject.pickCollider.size = new Vector3(editObject.CubeSize, editObject.CubeSize, editObject.CubeSize);
                    editObject.pickCollider.center = editObject.HideAsset.asset.meshData.vertices[i];
                    if (editObject.pickCollider.Raycast(ray, out hit, 1000.0f))
                    {
                        Debug.Log("vertex: " + editObject.HideAsset.asset.meshData.vertices[i] + " hit: " + hit.transform.position);
                        editObject.HideAsset.SetVertexFlag(editObject.HideAsset.asset.meshData.vertices[i], true);
                    }
                }
            }
        }

        private void DestroySceneEditObject()
        {
            MeshHideEditObject editObject = target as MeshHideEditObject;
            Selection.activeObject = editObject.HideAsset;
            DestroyImmediate(editObject.gameObject);
        }
    }
}
