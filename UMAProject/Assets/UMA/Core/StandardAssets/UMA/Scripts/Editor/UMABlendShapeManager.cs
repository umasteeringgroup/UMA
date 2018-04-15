using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    public class UMABlendShapeManager : EditorWindow
    {
        private List<SlotDataAsset> _slotAssets = new List<SlotDataAsset>();

        private string _find = "";
        private string _replace = "";

        [MenuItem("UMA/BlendShape Manager")]
        public static void OpenBlendShapeManagerWindow()
        {
            UMABlendShapeManager window = (UMABlendShapeManager)EditorWindow.GetWindow(typeof(UMABlendShapeManager));
            window.titleContent.text = "BlendShape Manager";
        }

        void OnGUI()
        {
            GUILayout.Label("", EditorStyles.boldLabel);
            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Drag SlotDataAssets here");

            EditorGUILayout.LabelField("SlotDataAssets List");
            EditorGUI.indentLevel++;
            if (_slotAssets != null && _slotAssets.Count > 0)
            {
                for (int i = 0; i < _slotAssets.Count; i++)
                {
                    EditorGUILayout.ObjectField(_slotAssets[i], typeof(SlotDataAsset), false);
                }
            }
            else
                EditorGUILayout.LabelField("No SlotDataAssets added!");
            EditorGUI.indentLevel--;

            if(GUILayout.Button("Clear Entire List"))
            {
                if(EditorUtility.DisplayDialog("Clear List", "Are you sure you want to clear the entire list?", "Yes", "Cancel"))
                {
                    _slotAssets.Clear();
                }
            }

            GUILayout.Space(50);
            _find = EditorGUILayout.TextField("String to search for: ", _find);
            _replace = EditorGUILayout.TextField("String to replace with: ", _replace);
            if(GUILayout.Button("Search and Replace!"))
            {
                SearchAndReplace(_find, _replace);
            }

            DropAreaGUI(dropArea);
        }

        private void SearchAndReplace(string find, string replace)
        {
            if (_slotAssets == null)
                return;
            if (_slotAssets.Count <= 0)
                return;

            for(int i = 0; i < _slotAssets.Count; i++)
            {
                EditorUtility.DisplayProgressBar(string.Format("Search and replace in {0} assets", _slotAssets.Count), ("Asset " + i), (i / _slotAssets.Count) );
                if (_slotAssets[i].meshData != null)
                    BlendShapeNameReplace(_slotAssets[i].meshData, find, replace);
            }
            EditorUtility.ClearProgressBar();
        }

        private void BlendShapeNameReplace(UMAMeshData meshData, string find, string replace)
        {
            if (meshData.blendShapes == null)
                return;
            if (meshData.blendShapes.Length <= 0)
                return;

            UMABlendShape[] blendShapes = meshData.blendShapes;

            for(int i = 0; i < blendShapes.Length; i++)
            {
                blendShapes[i].shapeName = blendShapes[i].shapeName.Replace(find,replace);
            }            
        }

        private void DropAreaGUI(Rect dropArea)
        {
            var evt = Event.current;

            if (evt.type == EventType.DragUpdated)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                }
            }

            if (evt.type == EventType.DragPerform)
            {
                if (dropArea.Contains(evt.mousePosition))
                {
                    DragAndDrop.AcceptDrag();
                    UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];

                    for (int i = 0; i < draggedObjects.Length; i++)
                    {
                        if(draggedObjects[i].GetType() == typeof(SlotDataAsset))
                        {
                            if (!_slotAssets.Contains(draggedObjects[i] as SlotDataAsset))
                                _slotAssets.Add(draggedObjects[i] as SlotDataAsset);
                        }
                    }
                }
            }
        }
    }
}
